using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Proves the ModifyLocalTile pipeline end-to-end (M11 11.5): UI enqueues the command,
/// CommandQueue delivers it to SimLoop, SimLoop's private command switch resolves it by writing
/// to world.db via PhaseRunner/EventStore — the same real path SpotlightCommandTests uses for
/// commands whose resolution isn't reachable any other way.
/// </summary>
public class ModifyLocalTileCommandTests
{
    private static WorldState BuildWorld(int seed = 1)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    private static (SimLoop loop, CommandQueue cmdQueue, EventStore store) MakeLoop(WorldState world)
    {
        var sim = TestSimConfig.Default();
        sim.SimLoop.UltrafastTicksPerSecond = float.MaxValue;
        sim.SimLoop.UltrafastSnapshotIntervalTicks = 1;

        var cmdQueue        = new CommandQueue();
        var cache           = new StateCache();
        var eventStore      = new EventStore();
        var eventCache      = new EventCache();
        var phaseRunner     = new PhaseRunner(sim, eventStore, eventCache);
        var snapshotBuilder = new SnapshotBuilder();
        var loop = new SimLoop(world, cmdQueue, cache, phaseRunner, snapshotBuilder, sim, eventCache);

        cmdQueue.Enqueue(new SetSimSpeed(SimSpeed.Ultrafast));
        return (loop, cmdQueue, eventStore);
    }

    [Fact]
    public async Task ModifyLocalTile_WritesDeltaToWorldDb()
    {
        var world = BuildWorld();
        var (loop, cmdQueue, store) = MakeLoop(world);
        var chunk = new ChunkCoord(new TileCoord(2, 2), 1, 1);
        var local = new LocalTileCoord(7, 8);
        var payload = """{"Elevation":200}""";

        loop.Start();
        cmdQueue.Enqueue(new ModifyLocalTile(chunk, local, LocalChangeType.CellOverride, payload));
        await Task.Delay(150);
        loop.Stop();

        var deltas = store.LoadLocalTileDeltas(chunk);
        deltas.Should().ContainSingle();
        deltas[0].Local.Should().Be(local);
        deltas[0].ChangeType.Should().Be(LocalChangeType.CellOverride);
        deltas[0].PayloadJson.Should().Be(payload);
    }

    [Fact]
    public async Task ModifyLocalTile_RoundTripsThroughApplierOntoARegeneratedChunk()
    {
        // The whole point of the pipeline: a delta written via the command path must still be
        // visible after the base chunk is thrown away and regenerated from scratch.
        var world = BuildWorld();
        var (loop, cmdQueue, store) = MakeLoop(world);
        var chunk = new ChunkCoord(new TileCoord(2, 2), 1, 1);
        var local = new LocalTileCoord(3, 3);

        loop.Start();
        cmdQueue.Enqueue(new ModifyLocalTile(chunk, local, LocalChangeType.CellOverride, """{"Elevation":222}"""));
        await Task.Delay(150);
        loop.Stop();

        var config = TestSimConfig.Default();
        var freshChunk = new LocalChunk(chunk, config.LocalGen.ChunkSizeTiles);
        foreach (var (c, _) in freshChunk.AllTiles())
            freshChunk.SetTile(c, new LocalTileData { Elevation = 5 });

        LocalTileDeltaApplier.Apply(freshChunk, store.LoadLocalTileDeltas(chunk));

        freshChunk.GetTile(local).Elevation.Should().Be(222);
    }
}
