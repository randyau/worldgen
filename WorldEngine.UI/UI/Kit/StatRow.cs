using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 2 — aligned label/value row, replaces ad-hoc "Label: value" AddLine calls.
/// <summary>Aligned label ↔ value pair, optional unit and value color (framework §4.2).</summary>
public static class StatRow
{
    public static Widget Build(string label, string value, UiTheme.ColorRole valueColor = UiTheme.ColorRole.TextPrimary, string? unit = null)
    {
        var row = new HorizontalStackPanel { Spacing = UiTheme.Space.Sm };
        row.Widgets.Add(new Label { Text = $"{label}:", TextColor = UiTheme.TextSecondary, Width = 110 });
        string text = unit is null ? value : $"{value} {unit}";
        row.Widgets.Add(new Label { Text = text, TextColor = UiTheme.Resolve(valueColor) });
        return row;
    }
}

// MAP: Layer 2 — column-aligned set of StatRows.
/// <summary>Column-aligned set of <see cref="StatRow"/>s (framework §4.2).</summary>
public sealed class KeyValueGrid : IWeWidget
{
    private readonly VerticalStackPanel _panel = new() { Spacing = UiTheme.Space.Xs };

    public Widget Root => _panel;

    public void Add(string label, string value, UiTheme.ColorRole valueColor = UiTheme.ColorRole.TextPrimary, string? unit = null) =>
        _panel.Widgets.Add(StatRow.Build(label, value, valueColor, unit));

    public void Clear() => _panel.Widgets.Clear();
}
