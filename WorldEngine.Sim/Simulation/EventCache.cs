using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// In-memory ring buffer of recent SimEvents for WorldSnapshot.RecentEvents.
/// Thread-safe via lock. Stub in Phase 4 — ring buffer implemented in Phase 6 (Epic 1.6).
/// </summary>
public sealed class EventCache
{
    private readonly object _lock = new();
    private readonly List<SimEvent> _events = new();

    public void Add(SimEvent simEvent)
    {
        lock (_lock) { _events.Add(simEvent); }
    }

    public IReadOnlyList<SimEvent> GetRecent(int maxCount)
    {
        lock (_lock)
        {
            int skip = Math.Max(0, _events.Count - maxCount);
            return _events.Skip(skip).ToList();
        }
    }
}
