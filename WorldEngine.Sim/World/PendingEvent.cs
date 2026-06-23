using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// Lightweight event record produced by Phase 1 (Environmental).
/// Phase 7 assigns Id, Year, Season, Tick, runs significance classification,
/// applies the event gate, and writes to SQLite + EventCache.
/// </summary>
public sealed record PendingEvent(
    EventType Type,
    TileCoord? Location,
    EventId? CauseEventId,       // null = root event; set = CausalEdge will be created
    string PayloadJson,
    IReadOnlyList<long>? EntityIds = null // entity IDs to cross-reference in EventEntities
);
