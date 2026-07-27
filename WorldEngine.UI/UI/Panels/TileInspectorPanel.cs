using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Present;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Contextual (Tile) panel: ruin/settlement/tile facts/seasonal/resources/
// disasters/territory/characters/artifacts/history, migrated onto the kit + Presenter (M8.3.1).
/// <summary>Contextual panel showing full detail for the currently inspected tile.</summary>
public sealed class TileInspectorPanel : IWorkspacePanel
{
    public string Id => "tile";
    public string Title => "Tile Inspector";
    public PanelPlacement Placement => new(PanelPlacementKind.Contextual, SelectionKind.Tile);

    /// <summary>
    /// Invoked when the user clicks [Watch] next to a character or beast name. Not routed through
    /// the selection bus — Game1 wires this to enqueue WatchEntity and reveal the (Summoned)
    /// Watch panel, per the M8.2.2 DECISION preserving pre-M8 Watch placement.
    /// </summary>
    public Action<long>? OnWatch;

    private readonly WeVStack _content = new(UiTheme.Space.Xs);
    private PanelContext _ctx;

    public Widget Build() => PanelFrame.Build(Title, _content.Root);

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public void Refresh()
    {
        _content.Clear();
        var data = _ctx.Snapshot.InspectedTile;
        if (data is null)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "No tile selected.", "Click a tile on the map to inspect it."));
            return;
        }

        var snapshot = _ctx.Snapshot;
        var present  = _ctx.Present;
        var tile     = data.RawTile;

        // Ruin — shown before or instead of settlement info
        if (snapshot.Ruins.TryGetValue(data.Coord, out var ruin) && !snapshot.Settlements.ContainsKey(data.Coord))
        {
            string ruinLabel = ruin.TimesSettled > 1
                ? $"RUINS OF {ruin.SettlementName.ToUpper()} (destroyed {ruin.TimesSettled}x)"
                : $"RUINS OF {ruin.SettlementName.ToUpper()}";
            _content.Add(SectionHeader.Build(ruinLabel));
            _content.Add(new WeText($"Last destroyed: Year {ruin.DestroyedYear} ({ruin.Cause})", color: UiTheme.ColorRole.TextSecondary));
        }

        // Settlement info first — most interesting to the user
        if (snapshot.Settlements.TryGetValue(data.Coord, out var settlement))
        {
            string ruinSuffix = snapshot.Ruins.TryGetValue(data.Coord, out var existingRuin)
                ? $" (on ruins; destroyed {existingRuin.TimesSettled}x)" : "";
            _content.Add(SectionHeader.Build($"{settlement.Name}{ruinSuffix}"));

            var healthColor = settlement.Health >= 70 ? UiTheme.ColorRole.StatePositive
                             : settlement.Health >= 40 ? UiTheme.ColorRole.StateWarning
                             : UiTheme.ColorRole.StateNegative;
            var grid = new KeyValueGrid();
            grid.Add("Civ", settlement.CivName);
            grid.Add("Pop", settlement.Population.ToString("N0"));
            grid.Add("Health", $"{settlement.Health}/100 ({present.Health(settlement.Health)})", healthColor);
            grid.Add("Founded", $"Year {settlement.FoundedYear}");
            if (settlement.ConqueredYear > 0)
                grid.Add("Conquered", $"Year {settlement.ConqueredYear} (from civ {settlement.ConqueredFromCivId})");
            _content.Add(grid);

            if (settlement.ResourceLedger is { Count: > 0 } ledger)
            {
                _content.Add(SectionHeader.Build("Resources (this tick)"));
                foreach (var (res, val) in ledger.OrderByDescending(kv => kv.Value))
                    _content.Add(StatRow.Build(res, $"{(val >= 0 ? "+" : "")}{val:F2}"));
            }
            if (settlement.ResourceStores is { Count: > 0 } stores)
            {
                _content.Add(SectionHeader.Build("Stores"));
                foreach (var (res, amount) in stores.OrderByDescending(kv => kv.Value))
                    _content.Add(StatRow.Build(res, $"{amount:F1}", unit: $"({present.Store(res, amount)})"));
            }
        }

        _content.Add(SectionHeader.Build($"Tile ({data.Coord.X}, {data.Coord.Y})"));
        var tileGrid = new KeyValueGrid();
        tileGrid.Add("Biome", ((BiomeType)tile.BiomeType).ToString());
        tileGrid.Add("Elevation", $"{tile.Elevation} ({present.Elevation(tile.Elevation)})");
        tileGrid.Add("Base Temp", $"{present.TempC(tile.BaseTemperature):F1}°C ({present.TempF(tile.BaseTemperature):F0}°F)");
        tileGrid.Add("Moisture", $"{tile.CurrentMoisture} ({present.Moisture(tile.CurrentMoisture)})");
        tileGrid.Add("Eff. Temp", $"{present.TempC(data.EffectiveTemperature):F1}°C ({present.TempF(data.EffectiveTemperature):F0}°F)");
        tileGrid.Add("Magic", $"{tile.MagicIntensity} ({present.MagicIntensity(tile.MagicIntensity)})");
        tileGrid.Add("Fertility", $"{tile.Fertility} ({present.Fertility(tile.Fertility)})");
        _content.Add(tileGrid);

        // DECISION: a dedicated SeasonalStrip composite (framework §4.2) is deferred; StatRows
        // convey the same four deltas without adding a new Layer-2 widget in this pass.
        _content.Add(SectionHeader.Build("Seasonal Profile"));
        var prof = data.SeasonalProfile;
        _content.Add(StatRow.Build("Spring", $"Temp {present.TempDeltaC(prof.TempDeltaSpring):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaSpring:+#;-#;0}"));
        _content.Add(StatRow.Build("Summer", $"Temp {present.TempDeltaC(prof.TempDeltaSummer):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaSummer:+#;-#;0}"));
        _content.Add(StatRow.Build("Autumn", $"Temp {present.TempDeltaC(prof.TempDeltaAutumn):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaAutumn:+#;-#;0}"));
        _content.Add(StatRow.Build("Winter", $"Temp {present.TempDeltaC(prof.TempDeltaWinter):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaWinter:+#;-#;0}"));

        _content.Add(SectionHeader.Build("Resources"));
        if (data.Deposits.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(none)"));
        else foreach (var d in data.Deposits)
            _content.Add(new WeText($"{d.DepositType} (Q:{d.Quality} D:{d.Depth})"));

        _content.Add(SectionHeader.Build("Disasters"));
        var disasters = data.Disasters.ToList(); // snapshot; sim thread may mutate the source
        if (disasters.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(none)"));
        else foreach (var d in disasters)
            _content.Add(new WeText($"{present.DisasterName(d.Type)} {d.Intensity:F2} [{(d.TicksRemaining < 0 ? "∞" : d.TicksRemaining.ToString())} ticks]", color: UiTheme.ColorRole.StateWarning));
        _content.Add(StatRow.Build("In drought", data.IsInActiveDrought.ToString()));

        // ── Territory ──────────────────────────────────────────────────────
        if (data.TerritoryOwnerName is not null)
        {
            _content.Add(SectionHeader.Build("Territory"));
            string cityPart = data.TerritoryCityName is not null ? $" (city: {data.TerritoryCityName})" : "";
            _content.Add(new WeText($"{data.TerritoryOwnerName}{cityPart}"));

            if (data.Improvement.HasValue)
            {
                string builtYear = data.ImprovementBuiltYear > 0 ? $", built Year {data.ImprovementBuiltYear}" : "";
                string builder = data.ImprovementBuilderName is not null ? $" by {data.ImprovementBuilderName}" : "";
                _content.Add(new WeText($"Improvement: {data.Improvement}{builtYear}{builder}"));
            }
        }

        AddBeastSection(data.Coord, snapshot.EntitySnapshots);
        AddCharacterSection(data.Coord, snapshot.EntitySnapshots, snapshot, present);

        if (snapshot.Settlements.ContainsKey(data.Coord))
            AddSettlementArtifacts(data.Coord, snapshot);

        if (data.TileHistory is { Count: > 0 } history)
        {
            _content.Add(SectionHeader.Build("History at this tile"));
            foreach (var (year, desc) in history)
                _content.Add(new WeText($"Year {year} — {desc}", color: UiTheme.ColorRole.TextSecondary));
        }
    }

    private void AddBeastSection(TileCoord coord, IReadOnlyDictionary<EntityId, EntitySnapshot> entitySnapshots)
    {
        var beasts = entitySnapshots.Values
            .Where(e => e.Kind == EntityKind.LegendaryBeast && e.IsAlive && e.Location == coord)
            .ToList();
        if (beasts.Count == 0) return;

        _content.Add(SectionHeader.Build("Creatures"));
        foreach (var b in beasts)
        {
            string tag = b.IsLegendary ? " [Legendary]" : "";
            long capturedId = b.Id.Value;

            var row = new WeHStack(UiTheme.Space.Xs);
            row.Add(EntityLink.Build(new EntityRef(SelectionKind.Beast, capturedId, default), $"{b.Name}{tag}", _ctx.Selection));
            var watchBtn = new WeButton("[Watch]", () => OnWatch?.Invoke(capturedId))
                { Padding = new Myra.Graphics2D.Thickness(2) };
            row.Add(watchBtn);
            _content.Add(row);

            _content.Add(StatRow.Build("  Status", $"HP {b.HealthFraction:P0}  Food {b.FoodFraction:P0}  Age {b.AgeSeason}"));
        }
    }

    private void AddCharacterSection(
        TileCoord coord, IReadOnlyDictionary<EntityId, EntitySnapshot> entitySnapshots,
        WorldSnapshot snapshot, Presenter present)
    {
        var tier1 = entitySnapshots.Values
            .Where(e => e.Kind == EntityKind.Tier1Character && e.IsAlive && e.Location == coord).ToList();
        var tier2 = entitySnapshots.Values
            .Where(e => e.Kind == EntityKind.Tier2Character && e.IsAlive && e.Location == coord).ToList();
        if (tier1.Count == 0 && tier2.Count == 0) return;

        _content.Add(SectionHeader.Build("Characters"));
        foreach (var c in tier1)
        {
            string civTag = c.CivName is not null ? $" [{c.CivName}]" : "";
            string ancTag = c.AncestryId.Length > 0 ? $" ({c.AncestryId})" : "";

            var row = new WeHStack(UiTheme.Space.Xs);
            long capturedId = c.Id.Value;
            row.Add(EntityLink.Build(new EntityRef(SelectionKind.Character, capturedId, default), $"{c.Name}{civTag}{ancTag}", _ctx.Selection));
            var watchBtn = new WeButton("[Watch]", () => OnWatch?.Invoke(capturedId))
                { Padding = new Myra.Graphics2D.Thickness(2) };
            row.Add(watchBtn);
            _content.Add(row);

            _content.Add(StatRow.Build("  Status", $"HP {c.HealthFraction:P0}  Age {c.AgeSeason}s  [{present.Wellbeing(c.Wellbeing)}]"));

            AddCharacterArtifacts(c.Id.Value, snapshot);
        }
        foreach (var c in tier2)
        {
            var row = new WeHStack(UiTheme.Space.Xs);
            long capturedId = c.Id.Value;
            row.Add(EntityLink.Build(new EntityRef(SelectionKind.Character, capturedId, default), $"{c.Name} [Tier2]", _ctx.Selection));
            var watchBtn = new WeButton("[Watch]", () => OnWatch?.Invoke(capturedId))
                { Padding = new Myra.Graphics2D.Thickness(2) };
            row.Add(watchBtn);
            _content.Add(row);

            _content.Add(StatRow.Build("  Status", $"HP {c.HealthFraction:P0}  Age {c.AgeSeason}s"));
        }
    }

    private void AddCharacterArtifacts(long characterId, WorldSnapshot snapshot)
    {
        if (snapshot.Artifacts is not { Count: > 0 } all) return;
        var carried = all.Where(a => !a.IsDestroyed && a.OwnerCharacterId == characterId).ToList();
        if (carried.Count == 0) return;

        _content.Add(new WeText("  Artifacts:", color: UiTheme.ColorRole.TextSecondary));
        foreach (var a in carried)
            _content.Add(new WeText($"    {a.Name} ({a.Category}) — Q:{a.Quality:F2} — {a.Origin}  [by {a.CreatorName}]"));
    }

    private void AddSettlementArtifacts(TileCoord coord, WorldSnapshot snapshot)
    {
        if (snapshot.Artifacts is not { Count: > 0 } all) return;
        var housed = all.Where(a => !a.IsDestroyed && a.OwnerSettlementTile == coord).ToList();
        if (housed.Count == 0) return;

        _content.Add(SectionHeader.Build("Artifacts at this settlement"));
        foreach (var a in housed)
            _content.Add(new WeText($"{a.Name} ({a.Category}) — Q:{a.Quality:F2} — {a.Origin}  [by {a.CreatorName}]"));
    }
}
