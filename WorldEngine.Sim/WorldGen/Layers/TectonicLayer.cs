namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates tectonic plate assignments, fault lines, and volcanic zones.
/// Algorithm: Poisson disc plate center sampling → cylinder-aware Voronoi → subduction detection.
/// </summary>
public sealed class TectonicLayer : IWorldGenLayer<TectonicResult>
{
    public TectonicResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.2
        var result = new TectonicResult(ctx.TileCount);
        progress?.Report(1.0f);
        return result;
    }
}
