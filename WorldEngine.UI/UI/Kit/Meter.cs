using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 2 — labeled n-segment bar with numeric readout, serves needs/traits/health meters.
/// <summary>Labeled 0–1 bar with numeric readout (framework §4.2). Serves the watch panel's needs/traits/health.</summary>
public static class Meter
{
    public static Widget Build(string label, float value01, int segments = 10, UiTheme.ColorRole? state = null)
    {
        value01 = Math.Clamp(value01, 0f, 1f);
        int filled = (int)MathF.Round(value01 * segments);

        var row = new HorizontalStackPanel { Spacing = UiTheme.Space.Xs };
        row.Widgets.Add(new Label { Text = $"{label}:", TextColor = UiTheme.TextSecondary, Width = 90 });

        var barColor = UiTheme.Resolve(state ?? DefaultState(value01));
        var bar = new Panel { Width = segments * 8, Height = 12, Background = new SolidBrush(UiTheme.SurfaceRaised) };
        var fill = new Panel { Width = filled * 8, Height = 12, Background = new SolidBrush(barColor) };
        bar.Widgets.Add(fill);
        row.Widgets.Add(bar);

        row.Widgets.Add(new Label { Text = $"{value01:P0}", TextColor = barColor });
        return row;
    }

    private static UiTheme.ColorRole DefaultState(float value01) => value01 switch
    {
        >= 0.66f => UiTheme.ColorRole.StatePositive,
        >= 0.33f => UiTheme.ColorRole.StateWarning,
        _        => UiTheme.ColorRole.StateNegative
    };
}
