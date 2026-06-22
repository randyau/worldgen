using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Fixed-capacity ring buffer of recent SimEvents.
/// Add() and GetRecent() are sim-thread-only. Thread safety via StateCache wrapping snapshots.
/// </summary>
public sealed class EventCache(int maxSize = 500)
{
    private readonly SimEvent[] _ring = new SimEvent[maxSize];
    private int _head;   // next write position
    private int _count;  // number of valid entries (≤ maxSize)
    private readonly HashSet<EventType> _seenTypes = new();

    public void Add(SimEvent evt)
    {
        _ring[_head] = evt;
        _head = (_head + 1) % maxSize;
        if (_count < maxSize) _count++;
        _seenTypes.Add(evt.Type);
    }

    public IReadOnlyList<SimEvent> GetRecent(int count)
    {
        int n = Math.Min(count, _count);
        var result = new SimEvent[n];
        // Read from tail (oldest) to head (newest), return last n
        int start = (_head - n + maxSize * 2) % maxSize;
        for (int i = 0; i < n; i++)
            result[i] = _ring[(start + i) % maxSize];
        return result;
    }

    public bool ContainsType(EventType type) => _seenTypes.Contains(type);

    public IReadOnlyList<SimEvent> GetByType(EventType type)
    {
        var result = new List<SimEvent>();
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - _count + i + maxSize * 2) % maxSize;
            if (_ring[idx].Type == type) result.Add(_ring[idx]);
        }
        return result;
    }
}
