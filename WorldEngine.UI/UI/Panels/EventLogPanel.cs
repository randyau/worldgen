using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Present;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Pinned Event Log panel: recent sim events, migrated onto the kit (M8.3.4).
/// <summary>
/// Pinned panel showing recent simulation events. Supports focus lens dimming and routes actor/
/// civ/cause-chain clicks immediately (M8.2.2) instead of a consume-once poll.
/// </summary>
public sealed class EventLogPanel : IWorkspacePanel
{
    private readonly WeList<SimEvent> _list = new();
    private PanelContext _ctx;
    private FocusLensState? _focusLens;
    private EventLogFilter _filter = EventLogFilter.Default;

    // BUG FIX: WeList.SetItems destroys and recreates every row widget, including the actor-name
    // buttons the player clicks to select a character. Refresh() runs on every committed sim
    // tick, so while the sim is running this teardown raced with in-flight clicks (mouse-down on
    // a button that no longer exists by mouse-up loses the click — Myra can't deliver it).
    // Skip the rebuild entirely when nothing that BuildRow depends on has actually changed.
    private List<long>? _lastRowIds;
    private EventLogFilter? _lastFilter;
    private FocusType _lastFocusType = FocusType.None;
    private long _lastFocusTarget;

    // M8.2.2: fired immediately at the click site instead of polled by Game1 each frame.
    public Action<long>? OnCauseChain;
    public Action<long>? OnCharacterProfile;
    public Action<long>? OnCiv;

    public string Id => "eventlog";
    public string Title => "Event Log";
    public PanelPlacement Placement => new(PanelPlacementKind.PinnedDefault);

    // BUG FIX: the pre-migration panel had a fixed-height ScrollViewer (Height=250) around its
    // rows; the M8.3.4 migration dropped that and let the list grow to fit content, which pushed
    // Filters/Tile Inspector out of the dock's own outer scroll region. Cap it here with its own
    // internal scrollbar, same as before.
    public Widget Build() => PanelFrame.Build(Title, new ScrollViewer { Content = _list.Root, Height = 280 });

    public void Bind(PanelContext ctx) => _ctx = ctx;

    /// <summary>Supplies the focus lens and current filter; set by Game1 before the frame's Refresh.</summary>
    public void SetContext(FocusLensState? focusLens, EventLogFilter? filter)
    {
        _focusLens = focusLens;
        _filter    = filter ?? EventLogFilter.Default;
    }

    public void Refresh()
    {
        var snapshot = _ctx.Snapshot;
        var present  = _ctx.Present;

        // Build CivId → CivName from territory+settlement tables for cross-panel civ links (6.3.4)
        var civNames = new Dictionary<long, string>();
        foreach (var (_, territory) in snapshot.TerritoryMap)
        {
            long cid = territory.CivId;
            if (cid > 0 && !civNames.ContainsKey(cid) &&
                snapshot.Settlements.TryGetValue(territory.CityTile, out var settle) &&
                !string.IsNullOrEmpty(settle.CivName))
            {
                civNames[cid] = settle.CivName;
            }
        }

        var rows = snapshot.RecentEvents.Reverse()
            .Where(ev => _filter.PassesTier(ev.TierInvolvement) && _filter.PassesDomain(ev.Domain)
                      && _filter.PassesActor(ev.ActorName) && _filter.PassesYear(ev.Year) && _filter.PassesGodMode(ev.IsGodMode))
            .ToList();

        var focusType   = _focusLens?.Type ?? FocusType.None;
        var focusTarget = _focusLens?.TargetId ?? 0;
        var rowIds      = rows.Select(ev => ev.Id.Value).ToList();

        bool unchanged = _lastRowIds is not null
            && _lastFilter == _filter
            && _lastFocusType == focusType
            && _lastFocusTarget == focusTarget
            && _lastRowIds.SequenceEqual(rowIds);

        if (unchanged) return;

        _lastRowIds     = rowIds;
        _lastFilter     = _filter;
        _lastFocusType  = focusType;
        _lastFocusTarget = focusTarget;

        _list.SetItems(rows, ev => BuildRow(ev, civNames, present));

        if (rows.Count == 0)
        {
            string msg = _filter.IsDefault ? "(no events yet)" : "(no events match filter)";
            var kind = _filter.IsDefault ? EmptyStateKind.PreSim : EmptyStateKind.FilteredEmpty;
            ((VerticalStackPanel)_list.Root).Widgets.Add(EmptyState.Build(kind, msg));
        }
    }

