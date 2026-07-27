using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Sim.Config;

/// <summary>
/// Configures the two UtilityScorer lookup tables:
///   1. Goal → action affinity weights (how well each action advances each goal type).
///   2. Action base-score need-weights (how much each need's deficit drives each action).
///
/// TOML section: [utility_affinity]
/// Sub-tables:
///   [utility_affinity.goal_affinity]  — goal-name → { action-name = weight }
///   [utility_affinity.action_needs]   — action-name → { need-name = coefficient, _default = fallback }
///
/// Unmapped (goal, action) pairs default to 0.0.
/// Unmapped action need-weights use _default (0.1 for the original fallback, 0.0 for unlisted actions).
/// </summary>
public sealed class UtilityAffinityConfig
{
    // ─── Goal → action affinity ───────────────────────────────────────────────
    // Keyed by GoalType name in snake_case → ActionType name in snake_case → weight.
    // e.g. goal_affinity.dominance.war = 1.0
    public Dictionary<string, Dictionary<string, float>> GoalAffinity { get; set; } = new();

    // ─── Action base-score need-weights ──────────────────────────────────────
    // Keyed by ActionType name in snake_case → need name → coefficient.
    // Need names: food, safety, shelter, belonging, status, purpose, spiritual.
    // "_default" key sets the fallback score for the action when no needs are listed.
    public Dictionary<string, Dictionary<string, float>> ActionNeeds { get; set; } = new();
}

/// <summary>
/// Pre-baked lookup arrays built from <see cref="UtilityAffinityConfig"/> at construction.
/// Indexed by (int)GoalType and (int)ActionType respectively.
/// Building once at startup keeps the hot-path (every character tick) to simple array reads.
/// </summary>
public sealed class UtilityAffinityTables
{
    // Number of GoalType enum values. Increase if new values are added.
    private const int GoalCount   = 19;
    // Number of ActionType values in UtilityScorer's private enum.
    private const int ActionCount = 14;
    // Need index constants (must match UtilityScorer.NeedIndex).
    internal const int NI_Food      = 0;
    internal const int NI_Safety    = 1;
    internal const int NI_Shelter   = 2;
    internal const int NI_Belonging = 3;
    internal const int NI_Status    = 4;
    internal const int NI_Purpose   = 5;
    internal const int NI_Spiritual = 6;
    internal const int NeedCount    = 7;

    /// <summary>
    /// [GoalType, ActionType] → affinity weight (0.0 = no contribution, 1.0 = strong match).
    /// </summary>
    public readonly float[,] GoalAffinity = new float[GoalCount, ActionCount];

    /// <summary>
    /// [ActionType, NeedIndex] → coefficient: score contribution = (1 - need) * coefficient.
    /// </summary>
    public readonly float[,] ActionNeedsCoeff = new float[ActionCount, NeedCount];

    /// <summary>
    /// [ActionType] → fallback base score when no needs apply to this action.
    /// Replaces the _ => 0.1f case in the original switch.
    /// </summary>
    public readonly float[] ActionNeedsDefault = new float[ActionCount];

    public UtilityAffinityTables(UtilityAffinityConfig cfg)
    {
        BakeGoalAffinity(cfg.GoalAffinity);
        BakeActionNeeds(cfg.ActionNeeds);
    }

    private void BakeGoalAffinity(Dictionary<string, Dictionary<string, float>> raw)
    {
        foreach (var (goalName, actionMap) in raw)
        {
            if (!TryParseGoal(goalName, out int gi)) continue;
            foreach (var (actionName, weight) in actionMap)
            {
                if (!TryParseAction(actionName, out int ai)) continue;
                GoalAffinity[gi, ai] = weight;
            }
        }
    }

    private void BakeActionNeeds(Dictionary<string, Dictionary<string, float>> raw)
    {
        // Default fallback for any action not listed in the table = 0.1 (same as original _ => 0.1f)
        for (int ai = 0; ai < ActionCount; ai++)
            ActionNeedsDefault[ai] = 0.1f;

        foreach (var (actionName, needMap) in raw)
        {
            if (!TryParseAction(actionName, out int ai)) continue;
            foreach (var (needName, coeff) in needMap)
            {
                if (needName == "_default")
                {
                    ActionNeedsDefault[ai] = coeff;
                    continue;
                }
                if (!TryParseNeed(needName, out int ni)) continue;
                ActionNeedsCoeff[ai, ni] = coeff;
            }
        }
    }

    // ─── Enum parsers ─────────────────────────────────────────────────────────

    private static bool TryParseGoal(string name, out int index)
    {
        // Map TOML snake_case names to GoalType enum values.
        // Enum.TryParse compares by member name (PascalCase), not snake_case, so we use a manual switch.
        index = name.ToLowerInvariant() switch
        {
            "survive"           => (int)GoalType.Survive,
            "security"          => (int)GoalType.Security,
            "acquire"           => (int)GoalType.Acquire,
            "flee"              => (int)GoalType.Flee,
            "endure"            => (int)GoalType.Endure,
            "dominance"         => (int)GoalType.Dominance,
            "alliance"          => (int)GoalType.Alliance,
            "unify"             => (int)GoalType.Unify,
            "bond"              => (int)GoalType.Bond,
            "protect"           => (int)GoalType.Protect,
            "avenge"            => (int)GoalType.Avenge,
            "grieve"            => (int)GoalType.Grieve,
            "create"            => (int)GoalType.Create,
            "found_city"        => (int)GoalType.FoundCity,
            "build_improvement" => (int)GoalType.BuildImprovement,
            "found_religion"    => (int)GoalType.FoundReligion,
            "slay_beast"        => (int)GoalType.SlayBeast,
            "covet_artifact"    => (int)GoalType.CovetArtifact,
            "sea_voyage"        => (int)GoalType.SeaVoyage,
            _                   => -1,
        };
        return index >= 0;
    }

    private static bool TryParseAction(string name, out int index)
    {
        // ActionType is a private enum inside UtilityScorer; we replicate its order here.
        // Order must stay in sync with UtilityScorer.ActionType enum definition.
        index = name.ToLowerInvariant() switch
        {
            "rest"              => 0,
            "travel"            => 1,
            "establish"         => 2,
            "ally"              => 3,
            "negotiate"         => 4,
            "rivalry"           => 5,
            "war"               => 6,
            "raid"              => 7,
            "create"            => 8,
            "flee"              => 9,
            "build_improvement" => 10,
            "found_city"        => 11,
            "hunt_beast"        => 12,
            "sea_voyage"        => 13,
            _                   => -1,
        };
        return index >= 0;
    }

    private static bool TryParseNeed(string name, out int index)
    {
        index = name.ToLowerInvariant() switch
        {
            "food"      => NI_Food,
            "safety"    => NI_Safety,
            "shelter"   => NI_Shelter,
            "belonging" => NI_Belonging,
            "status"    => NI_Status,
            "purpose"   => NI_Purpose,
            "spiritual" => NI_Spiritual,
            _           => -1,
        };
        return index >= 0;
    }
}
