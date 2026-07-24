using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI.Layout;

// MAP: Layer 3 seam — wraps a pre-M8 panel's existing Root as an IWorkspacePanel without rebuilding it.
/// <summary>
/// Adapts an existing (pre-M8) panel into <see cref="IWorkspacePanel"/> by exposing its current
/// <c>Root</c> widget and forwarding refresh to whatever bespoke <c>Update</c>/<c>Refresh</c>
/// method it already has (framework §12 migration path). Rebuilt panels in 8.3 replace this.
/// </summary>
// DECISION: the adapter keeps the wrapped panel's self-sizing internally for this phase; the
// host clamps the region regardless, so the historic bugs are already fixed even before the
// panel itself is rebuilt on the kit (8.3 removes the self-sizing).
public sealed class LegacyPanelAdapter : IToggleablePanel
{
    private readonly Widget _root;
    private readonly Action<PanelContext>? _onRefresh;
    private PanelContext _ctx;

    public string Id { get; }
    public string Title { get; }
    public PanelPlacement Placement { get; }

    /// <summary>Optional hooks for panels that need extra work on show/hide beyond toggling <c>Visible</c>.</summary>
    public Action? OnShow { get; init; }
    public Action? OnHide { get; init; }
    public Func<bool>? IsVisibleFunc { get; init; }

    public LegacyPanelAdapter(string id, string title, PanelPlacement placement, Widget root, Action<PanelContext>? onRefresh = null)
    {
        Id = id;
        Title = title;
        Placement = placement;
        _root = root;
        _onRefresh = onRefresh;
    }

    public Widget Build() => _root;
    public void Bind(PanelContext ctx) => _ctx = ctx;
    public void Refresh() => _onRefresh?.Invoke(_ctx);
    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;

    public void Show() { _root.Visible = true;  OnShow?.Invoke(); }
    public void Hide() { _root.Visible = false; OnHide?.Invoke(); }
    public bool IsVisible => IsVisibleFunc?.Invoke() ?? _root.Visible;
}
