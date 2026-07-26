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
    /// <summary>Number of layers in the pipeline (Tectonic through Poi).</summary>
    public const int LayerCount = 9;

    /// <summary>Layer names in pipeline order, indexed 0..LayerCount-1.</summary>
    public static readonly IReadOnlyList<string> LayerNames = new[]
    {
        "Tectonic", "Elevation", "Ocean", "River", "Magic", "Climate", "Biome", "Resource", "Poi"
    };

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
        await RunLayersAsync(ctx, 0, LayerCount - 1, progress, ct);

        ct.ThrowIfCancellationRequested();

        return TileGridAssembler.Assemble(ctx);
    }

    /// <summary>
    /// Runs a fresh pipeline from layer 0 up to and including <paramref name="layerIndex"/>,
    /// returning the in-progress context without assembling a WorldState. Used to seed a
    /// worldgen preview at a chosen layer.
    /// </summary>
    public async Task<WorldGenContext> RunUpToAsync(
        WorldConfig config,
        SimConfig simConfig,
        int layerIndex,
        IProgress<(string Layer, float Fraction)>? progress = null,
        CancellationToken ct = default)
    {
        ValidateLayerIndex(layerIndex);

        var ctx = new WorldGenContext(config, simConfig);
        await RunLayersAsync(ctx, 0, layerIndex, progress, ct);
        return ctx;
    }

    /// <summary>
    /// Re-enters an existing context at <paramref name="layerIndex"/>: clears that layer's
    /// result and every result after it, then re-runs from there to the end of the chain.
    /// Results before <paramref name="layerIndex"/> are reused untouched. Because the chain is
    /// strictly linear (each layer reads only completed predecessors), this is a mechanical
    /// slice of RunFullAsync rather than a dependency-tracking rerun.
    /// </summary>
    public async Task<WorldGenContext> RerunFromAsync(
        WorldGenContext ctx,
        int layerIndex,
        IProgress<(string Layer, float Fraction)>? progress = null,
        CancellationToken ct = default)
    {
        ValidateLayerIndex(layerIndex);

        ClearFrom(ctx, layerIndex);
        await RunLayersAsync(ctx, layerIndex, LayerCount - 1, progress, ct);
        return ctx;
    }

    private static void ValidateLayerIndex(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layerIndex),
                $"Layer index must be in [0, {LayerCount - 1}].");
        }
    }

    private static async Task RunLayersAsync(
        WorldGenContext ctx,
        int startIndex,
        int endIndex,
        IProgress<(string Layer, float Fraction)>? progress,
        CancellationToken ct)
    {
        for (int i = startIndex; i <= endIndex; i++)
        {
            ct.ThrowIfCancellationRequested();

            switch (i)
            {
                case 0: ctx.Tectonic = await RunLayerAsync(LayerNames[0], new TectonicLayer(), ctx, progress, ct); break;
                case 1: ctx.Elevation = await RunLayerAsync(LayerNames[1], new ElevationLayer(), ctx, progress, ct); break;
                case 2: ctx.Ocean = await RunLayerAsync(LayerNames[2], new OceanLayer(), ctx, progress, ct); break;
                case 3: ctx.River = await RunLayerAsync(LayerNames[3], new RiverLayer(), ctx, progress, ct); break;
                case 4: ctx.Magic = await RunLayerAsync(LayerNames[4], new MagicLayer(), ctx, progress, ct); break;
                case 5: ctx.Climate = await RunLayerAsync(LayerNames[5], new ClimateLayer(), ctx, progress, ct); break;
                case 6: ctx.Biome = await RunLayerAsync(LayerNames[6], new BiomeLayer(), ctx, progress, ct); break;
                case 7: ctx.Resource = await RunLayerAsync(LayerNames[7], new ResourceLayer(), ctx, progress, ct); break;
                case 8: ctx.Poi = await RunLayerAsync(LayerNames[8], new PoiCandidateLayer(), ctx, progress, ct); break;
            }
        }
    }

    /// <summary>Nulls out every layer result from <paramref name="layerIndex"/> onward.</summary>
    private static void ClearFrom(WorldGenContext ctx, int layerIndex)
    {
        if (layerIndex <= 0) ctx.Tectonic = null;
        if (layerIndex <= 1) ctx.Elevation = null;
        if (layerIndex <= 2) ctx.Ocean = null;
        if (layerIndex <= 3) ctx.River = null;
        if (layerIndex <= 4) ctx.Magic = null;
        if (layerIndex <= 5) ctx.Climate = null;
        if (layerIndex <= 6) ctx.Biome = null;
        if (layerIndex <= 7) ctx.Resource = null;
        if (layerIndex <= 8) ctx.Poi = null;
    }

    private static Task<TResult> RunLayerAsync<TResult>(
        string name,
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
