namespace WorldEngine.Sim.World;

/// <summary>
/// Thread-safe snapshot bridge. Sim thread calls Commit() after each tick.
/// UI thread calls Read() every frame. Lock held for microseconds only.
/// </summary>
public sealed class StateCache
{
    private readonly ReaderWriterLockSlim _lock = new();
    private WorldSnapshot? _snapshot;

    public void Commit(WorldSnapshot snapshot)
    {
        _lock.EnterWriteLock();
        try { _snapshot = snapshot; }
        finally { _lock.ExitWriteLock(); }
    }

    public WorldSnapshot? Read()
    {
        _lock.EnterReadLock();
        try { return _snapshot; }
        finally { _lock.ExitReadLock(); }
    }
}
