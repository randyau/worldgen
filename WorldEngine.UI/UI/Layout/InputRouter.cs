using Microsoft.Xna.Framework;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Layout;

// MAP: Layer 4 — arbitrates pointer input top-down by z-band; the click-leak fix.
/// <summary>
/// Arbitrates one pointer event per frame, top-down by z-band (framework §5.1). A Modal region
/// with content assigned captures unconditionally. Float/Transient regions are click-through by
/// design (legends, tooltips, toasts never block the map). Chrome regions consume when opaque and
/// hit. If nothing consumes, the caller (map/camera) gets the event.
/// </summary>
public sealed class InputRouter
{
    // DECISION: Float/Transient are click-through (framework §5.1) so a legend or toast never
    // blocks the map beneath it — only Modal and opaque Chrome/Base regions consume input.
    public Region? Route(Point pointer, LayoutHost host)
    {
        foreach (var region in host.RegionsTopDown())
        {
            if (region.Band == UiTheme.ZBand.Modal)
            {
                if (region.Content is not null) return region;
                continue;
            }
            if (region.Band is UiTheme.ZBand.Float or UiTheme.ZBand.Transient)
                continue;
            if (region.HitTest(pointer)) return region;
        }
        return null;
    }
}
