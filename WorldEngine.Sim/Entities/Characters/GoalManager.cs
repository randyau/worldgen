using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

public static class GoalManager
{
    private const int StaleSeasonLimit = 8; // prune goals not progressed for 2 years

    public static void UpdateGoals(Tier1Character c, IWorldStateReadOnly world, long currentTick)
    {
        // 1. Prune: completed or stale
        c.Goals.RemoveAll(g => g.IsComplete
            || (currentTick - g.StaleSince > StaleSeasonLimit && g.Progress < 0.1f));

        // 2. Urgent: unmet need overrides everything
        string? urgent = c.Needs.MostUrgentUnmet();
        if (urgent != null)
        {
            if (!c.Goals.Any(g => g.Type == GoalType.Survive))
                c.Goals.Add(new GoalData { Type = GoalType.Survive, Priority = 1.0f, StaleSince = (int)currentTick });
            return;
        }

        // Remove Survive goal if no longer urgent
        c.Goals.RemoveAll(g => g.Type == GoalType.Survive);

        // 3. Personality-driven goal generation (only if not already pursuing that type)
        bool hasExpansion = c.Goals.Any(g => g.Type == GoalType.Expansion);
        bool hasDominance = c.Goals.Any(g => g.Type == GoalType.Dominance);
        bool hasAlliance  = c.Goals.Any(g => g.Type == GoalType.Alliance);

        if (!hasExpansion && c.Personality.Ambition > 0.55f
            && !world.Settlements.ContainsKey(c.Location))
        {
            c.Goals.Add(new GoalData
            {
                Type      = GoalType.Expansion,
                Priority  = c.Personality.Ambition * 0.8f,
                StaleSince = (int)currentTick
            });
        }

        if (!hasDominance && c.Personality.Aggression > 0.6f)
        {
            var rival = FindNearbyRival(c, world);
            if (rival.HasValue)
                c.Goals.Add(new GoalData
                {
                    Type           = GoalType.Dominance,
                    TargetEntityId = rival,
                    Priority       = c.Personality.Aggression * 0.7f,
                    StaleSince     = (int)currentTick
                });
        }

        if (!hasAlliance && c.Personality.Sociability > 0.5f)
        {
            var potential = FindNearbyNeutral(c, world);
            if (potential.HasValue)
                c.Goals.Add(new GoalData
                {
                    Type           = GoalType.Alliance,
                    TargetEntityId = potential,
                    Priority       = c.Personality.Sociability * 0.6f,
                    StaleSince     = (int)currentTick
                });
        }

        // 4. Recompute priorities: scale with (1 - progress)
        foreach (var g in c.Goals)
            g.Priority = Math.Clamp(g.Priority * (1f - g.Progress), 0.01f, 1.0f);
    }

    private static EntityId? FindNearbyRival(Tier1Character c, IWorldStateReadOnly world)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, 5))
        {
            if (e is Tier1Character other && other.Id != c.Id && other.IsAlive)
            {
                var rel = world.GetRelationship(c.Id, other.Id);
                if (rel?.IsRival ?? false) return other.Id;
            }
        }
        return null;
    }

    private static EntityId? FindNearbyNeutral(Tier1Character c, IWorldStateReadOnly world)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, 4))
        {
            if (e is Tier1Character other && other.Id != c.Id && other.IsAlive)
            {
                var rel = world.GetRelationship(c.Id, other.Id);
                if (rel == null || (!rel.IsAlly && !rel.IsRival && !rel.IsAtWar))
                    return other.Id;
            }
        }
        return null;
    }
}
