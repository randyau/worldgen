using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

public sealed class TileInspectorPanel
{
    public readonly Panel Root;
    private readonly VerticalStackPanel _content;

    // Consume-once: set when user clicks [Watch] next to a character name.
    // Game1 reads this each frame and clears it after consuming.
    private long _pendingWatchCharacterId;
    public long ConsumePendingWatch()
    {
        var id = _pendingWatchCharacterId;
        _pendingWatchCharacterId = 0;
        return id;
    }

    public TileInspectorPanel()
    {
        _content = new VerticalStackPanel { Spacing = 2 };
        Root = new Panel { Width = 340, Visible = false };
        var scroll = new ScrollViewer { Content = _content, Height = 380 };
        Root.Widgets.Add(scroll);
    }

    public void Update(TileInspectorData? data, WorldSnapshot? snapshot = null)
    {
        Root.Visible = data is not null;
        if (data is null) return;

        _content.Widgets.Clear();
        var tile = data.RawTile;

        // Ruin — shown before or instead of settlement info
        if (snapshot?.Ruins.TryGetValue(data.Coord, out var ruin) == true
            && snapshot?.Settlements.ContainsKey(data.Coord) == false)
        {
            string ruinLabel = ruin.TimesSettled > 1
                ? $"RUINS OF {ruin.SettlementName.ToUpper()} (destroyed {ruin.TimesSettled}x)"
                : $"RUINS OF {ruin.SettlementName.ToUpper()}";
            AddLine($"--- {ruinLabel} ---");
            AddLine($"Last destroyed: Year {ruin.DestroyedYear} ({ruin.Cause})");
            AddLine("");
        }

        // Settlement info first — most interesting to the user
        if (snapshot?.Settlements.TryGetValue(data.Coord, out var settlement) == true)
        {
            string ruinSuffix = snapshot?.Ruins.TryGetValue(data.Coord, out var existingRuin) == true
                ? $" (on ruins; destroyed {existingRuin.TimesSettled}x)" : "";
            AddLine($"=== {settlement.Name}{ruinSuffix} ===");
            AddLine($"Civ: {settlement.CivName}");
            AddLine($"Pop: {settlement.Population:N0}");
            string healthLabel = settlement.Health >= 70 ? "Good"
                               : settlement.Health >= 40 ? "Struggling" : "Critical";
            AddLine($"Health: {settlement.Health}/100 ({healthLabel})");
            AddLine($"Founded: Year {settlement.FoundedYear}");
            if (settlement.ConqueredYear > 0)
                AddLine($"Conquered: Year {settlement.ConqueredYear} (from civ {settlement.ConqueredFromCivId})");
            if (settlement.ResourceLedger is { Count: > 0 } ledger)
            {
                AddLine("--- Resources (this tick) ---");
                foreach (var (res, val) in ledger.OrderByDescending(kv => kv.Value))
                    AddLine($"  {res}: {(val >= 0 ? "+" : "")}{val:F2}");
            }
            if (settlement.ResourceStores is { Count: > 0 } stores)
            {
                AddLine("--- Stores ---");
                foreach (var (res, amount) in stores.OrderByDescending(kv => kv.Value))
                {
                    string label = res is "food" or "water"
                        ? (amount >= 2f ? "well-stocked" : amount >= 0.5f ? "adequate" : "bare")
                        : (amount >= 10f ? "abundant" : amount >= 2f ? "moderate" : "scarce");
                    AddLine($"  {res}: {amount:F1} ({label})");
                }
            }
            AddLine("");
        }

        AddLine($"Tile ({data.Coord.X}, {data.Coord.Y})");
        AddLine($"Biome: {(BiomeType)tile.BiomeType}");
        AddLine($"Elevation: {tile.Elevation}");
        AddLine($"Base Temp: {TempC(tile.BaseTemperature):F1}°C  ({TempF(tile.BaseTemperature):F0}°F)");
        AddLine($"Current Moisture: {tile.CurrentMoisture}");
        AddLine($"Effective Temp: {TempC(data.EffectiveTemperature):F1}°C  ({TempF(data.EffectiveTemperature):F0}°F)");
        AddLine($"Magic: {tile.MagicIntensity}");
        AddLine($"Fertility: {tile.Fertility}");

        AddLine("--- Seasonal Profile ---");
        var prof = data.SeasonalProfile;
        AddLine($"Spring:  Temp {TempDeltaC(prof.TempDeltaSpring):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaSpring:+#;-#;0}");
        AddLine($"Summer:  Temp {TempDeltaC(prof.TempDeltaSummer):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaSummer:+#;-#;0}");
        AddLine($"Autumn:  Temp {TempDeltaC(prof.TempDeltaAutumn):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaAutumn:+#;-#;0}");
        AddLine($"Winter:  Temp {TempDeltaC(prof.TempDeltaWinter):+#.#;-#.#;0}°C  Moist {prof.MoistureDeltaWinter:+#;-#;0}");

        AddLine("--- Resources ---");
        if (data.Deposits.Count == 0) AddLine("(none)");
        else foreach (var d in data.Deposits) AddLine($"{d.DepositType} (Q:{d.Quality} D:{d.Depth})");

        AddLine("--- Disasters ---");
        var disasters = data.Disasters.ToList(); // snapshot; sim thread may mutate the source
        if (disasters.Count == 0) AddLine("(none)");
        else foreach (var d in disasters)
            AddLine($"{d.Type} {d.Intensity:F2} [{(d.TicksRemaining < 0 ? "∞" : d.TicksRemaining.ToString())} ticks]");
        AddLine($"In drought: {data.IsInActiveDrought}");

        // ── Territory section (M3 Phase 3.4) ─────────────────────────────────
        if (data.TerritoryOwnerName is not null)
        {
            AddLine("--- Territory ---");
            string cityPart = data.TerritoryCityName is not null
                ? $" (city: {data.TerritoryCityName})" : "";
            AddLine($"  {data.TerritoryOwnerName}{cityPart}");

            if (data.Improvement.HasValue)
            {
                string builtYear = data.ImprovementBuiltYear > 0
                    ? $", built Year {data.ImprovementBuiltYear}" : "";
                string builder = data.ImprovementBuilderName is not null
                    ? $" by {data.ImprovementBuilderName}" : "";
                AddLine($"  Improvement: {data.Improvement}{builtYear}{builder}");
            }
        }

        // ── Characters + Watch buttons (M3 Phase 3.4) ────────────────────────
        if (snapshot is not null)
        {
            AddBeastSection(data.Coord, snapshot.EntitySnapshots);
            AddCharacterSection(data.Coord, snapshot.EntitySnapshots);
        }

        // ── History at tile (M3 Phase 3.4) ───────────────────────────────────
        if (data.TileHistory is { Count: > 0 } history)
        {
            AddLine("--- History at this tile ---");
            foreach (var (year, desc) in history)
                AddLine($"  Year {year} — {desc}");
        }
    }

