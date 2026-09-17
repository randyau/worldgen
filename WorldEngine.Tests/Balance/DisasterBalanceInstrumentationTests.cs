using System.Linq;
using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
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
/// M16 16.3 — balance pass for the consequences wired onto disasters in 16.0/16.1/16.1b.
/// Same "instrument-first, print real observed numbers" discipline as
/// EconomyBalanceInstrumentationTests/ReligionBalanceInstrumentationTests: sanity bounds, not
/// fine-tuned bands. The thing this sweep exists to catch is a mechanic that never fires
/// (default config too conservative to ever matter) or one that runs away (default config
/// civilization-ending) — not to nail an exact frequency.
/// </summary>
[Trait("Category", "Balance")]
public class DisasterBalanceInstrumentationTests
{
    private readonly ITestOutputHelper _out;
    public DisasterBalanceInstrumentationTests(ITestOutputHelper output) => _out = output;

    private static readonly int[] Seeds = [42, 777, 9999];
    private const int RunYears = 300;

    private sealed record RunResult(WorldState World, EventStore Events) : IDisposable
    {
        public void Dispose() => Events.Dispose();
    }

    private static RunResult RunSim(int seed, int years)
    {
        IdGenerator.ResetForTests();
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

        simLoop.RunSynchronous(years * simCfg.SimLoop.TicksPerYear);
        phaseRunner.FlushPendingEvents(world);
        return new RunResult(world, eventStore);
    }

    [Fact]
    public void BalanceSweep_300Years_DisasterConsequencesFireWithoutEndingCivilizations()
    {
        int totalImprovementsDestroyed = 0, totalSettlementsDamaged = 0, totalBlights = 0, totalHarshWinters = 0;
        int totalDisasterRuins = 0;

        foreach (int seed in Seeds)
        {
            using var run = RunSim(seed, RunYears);
            var world = run.World;

            int improvementsDestroyed = run.Events.CountEventsOfType(nameof(EventType.ImprovementDestroyed));
            int settlementsDamaged    = run.Events.CountEventsOfType(nameof(EventType.SettlementDamagedByDisaster));
            int blights               = run.Events.CountEventsOfType(nameof(EventType.BlightBegan));
            int harshWinters          = run.Events.CountEventsOfType(nameof(EventType.HarshWinterBegan));
            int disasterRuins         = world.Ruins.Values.Count(r => r.Cause == "disaster");
            int livingCivs            = world.Civilizations.Values.Count(c => !c.IsCollapsed);
            int settlementsRemaining  = world.Settlements.Count;

            _out.WriteLine($"[seed {seed}, {RunYears}y] ImprovementsDestroyed={improvementsDestroyed} " +
                $"SettlementsDamaged={settlementsDamaged} DisasterRuins={disasterRuins} Blights={blights} " +
                $"HarshWinters={harshWinters} LivingCivs={livingCivs} SettlementsRemaining={settlementsRemaining}");

            // Not civilization-ending: at least one civ and one settlement should still exist
            // after 300 years of default-config disasters layered on top of everything else.
            livingCivs.Should().BeGreaterThan(0,
                $"[seed {seed}] disasters should not be able to wipe out every civilization at default config");
            settlementsRemaining.Should().BeGreaterThan(0,
                $"[seed {seed}] disasters should not be able to wipe out every settlement at default config");

            totalImprovementsDestroyed += improvementsDestroyed;
            totalSettlementsDamaged    += settlementsDamaged;
            totalBlights               += blights;
            totalHarshWinters          += harshWinters;
            totalDisasterRuins         += disasterRuins;
        }

        _out.WriteLine($"TOTAL across {Seeds.Length} seeds: ImprovementsDestroyed={totalImprovementsDestroyed} " +
            $"SettlementsDamaged={totalSettlementsDamaged} DisasterRuins={totalDisasterRuins} " +
            $"Blights={totalBlights} HarshWinters={totalHarshWinters}");

        // Meaningful: the consequences wired in 16.0/16.1/16.1b should actually fire somewhere
        // across 900 settlement-years of simulation, not sit dead at default config.
        (totalImprovementsDestroyed + totalSettlementsDamaged + totalDisasterRuins)
            .Should().BeGreaterThan(0, "16.0's settlement/improvement consequences should fire at least once across 3 seeds x 300 years");
        totalHarshWinters.Should().BeGreaterThan(0,
            "harsh winter (3%/year) should fire at least once across 3 seeds x 300 years");
    }
}
