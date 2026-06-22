namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Assigns mineral and rare resource deposits to tiles based on tectonic and biome data.
/// Results written to ResourceResult.Deposits and reflected in TileStaticFlags during assembly.
/// </summary>
public sealed class ResourceLayer : IWorldGenLayer<ResourceResult>
{
    public ResourceResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.7
        var result = new ResourceResult();
        progress?.Report(1.0f);
        return result;
    }
}
