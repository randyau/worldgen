using System.Text.RegularExpressions;
using Tomlyn;

namespace WorldEngine.Sim.Config;

public static class AncestryLoader
{
    private const string DefaultFile = "config/ancestries.toml";

    public static AncestryRegistry LoadOrDefault(string? nearPath = null)
    {
        var path = FindFile(nearPath);
        if (path is null || !File.Exists(path))
            return AncestryRegistry.Empty;

        var toml = File.ReadAllText(path);
        var options = new TomlModelOptions
        {
            ConvertPropertyName   = PascalToSnakeCase,
            IgnoreMissingProperties = true,
        };

        var file = Toml.ToModel<AncestryFile>(toml, null, options);
        var dict = file.Ancestry.ToDictionary(a => a.Id, a => a);
        return new AncestryRegistry(dict);
    }

    private static string? FindFile(string? nearPath)
    {
        // nearPath is typically the sim_config.toml path — look in the same config/ dir
        if (nearPath != null)
        {
            var dir = Path.GetDirectoryName(nearPath);
            if (dir != null)
            {
                var sibling = Path.Combine(dir, "ancestries.toml");
                if (File.Exists(sibling)) return sibling;
            }
        }

        // Try relative to working dir
        if (File.Exists(DefaultFile)) return DefaultFile;

        // Walk up from AppContext.BaseDirectory
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

    // Internal TOML model — [[ancestry]] maps to Ancestry list
    private sealed class AncestryFile
    {
        public List<AncestryConfig> Ancestry { get; set; } = new();
    }
}
