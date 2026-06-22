using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// Complete tile data for the inspector panel. Created by sim thread on demand.
/// Contains base values, seasonal profiles, and all registry data for the tile.
/// </summary>
public sealed record TileInspectorData(
    TileCoord Coord,
    TileData RawTile,                              // full 14-byte struct (base/genesis values)
    SeasonalProfile SeasonalProfile,               // all four season deltas
    float EffectiveTemperature,                    // float precision for display
    float CurrentMoistureF,                        // float precision for display
    IReadOnlyList<ResourceDeposit> Deposits,       // from ResourceRegistry
    IReadOnlyList<ActiveDisaster> Disasters,       // from ActiveTileDisasters
    bool IsInActiveDrought,                        // computed from ActiveDroughts list
    EventId? DroughtOriginEventId                  // set if IsInActiveDrought
);
