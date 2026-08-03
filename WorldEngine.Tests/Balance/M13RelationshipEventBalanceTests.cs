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
/// Calibration run (2026-08-02, after M13 13.6's same-civ Trust economy pass — see
/// docs/phases/archive/m13_generational_domestic_drama.md): seeds 42/777/9999, 300 years, default
/// config. Cumulative counts observed: CharacterMarried 0-4, CharacterBorn 141-212,
/// DebtIncurred 938-1987, DebtForgiven 274-696, CharacterDefected 0-5, CharacterGrieved 18-35,
/// RivalryFormed 1-37, RivalryPlacated 0-3, RivalryEscalatedToFeud 1-32.
/// RivalsReconciled/CharacterEstranged/OathBroken remained 0 across all 3 seeds — see rationale
/// below the bands.
///
/// **Known gap (not yet fixed):** tracing why Estrangement stayed at 0 even after tripling the
/// marriage-hardship sink (identical output before/after — the sim is deterministic, so identical
/// output means the branch never ran, not that it's just weak) led to a much bigger, separate
/// finding: `Tier1Character.AgeSeason` increments once per TICK (confirmed by
/// `CharacterSimConfig.Tier2MaxAgeSeasonsMin`'s own "~38 years at 16 ticks/year" comment), but the
/// "human" ancestry (`config/ancestries.toml`) sets `min/max_lifespan_seasons = 60/200` — 3.75 to
/// 12.5 *real* years, not enough time for a marriage (or most slow-building relationship
/// mechanics) to develop before natural death. `FamilyConfig.MarriageMinAgeSeasons=60` (3.75y) and
/// `CharacterSimConfig.MinRulerAgeSeasons=32` (2y) corroborate the same units mismatch. Other
/// ancestries (elf 50-125y, dwarf 20-50y, orc 25-75y) look correctly scaled for 16 ticks/year,
/// suggesting `TicksPerSeasonalChange` was raised at some point without rescaling every
/// `*Seasons`-suffixed duration constant to match — a cross-cutting fix well beyond a Trust-economy
/// pass, flagged for a dedicated follow-up rather than expanded into here. RivalryFormed/
/// RivalryPlacated/RivalryEscalatedToFeud went from "always exactly 0" to "fires, but seed 42's
/// count (1) is too close to 0 to safely assert a nonzero floor yet" — ceilings tightened to the
/// new observed range, floor still 0 pending the lifespan fix widening the sample. RivalsReconciled/
/// CharacterEstranged/OathBroken remain fully ceiling-only (no evidence they can fire yet at all).
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

        // M13 13.6 same-civ Trust economy pass unblocked these (previously always exactly 0).
        // Observed 1-37 / 0-3 / 1-32 across 3 seeds — wide variance from Tier1 population sparsity,
        // so no floor yet (seed 42's RivalryFormed=1 is too close to 0 to assert reliably), but the
        // ceiling now reflects real usage instead of "any nonzero value is already anomalous".
        [EventType.RivalryFormed]          = new Band(0, 55),
        [EventType.RivalryPlacated]        = new Band(0, 10),
        [EventType.RivalryEscalatedToFeud] = new Band(0, 48),

        // Known gap (see class doc) — ceiling-only, no floor asserted yet; no evidence these fire at all.
        [EventType.RivalsReconciled]       = new Band(0, 20),
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
