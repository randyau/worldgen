using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;
using ImpType = WorldEngine.Sim.World.ImprovementType;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>Goal formation, priority, staleness, and resolution for Tier1 characters (~357 lines).</summary>
public static class GoalManager
{
    // Goal types worth logging as narrative events when formed or resolved.
    // Expanded to include major character ambitions: expansion, dominance, alliances, plus inner-life goals.
    private static readonly HashSet<GoalType> NotableGoalTypes =
    [
        GoalType.Bond, GoalType.Avenge, GoalType.Create,
        GoalType.Dominance, GoalType.Alliance,
        GoalType.FoundCity, GoalType.BuildImprovement,
        GoalType.SlayBeast,
        GoalType.CovetArtifact,
        GoalType.SeaVoyage
    ];

    // Salt for covet goal formation RNG — distinct from other decision salts
    private const int SaltCovet = 701;

    public static void UpdateGoals(
        Tier1Character c, IWorldStateReadOnly world, long currentTick,
        CharacterSimConfig cfg, List<PendingEvent> pending)
    {
        // 1. Track notable goals before pruning so we can log completions and abandonments.
        var notableGoalsToCheck = c.Goals.Where(g => NotableGoalTypes.Contains(g.Type)).ToList();

        foreach (var g in notableGoalsToCheck)
        {
            if (g.IsComplete)
                pending.Add(MakeGoalEvent(EventType.GoalResolved, c, g, "completed"));
        }

        // Inner-life goals (Bond, Create) get a much longer stale window than tactical goals.
        long innerLifeLimit = cfg.GoalStaleSeasonLimit * 20L;

        // FoundCity: refresh StaleSince whenever the character is on unclaimed frontier land,
        // signalling active progress. Without this, a delegate wandering the frontier for 40+
        // years times out even while doing exactly what the goal asks.
        var foundCityGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.FoundCity);
        if (foundCityGoal != null && !world.TerritoryMap.ContainsKey(c.Location))
            foundCityGoal.StaleSince = (int)currentTick;

