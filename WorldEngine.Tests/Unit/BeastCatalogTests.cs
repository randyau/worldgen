using WorldEngine.Sim.Entities.Beasts;

namespace WorldEngine.Tests.Unit;

public class BeastCatalogTests
{
    [Fact]
    public void Catalog_LoadsFromToml_WolfEntryParsedCorrectly()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();

        var wolf = catalog.Get("wolf");
        wolf.Should().NotBeNull("wolf must be in beasts.toml");
        wolf!.DisplayName.Should().Be("Wolf");
        wolf.Category.Should().Be("predator");
        wolf.Health.Should().BeGreaterThan(0);
        wolf.Strength.Should().BeGreaterThan(0);
        wolf.Biomes.Should().Contain("boreal_forest");
        wolf.LegendaryChance.Should().BeGreaterThan(0f);
        wolf.LegendaryNameAdjectives.Should().NotBeEmpty();
    }

    [Fact]
    public void Catalog_LoadsFromToml_DragonIsMythological()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();

        var dragon = catalog.Get("dragon");
        dragon.Should().NotBeNull();
        dragon!.IsMythological.Should().BeTrue();
        dragon.Abilities.Should().Contain("flight");
        dragon.Health.Should().BeGreaterThan(100);
    }

    [Fact]
    public void Catalog_ByCategory_ReturnsPredatorsOnly()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();

        var predators = catalog.ByCategory("predator").ToList();
        predators.Should().NotBeEmpty();
        predators.Should().AllSatisfy(s => s.Category.Should().Be("predator"));
    }

    [Fact]
    public void Catalog_ByBiome_FindsRelevantSpecies()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();

        var tundraSpecies = catalog.ByBiome("tundra").ToList();
        tundraSpecies.Should().NotBeEmpty();
        tundraSpecies.Should().Contain(s => s.Id == "wolf");
        tundraSpecies.Should().Contain(s => s.Id == "polar_bear");
    }

    [Fact]
    public void Catalog_SpawnConfig_HasReasonableDefaults()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();
        catalog.SpawnConfig.TargetDensityPer10kTiles.Should().BeGreaterThan(0f);
        catalog.SpawnConfig.MythStartFraction.Should().BeInRange(0f, 1f);
        catalog.SpawnConfig.MythEmergenceYears.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Catalog_CombatConfig_HasReasonableDefaults()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();
        catalog.CombatConfig.MaxRoundsPerTick.Should().BeGreaterThan(0);
        catalog.CombatConfig.MaxGangSize.Should().BeGreaterThan(0);
        catalog.CombatConfig.RetreatHealthFraction.Should().BeInRange(0f, 1f);
    }

    [Fact]
    public void BeastFactory_SpawnWolf_ProducesNamedBeast()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();
        var species = catalog.Get("wolf")!;
        var coord = new WorldEngine.Sim.Core.TileCoord(10, 10);

        var beast = BeastFactory.Spawn(species, coord, worldSeed: 42, entitySeq: 1, forceLegendary: true);

        beast.SpeciesId.Should().Be("wolf");
        beast.Name.Should().StartWith("The ", "legendary beasts always get a title");
        beast.IsAlive.Should().BeTrue();
        beast.Health.Should().Be(beast.MaxHealth);
        beast.Location.Should().Be(coord);
    }

    [Fact]
    public void BeastFactory_SameSeed_SameName()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();
        var species = catalog.Get("wolf")!;
        var coord = new WorldEngine.Sim.Core.TileCoord(5, 5);

        var a = BeastFactory.Spawn(species, coord, worldSeed: 99, entitySeq: 7);
        var b = BeastFactory.Spawn(species, coord, worldSeed: 99, entitySeq: 7);

        a.Name.Should().Be(b.Name, "same seed + sequence must produce same name");
    }

    [Fact]
    public void BeastFactory_LegendaryBeast_HasBoostedStats()
    {
        var catalog = BeastCatalogLoader.LoadOrCreateDefault();
        var species = catalog.Get("wolf")!;
        var coord = new WorldEngine.Sim.Core.TileCoord(0, 0);

        var legendary = BeastFactory.Spawn(species, coord, worldSeed: 0, entitySeq: 0, forceLegendary: true);

        legendary.IsLegendary.Should().BeTrue();
        legendary.MaxHealth.Should().BeGreaterThan(species.Health, "legendary health multiplier applies");
        legendary.Strength.Should().BeGreaterThan(species.Strength, "legendary strength multiplier applies");
    }
}
