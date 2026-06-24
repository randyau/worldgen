using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Tiles;

/// <summary>
/// Computes the effective temperature for a tile at a given simulation moment,
/// combining static base temperature, seasonal delta, and global anomaly.
/// Shared by all phases that need per-tile climate conditions.
/// </summary>
public static class TileTemperature
{
    /// <summary>
    /// Effective temperature (0–255) for a tile this tick, accounting for
    /// seasonal delta from its SeasonalProfile and the global temperature anomaly.
    /// </summary>
    public static byte Effective(TileData tile, int tileIndex, WorldState world)
    {
        var profile = world.SeasonalProfiles[tileIndex];
        int delta = world.CurrentSeason switch
        {
            Season.Spring => profile.TempDeltaSpring,
            Season.Summer => profile.TempDeltaSummer,
            Season.Autumn => profile.TempDeltaAutumn,
            Season.Winter => profile.TempDeltaWinter,
            _             => 0
        };
        float normLat  = tileIndex / world.TileGrid.TileWidth / (float)world.TileGrid.TileHeight;
        float latScale = 1f + MathF.Abs(normLat - 0.5f) * world.SimConfig.Climate.LatTemperatureAnomalyScale;
        int   raw      = tile.BaseTemperature + delta + (int)(world.GlobalTemperatureAnomaly * latScale);
        return (byte)Math.Clamp(raw, 0, 255);
    }
}
