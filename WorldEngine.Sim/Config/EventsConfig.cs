using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Config;

/// <summary>
/// Nested config under [events.gate] in sim_config.toml.
/// Controls which event types are always or never recorded, independent of tier.
/// </summary>
public class EventGateConfig
{
    /// <summary>
    /// Event types that are always recorded regardless of minimum tier setting.
    /// Guarantees that historically significant events are never filtered even if the
    /// minimum_recorded_tier is raised for performance.
    /// </summary>
    public List<string> AlwaysRecordTypes { get; set; } = new();

    /// <summary>Event types that are always suppressed (pure bookkeeping noise).</summary>
    public List<string> SuppressedTypes { get; set; } = new();
}

public class EventsConfig
{
    public EventTier MinimumRecordedTier { get; set; } = EventTier.Background;
    public List<string> SuppressedTypes { get; set; } = new();
    public int RecentEventCacheSize { get; set; } = 500;

    /// <summary>Per-gate overrides: always_record_types and suppressed_types from [events.gate] section.</summary>
    public EventGateConfig Gate { get; set; } = new();
}