        // BuildImprovement: refresh StaleSince while standing on an owned unimproved tile.
        // Without this the goal abandons after 8 seasons every time the character moves away,
        // then immediately reforms → event spam. innerLifeLimit gives 40 years total.
        var existingBuildGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.BuildImprovement);
        if (existingBuildGoal != null
            && world.TerritoryMap.ContainsKey(c.Location)
            && !world.ImprovementMap.ContainsKey(c.Location))
            existingBuildGoal.StaleSince = (int)currentTick;

        // Alliance: complete if target died, else let staleness accumulate (innerLifeLimit = 40 years).
        var allianceGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.Alliance && g.TargetEntityId.HasValue);
        if (allianceGoal != null)
        {
            var allianceTarget = world.GetEntity(allianceGoal.TargetEntityId!.Value);
            if (allianceTarget == null || !allianceTarget.IsAlive)
                allianceGoal.IsComplete = true;
        }

        // SlayBeast: complete immediately if target died; otherwise let staleness accumulate naturally.
        // Capped by innerLifeLimit (~40 years) — prevents indefinite hunting expeditions.
        var slayGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.SlayBeast);
        if (slayGoal != null)
        {
            var targetBeast = slayGoal.TargetEntityId.HasValue
                ? world.GetEntity(slayGoal.TargetEntityId.Value) : null;
            if (targetBeast == null || !targetBeast.IsAlive)
                slayGoal.IsComplete = true;
            // StaleSince not refreshed — goal expires via innerLifeLimit if the beast eludes the hunter
        }

        // CovetArtifact: complete immediately if the character already owns the artifact
        // (it may have been transferred to them via another path, e.g. W1 death inheritance).
        // Also complete if the artifact was destroyed.
        foreach (var covetGoal in c.Goals.Where(g => g.Type == GoalType.CovetArtifact))
        {
            if (!world.Artifacts.TryGetValue(covetGoal.CovetedArtifactId, out var covetedArtifact))
            {
                covetGoal.IsComplete = true; // artifact removed from registry — complete
                continue;
            }
            if (covetedArtifact.IsDestroyed)
            {
                covetGoal.IsComplete = true; // artifact destroyed — goal moot
                continue;
            }
            // Already owns it (transferred via another route)
            if (covetedArtifact.Owner.Kind == ArtifactOwnerKind.Character
                && covetedArtifact.Owner.CharacterId == c.Id.Value)
                covetGoal.IsComplete = true;
        }

        var goalsToRemove = c.Goals.Where(g => g.IsComplete
            || (g.Type != GoalType.Grieve
                && g.Type != GoalType.Bond
                && g.Type != GoalType.Create
                && g.Type != GoalType.FoundCity        // innerLifeLimit — travel takes years
                && g.Type != GoalType.SlayBeast        // innerLifeLimit — hunts can take years
                && g.Type != GoalType.BuildImprovement // innerLifeLimit — building takes years
                && g.Type != GoalType.Alliance         // innerLifeLimit — diplomacy takes years
                && g.Type != GoalType.CovetArtifact   // innerLifeLimit — acquisition takes years
                && g.Type != GoalType.SeaVoyage        // innerLifeLimit — a voyage can take more ticks than GoalStaleSeasonLimit
                && currentTick - g.StaleSince > cfg.GoalStaleSeasonLimit
                && g.Progress < 0.1f)
            || ((g.Type == GoalType.Bond || g.Type == GoalType.Create || g.Type == GoalType.FoundCity
                 || g.Type == GoalType.SlayBeast
                 || g.Type == GoalType.BuildImprovement || g.Type == GoalType.Alliance
                 || g.Type == GoalType.CovetArtifact || g.Type == GoalType.SeaVoyage)
                && currentTick - g.StaleSince > innerLifeLimit
                && g.Progress < 0.1f)).ToList();

        // Log abandonments for notable goal types (failed to progress, timed out, or pruned without completion).
        foreach (var g in goalsToRemove)
        {
            if (!g.IsComplete && NotableGoalTypes.Contains(g.Type) && g.Progress < 0.95f)
                pending.Add(MakeGoalEvent(EventType.GoalResolved, c, g, "abandoned"));
        }

        c.Goals.RemoveAll(goalsToRemove.Contains);

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
        bool hasDominance = c.Goals.Any(g => g.Type == GoalType.Dominance);
        bool hasAlliance  = c.Goals.Any(g => g.Type == GoalType.Alliance);
        bool hasCreate    = c.Goals.Any(g => g.Type == GoalType.Create);
        // Bond cap scales with Compassion — empathetic characters can hold several deep attachments
        int  bondMax      = cfg.BondMaxBase + (int)(c.Personality.Compassion * cfg.BondMaxPerCompassion);
        int  activeBonds  = c.Goals.Count(g => g.Type == GoalType.Bond);
        bool hasBondRoom  = activeBonds < bondMax;

        bool isFounder = c.Identity.CivId.IsValid && world.ActiveFounders.Contains(c.Id);
        var myCiv = c.Identity.CivId.IsValid ? world.GetCivilization(c.Identity.CivId) : null;

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

        // BuildImprovement goal: hard-working civ members on unimproved territory tiles claim one to build on.
        // Runs only when character is actually standing on such a tile (saves evaluating every civ member every tick).
        bool hasBuildGoal = c.Goals.Any(g => g.Type == GoalType.BuildImprovement);
        if (!hasBuildGoal && c.Identity.CivId.IsValid && c.Aptitude.Diligence > cfg.GoalDiligenceThreshold)
        {
            if (world.TerritoryMap.TryGetValue(c.Location, out var cityTile)
                && !world.ImprovementMap.ContainsKey(c.Location))
            {
                var myCivForBuild = world.GetCivilization(c.Identity.CivId);
                // Only on tiles this character's civ actually owns
                if (myCivForBuild?.CityTerritories.ContainsKey(cityTile) == true)
                {
                    var biome = (BiomeType)world.GetTile(c.Location).BiomeType;
                    ImpType? impType = biome switch
                    {
                        BiomeType.Grassland or BiomeType.Plains or BiomeType.Savanna
                            => ImpType.Farm,
                        BiomeType.TemperateForest or BiomeType.BorealForest
                            => ImpType.LoggingCamp,
                        BiomeType.TropicalRainforest
                            => ImpType.LoggingCamp,
                        BiomeType.Mountain or BiomeType.Volcanic
                            => ImpType.Mine,
                        BiomeType.Beach or BiomeType.Swamp
                            => ImpType.Fishery,
                        _ => null
                    };
                    if (impType.HasValue)
                    {
                        var buildGoal = new GoalData
                        {
                            Type        = GoalType.BuildImprovement,
                            Object      = GoalObject.Material,
                            TargetTile  = c.Location,
                            ResourceTag = impType.Value.ToString(),
                            Priority    = c.Aptitude.Diligence * 0.7f,
                            Intensity   = c.Aptitude.Diligence,
                            StaleSince  = (int)currentTick,
                            FormedTick  = (int)currentTick,
                        };
                        c.Goals.Add(buildGoal);
                        pending.Add(MakeGoalEvent(EventType.GoalFormed, c, buildGoal));
                    }
                }
            }
        }

        // SlayBeast goal: combat-capable, aggressive non-rulers hunt nearby legendary beasts.
        bool hasSlayGoal = c.Goals.Any(g => g.Type == GoalType.SlayBeast);
        bool isRuler     = myCiv?.RulerId == c.Id; // rulers govern settlements, not hunt beasts
        if (!hasSlayGoal
            && !isRuler
            && c.Skills.Combat > cfg.SlayBeastCombatThreshold
            && c.Personality.Aggression > cfg.SlayBeastAggressionThreshold)
        {
            var targetBeast = FindNearbyLegendaryBeast(c, world, cfg.SlayBeastSearchRadius);
            if (targetBeast.HasValue)
            {
                var huntGoal = new GoalData
                {
                    Type           = GoalType.SlayBeast,
                    Object         = GoalObject.Region,
                    TargetEntityId = targetBeast,
                    Priority       = (c.Personality.Aggression + c.Skills.Combat) * 0.5f,
                    Intensity      = c.Personality.Aggression,
                    StaleSince     = (int)currentTick, FormedTick = (int)currentTick
                };
                c.Goals.Add(huntGoal);
                pending.Add(MakeGoalEvent(EventType.GoalFormed, c, huntGoal));
            }
        }

        // CovetArtifact: ambitious characters (high Ambition) evaluate all active artifacts
        // for ones they don't already own that meet the quality threshold.
        // Uses innerLifeLimit staleness — pursuing a specific artifact can take a long time.
        // DECISION: covet goals are capped at cfg.Artifacts.CovetMaxGoals to prevent obsessive
        // multi-artifact coveting that would drown out other goal types.
        var artifactCfg = world.SimConfig.Artifacts;
        int activeCovetCount = c.Goals.Count(g => g.Type == GoalType.CovetArtifact);
        if (activeCovetCount < artifactCfg.CovetMaxGoals
            && c.Personality.Ambition >= artifactCfg.CovetAmbitionThreshold)
        {
            // Collect artifact IDs already being coveted so we don't form duplicates
            var alreadyCoveting = c.Goals
                .Where(g => g.Type == GoalType.CovetArtifact)
                .Select(g => g.CovetedArtifactId)
                .ToHashSet();

            // Collect IDs of artifacts we already own
            // (check directly rather than calling ArtifactRegistry to avoid WorldState downcast)
            var ownedIds = world.Artifacts.Values
                .Where(a => !a.IsDestroyed
                         && a.Owner.Kind == ArtifactOwnerKind.Character
                         && a.Owner.CharacterId == c.Id.Value)
                .Select(a => a.Id)
                .ToHashSet();

            foreach (var artifact in world.Artifacts.Values)
            {
                if (artifact.IsDestroyed) continue;
                if (artifact.Quality < artifactCfg.CovetThreshold) continue;
                if (ownedIds.Contains(artifact.Id)) continue;
                if (alreadyCoveting.Contains(artifact.Id)) continue;

                // Stochastic guard: even ambitious characters don't covet every qualifying artifact.
                // Roll scales with (Ambition - threshold) so higher-ambition chars covet more freely.
                // DECISION: salt combines category ordinal and a quality bucket (0-9) so the roll
                // is fully determined by stable artifact properties — not the globally-assigned ArtifactId
                // (which is a counter and non-reproducible across independent world runs).
                float covetChance = (c.Personality.Ambition - artifactCfg.CovetAmbitionThreshold)
                                  * artifact.Quality;
                int artifactSalt = SaltCovet + (int)artifact.Category * 10 + (int)(artifact.Quality * 9);
                float roll = world.GetRandomFloat(c.Id, artifactSalt);
                if (roll > covetChance) continue;

                float intensity = c.Personality.Ambition * artifact.Quality;
                var covetGoal = new GoalData
                {
                    Type              = GoalType.CovetArtifact,
                    Object            = GoalObject.Artifact,
                    CovetedArtifactId = artifact.Id,
                    Priority          = intensity * 0.8f,
                    Intensity         = intensity,
                    StaleSince        = (int)currentTick,
                    FormedTick        = (int)currentTick,
                };
                c.Goals.Add(covetGoal);
                activeCovetCount++;
                alreadyCoveting.Add(artifact.Id);

                // Emit GoalFormed using the shared payload — TargetId carries the artifact id value
                var covetPayload = JsonSerializer.Serialize(new GoalEventPayload(
                    c.Id.Value, c.Identity.Name,
                    covetGoal.Type.ToString(), covetGoal.Object.ToString(),
                    artifact.Id.Value, intensity, "formed"));
                pending.Add(new PendingEvent(EventType.GoalFormed, c.Location, null, covetPayload,
                    new[] { c.Id.Value },
                    ActorId: c.Id.Value, ActorName: c.Identity.Name));

                // covet→conflict seam: when the coveted artifact is owned by another character
                // or settlement, the covetGoal (with its Priority and Intensity) is already
                // readable by the diplomacy layer via GoalData.Type==CovetArtifact.
                // The UtilityScorer GoalAdvancement method picks up CovetArtifact goals when
                // scoring aggressive actions (DeclareWar, Raid), raising their desirability
                // proportional to the covet goal's Priority. A dedicated rivalry/war cause
                // ("wants artifact owned by <enemy civ>") can be added by querying each ruler's
                // CovetArtifact goals for artifact owners from foreign civs — see the
                // // covet→conflict seam: comment in UtilityScorer for the consumption point.

                if (activeCovetCount >= artifactCfg.CovetMaxGoals) break;
            }
        }

        // 4. Recompute priorities: scale with (1 - progress) × intensity
        foreach (var g in c.Goals)
            g.Priority = Math.Clamp(g.Priority * (1f - g.Progress), 0.01f, 1.0f);
    }

    /// <summary>
    /// WorldState overload of UpdateGoals that additionally handles artifact claims.
    /// When a character with an active CovetArtifact goal is co-located with the coveted
    /// artifact and it is currently Lost, the character claims it immediately.
    /// This overload is preferred by C# over the IWorldStateReadOnly version when a
    /// WorldState is passed (as in CharacterBehaviorPhase).
    /// </summary>
    public static void UpdateGoals(
        Tier1Character c, WorldState world, long currentTick,
        CharacterSimConfig cfg, List<PendingEvent> pending)
    {
        // Run all standard goal logic first (including covet formation above)
        UpdateGoals(c, (IWorldStateReadOnly)world, currentTick, cfg, pending);

        // Artifact claim: if a coveted artifact is Lost and co-located with the character,
        // claim it immediately. Ownership transfer requires WorldState (mutable).
        foreach (var covetGoal in c.Goals.Where(g => g.Type == GoalType.CovetArtifact && !g.IsComplete))
        {
            if (!world.Artifacts.TryGetValue(covetGoal.CovetedArtifactId, out var artifact)) continue;
            if (artifact.IsDestroyed) continue;
            if (artifact.Owner.Kind != ArtifactOwnerKind.Lost) continue;

            // The Lost artifact is considered "at" a tile if it has no active location tracking
            // (Lost artifacts track their last known tile via Settlement ownership history).
            // For claim purposes: the character must be on the same tile as a settlement that
            // previously held the artifact (via SettlementTile), OR the artifact's owner tile
            // matches the character's location (for field-found Lost artifacts without settlement).
            // DECISION: for M5 W2, we treat all Lost artifacts as claimable by any co-located
            // character — the "where is a Lost artifact" question is for W3 inspector. Any
            // character in the same world can claim it when the sim happens to co-locate them.
            // This keeps W2 self-contained without requiring a tile-location index for Lost artifacts.
            // A future pass can refine with explicit Lost artifact tile tracking.

            var newOwner = ArtifactOwner.OfCharacter(c.Id);
            string fromDesc = artifact.Owner.Describe();
            ArtifactRegistry.SetOwner(world, artifact.Id, newOwner);

            var transferPayload = JsonSerializer.Serialize(new ArtifactTransferredPayload(
                artifact.Id.Value, artifact.Name, fromDesc, newOwner.Describe(), "claim"));
            pending.Add(new PendingEvent(EventType.ArtifactTransferred, c.Location, null, transferPayload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name));

            // Complete the covet goal and emit GoalResolved
            covetGoal.IsComplete = true;
            covetGoal.Progress   = 1f;
            var resolvePayload = JsonSerializer.Serialize(new GoalEventPayload(
                c.Id.Value, c.Identity.Name,
                covetGoal.Type.ToString(), covetGoal.Object.ToString(),
                artifact.Id.Value, covetGoal.Intensity, "completed"));
            pending.Add(new PendingEvent(EventType.GoalResolved, c.Location, null, resolvePayload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name));

            break; // claim at most one artifact per tick
        }
    }

    /// <summary>
    /// Updates Wellbeing each tick based on goals, relationships, and resource security.
    /// Call after UpdateGoals in the behavior loop.
    /// </summary>
    public static bool UpdateWellbeing(Tier1Character c, IWorldStateReadOnly world, long currentTick, CharacterSimConfig cfg, out bool crossedFlourishing)
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
                GoalType.FoundCity => g.Progress > 0f ? cfg.WellbeingGoalGainRate * g.Intensity : 0f,
                GoalType.Endure  => -cfg.WellbeingGoalGainRate * cfg.WellbeingEndureMultiplier,
                GoalType.Survive => -cfg.WellbeingGoalGainRate * cfg.WellbeingSurviveMultiplier,
                GoalType.Flee    => -cfg.WellbeingGoalGainRate * cfg.WellbeingFleeMultiplier,
                _                => 0f,
            };
        }

        // Stagnation penalty: goals stuck without progress drain wellbeing slowly
        foreach (var g in c.Goals)
        {
            if (currentTick - g.StaleSince > cfg.StagnationThresholdTicks)
                delta -= cfg.StagnationDrainRate;
        }

        // Purpose drought: characters with no flourishing goals lose wellbeing (existential aimlessness)
        bool hasFlourishing = c.Goals.Any(g => g.Type is GoalType.Create or GoalType.Bond or GoalType.FoundCity or GoalType.Protect);
        if (!hasFlourishing)
            delta -= cfg.PurposeDroughtDrain;

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
        var payload = JsonSerializer.Serialize(new CharacterGriefPayload(
            mourner.Id.Value, mourner.Identity.Name,
            deadId.Value, deadName,
            intensity, mourner.Wellbeing,
            mourner.Goals.Any(g => g.Type == GoalType.Avenge)));
        pending.Add(new PendingEvent(EventType.CharacterGrieved, mourner.Location, null, payload,
            new[] { mourner.Id.Value }, new[] { deadId.Value },
            ActorId: mourner.Id.Value, ActorName: mourner.Identity.Name));
    }

    private static PendingEvent MakeGoalEvent(EventType type, Tier1Character c, GoalData g, string outcome = "formed")
    {
        var payload = JsonSerializer.Serialize(new GoalEventPayload(
            c.Id.Value, c.Identity.Name,
            g.Type.ToString(), g.Object.ToString(),
            g.TargetEntityId?.Value, g.Intensity, outcome));
        return new PendingEvent(type, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name);
    }

    private static EntityId? FindNearbyLegendaryBeast(Tier1Character c, IWorldStateReadOnly world, int radius)
    {
        foreach (var e in world.GetEntitiesInRadius(c.Location, radius))
        {
            if (e is Entities.Beasts.LegendaryBeast beast && beast.IsAlive && beast.IsLegendary)
                return beast.Id;
        }
        return null;
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
