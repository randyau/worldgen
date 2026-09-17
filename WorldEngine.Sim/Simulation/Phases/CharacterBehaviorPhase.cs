using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.World;
using S = WorldEngine.Sim.Simulation.SimRngSalts;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 5 — updates all Tier 1 characters each tick:
/// needs decay, goal management, action selection (utility scoring),
/// lifecycle (aging, death), command resolution (settlement, war, etc.).
/// </summary>
public sealed class CharacterBehaviorPhase
{
    private readonly CharacterSimConfig _cfg;
    private readonly SettlementConfig   _settleCfg;
    private readonly SimConfig _simCfg;
    private readonly UtilityScorer      _scorer;

    public CharacterBehaviorPhase(SimConfig cfg)
    {
        _simCfg    = cfg;
        _cfg       = cfg.Character;
        _settleCfg = cfg.Settlement;
        _scorer    = new UtilityScorer(cfg);
    }

    public List<PendingEvent> Execute(WorldState world, long tick, bool isAnnualTick = false)
    {
        var pending = new List<PendingEvent>();

        // Snapshot to avoid modify-during-iteration
        var characters = world.Entities.Characters.ToList();
        var deathsThisTick = new List<(EntityId Id, string Name)>();

        // M15 15.1/15.2 — religion conversion + schism, run once per annual tick against the
        // tick-start snapshot (a religion founded later this same tick isn't yet a conversion
        // target — same "yesterday's state" tolerance the disease-spread mechanic accepts).
        if (isAnnualTick)
        {
            ProcessAnnualReligionConversion(characters, world, pending);
            ProcessAnnualReligionSchism(world, pending);
            ProcessAnnualReligionPersecution(characters, world, pending);
        }

        foreach (var c in characters)
        {
            if (!c.IsAlive) continue;
            UpdateLifecycle(c, world, tick, pending);
            if (!c.IsAlive) { deathsThisTick.Add((c.Id, c.Identity.Name)); continue; }

            // Annual disease: exposure at infected settlements, health drain, natural recovery
            if (isAnnualTick) ProcessAnnualDisease(c, world, pending);
            if (!c.IsAlive) { deathsThisTick.Add((c.Id, c.Identity.Name)); continue; }

            // Annual religion: progress existing FoundReligion goal first; then try to form one
            if (isAnnualTick)
            {
                AdvanceFoundReligionGoal(c, world, pending, tick);
                TryFormFoundReligionGoal(c, world, tick);
                TryFormPilgrimageGoal(c, world, tick, pending);
            }

            c.TicksInCurrentTile++;
            NeedsUpdater.Update(c, world, _cfg);
            GoalManager.UpdateGoals(c, world, tick, _cfg, pending);
            bool wasSpiraling = c.Wellbeing <= _cfg.SpiralThreshold;
            bool wasFlourishingBefore = c.Wellbeing >= _cfg.FlourishingThreshold;
            bool isSpiraling = GoalManager.UpdateWellbeing(c, world, tick, _cfg, out bool crossedFlourishing);
            if (crossedFlourishing)
                EmitFlourishingEvent(c, pending);
            if (isSpiraling && !wasSpiraling) // only emit on the crossing, not every tick
                EmitSpiralEvent(c, pending);
            ApplyTerritorialPressure(c, world, tick);
            ApplyPassiveDrains(c, world);
            ApplySameCivFamiliarity(c, world);
            CheckBeastEncounters(c, world, pending, tick);
            // Catch wound death from beast/battle damage accumulated this tick
            if (c.IsAlive && c.Health <= 0)
            {
                KillCharacter(c, world, "wounds", pending);
                deathsThisTick.Add((c.Id, c.Identity.Name));
            }
            if (!c.IsAlive) continue;
            var cmd = _scorer.SelectAction(c, world, _cfg);
            if (cmd != null)
                ResolveCommand(cmd, c, world, pending, tick);
        }

        // M14 14.0 (decision 5) — claim mechanic: any living character co-located with a standing
        // WealthDrop claims it whole, mirroring GoalManager's Lost-artifact co-location claim (M5)
        // rather than a new ICommand. Runs every tick (not annual-gated) for the same reason the
        // artifact claim does — no reason to make a co-located character wait a year to notice.
        ClaimWealthDrops(world);

        // Grief: after all deaths are resolved, notify mourners.
        // Build an index of this tick's CharacterDied events so we can retroactively add
        // mourner IDs — the death event then links to everyone it affected, enabling
        // "who was touched by this death" queries without a separate causal edge.
        var deathEventIndex = new Dictionary<long, int>();
        for (int i = 0; i < pending.Count; i++)
        {
            var pe = pending[i];
            if (pe.Type == EventType.CharacterDied && pe.PrimaryEntityIds is { Count: > 0 } ids)
                deathEventIndex[ids[0]] = i;
        }

        foreach (var (deadId, deadName) in deathsThisTick)
        {
            var mourners = new List<(EntityId, float)>();
            GoalManager.ApplyGriefToMourners(deadId, deadName, world, _cfg, mourners, pending);
            if (mourners.Count == 0) continue;

            // Amend the CharacterDied event to also reference mourner IDs
            if (deathEventIndex.TryGetValue(deadId.Value, out int deathIdx))
            {
                var deathEv = pending[deathIdx];
                var ids = (deathEv.PrimaryEntityIds ?? Array.Empty<long>()).ToList();
                ids.AddRange(mourners.Select(m => m.Item1.Value));
                pending[deathIdx] = deathEv with { PrimaryEntityIds = ids };
            }

            foreach (var (mournerId, _) in mourners)
            {
                if (world.GetEntity(mournerId) is Tier1Character mourner && mourner.IsAlive)
                    GoalManager.EmitGriefEvent(mourner, deadId, deadName, pending);
            }
        }

        // Remove dead characters from registry
        foreach (var c in characters.Where(ch => !ch.IsAlive))
            world.Entities.Remove(c.Id);

        // Spawn next-generation heroes once per year (Spring tick only) — runs per-tick otherwise
        // iterates all settlements for no additional output and burns O(settlements) per tick.
        if (isAnnualTick)
        {
            TrySpawnCivBorn(world, pending, tick);
            TrySpawnFamilyBirths(world, pending, tick);
            CheckMarriageEstrangement(world, pending);
        }

        return pending;
    }

    // ─── M13 13.0 — family childbirth ──────────────────────────────────────────

    /// <summary>
    /// Annual roll for each married couple (found via RelationshipEdge.IsMarried, not household
    /// membership scanning — a Family Organization's Members also includes any children already
    /// born into it, so the edge is the unambiguous source of "who are the two spouses").
    /// </summary>
    private void TrySpawnFamilyBirths(WorldState world, List<PendingEvent> pending, long tick)
    {
        var famCfg = world.SimConfig.Family;
        foreach (var edge in world.Relationships.AllEdges.Where(e => e.IsMarried).ToList())
        {
            if (world.GetEntity(edge.From) is not Tier1Character mother || !mother.IsAlive) continue;
            if (world.GetEntity(edge.To) is not Tier1Character father || !father.IsAlive) continue;
            if (mother.Location != father.Location) continue;

            var sharedFamilyMembership = mother.Memberships.FirstOrDefault(m =>
                world.Organizations.TryGetValue(m.OrganizationId, out var o) && o.Kind == OrganizationKind.Family
                && father.Memberships.Any(fm => fm.OrganizationId == m.OrganizationId));
            if (sharedFamilyMembership == null) continue;
            var familyOrg = world.Organizations[sharedFamilyMembership.OrganizationId];

            int livingChildren = familyOrg.Members.Keys.Count(mid =>
                world.GetEntity(mid) is Tier1Character kid && kid.IsAlive
                && (kid.Identity.MotherId == mother.Id || kid.Identity.MotherId == father.Id)
                && (kid.Identity.FatherId == mother.Id || kid.Identity.FatherId == father.Id));
            if (livingChildren >= famCfg.MaxChildrenPerCouple) continue;

            float roll = WorldRng.FloatAt(world.WorldSeed, (int)tick,
                (int)(edge.From.Value & 0x7FFFFFFF), (int)(edge.To.Value & 0x7FFFFFFF), S.CharFamilyBirth);
            if (roll > famCfg.ChildbirthChancePerYear) continue;

            long seq = (60_000L + tick * 997L + edge.From.Value * 31 + edge.To.Value) & 0x7FFFFFFF;
            var tileData = world.TileGrid.GetTile(mother.Location);
            var child = CharacterFactory.SpawnChild(
                mother, father, mother.Location, (BiomeType)tileData.BiomeType,
                world.WorldSeed, seq, _simCfg, world.CurrentYear);
            int bornOrdinal = world.ClaimNameOrdinal(child.Identity.Name);
            child.Identity = child.Identity with { NameOrdinal = bornOrdinal };

            var childMembership = new Membership(familyOrg.Id, OrganizationRole.Member, famCfg.NewbornFamilyLoyalty);
            child.Memberships.Add(childMembership);
            familyOrg.Members[child.Id] = childMembership;

            // Inherit civ membership from whichever parent has one; mother's takes precedence
            // when both do — arbitrary but deterministic (no RNG), same convention as the
            // marriage leader-seat tiebreak.
            var civId = mother.CivId.IsValid ? mother.CivId : father.CivId;
            if (civId.IsValid)
            {
                CivTracker.SetCharacterCiv(child, civId, OrganizationRole.Member, world);
                if (world.Civilizations.TryGetValue(civId, out var parentCiv))
                    parentCiv.Members.Add(child.Id);
            }

            world.Entities.Add(child);

            // M13 13.6 — childbirth milestone source: shared joy reinforces the marriage, not just
            // the individual Belonging bump above.
            world.Relationships.Upsert(edge with
            {
                Trust = Math.Min(1f, edge.Trust + famCfg.ChildbirthTrustGain)
            });

            var payload = JsonSerializer.Serialize(new CharacterBornPayload(
                child.Id.Value, child.Identity.Name, child.Identity.Epithet,
                child.Personality.Ambition, child.Personality.Aggression,
                Source: "family", AncestryId: child.Identity.AncestryId, Surname: child.Identity.Surname));
            pending.Add(new PendingEvent(EventType.CharacterBorn, child.Location, null, payload,
                new[] { child.Id.Value }, new[] { mother.Id.Value, father.Id.Value },
                ActorId: child.Id.Value, ActorName: child.Identity.Name, CivId: civId.Value));
        }
    }

