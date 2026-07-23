using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// Immutable snapshot of the active event-log filter criteria.
/// Passed to <see cref="EventLogPanel.Update"/> each frame.
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

    /// <summary>True if this god-mode event should be shown.</summary>
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
        string.IsNullOrEmpty(DomainText) ||
        domain.Contains(DomainText, StringComparison.OrdinalIgnoreCase);

    public bool PassesActor(string? actorName) =>
        string.IsNullOrEmpty(ActorText) ||
        (actorName is not null && actorName.Contains(ActorText, StringComparison.OrdinalIgnoreCase));

    public bool PassesYear(int year) =>
        (YearFrom is null || year >= YearFrom) &&
        (YearTo   is null || year <= YearTo);
}

/// <summary>
/// Collapsible filter panel above the event log.
/// Owns and exposes the current <see cref="EventLogFilter"/> for the event log to consume.
/// </summary>
public sealed class FilterPanel
{
    public readonly Panel Root;
    public EventLogFilter CurrentFilter { get; private set; } = EventLogFilter.Default;

    private readonly CheckBox _headlineBox;
    private readonly CheckBox _regionalBox;
    private readonly CheckBox _backgroundBox;
    private readonly CheckBox _godModeBox;
    private readonly TextBox  _domainBox;
    private readonly TextBox  _actorBox;
    private readonly TextBox  _yearFromBox;
    private readonly TextBox  _yearToBox;
    private readonly Panel    _body;
    private bool              _expanded = true;

    public FilterPanel()
    {
        // ── Tier checkboxes ────────────────────────────────────────────────────
        _headlineBox   = new CheckBox { Text = "Headline",   IsChecked = true };
        _regionalBox   = new CheckBox { Text = "Regional",   IsChecked = true };
        _backgroundBox = new CheckBox { Text = "Background", IsChecked = false };
        _godModeBox    = new CheckBox { Text = "Hide God Mode", IsChecked = false };

        var tierRow = new HorizontalStackPanel { Spacing = 6 };
        tierRow.Widgets.Add(_headlineBox);
        tierRow.Widgets.Add(_regionalBox);
        tierRow.Widgets.Add(_backgroundBox);

        // ── Domain text field ──────────────────────────────────────────────────
        _domainBox = new TextBox { Width = 90, HintText = "Domain…" };

        var domainRow = new HorizontalStackPanel { Spacing = 4 };
        domainRow.Widgets.Add(new Label { Text = "Domain:", TextColor = UiTheme.MutedText, VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center });
        domainRow.Widgets.Add(_domainBox);

        // ── Actor text field ───────────────────────────────────────────────────
        _actorBox = new TextBox { Width = 90, HintText = "Actor name…" };

        var actorRow = new HorizontalStackPanel { Spacing = 4 };
        actorRow.Widgets.Add(new Label { Text = "Actor:", TextColor = UiTheme.MutedText, VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center });
        actorRow.Widgets.Add(_actorBox);

        // ── Year range ─────────────────────────────────────────────────────────
        _yearFromBox = new TextBox { Width = 55, HintText = "From" };
        _yearToBox   = new TextBox { Width = 55, HintText = "To" };

        var yearRow = new HorizontalStackPanel { Spacing = 4 };
        yearRow.Widgets.Add(new Label { Text = "Year:", TextColor = UiTheme.MutedText, VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center });
        yearRow.Widgets.Add(_yearFromBox);
        yearRow.Widgets.Add(new Label { Text = "–", VerticalAlignment = Myra.Graphics2D.UI.VerticalAlignment.Center });
        yearRow.Widgets.Add(_yearToBox);

        // ── Clear button ───────────────────────────────────────────────────────
        var clearBtn = new TextButton { Text = "Clear", Width = 46, Height = 20 };
        clearBtn.Click += (_, _) => Clear();

        var bottomRow = new HorizontalStackPanel { Spacing = 6 };
        bottomRow.Widgets.Add(yearRow);
        bottomRow.Widgets.Add(clearBtn);

        // ── Collapse body ──────────────────────────────────────────────────────
        _body = new Panel { Visible = _expanded };
        var bodyStack = new VerticalStackPanel { Spacing = 3 };
        bodyStack.Widgets.Add(tierRow);
        bodyStack.Widgets.Add(_godModeBox);
        bodyStack.Widgets.Add(domainRow);
        bodyStack.Widgets.Add(actorRow);
        bodyStack.Widgets.Add(bottomRow);
        _body.Widgets.Add(bodyStack);

        // ── Header toggle ──────────────────────────────────────────────────────
        var header = new TextButton { Text = "▼ Filters", Width = 80, Height = 18 };
        header.Click += (_, _) =>
        {
            _expanded = !_expanded;
            _body.Visible = _expanded;
            header.Text = _expanded ? "▼ Filters" : "▶ Filters";
        };

        // ── Reactive filter rebuild ────────────────────────────────────────────
        _headlineBox.IsCheckedChanged   += (_, _) => Rebuild();
        _regionalBox.IsCheckedChanged   += (_, _) => Rebuild();
        _backgroundBox.IsCheckedChanged += (_, _) => Rebuild();
        _godModeBox.IsCheckedChanged    += (_, _) => Rebuild();
        _domainBox.TextChangedByUser    += (_, _) => Rebuild();
        _actorBox.TextChangedByUser     += (_, _) => Rebuild();
        _yearFromBox.TextChangedByUser  += (_, _) => Rebuild();
        _yearToBox.TextChangedByUser    += (_, _) => Rebuild();

        var wrapper = new VerticalStackPanel { Spacing = 2 };
        wrapper.Widgets.Add(header);
        wrapper.Widgets.Add(_body);

        Root = new Panel();
        Root.Widgets.Add(wrapper);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        int? yearFrom = int.TryParse(_yearFromBox.Text, out int yf) ? yf : null;
        int? yearTo   = int.TryParse(_yearToBox.Text,   out int yt) ? yt : null;

        CurrentFilter = new EventLogFilter(
            ShowHeadline:   _headlineBox.IsChecked,
            ShowRegional:   _regionalBox.IsChecked,
            ShowBackground: _backgroundBox.IsChecked,
            DomainText:     _domainBox.Text ?? "",
            ActorText:      _actorBox.Text  ?? "",
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
        _domainBox.Text   = "";
        _actorBox.Text    = "";
        _yearFromBox.Text = "";
        _yearToBox.Text   = "";
        CurrentFilter = EventLogFilter.Default;
    }
}
