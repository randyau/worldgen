using System.Text.RegularExpressions;
using Tomlyn;

namespace WorldEngine.Sim.Entities.Beasts;

public static class BeastCatalogLoader
{
    private static readonly string DefaultPath = Path.Combine("config", "beasts.toml");

    public static BeastCatalog LoadOrCreateDefault(string? path = null)
    {
        var resolved = path ?? FindFile();
        if (resolved is null || !File.Exists(resolved))
            return new BeastCatalog(new BeastCatalogFile());

        var toml = File.ReadAllText(resolved);
        var options = new TomlModelOptions
        {
            ConvertPropertyName = PascalToSnakeCase,
            IgnoreMissingProperties = true,
        };
        var file = Toml.ToModel<BeastCatalogFile>(toml, null, options);
        return new BeastCatalog(file);
    }

    private static string? FindFile()
    {
        var fromBase = Path.Combine(AppContext.BaseDirectory, DefaultPath);
        if (File.Exists(fromBase)) return fromBase;
        if (File.Exists(DefaultPath)) return DefaultPath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, DefaultPath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string PascalToSnakeCase(string name) =>
        Regex.Replace(name, "(?<=[a-zA-Z])([A-Z])", "_$1").ToLowerInvariant();
}
