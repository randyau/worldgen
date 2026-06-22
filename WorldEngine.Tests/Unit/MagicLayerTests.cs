using WorldEngine.Sim.Core;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class MagicLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = 2000, HeightKm = 1500, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic = new TectonicLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Magic_AllValuesInByteRange()
    {
        var ctx = MakeCtx();
        var result = new MagicLayer().Generate(ctx);

        // Byte values are always 0-255 by type, but assert the semantic intent
        result.MagicIntensity.Should().AllSatisfy(v =>
            v.Should().BeInRange(0, 255));
    }

    [Fact]
    public void Magic_VolcanicZonesStatisticallyHigherMagic()
    {
        var ctx = MakeCtx();
        var result = new MagicLayer().Generate(ctx);

        var volcanicMagic = ctx.Tectonic!.IsVolcanic
            .Select((isV, i) => (isV, i))
            .Where(t => t.isV)
            .Select(t => (float)result.MagicIntensity[t.i])
            .ToList();

        var globalMean = result.MagicIntensity.Average(v => (float)v);

        volcanicMagic.Should().NotBeEmpty("world should have volcanic tiles");
        var volcanicMean = volcanicMagic.Average();

        volcanicMean.Should().BeGreaterThan(globalMean,
            "volcanic zones should have statistically higher magic intensity than the global mean");
    }

    [Fact]
    public void Magic_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 77777);
        var ctx2 = MakeCtx(seed: 77777);

        var r1 = new MagicLayer().Generate(ctx1);
        var r2 = new MagicLayer().Generate(ctx2);

        r1.MagicIntensity.Should().BeEquivalentTo(r2.MagicIntensity,
            "same seed must produce identical MagicIntensity");
    }
}
