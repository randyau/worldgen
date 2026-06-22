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
}
