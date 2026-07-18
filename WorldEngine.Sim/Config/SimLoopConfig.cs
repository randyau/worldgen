namespace WorldEngine.Sim.Config;

public class SimLoopConfig
{
    /// <summary>
    /// Number of simulation ticks between each seasonal change. Default 4.
    /// Multiply by 4 (seasons/year) to get <see cref="TicksPerYear"/>.
    /// Alias: ticks_per_season.
    /// </summary>
    public int TicksPerSeasonalChange { get; set; } = 4;

    public float SlowTicksPerSecond      { get; set; } = 0.5f;
    public float NormalTicksPerSecond    { get; set; } = 1.0f;
    public float FastTicksPerSecond      { get; set; } = 10.0f;
    public float UltrafastTicksPerSecond { get; set; } = 200.0f;
    public int UltrafastSnapshotIntervalTicks { get; set; } = 160; // 10 years at default 16 ticks/year
    public int EventWriteBatchIntervalTicks   { get; set; } = 20;  // ~1 year at Normal speed; 0=every tick
    public int AutoSaveIntervalTicks          { get; set; } = 960; // ~60 years at default 16 ticks/year
    public string AutoSaveDir { get; set; } = "worldsave";

    /// <summary>
    /// When true, MetricsCollector samples world state once per in-game year and writes
    /// a row to the yearly_metrics table in world.db. Safe to enable in all modes.
    /// Default: true. Disable only for micro-benchmarks where DB writes must be minimized.
    /// </summary>
    public bool MetricsEnabled { get; set; } = true;

    // ─── Derived time-scale helpers ───────────────────────────────────────────

    /// <summary>Ticks per season (same as TicksPerSeasonalChange — more readable alias).</summary>
    public int TicksPerSeason => TicksPerSeasonalChange;

    /// <summary>
    /// Ticks per in-game year = TicksPerSeason × 4 seasons.
    /// Default: 4 × 4 = 16. Use this everywhere instead of the literal 16.
    /// </summary>
    public int TicksPerYear => TicksPerSeasonalChange * 4;
}
