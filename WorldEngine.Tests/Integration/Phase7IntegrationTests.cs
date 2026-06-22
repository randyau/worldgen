using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Integration;

public class Phase7IntegrationTests
{
    private static WorldState BuildWorld()
    {
        var cfg = new WorldConfig { Seed = 7, WidthKm = 300, HeightKm = 200, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    [Fact]
    public void Phase7_PendingEventsWrittenToDatabase()
    {
        var world = BuildWorld();
        using var store = new EventStore(":memory:");
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), store, cache);

        runner.InjectPendingEvent(new PendingEvent(EventType.WildfireOccurred, new TileCoord(2, 2), null,
            System.Text.Json.JsonSerializer.Serialize(new { Intensity = 0.5f })));
        runner.RunTick(world);

        store.GetEventsByYear(world.CurrentYear).Should().NotBeEmpty();
    }

    [Fact]
    public void Phase7_CausalEdgesInsertedForLinkedEvents()
    {
        var world = BuildWorld();
        using var store = new EventStore(":memory:");
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), store, cache);

        // First tick: produce a root event.
        runner.InjectPendingEvent(new PendingEvent(EventType.VolcanicEruption, new TileCoord(1, 1), null,
            System.Text.Json.JsonSerializer.Serialize(new { Intensity = 0.9f })));
        runner.RunTick(world);
        var root = store.GetEventsByType(EventType.VolcanicEruption).Single();

        // Second tick: produce an event caused by the root.
        runner.InjectPendingEvent(new PendingEvent(EventType.WildfireOccurred, new TileCoord(1, 2), root.Id,
            System.Text.Json.JsonSerializer.Serialize(new { Intensity = 0.5f })));
        runner.RunTick(world);
        var child = store.GetEventsByType(EventType.WildfireOccurred).Single();

        store.GetCausalSuccessors(root.Id).Should().ContainSingle().Which.Id.Should().Be(child.Id);
    }

    [Fact]
    public void Phase7_GatedEventsNotInDatabase()
    {
        var world = BuildWorld();
        using var store = new EventStore(":memory:");
        var cache = new EventCache();
        var cfg = TestSimConfig.With(c => c.Events.SuppressedTypes.Add(nameof(EventType.WildfireOccurred)));
        var gate = new EventGate(cfg);
        var runner = new PhaseRunner(cfg, store, cache, gate);

        runner.InjectPendingEvent(new PendingEvent(EventType.WildfireOccurred, new TileCoord(2, 2), null,
            System.Text.Json.JsonSerializer.Serialize(new { Intensity = 0.5f })));
        runner.RunTick(world);

        store.GetEventsByType(EventType.WildfireOccurred).Should().BeEmpty();
        cache.ContainsType(EventType.WildfireOccurred).Should().BeFalse();
    }

    [Fact]
    public void Phase7_EventCacheContainsInsertedEvents()
    {
        var world = BuildWorld();
        using var store = new EventStore(":memory:");
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), store, cache);

        runner.InjectPendingEvent(new PendingEvent(EventType.VolcanicEruption, new TileCoord(0, 0), null,
            System.Text.Json.JsonSerializer.Serialize(new { Intensity = 0.9f })));
        runner.RunTick(world);

        cache.GetRecent(10).Should().Contain(e => e.Type == EventType.VolcanicEruption);
    }

    [Fact]
    public void Phase7_WriteOrderDbBeforeCache()
    {
        var world = BuildWorld();
        using var store = new EventStore(":memory:");
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), store, cache);

        runner.InjectPendingEvent(new PendingEvent(EventType.VolcanicEruption, new TileCoord(0, 0), null,
            System.Text.Json.JsonSerializer.Serialize(new { Intensity = 0.9f })));
        runner.RunTick(world);

        var cached = cache.GetRecent(10).Single(e => e.Type == EventType.VolcanicEruption);
        var stored = store.GetEvent(cached.Id);
        stored.Should().NotBeNull("cache must hold the DB-assigned Id, proving DB write preceded cache add");
        stored!.Id.Should().Be(cached.Id);
    }
}
