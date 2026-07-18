using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Reproducibility;

public class ReproducibilityTests
{
    [Fact]
    public async Task SameSeedProducesSameWorld()
    {
        var config = new WorldConfig { Seed = 12345, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();

        var w1 = await new WorldGenPipeline().RunFullAsync(config, simCfg);
        var w2 = await new WorldGenPipeline().RunFullAsync(config, simCfg);

        int tileCount = w1.TileGrid.TileWidth * w1.TileGrid.TileHeight;

        for (int y = 0; y < w1.TileGrid.TileHeight; y++)
        {
            for (int x = 0; x < w1.TileGrid.TileWidth; x++)
            {
                var coord = new TileCoord(x, y);
                var t1 = w1.TileGrid.GetTile(coord);
                var t2 = w2.TileGrid.GetTile(coord);

                t1.Should().BeEquivalentTo(t2,
                    $"tile ({x},{y}) must be identical across two runs with seed 12345");
            }
        }

        w1.SeasonalProfiles.Should().BeEquivalentTo(w2.SeasonalProfiles,
            "seasonal profiles must be identical across runs");
    }

    /// <summary>
    /// C0a: Two in-process sim runs with the same seed must produce identical final
    /// world_population, active_civs, tier1_count, and tier2_count after N years.
    /// This validates that EntityId assignment is now deterministic (no longer
    /// driven by the process-global IdGenerator counter).
    /// </summary>
    [Fact]
    public void SameSeedProducesIdenticalSimMetrics_InProcess()
    {
        const int seed     = 77777;
        const int yearsToRun = 30;

        static (int pop, int civs, int t1, int t2) RunOnce()
        {
            var cfg    = new WorldConfig { Seed = seed, WidthKm = 200, HeightKm = 200, TileWidthKm = 10 };
            var simCfg = TestSimConfig.Default();
            var world  = new WorldGenPipeline().RunFullAsync(cfg, simCfg).GetAwaiter().GetResult();

            var eventStore   = new EventStore(":memory:");
            var eventCache   = new EventCache(simCfg.Events.RecentEventCacheSize);
            var gate         = new EventGate(simCfg);
            var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
            var phaseRunner  = new PhaseRunner(simCfg, eventStore, eventCache, gate,
                beastCatalog: beastCatalog);

            foreach (var pe in BeastSpawner.SpawnAll(world, beastCatalog))  phaseRunner.InjectPendingEvent(pe);
            foreach (var pe in CharacterSpawner.SpawnAll(world, simCfg))    phaseRunner.InjectPendingEvent(pe);
            foreach (var pe in Tier2Spawner.SpawnAll(world, simCfg))        phaseRunner.InjectPendingEvent(pe);

            var cmdQueue        = new CommandQueue();
            var stateCache      = new StateCache();
            var snapshotBuilder = new SnapshotBuilder();
            var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simCfg, eventCache);

            simLoop.RunSynchronous(yearsToRun * simCfg.SimLoop.TicksPerYear);
            phaseRunner.FlushPendingEvents(world);
            eventStore.Dispose();

            int pop   = world.Settlements.Values.Sum(s => s.Population);
            int civs  = world.Civilizations.Values.Count(c => !c.IsCollapsed);
            int t1    = world.Entities.Characters.Count;
            int t2    = world.Entities.Tier2Chars.Count;
            return (pop, civs, t1, t2);
        }

        var (pop1, civs1, t1_1, t2_1) = RunOnce();
        var (pop2, civs2, t1_2, t2_2) = RunOnce();

        pop1.Should().Be(pop2,
            $"world_population must be identical across two in-process runs with seed {seed}");
        civs1.Should().Be(civs2,
            $"active_civs must be identical across two in-process runs with seed {seed}");
        t1_1.Should().Be(t1_2,
            $"tier1_count must be identical across two in-process runs with seed {seed}");
        t2_1.Should().Be(t2_2,
            $"tier2_count must be identical across two in-process runs with seed {seed}");
    }
}
