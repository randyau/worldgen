namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile river and lake data produced by RiverLayer.</summary>
public sealed class RiverResult
{
    /// <summary>True if a river flows through this tile.</summary>
    public bool[] HasRiver { get; }

    /// <summary>True if tile is an inland lake (filled sink above basin threshold).</summary>
    public bool[] IsLake { get; }

    /// <summary>
    /// Accumulated upstream flow for each tile.
    /// Value = 1 + sum of uphill neighbors' flow.
    /// </summary>
    public int[] FlowAccumulation { get; }

    /// <summary>True if flow accumulation >= major_river_threshold (POI candidate).</summary>
    public bool[] IsPOICandidate { get; }

    public RiverResult(int tileCount)
    {
        HasRiver        = new bool[tileCount];
        IsLake          = new bool[tileCount];
        FlowAccumulation = new int[tileCount];
        IsPOICandidate  = new bool[tileCount];
    }
}
