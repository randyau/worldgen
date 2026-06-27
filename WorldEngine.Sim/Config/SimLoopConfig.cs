namespace WorldEngine.Sim.Config;

public class SimLoopConfig
{
    public int TicksPerSeasonalChange { get; set; } = 4;
    public float SlowTicksPerSecond   { get; set; } = 0.5f;
    public float NormalTicksPerSecond { get; set; } = 1.0f;
    public float FastTicksPerSecond   { get; set; } = 10.0f;
    public float UltrafastTicksPerSecond { get; set; } = 200.0f;
    public int UltrafastSnapshotIntervalTicks { get; set; } = 160; // 10 years; must be a multiple of TicksPerSeasonalChange×4 (=16 ticks/year)
    public int EventWriteBatchIntervalTicks { get; set; } = 20;   // Batch event writes every N ticks instead of every tick; 20≈1 year at Normal speed; 0=every tick
    public int AutoSaveIntervalTicks { get; set; } = 960;         // Auto-save every N ticks (default 960 = ~60 in-game years at 16 ticks/year)
    public string AutoSaveDir { get; set; } = "worldsave";        // Directory for auto-save and Ctrl+S saves
}
