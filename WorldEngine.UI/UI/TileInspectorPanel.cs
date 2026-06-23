using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

public sealed class TileInspectorPanel
{
    public readonly Panel Root;
    private readonly VerticalStackPanel _content;

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

        // Settlement info first — most interesting to the user
        if (snapshot?.Settlements.TryGetValue(data.Coord, out var settlement) == true)
        {
            AddLine("=== SETTLEMENT ===");
            AddLine($"Civ: {settlement.CivName}");
            AddLine($"Pop: {settlement.Population}");
            string healthLabel = settlement.Health >= 70 ? "Good"
                               : settlement.Health >= 40 ? "Struggling" : "Critical";
            AddLine($"Health: {settlement.Health}/100 ({healthLabel})");
            AddLine($"Founded: Year {settlement.FoundedYear}");
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
        var p = data.SeasonalProfile;
        AddLine($"Spring:  Temp {TempDeltaC(p.TempDeltaSpring):+#.#;-#.#;0}°C  Moist {p.MoistureDeltaSpring:+#;-#;0}");
        AddLine($"Summer:  Temp {TempDeltaC(p.TempDeltaSummer):+#.#;-#.#;0}°C  Moist {p.MoistureDeltaSummer:+#;-#;0}");
        AddLine($"Autumn:  Temp {TempDeltaC(p.TempDeltaAutumn):+#.#;-#.#;0}°C  Moist {p.MoistureDeltaAutumn:+#;-#;0}");
        AddLine($"Winter:  Temp {TempDeltaC(p.TempDeltaWinter):+#.#;-#.#;0}°C  Moist {p.MoistureDeltaWinter:+#;-#;0}");

        AddLine("--- Resources ---");
        if (data.Deposits.Count == 0) AddLine("(none)");
        else foreach (var d in data.Deposits) AddLine($"{d.DepositType} (Q:{d.Quality} D:{d.Depth})");

        AddLine("--- Disasters ---");
        var disasters = data.Disasters.ToList(); // snapshot; sim thread may mutate the source
        if (disasters.Count == 0) AddLine("(none)");
        else foreach (var d in disasters)
            AddLine($"{d.Type} {d.Intensity:F2} [{(d.TicksRemaining < 0 ? "∞" : d.TicksRemaining.ToString())} ticks]");
        AddLine($"In drought: {data.IsInActiveDrought}");

        // Beast section — built from EntitySnapshots filtered by tile coord
        if (snapshot is not null)
        {
            AddBeastSection(data.Coord, snapshot.EntitySnapshots);
            AddCharacterSection(data.Coord, snapshot.EntitySnapshots);
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
            AddLine($"{c.Name}{civTag}{ancTag}");
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
