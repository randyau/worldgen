using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// Lightweight event record produced during simulation phases.
/// Phase 7 assigns Id, Year, Season, Tick, runs significance classification,
/// applies the event gate, and writes to SQLite + EventCache.
/// </summary>
public sealed record PendingEvent(
    EventType Type,
    TileCoord? Location,
    EventId? CauseEventId,
    string PayloadJson,
    IReadOnlyList<long>? PrimaryEntityIds = null,
    IReadOnlyList<long>? SecondaryEntityIds = null,
    long ActorId = 0,
    string? ActorName = null,
    long CivId = 0,
    string? SettlementName = null
);
