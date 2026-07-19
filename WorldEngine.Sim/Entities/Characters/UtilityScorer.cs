using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using ImpType = WorldEngine.Sim.World.ImprovementType;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Scores candidate actions for a Tier 1 character and selects one via softmax.
/// Holds pre-baked lookup tables (goal→action affinity, action need-weights) built
/// once at construction from <see cref="UtilityAffinityConfig"/>.
/// </summary>
public sealed class UtilityScorer
{
    // Salt for softmax random selection
    private const int SaltSoftmax = 600;

    // Pre-baked affinity and need-weight tables, indexed by (int)GoalType and (int)ActionType.
    private readonly UtilityAffinityTables _tables;

    // ─── Settlement-invalidated caches ────────────────────────────────────────
    // _routeCache keyed by tile coord. Cleared whenever settlement count changes.
    // The sim is single-threaded so instance fields are safe.
    private int _cacheVersion = -1;
    private readonly Dictionary<TileCoord, float> _routeCache = new();

    public UtilityScorer(SimConfig cfg)
    {
        _tables = new UtilityAffinityTables(cfg.UtilityAffinity);
    }

    private void SyncCaches(IWorldStateReadOnly world)
    {
        int count = world.Settlements.Count;
        if (count == _cacheVersion) return;
        _cacheVersion = count;
        _routeCache.Clear();
    }

    public sealed record ScoredAction(ICommand Command, float Score);

