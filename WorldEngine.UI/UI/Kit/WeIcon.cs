using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — icon glyph with mandatory accessible label; no icon font is pinned yet.
/// <summary>
/// Icon glyph with a mandatory tooltip/label (framework §4.1: "never icon-only for anything
/// non-obvious"). No icon font is currently pinned in the project — renders a short text glyph
/// until one is added.
/// </summary>
// DECISION: FontAwesome is referenced in project docs but not yet a NuGet dependency; WeIcon
// renders its glyph string directly (e.g. "✖") rather than blocking on that pin.
public sealed class WeIcon : IWeWidget
{
    private readonly Label _label;

    public Widget Root => _label;

    public WeIcon(string glyph, string tooltipOrLabel, UiTheme.ColorRole color = UiTheme.ColorRole.TextPrimary)
    {
        if (string.IsNullOrWhiteSpace(tooltipOrLabel))
            throw new ArgumentException("WeIcon requires a non-empty tooltip/label.", nameof(tooltipOrLabel));

        _label = new Label { Text = glyph, TextColor = UiTheme.Resolve(color) };
        Tooltip.Attach(_label, tooltipOrLabel);
    }
}
