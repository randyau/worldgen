using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

public static class GoalManager
{
    private const int StaleSeasonLimit = 8; // prune goals not progressed for 2 years

    public static void UpdateGoals(Tier1Character c, IWorldStateReadOnly world, long currentTick)
    {
        // 1. Prune: completed or stale (but never prune Grieve — grief doesn't go stale, only decays)
        c.Goals.RemoveAll(g => g.IsComplete
            || (g.Type != GoalType.Grieve
                && currentTick - g.StaleSince > StaleSeasonLimit
                && g.Progress < 0.1f));

        // 2. Urgent: unmet need overrides everything
        string? urgent = c.Needs.MostUrgentUnmet();
        if (urgent != null)
        {
            if (!c.Goals.Any(g => g.Type == GoalType.Survive))
                c.Goals.Add(new GoalData
                {
                    Type = GoalType.Survive, Priority = 1.0f,
                    StaleSince = (int)currentTick, FormedTick = (int)currentTick
                });
            return;
        }
        c.Goals.RemoveAll(g => g.Type == GoalType.Survive);

        // 3. Personality-driven goal generation
        bool hasExpansion = c.Goals.Any(g => g.Type == GoalType.Expansion);
        bool hasDominance = c.Goals.Any(g => g.Type == GoalType.Dominance);
        bool hasAlliance  = c.Goals.Any(g => g.Type == GoalType.Alliance);
        bool hasBond      = c.Goals.Any(g => g.Type == GoalType.Bond);
        bool hasCreate    = c.Goals.Any(g => g.Type == GoalType.Create);

        if (!hasExpansion && c.Personality.Ambition > 0.55f
            && !world.Settlements.ContainsKey(c.Location))
        {
            c.Goals.Add(new GoalData
            {
                Type      = GoalType.Expansion,
                Priority  = c.Personality.Ambition * 0.8f,
                StaleSince = (int)currentTick, FormedTick = (int)currentTick
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
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
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
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
                });
        }

        // Bond goal: compassionate characters form attachments to high-trust co-located chars
        if (!hasBond && c.Personality.Compassion > 0.5f)
        {
            var companion = FindHighTrustCompanion(c, world);
            if (companion.HasValue)
                c.Goals.Add(new GoalData
                {
                    Type           = GoalType.Bond,
                    Object         = GoalObject.Person,
                    TargetEntityId = companion,
                    Priority       = c.Personality.Compassion * 0.7f,
                    Intensity      = c.Personality.Compassion,
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
                });
        }

        // Create goal: high-Ingenuity characters want to make things
        if (!hasCreate && c.Aptitude.Ingenuity > 0.55f && !c.Goals.Any(g => g.Type == GoalType.Grieve))
        {
            c.Goals.Add(new GoalData
            {
                Type      = GoalType.Create,
                Object    = GoalObject.Artwork,
                Priority  = c.Aptitude.Ingenuity * 0.6f,
                Intensity = c.Aptitude.Ingenuity,
                StaleSince = (int)currentTick, FormedTick = (int)currentTick
            });
        }

        // 4. Recompute priorities: scale with (1 - progress) × intensity
        foreach (var g in c.Goals)
            g.Priority = Math.Clamp(g.Priority * (1f - g.Progress), 0.01f, 1.0f);
    }