    private Widget BuildRow(SimEvent ev, IReadOnlyDictionary<long, string> civNames, Presenter present)
    {
        bool isFocused = _focusLens is null || _focusLens.Type == FocusType.None
                       || _focusLens.FocusedEventIds.Contains(ev.Id.Value);
        var textColor = isFocused ? UiTheme.TierColor(ev.TierInvolvement) : UiTheme.TextDisabled;

        var tierStripe = new Panel { Width = 4, Height = 20, Background = new SolidBrush(UiTheme.TierColor(ev.TierInvolvement)) };

        string shortDesc = present.EventVerbPhrase(ev.Type);
        string location  = ev.SettlementName is not null ? $" @ {ev.SettlementName}"
                          : ev.Location.HasValue          ? $" @({ev.Location.Value.X},{ev.Location.Value.Y})"
                          : "";
        string evText = $"[{ev.Year} {SeasonAbbrev(ev.Season)}] {shortDesc}{location}";

        var row = new WeHStack(UiTheme.Space.Xs);
        if (ev.IsGodMode) row.Add(new WeText("[G]", color: UiTheme.ColorRole.AccentGodMode));
        row.Add(tierStripe);

        if (ev.ActorId > 0 && ev.ActorName is not null && IsCharacterEvent(ev.Type))
        {
            long capturedActorId = ev.ActorId;
            row.Add(EntityLink.Build(new EntityRef(SelectionKind.Character, capturedActorId, default), ev.ActorName, _ctx.Selection));
        }

        row.Add(new WeText(evText, textColor));

        if (ev.CivId > 0 && civNames.TryGetValue(ev.CivId, out string? civName))
        {
            long capturedCivId = ev.CivId;
            row.Add(EntityLink.Build(new EntityRef(SelectionKind.Civ, capturedCivId, default), $"[{civName}]", _ctx.Selection));
        }

        long capturedEvId = ev.Id.Value;
        var causeBtn = new WeButton("->", () => OnCauseChain?.Invoke(capturedEvId)) { Width = 24, Height = 20 };
        row.Add(causeBtn);

        if (ev.IsFirstOfKind) row.Add(new WeText("★", color: UiTheme.ColorRole.AccentGodMode));

        return row.Root;
    }

    private static string SeasonAbbrev(Season season) => season switch
    {
        Season.Spring => "Sp",
        Season.Summer => "Su",
        Season.Autumn => "Au",
        Season.Winter => "Wi",
        _             => "??"
    };

    /// <summary>True if this event type typically has a meaningful actor who is a named character.</summary>
    private static bool IsCharacterEvent(EventType type) => type switch
    {
        EventType.CharacterBorn            or
        EventType.CharacterDied            or
        EventType.CharacterMarried         or
        EventType.CharacterExiled          or
        EventType.CharacterGrieved         or
        EventType.CharacterFlourishing     or
        EventType.CharacterSpiraling       or
        EventType.WarDeclared              or
        EventType.AllianceFormed           or
        EventType.RivalryFormed            or
        EventType.GoalFormed               or
        EventType.GoalResolved             or
        EventType.ArtworkCreated           or
        EventType.SettlementFounded        or
        EventType.SuccessionOccurred       or
        EventType.AppointedToRole          or
        EventType.DismissedFromRole        or
        EventType.ScholarDiscovery         or
        EventType.MerchantTradeCompleted   or
        EventType.TradePaid                or
        EventType.TradeRouteFormed         or
        EventType.CaravanRaided            or
        EventType.ArtifactPurchased        or
        EventType.PhysicianHealed          or
        EventType.BeastSlain               => true,
        _                                  => false
    };
}
