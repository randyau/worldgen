using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// Per-tile rendering data in WorldSnapshot.AllTiles (index: y * WorldTileWidth + x).
/// Contains effective (current) values, not genesis base values.
/// Created by the sim thread for the full world grid each tick.
/// HasActiveDisaster is computed from ActiveTileDisasters registry.
/// </summary>
public sealed record TileDisplayData(
    BiomeType Biome,
    byte Elevation,
    byte EffectiveTemperature,  // BaseTemp + seasonal delta + GlobalTemperatureAnomaly
    byte CurrentMoisture,       // dynamic moisture, updated each tick
    byte MagicIntensity,
    byte Fertility,
    TileStaticFlags StaticFlags,
    TileDynFlags DynFlags,
    bool HasActiveDisaster       // computed: ActiveTileDisasters.ContainsKey(coord)
);
