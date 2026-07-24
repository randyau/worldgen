using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Present;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned "Watch" panel: live needs/goals/spotlight HUD for WatchedCharacter (M8.3.2).
/// <summary>
/// Live panel tracking a single named (watched) character. When spotlighted (M7 Phase 7.4)
/// exposes intent controls: enter/exit spotlight, move-to, goal nudges.
/// </summary>
public sealed class CharacterWatchPanel : IToggleablePanel
{
    private readonly WeVStack _content = new(UiTheme.Space.Xs);
    private PanelContext _ctx;
    private EntityId? _spotlightCharacterId;
    private TileCoord? _inspectedTile;

    public string Id => "watch";
    public string Title => "Watch";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public Action<EntityId>? OnEnterSpotlight;
    public Action?           OnExitSpotlight;
    public Action?           OnMoveIntent;
    public Action?           OnWanderGoal;
    public Action?           OnSettleGoal;
    public Action<long>?     OnProfile;

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public EmptyStateSpec? EmptyFor(PanelContext ctx) =>
        ctx.Snapshot.WatchedCharacter is null ? new EmptyStateSpec(EmptyStateKind.PreSim, "No character watched.") : null;

    public void Show() { IsVisible = true; }
    public void Hide() { IsVisible = false; }

    /// <summary>Updates the spotlight/tile context used to gate the intent buttons. Called from Game1 each frame.</summary>
    public void SetContext(EntityId? spotlightCharacterId, TileCoord? inspectedTile)
    {
        _spotlightCharacterId = spotlightCharacterId;
        _inspectedTile        = inspectedTile;
    }

    public void Refresh()
    {
        _content.Clear();
        var watch = _ctx.Snapshot.WatchedCharacter;
        if (watch is null) { _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "No character watched.")); return; }

        var present = _ctx.Present;
        bool isSpotlighted = _spotlightCharacterId.HasValue && _spotlightCharacterId.Value == watch.Id;

        // ── Header ──────────────────────────────────────────────────────────
        string epithet = watch.Epithet.Length > 0 ? $" the {watch.Epithet}" : "";
        _content.Add(SectionHeader.Build($"{watch.Name}{epithet}"));
        _content.Add(new WeText($"Civ: {watch.CivName}  |  Age: {watch.AgeSeasons}s  ({watch.AgeSeasons / 4} yrs)", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"Location: ({watch.Location.X}, {watch.Location.Y}) — {watch.BiomeName}", color: UiTheme.ColorRole.TextSecondary));

        // ── Wellbeing ────────────────────────────────────────────────────────
        var wbColor = watch.Wellbeing >= 0.3f ? UiTheme.ColorRole.StatePositive
                    : watch.Wellbeing >= -0.3f ? UiTheme.ColorRole.TextSecondary
                    : UiTheme.ColorRole.StateNegative;
        _content.Add(new WeText($"Wellbeing: {present.Wellbeing(watch.Wellbeing)} ({watch.Wellbeing:+0.00;-0.00;0.00})", color: wbColor));

        // ── Needs (live) ─────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Needs"));
        var n = watch.Needs;
        _content.Add(Meter.Build("Food",      n.Food));
        _content.Add(Meter.Build("Safety",    n.Safety));
        _content.Add(Meter.Build("Shelter",   n.Shelter));
        _content.Add(Meter.Build("Belonging", n.Belonging));
        _content.Add(Meter.Build("Status",    n.Status));
        _content.Add(Meter.Build("Purpose",   n.Purpose));
        _content.Add(Meter.Build("Spiritual", n.Spiritual));

        // ── Active Goals ─────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Active Goals"));
        if (watch.Goals.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(none)"));
        else
            foreach (var g in watch.Goals)
                _content.Add(new WeText($"  {g.Description,-20} (priority {g.Priority:F2})", color: UiTheme.ColorRole.TextSecondary));

        // ── Personality ──────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Personality"));
        var pers = watch.Personality;
        _content.Add(new WeText($"  Ambition   {PersTick(pers.Ambition)}  Compassion {PersTick(pers.Compassion)}", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"  Curiosity  {PersTick(pers.Curiosity)}  Creativity {PersTick(pers.Creativity)}", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"  Loyalty    {PersTick(pers.Loyalty)}  Aggression {PersTick(pers.Aggression)}", color: UiTheme.ColorRole.TextSecondary));

        // ── Spotlight controls (M7 Phase 7.4) ───────────────────────────────
        if (isSpotlighted)
        {
            var exitBtn = new TextButton { Text = "[Exit Spotlight]", Padding = new Myra.Graphics2D.Thickness(4) };
            exitBtn.Click += (_, _) => OnExitSpotlight?.Invoke();
            _content.Add(exitBtn);

            _content.Add(new WeText("SPOTLIGHT ACTIVE", color: UiTheme.ColorRole.AccentSpotlight));

            var moveBtn = new TextButton
            {
                Text    = "[Move to inspected tile]",
                Padding = new Myra.Graphics2D.Thickness(4),
                Enabled = _inspectedTile.HasValue
            };
            moveBtn.Click += (_, _) => OnMoveIntent?.Invoke();
            _content.Add(moveBtn);

            var goalRow = new WeHStack(UiTheme.Space.Xs);
            var wanderBtn = new TextButton { Text = "[Goal: Wander]", Padding = new Myra.Graphics2D.Thickness(4) };
            wanderBtn.Click += (_, _) => OnWanderGoal?.Invoke();
            var settleBtn = new TextButton { Text = "[Goal: Settle]", Padding = new Myra.Graphics2D.Thickness(4) };
            settleBtn.Click += (_, _) => OnSettleGoal?.Invoke();
            goalRow.Add(wanderBtn);
            goalRow.Add(settleBtn);
            _content.Add(goalRow);
        }
        else
        {
            _content.Add(new WeText("Spotlight biases this character's decisions without", color: UiTheme.ColorRole.TextMuted));
            _content.Add(new WeText("overriding survival autonomy. Click tile → move intent.", color: UiTheme.ColorRole.TextMuted));
            EntityId capturedWatchId = watch.Id;
            var enterBtn = new TextButton { Text = "[Enter Spotlight]", Padding = new Myra.Graphics2D.Thickness(4) };
            enterBtn.Click += (_, _) => OnEnterSpotlight?.Invoke(capturedWatchId);
            _content.Add(enterBtn);
        }

        // ── Full Profile ─────────────────────────────────────────────────────
        long capturedId = watch.Id.Value;
        var profileBtn = new TextButton { Text = "[Full Profile]", Padding = new Myra.Graphics2D.Thickness(4) };
        profileBtn.Click += (_, _) => OnProfile?.Invoke(capturedId);
        _content.Add(profileBtn);
    }

    private static string PersTick(float v)
    {
        int n = (int)(v * 5);
        return $"[{new string('#', n)}{new string('.', 5 - n)}]";
    }
}
