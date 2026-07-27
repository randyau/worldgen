namespace WorldEngine.Sim.Config;

/// <summary>Adds TicksPerSeason (alias) and TicksPerYear (= TicksPerSeasonalChange × 4) derived properties; use these everywhere instead of hardcoded 16.</summary>
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

    /// <summary>
    /// Minimum wall-clock seconds between headless-runner progress log lines (see
    /// <see cref="Simulation.SimLoop.RunSynchronous"/>). Progress is sampled once/year
    /// internally but only printed at this cadence, so long runs stay observable without
    /// flooding the console with one line per simulated year. Default: 10s.
    /// </summary>
    public double HeadlessProgressIntervalSeconds { get; set; } = 10.0;

    /// <summary>
    /// In-game years between automatic <see cref="Persistence.EventStore.BuildSummaries"/> calls
    /// (rebuilds CharacterSummaries/CivSummaries/causal edges/etc. for CivHistoryPanel). 0 disables
    /// automatic rebuilds entirely — the caller is then responsible for calling BuildSummaries
    /// itself (e.g. once, on demand). Each rebuild rescans the full Events table, so cost grows
    /// with total historical event count; the headless runner sets this to 0 since nothing reads
    /// history mid-run. Default: 50 (matches the interactive UI's prior hardcoded cadence).
    /// </summary>
    public int SummaryRebuildIntervalYears { get; set; } = 50;

    // ─── Derived time-scale helpers ───────────────────────────────────────────

    /// <summary>Ticks per season (same as TicksPerSeasonalChange — more readable alias).</summary>
    public int TicksPerSeason => TicksPerSeasonalChange;

    /// <summary>
    /// Ticks per in-game year = TicksPerSeason × 4 seasons.
    /// Default: 4 × 4 = 16. Use this everywhere instead of the literal 16.
    /// </summary>
    public int TicksPerYear => TicksPerSeasonalChange * 4;
}