    /// <summary>Score all available actions and return a softmax-weighted selection.</summary>
    public ICommand? SelectAction(
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

    private List<ScoredAction> BuildCandidates(
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
            if (!atAllianceCap && rel?.Trust >= cfg.AllyTrustThreshold)
            {
                float sp = (c.Skills.Diplomacy + c.Personality.Sociability) * 0.5f;
                score = Score(c, ActionType.Ally, sp, world, cfg);
                cmd   = new AllyWith(c.Id, other.Id);
            }
            else if ((rel?.Trust ?? 0f) < cfg.NegotiateMaxTrust)
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
            var wCfg = world.SimConfig.War;  // D5: war knobs consolidated in WarConfig
            if (isRuler && c.Personality.Aggression > wCfg.WarAggressionThreshold
                && myCiv!.WarsAgainst.Count < wCfg.MaxActiveWars)
            {
                foreach (var coord in world.GetTilesInRadius(c.Location, cfg.PerceptionRadius))
                {
                    if (!world.Settlements.TryGetValue(coord, out var nearSettle)) continue;
                    if (!nearSettle.CivId.IsValid || nearSettle.CivId == c.Identity.CivId) continue;
                    if (myCiv.IsAtWarWith(nearSettle.CivId)) continue;
                    if (myCiv.InPeaceCooldownWith(nearSettle.CivId, world.CurrentYear, wCfg.PeaceCooldownYears, wCfg.WarExhaustionYearsPerWar)) continue;
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
                                      >= wCfg.TensionWarThreshold * wCfg.PersonalWarTensionFraction;

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
            // foundProb for FoundCity delegates: use high base (0.8) so founding beats Travel once a
            // valid tile is found. The delegate was explicitly assigned; they should commit when ready,
            // not perpetually seek a better tile. Fertility still modulates within territory (0.4 penalty).
            float foundProb = hasTerritory ? 0.32f : 0.8f;
            // Pre-check: reject if too close to any settlement — avoids emitting an action
            // the resolver will silently discard (GlobalSettlementMinDist enforcement).
            bool tooCloseToSettle = false;
            if (cfg.GlobalSettlementMinDist > 0)
            {
                int gmd = cfg.GlobalSettlementMinDist;
                int gmdSq = gmd * gmd;
                foreach (var s in world.Settlements.Values)
                {
                    int dx = c.Location.X - s.Tile.X, dy = c.Location.Y - s.Tile.Y;
                    if (dx * dx + dy * dy < gmdSq) { tooCloseToSettle = true; break; }
                }
            }

            // FoundCity delegates bypass the founding cooldown — they are explicitly assigned
            // to found a city and the civ has already decided it wants expansion.
            // The civ-level delegation check (RunCityExpansionDecisions) already enforces cooldown
            // at delegation time; double-checking here blocks delegates who were assigned
            // before the last founding and prevents all their work from bearing fruit.
            if (tileFert.Fertility >= cfg.MinFertilityToSettle
                && tileFert.BaseMoisture >= cfg.MinBaseMoistureToSettle
                && !tooCloseToSettle)
            {
                actions.Add(new(new EstablishSettlement(c.Id, c.Location),
                    Score(c, ActionType.FoundCity, foundProb, world, cfg)));
            }
        }

        // HuntBeast — move toward the target legendary beast when SlayBeast goal is active.
        // When on the same tile, CheckBeastEncounters fires naturally; no command needed here.
        var slayGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.SlayBeast && g.TargetEntityId.HasValue);
        if (slayGoal != null)
        {
            var targetBeast = world.GetEntity(slayGoal.TargetEntityId!.Value);
            if (targetBeast != null && targetBeast.IsAlive && targetBeast.Location != c.Location)
            {
                var step = StepToward(c.Location, targetBeast.Location, world);
                if (step.HasValue)
                    actions.Add(new(new MoveToTile(c.Id, step.Value),
                        Score(c, ActionType.HuntBeast, c.Skills.Combat, world, cfg)));
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

    // DECISION: ActionType is a private enum keeping the same integer indices as before.
    // UtilityAffinityTables.TryParseAction maps TOML names to these integer indices directly,
    // so adding a new ActionType requires updating both here and in TryParseAction.
    private enum ActionType { Rest, Travel, Establish, Ally, Negotiate, Rivalry, War, Raid, Create, Flee, BuildImprovement, FoundCity, HuntBeast }

    private float Score(
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

    /// <summary>
    /// Returns the need-satisfaction score for an action, driven by the character's current needs.
    /// Formula: sum of (1 - need_value) * coefficient over all need entries in the config table.
    /// The Rest action is special: its safety+food contribution is expressed as two separate
    /// coefficients on safety and food (equivalent to the original (2 - safety - food) * 0.2f).
    /// Actions not listed in the table return the _default value (0.1f).
    /// </summary>
    private float NeedsSatisfaction(Tier1Character c, ActionType a)
    {
        int ai = (int)a;
        float[] needs =
        {
            c.Needs.Food, c.Needs.Safety, c.Needs.Shelter,
            c.Needs.Belonging, c.Needs.Status, c.Needs.Purpose, c.Needs.Spiritual
        };

        float sum = 0f;
        bool hasAnyCoeff = false;
        for (int ni = 0; ni < UtilityAffinityTables.NeedCount; ni++)
        {
            float coeff = _tables.ActionNeedsCoeff[ai, ni];
            if (coeff == 0f) continue;
            hasAnyCoeff = true;
            sum += (1f - needs[ni]) * coeff;
        }
        return hasAnyCoeff ? sum : _tables.ActionNeedsDefault[ai];
    }

    /// <summary>
    /// Returns the goal-advancement score for an action against the character's active goals.
    /// Uses the pre-baked affinity table indexed by (int)GoalType × (int)ActionType.
    /// Unmapped (goal, action) pairs have weight 0.0 — same as the original _ => 0f case.
    /// The best-matching goal (weight × priority) wins.
    /// </summary>
    private float GoalAdvancement(Tier1Character c, ActionType a)
    {
        if (c.Goals.Count == 0) return 0f;
        int ai = (int)a;
        float best = 0f;
        foreach (var g in c.Goals)
        {
            int gi = (int)g.Type;
            if (gi < 0 || gi >= _tables.GoalAffinity.GetLength(0)) continue;
            float match = _tables.GoalAffinity[gi, ai];
            if (match > 0f)
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
        ActionType.HuntBeast        => c.Personality.Aggression * 0.7f + c.Skills.Combat * 0.3f,
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

    private TileCoord? BestAdjacentTile(Tier1Character c, IWorldStateReadOnly world, CharacterSimConfig cfg)
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

    // ─── Beast hunting ────────────────────────────────────────────────────────

    private static TileCoord? StepToward(TileCoord from, TileCoord to, IWorldStateReadOnly world)
    {
        int w = world.Config.TileWidth, h = world.Config.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        TileCoord? best = null;
        int bestDistSq = int.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            int nx = ((from.X + dx[i]) % w + w) % w;
            int ny = Math.Clamp(from.Y + dy[i], 0, h - 1);
            var cand = new TileCoord(nx, ny);
            if (!world.IsLand(cand)) continue;
            int ddx = cand.X - to.X, ddy = cand.Y - to.Y;
            int distSq = ddx * ddx + ddy * ddy;
            if (distSq < bestDistSq) { bestDistSq = distSq; best = cand; }
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
    private float ComputeRouteBonus(TileCoord coord, IWorldStateReadOnly world)
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
