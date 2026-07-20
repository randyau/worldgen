using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Unit tests for M5 W0 artifact foundation: registry round-trips,
/// name-gen reproducibility, and ArtifactConfig binding.
/// </summary>
public class ArtifactFoundationTests
{
    // ─── Build a minimal world for registry tests ──────────────────────────────

    private static WorldState BuildWorld(int seed = 42)
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

    // ─── ArtifactRegistry tests ────────────────────────────────────────────────

    [Fact]
    public void Registry_Create_InsertsArtifactIntoWorld()
    {
        var world = BuildWorld();
        var owner = ArtifactOwner.OfCharacter(new EntityId(1));

        var artifact = ArtifactRegistry.Create(
            world, "The Iron Blade", ArtifactCategory.Weapon,
            year: 100, creatorId: 1, creatorName: "Aldric",
            origin: "masterwork", quality: 0.8f, owner: owner);

        world.Artifacts.Should().ContainKey(artifact.Id);
        world.Artifacts[artifact.Id].Name.Should().Be("The Iron Blade");
        world.Artifacts[artifact.Id].Quality.Should().Be(0.8f);
        world.Artifacts[artifact.Id].IsDestroyed.Should().BeFalse();
    }

    [Fact]
    public void Registry_SetOwner_TransfersOwnership()
    {
        var world = BuildWorld();
        var charOwner  = ArtifactOwner.OfCharacter(new EntityId(1));
        var settleTile = new TileCoord(5, 5);
        var settleOwner = ArtifactOwner.OfSettlement(settleTile);

        var artifact = ArtifactRegistry.Create(
            world, "The Dawn Crown", ArtifactCategory.Regalia,
            year: 50, creatorId: 1, creatorName: "Mira",
            origin: "masterwork", quality: 0.9f, owner: charOwner);

        ArtifactRegistry.SetOwner(world, artifact.Id, settleOwner);

        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Settlement);
        world.Artifacts[artifact.Id].Owner.SettlementTile.Should().Be(settleTile);
    }

    [Fact]
    public void Registry_Destroy_MarksArtifactDestroyed()
    {
        var world = BuildWorld();
        var owner = ArtifactOwner.OfCharacter(new EntityId(2));

        var artifact = ArtifactRegistry.Create(
            world, "The Sundered Shield", ArtifactCategory.Armor,
            year: 200, creatorId: 2, creatorName: "Rowan",
            origin: "battle", quality: 0.7f, owner: owner);

        ArtifactRegistry.Destroy(world, artifact.Id, year: 210);

        world.Artifacts[artifact.Id].IsDestroyed.Should().BeTrue();
        world.Artifacts[artifact.Id].DestroyedYear.Should().Be(210);
    }

    [Fact]
    public void Registry_Active_ExcludesDestroyedArtifacts()
    {
        var world = BuildWorld();
        var owner = ArtifactOwner.OfCharacter(new EntityId(3));

        var a1 = ArtifactRegistry.Create(world, "Blade A", ArtifactCategory.Weapon,
            1, 3, "Kira", "masterwork", 0.8f, owner);
        var a2 = ArtifactRegistry.Create(world, "Blade B", ArtifactCategory.Weapon,
            2, 3, "Kira", "masterwork", 0.7f, owner);

        ArtifactRegistry.Destroy(world, a2.Id, year: 5);

        var active = ArtifactRegistry.Active(world).ToList();
        active.Should().ContainSingle(a => a.Id == a1.Id);
        active.Should().NotContain(a => a.Id == a2.Id);
    }

    [Fact]
    public void Registry_OwnedByCharacter_FiltersCorrectly()
    {
        var world = BuildWorld();
        var charA = new EntityId(10);
        var charB = new EntityId(20);

        ArtifactRegistry.Create(world, "A's Sword", ArtifactCategory.Weapon,
            1, charA.Value, "Alice", "masterwork", 0.8f, ArtifactOwner.OfCharacter(charA));
        ArtifactRegistry.Create(world, "B's Tome", ArtifactCategory.Tome,
            1, charB.Value, "Bob", "masterwork", 0.6f, ArtifactOwner.OfCharacter(charB));

        var aOwned = ArtifactRegistry.OwnedByCharacter(world, charA).ToList();
        aOwned.Should().ContainSingle(a => a.Name == "A's Sword");
        aOwned.Should().NotContain(a => a.Name == "B's Tome");
    }

    [Fact]
    public void Registry_InSettlement_FiltersCorrectly()
    {
        var world = BuildWorld();
        var tile1 = new TileCoord(3, 3);
        var tile2 = new TileCoord(7, 7);

        ArtifactRegistry.Create(world, "Tile1 Relic", ArtifactCategory.Relic,
            1, 0, "", "battle", 0.9f, ArtifactOwner.OfSettlement(tile1));
        ArtifactRegistry.Create(world, "Tile2 Crown", ArtifactCategory.Regalia,
            1, 0, "", "battle", 0.85f, ArtifactOwner.OfSettlement(tile2));

        var inTile1 = ArtifactRegistry.InSettlement(world, tile1).ToList();
        inTile1.Should().ContainSingle(a => a.Name == "Tile1 Relic");
        inTile1.Should().NotContain(a => a.Name == "Tile2 Crown");
    }

    [Fact]
    public void Registry_Destroy_ThenSetOwner_IsNoOp()
    {
        var world = BuildWorld();
        var owner = ArtifactOwner.OfCharacter(new EntityId(5));
        var artifact = ArtifactRegistry.Create(world, "Doomed", ArtifactCategory.Weapon,
            10, 5, "D", "battle", 0.5f, owner);

        ArtifactRegistry.Destroy(world, artifact.Id, 15);
        ArtifactRegistry.SetOwner(world, artifact.Id, ArtifactOwner.OfSettlement(new TileCoord(1, 1)));

        // Still destroyed, owner unchanged
        world.Artifacts[artifact.Id].IsDestroyed.Should().BeTrue();
        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Character);
    }

    [Fact]
    public void Registry_LostOwner_Describe_ReturnsLostString()
    {
        ArtifactOwner.Lost.Describe().Should().Be("Lost");
    }

    [Fact]
    public void ArtifactOwner_OfCharacter_Describe_ContainsId()
    {
        var owner = ArtifactOwner.OfCharacter(new EntityId(999));
        owner.Describe().Should().Contain("999");
    }

    [Fact]
    public void ArtifactOwner_OfSettlement_Describe_ContainsCoords()
    {
        var owner = ArtifactOwner.OfSettlement(new TileCoord(12, 34));
        owner.Describe().Should().Contain("12").And.Contain("34");
    }

    // ─── Name generator reproducibility tests ─────────────────────────────────

    [Fact]
    public void NameGenerator_SameSeed_ProducesSameName()
    {
        var world1 = BuildWorld(seed: 12345);
        var world2 = BuildWorld(seed: 12345);

        // Advance to the same tick on both worlds
        for (int i = 0; i < 16; i++)
        {
            world1.CurrentTick++;
            world2.CurrentTick++;
        }

        foreach (ArtifactCategory cat in Enum.GetValues<ArtifactCategory>())
        {
            string name1 = ArtifactNameGenerator.Generate(world1, cat, artifactIndex: 0);
            string name2 = ArtifactNameGenerator.Generate(world2, cat, artifactIndex: 0);
            name1.Should().Be(name2,
                $"same seed+tick must produce identical name for {cat}");
        }
    }

    [Fact]
    public void NameGenerator_DifferentIndex_ProducesDifferentNames()
    {
        var world = BuildWorld(seed: 99);
        string name0 = ArtifactNameGenerator.Generate(world, ArtifactCategory.Weapon, 0);
        string name1 = ArtifactNameGenerator.Generate(world, ArtifactCategory.Weapon, 1);
        // Very unlikely to collide (pool size ~40*14 = 560 combos)
        name0.Should().NotBe(name1, "different indexes should produce different names");
    }

    [Fact]
    public void NameGenerator_AllCategories_ProduceNonEmptyNames()
    {
        var world = BuildWorld(seed: 7);
        foreach (ArtifactCategory cat in Enum.GetValues<ArtifactCategory>())
        {
            string name = ArtifactNameGenerator.Generate(world, cat, 0);
            name.Should().NotBeNullOrEmpty();
            name.Should().StartWith("The ");
        }
    }

    // ─── ArtifactConfig binding tests ─────────────────────────────────────────

    [Fact]
    public void ArtifactConfig_BindsFromToml()
    {
        var config = SimConfigLoader.LoadOrCreateDefault();
        config.Artifacts.Should().NotBeNull();
        config.Artifacts.BaseGenerationProbability.Should().Be(0.05f);
        config.Artifacts.NotablePerformanceThreshold.Should().Be(0.75f);
        config.Artifacts.CovetThreshold.Should().Be(0.6f);
        config.Artifacts.BattleForgeProbability.Should().Be(0.03f);
        config.Artifacts.HeroicDeathForgeProbability.Should().Be(0.10f);
        config.Artifacts.LostOnDeathProbability.Should().Be(0.35f);
    }

    [Fact]
    public void ArtifactConfig_ValidatorAcceptsDefaults()
    {
        var config = SimConfigLoader.LoadOrCreateDefault();
        var act = () => SimConfigValidator.Validate(config);
        act.Should().NotThrow("default artifact config values are all valid probabilities");
    }
}
