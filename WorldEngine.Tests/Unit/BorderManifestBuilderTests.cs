using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class BorderManifestBuilderTests
{
    private static WorldGenContext MakeCtx(int seed = 42, int widthKm = 1000, int heightKm = 800)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean = new OceanLayer().Generate(ctx);
        ctx.River = new RiverLayer().Generate(ctx);
        ctx.Climate = new ClimateLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Build_ReturnsOneManifestPerTile()
    {
        var ctx = MakeCtx();
        var manifests = BorderManifestBuilder.Build(ctx);

        manifests.Should().HaveCount(ctx.TileCount);
        manifests.Select(m => m.Coord).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_AdjacentTilesAgreeOnSharedEdgeElevation()
    {
        var ctx = MakeCtx();
        var manifests = BorderManifestBuilder.Build(ctx).ToDictionary(m => m.Coord, m => m.Manifest);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var here  = new TileCoord(x, y);
                var south = here.South();

                var hereSouthEdge = manifests[here].South;
                var southNorthEdge = manifests[south].North;

                for (int k = 0; k < BorderManifest.SampleCount; k++)
                {
                    hereSouthEdge[k].Elevation.Should().Be(southNorthEdge[k].Elevation,
                        $"tile ({x},{y})'s South edge and tile ({x},{y + 1})'s North edge are the same physical boundary");
                }
            }
        }
    }

    [Fact]
    public void Build_RiverCrossingsAppearOnBothSidesOfTheBoundary()
    {
        var ctx = MakeCtx();
        var river = ctx.River!;
        river.Crossings.Should().NotBeEmpty();

        var manifests = BorderManifestBuilder.Build(ctx).ToDictionary(m => m.Coord, m => m.Manifest);

        foreach (var crossing in river.Crossings)
        {
            var fromEdge = manifests[crossing.FromTile].GetEdge(crossing.Edge);
            var toEdge   = manifests[crossing.ToTile].GetEdge(crossing.Edge.Opposite());

            int center = (int)(crossing.Position * BorderManifest.SampleCount);
            center = Math.Clamp(center, 0, BorderManifest.SampleCount - 1);

            fromEdge[center].HasRiverCrossing.Should().Be((byte)1,
                "the source tile's manifest must record the crossing on its own edge");
            toEdge[center].HasRiverCrossing.Should().Be((byte)1,
                "the destination tile's manifest must record the same crossing on the opposite edge");
        }
    }
}
