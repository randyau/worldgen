using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Scores candidate actions for a Tier 1 character and selects one via softmax.
/// </summary>
public static class UtilityScorer
{
    // Salt for softmax random selection
    private const int SaltSoftmax = 600;

    public sealed record ScoredAction(ICommand Command, float Score);

    /// <summary>Score all available actions and return a softmax-weighted selection.</summary>
    public static ICommand? SelectAction(
        Tier1Character c,
        IWorldStateReadOnly world,
        CharacterSimConfig cfg)
    {
        var candidates = BuildCandidates(c, world, cfg);
        if (candidates.Count == 0) return null;

        float temp = cfg.SoftmaxTempMin
            + c.Personality.Curiosity * (cfg.SoftmaxTempMax - cfg.SoftmaxTempMin);

        // Softmax weights
        float[] weights = new float[candidates.Count];
        float max = candidates.Max(a => a.Score); // numerical stability
        for (int i = 0; i < candidates.Count; i++)
            weights[i] = MathF.Exp((candidates[i].Score - max) / temp);

        float total = weights.Sum();
        float roll = world.GetRandomFloat(c.Id, SaltSoftmax) * total;
        float cumulative = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
                return candidates[i].Command;
        }
        return candidates[^1].Command;
    }

    private static List<ScoredAction> BuildCandidates(
        Tier1Character c,
        IWorldStateReadOnly world,
        CharacterSimConfig cfg)
    {
        var actions = new List<ScoredAction>();

        // Rest — always available
        actions.Add(new(new Rest(c.Id), Score(c, ActionType.Rest, 0f, world, cfg)));

        // Travel — pick best adjacent tile; wanderlust bonus grows with time stationary,
        // dampened by settled role (founder > civ member > free agent) and Curiosity
        var travelDest = BestAdjacentTile(c, world);
        bool isFounder = c.Identity.CivId.IsValid
            && world.Settlements.Values.Any(s => s.FounderId == c.Id);
        if (travelDest.HasValue)
        {
            float wanderlust = Math.Min(1f, (float)c.TicksInCurrentTile / cfg.WanderlustMaxTicks)
                             * cfg.WanderlustBonus
                             * WanderlustMultiplier(c, isFounder, cfg);
            actions.Add(new(new MoveToTile(c.Id, travelDest.Value),
                Score(c, ActionType.Travel, 0.5f, world, cfg) + wanderlust));
        }

        // EstablishSettlement — only if tile is fertile, empty, and char has no existing settlement
        bool alreadyHasSettlement = isFounder;
        if (!alreadyHasSettlement
            && !world.Settlements.ContainsKey(c.Location)
            && world.GetTile(c.Location).Fertility >= cfg.MinFertilityToSettle)
        {
            float successProb = (c.Skills.Leadership + c.Aptitude.Diligence) * 0.5f;
            actions.Add(new(new EstablishSettlement(c.Id, c.Location),
                Score(c, ActionType.Establish, successProb, world, cfg)));
        }

        // AllyWith / Negotiate — pick the single best social target per tick.
        // Adding one candidate per non-allied char floods the softmax and drowns out travel/action.
        ICommand? bestSocialCmd = null;
        float bestSocialScore   = float.MinValue;
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            var rel = world.GetRelationship(c.Id, other.Id);
            if (rel?.IsAtWar ?? false) continue;
            if (rel?.IsAlly ?? false) continue;

            ICommand? cmd;
            float score;
            if (rel?.Trust >= 0.4f)
            {
                float sp = (c.Skills.Diplomacy + c.Personality.Sociability) * 0.5f;
                score = Score(c, ActionType.Ally, sp, world, cfg);
                cmd   = new AllyWith(c.Id, other.Id);
            }
            else if ((rel?.Trust ?? 0f) < 0.7f)
            {
                score = Score(c, ActionType.Negotiate, 0.8f, world, cfg);
                cmd   = new Negotiate(c.Id, other.Id);
            }
            else continue;

            if (score > bestSocialScore) { bestSocialScore = score; bestSocialCmd = cmd; }
        }
        if (bestSocialCmd != null)
            actions.Add(new(bestSocialCmd, bestSocialScore));

        // DeclareRivalry — nearby character with low trust
        foreach (var e in world.GetEntitiesInRadius(c.Location, cfg.PerceptionRadius))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            var rel = world.GetRelationship(c.Id, other.Id);
            if ((rel?.Trust ?? 0f) < -0.1f && !(rel?.IsRival ?? false) && !(rel?.IsAtWar ?? false))
            {
                actions.Add(new(new DeclareRivalry(c.Id, other.Id),
                    Score(c, ActionType.Rivalry, 1.0f, world, cfg)));
                break; // one rival declaration per tick is enough
            }
        }

        // DeclareWar — rival with Aggression
        if (c.Personality.Aggression > 0.5f)
        {
            foreach (var e in world.GetEntitiesInRadius(c.Location, cfg.PerceptionRadius))
            {
                if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
                var rel = world.GetRelationship(c.Id, other.Id);
                if ((rel?.IsRival ?? false) && !(rel?.IsAtWar ?? false))
                {
                    actions.Add(new(new DeclareWar(c.Id, other.Id),
                        Score(c, ActionType.War, c.Personality.Aggression, world, cfg)));
                    break;
                }
            }
        }

        // RaidSettlement — nearby enemy settlement; Avenge goal raises chance threshold
        bool hasAvengeGoal = c.Goals.Any(g => g.Type == GoalType.Avenge);
        float raidAggressionMin = hasAvengeGoal ? 0.2f : 0.4f;
        if (c.Personality.Aggression > raidAggressionMin)
        {
            foreach (var coord in world.GetTilesInRadius(c.Location, cfg.PerceptionRadius))
            {
                if (!world.Settlements.TryGetValue(coord, out var settlement)) continue;
                var rel = world.GetRelationship(c.Id, settlement.FounderId);
                if (rel?.IsAtWar ?? false)
                {
                    float successProb = c.Skills.Combat * c.Aptitude.Diligence;
                    actions.Add(new(new RaidSettlement(c.Id, coord),
                        Score(c, ActionType.Raid, successProb, world, cfg)));
                    break;
                }
            }
        }

        // CreateArtwork — available when Wellbeing ≥ 0.3 and has a Create goal
        if (c.Wellbeing >= 0.3f && c.Goals.Any(g => g.Type == GoalType.Create))
        {
            float artisticProb = c.Aptitude.Ingenuity * (0.5f + c.Wellbeing * 0.5f);
            actions.Add(new(new CreateArtwork(c.Id),
                Score(c, ActionType.Create, artisticProb, world, cfg)));
        }

        // FleeRegion — available when character has a Flee goal and Wellbeing < 0
        var fleeGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.Flee);
        if (fleeGoal != null && c.Wellbeing < 0f)
        {
            var fleeDest = BestAdjacentTile(c, world); // move toward any better tile
            if (fleeDest.HasValue)
                actions.Add(new(new FleeRegion(c.Id, fleeDest.Value),
                    Score(c, ActionType.Flee, 1f, world, cfg)));
        }

        return actions;
    }

    private enum ActionType { Rest, Travel, Establish, Ally, Negotiate, Rivalry, War, Raid, Create, Flee }

    private static float Score(
        Tier1Character c,
        ActionType action,
        float successProb,
        IWorldStateReadOnly world,
        CharacterSimConfig cfg)
    {
        float needsSatisfaction   = NeedsSatisfaction(c, action);
        float goalAdvancement     = GoalAdvancement(c, action);
        float personalityFit      = PersonalityFit(c, action);

        float base_ = (needsSatisfaction * cfg.NeedsWeight
                     + goalAdvancement   * cfg.GoalsWeight
                     + personalityFit    * cfg.PersonalityWeight)
                     * Math.Max(0.1f, successProb);

        // Wellbeing modulates social and creative actions
        bool isSocial   = action is ActionType.Ally or ActionType.Negotiate;
        bool isCreative = action is ActionType.Create;
        if (isSocial || isCreative)
        {
            float wb = c.Wellbeing;
            float mod = wb switch
            {
                >= 0.7f => 1.4f,         // Flourishing: more generous and expressive
                >= 0.3f => 1.1f,         // Content: slightly open
                >= -0.3f => 1.0f,        // Neutral: baseline
                >= -0.7f => cfg.DistressedSocialSuppression, // Distressed: withdraws
                _ => cfg.DistressedSocialSuppression * 0.5f  // Spiraling: nearly shut down
            };
            base_ *= mod;
        }
        return base_;
    }

    private static float NeedsSatisfaction(Tier1Character c, ActionType a) => a switch
    {
        ActionType.Rest      => (2f - c.Needs.Safety - c.Needs.Food) * 0.2f,
        ActionType.Establish => (1f - c.Needs.Shelter) * 0.7f + (1f - c.Needs.Status) * 0.3f,
        ActionType.Ally      => (1f - c.Needs.Belonging) * 0.6f + (1f - c.Needs.Safety) * 0.4f,
        ActionType.Negotiate => (1f - c.Needs.Belonging) * 0.5f,
        ActionType.War       => (1f - c.Needs.Status) * 0.7f,
        ActionType.Raid      => (1f - c.Needs.Status) * 0.5f,
        ActionType.Travel    => (1f - c.Needs.Safety) * 0.3f,
        ActionType.Rivalry   => (1f - c.Needs.Status) * 0.4f,
        _                    => 0.1f
    };

    private static float GoalAdvancement(Tier1Character c, ActionType a)
    {
        if (c.Goals.Count == 0) return 0f;
        float best = 0f;
        foreach (var g in c.Goals)
        {
            float match = (g.Type, a) switch
            {
                (GoalType.Survive,   ActionType.Rest)      => 0.8f,
                (GoalType.Survive,   ActionType.Travel)    => 0.4f,
                (GoalType.Expansion, ActionType.Establish) => 1.0f,
                (GoalType.Expansion, ActionType.Travel)    => 0.3f,
                (GoalType.Dominance, ActionType.War)       => 1.0f,
                (GoalType.Dominance, ActionType.Raid)      => 0.8f,
                (GoalType.Alliance,  ActionType.Ally)      => 1.0f,
                (GoalType.Alliance,  ActionType.Negotiate) => 0.5f,
                // New goal types
                (GoalType.Create,    ActionType.Create)    => 1.0f,
                (GoalType.Bond,      ActionType.Ally)      => 1.0f,
                (GoalType.Bond,      ActionType.Negotiate) => 0.4f,
                (GoalType.Avenge,    ActionType.Raid)      => 0.9f,
                (GoalType.Avenge,    ActionType.War)       => 0.8f,
                (GoalType.Acquire,   ActionType.Raid)      => 0.7f,
                (GoalType.Acquire,   ActionType.Travel)    => 0.5f,
                (GoalType.Flee,      ActionType.Flee)      => 1.0f,
                (GoalType.Flee,      ActionType.Travel)    => 0.6f,
                (GoalType.Grieve,    ActionType.Rest)      => 0.7f,  // withdrawn, stays put
                (GoalType.Endure,    ActionType.Rest)      => 0.9f,
                (GoalType.Protect,   ActionType.Travel)    => 0.4f,  // move toward protected
                _                                          => 0f
            };
            best = Math.Max(best, match * g.Priority);
        }
        return best;
    }

    private static float PersonalityFit(Tier1Character c, ActionType a) => a switch
    {
        ActionType.Establish => c.Personality.Ambition,
        ActionType.War       => c.Personality.Aggression,
        ActionType.Raid      => c.Personality.Aggression * 0.8f,
        ActionType.Rivalry   => c.Personality.Aggression * 0.7f,
        ActionType.Ally      => c.Personality.Sociability,
        ActionType.Negotiate => c.Personality.Sociability * 0.7f + c.Personality.Honesty * 0.3f,
        ActionType.Rest      => c.Personality.Stability,
        ActionType.Travel    => c.Personality.Curiosity,
        ActionType.Create    => c.Aptitude.Ingenuity,
        ActionType.Flee      => (1f - c.Personality.Stability) * 0.8f,
        _                    => 0.2f
    };

    /// <summary>
    /// Scales the wanderlust bonus by role and Curiosity.
    /// Founders (rulers/kings) barely wander. Civ members wander occasionally.
    /// Free agents wander freely. Curiosity amplifies all three.
    /// </summary>
    private static float WanderlustMultiplier(
        Tier1Character c, bool isFounder, CharacterSimConfig cfg)
    {
        float roleBase = isFounder               ? cfg.WanderlustFounderMultiplier
                       : c.Identity.CivId.IsValid ? cfg.WanderlustMemberMultiplier
                       : 1.0f;

        // Curiosity scales from CuriosityFloor (low Curiosity) to 1.0 (max Curiosity)
        float curiosityScale = cfg.WanderlustCuriosityFloor
                             + (1f - cfg.WanderlustCuriosityFloor) * c.Personality.Curiosity;

        return roleBase * curiosityScale;
    }

    private static TileCoord? BestAdjacentTile(Tier1Character c, IWorldStateReadOnly world)
    {
        TileCoord? best = null;
        int bestScore = -1;
        int w = world.Config.TileWidth, h = world.Config.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = ((c.Location.X + dx[i]) % w + w) % w;
            int ny = Math.Clamp(c.Location.Y + dy[i], 0, h - 1);
            var coord = new TileCoord(nx, ny);
            if (!world.IsLand(coord)) continue;
            if ((BiomeType)world.GetTile(coord).BiomeType == BiomeType.HighMountain) continue;
            int score = world.GetTile(coord).Fertility;
            if (world.Settlements.ContainsKey(coord)) score += 50;
            if (score > bestScore) { bestScore = score; best = coord; }
        }
        return best;
    }
}
