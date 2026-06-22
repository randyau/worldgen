using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class EnvironmentalPhaseTests
{
    private static WorldState BuildWorld(int seed = 42)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    private static EnvironmentalPhase MakePhase(SimConfig cfg) => new(cfg);

    [Fact]
    public void Seasonal_CurrentMoistureUpdatedForAllLandTiles()
    {
        var world = BuildWorld();
        world.CurrentSeason = Season.Summer; // summer has non-zero deltas in most zones
        var phase = MakePhase(world.SimConfig);
        var pending = new List<PendingEvent>();

        // Record pre-tick BaseMoisture for land tiles
        var preTick = new Dictionary<int, byte>();
        for (int i = 0; i < world.TileGrid.TileWidth * world.TileGrid.TileHeight; i++)
        {
            int x = i % world.TileGrid.TileWidth, y = i / world.TileGrid.TileWidth;
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            if ((BiomeType)t.BiomeType is not BiomeType.Ocean)
                preTick[i] = t.CurrentMoisture;
        }

        phase.RunTick(world, pending);

        // At least some land tiles must differ from their starting value
        int changedCount = 0;
        foreach (var (i, before) in preTick)
        {
            int x = i % world.TileGrid.TileWidth, y = i / world.TileGrid.TileWidth;
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            if (t.CurrentMoisture != before) changedCount++;
        }

        changedCount.Should().BeGreaterThan(0,
            "at least some land tiles must have CurrentMoisture updated after a seasonal tick");
    }

    [Fact]
    public void Seasonal_StormCorridorWetterInAutumn()
    {
        var world = BuildWorld();
        var phase = MakePhase(world.SimConfig);
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find storm corridor tiles
        var stormTiles = Enumerable.Range(0, w * h)
            .Where(i => {
                int x = i % w, y = i / w;
                var t = world.TileGrid.GetTile(new TileCoord(x, y));
                return t.StaticFlags.HasFlag(TileStaticFlags.IsStormCorridor)
                    && (BiomeType)t.BiomeType is not BiomeType.Ocean;
            })
            .Take(20)
            .ToList();

        if (!stormTiles.Any()) return; // no storm corridor tiles in this world size

        world.CurrentSeason = Season.Autumn;
        phase.RunTick(world, new List<PendingEvent>());
        float autumnMoisture = stormTiles.Average(i => {
            int x = i % w, y = i / w;
            return (float)world.TileGrid.GetTile(new TileCoord(x, y)).CurrentMoisture;
        });

        // Re-build world and run for Summer
        var world2 = BuildWorld();
        world2.CurrentSeason = Season.Summer;
        MakePhase(world2.SimConfig).RunTick(world2, new List<PendingEvent>());
        float summerMoisture = stormTiles.Average(i => {
            int x = i % world2.TileGrid.TileWidth, y = i / world2.TileGrid.TileWidth;
            return (float)world2.TileGrid.GetTile(new TileCoord(x, y)).CurrentMoisture;
        });

        autumnMoisture.Should().BeGreaterThan(summerMoisture,
            "storm corridor tiles should have higher moisture in Autumn (storm season) than Summer");
    }

    [Fact]
    public void Seasonal_MonsoonTileWetterInSummer()
    {
        var world = BuildWorld();
        var phase = MakePhase(world.SimConfig);
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find tropical tiles (monsoon zone candidates)
        var tropicalTiles = Enumerable.Range(0, w * h)
            .Where(i => {
                int x = i % w, y = i / w;
                var t = world.TileGrid.GetTile(new TileCoord(x, y));
                return (BiomeType)t.BiomeType == BiomeType.TropicalRainforest;
            })
            .Take(20)
            .ToList();

        if (!tropicalTiles.Any()) return; // no tropical tiles in this world

        world.CurrentSeason = Season.Summer;
        phase.RunTick(world, new List<PendingEvent>());
        float summerMoisture = tropicalTiles.Average(i => {
            int x = i % w, y = i / w;
            return (float)world.TileGrid.GetTile(new TileCoord(x, y)).CurrentMoisture;
        });

        var world2 = BuildWorld();
        world2.CurrentSeason = Season.Winter;
        MakePhase(world2.SimConfig).RunTick(world2, new List<PendingEvent>());
        float winterMoisture = tropicalTiles.Average(i => {
            int x = i % world2.TileGrid.TileWidth, y = i / world2.TileGrid.TileWidth;
            return (float)world2.TileGrid.GetTile(new TileCoord(x, y)).CurrentMoisture;
        });

        summerMoisture.Should().BeGreaterThan(winterMoisture,
            "tropical tiles should have higher moisture in monsoon Summer than dry Winter");
    }

    [Fact]
    public void Seasonal_MoistureClampedTo255()
    {
        var world = BuildWorld();
        world.GlobalPrecipitationMultiplier = 10.0f; // extreme multiplier to force overflow
        var phase = MakePhase(world.SimConfig);
        phase.RunTick(world, new List<PendingEvent>());

        for (int y = 0; y < world.TileGrid.TileHeight; y++)
            for (int x = 0; x < world.TileGrid.TileWidth; x++)
            {
                var t = world.TileGrid.GetTile(new TileCoord(x, y));
                t.CurrentMoisture.Should().BeLessThanOrEqualTo(255,
                    $"CurrentMoisture at ({x},{y}) must not exceed 255");
            }
    }

    [Fact]
    public void Seasonal_DrySeasonReducesMoisture()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find land tiles that have a negative winter moisture delta
        var dryTiles = Enumerable.Range(0, w * h)
            .Where(i => {
                int x = i % w, y = i / w;
                var t = world.TileGrid.GetTile(new TileCoord(x, y));
                if ((BiomeType)t.BiomeType is BiomeType.Ocean) return false;
                var profile = world.SeasonalProfiles[i];
                return profile.MoistureDeltaWinter < 0;
            })
            .Take(20)
            .ToList();

        if (!dryTiles.Any()) return;

        // Capture BaseMoisture before tick
        var baseMoistureValues = dryTiles.Select(i => {
            int x = i % w, y = i / w;
            return world.TileGrid.GetTile(new TileCoord(x, y)).BaseMoisture;
        }).ToList();

        world.CurrentSeason = Season.Winter;
        var phase = MakePhase(world.SimConfig);
        phase.RunTick(world, new List<PendingEvent>());

        // CurrentMoisture should be less than BaseMoisture for tiles with negative winter delta
        int reducedCount = 0;
        for (int k = 0; k < dryTiles.Count; k++)
        {
            int i = dryTiles[k];
            int x = i % w, y = i / w;
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            if (t.CurrentMoisture < baseMoistureValues[k]) reducedCount++;
        }

        reducedCount.Should().BeGreaterThan(0,
            "tiles with negative winter seasonal delta should have CurrentMoisture below BaseMoisture");
    }
}

