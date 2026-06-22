using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class ResourceDynamicsTests
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
    public void Resource_FertilityRecovery()
    {
        var world = BuildWorld();
        world.SimConfig.WorldGen.Resources.FertilityRecoveryPerYear = 10;
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a land tile with fertility < 200 and manually lower it
        TileCoord? target = null;
        for (int y = 0; y < h && target is null; y++)
        for (int x = 0; x < w && target is null; x++)
        {
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            if ((BiomeType)t.BiomeType is not BiomeType.Ocean and not BiomeType.CoastalWater
                && t.Fertility < 190)
                target = new TileCoord(x, y);
        }
        if (target is null) return;

        var tile = world.TileGrid.GetTile(target.Value);
        byte before = tile.Fertility;
        tile.Fertility = (byte)Math.Max(0, before - 50);
        world.TileGrid.SetTile(target.Value, tile);

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        var after = world.TileGrid.GetTile(target.Value);
        after.Fertility.Should().BeGreaterThan(tile.Fertility,
            "fertility should recover by FertilityRecoveryPerYear on annual tick");
    }

    [Fact]
    public void Resource_DroughtReducesFertility()
    {
        var world = BuildWorld();
        world.SimConfig.WorldGen.Resources.DroughtFertilityPenaltyPerSeason = 20;
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a land tile to target
        TileCoord? target = null;
        BiomeType targetBiome = BiomeType.Grassland;
        int targetBand = -1;
        for (int y = 0; y < h && target is null; y++)
        for (int x = 0; x < w && target is null; x++)
        {
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            var biome = (BiomeType)t.BiomeType;
            if (biome is not BiomeType.Ocean and not BiomeType.CoastalWater && t.Fertility > 20)
            {
                targetBiome = biome;
                targetBand  = y / Math.Max(1, h / 4);
                target      = new TileCoord(x, y);
            }
        }
        if (target is null) return;

        // Inject a drought for this band+biome
        world.ActiveDroughts.Add(new ActiveDrought(targetBand, targetBiome, 1.0f, 3, new EventId(0)));

        byte before = world.TileGrid.GetTile(target.Value).Fertility;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        byte after = world.TileGrid.GetTile(target.Value).Fertility;
        after.Should().BeLessThan(before,
            "a tile in an active drought zone should have its fertility reduced each annual tick");
    }

    [Fact]
    public void Resource_OceanTilesNotAffected()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        var oceanBefore = new Dictionary<TileCoord, byte>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var coord = new TileCoord(x, y);
            var t = world.TileGrid.GetTile(coord);
            if ((BiomeType)t.BiomeType is BiomeType.Ocean)
                oceanBefore[coord] = t.Fertility;
        }

        if (!oceanBefore.Any()) return;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        foreach (var (coord, fertBefore) in oceanBefore)
        {
            var t = world.TileGrid.GetTile(coord);
            t.Fertility.Should().Be(fertBefore,
                $"ocean tile at {coord} should not have its Fertility modified by ResourceDynamics");
        }
    }

    [Fact]
    public void Resource_FertilityClampedTo255()
    {
        var world = BuildWorld();
        world.SimConfig.WorldGen.Resources.FertilityRecoveryPerYear = 255;
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        var phase = new EnvironmentalPhase(world.SimConfig);
        world.CurrentSeason = Season.Spring;
        phase.RunTick(world, new List<PendingEvent>(), isAnnualTick: true);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            t.Fertility.Should().BeLessThanOrEqualTo(255,
                $"Fertility at ({x},{y}) must never exceed 255 even with extreme recovery rate");
        }
    }
}
