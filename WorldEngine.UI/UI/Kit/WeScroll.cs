using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — scroll container that reserves scrollbar width so content never hides behind it.
/// <summary>
/// Scroll container wrapping <see cref="ScrollViewer"/>. Content width is always
/// <c>available width - UiTheme.ScrollReserve</c>, and the viewer clamps to its assigned height
/// rather than growing past it — the single fix for the scrollbar-obstruction bug (framework §3.2).
/// </summary>
public sealed class WeScroll : IWeWidget
{
    private readonly ScrollViewer _viewer = new();

    public Widget Root => _viewer;

    /// <summary>Sets the scrollable content and the viewer's outer size; content width auto-reserves scrollbar space.</summary>
    public void SetContent(Widget content, int width, int height)
    {
        content.Width = Math.Max(0, width - UiTheme.ScrollReserve);
        _viewer.Content = content;
        _viewer.Width = width;
        _viewer.Height = height;
    }

    public void SetContent(IWeWidget content, int width, int height) => SetContent(content.Root, width, height);
}
