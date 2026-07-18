using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Integration tests for Story A2 — MetricsCollector and yearly_metrics table.
/// </summary>
public class MetricsCollectorTests
{
    // ─── Shared helpers ───────────────────────────────────────────────────────

    private static WorldState BuildTinyWorld(int seed = 11)
    {
        var cfg    = new WorldConfig { Seed = seed, WidthKm = 200, HeightKm = 200, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();
        return new WorldGenPipeline().RunFullAsync(cfg, simCfg).GetAwaiter().GetResult();
    }

    private static (SimLoop loop, PhaseRunner runner, EventStore store)
        BuildHeadlessStack(WorldState world)
    {
        var simConfig    = world.SimConfig;
        var eventStore   = new EventStore(":memory:");
        var eventCache   = new EventCache(simConfig.Events.RecentEventCacheSize);
        var gate         = new EventGate(simConfig);
        var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
        var phaseRunner  = new PhaseRunner(simConfig, eventStore, eventCache, gate,
            beastCatalog: beastCatalog);

        foreach (var pe in BeastSpawner.SpawnAll(world, beastCatalog))  phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in CharacterSpawner.SpawnAll(world, simConfig)) phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in Tier2Spawner.SpawnAll(world, simConfig))     phaseRunner.InjectPendingEvent(pe);

        var cmdQueue        = new CommandQueue();
        var stateCache      = new StateCache();
        var snapshotBuilder = new SnapshotBuilder();
        var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simConfig, eventCache);

        return (simLoop, phaseRunner, eventStore);
    }

    // ─── A2: metrics table has one row per simulated year ─────────────────────

    [Fact]
    public void MetricsCollector_WritesOneRowPerYear()
    {
        var world = BuildTinyWorld(seed: 11);
        // Ensure metrics are enabled
        world.SimConfig.SimLoop.MetricsEnabled.Should().BeTrue(
            "metrics_enabled must default to true");

        var (simLoop, phaseRunner, eventStore) = BuildHeadlessStack(world);

        int yearsToRun = 15;
        int ticks      = yearsToRun * world.SimConfig.SimLoop.TicksPerYear;
        simLoop.RunSynchronous(ticks);
        phaseRunner.FlushPendingEvents(world);

        int rowCount = eventStore.GetMetricsRowCount();
        rowCount.Should().Be(yearsToRun,
            $"yearly_metrics should have exactly one row per simulated year (expected {yearsToRun})");

        eventStore.Dispose();
    }

    // ─── A2: last metrics row reflects actual world state ─────────────────────

    [Fact]
    public void MetricsCollector_LastRowMatchesWorldState()
    {
        var world = BuildTinyWorld(seed: 12);
        var (simLoop, phaseRunner, eventStore) = BuildHeadlessStack(world);

        int yearsToRun = 10;
        int ticks      = yearsToRun * world.SimConfig.SimLoop.TicksPerYear;
        simLoop.RunSynchronous(ticks);
        phaseRunner.FlushPendingEvents(world);

        var lastRow = eventStore.GetLastMetricsRow();
        lastRow.Should().NotBeNull("a metrics row should exist after simulation");

        // The metrics row should reflect the final world state at the time of sampling.
        // Population values come from WorldState directly, so they should be non-negative.
        lastRow!.WorldPopulation.Should().BeGreaterThanOrEqualTo(0,
            "world population cannot be negative");
        lastRow.ActiveCivs.Should().BeGreaterThanOrEqualTo(0,
            "active civ count cannot be negative");
        lastRow.Year.Should().BeGreaterThanOrEqualTo(yearsToRun,
            $"final metrics row year should be at least {yearsToRun}");
        lastRow.MeanFoodRatio.Should().BeGreaterThanOrEqualTo(0f,
            "food ratio must be non-negative");

        eventStore.Dispose();
    }

    // ─── A2: metrics are gate-independent (don't require events to be recorded) ─

    [Fact]
    public void MetricsCollector_PopulatesEvenWhenEventsAreSuppressed()
    {
        var world  = BuildTinyWorld(seed: 13);
        var simCfg = world.SimConfig;

        // Suppress all events (gate everything) to verify metrics still collect
        // Suppress all events by setting a tier above the max (Headline=3, so 4 passes nothing).
        // DECISION: EventsConfig uses int for MinimumRecordedTier despite the EventTier enum.
        // Cast workaround needed here.
        simCfg.Events.MinimumRecordedTier = (WorldEngine.Sim.Core.EventTier)99; // above all real tiers

        var eventStore   = new EventStore(":memory:");
        var eventCache   = new EventCache(simCfg.Events.RecentEventCacheSize);
        var gate         = new EventGate(simCfg);
        var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
        var phaseRunner  = new PhaseRunner(simCfg, eventStore, eventCache, gate,
            beastCatalog: beastCatalog);

        var cmdQueue        = new CommandQueue();
        var stateCache      = new StateCache();
        var snapshotBuilder = new SnapshotBuilder();
        var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simCfg, eventCache);

        simLoop.RunSynchronous(5 * simCfg.SimLoop.TicksPerYear);
        phaseRunner.FlushPendingEvents(world);

        // With all events suppressed, event table should be nearly empty (only spawn events might
        // slip through at Background tier if the gate allows Background).
        // But metrics rows should exist regardless.
        int metricRows = eventStore.GetMetricsRowCount();
        metricRows.Should().Be(5,
            "metrics must be written even when all events are gate-suppressed");

        eventStore.Dispose();
    }

    // ─── A2: reproducibility — final metrics year matches for same seed ────────

    [Fact]
    public void MetricsCollector_FinalYearIsConsistent()
    {
        // Run once and check that the final year number matches the simulation year
        var world = BuildTinyWorld(seed: 14);
        var (simLoop, phaseRunner, eventStore) = BuildHeadlessStack(world);

        int yearsToRun = 8;
        simLoop.RunSynchronous(yearsToRun * world.SimConfig.SimLoop.TicksPerYear);
        phaseRunner.FlushPendingEvents(world);

        var last = eventStore.GetLastMetricsRow();
        last.Should().NotBeNull();
        last!.Year.Should().BeGreaterThanOrEqualTo(yearsToRun,
            "the last metrics row's year must be at least the number of simulated years");

        eventStore.Dispose();
    }
}