    private void AddBeastSection(
        TileCoord coord,
        IReadOnlyDictionary<EntityId, EntitySnapshot> entitySnapshots)
    {
        var beasts = entitySnapshots.Values
            .Where(e => e.Kind == EntityKind.LegendaryBeast && e.IsAlive && e.Location == coord)
            .ToList();

        if (beasts.Count == 0) return;

        AddLine("--- Creatures ---");
        foreach (var b in beasts)
        {
            string tag = b.IsLegendary ? " [Legendary]" : "";
            AddLine($"{b.Name}{tag}");
            AddLine($"  HP {b.HealthFraction:P0}  Food {b.FoodFraction:P0}  Age {b.AgeSeason}");
        }
    }

    private void AddCharacterSection(
        TileCoord coord,
        IReadOnlyDictionary<EntityId, EntitySnapshot> entitySnapshots)
    {
        var tier1 = entitySnapshots.Values
            .Where(e => e.Kind == EntityKind.Tier1Character && e.IsAlive && e.Location == coord)
            .ToList();
        var tier2 = entitySnapshots.Values
            .Where(e => e.Kind == EntityKind.Tier2Character && e.IsAlive && e.Location == coord)
            .ToList();

        if (tier1.Count == 0 && tier2.Count == 0) return;

        AddLine("--- Characters ---");
        foreach (var c in tier1)
        {
            string civTag = c.CivName is not null ? $" [{c.CivName}]" : "";
            string ancTag = c.AncestryId.Length > 0 ? $" ({c.AncestryId})" : "";

            // Row: name label + Watch button
            var row = new HorizontalStackPanel { Spacing = 4 };
            row.Widgets.Add(new Label { Text = $"{c.Name}{civTag}{ancTag}" });

            long capturedId = c.Id.Value;
            var watchBtn = new TextButton
            {
                Text    = "[Watch]",
                Padding = new Myra.Graphics2D.Thickness(2)
            };
            watchBtn.Click += (_, _) => _pendingWatchCharacterId = capturedId;
            row.Widgets.Add(watchBtn);
            _content.Widgets.Add(row);

            string wbLabel = c.Wellbeing switch
            {
                >= 0.7f  => "Flourishing",
                >= 0.3f  => "Content",
                >= -0.3f => "Neutral",
                >= -0.7f => "Distressed",
                _        => "Spiraling"
            };
            AddLine($"  HP {c.HealthFraction:P0}  Age {c.AgeSeason}s  [{wbLabel}]");
        }
        foreach (var c in tier2)
        {
            AddLine($"{c.Name} [Tier2]");
            AddLine($"  HP {c.HealthFraction:P0}  Age {c.AgeSeason}s");
        }
    }

    // Raw 0-255 maps to -50°C … +50°C (100°C span)
    private static float TempC(float raw) => raw * (100f / 255f) - 50f;
    private static float TempF(float raw) => TempC(raw) * 9f / 5f + 32f;
    private static float TempDeltaC(float rawDelta) => rawDelta * (100f / 255f);

    private void AddLine(string text) =>
        _content.Widgets.Add(new Label { Text = text });
}
