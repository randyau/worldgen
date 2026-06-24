using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Resolves character commands that affect civilizations, settlements, and relationships.
/// </summary>
public static class CivTracker
{
    private const int SettlementStartPop    = 50;
    private const int SettlementStartHealth = 100;
    private const int RaidDamageMin         = 10;
    private const int RaidDamageMax         = 30;
    private const int SaltRaidDamage        = 700;

    public static void Resolve(
        ICommand cmd,
        WorldState world,
        List<PendingEvent> pending,
        SettlementNamesConfig? namesConfig = null)
    {
        switch (cmd)
        {
            case EstablishSettlement es:
                ResolveEstablish(es, world, pending, namesConfig ?? new()); break;
            case AllyWith aw:
                ResolveAlly(aw, world, pending); break;
            case DeclareRivalry dr:
                ResolveRivalry(dr, world, pending); break;
            case DeclareWar dw:
                ResolveWar(dw, world, pending); break;
            case RaidSettlement rs:
                ResolveRaid(rs, world, pending); break;
            case Negotiate ng:
                ResolveNegotiate(ng, world, pending); break;
        }
    }

    // ─── Establish ────────────────────────────────────────────────────────────

    private static void ResolveEstablish(
        EstablishSettlement cmd, WorldState world, List<PendingEvent> pending,
        SettlementNamesConfig namesConfig)
    {
        if (world.Settlements.ContainsKey(cmd.Tile)) return;
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character founder) return;

        // Create settlement
        var civId = founder.Identity.CivId;
        bool newCiv = !civId.IsValid;
        if (newCiv)
        {
            civId = new CivId(world.NextCivId++);
            string civName = $"{founder.Identity.Name}'s Domain";
            var civ = new Civilization(civId, civName, founder.Id, cmd.Tile, world.CurrentYear);
            civ.Members.Add(founder.Id);
            world.Civilizations[civId] = civ;
            founder.Identity = founder.Identity with { CivId = civId };

            FireCivFounded(civ, founder, world, pending);
        }
        else
        {
            world.Civilizations[civId].Members.Add(founder.Id);
        }

        string settlementName    = GenerateSettlementName(cmd.Tile, world, namesConfig);
        float  fertilityVariance = GenerateFertilityMultiplier(cmd.Tile, world);

        // Classify: colony if no same-civ settlement is within ColonyMinDistance tiles
        int colonyMinDist = world.SimConfig.Character.ColonyMinDistance;
        bool isColony = !newCiv && !world.Settlements.Values
            .Any(s => s.CivId == civId
                   && Math.Sqrt(Math.Pow(s.Tile.X - cmd.Tile.X, 2) + Math.Pow(s.Tile.Y - cmd.Tile.Y, 2)) < colonyMinDist);

        var stub = new SettlementStub(
            FounderId:           founder.Id,
            CivId:               civId,
            Tile:                cmd.Tile,
            FoundedYear:         world.CurrentYear,
            Population:          SettlementStartPop,
            Health:              SettlementStartHealth,
            Name:                settlementName,
            FertilityMultiplier: fertilityVariance,
            IsColony:            isColony);
        world.Settlements[cmd.Tile] = stub;
        world.AddActiveFounder(founder.Id);
        var civRecord = world.Civilizations[civId];
        if (isColony) civRecord.ColonyCount++;
        else          civRecord.SettlementCount++;
        civRecord.LastSettlementFoundedYear = world.CurrentYear;

        // Mark goal as progressed (works for both Expansion and Colonize)
        foreach (var g in founder.Goals)
            if (g.Type == GoalType.Expansion || g.Type == GoalType.Colonize)
                g.Progress = Math.Min(1f, g.Progress + 0.5f);

        FireSettlementFounded(stub, founder, world, pending);

