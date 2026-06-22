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

        // Priority 5: Hill elevation
        bool isHills = elevation >= t.HillsElevation;

        // Priority 6: Polar temperature (any moisture → Tundra)
        if (temperature < t.PolarTemperature) return BiomeType.Tundra;

        // Priority 7: Cold temperature
        if (temperature < t.ColdTemperature)
        {
            return moisture >= t.WetMoisture ? BiomeType.BorealForest : BiomeType.Tundra;
        }

        // Priority 8: Hot temperature + moisture matrix
        if (temperature >= t.HotTemperature)
        {
            if (moisture >= t.WetMoisture) return BiomeType.TropicalRainforest;
            if (moisture >= t.DryMoisture) return isHills ? BiomeType.Hills : BiomeType.Savanna;
            if (moisture >= t.AridMoisture) return BiomeType.Savanna;
            return BiomeType.Desert;
        }

        // Priority 9: Temperate zone — temperature between ColdTemperature and HotTemperature
        if (moisture >= t.WetMoisture)
        {
            return isHills ? BiomeType.Hills : BiomeType.TemperateForest;
        }
        if (moisture >= t.DryMoisture)
        {
            return isHills ? BiomeType.Hills : BiomeType.Grassland;
        }
        if (moisture >= t.AridMoisture)
        {
            return isHills ? BiomeType.Hills : BiomeType.Plains;
        }

        // Arid temperate → effectively desert conditions
        return BiomeType.Desert;
    }
}
