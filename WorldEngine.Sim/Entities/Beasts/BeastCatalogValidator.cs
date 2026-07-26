using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Validates beasts.toml after deserialization. Called automatically by
/// <see cref="BeastCatalogLoader.LoadOrCreateDefault"/>. Throws
/// <see cref="BeastCatalogValidationException"/> listing every violation found — a load-time gate
/// per M10 10.3 (fail fast, not silent degradation of simulation behavior).
/// </summary>
public static class BeastCatalogValidator
{
    private static readonly HashSet<string> ValidBiomeKeys =
        Enum.GetNames<BiomeType>().Select(PascalToSnakeCase).ToHashSet();

    private static readonly HashSet<string> ValidCategories = ["predator", "mythological"];

    /// <summary>Validate the whole catalog file. Throws on any violation.</summary>
    public static void Validate(BeastCatalogFile file)
    {
        var errors = new List<string>();

        ValidateSpawn(file.BeastSpawn, errors);
        ValidateSpecies(file.Beasts, errors);

        if (errors.Count > 0)
            throw new BeastCatalogValidationException(errors);
    }

    private static void ValidateSpawn(BeastSpawnConfig s, List<string> errors)
    {
        if (s.TargetDensityPer10kTiles <= 0f)
            errors.Add($"[beast_spawn] target_density_per_10k_tiles must be > 0 (got {s.TargetDensityPer10kTiles})");
        if (s.MythStartFraction < 0f || s.MythStartFraction > 1f)
            errors.Add($"[beast_spawn] myth_start_fraction must be in [0, 1] (got {s.MythStartFraction})");
        if (s.MythEmergenceYears < 0)
            errors.Add($"[beast_spawn] myth_emergence_years must be ≥ 0 (got {s.MythEmergenceYears})");
        if (s.PassiveFoodRecovery < 0f)
            errors.Add($"[beast_spawn] passive_food_recovery must be ≥ 0 (got {s.PassiveFoodRecovery})");
    }

