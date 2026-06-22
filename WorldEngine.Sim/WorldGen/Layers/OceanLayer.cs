namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Thresholds elevation into ocean/land using SeaLevelFraction,
/// then marks coast tiles adjacent to ocean.
/// </summary>
public sealed class OceanLayer : IWorldGenLayer<OceanResult>
{
    public OceanResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.3
        var result = new OceanResult(ctx.TileCount);
        progress?.Report(1.0f);
        return result;
    }
}