        founder.Needs = founder.Needs with
        {
            Status  = Math.Min(1f, founder.Needs.Status  + 0.2f),
            Purpose = Math.Min(1f, founder.Needs.Purpose + 0.15f)
        };
        founder.Skills = founder.Skills with
        {
            Leadership    = Math.Min(1f, founder.Skills.Leadership    + 0.02f),
            Administration = Math.Min(1f, founder.Skills.Administration + 0.02f)
        };
    }

    // ─── Alliance ─────────────────────────────────────────────────────────────

    private static void ResolveAlly(AllyWith cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsAlly) return;

        var cfg = world.SimConfig.Character;

        // Cross-civ only — same-civ relationships are just trust edges
        if (c.Identity.CivId.IsValid && target.Identity.CivId.IsValid
            && c.Identity.CivId == target.Identity.CivId) return;

        // Alliance cap
        int allianceMax = cfg.AllianceMaxBase + (int)(c.Personality.Sociability * cfg.AllianceMaxPerSociability);
        if (world.Relationships.CountAlliances(c.Id) >= allianceMax) return;

        // Enemy-of-ally: if target is allied with any of c's rivals, drain that relationship
        foreach (var bEdge in world.Relationships.GetAll(target.Id).Where(e => e.IsAlly).ToList())
        {
            var thirdId = bEdge.From == target.Id ? bEdge.To : bEdge.From;
            var cThird  = world.Relationships.Get(c.Id, thirdId);
            if (cThird?.IsRival ?? false)
            {
                world.Relationships.Upsert(cThird with
                {
                    Trust = Math.Clamp(cThird.Trust - cfg.EnemyOfAllyTrustDrain, -1f, 1f)
                });
            }
        }

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(1f, rel.Trust + 0.3f),
            Flags = rel.Flags | RelationshipFlags.IsAlly
        });

        c.Needs      = c.Needs with { Belonging = Math.Min(1f, c.Needs.Belonging + 0.15f) };
        target.Needs = target.Needs with { Belonging = Math.Min(1f, target.Needs.Belonging + 0.1f) };
        c.Skills     = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.02f) };

        foreach (var g in c.Goals)
            if (g.Type == GoalType.Alliance && g.TargetEntityId == target.Id)
                g.Progress = 1f;

        var payload = JsonSerializer.Serialize(new
        {
            declarerId   = c.Id.Value,
            declarerName = c.Identity.Name,
            targetId     = target.Id.Value,
            targetName   = target.Identity.Name,
            declarerCiv  = c.Identity.CivId.Value,
            targetCiv    = target.Identity.CivId.Value,
            location     = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.AllianceFormed, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── Rivalry ──────────────────────────────────────────────────────────────

    private static void ResolveRivalry(
        DeclareRivalry cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsRival) return;

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(rel.Trust, -0.1f),
            Fear  = Math.Min(1f, rel.Fear + 0.1f),
            Flags = rel.Flags | RelationshipFlags.IsRival
        });

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            charName    = c.Identity.Name,
            targetName  = target.Identity.Name
        });
        pending.Add(new PendingEvent(EventType.RivalryFormed, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── War ──────────────────────────────────────────────────────────────────

    private static void ResolveWar(DeclareWar cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (!c.Identity.CivId.IsValid) return;
        var declCiv = world.GetCivilization(c.Identity.CivId);
        var targCiv = world.GetCivilization(cmd.TargetCivId);
        if (declCiv == null || targCiv == null || declCiv.IsCollapsed || targCiv.IsCollapsed) return;
        StartWarBetween(declCiv, targCiv, "character_encounter", world, pending);
    }

    /// <summary>
    /// Records war on both sides, applies trust damage between rulers, and fires WarDeclared.
    /// Called from both character-command resolution and the annual border-tension check, so
    /// the logic lives here rather than being duplicated in ResolveWar.
    /// </summary>
    private static void StartWarBetween(
        Civilization declCiv, Civilization targCiv, string cause,
        WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;
        if (declCiv.IsAtWarWith(targCiv.Id)) return;
        if (declCiv.InPeaceCooldownWith(targCiv.Id, world.CurrentYear, cfg.PeaceCooldownYears)) return;

        declCiv.WarsAgainst[targCiv.Id] = world.CurrentYear;
        targCiv.WarsAgainst[declCiv.Id] = world.CurrentYear;

        // Trust hit between rulers even if they've never met — reputation travels
        var declFounder = world.GetEntity(declCiv.FounderId) as Tier1Character;
        var targFounder = world.GetEntity(targCiv.FounderId) as Tier1Character;
        if (declFounder != null && targFounder != null)
        {
            var rel = world.Relationships.GetOrCreate(declFounder.Id, targFounder.Id);
            bool wasAllied = rel.IsAlly;
            world.Relationships.Upsert(rel with
            {
                Trust = Math.Min(rel.Trust - 0.3f, -0.3f),
                Flags = (rel.Flags & ~RelationshipFlags.IsAlly) | RelationshipFlags.IsRival,
            });
            if (wasAllied) FireAllianceBroken(declFounder, targFounder, "war_declared", world, pending);
        }

        var eventTile = declFounder?.Location ?? declCiv.CapitalTile;
        var payload = JsonSerializer.Serialize(new
        {
            declarerId      = declCiv.FounderId.Value,
            declarerName    = declFounder?.Identity.Name ?? declCiv.Name,
            declarerCiv     = declCiv.Id.Value,
            declarerCivName = declCiv.Name,
            targetCiv       = targCiv.Id.Value,
            targetCivName   = targCiv.Name,
            cause,
        });
        pending.Add(new PendingEvent(EventType.WarDeclared, eventTile, null, payload,
            new[] { declCiv.FounderId.Value }));
    }

    // ─── Raid ─────────────────────────────────────────────────────────────────

    private static void ResolveRaid(
        RaidSettlement cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character raider) return;
        if (!world.Settlements.TryGetValue(cmd.SettlementTile, out var settlement)) return;

        int damage = RaidDamageMin
            + (int)(world.GetRandomFloat(raider.Id, SaltRaidDamage)
                    * (RaidDamageMax - RaidDamageMin));
        int newHealth = settlement.Health - damage;

        // Raids burn granaries and loot vaults — all resource stores take proportional damage.
        float destroyFraction = Math.Min(1f, damage * world.SimConfig.ResourcePressure.StoreRaidDestructionPerDamage);
        IReadOnlyDictionary<string, float>? newStores = null;
        if (settlement.ResourceStores is { Count: > 0 } existingStores)
        {
            var damaged = new Dictionary<string, float>(existingStores, StringComparer.OrdinalIgnoreCase);
            foreach (var key in damaged.Keys.ToList())
                damaged[key] = Math.Max(0f, damaged[key] * (1f - destroyFraction));
            newStores = damaged;
        }

        raider.Skills = raider.Skills with
            { Combat = Math.Min(1f, raider.Skills.Combat + 0.02f) };
        raider.Needs = raider.Needs with
            { Status = Math.Min(1f, raider.Needs.Status + 0.1f) };

        var payload = JsonSerializer.Serialize(new
        {
            raiderId   = raider.Id.Value,
            raiderName = raider.Identity.Name,
            tile       = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
            damage,
            settlementHealth = newHealth
        });
        pending.Add(new PendingEvent(EventType.BattleOccurred, cmd.SettlementTile, null, payload,
            new[] { raider.Id.Value }));

        if (newHealth <= 0)
        {
            bool canConquer = raider.Identity.CivId.IsValid
                           && settlement.CivId.IsValid
                           && raider.Identity.CivId != settlement.CivId;

            if (canConquer)
            {
                // Annexation: settlement survives under the raider's civ.
                // Population and health are reduced; the original founder remains in place (now a subject).
                CivId previousCivId = settlement.CivId;
                int   conqueredPop  = Math.Max(1, settlement.Population / 2);
                world.Settlements[cmd.SettlementTile] = settlement with
                {
                    CivId              = raider.Identity.CivId,
                    Health             = SettlementStartHealth / 2,
                    Population         = conqueredPop,
                    PopulationF        = 0f,
                    ConqueredYear      = world.CurrentYear,
                    ConqueredFromCivId = previousCivId.Value,
                    ResourceStores     = newStores, // granaries looted/burned during conquest
                };

                // Transfer settlement/colony count from losing civ to winning civ
                if (world.Civilizations.TryGetValue(previousCivId, out var losingCiv))
                {
                    if (settlement.IsColony) losingCiv.ColonyCount    = Math.Max(0, losingCiv.ColonyCount    - 1);
                    else                     losingCiv.SettlementCount = Math.Max(0, losingCiv.SettlementCount - 1);
                }
                if (world.Civilizations.TryGetValue(raider.Identity.CivId, out var winningCiv))
                {
                    if (settlement.IsColony) winningCiv.ColonyCount++;
                    else                     winningCiv.SettlementCount++;
                }

                pending.Add(new PendingEvent(EventType.SettlementConquered, cmd.SettlementTile, null,
                    JsonSerializer.Serialize(new
                    {
                        tile             = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
                        settlementName   = settlement.Name,
                        conqueredByCivId = raider.Identity.CivId.Value,
                        previousCivId    = previousCivId.Value,
                        conquererId      = raider.Id.Value,
                        conquererName    = raider.Identity.Name,
                        survivingPop     = conqueredPop
                    })));

                // If the losing civ has no settlements left, it collapses.
                bool anyLeft = losingCiv != null && losingCiv.SettlementCount > 0;
                if (!anyLeft)
                    pending.Add(new PendingEvent(EventType.CivilizationCollapsed, cmd.SettlementTile, null,
                        JsonSerializer.Serialize(new
                        {
                            civId  = previousCivId.Value,
                            reason = "conquered",
                            year   = world.CurrentYear
                        })));
            }
            else
            {
                world.Settlements.Remove(cmd.SettlementTile);
                int timesSettled = RegisterRuin(cmd.SettlementTile, settlement, "destroyed", world);
                pending.Add(new PendingEvent(EventType.SettlementDestroyed, cmd.SettlementTile, null,
                    JsonSerializer.Serialize(new
                    {
                        tile           = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
                        settlementName = settlement.Name,
                        founderId      = settlement.FounderId.Value,
                        destroyerId    = raider.Id.Value,
                        civId          = settlement.CivId.Value,
                        timesSettled
                    })));
            }
        }
        else
        {
            world.Settlements[cmd.SettlementTile] = settlement with
            {
                Health         = newHealth,
                ResourceStores = newStores
            };
        }
    }

    // ─── Negotiate ────────────────────────────────────────────────────────────

    private static void ResolveNegotiate(
        Negotiate cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        float trustGain = 0.05f + c.Skills.Diplomacy * 0.1f;
        world.Relationships.Upsert(rel with { Trust = Math.Clamp(rel.Trust + trustGain, -1f, 1f) });

        c.Skills = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.01f) };

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            trustGain
        });
        pending.Add(new PendingEvent(EventType.Negotiated, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── Ruin registration ────────────────────────────────────────────────────

    /// <summary>
    /// Records a settlement tile as a ruin. Increments TimesSettled if the tile has been ruined before.
    /// Returns the new TimesSettled count.
    /// </summary>
    public static int RegisterRuin(
        TileCoord tile, SettlementStub stub, string cause, WorldState world)
    {
        int timesSettled = world.Ruins.TryGetValue(tile, out var existing)
            ? existing.TimesSettled + 1
            : 1;

        world.Ruins[tile] = new RuinRecord(
            Tile:           tile,
            SettlementName: stub.Name,
            OriginalCivId:  stub.CivId,
            DestroyedYear:  world.CurrentYear,
            Cause:          cause,
            TimesSettled:   timesSettled);

        world.RemoveActiveFounder(stub.FounderId);

        if (world.Civilizations.TryGetValue(stub.CivId, out var civ))
        {
            if (stub.IsColony) civ.ColonyCount    = Math.Max(0, civ.ColonyCount    - 1);
            else               civ.SettlementCount = Math.Max(0, civ.SettlementCount - 1);
        }

        return timesSettled;
    }

    // ─── Annual diplomacy maintenance ─────────────────────────────────────────

    /// <summary>
    /// Called once per year (Spring).
    /// 1. Dissolves alliances where trust has fallen below the floor.
    /// 2. Expires wars that have lasted beyond MaxWarDurationYears.
    /// 3. Prunes relationship edges where both characters are dead (keeps graph lean).
    /// </summary>
    public static void RunAnnualDiplomacy(WorldState world, List<PendingEvent> pending)
    {
        var cfg       = world.SimConfig.Character;
        var toProcess = world.Relationships.AllEdges.ToList(); // snapshot before mutations

        foreach (var edge in toProcess)
        {
            bool aAlive = world.GetEntity(edge.From) is Tier1Character;
            bool bAlive = world.GetEntity(edge.To)   is Tier1Character;

            // 1. Prune stale edges where both chars are dead
            if (!aAlive && !bAlive)
            {
                world.Relationships.Remove(edge.From, edge.To);
                continue;
            }

            // 2. Alliance dissolution on trust decay
            if (edge.IsAlly && edge.Trust < cfg.AllianceTrustFloor)
            {
                world.Relationships.Upsert(edge with
                {
                    Flags = edge.Flags & ~RelationshipFlags.IsAlly
                });

                if (aAlive && bAlive &&
                    world.GetEntity(edge.From) is Tier1Character a &&
                    world.GetEntity(edge.To)   is Tier1Character b)
                    FireAllianceBroken(a, b, "trust_decay", world, pending);
            }

            // War expiry is handled at civ level below; no character-level war state here.
        }

        // 3. Border tension: accumulate territorial pressure; declare war if threshold crossed
        RunBorderTension(world, pending);

        // 4. Succession crisis: detect founding ruler death and flag distant settlements
        foreach (var civ in world.Civilizations.Values)
        {
            if (civ.IsCollapsed || civ.SuccessionCrisisEndYear != int.MinValue) continue;
            bool founderAlive = world.GetEntity(civ.FounderId) is Tier1Character fc && fc.IsAlive;
            if (founderAlive) continue;

            civ.SuccessionCrisisEndYear = world.CurrentYear + cfg.SuccessionCrisisYears;
            var payload = JsonSerializer.Serialize(new
            {
                civId           = civ.Id.Value,
                civName         = civ.Name,
                crisisEndYear   = civ.SuccessionCrisisEndYear,
                year            = world.CurrentYear
            });
            pending.Add(new PendingEvent(EventType.SuccessionCrisis, civ.CapitalTile, null, payload));
        }

        // 5. Civ-level war resolution: expiry, surrender, and collapse
        //    Iterate all civs; EndWarBetween handles symmetry so process each pair once.
        var processed = new HashSet<(CivId, CivId)>();
        foreach (var civ in world.Civilizations.Values)
        {
            foreach (var (enemyCivId, yearDeclared) in civ.WarsAgainst.ToList())
            {
                var key = (Min(civ.Id, enemyCivId), Max(civ.Id, enemyCivId));
                if (!processed.Add(key)) continue; // already handled this pair

                string? reason = null;

                // Truce by expiry
                if (world.CurrentYear - yearDeclared >= cfg.MaxWarDurationYears)
                    reason = "truce";

                // Surrender: either side's total population collapsed below the threshold
                if (reason == null)
                {
                    int popA = CivTotalPop(civ.Id, world);
                    int popB = CivTotalPop(enemyCivId, world);
                    if (popA < cfg.WarSurrenderPopThreshold || popB < cfg.WarSurrenderPopThreshold)
                        reason = "surrender";
                }

                // Destruction: either civ collapsed entirely
                if (reason == null)
                {
                    bool aGone = civ.IsCollapsed;
                    bool bGone = world.Civilizations.TryGetValue(enemyCivId, out var ec) && ec.IsCollapsed;
                    if (aGone || bGone)
                        reason = "destruction";
                }

                if (reason != null)
                    EndWarBetween(civ.Id, enemyCivId, reason, world, pending);
            }
        }
    }

    /// <summary>
    /// Ends a war between two civs, records a peace treaty on both sides, and fires the event.
    /// Safe to call regardless of which side initiated; handles asymmetric state gracefully.
    /// </summary>
    private static void EndWarBetween(
        CivId civA, CivId civB, string reason, WorldState world, List<PendingEvent> pending)
    {
        if (!world.Civilizations.TryGetValue(civA, out var ca)) return;
        if (!world.Civilizations.TryGetValue(civB, out var cb)) return;

        ca.WarsAgainst.Remove(civB);
        cb.WarsAgainst.Remove(civA);

        // Record peace so neither side can re-declare immediately
        ca.PeaceTreaties[civB] = world.CurrentYear;
        cb.PeaceTreaties[civA] = world.CurrentYear;

        // Peace resolves territorial tension — reset so the clock restarts after the cooldown
        ca.BorderTension.Remove(civB);
        cb.BorderTension.Remove(civA);

        var payload = JsonSerializer.Serialize(new
        {
            civAId   = civA.Value,
            civAName = ca.Name,
            civBId   = civB.Value,
            civBName = cb.Name,
            reason,
            year = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.WarEnded, ca.CapitalTile, null, payload, null));
    }

    private static void FireAllianceBroken(
        Tier1Character a, Tier1Character b, string reason,
        WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterAId   = a.Id.Value,
            characterAName = a.Identity.Name,
            characterBId   = b.Id.Value,
            characterBName = b.Identity.Name,
            reason,
            year = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.AllianceBroken, a.Location, null, payload,
            new[] { a.Id.Value, b.Id.Value }));
    }

    // ─── Border tension ───────────────────────────────────────────────────────

    /// <summary>
    /// Annual civ-level territorial pressure scan. For each pair of non-enemy civs whose
    /// settlements are within WarProximityRadius, tension accrues proportional to how many
    /// settlement pairs are close and how aggressive the declaring civ's ruler is.
    /// Tension decays when civs are no longer proximate. Crossing TensionWarThreshold
    /// triggers war if the ruler's Aggression meets the threshold — no physical contact needed.
    /// </summary>
    private static void RunBorderTension(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;
        int r   = cfg.WarProximityRadius;

        // Index settlements by CivId once — reused for all pair checks
        var byCiv = new Dictionary<CivId, List<TileCoord>>();
        foreach (var (coord, stub) in world.Settlements)
        {
            if (!byCiv.TryGetValue(stub.CivId, out var list))
                byCiv[stub.CivId] = list = new();
            list.Add(coord);
        }

        var activeCivs = world.Civilizations.Values
            .Where(c => !c.IsCollapsed && byCiv.ContainsKey(c.Id))
            .ToList();

        for (int i = 0; i < activeCivs.Count; i++)
        for (int j = i + 1; j < activeCivs.Count; j++)
        {
            var a = activeCivs[i];
            var b = activeCivs[j];

            if (a.IsAtWarWith(b.Id)) continue;
            if (a.InPeaceCooldownWith(b.Id, world.CurrentYear, cfg.PeaceCooldownYears)) continue;

            // Measure proximity: sum (1 - dist/r) over all close settlement pairs
            float proximity = 0f;
            foreach (var ca in byCiv[a.Id])
            foreach (var cb in byCiv[b.Id])
            {
                int dx = ca.X - cb.X, dy = ca.Y - cb.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= r) proximity += 1f - dist / r;
            }

            if (proximity <= 0f)
            {
                // No proximity this year — decay tension on both sides
                Decay(a.BorderTension, b.Id, cfg.TensionDecayRate);
                Decay(b.BorderTension, a.Id, cfg.TensionDecayRate);
                continue;
            }

            // Accumulate tension scaled by each ruler's Aggression (dead founders → neutral 0.5)
            float aggrA = (world.GetEntity(a.FounderId) as Tier1Character)?.Personality.Aggression ?? 0.5f;
            float aggrB = (world.GetEntity(b.FounderId) as Tier1Character)?.Personality.Aggression ?? 0.5f;

            a.BorderTension[b.Id] = a.BorderTension.GetValueOrDefault(b.Id, 0f) + proximity * aggrA * cfg.TensionAccrualPerPair;
            b.BorderTension[a.Id] = b.BorderTension.GetValueOrDefault(a.Id, 0f) + proximity * aggrB * cfg.TensionAccrualPerPair;
        }

        // Check threshold and declare war — one declaration per civ per annual tick
        foreach (var civ in activeCivs)
        {
            if (civ.WarsAgainst.Count >= cfg.MaxActiveWars) continue;
            float founderAggr = (world.GetEntity(civ.FounderId) as Tier1Character)?.Personality.Aggression ?? 0f;
            if (founderAggr < cfg.WarAggressionThreshold) continue;

            foreach (var (enemyCivId, tension) in civ.BorderTension.ToList())
            {
                if (tension < cfg.TensionWarThreshold) continue;
                if (civ.IsAtWarWith(enemyCivId)) continue;
                if (civ.InPeaceCooldownWith(enemyCivId, world.CurrentYear, cfg.PeaceCooldownYears)) continue;
                if (!world.Civilizations.TryGetValue(enemyCivId, out var enemy) || enemy.IsCollapsed) continue;

                StartWarBetween(civ, enemy, "border_tension", world, pending);
                // StartWarBetween is guarded internally, but clear tension regardless so the
                // pair resets whether or not the declaration succeeded
                civ.BorderTension.Remove(enemyCivId);
                enemy.BorderTension.Remove(civ.Id);
                break; // one war per civ per annual tick; re-evaluate next year if still hostile
            }
        }
    }

    private static void Decay(Dictionary<CivId, float> tension, CivId key, float rate)
    {
        if (!tension.TryGetValue(key, out float t)) return;
        t *= (1f - rate);
        if (t < 0.01f) tension.Remove(key);
        else tension[key] = t;
    }

    // ─── War helpers ──────────────────────────────────────────────────────────

    private static int CivTotalPop(CivId civId, WorldState world)
    {
        int total = 0;
        foreach (var s in world.Settlements.Values)
            if (s.CivId == civId) total += s.Population;
        return total;
    }

    // Canonical ordering for de-duplicating civ pairs in the annual war loop
    private static CivId Min(CivId a, CivId b) => a.Value < b.Value ? a : b;
    private static CivId Max(CivId a, CivId b) => a.Value > b.Value ? a : b;

    // ─── Event helpers ────────────────────────────────────────────────────────

    private static void FireCivFounded(
        Civilization civ, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            civId      = civ.Id.Value,
            civName    = civ.Name,
            founderId  = founder.Id.Value,
            founderName = founder.Identity.Name,
            capital    = new[] { civ.CapitalTile.X, civ.CapitalTile.Y },
            year       = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.CivilizationFounded, civ.CapitalTile, null, payload));
    }

    private static void FireSettlementFounded(
        SettlementStub stub, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            founderId      = founder.Id.Value,
            founderName    = founder.Identity.Name,
            settlementName = stub.Name,
            civId          = stub.CivId.Value,
            tile           = new[] { stub.Tile.X, stub.Tile.Y },
            year           = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.SettlementFounded, stub.Tile, null, payload));
    }

    // ─── Name generation ─────────────────────────────────────────────────────

    private const int SaltSettlementPrefix   = 5001;
    private const int SaltSettlementSuffix   = 5002;
    private const int SaltFertilityVariance  = 5003;

    private static string GenerateSettlementName(
        TileCoord tile, WorldState world, SettlementNamesConfig cfg)
    {
        if (cfg.Prefixes.Length == 0 || cfg.Suffixes.Length == 0)
            return $"Settlement ({tile.X},{tile.Y})";

        // Deterministic from world seed + tile position — same tile always gets same name
        float pf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementPrefix);
        float sf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementSuffix);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        // Bias prefix selection toward biome character
        int pi = BiasedIndex(pf, biome, cfg.Prefixes.Length);
        int si = (int)(sf * cfg.Suffixes.Length);
        return cfg.Prefixes[pi] + cfg.Suffixes[si];
    }

    // Deterministic founding-time fertility variance: maps [0,1] → [1-variance, 1+variance]
    // so each settlement has a permanent slight edge or disadvantage baked in at birth.
    private static float GenerateFertilityMultiplier(TileCoord tile, WorldState world)
    {
        float r = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltFertilityVariance);
        // r ∈ [0,1] → multiplier ∈ [0.85, 1.15] (variance of ±0.15 baked into SettlementConfig)
        // DECISION: variance range is hardcoded here; SettlementConfig.FertilityVariance is the
        // intended range, but injecting SimConfig into CivTracker adds coupling we avoid for now.
        const float variance = 0.15f;
        return 1f - variance + r * (variance * 2f);
    }

    // Slightly bias prefix selection so rocky biomes lean toward hard-sounding names,
    // warm biomes toward bright/green — purely cosmetic, not guaranteed.
    private static int BiasedIndex(float raw, BiomeType biome, int count)
    {
        // Map raw [0,1] through a small biome-dependent shift, then wrap
        float shift = biome switch
        {
            BiomeType.Mountain or BiomeType.Hills or BiomeType.Volcanic
                => 0.3f,   // push toward Iron/Stone/Crag/Flint end
            BiomeType.Grassland or BiomeType.Savanna or BiomeType.TemperateForest
                => -0.15f, // push toward Green/Gold/Fair end
            BiomeType.Tundra or BiomeType.BorealForest
                => 0.15f,  // push toward Cold/Frost/Dark end
            _ => 0f
        };
        return (int)(((raw + shift + 1f) % 1f) * count);
    }
}
