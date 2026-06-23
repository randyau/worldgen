using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// The complete mutable world state. Owned by the sim thread — never accessed from the UI thread.
/// The UI reads WorldSnapshot via StateCache.
/// </summary>
public sealed class WorldState : IWorldStateReadOnly
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
    public EntityRegistry Entities { get; } = new();

    /// <summary>Deferred mythological beast emergence schedule. Processed annually.</summary>
    public List<(int EmergenceYear, string SpeciesId)> BeastEmergenceSchedule { get; } = new();

    // === TIME ===
    public int CurrentYear { get; internal set; } = 1;
    public Season CurrentSeason { get; internal set; } = Season.Spring;
    public long CurrentTick { get; internal set; }

    // === DRIFT PARAMETERS (genesis = zero/defaults) ===
    public float GlobalTemperatureAnomaly { get; internal set; }
    public float CurrentSeaLevel { get; internal set; }
    public float GlobalPrecipitationMultiplier { get; internal set; } = 1.0f;

    /// <summary>Normalized lat of storm corridor center (0=south, 1=north). May drift.</summary>
    public float StormCorridorNormalizedLat { get; internal set; }

    /// <summary>Width of the storm corridor band (normalized). May drift.</summary>
    public float StormCorridorHalfWidth { get; internal set; }

    /// <summary>Monsoon season moisture multiplier. Starts at config value.</summary>
    public float MonsoonIntensityMultiplier { get; internal set; } = 1.5f;

    /// <summary>Global scaling of volcanic event probability. Starts at 1.0.</summary>
    public float VolcanicActivityMultiplier { get; internal set; } = 1.0f;

    // === UI INSPECTOR ===
    /// <summary>Set by SetInspectedTile command. Null means no tile is selected.</summary>
    public TileCoord? InspectedTile { get; internal set; }

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
        StormCorridorHalfWidth    = simConfig.Climate.StormCorridorHalfWidth;
        MonsoonIntensityMultiplier = simConfig.Climate.MonsoonIntensityMultiplier;
    }

    // === IWorldStateReadOnly ===

    /// <summary>Returns tile data with East-West cylinder wrapping applied.</summary>
    public TileData GetTile(TileCoord coord)
    {
        int w = TileGrid.TileWidth;
        int wrappedX = ((coord.X % w) + w) % w;
        int clampedY = Math.Clamp(coord.Y, 0, TileGrid.TileHeight - 1);
        return TileGrid.GetTile(new TileCoord(wrappedX, clampedY));
    }

    public bool IsLand(TileCoord coord) =>
        (BiomeType)GetTile(coord).BiomeType is not BiomeType.Ocean and not BiomeType.CoastalWater;

    public IEnumerable<TileCoord> GetTilesInRadius(TileCoord center, int radius)
    {
        int w = TileGrid.TileWidth, h = TileGrid.TileHeight;
        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = Math.Clamp(center.Y + dy, 0, h - 1);
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int x = ((center.X + dx) % w + w) % w;
                yield return new TileCoord(x, y);
            }
        }
    }

    public float GetRandomFloat(EntityId entityId, int salt = 0) =>
        WorldRng.FloatAt(WorldSeed, CurrentTick, (int)(entityId.Value & 0xFFFF), (int)(entityId.Value >> 32), salt);

    public int GetRandomInt(EntityId entityId, int min, int max, int salt = 0) =>
        min + (int)(GetRandomFloat(entityId, salt) * (max - min));

    // === IWorldStateReadOnly — entity access ===

    public IEntity? GetEntity(EntityId id) => Entities.Get(id);

    public IEnumerable<IEntity> GetEntitiesAt(TileCoord coord) => Entities.GetAt(coord);

    public IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius) =>
        Entities.GetInRadius(center, radius);
}
