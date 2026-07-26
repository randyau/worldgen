using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M9 phase 9.0: CreatedGoodType taxonomy unification (G-1) and weighted-category
/// artifact derivation (G-2 — Armor spawn source, battle/heroic-death variety).
/// </summary>
public class CreatedGoodTaxonomyTests
{
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

    // ─── Grouping coverage ──────────────────────────────────────────────────

    [Fact]
    public void Groups_CoverEveryCreatedGoodType_Exactly()
    {
        var all = Enum.GetValues<CreatedGoodType>().ToHashSet();
        var grouped = CreatedGoodTaxonomy.ArtisanGoods
            .Concat(CreatedGoodTaxonomy.ArtGoods)
            .Concat(CreatedGoodTaxonomy.DiscoveryGoods)
            .ToList();

        grouped.Should().OnlyHaveUniqueItems("no good type should appear in two groups");
        grouped.ToHashSet().Should().BeEquivalentTo(all, "every CreatedGoodType must belong to exactly one group");
    }

    [Fact]
    public void CategoryWeights_CoverEveryCreatedGoodType()
    {
        foreach (var good in Enum.GetValues<CreatedGoodType>())
            CreatedGoodTaxonomy.CategoryWeights.Should().ContainKey(good, $"{good} needs a category weight table");
    }

    [Fact]
    public void DiscoveryBonusKeys_CoverEveryDiscoveryGood()
    {
        foreach (var good in CreatedGoodTaxonomy.DiscoveryGoods)
            CreatedGoodTaxonomy.DiscoveryBonusKeys.Should().ContainKey(good);
    }

    // ─── WeightedPick determinism ───────────────────────────────────────────

    [Fact]
    public void WeightedPick_LowRoll_ReturnsFirstCategory()
    {
        (ArtifactCategory, float)[] table = [(ArtifactCategory.Weapon, 0.5f), (ArtifactCategory.Armor, 0.5f)];
        CreatedGoodTaxonomy.WeightedPick(table, 0.0f).Should().Be(ArtifactCategory.Weapon);
    }

    [Fact]
    public void WeightedPick_HighRoll_ReturnsLastCategory()
    {
        (ArtifactCategory, float)[] table = [(ArtifactCategory.Weapon, 0.5f), (ArtifactCategory.Armor, 0.5f)];
        CreatedGoodTaxonomy.WeightedPick(table, 0.999f).Should().Be(ArtifactCategory.Armor);
    }

    [Fact]
    public void WeightedPick_NeverReturnsAZeroWeightCategory()
    {
        (ArtifactCategory, float)[] table = [(ArtifactCategory.Weapon, 1.0f), (ArtifactCategory.Armor, 0.0f)];
        for (float roll = 0f; roll < 1f; roll += 0.05f)
            CreatedGoodTaxonomy.WeightedPick(table, roll).Should().Be(ArtifactCategory.Weapon);
    }

    // ─── PickCategory determinism (reproducibility) ─────────────────────────

    [Fact]
    public void PickCategory_SameSeedAndTick_ProducesSameCategory()
    {
        var world1 = BuildWorld(seed: 555);
        var world2 = BuildWorld(seed: 555);
        var id = new EntityId(77);

        var cat1 = CreatedGoodTaxonomy.PickCategory(world1, id, CreatedGoodType.Metalwork);
        var cat2 = CreatedGoodTaxonomy.PickCategory(world2, id, CreatedGoodType.Metalwork);

        cat1.Should().Be(cat2);
    }

    // ─── G-2: Armor is now reachable ─────────────────────────────────────────

    [Fact]
    public void Armor_IsReachable_FromMetalworkLeatherworkOrMetallurgy()
    {
        var armorSources = CreatedGoodTaxonomy.CategoryWeights
            .Where(kv => kv.Value.Any(w => w.Category == ArtifactCategory.Armor))
            .Select(kv => kv.Key)
            .ToList();

        armorSources.Should().Contain(CreatedGoodType.Metalwork);
        armorSources.Should().Contain(CreatedGoodType.Leatherwork);
        armorSources.Should().Contain(CreatedGoodType.Metallurgy);
    }

    [Fact]
    public void Armor_IsReachable_ByRollingAcrossManyEntities()
    {
        var world = BuildWorld(seed: 1);
        bool sawArmor = false;
        for (long i = 0; i < 500 && !sawArmor; i++)
        {
            if (CreatedGoodTaxonomy.PickCategory(world, new EntityId(i), CreatedGoodType.Metalwork) == ArtifactCategory.Armor)
                sawArmor = true;
        }
        sawArmor.Should().BeTrue("Metalwork has a 0.35 weight toward Armor — 500 rolls should hit it");
    }
}
