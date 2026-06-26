using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using ImpType = WorldEngine.Sim.World.ImprovementType;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Scores candidate actions for a Tier 1 character and selects one via softmax.
/// </summary>
public static class UtilityScorer
{
    // Salt for softmax random selection
    private const int SaltSoftmax = 600;

    // ─── Settlement-invalidated caches ────────────────────────────────────────
    // _routeCache keyed by tile coord. Cleared whenever settlement count changes.
    // The sim is single-threaded so static fields are safe.
    private static int _cacheVersion = -1;
    private static readonly Dictionary<TileCoord, float> _routeCache = new();

    private static void SyncCaches(IWorldStateReadOnly world)
    {
        int count = world.Settlements.Count;
        if (count == _cacheVersion) return;
        _cacheVersion = count;
        _routeCache.Clear();
    }

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
        SyncCaches(world);
        var actions = new List<ScoredAction>();

        // Rest — always available
        actions.Add(new(new Rest(c.Id), Score(c, ActionType.Rest, 0f, world, cfg)));

        // Travel — pick best adjacent tile; wanderlust bonus grows with time stationary,
        // dampened by settled role (founder > civ member > free agent) and Curiosity
        var travelDest = BestAdjacentTile(c, world, cfg);
        bool isFounder = c.Identity.CivId.IsValid && world.ActiveFounders.Contains(c.Id);
        if (travelDest.HasValue)
        {
            float wanderlust = Math.Min(1f, (float)c.TicksInCurrentTile / cfg.WanderlustMaxTicks)
                             * cfg.WanderlustBonus
                             * WanderlustMultiplier(c, isFounder, cfg);
            actions.Add(new(new MoveToTile(c.Id, travelDest.Value),
                Score(c, ActionType.Travel, 0.5f, world, cfg) + wanderlust));
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
            if (rel?.IsAlly ?? false) continue;
            // Don't form alliances across enemy civ lines
            if (c.Identity.CivId.IsValid && other.Identity.CivId.IsValid)
            {
                var myCivForAlly = world.GetCivilization(c.Identity.CivId);
                if (myCivForAlly?.IsAtWarWith(other.Identity.CivId) == true) continue;
            }

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
        // Cap scales with Aggression: aggressive characters sustain more rivalries; peaceful ones almost none.
        int rivalMax = cfg.RivalryMaxBase + (int)(c.Personality.Aggression * cfg.RivalryMaxPerAggression);
        if (world.CountRivals(c.Id) < rivalMax)
        {
            foreach (var e in world.GetEntitiesInRadius(c.Location, cfg.PerceptionRadius))
            {
                if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
                var rel = world.GetRelationship(c.Id, other.Id);
                if ((rel?.Trust ?? 0f) < cfg.RivalryTrustThreshold
                    && !(rel?.IsRival ?? false))
                {
                    actions.Add(new(new DeclareRivalry(c.Id, other.Id),
                        Score(c, ActionType.Rivalry, 1.0f, world, cfg)));
                    break; // one rival declaration per tick is enough
                }
            }
        }

        // DeclareWar — civ-level: only the current ruler can start a war.
        // Primary trigger is border tension (rulers know about territorial disputes without needing
        // to meet enemy rulers personally). Personal animosity with any visible enemy is a secondary trigger.
        if (c.Identity.CivId.IsValid)
        {
            var myCiv = world.GetCivilization(c.Identity.CivId);
            bool isRuler = myCiv?.RulerId == c.Id;
            if (isRuler && c.Personality.Aggression > cfg.WarAggressionThreshold
                && myCiv!.WarsAgainst.Count < cfg.MaxActiveWars)
            {
                foreach (var coord in world.GetTilesInRadius(c.Location, cfg.PerceptionRadius))
                {
                    if (!world.Settlements.TryGetValue(coord, out var nearSettle)) continue;
                    if (!nearSettle.CivId.IsValid || nearSettle.CivId == c.Identity.CivId) continue;
                    if (myCiv.IsAtWarWith(nearSettle.CivId)) continue;
                    if (myCiv.InPeaceCooldownWith(nearSettle.CivId, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) continue;
                    var targetCiv = world.GetCivilization(nearSettle.CivId);
                    if (targetCiv == null) continue;

                    // War justified by: personal animosity with any visible enemy character,
                    // OR border tension already elevated (ruler is aware of the territorial dispute)
                    bool hostileEnough = false;
                    foreach (var e2 in world.GetEntitiesInRadius(c.Location, cfg.PerceptionRadius))
                    {
                        if (e2 is not Tier1Character enemy || !enemy.IsAlive || enemy.Id == c.Id) continue;
                        if (enemy.Identity.CivId != nearSettle.CivId) continue;
                        var rel = world.GetRelationship(c.Id, enemy.Id);
                        if ((rel?.IsRival ?? false) || (rel?.Trust ?? 0f) < cfg.RivalryTrustThreshold)
                        {
                            hostileEnough = true;
                            break;
                        }
                    }
                    if (!hostileEnough)
                        hostileEnough = myCiv.BorderTension.GetValueOrDefault(nearSettle.CivId, 0f)
                                      >= cfg.TensionWarThreshold * 0.6f;

                    if (hostileEnough)
                    {
                        actions.Add(new(new DeclareWar(c.Id, nearSettle.CivId),
                            Score(c, ActionType.War, c.Personality.Aggression, world, cfg)));
                        break;
                    }
                }
            }
        }

        // RaidSettlement — only available to characters whose civ is at war with the target civ.
        // Individual characters represent their civ's military effort during wartime.
        bool hasAvengeGoal = c.Goals.Any(g => g.Type == GoalType.Avenge);
        float raidAggressionMin = hasAvengeGoal ? 0.2f : 0.4f;
        if (c.Personality.Aggression > raidAggressionMin && c.Identity.CivId.IsValid)
        {
            var myCivForRaid = world.GetCivilization(c.Identity.CivId);
            if (myCivForRaid != null)
            {
                foreach (var coord in world.GetTilesInRadius(c.Location, cfg.PerceptionRadius))
                {
                    if (!world.Settlements.TryGetValue(coord, out var settlement)) continue;
                    if (!settlement.CivId.IsValid) continue;
                    if (myCivForRaid.IsAtWarWith(settlement.CivId))
                    {
                        float successProb = c.Skills.Combat * c.Aptitude.Diligence;
                        actions.Add(new(new RaidSettlement(c.Id, coord),
                            Score(c, ActionType.Raid, successProb, world, cfg)));
                        break;
                    }
                }
            }
        }

        // CreateArtwork — available whenever a Create goal exists.
        // Wellbeing scales quality/probability but isn't a hard gate; the first act of creation
        // is what bootstraps wellbeing, so we can't require wellbeing to already be high.
        if (c.Goals.Any(g => g.Type == GoalType.Create))
        {
            float artisticProb = c.Aptitude.Ingenuity * (0.3f + Math.Max(0f, c.Wellbeing) * 0.7f);
            actions.Add(new(new CreateArtwork(c.Id),
                Score(c, ActionType.Create, artisticProb, world, cfg)));
        }

        // BuildImprovement — character with BuildImprovement goal on an unimproved territory tile they own.
        // ImprovementType is stored in goal ResourceTag; we advance progress each tick they stay.
        var buildGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.BuildImprovement
                                                  && g.TargetTile.HasValue);
        if (buildGoal != null && c.Identity.CivId.IsValid)
        {
            var targetTile = buildGoal.TargetTile!.Value;
            // Character must be on the target tile, tile must be owned by their civ, no existing improvement
            if (c.Location == targetTile
                && world.TerritoryMap.TryGetValue(targetTile, out var cityTile)
                && !world.ImprovementMap.ContainsKey(targetTile))
            {
                var myCivForBuild = world.GetCivilization(c.Identity.CivId);
                if (myCivForBuild?.CityTerritories.ContainsKey(cityTile) == true)
                {
                    if (Enum.TryParse<ImpType>(buildGoal.ResourceTag, out var impType))
                    {
                        float buildProb = c.Aptitude.Diligence * (0.5f + c.Skills.Administration * 0.5f);
                        actions.Add(new(new BuildImprovement(c.Id, targetTile, impType),
                            Score(c, ActionType.BuildImprovement, buildProb, world, cfg)));
                    }
                }
            }
        }

        // FoundCity — delegated by ruler (see Epic 3.0.5); character travels to frontier, then founds.
        // The EstablishSettlement action already handles the actual founding; this goal type just
        // makes travel to good founding sites score higher.
        var foundCityGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.FoundCity);
        if (foundCityGoal != null && !isFounder && !world.Settlements.ContainsKey(c.Location))
        {
            var tileFert = world.GetTile(c.Location);
            bool hasTerritory = world.TerritoryMap.ContainsKey(c.Location);
            // Score is highest on unowned high-fertility tiles (prime city-founding candidates)
            float foundProb = (tileFert.Fertility / 255f) * c.Aptitude.Diligence
                            * (hasTerritory ? 0.3f : 1.0f); // penalty for already-claimed land
            if (!InCivFoundingCooldown(c, world, cfg) && tileFert.Fertility > 0
                && tileFert.BaseMoisture >= cfg.MinBaseMoistureToSettle)
            {
                actions.Add(new(new EstablishSettlement(c.Id, c.Location),
                    Score(c, ActionType.FoundCity, foundProb, world, cfg)));
            }
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

    private enum ActionType { Rest, Travel, Establish, Ally, Negotiate, Rivalry, War, Raid, Create, Flee, BuildImprovement, FoundCity }

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
        ActionType.Rest             => (2f - c.Needs.Safety - c.Needs.Food) * 0.2f + (1f - c.Needs.Shelter) * 0.15f,
        ActionType.Establish        => (1f - c.Needs.Shelter) * 0.7f + (1f - c.Needs.Status) * 0.3f,
        ActionType.Ally             => (1f - c.Needs.Belonging) * 0.6f + (1f - c.Needs.Safety) * 0.4f,
        ActionType.Negotiate        => (1f - c.Needs.Belonging) * 0.5f,
        ActionType.War              => (1f - c.Needs.Status) * 0.7f,
        ActionType.Raid             => (1f - c.Needs.Status) * 0.5f,
        ActionType.Travel           => (1f - c.Needs.Safety) * 0.3f,
        ActionType.Rivalry          => (1f - c.Needs.Status) * 0.4f,
        ActionType.BuildImprovement => (1f - c.Needs.Purpose) * 0.5f + (1f - c.Needs.Status) * 0.2f,
        ActionType.FoundCity        => (1f - c.Needs.Shelter) * 0.5f + (1f - c.Needs.Status) * 0.5f,
        _                           => 0.1f
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
                // M3 city-state goals
                (GoalType.BuildImprovement, ActionType.BuildImprovement) => 1.0f,
                (GoalType.BuildImprovement, ActionType.Rest)             => 0.3f, // stay put = progress
                (GoalType.FoundCity,        ActionType.FoundCity)        => 1.0f,
                (GoalType.FoundCity,        ActionType.Travel)           => 0.8f, // travel to find good site
                _                                          => 0f
            };
            best = Math.Max(best, match * g.Priority);
        }
        return best;
    }

    private static float PersonalityFit(Tier1Character c, ActionType a) => a switch
    {
        ActionType.Establish        => c.Personality.Ambition,
        ActionType.War              => c.Personality.Aggression,
        ActionType.Raid             => c.Personality.Aggression * 0.8f,
        ActionType.Rivalry          => c.Personality.Aggression * 0.7f,
        ActionType.Ally             => c.Personality.Sociability,
        ActionType.Negotiate        => c.Personality.Sociability * 0.7f + c.Personality.Honesty * 0.3f,
        ActionType.Rest             => c.Personality.Stability,
        ActionType.Travel           => c.Personality.Curiosity,
        ActionType.Create           => c.Aptitude.Ingenuity,
        ActionType.Flee             => (1f - c.Personality.Stability) * 0.8f,
        ActionType.BuildImprovement => c.Aptitude.Diligence,
        ActionType.FoundCity        => c.Personality.Ambition * 0.9f,
        _                           => 0.2f
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

        bool isFoundCityChar = c.Goals.Any(g => g.Type == GoalType.FoundCity);

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

            // Settlement pull — home is attractive, but FoundCity-goal characters need to leave.
            if (world.Settlements.TryGetValue(coord, out var s))
            {
                bool isSameCiv = c.Identity.CivId.IsValid && s.CivId == c.Identity.CivId;
                if (isSameCiv && isFoundCityChar)
                    score -= cfg.ExpansionHomePenalty; // push away to find a founding site
                else
                    score += isSameCiv ? 150 : 50;
            }
            else if (isFoundCityChar)
            {
                // FoundCity delegates seek unowned high-fertility land.
                // Bonus for tiles outside any existing settlement's radius (ColonyMinDistance).
                bool nearAnySettlement = false;
                int fd = cfg.ColonyMinDistance;
                for (int fy = -fd; fy <= fd && !nearAnySettlement; fy++)
                for (int fx = -fd; fx <= fd && !nearAnySettlement; fx++)
                {
                    if (fx * fx + fy * fy > fd * fd) continue;
                    if (world.Settlements.ContainsKey(new TileCoord(coord.X + fx, coord.Y + fy)))
                        nearAnySettlement = true;
                }
                if (!nearAnySettlement)
                    score += cfg.ColonyFrontierBonus;
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

    // ─── Founding cooldown ────────────────────────────────────────────────────

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

        // TotalPopulation is maintained by PopulationDynamicsPhase — O(1) read instead of O(settlements) scan
        float effective = cfg.BaseFoundingCooldownYears
                        / (1f + civ.TotalPopulation / (float)cfg.FoundingCooldownPopScale);
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
    /// For each pair of the K nearest settlements, score = 1/(dist_a × dist_b); sum across pairs.
    /// Capped at the nearest 8 settlements to avoid O(n²) cost as settlement count grows.
    /// Returns 0 when there are fewer than two settlements.
    /// </summary>
    private const int RouteMaxSettlements = 8;
    private static float ComputeRouteBonus(TileCoord coord, IWorldStateReadOnly world)
    {
        if (_routeCache.TryGetValue(coord, out float cached)) return cached;
        if (world.Settlements.Count < 2) { _routeCache[coord] = 0f; return 0f; }

        var nearest = world.Settlements.Keys
            .Select(s => (Coord: s, Dist: TileDistance(coord, s)))
            .Where(x => x.Dist > 0f)
            .OrderBy(x => x.Dist)
            .Take(RouteMaxSettlements)
            .ToList();

        if (nearest.Count < 2) { _routeCache[coord] = 0f; return 0f; }

        float bonus = 0f;
        for (int i = 0; i < nearest.Count; i++)
        for (int j = i + 1; j < nearest.Count; j++)
            bonus += 1f / (nearest[i].Dist * nearest[j].Dist);

        float result = Math.Min(bonus, 1f);
        _routeCache[coord] = result;
        return result;
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
