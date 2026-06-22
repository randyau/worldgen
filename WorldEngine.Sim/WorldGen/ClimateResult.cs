using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile climate data produced by ClimateLayer.</summary>
public sealed class ClimateResult
{
    /// <summary>Genesis base temperature (0–255) for each tile.</summary>
    public byte[] BaseTemperature { get; }

    /// <summary>Genesis base moisture (0–255) for each tile.</summary>
    public byte[] BaseMoisture { get; }

    /// <summary>
    /// True if tile is in a tropical monsoon zone.
    /// Stored here (not in TileData) — no StaticFlag bit allocated for it yet.
    /// </summary>
    public bool[] IsMonsoonTile { get; }

    /// <summary>True if tile is within the storm corridor latitude band.</summary>
    public bool[] IsStormCorridor { get; }

    /// <summary>Seasonal temperature/moisture deltas. Parallel to TileGrid.</summary>
    public SeasonalProfile[] SeasonalProfiles { get; }

    public ClimateResult(int tileCount)
    {
        BaseTemperature  = new byte[tileCount];
        BaseMoisture     = new byte[tileCount];
        IsMonsoonTile    = new bool[tileCount];
        IsStormCorridor  = new bool[tileCount];
        SeasonalProfiles = new SeasonalProfile[tileCount];
    }
}
