using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// An ongoing disaster affecting a specific tile.
/// Created by Phase 1 (Environmental). Cleared by Phase 1 when resolved.
/// OriginEventId links to the SimEvent that started this disaster for causal graph.
/// </summary>
public sealed record ActiveDisaster(
    DisasterType Type,
    float Intensity,        // 0.0–1.0 severity
    int TicksRemaining,     // -1 = indefinite (ash deposits, etc.), else countdown
    EventId OriginEventId   // SimEvent.Id of the event that created this disaster
);
