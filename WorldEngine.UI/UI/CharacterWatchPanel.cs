using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// Live panel tracking a single named character. When the character is spotlighted
/// (M7 Phase 7.4) exposes intent controls: enter/exit spotlight, move-to, goal nudges.
/// </summary>
// MAP: Character watch + spotlight HUD (M3 Phase 3.4 + M7 Phase 7.4).
public sealed class CharacterWatchPanel : IPanel
{
    private readonly VerticalStackPanel _content;

    public Widget Root { get; }
    public bool IsVisible { get; private set; }

    // Consume-once flags for profile navigation
    private long _pendingProfileCharacterId;
    public long ConsumePendingProfile()
    {
        var id = _pendingProfileCharacterId;
        _pendingProfileCharacterId = 0;
        return id;
    }

    // Consume-once spotlight intent flags (M7 Phase 7.4)
    private EntityId? _pendingEnterSpotlight;
    private bool      _pendingExitSpotlight;
    private bool      _pendingMoveIntent;
    private bool      _pendingWanderGoal;
    private bool      _pendingSettleGoal;

    public EntityId? ConsumePendingEnterSpotlight() { var v = _pendingEnterSpotlight; _pendingEnterSpotlight = null; return v; }
    public bool ConsumePendingExitSpotlight()   { var v = _pendingExitSpotlight;  _pendingExitSpotlight = false;  return v; }
    public bool ConsumePendingMoveIntent()      { var v = _pendingMoveIntent;      _pendingMoveIntent = false;     return v; }
    public bool ConsumePendingWanderGoal()      { var v = _pendingWanderGoal;      _pendingWanderGoal = false;     return v; }
    public bool ConsumePendingSettleGoal()      { var v = _pendingSettleGoal;      _pendingSettleGoal = false;     return v; }

    public CharacterWatchPanel()
    {
        _content = new VerticalStackPanel { Spacing = 2 };

        var scroll = new ScrollViewer { Content = _content, Width = UiTheme.ScrollWidth, Height = 420 };
        Root = PanelChrome.Wrap("CHARACTER WATCH", scroll, Hide);
        Root.Visible = false;
    }

    public void Show()   { Root.Visible = true;  IsVisible = true; }
    public void Hide()   { Root.Visible = false; IsVisible = false; }
    public void Toggle() { if (IsVisible) Hide(); else Show(); }

