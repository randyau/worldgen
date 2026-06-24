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
            int reachRadius = stub.ReachRadius();
            var ledger = BuildLedger(coord, stub, reachRadius, world);

            float rawFoodRatio = ledger.TryGetValue("food",  out var f) ? f : 1f;
            float waterRatio   = ledger.TryGetValue("water", out var w) ? w : 1f;

            // ─── Food stores ─────────────────────────────────────────────────
            float maxStore = Math.Max(_cfg.StoreMinSeasons,
                                      stub.Population / 1000f * _cfg.StoreMaxSeasonsPerKPop);
            float stores   = stub.FoodStores;

            // Spoilage every tick (food degrades even in the granary)
            stores *= (1f - _cfg.StoreSpoilageRate);

            float effectiveFoodRatio;
            if (rawFoodRatio >= 1f)
            {
                // Surplus — fill stores with a fraction of excess
                float surplus = (rawFoodRatio - 1f) * _cfg.StoreAccumulateRate;
                stores = Math.Min(stores + surplus, maxStore);
                effectiveFoodRatio = rawFoodRatio; // surplus doesn't need store support
            }
            else
            {
                // Deficit — draw from stores to cover the gap (up to what's available)
                float needed = 1f - rawFoodRatio;
                float drawn  = Math.Min(stores, needed);
                stores -= drawn;
                effectiveFoodRatio = rawFoodRatio + drawn;
            }

            stores = Math.Max(0f, stores);

            world.Settlements[coord] = stub with
            {
                FoodPressureRatio  = effectiveFoodRatio,
                WaterPressureRatio = waterRatio,
                ResourceLedger     = ledger,
                FoodStores         = stores
            };

            // Shortage response (based on effective ratio after store draw)
            if (effectiveFoodRatio < _cfg.ShortageThreshold)
            {
                SeedResourceGoals(coord, GoalObject.Food, "food", effectiveFoodRatio, world, tick);

                if (tick - stub.LastStrainEventTick > _cfg.StrainEventCooldown)
                {
                    pending.Add(MakeStrainEvent(coord, stub, "food", effectiveFoodRatio));
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

            // Food: fertility × moisture.
            // CurrentMoisture is seasonal and can crash to near-zero in winter on inland tiles,
            // zeroing food supply and triggering famine even on fertile land. Use the higher of
            // actual moisture or a fraction of base moisture to represent stored food, wells, etc.
            float effectiveMoisture = Math.Max(
                tile.CurrentMoisture / 255f,
                tile.BaseMoisture / 255f * _cfg.FoodMoistureFloor);
            float foodContrib = (tile.Fertility / 255f) * effectiveMoisture;
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
