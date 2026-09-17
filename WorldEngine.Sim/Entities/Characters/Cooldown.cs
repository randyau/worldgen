namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Shared arithmetic and sentinels for the per-character cooldown stamps on
/// <see cref="Tier1Character"/> (<c>LastCreateCompletedTick</c>, <c>LastDefectionTick</c>,
/// <c>LastArtworkYear</c>, <c>LastReligionFoundedYear</c>, <c>LastPilgrimageYear</c>).
/// </summary>
/// <remarks>
/// M15.9 call-site cleanup only — this centralizes the "how long since" arithmetic and the two
/// magic sentinel values, and deliberately does <i>not</i> unify the fields' differing boundary
/// conventions. Each call site keeps its own comparison operator and its own decision about
/// whether to sentinel-guard, because changing any of those would change simulation behavior.
/// </remarks>
public static class Cooldown
{
    /// <summary>Sentinel stored in a tick-based cooldown stamp that has never fired.</summary>
    public const int UnsetTick = -1;

    /// <summary>Sentinel stored in a year-based cooldown stamp that has never fired.</summary>
    public const int UnsetYear = -999;

    /// <summary>
    /// Ticks elapsed since <paramref name="lastTick"/>. An unset stamp
    /// (<see cref="UnsetTick"/>) yields a value one greater than the current tick, which is how
    /// the tick-based sites read "never fired" as "cooldown clear" without an explicit guard.
    /// </summary>
    public static long TicksElapsed(long currentTick, int lastTick) => currentTick - lastTick;

    /// <summary>Years elapsed since <paramref name="lastYear"/>.</summary>
    public static int YearsElapsed(int currentYear, int lastYear) => currentYear - lastYear;

    /// <summary>
    /// True when a year-based stamp has actually been written at least once (i.e. is not
    /// <see cref="UnsetYear"/>). Only the sites that already guarded on the sentinel call this;
    /// <c>LastArtworkYear</c> intentionally does not.
    /// </summary>
    public static bool HasFired(int lastYear) => lastYear > UnsetYear;
}
