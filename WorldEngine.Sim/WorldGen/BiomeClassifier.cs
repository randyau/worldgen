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
        // DECISION: Stub — real implementation in story 1.3.7
        // Priority order will be: Ocean → HighMountain → Mountain → Volcanic → Lake →
        //   Beach → temperature/moisture matrix
        return BiomeType.Plains;
    }
}