    // ─── M13 13.5 — Estrangement ────────────────────────────────────────────────

    /// <summary>
    /// Annual check on every married edge (same discovery method as <see cref="TrySpawnFamilyBirths"/>):
    /// a marriage whose Trust has decayed to/below FamilyConfig.EstrangementTrustThreshold ends —
    /// clears IsMarried|IsFamily rather than the flags persisting regardless of how the personal
    /// relationship has actually decayed (roadmap proposal #5). The household Family Organization
    /// and its membership are left intact — divorce ends the personal bond, not shared lineage/
    /// inheritance records already built on it.
    /// </summary>
    private void CheckMarriageEstrangement(WorldState world, List<PendingEvent> pending)
    {
        var famCfg = world.SimConfig.Family;
        foreach (var edge in world.Relationships.AllEdges.Where(e => e.IsMarried).ToList())
        {
            if (world.GetEntity(edge.From) is not Tier1Character a || !a.IsAlive) continue;
            if (world.GetEntity(edge.To) is not Tier1Character b || !b.IsAlive) continue;

            // M13 13.6 — hardship sink: poverty strains a marriage on top of whatever the general
            // same-civ companionship drift (ApplySameCivFamiliarity) already did this year, giving
            // Estrangement a distinct "hard times tore them apart" cause, not just baseline
            // personality mismatch.
            bool hardship = a.Needs.Food < famCfg.MarriageHardshipNeedThreshold || a.Needs.Safety < famCfg.MarriageHardshipNeedThreshold
                         || b.Needs.Food < famCfg.MarriageHardshipNeedThreshold || b.Needs.Safety < famCfg.MarriageHardshipNeedThreshold;
            var current = hardship
                ? edge with { Trust = Math.Max(-1f, edge.Trust - famCfg.MarriageHardshipTrustDrain) }
                : edge;
            if (hardship) world.Relationships.Upsert(current);

            if (current.Trust > famCfg.EstrangementTrustThreshold) continue;

            world.Relationships.Upsert(current with
            {
                Flags = current.Flags & ~(RelationshipFlags.IsMarried | RelationshipFlags.IsFamily)
            });

            var payload = JsonSerializer.Serialize(new CharacterEstrangedPayload(
                a.Id.Value, a.Identity.Name, b.Id.Value, b.Identity.Name));
            pending.Add(new PendingEvent(EventType.CharacterEstranged, a.Location, null, payload,
                new[] { a.Id.Value }, new[] { b.Id.Value },
                ActorId: a.Id.Value, ActorName: a.Identity.Name));
        }
    }

    // ─── Civ-born character generation ───────────────────────────────────────


    private void TrySpawnCivBorn(WorldState world, List<PendingEvent> pending, long tick)
    {
        foreach (var kvp in world.Settlements)
        {
            var stub = kvp.Value;
            if (stub.Population < _cfg.CivBirthMinPop) continue;
            if (!world.Civilizations.TryGetValue(stub.CivId, out var civ)) continue;
            if (civ.IsCollapsed) continue;

            // Probability scales with population above minimum
            float popFactor = Math.Min(3f, (float)stub.Population / _cfg.CivBirthMinPop);
            float chance    = _cfg.CivBirthChancePerSeason * popFactor;

            // Emigration pressure: over-capacity settlements get an extra spawn boost and the
            // new character is seeded with a Colonize goal so they actively seek distant land.
            bool overCapacity = stub.CarryingCapacity > 0
                && stub.Population > _settleCfg.EmigrationThreshold * stub.CarryingCapacity;
            if (overCapacity)
            {
                float pressureFactor = Math.Clamp(
                    ((float)stub.Population / stub.CarryingCapacity - _settleCfg.EmigrationThreshold)
                    / (1f - _settleCfg.EmigrationThreshold), 0f, 1f);
                chance += _settleCfg.EmigrationBonusChance * pressureFactor;
            }

            float r = WorldRng.FloatAt(world.WorldSeed, (int)tick, (int)(stub.FounderId.Value & 0x7FFFFFFF), 0, S.CharCivBirth);
            if (r > chance) continue;

            // Unique entitySeq derived from tick + tile to stay deterministic
            long seq  = (50_000L + tick * 997L + kvp.Key.X * 31 + kvp.Key.Y) & 0x7FFFFFFF;
            var tileData = world.TileGrid.GetTile(kvp.Key);
            var born  = CharacterFactory.Spawn(kvp.Key, (BiomeType)tileData.BiomeType, world.WorldSeed, seq, _simCfg, world.CurrentYear);
            int bornOrdinal = world.ClaimNameOrdinal(born.Identity.Name);
            born.Identity = born.Identity with { NameOrdinal = bornOrdinal };
            CivTracker.SetCharacterCiv(born, stub.CivId, OrganizationRole.Member, world);
            civ.Members.Add(born.Id);
            world.Entities.Add(born);

            // Emigrant: seed FoundCity (or, M11, SeaVoyage when the home landmass has no local
            // frontier left and the civ has invested in a Port) immediately, and deduct
            // population from parent.
            if (overCapacity)
            {
                TileCoord? voyageDest = null;
                if (world.SimConfig.Seafaring.OceanCrossingEnabled
                    && !HasLocalFrontier(kvp.Key, world, _cfg)
                    && CivOwnsPort(civ, world))
                    voyageDest = _scorer.FindVoyageDestination(kvp.Key, world);

                born.Goals.Add(voyageDest.HasValue
                    ? new GoalData
                    {
                        Type       = GoalType.SeaVoyage,
                        Priority   = 0.9f,
                        TargetTile = voyageDest,
                        StaleSince = (int)tick,
                        FormedTick = (int)tick
                    }
                    : new GoalData
                    {
                        Type       = GoalType.FoundCity,
                        Priority   = 0.9f,
                        StaleSince = (int)tick,
                        FormedTick = (int)tick
                    });
                // Re-read stub in case it was updated earlier this tick
                if (world.Settlements.TryGetValue(kvp.Key, out var freshStub))
                    world.Settlements[kvp.Key] = freshStub with
                        { Population = Math.Max(0, freshStub.Population - _settleCfg.EmigrantPopCost) };
            }

            var payload = JsonSerializer.Serialize(new CharacterBornPayload(
                born.Id.Value, born.Identity.Name, born.Identity.Epithet,
                born.Personality.Ambition, born.Personality.Aggression,
                AncestryId: born.Identity.AncestryId, Surname: born.Identity.Surname));
            pending.Add(new PendingEvent(EventType.CharacterBorn, born.Location, null, payload,
                new[] { born.Id.Value },
                ActorId: born.Id.Value, ActorName: born.Identity.Name, CivId: stub.CivId.Value));
        }
    }

    // ─── M11 — sea-voyage delegation gates ─────────────────────────────────────

    // DECISION: "landlocked" is a bounded local search (radius = ColonyMinDistance * 3, same
    // landmass as the settlement), not an exhaustive scan of the whole landmass — this runs on
    // every over-capacity settlement on every annual tick, so it stays cheap. A settlement whose
    // immediate landmass neighborhood is fully claimed is treated as landlocked even if distant
    // unclaimed land exists elsewhere on the same landmass; overseas expansion is offered instead
    // of a very-long-range overland trek, which is the more interesting outcome anyway.
    private static bool HasLocalFrontier(TileCoord settlementTile, WorldState world, CharacterSimConfig cfg)
    {
        int radius = cfg.ColonyMinDistance * 3;
        int landmass = world.GetLandmassId(settlementTile);
        foreach (var coord in world.GetTilesInRadius(settlementTile, radius))
        {
            if (world.TerritoryMap.ContainsKey(coord)) continue;
            if (!world.IsLand(coord) || world.GetLandmassId(coord) != landmass) continue;
            var tile = world.GetTile(coord);
            if (tile.Fertility >= cfg.MinFertilityToSettle && tile.BaseMoisture >= cfg.MinBaseMoistureToSettle)
                return true;
        }
        return false;
    }

