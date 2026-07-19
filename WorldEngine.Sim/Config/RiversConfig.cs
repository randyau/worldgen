namespace WorldEngine.Sim.Config;

/// <summary>River flow accumulation threshold and lake detection constants for world generation.</summary>
public class RiversConfig
{
    public int FlowAccumulationThreshold { get; set; } = 50;
    public int MinLakeBasinTiles { get; set; } = 20;
    public int MajorRiverThreshold { get; set; } = 500;
}
