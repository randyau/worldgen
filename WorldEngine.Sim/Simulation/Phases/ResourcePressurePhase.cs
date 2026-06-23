using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Each tick:
///   1. Computes each settlement's "reach" — the set of tiles it can exploit.
///   2. Builds an extensible resource ledger (Dictionary keyed by resource type string)
///      from reach tiles: food (fertility × moisture), timber (forest biomes),
///      and any mineral deposits. New resource types added to config flow through
///      automatically without code changes.
///   3. Seeds Acquire / Flee goals on resident characters when ledger shows deficits.
///   4. Emits SettlementStraining events (rate-limited) for significant shortages.
/// </summary>
public sealed class ResourcePressurePhase
{
    private readonly ResourcePressureConfig _cfg;

    public ResourcePressurePhase(SimConfig cfg) => _cfg = cfg.ResourcePressure;

    // Timber contribution per forest reach tile (normalized supply unit)
    private const float TimberPerForestTile = 0.5f;

    public List<PendingEvent> Execute(WorldState world, long tick)
    {
        var pending = new List<PendingEvent>();

        foreach (var (coord, stub) in world.Settlements.ToList())
        {
            int reachRadius = ReachRadius(stub.Population);
            var ledger = BuildLedger(coord, stub, reachRadius, world);

            float foodRatio  = ledger.TryGetValue("food",  out var f) ? f : 1f;
            float waterRatio = ledger.TryGetValue("water", out var w) ? w : 1f;

            world.Settlements[coord] = stub with
            {
                FoodPressureRatio  = foodRatio,
                WaterPressureRatio = waterRatio,
                ResourceLedger     = ledger
            };

            // Shortage response
            if (foodRatio < _cfg.ShortageThreshold)
            {
                SeedResourceGoals(coord, GoalObject.Food, "food", foodRatio, world, tick);

                if (tick - stub.LastStrainEventTick > _cfg.StrainEventCooldown)
                {
                    pending.Add(MakeStrainEvent(coord, stub, "food", foodRatio));
                    world.Settlements[coord] = world.Settlements[coord] with
                    {
                        LastStrainEventTick = (int)tick
                    };
                }
            }

            if (waterRatio < _cfg.ShortageThreshold)
                SeedResourceGoals(coord, GoalObject.Water, "water", waterRatio, world, tick);
        }

        return pending;
    }

    // ─── Reach radius ─────────────────────────────────────────────────────────

    private int ReachRadius(int population)
    {
        // Small settlements work 2-3 tiles out; large ones up to 5.
        // Each 2000 people roughly adds one tile of effective reach.
        return Math.Clamp(2 + population / 2000, 2, 5);
    }

    // ─── Ledger construction ──────────────────────────────────────────────────

    private Dictionary<string, float> BuildLedger(
        TileCoord center, SettlementStub stub, int radius, WorldState world)
    {
        var supply = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        int tileCount = 0;

        foreach (var coord in world.GetTilesInRadius(center, radius))
        {
            if (!world.IsLand(coord)) continue;
            var tile = world.TileGrid.GetTile(coord);
            tileCount++;

            // Food: fertility × moisture (both 0–255, normalized to 0–1)
            float foodContrib = (tile.Fertility / 255f) * (tile.CurrentMoisture / 255f);
            Accumulate(supply, "food", foodContrib);

            // Water: moisture alone (wells, streams, rainfall access)
            Accumulate(supply, "water", tile.CurrentMoisture / 255f);

            // Timber: forested biomes
            var biome = (BiomeType)tile.BiomeType;
            if (biome is BiomeType.TemperateForest or BiomeType.BorealForest
                      or BiomeType.TropicalRainforest or BiomeType.Swamp)
                Accumulate(supply, "timber", TimberPerForestTile);

            // Mineral deposits — extensible: any new deposit type flows through automatically
            if (world.ResourceRegistry.TryGetValue(coord, out var deposits))
            {
                foreach (var dep in deposits)
                {
                    // Key = lowercase deposit type: "iron", "copper", "gold", etc.
                    string key = dep.DepositType.ToLowerInvariant();
                    float contrib = dep.Quality / 255f * (1f - dep.Depth / 255f * 0.5f);
                    Accumulate(supply, key, contrib);
                }
            }
        }

        if (tileCount == 0) return supply;

        // Convert absolute supply to supply/demand ratios.
        // Demand scales with population; PopulationCapPerTile is the full-demand baseline per tile.
        float demand = Math.Max(1f, stub.Population / _cfg.PopulationCapPerTile);

        // Food and water are per-capita resources; minerals are absolute supply
        if (supply.TryGetValue("food",  out float fs)) supply["food"]  = fs / demand;
        if (supply.TryGetValue("water", out float ws)) supply["water"] = ws / demand;
        // Minerals: leave as absolute supply (surplus if > 0, demand is driven by artisans later)

        return supply;
    }

    private static void Accumulate(Dictionary<string, float> dict, string key, float value)
    {
        dict[key] = dict.TryGetValue(key, out float existing) ? existing + value : value;
    }

    // ─── Goal seeding ─────────────────────────────────────────────────────────

    private void SeedResourceGoals(
        TileCoord coord, GoalObject obj, string resourceTag, float ratio,
        WorldState world, long tick)
    {
        foreach (var entity in world.GetEntitiesAt(coord))
        {
            if (entity is not Tier1Character c || !c.IsAlive) continue;

            bool hasAcquire = c.Goals.Any(g => g.Type == GoalType.Acquire
                                            && g.ResourceTag == resourceTag);
            if (!hasAcquire)
                c.Goals.Add(new GoalData
                {
                    Type        = GoalType.Acquire,
                    Object      = obj,
                    ResourceTag = resourceTag,
                    Priority    = _cfg.AcquireGoalIntensity * (1f - ratio),
                    Intensity   = _cfg.AcquireGoalIntensity,
                    FormedTick  = (int)tick,
                    StaleSince  = (int)tick
                });

            if (ratio < _cfg.CrisisThreshold && c.Personality.Stability < 0.5f
                && !c.Goals.Any(g => g.Type == GoalType.Flee))
                c.Goals.Add(new GoalData
                {
                    Type       = GoalType.Flee,
                    Object     = GoalObject.Region,
                    Priority   = _cfg.FleeGoalIntensity,
                    Intensity  = _cfg.FleeGoalIntensity,
                    FormedTick = (int)tick,
                    StaleSince = (int)tick
                });
        }
    }

    // ─── Event emission ───────────────────────────────────────────────────────

    private static PendingEvent MakeStrainEvent(
        TileCoord coord, SettlementStub stub, string resource, float ratio)
    {
        var payload = JsonSerializer.Serialize(new
        {
            settlementName = stub.Name,
            settlementTile = new[] { coord.X, coord.Y },
            civId          = stub.CivId.Value,
            resource,
            ratio,
            impact         = ratio < 0.3f ? "crisis" : "shortage"
        });
        return new PendingEvent(EventType.SettlementStraining, coord, null, payload);
    }
}
