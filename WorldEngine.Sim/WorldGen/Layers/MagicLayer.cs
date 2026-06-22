namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates magic intensity using Simplex noise with volcanic zone weighting.
/// M1 stub: generates and stores MagicIntensity, marks IsPOICandidate. No behavioral effects.
/// </summary>
public sealed class MagicLayer : IWorldGenLayer<MagicResult>
{
    public MagicResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // V2: magic physical substrate — behaviors driven by magic intensity not implemented until M2+
        // DECISION: Stub — real implementation in story 1.3.5
        var result = new MagicResult(ctx.TileCount);
        progress?.Report(1.0f);
        return result;
    }
}
