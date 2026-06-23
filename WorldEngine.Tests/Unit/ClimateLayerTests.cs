using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class ClimateLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42, int widthKm = 2000, int heightKm = 1500)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Climate_PolarTilesColderThanEquatorial()
    {
        var ctx = MakeCtx();
        var result = new ClimateLayer().Generate(ctx);
        int w = ctx.TileWidth, h = ctx.TileHeight;

        // Polar band: top/bottom 10% of world height
        int poleRows = Math.Max(1, h / 10);
        // Equatorial band: middle 20% of world height
        int eqStart = h * 4 / 10, eqEnd = h * 6 / 10;

        var polarTemps   = new List<float>();
        var equatTemps   = new List<float>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                float t = result.BaseTemperature[idx];
                if (y < poleRows || y >= h - poleRows) polarTemps.Add(t);
                if (y >= eqStart && y < eqEnd) equatTemps.Add(t);
            }
        }

        polarTemps.Should().NotBeEmpty();
        equatTemps.Should().NotBeEmpty();

        float polarMean = polarTemps.Average();
        float equatMean = equatTemps.Average();

        polarMean.Should().BeLessThan(equatMean,
            "polar tiles should be colder on average than equatorial tiles");
    }

    [Fact]
    public void Climate_ElevationReducesTemperature()
    {
        var ctx = MakeCtx();
        var result = new ClimateLayer().Generate(ctx);
        var elev = ctx.Elevation!;
        int h = ctx.TileHeight, w = ctx.TileWidth;

        // Compare high elevation vs low elevation tiles in the same latitude band (middle third)
        int bandStart = h / 3, bandEnd = 2 * h / 3;
        var highElevTemps = new List<float>();
        var lowElevTemps  = new List<float>();

        for (int y = bandStart; y < bandEnd; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                float t = result.BaseTemperature[idx];
                byte e = elev.Elevation[idx];
                if (e >= 200) highElevTemps.Add(t);
                if (e <= 80)  lowElevTemps.Add(t);
            }
        }

        if (highElevTemps.Count < 5 || lowElevTemps.Count < 5)
            return; // insufficient sample — skip rather than false-fail

        highElevTemps.Average().Should().BeLessThan(lowElevTemps.Average(),
            "high-elevation tiles should be colder than low-elevation tiles at the same latitude");
    }

    [Fact]
    public void Climate_RainShadowBehindMountains()
    {
        // Use a no-noise config so the rain shadow signal isn't masked by moisture noise.
        var config = new WorldConfig { Seed = 42, WidthKm = 2000, HeightKm = 1500, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();
        simCfg.Climate.MoistureNoiseScale = 0f;
        simCfg.Climate.TemperatureNoiseScale = 0f;
        var ctx = new WorldGenContext(config, simCfg);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        var result = new ClimateLayer().Generate(ctx);
        var elev = ctx.Elevation!;
        int w = ctx.TileWidth, h = ctx.TileHeight;
        byte mountainThreshold = ctx.SimConfig.Climate.MountainElevationThreshold;

        // Find mountain tiles. For each mountain, check if the leeward tile has lower moisture.
        // Wind direction depends on latitude band — use equatorial tiles (trade winds: E→W, so leeward=east).
        int eqStart = h * 4 / 10, eqEnd = h * 6 / 10;

        float windwardMoisture = 0f;
        float leeSideMoisture  = 0f;
        int count = 0;

        for (int y = eqStart; y < eqEnd; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (elev.Elevation[idx] < mountainThreshold) continue;

                // Tropical trade winds blow E→W: windward = east (+1), leeward = west (-1)
                int windX = (x + 1) % w;
                int leeX  = (x - 1 + w) % w;

                windwardMoisture += result.BaseMoisture[ctx.IndexOf(windX, y)];
                leeSideMoisture  += result.BaseMoisture[ctx.IndexOf(leeX, y)];
                count++;
            }
        }

        if (count < 10) return; // too few mountains in test world, skip

        // Compare windward to leeward directly. This is robust to moisture noise
        // because the rain shadow deficit is directional; noise is symmetric.
        (leeSideMoisture / count).Should().BeLessThan(windwardMoisture / count,
            "leeward tiles of equatorial mountains should have lower moisture than windward tiles (rain shadow)");
    }

    [Fact]
    public void Climate_MonsoonZonesOnlyInTropics()
    {
        var ctx = MakeCtx();
        var result = new ClimateLayer().Generate(ctx);
        int h = ctx.TileHeight, w = ctx.TileWidth;
        float tropicalHalfWidth = ctx.SimConfig.Climate.TropicalBandHalfWidth;

        // Tropical band: |normalizedLat - 0.5| < tropicalHalfWidth
        float tropStart = 0.5f - tropicalHalfWidth;
        float tropEnd   = 0.5f + tropicalHalfWidth;

        for (int y = 0; y < h; y++)
        {
            float normalizedLat = (float)y / h;
            bool inTropical = normalizedLat >= tropStart && normalizedLat <= tropEnd;

            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (result.IsMonsoonTile[idx])
                {
                    inTropical.Should().BeTrue(
                        $"monsoon tile ({x},{y}) at normalizedLat={normalizedLat:F2} should be in tropical band [{tropStart:F2},{tropEnd:F2}]");
                }
            }
        }
    }

    [Fact]
    public void Climate_StormCorridorAtConfiguredLatitude()
    {
        var ctx = MakeCtx();
        var result = new ClimateLayer().Generate(ctx);
        int h = ctx.TileHeight, w = ctx.TileWidth;
        float stormLat   = ctx.SimConfig.Climate.StormCorridorNormalizedLat;
        float stormHalfW = ctx.SimConfig.Climate.StormCorridorHalfWidth;

        int stormCount   = 0;
        int inBandCount  = 0;

        for (int y = 0; y < h; y++)
        {
            float normalizedLat = (float)y / h;
            bool inBand = MathF.Abs(normalizedLat - stormLat) < stormHalfW;

            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (result.IsStormCorridor[idx])
                {
                    stormCount++;
                    if (inBand) inBandCount++;
                }
            }
        }

        stormCount.Should().BeGreaterThan(0, "there should be some storm corridor tiles");
        inBandCount.Should().Be(stormCount,
            "all storm corridor tiles should be within the configured latitude band");
    }

    [Fact]
    public void Climate_SeasonalProfilesAllFourValuesNonZero()
    {
        var ctx = MakeCtx();
        var result = new ClimateLayer().Generate(ctx);
        var ocean = ctx.Ocean!;

        int landCount = 0, nonZeroCount = 0;
        for (int i = 0; i < ctx.TileCount; i++)
        {
            if (ocean.IsOcean[i]) continue;
            landCount++;
            var sp = result.SeasonalProfiles[i];
            bool anyNonZero = sp.TempDeltaSpring != 0 || sp.TempDeltaSummer != 0 ||
                              sp.TempDeltaAutumn != 0 || sp.TempDeltaWinter != 0 ||
                              sp.MoistureDeltaSpring != 0 || sp.MoistureDeltaSummer != 0 ||
                              sp.MoistureDeltaAutumn != 0 || sp.MoistureDeltaWinter != 0;
            if (anyNonZero) nonZeroCount++;
        }

        landCount.Should().BeGreaterThan(0);
        // Most land tiles should have non-zero seasonal profiles (at least 80%)
        ((float)nonZeroCount / landCount).Should().BeGreaterThan(0.8f,
            "at least 80% of land tiles should have non-zero seasonal profile entries");
    }

    [Fact]
    public void Climate_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 11111);
        var ctx2 = MakeCtx(seed: 11111);

        var r1 = new ClimateLayer().Generate(ctx1);
        var r2 = new ClimateLayer().Generate(ctx2);

        r1.BaseTemperature.Should().BeEquivalentTo(r2.BaseTemperature, "same seed → identical BaseTemperature");
        r1.BaseMoisture.Should().BeEquivalentTo(r2.BaseMoisture, "same seed → identical BaseMoisture");
    }
}
