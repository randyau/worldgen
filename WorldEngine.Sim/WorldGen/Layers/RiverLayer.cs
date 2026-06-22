namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Computes drainage using D8 flow direction + Priority Flood sink filling (Barnes 2014),
/// then accumulates flow to identify rivers and lakes.
/// </summary>
public sealed class RiverLayer : IWorldGenLayer<RiverResult>
{
    public RiverResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.4
        var result = new RiverResult(ctx.TileCount);
        progress?.Report(1.0f);
        return result;
    }
}
