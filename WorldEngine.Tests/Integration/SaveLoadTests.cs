using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Round-trip tests for Phase 3.6 save/load system.
/// </summary>
public class SaveLoadTests : IDisposable
{
    // Scratch dir under system temp so parallel test runs don't collide
    private readonly string _saveDir;

    public SaveLoadTests()
    {
        _saveDir = Path.Combine(Path.GetTempPath(), $"worldsave_test_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        WorldStateSaver.DeleteSave(_saveDir);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Generate a small world and run it for a few ticks so state is non-trivial.</summary>
    private static WorldState BuildAndRunWorld(int ticks = 200)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 77);
        var simCfg = TestSimConfig.Default();

        // Suppress auto-save during test runs
        simCfg.SimLoop.AutoSaveIntervalTicks = int.MaxValue;

        var cmdQueue       = new CommandQueue();
        var stateCache     = new StateCache();
        var eventStore     = new EventStore();
        var eventCache     = new EventCache();
        var phaseRunner    = new PhaseRunner(simCfg, eventStore, eventCache);
        var snapBuilder    = new SnapshotBuilder();
        var loop           = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapBuilder, simCfg, eventCache);

        cmdQueue.Enqueue(new WorldEngine.Sim.Commands.SetSimSpeed(WorldEngine.Sim.Core.SimSpeed.Ultrafast));
        loop.Start();
        // Let it run for a bit
        Thread.Sleep(500);
        loop.Stop();

        return world;
    }

    // ── Test 1: files created ─────────────────────────────────────────────────

    [Fact]
    public void WorldStateSaver_Save_CreatesExpectedFiles()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 1);
        var simCfg = TestSimConfig.Default();

        WorldStateSaver.Save(world, _saveDir, simCfg);

        Assert.True(File.Exists(Path.Combine(_saveDir, "meta.json")),  "meta.json missing");
        Assert.True(File.Exists(Path.Combine(_saveDir, "state.bin")),  "state.bin missing");
        Assert.True(WorldStateSaver.HasSave(_saveDir), "HasSave returned false");
    }

    // ── Test 2: year restored ─────────────────────────────────────────────────

    [Fact]
    public void WorldStateSaver_Load_RestoresYear()
    {
        var world  = BuildAndRunWorld(ticks: 200);
        var simCfg = TestSimConfig.Default();
        int savedYear = world.CurrentYear;

        WorldStateSaver.Save(world, _saveDir, simCfg);

        var loaded = WorldStateSaver.Load(_saveDir, simCfg);
        Assert.Equal(savedYear, loaded.CurrentYear);
    }

    // ── Test 3: settlements restored ─────────────────────────────────────────

    [Fact]
    public void WorldStateSaver_Load_RestoresSettlements()
    {
        var world  = BuildAndRunWorld(ticks: 200);
        var simCfg = TestSimConfig.Default();
        int settCount = world.Settlements.Count;

        WorldStateSaver.Save(world, _saveDir, simCfg);

        var loaded = WorldStateSaver.Load(_saveDir, simCfg);
        Assert.Equal(settCount, loaded.Settlements.Count);
    }

    // ── Test 4: entities restored ─────────────────────────────────────────────

    [Fact]
    public void WorldStateSaver_Load_RestoresEntities()
    {
        var world  = BuildAndRunWorld(ticks: 200);
        var simCfg = TestSimConfig.Default();
        int entityCount = world.Entities.Count;

        WorldStateSaver.Save(world, _saveDir, simCfg);

        var loaded = WorldStateSaver.Load(_saveDir, simCfg);
        Assert.Equal(entityCount, loaded.Entities.Count);
    }

    // ── Test 5: territory map restored ───────────────────────────────────────

    [Fact]
    public void WorldStateSaver_Load_RestoresTerritoryMap()
    {
        var world  = BuildAndRunWorld(ticks: 200);
        var simCfg = TestSimConfig.Default();
        int tileCount = world.TerritoryMap.Count;

        WorldStateSaver.Save(world, _saveDir, simCfg);

        var loaded = WorldStateSaver.Load(_saveDir, simCfg);
        Assert.Equal(tileCount, loaded.TerritoryMap.Count);
    }

    // ── Test 6: round-trip identical state ───────────────────────────────────

    [Fact]
    public void WorldStateSaver_RoundTrip_IdenticalState()
    {
        var world  = BuildAndRunWorld(ticks: 200);
        var simCfg = TestSimConfig.Default();

        int yearBefore        = world.CurrentYear;
        long tickBefore       = world.CurrentTick;
        int entityCountBefore = world.Entities.Count;
        int settCountBefore   = world.Settlements.Count;
        int civCountBefore    = world.Civilizations.Count;
        int terrTilesBefore   = world.TerritoryMap.Count;

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        Assert.Equal(yearBefore,        loaded.CurrentYear);
        Assert.Equal(tickBefore,        loaded.CurrentTick);
        Assert.Equal(entityCountBefore, loaded.Entities.Count);
        Assert.Equal(settCountBefore,   loaded.Settlements.Count);
        Assert.Equal(civCountBefore,    loaded.Civilizations.Count);
        Assert.Equal(terrTilesBefore,   loaded.TerritoryMap.Count);
    }

    // ── Test 7: meta read ─────────────────────────────────────────────────────

    [Fact]
    public void WorldStateSaver_ReadMeta_ReturnsSavedYear()
    {
        var world  = BuildAndRunWorld(ticks: 64);
        var simCfg = TestSimConfig.Default();
        int savedYear = world.CurrentYear;

        WorldStateSaver.Save(world, _saveDir, simCfg);

        var meta = WorldStateSaver.ReadMeta(_saveDir);
        Assert.NotNull(meta);
        Assert.Equal(savedYear, meta!.SavedYear);
        Assert.Equal(WorldStateSaver.FormatVersion, meta.FormatVersion);
    }

    // ── Test 8: empty world (no entities yet) ────────────────────────────────

    [Fact]
    public void WorldStateSaver_Save_EmptyWorld_NoThrow()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 99);
        var simCfg = TestSimConfig.Default();

        // Should not throw even with no entities/settlements
        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);
        Assert.Equal(world.CurrentYear, loaded.CurrentYear);
    }
}
