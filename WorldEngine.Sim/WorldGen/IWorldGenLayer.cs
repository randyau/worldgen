namespace WorldEngine.Sim.WorldGen;

/// <summary>World generation layer. Stateless — all state lives in WorldGenContext.</summary>
public interface IWorldGenLayer<TResult>
{
    TResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default);
}
