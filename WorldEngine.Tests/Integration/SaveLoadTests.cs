using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using FluentAssertions;

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

    // ── Test 9: KnownCivs and PendingEmissaries round-trip (M4 Phase 1) ─────

    [Fact]
    public void WorldStateSaver_RoundTrip_KnownCivsAndPendingEmissaries()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);
        var simCfg = TestSimConfig.Default();

        // Build two minimal civs with a known-civ relationship
        var civ1Id = new CivId(1);
        var civ2Id = new CivId(2);
        var capital1 = new TileCoord(5, 5);
        var capital2 = new TileCoord(20, 5);

        var founder1 = new WorldEngine.Sim.Entities.Characters.Tier1Character(
            new EntityId(101L), capital1,
            WorldEngine.Sim.Entities.Characters.PersonalityVector.Default,
            WorldEngine.Sim.Entities.Characters.AptitudeVector.Default,
            WorldEngine.Sim.Entities.Characters.SkillVector.Default,
            new WorldEngine.Sim.Entities.Characters.IdentityData("Ruler1", "the First", "test",
                null, null, civ1Id, 0, 0),
            100, 200);
        var founder2 = new WorldEngine.Sim.Entities.Characters.Tier1Character(
            new EntityId(102L), capital2,
            WorldEngine.Sim.Entities.Characters.PersonalityVector.Default,
            WorldEngine.Sim.Entities.Characters.AptitudeVector.Default,
            WorldEngine.Sim.Entities.Characters.SkillVector.Default,
            new WorldEngine.Sim.Entities.Characters.IdentityData("Ruler2", "the Second", "test",
                null, null, civ2Id, 0, 0),
            100, 200);
        world.Entities.Add(founder1);
        world.Entities.Add(founder2);

        var civ1 = new Civilization(civ1Id, "Civ1", founder1.Id, capital1, 0);
        var civ2 = new Civilization(civ2Id, "Civ2", founder2.Id, capital2, 0);
        world.Civilizations[civ1Id] = civ1;
        world.Civilizations[civ2Id] = civ2;

        // Seed a contact and a pending emissary
        civ1.KnownCivs[civ2Id] = new CivContact(
            civ2Id, YearFirstContact: 0, YearLastContact: 1,
            CivContactSource.WandererMet, capital2, Confidence: 0.75f);

        world.PendingEmissaries.Add(new PendingEmissary(
            FromCiv: civ1Id, ToCiv: civ2Id,
            Purpose: EmissaryPurpose.Trade,
            DepartedYear: 0, ArrivalYear: 3, SurvivalChance: 0.8f));

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        // KnownCivs round-trip
        loaded.Civilizations.Should().ContainKey(civ1Id);
        var loadedCiv1 = loaded.Civilizations[civ1Id];
        loadedCiv1.KnownCivs.Should().ContainKey(civ2Id, "KnownCivs must survive save/load");
        var contact = loadedCiv1.KnownCivs[civ2Id];
        contact.BestSource.Should().Be(CivContactSource.WandererMet);
        contact.Confidence.Should().BeApproximately(0.75f, 0.001f);
        contact.CapitalTile.Should().Be(capital2);

        // PendingEmissaries round-trip
        loaded.PendingEmissaries.Should().HaveCount(1, "one pending emissary must survive save/load");
        var em = loaded.PendingEmissaries[0];
        em.FromCiv.Should().Be(civ1Id);
        em.ToCiv.Should().Be(civ2Id);
        em.Purpose.Should().Be(EmissaryPurpose.Trade);
        em.ArrivalYear.Should().Be(3);
        em.SurvivalChance.Should().BeApproximately(0.8f, 0.001f);
    }
}
