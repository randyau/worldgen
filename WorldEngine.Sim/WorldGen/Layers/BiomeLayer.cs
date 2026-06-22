namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Classifies each tile's biome using BiomeClassifier (Whittaker diagram + priority rules)
/// and computes Fertility from biome and climate inputs.
/// </summary>
public sealed class BiomeLayer : IWorldGenLayer<BiomeResult>
{
    public BiomeResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.7
        var result = new BiomeResult(ctx.TileCount);
        progress?.Report(1.0f);
        return result;
    }
}
