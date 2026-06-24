using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

public static class GoalManager
{
    // Goal types worth logging as narrative events when formed or resolved.
    private static readonly HashSet<GoalType> NotableGoalTypes =
    [
        GoalType.Bond, GoalType.Avenge, GoalType.Create, GoalType.Colonize
    ];

    public static void UpdateGoals(
        Tier1Character c, IWorldStateReadOnly world, long currentTick,
        CharacterSimConfig cfg, List<PendingEvent> pending)
    {
        // 1. Emit GoalResolved for any notable goals that completed since last tick, then prune.
        foreach (var g in c.Goals)
        {
            if (g.IsComplete && NotableGoalTypes.Contains(g.Type))
                pending.Add(MakeGoalEvent(EventType.GoalResolved, c, g));
        }
        // Inner-life goals (Bond, Create) get a much longer stale window than tactical goals.
        long innerLifeLimit = cfg.GoalStaleSeasonLimit * 20L;
        c.Goals.RemoveAll(g => g.IsComplete
            || (g.Type != GoalType.Grieve
                && g.Type != GoalType.Bond
                && g.Type != GoalType.Create
                && currentTick - g.StaleSince > cfg.GoalStaleSeasonLimit
                && g.Progress < 0.1f)
            || ((g.Type == GoalType.Bond || g.Type == GoalType.Create)
                && currentTick - g.StaleSince > innerLifeLimit
                && g.Progress < 0.1f));

        // 2. Critical survival needs block all goal formation (truly desperate — near zero).
        // Non-critical unmet needs (Status, Purpose, Spiritual) don't block inner-life goals;
        // wanting to bond or create while hungry makes narrative sense once survival isn't imminent.
        bool criticallyUnsafe = c.Needs.Safety < 0.1f || c.Needs.Food < 0.1f;
        if (criticallyUnsafe)
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

        // Mild unmet needs still suppress action but don't block goal formation entirely.
        string? urgent = c.Needs.MostUrgentUnmet();

        // 3. Personality-driven goal generation
        bool hasExpansion = c.Goals.Any(g => g.Type == GoalType.Expansion);
        bool hasColonize  = c.Goals.Any(g => g.Type == GoalType.Colonize);
        bool hasDominance = c.Goals.Any(g => g.Type == GoalType.Dominance);
        bool hasAlliance  = c.Goals.Any(g => g.Type == GoalType.Alliance);
        bool hasCreate    = c.Goals.Any(g => g.Type == GoalType.Create);
        // Bond cap scales with Compassion — empathetic characters can hold several deep attachments
        int  bondMax      = cfg.BondMaxBase + (int)(c.Personality.Compassion * cfg.BondMaxPerCompassion);
        int  activeBonds  = c.Goals.Count(g => g.Type == GoalType.Bond);
        bool hasBondRoom  = activeBonds < bondMax;

        // Expansion goal: ambitious non-founders want to build a new settlement.
        // Allowed while inside a home settlement — wanderlust will push them toward open land;
        // EstablishSettlement scoring gates on the actual tile being worthwhile.
        bool isFounder = c.Identity.CivId.IsValid && world.ActiveFounders.Contains(c.Id);
        var myCiv = c.Identity.CivId.IsValid ? world.GetCivilization(c.Identity.CivId) : null;

        // Local expansion: fill the civ's existing territory; capped at MaxSettlementsPerCiv
        bool civAtSettlementCap = myCiv != null && myCiv.SettlementCount >= cfg.MaxSettlementsPerCiv;
        if (!hasExpansion && !isFounder && !civAtSettlementCap && c.Personality.Ambition > cfg.GoalAmbitionThreshold)
        {
            c.Goals.Add(new GoalData
            {
                Type      = GoalType.Expansion,
                Priority  = c.Personality.Ambition * 0.8f,
                StaleSince = (int)currentTick, FormedTick = (int)currentTick
            });
        }

        // Colonization: found a distant outpost; requires higher Ambition; separate cap
        bool civAtColonyCap = myCiv != null && myCiv.ColonyCount >= cfg.MaxColoniesPerCiv;
        if (!hasColonize && !isFounder && !civAtColonyCap
            && c.Personality.Ambition > cfg.ColonizeAmbitionThreshold)
        {
            c.Goals.Add(new GoalData
            {
                Type      = GoalType.Colonize,
                Priority  = c.Personality.Ambition * 0.9f,
                StaleSince = (int)currentTick, FormedTick = (int)currentTick
            });
        }

        if (!hasDominance && c.Personality.Aggression > cfg.GoalAggressionThreshold)
        {
            var rival = FindNearbyRival(c, world, cfg.RivalSearchRadius);
            if (rival.HasValue)
                c.Goals.Add(new GoalData
                {
                    Type           = GoalType.Dominance,
                    TargetEntityId = rival,
                    Priority       = c.Personality.Aggression * 0.7f,
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
                });
        }

        if (!hasAlliance && c.Personality.Sociability > cfg.GoalSociabilityThreshold)
        {
            var potential = FindNearbyNeutral(c, world, cfg.AllianceSearchRadius);
            if (potential.HasValue)
                c.Goals.Add(new GoalData
                {
                    Type           = GoalType.Alliance,
                    TargetEntityId = potential,
                    Priority       = c.Personality.Sociability * 0.6f,
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
                });
        }

        // Bond goal: compassionate characters form attachments to high-trust co-located chars.
        // Each bond must target a different person — check existing bonds don't already cover this companion.
        if (hasBondRoom && c.Personality.Compassion > cfg.GoalCompassionThreshold)
        {
            var alreadyBonded = c.Goals
                .Where(g => g.Type == GoalType.Bond && g.TargetEntityId.HasValue)
                .Select(g => g.TargetEntityId!.Value)
                .ToHashSet();
            var companion = FindHighTrustCompanion(c, world, cfg.BondSearchRadius, cfg.BondTrustThreshold, alreadyBonded);
            if (companion.HasValue)
            {
                var bondGoal = new GoalData
                {
                    Type           = GoalType.Bond,
                    Object         = GoalObject.Person,
                    TargetEntityId = companion,
                    Priority       = c.Personality.Compassion * 0.7f,
                    Intensity      = c.Personality.Compassion,
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
                };
                c.Goals.Add(bondGoal);
                pending.Add(MakeGoalEvent(EventType.GoalFormed, c, bondGoal));
            }
        }

        // Create goal: high-Ingenuity characters want to make things.
        // Cooldown prevents immediate re-formation after completing a project.
        bool createCooldownClear = currentTick - c.LastCreateCompletedTick > cfg.CreateGoalCooldownTicks;
        if (!hasCreate && createCooldownClear && c.Aptitude.Ingenuity > cfg.GoalIngenuityThreshold
            && !c.Goals.Any(g => g.Type == GoalType.Grieve))
        {
            var createGoal = new GoalData
            {
                Type      = GoalType.Create,
                Object    = GoalObject.Artwork,
                Priority  = c.Aptitude.Ingenuity * 0.6f,
                Intensity = c.Aptitude.Ingenuity,
                StaleSince = (int)currentTick, FormedTick = (int)currentTick
            };
            c.Goals.Add(createGoal);
            pending.Add(MakeGoalEvent(EventType.GoalFormed, c, createGoal));
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
                GoalType.Create   => g.Progress > 0f ? cfg.WellbeingGoalGainRate * g.Intensity : 0f,
                GoalType.Bond     => g.Progress > 0f ? cfg.WellbeingGoalGainRate * g.Intensity : 0f,
                GoalType.Colonize => g.Progress > 0f ? cfg.WellbeingGoalGainRate * g.Intensity : 0f,
                GoalType.Endure  => -cfg.WellbeingGoalGainRate * cfg.WellbeingEndureMultiplier,
                GoalType.Survive => -cfg.WellbeingGoalGainRate * cfg.WellbeingSurviveMultiplier,
                GoalType.Flee    => -cfg.WellbeingGoalGainRate * cfg.WellbeingFleeMultiplier,
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
        if (c.Needs.Food < cfg.WellbeingHungerThreshold)
            delta -= cfg.WellbeingHungerDrain * (cfg.WellbeingHungerThreshold - c.Needs.Food) / cfg.WellbeingHungerThreshold;

        // Mean reversion toward 0
        delta -= c.Wellbeing * cfg.WellbeingMeanReversionRate;

        c.Wellbeing = Math.Clamp(c.Wellbeing + delta, -1f, 1f);

        // Grief intensity decay (moves toward resolution)
        foreach (var g in c.Goals.Where(g => g.Type == GoalType.Grieve))
        {
            g.Intensity = Math.Max(0f, g.Intensity - cfg.GriefDecayRate);
            if (g.Intensity < cfg.GriefCompletionThreshold)
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
        EntityId deadId, string deadName, WorldState world, CharacterSimConfig cfg,
        List<(EntityId MournerId, float Intensity)> output, List<PendingEvent> pending)
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
            c.Wellbeing = Math.Max(-1f, c.Wellbeing - intensity * cfg.GriefWellbeingShock);

            // High-aggression characters may form Avenge goal if the death wasn't from old age
            if (c.Personality.Aggression > cfg.AvengeAggressionThreshold && intensity > cfg.AvengeIntensityThreshold)
            {
                var avengeGoal = new GoalData
                {
                    Type      = GoalType.Avenge,
                    Object    = GoalObject.Person,
                    TargetEntityId = deadId,
                    Priority  = c.Personality.Aggression * intensity,
                    Intensity = intensity,
                    FormedTick = (int)world.CurrentTick,
                    StaleSince = (int)world.CurrentTick
                };
                c.Goals.Add(avengeGoal);
                pending.Add(MakeGoalEvent(EventType.GoalFormed, c, avengeGoal));
            }

            output.Add((c.Id, intensity));
        }
    }

