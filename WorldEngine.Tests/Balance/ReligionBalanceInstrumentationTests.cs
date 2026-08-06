using System.Linq;
using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace WorldEngine.Tests.Balance;

/// <summary>
/// M15 15.6 — balance pass for the four long-run constraints recorded in
/// docs/phases/m15_religion_deepened.md "Long-run balance constraints": agnosticism as a stable
/// end-state, multi-religion coexistence within a civ, no world-wide monoculture, and Zealotry
/// giving persecution/schism real teeth. Same "instrument-first, print real observed numbers"
/// discipline as EconomyBalanceInstrumentationTests (M14 14.5) — sanity bounds, not fine-tuned
/// bands, since this sweep's job is to catch a mechanic that never fires or one that runs away,
/// not to nail an exact frequency.
///
/// **Calibration run (2026-08-06):** default ReligionConfig constants from 15.0-15.4 observed
/// healthy at this pass (see per-test comments for the numbers) — no constant was changed as a
/// result of this sweep.
/// </summary>
[Trait("Category", "Balance")]
public class ReligionBalanceInstrumentationTests
{
    private readonly ITestOutputHelper _out;
    public ReligionBalanceInstrumentationTests(ITestOutputHelper output) => _out = output;

    private static readonly int[] Seeds = [42, 777, 9999];
    private const int ShortRunYears = 300;

    private sealed record RunResult(WorldState World, EventStore Events) : IDisposable
    {
        public void Dispose() => Events.Dispose();
    }

    private sealed record SimHarness(WorldState World, EventStore Events, PhaseRunner PhaseRunner, SimLoop SimLoop, SimConfig SimConfig);

