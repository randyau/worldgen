using FluentAssertions;
using WorldEngine.Sim.Entities.Beasts;

namespace WorldEngine.Tests.Unit;

public class BeastCatalogValidatorTests
{
    private static BeastSpeciesConfig ValidPredator(string id = "wolf") => new()
    {
        Id = id,
        DisplayName = "Wolf",
        Category = "predator",
        Biomes = ["tundra"],
        MaxPerWorld = 10,
        PackSizeMin = 1,
        PackSizeMax = 4,
        Health = 30,
        Strength = 10,
        Speed = 4,
        Aggression = 0.5f,
        TerritoryRadius = 5,
        FoodDepletion = 0.1f,
        FoodFromHunt = 1f,
        FoodFromGraze = 0f,
        AgeMinSeasons = 0,
        AgeMaxSeasons = 40,
        ReproductionMinAge = 4,
        ReproductionFoodThreshold = 0.5f,
        ReproductionChance = 0.1f,
        LegendaryChance = 0.05f,
        LegendaryNameAdjectives = ["Grim"],
        LegendaryNameNouns = ["Fang"],
    };

    private static BeastCatalogFile FileWith(params BeastSpeciesConfig[] beasts) => new()
    {
        BeastSpawn = new(),
        Combat = new(),
        Beasts = beasts.ToList(),
    };

    [Fact]
    public void Validate_AcceptsWellFormedCatalog()
    {
        var act = () => BeastCatalogValidator.Validate(FileWith(ValidPredator()));
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        var act = () => BeastCatalogValidator.Validate(FileWith(ValidPredator("wolf"), ValidPredator("wolf")));
        act.Should().Throw<BeastCatalogValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("duplicate beast id"));
    }

    [Fact]
    public void Validate_RejectsInvalidCategory()
    {
        var b = ValidPredator();
        b.Category = "ghost";
        var act = () => BeastCatalogValidator.Validate(FileWith(b));
        act.Should().Throw<BeastCatalogValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("category must be"));
    }

    [Fact]
    public void Validate_RejectsUnknownBiome()
    {
        var b = ValidPredator();
        b.Biomes = ["not_a_biome"];
        var act = () => BeastCatalogValidator.Validate(FileWith(b));
        act.Should().Throw<BeastCatalogValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("unknown biome"));
    }

    [Fact]
    public void Validate_AcceptsAnyAsSpecialBiomeKeyword()
    {
        var b = ValidPredator();
        b.Biomes = ["any"];
        var act = () => BeastCatalogValidator.Validate(FileWith(b));
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsInvertedPackSizeRange()
    {
        var b = ValidPredator();
        b.PackSizeMin = 5;
        b.PackSizeMax = 2;
        var act = () => BeastCatalogValidator.Validate(FileWith(b));
        act.Should().Throw<BeastCatalogValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("pack_size_min") && v.Contains("pack_size_max"));
    }

    [Fact]
    public void Validate_RejectsMythologicalSpeciesMissingNameLists()
    {
        var b = ValidPredator();
        b.Category = "mythological";
        b.NameAdjectives = [];
        b.NameNouns = [];
        var act = () => BeastCatalogValidator.Validate(FileWith(b));
        act.Should().Throw<BeastCatalogValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("name_adjectives") && v.Contains("mythological"));
    }

    [Fact]
    public void Validate_RejectsOutOfRangeSpawnFraction()
    {
        var file = FileWith(ValidPredator());
        file.BeastSpawn.MythStartFraction = 1.5f;
        var act = () => BeastCatalogValidator.Validate(file);
        act.Should().Throw<BeastCatalogValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("myth_start_fraction"));
    }

    [Fact]
    public void Validate_RealBeastsToml_PassesValidation()
    {
        var act = () => BeastCatalogLoader.LoadOrCreateDefault();
        act.Should().NotThrow();
    }
}
