using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// Top-bar "Map Display" control (M6 Epic 6.1.1; collapsed to a dropdown per playtest feedback —
/// was a 7-button, 2-row grid). Selecting an overlay enqueues <c>SetActiveOverlay</c> — the same
/// command the accelerator keys fire — and the dropdown reflects <c>WorldSnapshot.ActiveOverlay</c>.
/// </summary>
// MAP: Top-bar Map Display dropdown; enqueues SetActiveOverlay and reflects the active overlay.
public sealed class OverlayBar
{
    public readonly Widget Root;
    private readonly WeDropdown<OverlayType> _dropdown = new();
    private OverlayType _active = (OverlayType)(-1); // force first Update to apply

    private static readonly (OverlayType Type, string Label)[] Overlays =
    {
        (OverlayType.Biome,          "Biome"),
        (OverlayType.Elevation,      "Elevation"),
        (OverlayType.Temperature,    "Temperature"),
        (OverlayType.Moisture,       "Moisture"),
        (OverlayType.Resources,      "Resources"),
        (OverlayType.MagicIntensity, "Magic"),
        (OverlayType.Territory,      "Territory"),
    };

    public OverlayBar(CommandQueue queue)
    {
        var row = new WeHStack(UiTheme.Space.Sm);
        row.Add(new WeText("Map:"));

        _dropdown.Render(t => Overlays.First(o => o.Type == t).Label);
        _dropdown.SetItems(Overlays.Select(o => o.Type));
        _dropdown.OnChanged += t => queue.Enqueue(new SetActiveOverlay(t));
        row.Add(_dropdown);

        Root = row.Root;
    }

    /// <summary>Reflects the currently active overlay in the dropdown (called each snapshot).</summary>
    public void Update(OverlayType active)
    {
        if (active == _active) return;
        _active = active;
        _dropdown.Selected = active;
    }
}
