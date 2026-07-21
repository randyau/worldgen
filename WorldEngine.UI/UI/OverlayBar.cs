using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// Visible, labeled overlay toolbar (M6 Epic 6.1.1). One button per <see cref="OverlayType"/>;
/// each enqueues <c>SetActiveOverlay</c> — the same command the accelerator keys fire — and the
/// active overlay is highlighted from <c>WorldSnapshot.ActiveOverlay</c>. Restores Temperature,
/// which had been dropped off the keyboard.
/// </summary>
// MAP: Labeled overlay toggle bar; enqueues SetActiveOverlay and highlights the active overlay.
public sealed class OverlayBar
{
    public readonly HorizontalStackPanel Root;
    private readonly Dictionary<OverlayType, TextButton> _buttons = new();
    private OverlayType _active = (OverlayType)(-1);   // force first Update to apply

    private static readonly (OverlayType Type, string Label)[] Overlays =
    {
        (OverlayType.Biome,          "Biome"),
        (OverlayType.Elevation,      "Elevation"),
        (OverlayType.Temperature,    "Temp"),
        (OverlayType.Moisture,       "Moisture"),
        (OverlayType.Resources,      "Resources"),
        (OverlayType.MagicIntensity, "Magic"),
        (OverlayType.Territory,      "Territory"),
    };

    public OverlayBar(CommandQueue queue)
    {
        Root = new HorizontalStackPanel { Spacing = UiTheme.PanelSpacing };
        foreach (var (type, label) in Overlays)
        {
            var captured = type;
            var btn = new TextButton { Text = label };
            btn.Click += (_, _) => queue.Enqueue(new SetActiveOverlay(captured));
            _buttons[type] = btn;
            Root.Widgets.Add(btn);
        }
    }

    /// <summary>Highlights the button for the currently active overlay (called each snapshot).</summary>
    public void Update(OverlayType active)
    {
        if (active == _active) return;
        _active = active;
        foreach (var (type, btn) in _buttons)
            btn.TextColor = type == active ? UiTheme.Accent : UiTheme.BodyText;
    }
}
