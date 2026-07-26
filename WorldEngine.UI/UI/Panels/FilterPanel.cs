using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

/// <summary>
/// Immutable snapshot of the active event-log filter criteria. Passed to
/// <see cref="EventLogPanel"/> each frame via <see cref="FilterPanel.CurrentFilter"/>.
/// </summary>
public sealed record EventLogFilter(
    bool ShowHeadline,
    bool ShowRegional,
    bool ShowBackground,
    string DomainText,
    string ActorText,
    int? YearFrom,
    int? YearTo,
    bool HideGodMode = false)
{
    public static readonly EventLogFilter Default = new(
        ShowHeadline:   true,
        ShowRegional:   true,
        ShowBackground: false,
        DomainText:     "",
        ActorText:      "",
        YearFrom:       null,
        YearTo:         null,
        HideGodMode:    false);

    /// <summary>True when every event passes without extra filtering.</summary>
    public bool IsDefault =>
        ShowHeadline && ShowRegional && !ShowBackground &&
        string.IsNullOrEmpty(DomainText) && string.IsNullOrEmpty(ActorText) &&
        YearFrom is null && YearTo is null && !HideGodMode;

    public bool PassesGodMode(bool isGodMode) => !HideGodMode || !isGodMode;

    public bool PassesTier(EventTier tier) => tier switch
    {
        EventTier.Headline   => ShowHeadline,
        EventTier.Regional   => ShowRegional,
        EventTier.Character  => ShowRegional,
        EventTier.Background => ShowBackground,
        _                    => false
    };

    public bool PassesDomain(string domain) =>
        string.IsNullOrEmpty(DomainText) || domain.Contains(DomainText, StringComparison.OrdinalIgnoreCase);

    public bool PassesActor(string? actorName) =>
        string.IsNullOrEmpty(ActorText) ||
        (actorName is not null && actorName.Contains(ActorText, StringComparison.OrdinalIgnoreCase));

    public bool PassesYear(int year) =>
        (YearFrom is null || year >= YearFrom) && (YearTo is null || year <= YearTo);
}

// MAP: Layer 3 — Pinned collapsible filter section above the Event Log, migrated onto the kit (M8.3.4).
/// <summary>Collapsible filter panel above the event log. Owns the current <see cref="EventLogFilter"/>.</summary>
public sealed class FilterPanel : IWorkspacePanel
{
    public EventLogFilter CurrentFilter { get; private set; } = EventLogFilter.Default;

    private readonly WeCheckBox _headlineBox   = new("Headline", true);
    private readonly WeCheckBox _regionalBox   = new("Regional", true);
    private readonly WeCheckBox _backgroundBox = new("Background", false);
    private readonly WeCheckBox _godModeBox    = new("Hide God Mode", false);
    private readonly WeField    _domainBox     = new("Domain:", "Domain…");
    private readonly WeField    _actorBox      = new("Actor:", "Actor name…");
    private readonly WeField    _yearFromBox   = new("Year:", "From");
    private readonly WeField    _yearToBox     = new("–", "To");
    private readonly WeVStack   _body          = new(UiTheme.Space.Xs);
    private bool _expanded = true;
    private WeButton? _header;

    public string Id => "filter";
    public string Title => "Filters";
    public PanelPlacement Placement => new(PanelPlacementKind.PinnedDefault);

    public FilterPanel()
    {
        _headlineBox.Changed   += Rebuild;
        _regionalBox.Changed   += Rebuild;
        _backgroundBox.Changed += Rebuild;
        _godModeBox.Changed    += Rebuild;
        _domainBox.Changed     += Rebuild;
        _actorBox.Changed      += Rebuild;
        _yearFromBox.Changed   += Rebuild;
        _yearToBox.Changed     += Rebuild;
    }

    public Widget Build()
    {
        var tierRow = new WeHStack(UiTheme.Space.Sm);
        tierRow.Add(_headlineBox);
        tierRow.Add(_regionalBox);
        tierRow.Add(_backgroundBox);

        var yearRow = new WeHStack(UiTheme.Space.Xs);
        yearRow.Add(_yearFromBox);
        yearRow.Add(_yearToBox);

        var clearBtn = new WeButton("Clear", Clear, WeButtonVariant.Ghost) { Width = 46, Height = 20 };

        var bottomRow = new WeHStack(UiTheme.Space.Sm);
        bottomRow.Add(yearRow);
        bottomRow.Add(clearBtn);

        _body.Add(tierRow);
        _body.Add(_godModeBox);
        _body.Add(_domainBox);
        _body.Add(_actorBox);
        _body.Add(bottomRow);

        _header = new WeButton("▼ Filters", () =>
        {
            _expanded = !_expanded;
            _body.Root.Visible = _expanded;
            _header!.Text = _expanded ? "▼ Filters" : "▶ Filters";
        }) { Width = 80, Height = 18 };

        var wrapper = new WeVStack(UiTheme.Space.Xs);
        wrapper.Add(_header);
        wrapper.Add(_body);
        return wrapper.Root;
    }

    public void Bind(PanelContext ctx) { }
    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;
    public void Refresh() { /* filter state changes reactively via the Changed events above */ }

    private void Rebuild()
    {
        int? yearFrom = int.TryParse(_yearFromBox.Value, out int yf) ? yf : null;
        int? yearTo   = int.TryParse(_yearToBox.Value,   out int yt) ? yt : null;

        CurrentFilter = new EventLogFilter(
            ShowHeadline:   _headlineBox.IsChecked,
            ShowRegional:   _regionalBox.IsChecked,
            ShowBackground: _backgroundBox.IsChecked,
            DomainText:     _domainBox.Value,
            ActorText:      _actorBox.Value,
            YearFrom:       yearFrom,
            YearTo:         yearTo,
            HideGodMode:    _godModeBox.IsChecked);
    }

    private void Clear()
    {
        _headlineBox.IsChecked   = true;
        _regionalBox.IsChecked   = true;
        _backgroundBox.IsChecked = false;
        _godModeBox.IsChecked    = false;
        _domainBox.Value   = "";
        _actorBox.Value    = "";
        _yearFromBox.Value = "";
        _yearToBox.Value   = "";
        CurrentFilter = EventLogFilter.Default;
    }
}