    private static void ValidateSpecies(List<BeastSpeciesConfig> beasts, List<string> errors)
    {
        var seenIds = new HashSet<string>();

        foreach (var b in beasts)
        {
            string tag = string.IsNullOrWhiteSpace(b.Id) ? "<blank id>" : b.Id;

            if (string.IsNullOrWhiteSpace(b.Id))
                errors.Add("[beasts] id must not be blank");
            else if (!seenIds.Add(b.Id))
                errors.Add($"[beasts.{tag}] duplicate beast id");

            if (string.IsNullOrWhiteSpace(b.DisplayName))
                errors.Add($"[beasts.{tag}] display_name must not be blank");

            if (!ValidCategories.Contains(b.Category))
                errors.Add($"[beasts.{tag}] category must be 'predator' or 'mythological' (got '{b.Category}')");

            if (b.Biomes.Length == 0)
                errors.Add($"[beasts.{tag}] biomes must not be empty");
            foreach (var biomeKey in b.Biomes)
                if (!biomeKey.Equals("any", StringComparison.OrdinalIgnoreCase) && !ValidBiomeKeys.Contains(biomeKey.ToLowerInvariant()))
                    errors.Add($"[beasts.{tag}] biomes references unknown biome '{biomeKey}'");

            if (b.MaxPerWorld < 0)
                errors.Add($"[beasts.{tag}] max_per_world must be ≥ 0 (got {b.MaxPerWorld})");
            if (b.PackSizeMin < 1)
                errors.Add($"[beasts.{tag}] pack_size_min must be ≥ 1 (got {b.PackSizeMin})");
            if (b.PackSizeMin > b.PackSizeMax)
                errors.Add($"[beasts.{tag}] pack_size_min ({b.PackSizeMin}) must be ≤ pack_size_max ({b.PackSizeMax})");

            if (b.Health <= 0)
                errors.Add($"[beasts.{tag}] health must be > 0 (got {b.Health})");
            if (b.Strength < 0)
                errors.Add($"[beasts.{tag}] strength must be ≥ 0 (got {b.Strength})");
            if (b.Speed < 0)
                errors.Add($"[beasts.{tag}] speed must be ≥ 0 (got {b.Speed})");
            CheckProbability($"beasts.{tag}.aggression", b.Aggression, errors);
            if (b.TerritoryRadius < 0)
                errors.Add($"[beasts.{tag}] territory_radius must be ≥ 0 (got {b.TerritoryRadius})");

            CheckProbability($"beasts.{tag}.food_depletion", b.FoodDepletion, errors);
            if (b.FoodFromHunt < 0f)
                errors.Add($"[beasts.{tag}] food_from_hunt must be ≥ 0 (got {b.FoodFromHunt})");
            if (b.FoodFromGraze < 0f)
                errors.Add($"[beasts.{tag}] food_from_graze must be ≥ 0 (got {b.FoodFromGraze})");

            if (b.AgeMinSeasons < 0)
                errors.Add($"[beasts.{tag}] age_min_seasons must be ≥ 0 (got {b.AgeMinSeasons})");
            if (b.AgeMinSeasons > b.AgeMaxSeasons)
                errors.Add($"[beasts.{tag}] age_min_seasons ({b.AgeMinSeasons}) must be ≤ age_max_seasons ({b.AgeMaxSeasons})");
            if (b.ReproductionMinAge < 0)
                errors.Add($"[beasts.{tag}] reproduction_min_age must be ≥ 0 (got {b.ReproductionMinAge})");
            CheckProbability($"beasts.{tag}.reproduction_food_threshold", b.ReproductionFoodThreshold, errors);
            CheckProbability($"beasts.{tag}.reproduction_chance", b.ReproductionChance, errors);

            CheckProbability($"beasts.{tag}.legendary_chance", b.LegendaryChance, errors);
            if (b.LegendaryHealthMult < 0f)
                errors.Add($"[beasts.{tag}] legendary_health_mult must be ≥ 0 (got {b.LegendaryHealthMult})");
            if (b.LegendaryStrengthMult < 0f)
                errors.Add($"[beasts.{tag}] legendary_strength_mult must be ≥ 0 (got {b.LegendaryStrengthMult})");
            if (b.LegendaryAgeMult < 0f)
                errors.Add($"[beasts.{tag}] legendary_age_mult must be ≥ 0 (got {b.LegendaryAgeMult})");
            if (b.LegendaryTerritoryMult < 0f)
                errors.Add($"[beasts.{tag}] legendary_territory_mult must be ≥ 0 (got {b.LegendaryTerritoryMult})");

            if (b.IsMythological)
            {
                if (b.NameAdjectives.Length == 0)
                    errors.Add($"[beasts.{tag}] name_adjectives must not be empty for a mythological species");
                if (b.NameNouns.Length == 0)
                    errors.Add($"[beasts.{tag}] name_nouns must not be empty for a mythological species");
            }
            else
            {
                if (b.LegendaryNameAdjectives.Length == 0)
                    errors.Add($"[beasts.{tag}] legendary_name_adjectives must not be empty for a predator species");
                if (b.LegendaryNameNouns.Length == 0)
                    errors.Add($"[beasts.{tag}] legendary_name_nouns must not be empty for a predator species");
            }
        }
    }

    private static void CheckProbability(string key, float value, List<string> errors)
    {
        if (value < 0f || value > 1f)
            errors.Add($"[{key}] must be in [0, 1] (got {value})");
    }

    private static string PascalToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}

/// <summary>Thrown when beasts.toml fails validation. Contains all violation messages.</summary>
public sealed class BeastCatalogValidationException : InvalidOperationException
{
    public IReadOnlyList<string> Violations { get; }

    public BeastCatalogValidationException(IReadOnlyList<string> violations)
        : base($"beasts.toml failed validation with {violations.Count} error(s):\n"
               + string.Join("\n", violations.Select(v => $"  {v}")))
    {
        Violations = violations;
    }
}
