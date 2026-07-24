using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — tokenized-spacing vertical/horizontal stacks wrapping Myra StackPanels.
/// <summary>Vertical stack with tokenized spacing (framework §4.1). Wraps <see cref="VerticalStackPanel"/>.</summary>
public sealed class WeVStack : IWeWidget
{
    private readonly VerticalStackPanel _panel;

    public Widget Root => _panel;

    public WeVStack(int spacing = UiTheme.Space.Sm) => _panel = new VerticalStackPanel { Spacing = spacing };

    public void Add(Widget widget) => _panel.Widgets.Add(widget);
    public void Add(IWeWidget widget) => _panel.Widgets.Add(widget.Root);
    public void Clear() => _panel.Widgets.Clear();
}

/// <summary>Horizontal stack with tokenized spacing (framework §4.1). Wraps <see cref="HorizontalStackPanel"/>.</summary>
public sealed class WeHStack : IWeWidget
{
    private readonly HorizontalStackPanel _panel;

    public Widget Root => _panel;

    public WeHStack(int spacing = UiTheme.Space.Sm) => _panel = new HorizontalStackPanel { Spacing = spacing };

    public void Add(Widget widget) => _panel.Widgets.Add(widget);
    public void Add(IWeWidget widget) => _panel.Widgets.Add(widget.Root);
    public void Clear() => _panel.Widgets.Clear();
}