    /// <summary>
    /// Refreshes displayed data from the snapshot. Called each frame when IsVisible.
    /// </summary>
    public void Refresh(WorldSnapshot snapshot, EntityId? spotlightId = null, TileCoord? inspectedTile = null)
    {
        if (!IsVisible) return;
        var watch = snapshot.WatchedCharacter;
        if (watch is null) { _content.Widgets.Clear(); return; }

        bool isSpotlighted = spotlightId.HasValue && spotlightId.Value == watch.Id;

        _content.Widgets.Clear();

        // ── Header ──────────────────────────────────────────────────────────
        string epithet = watch.Epithet.Length > 0 ? $" the {watch.Epithet}" : "";
        AddLine($"{watch.Name}{epithet}", UiTheme.HeaderText);
        AddLine($"Civ: {watch.CivName}  |  Age: {watch.AgeSeasons}s  ({watch.AgeSeasons / 4} yrs)", Color.LightGray);
        AddLine($"Location: ({watch.Location.X}, {watch.Location.Y}) — {watch.BiomeName}", Color.LightGray);

        AddSeparator();

        // ── Wellbeing ────────────────────────────────────────────────────────
        string wbLabel = watch.Wellbeing switch
        {
            >= 0.7f  => "Flourishing",
            >= 0.3f  => "Content",
            >= -0.3f => "Neutral",
            >= -0.7f => "Distressed",
            _        => "Spiraling"
        };
        var wbColor = watch.Wellbeing >= 0.3f ? Color.LightGreen
                    : watch.Wellbeing >= -0.3f ? Color.LightGray
                    : Color.OrangeRed;
        AddLine($"Wellbeing: {wbLabel} ({watch.Wellbeing:+0.00;-0.00;0.00})", wbColor);

        AddSeparator();

        // ── Needs (live) ─────────────────────────────────────────────────────
        AddLine("NEEDS", Color.White);
        var n = watch.Needs;
        AddNeedBar("Food",      n.Food);
        AddNeedBar("Safety",    n.Safety);
        AddNeedBar("Shelter",   n.Shelter);
        AddNeedBar("Belonging", n.Belonging);
        AddNeedBar("Status",    n.Status);
        AddNeedBar("Purpose",   n.Purpose);
        AddNeedBar("Spiritual", n.Spiritual);

        AddSeparator();

        // ── Active Goals ─────────────────────────────────────────────────────
        AddLine("ACTIVE GOALS", Color.White);
        if (watch.Goals.Count == 0)
        {
            AddLine("  (none)", Color.LightGray);
        }
        else
        {
            foreach (var g in watch.Goals)
                AddLine($"  {g.Description,-20} (priority {g.Priority:F2})", Color.LightGray);
        }

        AddSeparator();

        // ── Personality ──────────────────────────────────────────────────────
        AddLine("PERSONALITY", Color.White);
        var pers = watch.Personality;
        AddLine($"  Ambition   {PersTick(pers.Ambition)}  Compassion {PersTick(pers.Compassion)}", Color.LightGray);
        AddLine($"  Curiosity  {PersTick(pers.Curiosity)}  Creativity {PersTick(pers.Creativity)}", Color.LightGray);
        AddLine($"  Loyalty    {PersTick(pers.Loyalty)}  Aggression {PersTick(pers.Aggression)}", Color.LightGray);

        AddSeparator();

        // ── Spotlight controls (M7 Phase 7.4) ───────────────────────────────
        if (isSpotlighted)
        {
            EntityId capturedSpotId = watch.Id;

            var exitBtn = new TextButton { Text = "[Exit Spotlight]", Padding = new Myra.Graphics2D.Thickness(4) };
            exitBtn.Click += (_, _) => _pendingExitSpotlight = true;
            _content.Widgets.Add(exitBtn);

            AddLine("SPOTLIGHT ACTIVE", Color.Cyan);

            var moveBtn = new TextButton
            {
                Text    = "[Move to inspected tile]",
                Padding = new Myra.Graphics2D.Thickness(4),
                Enabled = inspectedTile.HasValue
            };
            moveBtn.Click += (_, _) => _pendingMoveIntent = true;
            _content.Widgets.Add(moveBtn);

            var goalRow = new HorizontalStackPanel { Spacing = 4 };
            var wanderBtn = new TextButton { Text = "[Goal: Wander]", Padding = new Myra.Graphics2D.Thickness(4) };
            wanderBtn.Click += (_, _) => _pendingWanderGoal = true;
            var settleBtn = new TextButton { Text = "[Goal: Settle]", Padding = new Myra.Graphics2D.Thickness(4) };
            settleBtn.Click += (_, _) => _pendingSettleGoal = true;
            goalRow.Widgets.Add(wanderBtn);
            goalRow.Widgets.Add(settleBtn);
            _content.Widgets.Add(goalRow);
        }
        else
        {
            EntityId capturedWatchId = watch.Id;
            var enterBtn = new TextButton { Text = "[Enter Spotlight]", Padding = new Myra.Graphics2D.Thickness(4) };
            enterBtn.Click += (_, _) => _pendingEnterSpotlight = capturedWatchId;
            _content.Widgets.Add(enterBtn);
        }

        // ── Full Profile + Close ─────────────────────────────────────────────
        long capturedId = watch.Id.Value;
        var profileBtn = new TextButton { Text = "[Full Profile]", Padding = new Myra.Graphics2D.Thickness(4) };
        profileBtn.Click += (_, _) => _pendingProfileCharacterId = capturedId;
        _content.Widgets.Add(profileBtn);

        var closeBtn = new TextButton { Text = "[Close]", Padding = new Myra.Graphics2D.Thickness(4) };
        closeBtn.Click += (_, _) => Hide();
        _content.Widgets.Add(closeBtn);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NeedBar(float v)
    {
        int filled = (int)(v * 10);
        return $"[{new string('#', filled)}{new string('.', 10 - filled)}] {v:F2}";
    }

    private static string PersTick(float v)
    {
        int n = (int)(v * 5);
        return $"[{new string('#', n)}{new string('.', 5 - n)}]";
    }

    private void AddNeedBar(string label, float value)
    {
        string lowTag = value < 0.25f ? " !" : "";
        var color = value < 0.25f ? Color.OrangeRed : Color.LightGray;
        AddLine($"  {label,-10} {NeedBar(value)}{lowTag}", color);
    }

    private void AddLine(string text, Color? color = null)
    {
        var lbl = new Label { Text = text };
        if (color.HasValue) lbl.TextColor = color.Value;
        _content.Widgets.Add(lbl);
    }

    private void AddSeparator() =>
        _content.Widgets.Add(new Label { Text = new string('-', 36) });
}
