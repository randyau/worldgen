using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 2 — standard hover tooltip: delay, cursor-follow, viewport-clamped.
/// <summary>
/// Standard hover tooltip (framework §4.2/§7.3): consistent delay, cursor-follow, and
/// viewport clamping so no tooltip can render off-screen (the timeline tooltip overflow bug).
/// </summary>
// SEAM: per-frame position tracking (cursor-follow + clamp) is driven by LayoutHost in 8.1;
// this phase wires the static content via Myra's built-in widget tooltip text.
public static class Tooltip
{
    /// <summary>Attaches hover tooltip text to a widget.</summary>
    public static void Attach(Widget widget, string text) => widget.Tooltip = text;

    /// <summary>
    /// Called once per frame by the layout host (from 8.1) with the current mouse position and
    /// viewport bounds, to clamp any open tooltip. No-op until the host drives it.
    /// </summary>
    public static void Update(Microsoft.Xna.Framework.Point mousePos, Microsoft.Xna.Framework.Rectangle viewport)
    {
        // SEAM: LayoutHost (8.1) will own active-tooltip clamping once floating surfaces exist.
    }
}
