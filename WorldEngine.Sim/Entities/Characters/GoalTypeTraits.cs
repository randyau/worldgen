using System.Collections.Frozen;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Per-<see cref="GoalType"/> classification flags, replacing the five hand-maintained goal-type
/// membership lists <see cref="GoalManager"/> used to keep in sync by hand.
/// </summary>
/// <param name="IsNotable">Worth logging as a narrative event when formed or resolved.</param>
/// <param name="IsLongRunning">
/// Legitimately takes years, so it is pruned against the longer inner-life staleness limit rather
/// than <c>GoalStaleSeasonLimit</c>.
/// </param>
/// <param name="IsDiscretionary">Counts against the shared <c>MaxConcurrentGoals</c> ceiling.</param>
/// <param name="IsFlourishing">Contributes to the Wellbeing "has something to live for" check.</param>
public readonly record struct GoalTraits(
    bool IsNotable,
    bool IsLongRunning,
    bool IsDiscretionary,
    bool IsFlourishing);

/// <summary>
/// The single source of truth for which <see cref="GoalType"/>s are notable / long-running /
/// discretionary / flourishing. Every enum value must have a row — see
/// <c>GoalTypeTraitsTests.AllGoalTypesHaveTraits</c>.
/// </summary>
public static class GoalTypeTraits
{
    private static readonly FrozenDictionary<GoalType, GoalTraits> Table = new Dictionary<GoalType, GoalTraits>
    {
        //                                    notable longRun discretionary flourishing
        [GoalType.Survive]          = new(false,  false,  false, false),
        [GoalType.Security]         = new(false,  false,  false, false),
        [GoalType.Acquire]          = new(false,  false,  false, false),
        [GoalType.Flee]             = new(false,  false,  false, false),
        [GoalType.Endure]           = new(false,  false,  false, false),
        [GoalType.Dominance]        = new(true,   false,  true,  false),
        [GoalType.Alliance]         = new(true,   true,   true,  false),
        [GoalType.Unify]            = new(false,  false,  false, false),
        [GoalType.Bond]             = new(true,   true,   true,  true),
        [GoalType.Protect]          = new(false,  false,  false, true),
        [GoalType.Avenge]           = new(true,   false,  false, false),
        [GoalType.Grieve]           = new(false,  false,  false, false),
        [GoalType.Create]           = new(true,   true,   true,  true),
        [GoalType.FoundCity]        = new(true,   true,   false, true),
        [GoalType.BuildImprovement] = new(true,   true,   true,  false),
        [GoalType.FoundReligion]    = new(false,  false,  false, false),
        [GoalType.SlayBeast]        = new(true,   true,   true,  false),
        [GoalType.CovetArtifact]    = new(true,   true,   true,  false),
        [GoalType.SeaVoyage]        = new(true,   true,   false, false),
        [GoalType.Pilgrimage]       = new(true,   true,   false, false),
    }.ToFrozenDictionary();

    /// <summary>Traits for <paramref name="type"/>. Throws if the enum value has no row.</summary>
    public static GoalTraits Of(GoalType type) => Table[type];

    /// <summary>Worth logging as a narrative event when formed or resolved.</summary>
    public static bool IsNotable(GoalType type) => Table[type].IsNotable;

    /// <summary>Pruned against the longer inner-life staleness limit rather than GoalStaleSeasonLimit.</summary>
    public static bool IsLongRunning(GoalType type) => Table[type].IsLongRunning;

    /// <summary>Counts against the shared MaxConcurrentGoals ceiling.</summary>
    public static bool IsDiscretionary(GoalType type) => Table[type].IsDiscretionary;

    /// <summary>Contributes to the Wellbeing "has something to live for" check.</summary>
    public static bool IsFlourishing(GoalType type) => Table[type].IsFlourishing;
}
