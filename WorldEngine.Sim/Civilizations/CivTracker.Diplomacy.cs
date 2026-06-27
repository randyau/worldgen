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
        // 3b. Territory-adjacency tension: adjacent territory tiles build conflict pressure
        RunTerritoryBorderTension(world, pending);
        // 3c. Annual war campaigns: one abstract battle attempt per active war per year
        RunWarCampaigns(world, pending);

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

        // 5d. Emissary dispatch: rulers consider sending emissaries every DispatchCheckYears.
        if (world.CurrentYear % world.SimConfig.Emissary.DispatchCheckYears == 0)
            RunEmissaryDispatch(world, pending);

        // 5e. Emissary resolution: arrivals for this year are processed.
        RunEmissaryResolution(world, pending);

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
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
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
    /// Transfers border territory from the loser to the winner proportional to battle advantage.
    /// </summary>
    internal static void EndWarBetween(
        CivId civA, CivId civB, string reason, WorldState world, List<PendingEvent> pending)
    {
        if (!world.Civilizations.TryGetValue(civA, out var ca)) return;
        if (!world.Civilizations.TryGetValue(civB, out var cb)) return;

        // Territory transfer based on battle wins accrued during the war
        int aWins = ca.WarBattleWins.GetValueOrDefault(civB, 0);
        int bWins = cb.WarBattleWins.GetValueOrDefault(civA, 0);
        int advantage = aWins - bWins;

        if (Math.Abs(advantage) >= 1)
        {
            var wCfg = world.SimConfig.War;
            (CivId winner, CivId loser) = advantage > 0 ? (civA, civB) : (civB, civA);
            int tilesToTransfer = Math.Min(Math.Abs(advantage) * wCfg.TilesPerBattleWin,
                                           wCfg.MaxTilesTransferredPerWar);
            TransferBorderTiles(winner, loser, tilesToTransfer, world, pending);
        }

        // Reset per-war battle win counters
        ca.WarBattleWins.Remove(civB);
        cb.WarBattleWins.Remove(civA);

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

    // ─── Territory-adjacency border tension (M4.2) ────────────────────────────

    /// <summary>
    /// Scans every tile in the territory map and accrues border tension between civs whose
    /// territory tiles share an edge. Works independently of city-proximity tension.
    /// Only counts each pair once by requiring the lower-Id civ's tile to initiate.
    /// </summary>
    private static void RunTerritoryBorderTension(WorldState world, List<PendingEvent> pending)
    {
        float tensionRate = world.SimConfig.War.TerritoryTensionPerAdjacentPair;
        int w = world.TileGrid.TileWidth;
        int h = world.TileGrid.TileHeight;
        ReadOnlySpan<(int dx, int dy)> neighbors = [(1, 0), (0, 1), (-1, 0), (0, -1)];

        foreach (var (tile, cityTile) in world.TerritoryMap)
        {
            if (!world.Settlements.TryGetValue(cityTile, out var ownerStub)) continue;
            var ownerCivId = ownerStub.CivId;

            foreach (var (dx, dy) in neighbors)
            {
                int nx = ((tile.X + dx) % w + w) % w;
                int ny = tile.Y + dy;
                if (ny < 0 || ny >= h) continue;
                var nCoord = new TileCoord(nx, ny);

                if (!world.TerritoryMap.TryGetValue(nCoord, out var nCityTile)) continue;
                if (!world.Settlements.TryGetValue(nCityTile, out var nStub)) continue;
                var nCivId = nStub.CivId;

                if (nCivId == ownerCivId) continue;
                // Count each pair once: only process when this tile's civ has the lower Id
                if (ownerCivId.Value > nCivId.Value) continue;

                if (!world.Civilizations.TryGetValue(ownerCivId, out var civA)) continue;
                if (!world.Civilizations.TryGetValue(nCivId, out var civB)) continue;

                // Don't accrue tension if already at war — war is the outcome, not the cause
                if (civA.IsAtWarWith(nCivId)) continue;

                civA.BorderTension[nCivId] = civA.BorderTension.GetValueOrDefault(nCivId, 0f) + tensionRate;
                civB.BorderTension[ownerCivId] = civB.BorderTension.GetValueOrDefault(ownerCivId, 0f) + tensionRate;
            }
        }
        // War declaration from the accumulated tension is handled in the next call to RunBorderTension
    }

    /// <summary>
    /// Transfers up to <paramref name="count"/> border tiles from the loser's territory to the winner's.
    /// Picks tiles on the loser's border adjacent to the winner's territory, sorted by proximity
    /// to the loser's capital (closest tiles are most painful to lose — most strategically significant).
    /// Emits TerritoryLost for the loser and TerritoryExpanded for the winner.
    /// </summary>
    private static void TransferBorderTiles(
        CivId winnerCivId, CivId loserCivId, int count,
        WorldState world, List<PendingEvent> pending)
    {
        if (!world.Civilizations.TryGetValue(winnerCivId, out var winner)) return;
        if (!world.Civilizations.TryGetValue(loserCivId, out var loser)) return;
        if (count <= 0) return;

        int w = world.TileGrid.TileWidth;
        int h = world.TileGrid.TileHeight;
        ReadOnlySpan<(int dx, int dy)> neighbors = [(1, 0), (0, 1), (-1, 0), (0, -1)];

        // Find loser's border tiles adjacent to winner's territory
        var candidates = new List<(TileCoord tile, TileCoord loserCity, float distToLoserCapital)>();
        foreach (var (tile, cityTile) in world.TerritoryMap)
        {
            if (!world.Settlements.TryGetValue(cityTile, out var stub)) continue;
            if (stub.CivId != loserCivId) continue;

            bool adjacentToWinner = false;
            foreach (var (dx, dy) in neighbors)
            {
                int nx = ((tile.X + dx) % w + w) % w;
                int ny = tile.Y + dy;
                if (ny < 0 || ny >= h) continue;
                var nc = new TileCoord(nx, ny);
                if (!world.TerritoryMap.TryGetValue(nc, out var nCity)) continue;
                if (!world.Settlements.TryGetValue(nCity, out var nStub)) continue;
                if (nStub.CivId == winnerCivId) { adjacentToWinner = true; break; }
            }
            if (!adjacentToWinner) continue;

            // Sort by distance to loser capital — closest = highest strategic value to lose
            int cdx = tile.X - loser.CapitalTile.X, cdy = tile.Y - loser.CapitalTile.Y;
            float dist = MathF.Sqrt(cdx * cdx + cdy * cdy);
            candidates.Add((tile, cityTile, dist));
        }

        if (candidates.Count == 0) return;

        // Sort ascending by distance to loser capital (closest first)
        candidates.Sort((a, b) => a.distToLoserCapital.CompareTo(b.distToLoserCapital));

        int toTransfer = Math.Min(count, candidates.Count);

        // Find nearest winner city (for assignment of transferred tiles)
        TileCoord winnerCity = winner.CapitalTile;
        float nearestWinnerDist = float.MaxValue;
        foreach (var cityTile in winner.CityTerritories.Keys)
        {
            int dx = cityTile.X - loser.CapitalTile.X, dy = cityTile.Y - loser.CapitalTile.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < nearestWinnerDist) { nearestWinnerDist = d; winnerCity = cityTile; }
        }

        // Transfer each candidate tile
        var loserCityCountLost = new Dictionary<TileCoord, int>();
        for (int i = 0; i < toTransfer; i++)
        {
            var (tile, loserCity, _) = candidates[i];

            // Remove from loser
            if (loser.CityTerritories.TryGetValue(loserCity, out var loserTiles))
                loserTiles.Remove(tile);
            world.TerritoryMap.Remove(tile);

            // Add to winner
            if (!winner.CityTerritories.TryGetValue(winnerCity, out var winnerTiles))
                winner.CityTerritories[winnerCity] = winnerTiles = new HashSet<TileCoord>();
            winnerTiles.Add(tile);
            world.TerritoryMap[tile] = winnerCity;

            loserCityCountLost[loserCity] = loserCityCountLost.GetValueOrDefault(loserCity, 0) + 1;
        }

        // Emit TerritoryLost per affected loser city
        foreach (var (loserCity, lostCount) in loserCityCountLost)
        {
            int loserRemaining = loser.CityTerritories.TryGetValue(loserCity, out var rt) ? rt.Count : 0;
            var lostPayload = JsonSerializer.Serialize(new TerritoryLostPayload(
                loserCivId.Value, loser.Name, loserCity.X, loserCity.Y,
                lostCount, loserRemaining, "war_outcome"));
            pending.Add(new PendingEvent(EventType.TerritoryLost, loserCity, null, lostPayload,
                CivId: loserCivId.Value));
        }

        // Emit TerritoryExpanded for winner
        int winnerTotal = winner.CityTerritories.TryGetValue(winnerCity, out var wt) ? wt.Count : 0;
        var gainPayload = JsonSerializer.Serialize(new TerritoryExpandedPayload(
            winnerCivId.Value, winner.Name, winnerCity.X, winnerCity.Y, toTransfer, winnerTotal));
        pending.Add(new PendingEvent(EventType.TerritoryExpanded, winnerCity, null, gainPayload,
            CivId: winnerCivId.Value));
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

    // ─── Civ knowledge system (M4.1) ─────────────────────────────────────────

    private const int SaltEmissaryDispatch   = 4102;
    private const int SaltEmissaryResolution = 4103;

    /// <summary>
    /// Upserts a CivContact into the target civ's KnownCivs. If the contact already exists,
    /// upgrades BestSource if the new source is higher-fidelity, adds confidence (clamped to 1),
    /// and updates YearLastContact. If new, creates the contact.
    /// </summary>
    public static void SeedCivContact(
        CivId knowerCivId, CivId knownCivId,
        CivContactSource source, TileCoord capitalTile,
        float confidenceGain, WorldState world)
    {
        if (!world.Civilizations.TryGetValue(knowerCivId, out var knower)) return;

        if (knower.KnownCivs.TryGetValue(knownCivId, out var existing))
        {
            knower.KnownCivs[knownCivId] = existing with
            {
                YearLastContact = world.CurrentYear,
                BestSource      = (CivContactSource)Math.Max((int)existing.BestSource, (int)source),
                CapitalTile     = source >= existing.BestSource ? capitalTile : existing.CapitalTile,
                Confidence      = Math.Min(1f, existing.Confidence + confidenceGain)
            };
        }
        else
        {
            knower.KnownCivs[knownCivId] = new CivContact(
                KnownCivId:      knownCivId,
                YearFirstContact: world.CurrentYear,
                YearLastContact: world.CurrentYear,
                BestSource:      source,
                CapitalTile:     capitalTile,
                Confidence:      Math.Min(1f, confidenceGain));
        }
    }

    // ─── Emissary dispatch ────────────────────────────────────────────────────

    /// <summary>
    /// Annual emissary dispatch decision. Called from RunAnnualDiplomacy when
    /// CurrentYear % DispatchCheckYears == 0.
    ///
    /// For each non-collapsed civ that is under the active emissary cap, evaluates
    /// known civs with sufficient confidence and dispatches emissaries based on
    /// ruler personality and trust levels.
    /// </summary>
    internal static void RunEmissaryDispatch(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Emissary;

        foreach (var civ in world.Civilizations.Values)
        {
            if (civ.IsCollapsed) continue;

            int totalActive = 0;
            foreach (var count in civ.ActiveEmissaryCountByTarget.Values) totalActive += count;
            if (totalActive >= cfg.MaxActiveEmissariesPerCiv) continue;

            var ruler = world.GetEntity(civ.RulerId) as Tier1Character;

            foreach (var (targetCivId, contact) in civ.KnownCivs)
            {
                if (contact.Confidence <= 0.1f) continue;
                if (!world.Civilizations.TryGetValue(targetCivId, out var targetCiv)) continue;
                if (targetCiv.IsCollapsed) continue;
                if (civ.IsAtWarWith(targetCivId)) continue;

                // Cap re-check (may have dispatched earlier in this loop)
                totalActive = 0;
                foreach (var count in civ.ActiveEmissaryCountByTarget.Values) totalActive += count;
                if (totalActive >= cfg.MaxActiveEmissariesPerCiv) break;

                // Already have an active emissary to this target
                if (civ.ActiveEmissaryCountByTarget.GetValueOrDefault(targetCivId, 0) > 0) continue;

                // Determine purpose
                float trust = 0f;
                if (ruler != null && world.GetEntity(targetCiv.RulerId) is Tier1Character targetRuler)
                    trust = world.Relationships.Get(civ.RulerId, targetRuler.Id)?.Trust ?? 0f;

                EmissaryPurpose? purpose = SelectEmissaryPurpose(ruler, trust, civ, targetCivId, cfg, world);
                if (purpose is null) continue;

                // Distance and survival chance
                int dx = civ.CapitalTile.X - contact.CapitalTile.X;
                int dy = civ.CapitalTile.Y - contact.CapitalTile.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float survivalChance = Math.Clamp(
                    1f - dist * cfg.EmissaryDeathPerTile,
                    cfg.EmissaryMinSurvivalChance, 1f);
                int arrivalYear = world.CurrentYear + (int)MathF.Ceiling(dist / cfg.EmissaryTravelSpeedTilesPerYear);

                var emissary = new PendingEmissary(
                    FromCiv: civ.Id, ToCiv: targetCivId,
                    Purpose: purpose.Value,
                    DepartedYear: world.CurrentYear,
                    ArrivalYear: arrivalYear,
                    SurvivalChance: survivalChance);

                world.PendingEmissaries.Add(emissary);
                civ.ActiveEmissaryCountByTarget[targetCivId] =
                    civ.ActiveEmissaryCountByTarget.GetValueOrDefault(targetCivId, 0) + 1;

                // Fire EmissaryDispatched event for history log
                var payload = JsonSerializer.Serialize(new EmissaryDispatchedPayload(
                    civ.Id.Value, civ.Name, targetCivId.Value, targetCiv.Name,
                    purpose.Value.ToString(), arrivalYear, survivalChance));
                pending.Add(new PendingEvent(EventType.EmissaryDispatched, civ.CapitalTile,
                    null, payload, CivId: civ.Id.Value));
            }
        }
    }

    private static EmissaryPurpose? SelectEmissaryPurpose(
        Tier1Character? ruler, float trust, Civilization civ,
        CivId targetCivId, EmissaryConfig cfg, WorldState world)
    {
        // DECISION: Cunning is proxied from Rationality + (1 - Honesty) since Tier1 has no
        // explicit Cunning trait. Piety comes from Skills.Piety (a skill, not personality).
        float cunning = ruler != null
            ? (ruler.Personality.Rationality + (1f - ruler.Personality.Honesty)) * 0.5f
            : 0.3f;
        float piety = ruler?.Skills.Piety ?? 0f;

        if (trust < cfg.SpyDispatchMaxTrust && cunning > 0.5f)
            return EmissaryPurpose.Spy;
        if (trust >= cfg.TradeDispatchMinTrust)
            return EmissaryPurpose.Trade;
        if (trust >= cfg.DiplomacyDispatchMinTrust && !civ.IsAtWarWith(targetCivId))
            return EmissaryPurpose.Diplomacy;
        if (piety > 0.6f)
            return EmissaryPurpose.Religious;
        return null;
    }

    // ─── Emissary resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Resolves all emissaries whose ArrivalYear == CurrentYear.
    /// Applies mortality roll, then purpose-specific outcomes.
    /// </summary>
    internal static void RunEmissaryResolution(WorldState world, List<PendingEvent> pending)
    {
        var cfg      = world.SimConfig.Emissary;
        var arrivals = world.PendingEmissaries
            .Where(e => e.ArrivalYear == world.CurrentYear)
            .ToList();

        foreach (var emissary in arrivals)
        {
            world.PendingEmissaries.Remove(emissary);

            if (!world.Civilizations.TryGetValue(emissary.FromCiv, out var fromCiv)) continue;
            if (!world.Civilizations.TryGetValue(emissary.ToCiv,   out var toCiv))   continue;

            // Decrement active count
            if (fromCiv.ActiveEmissaryCountByTarget.TryGetValue(emissary.ToCiv, out int cur) && cur > 0)
                fromCiv.ActiveEmissaryCountByTarget[emissary.ToCiv] = cur - 1;

            // Mark emissary dispatch-refreshed so proximity decay doesn't erase the contact this tick
            // (the emissary itself is knowledge; the seed below handles the confidence bump)

            // Mortality roll
            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                emissary.FromCiv.Value, emissary.ToCiv.Value, SaltEmissaryResolution);

            if (roll > emissary.SurvivalChance)
            {
                var lostPayload = JsonSerializer.Serialize(new EmissaryLostPayload(
                    emissary.FromCiv.Value, fromCiv.Name,
                    emissary.ToCiv.Value,   toCiv.Name,
                    emissary.Purpose.ToString()));
                pending.Add(new PendingEvent(EventType.EmissaryLost, fromCiv.CapitalTile,
                    null, lostPayload, CivId: emissary.FromCiv.Value));
                continue;
            }

            // Successful arrival — upgrade contact to EmissaryExchange
            SeedCivContact(emissary.FromCiv, emissary.ToCiv,
                CivContactSource.EmissaryExchange, toCiv.CapitalTile, 1f, world);

            ResolveEmissaryArrival(emissary, fromCiv, toCiv, world, pending, cfg);
        }
    }

    private static void ResolveEmissaryArrival(
        PendingEmissary emissary,
        Civilization fromCiv, Civilization toCiv,
        WorldState world, List<PendingEvent> pending,
        EmissaryConfig cfg)
    {
        switch (emissary.Purpose)
        {
            case EmissaryPurpose.Trade:
                ResolveTrade(emissary, fromCiv, toCiv, world, pending, cfg);
                break;
            case EmissaryPurpose.Diplomacy:
                ResolveDiplomacy(emissary, fromCiv, toCiv, world, pending, cfg);
                break;
            case EmissaryPurpose.Spy:
                ResolveSpy(emissary, fromCiv, toCiv, world, pending, cfg);
                break;
            case EmissaryPurpose.Religious:
                ResolveReligious(emissary, fromCiv, toCiv, world, pending, cfg);
                break;
        }
    }

    private static void ResolveTrade(
        PendingEmissary emissary, Civilization fromCiv, Civilization toCiv,
        WorldState world, List<PendingEvent> pending, EmissaryConfig cfg)
    {
        // Both civs need minimum population to meaningfully trade
        if (fromCiv.TotalPopulation < cfg.TradeMinPopForGoods ||
            toCiv.TotalPopulation   < cfg.TradeMinPopForGoods)
            return;

        // Fire MerchantTradeCompleted (reuses existing event type)
        var tradePayload = JsonSerializer.Serialize(new MerchantTradePayload(
            fromCiv.RulerId.Value, fromCiv.Name, "diplomacy_goods",
            toCiv.CapitalTile.X, toCiv.CapitalTile.Y));
        pending.Add(new PendingEvent(EventType.MerchantTradeCompleted, fromCiv.CapitalTile,
            null, tradePayload, CivId: fromCiv.Id.Value));

        // Trust bump between rulers
        if (world.GetEntity(fromCiv.RulerId) is Tier1Character fromRuler &&
            world.GetEntity(toCiv.RulerId)   is Tier1Character toRuler)
        {
            var rel = world.Relationships.GetOrCreate(fromRuler.Id, toRuler.Id);
            world.Relationships.Upsert(rel with
            {
                Trust = Math.Clamp(rel.Trust + cfg.TradeTrustGain, -1f, 1f)
            });
        }
    }

    private static void ResolveDiplomacy(
        PendingEmissary emissary, Civilization fromCiv, Civilization toCiv,
        WorldState world, List<PendingEvent> pending, EmissaryConfig cfg)
    {
        if (world.GetEntity(fromCiv.RulerId) is not Tier1Character fromRuler) return;
        if (world.GetEntity(toCiv.RulerId)   is not Tier1Character toRuler)   return;

        var rel = world.Relationships.GetOrCreate(fromRuler.Id, toRuler.Id);
        float newTrust = Math.Clamp(rel.Trust + cfg.TradeTrustGain * 2f, -1f, 1f);
        world.Relationships.Upsert(rel with { Trust = newTrust });

        // Check if trust now crosses alliance threshold
        if (newTrust >= cfg.DiplomacyAllianceMinTrust && !rel.IsAlly)
        {
            world.Relationships.Upsert(world.Relationships.GetOrCreate(fromRuler.Id, toRuler.Id) with
            {
                Flags = rel.Flags | RelationshipFlags.IsAlly
            });

            var alliancePayload = JsonSerializer.Serialize(new AllianceFormedPayload(
                fromRuler.Id.Value, fromRuler.Identity.Name,
                toRuler.Id.Value,   toRuler.Identity.Name,
                fromCiv.Id.Value, toCiv.Id.Value));
            pending.Add(new PendingEvent(EventType.AllianceFormed, fromCiv.CapitalTile,
                null, alliancePayload, new[] { fromRuler.Id.Value }, new[] { toRuler.Id.Value },
                ActorId: fromRuler.Id.Value, ActorName: fromRuler.Identity.Name,
                CivId: fromCiv.Id.Value));
        }
    }

    private static void ResolveSpy(
        PendingEmissary emissary, Civilization fromCiv, Civilization toCiv,
        WorldState world, List<PendingEvent> pending, EmissaryConfig cfg)
    {
        // Spy emissaries upgrade confidence significantly; no visible event to target civ
        if (fromCiv.KnownCivs.TryGetValue(toCiv.Id, out var contact))
        {
            fromCiv.KnownCivs[toCiv.Id] = contact with
            {
                Confidence  = Math.Min(1f, contact.Confidence + cfg.SpyConfidenceBoost),
                BestSource  = (CivContactSource)Math.Max((int)contact.BestSource,
                                                          (int)CivContactSource.EmissaryExchange)
            };
        }

        // Fire CivIntelGathered for the history log (silent to target)
        float finalConfidence = fromCiv.KnownCivs.TryGetValue(toCiv.Id, out var updated)
            ? updated.Confidence : cfg.SpyConfidenceBoost;

        var intelPayload = JsonSerializer.Serialize(new CivIntelGatheredPayload(
            fromCiv.Id.Value, fromCiv.Name, toCiv.Id.Value, toCiv.Name, finalConfidence));
        pending.Add(new PendingEvent(EventType.CivIntelGathered, fromCiv.CapitalTile,
            null, intelPayload, CivId: fromCiv.Id.Value));
    }

    private static void ResolveReligious(
        PendingEmissary emissary, Civilization fromCiv, Civilization toCiv,
        WorldState world, List<PendingEvent> pending, EmissaryConfig cfg)
    {
        int affected = 0;

        // Boost Spiritual need for all target-civ living characters — seeds religion founding
        foreach (var memberId in toCiv.Members)
        {
            if (world.GetEntity(memberId) is not Tier1Character member || !member.IsAlive) continue;
            member.Needs = member.Needs with
            {
                Spiritual = Math.Min(1f, member.Needs.Spiritual + cfg.ReligiousSpreadAweBoost)
            };
            affected++;
        }

        var payload = JsonSerializer.Serialize(new ReligiousEmissaryArrivedPayload(
            fromCiv.Id.Value, fromCiv.Name, toCiv.Id.Value, toCiv.Name, affected));
        pending.Add(new PendingEvent(EventType.ReligiousEmissaryArrived, toCiv.CapitalTile,
            null, payload, CivId: toCiv.Id.Value));
    }
}
