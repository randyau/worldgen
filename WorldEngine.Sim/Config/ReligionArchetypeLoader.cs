using System.Text.RegularExpressions;
using Tomlyn;

namespace WorldEngine.Sim.Config;

/// <summary>Loads religions.toml into ReligionArchetypeConfig instances.</summary>
public static class ReligionArchetypeLoader
{
    private const string DefaultFile = "config/religions.toml";

    public static ReligionArchetypeRegistry LoadOrDefault(string? nearPath = null)
    {
        var path = FindFile(nearPath);
        if (path is null || !File.Exists(path))
            return ReligionArchetypeRegistry.Empty;

        var toml = File.ReadAllText(path);
        var options = new TomlModelOptions
        {
            ConvertPropertyName     = PascalToSnakeCase,
            IgnoreMissingProperties = true,
        };

        var file = Toml.ToModel<ReligionFile>(toml, null, options);
        ReligionArchetypeValidator.Validate(file.Religion);
        return new ReligionArchetypeRegistry(file.Religion);
    }

    private static string? FindFile(string? nearPath)
    {
        if (nearPath != null)
        {
            var dir = Path.GetDirectoryName(nearPath);
            if (dir != null)
            {
                var sibling = Path.Combine(dir, "religions.toml");
                if (File.Exists(sibling)) return sibling;
            }
        }

        if (File.Exists(DefaultFile)) return DefaultFile;

        var appDir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && appDir != null; i++, appDir = appDir.Parent)
        {
            var candidate = Path.Combine(appDir.FullName, DefaultFile);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string PascalToSnakeCase(string name) =>
        Regex.Replace(name, "(?<=[a-zA-Z])([A-Z])", "_$1").ToLowerInvariant();

    // Internal TOML model — [[religion]] maps to Religion list
    private sealed class ReligionFile
    {
        public List<ReligionArchetypeConfig> Religion { get; set; } = new();
    }
}
