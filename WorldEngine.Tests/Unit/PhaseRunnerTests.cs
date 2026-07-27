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

    /// <summary>
    /// M11 phase 0: BuildSummaries() does a full Events-table rescan per call, which was the
    /// dominant cost behind a ~3x tick-rate slowdown over a 10k-year run when triggered on a
    /// hardcoded 50-year cadence regardless of whether anything reads the summary tables.
    /// SummaryRebuildIntervalYears=0 must fully disable the automatic rebuild.
    /// </summary>
    [Fact]
    public void SummaryRebuildInterval_ZeroDisablesAutoRebuild()
    {
        var world = BuildWorld();
        var cfg = TestSimConfig.Default();
        cfg.SimLoop.SummaryRebuildIntervalYears = 0;
        cfg.SimLoop.EventWriteBatchIntervalTicks = 0; // write every tick so the injected event is visible to any rebuild immediately
        var eventStore = new EventStore();
        var runner = new PhaseRunner(cfg, eventStore, new EventCache());

        runner.InjectPendingEvent(new PendingEvent(EventType.CivilizationFounded, null, null, "{}", CivId: 99));

        int ticksPerYear = cfg.SimLoop.TicksPerYear;
        for (int i = 0; i < ticksPerYear * 51; i++) runner.RunTick(world); // past the old hardcoded 50-year cadence
        runner.FlushPendingEvents(world);

        eventStore.GetHistoryQuery().GetCivSummary(new CivId(99)).Should().BeNull(
            "SummaryRebuildIntervalYears=0 must disable automatic BuildSummaries calls entirely");
    }

    /// <summary>
    /// Counterpart to the disabled case above — a configured interval must still trigger the
    /// rebuild on the next matching annual tick (behavior preserved from the prior hardcoded % 50).
    /// </summary>
    [Fact]
    public void SummaryRebuildInterval_TriggersAtConfiguredCadence()
    {
        var world = BuildWorld();
        var cfg = TestSimConfig.Default();
        cfg.SimLoop.SummaryRebuildIntervalYears = 1; // rebuild every year — fast, deterministic
        cfg.SimLoop.EventWriteBatchIntervalTicks = 0;
        var eventStore = new EventStore();
        var runner = new PhaseRunner(cfg, eventStore, new EventCache());

        runner.InjectPendingEvent(new PendingEvent(EventType.CivilizationFounded, null, null, "{}", CivId: 99));

        int ticksPerYear = cfg.SimLoop.TicksPerYear;
        for (int i = 0; i < ticksPerYear * 2; i++) runner.RunTick(world); // cross at least one annual boundary
        runner.FlushPendingEvents(world);

        eventStore.GetHistoryQuery().GetCivSummary(new CivId(99)).Should().NotBeNull(
            "SummaryRebuildIntervalYears=1 must auto-rebuild on the next annual tick");
    }
}
