using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Layout;

// MAP: Layer 5 — the tabbed right dock: pinned zone + single-visible contextual tab (framework §5.2-5.3).
/// <summary>
/// Owns the <see cref="RegionSlot.RightDock"/> content: a pinned zone (always-visible panels,
/// stacked) and a contextual tab zone where exactly one panel is visible at a time — no
/// cross-panel overflow, no stacking (framework §5.2-5.3). Also drives the Float region's
/// summoned panels (God Mode, Help).
/// </summary>
// MOD SEAM: PanelRegistry — panels are registered by id at StartSim time; a future modding
// surface could register third-party panels the same way.
public sealed class SimWorkspace
{
    private readonly Dictionary<string, IWorkspacePanel> _panels = new();
    private readonly Dictionary<string, Widget> _built = new();
    private readonly Dictionary<string, TextButton> _tabButtons = new();

    private readonly WeVStack _pinnedStack     = new(UiTheme.Space.Sm);
    private readonly WeHStack _tabStrip        = new(UiTheme.Space.Xs);
    private readonly WeVStack _contextualBody  = new(UiTheme.Space.Sm);
    private readonly WeVStack _dockRoot        = new(UiTheme.Space.Md);
    private readonly WeVStack _floatStack      = new(UiTheme.Space.Sm);

    private string? _activeContextualId;
    private PanelContext _ctx;

    public Widget DockRoot  => _dockRoot.Root;
    public Widget FloatRoot => _floatStack.Root;

    public SimWorkspace()
    {
        _dockRoot.Add(_pinnedStack);
        _dockRoot.Add(_tabStrip);
        _dockRoot.Add(_contextualBody);
    }

    public void Register(IWorkspacePanel panel)
    {
        _panels[panel.Id] = panel;
        if (panel.Placement.Kind == PanelPlacementKind.PinnedDefault)
            _pinnedStack.Add(GetBuilt(panel));
    }

    /// <summary>Binds the current frame's context to every registered panel.</summary>
    public void Bind(PanelContext ctx)
    {
        _ctx = ctx;
        foreach (var panel in _panels.Values) panel.Bind(ctx);
    }

    /// <summary>Refreshes pinned panels, the active contextual panel, and any visible summoned panel.</summary>
    public void RefreshVisible()
    {
        foreach (var panel in _panels.Values)
        {
            bool visible = panel.Placement.Kind switch
            {
                PanelPlacementKind.PinnedDefault => true,
                PanelPlacementKind.Contextual     => panel.Id == _activeContextualId,
                PanelPlacementKind.Summoned       => panel is IToggleablePanel { IsVisible: true },
                _                                  => false
            };

            // Summoned panels can call Hide() from inside their own [Close] button (PanelFrame's
            // OnClose), not just through ToggleSummoned/ShowSummoned — sync the built widget's
            // Visible here every frame so every Hide() path actually hides it (see the bug note
            // on TryGetToggleable).
            if (panel.Placement.Kind == PanelPlacementKind.Summoned && _built.TryGetValue(panel.Id, out var widget))
                widget.Visible = visible;

            if (visible) panel.Refresh();
        }
    }

    /// <summary>Routes a selection to its matching contextual panel, or clears the contextual zone if none match.</summary>
    public void SetSelection(SelectionKind kind)
    {
        var match = _panels.Values.FirstOrDefault(p =>
            p.Placement.Kind == PanelPlacementKind.Contextual && p.Placement.For == kind);

        if (match is null) { ClearContextual(); return; }
        ShowContextual(match);
    }

    private void ShowContextual(IWorkspacePanel panel)
    {
        if (!_tabButtons.TryGetValue(panel.Id, out var btn))
        {
            btn = new TextButton { Text = panel.Title };
            btn.Click += (_, _) => ShowContextual(panel);
            _tabButtons[panel.Id] = btn;
            _tabStrip.Add(btn);
        }
        foreach (var (id, b) in _tabButtons)
            b.TextColor = id == panel.Id ? UiTheme.AccentInteractive : UiTheme.TextPrimary;

        if (_activeContextualId == panel.Id) return;
        _activeContextualId = panel.Id;
        _contextualBody.Clear();
        _contextualBody.Add(GetBuilt(panel));
        panel.Refresh();
    }

    private void ClearContextual()
    {
        _activeContextualId = null;
        _contextualBody.Clear();
    }

    /// <summary>Shows/hides a Summoned panel (God Mode, Help, Watch, Civ History) in the Float region.</summary>
    public void ToggleSummoned(string id)
    {
        if (!TryGetToggleable(id, out var toggleable)) return;
        if (toggleable!.IsVisible) toggleable.Hide(); else toggleable.Show();
    }

    /// <summary>Ensures a Summoned panel is shown (not a toggle — used when a click should always reveal it).</summary>
    public void ShowSummoned(string id)
    {
        if (TryGetToggleable(id, out var toggleable) && !toggleable!.IsVisible)
            toggleable.Show();
    }

    /// <summary>True if the given Summoned panel is currently visible — for highlighting a menu button.</summary>
    public bool IsSummonedVisible(string id) =>
        _panels.TryGetValue(id, out var panel) && panel is IToggleablePanel { IsVisible: true };

    private bool TryGetToggleable(string id, out IToggleablePanel? toggleable)
    {
        toggleable = null;
        if (!_panels.TryGetValue(id, out var panel) || panel is not IToggleablePanel t) return false;
        if (!_built.ContainsKey(id))
        {
            var widget = GetBuilt(panel);
            widget.Visible = false; // panels default IsVisible=false; RefreshVisible() keeps this in sync from here on
            _floatStack.Add(widget);
        }
        toggleable = t;
        return true;
    }

    /// <summary>Drops all panel registrations and clears the dock/float content for a world reset;
    /// the next <see cref="Register"/> calls (from a fresh StartSim) rebuild it from scratch.</summary>
    // DECISION: unlike the retired PanelManager, this does not remember open/closed state across
    // a reset — each StartSim creates fresh panel instances that already default to hidden.
    public void Reset()
    {
        _panels.Clear();
        _built.Clear();
        _tabButtons.Clear();
        _pinnedStack.Clear();
        _tabStrip.Clear();
        _contextualBody.Clear();
        _floatStack.Clear();
        _activeContextualId = null;
    }

    private Widget GetBuilt(IWorkspacePanel panel)
    {
        if (!_built.TryGetValue(panel.Id, out var w))
        {
            w = panel.Build();
            _built[panel.Id] = w;
        }
        return w;
    }
}
