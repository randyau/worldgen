namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Aggregates IsPOICandidate flags from Magic and River layers into a unified POI set.
/// High-magic volcanic tiles and major river confluences are primary candidates.
/// </summary>
public sealed class PoiCandidateLayer : IWorldGenLayer<PoiResult>
{
    public PoiResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.7
        var result = new PoiResult();
        progress?.Report(1.0f);
        return result;
    }
}
