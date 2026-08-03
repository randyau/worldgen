using System.Diagnostics;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Balance;

/// <summary>
/// M13.8.3 — balance and performance validation for M13.8.2's Notability-driven crystallization
/// path (and, transitively, all of 13.8's Tier2-as-relationship-target work). See
/// docs/phases/m13_8_tier2_relationship_exposure.md. Confirms three things the scale constraint
/// running throughout M13.8 depends on:
///   (a) CharacterCrystallized rises moderately with Notability's alternate gate, not into a
///       runaway "everyone gets promoted" regime that would balloon Tier1 back toward Tier2 scale;
///   (b) a 300-year run's wall-clock time stays within a generous ceiling — the actual test of
///       "Tier2 never runs its own scorer, nothing iterates all of Tier2 every tick", not just an
///       assertion that it's true;
///   (c) RelationshipEdge count grows with Tier1 contacts, not with Tier2 population size — a
///       structural guarantee, not a coincidence of these three seeds.
///
/// Calibration run (2026-08-03, after M13.8.2 shipped): seeds 42/777/9999, 300 years, default
/// config. CharacterCrystallized 31-59 (Tier1 population 9-15, Tier2 population 50-85 by year 300,
/// starting from 0 — Tier2 only spawns once settlements exist and grow, via
/// PopulationDynamicsPhase). RelationshipEdge count 26-66 — well below Tier2 population size and
/// tracking Tier1 count (edge/Tier1 ratio 2.9-4.8), not Tier2 population (which would put a
/// naive O(Tier1×Tier2) scan north of 500-1000 edges). Wall-clock ~50-60s per 300-year seed on the
/// dev machine at time of writing.
/// </summary>
[Trait("Category", "Balance")]
public class Tier2CrystallizationBalanceTests
{
    private static readonly int[] Seeds = [42, 777, 9999];
    private const int Years = 300;

    // Bands = observed ± generous margin (same philosophy as M13RelationshipEventBalanceTests) —
    // this pass is about confirming the mechanic doesn't run away or regress performance, not
    // nailing exact frequencies.
    private const int CrystallizedMin = 10;
    private const int CrystallizedMax = 100;

    // RelationshipEdge count must scale with Tier1 contacts, not Tier2 population — bound it as a
    // multiple of Tier1 count (observed ratio 2.9-4.8×) rather than an absolute number, since Tier1
    // population itself varies by seed. A genuine O(Tier1×Tier2) regression would blow past this
    // by an order of magnitude or more.
    private const int MaxEdgesPerTier1 = 20;

    // Generous per-seed wall-clock ceiling — not a tight perf bound, just a tripwire for a
    // regression that reintroduces an O(Tier2) or O(Tier1×Tier2) scan per tick (observed ~50-60s
    // per seed on the dev machine at time of writing).
    private static readonly TimeSpan MaxWallClockPerSeed = TimeSpan.FromSeconds(180);

    [Fact]
    public void Year300_CrystallizationBalanceAndPerf_WithinBounds()
    {
        var failures = new List<string>();

        foreach (int seed in Seeds)
        {
            var sw = Stopwatch.StartNew();
            var (world, eventStore) = RunSim(seed);
            sw.Stop();

            int crystallized = eventStore.CountEventsOfType(EventType.CharacterCrystallized.ToString());
            int tier1Count   = world.Entities.Characters.Count;
            int tier2Count   = world.Entities.Tier2Chars.Count;
            int edgeCount    = world.Relationships.EdgeCount;

            if (crystallized < CrystallizedMin || crystallized > CrystallizedMax)
                failures.Add($"seed={seed} CharacterCrystallized={crystallized} outside [{CrystallizedMin}, {CrystallizedMax}]");

            int maxEdges = Math.Max(1, tier1Count) * MaxEdgesPerTier1;
            if (edgeCount > maxEdges)
                failures.Add($"seed={seed} RelationshipEdge count={edgeCount} exceeds {maxEdges} (tier1Count={tier1Count} × {MaxEdgesPerTier1}) — possible O(Tier1×Tier2) regression (tier2Count={tier2Count})");

            if (sw.Elapsed > MaxWallClockPerSeed)
                failures.Add($"seed={seed} wall-clock {sw.Elapsed} exceeds {MaxWallClockPerSeed} — possible per-tick Tier2 population scan regression");

            eventStore.Dispose();
        }

        if (failures.Count > 0)
            Assert.Fail("M13.8.3 crystallization balance/perf regression:\n" + string.Join("\n", failures.Select(f => $"  • {f}")));
    }

    private static (WorldState world, EventStore eventStore) RunSim(int seed)
    {
        var cfg    = new WorldConfig { Seed = seed, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();
        var world  = new WorldGenPipeline().RunFullAsync(cfg, simCfg).GetAwaiter().GetResult();

        var eventStore   = new EventStore(":memory:");
        var eventCache   = new EventCache(simCfg.Events.RecentEventCacheSize);
        var gate         = new EventGate(simCfg);
        var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
        var phaseRunner  = new PhaseRunner(simCfg, eventStore, eventCache, gate, beastCatalog: beastCatalog);

        foreach (var pe in BeastSpawner.SpawnAll(world, beastCatalog))  phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in CharacterSpawner.SpawnAll(world, simCfg))    phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in Tier2Spawner.SpawnAll(world, simCfg))        phaseRunner.InjectPendingEvent(pe);

        var cmdQueue        = new CommandQueue();
        var stateCache      = new StateCache();
        var snapshotBuilder = new SnapshotBuilder();
        var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simCfg, eventCache);

        simLoop.RunSynchronous(Years * simCfg.SimLoop.TicksPerYear);
        phaseRunner.FlushPendingEvents(world);

        return (world, eventStore);
    }
}
