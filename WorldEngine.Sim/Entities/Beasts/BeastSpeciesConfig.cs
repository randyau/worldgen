namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Configuration for one beast species loaded from config/beasts.toml.
/// </summary>
public sealed class BeastSpeciesConfig
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";    // "predator" | "mythological"
    public string[] Biomes { get; set; } = [];
    public int MaxPerWorld { get; set; }
    public int PackSizeMin { get; set; } = 1;
    public int PackSizeMax { get; set; } = 1;
    public bool PrefersCompany { get; set; }
    public bool Hibernates { get; set; }
    public string[] Abilities { get; set; } = [];

    // Core stats
    public int Health { get; set; }
    public int Strength { get; set; }
    public int Speed { get; set; }
    public float Aggression { get; set; }
    public int TerritoryRadius { get; set; }
    public float FoodDepletion { get; set; }
    public float FoodFromHunt { get; set; }
    public float FoodFromGraze { get; set; }
    public int AgeMinSeasons { get; set; }
    public int AgeMaxSeasons { get; set; }
    public int ReproductionMinAge { get; set; }
    public float ReproductionFoodThreshold { get; set; }
    public float ReproductionChance { get; set; }

    // Predator legendary variant multipliers (ignored for mythological category)
    public float LegendaryChance { get; set; }
    public float LegendaryHealthMult { get; set; } = 1f;
    public float LegendaryStrengthMult { get; set; } = 1f;
    public float LegendaryAgeMult { get; set; } = 1f;
    public float LegendaryTerritoryMult { get; set; } = 1f;
    public string[] LegendaryNameAdjectives { get; set; } = [];
    public string[] LegendaryNameNouns { get; set; } = [];

    // Mythological creature name lists (used when category = "mythological")
    public string[] NameAdjectives { get; set; } = [];
    public string[] NameNouns { get; set; } = [];

    public bool IsMythological => Category == "mythological";

    /// <summary>Name adjective list appropriate for this species' category.</summary>
    public string[] EffectiveNameAdjectives =>
        IsMythological ? NameAdjectives : LegendaryNameAdjectives;

    /// <summary>Name noun list appropriate for this species' category.</summary>
    public string[] EffectiveNameNouns =>
        IsMythological ? NameNouns : LegendaryNameNouns;
}
