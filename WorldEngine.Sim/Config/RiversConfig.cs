namespace WorldEngine.Sim.Config;

/// <summary>River flow accumulation threshold and lake detection constants for world generation.</summary>
public class RiversConfig
{
    public int FlowAccumulationThreshold { get; set; } = 50;
    public int MinLakeBasinTiles { get; set; } = 20;
    public int MajorRiverThreshold { get; set; } = 500;

    /// <summary>Minimum width (fraction of a tile edge's length) a border-manifest river crossing occupies.</summary>
    public float CrossingMinWidthFraction { get; set; } = 0.05f;

    /// <summary>Width (fraction of a tile edge's length) a crossing occupies at/above MajorRiverThreshold flow.</summary>
    public float CrossingMaxWidthFraction { get; set; } = 0.25f;
}
