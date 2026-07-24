using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// Dismissible first-run orientation dialog shown once when the simulation starts for the first time.
/// Points the player at the time controls, overlays, and event log.
/// </summary>
public static class FirstRunOverlay
{
    private static readonly string _flagFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorldEngine", "firstrun.done");

    /// <summary>True when the player has not yet dismissed the orientation.</summary>
    public static bool ShouldShow => !File.Exists(_flagFile);

    /// <summary>
    /// Shows the orientation dialog via the shared <see cref="ModalHost"/> (M8 8.1.5 proof case)
    /// and marks it as seen when dismissed. Call once after the simulation starts.
    /// </summary>
    public static void Show(ModalHost modal)
    {
        if (!ShouldShow) return;

        var content = new VerticalStackPanel { Spacing = 6, Width = 460 };
        content.Widgets.Add(new Label { Text = "Getting Started", TextColor = UiTheme.HeaderText });

        void AddTip(string icon, string text)
        {
            var row = new HorizontalStackPanel { Spacing = 6 };
            row.Widgets.Add(new Label { Text = icon, TextColor = Color.Gold, Width = 18 });
            row.Widgets.Add(new Label { Text = text, TextColor = UiTheme.BodyText });
            content.Widgets.Add(row);
        }

        content.Widgets.Add(new Label { Text = "Welcome to World Engine", TextColor = UiTheme.BodyText });
        content.Widgets.Add(new Label { Text = " ", Height = 4 });

        AddTip("▶", "Time Controls (top bar) — pause, play, or fast-forward history.");
        AddTip("■", "Overlays (top bar, left) — switch map layers: elevation, climate, territory…");
        AddTip(">>", "Event Log (right sidebar) — recent events; use the Filters to narrow results.");
        AddTip("->", "Click any event's -> button to trace its causal chain.");
        AddTip("?", "Press ? at any time to see all keybindings.");

        content.Widgets.Add(new Label { Text = " ", Height = 4 });

        var dismissBtn = new TextButton
        {
            Text              = "Got it — start exploring",
            HorizontalAlignment = HorizontalAlignment.Center
        };

        content.Widgets.Add(dismissBtn);

        dismissBtn.Click += (_, _) => modal.Close();

        modal.Show(content, onClose: MarkSeen);
    }

    private static void MarkSeen()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_flagFile)!);
            File.WriteAllText(_flagFile, "1");
        }
        catch { /* non-critical */ }
    }
}
