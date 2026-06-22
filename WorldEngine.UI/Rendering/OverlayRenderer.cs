using Microsoft.Xna.Framework;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.Rendering;

public static class OverlayRenderer
{
    public static Color GetColor(TileDisplayData tile, OverlayType overlay) => overlay switch
    {
        OverlayType.Biome       => GetBiomeColor(tile.Biome),
        OverlayType.Elevation   => Greyscale(tile.Elevation),
        OverlayType.Temperature => TempGradient(tile.EffectiveTemperature),
        OverlayType.Moisture    => MoistureGradient(tile.CurrentMoisture),
        OverlayType.Resources   => GetResourceColor(tile),
        OverlayType.MagicIntensity => MagicGradient(tile.MagicIntensity),
        _ => Color.Magenta
    };

    private static Color GetBiomeColor(BiomeType b) => b switch
    {
        BiomeType.Ocean            => new Color(0, 50, 160),
        BiomeType.CoastalWater     => new Color(65, 125, 210),
        BiomeType.Beach            => new Color(238, 214, 175),
        BiomeType.Tundra           => new Color(200, 210, 215),
        BiomeType.BorealForest     => new Color(30, 80, 50),
        BiomeType.TemperateForest  => new Color(50, 130, 60),
        BiomeType.TropicalRainforest => new Color(20, 180, 50),
        BiomeType.Grassland        => new Color(140, 195, 80),
        BiomeType.Savanna          => new Color(190, 175, 90),
        BiomeType.Desert           => new Color(220, 150, 60),
        BiomeType.Swamp            => new Color(80, 100, 50),
        BiomeType.HighMountain     => new Color(240, 240, 245),
        BiomeType.Mountain         => new Color(160, 155, 150),
        BiomeType.Hills            => new Color(155, 130, 90),
        BiomeType.Plains           => new Color(200, 195, 140),
        BiomeType.Volcanic         => new Color(140, 30, 20),
        _ => Color.Magenta
    };

    private static Color Greyscale(byte v) => new Color(v, v, v);

    private static Color TempGradient(byte v)
    {
        float t = v / 255f;
        return new Color((int)(t * 255), 0, (int)((1 - t) * 255));
    }

    private static Color MoistureGradient(byte v)
    {
        float t = v / 255f;
        return Color.Lerp(new Color(210, 185, 135), new Color(30, 90, 200), t);
    }

    private static Color GetResourceColor(TileDisplayData tile)
    {
        if (tile.StaticFlags.HasFlag(Sim.Tiles.TileStaticFlags.HasRareResource)) return new Color(180, 0, 220);
        if (tile.StaticFlags.HasFlag(Sim.Tiles.TileStaticFlags.HasDeposit)) return Color.Yellow;
        return GetBiomeColor(tile.Biome);
    }

    private static Color MagicGradient(byte v)
    {
        float t = v / 255f;
        return Color.Lerp(Color.Black, new Color(180, 80, 255), t);
    }
}
