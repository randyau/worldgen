namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile ocean and coast flags produced by OceanLayer.</summary>
public sealed class OceanResult
{
    /// <summary>True if tile is ocean (below sea level threshold).</summary>
    public bool[] IsOcean { get; }

    /// <summary>True if tile is land adjacent to at least one ocean tile.</summary>
    public bool[] IsCoastal { get; }

    public OceanResult(int tileCount)
    {
        IsOcean   = new bool[tileCount];
        IsCoastal = new bool[tileCount];
    }
}
