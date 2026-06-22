using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class SeaLevelTests
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

    [Fact]
    public void SeaLevel_NoChangeWhenRateIsZero()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = 0.0f;
        float before = world.CurrentSeaLevel;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        world.CurrentSeaLevel.Should().Be(before,
            "CurrentSeaLevel must not change when AnnualSeaLevelDriftRate is 0");
    }

    [Fact]
    public void SeaLevel_LevelChangesWhenRateIsSet()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = 0.05f;
        float before = world.CurrentSeaLevel;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        world.CurrentSeaLevel.Should().NotBe(before,
            "CurrentSeaLevel should change when AnnualSeaLevelDriftRate > 0");
    }

    [Fact]
    public void SeaLevel_EventEmittedWhenThresholdCrossed()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = 0.5f; // large delta
        world.SimConfig.Climate.SeaLevelEventThreshold = 0.1f;

        var phase = new EnvironmentalPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, pending, isAnnualTick: true);

        pending.Should().Contain(p => p.Type == EventType.SeaLevelChanged,
            "a large sea level change should emit a SeaLevelChanged pending event");
    }

    [Fact]
    public void SeaLevel_NoEventBelowThreshold()
    {
        var world = BuildWorld();
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = 0.001f; // tiny delta
        world.SimConfig.Climate.SeaLevelEventThreshold = 0.1f;

        var phase = new EnvironmentalPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, pending, isAnnualTick: true);

        pending.Should().NotContain(p => p.Type == EventType.SeaLevelChanged,
            "a small sea level change below the threshold should not emit a SeaLevelChanged event");
    }

    [Fact]
    public void SeaLevel_LowElevationTilesSubmergedOnRise()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a land tile with very low elevation that's currently just above sea level
        TileCoord? target = null;
        for (int y = 0; y < h && target is null; y++)
        for (int x = 0; x < w && target is null; x++)
        {
            var coord = new TileCoord(x, y);
            var t = world.TileGrid.GetTile(coord);
            if ((BiomeType)t.BiomeType is not BiomeType.Ocean and not BiomeType.CoastalWater
                && t.Elevation < 50)
                target = coord;
        }
        if (target is null) return;

        // Set sea level to just below target tile's elevation as fraction of 255
        var targetTile = world.TileGrid.GetTile(target.Value);
        float fractionalElevation = (targetTile.Elevation + 1) / 255f;

        world.CurrentSeaLevel = 0.0f;
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = fractionalElevation + 0.01f;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        var after = world.TileGrid.GetTile(target.Value);
        ((BiomeType)after.BiomeType).Should().Be(BiomeType.Ocean,
            "a tile whose elevation falls below the new sea level should be reclassified as Ocean");
    }
}
