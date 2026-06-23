using System.Text.RegularExpressions;
using Tomlyn;

namespace WorldEngine.Sim.Config;

public static class SimConfigLoader
{
    private static readonly string DefaultConfigPath = Path.Combine("config", "sim_config.toml");

    public static SimConfig LoadOrCreateDefault(string? path = null)
    {
        var resolvedPath = path ?? FindConfigFile();
        if (resolvedPath is null || !File.Exists(resolvedPath))
            return SimConfig.Default();

        var toml = File.ReadAllText(resolvedPath);
        var options = new TomlModelOptions
        {
            ConvertPropertyName = PascalToSnakeCase,
            IgnoreMissingProperties = true,
            // Allow integer TOML values to map onto enum-typed config properties
            // (Tomlyn does not cast Int64 → enum by default).
            ConvertToModel = (value, targetType) =>
                value is long l && targetType.IsEnum
                    ? Enum.ToObject(targetType, l)
                    : null
        };

        var config = Toml.ToModel<SimConfig>(toml, null, options);
        config.AncestryRegistry = AncestryLoader.LoadOrDefault(resolvedPath);
        return config;
    }

    private static string? FindConfigFile()
    {
        // Try relative to AppContext.BaseDirectory (works when running from output dir)
        var fromBase = Path.Combine(AppContext.BaseDirectory, DefaultConfigPath);
        if (File.Exists(fromBase)) return fromBase;

        // Try relative to current directory (useful in tests via dotnet test)
        if (File.Exists(DefaultConfigPath)) return DefaultConfigPath;

        // Walk up from AppContext.BaseDirectory to find repo root (net8.0/Debug/... → project root)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, DefaultConfigPath);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string PascalToSnakeCase(string name)
    {
        return Regex.Replace(name, "(?<=[a-zA-Z])([A-Z])", "_$1").ToLowerInvariant();
    }
}
