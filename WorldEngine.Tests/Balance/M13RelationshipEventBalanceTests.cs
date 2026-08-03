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
/// Calibration run (2026-08-02, after fixing the human-ancestry lifespan units-mismatch bug — see
/// docs/phases/archive/m13_generational_domestic_drama.md and the project_human_lifespan_units_bug
/// memory): seeds 42/777/9999, 300 years, default config. Cumulative counts observed:
/// CharacterMarried 0-10, CharacterBorn 69-112, DebtIncurred 1049-2281, DebtForgiven 506-1036,
/// CharacterDefected 1-51, CharacterGrieved 23-50, RivalryFormed 16-40, RivalryPlacated 0-3,
/// RivalryEscalatedToFeud 16-34. RivalsReconciled/CharacterEstranged/OathBroken remained 0 across
/// all 3 seeds — see rationale below the bands.
///
/// **The lifespan fix:** `Tier1Character.AgeSeason` increments once per tick (16 ticks/year), but
/// `config/ancestries.toml`'s "human" ancestry set `min/max_lifespan_seasons = 60/200` — 3.75 to
/// 12.5 *real* years, not enough time for a marriage (or most slow-building relationship mechanics)
/// to develop before natural death. Fixed to 600/1200 (~37.5-75y), matching the already-correct
/// Tier2/elf/dwarf/dark_elf/orc/halfling values. `FamilyConfig.MarriageMinAgeSeasons` (60→240) and
/// `CharacterSimConfig.MinRulerAgeSeasons` (32→96) had the same units mismatch, also fixed. This
/// surfaced a second bug: `Defect` had no cooldown, so a character in a chronic Wellbeing crisis
/// with now-decades-long lifespans could re-select it every tick a different-civ confidant was
/// available — one seed hit 640 defections before `DefectionConfig.DefectionCooldownTicks` was
/// added (64 ticks = 4 years); the same seed now lands at 51, a real but no longer runaway count.
///
/// RivalryFormed/RivalryPlacated/RivalryEscalatedToFeud counts grew substantially (characters now
/// survive long enough to accumulate real rivalry history) and floors are now safely nonzero.
/// RivalsReconciled/CharacterEstranged/OathBroken still didn't fire in any of the 3 seeds even with
/// realistic lifespans — but CharacterMarried itself is still low-volume (0-10 over 300 years,
/// bounded by how few Tier1 "named" characters exist at once), so the marriage-hardship→Estrangement
/// and Feud→Reconciliation pathways likely just have too small a sample in a single 300-year/seed
/// run to hit yet, not a re-confirmed structural block.
///
/// **M13.8.1 update (2026-08-03):** Tier2 became a valid Bond/Marry target (mirroring
/// GrantAid/ForgiveDebt's Tier2 shortcut — see docs/phases/m13_8_tier2_relationship_exposure.md), and
/// proposing marriage to a Tier2 auto-crystallizes them to Tier1 as part of resolving the command.
/// This massively widened CharacterMarried's Tier1-scarcity bottleneck (Tier2 is the bulk background
/// population): re-observed 32-67 (was 0-10). CharacterGrieved rose too (114 in one seed, was 23-50)
/// — more marriages compounding into more crystallizations, more population, more deaths to mourn.
/// Bands widened accordingly below. Whether this rate is itself something to brake (a Notability/
/// crystallization-rate look, or a marriage-specific cooldown) rather than just re-observe is exactly
/// what M13.8.2 (Notability) and M13.8.3 (balance/perf validation) are for — not decided here.
///
/// **2026-08-03 Fear/Placate rebalance:** diagnosed why RivalsReconciled/CharacterEstranged/
/// OathBroken had never fired in ANY calibration run since M13.5, not just a small-sample
/// artifact — `FearConfig.PlacateFearThreshold` (0.4) was structurally unreachable by a same-power
/// rivalry (max achievable Fear was `RivalryBaseFearIncrement` 0.1, +`FeudFearIncrement` 0.15 =
/// 0.25) and *always* unreachable by a Tier2 rivalry (target power is always 0 — see
/// `CivTracker.TargetPower`), so `Placate` never got scored as a candidate action for either case.
/// Rebalanced: `PlacateFearThreshold` 0.4→0.05 (any formed rivalry now qualifies immediately),
/// `PlacateFearReduction` 0.3→0.05 (several successful placations drain a rivalry instead of one,
/// giving Trust room to climb before Fear hits 0 and blocks further Placate resolution),
/// `PlacateTrustGain` 0.1→0.2 (climbs to `ReconciliationTrustThreshold` in ~2 placations for a
/// plain rivalry, ~4 for a Feud — deliberately more work for the more escalated case). Re-observed:
/// RivalryPlacated 51-154 (was 0-3), RivalsReconciled 7-18 (was 0 in every prior run) — the
/// diagnosed sink now actually engages. CharacterEstranged/OathBroken remain 0 — these gate on
/// unrelated mechanics (marriage-Trust hardship drift; a narrow Tier1-Tier1-debt-vs-war-target
/// intersection) that weren't part of this diagnosis, and are left as a separate, lower-confidence
/// follow-up rather than adjusted speculatively here.
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
        // Re-calibrated 2026-08-02 after the lifespan fix (observed 69-112, was 80-320 pre-fix —
        // longer lives mean fewer deaths mean fewer replacement births).
        [EventType.CharacterBorn]   = new Band(40, 320),

        // Re-calibrated 2026-08-03 after M13.8.1's marriage-to-Tier2 crystallization: population
        // grows faster now (more marriages → more crystallizations), so more deaths to mourn.
        // Observed 114 in one seed (was 23-50 pre-13.8.1); ceiling raised with margin.
        [EventType.CharacterGrieved] = new Band(5, 150),

        // Re-calibrated 2026-08-03: M13.8.1 let a Bond goal target a Tier2 (Tier1's scarcity
        // bottleneck lifted — Tier2 is the bulk background population), and marrying one
        // auto-crystallizes them. Observed 32-67 (was 0-10 pre-13.8.1); floor now safely nonzero.
        [EventType.CharacterMarried] = new Band(20, 95),

        // M13 13.5 dispatch-bug fix (GrantAid/ForgiveDebt were never wired into
        // CharacterBehaviorPhase.ResolveCommand's switch — silently no-op'd every time the utility
        // scorer picked them). Now fires constantly via the Tier2 community-aid shortcut since
        // Tier2 needs sit near their decay floor most of the time. High-side band reflects that;
        // narrowing this is exactly the "fine details" tuning the project has deferred.
        [EventType.DebtIncurred]  = new Band(400, 3200),
        [EventType.DebtForgiven]  = new Band(120, 1100),

        // Re-calibrated 2026-08-02: the lifespan fix let a chronic-Wellbeing-crisis character
        // re-select Defect every tick a foreign confidant was available (640 defections in one
        // seed pre-DefectionCooldownTicks). With the cooldown, observed 1-51 — still occasionally
        // high for a single seed with a persistently miserable character, so ceiling stays generous.
        [EventType.CharacterDefected] = new Band(0, 80),

        // Re-calibrated 2026-08-02 after the lifespan fix. Observed 16-40 / 0-3 / 16-34 — counts
        // grew substantially now that characters survive long enough to accumulate real rivalry
        // history; floors moved off 0 since all 3 seeds now reliably form at least some rivalries.
        [EventType.RivalryFormed]          = new Band(10, 60),
        // Re-calibrated 2026-08-03 with the Fear/Placate rebalance (see class doc): Placate is now
        // reachable for any formed rivalry instead of only steep power-mismatches, so it fires far
        // more — observed 51-154 (was 0-3).
        [EventType.RivalryPlacated]        = new Band(20, 220),
        [EventType.RivalryEscalatedToFeud] = new Band(10, 55),

        // Re-calibrated 2026-08-03: the Fear/Placate rebalance above closed the gap that kept this
        // at 0 in every prior run (see class doc) — observed 7-18, now a real floor+ceiling instead
        // of a ceiling-only "no evidence this fires" band.
        [EventType.RivalsReconciled]       = new Band(3, 30),
        // Still 0 in all 3 seeds — gates on unrelated mechanics (marriage-Trust hardship drift for
        // Estrangement; a narrow Tier1-Tier1-debt-vs-war-target intersection for OathBroken) that
        // weren't part of the Fear/Placate diagnosis. Left as ceiling-only pending a separate look.
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
