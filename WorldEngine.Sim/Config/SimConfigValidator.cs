namespace WorldEngine.Sim.Config;

/// <summary>
/// Validates a loaded SimConfig for range, ordering, and cross-field invariants.
/// Called automatically by SimConfigLoader after deserialization.
/// Throws <see cref="SimConfigValidationException"/> listing every violation found.
/// </summary>
public static class SimConfigValidator
{
    /// <summary>
    /// Validate the config. Throws <see cref="SimConfigValidationException"/> if any check fails.
    /// </summary>
    public static void Validate(SimConfig cfg)
    {
        var errors = new List<string>();

        ValidateCharacter(cfg.Character, errors);
        ValidateResourcePressure(cfg.ResourcePressure, errors);
        ValidateDisasters(cfg.Disasters, errors);
        ValidateSettlement(cfg.Settlement, errors);
        ValidateReligion(cfg.Religion, errors);
        ValidateWar(cfg.War, errors);
        ValidateArtifacts(cfg.Artifacts, errors);

        if (errors.Count > 0)
            throw new SimConfigValidationException(errors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [character]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateCharacter(CharacterSimConfig c, List<string> errors)
    {
        // Utility weights must sum to 1.0
        float weightSum = c.NeedsWeight + c.GoalsWeight + c.PersonalityWeight;
        if (MathF.Abs(weightSum - 1.0f) > 0.001f)
            errors.Add($"[character] needs_weight + goals_weight + personality_weight must sum to 1.0 (got {weightSum:F4})");

        // Probability checks
        CheckProbability("character.needs_decay_food", c.NeedsDecayFood, errors);
        CheckProbability("character.needs_decay_safety", c.NeedsDecaySafety, errors);
        CheckProbability("character.needs_decay_shelter", c.NeedsDecayShelter, errors);
        CheckProbability("character.needs_decay_belonging", c.NeedsDecayBelonging, errors);
        CheckProbability("character.needs_decay_status", c.NeedsDecayStatus, errors);
        CheckProbability("character.needs_decay_purpose", c.NeedsDecayPurpose, errors);
        CheckProbability("character.needs_decay_spiritual", c.NeedsDecaySpiritual, errors);
        CheckProbability("character.civ_birth_chance_per_season", c.CivBirthChancePerSeason, errors);
        CheckProbability("character.civ_floor_spawn_chance", c.CivFloorSpawnChance, errors);
        CheckProbability("character.beast_encounter_chance", c.BeastEncounterChance, errors);
        CheckProbability("character.character_disease_exposure_chance", c.CharacterDiseaseExposureChance, errors);
        CheckProbability("character.character_disease_recovery_chance", c.CharacterDiseaseRecoveryChance, errors);
        CheckProbability("character.alliance_trust_floor", c.AllianceTrustFloor, errors);

        // Min ≤ Max pairs
        CheckMinMax("character.max_age_seasons_min/max",
            c.MaxAgeSeasonsMin, c.MaxAgeSeasonsMax, errors);
        CheckMinMax("character.tier2_max_age_seasons_min/max",
            c.Tier2MaxAgeSeasonsMin, c.Tier2MaxAgeSeasonsMax, errors);
        // raid_damage_min/max moved to WarConfig (D5) — validated in ValidateWar below
        CheckMinMax("character.softmax_temp_min/max",
            c.SoftmaxTempMin, c.SoftmaxTempMax, errors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [resource_pressure]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateResourcePressure(ResourcePressureConfig rp, List<string> errors)
    {
        // shortage_threshold must be above crisis_threshold
        if (rp.ShortageThreshold <= rp.CrisisThreshold)
            errors.Add($"[resource_pressure] shortage_threshold ({rp.ShortageThreshold}) must be greater than crisis_threshold ({rp.CrisisThreshold})");

        CheckProbability("resource_pressure.shortage_threshold", rp.ShortageThreshold, errors);
        CheckProbability("resource_pressure.crisis_threshold", rp.CrisisThreshold, errors);
        CheckProbability("resource_pressure.acquire_goal_intensity", rp.AcquireGoalIntensity, errors);
        CheckProbability("resource_pressure.flee_goal_intensity", rp.FleeGoalIntensity, errors);
        CheckProbability("resource_pressure.food_moisture_floor", rp.FoodMoistureFloor, errors);
        CheckProbability("resource_pressure.food_moisture_absolute_floor", rp.FoodMoistureAbsoluteFloor, errors);
        CheckProbability("resource_pressure.cold_hardy_food_floor", rp.ColdHardyFoodFloor, errors);
        CheckProbability("resource_pressure.heat_stress_factor", rp.HeatStressFactor, errors);

        // Temperature ordering
        if (rp.OptimalTemperatureLow >= rp.OptimalTemperatureHigh)
            errors.Add($"[resource_pressure] optimal_temperature_low ({rp.OptimalTemperatureLow}) must be less than optimal_temperature_high ({rp.OptimalTemperatureHigh})");
        if (rp.FrostTemperatureThreshold >= rp.OptimalTemperatureLow)
            errors.Add($"[resource_pressure] frost_temperature_threshold ({rp.FrostTemperatureThreshold}) must be less than optimal_temperature_low ({rp.OptimalTemperatureLow})");

        if (rp.PeoplePerTilePeak <= 0f)
            errors.Add($"[resource_pressure] people_per_tile_peak must be > 0 (got {rp.PeoplePerTilePeak})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [disasters]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateDisasters(DisasterConfig d, List<string> errors)
    {
        CheckProbability("disasters.wildfire_ignition_probability_per_tick", d.WildfireIgnitionProbabilityPerTick, errors);
        CheckProbability("disasters.wildfire_spread_probability_per_tick", d.WildfireSpreadProbabilityPerTick, errors);
        CheckProbability("disasters.flood_ignition_probability_per_tick", d.FloodIgnitionProbabilityPerTick, errors);
        CheckProbability("disasters.volcanic_eruption_probability_per_tick", d.VolcanicEruptionProbabilityPerTick, errors);
        CheckProbability("disasters.earthquake_probability_per_tick", d.EarthquakeProbabilityPerTick, errors);
        CheckProbability("disasters.drought_probability_per_year", d.DroughtProbabilityPerYear, errors);

        if (d.DroughtMinSeasons > d.DroughtMaxSeasons)
            errors.Add($"[disasters] drought_min_seasons ({d.DroughtMinSeasons}) must be ≤ drought_max_seasons ({d.DroughtMaxSeasons})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [settlement]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateSettlement(SettlementConfig s, List<string> errors)
    {
        CheckProbability("settlement.pop_growth_rate", s.PopGrowthRate, errors);
        CheckProbability("settlement.pop_decay_rate", s.PopDecayRate, errors);
        CheckProbability("settlement.disease_base_chance", s.DiseaseBaseChance, errors);
        CheckProbability("settlement.disease_spread_chance", s.DiseaseSpreadChance, errors);
        CheckProbability("settlement.disease_recovery_chance", s.DiseaseRecoveryChance, errors);
        CheckProbability("settlement.disease_mortality_per_year", s.DiseaseMortalityPerYear, errors);
        CheckProbability("settlement.wildlife_attack_base_chance", s.WildlifeAttackBaseChance, errors);
        CheckProbability("settlement.emigration_threshold", s.EmigrationThreshold, errors);
        CheckProbability("settlement.emigration_bonus_chance", s.EmigrationBonusChance, errors);
        CheckProbability("settlement.fertility_variance", s.FertilityVariance, errors);

        if (s.PopMinViable >= s.PopMax)
            errors.Add($"[settlement] pop_min_viable ({s.PopMinViable}) must be less than pop_max ({s.PopMax})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [religion]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateReligion(ReligionConfig r, List<string> errors)
    {
        CheckProbability("religion.spiritual_founding_threshold", r.SpiritualFoundingThreshold, errors);
        CheckProbability("religion.piety_founding_threshold", r.PietyFoundingThreshold, errors);
        CheckProbability("religion.wonder_founding_threshold", r.WonderFoundingThreshold, errors);
        CheckProbability("religion.religion_founding_progress_per_year", r.ReligionFoundingProgressPerYear, errors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [war]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateWar(WarConfig w, List<string> errors)
    {
        CheckProbability("war.campaign_battle_base_strength", w.CampaignBattleBaseStrength, errors);
        CheckProbability("war.war_aggression_threshold", w.WarAggressionThreshold, errors);
        CheckProbability("war.opportunistic_war_aggression_threshold", w.OpportunisticWarAggressionThreshold, errors);
        CheckProbability("war.weak_neighbor_settlement_fraction", w.WeakNeighborSettlementFraction, errors);

        if (w.TilesPerBattleWin < 0)
            errors.Add($"[war] tiles_per_battle_win must be ≥ 0 (got {w.TilesPerBattleWin})");
        if (w.MaxTilesTransferredPerWar < w.TilesPerBattleWin)
            errors.Add($"[war] max_tiles_transferred_per_war ({w.MaxTilesTransferredPerWar}) must be ≥ tiles_per_battle_win ({w.TilesPerBattleWin})");
        // D5: raid damage min/max validation (consolidated from [character])
        CheckMinMax("war.raid_damage_min/max", w.RaidDamageMin, w.RaidDamageMax, errors);
        if (w.MaxWarDurationYears < 1)
            errors.Add($"[war] max_war_duration_years must be ≥ 1 (got {w.MaxWarDurationYears})");
        if (w.PeaceCooldownYears < 0)
            errors.Add($"[war] peace_cooldown_years must be ≥ 0 (got {w.PeaceCooldownYears})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [artifacts]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateArtifacts(ArtifactConfig a, List<string> errors)
    {
        CheckProbability("artifacts.base_generation_probability",    a.BaseGenerationProbability,   errors);
        CheckProbability("artifacts.notable_performance_threshold",  a.NotablePerformanceThreshold, errors);
        CheckProbability("artifacts.covet_threshold",                a.CovetThreshold,              errors);
        CheckProbability("artifacts.battle_forge_probability",       a.BattleForgeProbability,      errors);
        CheckProbability("artifacts.heroic_death_forge_probability", a.HeroicDeathForgeProbability, errors);
        CheckProbability("artifacts.lost_on_death_probability",      a.LostOnDeathProbability,      errors);
        CheckProbability("artifacts.covet_ambition_threshold",       a.CovetAmbitionThreshold,      errors);
        CheckProbability("artifacts.lost_artifact_annual_decay",     a.LostArtifactAnnualDecay,     errors);
        CheckProbability("artifacts.owned_artifact_annual_decay",    a.OwnedArtifactAnnualDecay,    errors);
        if (a.CovetMaxGoals < 1)
            errors.Add($"[artifacts] covet_max_goals must be ≥ 1 (got {a.CovetMaxGoals})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void CheckProbability(string key, float value, List<string> errors)
    {
        if (value < 0f || value > 1f)
            errors.Add($"[{key}] must be in [0, 1] (got {value})");
    }

    private static void CheckMinMax(string key, float min, float max, List<string> errors)
    {
        if (min > max)
            errors.Add($"[{key}] min ({min}) must be ≤ max ({max})");
    }

    private static void CheckMinMax(string key, int min, int max, List<string> errors)
    {
        if (min > max)
            errors.Add($"[{key}] min ({min}) must be ≤ max ({max})");
    }
}

/// <summary>
/// Thrown when SimConfig fails validation. Contains all violation messages.
/// </summary>
public sealed class SimConfigValidationException : InvalidOperationException
{
    public IReadOnlyList<string> Violations { get; }

    public SimConfigValidationException(IReadOnlyList<string> violations)
        : base($"sim_config.toml failed validation with {violations.Count} error(s):\n"
               + string.Join("\n", violations.Select(v => $"  {v}")))
    {
        Violations = violations;
    }
}
