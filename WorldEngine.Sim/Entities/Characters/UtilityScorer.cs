using WorldEngine.Sim.Civilizations;
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
        var travelDest = BestAdjacentTile(c, world, cfg);
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

        // EstablishSettlement — fertility OR valuable deposits required; route position is a bonus.
        // Same-civ hinterland overlap reduces effective fertility so the tile looks unattractive.
        // Per-civ founding cooldown prevents two settlements crystallising in the same year.
        bool alreadyHasSettlement = isFounder;
        if (!alreadyHasSettlement && !world.Settlements.ContainsKey(c.Location)
            && !InCivFoundingCooldown(c, world, cfg))
        {
            var tileFert = world.GetTile(c.Location);
            float hinterlandFactor = HinterlandFactor(c.Location, c.Identity.CivId, world, cfg);
            float effectiveFertility = tileFert.Fertility * hinterlandFactor;
            float depositVal  = ComputeDepositValue(c.Location, world);
            // Hard ruin cooldown: a recently destroyed site is too dangerous to settle,
            // regardless of how valuable the deposits are.
            bool inRuinCooldown = world.Ruins.TryGetValue(c.Location, out var ruin)
                && world.CurrentYear - ruin.DestroyedYear < cfg.RuinCooldownYears;
            // Deposit override: a rich deposit is worth settling even if hinterland drains fertility.
            // BUT: tiles with zero base fertility cannot sustain a population at all — no food
            // production means growthF=0 and the settlement bleeds to death over decades.
            // Until food import mechanics exist, block founding on truly barren tiles.
            bool  worthSettle = !inRuinCooldown
                              && tileFert.Fertility > 0
                              && tileFert.BaseMoisture >= cfg.MinBaseMoistureToSettle
                              && (effectiveFertility >= cfg.MinFertilityToSettle
                                  || depositVal > cfg.DepositSettleThreshold);
            if (worthSettle)
            {
                float routeBonus = ComputeRouteBonus(c.Location, world);
                float ruinPenalty = RuinFoundingPenalty(c.Location, world, cfg);
                float successProb = (c.Skills.Leadership + c.Aptitude.Diligence) * 0.5f
                                  * (1f + depositVal * cfg.DepositScoreMultiplier
                                       + routeBonus  * cfg.RouteScoreMultiplier
                                       - ruinPenalty);
                actions.Add(new(new EstablishSettlement(c.Id, c.Location),
                    Score(c, ActionType.Establish, successProb, world, cfg)));
            }
        }

        // AllyWith / Negotiate — pick the single best social target per tick.
        // Adding one candidate per non-allied char floods the softmax and drowns out travel/action.
        ICommand? bestSocialCmd = null;
        float bestSocialScore   = float.MinValue;
        // Alliance cap — max alliances scales with Sociability
        int allianceMax = cfg.AllianceMaxBase + (int)(c.Personality.Sociability * cfg.AllianceMaxPerSociability);
        bool atAllianceCap = world.CountAlliances(c.Id) >= allianceMax;

        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            // Alliances are cross-civ only
            if (c.Identity.CivId.IsValid && other.Identity.CivId.IsValid
                && c.Identity.CivId == other.Identity.CivId) continue;
            var rel = world.GetRelationship(c.Id, other.Id);
            if (rel?.IsAtWar ?? false) continue;
            if (rel?.IsAlly ?? false) continue;

            ICommand? cmd;
            float score;
            if (!atAllianceCap && rel?.Trust >= 0.4f)
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

        // DeclareRivalry — nearby character with substantially low trust (not just one bad encounter).
        // Capped at MaxActiveRivals to prevent the rivalry→war pipeline flooding the relationship graph.
        if (world.CountRivals(c.Id) < cfg.MaxActiveRivals)
        {
            foreach (var e in world.GetEntitiesInRadius(c.Location, cfg.PerceptionRadius))
            {
                if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
                var rel = world.GetRelationship(c.Id, other.Id);
                if ((rel?.Trust ?? 0f) < cfg.RivalryTrustThreshold
                    && !(rel?.IsRival ?? false) && !(rel?.IsAtWar ?? false))
                {
                    actions.Add(new(new DeclareRivalry(c.Id, other.Id),
                        Score(c, ActionType.Rivalry, 1.0f, world, cfg)));
                    break; // one rival declaration per tick is enough
                }
            }
        }

        // DeclareWar — rival with Aggression. Capped at MaxActiveWars to bound the relationship graph.
        if (c.Personality.Aggression > cfg.WarAggressionThreshold
            && world.CountWars(c.Id) < cfg.MaxActiveWars)
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
            var fleeDest = BestAdjacentTile(c, world, cfg); // move toward any better tile
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
        // Rest scores higher when safety, food, OR shelter is depleted — camping is a valid shelter strategy
        ActionType.Rest      => (2f - c.Needs.Safety - c.Needs.Food) * 0.2f + (1f - c.Needs.Shelter) * 0.15f,
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
                (GoalType.Expansion, ActionType.Travel)    => 0.7f,  // strong travel drive — expansion chars must physically leave home
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

    private static TileCoord? BestAdjacentTile(Tier1Character c, IWorldStateReadOnly world, CharacterSimConfig cfg)
    {
        TileCoord? best = null;
        int bestScore = -1;
        int w = world.Config.TileWidth, h = world.Config.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        // Count non-ocean adjacent tiles of current position — penalise "dead-end" tiles
        // (beaches/peninsulas with only 1–2 exits) so characters don't get trapped there.
        int currentExits = 0;
        for (int i = 0; i < 4; i++)
        {
            int ex = ((c.Location.X + dx[i]) % w + w) % w;
            int ey = Math.Clamp(c.Location.Y + dy[i], 0, h - 1);
            if (world.IsLand(new TileCoord(ex, ey))) currentExits++;
        }

        for (int i = 0; i < 4; i++)
        {
            int nx = ((c.Location.X + dx[i]) % w + w) % w;
            int ny = Math.Clamp(c.Location.Y + dy[i], 0, h - 1);
            var coord = new TileCoord(nx, ny);
            if (!world.IsLand(coord)) continue;
            if ((BiomeType)world.GetTile(coord).BiomeType == BiomeType.HighMountain) continue;

            int score = world.GetTile(coord).Fertility;

            // Dead-end penalty: if the candidate has fewer exits than current tile, subtract
            // enough to make open terrain more attractive than a coastal cul-de-sac.
            int candidateExits = 0;
            for (int j = 0; j < 4; j++)
            {
                int ex = ((nx + dx[j]) % w + w) % w;
                int ey = Math.Clamp(ny + dy[j], 0, h - 1);
                if (world.IsLand(new TileCoord(ex, ey))) candidateExits++;
            }
            if (candidateExits < currentExits) score -= 60;

            // Settlement pull — home is attractive, but expansion-goal characters need to leave.
            // An Expansion character sees their own civ's settlements as unattractive (they're
            // trying to get away from home, not orbit it). They still approach foreign settlements
            // for trade/diplomacy. Empty tiles beyond any settlement's hinterland get a bonus
            // so expansion characters actively navigate toward unclaimed land.
            bool isExpandingChar = c.Goals.Any(g => g.Type == GoalType.Expansion);
            if (world.Settlements.TryGetValue(coord, out var s))
            {
                bool isSameCiv = c.Identity.CivId.IsValid && s.CivId == c.Identity.CivId;
                if (isSameCiv && isExpandingChar)
                    score -= cfg.ExpansionHomePenalty; // actively push away from home
                else
                    score += isSameCiv ? 150 : 50;
            }
            else if (isExpandingChar)
            {
                // Bonus for tiles outside any settlement's hinterland — this is where they want to be
                bool inAnyHinterland = false;
                foreach (var (st, stub) in world.Settlements)
                    if (TileDistance(coord, st) <= stub.ReachRadius()) { inAnyHinterland = true; break; }
                if (!inAnyHinterland)
                    score += cfg.ExpansionEmptyTileBonus;
            }

            // When shelter is critically low, prefer terrain that provides natural cover.
            // This makes explorers navigate toward forests and mountains rather than open plains.
            if (c.Needs.Shelter < cfg.ShelterSeekThreshold)
            {
                var candidateTile = world.GetTile(coord);
                score += (int)(BiomeShelterScore((BiomeType)candidateTile.BiomeType)
                             * cfg.ShelterSeekTileBonus
                             * (1f - c.Needs.Shelter)); // bonus scales with how desperate they are
            }

            if (score > bestScore) { bestScore = score; best = coord; }
        }
        return best;
    }

    // ─── Hinterland and founding cooldown ─────────────────────────────────────

    /// <summary>
    /// Returns a factor ∈ [HinterlandDrainFactor, 1.0].
    /// When the candidate tile falls inside an existing same-civ settlement's reach,
    /// the factor is HinterlandDrainFactor — the owning settlement is already extracting
    /// those resources, so the tile looks nearly worthless to a new founder.
    /// Foreign-civ overlaps are intentionally ignored: competing for the same resources
    /// is a conflict driver, not something to suppress.
    /// </summary>
    private static float HinterlandFactor(
        TileCoord candidate, CivId civId, IWorldStateReadOnly world, CharacterSimConfig cfg)
    {
        if (!civId.IsValid) return 1f;
        foreach (var (settleTile, stub) in world.Settlements)
        {
            if (stub.CivId != civId) continue;
            if (TileDistance(candidate, settleTile) <= stub.ReachRadius())
                return cfg.HinterlandDrainFactor;
        }
        return 1f;
    }

    /// <summary>
    /// Returns true when the character's civ is still in its founding cooldown.
    /// The cooldown compresses as civ population grows — a large civ can send settlers
    /// sooner because it has more surplus people to draw from.
    /// Formula: effectiveCooldown = max(Min, Base / (1 + civPop / PopScale))
    /// </summary>
    private static bool InCivFoundingCooldown(
        Tier1Character c, IWorldStateReadOnly world, CharacterSimConfig cfg)
    {
        if (!c.Identity.CivId.IsValid) return false;
        var civ = world.GetCivilization(c.Identity.CivId);
        if (civ is null) return false;

        // Sum population across all settlements belonging to this civ
        int civPop = 0;
        foreach (var stub in world.Settlements.Values)
            if (stub.CivId == c.Identity.CivId) civPop += stub.Population;

        float effective = cfg.BaseFoundingCooldownYears
                        / (1f + civPop / (float)cfg.FoundingCooldownPopScale);
        int cooldown = Math.Max(cfg.MinFoundingCooldownYears, (int)effective);
        return world.CurrentYear - civ.LastSettlementFoundedYear < cooldown;
    }

    // ─── Ruin penalty ─────────────────────────────────────────────────────────

    /// <summary>
    /// Score penalty for settling on a ruined tile. Decays exponentially from RuinFoundingPenalty
    /// toward zero as years pass. High deposits or fertility can still overcome this in scoring.
    /// </summary>
    private static float RuinFoundingPenalty(
        TileCoord coord, IWorldStateReadOnly world, CharacterSimConfig cfg)
    {
        if (!world.Ruins.TryGetValue(coord, out var ruin)) return 0f;
        int yearsAgo = world.CurrentYear - ruin.DestroyedYear;
        if (cfg.RuinDecayHalfLifeYears <= 0) return cfg.RuinFoundingPenalty;
        return cfg.RuinFoundingPenalty * MathF.Exp(-yearsAgo * MathF.Log(2f) / cfg.RuinDecayHalfLifeYears);
    }

    // ─── Founding score helpers ────────────────────────────────────────────────

    /// <summary>
    /// Sum of deposit quality contributions on a tile, normalized to 0–1 range.
    /// A single high-quality surface deposit scores ~1.0.
    /// </summary>
    private static float ComputeDepositValue(TileCoord coord, IWorldStateReadOnly world)
    {
        if (!world.ResourceDeposits.TryGetValue(coord, out var deposits))
            return 0f;
        float total = 0f;
        foreach (var dep in deposits)
            total += dep.Quality / 255f * (1f - dep.Depth / 255f * 0.5f);
        return Math.Min(total, 2f);  // cap; a tile with 3+ rich deposits is still just "very rich"
    }

    /// <summary>
    /// Bonus for being positioned on a trade route between existing settlements.
    /// For each pair of settlements, score = 1/(dist_a × dist_b); sum across all pairs.
    /// Returns 0 when there are fewer than two settlements.
    /// </summary>
    private static float ComputeRouteBonus(TileCoord coord, IWorldStateReadOnly world)
    {
        var settlementsAsList = world.Settlements.Keys.ToList();
        if (settlementsAsList.Count < 2) return 0f;

        float bonus = 0f;
        for (int i = 0; i < settlementsAsList.Count; i++)
        for (int j = i + 1; j < settlementsAsList.Count; j++)
        {
            float dA = TileDistance(coord, settlementsAsList[i]);
            float dB = TileDistance(coord, settlementsAsList[j]);
            if (dA > 0f && dB > 0f)
                bonus += 1f / (dA * dB);
        }
        return Math.Min(bonus, 1f);  // cap so a perfectly central tile doesn't dominate the score
    }

    private static float TileDistance(TileCoord a, TileCoord b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 0–1 score for how much natural shelter a biome provides.
    /// Mirrors BiomeShelterRecovery in NeedsUpdater but as a normalised 0–1 value
    /// so it can be used as a weighted score component in BestAdjacentTile.
    /// </summary>
    private static float BiomeShelterScore(BiomeType biome) => biome switch
    {
        BiomeType.TemperateForest    => 1.0f,
        BiomeType.TropicalRainforest => 1.0f,
        BiomeType.BorealForest       => 0.8f,
        BiomeType.Mountain           => 0.8f,
        BiomeType.Swamp              => 0.6f,
        BiomeType.Grassland          => 0.4f,
        BiomeType.Plains             => 0.3f,
        BiomeType.Savanna            => 0.3f,
        BiomeType.Tundra             => 0.3f,
        BiomeType.Beach              => 0.1f,
        BiomeType.Desert             => 0.1f,
        BiomeType.Volcanic           => 0.1f,
        _                            => 0.2f,
    };
}
