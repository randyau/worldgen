using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Config;

/// <summary>
/// Per-biome wildlife-raid risk multiplier table (D3 extraction from PopulationDynamicsPhase).
///
/// TOML section: [wildlife_risk]
/// Each key is a BiomeType name in snake_case (e.g. tropical_rainforest, boreal_forest).
/// The value is a float multiplier applied to <see cref="SettlementConfig.WildlifeAttackBaseChance"/>.
///
/// Design rationale (from PopulationDynamicsPhase comment):
///   Dense cover (forest, swamp) gives predators ambush advantage — multipliers above 1.0.
///   Open terrain (plains, savanna, desert) provides visibility — multipliers below 1.0.
///   The default for unlisted biomes is 0.6 (matches the original _ => 0.6f fallback).
///
/// TOML section: [wildlife_risk]
/// </summary>
public sealed class WildlifeRiskConfig
{
    /// <summary>
    /// Raw TOML table: biome-name (snake_case) → risk multiplier float.
    /// Loaded by Tomlyn. Baked into a float[16] array indexed by (int)BiomeType at construction.
    /// Biome names use lowercase with underscores (e.g. "tropical_rainforest", "boreal_forest").
    /// </summary>
    public Dictionary<string, float> BiomeRisk { get; set; } = new();

    /// <summary>
    /// Fallback multiplier for any BiomeType not listed in <see cref="BiomeRisk"/>.
    /// Matches the original _ => 0.6f switch fallback.
    /// </summary>
    public float DefaultRisk { get; set; } = 0.6f;

    /// <summary>
    /// Build and return a pre-baked float[16] array indexed by (int)BiomeType.
    /// Called once at PopulationDynamicsPhase construction, exactly as
    /// ResourcePressurePhase builds its _foodTable.
    /// </summary>
    public float[] BuildTable()
    {
        var table = new float[16];

        // Fill with default first; override from BiomeRisk entries.
        for (int i = 0; i < 16; i++)
            table[i] = DefaultRisk;

        foreach (var (name, value) in BiomeRisk)
        {
            if (TryParseBiome(name, out int bi))
                table[bi] = value;
        }

        return table;
    }

    private static bool TryParseBiome(string name, out int index)
    {
        // Map snake_case TOML names to BiomeType enum integers.
        // BiomeType order: Ocean=0, CoastalWater=1, Beach=2, Tundra=3, BorealForest=4,
        // TemperateForest=5, TropicalRainforest=6, Grassland=7, Savanna=8, Desert=9,
        // Swamp=10, HighMountain=11, Mountain=12, Hills=13, Plains=14, Volcanic=15
        index = name.ToLowerInvariant() switch
        {
            "ocean"               => (int)BiomeType.Ocean,
            "coastal_water"       => (int)BiomeType.CoastalWater,
            "beach"               => (int)BiomeType.Beach,
            "tundra"              => (int)BiomeType.Tundra,
            "boreal_forest"       => (int)BiomeType.BorealForest,
            "temperate_forest"    => (int)BiomeType.TemperateForest,
            "tropical_rainforest" => (int)BiomeType.TropicalRainforest,
            "grassland"           => (int)BiomeType.Grassland,
            "savanna"             => (int)BiomeType.Savanna,
            "desert"              => (int)BiomeType.Desert,
            "swamp"               => (int)BiomeType.Swamp,
            "high_mountain"       => (int)BiomeType.HighMountain,
            "mountain"            => (int)BiomeType.Mountain,
            "hills"               => (int)BiomeType.Hills,
            "plains"              => (int)BiomeType.Plains,
            "volcanic"            => (int)BiomeType.Volcanic,
            _                     => -1,
        };
        return index >= 0;
    }
}
