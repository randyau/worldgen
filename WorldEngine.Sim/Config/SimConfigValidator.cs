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
        ValidateEmissary(cfg.Emissary, errors);
        ValidateTerritory(cfg.Territory, errors);
        ValidateUnrest(cfg.Unrest, errors);
        ValidateCulturalTraits(cfg.CulturalTraits, errors);

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

        if (c.TradeIncomeBonusScale < 0f)
            errors.Add($"[character] trade_income_bonus_scale must be ≥ 0 (got {c.TradeIncomeBonusScale})");
        if (c.TradeIncomeBonusCap < 0f)
            errors.Add($"[character] trade_income_bonus_cap must be ≥ 0 (got {c.TradeIncomeBonusCap})");
        if (c.MerchantMaxDemandWeight < 1f)
            errors.Add($"[character] merchant_max_demand_weight must be ≥ 1 (got {c.MerchantMaxDemandWeight})");
        if (c.MerchantSpecializationBonusScale < 0f)
            errors.Add($"[character] merchant_specialization_bonus_scale must be ≥ 0 (got {c.MerchantSpecializationBonusScale})");
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

        if (rp.NonVitalDemandPerCapita <= 0f)
            errors.Add($"[resource_pressure] non_vital_demand_per_capita must be > 0 (got {rp.NonVitalDemandPerCapita})");
        if (rp.FoodYieldBonusScale < 0f)
            errors.Add($"[resource_pressure] food_yield_bonus_scale must be ≥ 0 (got {rp.FoodYieldBonusScale})");
        if (rp.FoodYieldBonusCap < 0f)
            errors.Add($"[resource_pressure] food_yield_bonus_cap must be ≥ 0 (got {rp.FoodYieldBonusCap})");

        CheckProbability("resource_pressure.specialization_smoothing_alpha", rp.SpecializationSmoothingAlpha, errors);
        if (rp.SpecializationMinRatio < 0f)
            errors.Add($"[resource_pressure] specialization_min_ratio must be ≥ 0 (got {rp.SpecializationMinRatio})");
        if (rp.SpecializationBonusScale < 0f)
            errors.Add($"[resource_pressure] specialization_bonus_scale must be ≥ 0 (got {rp.SpecializationBonusScale})");
        if (rp.SpecializationBonusCap < 0f)
            errors.Add($"[resource_pressure] specialization_bonus_cap must be ≥ 0 (got {rp.SpecializationBonusCap})");
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

        if (s.DiseaseResistanceBonusScale < 0f)
            errors.Add($"[settlement] disease_resistance_bonus_scale must be ≥ 0 (got {s.DiseaseResistanceBonusScale})");
        if (s.DiseaseResistanceBonusCap < 0f)
            errors.Add($"[settlement] disease_resistance_bonus_cap must be ≥ 0 (got {s.DiseaseResistanceBonusCap})");
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

        if (w.MilitaryStrengthBonusScale < 0f)
            errors.Add($"[war] military_strength_bonus_scale must be ≥ 0 (got {w.MilitaryStrengthBonusScale})");
        if (w.MilitaryStrengthBonusCap < 0f)
            errors.Add($"[war] military_strength_bonus_cap must be ≥ 0 (got {w.MilitaryStrengthBonusCap})");
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

        // M9 G-2: category weights must sum to ~1.0
        float battleSum = a.BattleCategoryWeightWeapon + a.BattleCategoryWeightArmor + a.BattleCategoryWeightRegalia;
        if (Math.Abs(battleSum - 1.0f) > 0.001f)
            errors.Add($"[artifacts] battle_category_weight_* must sum to 1.0 (got {battleSum:F4})");
        float heroicSum = a.HeroicDeathCategoryWeightWeapon + a.HeroicDeathCategoryWeightRelic + a.HeroicDeathCategoryWeightRegalia;
        if (Math.Abs(heroicSum - 1.0f) > 0.001f)
            errors.Add($"[artifacts] heroic_death_category_weight_* must sum to 1.0 (got {heroicSum:F4})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [emissary]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateEmissary(EmissaryConfig e, List<string> errors)
    {
        CheckProbability("emissary.rumor_confidence_gain", e.RumorConfidenceGain, errors);
        CheckProbability("emissary.encounter_confidence_gain", e.EncounterConfidenceGain, errors);
        CheckProbability("emissary.confidence_decay_per_year", e.ConfidenceDecayPerYear, errors);
        CheckProbability("emissary.rumor_chain_probability", e.RumorChainProbability, errors);
        CheckProbability("emissary.rumor_chain_confidence_factor", e.RumorChainConfidenceFactor, errors);
        CheckProbability("emissary.emissary_death_per_tile", e.EmissaryDeathPerTile, errors);
        CheckProbability("emissary.emissary_min_survival_chance", e.EmissaryMinSurvivalChance, errors);
        CheckProbability("emissary.trade_trust_gain", e.TradeTrustGain, errors);
        CheckProbability("emissary.spy_confidence_boost", e.SpyConfidenceBoost, errors);
        CheckProbability("emissary.religious_spread_awe_boost", e.ReligiousSpreadAweBoost, errors);

        // Trust fields (unlike the probabilities above) span the Trust scale, [-1, 1].
        CheckTrust("emissary.trade_dispatch_min_trust", e.TradeDispatchMinTrust, errors);
        CheckTrust("emissary.diplomacy_dispatch_min_trust", e.DiplomacyDispatchMinTrust, errors);
        CheckTrust("emissary.spy_dispatch_max_trust", e.SpyDispatchMaxTrust, errors);
        CheckTrust("emissary.diplomacy_alliance_min_trust", e.DiplomacyAllianceMinTrust, errors);

        // DiplomacyDispatchMinTrust is checked before TradeDispatchMinTrust in SelectEmissaryPurpose
        // (CivTracker.Diplomacy.cs) specifically because it must be the higher, more exclusive bar —
        // otherwise Trade's broader range swallows it and Diplomacy emissaries can never dispatch.
        if (e.DiplomacyDispatchMinTrust <= e.TradeDispatchMinTrust)
            errors.Add($"[emissary] diplomacy_dispatch_min_trust ({e.DiplomacyDispatchMinTrust}) must be greater than trade_dispatch_min_trust ({e.TradeDispatchMinTrust}), or Diplomacy emissaries can never be dispatched");

        if (e.KnowledgeSpreadRadius <= 0)
            errors.Add($"[emissary] knowledge_spread_radius must be > 0 (got {e.KnowledgeSpreadRadius})");
        if (e.DispatchCheckYears < 1)
            errors.Add($"[emissary] dispatch_check_years must be ≥ 1 (got {e.DispatchCheckYears})");
        if (e.MaxActiveEmissariesPerCiv < 1)
            errors.Add($"[emissary] max_active_emissaries_per_civ must be ≥ 1 (got {e.MaxActiveEmissariesPerCiv})");
        if (e.EmissaryTravelSpeedTilesPerYear <= 0f)
            errors.Add($"[emissary] emissary_travel_speed_tiles_per_year must be > 0 (got {e.EmissaryTravelSpeedTilesPerYear})");
        if (e.TradeMinPopForGoods < 0)
            errors.Add($"[emissary] trade_min_pop_for_goods must be ≥ 0 (got {e.TradeMinPopForGoods})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [territory]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateTerritory(TerritoryConfig t, List<string> errors)
    {
        if (t.MinCityTiles > t.MaxCityTiles)
            errors.Add($"[territory] min_city_tiles ({t.MinCityTiles}) must be ≤ max_city_tiles ({t.MaxCityTiles})");
        CheckMinMax("territory.min_territory_radius/max_territory_radius", t.MinTerritoryRadius, t.MaxTerritoryRadius, errors);
        if (t.ClaimTilesPerPerson <= 0)
            errors.Add($"[territory] claim_tiles_per_person must be > 0 (got {t.ClaimTilesPerPerson})");
        if (t.PopPerTerritoryRadiusTile <= 0)
            errors.Add($"[territory] pop_per_territory_radius_tile must be > 0 (got {t.PopPerTerritoryRadiusTile})");
        if (t.TerritoryGrowthPerYear < 0)
            errors.Add($"[territory] territory_growth_per_year must be ≥ 0 (got {t.TerritoryGrowthPerYear})");
        if (t.InitialCityClaimRadius < 0)
            errors.Add($"[territory] initial_city_claim_radius must be ≥ 0 (got {t.InitialCityClaimRadius})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [unrest]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateUnrest(UnrestConfig u, List<string> errors)
    {
        CheckProbability("unrest.unrest_decay_rate", u.UnrestDecayRate, errors);
        CheckProbability("unrest.unrest_secession_chance", u.UnrestSecessionChance, errors);
        CheckProbability("unrest.unrest_secession_threshold", u.UnrestSecessionThreshold, errors);
        CheckProbability("unrest.unrest_cluster_min_unrest", u.UnrestClusterMinUnrest, errors);
        CheckProbability("unrest.splinter_initial_tension", u.SplinterInitialTension, errors);

        if (u.UnrestComfortRadius < 0)
            errors.Add($"[unrest] unrest_comfort_radius must be ≥ 0 (got {u.UnrestComfortRadius})");
        if (u.UnrestDistancePerTile < 0f)
            errors.Add($"[unrest] unrest_distance_per_tile must be ≥ 0 (got {u.UnrestDistancePerTile})");
        if (u.UnrestSoftCityThreshold < 0)
            errors.Add($"[unrest] unrest_soft_city_threshold must be ≥ 0 (got {u.UnrestSoftCityThreshold})");
        if (u.UnrestPerExcessCity < 0f)
            errors.Add($"[unrest] unrest_per_excess_city must be ≥ 0 (got {u.UnrestPerExcessCity})");
        if (u.UnrestFamineBonus < 0f)
            errors.Add($"[unrest] unrest_famine_bonus must be ≥ 0 (got {u.UnrestFamineBonus})");
        if (u.UnrestSuccessionMult < 0f)
            errors.Add($"[unrest] unrest_succession_mult must be ≥ 0 (got {u.UnrestSuccessionMult})");
        if (u.UnrestClusterRadius < 0)
            errors.Add($"[unrest] unrest_cluster_radius must be ≥ 0 (got {u.UnrestClusterRadius})");
        if (u.SecessionMinCivPop < 0)
            errors.Add($"[unrest] secession_min_civ_pop must be ≥ 0 (got {u.SecessionMinCivPop})");
        if (u.SecessionPopRampRange <= 0)
            errors.Add($"[unrest] secession_pop_ramp_range must be > 0 (got {u.SecessionPopRampRange})");

        if (u.CohesionBonusScale < 0f)
            errors.Add($"[unrest] cohesion_bonus_scale must be ≥ 0 (got {u.CohesionBonusScale})");
        if (u.CohesionBonusCap < 0f)
            errors.Add($"[unrest] cohesion_bonus_cap must be ≥ 0 (got {u.CohesionBonusCap})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // [cultural_traits]
    // ─────────────────────────────────────────────────────────────────────────

    private static void ValidateCulturalTraits(CulturalTraitsConfig c, List<string> errors)
    {
        if (c.MilitaristicMinWars < 0)
            errors.Add($"[cultural_traits] militaristic_min_wars must be ≥ 0 (got {c.MilitaristicMinWars})");
        if (c.MilitaristicWarsPerDecade < 0f)
            errors.Add($"[cultural_traits] militaristic_wars_per_decade must be ≥ 0 (got {c.MilitaristicWarsPerDecade})");
        if (c.ExpansionistFoundingRate < 0f)
            errors.Add($"[cultural_traits] expansionist_founding_rate must be ≥ 0 (got {c.ExpansionistFoundingRate})");
        if (c.ExpansionistSustainedYears < 0)
            errors.Add($"[cultural_traits] expansionist_sustained_years must be ≥ 0 (got {c.ExpansionistSustainedYears})");
        if (c.WarWearyMinRepeatWars < 0)
            errors.Add($"[cultural_traits] war_weary_min_repeat_wars must be ≥ 0 (got {c.WarWearyMinRepeatWars})");
        if (c.ResilientMinNearCollapseCount < 0)
            errors.Add($"[cultural_traits] resilient_min_near_collapse_count must be ≥ 0 (got {c.ResilientMinNearCollapseCount})");
        if (c.ResilientNearCollapsePopThreshold < 0)
            errors.Add($"[cultural_traits] resilient_near_collapse_pop_threshold must be ≥ 0 (got {c.ResilientNearCollapsePopThreshold})");
        if (c.ScholarlyMinDiscoveries < 0)
            errors.Add($"[cultural_traits] scholarly_min_discoveries must be ≥ 0 (got {c.ScholarlyMinDiscoveries})");
        if (c.UnstableThroneMinSuccessions < 0)
            errors.Add($"[cultural_traits] unstable_throne_min_successions must be ≥ 0 (got {c.UnstableThroneMinSuccessions})");
        if (c.UnstableThroneYears <= 0)
            errors.Add($"[cultural_traits] unstable_throne_years must be > 0 (got {c.UnstableThroneYears})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void CheckProbability(string key, float value, List<string> errors)
    {
        if (value < 0f || value > 1f)
            errors.Add($"[{key}] must be in [0, 1] (got {value})");
    }

    /// <summary>Relationship trust fields span [-1, 1], unlike probability/rate fields.</summary>
    private static void CheckTrust(string key, float value, List<string> errors)
    {
        if (value < -1f || value > 1f)
            errors.Add($"[{key}] must be in [-1, 1] (got {value})");
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
