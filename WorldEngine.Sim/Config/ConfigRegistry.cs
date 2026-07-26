using System.Reflection;

namespace WorldEngine.Sim.Config;

/// <summary>Value kinds the generic settings UI knows how to render (M10 10.2, ui_design_framework.md §9.3).</summary>
public enum ConfigValueKind { Int, Float, Byte, Bool, String }

/// <summary>
/// Builds <see cref="Entry"/> descriptors by reflecting over a live <see cref="SimConfig"/>
/// instance, generically — no per-key UI code (ui_design_framework.md §9.3). Defaults are read from
/// a second, independently-loaded <see cref="SimConfig"/> snapshot, per
/// docs/phases/m10_worldgen_preview_modding.md DECISION (10.2): "default" means the shipped,
/// post-profile-merge sim_config.toml as loaded at session start, not bare C# property initializers.
/// </summary>
// MOD SEAM: this registry is exactly the seam a future mod-config schema would populate — see
// ui_design_framework.md §10 ("Config surfacing pattern" / presets).
public static class ConfigRegistry
{
    /// <summary>
    /// One config-control descriptor for a single leaf property somewhere in the <see cref="SimConfig"/>
    /// object graph. <see cref="Group"/> is the top-level SimConfig section (e.g. "WorldGen");
    /// <see cref="Path"/> is dotted from that root (e.g. "Ocean.DefaultSeaLevel").
    /// </summary>
    public sealed class Entry
    {
        public required string Group { get; init; }
        public required string Path { get; init; }
        public required ConfigValueKind Kind { get; init; }
        public required Func<object> Get { get; init; }
        public required Action<object> Set { get; init; }
        public required object Default { get; init; }

        public string Key => $"{Group}.{Path}";
        public bool IsModified => !Equals(Get(), Default);
    }

    private static readonly HashSet<Type> LeafTypes =
        [typeof(int), typeof(float), typeof(byte), typeof(bool), typeof(string)];

    /// <summary>
    /// Reflects every leaf tunable reachable from <paramref name="live"/>. <paramref name="live"/>
    /// is the object the running sim actually reads each tick — entries write straight back into
    /// it. <paramref name="defaults"/> supplies each entry's reset/diff baseline and is never
    /// mutated. Skips <see cref="SimConfig.AncestryRegistry"/> (loaded separately, not from
    /// sim_config.toml) and any non-leaf collection (arrays/lists/dictionaries/enums) — those
    /// aren't single-control tunables the generic {key, kind} shape can represent.
    /// </summary>
    public static IReadOnlyList<Entry> Build(SimConfig live, SimConfig defaults)
    {
        var entries = new List<Entry>();
        foreach (var groupProp in typeof(SimConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (groupProp.Name == nameof(SimConfig.AncestryRegistry)) continue;
            if (groupProp.PropertyType.Namespace != typeof(SimConfig).Namespace) continue;

            var liveGroup = groupProp.GetValue(live);
            var defaultGroup = groupProp.GetValue(defaults);
            if (liveGroup is null || defaultGroup is null) continue;

            Walk(entries, groupProp.Name, "", liveGroup, defaultGroup);
        }
        return entries;
    }

    private static void Walk(List<Entry> entries, string group, string prefix, object liveObj, object defaultObj)
    {
        foreach (var prop in liveObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            string path = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";

            if (LeafTypes.Contains(prop.PropertyType))
            {
                var capturedProp = prop;
                var capturedLive = liveObj;
                entries.Add(new Entry
                {
                    Group   = group,
                    Path    = path,
                    Kind    = KindOf(prop.PropertyType),
                    Get     = () => capturedProp.GetValue(capturedLive)!,
                    Set     = v => capturedProp.SetValue(capturedLive, v),
                    Default = prop.GetValue(defaultObj)!
                });
            }
            else if (prop.PropertyType.IsClass && prop.PropertyType.Namespace == typeof(SimConfig).Namespace)
            {
                var nestedLive = prop.GetValue(liveObj);
                var nestedDefault = prop.GetValue(defaultObj);
                if (nestedLive is not null && nestedDefault is not null)
                    Walk(entries, group, path, nestedLive, nestedDefault);
            }
            // else: arrays/lists/dictionaries/enums — not representable as a single generic control, skip.
        }
    }

    private static ConfigValueKind KindOf(Type t) =>
        t == typeof(int) ? ConfigValueKind.Int
        : t == typeof(float) ? ConfigValueKind.Float
        : t == typeof(byte) ? ConfigValueKind.Byte
        : t == typeof(bool) ? ConfigValueKind.Bool
        : ConfigValueKind.String;
}
