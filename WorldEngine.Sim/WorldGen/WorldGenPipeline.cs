using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen.Layers;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Runs the full world generation pipeline and returns a populated WorldState.
/// Each layer receives the WorldGenContext (read-only access to previous results).
/// Progress is reported as (LayerName, fraction) per layer step.
/// </summary>
public sealed class WorldGenPipeline
{
    private const int LayerCount = 9;

    /// <summary>
    /// Runs all generation layers in dependency order and assembles the result into a WorldState.
    /// </summary>
    public async Task<WorldState> RunFullAsync(
        WorldConfig config,
        SimConfig simConfig,
        IProgress<(string Layer, float Fraction)>? progress = null,
        CancellationToken ct = default)
    {
        var ctx = new WorldGenContext(config, simConfig);
        int step = 0;

        ctx.Tectonic = await RunLayerAsync("Tectonic", step++, new TectonicLayer(), ctx, progress, ct);
        ctx.Elevation = await RunLayerAsync("Elevation", step++, new ElevationLayer(), ctx, progress, ct);
        ctx.Ocean = await RunLayerAsync("Ocean", step++, new OceanLayer(), ctx, progress, ct);
        ctx.River = await RunLayerAsync("River", step++, new RiverLayer(), ctx, progress, ct);
        ctx.Magic = await RunLayerAsync("Magic", step++, new MagicLayer(), ctx, progress, ct);
        ctx.Climate = await RunLayerAsync("Climate", step++, new ClimateLayer(), ctx, progress, ct);
        ctx.Biome = await RunLayerAsync("Biome", step++, new BiomeLayer(), ctx, progress, ct);
        ctx.Resource = await RunLayerAsync("Resource", step++, new ResourceLayer(), ctx, progress, ct);
        ctx.Poi = await RunLayerAsync("Poi", step++, new PoiCandidateLayer(), ctx, progress, ct);

        ct.ThrowIfCancellationRequested();

        return TileGridAssembler.Assemble(ctx);
    }

    private static Task<TResult> RunLayerAsync<TResult>(
        string name,
        int stepIndex,
        IWorldGenLayer<TResult> layer,
        WorldGenContext ctx,
        IProgress<(string Layer, float Fraction)>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var layerProgress = progress is null
            ? null
            : new Progress<float>(f => progress.Report((name, f)));

        // Layers are synchronous; wrap in Task.Run to avoid blocking the caller
        return Task.Run(() => layer.Generate(ctx, layerProgress, ct), ct);
    }
}
