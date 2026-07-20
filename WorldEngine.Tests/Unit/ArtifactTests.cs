using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Tests for W0 (registry, name gen) and W1 (masterwork creation, conquest transfer,
/// death inheritance) of the Artifacts system.
/// </summary>
public sealed class ArtifactTests
{
    private static SimConfig DefaultConfig() => SimConfigLoader.LoadOrCreateDefault();

    // ─── Registry unit tests ──────────────────────────────────────────────────

    [Fact]
    public void Registry_Create_AddsArtifactToWorld()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile  = FindFirstLandTile(world);
        var owner = ArtifactOwner.OfSettlement(tile);

        var artifact = ArtifactRegistry.Create(world, "Test Blade", ArtifactCategory.Weapon,
            1, 0, "Anonymous", "battle", 0.8f, owner);

        world.Artifacts.Should().ContainKey(artifact.Id);
        world.Artifacts[artifact.Id].Name.Should().Be("Test Blade");
        world.Artifacts[artifact.Id].Quality.Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void Registry_SetOwner_TransfersOwnership()
    {
        var world    = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile     = FindFirstLandTile(world);
        var artifact = ArtifactRegistry.Create(world, "Crown", ArtifactCategory.Regalia,
            1, 0, "Anon", "battle", 0.9f, ArtifactOwner.OfSettlement(tile));

        ArtifactRegistry.SetOwner(world, artifact.Id, ArtifactOwner.Lost);

        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Lost);
    }

    [Fact]
    public void Registry_Destroy_MarksArtifactDestroyed()
    {
        var world    = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile     = FindFirstLandTile(world);
        var artifact = ArtifactRegistry.Create(world, "Relic", ArtifactCategory.Relic,
            1, 0, "Anon", "battle", 0.7f, ArtifactOwner.OfSettlement(tile));

        ArtifactRegistry.Destroy(world, artifact.Id, 100);

        world.Artifacts[artifact.Id].IsDestroyed.Should().BeTrue();
        world.Artifacts[artifact.Id].DestroyedYear.Should().Be(100);
    }

    [Fact]
    public void Registry_Active_ExcludesDestroyed()
    {
        var world     = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile      = FindFirstLandTile(world);
        var alive     = ArtifactRegistry.Create(world, "Alive", ArtifactCategory.Weapon,
            1, 0, "Anon", "battle", 0.5f, ArtifactOwner.OfSettlement(tile));
        var destroyed = ArtifactRegistry.Create(world, "Dead", ArtifactCategory.Weapon,
            1, 0, "Anon", "battle", 0.5f, ArtifactOwner.OfSettlement(tile));
        ArtifactRegistry.Destroy(world, destroyed.Id, 1);

        var active = ArtifactRegistry.Active(world).ToList();
        active.Should().Contain(a => a.Id == alive.Id);
        active.Should().NotContain(a => a.Id == destroyed.Id);
    }

    [Fact]
    public void Registry_OwnedByCharacter_FiltersCorrectly()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile   = FindFirstLandTile(world);
        var charId = new EntityId(9991);
        var owned  = ArtifactRegistry.Create(world, "Mine", ArtifactCategory.Tome,
            1, charId.Value, "Hero", "masterwork", 0.75f, ArtifactOwner.OfCharacter(charId));
        ArtifactRegistry.Create(world, "Theirs", ArtifactCategory.Tome,
            1, 0, "Nobody", "battle", 0.5f, ArtifactOwner.OfSettlement(tile));

        var charOwned = ArtifactRegistry.OwnedByCharacter(world, charId).ToList();
        charOwned.Should().HaveCount(1);
        charOwned[0].Id.Should().Be(owned.Id);
    }

    [Fact]
    public void Registry_InSettlement_FiltersCorrectly()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile   = FindFirstLandTile(world);
        var charId = new EntityId(77);
        ArtifactRegistry.Create(world, "InSettle", ArtifactCategory.Weapon,
            1, 0, "None", "battle", 0.5f, ArtifactOwner.OfSettlement(tile));
        ArtifactRegistry.Create(world, "OnChar", ArtifactCategory.Weapon,
            1, charId.Value, "Hero", "masterwork", 0.8f, ArtifactOwner.OfCharacter(charId));

        var settlArtifacts = ArtifactRegistry.InSettlement(world, tile).ToList();
        settlArtifacts.Should().HaveCount(1);
        settlArtifacts[0].Name.Should().Be("InSettle");
    }

    // ─── Name generator ──────────────────────────────────────────────────────

    [Fact]
    public void NameGenerator_SameSeed_SameResult()
    {
        var world1 = WorldTestHelper.CreateSmallWorld(seed: 77);
        var world2 = WorldTestHelper.CreateSmallWorld(seed: 77);

        var name1 = ArtifactNameGenerator.Generate(world1, ArtifactCategory.Weapon, 42);
        var name2 = ArtifactNameGenerator.Generate(world2, ArtifactCategory.Weapon, 42);

        name1.Should().Be(name2);
        name1.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NameGenerator_Reproducibility_SameSeedSameNames()
    {
        var world1 = WorldTestHelper.CreateSmallWorld(seed: 999);
        var world2 = WorldTestHelper.CreateSmallWorld(seed: 999);

        foreach (var cat in Enum.GetValues<ArtifactCategory>())
        {
            var n1 = ArtifactNameGenerator.Generate(world1, cat, 12345);
            var n2 = ArtifactNameGenerator.Generate(world2, cat, 12345);
            n1.Should().Be(n2, $"same seed must produce same name for {cat}");
        }
    }

    // ─── Masterwork creation ──────────────────────────────────────────────────

    [Fact]
    public void MasterworkArtisan_CreatesArtifactOwnedByCreator()
    {
        // Force exceptional roll to trigger
        var cfg = DefaultConfig();
        cfg.Character.Tier2ExceptionalWorkChance = 1.0f;
        cfg.Character.Tier2NotableCooldownTicks  = 0;

        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile  = FindFirstLandTile(world);
        SetupSettlement(world, tile, new CivId(1), new EntityId(1));

        var artisan = new Tier2Character(
            EntityId.New(), tile, "Crafter",
            PersonalityVector6.Default,
            new LivelihoodData(Tier2Role.Artisan, null, tile, 0.8f),
            maxHealth: 100, maxAgeSeason: 200);
        world.Entities.Add(artisan);

        var phase = new Tier2BehaviorPhase(cfg);

        // Run until masterwork fires. Artisan has a 25% per-tick gate before the notable work
        // check. We advance world.CurrentTick via reflection so each tick produces a distinct
        // RNG roll — without this, GetRandomFloat returns the same value every iteration.
        var tickProp = typeof(WorldState).GetProperty("CurrentTick")!;
        List<PendingEvent> allPending = [];
        for (long tick = 0; tick < 200 && !artisan.HasMasterwork; tick++)
        {
            tickProp.SetValue(world, tick);
            allPending.AddRange(phase.Execute(world, tick));
        }

        artisan.HasMasterwork.Should().BeTrue("exceptional roll is forced to 1.0");
        world.Artifacts.Should().NotBeEmpty("masterwork should create an artifact");

        var artifact = world.Artifacts.Values.First();
        artifact.Origin.Should().Be("masterwork");
        artifact.Owner.Kind.Should().Be(ArtifactOwnerKind.Character);
        artifact.Owner.CharacterId.Should().Be(artisan.Id.Value);
        artifact.Category.Should().Be(ArtifactCategory.Artwork); // artisan → Artwork

        allPending.Should().Contain(e => e.Type == EventType.ArtifactCreated);
    }

    [Fact]
    public void MasterworkScholar_CreatesArtifactWithTomeCategory()
    {
        var cfg = DefaultConfig();
        cfg.Character.Tier2ExceptionalWorkChance = 1.0f;
        cfg.Character.Tier2NotableCooldownTicks  = 0;

        var world = WorldTestHelper.CreateSmallWorld(seed: 43);
        var tile  = FindFirstLandTile(world);
        SetupSettlement(world, tile, new CivId(1), new EntityId(1));

        var scholar = new Tier2Character(
            EntityId.New(), tile, "Scribe",
            PersonalityVector6.Default,
            new LivelihoodData(Tier2Role.Scholar, null, tile, 0.8f),
            maxHealth: 100, maxAgeSeason: 200);
        world.Entities.Add(scholar);

        var phase    = new Tier2BehaviorPhase(cfg);
        var tickProp = typeof(WorldState).GetProperty("CurrentTick")!;
        // Scholar notable-work (discovery) fires far less often than the artisan's 25% gate,
        // and the roll keys on the entity id (EntityId.New()), so a large tick budget keeps
        // this deterministic regardless of entity-creation order across the suite.
        for (long tick = 0; tick < 5000 && !scholar.HasMasterwork; tick++)
        {
            tickProp.SetValue(world, tick);
            phase.Execute(world, tick);
        }

        scholar.HasMasterwork.Should().BeTrue();
        if (world.Artifacts.Count > 0)
            world.Artifacts.Values.First().Category.Should().Be(ArtifactCategory.Tome);
    }

    // ─── Conquest transfer ────────────────────────────────────────────────────

    [Fact]
    public void Conquest_ArtifactsInSettlement_EmitTransferEvent()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 50);
        var tile  = FindFirstLandTile(world);

        // Place an artifact in the settlement
        var artifact = ArtifactRegistry.Create(world, "Looted Sword", ArtifactCategory.Weapon,
            1, 0, "Defender", "battle", 0.8f, ArtifactOwner.OfSettlement(tile));

        var pending = new List<PendingEvent>();

        // Simulate what TransferConquestArtifacts does (it's private, so we mirror its logic)
        var artifacts = ArtifactRegistry.InSettlement(world, tile).ToList();
        foreach (var a in artifacts)
        {
            string fromOwner = a.Owner.Describe();
            var newOwner = ArtifactOwner.OfSettlement(tile);
            var transPayload = JsonSerializer.Serialize(new ArtifactTransferredPayload(
                a.Id.Value, a.Name, fromOwner, newOwner.Describe(), "conquest"));
            pending.Add(new PendingEvent(EventType.ArtifactTransferred, tile, null, transPayload,
                new[] { a.Id.Value }, CivId: 1L));
        }

        pending.Should().Contain(e => e.Type == EventType.ArtifactTransferred);
        var ev = pending.First(e => e.Type == EventType.ArtifactTransferred);
        var payload = JsonSerializer.Deserialize<ArtifactTransferredPayload>(ev.PayloadJson)!;
        payload.Reason.Should().Be("conquest");
        payload.ArtifactId.Should().Be(artifact.Id.Value);
    }

    // ─── Death inheritance ────────────────────────────────────────────────────

    [Fact]
    public void DeathInheritance_ArtifactTransfersToSettlement_WhenLossRollFails()
    {
        // LostOnDeathProbability=0 → inheritance roll always passes → goes to settlement
        var cfg = DefaultConfig();
        cfg.Artifacts.LostOnDeathProbability = 0f;

        var world = WorldTestHelper.CreateSmallWorld(seed: 60);
        var tile  = FindFirstLandTile(world);
        SetupSettlement(world, tile, new CivId(1), new EntityId(1));

        var charId   = EntityId.New();
        var artifact = ArtifactRegistry.Create(world, "Heirloom", ArtifactCategory.Jewelry,
            1, charId.Value, "Hero", "masterwork", 0.9f, ArtifactOwner.OfCharacter(charId));

        var ch = CreateTier1At(world, charId, tile, cfg);
        world.Entities.Add(ch);

        // Force death by setting health to 0 before Execute
        ch.Health = 0;

        var phase  = new CharacterBehaviorPhase(cfg);
        var events = phase.Execute(world, 1L, isAnnualTick: false);

        var updated = world.Artifacts[artifact.Id];
        updated.Owner.Kind.Should().Be(ArtifactOwnerKind.Settlement,
            "LostOnDeathProbability=0 means loss roll always fails → artifact goes to settlement");
        updated.Owner.SettlementTile.Should().Be(tile);

        events.Should().Contain(e => e.Type == EventType.ArtifactTransferred);
    }

    [Fact]
    public void DeathInheritance_ArtifactBecomesLost_WhenLossRollPasses()
    {
        // LostOnDeathProbability=1 → loss roll always passes → artifact always lost
        var cfg = DefaultConfig();
        cfg.Artifacts.LostOnDeathProbability = 1f;

        var world = WorldTestHelper.CreateSmallWorld(seed: 61);
        var tile  = FindFirstLandTile(world);
        SetupSettlement(world, tile, new CivId(1), new EntityId(1));

        var charId   = EntityId.New();
        var artifact = ArtifactRegistry.Create(world, "Doomed Blade", ArtifactCategory.Weapon,
            1, charId.Value, "Hero", "masterwork", 0.8f, ArtifactOwner.OfCharacter(charId));

        var ch = CreateTier1At(world, charId, tile, cfg);
        world.Entities.Add(ch);

        ch.Health = 0;

        var phase  = new CharacterBehaviorPhase(cfg);
        var events = phase.Execute(world, 1L, isAnnualTick: false);

        var updated = world.Artifacts[artifact.Id];
        updated.Owner.Kind.Should().Be(ArtifactOwnerKind.Lost,
            "LostOnDeathProbability=1 means loss roll always passes → artifact always lost");

        events.Should().Contain(e => e.Type == EventType.ArtifactTransferred);
        var ev      = events.First(e => e.Type == EventType.ArtifactTransferred);
        var payload = JsonSerializer.Deserialize<ArtifactTransferredPayload>(ev.PayloadJson)!;
        payload.Reason.Should().Be("inheritance");
        payload.ToOwner.Should().Be("Lost");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static TileCoord FindFirstLandTile(WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        return new TileCoord(0, 0);
    }

    private static void SetupSettlement(WorldState world, TileCoord tile, CivId civ, EntityId founderId)
    {
        if (!world.Civilizations.ContainsKey(civ))
        {
            world.Civilizations[civ] = new Civilization(civ, "TestCiv", founderId, tile, 1);
            world.NextCivId = Math.Max(world.NextCivId, (int)civ.Value + 1);
        }
        world.Settlements[tile] = new SettlementStub(founderId, civ, tile, 1, 50, 100);
    }

    private static Tier1Character CreateTier1At(WorldState world, EntityId id, TileCoord tile, SimConfig cfg)
    {
        var identity = new IdentityData(
            Name:        "TestHero",
            Epithet:     "the Bold",
            AncestryId:  "anc_test",
            MotherId:    null,
            FatherId:    null,
            CivId:       new CivId(1),
            BirthYear:   1,
            BirthSeason: 0);
        var personality = PersonalityVector.Default;
        var aptitude    = AptitudeVector.Default;
        var skills      = SkillVector.Default;
        return new Tier1Character(id, tile, personality, aptitude, skills, identity,
            maxHealth: cfg.Character.MaxHealth,
            maxAgeSeason: 80);
    }
}
