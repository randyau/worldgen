using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Config;

/// <summary>
/// Validates ancestries.toml after deserialization. Called automatically by
/// <see cref="AncestryLoader.LoadOrDefault"/>. Throws <see cref="AncestryValidationException"/>
/// listing every violation found — a load-time gate per M10 10.3 (fail fast, not silent
/// degradation of simulation behavior).
/// </summary>
public static class AncestryValidator
{
    private static readonly HashSet<string> ValidBiomeKeys =
        Enum.GetNames<BiomeType>().Select(PascalToSnakeCase).ToHashSet();

    /// <summary>Validate every ancestry in <paramref name="ancestries"/>. Throws on any violation.</summary>
    public static void Validate(IReadOnlyList<AncestryConfig> ancestries)
    {
        var errors = new List<string>();
        var seenIds = new HashSet<string>();
        var allIds = ancestries.Select(a => a.Id).ToHashSet();

        foreach (var a in ancestries)
        {
            string tag = string.IsNullOrWhiteSpace(a.Id) ? "<blank id>" : a.Id;

            if (string.IsNullOrWhiteSpace(a.Id))
                errors.Add("[ancestry] id must not be blank");
            else if (!seenIds.Add(a.Id))
                errors.Add($"[ancestry.{tag}] duplicate ancestry id");

            if (string.IsNullOrWhiteSpace(a.DisplayName))
                errors.Add($"[ancestry.{tag}] display_name must not be blank");

            if (a.MinLifespanSeasons <= 0)
                errors.Add($"[ancestry.{tag}] min_lifespan_seasons must be > 0 (got {a.MinLifespanSeasons})");
            if (a.MinLifespanSeasons > a.MaxLifespanSeasons)
                errors.Add($"[ancestry.{tag}] min_lifespan_seasons ({a.MinLifespanSeasons}) must be ≤ max_lifespan_seasons ({a.MaxLifespanSeasons})");

            if (a.NameOnsets.Length == 0 || a.NameCodas.Length == 0)
                errors.Add($"[ancestry.{tag}] name_onsets and name_codas must not be empty");
            if (a.SurnameOnsets.Length == 0 || a.SurnameCodas.Length == 0)
                errors.Add($"[ancestry.{tag}] surname_onsets and surname_codas must not be empty");
            if (a.Epithets.Length == 0)
                errors.Add($"[ancestry.{tag}] epithets must not be empty");

            foreach (var biomeKey in a.SpawnWeights.Keys)
                if (!ValidBiomeKeys.Contains(biomeKey))
                    errors.Add($"[ancestry.{tag}] spawn_weights references unknown biome '{biomeKey}'");
            foreach (var (biomeKey, weight) in a.SpawnWeights)
                if (weight < 0f)
                    errors.Add($"[ancestry.{tag}] spawn_weights.{biomeKey} must be ≥ 0 (got {weight})");

            foreach (var (otherId, trust) in a.FirstMeetingTrust)
            {
                if (!allIds.Contains(otherId))
                    errors.Add($"[ancestry.{tag}] first_meeting_trust references unknown ancestry '{otherId}'");
                if (trust < -1f || trust > 1f)
                    errors.Add($"[ancestry.{tag}] first_meeting_trust.{otherId} must be in [-1, 1] (got {trust})");
            }

            foreach (var (otherId, dist) in a.CulturalDistance)
            {
                if (!allIds.Contains(otherId))
                    errors.Add($"[ancestry.{tag}] cultural_distance references unknown ancestry '{otherId}'");
                if (dist < 0f || dist > 1f)
                    errors.Add($"[ancestry.{tag}] cultural_distance.{otherId} must be in [0, 1] (got {dist})");
            }
        }

        if (errors.Count > 0)
            throw new AncestryValidationException(errors);
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

/// <summary>Thrown when ancestries.toml fails validation. Contains all violation messages.</summary>
public sealed class AncestryValidationException : InvalidOperationException
{
    public IReadOnlyList<string> Violations { get; }

    public AncestryValidationException(IReadOnlyList<string> violations)
        : base($"ancestries.toml failed validation with {violations.Count} error(s):\n"
               + string.Join("\n", violations.Select(v => $"  {v}")))
    {
        Violations = violations;
    }
}
