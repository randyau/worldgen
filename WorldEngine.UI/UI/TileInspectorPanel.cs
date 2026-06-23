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

        AddLine($"Tile ({data.Coord.X}, {data.Coord.Y})");
        AddLine($"Biome: {(BiomeType)tile.BiomeType}");
        AddLine($"Elevation: {tile.Elevation}");
        AddLine($"Base Temp: {tile.BaseTemperature}");
        AddLine($"Current Moisture: {tile.CurrentMoisture}");
        AddLine($"Effective Temp: {data.EffectiveTemperature:F1}");
        AddLine($"Magic: {tile.MagicIntensity}");
        AddLine($"Fertility: {tile.Fertility}");

        AddLine("--- Seasonal Profile ---");
        var p = data.SeasonalProfile;
        AddLine($"Spring:  Temp {p.TempDeltaSpring:+#;-#;0}  Moist {p.MoistureDeltaSpring:+#;-#;0}");
        AddLine($"Summer:  Temp {p.TempDeltaSummer:+#;-#;0}  Moist {p.MoistureDeltaSummer:+#;-#;0}");
        AddLine($"Autumn:  Temp {p.TempDeltaAutumn:+#;-#;0}  Moist {p.MoistureDeltaAutumn:+#;-#;0}");
        AddLine($"Winter:  Temp {p.TempDeltaWinter:+#;-#;0}  Moist {p.MoistureDeltaWinter:+#;-#;0}");

        AddLine("--- Resources ---");
        if (data.Deposits.Count == 0) AddLine("(none)");
        else foreach (var d in data.Deposits) AddLine($"{d.DepositType} (Q:{d.Quality} D:{d.Depth})");

        AddLine("--- Disasters ---");
        if (data.Disasters.Count == 0) AddLine("(none)");
        else foreach (var d in data.Disasters)
            AddLine($"{d.Type} {d.Intensity:F2} [{(d.TicksRemaining < 0 ? "∞" : d.TicksRemaining.ToString())} ticks]");
        AddLine($"In drought: {data.IsInActiveDrought}");

        // Beast section — built from EntitySnapshots filtered by tile coord
        if (snapshot is not null)
            AddBeastSection(data.Coord, snapshot.EntitySnapshots);
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

    private void AddLine(string text) =>
        _content.Widgets.Add(new Label { Text = text });
}
