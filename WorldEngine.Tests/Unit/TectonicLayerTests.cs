using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class TectonicLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42, int widthKm = 2000, int heightKm = 1500)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = 10 };
        return new WorldGenContext(config, TestSimConfig.Default());
    }

    private static TectonicResult Run(WorldGenContext ctx)
        => new TectonicLayer().Generate(ctx);

    [Fact]
    public void Tectonics_AllTilesHavePlateIdAssigned()
    {
        var ctx = MakeCtx();
        int plateCount = ctx.SimConfig.WorldGen.Tectonics.PlateCount;
        var result = Run(ctx);

        result.PlateId.Should().NotContain(
            (byte)plateCount,
            "no tile should have an unset sentinel PlateId >= plateCount");

        for (int i = 0; i < result.PlateId.Length; i++)
            result.PlateId[i].Should().BeLessThan((byte)plateCount);
    }

    [Fact]
    public void Tectonics_PlateCountMatchesConfig()
    {
        var ctx = MakeCtx();
        int expectedCount = ctx.SimConfig.WorldGen.Tectonics.PlateCount;
        var result = Run(ctx);

        var distinct = result.PlateId.Distinct().Count();
        distinct.Should().Be(expectedCount,
            "every configured plate should have at least one tile assigned to it");
    }

    [Fact]
    public void Tectonics_FaultLinesAtPlateBoundaries()
    {
        var ctx = MakeCtx();
        var result = Run(ctx);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!result.IsFaultLine[ctx.IndexOf(x, y)]) continue;

                byte pid = result.PlateId[ctx.IndexOf(x, y)];
                bool hasDifferentNeighbor =
                    (y > 0     && result.PlateId[ctx.IndexOf(x, y - 1)] != pid) ||
                    (y < h - 1 && result.PlateId[ctx.IndexOf(x, y + 1)] != pid) ||
                    (result.PlateId[ctx.IndexOf((x + 1) % w, y)] != pid) ||
                    (result.PlateId[ctx.IndexOf((x - 1 + w) % w, y)] != pid);

                hasDifferentNeighbor.Should().BeTrue(
                    $"fault-line tile ({x},{y}) must have at least one neighbor with a different plate");
            }
        }
    }

    [Fact]
    public void Tectonics_VolcanicZonesOnlyAtSubduction()
    {
        var ctx = MakeCtx();
        var result = Run(ctx);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (!result.IsVolcanic[idx]) continue;

                // Volcanic tile must be oceanic
                result.IsContinentalTile[idx].Should().BeFalse(
                    $"volcanic tile ({x},{y}) must be on an oceanic plate");

                // Must have at least one continental neighbor on a different plate
                int[] nx = { (x + 1) % w, (x - 1 + w) % w, x, x };
                int[] ny = { y, y, Math.Max(0, y - 1), Math.Min(h - 1, y + 1) };

                bool hasSubduction = false;
                for (int n = 0; n < 4; n++)
                {
                    int nIdx = ctx.IndexOf(nx[n], ny[n]);
                    if (result.PlateId[nIdx] != result.PlateId[idx] && result.IsContinentalTile[nIdx])
                    {
                        hasSubduction = true;
                        break;
                    }
                }

                hasSubduction.Should().BeTrue(
                    $"volcanic tile ({x},{y}) must have a continental neighbor on a different plate");
            }
        }
    }

    [Fact]
    public void Tectonics_ContinentalFractionApproximate()
    {
        var ctx = MakeCtx();
        float expected = ctx.SimConfig.WorldGen.Tectonics.ContinentalPlateFraction;
        var result = Run(ctx);

        float actual = (float)result.IsContinentalTile.Count(b => b) / result.IsContinentalTile.Length;
        actual.Should().BeApproximately(expected, 0.10f,
            "continental tile fraction should be within ±10% of config value");
    }

    [Fact]
    public void Tectonics_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 99999);
        var ctx2 = MakeCtx(seed: 99999);

        var r1 = Run(ctx1);
        var r2 = Run(ctx2);

        r1.PlateId.Should().BeEquivalentTo(r2.PlateId, "same seed must produce identical PlateId");
        r1.IsVolcanic.Should().BeEquivalentTo(r2.IsVolcanic, "same seed must produce identical IsVolcanic");
        r1.IsFaultLine.Should().BeEquivalentTo(r2.IsFaultLine, "same seed must produce identical IsFaultLine");
    }
}
