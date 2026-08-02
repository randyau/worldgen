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
/// Balance regression harness for M13's relationship-transition event surface (marriage/childbirth,
/// grief, Debt, non-ruler-bonds/defection, and 13.5's Reconciliation/Feud/Estrangement/Oath-breaking).
/// Mirrors <see cref="BalanceRegressionTests"/>'s seed set and philosophy (observed-healthy ± margin,
/// see docs/balance_invariants.md) but asserts on cumulative event *counts* over a run
/// (via <c>EventStore.CountEventsOfType</c>) rather than year-300 snapshot metrics, since these are
/// per-history-tallies, not point-in-time world state.
///
/// Calibration run (2026-08-02, post M13 13.5 dispatch-bug fix): seeds 42/777/9999, 300 years,
/// default config. Cumulative counts observed: CharacterMarried 0-4, CharacterBorn 136-219,
/// DebtIncurred 918-2113, DebtForgiven 308-689, CharacterDefected 0-7, CharacterGrieved 14-25.
/// RivalryFormed/RivalryPlacated/RivalsReconciled/RivalryEscalatedToFeud/CharacterEstranged/
/// OathBroken were all 0 across all 3 seeds — see rationale below the bands.
///
/// **Known gap (not yet fixed):** Rivalry-derived mechanics require two Tier1 (named) characters
/// to personally interact — either share a rivalry needing Trust to drift below -0.4 (mostly a
/// cross-civ dynamic), or hold a cross-civ Debt/Trust relationship. Tier1 characters are rare
/// (single digits to ~15 alive at once) and largely stationary once settled; cross-civ *personal*
/// contact is much rarer than the aid-a-hungry-townsperson path 13.5 unblocked (which only needed
/// same-civ contact, reachable via the Tier2 companion shortcut). Getting these off zero requires
/// either longer runs, more Tier1 population, or deliberately more Tier1 cross-civ travel — out of
/// scope for this pass. These bands assert a ceiling only (catch runaway firing) and do not assert
/// a floor (catch total absence) — do NOT read the ceiling-only assertion as "this is fine forever";
/// revisit once cross-civ Tier1 contact frequency is addressed.
/// </summary>
[Trait("Category", "Balance")]
public class M13RelationshipEventBalanceTests
{
    private static readonly int[] Seeds = [42, 777, 9999];
    private const int Years = 300;

    private sealed record Band(int Min, int Max);

    // Bands = observed ± ~40% margin (matches BalanceRegressionTests' philosophy), except where
    // noted. These are deliberately generous — M13 13.5's fix pass was about confirming mechanics
    // fire at all and aren't wildly runaway, not nailing exact frequencies (fine-tuning is a
    // follow-up pass per project convention — see docs/balance_invariants.md).
    private static readonly Dictionary<EventType, Band> Bands = new()
    {
        // Reliable across all 3 seeds pre-fix and post-fix; unaffected by the 13.5 dispatch bugfix.
        [EventType.CharacterBorn]   = new Band(80, 320),
        [EventType.CharacterGrieved] = new Band(5, 60),

        // Marriage is real but low-volume given how few Tier1 characters exist; seed 42 hit 0 in
        // calibration, so no floor — only a ceiling to catch runaway marriage-spam.
        [EventType.CharacterMarried] = new Band(0, 12),

        // M13 13.5 dispatch-bug fix (GrantAid/ForgiveDebt were never wired into
        // CharacterBehaviorPhase.ResolveCommand's switch — silently no-op'd every time the utility
        // scorer picked them). Now fires constantly via the Tier2 community-aid shortcut since
        // Tier2 needs sit near their decay floor most of the time. High-side band reflects that;
        // narrowing this is exactly the "fine details" tuning the project has deferred.
        [EventType.DebtIncurred]  = new Band(400, 3200),
        [EventType.DebtForgiven]  = new Band(120, 1100),

        // Same dispatch-bug fix unblocked Defect. Rare — requires Wellbeing crisis + a
        // sufficiently-trusted foreign confidant — so a wide low-count band, no assumed floor.
        [EventType.CharacterDefected] = new Band(0, 20),

        // Known gap (see class doc) — ceiling-only, no floor asserted yet.
        [EventType.RivalryFormed]          = new Band(0, 30),
        [EventType.RivalryPlacated]        = new Band(0, 30),
        [EventType.RivalsReconciled]       = new Band(0, 20),
        [EventType.RivalryEscalatedToFeud] = new Band(0, 20),
        [EventType.CharacterEstranged]     = new Band(0, 20),
        [EventType.OathBroken]             = new Band(0, 20),
    };

    [Fact]
    public void Year300_M13EventCountsWithinBands()
    {
        var failures = new List<string>();

        foreach (int seed in Seeds)
        {
            var (world, eventStore) = RunSim(seed);
            foreach (var (type, band) in Bands)
            {
                int count = eventStore.CountEventsOfType(type.ToString());
                if (count < band.Min || count > band.Max)
                    failures.Add($"seed={seed} {type}={count} outside [{band.Min}, {band.Max}]");
            }
            eventStore.Dispose();
        }

        if (failures.Count > 0)
            Assert.Fail("M13 event-count regression:\n" + string.Join("\n", failures.Select(f => $"  • {f}")));
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
