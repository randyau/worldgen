namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Thresholds elevation into ocean/land using DefaultSeaLevel (fraction of tiles that are ocean).
/// Then marks land tiles adjacent to any ocean tile as IsCoastal.
/// </summary>
public sealed class OceanLayer : IWorldGenLayer<OceanResult>
{
    public OceanResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var elev   = ctx.Elevation!;
        float frac = ctx.SimConfig.WorldGen.Ocean.DefaultSeaLevel;
        int w = ctx.TileWidth, h = ctx.TileHeight, n = ctx.TileCount;

        var result = new OceanResult(n);

        // --- Determine sea level threshold by elevation rank ---
        // Copy and sort elevations to find the value at the given fraction
        byte[] sorted = (byte[])elev.Elevation.Clone();
        Array.Sort(sorted);
        int thresholdIdx = Math.Clamp((int)(frac * n), 0, n - 1);
        byte seaThreshold = sorted[thresholdIdx];

        progress?.Report(0.3f);
        ct.ThrowIfCancellationRequested();

        // Mark ocean tiles (elevation ≤ threshold)
        for (int i = 0; i < n; i++)
            result.IsOcean[i] = elev.Elevation[i] <= seaThreshold;

        progress?.Report(0.6f);
        ct.ThrowIfCancellationRequested();

        // Mark coastal tiles: land tile with at least one ocean 4-neighbor
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (result.IsOcean[idx]) continue;

                bool coastal =
                    (y > 0     && result.IsOcean[ctx.IndexOf(x, y - 1)])        ||
                    (y < h - 1 && result.IsOcean[ctx.IndexOf(x, y + 1)])        ||
                    result.IsOcean[ctx.IndexOf((x + 1) % w, y)]                 ||
                    result.IsOcean[ctx.IndexOf((x - 1 + w) % w, y)];

                result.IsCoastal[idx] = coastal;
            }
        }

        progress?.Report(1.0f);
        return result;
    }
}
