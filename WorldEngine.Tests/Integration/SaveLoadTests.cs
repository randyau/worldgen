using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles.LocalScale;
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
                null, null, 0, 0),
            100, 200);
        var founder2 = new WorldEngine.Sim.Entities.Characters.Tier1Character(
            new EntityId(102L), capital2,
            WorldEngine.Sim.Entities.Characters.PersonalityVector.Default,
            WorldEngine.Sim.Entities.Characters.AptitudeVector.Default,
            WorldEngine.Sim.Entities.Characters.SkillVector.Default,
            new WorldEngine.Sim.Entities.Characters.IdentityData("Ruler2", "the Second", "test",
                null, null, 0, 0),
            100, 200);
        founder1.WithCiv(civ1Id);
        founder2.WithCiv(civ2Id);
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

    [Fact]
    public void WorldStateSaver_RoundTrip_Organizations()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);
        var simCfg = TestSimConfig.Default();

        var civId = new CivId(1);
        var capital = new TileCoord(5, 5);
        var ruler = new Tier1Character(
            new EntityId(101L), capital,
            PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
            new IdentityData("Ruler1", "the First", "test", null, null, 0, 0),
            100, 200);
        world.Entities.Add(ruler);

        var civ = new Civilization(civId, "Civ1", ruler.Id, capital, 0);
        world.Civilizations[civId] = civ;

        var orgId = new OrganizationId(1);
        var otherOrgId = new OrganizationId(2);
        var org = new Organization(orgId, OrganizationKind.Civilization, "Civ1", ruler.Id, 0);
        var membership = new Membership(orgId, OrganizationRole.Leader, 1.0f, civId);
        org.Members[ruler.Id] = membership;
        org.Allies.Add(otherOrgId);
        world.Organizations[orgId] = org;
        world.NextOrganizationId = 2;
        civ.OrgId = orgId;
        ruler.Memberships.Add(membership);

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        loaded.NextOrganizationId.Should().Be(2);
        loaded.Organizations.Should().ContainKey(orgId);
        var loadedOrg = loaded.Organizations[orgId];
        loadedOrg.Kind.Should().Be(OrganizationKind.Civilization);
        loadedOrg.LeaderId.Should().Be(ruler.Id);
        loadedOrg.Members.Should().ContainKey(ruler.Id);
        loadedOrg.Members[ruler.Id].Role.Should().Be(OrganizationRole.Leader);
        loadedOrg.Allies.Should().Contain(otherOrgId, "alliance facts must survive save/load");

        loaded.Civilizations[civId].OrgId.Should().Be(orgId, "the civ-to-org link must survive save/load");

        var loadedRuler = loaded.GetEntity(ruler.Id) as WorldEngine.Sim.Entities.Characters.Tier1Character;
        loadedRuler.Should().NotBeNull();
        loadedRuler!.Memberships.Should().ContainSingle(
            "M12 12.2: Tier1Character.Memberships (replacing IdentityData.CivId) must survive save/load");
        loadedRuler.CivId.Should().Be(civId);
    }

    // ── Test 10: settlement specialization round-trip (M9 9.2) ──────────────
    // The 9.2 phase doc explicitly calls for DTO/persistence coverage of these two fields
    // (same pattern as ResourceStores/Unrest); this was the one concrete gap the M9 audit found.

    [Fact]
    public void WorldStateSaver_RoundTrip_SettlementSpecialization()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 43);
        var simCfg = TestSimConfig.Default();

        var civId = new CivId(1);
        var tile  = new TileCoord(5, 5);
        var founderId = new EntityId(201);
        world.Civilizations[civId] = new Civilization(civId, "SpecCiv", founderId, tile, 0);
        world.Settlements[tile] = new SettlementStub(founderId, civId, tile, FoundedYear: 0, Population: 50, Health: 100)
        {
            Specialization         = "timber",
            SpecializationStrength = 0.72f,
        };

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        loaded.Settlements.Should().ContainKey(tile);
        var loadedStub = loaded.Settlements[tile];
        loadedStub.Specialization.Should().Be("timber", "Specialization must survive save/load");
        loadedStub.SpecializationStrength.Should().BeApproximately(0.72f, 0.001f, "SpecializationStrength must survive save/load");
    }

    [Fact]
    public void WorldStateSaver_RoundTrip_SettlementSpecialization_NullRoundTripsAsNull()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 44);
        var simCfg = TestSimConfig.Default();

        var civId = new CivId(1);
        var tile  = new TileCoord(5, 5);
        var founderId = new EntityId(202);
        world.Civilizations[civId] = new Civilization(civId, "NoSpecCiv", founderId, tile, 0);
        // Specialization left at its default (null) — a settlement with no dominant resource yet
        world.Settlements[tile] = new SettlementStub(founderId, civId, tile, FoundedYear: 0, Population: 10, Health: 100);

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        var loadedStub = loaded.Settlements[tile];
        loadedStub.Specialization.Should().BeNull();
        loadedStub.SpecializationStrength.Should().Be(0f);
    }

    // ── Test 11: Tier1Character local-presence hook round-trip (M11 11.6) ────
    // No current sim logic populates LocalChunk/LocalPosition, but the data shape must survive
    // save/load like every other character field, both when unset (the common case today) and
    // when a future system sets it.

    [Fact]
    public void WorldStateSaver_RoundTrip_Tier1LocalPresence_DefaultsToNull()
    {
        var world  = BuildAndRunWorld(ticks: 200);
        var simCfg = TestSimConfig.Default();

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        var tier1s = loaded.Entities.Characters.ToList();
        tier1s.Should().NotBeEmpty("BuildAndRunWorld must produce at least one Tier1Character");
        tier1s.Should().OnlyContain(c => c.LocalChunk == null && c.LocalPosition == null,
            "no current sim logic populates the local-presence hook");
    }

    [Fact]
    public void WorldStateSaver_RoundTrip_Tier1LocalPresence_PopulatedValueSurvives()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 45);
        var simCfg = TestSimConfig.Default();

        var civId = new CivId(1);
        var tile  = new TileCoord(5, 5);
        var founder = new Tier1Character(
            new EntityId(301L), tile,
            PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
            new IdentityData("Ruler3", "the Third", "test", null, null, 0, 0),
            100, 200)
        {
            LocalChunk    = new ChunkCoord(tile, 2, 3),
            LocalPosition = new LocalTileCoord(7, 9),
        };
        founder.WithCiv(civId);
        world.Entities.Add(founder);
        world.Civilizations[civId] = new Civilization(civId, "LocalPresenceCiv", founder.Id, tile, 0);

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        var loadedFounder = loaded.Entities.Characters.Single(c => c.Id == founder.Id);
        loadedFounder.LocalChunk.Should().Be(new ChunkCoord(tile, 2, 3));
        loadedFounder.LocalPosition.Should().Be(new LocalTileCoord(7, 9));
    }
}
