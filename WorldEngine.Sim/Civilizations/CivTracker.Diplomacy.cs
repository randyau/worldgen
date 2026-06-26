using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

public static partial class CivTracker
{
    private const int SaltCivFloor = 760;

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

        // 4. Succession crisis: if no living ruler exists AND no succession occurred this year,
        //    flag distant settlements. (Normal succession is handled immediately in KillCharacter.)
        foreach (var civ in world.Civilizations.Values)
        {
            if (civ.IsCollapsed || civ.SuccessionCrisisEndYear != int.MinValue) continue;
            bool rulerAlive = world.GetEntity(civ.RulerId) is Tier1Character rc && rc.IsAlive;
            if (rulerAlive) continue;
            bool anyLivingMember = civ.Members.Any(m => world.GetEntity(m) is Tier1Character mc && mc.IsAlive);
            if (anyLivingMember) continue;

            civ.SuccessionCrisisEndYear = world.CurrentYear + cfg.SuccessionCrisisYears;
            pending.Add(new PendingEvent(EventType.SuccessionCrisis, civ.CapitalTile, null,
                JsonSerializer.Serialize(new SuccessionCrisisPayload(
                    civ.Id.Value, civ.Name, civ.SuccessionCrisisEndYear)),
                CivId: civ.Id.Value));
        }

        // 5. Civilisation floor: spawn new founders if active civ count falls below threshold
        RunCivFloorSpawns(world, pending, world.SimConfig);

        // 5b. City expansion decisions: rulers delegate FoundCity goals to ambitious members.
        RunCityExpansionDecisions(world, pending);

        // 5c. Cultural trait evaluation: assign permanent traits when thresholds are crossed.
        EvaluateCulturalTraits(world, pending);

        // 6. Civ-level war resolution: expiry, surrender, and collapse
        var processed = new HashSet<(CivId, CivId)>();
        foreach (var civ in world.Civilizations.Values)
        {
            foreach (var (enemyCivId, yearDeclared) in civ.WarsAgainst.ToList())
            {
                var key = (Min(civ.Id, enemyCivId), Max(civ.Id, enemyCivId));
                if (!processed.Add(key)) continue;

                string? reason = null;

                // Truce by expiry — but if the defender's capital is critically damaged, the
                // attacker can force a conquest rather than accepting a mere truce.
                if (world.CurrentYear - yearDeclared >= cfg.MaxWarDurationYears)
                {
                    bool conquestForced = false;
                    if (world.Civilizations.TryGetValue(enemyCivId, out var enemyCiv)
                        && world.Settlements.TryGetValue(enemyCiv.CapitalTile, out var capitalStub)
                        && capitalStub.Health <= cfg.WarConquestHealthThreshold
                        && civ.RulerId.Value != 0)
                    {
                        var attacker = world.GetEntity(civ.RulerId) as Tier1Character;
                        if (attacker != null)
                        {
                            var siegeCmd = new RaidSettlement(attacker.Id, enemyCiv.CapitalTile);
                            world.Settlements[enemyCiv.CapitalTile] = capitalStub with { Health = 0 };
                            ResolveRaid(siegeCmd, world, pending);
                            conquestForced = true;
                        }
                    }
                    reason = conquestForced ? null : "truce";
                }

                if (reason == null)
                {
                    int popA = CivTotalPop(civ.Id, world);
                    int popB = CivTotalPop(enemyCivId, world);
                    if (popA < cfg.WarSurrenderPopThreshold || popB < cfg.WarSurrenderPopThreshold)
                        reason = "surrender";
                }

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

    // ─── City expansion decisions ─────────────────────────────────────────────

    /// <summary>
    /// Annual: rulers with room to grow delegate FoundCity goals to ambitious non-founder civ members.
    /// Only one delegation per civ per year; won't delegate if any member already has a FoundCity goal.
    /// </summary>
    private static void RunCityExpansionDecisions(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;

        foreach (var civ in world.Civilizations.Values)
        {
            if (civ.IsCollapsed) continue;
            int totalCities = civ.SettlementCount + civ.ColonyCount;
            if (totalCities >= cfg.MaxCitiesPerCiv) continue;

            // Skip if any member already has a FoundCity goal (avoid duplicate delegates)
            bool alreadyDelegated = false;
            foreach (var memberId in civ.Members)
            {
                if (world.GetEntity(memberId) is Tier1Character mc && mc.IsAlive
                    && mc.Goals.Any(g => g.Type == GoalType.FoundCity))
                {
                    alreadyDelegated = true;
                    break;
                }
            }
            if (alreadyDelegated) continue;

            // Pick the most ambitious non-ruler, non-founder living member
            Tier1Character? best = null;
            float bestAmbition = cfg.CityFoundingAmbitionThreshold;
            bool isFoundingCooldown = world.CurrentYear - civ.LastSettlementFoundedYear
                                     < cfg.MinFoundingCooldownYears;
            if (isFoundingCooldown) continue;

            foreach (var memberId in civ.Members)
            {
                if (world.GetEntity(memberId) is not Tier1Character m || !m.IsAlive) continue;
                if (m.Id == civ.RulerId) continue;     // ruler doesn't self-delegate
                if (world.ActiveFounders.Contains(m.Id)) continue; // already a founder
                if (m.Personality.Ambition > bestAmbition)
                {
                    bestAmbition = m.Personality.Ambition;
                    best = m;
                }
            }

            if (best == null) continue;

            best.Goals.Add(new GoalData
            {
                Type       = GoalType.FoundCity,
                Priority   = bestAmbition * 0.9f,
                Intensity  = bestAmbition,
                StaleSince = (int)world.CurrentTick,
                FormedTick = (int)world.CurrentTick,
            });

            // Log as a GoalFormed event so it appears in the history graph
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                CharacterId = best.Id.Value,
                CharacterName = best.Identity.Name,
                GoalType = GoalType.FoundCity.ToString(),
                Priority = bestAmbition,
                Outcome = "delegated_by_ruler"
            });
            pending.Add(new PendingEvent(EventType.GoalFormed, best.Location, null, payload,
                new[] { best.Id.Value },
                ActorId: best.Id.Value, ActorName: best.Identity.Name,
                CivId: civ.Id.Value));
        }
    }

