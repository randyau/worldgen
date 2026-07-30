using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

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
        world.SimConfig.Climate.AnnualTempDriftRate = 0.5f;
        var phase = new EnvironmentalPhase(world.SimConfig);
        float before = world.GlobalTemperatureAnomaly;

        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        world.GlobalTemperatureAnomaly.Should().BeGreaterThan(before,
            "with positive drift rate, GlobalTemperatureAnomaly should increase after annual tick");
    }

    [Fact]
    public void Drift_AnomalyClamped()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualTempDriftRate = 100f;
        world.SimConfig.Climate.MaxWarmingAnomaly = 5.0f;
        world.SimConfig.Climate.ClimateCycleAmplitude = 0f; // isolate the secular-clamp behavior under test
        var phase = new EnvironmentalPhase(world.SimConfig);

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
        world.SimConfig.Climate.ClimateCycleAmplitude = 0f; // isolate the secular-drift-off behavior under test
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
    public void Drift_CycleKeepsAnomalyMovingAfterSecularTrendSaturates()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualTempDriftRate = 100f;   // saturates SecularTemperatureAnomaly in 1 tick
        world.SimConfig.Climate.MaxWarmingAnomaly = 5.0f;
        world.SimConfig.Climate.MaxCoolingAnomaly = 5.0f;
        world.SimConfig.Climate.ClimateCycleAmplitude = 3.0f;
        world.SimConfig.Climate.ClimateCyclePeriodYears = 100;
        var phase = new EnvironmentalPhase(world.SimConfig);

        var seen = new HashSet<float>();
        for (int y = 0; y < 100; y++)
        {
            world.CurrentSeason = Season.Spring;
            phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);
            seen.Add(world.GlobalTemperatureAnomaly);
        }

        seen.Count.Should().BeGreaterThan(1,
            "even after the secular trend saturates at its clamp, the cyclical term should keep " +
            "GlobalTemperatureAnomaly moving across a long run instead of freezing at the ceiling");
    }

    [Fact]
    public void Drift_BiomeChangedEventQueuedOnChange()
    {
        var world = BuildWorld();
        world.GlobalTemperatureAnomaly = 50f;
        world.SimConfig.Climate.AnnualTempDriftRate = 0.0f;
        var phase = new EnvironmentalPhase(world.SimConfig);
        var pending = new List<PendingEvent>();

        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, pending, isAnnualTick: true);

        pending.Should().Contain(p => p.Type == EventType.BiomeChanged,
            "extreme temperature anomaly should cause biome reclassification, emitting BiomeChanged events");
    }

    [Fact]
    public void Drift_BiomeChangesAfterSufficientWarming()
    {
        var world = BuildWorld();
        int tundraCount = 0;
        for (int y = 0; y < world.TileGrid.TileHeight; y++)
            for (int x = 0; x < world.TileGrid.TileWidth; x++)
                if ((BiomeType)world.TileGrid.GetTile(new TileCoord(x, y)).BiomeType == BiomeType.Tundra)
                    tundraCount++;

        if (tundraCount == 0) return;

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
