namespace WorldEngine.Sim.Config;

/// <summary>
/// Validates religions.toml after deserialization. Called automatically by
/// <see cref="ReligionArchetypeLoader.LoadOrDefault"/>. Throws <see cref="ReligionArchetypeValidationException"/>
/// listing every violation found — fail fast, same gate style as <see cref="AncestryValidator"/>.
/// </summary>
public static class ReligionArchetypeValidator
{
    public static void Validate(IReadOnlyList<ReligionArchetypeConfig> archetypes)
    {
        var errors = new List<string>();
        var seenIds = new HashSet<string>();

        foreach (var a in archetypes)
        {
            string tag = string.IsNullOrWhiteSpace(a.Id) ? "<blank id>" : a.Id;

            if (string.IsNullOrWhiteSpace(a.Id))
                errors.Add("[religion] id must not be blank");
            else if (!seenIds.Add(a.Id))
                errors.Add($"[religion.{tag}] duplicate religion id");

            if (string.IsNullOrWhiteSpace(a.DisplayName))
                errors.Add($"[religion.{tag}] display_name must not be blank");
            if (a.DeityNames.Length == 0)
                errors.Add($"[religion.{tag}] deity_names must not be empty");
            if (a.NameTemplates.Length == 0)
                errors.Add($"[religion.{tag}] name_templates must not be empty");
            foreach (var t in a.NameTemplates)
                if (!t.Contains("{deity}"))
                    errors.Add($"[religion.{tag}] name_template '{t}' must contain a {{deity}} placeholder");
        }

        if (errors.Count > 0)
            throw new ReligionArchetypeValidationException(errors);
    }
}

/// <summary>Thrown when religions.toml fails validation. Contains all violation messages.</summary>
public sealed class ReligionArchetypeValidationException : InvalidOperationException
{
    public IReadOnlyList<string> Violations { get; }

    public ReligionArchetypeValidationException(IReadOnlyList<string> violations)
        : base($"religions.toml failed validation with {violations.Count} error(s):\n"
               + string.Join("\n", violations.Select(v => $"  {v}")))
    {
        Violations = violations;
    }
}
