using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;

namespace WorldEngine.Sim.World;

/// <summary>
/// Immutable projection of world state for the UI. Created after each tick.
/// UI thread reads this every frame — never touches WorldState directly.
/// </summary>
public sealed record WorldSnapshot(
    // Time
    int CurrentYear,
    Season CurrentSeason,
    SimSpeed CurrentSpeed,
    bool IsPaused,
    long TicksPerSecond,

    // Map — flat array indexed by (y * WorldTileWidth + x); X wraps, Y clamps
    TileDisplayData[] AllTiles,
    OverlayType ActiveOverlay,
    int WorldTileWidth,
    int WorldTileHeight,

    // Event log
    IReadOnlyList<SimEvent> RecentEvents,

    // Tile inspector (null if no tile selected)
    TileInspectorData? InspectedTile,

    // Entities — flat lookup by EntityId; used by inspector and map renderer
    IReadOnlyDictionary<EntityId, EntitySnapshot> EntitySnapshots,

    // World-level drift parameters for UI status display
    float GlobalTemperatureAnomaly,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat
);
