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
public sealed class HelpOverlayPanel : IPanel
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

        // Static section: button-based flows that have no keyboard shortcut
        _content.Widgets.Add(new Label { Text = "GOD MODE (F2)", TextColor = UiTheme.HeaderText });
        _content.Widgets.Add(new Label { Text = "  1. Click map tile to select target", TextColor = UiTheme.BodyText });
        _content.Widgets.Add(new Label { Text = "  2. Pause (Space), then choose action", TextColor = UiTheme.BodyText });
        _content.Widgets.Add(new Label { Text = "  Nudge: open Watch (W) first to select character", TextColor = UiTheme.BodyText });
        _content.Widgets.Add(new Label { Text = "" });
        _content.Widgets.Add(new Label { Text = "SPOTLIGHT (W panel)", TextColor = UiTheme.HeaderText });
        _content.Widgets.Add(new Label { Text = "  Open Watch (W) → [Enter Spotlight]", TextColor = UiTheme.BodyText });
        _content.Widgets.Add(new Label { Text = "  Click map tile → move intent", TextColor = UiTheme.BodyText });
        _content.Widgets.Add(new Label { Text = "  Goal buttons → bias character behavior", TextColor = UiTheme.BodyText });
        _content.Widgets.Add(new Label { Text = "  Character remains autonomous; intent biases decisions", TextColor = UiTheme.BodyText });
    }

    public void Show() { Root.Visible = true; IsVisible = true; }
    public void Hide() { Root.Visible = false; IsVisible = false; }
    public void Toggle() { if (IsVisible) Hide(); else Show(); }
}
