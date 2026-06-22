using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class WorldStateTests
{
    private static WorldState BuildMinimalWorld(int seed = 1)
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
    public void WorldState_ImplementsIWorldStateReadOnly()
    {
        typeof(WorldState).Should().Implement<IWorldStateReadOnly>(
            "WorldState must satisfy the IWorldStateReadOnly contract for entity decision-making in M2+");
    }

    [Fact]
    public void WorldState_InitialSeasonIsSpring()
    {
        var world = BuildMinimalWorld();
        world.CurrentSeason.Should().Be(Season.Spring, "initial season must be Spring");
    }

    [Fact]
    public void WorldState_InitialYearIsOne()
    {
        var world = BuildMinimalWorld();
        world.CurrentYear.Should().Be(1, "initial year must be 1 (Year 1 of history)");
    }

    [Fact]
    public void WorldState_GetTileWrapsX()
    {
        var world = BuildMinimalWorld();
        int w = world.TileGrid.TileWidth;

        var coord     = new TileCoord(0, 0);
        var wrapCoord = new TileCoord(w, 0); // one full wrap around

        var tile1 = world.GetTile(coord);
        var tile2 = world.GetTile(wrapCoord);

        tile1.Should().BeEquivalentTo(tile2,
            "GetTile(w,0) should wrap to GetTile(0,0) on a cylinder world");
    }

    [Fact]
    public void WorldState_DroughtParametersDefaultToGenesis()
    {
        var world = BuildMinimalWorld();

        world.GlobalTemperatureAnomaly.Should().Be(0f, "no anomaly at genesis");
        world.GlobalPrecipitationMultiplier.Should().Be(1.0f, "precipitation multiplier starts at 1.0");
        world.VolcanicActivityMultiplier.Should().Be(1.0f, "volcanic multiplier starts at 1.0");
        world.StormCorridorHalfWidth.Should().BeGreaterThan(0f, "storm corridor must have a width");
    }
}
