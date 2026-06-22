namespace WorldEngine.Sim.Config;

public class SimLoopConfig
{
    public int TicksPerSeasonalChange { get; set; } = 4;
    public float SlowTicksPerSecond   { get; set; } = 0.5f;
    public float NormalTicksPerSecond { get; set; } = 1.0f;
    public float FastTicksPerSecond   { get; set; } = 10.0f;
    public float UltrafastTicksPerSecond { get; set; } = 200.0f;
    public int UltrafastSnapshotIntervalTicks { get; set; } = 40;
}
