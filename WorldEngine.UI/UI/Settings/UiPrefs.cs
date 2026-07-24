using System.Text.Json;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Settings;

// MAP: Small persisted UI-only preferences store (M8.5.1) — separate from sim config.toml.
/// <summary>
/// Persisted UI preferences: display tuning + keybind overrides. Global (not per-world), stored
/// next to the first-run flag file. Sim tuning stays in <c>config/*.toml</c> — this is UI-only.
/// </summary>
// DECISION: JSON via System.Text.Json, not TOML — config/*.toml is the sim's format; keeping UI
// prefs in a separate, separately-owned file/format avoids any risk of the doc-check generator
// or sim config loader treating this file as sim-owned.
public sealed record UiPrefs
{
    // DECISION: ThemeVariant/HighContrast/ReduceMotion/OverlayPalette/Density are persisted here
    // but not yet applied live — UiTheme is currently a fixed set of `static readonly` tokens
    // with no variant-swap mechanism, and the kit has no animation system yet for ReduceMotion
    // to suppress. Wiring these live is a UiTheme/kit change beyond this settings-shell scaffold;
    // the Settings UI stores the player's choice now so it isn't lost once that lands.
    public string ThemeVariant { get; init; } = "Default";
    public bool HighContrast { get; init; }
    public bool ReduceMotion { get; init; }
    public string OverlayPalette { get; init; } = "Default";
    public string Density { get; init; } = "Comfortable";

    /// <summary>Right-dock width in pixels — applied live to <c>LayoutHost.DockWidth</c>.</summary>
    public int DockWidth { get; init; } = UiTheme.SidebarWidth;

    /// <summary>Command id → key label (e.g. "Ctrl+S"), applied over the registry defaults on load.</summary>
    public Dictionary<string, string> KeybindOverrides { get; init; } = new();
}

/// <summary>Loads/saves <see cref="UiPrefs"/> as JSON in the user's local app-data directory.</summary>
public static class UiPrefsStore
{
    private static readonly string Path_ =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorldEngine", "ui_prefs.json");

    public static UiPrefs Load()
    {
        try
        {
            if (File.Exists(Path_))
                return JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(Path_)) ?? new UiPrefs();
        }
        catch { /* corrupt/missing file — fall back to defaults */ }
        return new UiPrefs();
    }

    public static void Save(UiPrefs prefs)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-critical */ }
    }
}
