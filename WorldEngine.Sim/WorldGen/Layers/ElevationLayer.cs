namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates per-tile elevation using FastNoiseLite Simplex noise combined with
/// tectonic contributions: mountain ridges at continental collisions, trenches at
/// subduction zones, and a continental highland bias.
/// Output normalized to byte range 0–255.
/// </summary>
public sealed class ElevationLayer : IWorldGenLayer<ElevationResult>
{
    public ElevationResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var tec  = ctx.Tectonic!;
        var cfg  = ctx.SimConfig.WorldGen.Elevation;
        int seed = ctx.Config.Seed ^ LayerSeeds.Elevation;
        int w = ctx.TileWidth, h = ctx.TileHeight;

        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFrequency(cfg.NoiseScale * 0.01f);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(5);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);

        float[] raw = new float[ctx.TileCount];
        float ti = cfg.TectonicIntensity;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);

                // Base simplex noise in [-1, 1]
                float n = noise.GetNoise(x, y);

                // Tectonic contributions
                float tectonic = 0f;

                if (tec.IsFaultLine[idx])
                {
                    if (tec.IsContinentalTile[idx])
                    {
                        // Continental fault → mountain ridge
                        tectonic = 0.6f * ti;
                    }
                    else if (tec.IsVolcanic[idx])
                    {
                        // Volcanic/subduction → trench on the oceanic side, but also volcanic peaks
                        // DECISION: volcanic tiles get a large positive boost (volcanic mountains)
                        tectonic = 0.5f * ti;
                    }
                    else
                    {
                        // Oceanic non-volcanic fault → slight trench
                        tectonic = -0.3f * ti;
                    }
                }
                else if (tec.IsContinentalTile[idx])
                {
                    // Continental interior → highland bias
                    tectonic = 0.15f * ti;
                }
                else
                {
                    // Oceanic interior → slight basin
                    tectonic = -0.10f * ti;
                }

                raw[idx] = n + tectonic;
            }

            if (y % 32 == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((float)y / h * 0.9f);
            }
        }

        // Normalize raw values to [0, 255]
        float min = raw[0], max = raw[0];
        for (int i = 1; i < raw.Length; i++)
        {
            if (raw[i] < min) min = raw[i];
            if (raw[i] > max) max = raw[i];
        }

        float range = max - min;
        var result = new ElevationResult(ctx.TileCount);
        for (int i = 0; i < raw.Length; i++)
        {
            result.Elevation[i] = (byte)Math.Clamp((int)((raw[i] - min) / range * 255f), 0, 255);
        }

        progress?.Report(1.0f);
        return result;
    }
}
