using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI.Theme;

/// <summary>
/// Builds the standard docked-panel chrome — a bordered, padded container with a title bar
/// and optional <c>[Close]</c> button — so every panel shares one look instead of each
/// re-inventing its own header/close/background (M6 Epic 6.2.1).
/// </summary>
// MAP: Helper that wraps panel content in consistent titled, bordered, padded chrome.
public static class PanelChrome
{
    /// <summary>
    /// Wraps <paramref name="body"/> in themed chrome with a <paramref name="title"/> header.
    /// When <paramref name="onClose"/> is supplied a "[Close]" button is added to the title row.
    /// </summary>
    public static Panel Wrap(string title, Widget body, Action? onClose = null)
    {
        var header = new Label
        {
            Text      = title,
            TextColor = UiTheme.HeaderText,
        };

        var titleRow = new HorizontalStackPanel { Spacing = UiTheme.PanelSpacing };
        titleRow.Widgets.Add(header);
        if (onClose is not null)
        {
            var closeBtn = new TextButton { Text = "[Close]", HorizontalAlignment = HorizontalAlignment.Right };
            closeBtn.Click += (_, _) => onClose();
            titleRow.Widgets.Add(closeBtn);
        }

        var stack = new VerticalStackPanel { Spacing = UiTheme.PanelSpacing };
        stack.Widgets.Add(titleRow);
        stack.Widgets.Add(body);

        var outer = new Panel
        {
            Width           = UiTheme.PanelWidth,
            Padding         = new Thickness(UiTheme.PanelPad),
            Background      = new SolidBrush(UiTheme.PanelBackground),
            Border          = new SolidBrush(UiTheme.PanelBorder),
            BorderThickness = new Thickness(1),
        };
        outer.Widgets.Add(stack);
        return outer;
    }
}