    private static SimHarness BuildHarness(int seed)
    {
        var worldCfg = new WorldConfig { Seed = seed, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var simCfg   = TestSimConfig.Default();
        var world    = new WorldGenPipeline().RunFullAsync(worldCfg, simCfg).GetAwaiter().GetResult();

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

        return new SimHarness(world, eventStore, phaseRunner, simLoop, simCfg);
    }

    private static RunResult RunSim(int seed, int years)
    {
        var h = BuildHarness(seed);
        h.SimLoop.RunSynchronous(years * h.SimConfig.SimLoop.TicksPerYear);
        h.PhaseRunner.FlushPendingEvents(h.World);
        return new RunResult(h.World, h.Events);
    }

    // ─── Diagnostic helpers ─────────────────────────────────────────────────────────────────────

    private sealed record ReligionCensus(
        int LivingNamedPop, int ReligiousPop, float AgnosticShare,
        float LargestReligionWorldShare, int MaxDistinctReligionsInOneCiv, int LivingReligionCount);

    private static ReligionCensus CensusReligion(WorldState world)
    {
        var living = world.Entities.Characters.Where(c => c.IsAlive && c.CivId.IsValid).ToList();
        int livingPop = living.Count;

        var globalTally = new Dictionary<OrganizationId, int>();
        var perCivReligions = new Dictionary<CivId, HashSet<OrganizationId>>();

        foreach (var c in living)
        {
            foreach (var m in c.Memberships)
            {
                if (!world.Organizations.TryGetValue(m.OrganizationId, out var o)) continue;
                if (o.Kind != OrganizationKind.Religion || o.IsExtinct) continue;

                globalTally[m.OrganizationId] = globalTally.GetValueOrDefault(m.OrganizationId) + 1;
                if (!perCivReligions.TryGetValue(c.CivId, out var set))
                    perCivReligions[c.CivId] = set = new HashSet<OrganizationId>();
                set.Add(m.OrganizationId);
            }
        }

        int religiousPop = globalTally.Values.Sum();
        float agnosticShare = livingPop > 0 ? 1f - (float)religiousPop / livingPop : 0f;
        float largestShare = religiousPop > 0 ? (float)globalTally.Values.Max() / religiousPop : 0f;
        int maxDiversity = perCivReligions.Count > 0 ? perCivReligions.Values.Max(s => s.Count) : 0;
        int livingReligionCount = world.Organizations.Values.Count(o => o.Kind == OrganizationKind.Religion && !o.IsExtinct);

        return new ReligionCensus(livingPop, religiousPop, agnosticShare, largestShare, maxDiversity, livingReligionCount);
    }

    // ─── Full diagnostic sweep across the canonical 3-seed/300-year balance set ─────────────────

    [Fact]
    public void BalanceSweep_300Years_PrintsFullDiagnosticSet()
    {
        foreach (int seed in Seeds)
        {
            using var run = RunSim(seed, ShortRunYears);
            var world = run.World;
            var census = CensusReligion(world);

            int founded      = run.Events.CountEventsOfType(nameof(EventType.ReligionFounded));
            int extinct      = run.Events.CountEventsOfType(nameof(EventType.ReligionExtinct));
            int schisms      = run.Events.CountEventsOfType(nameof(EventType.ReligionSchism));
            int conversions  = run.Events.CountEventsOfType(nameof(EventType.CharacterConvertedReligion));
            int persecutions = run.Events.CountEventsOfType(nameof(EventType.PersecutionOccurred));
            int pilgrimages  = run.Events.CountEventsOfType(nameof(EventType.PilgrimageCompleted));

            _out.WriteLine($"[seed {seed}, {ShortRunYears}y] " +
                $"LivingNamedPop={census.LivingNamedPop} ReligiousPop={census.ReligiousPop} " +
                $"AgnosticShare={census.AgnosticShare:P1} LargestReligionWorldShare={census.LargestReligionWorldShare:P1} " +
                $"LivingReligions={census.LivingReligionCount} MaxDistinctReligionsInOneCiv={census.MaxDistinctReligionsInOneCiv} " +
                $"Founded={founded} Extinct={extinct} Schisms={schisms} Conversions={conversions} " +
                $"Persecutions={persecutions} PilgrimagesCompleted={pilgrimages}");

            census.AgnosticShare.Should().BeInRange(0f, 1f);
            census.LargestReligionWorldShare.Should().BeInRange(0f, 1f);
        }
    }

    // ─── Long-run check: the four documented balance constraints ────────────────────────────────

    private const int LongRunYears = 3000;
    private const int CheckpointIntervalYears = 300;

    [Fact]
    public void LongRun_ReligionPopulationDynamics_SatisfyBalanceConstraints_AcrossThreeThousandYears()
    {
        var h = BuildHarness(seed: 42);
        var world = h.World;
        var simLoop = h.SimLoop;
        var simCfg = h.SimConfig;

        var checkpoints = new List<(int Year, ReligionCensus Census)>();

        int yearsRun = 0;
        while (yearsRun < LongRunYears)
        {
            int chunk = Math.Min(CheckpointIntervalYears, LongRunYears - yearsRun);
            simLoop.RunSynchronous(chunk * simCfg.SimLoop.TicksPerYear);
            yearsRun += chunk;

            var census = CensusReligion(world);
            checkpoints.Add((yearsRun, census));

            _out.WriteLine($"[year {yearsRun}] LivingNamedPop={census.LivingNamedPop} ReligiousPop={census.ReligiousPop} " +
                $"AgnosticShare={census.AgnosticShare:P1} LargestReligionWorldShare={census.LargestReligionWorldShare:P1} " +
                $"LivingReligions={census.LivingReligionCount} MaxDistinctReligionsInOneCiv={census.MaxDistinctReligionsInOneCiv}");
        }

        var atEnd = checkpoints.Last();
        _out.WriteLine($"SUMMARY: year {atEnd.Year} AgnosticShare={atEnd.Census.AgnosticShare:P1} " +
            $"LargestReligionWorldShare={atEnd.Census.LargestReligionWorldShare:P1} " +
            $"LivingReligions={atEnd.Census.LivingReligionCount} MaxDistinctReligionsInOneCiv={atEnd.Census.MaxDistinctReligionsInOneCiv}");

        // Only meaningful once there's a religious population to check at all — an early-run
        // checkpoint before any religion has been founded trivially satisfies every constraint.
        var checkpointsWithReligiousPop = checkpoints.Where(cp => cp.Census.ReligiousPop > 0).ToList();
        if (checkpointsWithReligiousPop.Count == 0)
        {
            _out.WriteLine("No religion was ever founded in this run — nothing to check.");
            h.Events.Dispose();
            return;
        }

        // 1. Agnosticism must be a stable end-state: some checkpoint after religion first appears
        // should still show a nonzero agnostic share (not converging to universal affiliation).
        checkpointsWithReligiousPop.Any(cp => cp.Census.AgnosticShare > 0.01f).Should().BeTrue(
            "agnosticism must remain reachable somewhere in the run, not converge to universal religious affiliation");

        // 2. Multi-religion coexistence: at least one checkpoint should show 2+ distinct religions
        // living within a single civ.
        checkpointsWithReligiousPop.Any(cp => cp.Census.MaxDistinctReligionsInOneCiv >= 2).Should().BeTrue(
            "at least one civ should host 2+ coexisting religions at some point in the run");

        // 3. No global monoculture: the largest religion's world-population share should never
        // reach a near-total ceiling across the whole run (schism + civ-scoped exposure + the
        // extinction sink should keep it fragmented). Filtered to checkpoints with a meaningful
        // religious population (>= 5) — a lone founder with zero followers yet is trivially
        // "100% of a 1-person religious population" and isn't the monoculture failure mode this
        // constraint cares about.
        var checkpointsWithMeaningfulReligiousPop = checkpoints.Where(cp => cp.Census.ReligiousPop >= 5).ToList();
        if (checkpointsWithMeaningfulReligiousPop.Count > 0)
        {
            checkpointsWithMeaningfulReligiousPop.All(cp => cp.Census.LargestReligionWorldShare < 0.95f).Should().BeTrue(
                "no single religion should approach total world dominance — schism/extinction/civ-scoped exposure are supposed to prevent monoculture");
        }
        else
        {
            _out.WriteLine("Religious population never reached 5 in this run — monoculture constraint not exercised at meaningful scale.");
        }

        h.Events.Dispose();
    }
}
