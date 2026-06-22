namespace WorldEngine.Sim.Core;

public readonly record struct EntityId(long Value)
{
    public static EntityId New() => new(IdGenerator.Next());
}

internal static class IdGenerator
{
    private static long _counter;
    public static long Next() => Interlocked.Increment(ref _counter);
}
