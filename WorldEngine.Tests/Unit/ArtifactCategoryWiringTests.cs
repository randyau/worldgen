using System.Reflection;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M9 G-2 replaced hardcoded ArtifactCategory.Weapon with a config-weighted roll for both
/// battle-forged and heroic-death artifacts (CreatedGoodTaxonomy.WeightedPick). The picker itself
/// is exhaustively tested (CreatedGoodTaxonomyTests) and the config validator confirms the
/// weights sum to 1.0 (ArtifactFoundationTests), but nothing proved the *wiring* — that these two
/// call sites actually pass the configured weights through rather than still being hardcoded to
/// Weapon. Both call sites are private (TryForgeBattleArtifact: private static on CivTracker;
/// TryHeroicDeathForge: private instance method on CharacterBehaviorPhase), so these tests use
/// reflection to invoke them directly with a zero-weight-for-Weapon config — if the wiring were
/// still hardcoded to Weapon, these tests would fail regardless of config.
/// </summary>
public class ArtifactCategoryWiringTests
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

    private static TileCoord FindLandTile(WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new TileCoord(x, y);
                if (world.IsLand(c)) return c;
            }
        throw new InvalidOperationException("No land tile found.");
    }

    // ── Battle-forged artifacts (CivTracker.TryForgeBattleArtifact) ───────────

    [Fact]
    public void BattleForgedArtifact_CanBeArmor_WhenWeaponWeightIsZero()
    {
        var world = BuildWorld();
        var tile  = FindLandTile(world);
        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), tile, 0);
        world.Settlements[tile] = new SettlementStub(new EntityId(1), civId, tile, 0, 50, 100);

        var artCfg = world.SimConfig.Artifacts;
        artCfg.BattleForgeProbability     = 1.0f; // guarantee forging happens
        artCfg.BattleCategoryWeightWeapon = 0f;    // if the roll were still hardcoded to Weapon, this would prove it
        artCfg.BattleCategoryWeightArmor  = 1f;
        artCfg.BattleCategoryWeightRegalia = 0f;

        int before = world.Artifacts.Count;
        var pending = new List<PendingEvent>();

        var method = typeof(CivTracker).GetMethod("TryForgeBattleArtifact", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("CivTracker.TryForgeBattleArtifact must exist — if renamed, update this test alongside it");
        method!.Invoke(null, new object[] { world, pending, tile, civId, 42L, "Test Attacker", world.CurrentYear, 12345 });

        world.Artifacts.Count.Should().Be(before + 1, "BattleForgeProbability=1.0 must guarantee an artifact is forged");
        world.Artifacts.Values.Should().Contain(a => a.Category == ArtifactCategory.Armor,
            "with Weapon weight 0, the weighted roll must be able to produce Armor — proves the category is config-driven, not hardcoded");
    }

    [Fact]
    public void BattleForgedArtifact_DoesNotForge_WhenProbabilityIsZero()
    {
        var world = BuildWorld();
        var tile  = FindLandTile(world);
        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), tile, 0);
        world.Settlements[tile] = new SettlementStub(new EntityId(1), civId, tile, 0, 50, 100);

        world.SimConfig.Artifacts.BattleForgeProbability = 0f;
        int before = world.Artifacts.Count;
        var pending = new List<PendingEvent>();

        var method = typeof(CivTracker).GetMethod("TryForgeBattleArtifact", BindingFlags.NonPublic | BindingFlags.Static);
        method!.Invoke(null, new object[] { world, pending, tile, civId, 42L, "Test Attacker", world.CurrentYear, 54321 });

        world.Artifacts.Count.Should().Be(before, "BattleForgeProbability=0 must guarantee no artifact is forged");
    }

    // ── Heroic-death artifacts (CharacterBehaviorPhase.TryHeroicDeathForge) ───

    private static Tier1Character MakeHighCombatCharacter(TileCoord loc, EntityId id, SimConfig cfg)
    {
        var identity = new IdentityData("Hero", "the Bold", "test", null, null, default, 0, 0);
        var skills   = SkillVector.Default with { Combat = 0.9f };
        return new Tier1Character(id, loc, PersonalityVector.Default, AptitudeVector.Default, skills,
            identity, cfg.Character.MaxHealth, 200);
    }

    [Fact]
    public void HeroicDeathArtifact_CanBeRelic_WhenWeaponWeightIsZero()
    {
        var world = BuildWorld();
        var tile  = FindLandTile(world);
        var cfg   = TestSimConfig.Default();
        cfg.Artifacts.HeroicDeathForgeProbability   = 1.0f;
        cfg.Artifacts.HeroicDeathCategoryWeightWeapon = 0f;
        cfg.Artifacts.HeroicDeathCategoryWeightRelic   = 1f;
        cfg.Artifacts.HeroicDeathCategoryWeightRegalia = 0f;

        var character = MakeHighCombatCharacter(tile, new EntityId(950), cfg);
        world.Entities.Add(character);

        int before = world.Artifacts.Count;
        var pending = new List<PendingEvent>();
        var phase = new CharacterBehaviorPhase(cfg);

        var method = typeof(CharacterBehaviorPhase).GetMethod("TryHeroicDeathForge", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("CharacterBehaviorPhase.TryHeroicDeathForge must exist — if renamed, update this test alongside it");
        method!.Invoke(phase, new object[] { character, "wounds", world, pending });

        world.Artifacts.Count.Should().Be(before + 1, "HeroicDeathForgeProbability=1.0 and Combat >= 0.5 must guarantee an artifact is forged");
        world.Artifacts.Values.Should().Contain(a => a.Category == ArtifactCategory.Relic,
            "with Weapon weight 0, the weighted roll must be able to produce Relic — proves the category is config-driven, not hardcoded");
    }

    [Fact]
    public void HeroicDeathArtifact_DoesNotForge_ForLowCombatSkill()
    {
        var world = BuildWorld();
        var tile  = FindLandTile(world);
        var cfg   = TestSimConfig.Default();
        cfg.Artifacts.HeroicDeathForgeProbability = 1.0f;

        var identity = new IdentityData("Weakling", "", "test", null, null, default, 0, 0);
        var lowSkills = SkillVector.Default with { Combat = 0.1f }; // below the 0.5 legendary threshold
        var character = new Tier1Character(new EntityId(951), tile, PersonalityVector.Default, AptitudeVector.Default,
            lowSkills, identity, cfg.Character.MaxHealth, 200);
        world.Entities.Add(character);

        int before = world.Artifacts.Count;
        var pending = new List<PendingEvent>();
        var phase = new CharacterBehaviorPhase(cfg);

        var method = typeof(CharacterBehaviorPhase).GetMethod("TryHeroicDeathForge", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(phase, new object[] { character, "wounds", world, pending });

        world.Artifacts.Count.Should().Be(before, "Combat < 0.5 must never forge a heroic-death artifact regardless of probability");
    }

    [Fact]
    public void HeroicDeathArtifact_DoesNotForge_ForNonCombatDeathCause()
    {
        var world = BuildWorld();
        var tile  = FindLandTile(world);
        var cfg   = TestSimConfig.Default();
        cfg.Artifacts.HeroicDeathForgeProbability = 1.0f;

        var character = MakeHighCombatCharacter(tile, new EntityId(952), cfg);
        world.Entities.Add(character);

        int before = world.Artifacts.Count;
        var pending = new List<PendingEvent>();
        var phase = new CharacterBehaviorPhase(cfg);

        var method = typeof(CharacterBehaviorPhase).GetMethod("TryHeroicDeathForge", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(phase, new object[] { character, "starvation", world, pending });

        world.Artifacts.Count.Should().Be(before, "a non-combat death cause (starvation) must never forge a heroic-death artifact");
    }
}
