using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Classifies each tile's biome using BiomeClassifier and computes Fertility
/// from biome and climate inputs.
/// </summary>
public sealed class BiomeLayer : IWorldGenLayer<BiomeResult>
{
    // Approximate fertility by biome type (0-255)
    private static readonly Dictionary<BiomeType, byte> BaseFertility = new()
    {
        [BiomeType.Ocean]             = 0,
        [BiomeType.CoastalWater]      = 0,
        [BiomeType.Beach]             = 30,
        [BiomeType.Tundra]            = 10,
        [BiomeType.BorealForest]      = 60,
        [BiomeType.TemperateForest]   = 180,
        [BiomeType.TropicalRainforest] = 220,
        [BiomeType.Grassland]         = 160,
        [BiomeType.Savanna]           = 100,
        [BiomeType.Desert]            = 5,
        [BiomeType.Swamp]             = 140,
        [BiomeType.HighMountain]      = 0,
        [BiomeType.Mountain]          = 20,
        [BiomeType.Hills]             = 80,
        [BiomeType.Plains]            = 150,
        [BiomeType.Volcanic]          = 40,
    };

    public BiomeResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var tec     = ctx.Tectonic!;
        var elev    = ctx.Elevation!;
        var ocean   = ctx.Ocean!;
        var climate = ctx.Climate!;
        var cfg     = ctx.SimConfig;
        int n = ctx.TileCount;

        var result = new BiomeResult(n);

        for (int i = 0; i < n; i++)
        {
            if (ocean.IsOcean[i])
            {
                result.Biomes[i]   = BiomeType.Ocean;
                result.Fertility[i] = 0;
                continue;
            }

            // Build flags from prior layer results
            TileStaticFlags flags = TileStaticFlags.None;
            if (tec.IsVolcanic[i])     flags |= TileStaticFlags.IsVolcanic;
            if (tec.IsFaultLine[i])    flags |= TileStaticFlags.IsFaultLine;
            if (ocean.IsCoastal[i])    flags |= TileStaticFlags.IsCoastal;
            if (climate.IsStormCorridor[i]) flags |= TileStaticFlags.IsStormCorridor;
            // River/lake flags are set from RiverResult if it's available
            if (ctx.River is { } river)
            {
                if (river.IsLake[i])    flags |= TileStaticFlags.IsLake;
                if (river.HasRiver[i])  flags |= TileStaticFlags.HasRiver;
            }

            var biome = BiomeClassifier.Classify(
                climate.BaseTemperature[i],
                climate.BaseMoisture[i],
                elev.Elevation[i],
                flags,
                cfg);

            result.Biomes[i] = biome;

            // Fertility = base + moisture bonus
            float moistureBonus = climate.BaseMoisture[i] / 255f * 30f;
            byte baseFert = BaseFertility.TryGetValue(biome, out byte bv) ? bv : (byte)50;
            result.Fertility[i] = (byte)Math.Clamp(baseFert + moistureBonus, 0, 255);
        }

        if (n > 0)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(1.0f);
        }

        return result;
    }
}
