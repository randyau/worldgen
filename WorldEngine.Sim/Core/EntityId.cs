namespace WorldEngine.Sim.Core;

public readonly record struct EntityId(long Value)
{
    public static EntityId New() => new(IdGenerator.Next());

    /// <summary>
    /// Advances the global ID counter to at least <paramref name="minValue"/>.
    /// Call after loading saved entities to prevent ID collisions with newly created entities.
    /// Thread-safe via compare-exchange.
    /// </summary>
    public static void EnsureCounterExceeds(long minValue) =>
        IdGenerator.EnsureCounterExceeds(minValue);
}

internal static class IdGenerator
{
    private static long _counter;
    public static long Next() => Interlocked.Increment(ref _counter);

    public static void EnsureCounterExceeds(long minValue)
    {
        long current;
        do { current = Interlocked.Read(ref _counter); }
        while (current < minValue
            && Interlocked.CompareExchange(ref _counter, minValue, current) != current);
    }
}
