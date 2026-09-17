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

    // Test reset base: comfortably above the small literal seed offsets (CharacterFactory.Spawn's
    // entitySeq, typically single/double digits) that test helpers pass to hand-construct
    // characters alongside EntityId.New()-assigned ones — resetting to 0 let a freshly-reset
    // New() collide with those literals (e.g. WorldGenTestHelpers.SpawnTier2At's EntityId.New()
    // landing on the same id as SpawnAt(..., seedOffset: 1L)).
    private const long TestResetBase = 1_000_000L;

    /// <summary>
    /// Resets the counter to a fixed base. Test-only: keeps ID sequences (and the WorldRng draws
    /// keyed off them) reproducible regardless of how many IDs earlier tests in the same
    /// process consumed. Never call from production code paths.
    /// </summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _counter, TestResetBase);
}
