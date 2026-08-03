using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Present;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned "Watch" panel: live vitals HUD for whatever is watched (M8.3.2).
/// <summary>
/// Live panel tracking a single watched entity. Tier1Character gets the rich needs/goals/
/// spotlight HUD (<see cref="WorldSnapshot.WatchedCharacter"/>); any other watchable kind
/// (Tier2Character, LegendaryBeast, ...) gets the thinner vitals-only card
/// (<see cref="WorldSnapshot.WatchedBasic"/>) — the same single watch slot, rendered differently
/// depending on what's in it. When spotlighted (M7 Phase 7.4) exposes intent controls: enter/exit
/// spotlight, move-to, goal nudges — spotlight only ever applies to a Tier1Character.
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
    public Action<long>?     OnBeastProfile;

    /// <summary>Watches whatever is currently selected — kind-agnostic; the sim resolves the
    /// entity's actual kind when the WatchEntity command is handled.</summary>
    public Action<long>?     OnWatchSelected;

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) => _ctx = ctx;

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
        var basic = _ctx.Snapshot.WatchedBasic;

        // Whichever character/beast is currently selected via SelectionBus (e.g. clicked in Tile
        // Inspector or Event Log) can be watched directly from here — the only prior entry point
        // was the Tile Inspector's own [Watch] button, which left this panel with no way to pick
        // or change its own target.
        var selected = _ctx.Selection.Current;
        long? watchedId = watch?.Id.Value ?? basic?.Id.Value;
        bool hasDifferentWatchableSelected = selected.Kind is SelectionKind.Character or SelectionKind.Beast
            && (watchedId is null || selected.Id != watchedId.Value);

        if (basic is not null)
        {
            RefreshBasic(basic, hasDifferentWatchableSelected, selected.Id);
            return;
        }

        if (watch is not { } w)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "Nothing watched."));
            if (hasDifferentWatchableSelected)
            {
                long selId = selected.Id;
                var watchSelBtn = new WeButton("[Watch Selected]", () => OnWatchSelected?.Invoke(selId))
                    { Padding = new Myra.Graphics2D.Thickness(4) };
                _content.Add(watchSelBtn);
            }
            return;
        }

        var present = _ctx.Present;
        bool isSpotlighted = _spotlightCharacterId.HasValue && _spotlightCharacterId.Value == w.Id;

        // ── Header ──────────────────────────────────────────────────────────
        string epithet = w.Epithet.Length > 0 ? $" the {w.Epithet}" : "";
        _content.Add(SectionHeader.Build($"{w.Name}{epithet}"));
        _content.Add(new WeText($"Civ: {w.CivName}  |  Age: {w.AgeSeasons}s  ({w.AgeSeasons / 16} yrs)", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"Location: ({w.Location.X}, {w.Location.Y}) — {w.BiomeName}", color: UiTheme.ColorRole.TextSecondary));

        // ── Wellbeing ────────────────────────────────────────────────────────
        var wbColor = w.Wellbeing >= 0.3f ? UiTheme.ColorRole.StatePositive
                    : w.Wellbeing >= -0.3f ? UiTheme.ColorRole.TextSecondary
                    : UiTheme.ColorRole.StateNegative;
        _content.Add(new WeText($"Wellbeing: {present.Wellbeing(w.Wellbeing)} ({w.Wellbeing:+0.00;-0.00;0.00})", color: wbColor));

        // ── Needs (live) ─────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Needs"));
        var n = w.Needs;
        _content.Add(Meter.Build("Food",      n.Food));
        _content.Add(Meter.Build("Safety",    n.Safety));
        _content.Add(Meter.Build("Shelter",   n.Shelter));
        _content.Add(Meter.Build("Belonging", n.Belonging));
        _content.Add(Meter.Build("Status",    n.Status));
        _content.Add(Meter.Build("Purpose",   n.Purpose));
        _content.Add(Meter.Build("Spiritual", n.Spiritual));

        // ── Active Goals ─────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Active Goals"));
        if (w.Goals.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(none)"));
        else
            foreach (var g in w.Goals)
                _content.Add(new WeText($"  {g.Description,-20} (priority {g.Priority:F2})", color: UiTheme.ColorRole.TextSecondary));

        // ── Personality ──────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Personality"));
        var pers = w.Personality;
        _content.Add(new WeText($"  Ambition   {PersTick(pers.Ambition)}  Compassion {PersTick(pers.Compassion)}", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"  Curiosity  {PersTick(pers.Curiosity)}  Creativity {PersTick(pers.Creativity)}", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"  Loyalty    {PersTick(pers.Loyalty)}  Aggression {PersTick(pers.Aggression)}", color: UiTheme.ColorRole.TextSecondary));

        // ── Spotlight controls (M7 Phase 7.4) ───────────────────────────────
        if (isSpotlighted)
        {
            var exitBtn = new WeButton("[Exit Spotlight]", () => OnExitSpotlight?.Invoke(), WeButtonVariant.Danger)
                { Padding = new Myra.Graphics2D.Thickness(4) };
            _content.Add(exitBtn);

            _content.Add(new WeText("SPOTLIGHT ACTIVE", color: UiTheme.ColorRole.AccentSpotlight));

            var moveBtn = new WeButton("[Move to inspected tile]", () => OnMoveIntent?.Invoke())
            {
                Padding = new Myra.Graphics2D.Thickness(4),
                Enabled = _inspectedTile.HasValue
            };
            _content.Add(moveBtn);

            var goalRow = new WeHStack(UiTheme.Space.Xs);
            var wanderBtn = new WeButton("[Goal: Wander]", () => OnWanderGoal?.Invoke())
                { Padding = new Myra.Graphics2D.Thickness(4) };
            var settleBtn = new WeButton("[Goal: Settle]", () => OnSettleGoal?.Invoke())
                { Padding = new Myra.Graphics2D.Thickness(4) };
            goalRow.Add(wanderBtn);
            goalRow.Add(settleBtn);
            _content.Add(goalRow);
        }
        else
        {
            _content.Add(new WeText("Spotlight biases this character's decisions without", color: UiTheme.ColorRole.TextMuted));
            _content.Add(new WeText("overriding survival autonomy. Click tile → move intent.", color: UiTheme.ColorRole.TextMuted));
            EntityId capturedWatchId = w.Id;
            var enterBtn = new WeButton("[Enter Spotlight]", () => OnEnterSpotlight?.Invoke(capturedWatchId))
                { Padding = new Myra.Graphics2D.Thickness(4) };
            _content.Add(enterBtn);
        }

        // ── Full Profile ─────────────────────────────────────────────────────
        long capturedId = w.Id.Value;
        var profileBtn = new WeButton("[Full Profile]", () => OnProfile?.Invoke(capturedId))
            { Padding = new Myra.Graphics2D.Thickness(4) };
        _content.Add(profileBtn);

        if (hasDifferentWatchableSelected)
        {
            long selId = selected.Id;
            var watchSelBtn = new WeButton("[Watch Selected Instead]", () => OnWatchSelected?.Invoke(selId))
                { Padding = new Myra.Graphics2D.Thickness(4) };
            _content.Add(watchSelBtn);
        }
    }

    private void RefreshBasic(BasicWatchSnapshot basic, bool hasDifferentWatchableSelected, long selectedId)
    {
        string tag = basic.IsLegendary ? " [Legendary]" : "";
        _content.Add(SectionHeader.Build($"{basic.Name}{tag}"));
        if (basic.SpeciesId.Length > 0)
            _content.Add(new WeText($"Species: {basic.SpeciesId}", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"Age: {basic.AgeSeasons}s ({basic.AgeSeasons / 16} yrs)", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"Location: ({basic.Location.X}, {basic.Location.Y}) — {basic.BiomeName}", color: UiTheme.ColorRole.TextSecondary));

        var grid = new KeyValueGrid();
        grid.Add("Health", $"{basic.HealthFraction:P0}");
        if (basic.FoodFraction >= 0f) grid.Add("Food", $"{basic.FoodFraction:P0}");
        _content.Add(grid);

        long capturedId = basic.Id.Value;
        var profileBtn = new WeButton("[Full Profile]", () => OnBeastProfile?.Invoke(capturedId))
            { Padding = new Myra.Graphics2D.Thickness(4) };
        _content.Add(profileBtn);

        if (hasDifferentWatchableSelected)
        {
            var watchSelBtn = new WeButton("[Watch Selected Instead]", () => OnWatchSelected?.Invoke(selectedId))
                { Padding = new Myra.Graphics2D.Thickness(4) };
            _content.Add(watchSelBtn);
        }
    }

    private static string PersTick(float v)
    {
        int n = (int)(v * 5);
        return $"[{new string('#', n)}{new string('.', 5 - n)}]";
    }
}
