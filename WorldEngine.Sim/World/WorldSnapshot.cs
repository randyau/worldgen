using System.Collections.Generic;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;

namespace WorldEngine.Sim.World;

/// <summary>
/// Snapshot entry for one territory tile: which city owns it and which civ that city belongs to.
/// Keyed by tile coord in WorldSnapshot.TerritoryMap.
/// </summary>
public sealed record TerritorySnapshot(
    TileCoord CityTile,
    long      CivId);

/// <summary>
/// Snapshot of a tile improvement: type, owning city, year built, and builder.
/// Keyed by tile coord in WorldSnapshot.ImprovementMap.
/// </summary>
public sealed record ImprovementSnapshot(
    string    ImprovementType,
    TileCoord CityTile,
    int       BuiltYear,
    long      BuilderId);

/// <summary>Immutable settlement info for UI display.</summary>
public sealed record SettlementSnapshot(
    TileCoord Coord,
    string    Name,
    string    CivName,
    int       Population,
    int       Health,
    int       FoundedYear,
    IReadOnlyDictionary<string, float>? ResourceLedger = null,
    int       ConqueredYear      = 0,
    int       ConqueredFromCivId = 0,
    IReadOnlyDictionary<string, float>? ResourceStores = null);

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

    // Settlements — keyed by tile coord; used by inspector and map renderer
    IReadOnlyDictionary<TileCoord, SettlementSnapshot> Settlements,

    // Ruins — keyed by tile coord; displayed in inspector and map renderer
    IReadOnlyDictionary<TileCoord, RuinRecord> Ruins,

    // Territory and improvements (M3 Phase 3.0)
    // TerritoryMap: tile → (owning city tile, civ id). Absent = unclaimed.
    IReadOnlyDictionary<TileCoord, TerritorySnapshot> TerritoryMap,
    // ImprovementMap: tile → improvement snapshot. Absent = no improvement.
    IReadOnlyDictionary<TileCoord, ImprovementSnapshot> ImprovementMap,

    // World-level drift parameters for UI status display
    float GlobalTemperatureAnomaly,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat
);
