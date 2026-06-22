using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class ElevationLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42, int widthKm = 2000, int heightKm = 1500)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic = new TectonicLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Elevation_AllValuesInByteRange()
    {
        var ctx = MakeCtx();
        var result = new ElevationLayer().Generate(ctx);

        foreach (byte elevation in result.Elevation)
        {
            elevation.Should().BeLessThanOrEqualTo(255,
                "elevation values must fit in byte range (0-255)");
        }
    }

    [Fact]
    public void Elevation_MountainBoundaryHigherThanPlains()
    {
        var ctx = MakeCtx();
        var result = new ElevationLayer().Generate(ctx);

        int totalFaultElevation = 0, faultCount = 0;
        int totalOtherElevation = 0, otherCount = 0;

        for (int i = 0; i < result.Elevation.Length; i++)
        {
            if (ctx.Tectonic!.IsFaultLine[i]) { totalFaultElevation += result.Elevation[i]; faultCount++; }
            else                              { totalOtherElevation += result.Elevation[i]; otherCount++; }
        }

        float meanFault = faultCount > 0 ? (float)totalFaultElevation / faultCount : 0;
        float meanOther = otherCount > 0 ? (float)totalOtherElevation / otherCount : 0;

        meanFault.Should().BeGreaterThan(meanOther,
            "mean elevation at fault-line tiles should be higher than mean elevation elsewhere");
    }

    [Fact]
    public void Elevation_VolcanicTilesHaveHighElevation()
    {
        var ctx = MakeCtx();
        var result = new ElevationLayer().Generate(ctx);

        int totalVolcanic = 0, volcanicCount = 0;
        int totalOther = 0, otherCount = 0;

        for (int i = 0; i < result.Elevation.Length; i++)
        {
            if (ctx.Tectonic!.IsVolcanic[i]) { totalVolcanic += result.Elevation[i]; volcanicCount++; }
            else                             { totalOther += result.Elevation[i]; otherCount++; }
        }

        float meanVolcanic = volcanicCount > 0 ? (float)totalVolcanic / volcanicCount : 0;
        float meanOther    = otherCount > 0    ? (float)totalOther / otherCount    : 0;

        meanVolcanic.Should().BeGreaterThan(meanOther,
            "mean elevation at volcanic tiles should be higher than mean elevation elsewhere");
    }

    [Fact]
    public void Elevation_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 99999);
        var ctx2 = MakeCtx(seed: 99999);

        var r1 = new ElevationLayer().Generate(ctx1);
        var r2 = new ElevationLayer().Generate(ctx2);

        r1.Elevation.Should().BeEquivalentTo(r2.Elevation,
            "same seed must produce byte-identical elevation arrays");
    }
}

public class OceanLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42, int widthKm = 2000, int heightKm = 1500)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Ocean_LandFractionMatchesConfig()
    {
        var ctx = MakeCtx();
        float expectedSeaLevel    = ctx.SimConfig.WorldGen.Ocean.DefaultSeaLevel;
        float expectedLandFraction = 1.0f - expectedSeaLevel;

        var result = new OceanLayer().Generate(ctx);

        int oceanTiles = result.IsOcean.Count(o => o);
        float actualLandFraction = 1.0f - (float)oceanTiles / result.IsOcean.Length;

        actualLandFraction.Should().BeApproximately(expectedLandFraction, 0.05f,
            "land fraction should be within ±5% of (1 - DefaultSeaLevel)");
    }

    [Fact]
    public void Ocean_CoastalFlagOnLandAdjacentToOcean()
    {
        var ctx = MakeCtx();
        var result = new OceanLayer().Generate(ctx);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (!result.IsCoastal[idx]) continue;

                result.IsOcean[idx].Should().BeFalse($"coastal tile ({x},{y}) must be land");

                int[] nx = { (x + 1) % w, (x - 1 + w) % w, x, x };
                int[] ny = { y, y, Math.Max(0, y - 1), Math.Min(h - 1, y + 1) };

                bool hasOcean = Enumerable.Range(0, 4).Any(n => result.IsOcean[ctx.IndexOf(nx[n], ny[n])]);
                hasOcean.Should().BeTrue($"coastal tile ({x},{y}) must have an ocean 4-neighbor");
            }
        }
    }

    [Fact]
    public void Ocean_NoCoastalFlagOnInteriorLand()
    {
        var ctx = MakeCtx();
        var result = new OceanLayer().Generate(ctx);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (result.IsOcean[idx] || result.IsCoastal[idx]) continue;

                int[] nx = { (x + 1) % w, (x - 1 + w) % w, x, x };
                int[] ny = { y, y, Math.Max(0, y - 1), Math.Min(h - 1, y + 1) };

                for (int n = 0; n < 4; n++)
                    result.IsOcean[ctx.IndexOf(nx[n], ny[n])].Should().BeFalse(
                        $"interior land tile ({x},{y}) should have no ocean neighbors");
            }
        }
    }

    [Fact]
    public void Ocean_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 99999);
        var ctx2 = MakeCtx(seed: 99999);

        var r1 = new OceanLayer().Generate(ctx1);
        var r2 = new OceanLayer().Generate(ctx2);

        r1.IsOcean.Should().BeEquivalentTo(r2.IsOcean, "same seed → identical IsOcean");
        r1.IsCoastal.Should().BeEquivalentTo(r2.IsCoastal, "same seed → identical IsCoastal");
    }
}
