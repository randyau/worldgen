using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
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

    /// <summary>
    /// M11 11.8 close-out: the local-scale generation pipeline (11.1-11.7) is deterministic
    /// end-to-end, not just per-component (11.3/11.4/11.7's unit tests already prove
    /// Amplify/Thread/Decoration are individually pure functions). Two full world-gen runs with
    /// the same seed must produce byte-identical BorderManifests, and feeding each run's own
    /// (parent TileData, BorderManifest) for the same chunk through the exact chunk-generation
    /// sequence LocalViewScreen.GenerateChunk uses (Amplify -> Thread -> Decorate) must produce
    /// identical LocalChunks.
    /// </summary>
    [Fact]
    public async Task SameSeedProducesSameBorderManifestsAndLocalChunks()
    {
        var config = new WorldConfig { Seed = 777, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();

        var (world1, manifests1) = await new WorldGenPipeline().RunFullWithManifestsAsync(config, simCfg);
        var (world2, manifests2) = await new WorldGenPipeline().RunFullWithManifestsAsync(config, simCfg);

        var manifestMap1 = manifests1.ToDictionary(m => m.Coord, m => m.Manifest);
        var manifestMap2 = manifests2.ToDictionary(m => m.Coord, m => m.Manifest);
        manifestMap1.Keys.Should().BeEquivalentTo(manifestMap2.Keys);

        // Spot-check a handful of tiles (asserting all N*N tiles' 4*64 samples would be
        // needlessly slow) — enough to prove the manifest builder is deterministic, not just
        // structurally present.
        var sampleCoords = manifestMap1.Keys.Take(5).ToList();
        foreach (var coord in sampleCoords)
        {
            var m1 = manifestMap1[coord];
            var m2 = manifestMap2[coord];
            m1.North.Should().BeEquivalentTo(m2.North, $"North edge at {coord} must match across seed-{config.Seed} runs");
            m1.South.Should().BeEquivalentTo(m2.South, $"South edge at {coord} must match across seed-{config.Seed} runs");
            m1.East.Should().BeEquivalentTo(m2.East, $"East edge at {coord} must match across seed-{config.Seed} runs");
            m1.West.Should().BeEquivalentTo(m2.West, $"West edge at {coord} must match across seed-{config.Seed} runs");
        }

        // End-to-end chunk generation: same sequence LocalViewScreen.GenerateChunk runs.
        var localGenConfig = simCfg.LocalGen;
        var worldTile = sampleCoords[0];
        var parentTile1 = world1.TileGrid.GetTile(worldTile);
        var parentTile2 = world2.TileGrid.GetTile(worldTile);
        var chunkCoord = new ChunkCoord(worldTile, 1, 1);

        LocalChunk GenerateChunk(TileData parentTile, BorderManifest manifest)
        {
            var chunk = LocalTerrainAmplifier.Amplify(chunkCoord, parentTile, manifest, config.Seed, localGenConfig);
            LocalRiverThreader.Thread(chunk, chunkCoord, parentTile, manifest, localGenConfig);
            LocalDecorationGenerator.Generate(chunk, chunkCoord, (BiomeType)parentTile.BiomeType, config.Seed, localGenConfig);
            return chunk;
        }

        var chunk1 = GenerateChunk(parentTile1, manifestMap1[worldTile]);
        var chunk2 = GenerateChunk(parentTile2, manifestMap2[worldTile]);

        foreach (var (local, _) in chunk1.AllTiles())
        {
            chunk1.GetTile(local).Should().BeEquivalentTo(chunk2.GetTile(local),
                $"local cell {local} of chunk {chunkCoord} must be identical across seed-{config.Seed} runs");
        }
    }
}
