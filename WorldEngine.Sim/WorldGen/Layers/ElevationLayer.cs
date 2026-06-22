namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates per-tile elevation using FastNoiseLite Simplex noise
/// combined with tectonic contributions (mountain ridges, trenches, highlands).
/// </summary>
public sealed class ElevationLayer : IWorldGenLayer<ElevationResult>
{
    public ElevationResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.3
        var result = new ElevationResult(ctx.TileCount);
        progress?.Report(1.0f);
        return result;
    }
}
