using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — tokenized text widget wrapping Myra Label; only entry point for rendering text.
/// <summary>
/// The only way to render text in the M8 kit. Wraps <see cref="Label"/> and accepts only
/// tokenized <see cref="UiTheme.TypographyRole"/> / <see cref="UiTheme.ColorRole"/> — a caller
/// cannot pass a raw <see cref="Microsoft.Xna.Framework.Color"/> (framework §4.1).
/// </summary>
public sealed class WeText : IWeWidget
{
    private readonly Label _label = new();

    /// <summary>Typography role this text was constructed with (kept for future font-scale mapping).</summary>
    public UiTheme.TypographyRole Role { get; }

    public Widget Root => _label;

    // DECISION: Myra 1.6 (as pinned) exposes only a single default font/size on Label, so
    // TypographyRole is stored but not yet applied to glyph size — no visual change this phase.
    // A multi-weight font set (or per-role SpriteFontBase) is the seam for a later phase.
    public WeText(string text, UiTheme.TypographyRole role = UiTheme.TypographyRole.Body,
        UiTheme.ColorRole color = UiTheme.ColorRole.TextPrimary)
    {
        Role = role;
        _label.Text = text;
        _label.TextColor = UiTheme.Resolve(color);
    }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    public void SetColor(UiTheme.ColorRole color) => _label.TextColor = UiTheme.Resolve(color);

    // Escape hatch for themed-but-dynamic lookups (e.g. UiTheme.TierColor(tier)) that don't map
    // to a single ColorRole — not for hand-picked literals; callers must still source the Color
    // from a UiTheme helper.
    public WeText(string text, Color themedColor, UiTheme.TypographyRole role = UiTheme.TypographyRole.Body)
    {
        Role = role;
        _label.Text = text;
        _label.TextColor = themedColor;
    }
}
