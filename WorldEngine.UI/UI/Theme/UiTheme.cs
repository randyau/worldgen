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

    // ── M8 color roles (framework §2.1) ─────────────────────────────────────
    // Canonical role names; several alias the pre-M8 members above during migration.
    public static readonly Color TextPrimary      = BodyText;
    public static readonly Color TextSecondary    = MutedText;
    public static readonly Color TextMuted        = Color.Gray;
    public static readonly Color TextDisabled     = DisabledText;
    public static readonly Color TextHeader       = HeaderText;
    public static readonly Color AccentInteractive = Accent;
    /// <summary>God Mode authoring surfaces — gold/amber (framework §7.7).</summary>
    public static readonly Color AccentGodMode    = new(230, 180, 60);
    /// <summary>Spotlight surfaces — cyan (framework §7.7).</summary>
    public static readonly Color AccentSpotlight  = new(80, 220, 220);
    public static readonly Color StatePositive    = Color.LightGreen;
    public static readonly Color StateWarning     = new(230, 180, 60);
    public static readonly Color StateNegative    = new(230, 90, 70);
    public static readonly Color SurfacePanel     = PanelBackground;
    public static readonly Color SurfaceRaised    = new Color(32, 35, 43) * 0.95f;
    public static readonly Color SurfaceModalScrim = new Color(0, 0, 0) * 0.55f;
    public static readonly Color BorderPanel      = PanelBorder;
    public static readonly Color BorderFocus      = Accent;

    // ── Typography roles (framework §2.2) ───────────────────────────────────
    /// <summary>Named text roles; Layer 1 (<c>UI/Kit/WeText.cs</c>) maps these to Myra font/scale.</summary>
    public enum TypographyRole { Display, Title, SectionHeader, Body, BodyStrong, Caption, Mono }

    /// <summary>Named color roles; Layer 1 widgets accept only this, never a raw <see cref="Color"/>.</summary>
    public enum ColorRole
    {
        TextPrimary, TextSecondary, TextMuted, TextDisabled, TextHeader,
        AccentInteractive, AccentGodMode, AccentSpotlight,
        StatePositive, StateWarning, StateNegative
    }

    /// <summary>Resolves a <see cref="ColorRole"/> to its current <see cref="Color"/> value.</summary>
    public static Color Resolve(ColorRole role) => role switch
    {
        ColorRole.TextPrimary       => TextPrimary,
        ColorRole.TextSecondary     => TextSecondary,
        ColorRole.TextMuted         => TextMuted,
        ColorRole.TextDisabled      => TextDisabled,
        ColorRole.TextHeader        => TextHeader,
        ColorRole.AccentInteractive => AccentInteractive,
        ColorRole.AccentGodMode     => AccentGodMode,
        ColorRole.AccentSpotlight   => AccentSpotlight,
        ColorRole.StatePositive     => StatePositive,
        ColorRole.StateWarning      => StateWarning,
        ColorRole.StateNegative     => StateNegative,
        _                           => TextPrimary
    };

    // ── Spacing ramp (framework §2.3) ───────────────────────────────────────
    public static class Space
    {
        public const int Xs = 2;
        public const int Sm = 4;
        public const int Md = 8;
        public const int Lg = 12;
        public const int Xl = 16;
    }

    // ── Z-bands (framework §2.4 / §5.1; consumed by the layout host in 8.1) ─
    public enum ZBand { Base = 0, Chrome = 100, Float = 200, Transient = 300, Modal = 400 }

    /// <summary>Pixels reserved for the scrollbar so content never hides behind it (framework §3.2).</summary>
    public const int ScrollReserve = 16;

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
