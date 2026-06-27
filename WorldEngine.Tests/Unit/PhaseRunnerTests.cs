using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class PhaseRunnerTests
{
    private static WorldState BuildWorld()
    {
        var cfg = new WorldConfig { Seed = 1, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
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
    public void PhaseRunner_ExecutesPhasesInCorrectOrder()
    {
        var world = BuildWorld();
        var executionOrder = new List<int>();
        var runner = new PhaseRunner(
            TestSimConfig.Default(),
            new EventStore(),
            new EventCache(),
            phaseObserver: phase => executionOrder.Add((int)phase));

        runner.RunTick(world);

        executionOrder.Should().Equal(new[] { 1, 2, 3, 4, 5, 6, 7 },
            "simulation phases must execute in order 1 (Environmental) through 7 (EventGeneration)");
    }

    [Fact]
    public void PhaseRunner_Phase7ReceivesPendingEventsFromPhase1()
    {
        var world = BuildWorld();
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), new EventStore(), cache);

        // Inject a test pending event source into Phase 1
        runner.InjectPendingEvent(new PendingEvent(EventType.WildfireOccurred, null, null, "{}"));
        runner.RunTick(world);
        runner.FlushPendingEvents(world);

        // Phase 7 should have processed the injected event into the cache
        var recent = cache.GetRecent(10);
        recent.Should().ContainSingle(e => e.Type == EventType.WildfireOccurred,
            "pending event injected in Phase 1 should reach the EventCache via Phase 7");
    }

    [Fact]
    public void PhaseRunner_TickAdvancesTickCounter()
    {
        var world = BuildWorld();
        long before = world.CurrentTick;
        var runner = new PhaseRunner(TestSimConfig.Default(), new EventStore(), new EventCache());

        runner.RunTick(world);

        world.CurrentTick.Should().Be(before + 1, "RunTick must increment world.CurrentTick by 1");
    }
}
