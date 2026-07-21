using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// Sidebar panel showing recent simulation events.
/// Supports focus lens filtering (dimming events not involving the focus target)
/// and exposes pending requests for the character profile card and causal chain dialog.
/// </summary>
public sealed class EventLogPanel
{
    public readonly Panel Root;
    private readonly VerticalStackPanel _rows;
    private readonly ScrollViewer _scroll;

    // Consumed by Game1 each frame — cleared after reading
    private long? _pendingCauseChainEventId;
    private long? _pendingCharacterProfileId;
    private long? _pendingCivId;

    public EventLogPanel()
    {
        _rows   = new VerticalStackPanel { Spacing = 2 };
        _scroll = new ScrollViewer { Content = _rows, Width = 340, Height = 250 };

        Root = new Panel();
        Root.Widgets.Add(_scroll);
    }

    // ── Public API ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the event log from the latest snapshot.
    /// Applies <paramref name="filter"/> criteria and optionally dims events outside <paramref name="focusLens"/>.
    /// </summary>
    public void Update(WorldSnapshot snapshot, FocusLensState? focusLens = null, EventLogFilter? filter = null)
    {
        filter ??= EventLogFilter.Default;
        _rows.Widgets.Clear();

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

        int rowCount = 0;
        foreach (var ev in snapshot.RecentEvents.Reverse())
        {
            if (!filter.PassesTier(ev.TierInvolvement))  continue;
            if (!filter.PassesDomain(ev.Domain))          continue;
            if (!filter.PassesActor(ev.ActorName))        continue;
            if (!filter.PassesYear(ev.Year))              continue;

            bool isFocused = focusLens is null
                          || focusLens.Type == FocusType.None
                          || focusLens.FocusedEventIds.Contains(ev.Id.Value);

            Color textColor = isFocused ? UiTheme.TierColor(ev.TierInvolvement) : UiTheme.DisabledText;

            // Tier color stripe — 4px left bar showing event tier at a glance
            var tierStripe = new Panel
            {
                Width      = 4,
                Height     = 20,
                Background = new SolidBrush(UiTheme.TierColor(ev.TierInvolvement))
            };

            // Richer text: [Year Season] Domain — TypeName [@SettlementName or @(x,y)]
            string evText = $"[{ev.Year} {SeasonAbbrev(ev.Season)}] {ev.Domain} — {ev.TypeName}";
            if (ev.SettlementName is not null)
                evText += $" @ {ev.SettlementName}";
            else if (ev.Location.HasValue)
                evText += $" @({ev.Location.Value.X},{ev.Location.Value.Y})";

            var evLabel = new Label { Text = evText, TextColor = textColor };

            // Clickable actor name button (if actor is a named character entity)
            Widget? actorWidget = null;
            if (ev.ActorId > 0 && ev.ActorName is not null && IsCharacterEvent(ev.Type))
            {
                long capturedActorId = ev.ActorId;
                var actorBtn = new TextButton
                {
                    Text  = ev.ActorName,
                    Width = 90
                };
                actorBtn.Click += (_, _) => _pendingCharacterProfileId = capturedActorId;
                actorWidget = actorBtn;
            }

            // Civ link button
            Widget? civWidget = null;
            if (ev.CivId > 0 && civNames.TryGetValue(ev.CivId, out string? civName))
            {
                long capturedCivId = ev.CivId;
                var civBtn = new TextButton { Text = $"[{civName}]", Height = 20 };
                civBtn.Click += (_, _) => _pendingCivId = capturedCivId;
                civWidget = civBtn;
            }

            // Cause chain button
            long capturedEvId = ev.Id.Value;
            var causeBtn = new TextButton { Text = "->", Width = 24, Height = 20 };
            causeBtn.Click += (_, _) => _pendingCauseChainEventId = capturedEvId;

            // First-of-kind badge
            Widget? badgeWidget = ev.IsFirstOfKind
                ? new Label { Text = "★", TextColor = Color.Gold }
                : null;

            var row = new HorizontalStackPanel { Spacing = 3 };
            row.Widgets.Add(tierStripe);
            row.Widgets.Add(evLabel);
            if (actorWidget is not null) row.Widgets.Add(actorWidget);
            if (civWidget   is not null) row.Widgets.Add(civWidget);
            row.Widgets.Add(causeBtn);
            if (badgeWidget is not null) row.Widgets.Add(badgeWidget);
            _rows.Widgets.Add(row);
            rowCount++;
        }

        // 6.4.3 — empty state when filter yields no results
        if (rowCount == 0)
        {
            string msg = filter.IsDefault
                ? "(no events yet)"
                : "(no events match filter)";
            _rows.Widgets.Add(new Label { Text = msg, TextColor = UiTheme.MutedText });
        }
    }

    /// <summary>
    /// Returns the event ID for which a causal chain was requested, then clears it.
    /// Call from Game1.Update each frame.
    /// </summary>
    public long? ConsumePendingCauseChain()
    {
        var val = _pendingCauseChainEventId;
        _pendingCauseChainEventId = null;
        return val;
    }

    /// <summary>Returns the civ ID clicked for navigation, then clears it.</summary>
    public long? ConsumePendingCiv()
    {
        var val = _pendingCivId;
        _pendingCivId = null;
        return val;
    }

    /// <summary>
    /// Returns the character ID for which a profile was requested, then clears it.
    /// Call from Game1.Update each frame.
    /// </summary>
    public long? ConsumePendingCharacterProfile()
    {
        var val = _pendingCharacterProfileId;
        _pendingCharacterProfileId = null;
        return val;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

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
        EventType.PhysicianHealed          or
        EventType.BeastSlain               => true,
        _                                  => false
    };
}