    private static EntityId? FindNearbyRival(Tier1Character c, IWorldStateReadOnly world, int radius)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, radius))
        {
            if (e is Tier1Character other && other.Id != c.Id && other.IsAlive)
            {
                var rel = world.GetRelationship(c.Id, other.Id);
                if (rel?.IsRival ?? false) return other.Id;
            }
        }
        return null;
    }

    private static EntityId? FindNearbyNeutral(Tier1Character c, IWorldStateReadOnly world, int radius)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, radius))
        {
            if (e is Tier1Character other && other.Id != c.Id && other.IsAlive)
            {
                var rel = world.GetRelationship(c.Id, other.Id);
                if (rel == null || (!rel.IsAlly && !rel.IsRival))
                    return other.Id;
            }
        }
        return null;
    }

    public static void EmitGriefEvent(
        Tier1Character mourner, EntityId deadId, string deadName, List<PendingEvent> pending)
    {
        var bond = mourner.Goals.FirstOrDefault(g => g.Type == GoalType.Bond && g.TargetEntityId == deadId);
        float intensity = bond?.Intensity ?? 0.3f;
        var payload = JsonSerializer.Serialize(new
        {
            characterId   = mourner.Id.Value,
            characterName = mourner.Identity.Name,
            deceasedId    = deadId.Value,
            deceasedName  = deadName,
            intensity,
            wellbeing     = mourner.Wellbeing,
            hasAvenge     = mourner.Goals.Any(g => g.Type == GoalType.Avenge)
        });
        pending.Add(new PendingEvent(EventType.CharacterGrieved, mourner.Location, null, payload,
            new[] { mourner.Id.Value, deadId.Value }));
    }

    private static PendingEvent MakeGoalEvent(EventType type, Tier1Character c, GoalData g)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterId   = c.Id.Value,
            characterName = c.Identity.Name,
            goalType      = g.Type.ToString(),
            goalObject    = g.Object.ToString(),
            targetId      = g.TargetEntityId?.Value,
            intensity     = g.Intensity,
            location      = new[] { c.Location.X, c.Location.Y }
        });
        return new PendingEvent(type, c.Location, null, payload, new[] { c.Id.Value });
    }

    private static EntityId? FindHighTrustCompanion(
        Tier1Character c, IWorldStateReadOnly world,
        int radius, float trustThreshold, HashSet<EntityId>? exclude = null)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, radius))
        {
            if (exclude != null && e is IEntity en && exclude.Contains(en.Id)) continue;

            // Same-civ Tier2 characters co-located in the ruler's settlement are community bonds —
            // no relationship registry needed; shared homeland is enough for the first bond.
            if (e is Tier2Character t2 && t2.IsAlive && t2.Location == c.Location
                && t2.Livelihood.SettlementTile == c.Location)
                return t2.Id;

            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            var rel = world.GetRelationship(c.Id, other.Id);
            if ((rel?.Trust ?? 0f) >= trustThreshold) return other.Id;
        }
        return null;
    }
}