    // ─── Civilisation floor ───────────────────────────────────────────────────

    /// <summary>
    /// If active civs drop below the configured floor, probabilistically spawn new free-agent
    /// founders on unclaimed fertile land with an Expansion goal.
    /// </summary>
    private static void RunCivFloorSpawns(WorldState world, List<PendingEvent> pending, SimConfig cfg)
    {
        int activeCivs = 0;
        foreach (var c in world.Civilizations.Values)
            if (!c.IsCollapsed) activeCivs++;

        int deficit = cfg.Character.CivFloorCount - activeCivs;
        if (deficit <= 0) return;

        var charCfg = cfg.Character;
        for (int slot = 0; slot < deficit; slot++)
        {
            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, slot, 0, SaltCivFloor);
            if (roll >= charCfg.CivFloorSpawnChance) continue;

            var tile = FindCivFloorSpawnTile(world, cfg);
            if (tile is null) continue;

            long seq     = (200_000L + world.CurrentYear * 997L + slot * 31L) & 0x7FFFFFFF;
            var  biome   = (BiomeType)world.TileGrid.GetTile(tile.Value).BiomeType;
            var  founder = CharacterFactory.Spawn(tile.Value, biome, world.WorldSeed, seq, cfg, world.CurrentYear);
            int  founderOrdinal = world.ClaimNameOrdinal(founder.Identity.Name);
            if (founderOrdinal > 0)
                founder.Identity = founder.Identity with { NameOrdinal = founderOrdinal };

            founder.Goals.Add(new GoalData
            {
                Type       = GoalType.FoundCity,
                Priority   = 1.0f,
                StaleSince = (int)world.CurrentTick,
                FormedTick = (int)world.CurrentTick
            });

            world.Entities.Add(founder);
            pending.Add(new PendingEvent(EventType.CharacterBorn, tile.Value, null,
                JsonSerializer.Serialize(new CharacterBornPayload(
                    founder.Id.Value, founder.Identity.Name, founder.Identity.Epithet,
                    founder.Personality.Ambition, founder.Personality.Aggression, Source: "civ_floor")),
                new[] { founder.Id.Value },
                ActorId: founder.Id.Value, ActorName: founder.Identity.Name));
        }
    }

    private static TileCoord? FindCivFloorSpawnTile(WorldState world, SimConfig cfg)
    {
        int minFertility = cfg.Character.MinFertilityToSettle;
        int minDist      = cfg.Character.CivFloorMinDist;
        int minDistSq    = minDist * minDist;
        int w = world.TileGrid.TileWidth;
        int h = world.TileGrid.TileHeight;

        var candidates = new List<TileCoord>();
        for (int y = 1; y < h - 1; y += 4)
        for (int x = 0; x < w; x += 4)
        {
            var coord = new TileCoord(x, y);
            if (!world.IsLand(coord)) continue;
            var tile = world.TileGrid.GetTile(coord);
            if ((BiomeType)tile.BiomeType == BiomeType.HighMountain) continue;
            if (tile.Fertility < minFertility) continue;

            bool tooClose = false;
            foreach (var s in world.Settlements.Keys)
            {
                int dx = coord.X - s.X, dy = coord.Y - s.Y;
                if (dx * dx + dy * dy < minDistSq) { tooClose = true; break; }
            }
            if (tooClose) continue;

            candidates.Add(coord);
        }

        if (candidates.Count == 0) return null;

        int idx = (int)(WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, 0, 1, SaltCivFloor) * candidates.Count);
        return candidates[Math.Clamp(idx, 0, candidates.Count - 1)];
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

        ca.PeaceTreaties[civB] = world.CurrentYear;
        cb.PeaceTreaties[civA] = world.CurrentYear;

        // Peace resolves territorial tension — reset so the clock restarts after the cooldown
        ca.BorderTension.Remove(civB);
        cb.BorderTension.Remove(civA);

        int warCount = ca.WarHistory.GetValueOrDefault(civB, 0);
        var payload = JsonSerializer.Serialize(new WarEndedPayload(
            civA.Value, ca.Name, civB.Value, cb.Name, reason, warCount));
        long[]? warEndSecondary = cb.RulerId.Value != ca.RulerId.Value ? new[] { cb.RulerId.Value } : null;
        pending.Add(new PendingEvent(EventType.WarEnded, ca.CapitalTile, null, payload,
            new[] { ca.RulerId.Value }, warEndSecondary,
            CivId: civA.Value));
    }

    private static void FireAllianceBroken(
        Tier1Character a, Tier1Character b, string reason,
        WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new AllianceBrokenPayload(
            a.Id.Value, a.Identity.Name, b.Id.Value, b.Identity.Name, reason));
        pending.Add(new PendingEvent(EventType.AllianceBroken, a.Location, null, payload,
            new[] { a.Id.Value }, new[] { b.Id.Value },
            ActorId: a.Id.Value, ActorName: a.Identity.Name));
    }

    // ─── Border tension ───────────────────────────────────────────────────────

    /// <summary>
    /// Annual civ-level territorial pressure scan. Tension accrues when settlements of non-enemy
    /// civs are within WarProximityRadius. Crossing TensionWarThreshold triggers war if the
    /// ruler's Aggression meets the threshold.
    /// </summary>
    private static void RunBorderTension(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;
        int r   = cfg.WarProximityRadius;

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
            if (a.InPeaceCooldownWith(b.Id, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) continue;

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
                Decay(a.BorderTension, b.Id, cfg.TensionDecayRate);
                Decay(b.BorderTension, a.Id, cfg.TensionDecayRate);
                continue;
            }

            float aggrA = (world.GetEntity(a.RulerId) as Tier1Character)?.Personality.Aggression ?? 0.5f;
            float aggrB = (world.GetEntity(b.RulerId) as Tier1Character)?.Personality.Aggression ?? 0.5f;

            a.BorderTension[b.Id] = a.BorderTension.GetValueOrDefault(b.Id, 0f) + proximity * aggrA * cfg.TensionAccrualPerPair;
            b.BorderTension[a.Id] = b.BorderTension.GetValueOrDefault(a.Id, 0f) + proximity * aggrB * cfg.TensionAccrualPerPair;
        }

        foreach (var civ in activeCivs)
        {
            if (civ.WarsAgainst.Count >= cfg.MaxActiveWars) continue;
            float rulerAggr = (world.GetEntity(civ.RulerId) as Tier1Character)?.Personality.Aggression ?? 0f;
            if (rulerAggr < cfg.WarAggressionThreshold) continue;

            foreach (var (enemyCivId, tension) in civ.BorderTension.ToList())
            {
                if (tension < cfg.TensionWarThreshold) continue;
                if (civ.IsAtWarWith(enemyCivId)) continue;
                if (civ.InPeaceCooldownWith(enemyCivId, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) continue;
                if (!world.Civilizations.TryGetValue(enemyCivId, out var enemy) || enemy.IsCollapsed) continue;

                StartWarBetween(civ, enemy, "border_tension", world, pending);
                civ.BorderTension.Remove(enemyCivId);
                enemy.BorderTension.Remove(civ.Id);
                break;
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

    private static CivId Min(CivId a, CivId b) => a.Value < b.Value ? a : b;
    private static CivId Max(CivId a, CivId b) => a.Value > b.Value ? a : b;

    // ─── Cultural trait evaluation ────────────────────────────────────────────

    /// <summary>
    /// Annual evaluation pass: assigns permanent cultural traits to civs that have crossed
    /// historical thresholds. Traits are never removed once assigned.
    /// Fires CivTraitAcquired events for new assignments.
    /// </summary>
    private static void EvaluateCulturalTraits(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.CulturalTraits;

        foreach (var (_, civ) in world.Civilizations)
        {
            if (civ.IsCollapsed) continue;
            int yearsElapsed = world.CurrentYear - civ.FoundedYear;
            if (yearsElapsed < 10) continue;  // not enough history to classify

            // Track near-collapse each year (TotalPopulation refreshed by PopulationDynamicsPhase)
            if (civ.TotalPopulation > 0 && civ.TotalPopulation < cfg.ResilientNearCollapsePopThreshold)
                civ.NearCollapseCount++;

            TryAssignTrait(civ, CulturalTrait.Militaristic, world, pending,
                MilitaristicQualifies(civ, yearsElapsed, cfg));

            TryAssignTrait(civ, CulturalTrait.Expansionist, world, pending,
                ExpansionistQualifies(civ, yearsElapsed, cfg));

            TryAssignTrait(civ, CulturalTrait.WarWeary, world, pending,
                WarWearyQualifies(civ, cfg));

            TryAssignTrait(civ, CulturalTrait.Resilient, world, pending,
                ResilientQualifies(civ, cfg));

            TryAssignTrait(civ, CulturalTrait.Scholarly, world, pending,
                civ.TotalScholarDiscoveries >= cfg.ScholarlyMinDiscoveries);

            TryAssignTrait(civ, CulturalTrait.UnstableThrone, world, pending,
                UnstableThroneQualifies(civ, yearsElapsed, cfg));
        }
    }

    private static void TryAssignTrait(
        Civilization civ, CulturalTrait trait,
        WorldState world, List<PendingEvent> pending,
        bool qualifies)
    {
        if (!qualifies) return;
        string traitName = trait.ToString();
        if (!civ.CulturalTraits.Add(traitName)) return;  // already assigned — no duplicate event

        string reason = trait switch
        {
            CulturalTrait.Militaristic   => $"initiated {civ.TotalWarsInitiated} total wars",
            CulturalTrait.Expansionist   => $"founded {civ.TotalSettlementsFounded} settlements",
            CulturalTrait.WarWeary       => "repeatedly exhausted by wars against the same rival",
            CulturalTrait.Resilient      => $"survived {civ.NearCollapseCount} near-collapse episode(s)",
            CulturalTrait.Scholarly      => $"made {civ.TotalScholarDiscoveries} scholarly discoveries",
            CulturalTrait.UnstableThrone => $"had {civ.TotalSuccessions} successions in recent history",
            _                            => "threshold crossed"
        };

        var payload = JsonSerializer.Serialize(new CivTraitAcquiredPayload(
            (int)civ.Id.Value, civ.Name, traitName, reason));
        pending.Add(new PendingEvent(EventType.CivTraitAcquired, civ.CapitalTile, null, payload,
            CivId: civ.Id.Value));
    }

    private static bool MilitaristicQualifies(Civilization civ, int yearsElapsed, CulturalTraitsConfig cfg)
    {
        if (civ.TotalWarsInitiated < cfg.MilitaristicMinWars) return false;
        float decades = yearsElapsed / 10f;
        return decades > 0 && (civ.TotalWarsInitiated / decades) >= cfg.MilitaristicWarsPerDecade;
    }

    private static bool ExpansionistQualifies(Civilization civ, int yearsElapsed, CulturalTraitsConfig cfg)
    {
        if (yearsElapsed < cfg.ExpansionistSustainedYears) return false;
        float rate = civ.TotalSettlementsFounded / (yearsElapsed / 10f);
        return rate >= cfg.ExpansionistFoundingRate;
    }

    private static bool WarWearyQualifies(Civilization civ, CulturalTraitsConfig cfg)
    {
        foreach (var count in civ.WarHistory.Values)
            if (count >= cfg.WarWearyMinRepeatWars) return true;
        return false;
    }

    private static bool ResilientQualifies(Civilization civ, CulturalTraitsConfig cfg)
        => civ.NearCollapseCount >= cfg.ResilientMinNearCollapseCount;

    private static bool UnstableThroneQualifies(Civilization civ, int yearsElapsed, CulturalTraitsConfig cfg)
    {
        // DECISION: uses TotalSuccessions as proxy; a proper rolling window would require
        // per-succession year tracking which adds significant state. This approximation
        // checks if rate-over-lifetime exceeds the per-window threshold.
        if (yearsElapsed < cfg.UnstableThroneYears) return false;
        float windows = yearsElapsed / (float)cfg.UnstableThroneYears;
        return civ.TotalSuccessions / windows >= cfg.UnstableThroneMinSuccessions;
    }
}
