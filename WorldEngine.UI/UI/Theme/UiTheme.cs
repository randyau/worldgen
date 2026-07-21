using Microsoft.Xna.Framework;
using WorldEngine.Sim.Core;

namespace WorldEngine.UI.UI.Theme;

/// <summary>
/// Single source of truth for the UI's visual language: colors, spacing, and metrics.
/// Panels pull named tokens from here instead of hardcoding <see cref="Color"/> literals
/// and pixel widths, so the whole UI can be retuned from one place (M6 Epic 6.2.1).
/// </summary>
// MAP: Centralized UI design tokens (colors, spacing, panel metrics) + shared civ-color derivation.
public static class UiTheme
{
    // ── Text colors ──────────────────────────────────────────────────────────
    /// <summary>Panel titles and emphasis (formerly Color.Gold, scattered inline).</summary>
    public static readonly Color HeaderText = Color.Gold;
    /// <summary>Primary body text.</summary>
    public static readonly Color BodyText = Color.White;
    /// <summary>Secondary / lower-emphasis text.</summary>
    public static readonly Color MutedText = Color.LightGray;
    /// <summary>Dimmed / disabled / de-focused text.</summary>
    public static readonly Color DisabledText = Color.DarkGray;
    /// <summary>Interactive accent (selected toolbar button, links).</summary>
    public static readonly Color Accent = new(120, 190, 255);
    /// <summary>Warning / error text.</summary>
    public static readonly Color Warning = Color.Red;

    // ── Panel chrome ─────────────────────────────────────────────────────────
    public static readonly Color PanelBackground = new Color(20, 22, 28) * 0.92f;
    public static readonly Color PanelBorder     = new(70, 80, 95);

    // ── Metrics ──────────────────────────────────────────────────────────────
    /// <summary>Width reserved for the right-hand sidebar (map area = viewport - this).</summary>
    public const int SidebarWidth = 360;
    /// <summary>Standard content width for a docked panel (fits inside the sidebar).</summary>
    public const int PanelWidth = 330;
    /// <summary>Standard scroll-viewer content width inside a panel.</summary>
    public const int ScrollWidth = 340;
    /// <summary>Vertical gap between stacked widgets.</summary>
    public const int PanelSpacing = 4;
    /// <summary>Inner padding for panel chrome.</summary>
    public const int PanelPad = 6;
    /// <summary>Top offset that clears the time-controls bar.</summary>
    public const int TopBarClearance = 44;

    // ── Event tier colors ────────────────────────────────────────────────────
    /// <summary>Color for an event of the given tier (shared by event log, timeline pips).</summary>
    public static Color TierColor(EventTier tier) => tier switch
    {
        EventTier.Headline  => Color.Gold,
        EventTier.Regional  => Color.White,
        EventTier.Character => Color.LightGray,
        _                   => Color.DarkGray
    };

    // ── Civilization colors ──────────────────────────────────────────────────
    /// <summary>
    /// Derives a deterministic, visually distinct color for a civilization from its numeric
    /// id. Shared between the territory overlay (<c>TileMapRenderer</c>) and panels so map
    /// and UI agree. Uses a golden-angle hue rotation so colors spread evenly.
    /// </summary>
    public static Color CivColor(long civId)
    {
        float hue = (civId * 137.508f) % 360f;
        return HsvToRgb(hue, 0.65f, 0.90f);
    }

    private static Color HsvToRgb(float h, float s, float v)
    {
        h = h % 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float r1, g1, b1;
        if      (h < 60)  { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else              { r1 = c; g1 = 0; b1 = x; }
        return new Color((int)((r1 + m) * 255), (int)((g1 + m) * 255), (int)((b1 + m) * 255));
    }
}
