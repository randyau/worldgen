using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Layout;

/// <summary>The fixed grid slots the <see cref="LayoutHost"/> lays out (framework §5.1).</summary>
public enum RegionSlot { TopBar, MapCanvas, RightDock, Timeline, Float, Modal }

// MAP: Layer 4 — a non-overlapping screen rectangle with a z-band; the unit the host lays out.
/// <summary>
/// A rectangle + z-band the <see cref="LayoutHost"/> owns. Regions never set absolute
/// geometry on their own content — the host assigns <see cref="Bounds"/> (framework §3.2).
/// </summary>
public sealed class Region
{
    public RegionSlot Slot { get; }
    public UiTheme.ZBand Band { get; }
    /// <summary>Whether this region consumes pointer input within its bounds. Map/Float regions are non-opaque.</summary>
    public bool Opaque { get; set; } = true;
    public Rectangle Bounds { get; internal set; }
    /// <summary>The Myra content assigned to this region, or null if empty.</summary>
    public Widget? Content { get; set; }

    public Region(RegionSlot slot, UiTheme.ZBand band) { Slot = slot; Band = band; }

    public bool HitTest(Point p) => Opaque && Bounds.Contains(p);
}
