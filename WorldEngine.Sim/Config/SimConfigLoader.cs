using System.Reflection;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace WorldEngine.Sim.Config;

public static class SimConfigLoader
{
    private static readonly string DefaultConfigPath = Path.Combine("config", "sim_config.toml");
    private static readonly string ProfilesDirectory = Path.Combine("config", "profiles");

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

    /// <summary>
    /// Load the config from the default (or given) path, optionally applying a named profile
    /// overlay from config/profiles/&lt;profileName&gt;.toml, then zero or more dotted-path
    /// key overrides (e.g. "settlement.pop_max=10000"). The last write wins.
    /// </summary>
    /// <param name="path">Path to the base config file. Null = auto-discover.</param>
    /// <param name="profileName">
    /// Name of a profile in config/profiles/. Null or empty = no profile.
    /// The profile file path is &lt;profiles-dir&gt;/&lt;profileName&gt;.toml.
    /// Profile files need only contain the keys they override.
    /// </param>
    /// <param name="overrides">
    /// Optional dotted-path=value pairs applied after the profile, e.g.
    /// ("settlement.pop_max", "10000"). Values are interpreted as bare TOML scalars.
    /// Used by the Phase A headless runner's --set flag.
    /// </param>
    public static SimConfig Load(
        string? path = null,
        string? profileName = null,
        IEnumerable<KeyValuePair<string, string>>? overrides = null)
    {
        var resolvedPath = path ?? FindConfigFile();
        if (resolvedPath is null || !File.Exists(resolvedPath))
            return SimConfig.Default();

        var baseToml = File.ReadAllText(resolvedPath);

        // Merge profile if requested
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var profilePath = FindProfileFile(profileName, resolvedPath);
            if (profilePath is null || !File.Exists(profilePath))
                throw new FileNotFoundException($"Profile '{profileName}' not found. Expected: {profilePath}");

            var profileToml = File.ReadAllText(profilePath);
            baseToml = MergeToml(baseToml, profileToml);
        }

        // Apply programmatic overrides
        if (overrides is not null)
        {
            foreach (var kv in overrides)
                baseToml = ApplyDottedOverride(baseToml, kv.Key, kv.Value);
        }

        return LoadFromToml(baseToml, ancestryBasePath: resolvedPath);
    }

    /// <summary>Legacy entry point — equivalent to Load(path).</summary>
    public static SimConfig LoadOrCreateDefault(string? path = null)
        => Load(path);

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

        // 4. Validate ranges, ordering invariants, and cross-field constraints
        SimConfigValidator.Validate(config);

        return config;
    }

    // ─── Profile merging ─────────────────────────────────────────────────────

    /// <summary>
    /// Merge a profile TOML document over the base document at the Tomlyn model level.
    /// Only keys present in the profile override the base; all other base keys are preserved.
    /// The result is serialized back to a TOML string for downstream processing (strict mode
    /// validation still applies to the merged document).
    /// </summary>
    private static string MergeToml(string baseToml, string profileToml)
    {
        var baseTable    = Toml.ToModel(baseToml);
        var profileTable = Toml.ToModel(profileToml);

        MergeTable(baseTable, profileTable);

        return Toml.FromModel(baseTable);
    }

    /// <summary>
    /// Deep-merge <paramref name="overlay"/> into <paramref name="target"/>.
    /// Scalar/array values in overlay overwrite the corresponding value in target.
    /// Sub-tables are merged recursively.
    /// </summary>
    private static void MergeTable(TomlTable target, TomlTable overlay)
    {
        foreach (var kvp in overlay)
        {
            if (kvp.Value is TomlTable overlayChild)
            {
                if (target.TryGetValue(kvp.Key, out var existing) && existing is TomlTable targetChild)
                    MergeTable(targetChild, overlayChild); // recurse
                else
                    target[kvp.Key] = overlayChild;        // new sub-table
            }
            else
            {
                target[kvp.Key] = kvp.Value;               // scalar / array override
            }
        }
    }

    // ─── Dotted-path override (--set) ────────────────────────────────────────

    /// <summary>
    /// Apply a single dotted-path=value override at the Tomlyn model level.
    /// e.g. ("settlement.pop_max", "10000") sets pop_max inside the settlement sub-table.
    /// The value string is parsed as a bare TOML scalar via a synthetic single-key document.
    /// </summary>
    private static string ApplyDottedOverride(string toml, string dottedKey, string value)
    {
        var baseTable = Toml.ToModel(toml);

        // Parse the value by wrapping it as a synthetic TOML doc
        var syntheticToml  = $"__val = {value}";
        var syntheticTable = Toml.ToModel(syntheticToml);
        var parsedValue    = syntheticTable["__val"];

        // Navigate / create the table path and set the leaf key
        var parts = dottedKey.Split('.');
        var table = baseTable;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var segment = parts[i];
            if (!table.TryGetValue(segment, out var child) || child is not TomlTable childTable)
            {
                childTable = new TomlTable();
                table[segment] = childTable;
            }
            table = (TomlTable)table[segment];
        }
        table[parts[^1]] = parsedValue;

        return Toml.FromModel(baseTable);
    }

    // ─── Unbound key detection ────────────────────────────────────────────────

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
            else if (kvp.Value is TomlTableArray)
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

    // ─── File discovery ───────────────────────────────────────────────────────

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

    private static string? FindProfileFile(string profileName, string baseConfigPath)
    {
        // Look in config/profiles/ relative to the base config's directory
        var configDir    = Path.GetDirectoryName(baseConfigPath) ?? AppContext.BaseDirectory;
        var profilesDir  = Path.Combine(configDir, "profiles");
        var fromConfig   = Path.Combine(profilesDir, $"{profileName}.toml");
        if (File.Exists(fromConfig)) return fromConfig;

        // Walk up from AppContext.BaseDirectory (test / CI environments)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ProfilesDirectory, $"{profileName}.toml");
            if (File.Exists(candidate)) return candidate;
        }

        return fromConfig; // return expected path even if not found (used in error message)
    }

    // ─── Utility ──────────────────────────────────────────────────────────────

    private static string PascalToSnakeCase(string name)
    {
        // Insert underscore before uppercase letters preceded by a lowercase/uppercase letter.
        var s = Regex.Replace(name, "(?<=[a-zA-Z])([A-Z])", "_$1");
        // Also insert underscore before uppercase letters preceded by a digit (handles Tier2Notable, etc.)
        s = Regex.Replace(s, @"(?<=[0-9])([A-Z])", "_$1");
        return s.ToLowerInvariant();
    }
}
