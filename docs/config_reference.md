# Config Reference
<!-- GENERATED from config/sim_config.toml — edit the source TOML comments, not this file. -->
<!-- Regenerate: python3 scripts/gen-config-ref.py -->

All simulation tuning constants in one place. For balance guidance see `docs/sim_tuning.md`.
Edit values in `sim_config.toml`; all keys live there without recompiling.


## Sections

- [world_gen](#world-gen)
- [world_gen.elevation](#world-genelevation)
- [world_gen.ocean](#world-genocean)
- [world_gen.climate](#world-genclimate)
- [world_gen.resources](#world-genresources)
- [sim_loop](#sim-loop)
- [sim_loop.speed](#sim-loopspeed)
- [events](#events)
- [events.population_impact_thresholds](#eventspopulation-impact-thresholds)
- [events.gate](#eventsgate)
- [environment](#environment)
- [civilization](#civilization)
- [civilization.settler_seeding](#civilizationsettler-seeding)
- [admin_distance](#admin-distance)
- [admin_distance.movement_costs](#admin-distancemovement-costs)
- [admin_distance.anchors.capital](#admin-distanceanchorscapital)
- [admin_distance.anchors.sub_capital](#admin-distanceanchorssub-capital)
- [admin_distance.anchors.tier1_presence](#admin-distanceanchorstier1-presence)
- [admin_distance.anchors.garrison](#admin-distanceanchorsgarrison)
- [admin_distance.anchors.religious_center](#admin-distanceanchorsreligious-center)
- [characters](#characters)
- [characters.needs](#charactersneeds)
- [characters.skills](#charactersskills)
- [characters.aging](#charactersaging)
- [utility](#utility)
- [goals](#goals)
- [specialists](#specialists)
- [artifacts](#artifacts)
- [religion](#religion)
- [cultural_modifiers](#cultural-modifiers)
- [cultural_modifiers.half_life_years](#cultural-modifiershalf-life-years)
- [spatial_buffer](#spatial-buffer)
- [performance](#performance)

## `[world_gen]` {#world-gen}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `default_tile_size_km` | `10` | `SimConfig.WorldGen.DefaultTileSizeKm` | Real-world km per world-scale tile |
| `default_width_km` | `4000` | `SimConfig.WorldGen.DefaultWidthKm` | Default world width (Europe-scale) |
| `default_height_km` | `3000` | `SimConfig.WorldGen.DefaultHeightKm` | Default world height (Europe-scale) |
| `chunk_size` | `16` | `SimConfig.WorldGen.ChunkSize` | Tile grid chunk dimensions (16×16 tiles per chunk) |
| `border_manifest_samples` | `64` | `SimConfig.WorldGen.BorderManifestSamples` | Samples per tile edge in border manifests |
| `magic_intensity_scale` | `1.0` | `SimConfig.WorldGen.MagicIntensityScale` | Multiplier on magic intensity peaks (V2 stub) |

## `[world_gen.elevation]` {#world-genelevation}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `noise_scale` | `0.3` | `SimConfig.WorldGen.Elevation.NoiseScale` | How much noise varies elevation around tectonic baseline |
| `mountain_threshold` | `0.7` | `SimConfig.WorldGen.Elevation.MountainThreshold` | Elevation above which terrain is classified as mountain |
| `tectonic_intensity` | `0.8` | `SimConfig.WorldGen.Elevation.TectonicIntensity` | How dramatic plate collision effects are (0=gentle, 1=extreme) |

## `[world_gen.ocean]` {#world-genocean}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `default_sea_level` | `0.35` | `SimConfig.WorldGen.Ocean.DefaultSeaLevel` | Fraction of elevation range below which is ocean |

## `[world_gen.climate]` {#world-genclimate}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `equator_temperature` | `0.9` | `SimConfig.WorldGen.Climate.EquatorTemperature` | Temperature at equator (0-1 scaled) |
| `pole_temperature` | `0.05` | `SimConfig.WorldGen.Climate.PoleTemperature` | Temperature at poles (0-1 scaled) |
| `elevation_temp_penalty` | `0.4` | `SimConfig.WorldGen.Climate.ElevationTempPenalty` | How much elevation reduces temperature |
| `rain_shadow_strength` | `0.7` | `SimConfig.WorldGen.Climate.RainShadowStrength` | How much mountains reduce precipitation on leeward side |
| `monsoon_threshold` | `0.6` | `SimConfig.WorldGen.Climate.MonsoonThreshold` | Moisture level above which a region has monsoon season |
| `storm_corridor_latitude` | `0.55` | `SimConfig.WorldGen.Climate.StormCorridorLatitude` | Normalized latitude band where storm corridors form |

## `[world_gen.resources]` {#world-genresources}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `iron_density` | `0.08` | `SimConfig.WorldGen.Resources.IronDensity` | Fraction of mountain/hill tiles with iron deposits |
| `copper_density` | `0.04` | `SimConfig.WorldGen.Resources.CopperDensity` | Fraction of volcanic-adjacent tiles with copper |
| `tin_density` | `0.015` | `SimConfig.WorldGen.Resources.TinDensity` | Fraction of eligible tiles with tin (rare by design) |
| `precious_metal_density` | `0.005` | `SimConfig.WorldGen.Resources.PreciousMetalDensity` | Fraction of volcanic tiles with precious metals |
| `rare_resource_density` | `0.003` | `SimConfig.WorldGen.Resources.RareResourceDensity` | Fraction of tiles with rare/magical resources |

## `[sim_loop]` {#sim-loop}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `days_per_season` | `90` | `SimConfig.SimLoop.DaysPerSeason` | Simulation days in one season (affects Daily/Standard sync) |
| `seasons_per_year` | `4` | `SimConfig.SimLoop.SeasonsPerYear` | Always 4: Spring, Summer, Autumn, Winter |

## `[sim_loop.speed]` {#sim-loopspeed}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `slow_ticks_per_second` | `0.5` | `SimConfig.SimLoop.Speed.SlowTicksPerSecond` | Seasonal ticks per real second at Slow speed |
| `normal_ticks_per_second` | `1.0` | `SimConfig.SimLoop.Speed.NormalTicksPerSecond` | Seasonal ticks per real second at Normal speed |
| `fast_ticks_per_second` | `10.0` | `SimConfig.SimLoop.Speed.FastTicksPerSecond` | Seasonal ticks per real second at Fast speed |
| `ultrafast_snapshot_years` | `10` | `SimConfig.SimLoop.Speed.UltrafastSnapshotYears` | Push UI snapshot every N sim-years in Ultrafast mode |

## `[events]` {#events}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `minimum_tier_to_record` | `"Background"` | `SimConfig.Events.MinimumTierToRecord` | Start permissive, tighten empirically |
| `minimum_population_impact` | `"None"` | `SimConfig.Events.MinimumPopulationImpact` | Effectively no population gate initially |
| `cache_size` | `400` | `SimConfig.Events.CacheSize` | Number of recent events kept in memory |
| `retention_years` | `500` | `SimConfig.Events.RetentionYears` | Pruning: events below Regional tier older than this are eligible for pruning Set to 0 to disable pruning (development mode) |
| `first_of_kind_lookback_years` | `50` | `SimConfig.Events.FirstOfKindLookbackYears` | How far back to check when classifying IsFirstOfKind |

## `[events.population_impact_thresholds]` {#eventspopulation-impact-thresholds}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `catastrophic_absolute` | `10000` | `SimConfig.Events.PopulationImpactThresholds.CatastrophicAbsolute` | Absolute population affected |
| `catastrophic_fraction` | `0.25` | `SimConfig.Events.PopulationImpactThresholds.CatastrophicFraction` | Fraction of regional population affected |
| `major_absolute` | `500` | `SimConfig.Events.PopulationImpactThresholds.MajorAbsolute` |  |
| `major_fraction` | `0.05` | `SimConfig.Events.PopulationImpactThresholds.MajorFraction` |  |
| `moderate_absolute` | `50` | `SimConfig.Events.PopulationImpactThresholds.ModerateAbsolute` |  |
| `moderate_fraction` | `0.01` | `SimConfig.Events.PopulationImpactThresholds.ModerateFraction` |  |

## `[events.gate]` {#eventsgate}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `always_record_types` | `CharacterBorn, CharacterDied, CivilizationFounded, CivilizationCollapsed, ArtifactCreated, ArtifactDestroyed, ReligionFounded, ReligionExtinct, WarDeclared, WarEnded, GodModeDisasterTriggered, GodModeEntitySpawned, GodModeCharacterCreated, GodModeArtifactPlaced, GodModeCivilizationForced` | `SimConfig.Events.Gate.AlwaysRecordTypes` | Event types always recorded regardless of other gate settings Add to this list for types that should never be filtered |
| `suppressed_types` | `Tier3ResourceTick, SettlementFoodAccounting, PopulationGrowthIncrement, ArmySupplyConsumption, RelationshipDecayTick` | `SimConfig.Events.Gate.SuppressedTypes` | Event types always suppressed regardless of other gate settings Pure simulation bookkeeping with no narrative value Add to this list empirically as you discover noise categories |
| `suppressed_verb_classes` | `[]` | `SimConfig.Events.Gate.SuppressedVerbClasses` | Verb classes to suppress entirely (empty = suppress nothing) Add verb classes here if an entire category proves to be noise |

## `[environment]` {#environment}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `volcanic_eruption_prob` | `0.0001` | `SimConfig.Environment.VolcanicEruptionProb` | Per volcanic tile per season |
| `earthquake_prob` | `0.0002` | `SimConfig.Environment.EarthquakeProb` | Per fault line tile per season |
| `wildfire_prob` | `0.0005` | `SimConfig.Environment.WildfireProb` | Per forest tile in dry season |
| `flood_prob` | `0.001` | `SimConfig.Environment.FloodProb` | Per river-adjacent tile in wet season |
| `drought_chance_per_year` | `0.02` | `SimConfig.Environment.DroughtChancePerYear` | Per region per year (not per tile) |
| `wildfire_spread_prob` | `0.3` | `SimConfig.Environment.WildfireSpreadProb` | Probability wildfire spreads to adjacent forest tile per tick |
| `flood_spread_radius` | `3` | `SimConfig.Environment.FloodSpreadRadius` | Maximum tiles a flood event spreads from origin |
| `climate_drift_rate` | `0.001` | `SimConfig.Environment.ClimateDriftRate` | How fast regional temperature/moisture drifts per year |
| `biome_change_threshold` | `0.15` | `SimConfig.Environment.BiomeChangeThreshold` | How much climate must drift before biome reclassifies |
| `forest_regrowth_rate` | `0.05` | `SimConfig.Environment.ForestRegrowthRate` | Forests regrow at 5% per year after wildfire |
| `soil_recovery_rate` | `0.08` | `SimConfig.Environment.SoilRecoveryRate` | Soil fertility recovers at 8% per year after drought |
| `fish_recovery_rate` | `0.12` | `SimConfig.Environment.FishRecoveryRate` | Fish populations recover at 12% per year |
| `sea_level_change_rate` | `0.0002` | `SimConfig.Environment.SeaLevelChangeRate` | Maximum sea level change per year (fraction of world height) |
| `sea_level_event_threshold` | `0.01` | `SimConfig.Environment.SeaLevelEventThreshold` | Sea level change required to trigger a SeaLevelChanged event |

## `[civilization]` {#civilization}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `min_viable_population` | `50` | `SimConfig.Civilization.MinViablePopulation` | Below this, settlement is in danger of collapse |
| `collapse_threshold` | `0.15` | `SimConfig.Civilization.CollapseThreshold` | Fraction of peak population — triggers Collapsed state |
| `recovery_threshold` | `0.40` | `SimConfig.Civilization.RecoveryThreshold` | Fraction needed to exit Declining state |
| `first_tier2_threshold` | `100` | `SimConfig.Civilization.FirstTier2Threshold` | Population before first Tier 2 role crystallizes |
| `tier2_per_population` | `200` | `SimConfig.Civilization.Tier2PerPopulation` | Roughly one new Tier 2 role per N additional population |
| `min_tier1_for_civ_label` | `1` | `SimConfig.Civilization.MinTier1ForCivLabel` | Minimum Tier 1 leaders |
| `min_tier2_roles_for_civ` | `3` | `SimConfig.Civilization.MinTier2RolesForCiv` | Minimum distinct Tier 2 roles |
| `min_territory_tiles` | `5` | `SimConfig.Civilization.MinTerritoryTiles` | Minimum controlled tiles |
| `ruins_to_myth_years` | `500` | `SimConfig.Civilization.RuinsToMythYears` | Years before abandoned ruins become Myth state |
| `ruins_proximity_to_active_bonus` | `200` | `SimConfig.Civilization.RuinsProximityToActiveBonus` | Bonus years before Myth if near active civilization |

## `[civilization.settler_seeding]` {#civilizationsettler-seeding}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `global_pop_threshold` | `500` | `SimConfig.Civilization.SettlerSeeding.GlobalPopThreshold` | World population below this triggers spontaneous seeding check |
| `probability_per_century` | `0.15` | `SimConfig.Civilization.SettlerSeeding.ProbabilityPerCentury` | 15% chance per century when below threshold |
| `starting_population` | `20` | `SimConfig.Civilization.SettlerSeeding.StartingPopulation` | Initial settler group size |

## `[admin_distance]` {#admin-distance}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `max_distance_penalty` | `0.6` | `SimConfig.AdminDistance.MaxDistancePenalty` | Maximum loyalty reduction at zero authority |
| `max_cultural_bonus` | `0.2` | `SimConfig.AdminDistance.MaxCulturalBonus` | Loyalty bonus from cultural alignment |
| `max_religion_bonus` | `0.15` | `SimConfig.AdminDistance.MaxReligionBonus` | Loyalty bonus from shared religion |
| `max_personal_bonus` | `0.25` | `SimConfig.AdminDistance.MaxPersonalBonus` | Loyalty bonus from personal relationship with Tier 1 |
| `revolt_threshold` | `0.25` | `SimConfig.AdminDistance.RevoltThreshold` | Loyalty below this enables revolt probability |
| `base_revolt_probability` | `0.02` | `SimConfig.AdminDistance.BaseRevoltProbability` | Base revolt chance per season at threshold (2%) |

## `[admin_distance.movement_costs]` {#admin-distancemovement-costs}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `plains` | `1.0` | `SimConfig.AdminDistance.MovementCosts.Plains` | Base movement cost (seasons to traverse) by biome |
| `grassland` | `1.0` | `SimConfig.AdminDistance.MovementCosts.Grassland` |  |
| `forest` | `2.0` | `SimConfig.AdminDistance.MovementCosts.Forest` |  |
| `dense_forest` | `3.0` | `SimConfig.AdminDistance.MovementCosts.DenseForest` |  |
| `hills` | `2.0` | `SimConfig.AdminDistance.MovementCosts.Hills` |  |
| `mountains` | `5.0` | `SimConfig.AdminDistance.MovementCosts.Mountains` |  |
| `high_mountains` | `10.0` | `SimConfig.AdminDistance.MovementCosts.HighMountains` |  |
| `desert` | `2.5` | `SimConfig.AdminDistance.MovementCosts.Desert` |  |
| `swamp` | `3.0` | `SimConfig.AdminDistance.MovementCosts.Swamp` |  |
| `tundra` | `2.0` | `SimConfig.AdminDistance.MovementCosts.Tundra` |  |
| `road_multiplier` | `0.4` | `SimConfig.AdminDistance.MovementCosts.RoadMultiplier` | Roads reduce movement cost by 60% |
| `river_following` | `0.7` | `SimConfig.AdminDistance.MovementCosts.RiverFollowing` | Following a river reduces cost |
| `river_crossing` | `1.5` | `SimConfig.AdminDistance.MovementCosts.RiverCrossing` | Crossing a river increases cost |
| `winter_mult` | `1.8` | `SimConfig.AdminDistance.MovementCosts.WinterMult` | Winter slows all movement |
| `monsoon_mult` | `1.6` | `SimConfig.AdminDistance.MovementCosts.MonsoonMult` | Monsoon season slows movement |

## `[admin_distance.anchors.capital]` {#admin-distanceanchorscapital}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `core_radius` | `3.0` | `SimConfig.AdminDistance.Anchors.Capital.CoreRadius` | Seasons of travel = full authority |
| `max_radius` | `15.0` | `SimConfig.AdminDistance.Anchors.Capital.MaxRadius` | Seasons of travel = zero authority |
| `decay_rate` | `0.3` | `SimConfig.AdminDistance.Anchors.Capital.DecayRate` |  |
| `strength` | `1.0` | `SimConfig.AdminDistance.Anchors.Capital.Strength` |  |

## `[admin_distance.anchors.sub_capital]` {#admin-distanceanchorssub-capital}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `core_radius` | `2.0` | `SimConfig.AdminDistance.Anchors.SubCapital.CoreRadius` |  |
| `max_radius` | `10.0` | `SimConfig.AdminDistance.Anchors.SubCapital.MaxRadius` |  |
| `decay_rate` | `0.35` | `SimConfig.AdminDistance.Anchors.SubCapital.DecayRate` |  |
| `strength` | `0.7` | `SimConfig.AdminDistance.Anchors.SubCapital.Strength` |  |

## `[admin_distance.anchors.tier1_presence]` {#admin-distanceanchorstier1-presence}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `core_radius` | `1.0` | `SimConfig.AdminDistance.Anchors.Tier1Presence.CoreRadius` |  |
| `max_radius` | `5.0` | `SimConfig.AdminDistance.Anchors.Tier1Presence.MaxRadius` |  |
| `decay_rate` | `0.5` | `SimConfig.AdminDistance.Anchors.Tier1Presence.DecayRate` |  |
| `strength` | `0.5` | `SimConfig.AdminDistance.Anchors.Tier1Presence.Strength` |  |

## `[admin_distance.anchors.garrison]` {#admin-distanceanchorsgarrison}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `core_radius` | `1.0` | `SimConfig.AdminDistance.Anchors.Garrison.CoreRadius` |  |
| `max_radius` | `3.0` | `SimConfig.AdminDistance.Anchors.Garrison.MaxRadius` |  |
| `decay_rate` | `0.6` | `SimConfig.AdminDistance.Anchors.Garrison.DecayRate` |  |
| `strength` | `0.3` | `SimConfig.AdminDistance.Anchors.Garrison.Strength` |  |

## `[admin_distance.anchors.religious_center]` {#admin-distanceanchorsreligious-center}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `core_radius` | `2.0` | `SimConfig.AdminDistance.Anchors.ReligiousCenter.CoreRadius` |  |
| `max_radius` | `8.0` | `SimConfig.AdminDistance.Anchors.ReligiousCenter.MaxRadius` |  |
| `decay_rate` | `0.4` | `SimConfig.AdminDistance.Anchors.ReligiousCenter.DecayRate` |  |
| `strength` | `0.4` | `SimConfig.AdminDistance.Anchors.ReligiousCenter.Strength` |  |

## `[characters]` {#characters}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `max_tier1_active` | `100` | `SimConfig.Characters.MaxTier1Active` | Maximum simultaneously simulated Tier 1 characters |
| `max_tier2_active` | `500` | `SimConfig.Characters.MaxTier2Active` | Maximum simultaneously simulated Tier 2 characters |
| `relationship_decay_per_tick` | `0.01` | `SimConfig.Characters.RelationshipDecayPerTick` | Relationship drifts toward neutral without interaction |
| `max_relationship_value` | `1.0` | `SimConfig.Characters.MaxRelationshipValue` |  |
| `min_relationship_value` | `-1.0` | `SimConfig.Characters.MinRelationshipValue` |  |
| `personality_noise_stddev` | `0.2` | `SimConfig.Characters.PersonalityNoiseStddev` | Gaussian noise on trait generation |
| `parental_inheritance_weight` | `0.6` | `SimConfig.Characters.ParentalInheritanceWeight` | How much traits pull toward parent average |
| `cultural_shift_weight` | `0.3` | `SimConfig.Characters.CulturalShiftWeight` | How much cultural modifiers shift trait distributions |

## `[characters.needs]` {#charactersneeds}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `food_decay` | `0.10` | `SimConfig.Characters.Needs.FoodDecay` | Needs decay rates per seasonal tick (how fast unmet needs worsen) |
| `safety_decay` | `0.05` | `SimConfig.Characters.Needs.SafetyDecay` |  |
| `shelter_decay` | `0.01` | `SimConfig.Characters.Needs.ShelterDecay` |  |
| `belonging_decay` | `0.02` | `SimConfig.Characters.Needs.BelongingDecay` |  |
| `status_decay` | `0.03` | `SimConfig.Characters.Needs.StatusDecay` |  |
| `purpose_decay` | `0.03` | `SimConfig.Characters.Needs.PurposeDecay` |  |
| `spiritual_decay` | `0.02` | `SimConfig.Characters.Needs.SpiritualDecay` |  |
| `critical_need_threshold` | `0.2` | `SimConfig.Characters.Needs.CriticalNeedThreshold` | Below this, character is in crisis for this need |
| `unmet_need_threshold` | `0.4` | `SimConfig.Characters.Needs.UnmetNeedThreshold` | Below this, need is considered unmet in utility calc |

## `[characters.skills]` {#charactersskills}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `base_growth_rate` | `0.002` | `SimConfig.Characters.Skills.BaseGrowthRate` | Base skill growth per use per tick |
| `max_skill_value` | `1.0` | `SimConfig.Characters.Skills.MaxSkillValue` |  |
| `min_skill_value` | `0.0` | `SimConfig.Characters.Skills.MinSkillValue` |  |
| `diminishing_returns_factor` | `0.7` | `SimConfig.Characters.Skills.DiminishingReturnsFactor` | How much growth slows near max skill |

## `[characters.aging]` {#charactersaging}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `adult_age` | `18` | `SimConfig.Characters.Aging.AdultAge` | Age at which character becomes fully active |
| `elder_age` | `60` | `SimConfig.Characters.Aging.ElderAge` | Age at which decline mechanics begin |
| `max_age_base` | `80` | `SimConfig.Characters.Aging.MaxAgeBase` | Base maximum age before death check each year |
| `max_age_variance` | `20` | `SimConfig.Characters.Aging.MaxAgeVariance` | Random variance added to max age |

## `[utility]` {#utility}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `needs_weight` | `0.40` | `SimConfig.Utility.NeedsWeight` | Weights for the utility function — must sum to 1.0 |
| `goals_weight` | `0.35` | `SimConfig.Utility.GoalsWeight` |  |
| `personality_weight` | `0.15` | `SimConfig.Utility.PersonalityWeight` |  |
| `relationship_weight` | `0.10` | `SimConfig.Utility.RelationshipWeight` |  |
| `min_temperature` | `0.2` | `SimConfig.Utility.MinTemperature` | Softmax selection temperature range Higher temperature = more random selection (less deterministic) Curiosity trait interpolates between min and max |
| `max_temperature` | `0.5` | `SimConfig.Utility.MaxTemperature` |  |
| `min_utility_threshold` | `0.05` | `SimConfig.Utility.MinUtilityThreshold` | Actions with utility below this threshold are ignored |
| `max_risk_penalty` | `0.6` | `SimConfig.Utility.MaxRiskPenalty` | Maximum utility reduction from risk aversion |

## `[goals]` {#goals}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `urgency_ramp_ticks` | `20` | `SimConfig.Goals.UrgencyRampTicks` | 5 years at seasonal resolution |
| `max_active_goals` | `5` | `SimConfig.Goals.MaxActiveGoals` | Goals beyond this are queued |
| `vengeance_aggression_min` | `0.3` | `SimConfig.Goals.VengeanceAggressionMin` | Minimum Aggression to spawn VengeanceGoal |
| `religion_wonder_min` | `0.5` | `SimConfig.Goals.ReligionWonderMin` | Minimum Wonder to spawn FoundReligionGoal |
| `seize_power_aggression_min` | `0.6` | `SimConfig.Goals.SeizePowerAggressionMin` | Minimum Aggression for SeizePowerGoal |
| `seize_power_greed_min` | `0.4` | `SimConfig.Goals.SeizePowerGreedMin` | Minimum Greed for SeizePowerGoal |

## `[specialists]` {#specialists}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `apothecary_threshold` | `200` | `SimConfig.Specialists.ApothecaryThreshold` | Minimum settlement population to support each specialist type |
| `priest_threshold` | `300` | `SimConfig.Specialists.PriestThreshold` |  |
| `entertainer_threshold` | `500` | `SimConfig.Specialists.EntertainerThreshold` |  |
| `teacher_threshold` | `500` | `SimConfig.Specialists.TeacherThreshold` |  |
| `weaponsmith_threshold` | `800` | `SimConfig.Specialists.WeaponsmithThreshold` |  |
| `physician_threshold` | `1000` | `SimConfig.Specialists.PhysicianThreshold` |  |
| `scholar_threshold` | `2000` | `SimConfig.Specialists.ScholarThreshold` |  |
| `alchemist_threshold` | `3000` | `SimConfig.Specialists.AlchemistThreshold` |  |
| `jeweler_threshold` | `3000` | `SimConfig.Specialists.JewelerThreshold` |  |
| `cartographer_threshold` | `5000` | `SimConfig.Specialists.CartographerThreshold` |  |
| `advisor_threshold` | `5000` | `SimConfig.Specialists.AdvisorThreshold` |  |
| `spy_threshold` | `8000` | `SimConfig.Specialists.SpyThreshold` |  |
| `architect_threshold` | `10000` | `SimConfig.Specialists.ArchitectThreshold` |  |
| `subsistence_needs_threshold` | `0.3` | `SimConfig.Specialists.SubsistenceNeedsThreshold` | Needs below this = Survival state |
| `independent_client_minimum` | `3` | `SimConfig.Specialists.IndependentClientMinimum` | Minimum regular clients for Independent state |
| `reputation_boost_threshold` | `0.7` | `SimConfig.Specialists.ReputationBoostThreshold` | Quality above which work boosts reputation |
| `reputation_decay_rate` | `0.005` | `SimConfig.Specialists.ReputationDecayRate` | Reputation decay per tick without high-quality work |
| `reputation_spread_hops` | `2` | `SimConfig.Specialists.ReputationSpreadHops` | How many relationship hops reputation spreads |

## `[artifacts]` {#artifacts}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `base_generation_probability` | `0.05` | `SimConfig.Artifacts.BaseGenerationProbability` | At max skill, 5% chance per crafting task |
| `notable_performance_threshold` | `0.75` | `SimConfig.Artifacts.NotablePerformanceThreshold` | Quality above which a performance is notable |
| `covet_threshold` | `0.6` | `SimConfig.Artifacts.CovetThreshold` | Artifact property score above which NPCs covet it |
| `battle_forge_probability` | `0.03` | `SimConfig.Artifacts.BattleForgeProbability` | Chance a decisive battle forges an artifact |
| `heroic_death_forge_probability` | `0.10` | `SimConfig.Artifacts.HeroicDeathForgeProbability` | Chance a legendary character's combat death forges one |
| `lost_on_death_probability` | `0.35` | `SimConfig.Artifacts.LostOnDeathProbability` | Probability artifact becomes Lost on owner death |
| `covet_ambition_threshold` | `0.55` | `SimConfig.Artifacts.CovetAmbitionThreshold` | Minimum Ambition to form covet-artifact goals |
| `covet_max_goals` | `2` | `SimConfig.Artifacts.CovetMaxGoals` | Max simultaneous covet-artifact goals per character |
| `lost_artifact_annual_decay` | `0.008` | `SimConfig.Artifacts.LostArtifactAnnualDecay` | Annual prob a Lost artifact is destroyed (primary accumulation sink) |
| `owned_artifact_annual_decay` | `0.001` | `SimConfig.Artifacts.OwnedArtifactAnnualDecay` | Annual prob an owned artifact is destroyed (accident/disaster/war) |

## `[religion]` {#religion}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `awe_threshold_base` | `0.6` | `SimConfig.Religion.AweThresholdBase` | Base probability threshold for awe event to trigger religion |
| `wonder_trait_multiplier` | `0.5` | `SimConfig.Religion.WonderTraitMultiplier` | High Wonder reduces the threshold (more receptive) |
| `piety_trait_multiplier` | `0.3` | `SimConfig.Religion.PietyTraitMultiplier` | High Piety also reduces threshold |
| `spread_trust_threshold` | `0.5` | `SimConfig.Religion.SpreadTrustThreshold` | Minimum trust required for religion to spread via relationship |
| `inter_religion_trust_penalty` | `-0.15` | `SimConfig.Religion.InterReligionTrustPenalty` | Baseline trust penalty between members of different religions |
| `conversion_experience_threshold` | `0.3` | `SimConfig.Religion.ConversionExperienceThreshold` | How much more positive experience with new religion before conversion |

## `[cultural_modifiers]` {#cultural-modifiers}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `expiry_threshold` | `0.03` | `SimConfig.CulturalModifiers.ExpiryThreshold` | Modifier magnitude below this is considered expired |
| `max_active_per_region` | `20` | `SimConfig.CulturalModifiers.MaxActivePerRegion` | Performance cap on active modifiers per region |
| `reinforcement_window_years` | `50` | `SimConfig.CulturalModifiers.ReinforcementWindowYears` | How recent must a similar event be to reinforce decay slowdown |
| `reinforcement_bonus` | `0.15` | `SimConfig.CulturalModifiers.ReinforcementBonus` | Magnitude boost per reinforcing event |

## `[cultural_modifiers.half_life_years]` {#cultural-modifiershalf-life-years}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `animosity_major_war` | `150` | `SimConfig.CulturalModifiers.HalfLifeYears.AnimosityMajorWar` | How many years for a modifier to halve in magnitude Shorter = fades faster, Longer = persists longer |
| `animosity_minor_war` | `60` | `SimConfig.CulturalModifiers.HalfLifeYears.AnimosityMinorWar` |  |
| `fear_disaster` | `100` | `SimConfig.CulturalModifiers.HalfLifeYears.FearDisaster` |  |
| `reverence_golden_age` | `120` | `SimConfig.CulturalModifiers.HalfLifeYears.ReverenceGoldenAge` |  |
| `xenophobia_plague` | `80` | `SimConfig.CulturalModifiers.HalfLifeYears.XenophobiaPlague` |  |
| `religious_fervor` | `200` | `SimConfig.CulturalModifiers.HalfLifeYears.ReligiousFervor` |  |
| `trade_goodwill` | `40` | `SimConfig.CulturalModifiers.HalfLifeYears.TradeGoodwill` |  |
| `military_trauma` | `100` | `SimConfig.CulturalModifiers.HalfLifeYears.MilitaryTrauma` |  |
| `cultural_pride` | `90` | `SimConfig.CulturalModifiers.HalfLifeYears.CulturalPride` |  |

## `[spatial_buffer]` {#spatial-buffer}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `detailed_radius_world_tiles` | `1` | `SimConfig.SpatialBuffer.DetailedRadiusWorldTiles` | World tiles of daily resolution around spotlight (3×3 zone) |
| `buffer_width_world_tiles` | `2` | `SimConfig.SpatialBuffer.BufferWidthWorldTiles` | Width of interpolation buffer ring in world tiles |
| `interpolation_noise_max` | `1` | `SimConfig.SpatialBuffer.InterpolationNoiseMax` | Maximum tile offset in daily path interpolation |

## `[performance]` {#performance}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `autosave_interval_ticks` | `40` | `SimConfig.Performance.AutosaveIntervalTicks` | state.bin autosave every N ticks (10 years at seasonal) |
| `snapshot_interval_years` | `100` | `SimConfig.Performance.SnapshotIntervalYears` | Historical snapshot every N sim-years |
| `event_cache_size` | `400` | `SimConfig.Performance.EventCacheSize` | Recent events kept in memory (ring buffer) |
| `voxel_cache_entries` | `5` | `SimConfig.Performance.VoxelCacheEntries` | LRU voxel grid cache entries |
| `influence_map_recompute_batch` | `10` | `SimConfig.Performance.InfluenceMapRecomputeBatch` | Max influence maps recomputed per tick during invalidation |
| `dirty_flag_chunk_skip` | `true` | `SimConfig.Performance.DirtyFlagChunkSkip` | Skip chunks with no dirty tiles (significant performance gain) |
| `parallel_tile_assembly` | `true` | `SimConfig.Performance.ParallelTileAssembly` | Assemble TileGrid in parallel after world gen |
| `sqlite_cache_size_kb` | `4096` | `SimConfig.Performance.SqliteCacheSizeKb` | SQLite page cache size |
| `sqlite_mmap_size_mb` | `64` | `SimConfig.Performance.SqliteMmapSizeMb` | Memory-mapped I/O size for SQLite |