    /// <summary>
    /// Updates Wellbeing each tick based on goals, relationships, and resource security.
    /// Call after UpdateGoals in the behavior loop.
    /// </summary>
    public static bool UpdateWellbeing(Tier1Character c, IWorldStateReadOnly world, CharacterSimConfig cfg, out bool crossedFlourishing)
    {
        crossedFlourishing = false;
        float prev = c.Wellbeing;
        float delta = 0f;

        // Goal satisfaction / frustration
        foreach (var g in c.Goals)
        {
            delta += g.Type switch
            {
                GoalType.Grieve  => -cfg.GriefDrainRate * g.Intensity,
                GoalType.Create  => g.Progress > 0f ? cfg.WellbeingGoalGainRate * g.Intensity : 0f,
                GoalType.Bond    => g.Progress > 0f ? cfg.WellbeingGoalGainRate * g.Intensity : 0f,
                GoalType.Endure  => -cfg.WellbeingGoalGainRate * 0.5f,
                GoalType.Survive => -cfg.WellbeingGoalGainRate * 0.3f,
                GoalType.Flee    => -cfg.WellbeingGoalGainRate * 0.4f,
                _                => 0f,
            };
        }

        // Co-location with a Bond target is a passive wellbeing gain
        foreach (var g in c.Goals)
        {
            if (g.Type != GoalType.Bond || g.TargetEntityId == null) continue;
            if (world.GetEntity(g.TargetEntityId.Value) is Tier1Character companion
                && companion.IsAlive && companion.Location == c.Location)
                delta += cfg.WellbeingCompanionBoost;
        }

        // Resource security: food shortage drains wellbeing
        if (c.Needs.Food < 0.3f)
            delta -= cfg.WellbeingHungerDrain * (0.3f - c.Needs.Food) / 0.3f;

        // Mean reversion toward 0
        delta -= c.Wellbeing * cfg.WellbeingMeanReversionRate;

        c.Wellbeing = Math.Clamp(c.Wellbeing + delta, -1f, 1f);

        // Grief intensity decay (moves toward resolution)
        foreach (var g in c.Goals.Where(g => g.Type == GoalType.Grieve))
        {
            g.Intensity = Math.Max(0f, g.Intensity - cfg.GriefDecayRate);
            if (g.Intensity < 0.05f)
                g.IsComplete = true;
        }

        // Detect crossing the flourishing threshold upward
        crossedFlourishing = prev < cfg.FlourishingThreshold && c.Wellbeing >= cfg.FlourishingThreshold;
        return c.Wellbeing < cfg.SpiralThreshold; // true = character is spiraling
    }

    /// <summary>
    /// Applies grief to all characters who have a Bond goal targeting the newly-dead.
    /// Returns event payloads to emit.
    /// </summary>
    public static void ApplyGriefToMourners(
        EntityId deadId, string deadName, WorldState world,
        List<(EntityId MournerId, float Intensity)> output)
    {
        foreach (var c in world.Entities.Characters)
        {
            if (!c.IsAlive) continue;
            var bond = c.Goals.FirstOrDefault(g => g.Type == GoalType.Bond && g.TargetEntityId == deadId);
            if (bond == null) continue;

            bond.IsComplete = true; // mark bond resolved

            float intensity = bond.Intensity;
            c.Goals.Add(new GoalData
            {
                Type           = GoalType.Grieve,
                Object         = GoalObject.Person,
                TargetEntityId = deadId,
                Intensity      = intensity,
                Priority       = intensity,
                FormedTick     = (int)world.CurrentTick,
                StaleSince     = (int)world.CurrentTick
            });

            // Immediate wellbeing shock
            c.Wellbeing = Math.Max(-1f, c.Wellbeing - intensity * 0.4f);

            // High-Courage characters may form Avenge goal if the death wasn't from old age
            if (c.Personality.Aggression > 0.6f && intensity > 0.5f)
            {
                c.Goals.Add(new GoalData
                {
                    Type      = GoalType.Avenge,
                    Object    = GoalObject.Person,
                    TargetEntityId = deadId, // points at the deceased — resolved when we raid killer
                    Priority  = c.Personality.Aggression * intensity,
                    Intensity = intensity,
                    FormedTick = (int)world.CurrentTick,
                    StaleSince = (int)world.CurrentTick
                });
            }

            output.Add((c.Id, intensity));
        }
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

    private static EntityId? FindHighTrustCompanion(Tier1Character c, IWorldStateReadOnly world)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, 3))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            var rel = world.GetRelationship(c.Id, other.Id);
            if ((rel?.Trust ?? 0f) >= 0.5f) return other.Id;
        }
        return null;
    }
}
