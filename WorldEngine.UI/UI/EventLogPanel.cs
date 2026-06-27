using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

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

    private bool _showHeadline  = true;
    private bool _showRegional  = true;
    private bool _showBackground;

    // Consumed by Game1 each frame — cleared after reading
    private long? _pendingCauseChainEventId;
    private long? _pendingCharacterProfileId;

    public EventLogPanel()
    {
        _rows   = new VerticalStackPanel { Spacing = 2 };
        _scroll = new ScrollViewer { Content = _rows, Width = 340, Height = 250 };

        var headlineBox   = new CheckBox { Text = "Headline",   IsChecked = _showHeadline };
        var regionalBox   = new CheckBox { Text = "Regional",   IsChecked = _showRegional };
        var backgroundBox = new CheckBox { Text = "Background", IsChecked = _showBackground };

        headlineBox.PressedChanged   += (_, _) => _showHeadline   = headlineBox.IsChecked;
        regionalBox.PressedChanged   += (_, _) => _showRegional   = regionalBox.IsChecked;
        backgroundBox.PressedChanged += (_, _) => _showBackground = backgroundBox.IsChecked;

        var filterBar = new HorizontalStackPanel { Spacing = 8 };
        filterBar.Widgets.Add(headlineBox);
        filterBar.Widgets.Add(regionalBox);
        filterBar.Widgets.Add(backgroundBox);

        var stack = new VerticalStackPanel { Spacing = 4 };
        stack.Widgets.Add(filterBar);
        stack.Widgets.Add(_scroll);

        Root = new Panel();
        Root.Widgets.Add(stack);
    }

    // ── Public API ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the event log from the latest snapshot.
    /// When focusLens is active, events not in its FocusedEventIds are dimmed.
    /// </summary>
    public void Update(WorldSnapshot snapshot, FocusLensState? focusLens = null)
    {
        _rows.Widgets.Clear();

        foreach (var ev in snapshot.RecentEvents.Reverse())
        {
            if (!ShouldShow(ev.TierInvolvement)) continue;

            bool isFocused = focusLens is null
                          || focusLens.Type == FocusType.None
                          || focusLens.FocusedEventIds.Contains(ev.Id.Value);

            Color textColor = isFocused ? TierColor(ev.TierInvolvement) : Color.DarkGray;

            // Main event label text
            string evText = $"[{ev.Year}] {ev.TypeName}";
            if (ev.Location.HasValue)
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

            // Cause chain button
            long capturedEvId = ev.Id.Value;
            var causeBtn = new TextButton { Text = "->", Width = 24, Height = 20 };
            causeBtn.Click += (_, _) => _pendingCauseChainEventId = capturedEvId;

            var row = new HorizontalStackPanel { Spacing = 3 };
            row.Widgets.Add(evLabel);
            if (actorWidget is not null) row.Widgets.Add(actorWidget);
            row.Widgets.Add(causeBtn);
            _rows.Widgets.Add(row);
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

    private bool ShouldShow(EventTier tier) => tier switch
    {
        EventTier.Headline   => _showHeadline,
        EventTier.Regional   => _showRegional,
        EventTier.Character  => _showRegional,   // shown alongside regional
        EventTier.Background => _showBackground,
        _                    => false
    };

    private static Color TierColor(EventTier tier) => tier switch
    {
        EventTier.Headline  => Color.Gold,
        EventTier.Regional  => Color.White,
        EventTier.Character => Color.LightGray,
        _                   => Color.DarkGray
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
