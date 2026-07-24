using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

/// <summary>Optional affordances for a <see cref="PanelFrame"/> title row.</summary>
public sealed class PanelFrameOptions
{
    public Action? OnClose { get; init; }
    public Action? OnPin { get; init; }
}

// MAP: Layer 2 — titled/bordered/padded panel shell; the PanelChrome.Wrap successor.
/// <summary>
/// Titled, bordered, padded panel shell (framework §4.2). Supersedes <c>PanelChrome.Wrap</c>.
/// Unlike <c>PanelChrome</c>, this does <b>not</b> set <c>Width</c> — the layout host (8.1)
/// sizes the panel; callers only declare content.
/// </summary>
public static class PanelFrame
{
    public static Panel Build(string title, Widget body, PanelFrameOptions? opts = null)
    {
        var titleRow = new HorizontalStackPanel { Spacing = UiTheme.Space.Sm };
        titleRow.Widgets.Add(new Label { Text = title, TextColor = UiTheme.TextHeader });

        if (opts?.OnPin is { } onPin)
        {
            var pinBtn = new TextButton { Text = "[Pin]", HorizontalAlignment = HorizontalAlignment.Right };
            pinBtn.Click += (_, _) => onPin();
            titleRow.Widgets.Add(pinBtn);
        }
        if (opts?.OnClose is { } onClose)
        {
            var closeBtn = new TextButton { Text = "[Close]", HorizontalAlignment = HorizontalAlignment.Right };
            closeBtn.Click += (_, _) => onClose();
            titleRow.Widgets.Add(closeBtn);
        }

        var stack = new VerticalStackPanel { Spacing = UiTheme.Space.Sm };
        stack.Widgets.Add(titleRow);
        stack.Widgets.Add(body);

        var outer = new Panel
        {
            Padding         = new Thickness(UiTheme.PanelPad),
            Background      = new SolidBrush(UiTheme.SurfacePanel),
            Border          = new SolidBrush(UiTheme.BorderPanel),
            BorderThickness = new Thickness(1),
        };
        outer.Widgets.Add(stack);
        return outer;
    }

    public static Panel Build(string title, IWeWidget body, PanelFrameOptions? opts = null) =>
        Build(title, body.Root, opts);
}
