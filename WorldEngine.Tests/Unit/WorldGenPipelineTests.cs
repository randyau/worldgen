using System.Reflection;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class WorldGenPipelineTests
{
    private static WorldConfig SmallConfig(int seed = 42) => new()
    {
        Seed = seed,
        WidthKm = 100,
        HeightKm = 100,
        TileWidthKm = 10
    };

    [Fact]
    public void LayerSeeds_AllValuesAreUnique()
    {
        var fields = typeof(LayerSeeds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => (int)f.GetRawConstantValue()!)
            .ToList();

        fields.Should().OnlyHaveUniqueItems("all LayerSeeds constants must be distinct");
        fields.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pipeline_RunFullAsyncCompletesWithoutThrow()
    {
        var pipeline = new WorldGenPipeline();
        var config = SmallConfig();
        var simConfig = TestSimConfig.Default();

        var world = await pipeline.RunFullAsync(config, simConfig);

        world.Should().NotBeNull();
    }

    [Fact]
    public async Task Pipeline_ProgressCallbackInvokedForEachLayer()
    {
        var pipeline = new WorldGenPipeline();
        var config = SmallConfig();
        var simConfig = TestSimConfig.Default();

        var progressEvents = new System.Collections.Concurrent.ConcurrentBag<(string Layer, float Fraction)>();
        // Use synchronous IProgress implementation to avoid async dispatch timing issues
        var progress = new SyncProgress<(string Layer, float Fraction)>(e => progressEvents.Add(e));

        await pipeline.RunFullAsync(config, simConfig, progress);

        progressEvents.Should().HaveCountGreaterThanOrEqualTo(9,
            "each of the 9 layers should report at least one progress event");
    }

    private sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    [Fact]
    public async Task Pipeline_ReturnsNonNullWorldState()
    {
        var pipeline = new WorldGenPipeline();
        var world = await pipeline.RunFullAsync(SmallConfig(), TestSimConfig.Default());

        world.Should().NotBeNull();
        world.Config.Seed.Should().Be(42);
        world.TileGrid.Should().NotBeNull();
    }

    [Fact]
    public async Task RunUpToAsync_PopulatesOnlyLayersThroughIndex()
    {
        var pipeline = new WorldGenPipeline();
        var ctx = await pipeline.RunUpToAsync(SmallConfig(), TestSimConfig.Default(), layerIndex: 2);

        ctx.Tectonic.Should().NotBeNull();
        ctx.Elevation.Should().NotBeNull();
        ctx.Ocean.Should().NotBeNull();
        ctx.River.Should().BeNull("River is layer index 3, past the requested layer 2");
        ctx.Poi.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(WorldGenPipeline.LayerCount)]
    public async Task RunUpToAsync_RejectsOutOfRangeLayerIndex(int layerIndex)
    {
        var pipeline = new WorldGenPipeline();

        var act = async () => await pipeline.RunUpToAsync(SmallConfig(), TestSimConfig.Default(), layerIndex);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RerunFromAsync_ClearsAndRegeneratesFromRequestedLayerOnward()
    {
        var pipeline = new WorldGenPipeline();
        var ctx = await pipeline.RunUpToAsync(SmallConfig(), TestSimConfig.Default(), layerIndex: WorldGenPipeline.LayerCount - 1);

        var originalPoi = ctx.Poi;
        var reranCtx = await pipeline.RerunFromAsync(ctx, layerIndex: 8);

        reranCtx.Should().BeSameAs(ctx);
        reranCtx.Poi.Should().NotBeNull();
        // Same seed/config through an unchanged deterministic layer reruns to an equal result,
        // not merely a non-null one.
        reranCtx.Poi.Should().BeEquivalentTo(originalPoi);
    }

    [Fact]
    public async Task PartialRerun_WithUnchangedParams_ReproducesFullRun()
    {
        var pipeline = new WorldGenPipeline();
        var config = SmallConfig();
        var simConfig = TestSimConfig.Default();

        var fullWorld = await pipeline.RunFullAsync(config, simConfig);

        var partialCtx = await pipeline.RunUpToAsync(config, simConfig, layerIndex: 4);
        var resumedCtx = await pipeline.RerunFromAsync(partialCtx, layerIndex: 5);
        var resumedWorld = TileGridAssembler.Assemble(resumedCtx);

        resumedWorld.Should().BeEquivalentTo(fullWorld);
    }
}
