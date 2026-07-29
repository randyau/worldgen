using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// M11 11.8 close-out: full persistence round-trip for the local-scale generation pipeline —
/// proves that after a save/load cycle (simulated by writing manifests.bin + world.db to a real
/// scratch directory, then re-opening them as a fresh process would), a chunk's base terrain
/// regenerates identically and its delta overlay survives. Complements
/// ModifyLocalTileCommandTests (11.5, proves the write path through the real command/SimLoop
/// pipeline) and ReproducibilityTests.SameSeedProducesSameBorderManifestsAndLocalChunks (11.8,
/// proves same-seed determinism) — this test is the one that exercises actual file I/O.
/// </summary>
public class LocalScalePersistenceTests : IDisposable
{
    private readonly string _saveDir;

    public LocalScalePersistenceTests()
    {
        _saveDir = Path.Combine(Path.GetTempPath(), $"worldsave_local_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_saveDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_saveDir))
            Directory.Delete(_saveDir, recursive: true);
    }

    [Fact]
    public async Task ManifestsAndDeltas_SurviveSaveLoad_AndRegenerateIdenticalBaseTerrain()
    {
        var worldConfig = new WorldConfig { Seed = 4242, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var simConfig   = TestSimConfig.Default();

        var (world, manifests) = await new WorldGenPipeline().RunFullWithManifestsAsync(worldConfig, simConfig);
        var manifestMap = manifests.ToDictionary(m => m.Coord, m => m.Manifest);
        var worldTile   = manifestMap.Keys.First();
        var chunkCoord  = new ChunkCoord(worldTile, 2, 2);
        var parentTile  = world.TileGrid.GetTile(worldTile);

        // "Save": write manifests.bin + a modified cell to world.db, exactly as Game1.StartSim /
        // ModifyLocalTile do for a fresh world.
        var manifestsPath = Path.Combine(_saveDir, "manifests.bin");
        BorderManifestStore.WriteToFile(manifestsPath, manifestMap.Select(kv => (kv.Key, kv.Value)));

        var dbPath = Path.Combine(_saveDir, "world.db");
        var modifiedCell = new LocalTileCoord(5, 5);
        using (var store = new EventStore(dbPath))
        {
            store.WriteLocalTileDelta(new LocalTileDelta(
                chunkCoord, modifiedCell, LocalChangeType.CellOverride, """{"Elevation":250}"""));
        }

        // Base terrain generated once, before any "restart" — the baseline to compare against.
        LocalChunk GenerateBase(BorderManifest manifest) =>
            GenerateChunk(chunkCoord, parentTile, manifest, worldConfig.Seed, simConfig.LocalGen);

        var chunkBeforeRestart = GenerateBase(manifestMap[worldTile]);

        // "Load": a fresh process/session reads manifests.bin and re-opens world.db independently.
        var reloadedManifests = BorderManifestStore.LoadFromFile(manifestsPath).ToDictionary(m => m.Item1, m => m.Item2);
        reloadedManifests.Should().ContainKey(worldTile);

        var chunkAfterRestart = GenerateBase(reloadedManifests[worldTile]);

        foreach (var (local, _) in chunkBeforeRestart.AllTiles())
        {
            chunkAfterRestart.GetTile(local).Should().BeEquivalentTo(chunkBeforeRestart.GetTile(local),
                $"base terrain for cell {local} must regenerate identically after manifests.bin round-trips through disk");
        }

        using (var reloadedStore = new EventStore(dbPath))
        {
            var reloadedDeltas = reloadedStore.LoadLocalTileDeltas(chunkCoord);
            reloadedDeltas.Should().ContainSingle("the delta written before \"restart\" must still be in world.db");

            LocalTileDeltaApplier.Apply(chunkAfterRestart, reloadedDeltas);
            chunkAfterRestart.GetTile(modifiedCell).Elevation.Should().Be(250,
                "the persisted delta must still override this cell after the base chunk was regenerated fresh");
        }
    }

    private static LocalChunk GenerateChunk(
        ChunkCoord coord, WorldEngine.Sim.Tiles.TileData parentTile, BorderManifest manifest,
        int worldSeed, LocalGenConfig config)
    {
        var chunk = LocalTerrainAmplifier.Amplify(coord, parentTile, manifest, worldSeed, config);
        LocalRiverThreader.Thread(chunk, coord, parentTile, manifest, config);
        LocalDecorationGenerator.Generate(chunk, coord, (BiomeType)parentTile.BiomeType, worldSeed, config);
        return chunk;
    }
}
