using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — vertical list rebuilding rows from a data source (simple, non-virtualized).
/// <summary>
/// Vertical list of rows built from a data source. Non-virtualized — fine at current scale.
/// </summary>
// PERF: virtualize in 8.3.4 for M11 scale (large event/history logs).
public sealed class WeList<T> : IWeWidget
{
    private readonly VerticalStackPanel _panel = new() { Spacing = UiTheme.Space.Xs };

    public Widget Root => _panel;

    public void SetItems(IEnumerable<T> items, Func<T, Widget> buildRow)
    {
        _panel.Widgets.Clear();
        foreach (var item in items)
            _panel.Widgets.Add(buildRow(item));
    }

    public void Clear() => _panel.Widgets.Clear();
}