    private static bool CivOwnsPort(Civilization civ, WorldState world)
    {
        foreach (var territory in civ.CityTerritories.Values)
        foreach (var tile in territory)
        {
            if (world.ImprovementMap.TryGetValue(tile, out var imp) && imp.Type == ImprovementType.Port)
                return true;
        }
        return false;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void UpdateLifecycle(
        Tier1Character c, WorldState world, long tick, List<PendingEvent> pending)
    {
        c.AgeSeason++;

        // Death from wounds carried over from last tick (battle damage, wildlife injury)
        if (c.Health <= 0)
        {
            KillCharacter(c, world, c.IsInfected ? "disease" : "wounds", pending);
            return;
        }

        // Health regeneration (only while not diseased — disease suppresses healing)
        if (!c.IsInfected && c.Health < _cfg.MaxHealth)
            c.Health = Math.Min(_cfg.MaxHealth, c.Health + _cfg.HealthPerSeasonHeal);

        // Death by old age
        if (c.AgeSeason >= c.MaxAgeSeason)
        {
            KillCharacter(c, world, "old age", pending);
            return;
        }

        // Death from unmet needs (starvation, etc.)
        if (c.Needs.Food <= 0f || c.Needs.Safety <= 0f)
        {
            KillCharacter(c, world,
                c.Needs.Food <= 0f ? "starvation" : "violence", pending);
        }
    }

    private void KillCharacter(
        Tier1Character c, WorldState world, string cause, List<PendingEvent> pending)
    {
        c.IsAlive = false;

        // Clear spotlight if the dead character was being controlled (7.3.3 — no state leaks on death)
        if (world.SpotlightCharacterId == c.Id)
        {
            world.SpotlightCharacterId = null;
            world.SpotlightIntent      = null;
        }

        // Rivalries end when a participant dies — you can't have a personal feud with a corpse.
        // Wars are civ-level and continue regardless of whether this individual is alive.
        // Alliances and bond edges are left in place: they feed grief/mourning logic
        // (ApplyGriefToMourners runs after this) and are pruned by the annual cleanup.
        foreach (var edge in world.Relationships.GetAll(c.Id).ToList())
        {
            if (edge.IsRival)
            {
                world.Relationships.Upsert(edge with
                {
                    Flags = edge.Flags & ~RelationshipFlags.IsRival
                });
            }
        }

        // M13 13.2 — Debt is inheritable: a dead character's obligations pass to their household
        // heir rather than vanishing with them.
        TransferDebtOnDeath(c, world);

        // M14 14.0 — Wealth disposition: WealthInheritanceShare to the heir (100% drops if no
        // eligible heir exists), the remainder becomes an unclaimed WealthDrop at the death tile.
        TransferWealthOnDeath(c, world);

        var payload = JsonSerializer.Serialize(new CharacterDeathPayload(
            c.Id.Value, c.Identity.Name, cause, c.AgeSeason,
            AncestryId: c.Identity.AncestryId, MaxAgeSeason: c.MaxAgeSeason));
        pending.Add(new PendingEvent(EventType.CharacterDied, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));

        // Artifact inheritance — transfer or lose owned artifacts on death
        HandleArtifactInheritanceOnDeath(c, world, pending);

        // Heroic-death forging: legendary (high-skill) characters who die in combat may forge an artifact
        TryHeroicDeathForge(c, cause, world, pending);

        // Handle succession when a civ member dies
        if (c.CivId.IsValid
            && world.Civilizations.TryGetValue(c.CivId, out var civ))
        {
            bool wasRuler = civ.RulerId == c.Id;
            civ.Members.Remove(c.Id);

            // Succession: promote the highest-scoring living member to ruler. The heir-selection
            // kernel is generalized onto Organization (M12 12.3, SuccessionResolver) so M13-M15 can
            // reuse it for family/guild/religious-leader seats instead of hand-rolling three more.
            if (wasRuler && !civ.IsCollapsed)
            {
                Organization? succOrg = civ.OrgId.HasValue && world.Organizations.TryGetValue(civ.OrgId.Value, out var o) ? o : null;
                EntityId? successorId = succOrg != null
                    ? SuccessionResolver.SelectSuccessor(succOrg, world, _cfg.MinRulerAgeSeasons,
                        member => (member.Personality.Aggression + member.Skills.Leadership) * 0.5f)
                    : null;
                if (successorId.HasValue)
                {
                    civ.RulerId = successorId.Value;
                    // Keep the Organization's leader seat mirrored — see docs/phases/m12_organization_model.md.
                    succOrg!.LeaderId = successorId.Value;
                    civ.RulerCount++;
                    civ.TotalSuccessions++;
                    var successor = (Tier1Character)world.GetEntity(successorId.Value)!;
                    successor.Identity = successor.Identity with { RulerOrdinal = civ.RulerCount };
                    string[]? civTraits = civ.CulturalTraits.Count > 0
                        ? civ.CulturalTraits.Select(t => t.ToString()).ToArray()
                        : null;
                    var succPayload = JsonSerializer.Serialize(new SuccessionPayload(
                        c.Id.Value, c.Identity.Name, c.Identity.RulerOrdinal,
                        successorId.Value.Value, successor.Identity.Name, civ.RulerCount,
                        CivTraits: civTraits));
                    pending.Add(new PendingEvent(EventType.SuccessionOccurred, civ.CapitalTile, null,
                        succPayload, new[] { c.Id.Value, successorId.Value.Value },
                        ActorId: successorId.Value.Value, ActorName: successor.Identity.Name, CivId: civ.Id.Value));
                }
                // No successor found → succession crisis fires in RunAnnualDiplomacy
            }

            // When no members remain, check if a settlement survives. If so, a new leader
            // rises from the population rather than the civilization collapsing. A civ only
            // truly collapses when both its named members AND all its settlements are gone.
            if (civ.Members.Count == 0 && !civ.IsCollapsed)
            {
                TileCoord? survivingTile = null;
                foreach (var (tile, stub) in world.Settlements)
                {
                    if (stub.CivId == civ.Id && stub.Population >= _cfg.CivBirthMinPop)
                    {
                        survivingTile = tile;
                        break;
                    }
                }

                if (survivingTile.HasValue)
                {
                    var sTile = survivingTile.Value;
                    long seq = (300_000L + world.CurrentYear * 997L + sTile.X * 31L + sTile.Y) & 0x7FFFFFFF;
                    var tileData = world.TileGrid.GetTile(sTile);
                    var newRuler = CharacterFactory.Spawn(sTile, (BiomeType)tileData.BiomeType,
                        world.WorldSeed, seq, _simCfg, world.CurrentYear, startAsAdult: true);
                    int nameOrdinal = world.ClaimNameOrdinal(newRuler.Identity.Name);
                    civ.RulerId = newRuler.Id;
                    if (civ.OrgId.HasValue && world.Organizations.TryGetValue(civ.OrgId.Value, out var reOrg))
                        reOrg.LeaderId = newRuler.Id;
                    civ.RulerCount++;
                    civ.TotalSuccessions++;
                    civ.Members.Add(newRuler.Id);
                    newRuler.Identity = newRuler.Identity with
                    {
                        NameOrdinal = nameOrdinal,
                        RulerOrdinal = civ.RulerCount
                    };
                    CivTracker.SetCharacterCiv(newRuler, civ.Id, OrganizationRole.Leader, world);
                    world.Entities.Add(newRuler);

                    var bornPayload = JsonSerializer.Serialize(new CharacterBornPayload(
                        newRuler.Id.Value, newRuler.Identity.Name, newRuler.Identity.Epithet,
                        newRuler.Personality.Ambition, newRuler.Personality.Aggression,
                        AncestryId: newRuler.Identity.AncestryId, Surname: newRuler.Identity.Surname));
                    pending.Add(new PendingEvent(EventType.CharacterBorn, sTile, null, bornPayload,
                        new[] { newRuler.Id.Value },
                        ActorId: newRuler.Id.Value, ActorName: newRuler.Identity.Name, CivId: civ.Id.Value));
                }
                else
                {
                    civ.IsCollapsed = true;
                    var civPayload = JsonSerializer.Serialize(new CivCollapsedPayload(civ.Id.Value));
                    pending.Add(new PendingEvent(
                        EventType.CivilizationCollapsed, civ.CapitalTile, null, civPayload,
                        CivId: civ.Id.Value));
                }
            }
        }

        // M14 14.4 — Guild leader succession: reuses SuccessionResolver.SelectSuccessor unmodified
        // (no new succession mechanism, per the M12 audit note this milestone is bound by),
        // mirroring the civ ruler succession block above generalized only as far as Guild needs
        // it (Family/Religion leader succession stays out of scope for 14.4). Dead members are
        // never removed from Organization.Members (same convention Family already follows — see
        // FindHeir/SelectSuccessor's IsAlive filters below), so no cleanup is needed here beyond
        // reassigning LeaderId, mirroring how the civ block above only reassigns civ.RulerId/
        // succOrg.LeaderId without touching Members' Role entries.
        foreach (var membership in c.Memberships)
        {
            if (!world.Organizations.TryGetValue(membership.OrganizationId, out var guildOrg)) continue;
            if (guildOrg.Kind != OrganizationKind.Guild || guildOrg.LeaderId != c.Id) continue;

            var successorId = SuccessionResolver.SelectSuccessor(guildOrg, world, _cfg.MinRulerAgeSeasons,
                member => (member.Personality.Ambition + member.Skills.Leadership) * 0.5f);
            if (!successorId.HasValue) continue; // no eligible member — seat simply stays vacant

            guildOrg.LeaderId = successorId.Value;
            var successor = (Tier1Character)world.GetEntity(successorId.Value)!;

            var succPayload = JsonSerializer.Serialize(new GuildSuccessionPayload(
                guildOrg.Id.Value, guildOrg.Name,
                c.Id.Value, c.Identity.Name,
                successorId.Value.Value, successor.Identity.Name));
            pending.Add(new PendingEvent(EventType.GuildLeadershipTransferred, c.Location, null,
                succPayload, new[] { c.Id.Value, successorId.Value.Value },
                ActorId: successorId.Value.Value, ActorName: successor.Identity.Name));
        }

        // M15 15.0 — Religion leader succession: same SuccessionResolver reuse as the Guild block
        // above. Unlike Guild (seat simply stays vacant when no eligible successor exists), a
        // religion with zero remaining living members is genuinely extinct — the sink half of
        // M15's population balance (docs/phases/m15_religion_deepened.md "Long-run balance
        // constraints"). "Eligible" (age-gated) successor absence doesn't necessarily mean extinct
        // (a young-only congregation still has living followers), so extinction is checked
        // separately against IsAlive alone, not SelectSuccessor's age-gated result.
        foreach (var membership in c.Memberships)
        {
            if (!world.Organizations.TryGetValue(membership.OrganizationId, out var religionOrg)) continue;
            if (religionOrg.Kind != OrganizationKind.Religion || religionOrg.LeaderId != c.Id) continue;

            var successorId = SuccessionResolver.SelectSuccessor(religionOrg, world, _cfg.MinRulerAgeSeasons,
                member => (member.Personality.Wonder + member.Skills.Piety) * 0.5f);
            if (successorId.HasValue)
            {
                religionOrg.LeaderId = successorId.Value;
                var successor = (Tier1Character)world.GetEntity(successorId.Value)!;

                var succPayload = JsonSerializer.Serialize(new ReligiousLeadershipTransferredPayload(
                    religionOrg.Id.Value, religionOrg.Name,
                    c.Id.Value, c.Identity.Name,
                    successorId.Value.Value, successor.Identity.Name));
                pending.Add(new PendingEvent(EventType.ReligiousLeadershipTransferred, c.Location, null,
                    succPayload, new[] { c.Id.Value, successorId.Value.Value },
                    ActorId: successorId.Value.Value, ActorName: successor.Identity.Name));
                continue;
            }

            bool anyLivingFollower = religionOrg.Members.Keys.Any(id =>
                id != c.Id && world.GetEntity(id) is Tier1Character t && t.IsAlive);
            if (anyLivingFollower) continue; // seat vacant, congregation survives — mirrors Guild

            religionOrg.IsExtinct = true;
            var extinctPayload = JsonSerializer.Serialize(new ReligionExtinctPayload(
                religionOrg.Id.Value, religionOrg.Name, world.CurrentYear));
            pending.Add(new PendingEvent(EventType.ReligionExtinct, c.Location, null,
                extinctPayload, new[] { c.Id.Value }));
        }
    }

    /// <summary>
    /// M13 13.2 — Debt as an obligation mechanic: inheritable, tying into the household Family
    /// Organization rather than a full succession-seat change. The heir is the deceased's spouse
    /// (preferred) or, absent one, any other living member of their household. Each of the
    /// deceased's nonzero-Debt edges is re-pointed at the heir (summed with any debt the heir
    /// already carries toward the same counterparty); a debt directly between the deceased and
    /// their own heir is simply extinguished rather than transferred to itself.
    /// </summary>
    /// <summary>
    /// Shared heir-selection kernel for death disposition: the deceased's spouse (preferred) or,
    /// absent one, any other living member of their household Family Organization. Reused by both
    /// TransferDebtOnDeath (M13.2) and TransferWealthOnDeath (M14 14.0) rather than each rolling its
    /// own — see docs/phases/m14_economy_independent_wealth.md 14.0's instruction to reuse the
    /// existing heir-selection logic, not invent a second one.
    /// </summary>
    private static Tier1Character? FindHeir(Tier1Character c, WorldState world)
    {
        var familyMembership = c.Memberships.FirstOrDefault(m =>
            world.Organizations.TryGetValue(m.OrganizationId, out var o) && o.Kind == OrganizationKind.Family);
        if (familyMembership == null) return null;
        var familyOrg = world.Organizations[familyMembership.OrganizationId];

        Tier1Character? heir = null;
        foreach (var memberId in familyOrg.Members.Keys)
        {
            if (memberId == c.Id) continue;
            if (world.GetEntity(memberId) is not Tier1Character candidate || !candidate.IsAlive) continue;
            if (world.Relationships.Get(c.Id, memberId)?.IsMarried ?? false) { heir = candidate; break; }
            heir ??= candidate;
        }
        return heir;
    }

    /// <summary>
    /// M14 14.0 (decision 5) — WealthInheritanceShare of the deceased's Wealth passes to the heir
    /// found via <see cref="FindHeir"/>; the remainder becomes an unclaimed WealthDrop at the death
    /// tile. If no eligible heir exists, 100% drops (decision 5's revision — explicit, not left
    /// undefined). A character with zero Wealth is a no-op.
    /// </summary>
    private static void TransferWealthOnDeath(Tier1Character c, WorldState world)
    {
        float wealth = c.Wealth;
        if (wealth <= 0f) return;

        var econCfg = world.SimConfig.Economy;
        var heir = FindHeir(c, world);

        float inherited = heir != null ? wealth * econCfg.WealthInheritanceShare : 0f;
        float dropped = wealth - inherited;

        c.AddWealth(-wealth);
        if (heir != null && inherited > 0f)
            heir.AddWealth(inherited);
        if (dropped > 0f)
            world.WealthDrops.Add(new WealthDrop(c.Location, dropped, (int)world.CurrentTick));
    }

    /// <summary>
    /// M14 14.0 (decision 5) — any living character standing on a WealthDrop's tile claims the
    /// whole pool. Deterministic within a tick: first match in entity-at-tile iteration order.
    /// </summary>
    private static void ClaimWealthDrops(WorldState world)
    {
        if (world.WealthDrops.Count == 0) return;
        for (int i = world.WealthDrops.Count - 1; i >= 0; i--)
        {
            var drop = world.WealthDrops[i];
            var claimant = world.GetEntitiesAt(drop.Location)
                .OfType<Tier1Character>().FirstOrDefault(ch => ch.IsAlive);
            if (claimant == null) continue;
            claimant.AddWealth(drop.Amount);
            world.WealthDrops.RemoveAt(i);
        }
    }

    private static void TransferDebtOnDeath(Tier1Character c, WorldState world)
    {
        var heir = FindHeir(c, world);
        if (heir == null) return;

        foreach (var edge in world.Relationships.GetAll(c.Id).ToList())
        {
            if (edge.Debt == 0f) continue;
            var counterpartyId = edge.From == c.Id ? edge.To : edge.From;

            if (counterpartyId == heir.Id)
            {
                world.Relationships.Upsert(edge with { Debt = 0f });
                continue;
            }

            float owedByC = edge.DebtorId == c.Id ? Math.Abs(edge.Debt) : -Math.Abs(edge.Debt);

            var heirEdge = world.Relationships.GetOrCreate(heir.Id, counterpartyId);
            float existingOwedByHeir = heirEdge.DebtorId == heir.Id ? Math.Abs(heirEdge.Debt)
                                      : heirEdge.DebtorId == counterpartyId ? -Math.Abs(heirEdge.Debt)
                                      : 0f;
            float newOwedByHeir = Math.Clamp(existingOwedByHeir + owedByC, -1f, 1f);
            float sign = heir.Id == heirEdge.From ? 1f : -1f;
            world.Relationships.Upsert(heirEdge with { Debt = newOwedByHeir * sign });

            world.Relationships.Upsert(edge with { Debt = 0f });
        }
    }

    // ─── Heroic-death artifact forging ───────────────────────────────────────

    /// <summary>
    /// When a legendary character (high Combat skill) dies in combat, roll HeroicDeathForgeProbability
    /// to forge an artifact owned by the fallen's settlement (or lost if no settlement).
    /// </summary>
    private void TryHeroicDeathForge(
        Tier1Character c, string cause, WorldState world, List<PendingEvent> pending)
    {
        // Only trigger for combat/wound deaths of high-skill characters
        bool isCombatDeath = cause.Contains("wound") || cause.Contains("killed") || cause.Contains("slain");
        if (!isCombatDeath) return;

        // DECISION: "legendary" threshold = Combat >= 0.5f (top half of the skill range)
        if (c.Skills.Combat < 0.5f) return;

        // Use _simCfg (injected at construction) so test overrides take effect
        var artCfg = _simCfg.Artifacts;
        float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick,
            (int)(c.Id.Value & 0xFFFF), (int)(c.Id.Value >> 16), S.ArtifactHeroicDeath);
        if (roll >= artCfg.HeroicDeathForgeProbability) return;

        // Artifact is owned by the fallen's settlement, or becomes Lost
        ArtifactOwner owner;
        if (world.Settlements.ContainsKey(c.Location))
            owner = ArtifactOwner.OfSettlement(c.Location);
        else if (c.CivId.IsValid
                 && world.Civilizations.TryGetValue(c.CivId, out var civ3)
                 && world.Settlements.ContainsKey(civ3.CapitalTile))
            owner = ArtifactOwner.OfSettlement(civ3.CapitalTile);
        else
            owner = ArtifactOwner.Lost;

        float quality = Math.Clamp(0.55f + c.Skills.Combat * 0.4f, 0f, 1f);
        // M9 G-2: weighted category roll (no CreatedGoodType context for combat-triggered forging)
        float categoryRoll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick,
            (int)(c.Id.Value & 0xFFFF), (int)(c.Id.Value >> 16), S.ArtifactHeroicCategory);
        var cat = CreatedGoodTaxonomy.WeightedPick(
            [
                (ArtifactCategory.Weapon, artCfg.HeroicDeathCategoryWeightWeapon),
                (ArtifactCategory.Relic, artCfg.HeroicDeathCategoryWeightRelic),
                (ArtifactCategory.Regalia, artCfg.HeroicDeathCategoryWeightRegalia),
            ], categoryRoll);
        var name     = ArtifactNameGenerator.Generate(world, cat, (int)c.Id.Value);
        var artifact = ArtifactRegistry.Create(world, name, cat, world.CurrentYear,
            creatorId:   0,
            creatorName: c.Identity.Name,
            origin:      "heroic_death",
            quality:     quality,
            owner:       owner);