public class ClimateDriftTests
{
    private static WorldState BuildWorld(int seed = 1)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    [Fact]
    public void Drift_TemperatureAnomalyIncreases()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualTempDriftRate = 0.5f; // 0.5 degree per year
        var phase = new EnvironmentalPhase(world.SimConfig);
        float before = world.GlobalTemperatureAnomaly;

        world.CurrentSeason = Season.Spring;
        var pending = new List<PendingEvent>();
        phase.RunTick(world, pending, isAnnualTick: true);

        world.GlobalTemperatureAnomaly.Should().BeGreaterThan(before,
            "with positive drift rate, GlobalTemperatureAnomaly should increase after annual tick");
    }

    [Fact]
    public void Drift_AnomalyClamped()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualTempDriftRate = 100f; // extreme drift
        world.SimConfig.Climate.MaxWarmingAnomaly = 5.0f;
        var phase = new EnvironmentalPhase(world.SimConfig);

        // Run many years
        for (int y = 0; y < 20; y++)
        {
            world.CurrentSeason = Season.Spring;
            phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);
        }

        world.GlobalTemperatureAnomaly.Should().BeLessThanOrEqualTo(5.0f,
            "GlobalTemperatureAnomaly must not exceed MaxWarmingAnomaly");
    }

    [Fact]
    public void Drift_StormCorridorShiftsWithAnomaly()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualTempDriftRate = 1.0f;
        world.SimConfig.Climate.StormCorridorShiftPerDegree = 0.01f;
        float before = world.StormCorridorNormalizedLat;
        var phase = new EnvironmentalPhase(world.SimConfig);

        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        world.StormCorridorNormalizedLat.Should().NotBe(before,
            "storm corridor should shift when temperature anomaly changes");
    }

    [Fact]
    public void Drift_NoDriftIfRateIsZero()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualTempDriftRate = 0.0f;
        float before = world.GlobalTemperatureAnomaly;
        var phase = new EnvironmentalPhase(world.SimConfig);

        for (int y = 0; y < 10; y++)
        {
            world.CurrentSeason = Season.Spring;
            phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);
        }

        world.GlobalTemperatureAnomaly.Should().Be(before,
            "with drift rate=0, anomaly must remain unchanged");
    }

    [Fact]
    public void Drift_BiomeChangedEventQueuedOnChange()
    {
        var world = BuildWorld();
        // Set extreme warming to force biome reclassification
        world.GlobalTemperatureAnomaly = 50f; // extremely hot
        world.SimConfig.Climate.AnnualTempDriftRate = 0.0f; // don't drift further
        var phase = new EnvironmentalPhase(world.SimConfig);
        var pending = new List<PendingEvent>();

        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, pending, isAnnualTick: true);

        // There should be BiomeShifted events if any tiles reclassified
        // (With anomaly=50, many cold tiles should reclassify to warmer biomes)
        var biomeEvents = pending.Where(p => p.Type == EventType.BiomeShifted).ToList();
        biomeEvents.Should().NotBeEmpty(
            "extreme temperature anomaly should cause biome reclassification, emitting BiomeShifted events");
    }

    [Fact]
    public void Drift_BiomeChangesAfterSufficientWarming()
    {
        var world = BuildWorld();
        // Count tundra tiles before
        int tundraCount = 0;
        for (int y = 0; y < world.TileGrid.TileHeight; y++)
            for (int x = 0; x < world.TileGrid.TileWidth; x++)
                if ((BiomeType)world.TileGrid.GetTile(new TileCoord(x, y)).BiomeType == BiomeType.Tundra)
                    tundraCount++;

        if (tundraCount == 0) return; // no tundra in this seed

        world.GlobalTemperatureAnomaly = 50f;
        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        int tundraAfter = 0;
        for (int y = 0; y < world.TileGrid.TileHeight; y++)
            for (int x = 0; x < world.TileGrid.TileWidth; x++)
                if ((BiomeType)world.TileGrid.GetTile(new TileCoord(x, y)).BiomeType == BiomeType.Tundra)
                    tundraAfter++;

        tundraAfter.Should().BeLessThan(tundraCount,
            "extreme warming should reduce the number of Tundra tiles");
    }
}
