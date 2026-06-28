using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Annual phase (Spring only): grows or contracts each city's territory based on population.
/// Expansion: claims the highest-fertility unclaimed adjacent tile, up to TerritoryGrowthPerYear per city.
/// Contraction: releases the tiles farthest from the city center when population has dropped.
/// </summary>
public sealed class TerritoryPhase
{
    private readonly TerritoryConfig _cfg;

    public TerritoryPhase(SimConfig cfg)
    {
        _cfg = cfg.Territory;
    }

    public List<PendingEvent> Execute(WorldState world)
    {
        var pending = new List<PendingEvent>();

        foreach (var (civId, civ) in world.Civilizations)
        {
            if (civ.IsCollapsed) continue;

            foreach (var (cityTile, ownedTiles) in civ.CityTerritories.ToList())
            {
                if (!world.Settlements.TryGetValue(cityTile, out var stub)) continue;
                // Only process settlements belonging to this civ
                if (stub.CivId != civId) continue;

                int owned = ownedTiles.Count;

                // Territory expands freely up to the radius cap (enforced inside ExpandTerritory)
                // and the MaxCityTiles ceiling. Population is NOT used as a gate here — the
                // carrying capacity model in ResourcePressurePhase lets population grow to match
                // what the territory can actually support, rather than the reverse.
                if (owned < _cfg.MaxCityTiles)
                {
                    int canExpand = Math.Min(_cfg.TerritoryGrowthPerYear, _cfg.MaxCityTiles - owned);
                    ExpandTerritory(cityTile, civId, civ, ownedTiles, canExpand, world, pending);
                }
                // Automatic contraction removed — territory is only lost through war/raid
                // damage or settlement abandonment (handled in CivTracker and PopDynamics).
            }
        }

        return pending;
    }

    // ─── Expansion ────────────────────────────────────────────────────────────

    private static void ExpandTerritory(
        TileCoord cityTile, CivId civId, Civilization civ,
        HashSet<TileCoord> ownedTiles, int claimCount,
        WorldState world, List<PendingEvent> pending)
    {
        int w = world.TileGrid.TileWidth;
        int h = world.TileGrid.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        int maxRadiusSq = world.SimConfig.Territory.MaxTerritoryRadius
                        * world.SimConfig.Territory.MaxTerritoryRadius;

        int claimed = 0;

        for (int pass = 0; pass < claimCount; pass++)
        {
            // Find the highest-fertility unclaimed adjacent land tile within MaxTerritoryRadius.
            // Radius cap forces civs to found new cities to access land beyond it.
            TileCoord? bestCoord = null;
            int bestFertility = -1;

            foreach (var ownedTile in ownedTiles)
            {
                for (int i = 0; i < 4; i++)
                {
                    int nx = ((ownedTile.X + dx[i]) % w + w) % w;
                    int ny = Math.Clamp(ownedTile.Y + dy[i], 0, h - 1);
                    var candidate = new TileCoord(nx, ny);

                    if (world.TerritoryMap.ContainsKey(candidate)) continue;
                    if (!world.IsLand(candidate)) continue;

                    // Enforce radius cap from city center (wrap-aware on X axis).
                    int rdx = candidate.X - cityTile.X;
                    if (Math.Abs(rdx) > w / 2) rdx -= Math.Sign(rdx) * w;
                    int rdy = candidate.Y - cityTile.Y;
                    if (rdx * rdx + rdy * rdy > maxRadiusSq) continue;

                    int fertility = world.TileGrid.GetTile(candidate).Fertility;
                    if (fertility > bestFertility)
                    {
                        bestFertility = fertility;
                        bestCoord = candidate;
                    }
                }
            }

            if (!bestCoord.HasValue) break;

            world.TerritoryMap[bestCoord.Value] = cityTile;
            ownedTiles.Add(bestCoord.Value);
            claimed++;
        }

        if (claimed <= 0) return;

        string civName = civ.Name;
        var payload = JsonSerializer.Serialize(new TerritoryExpandedPayload(
            civId.Value, civName, cityTile.X, cityTile.Y, claimed, ownedTiles.Count));
        pending.Add(new PendingEvent(EventType.TerritoryExpanded, cityTile, null, payload,
            CivId: civId.Value,
            SettlementName: world.Settlements.TryGetValue(cityTile, out var s) ? s.Name : null));
    }

    // ─── Contraction ─────────────────────────────────────────────────────────

    private static void ContractTerritory(
        TileCoord cityTile, CivId civId, Civilization civ,
        HashSet<TileCoord> ownedTiles, int releaseCount,
        WorldState world, List<PendingEvent> pending)
    {
        // Never release the city tile itself
        var releasable = ownedTiles
            .Where(t => t != cityTile)
            .OrderByDescending(t => EuclideanDistSq(t, cityTile))
            .Take(releaseCount)
            .ToList();

        foreach (var t in releasable)
        {
            world.TerritoryMap.Remove(t);
            ownedTiles.Remove(t);
            world.ImprovementMap.Remove(t);
        }

        if (releasable.Count <= 0) return;

        string civName = civ.Name;
        var payload = JsonSerializer.Serialize(new TerritoryLostPayload(
            civId.Value, civName, cityTile.X, cityTile.Y,
            releasable.Count, ownedTiles.Count, "population_decline"));
        pending.Add(new PendingEvent(EventType.TerritoryLost, cityTile, null, payload,
            CivId: civId.Value,
            SettlementName: world.Settlements.TryGetValue(cityTile, out var s) ? s.Name : null));
    }

    private static float EuclideanDistSq(TileCoord a, TileCoord b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