        var artPayload = JsonSerializer.Serialize(new ArtifactCreatedPayload(
            artifact.Id.Value, artifact.Name, artifact.Category.ToString(),
            0, c.Identity.Name, "heroic_death", quality));
        pending.Add(new PendingEvent(EventType.ArtifactCreated, c.Location, null, artPayload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name,
            CivId: c.CivId.IsValid ? c.CivId.Value : 0));
    }

    // ─── Artifact inheritance on death ───────────────────────────────────────

    /// <summary>
    /// For each artifact owned by the deceased character, roll LostOnDeathProbability.
    /// Failures become Lost; successes transfer to the deceased's home settlement (if any).
    /// Emits ArtifactTransferred for each artifact affected.
    /// </summary>
    private void HandleArtifactInheritanceOnDeath(
        Tier1Character c, WorldState world, List<PendingEvent> pending)
    {
        var ownedArtifacts = ArtifactRegistry.OwnedByCharacter(world, c.Id).ToList();
        if (ownedArtifacts.Count == 0) return;

        // Determine inheritance settlement: deceased's civ capital, or their current tile if settled
        TileCoord? settleTile = null;
        if (world.Settlements.ContainsKey(c.Location))
            settleTile = c.Location;
        else if (c.CivId.IsValid
                 && world.Civilizations.TryGetValue(c.CivId, out var civ2)
                 && world.Settlements.ContainsKey(civ2.CapitalTile))
            settleTile = civ2.CapitalTile;

        // Use _simCfg (injected at construction) so test overrides take effect
        var artCfg = _simCfg.Artifacts;
        for (int i = 0; i < ownedArtifacts.Count; i++)
        {
            var artifact = ownedArtifacts[i];
            float lossRoll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick,
                (int)(c.Id.Value & 0xFFFF), i, S.ArtifactDeathInheritance);

            string fromOwner = artifact.Owner.Describe();
            ArtifactOwner newOwner;
            string toOwnerDesc;

            if (lossRoll < artCfg.LostOnDeathProbability || settleTile is null)
            {
                newOwner    = ArtifactOwner.Lost;
                toOwnerDesc = ArtifactOwner.Lost.Describe();
            }
            else
            {
                newOwner    = ArtifactOwner.OfSettlement(settleTile.Value);
                toOwnerDesc = newOwner.Describe();
            }

            ArtifactRegistry.SetOwner(world, artifact.Id, newOwner);

            var transPayload = JsonSerializer.Serialize(new ArtifactTransferredPayload(
                artifact.Id.Value, artifact.Name, fromOwner, toOwnerDesc, "inheritance"));
            pending.Add(new PendingEvent(EventType.ArtifactTransferred, c.Location, null, transPayload,
                new[] { artifact.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name));
        }
    }

    // ─── Disease ─────────────────────────────────────────────────────────────


    /// <summary>
    /// Annual disease processing for a single character.
    /// Uninfected characters at infected settlements may contract disease.
    /// Infected characters lose health each year and have a chance to recover.
    /// </summary>
    private void ProcessAnnualDisease(
        Tier1Character c, WorldState world, List<PendingEvent> pending)
    {
        bool atInfectedSettlement = world.Settlements.TryGetValue(c.Location, out var stub)
                                 && stub.IsInfected;

        if (!c.IsInfected && atInfectedSettlement)
        {
            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                                          (int)(c.Id.Value & 0x7FFFFFFF), 0, S.CharDiseaseExposure);
            if (roll < _cfg.CharacterDiseaseExposureChance)
            {
                c.IsInfected      = true;
                c.InfectedSinceYear = world.CurrentYear;
            }
        }

        if (c.IsInfected)
        {
            c.Health = Math.Max(0, c.Health - _cfg.CharacterDiseaseHealthDrain);
            if (c.Health <= 0)
            {
                KillCharacter(c, world, "disease", pending);
                return;
            }

            float recRoll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                                              (int)(c.Id.Value & 0x7FFFFFFF), 1, S.CharDiseaseRecovery);
            if (recRoll < _cfg.CharacterDiseaseRecoveryChance)
            {
                c.IsInfected       = false;
                c.InfectedSinceYear = 0;
            }
        }
    }

    // ─── Religion conversion (M15 15.1) ────────────────────────────────────────

    /// <summary>
    /// Shared per-civ tally: named population count, and living-membership count per Religion
    /// Organization. Rebuilt by conversion (15.1) and by the state-religion/heresy determination
    /// (15.3), which needs exactly this same civ-scoped shape.
    /// </summary>
    private static (Dictionary<CivId, int> CivPopulation, Dictionary<CivId, Dictionary<OrganizationId, int>> ReligionTally)
        BuildReligionTally(List<Tier1Character> characters, WorldState world)
    {
        var civPopulation = new Dictionary<CivId, int>();
        var religionTally  = new Dictionary<CivId, Dictionary<OrganizationId, int>>();
        foreach (var ch in characters)
        {
            if (!ch.IsAlive || !ch.CivId.IsValid) continue;
            civPopulation[ch.CivId] = civPopulation.GetValueOrDefault(ch.CivId) + 1;

            foreach (var m in ch.Memberships)
            {
                if (!world.Organizations.TryGetValue(m.OrganizationId, out var o)) continue;
                if (o.Kind != OrganizationKind.Religion || o.IsExtinct) continue;
                if (!religionTally.TryGetValue(ch.CivId, out var byOrg))
                    religionTally[ch.CivId] = byOrg = new Dictionary<OrganizationId, int>();
                byOrg[m.OrganizationId] = byOrg.GetValueOrDefault(m.OrganizationId) + 1;
            }
        }
        return (civPopulation, religionTally);
    }

    /// <summary>
    /// Exposure/personal-receptivity conversion: characters weigh each Religion present in their
    /// own civ (civ-scoped exposure, not settlement-scoped — DECISION: simplest reasonable choice
    /// that still satisfies "multiple religions can coexist in a civ"; also happens to be exactly
    /// the scope 15.3's state-religion/heresy determination needs) by presence fraction × personal
    /// receptivity × archetype pull, resisted by their current religion's Loyalty if they have one.
    /// The highest-pull candidate gets one roll per character per year — DECISION: picking the
    /// single best candidate rather than a full weighted-random draw across all candidates is a
    /// simplification; a candidate that loses this year's comparison still gets its own turn in
    /// later years as presence/receptivity shift. See docs/phases/m15_religion_deepened.md
    /// "Long-run balance constraints" for the sink/source reasoning behind every term here.
    /// </summary>
    private void ProcessAnnualReligionConversion(List<Tier1Character> characters, WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Religion;
        var (civPopulation, religionTally) = BuildReligionTally(characters, world);

        foreach (var ch in characters)
        {
            if (!ch.IsAlive || !ch.CivId.IsValid) continue;
            if (!religionTally.TryGetValue(ch.CivId, out var byOrg) || byOrg.Count == 0) continue;
            int civPop = civPopulation[ch.CivId];
            if (civPop <= 0) continue;

            var p  = ch.Personality;
            float receptivity = Math.Clamp(
                cfg.ConversionWeightPiety      * ch.Skills.Piety
              + cfg.ConversionWeightWonder     * p.Wonder
              + cfg.ConversionWeightCuriosity  * p.Curiosity
              - cfg.ConversionWeightRationality * p.Rationality
              - cfg.ConversionBaselineSkepticism,
                0f, 1f);
            if (receptivity <= 0f) continue; // hard gate — agnosticism is a stable end-state, not a slow transient

            var currentMembership = ch.Memberships.FirstOrDefault(m =>
                world.Organizations.TryGetValue(m.OrganizationId, out var o) && o.Kind == OrganizationKind.Religion);

            float bestPull = 0f;
            OrganizationId? bestOrgId = null;
            foreach (var (orgId, count) in byOrg)
            {
                if (currentMembership != null && orgId == currentMembership.OrganizationId) continue;

                var org = world.Organizations[orgId];
                float presence = (float)count / civPop;
                var archetype  = world.SimConfig.ReligionArchetypes.Get(org.ReligionArchetypeId);
                float zealBonus = archetype is { Zealotry: > 0f }
                    ? 1f + archetype.Zealotry * cfg.ZealotryConversionBonus
                    : 1f;

                float pull = presence * receptivity * zealBonus * cfg.ConversionBasePullScale;
                if (currentMembership != null)
                    pull *= Math.Max(0f, 1f - currentMembership.Loyalty * cfg.ExistingLoyaltyResistance);

                if (pull > bestPull) { bestPull = pull; bestOrgId = orgId; }
            }
            if (bestOrgId is not { } targetOrgId || bestPull <= 0f) continue;

            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                (int)(ch.Id.Value & 0x7FFFFFFF), 0, S.ReligionConversionRoll);
            if (roll >= bestPull) continue;

            long fromOrgId = currentMembership?.OrganizationId.Value ?? 0;
            if (currentMembership != null)
            {
                world.Organizations[currentMembership.OrganizationId].Members.Remove(ch.Id);
                ch.Memberships.Remove(currentMembership);
            }
            var targetOrg = world.Organizations[targetOrgId];
            var newMembership = new Membership(targetOrgId, OrganizationRole.Member, cfg.InitialConvertLoyalty);
            ch.Memberships.Add(newMembership);
            targetOrg.Members[ch.Id] = newMembership;

            var payload = JsonSerializer.Serialize(new CharacterConvertedPayload(
                ch.Id.Value, ch.Identity.Name, targetOrgId.Value, targetOrg.Name, fromOrgId));
            pending.Add(new PendingEvent(EventType.CharacterConvertedReligion, ch.Location, null,
                payload, new[] { ch.Id.Value },
                ActorId: ch.Id.Value, ActorName: ch.Identity.Name, CivId: ch.CivId.Value));
        }
    }

    // ─── Heresy & persecution (M15 15.3) ───────────────────────────────────────

    /// <summary>
    /// A civ's "state religion" is whichever Religion its religious (non-agnostic) population
    /// follows in plurality, when that plurality is decisive enough (HeresyStateReligionMinShare).
    /// Persecution only exists when the state religion's Zealotry is positive enough — a
    /// tolerant/syncretic religion never persecutes, per the Zealotry axis's design intent (see
    /// docs/phases/m15_religion_deepened.md point 4). Effects are soft (political/social pressure
    /// only, per roadmap): a resisted hit penalizes the heretic's civ Loyalty and Needs; a
    /// forced-conversion hit moves them into the state religion outright, at low (coerced) Loyalty.
    /// </summary>
    private void ProcessAnnualReligionPersecution(List<Tier1Character> characters, WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Religion;
        var (_, religionTally) = BuildReligionTally(characters, world);

        foreach (var (civId, byOrg) in religionTally)
        {
            int totalReligious = byOrg.Values.Sum();
            if (totalReligious == 0) continue;

            var (stateOrgId, stateCount) = byOrg.OrderByDescending(kv => kv.Value).First();
            if ((float)stateCount / totalReligious < cfg.HeresyStateReligionMinShare) continue;

            var stateOrg = world.Organizations[stateOrgId];
            var stateArchetype = world.SimConfig.ReligionArchetypes.Get(stateOrg.ReligionArchetypeId);
            float zealotry = stateArchetype?.Zealotry ?? 0f;
            if (zealotry <= cfg.PersecutionMinZealotry) continue;

            float hitChance = cfg.PersecutionBaseChance * zealotry;

            foreach (var ch in characters)
            {
                if (!ch.IsAlive || ch.CivId != civId) continue;
                var religionMembership = ch.Memberships.FirstOrDefault(m =>
                    world.Organizations.TryGetValue(m.OrganizationId, out var o) && o.Kind == OrganizationKind.Religion);
                if (religionMembership == null || religionMembership.OrganizationId == stateOrgId) continue; // not a heretic

                float hitRoll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                    (int)(ch.Id.Value & 0x7FFFFFFF), 0, S.PersecutionHitRoll);
                if (hitRoll >= hitChance) continue;

                var heresyOrg = world.Organizations[religionMembership.OrganizationId];
                float outcomeRoll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                    (int)(ch.Id.Value & 0x7FFFFFFF), 1, S.PersecutionOutcomeRoll);
                string outcome;

                if (outcomeRoll < cfg.PersecutionForcedConversionChance)
                {
                    outcome = "forced_conversion";
                    heresyOrg.Members.Remove(ch.Id);
                    ch.Memberships.Remove(religionMembership);
                    var newMembership = new Membership(stateOrgId, OrganizationRole.Member, cfg.ForcedConvertLoyalty);
                    ch.Memberships.Add(newMembership);
                    stateOrg.Members[ch.Id] = newMembership;
                }
                else
                {
                    outcome = "resisted";
                    var civMembership = ch.Memberships.FirstOrDefault(m => m.CivId.IsValid);
                    if (civMembership != null)
                    {
                        var updated = civMembership with
                        {
                            Loyalty = Math.Max(0f, civMembership.Loyalty - cfg.PersecutionCivLoyaltyPenalty)
                        };
                        ch.Memberships.Remove(civMembership);
                        ch.Memberships.Add(updated);
                        if (world.Organizations.TryGetValue(civMembership.OrganizationId, out var civOrg))
                            civOrg.Members[ch.Id] = updated;
                    }
                    ch.Needs = ch.Needs with
                    {
                        Safety = Math.Max(0f, ch.Needs.Safety - cfg.PersecutionNeedsPenalty),
                        Status = Math.Max(0f, ch.Needs.Status - cfg.PersecutionNeedsPenalty),
                    };
                }

                var payload = JsonSerializer.Serialize(new PersecutionOccurredPayload(
                    ch.Id.Value, ch.Identity.Name, civId.Value,
                    stateOrgId.Value, stateOrg.Name,
                    heresyOrg.Id.Value, heresyOrg.Name,
                    outcome));
                pending.Add(new PendingEvent(EventType.PersecutionOccurred, ch.Location, null, payload,
                    new[] { ch.Id.Value },
                    ActorId: ch.Id.Value, ActorName: ch.Identity.Name, CivId: civId.Value));
            }
        }
    }

    // ─── Religion founding ────────────────────────────────────────────────────

    private void TryFormFoundReligionGoal(Tier1Character c, WorldState world, long tick)
    {
        var cfg = world.SimConfig.Religion;
        if (c.Needs.Spiritual    < cfg.SpiritualFoundingThreshold) return;
        if (c.Skills.Piety       < cfg.PietyFoundingThreshold)     return;
        if (c.Personality.Wonder < cfg.WonderFoundingThreshold)    return;
        if (c.LastReligionFoundedYear > -999
            && world.CurrentYear - c.LastReligionFoundedYear < cfg.ReligionFoundingCooldownYears)
            return;
        if (c.Goals.Any(g => g.Type == GoalType.FoundReligion && !g.IsComplete)) return;

        c.Goals.Add(new GoalData
        {
            Type       = GoalType.FoundReligion,
            Priority   = 0.8f,
            Intensity  = 0.9f,
            Progress   = 0f,
            FormedTick = (int)tick,
            StaleSince = (int)tick,  // prevent immediate GoalManager staleness pruning
        });
    }

    private void AdvanceFoundReligionGoal(
        Tier1Character c, WorldState world, List<PendingEvent> pending, long tick = 0L)
    {
        var goal = c.Goals.FirstOrDefault(g => g.Type == GoalType.FoundReligion && !g.IsComplete);
        if (goal is null) return;

        var cfg = world.SimConfig.Religion;

        // Abandon if Spiritual has dropped well below the threshold
        if (c.Needs.Spiritual < cfg.SpiritualFoundingThreshold - 0.1f)
        {
            goal.IsComplete = true;
            return;
        }

        goal.StaleSince = (int)tick;  // refresh so GoalManager doesn't prune mid-journey
        goal.Progress = Math.Min(1f, goal.Progress + cfg.ReligionFoundingProgressPerYear);
        if (goal.Progress < 1f) return;

        goal.IsComplete = true;
        c.LastReligionFoundedYear = world.CurrentYear;
        c.Needs = c.Needs with
        {
            Purpose   = Math.Min(1f, c.Needs.Purpose   + 0.25f),
            Spiritual = Math.Min(1f, c.Needs.Spiritual + 0.15f),
            Status    = Math.Min(1f, c.Needs.Status    + 0.20f),
        };

        // M15 15.0 — Religion becomes a real Organization (previously a bare flavor event;
        // OrganizationKind.Religion existed since M12 but nothing ever instantiated it).
        var (religionName, archetypeId) = RollReligionIdentity(c, world);

        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, religionName, c.Id, c.Location);
        var org   = world.Organizations[orgId];
        org.ReligionArchetypeId = archetypeId;
        var membership = new Membership(orgId, OrganizationRole.Leader, 1.0f);
        c.Memberships.Add(membership);
        org.Members[c.Id] = membership;

        var payload = JsonSerializer.Serialize(new ReligionFoundedPayload(
            c.Id.Value, c.Identity.Name, world.CurrentYear,
            c.Location.X, c.Location.Y,
            OrganizationId: orgId.Value, ReligionName: religionName, ArchetypeId: archetypeId));
        pending.Add(new PendingEvent(EventType.ReligionFounded, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name,
            CivId: c.CivId.Value));
    }

    // ─── Pilgrimage (M15 15.4) ──────────────────────────────────────────────────

    /// <summary>
    /// Mirrors TryFormFoundReligionGoal's shape: a devout member of a Religion (Piety above
    /// threshold, past cooldown) forms a Pilgrimage goal targeting their religion's
    /// HomeSettlementCoord. Travel itself is handled by UtilityScorer (like SeaVoyage); arrival is
    /// detected in ResolveMoveWithVoyageTracking.
    /// </summary>
    private void TryFormPilgrimageGoal(Tier1Character c, WorldState world, long tick, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Religion;
        if (c.Skills.Piety < cfg.PilgrimagePietyThreshold) return;
        if (c.LastPilgrimageYear > -999
            && world.CurrentYear - c.LastPilgrimageYear < cfg.PilgrimageCooldownYears)
            return;
        if (c.Goals.Any(g => g.Type == GoalType.Pilgrimage && !g.IsComplete)) return;

        var religionMembership = c.Memberships.FirstOrDefault(m =>
            world.Organizations.TryGetValue(m.OrganizationId, out var o) && o.Kind == OrganizationKind.Religion);
        if (religionMembership == null) return;

        var org = world.Organizations[religionMembership.OrganizationId];
        if (!org.HomeSettlementCoord.HasValue || org.HomeSettlementCoord.Value == c.Location) return;

        c.Goals.Add(new GoalData
        {
            Type       = GoalType.Pilgrimage,
            Priority   = 0.6f,
            Intensity  = 0.7f,
            TargetTile = org.HomeSettlementCoord,
            FormedTick = (int)tick,
            StaleSince = (int)tick,
        });

        var payload = JsonSerializer.Serialize(new PilgrimagePayload(
            c.Id.Value, c.Identity.Name, org.Id.Value, org.Name,
            org.HomeSettlementCoord.Value.X, org.HomeSettlementCoord.Value.Y));
        pending.Add(new PendingEvent(EventType.PilgrimageEmbarked, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.CivId.Value));
    }

    /// <summary>Arrival at the pilgrimage site: completes the goal, grants the Needs/Loyalty boost, and starts the cooldown.</summary>
    private static void CompletePilgrimage(Tier1Character c, GoalData goal, WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Religion;
        goal.IsComplete = true;
        c.LastPilgrimageYear = world.CurrentYear;
        c.Needs = c.Needs with
        {
            Spiritual = Math.Min(1f, c.Needs.Spiritual + cfg.PilgrimageNeedsBoost),
            Purpose   = Math.Min(1f, c.Needs.Purpose   + cfg.PilgrimageNeedsBoost),
        };

        var religionMembership = c.Memberships.FirstOrDefault(m =>
            world.Organizations.TryGetValue(m.OrganizationId, out var o) && o.Kind == OrganizationKind.Religion);
        string religionName = "";
        long orgIdValue = 0;
        if (religionMembership != null)
        {
            var org = world.Organizations[religionMembership.OrganizationId];
            religionName = org.Name;
            orgIdValue = org.Id.Value;
            var updated = religionMembership with { Loyalty = Math.Min(1f, religionMembership.Loyalty + cfg.PilgrimageLoyaltyBoost) };
            c.Memberships.Remove(religionMembership);
            c.Memberships.Add(updated);
            org.Members[c.Id] = updated;
        }

        var payload = JsonSerializer.Serialize(new PilgrimagePayload(
            c.Id.Value, c.Identity.Name, orgIdValue, religionName, c.Location.X, c.Location.Y));
        pending.Add(new PendingEvent(EventType.PilgrimageCompleted, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.CivId.Value));
    }

    /// <summary>
    /// Selects the archetype whose affinity biases best match <paramref name="founder"/>'s
    /// PersonalityVector (see ReligionArchetypeRegistry.SelectForFounder) and rolls a deity name +
    /// name template deterministically from its pools. Shared by religion founding (15.0) and
    /// schism (15.2) — a schismatic sect re-rolls its identity the same way a founder does, which
    /// is also how genuine doctrinal drift happens: the dissenting leader's own personality picks
    /// the new archetype, not necessarily the parent religion's.
    /// </summary>
    private static (string Name, string ArchetypeId) RollReligionIdentity(Tier1Character founder, WorldState world)
    {
        var p = founder.Personality;
        var archetype = world.SimConfig.ReligionArchetypes.SelectForFounder(
            p.Compassion, p.Aggression, p.Curiosity, p.Wonder, p.Ambition, p.Stability);

        if (archetype == null)
            return ($"Faith of {founder.Identity.Name}", ""); // fallback if no archetypes configured

        string deity = archetype.DeityNames[
            (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)founder.Id.Value, 0, S.ReligionDeityName) * archetype.DeityNames.Length)];
        string template = archetype.NameTemplates[
            (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)founder.Id.Value, 1, S.ReligionNameTemplate) * archetype.NameTemplates.Length)];
        return (template.Replace("{deity}", deity), archetype.Id);
    }

    // ─── Religion schism (M15 15.2) ────────────────────────────────────────────

    /// <summary>
    /// Reuses CivSplintered's shape (gather a subset, promote a leader, transfer members, fire an
    /// event) at the Organization-membership level instead of geography. Doctrinal tension is
    /// approximated by low average non-leader Loyalty — a large religion with many
    /// exposure-converted (rather than devout) members is ripe for schism. The seceding faction is
    /// every living non-leader member at or below the org's average Loyalty, led by whichever of
    /// them has the single lowest Loyalty (the "dissenter"). See
    /// docs/phases/m15_religion_deepened.md "Long-run balance constraints" point 3.
    /// </summary>
    private void ProcessAnnualReligionSchism(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Religion;

        foreach (var org in world.Organizations.Values.ToList())
        {
            if (org.Kind != OrganizationKind.Religion || org.IsExtinct) continue;

            var livingMembers = org.Members
                .Where(kv => world.GetEntity(kv.Key) is Tier1Character t && t.IsAlive)
                .ToList();
            if (livingMembers.Count < cfg.SchismMinMembers) continue;

            var nonLeader = livingMembers.Where(kv => kv.Key != org.LeaderId).ToList();
            if (nonLeader.Count == 0) continue;

            float avgLoyalty = nonLeader.Average(kv => kv.Value.Loyalty);
            if (avgLoyalty >= cfg.SchismAvgLoyaltyThreshold) continue;

            float chance = cfg.SchismBaseChance * (1f - avgLoyalty);
            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, org.Id.Value, 0, S.ReligionSchismRoll);
            if (roll >= chance) continue;

            var seceding = nonLeader.Where(kv => kv.Value.Loyalty <= avgLoyalty).ToList();
            if (seceding.Count == 0) continue;

            var dissenterEntry = seceding.OrderBy(kv => kv.Value.Loyalty).ThenBy(kv => kv.Key.Value).First();
            var dissenter = (Tier1Character)world.GetEntity(dissenterEntry.Key)!;

            var (newName, newArchetypeId) = RollReligionIdentity(dissenter, world);
            var newOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, newName, dissenter.Id, dissenter.Location);
            var newOrg = world.Organizations[newOrgId];
            newOrg.ReligionArchetypeId = newArchetypeId;

            foreach (var (memberId, oldMembership) in seceding)
            {
                if (world.GetEntity(memberId) is not Tier1Character member) continue;
                org.Members.Remove(memberId);
                member.Memberships.Remove(oldMembership);

                var role = memberId == dissenter.Id ? OrganizationRole.Leader : OrganizationRole.Member;
                var newMembership = new Membership(newOrgId, role, oldMembership.Loyalty);
                member.Memberships.Add(newMembership);
                newOrg.Members[memberId] = newMembership;
            }

            var payload = JsonSerializer.Serialize(new ReligionSchismPayload(
                org.Id.Value, org.Name,
                newOrgId.Value, newName,
                dissenter.Id.Value, dissenter.Identity.Name,
                seceding.Count, newArchetypeId));
            pending.Add(new PendingEvent(EventType.ReligionSchism, dissenter.Location, null, payload,
                new[] { dissenter.Id.Value },
                ActorId: dissenter.Id.Value, ActorName: dissenter.Identity.Name));
        }
    }

    // ─── Command resolution ────────────────────────────────────────────────────

    private void ResolveCommand(
        ICommand cmd,
        Tier1Character c,
        WorldState world,
        List<PendingEvent> pending,
        long tick)
    {
        switch (cmd)
        {
            case MoveToTile move:
                ResolveMoveWithVoyageTracking(c, move.Destination, world, pending, tick);
                break;
            case Rest:
                ResolveRest(c);
                break;
            case CreateArtwork:
                ResolveCreateArtwork(c, world, pending, tick);
                break;
            case FleeRegion flee:
                ResolveMove(c, flee.Destination, world);
                break;
            case EstablishSettlement:
            case AllyWith:
            case DeclareRivalry:
            case DeclareWar:
            case RaidSettlement:
            case Negotiate:
            case ProposeMarriage:
            case GrantAid:
            case ForgiveDebt:
            case Placate:
            case Defect:
            case BuildImprovement:
            case ContributeToTreasury:
            case WithdrawFromTreasury:
                CivTracker.Resolve(cmd, world, pending, _simCfg.SettlementNames);
                break;
        }
    }

    private static void ResolveMove(Tier1Character c, TileCoord dest, WorldState world)
    {
        world.Entities.UpdateLocation(c.Id, c.Location, dest);
        c.Location = dest;
        c.TicksInCurrentTile = 0;
    }

    // M11 — wraps ResolveMove to detect a SeaVoyage goal crossing the land/water boundary and
    // emit SeaVoyageEmbarked/Completed. No new ICommand: MoveToTile already carries everything
    // resolution needs, and "did this move cross water" is fully determined by comparing
    // old/new tile land status, not something the emitting (EMIT-step) code needs to flag.
    // M15 15.4 — also detects a Pilgrimage goal's arrival at its TargetTile the same way.
    private static void ResolveMoveWithVoyageTracking(
        Tier1Character c, TileCoord dest, WorldState world, List<PendingEvent> pending, long tick)
    {
        var voyageGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.SeaVoyage && !g.IsComplete);
        var pilgrimageGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.Pilgrimage && !g.IsComplete && g.TargetTile.HasValue);
        bool oldWasLand = world.IsLand(c.Location);

        ResolveMove(c, dest, world);

        if (pilgrimageGoal != null && dest == pilgrimageGoal.TargetTile!.Value)
            CompletePilgrimage(c, pilgrimageGoal, world, pending);

        if (voyageGoal == null) return;
        bool newIsLand = world.IsLand(dest);

        if (oldWasLand && !newIsLand)
        {
            var payload = JsonSerializer.Serialize(new SeaVoyagePayload(
                c.Id.Value, c.Identity.Name, c.CivId.Value, dest.X, dest.Y));
            pending.Add(new PendingEvent(EventType.SeaVoyageEmbarked, dest, null, payload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.CivId.Value));
        }
        else if (!oldWasLand && newIsLand)
        {
            voyageGoal.IsComplete = true;
            var payload = JsonSerializer.Serialize(new SeaVoyagePayload(
                c.Id.Value, c.Identity.Name, c.CivId.Value, dest.X, dest.Y));
            pending.Add(new PendingEvent(EventType.SeaVoyageCompleted, dest, null, payload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.CivId.Value));

            // Closing the loop: a delegate who crossed the water is here to found a city, same as
            // any other FoundCity delegate — reuse the existing flow rather than a second founding
            // mechanism (see m11_phase2_delegation_behavior.md).
            c.Goals.Add(new GoalData
            {
                Type       = GoalType.FoundCity,
                Priority   = 0.9f,
                StaleSince = (int)tick,
                FormedTick = (int)tick
            });
        }
    }

    private static void ResolveRest(Tier1Character c)
    {
        // Resting restores physical needs plus identity/spiritual needs —
        // stillness enables reflection, contemplation, and sense of self.
        c.Needs = c.Needs with
        {
            Safety    = Math.Min(1f, c.Needs.Safety    + 0.05f),
            Food      = Math.Min(1f, c.Needs.Food      + 0.05f),
            Shelter   = Math.Min(1f, c.Needs.Shelter   + 0.03f),
            Status    = Math.Min(1f, c.Needs.Status    + 0.01f),
            Purpose   = Math.Min(1f, c.Needs.Purpose   + 0.02f),
            Spiritual = Math.Min(1f, c.Needs.Spiritual + 0.03f),
        };
    }

    // ─── Territorial pressure ────────────────────────────────────────────────

    /// <summary>
    /// Aggressive founders who see foreign chars on their settlement tile slowly
    /// develop negative trust with them — the seed of rivalry and eventual war.
    /// </summary>
    private void ApplyTerritorialPressure(Tier1Character c, WorldState world, long tick)
    {
        if (c.Personality.Aggression < _cfg.TerritorialAggressionMin) return;
        if (!world.Settlements.ContainsKey(c.Location)) return;

        // Only applies to the founding char (or any char with high aggression at their own settlement)
        bool atOwnSettlement = world.Settlements.TryGetValue(c.Location, out var stub)
            && stub.CivId == c.CivId;
        if (!atOwnSettlement) return;

        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            // Tier1-only by design — see docs/phases/m13_8_tier2_relationship_exposure.md (M13.8.0).
            // A co-located Tier2 must never accrue territorial-pressure drain. Do not widen this to
            // Tier2Character (which has no CivId to compare against anyway).
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            if (other.CivId == c.CivId) continue; // same civ — no pressure

            var rel = world.Relationships.GetOrCreate(c.Id, other.Id);
            if (rel.IsAlly || rel.IsRival) continue; // relationship already decided

            // Drain trust by a small amount each tick — enough to reach -0.1 within ~5 years of contact
            world.Relationships.Upsert(rel with
            {
                Trust = Math.Max(-0.5f, rel.Trust - _cfg.TerritorialTrustDrain)
            });
        }
    }

    // ─── Passive trust drains (ancestry cultural distance + personality mismatch) ──

    /// <summary>
    /// Applied each tick for every pair of co-located characters from different civs.
    /// First meeting applies a one-time ancestry modifier; subsequent ticks drain by
    /// cultural distance and personality mismatch.
    /// </summary>
    private void ApplyPassiveDrains(Tier1Character c, WorldState world)
    {
        var registry = _simCfg.AncestryRegistry;

        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            // Only drain between chars of different civs; same-civ pairs use ApplySameCivFamiliarity instead.
            if (c.CivId == other.CivId) continue;

            bool isFirstMeeting = world.Relationships.Get(c.Id, other.Id) == null;
            var rel = world.Relationships.GetOrCreate(c.Id, other.Id);
            if (rel.IsAlly) continue;

            float trust = rel.Trust;

            if (isFirstMeeting)
            {
                // First-meeting modifier: average of both ancestries' view of the other
                float modifierAB = registry.GetFirstMeetingTrust(c.Identity.AncestryId, other.Identity.AncestryId);
                float modifierBA = registry.GetFirstMeetingTrust(other.Identity.AncestryId, c.Identity.AncestryId);
                trust += (modifierAB + modifierBA) * 0.5f;

                // Seed cross-civ awareness: both civs learn of each other at WandererMet fidelity
                float encounterGain = _simCfg.Emissary.EncounterConfidenceGain;
                if (c.CivId.IsValid && other.CivId.IsValid)
                {
                    if (world.Civilizations.TryGetValue(other.CivId, out var otherCiv))
                    {
                        CivTracker.SeedCivContact(c.CivId, other.CivId,
                            CivContactSource.WandererMet, otherCiv.CapitalTile, encounterGain, world);
                    }
                    if (world.Civilizations.TryGetValue(c.CivId, out var cCiv))
                    {
                        CivTracker.SeedCivContact(other.CivId, c.CivId,
                            CivContactSource.WandererMet, cCiv.CapitalTile, encounterGain, world);
                    }
                }
            }

            // Cultural distance drain — proportional to how different the ancestries are
            float culturalDist = registry.GetCulturalDistance(c.Identity.AncestryId, other.Identity.AncestryId);
            trust -= culturalDist * _cfg.CulturalDistanceDrainRate;

            // Personality mismatch drain — characters with very different Stability punish each other
            float stabilityDiff = Math.Abs(c.Personality.Stability - other.Personality.Stability);
            trust -= stabilityDiff * _cfg.PersonalityMismatchDrainRate;

            world.Relationships.Upsert(rel with { Trust = Math.Clamp(trust, -1f, 1f) });
        }
    }

    // ─── Same-civ Trust economy (M13 13.6) ─────────────────────────────────────

    /// <summary>
    /// Applied each tick for every pair of co-located characters from the SAME civ — the
    /// counterpart <see cref="ApplyPassiveDrains"/> never had. Warmth (Sociability/Compassion)
    /// grows Trust toward Bond/Marriage/Debt eligibility; clash (Ambition/Aggression mismatch)
    /// drains it toward the existing DeclareRivalry threshold, which was never reachable for
    /// same-civ pairs before (only cross-civ contact could push Trust negative). Also the reason
    /// a married couple's Trust can move at all now (Estrangement's precondition) and, as a side
    /// effect, gives parent-child pairs their first organic Trust edge (commonly co-located
    /// same-civ pairs too) — a companion is not built, personality compatibility just quietly does
    /// the work for whichever pairing happens to be nearby.
    /// </summary>
    private void ApplySameCivFamiliarity(Tier1Character c, WorldState world)
    {
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            if (!c.CivId.IsValid || c.CivId != other.CivId) continue;

            var rel = world.Relationships.GetOrCreate(c.Id, other.Id);
            if (rel.IsFeud) continue; // fully escalated — Reconciliation is the only way back

            float warmth = (c.Personality.Sociability + other.Personality.Sociability
                          + c.Personality.Compassion  + other.Personality.Compassion) * 0.25f;
            float growth = _cfg.SameCivFamiliarityBaseRate + _cfg.SameCivWarmthBonusRate * warmth;

            float clash = (Math.Abs(c.Personality.Ambition   - other.Personality.Ambition)
                         + Math.Abs(c.Personality.Aggression - other.Personality.Aggression)) * 0.5f;
            float friction = _cfg.SameCivFrictionBaseRate + _cfg.SameCivFrictionRate * clash;

            world.Relationships.Upsert(rel with { Trust = Math.Clamp(rel.Trust + growth - friction, -1f, 1f) });
        }
    }

    // ─── Artwork creation ────────────────────────────────────────────────────


    private void ResolveCreateArtwork(
        Tier1Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        // Progress the Create goal and boost Wellbeing regardless of cooldown
        var createGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.Create);
        if (createGoal != null)
        {
            createGoal.Progress = Math.Min(1f, createGoal.Progress + 0.2f);
            // Don't reset StaleSince — let the existing staleness pruning work normally.
            // Mark complete when the project finishes (5 artworks at +0.2 each).
            if (createGoal.Progress >= 1.0f)
            {
                createGoal.IsComplete = true;
                c.LastCreateCompletedTick = (int)tick;
            }
        }
        c.Wellbeing = Math.Min(1f, c.Wellbeing + 0.05f);

        // Gate ArtworkCreated events to at most one per cooldown period to prevent
        // 200k+ event explosion when a character with an active Create goal spams per-tick.
        if (world.CurrentYear - c.LastArtworkYear < _cfg.ArtworkCooldownYears) return;
        c.LastArtworkYear = world.CurrentYear;

        // Art type weighted toward character personality:
        // high Compassion → Epic/Song (social/emotional), high Ingenuity → Sculpture/Painting,
        // high Aggression → Monument (assertive permanence)
        var artGoods = CreatedGoodTaxonomy.ArtGoods;
        int artIndex = (int)(world.GetRandomFloat(c.Id, S.CharArtType) * artGoods.Length) % artGoods.Length;
        var artType = artGoods[artIndex];

        // Apply a small culture cohesion bonus to the settlement where the artwork is created.
        if (world.Settlements.TryGetValue(c.Location, out var homeStub))
        {
            var stores = homeStub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(homeStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
            stores["bonus_civ_cohesion"] = (stores.TryGetValue("bonus_civ_cohesion", out var cur) ? cur : 0f) + 0.02f;
            world.Settlements[c.Location] = homeStub with { ResourceStores = stores };
        }

        var payload = JsonSerializer.Serialize(new ArtworkCreatedPayload(
            c.Id.Value, c.Identity.Name, artType.ToString(), c.Wellbeing));
        pending.Add(new PendingEvent(EventType.ArtworkCreated, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    // ─── Emotional state events ──────────────────────────────────────────────

    private static void EmitFlourishingEvent(Tier1Character c, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new CharacterWellbeingPayload(
            c.Id.Value, c.Identity.Name, c.Wellbeing));
        pending.Add(new PendingEvent(EventType.CharacterFlourishing, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    private static void EmitSpiralEvent(Tier1Character c, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new CharacterWellbeingPayload(
            c.Id.Value, c.Identity.Name, c.Wellbeing));
        pending.Add(new PendingEvent(EventType.CharacterSpiraling, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    // ─── Beast encounters ────────────────────────────────────────────────────


    /// <summary>
    /// When a predatory beast and a character share a tile, there is a chance
    /// the beast attacks. Characters can be wounded or killed; this creates the
    /// adventure/survival events the history log needs.
    /// </summary>
    private void CheckBeastEncounters(
        Tier1Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Entities.Beasts.LegendaryBeast beast || !beast.IsAlive) continue;
            if (beast.Aggression < _cfg.BeastEncounterAggressionMin) continue;

            float roll = WorldRng.FloatAt(world.WorldSeed, tick,
                                          (int)(beast.Id.Value & 0x7FFFFFFF),
                                          (int)(c.Id.Value & 0x7FFFFFFF),
                                          S.CharBeastEncounter);
            if (roll > _cfg.BeastEncounterChance) continue;

            int damage = Math.Max(1, (int)(beast.Strength * _cfg.BeastDamageMultiplier));
            c.Health -= damage;

            // Counter-attack: character fights back; Combat skill scales how hard they hit.
            int counterDamage = Math.Max(1, (int)(c.Skills.Combat * _cfg.MaxHealth * _cfg.CharCounterDamageMultiplier));
            beast.Health -= counterDamage;

            var payload = JsonSerializer.Serialize(new BeastCharEncounterPayload(
                c.Id.Value, c.Identity.Name, beast.Id.Value, beast.Name,
                damage, counterDamage, c.Health, beast.Health));
            pending.Add(new PendingEvent(EventType.BeastAttackedChar, c.Location, null, payload,
                new[] { c.Id.Value }, new[] { beast.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name));

            if (beast.Health <= 0)
            {
                beast.IsAlive = false;
                var slainPayload = JsonSerializer.Serialize(new BeastDeathPayload(
                    beast.Id.Value, beast.Name, beast.SpeciesId, beast.IsLegendary, beast.AgeSeason,
                    $"slain by {c.Identity.Name}", c.Id.Value, c.Identity.Name));
                pending.Add(new PendingEvent(EventType.BeastSlain, c.Location, null, slainPayload,
                    new[] { beast.Id.Value }, new[] { c.Id.Value },
                    ActorId: c.Id.Value, ActorName: c.Identity.Name));

                // Complete any SlayBeast goal targeting this beast across all characters
                foreach (var hunter in world.Entities.Characters)
                {
                    var huntGoal = hunter.Goals
                        .FirstOrDefault(g => g.Type == Entities.Characters.GoalType.SlayBeast
                                          && g.TargetEntityId == beast.Id);
                    if (huntGoal != null)
                        huntGoal.IsComplete = true;
                }
            }

            if (c.Health <= 0)
            {
                KillCharacter(c, world, $"killed by {beast.Name}", pending);
                break; // character is dead — stop processing further beasts this tick
            }
        }
    }
}
