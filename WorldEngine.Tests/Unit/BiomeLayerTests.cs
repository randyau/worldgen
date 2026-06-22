using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class BiomeLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = 2000, HeightKm = 1500, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Biome_HighMountainForHighElevation()
    {
        var ctx = MakeCtx();
        var result = new BiomeLayer().Generate(ctx);
        byte threshold = ctx.SimConfig.WorldGen.BiomeThresholds.HighMountainElevation;

        for (int i = 0; i < ctx.TileCount; i++)
        {
            if (ctx.Ocean!.IsOcean[i]) continue;
            if (ctx.Elevation!.Elevation[i] >= threshold)
            {
                result.Biomes[i].Should().Be(BiomeType.HighMountain,
                    $"tile {i} with elevation {ctx.Elevation.Elevation[i]} >= threshold {threshold} should be HighMountain");
            }
        }
    }

    [Fact]
    public void Biome_VolcanicOverridesClimate()
    {
        var ctx = MakeCtx();
        var result = new BiomeLayer().Generate(ctx);
        byte mountainThresh = ctx.SimConfig.WorldGen.BiomeThresholds.MountainElevation;

        for (int i = 0; i < ctx.TileCount; i++)
        {
            if (!ctx.Tectonic!.IsVolcanic[i]) continue;
            if (ctx.Ocean!.IsOcean[i]) continue;
            // Mountain and HighMountain priority ranks above Volcanic
            if (ctx.Elevation!.Elevation[i] >= mountainThresh) continue;

            result.Biomes[i].Should().Be(BiomeType.Volcanic,
                $"volcanic tile {i} (non-ocean, elevation {ctx.Elevation.Elevation[i]} < mountain threshold) should have Volcanic biome");
        }
    }

    [Fact]
    public void Biome_OceanTilesAreOceanBiome()
    {
        var ctx = MakeCtx();
        var result = new BiomeLayer().Generate(ctx);

        for (int i = 0; i < ctx.TileCount; i++)
        {
            if (ctx.Ocean!.IsOcean[i])
                result.Biomes[i].Should().Be(BiomeType.Ocean,
                    $"ocean tile {i} should have BiomeType.Ocean");
        }
    }

    [Fact]
    public void Biome_ThresholdsAffectClassification()
    {
        // Verify that changing config thresholds changes the output meaningfully.
        var ctx1 = MakeCtx(seed: 42);
        var r1 = new BiomeLayer().Generate(ctx1);
        int desertDefault = r1.Biomes.Count(b => b == BiomeType.Desert);

        var ctx2 = MakeCtx(seed: 42);
        // Lower AridMoisture so more tiles qualify as "arid" → more Desert
        ctx2.SimConfig.WorldGen.BiomeThresholds.AridMoisture = 200;
        var r2 = new BiomeLayer().Generate(ctx2);
        int desertExtreme = r2.Biomes.Count(b => b == BiomeType.Desert);

        desertExtreme.Should().BeGreaterThan(desertDefault,
            "raising the AridMoisture threshold to 200 should classify more tiles as Desert");
    }

    [Fact]
    public void Biome_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 33333);
        var ctx2 = MakeCtx(seed: 33333);

        var r1 = new BiomeLayer().Generate(ctx1);
        var r2 = new BiomeLayer().Generate(ctx2);

        r1.Biomes.Should().BeEquivalentTo(r2.Biomes, "same seed → identical biome array");
    }
}

public class BiomeClassifierTests
{
    private static SimConfig HotDryConfig()
    {
        var cfg = TestSimConfig.Default();
        cfg.WorldGen.BiomeThresholds.HotTemperature    = 100;
        cfg.WorldGen.BiomeThresholds.ColdTemperature   = 50;
        cfg.WorldGen.BiomeThresholds.PolarTemperature  = 20;
        cfg.WorldGen.BiomeThresholds.WetMoisture       = 180;
        cfg.WorldGen.BiomeThresholds.DryMoisture       = 80;
        cfg.WorldGen.BiomeThresholds.AridMoisture      = 30;
        cfg.WorldGen.BiomeThresholds.HighMountainElevation = 220;
        cfg.WorldGen.BiomeThresholds.MountainElevation = 180;
        cfg.WorldGen.BiomeThresholds.HillsElevation    = 140;
        return cfg;
    }

    [Fact]
    public void BiomeClassifier_DesertForHotDryInput()
    {
        var cfg    = HotDryConfig();
        var biome  = BiomeClassifier.Classify(200, 20, 50, TileStaticFlags.None, cfg);
        biome.Should().Be(BiomeType.Desert, "hot+arid input should produce Desert");
    }

    [Fact]
    public void BiomeClassifier_TropicalRainforestForHotWet()
    {
        var cfg   = HotDryConfig();
        var biome = BiomeClassifier.Classify(200, 230, 50, TileStaticFlags.None, cfg);
        biome.Should().Be(BiomeType.TropicalRainforest, "hot+wet input should produce TropicalRainforest");
    }

    [Fact]
    public void BiomeClassifier_TundraForColdInput()
    {
        var cfg   = HotDryConfig();
        var biome = BiomeClassifier.Classify(15, 100, 50, TileStaticFlags.None, cfg);
        biome.Should().Be(BiomeType.Tundra, "polar-cold input should produce Tundra regardless of moisture");
    }
}

public class ResourceLayerTests
{
    private static WorldGenContext MakeCtx(int seed = 42)
    {
        var config = new WorldConfig { Seed = seed, WidthKm = 2000, HeightKm = 1500, TileWidthKm = 10 };
        var ctx = new WorldGenContext(config, TestSimConfig.Default());
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        return ctx;
    }

    [Fact]
    public void Resource_DepositAtVolcanicTiles()
    {
        var ctx = MakeCtx();
        var result = new ResourceLayer().Generate(ctx);

        var volcanicDeposits = Enumerable.Range(0, ctx.TileCount)
            .Where(i => ctx.Tectonic!.IsVolcanic[i] && !ctx.Ocean!.IsOcean[i])
            .Select(i => {
                int x = i % ctx.TileWidth, y = i / ctx.TileWidth;
                return new TileCoord(x, y);
            })
            .Count(coord => result.Deposits.ContainsKey(coord));

        volcanicDeposits.Should().BeGreaterThan(0,
            "at least some volcanic tiles should have resource deposits");
    }

    [Fact]
    public void Resource_HasDepositFlagWhenRegistryEntry()
    {
        var ctx = MakeCtx();
        var result = new ResourceLayer().Generate(ctx);

        // Every coord in the registry should have a deposit
        foreach (var (coord, deposits) in result.Deposits)
            deposits.Should().NotBeEmpty($"registry entry at {coord} should have at least one deposit");
    }

    [Fact]
    public void Resource_SameSeedSameResult()
    {
        var ctx1 = MakeCtx(seed: 44444);
        var ctx2 = MakeCtx(seed: 44444);

        var r1 = new ResourceLayer().Generate(ctx1);
        var r2 = new ResourceLayer().Generate(ctx2);

        r1.Deposits.Keys.Should().BeEquivalentTo(r2.Deposits.Keys,
            "same seed should produce deposits at identical coordinates");
    }
}
