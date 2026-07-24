using Microsoft.Xna.Framework;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Layout;

// MAP: Layer 4 — owns every screen rectangle; recomputes the fixed grid on resize.
/// <summary>
/// Owns every screen rectangle, z-band, and (via <see cref="RegionSlot"/>) hit-test precedence
/// (framework §3.2, §5.1). Panels declare content into a <see cref="Region"/>; they never see or
/// set a raw <c>Top/Left/Width/Height</c>. Fixed grid: <c>TopBar</c> (full width, top chrome
/// strip), <c>Timeline</c> (full width minus dock, bottom strip), <c>RightDock</c> (right column,
/// below TopBar), <c>MapCanvas</c> (remaining — non-opaque, camera/tile-pick owns it).
/// <c>Float</c>/<c>Modal</c> are viewport-sized overlays, not part of the grid.
/// </summary>
public sealed class LayoutHost
{
    /// <summary>Height reserved for TimeControls + OverlayBar stacked (was two floating rows pre-M8).</summary>
    public const int TopChromeHeight = 84;
    public const int TimelineHeight  = 40;
    public const int MinDockWidth    = 260;
    public const int MaxDockWidth    = 520;

    private readonly Dictionary<RegionSlot, Region> _regions;

    /// <summary>Right-dock width; resizable within [<see cref="MinDockWidth"/>, <see cref="MaxDockWidth"/>].
    /// MapCanvas recomputes from this on the next <see cref="SetViewport"/> so the map is never
    /// occluded by a resized dock.</summary>
    // DECISION: no drag-to-resize affordance yet (no interactive host in this environment to
    // verify); the property exists so a future story only needs to wire an input gesture to it.
    public int DockWidth { get; set; } = UiTheme.SidebarWidth;

    public Rectangle Viewport { get; private set; }

    public LayoutHost()
    {
        _regions = new Dictionary<RegionSlot, Region>
        {
            [RegionSlot.TopBar]    = new Region(RegionSlot.TopBar, UiTheme.ZBand.Chrome),
            [RegionSlot.MapCanvas] = new Region(RegionSlot.MapCanvas, UiTheme.ZBand.Base) { Opaque = false },
            [RegionSlot.RightDock] = new Region(RegionSlot.RightDock, UiTheme.ZBand.Chrome),
            [RegionSlot.Timeline]  = new Region(RegionSlot.Timeline, UiTheme.ZBand.Chrome),
            [RegionSlot.Float]     = new Region(RegionSlot.Float, UiTheme.ZBand.Float) { Opaque = false },
            [RegionSlot.Modal]     = new Region(RegionSlot.Modal, UiTheme.ZBand.Modal) { Opaque = false },
        };
    }

    public Region Slot(RegionSlot slot) => _regions[slot];

    /// <summary>Recomputes every region's <see cref="Region.Bounds"/> from the current viewport. Call on resize.</summary>
    public void SetViewport(Rectangle vp)
    {
        Viewport = vp;
        int dockWidth = Math.Clamp(DockWidth, MinDockWidth, MaxDockWidth);

        _regions[RegionSlot.TopBar].Bounds    = new Rectangle(vp.X, vp.Y, vp.Width, TopChromeHeight);
        _regions[RegionSlot.RightDock].Bounds = new Rectangle(vp.Right - dockWidth, vp.Y + TopChromeHeight, dockWidth, vp.Height - TopChromeHeight);
        _regions[RegionSlot.Timeline].Bounds  = new Rectangle(vp.X, vp.Bottom - TimelineHeight, vp.Width - dockWidth, TimelineHeight);
        _regions[RegionSlot.MapCanvas].Bounds = new Rectangle(vp.X, vp.Y + TopChromeHeight, vp.Width - dockWidth, vp.Height - TopChromeHeight - TimelineHeight);
        _regions[RegionSlot.Float].Bounds     = vp;
        _regions[RegionSlot.Modal].Bounds     = vp;
    }

    /// <summary>All regions ordered by z-band, highest first — the order the <see cref="InputRouter"/> tests them in.</summary>
    public IReadOnlyList<Region> RegionsTopDown() =>
        _regions.Values.OrderByDescending(r => (int)r.Band).ToList();
}
