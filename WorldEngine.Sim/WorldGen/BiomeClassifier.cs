using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Pure static function that maps (temperature, moisture, elevation, flags) → BiomeType.
/// Priority rules applied top-to-bottom; first match wins.
/// All thresholds come from SimConfig.WorldGen.BiomeThresholds.
/// </summary>
public static class BiomeClassifier
{
    public static BiomeType Classify(
        byte temperature,
        byte moisture,
        byte elevation,
        TileStaticFlags flags,
        SimConfig config)
    {
        var t = config.WorldGen.BiomeThresholds;

        // Priority 1: Elevation extremes
        if (elevation >= t.HighMountainElevation) return BiomeType.HighMountain;
        if (elevation >= t.MountainElevation)     return BiomeType.Mountain;

        // Priority 2: Volcanic flag (after mountain checks — high volcanic peaks stay HighMountain)
        if (flags.HasFlag(TileStaticFlags.IsVolcanic)) return BiomeType.Volcanic;

        // Priority 3: Lakes
        if (flags.HasFlag(TileStaticFlags.IsLake)) return BiomeType.CoastalWater;

        // Priority 4: Coastal low-elevation → Beach
        if (flags.HasFlag(TileStaticFlags.IsCoastal) && elevation < t.HillsElevation)
            return BiomeType.Beach;

        // Hills elevation (140–179) is a terrain feature, not a biome — tiles in this
        // range classify by temperature/moisture like any other land tile. Mountain and
        // HighMountain (handled above) are the only elevation-driven biome overrides.

        // Priority 5: Polar temperature (any moisture → Tundra)
        if (temperature < t.PolarTemperature) return BiomeType.Tundra;

        // Priority 6: Cold temperature
        if (temperature < t.ColdTemperature)
        {
            return moisture >= t.WetMoisture ? BiomeType.BorealForest : BiomeType.Tundra;
        }

        // Priority 7: Hot temperature + moisture matrix
        if (temperature >= t.HotTemperature)
        {
            if (moisture >= t.WetMoisture) return BiomeType.TropicalRainforest;
            if (moisture >= t.DryMoisture) return BiomeType.Savanna;
            if (moisture >= t.AridMoisture) return BiomeType.Savanna;
            return BiomeType.Desert;
        }

        // Priority 8: Temperate zone — temperature between ColdTemperature and HotTemperature
        if (moisture >= t.WetMoisture)  return BiomeType.TemperateForest;
        if (moisture >= t.DryMoisture)  return BiomeType.Grassland;
        if (moisture >= t.AridMoisture) return BiomeType.Plains;

        // Arid temperate → effectively desert conditions
        return BiomeType.Desert;
    }
}
