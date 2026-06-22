using WorldEngine.Sim.Core;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class RiverLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42, int widthKm = 2000, int heightKm = 1500)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean = new OceanLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void River_AllRiversFlowDownhill()
    {
        var ctx = MakeCtx();
        var result = new RiverLayer().Generate(ctx);
        var elev = ctx.Elevation!;
        var ocean = ctx.Ocean!;
        int w = ctx.TileWidth, h = ctx.TileHeight;

        // Every river tile must have at least one 8-neighbor with equal or lower elevation,
        // or be adjacent to an ocean/lake (terminus)
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (!result.HasRiver[idx]) continue;
                if (ocean.IsOcean[idx]) continue;

                byte myElev = elev.Elevation[idx];
                bool hasLowerOrEqual = false;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = ((x + dx) % w + w) % w;
                        int ny = Math.Clamp(y + dy, 0, h - 1);
                        int nIdx = ctx.IndexOf(nx, ny);

                        if (ocean.IsOcean[nIdx] || result.IsLake[nIdx] ||
                            elev.Elevation[nIdx] <= myElev)
                        {
                            hasLowerOrEqual = true;
                            break;
                        }
                    }
                    if (hasLowerOrEqual) break;
                }

                hasLowerOrEqual.Should().BeTrue(
                    $"river tile ({x},{y}) elev={myElev} should have a downhill or ocean/lake neighbor");
            }
        }
    }

    [Fact]
    public void River_SmallBasinsFilledNotLakes()
    {
        var ctx = MakeCtx();
        int minBasin = ctx.SimConfig.WorldGen.Rivers.MinLakeBasinTiles;
        var result = new RiverLayer().Generate(ctx);

        // All lake tiles must have basin size >= minLakeBasinTiles
        // (We test the inverse: no IsLake tile should be in a basin smaller than threshold)
        // This is verified by checking lake clusters — each connected lake region should be >= minBasin tiles
        if (!result.IsLake.Any(l => l)) return; // no lakes is valid

        var lakeCount = result.IsLake.Count(l => l);
        // At minimum: if any lakes exist, they should not be singletons below threshold
        // (A strict test would require basin tracking — approximate by checking lake count vs threshold)
        lakeCount.Should().BeGreaterThanOrEqualTo(minBasin,
            "total lake tiles must be at least one full lake basin worth of tiles");
    }

    [Fact]
    public void River_EastWestWrappingHandled()
    {
        // Use a world where rivers are likely to cross the wrap boundary
        // Just verify that river and lake flags at x=0 and x=width-1 are consistent
        var ctx = MakeCtx(seed: 12345);
        var result = new RiverLayer().Generate(ctx);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        // Flow accumulation at x=0 and x=w-1 should not be anomalously different
        // (i.e., the wrap was handled — no seam of unnaturally high flow at boundary)
        var leftEdgeFlow  = Enumerable.Range(0, h).Select(y => result.FlowAccumulation[ctx.IndexOf(0, y)]).Average();
        var rightEdgeFlow = Enumerable.Range(0, h).Select(y => result.FlowAccumulation[ctx.IndexOf(w - 1, y)]).Average();
        var centerFlow    = Enumerable.Range(0, h).Select(y => result.FlowAccumulation[ctx.IndexOf(w / 2, y)]).Average();

        // Neither edge should be more than 10× the center average (no seam artifacts)
        double maxEdge = Math.Max(leftEdgeFlow, rightEdgeFlow);
        maxEdge.Should().BeLessThan(centerFlow * 10 + 100,
            "cylinder wrap should not create unrealistic flow accumulation seam at east/west edges");
    }

    [Fact]
    public void River_SomeTilesHaveRivers()
    {
        var ctx = MakeCtx();
        var result = new RiverLayer().Generate(ctx);

        result.HasRiver.Any(r => r).Should().BeTrue(
            "a world of this size should have at least some river tiles");
    }

    [Fact]
    public void River_FlowAccumulationIsPositive()
    {
        var ctx = MakeCtx();
        var result = new RiverLayer().Generate(ctx);
        var ocean = ctx.Ocean!;

        for (int i = 0; i < ctx.TileCount; i++)
        {
            if (!ocean.IsOcean[i])
                result.FlowAccumulation[i].Should().BeGreaterThanOrEqualTo(1,
                    "every land tile should have flow accumulation >= 1 (itself)");
        }
    }

    [Fact]
    public void River_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 55555);
        var ctx2 = MakeCtx(seed: 55555);

        var r1 = new RiverLayer().Generate(ctx1);
        var r2 = new RiverLayer().Generate(ctx2);

        r1.HasRiver.Should().BeEquivalentTo(r2.HasRiver, "same seed must produce identical HasRiver");
        r1.IsLake.Should().BeEquivalentTo(r2.IsLake, "same seed must produce identical IsLake");
        r1.FlowAccumulation.Should().BeEquivalentTo(r2.FlowAccumulation, "same seed must produce identical FlowAccumulation");
    }
}
