namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates magic intensity using Simplex noise with a volcanic zone weighting.
/// Volcanic tiles get ×2 multiplier. High-magic tiles near volcanic zones are
/// flagged as IsPOICandidate.
/// M1: generates and stores data only — no behavioral effects until M2+.
/// </summary>
public sealed class MagicLayer : IWorldGenLayer<MagicResult>
{
    // V2: magic physical substrate — behaviors driven by magic intensity not implemented until M2+

    public MagicResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var tec  = ctx.Tectonic!;
        int seed = ctx.Config.Seed ^ LayerSeeds.Magic;
        int w = ctx.TileWidth, h = ctx.TileHeight;
        float scale = ctx.SimConfig.WorldGen.MagicIntensityScale;

        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFrequency(0.008f);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(4);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);

        float[] raw = new float[ctx.TileCount];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                float n = (noise.GetNoise(x, y) + 1f) * 0.5f; // map [-1,1] → [0,1]
                float multiplier = tec.IsVolcanic[idx] ? 2.0f * scale : scale;
                raw[idx] = Math.Min(1.0f, n * multiplier);
            }
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.8f);

        var result = new MagicResult(ctx.TileCount);

        // Determine POI threshold: tiles in top 5% of magic intensity near volcanic zones
        float poiThreshold = 0.85f;

        for (int i = 0; i < ctx.TileCount; i++)
        {
            result.MagicIntensity[i] = (byte)Math.Clamp((int)(raw[i] * 255f), 0, 255);
            if (tec.IsVolcanic[i] && raw[i] >= poiThreshold)
                result.IsPOICandidate[i] = true;
        }

        progress?.Report(1.0f);
        return result;
    }
}
