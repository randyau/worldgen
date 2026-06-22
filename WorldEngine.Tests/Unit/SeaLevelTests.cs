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

    [Fact]
    public void SeaLevel_IsCoastalFlagSetOnNewCoast()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a low-elevation land tile adjacent to another low-elevation land tile
        // so that when the first is submerged the second gains IsCoastal
        TileCoord? origin = null;
        TileCoord? neighbor = null;
        for (int y = 1; y < h - 1 && origin is null; y++)
        for (int x = 0; x < w && origin is null; x++)
        {
            var coord = new TileCoord(x, y);
            var t = world.TileGrid.GetTile(coord);
            if ((BiomeType)t.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater || t.Elevation >= 50) continue;

            // Check neighbors: find one that is also land with slightly higher elevation
            TileCoord[] candidates = {
                new TileCoord(((x + 1) % w + w) % w, y),
                new TileCoord(((x - 1) % w + w) % w, y),
                new TileCoord(x, y - 1),
                new TileCoord(x, y + 1),
            };
            foreach (var nb in candidates)
            {
                var nbt = world.TileGrid.GetTile(nb);
                if ((BiomeType)nbt.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;
                if (nbt.Elevation <= t.Elevation || nbt.Elevation >= 100) continue;
                origin   = coord;
                neighbor = nb;
                break;
            }
        }
        if (origin is null || neighbor is null) return;

        // Submerge the origin tile; the neighbor should gain IsCoastal
        var originTile = world.TileGrid.GetTile(origin.Value);
        float driftNeeded = (originTile.Elevation + 1) / 255f + 0.01f;
        world.CurrentSeaLevel = 0.0f;
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = driftNeeded;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        var nb2 = world.TileGrid.GetTile(neighbor.Value);
        if ((BiomeType)nb2.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) return; // also submerged — seed edge case
        nb2.StaticFlags.HasFlag(TileStaticFlags.IsCoastal).Should().BeTrue(
            "a land tile newly adjacent to ocean (due to sea level rise) should have IsCoastal set");
    }

    [Fact]
    public void VolcanicMultiplier_DecaysTowardOne()
    {
        var world = BuildWorld();
        world.VolcanicActivityMultiplier = 5.0f;
        world.SimConfig.Climate.VolcanicDecayRate = 0.1f;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = 0.0f;
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 0.0f;
        world.SimConfig.Disasters.EarthquakeProbabilityPerTick = 0.0f;

        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        world.VolcanicActivityMultiplier.Should().BeLessThan(5.0f,
            "VolcanicActivityMultiplier above 1.0 should decay toward 1.0 on each annual tick");
        world.VolcanicActivityMultiplier.Should().BeGreaterThan(1.0f,
            "a single decay step should not bring a high multiplier all the way to 1.0");
    }

    [Fact]
    public void VolcanicMultiplier_NeverGoesBelow1()
    {
        var world = BuildWorld();
        world.VolcanicActivityMultiplier = 1.0f;
        world.SimConfig.Climate.VolcanicDecayRate = 1.0f; // maximum decay rate
        world.SimConfig.Climate.AnnualSeaLevelDriftRate = 0.0f;
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 0.0f;
        world.SimConfig.Disasters.EarthquakeProbabilityPerTick = 0.0f;

        var phase = new EnvironmentalPhase(world.SimConfig);
        for (int i = 0; i < 20; i++)
        {
            world.CurrentSeason = Season.Spring;
            phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);
        }

        world.VolcanicActivityMultiplier.Should().BeGreaterThanOrEqualTo(1.0f,
            "VolcanicActivityMultiplier must never decay below 1.0 regardless of decay rate");
    }
}
