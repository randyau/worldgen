using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
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

    public CharacterBehaviorPhase(SimConfig cfg)
    {
        _simCfg    = cfg;
        _cfg       = cfg.Character;
        _settleCfg = cfg.Settlement;
    }

    public List<PendingEvent> Execute(WorldState world, long tick, bool isAnnualTick = false)
    {
        var pending = new List<PendingEvent>();

        // Snapshot to avoid modify-during-iteration
        var characters = world.Entities.Characters.ToList();
        var deathsThisTick = new List<(EntityId Id, string Name)>();

        foreach (var c in characters)
        {
            if (!c.IsAlive) continue;
            UpdateLifecycle(c, world, tick, pending);
            if (!c.IsAlive) { deathsThisTick.Add((c.Id, c.Identity.Name)); continue; }

            // Annual disease: exposure at infected settlements, health drain, natural recovery
            if (isAnnualTick) ProcessAnnualDisease(c, world, pending);
            if (!c.IsAlive) { deathsThisTick.Add((c.Id, c.Identity.Name)); continue; }

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
            CheckBeastEncounters(c, world, pending, tick);
            // Catch wound death from beast/battle damage accumulated this tick
            if (c.IsAlive && c.Health <= 0)
            {
                KillCharacter(c, world, "wounds", pending);
                deathsThisTick.Add((c.Id, c.Identity.Name));
            }
            if (!c.IsAlive) continue;
            var cmd = UtilityScorer.SelectAction(c, world, _cfg);
            if (cmd != null)
                ResolveCommand(cmd, c, world, pending, tick);
        }

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
            TrySpawnCivBorn(world, pending, tick);

        return pending;
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
            born.Identity = born.Identity with { CivId = stub.CivId, NameOrdinal = bornOrdinal };
            civ.Members.Add(born.Id);
            world.Entities.Add(born);

            // Emigrant: seed FoundCity goal immediately and deduct population from parent
            if (overCapacity)
            {
                born.Goals.Add(new GoalData
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
                born.Personality.Ambition, born.Personality.Aggression));
            pending.Add(new PendingEvent(EventType.CharacterBorn, born.Location, null, payload,
                new[] { born.Id.Value },
                ActorId: born.Id.Value, ActorName: born.Identity.Name, CivId: stub.CivId.Value));
        }
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

    private static void KillCharacter(
        Tier1Character c, WorldState world, string cause, List<PendingEvent> pending)
    {
        c.IsAlive = false;

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

        var payload = JsonSerializer.Serialize(new CharacterDeathPayload(
            c.Id.Value, c.Identity.Name, cause, c.AgeSeason));
        pending.Add(new PendingEvent(EventType.CharacterDied, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));

        // Handle succession when a civ member dies
        if (c.Identity.CivId.IsValid
            && world.Civilizations.TryGetValue(c.Identity.CivId, out var civ))
        {
            bool wasRuler = civ.RulerId == c.Id;
            civ.Members.Remove(c.Id);

            // Succession: promote the highest-scoring living member to ruler
            if (wasRuler && !civ.IsCollapsed)
            {
                EntityId? successorId = null;
                float bestScore = float.MinValue;
                foreach (var memberId in civ.Members)
                {
                    if (world.GetEntity(memberId) is not Tier1Character member || !member.IsAlive) continue;
                    float score = (member.Personality.Aggression + member.Skills.Leadership) * 0.5f;
                    if (score > bestScore) { bestScore = score; successorId = memberId; }
                }
                if (successorId.HasValue)
                {
                    civ.RulerId = successorId.Value;
                    civ.RulerCount++;
                    civ.TotalSuccessions++;
                    var successor = (Tier1Character)world.GetEntity(successorId.Value)!;
                    successor.Identity = successor.Identity with { RulerOrdinal = civ.RulerCount };
                    string[]? civTraits = civ.CulturalTraits.Count > 0 ? civ.CulturalTraits.ToArray() : null;
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

            if (civ.Members.Count == 0 && !civ.IsCollapsed)
            {
                civ.IsCollapsed = true;
                var civPayload = JsonSerializer.Serialize(new CivCollapsedPayload(civ.Id.Value));
                pending.Add(new PendingEvent(
                    EventType.CivilizationCollapsed, civ.CapitalTile, null, civPayload,
                    CivId: civ.Id.Value));
            }
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
                ResolveMove(c, move.Destination, world);
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
            && stub.CivId == c.Identity.CivId;
        if (!atOwnSettlement) return;

        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            if (other.Identity.CivId == c.Identity.CivId) continue; // same civ — no pressure

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
            // Only drain between chars of different civs; same-civ relations handled by territorial pressure
            if (c.Identity.CivId == other.Identity.CivId) continue;

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
                if (c.Identity.CivId.IsValid && other.Identity.CivId.IsValid)
                {
                    if (world.Civilizations.TryGetValue(other.Identity.CivId, out var otherCiv))
                    {
                        CivTracker.SeedCivContact(c.Identity.CivId, other.Identity.CivId,
                            CivContactSource.WandererMet, otherCiv.CapitalTile, encounterGain, world);
                    }
                    if (world.Civilizations.TryGetValue(c.Identity.CivId, out var cCiv))
                    {
                        CivTracker.SeedCivContact(other.Identity.CivId, c.Identity.CivId,
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

    // ─── Artwork creation ────────────────────────────────────────────────────


    private static void ResolveCreateArtwork(
        Tier1Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        // Progress the Create goal and boost Wellbeing
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

        // Art type weighted toward character personality:
        // high Compassion → Epic/Song (social/emotional), high Ingenuity → Sculpture/Painting,
        // high Aggression → Monument (assertive permanence)
        int artCount = Enum.GetValues<ArtType>().Length;
        int artIndex = (int)(world.GetRandomFloat(c.Id, S.CharArtType) * artCount) % artCount;
        var artType = (ArtType)artIndex;

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
            }

            if (c.Health <= 0)
                KillCharacter(c, world, $"killed by {beast.Name}", pending);
        }
    }
}
