using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// "?"-toggled panel listing every keyboard shortcut, grouped by category. Rendered directly
/// from the <see cref="KeybindRegistry"/> so it can never drift from actual input handling
/// (M6 Epic 6.1.3).
/// </summary>
// MAP: Help panel listing all shortcuts, generated from KeybindRegistry so it can't drift.
public sealed class HelpOverlayPanel
{
    private readonly VerticalStackPanel _content;

    public Widget Root { get; }
    public bool IsVisible { get; private set; }

    public HelpOverlayPanel()
    {
        _content = new VerticalStackPanel { Spacing = 2 };
        var scroll = new ScrollViewer { Content = _content, Width = UiTheme.ScrollWidth, Height = 460 };
        var outer = PanelChrome.Wrap("KEYBOARD SHORTCUTS", scroll, Hide);
        outer.Visible = false;
        Root = outer;
    }

    /// <summary>Rebuilds the shortcut list from the registry. Call once after the registry is built.</summary>
    public void Populate(KeybindRegistry registry)
    {
        _content.Widgets.Clear();
        foreach (var group in registry.Bindings.GroupBy(b => b.Category))
        {
            _content.Widgets.Add(new Label { Text = group.Key.ToUpperInvariant(), TextColor = UiTheme.HeaderText });
            foreach (var b in group)
                _content.Widgets.Add(new Label
                {
                    Text      = $"  {KeybindRegistry.KeyLabel(b),-10}  {b.Label}",
                    TextColor = UiTheme.BodyText
                });
            _content.Widgets.Add(new Label { Text = "" });
        }
    }

    public void Show() { Root.Visible = true; IsVisible = true; }
    public void Hide() { Root.Visible = false; IsVisible = false; }
    public void Toggle() { if (IsVisible) Hide(); else Show(); }
}
