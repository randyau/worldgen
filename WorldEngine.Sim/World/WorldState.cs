using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// The complete mutable world state. Owned by the sim thread — never accessed from the UI thread.
/// The UI reads WorldSnapshot via StateCache.
/// </summary>
public sealed class WorldState
{
    // === IDENTITY ===
    public WorldConfig Config { get; }
    public SimConfig SimConfig { get; }
    public int WorldSeed => Config.Seed;

    // === TILE DATA ===
    public TileGrid TileGrid { get; }

    /// <summary>
    /// Seasonal climate deltas parallel to TileGrid.
    /// Populated during world gen assembly (1.3.8). Never mutated after world gen.
    /// </summary>
    public SeasonalProfile[] SeasonalProfiles { get; }

    // === REGISTRIES ===
    public Dictionary<TileCoord, List<ResourceDeposit>> ResourceRegistry { get; }
    public Dictionary<TileCoord, List<ActiveDisaster>> ActiveTileDisasters { get; } = new();
    public List<ActiveDrought> ActiveDroughts { get; } = new();

    // === TIME ===
    public int CurrentYear { get; set; }
    public Season CurrentSeason { get; set; }
    public long CurrentTick { get; set; }

    // === DRIFT PARAMETERS (genesis = zero/defaults) ===
    public float GlobalTemperatureAnomaly { get; set; }
    public float CurrentSeaLevel { get; set; }
    public float GlobalPrecipitationMultiplier { get; set; } = 1.0f;

    /// <summary>Normalized lat of storm corridor center (0=south, 1=north). May drift.</summary>
    public float StormCorridorNormalizedLat { get; set; }

    /// <summary>Monsoon season moisture multiplier. Starts at config value.</summary>
    public float MonsoonIntensityMultiplier { get; set; } = 1.5f;

    public WorldState(
        WorldConfig config,
        SimConfig simConfig,
        TileGrid tileGrid,
        SeasonalProfile[] seasonalProfiles,
        Dictionary<TileCoord, List<ResourceDeposit>> resourceRegistry,
        float stormCorridorNormalizedLat)
    {
        Config                    = config;
        SimConfig                 = simConfig;
        TileGrid                  = tileGrid;
        SeasonalProfiles          = seasonalProfiles;
        ResourceRegistry          = resourceRegistry;
        StormCorridorNormalizedLat = stormCorridorNormalizedLat;
        MonsoonIntensityMultiplier = simConfig.Climate.MonsoonIntensityMultiplier;
    }
}
