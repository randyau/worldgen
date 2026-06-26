using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
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
    private readonly SettlementConfig       _settleCfg;

    // Pre-baked lookup tables to avoid switch/piecewise computation on every reach tile.
    // Built once at construction from config; indexed by (byte)effTemp and (int)BiomeType.
    private readonly float[] _gsTable;    // GrowingSeasonFactor: 256 entries indexed by effective temperature byte
    private readonly float[] _foodTable;  // BiomeFoodMultiplier: 16 entries indexed by (int)BiomeType
    private readonly int[]   _carryTable; // BiomeCarryingCapacity: 16 entries indexed by (int)BiomeType

    public ResourcePressurePhase(SimConfig cfg)
    {
        _cfg       = cfg.ResourcePressure;
        _settleCfg = cfg.Settlement;
        _gsTable    = BuildGrowingSeasonTable(_cfg);
        _foodTable  = BuildFoodTable(_cfg);
        _carryTable = BuildCarryTable(_settleCfg);
    }

    private static float[] BuildGrowingSeasonTable(ResourcePressureConfig cfg)
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++) t[i] = GrowingSeasonFactor((byte)i, cfg);
        return t;
    }

    private static float[] BuildFoodTable(ResourcePressureConfig cfg)
    {
        var t = new float[16];
        for (int i = 0; i < 16; i++) t[i] = BiomeFoodMultiplier((BiomeType)i, cfg);
        return t;
    }

    private static int[] BuildCarryTable(SettlementConfig settleCfg)
    {
        var t = new int[16];
        for (int i = 0; i < 16; i++) t[i] = BiomeCarryingCapacity((BiomeType)i, settleCfg);
        return t;
    }

    // Timber contribution per forest reach tile (normalized supply unit)
    private const float TimberPerForestTile = 0.5f;

    public List<PendingEvent> Execute(WorldState world, long tick)
    {
        var pending = new List<PendingEvent>();

        foreach (var (coord, stub) in world.Settlements.ToList())
        {
            int reachRadius = stub.ReachRadius();
            var (ledger, carryingCapacity) = BuildLedger(coord, stub, reachRadius, world);

            // ─── Update resource stores ──────────────────────────────────────
            // Build new stores dict from existing stores, applying spoilage and accumulating
            // from this tick's tile yields. Vital resources (food, water) also draw during deficit.
            var newStores = stub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(stub.ResourceStores, StringComparer.OrdinalIgnoreCase);

            float maxVitalStore = Math.Max(_cfg.StoreMinSeasons,
                                           stub.Population / 1000f * _cfg.StoreMaxSeasonsPerKPop);

            // Process vital resources: food and water (supply/demand ratios → season-units in stores)
            float effectiveFoodRatio  = ApplyVitalStore("food",  GoalObject.Food,
                ledger, newStores, maxVitalStore, _cfg.FoodSpoilageRate,  _cfg);
            float effectiveWaterRatio = ApplyVitalStore("water", GoalObject.Water,
                ledger, newStores, maxVitalStore, _cfg.WaterSpoilageRate, _cfg);

            // Process non-vital resources: minerals, timber, gold, trade goods
            // These just accumulate from surplus tile production; no demand draw.
            foreach (var (res, supply) in ledger)
            {
                if (res is "food" or "water") continue; // already handled above

                float spoilage = res is "gold" or "gems" or "silver" ? _cfg.WealthSpoilageRate
                               : _cfg.StockpileSpoilageRate;
                float current  = newStores.GetValueOrDefault(res, 0f);
                current *= (1f - spoilage);
                current += supply * _cfg.WealthAccumulateRate;
                newStores[res] = Math.Max(0f, current);
            }

            world.Settlements[coord] = stub with
            {
                FoodPressureRatio  = effectiveFoodRatio,
                WaterPressureRatio = effectiveWaterRatio,
                ResourceLedger     = ledger,
                ResourceStores     = newStores,
                CarryingCapacity   = carryingCapacity
            };

            // Shortage response (based on effective ratios after store draw)
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

            if (effectiveWaterRatio < _cfg.ShortageThreshold)
                SeedResourceGoals(coord, GoalObject.Water, "water", effectiveWaterRatio, world, tick);
        }

        return pending;
    }

    // ─── Ledger construction ──────────────────────────────────────────────────

    private (Dictionary<string, float> Ledger, int CarryingCapacity) BuildLedger(
        TileCoord center, SettlementStub stub, int radius, WorldState world)
    {
        var supply = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        int tileCount = 0;
        int carryTotal = 0;

        int w = world.TileGrid.TileWidth;

        // GetCachedLandTilesInRadius avoids re-computing circle geometry + IsLand filter every tick;
        // world geometry is immutable after worldgen so results are cached permanently on WorldState.
        foreach (var coord in world.GetCachedLandTilesInRadius(center, radius))
        {
            var tile = world.TileGrid.GetTile(coord);
            tileCount++;

            // Food: fertility × moisture × growing-season temperature factor.
            // Moisture floor: use higher of current or 25% of base to represent wells/irrigation.
            // Temperature factor: 0 at frost, 1.0 in the optimal temperate band, tapering at extreme heat.
            float effectiveMoisture = Math.Max(
                tile.CurrentMoisture / 255f,
                tile.BaseMoisture / 255f * _cfg.FoodMoistureFloor);
            int   idx        = coord.X + coord.Y * w;
            byte  effTemp    = TileTemperature.Effective(tile, idx, world);
            var   biome      = (BiomeType)tile.BiomeType;
            int   biomeIdx   = (int)biome;
            float foodContrib = (tile.Fertility / 255f) * effectiveMoisture * _gsTable[effTemp] * _foodTable[biomeIdx];
            Accumulate(supply, "food", foodContrib);

            // Water: moisture alone (wells, streams, rainfall access)
            Accumulate(supply, "water", tile.CurrentMoisture / 255f);

            // Timber: forested biomes
            if (biome is BiomeType.TemperateForest or BiomeType.BorealForest
                      or BiomeType.TropicalRainforest or BiomeType.Swamp)
                Accumulate(supply, "timber", TimberPerForestTile);

            // Carrying capacity — accumulated alongside food/water in the same tile walk
            carryTotal += _carryTable[biomeIdx];

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

        int capacity = Math.Max(_settleCfg.CarryCapMinimum, carryTotal);
        if (tileCount == 0) return (supply, capacity);

        // Convert absolute supply to supply/demand ratios.
        // Demand scales with population; PopulationCapPerTile is the full-demand baseline per tile.
        float demand = Math.Max(1f, stub.Population / _cfg.PopulationCapPerTile);

        // Food and water are per-capita resources; minerals are absolute supply
        if (supply.TryGetValue("food",  out float fs)) supply["food"]  = fs / demand;
        if (supply.TryGetValue("water", out float ws)) supply["water"] = ws / demand;
        // Minerals: leave as absolute supply (surplus if > 0, demand is driven by artisans later)

        return (supply, capacity);
    }

    private static int BiomeCarryingCapacity(BiomeType biome, SettlementConfig cfg) => biome switch
    {
        BiomeType.Grassland          => cfg.CarryCapGrassland,
        BiomeType.Plains             => cfg.CarryCapPlains,
        BiomeType.TropicalRainforest => cfg.CarryCapTropicalRainforest,
        BiomeType.Savanna            => cfg.CarryCapSavanna,
        BiomeType.TemperateForest    => cfg.CarryCapTemperateForest,
        BiomeType.BorealForest       => cfg.CarryCapBorealForest,
        BiomeType.Swamp              => cfg.CarryCapSwamp,
        BiomeType.Beach              => cfg.CarryCapBeach,
        BiomeType.Mountain           => cfg.CarryCapMountain,
        BiomeType.HighMountain       => cfg.CarryCapHighMountain,
        BiomeType.Desert             => cfg.CarryCapDesert,
        BiomeType.Volcanic           => cfg.CarryCapVolcanic,
        _                            => cfg.CarryCapDefault,
    };

    private static void Accumulate(Dictionary<string, float> dict, string key, float value)
    {
        dict[key] = dict.TryGetValue(key, out float existing) ? existing + value : value;
    }

    /// <summary>
    /// Piecewise linear growing-season factor based on effective temperature.
    /// 0 at and below frost threshold → ramp to 1.0 at optimal low → flat 1.0 through optimal high
    /// → ramp down to HeatStressFactor at 255.
    /// </summary>
    private static float GrowingSeasonFactor(byte effTemp, ResourcePressureConfig cfg)
    {
        if (effTemp <= cfg.FrostTemperatureThreshold) return 0f;
        if (effTemp <= cfg.OptimalTemperatureLow)
            return (float)(effTemp - cfg.FrostTemperatureThreshold)
                 / (cfg.OptimalTemperatureLow - cfg.FrostTemperatureThreshold);
        if (effTemp <= cfg.OptimalTemperatureHigh) return 1f;
        // Linear ramp from 1.0 at OptimalHigh to HeatStressFactor at 255
        float t = (float)(effTemp - cfg.OptimalTemperatureHigh) / (255 - cfg.OptimalTemperatureHigh);
        return 1f - t * (1f - cfg.HeatStressFactor);
    }

    /// <summary>
    /// Per-biome food production multiplier. Reflects farming suitability: flat grassland
    /// and tropical forest are ideal; tundra and desert are hostile to agriculture.
    /// Scaled by cfg.BiomeFoodBonusScale so the whole system can be dampened in config.
    /// </summary>
    private static float BiomeFoodMultiplier(BiomeType biome, ResourcePressureConfig cfg)
    {
        float raw = biome switch
        {
            // Excellent farmland — flat, fertile, well-watered
            BiomeType.Grassland          => 2.0f,
            BiomeType.Plains             => 1.6f,
            // Tropical: year-round growing season, high rainfall
            BiomeType.TropicalRainforest => 2.5f,
            BiomeType.Savanna            => 1.2f,
            // Temperate forests: cleared land is excellent; foraging supplements
            BiomeType.TemperateForest    => 1.8f,
            // Cold biomes: short growing season; meaningful but constrained
            BiomeType.BorealForest       => 0.9f,
            BiomeType.Tundra             => 0.5f,
            // Water-adjacent: fishing compensates for poor tillage
            BiomeType.Swamp              => 1.1f,
            BiomeType.Beach              => 0.8f,
            // Rugged terrain: limited flat land; terracing is expensive
            BiomeType.Mountain           => 0.7f,
            BiomeType.HighMountain       => 0.3f,
            // Hostile: minimal viable agriculture
            BiomeType.Desert             => 0.3f,
            BiomeType.Volcanic           => 0.8f, // fertile ash soil when not actively erupting
            _                            => 1.0f,
        };
        // Lerp between 1.0 (no bonus) and raw at the configured scale
        return 1f + (raw - 1f) * cfg.BiomeFoodBonusScale;
    }

    /// <summary>
    /// Applies spoilage, accumulates surplus, and draws deficit from stores for a vital resource.
    /// Returns the effective ratio after store support (may differ from raw tile ratio).
    /// </summary>
    private static float ApplyVitalStore(
        string resource, GoalObject _,
        Dictionary<string, float> ledger,
        Dictionary<string, float> stores,
        float maxStore, float spoilageRate,
        ResourcePressureConfig cfg)
    {
        float rawRatio = ledger.TryGetValue(resource, out var r) ? r : 1f;
        float current  = stores.GetValueOrDefault(resource, 0f);

        current *= (1f - spoilageRate);

        float effective;
        if (rawRatio >= 1f)
        {
            current  = Math.Min(current + (rawRatio - 1f) * cfg.StoreAccumulateRate, maxStore);
            effective = rawRatio;
        }
        else
        {
            float drawn = Math.Min(current, 1f - rawRatio);
            current  -= drawn;
            effective = rawRatio + drawn;
        }

        stores[resource] = Math.Max(0f, current);
        return effective;
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
        var payload = JsonSerializer.Serialize(new SettlementStrainPayload(
            resource, ratio, ratio < 0.3f ? "crisis" : "shortage"));
        return new PendingEvent(EventType.SettlementStraining, coord, null, payload,
            CivId: stub.CivId.Value, SettlementName: stub.Name);
    }
}
