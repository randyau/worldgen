using System.Reflection;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace WorldEngine.Sim.Config;

public static class SimConfigLoader
{
    private static readonly string DefaultConfigPath = Path.Combine("config", "sim_config.toml");

    /// <summary>
    /// When true, unknown TOML keys throw an exception instead of logging a warning.
    /// Defaults to true in DEBUG builds (enforces strict config hygiene during development).
    /// Release builds warn only. Tests may set this explicitly.
    /// </summary>
    public static bool StrictMode { get; set; }
#if DEBUG
        = true;
#else
        = false;
#endif

    public static SimConfig LoadOrCreateDefault(string? path = null)
    {
        var resolvedPath = path ?? FindConfigFile();
        if (resolvedPath is null || !File.Exists(resolvedPath))
            return SimConfig.Default();

        var toml = File.ReadAllText(resolvedPath);
        // Pass resolved path so AncestryLoader can find ancestries.toml in the same dir
        return LoadFromToml(toml, ancestryBasePath: resolvedPath);
    }

    /// <summary>
    /// Load SimConfig from a TOML string (used for profile merging and tests).
    /// </summary>
    /// <param name="toml">TOML document text.</param>
    /// <param name="ancestryBasePath">Optional path to a file in the config directory; used to locate ancestries.toml.</param>
    public static SimConfig LoadFromToml(string toml, string? ancestryBasePath = null)
    {
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

        // 1. Parse to dynamic TomlTable first (gives us the raw key tree)
        var tomlTable = Toml.ToModel(toml);

        // 2. Validate against the config object graph
        var unbound = FindUnboundKeys(tomlTable, typeof(SimConfig));
        if (unbound.Count > 0)
        {
            var message = $"sim_config.toml contains {unbound.Count} unbound key(s) that map to no config property:\n"
                + string.Join("\n", unbound.Select(k => $"  {k}"));

            if (StrictMode)
                throw new InvalidOperationException(message);
            else
                Console.Error.WriteLine($"[SimConfigLoader] WARNING: {message}");
        }

        // 3. Deserialize to typed config
        var config = Toml.ToModel<SimConfig>(toml, null, options);
        config.AncestryRegistry = AncestryLoader.LoadOrDefault(ancestryBasePath);
        return config;
    }

    /// <summary>
    /// Walk the Tomlyn TomlTable and collect dotted paths for any key that does not
    /// correspond to a property on the config type graph.
    /// </summary>
    private static List<string> FindUnboundKeys(TomlTable table, Type configType)
    {
        var unbound = new List<string>();
        WalkTable(table, configType, "", unbound);
        return unbound;
    }

    private static void WalkTable(TomlTable table, Type? configType, string prefix, List<string> unbound)
    {
        foreach (var kvp in table)
        {
            var rawKey  = kvp.Key;
            var dotPath = prefix.Length > 0 ? $"{prefix}.{rawKey}" : rawKey;

            // Find the property on the config type that matches this TOML key
            var prop = configType is null ? null : FindProperty(configType, rawKey);

            if (kvp.Value is TomlTable nested)
            {
                // It's a sub-table: recurse into it using the property's declared type (if found)
                var childType = prop?.PropertyType;
                WalkTable(nested, childType, dotPath, unbound);
            }
            else if (kvp.Value is TomlTableArray tableArray)
            {
                // Array of tables: check that the property exists on the parent
                if (prop is null)
                    unbound.Add(dotPath);
                // We don't recurse into array-of-tables elements for now
            }
            else
            {
                // Scalar / array value: report if unbound
                if (prop is null)
                    unbound.Add(dotPath);
            }
        }
    }

    /// <summary>
    /// Find a property on <paramref name="type"/> whose snake_case name matches <paramref name="tomlKey"/>.
    /// Also checks the Speed and Persistence nested types on SimLoopConfig.
    /// </summary>
    private static PropertyInfo? FindProperty(Type type, string tomlKey)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (PascalToSnakeCase(prop.Name) == tomlKey)
                return prop;
        }
        return null;
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
        // Insert underscore before uppercase letters preceded by a lowercase/uppercase letter.
        var s = Regex.Replace(name, "(?<=[a-zA-Z])([A-Z])", "_$1");
        // Also insert underscore before uppercase letters preceded by a digit (handles Tier2Notable, etc.)
        s = Regex.Replace(s, @"(?<=[0-9])([A-Z])", "_$1");
        return s.ToLowerInvariant();
    }
}
