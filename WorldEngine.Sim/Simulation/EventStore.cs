using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Persists SimEvents to SQLite. Stub in Phase 4 — full implementation in Phase 6 (Epic 1.6).
/// Phase 7 calls Write() after enriching PendingEvents.
/// </summary>
public sealed class EventStore
{
    // DECISION: Stub — write to SQLite implemented in Epic 1.6
    public void Write(SimEvent simEvent) { }
}
