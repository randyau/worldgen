using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Beast panel: the same select -> dialog -> watch pattern as
// CharacterProfilePanel, sized to what a beast actually has (no history query, no needs/goals —
// EntitySnapshot already carries everything this panel shows).
/// <summary>
/// Structured beast profile card populated from the live <see cref="EntitySnapshot"/> — beasts
/// have no derived history-query summary the way characters do, so unlike
/// <see cref="CharacterProfilePanel"/> this reads sim-snapshot data directly instead of an
/// <c>IHistoryQuery</c>.
/// </summary>
public sealed class BeastProfilePanel : IToggleablePanel
{
    private readonly WeVStack _content = new(UiTheme.Space.Xs);

    private long _beastId;
    private bool _hasBeast;
    private PanelContext _ctx;

    public string Id => "beast";
    public string Title => "Beast";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    /// <summary>Fired when the player clicks [Watch] on the currently-shown beast.</summary>
    public Action<long>? OnWatch;

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;

    /// <summary>Selects which beast's card to show. Called by the selection bus.</summary>
    public void ShowBeast(long beastId)
    {
        _beastId  = beastId;
        _hasBeast = true;
        Show();
        // Content is sim-snapshot-driven, but populate immediately anyway (same pattern as
        // CharacterProfilePanel.ShowCharacter) — the bound context is already fresh as of this
        // frame's Bind(), so there's no reason to wait for the next tick-gated RefreshVisible().
        Refresh();
    }

    public void Refresh()
    {
        _content.Clear();
        if (!_hasBeast) { _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "No beast selected.")); return; }

        if (!_ctx.Snapshot.EntitySnapshots.TryGetValue(new EntityId(_beastId), out var beast))
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "Selected beast has no data available."));
            return;
        }

        long capturedId = _beastId;
        _content.Add(new WeButton("[Watch]", () => OnWatch?.Invoke(capturedId)) { Padding = new Myra.Graphics2D.Thickness(4) });

        string tag = beast.IsLegendary ? " [Legendary]" : "";
        _content.Add(SectionHeader.Build($"{beast.Name}{tag}"));
        _content.Add(new WeText($"Species: {beast.SpeciesId}", color: UiTheme.ColorRole.TextSecondary));
        _content.Add(new WeText($"Location: ({beast.Location.X}, {beast.Location.Y})", color: UiTheme.ColorRole.TextSecondary));

        var grid = new KeyValueGrid();
        grid.Add("Health", $"{beast.HealthFraction:P0}");
        if (beast.FoodFraction >= 0f) grid.Add("Food", $"{beast.FoodFraction:P0}");
        grid.Add("Age", $"{beast.AgeSeason}s ({beast.AgeSeason / 16} yrs)");
        grid.Add("Status", beast.IsAlive ? "Alive" : "Dead");
        _content.Add(grid);
    }
}
