# Config Reference
<!-- GENERATED from config/sim_config.toml — edit the source TOML comments, not this file. -->
<!-- Regenerate: python3 scripts/gen-config-ref.py -->

All simulation tuning constants in one place. For balance guidance see `docs/sim_tuning.md`.
Edit values in `sim_config.toml`; all keys live there without recompiling.


## Sections

- [world_gen](#world-gen)
- [world_gen.elevation](#world-genelevation)
- [world_gen.ocean](#world-genocean)
- [world_gen.resources](#world-genresources)
- [sim_loop](#sim-loop)
- [events](#events)
- [events.gate](#eventsgate)
- [world_gen.tectonics](#world-gentectonics)
- [world_gen.rivers](#world-genrivers)
- [world_gen.biome_thresholds](#world-genbiome-thresholds)
- [local_gen](#local-gen)
- [climate](#climate)
- [disasters](#disasters)
- [beasts](#beasts)
- [character](#character)
- [character.biome_shelter_recovery](#characterbiome-shelter-recovery)
- [settlement](#settlement)
- [resource_pressure](#resource-pressure)
- [territory](#territory)
- [improvements](#improvements)
- [seafaring](#seafaring)
- [settlement_names](#settlement-names)
- [cultural_traits](#cultural-traits)
- [emissary](#emissary)
- [war](#war)
- [religion](#religion)
- [family](#family)
- [debt](#debt)
- [fear](#fear)
- [defection](#defection)
- [unrest](#unrest)
- [utility_affinity.goal_affinity](#utility-affinitygoal-affinity)
- [utility_affinity.action_needs](#utility-affinityaction-needs)
- [wildlife_risk](#wildlife-risk)
- [wildlife_risk.biome_risk](#wildlife-riskbiome-risk)
- [artifacts](#artifacts)

## `[world_gen]` {#world-gen}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `default_tile_size_km` | `10` | `SimConfig.WorldGen.DefaultTileSizeKm` | Real-world km per world-scale tile |
| `default_width_km` | `4000` | `SimConfig.WorldGen.DefaultWidthKm` | Default world width (Europe-scale) |
| `default_height_km` | `3000` | `SimConfig.WorldGen.DefaultHeightKm` | Default world height (Europe-scale) |
| `chunk_size` | `16` | `SimConfig.WorldGen.ChunkSize` | Tile grid chunk dimensions (16×16 tiles per chunk) |
| `magic_intensity_scale` | `1.0` | `SimConfig.WorldGen.MagicIntensityScale` | Multiplier on magic intensity peaks (V2 stub) |
| `fertility_micro_variance` | `20` | `SimConfig.WorldGen.FertilityMicroVariance` | ±range of high-freq noise applied to tile fertility at world gen; |
| `fertility_micro_frequency` | `0.07` | `SimConfig.WorldGen.FertilityMicroFrequency` | FastNoiseLite frequency for the fertility micro-variation noise |
| `fertility_micro_octaves` | `3` | `SimConfig.WorldGen.FertilityMicroOctaves` | FastNoiseLite fractal octave count for the fertility micro-variation noise |

## `[world_gen.elevation]` {#world-genelevation}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `noise_scale` | `0.3` | `SimConfig.WorldGen.Elevation.NoiseScale` | How much noise varies elevation around tectonic baseline |
| `mountain_threshold` | `0.7` | `SimConfig.WorldGen.Elevation.MountainThreshold` | Elevation above which terrain is classified as mountain |
| `tectonic_intensity` | `0.8` | `SimConfig.WorldGen.Elevation.TectonicIntensity` | How dramatic plate collision effects are (0=gentle, 1=extreme) |
| `smoothing_passes` | `3` | `SimConfig.WorldGen.Elevation.SmoothingPasses` | Box-blur passes on elevation after normalization. |
| `continental_fault_weight` | `0.6` | `SimConfig.WorldGen.Elevation.ContinentalFaultWeight` | continental fault line → mountain ridge |
| `volcanic_fault_weight` | `0.5` | `SimConfig.WorldGen.Elevation.VolcanicFaultWeight` | volcanic/subduction fault → volcanic peaks |
| `oceanic_fault_weight` | `-0.3` | `SimConfig.WorldGen.Elevation.OceanicFaultWeight` | oceanic non-volcanic fault → slight trench |
| `continental_interior_weight` | `0.15` | `SimConfig.WorldGen.Elevation.ContinentalInteriorWeight` | continental interior (no fault) → highland bias |
| `oceanic_interior_weight` | `-0.10` | `SimConfig.WorldGen.Elevation.OceanicInteriorWeight` | oceanic interior (no fault) → slight basin |
| `fractal_octaves` | `5` | `SimConfig.WorldGen.Elevation.FractalOctaves` | FastNoiseLite fractal parameters for the base elevation noise |
| `fractal_lacunarity` | `2.0` | `SimConfig.WorldGen.Elevation.FractalLacunarity` |  |
| `fractal_gain` | `0.5` | `SimConfig.WorldGen.Elevation.FractalGain` |  |

## `[world_gen.ocean]` {#world-genocean}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `default_sea_level` | `0.40` | `SimConfig.WorldGen.Ocean.DefaultSeaLevel` | Fraction of tiles (by elevation rank) that are ocean |
| `erosion_passes` | `2` | `SimConfig.WorldGen.Ocean.ErosionPasses` | Passes to strip thin ridges (≥min_ocean8_neighbors ocean 8-neighbors → ocean) |
| `min_ocean8_neighbors` | `5` | `SimConfig.WorldGen.Ocean.MinOcean8Neighbors` | Threshold for erosion: tiles with this many ocean 8-neighbors become ocean |

## `[world_gen.resources]` {#world-genresources}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `iron_density` | `0.08` | `SimConfig.WorldGen.Resources.IronDensity` | Fraction of mountain/hill tiles with iron deposits |
| `copper_density` | `0.04` | `SimConfig.WorldGen.Resources.CopperDensity` | Fraction of volcanic-adjacent tiles with copper |
| `tin_density` | `0.015` | `SimConfig.WorldGen.Resources.TinDensity` | Fraction of eligible tiles with tin (rare by design) |
| `precious_metal_density` | `0.005` | `SimConfig.WorldGen.Resources.PreciousMetalDensity` | Fraction of volcanic tiles with precious metals |
| `rare_resource_density` | `0.003` | `SimConfig.WorldGen.Resources.RareResourceDensity` | Fraction of tiles with rare/magical resources |
| `stone_on_fault_threshold` | `0.35` | `SimConfig.WorldGen.Resources.StoneOnFaultThreshold` | continental fault, after Iron/Copper rolls |
| `volcanic_sulfur_threshold` | `0.25` | `SimConfig.WorldGen.Resources.VolcanicSulfurThreshold` | volcanic tile, after Obsidian roll |
| `hill_coal_threshold` | `0.15` | `SimConfig.WorldGen.Resources.HillCoalThreshold` | mountain/hill tile, after Gold roll |
| `hill_stone_threshold` | `0.3` | `SimConfig.WorldGen.Resources.HillStoneThreshold` | mountain/hill tile, after Gold/Coal rolls |
| `herb_density` | `0.35` | `SimConfig.WorldGen.Resources.HerbDensity` | Base fraction of eligible tiles with Herbs (forests/grassland/swamp) |
| `wild_game_density` | `0.30` | `SimConfig.WorldGen.Resources.WildGameDensity` | Base fraction of eligible tiles with Wild_Game (forest/grassland/savanna) |
| `clay_density` | `0.25` | `SimConfig.WorldGen.Resources.ClayDensity` | Base fraction of eligible tiles with Clay (swamp/river/hills) |
| `flint_density` | `0.20` | `SimConfig.WorldGen.Resources.FlintDensity` | Base fraction of eligible tiles with Flint (desert/hills/beach) |
| `fertility_recovery_per_year` | `3` | `SimConfig.WorldGen.Resources.FertilityRecoveryPerYear` | byte/year; recovery must be faster than penalty (ratio ~1:1) |
| `post_fire_fertility_boost` | `30` | `SimConfig.WorldGen.Resources.PostFireFertilityBoost` | temporary fertility bonus (byte) after fire clears |
| `drought_fertility_penalty_per_season` | `3` | `SimConfig.WorldGen.Resources.DroughtFertilityPenaltyPerSeason` | byte/season; balanced against recovery rate |
| `drought_fertility_floor` | `5` | `SimConfig.WorldGen.Resources.DroughtFertilityFloor` | minimum fertility during drought (byte); prevents permanent zero |

## `[sim_loop]` {#sim-loop}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `ticks_per_seasonal_change` | `4` | `SimConfig.SimLoop.TicksPerSeasonalChange` | ticks between season changes (4 ticks/season × 4 seasons = 16 ticks/year) |
| `auto_save_interval_ticks` | `960` | `SimConfig.SimLoop.AutoSaveIntervalTicks` | auto-save every N ticks (960 = ~60 in-game years at 16 ticks/year) |
| `auto_save_dir` | `"worldsave"` | `SimConfig.SimLoop.AutoSaveDir` | directory for auto-saves and Ctrl+S saves |
| `slow_ticks_per_second` | `0.5` | `SimConfig.SimLoop.SlowTicksPerSecond` | seasonal ticks per real second at Slow speed |
| `normal_ticks_per_second` | `1.0` | `SimConfig.SimLoop.NormalTicksPerSecond` | seasonal ticks per real second at Normal speed |
| `fast_ticks_per_second` | `10.0` | `SimConfig.SimLoop.FastTicksPerSecond` | seasonal ticks per real second at Fast speed |
| `ultrafast_snapshot_interval_ticks` | `160` | `SimConfig.SimLoop.UltrafastSnapshotIntervalTicks` | push UI snapshot every N ticks in Ultrafast (160 = 10 years) |
| `event_write_batch_interval_ticks` | `20` | `SimConfig.SimLoop.EventWriteBatchIntervalTicks` | batch event writes every N ticks; ~1 year at Normal speed; 0=every tick |
| `metrics_enabled` | `true` | `SimConfig.SimLoop.MetricsEnabled` | false only for micro-benchmarks where DB writes must be minimized |
| `headless_progress_interval_seconds` | `10.0` | `SimConfig.SimLoop.HeadlessProgressIntervalSeconds` | min wall-clock gap between headless-runner progress lines (M11 phase 0) |
| `summary_rebuild_interval_years` | `50` | `SimConfig.SimLoop.SummaryRebuildIntervalYears` | years between auto BuildSummaries() rebuilds; 0=disabled (caller must call it). Headless runner sets 0 (M11 phase 0 perf fix). |

## `[events]` {#events}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `minimum_recorded_tier` | `0` | `SimConfig.Events.MinimumRecordedTier` | 0=Background, 1=Character, 2=Regional, 3=Headline |
| `recent_event_cache_size` | `500` | `SimConfig.Events.RecentEventCacheSize` |  |
| `suppressed_types` | `["SettlementGrew", "SettlementShrank", "Negotiated"]` | `SimConfig.Events.SuppressedTypes` | suppress per-tick noise; Negotiated floods DB before chars ally |

## `[events.gate]` {#eventsgate}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `always_record_types` | `CharacterBorn, CharacterDied, CivilizationFounded, CivilizationCollapsed, ArtifactCreated, ArtifactDestroyed, ReligionFounded, ReligionExtinct, WarDeclared, WarEnded, TerritoryExpanded, TerritoryLost, SettlementConquered, GodModeDisasterTriggered, GodModeEntitySpawned, GodModeCharacterCreated, GodModeArtifactPlaced, GodModeCivilizationForced, CivSplintered` | `SimConfig.Events.Gate.AlwaysRecordTypes` | Event types always recorded regardless of other gate settings |
| `suppressed_types` | `Tier3ResourceTick, SettlementFoodAccounting, PopulationGrowthIncrement, ArmySupplyConsumption, RelationshipDecayTick, CharacterMoved, 77` | `SimConfig.Events.Gate.SuppressedTypes` | Event types always suppressed regardless of other gate settings |

## `[world_gen.tectonics]` {#world-gentectonics}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `plate_count` | `15` | `SimConfig.WorldGen.Tectonics.PlateCount` |  |
| `min_plate_separation_fraction` | `0.12` | `SimConfig.WorldGen.Tectonics.MinPlateSeparationFraction` |  |
| `continental_plate_fraction` | `0.45` | `SimConfig.WorldGen.Tectonics.ContinentalPlateFraction` |  |
| `boundary_perturb_strength` | `10.0` | `SimConfig.WorldGen.Tectonics.BoundaryPerturbStrength` | Tiles of max noise displacement at Voronoi assignment; |
| `boundary_perturb_frequency` | `0.07` | `SimConfig.WorldGen.Tectonics.BoundaryPerturbFrequency` | Noise frequency for perturbation. Lower=broader waves. |

## `[world_gen.rivers]` {#world-genrivers}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `flow_accumulation_threshold` | `50` | `SimConfig.WorldGen.Rivers.FlowAccumulationThreshold` |  |
| `min_lake_basin_tiles` | `20` | `SimConfig.WorldGen.Rivers.MinLakeBasinTiles` |  |
| `major_river_threshold` | `500` | `SimConfig.WorldGen.Rivers.MajorRiverThreshold` |  |
| `crossing_min_width_fraction` | `0.05` | `SimConfig.WorldGen.Rivers.CrossingMinWidthFraction` | Border-manifest river-crossing width (fraction of a tile edge's length), M11 local-scale gen |
| `crossing_max_width_fraction` | `0.25` | `SimConfig.WorldGen.Rivers.CrossingMaxWidthFraction` |  |

## `[world_gen.biome_thresholds]` {#world-genbiome-thresholds}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `high_mountain_elevation` | `220` | `SimConfig.WorldGen.BiomeThresholds.HighMountainElevation` | byte elevation above which terrain is HighMountain |
| `mountain_elevation` | `180` | `SimConfig.WorldGen.BiomeThresholds.MountainElevation` | byte elevation above which terrain is Mountain |
| `hills_elevation` | `140` | `SimConfig.WorldGen.BiomeThresholds.HillsElevation` | byte elevation above which terrain is Hills |
| `hot_temperature` | `180` | `SimConfig.WorldGen.BiomeThresholds.HotTemperature` | byte temp above which tile is "hot" (tropical/desert zone) |
| `cold_temperature` | `80` | `SimConfig.WorldGen.BiomeThresholds.ColdTemperature` | byte temp below which tile is "cold" (boreal/tundra zone) |
| `polar_temperature` | `40` | `SimConfig.WorldGen.BiomeThresholds.PolarTemperature` | byte temp below which tile is "polar" (tundra/ice) |
| `wet_moisture` | `160` | `SimConfig.WorldGen.BiomeThresholds.WetMoisture` | byte moisture above which tile is "wet" (forest/rainforest) |
| `dry_moisture` | `60` | `SimConfig.WorldGen.BiomeThresholds.DryMoisture` | byte moisture below which tile is "dry" (savanna/desert) |
| `arid_moisture` | `30` | `SimConfig.WorldGen.BiomeThresholds.AridMoisture` | byte moisture below which tile is "arid" (desert only) |

## `[local_gen]` {#local-gen}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `chunk_size_tiles` | `40` | `SimConfig.LocalGen.ChunkSizeTiles` | Local tiles per chunk edge (chunk = 40x40 cells) |
| `local_tiles_per_world_tile_edge` | `1000` | `SimConfig.LocalGen.LocalTilesPerWorldTileEdge` | 10km world tile / 10m local tile = 1000; must divide |
| `edge_blend_band_tiles` | `100` | `SimConfig.LocalGen.EdgeBlendBandTiles` | width (local tiles) of the blend-toward-manifest band at each world-tile edge |
| `noise_frequency` | `0.05` | `SimConfig.LocalGen.NoiseFrequency` | FastNoiseLite frequency for local elevation detail, sampled in absolute local-tile coords |
| `noise_octaves` | `3` | `SimConfig.LocalGen.NoiseOctaves` | fractal octaves for local elevation detail noise |
| `noise_amplitude` | `6.0` | `SimConfig.LocalGen.NoiseAmplitude` | max +/- byte contribution the detail noise adds atop the blended macro elevation |
| `river_channel_depth` | `20` | `SimConfig.LocalGen.RiverChannelDepth` | byte elevation decrement applied to cells carved into a river channel |
| `river_source_width_tiles` | `15.0` | `SimConfig.LocalGen.RiverSourceWidthTiles` | channel width (local tiles) at a river's interior source/mouth anchor |
| `view_distance_chunks` | `3` | `SimConfig.LocalGen.ViewDistanceChunks` | chunk-radius kept generated around the local-view camera; farther chunks are discarded, not persisted |
| `decoration_cluster_frequency` | `0.015` | `SimConfig.LocalGen.DecorationClusterFrequency` | FastNoiseLite frequency for decoration cluster placement (tree stands, wetland patches) — low freq for large patchy regions |
| `decoration_cluster_threshold` | `0.15` | `SimConfig.LocalGen.DecorationClusterThreshold` | noise threshold ([-1,1]) above which a cell falls inside a decoration cluster |
| `decoration_sparse_frequency` | `0.25` | `SimConfig.LocalGen.DecorationSparseFrequency` | FastNoiseLite frequency for sparse secondary decoration (scattered rocks/shrubs) — high freq for isolated single-cell features |
| `decoration_sparse_threshold` | `0.55` | `SimConfig.LocalGen.DecorationSparseThreshold` | noise threshold ([-1,1]) above which a cell gets the sparse secondary decoration, when not already in a cluster |

## `[climate]` {#climate}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `storm_corridor_moisture_bonus` | `1.3` | `SimConfig.Climate.StormCorridorMoistureBonus` | Runtime: moisture multiplier inside storm corridor |
| `monsoon_intensity_multiplier` | `1.5` | `SimConfig.Climate.MonsoonIntensityMultiplier` | Runtime: moisture multiplier in monsoon season |
| `tropical_band_half_width` | `0.25` | `SimConfig.Climate.TropicalBandHalfWidth` | Fraction of world height defining tropical zone |
| `rain_shadow_loss_fraction` | `0.6` | `SimConfig.Climate.RainShadowLossFraction` | Moisture lost per tile immediately leeward of mountain |
| `mountain_elevation_threshold` | `180` | `SimConfig.Climate.MountainElevationThreshold` | Byte elevation above which rain shadow applies |
| `monsoon_moisture_threshold` | `160` | `SimConfig.Climate.MonsoonMoistureThreshold` | Byte moisture above which tile is monsoon zone |
| `storm_corridor_normalized_lat` | `0.35` | `SimConfig.Climate.StormCorridorNormalizedLat` | Fractional latitude of storm corridor center (0=S, 1=N) |
| `storm_corridor_half_width` | `0.08` | `SimConfig.Climate.StormCorridorHalfWidth` | Fraction of world height defining storm corridor band |
| `storm_corridor_moisture_bonus_genesis` | `0.3` | `SimConfig.Climate.StormCorridorMoistureBonusGenesis` | Moisture bonus multiplier during storm season |
| `annual_temp_drift_rate` | `0.05` | `SimConfig.Climate.AnnualTempDriftRate` | degrees per year; 0 = stable climate |
| `max_warming_anomaly` | `5.0` | `SimConfig.Climate.MaxWarmingAnomaly` | max GlobalTemperatureAnomaly (positive) |
| `max_cooling_anomaly` | `3.0` | `SimConfig.Climate.MaxCoolingAnomaly` | max GlobalTemperatureAnomaly magnitude (negative) |
| `storm_corridor_shift_per_degree` | `0.005` | `SimConfig.Climate.StormCorridorShiftPerDegree` | fractional lat shift per degree of anomaly |
| `monsoon_anomaly_sensitivity` | `0.01` | `SimConfig.Climate.MonsoonAnomalySensitivity` | MonsoonIntensityMultiplier change per degree of anomaly |
| `monsoon_multiplier_min` | `0.5` | `SimConfig.Climate.MonsoonMultiplierMin` | minimum MonsoonIntensityMultiplier |
| `monsoon_multiplier_max` | `3.0` | `SimConfig.Climate.MonsoonMultiplierMax` | maximum MonsoonIntensityMultiplier |
| `lat_temperature_anomaly_scale` | `1.4` | `SimConfig.Climate.LatTemperatureAnomalyScale` | amplification of temperature anomaly at high latitudes |
| `climate_cycle_amplitude` | `2.5` | `SimConfig.Climate.ClimateCycleAmplitude` | degrees; long-period oscillation layered on top of the secular |
| `climate_cycle_period_years` | `800` | `SimConfig.Climate.ClimateCyclePeriodYears` | years per full oscillation of climate_cycle_amplitude |
| `annual_sea_level_drift_rate` | `0.0` | `SimConfig.Climate.AnnualSeaLevelDriftRate` | sea level change per year; 0 = stable |
| `sea_level_event_threshold` | `0.1` | `SimConfig.Climate.SeaLevelEventThreshold` | minimum delta to emit SeaLevelChanged event |
| `volcanic_decay_rate` | `0.005` | `SimConfig.Climate.VolcanicDecayRate` | VolcanicActivityMultiplier lerp toward 1.0 per tick (~0.07/year at 14 ticks/year) |
| `moisture_carry_decay` | `0.993` | `SimConfig.Climate.MoistureCarryDecay` | Fraction of moisture retained per inland tile during wind sweep |
| `temperature_noise_scale` | `0.28` | `SimConfig.Climate.TemperatureNoiseScale` | ±amplitude of coherent noise added to temperature latitude fraction. |
| `temperature_noise_frequency` | `0.009` | `SimConfig.Climate.TemperatureNoiseFrequency` | Noise frequency for temperature anomalies. ~111 tile period = broad regional blobs. |
| `moisture_noise_scale` | `60.0` | `SimConfig.Climate.MoistureNoiseScale` | ±amplitude (byte units 0-255) added to moisture after wind sweeps. |
| `moisture_noise_frequency` | `0.009` | `SimConfig.Climate.MoistureNoiseFrequency` | Noise frequency for moisture anomalies. Matched to temp frequency for same blob size. |
| `moisture_angle_blend` | `0.22` | `SimConfig.Climate.MoistureAngleBlend` | Fraction of carry that bleeds to adjacent latitude rows at each column step. |
| `continental_radius_tiles` | `25.0` | `SimConfig.Climate.ContinentalRadiusTiles` | E-folding distance (tiles) for maritime influence decay from ocean/lakes. |
| `continental_amplification` | `0.18` | `SimConfig.Climate.ContinentalAmplification` | Temperature deviation: coast cooled, interior warmed by this fraction × latDeviation. |
| `lake_moisture_recharge` | `0.40` | `SimConfig.Climate.LakeMoistureRecharge` | Moisture carry level that lake tiles raise the sweep to (0-1, fraction of max). |
| `river_moisture_bonus` | `0.08` | `SimConfig.Climate.RiverMoistureBonus` | Flat carry bonus added when sweep crosses a river tile. Stacks with existing carry. |
| `continental_seasonal_threshold` | `0.25` | `SimConfig.Climate.ContinentalSeasonalThreshold` | maritime influence below this → continental seasonal profile (wet summer, dry winter) |
| `maritime_seasonal_threshold` | `0.50` | `SimConfig.Climate.MaritimeSeasonalThreshold` | maritime influence above this → maritime profile (dry summer, wet autumn/winter) |
| `min_seasonal_moisture_ratio` | `0.20` | `SimConfig.Climate.MinSeasonalMoistureRatio` | seasonal CurrentMoisture floor: max(BaseMoisture + delta, BaseMoisture × this) |
| `wind_band_feather_rows` | `6` | `SimConfig.Climate.WindBandFeatherRows` | half-width of vertical blend zone at tropical/mid-lat boundary. |

## `[disasters]` {#disasters}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `wildfire_ignition_probability_per_tick` | `0.000003` | `SimConfig.Disasters.WildfireIgnitionProbabilityPerTick` | 16 ticks/year × ~10k forest tiles → ~0.5/yr at this value |
| `wildfire_ignition_dry_multiplier` | `3.0` | `SimConfig.Disasters.WildfireIgnitionDryMultiplier` | multiplier when moisture < dry threshold |
| `wildfire_spread_probability_per_tick` | `0.10` | `SimConfig.Disasters.WildfireSpreadProbabilityPerTick` | reduced from 0.20 — fires were spreading too aggressively |
| `wildfire_max_ticks` | `16` | `SimConfig.Disasters.WildfireMaxTicks` |  |
| `wildfire_dry_moisture_threshold` | `60` | `SimConfig.Disasters.WildfireDryMoistureThreshold` |  |
| `wildfire_hot_temperature_threshold` | `170` | `SimConfig.Disasters.WildfireHotTemperatureThreshold` | effective temp above which heat multiplier kicks in |
| `wildfire_ignition_hot_multiplier` | `2.0` | `SimConfig.Disasters.WildfireIgnitionHotMultiplier` | stacks with dry multiplier; hot+dry = 6× base probability |
| `wildfire_intensity` | `1.0` | `SimConfig.Disasters.WildfireIntensity` | 0-1 severity assigned to wildfire ActiveDisaster |
| `flood_ignition_probability_per_tick` | `0.000002` | `SimConfig.Disasters.FloodIgnitionProbabilityPerTick` | 16 ticks/year × eligible tiles → ~0.2/yr |
| `flood_wet_moisture_threshold` | `200` | `SimConfig.Disasters.FloodWetMoistureThreshold` |  |
| `flood_wet_multiplier` | `2.0` | `SimConfig.Disasters.FloodWetMultiplier` | probability multiplier when tile is very wet |
| `flood_spread_radius` | `1` | `SimConfig.Disasters.FloodSpreadRadius` | tiles of radius from origin that get secondary flood |
| `flood_origin_intensity` | `0.7` | `SimConfig.Disasters.FloodOriginIntensity` | intensity at origin tile |
| `flood_spread_intensity` | `0.5` | `SimConfig.Disasters.FloodSpreadIntensity` | intensity at spread tiles |
| `flood_origin_ticks` | `6` | `SimConfig.Disasters.FloodOriginTicks` | duration (seasons) at origin |
| `flood_spread_ticks` | `4` | `SimConfig.Disasters.FloodSpreadTicks` | duration (seasons) at spread tiles |
| `volcanic_eruption_probability_per_tick` | `0.000005` | `SimConfig.Disasters.VolcanicEruptionProbabilityPerTick` | 16 ticks/year × volcanic tiles → ~0.2/yr |
| `volcanic_ash_intensity` | `1.0` | `SimConfig.Disasters.VolcanicAshIntensity` | intensity assigned to VolcanicAsh ActiveDisaster |
| `volcanic_activity_boost` | `0.05` | `SimConfig.Disasters.VolcanicActivityBoost` | added to VolcanicActivityMultiplier per eruption |
| `volcanic_activity_multiplier_cap` | `10.0` | `SimConfig.Disasters.VolcanicActivityMultiplierCap` | VolcanicActivityMultiplier is clamped to this ceiling |
| `earthquake_probability_per_tick` | `0.000005` | `SimConfig.Disasters.EarthquakeProbabilityPerTick` | 16 ticks/year × ~4k fault tiles → ~0.3/yr |
| `earthquake_intensity` | `0.8` | `SimConfig.Disasters.EarthquakeIntensity` | intensity assigned to SeismicDamage ActiveDisaster |
| `earthquake_decay_ticks` | `8` | `SimConfig.Disasters.EarthquakeDecayTicks` |  |
| `drought_probability_per_year` | `0.05` | `SimConfig.Disasters.DroughtProbabilityPerYear` |  |
| `drought_drought_multiplier` | `2.0` | `SimConfig.Disasters.DroughtDroughtMultiplier` | chance multiplier when precipitation low |
| `drought_precipitation_threshold` | `0.7` | `SimConfig.Disasters.DroughtPrecipitationThreshold` |  |
| `drought_min_seasons` | `2` | `SimConfig.Disasters.DroughtMinSeasons` |  |
| `drought_max_seasons` | `8` | `SimConfig.Disasters.DroughtMaxSeasons` |  |

## `[beasts]` {#beasts}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `starvation_health_loss` | `5` | `SimConfig.Beasts.StarvationHealthLoss` | health points lost per season when FoodNeed < 0.2 |

## `[character]` {#character}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `initial_count` | `14` | `SimConfig.Character.InitialCount` |  |
| `max_age_seasons_min` | `600` | `SimConfig.Character.MaxAgeSeasonsMin` | ~37.5 years at 16 ticks/year; fallback for characters without ancestry data |
| `max_age_seasons_max` | `1200` | `SimConfig.Character.MaxAgeSeasonsMax` | ~75 years |
| `max_health` | `100` | `SimConfig.Character.MaxHealth` |  |
| `min_ruler_age_seasons` | `96` | `SimConfig.Character.MinRulerAgeSeasons` | 6 years at 16 ticks/year; succession skips members younger than this (no infant monarchs) |
| `needs_decay_safety` | `0.05` | `SimConfig.Character.NeedsDecaySafety` |  |
| `needs_decay_food` | `0.08` | `SimConfig.Character.NeedsDecayFood` |  |
| `needs_decay_shelter` | `0.04` | `SimConfig.Character.NeedsDecayShelter` |  |
| `shelter_comfort_temp_low` | `80` | `SimConfig.Character.ShelterComfortTempLow` | byte; tile BaseTemperature below this → cold shelter pressure |
| `shelter_comfort_temp_high` | `180` | `SimConfig.Character.ShelterComfortTempHigh` | byte; tile BaseTemperature above this → heat shelter pressure |
| `shelter_temperature_scale` | `0.8` | `SimConfig.Character.ShelterTemperatureScale` | max additional decay multiplier at temp extremes (0→low or 255→high) |
| `expansion_home_penalty` | `120` | `SimConfig.Character.ExpansionHomePenalty` | tile score penalty applied to own-civ settlement tiles when character has Expansion goal (pushes them to leave) |
| `expansion_empty_tile_bonus` | `80` | `SimConfig.Character.ExpansionEmptyTileBonus` | tile score bonus for tiles outside every settlement's hinterland when Expansion goal is active |
| `expansion_compactness_radius` | `8` | `SimConfig.Character.ExpansionCompactnessRadius` | max tile distance from a same-civ settlement that earns the compactness bonus |
| `expansion_compactness_bonus` | `60` | `SimConfig.Character.ExpansionCompactnessBonus` | bonus added when an unclaimed tile is near an existing same-civ settlement (encourages blob growth) |
| `max_settlements_per_civ` | `15` | `SimConfig.Character.MaxSettlementsPerCiv` | generous backstop; splinter mechanic (S2) is the real soft cap at ~3–7 cities |
| `colony_min_distance` | `10` | `SimConfig.Character.ColonyMinDistance` | tiles from nearest same-civ settlement for a tile to be "frontier" |
| `global_settlement_min_dist` | `3` | `SimConfig.Character.GlobalSettlementMinDist` | minimum tiles from ANY existing settlement (any civ) — prevents clustering |
| `colony_frontier_bonus` | `120` | `SimConfig.Character.ColonyFrontierBonus` | tile score bonus for candidate tiles in frontier territory |
| `colonize_ambition_threshold` | `0.5` | `SimConfig.Character.ColonizeAmbitionThreshold` | min Ambition to form a Colonize goal (was 0.72 — too restrictive; S1 2026-07-18) |
| `max_colonies_per_civ` | `5` | `SimConfig.Character.MaxColoniesPerCiv` | hard cap on live colonies (was 3; S1 growth overhaul 2026-07-18) |
| `founding_deposit_weight` | `6` | `SimConfig.Character.FoundingDepositWeight` | founding-site score weight for resource deposit value (0-1 normalized, capped 2.0 before scaling) |
| `founding_route_weight` | `4` | `SimConfig.Character.FoundingRouteWeight` | founding-site score weight for trade-route positioning (0-1 normalized) |
| `max_cities_per_civ` | `15` | `SimConfig.Character.MaxCitiesPerCiv` | total city cap per civ (was 8; soft backstop, splinter is real limiter; S1 2026-07-18) |
| `city_founding_ambition_threshold` | `0.3` | `SimConfig.Character.CityFoundingAmbitionThreshold` | min Ambition for delegation (was 0.5 — blocked ~60% of chars; S1 2026-07-18) |
| `shelter_seek_threshold` | `0.35` | `SimConfig.Character.ShelterSeekThreshold` | below this shelter level, characters prefer terrain with natural cover when choosing where to move |
| `shelter_seek_tile_bonus` | `80` | `SimConfig.Character.ShelterSeekTileBonus` | max tile-score bonus for a perfect-shelter biome (forest/mountain) when shelter-seeking |
| `needs_decay_belonging` | `0.03` | `SimConfig.Character.NeedsDecayBelonging` | scales by desperation: bonus × BiomeShelterScore × (1 - shelter) |
| `needs_decay_status` | `0.03` | `SimConfig.Character.NeedsDecayStatus` |  |
| `needs_decay_purpose` | `0.04` | `SimConfig.Character.NeedsDecayPurpose` |  |
| `needs_decay_spiritual` | `0.02` | `SimConfig.Character.NeedsDecaySpiritual` |  |
| `belonging_own_settlement_recovery` | `0.03` | `SimConfig.Character.BelongingOwnSettlementRecovery` | own-civ settlement: full community belonging (balances decay) |
| `belonging_foreign_settlement_recovery` | `0.01` | `SimConfig.Character.BelongingForeignSettlementRecovery` | foreign settlement: partial benefit as a stranger |
| `status_own_settlement_recovery` | `0.03` | `SimConfig.Character.StatusOwnSettlementRecovery` | recognized by civ peers (balances decay) |
| `purpose_own_settlement_recovery` | `0.02` | `SimConfig.Character.PurposeOwnSettlementRecovery` | shared work and goals (rest makes up the rest) |
| `spiritual_settlement_recovery` | `0.01` | `SimConfig.Character.SpiritualSettlementRecovery` | any settlement: shared ritual, culture |
| `ambient_safety_recovery` | `0.05` | `SimConfig.Character.AmbientSafetyRecovery` | Ambient recovery — background regeneration every tick, independent of location |
| `ambient_food_recovery` | `0.07` | `SimConfig.Character.AmbientFoodRecovery` |  |
| `settlement_shelter_recovery` | `0.10` | `SimConfig.Character.SettlementShelterRecovery` | shelter recovery while on a settlement tile |
| `ally_presence_belonging_bonus` | `0.05` | `SimConfig.Character.AllyPresenceBelongingBonus` | belonging bonus for standing near an allied character |
| `default_biome_shelter_recovery` | `0.01` | `SimConfig.Character.DefaultBiomeShelterRecovery` | fallback for biomes not listed in [character.biome_shelter_recovery] |
| `needs_weight` | `0.5` | `SimConfig.Character.NeedsWeight` |  |
| `goals_weight` | `0.3` | `SimConfig.Character.GoalsWeight` |  |
| `personality_weight` | `0.2` | `SimConfig.Character.PersonalityWeight` |  |
| `softmax_temp_min` | `0.5` | `SimConfig.Character.SoftmaxTempMin` |  |
| `softmax_temp_max` | `2.0` | `SimConfig.Character.SoftmaxTempMax` |  |
| `perception_radius` | `3` | `SimConfig.Character.PerceptionRadius` |  |
| `health_per_season_heal` | `5` | `SimConfig.Character.HealthPerSeasonHeal` |  |
| `combat_damage_base` | `20` | `SimConfig.Character.CombatDamageBase` |  |
| `min_fertility_to_settle` | `5` | `SimConfig.Character.MinFertilityToSettle` | floor for founding — desert/tundra allowed; food model constrains pop there naturally |
| `min_base_moisture_to_settle` | `5` | `SimConfig.Character.MinBaseMoistureToSettle` | effectively zero — hardcoded land moisture floor is 10 so all land passes; moisture no longer gates founding |
| `deposit_settle_threshold` | `0.5` | `SimConfig.Character.DepositSettleThreshold` | min deposit value to settle despite low fertility |
| `deposit_score_multiplier` | `0.5` | `SimConfig.Character.DepositScoreMultiplier` | how much deposit value boosts establish score |
| `route_score_multiplier` | `0.3` | `SimConfig.Character.RouteScoreMultiplier` | how much route-position bonus boosts establish score |
| `ruin_cooldown_years` | `10` | `SimConfig.Character.RuinCooldownYears` | hard block: cannot settle a ruined tile for this many years (deposit override disabled) |
| `ruin_founding_penalty` | `0.4` | `SimConfig.Character.RuinFoundingPenalty` | score penalty after cooldown expires (0–1); high deposits or fertility can still overcome it |
| `ruin_decay_half_life_years` | `50` | `SimConfig.Character.RuinDecayHalfLifeYears` | years for the penalty to halve (exponential decay) |
| `alliance_max_base` | `2` | `SimConfig.Character.AllianceMaxBase` | minimum alliance slots per character |
| `alliance_max_per_sociability` | `3` | `SimConfig.Character.AllianceMaxPerSociability` | +floor(Sociability × this) extra slots; Soc=1.0 → 5 slots total |
| `alliance_trust_floor` | `0.1` | `SimConfig.Character.AllianceTrustFloor` | trust below this dissolves the alliance on annual check |
| `alliance_war_trust_drain` | `0.4` | `SimConfig.Character.AllianceWarTrustDrain` | trust drained from aggressor to target's allies on war declaration |
| `enemy_of_ally_trust_drain` | `0.15` | `SimConfig.Character.EnemyOfAllyTrustDrain` | trust drain on C when A allies B who is already allied with C's rival |
| `ally_protect_goal_intensity` | `0.6` | `SimConfig.Character.AllyProtectGoalIntensity` | priority of the Protect goal seeded on allies when their ally is attacked |
| `ally_disaster_aid_intensity` | `0.3` | `SimConfig.Character.AllyDisasterAidIntensity` | (reserved for future goal seeding; merchant routing handles aid currently) |
| `rivalry_max_base` | `1` | `SimConfig.Character.RivalryMaxBase` | War lifecycle keys moved to [war] section (D5 consolidation — see below) Rivalry cap scales with Aggression: max = floor(base + Aggression × per_aggression) Aggression=0 → 1 rival max; Aggression=1.0 → 4 rivals max |
| `rivalry_max_per_aggression` | `3` | `SimConfig.Character.RivalryMaxPerAggression` |  |
| `rivalry_trust_threshold` | `-0.4` | `SimConfig.Character.RivalryTrustThreshold` | trust must fall below this before DeclareRivalry is available; prevents one-encounter rivals |
| `bond_max_base` | `1` | `SimConfig.Character.BondMaxBase` | Bond cap scales with Compassion: max = floor(base + Compassion × per_compassion) Compassion=0 → 1 bond max; Compassion=1.0 → 3 bonds max |
| `bond_max_per_compassion` | `2` | `SimConfig.Character.BondMaxPerCompassion` |  |
| `base_founding_cooldown_years` | `2` | `SimConfig.Character.BaseFoundingCooldownYears` | base cooldown between same-civ settlements; halves with population |
| `min_founding_cooldown_years` | `1` | `SimConfig.Character.MinFoundingCooldownYears` | floor — even large civs can't expand faster than once per year |
| `founding_cooldown_pop_scale` | `2000` | `SimConfig.Character.FoundingCooldownPopScale` | civ pop at which cooldown halves (4y→2y at 2k pop) |
| `civ_birth_min_pop` | `20` | `SimConfig.Character.CivBirthMinPop` | lowered from 30; settlements reach this faster with improved farming |
| `civ_birth_chance_per_season` | `0.01` | `SimConfig.Character.CivBirthChancePerSeason` | 1% per year at min-pop; keeps character count manageable for performance |
| `territorial_aggression_min` | `0.55` | `SimConfig.Character.TerritorialAggressionMin` | chars below this aggression don't apply pressure |
| `territorial_trust_drain` | `0.025` | `SimConfig.Character.TerritorialTrustDrain` | trust lost per tick; ~-0.1 in 1 season → rivalry within a year |
| `same_civ_familiarity_base_rate` | `0.0015` | `SimConfig.Character.SameCivFamiliarityBaseRate` | always-on per-tick companionship growth |
| `same_civ_warmth_bonus_rate` | `0.003` | `SimConfig.Character.SameCivWarmthBonusRate` | extra growth × avg(Sociability, Compassion) |
| `same_civ_friction_base_rate` | `0.0015` | `SimConfig.Character.SameCivFrictionBaseRate` | always-on per-tick friction (petty jealousy exists even in compatible pairs) |
| `same_civ_friction_rate` | `0.014` | `SimConfig.Character.SameCivFrictionRate` | extra drain × avg(\|Ambition diff\|, \|Aggression diff\|) |
| `beast_encounter_aggression_min` | `0.3` | `SimConfig.Character.BeastEncounterAggressionMin` | beasts below this aggression are passive toward characters |
| `beast_encounter_chance` | `0.15` | `SimConfig.Character.BeastEncounterChance` | probability of attack per shared tick (~every 7 ticks) |
| `beast_damage_multiplier` | `0.3` | `SimConfig.Character.BeastDamageMultiplier` | beast.Strength × this = damage dealt to character |
| `char_counter_damage_multiplier` | `0.4` | `SimConfig.Character.CharCounterDamageMultiplier` | c.Skills.Combat × MaxHealth × this = counter-damage dealt to beast per exchange |
| `slay_beast_combat_threshold` | `0.55` | `SimConfig.Character.SlayBeastCombatThreshold` | minimum Combat skill to form SlayBeast goal (dedicated fighters only) |
| `slay_beast_aggression_threshold` | `0.65` | `SimConfig.Character.SlayBeastAggressionThreshold` | minimum Aggression to form SlayBeast goal (truly aggressive) |
| `slay_beast_search_radius` | `5` | `SimConfig.Character.SlayBeastSearchRadius` | tile radius for legendary beast detection |
| `character_disease_exposure_chance` | `0.10` | `SimConfig.Character.CharacterDiseaseExposureChance` | annual probability of contracting disease while at an infected settlement |
| `character_disease_health_drain` | `20` | `SimConfig.Character.CharacterDiseaseHealthDrain` | HP drained per year while infected (suppresses natural healing) |
| `character_disease_recovery_chance` | `0.30` | `SimConfig.Character.CharacterDiseaseRecoveryChance` | annual natural recovery probability (physician healing can supplement) |
| `defender_counter_damage_multiplier` | `0.25` | `SimConfig.Character.DefenderCounterDamageMultiplier` | defender.Combat × MaxHealth × this = damage dealt to raider |
| `raider_char_damage_multiplier` | `0.20` | `SimConfig.Character.RaiderCharDamageMultiplier` | raider.Combat × MaxHealth × this = damage dealt to defending character |
| `wildlife_char_injury_fraction` | `0.12` | `SimConfig.Character.WildlifeCharInjuryFraction` | fraction of MaxHealth lost when a character is present at a wildlife raid |
| `wildlife_char_defense_reduction` | `0.40` | `SimConfig.Character.WildlifeCharDefenseReduction` | defender.Combat scales this; reduces population damage fraction (max reduction = this value) |
| `wanderlust_max_ticks` | `8` | `SimConfig.Character.WanderlustMaxTicks` | full bonus after 2 seasons (half a year) stationary |
| `wanderlust_bonus` | `0.4` | `SimConfig.Character.WanderlustBonus` | travel score bonus at max wanderlust; ~0.15 base → ~0.55 for free agents |
| `wanderlust_founder_multiplier` | `0.15` | `SimConfig.Character.WanderlustFounderMultiplier` | settlement founders (kings, rulers) — 15% of base wanderlust |
| `wanderlust_member_multiplier` | `0.8` | `SimConfig.Character.WanderlustMemberMultiplier` | civ members — 70% of base wanderlust (was 0.5; raised to encourage expansion) |
| `wanderlust_curiosity_floor` | `0.3` | `SimConfig.Character.WanderlustCuriosityFloor` | even the least curious chars have 30% of their role's wanderlust |
| `personality_mismatch_drain_rate` | `0.003` | `SimConfig.Character.PersonalityMismatchDrainRate` | drain × \|stability diff\| per tick |
| `cultural_distance_drain_rate` | `0.002` | `SimConfig.Character.CulturalDistanceDrainRate` | drain × cultural_distance (0–1) per tick |
| `ally_trust_threshold` | `0.4` | `SimConfig.Character.AllyTrustThreshold` | trust ≥ this → offer alliance (otherwise fall back to negotiation) |
| `negotiate_max_trust` | `0.7` | `SimConfig.Character.NegotiateMaxTrust` | trust < this → negotiation available (no need to negotiate once already warm) |
| `goal_aggression_threshold` | `0.6` | `SimConfig.Character.GoalAggressionThreshold` | minimum Aggression to generate Dominance goal |
| `goal_sociability_threshold` | `0.5` | `SimConfig.Character.GoalSociabilityThreshold` | minimum Sociability to generate Alliance goal |
| `goal_compassion_threshold` | `0.5` | `SimConfig.Character.GoalCompassionThreshold` | minimum Compassion to generate Bond goal |
| `goal_ingenuity_threshold` | `0.55` | `SimConfig.Character.GoalIngenuityThreshold` | minimum Ingenuity to generate Create goal |
| `goal_diligence_threshold` | `0.45` | `SimConfig.Character.GoalDiligenceThreshold` | minimum Diligence to generate BuildImprovement goal |
| `max_concurrent_goals` | `2` | `SimConfig.Character.MaxConcurrentGoals` | M13 13.5 balance: ceiling on simultaneous *discretionary* goals (Dominance/Alliance/Bond/Create/ BuildImprovement/SlayBeast/CovetArtifact — not Survive/Grieve/Avenge/FoundCity/SeaVoyage, which are existential or externally imposed). Without this, goals stack freely (up to ~10 for a high-trait character) and GoalAdvancement scoring is perpetually dominated by something, crowding out idle/opportunistic behavior (Rest, GrantAid, Placate, Ally, Negotiate). |
| `avenge_aggression_threshold` | `0.6` | `SimConfig.Character.AvengeAggressionThreshold` | minimum Aggression to form Avenge goal on mourning |
| `avenge_intensity_threshold` | `0.5` | `SimConfig.Character.AvengeIntensityThreshold` | minimum grief intensity required to form Avenge goal |
| `bond_trust_threshold` | `0.5` | `SimConfig.Character.BondTrustThreshold` | minimum relationship Trust before a character qualifies as Bond companion |
| `goal_stale_season_limit` | `32` | `SimConfig.Character.GoalStaleSeasonLimit` | ticks (2 years at 16 ticks/year) without progress before a non-Grieve goal is pruned |
| `rival_search_radius` | `5` | `SimConfig.Character.RivalSearchRadius` | tile radius when scanning for nearby rivals (Dominance goal) |
| `alliance_search_radius` | `4` | `SimConfig.Character.AllianceSearchRadius` | tile radius when scanning for nearby neutrals (Alliance goal) |
| `bond_search_radius` | `3` | `SimConfig.Character.BondSearchRadius` | tile radius when scanning for Bond companions |
| `civ_floor_count` | `4` | `SimConfig.Character.CivFloorCount` | minimum active civs; floor kicks in below this |
| `civ_floor_spawn_chance` | `0.3` | `SimConfig.Character.CivFloorSpawnChance` | annual probability per missing civ slot to spawn a founder |
| `civ_floor_min_dist` | `20` | `SimConfig.Character.CivFloorMinDist` | minimum tile distance from existing settlements for the spawn tile |
| `civ_floor_preferred_max_dist` | `80` | `SimConfig.Character.CivFloorPreferredMaxDist` | S3: prefer floor-spawn sites within this distance of an existing |
| `spawn_weight_tundra` | `0.05` | `SimConfig.Character.SpawnWeightTundra` | Biome spawn weights (S3) — multiplied into spawn-site selection for both initial character spawns (CharacterSpawner) and civ floor spawns (RunCivFloorSpawns). Down-weight harsh biomes (tundra/desert/mountain) so civs stop spawning where they struggle and leave ruins; favor grassland/plains/temperate/coast. |
| `spawn_weight_boreal_forest` | `0.4` | `SimConfig.Character.SpawnWeightBorealForest` |  |
| `spawn_weight_temperate_forest` | `1.0` | `SimConfig.Character.SpawnWeightTemperateForest` |  |
| `spawn_weight_tropical_rainforest` | `0.5` | `SimConfig.Character.SpawnWeightTropicalRainforest` |  |
| `spawn_weight_grassland` | `1.5` | `SimConfig.Character.SpawnWeightGrassland` |  |
| `spawn_weight_savanna` | `0.8` | `SimConfig.Character.SpawnWeightSavanna` |  |
| `spawn_weight_desert` | `0.05` | `SimConfig.Character.SpawnWeightDesert` |  |
| `spawn_weight_swamp` | `0.3` | `SimConfig.Character.SpawnWeightSwamp` |  |
| `spawn_weight_mountain` | `0.15` | `SimConfig.Character.SpawnWeightMountain` |  |
| `spawn_weight_hills` | `0.8` | `SimConfig.Character.SpawnWeightHills` |  |
| `spawn_weight_plains` | `1.5` | `SimConfig.Character.SpawnWeightPlains` |  |
| `spawn_weight_beach` | `0.7` | `SimConfig.Character.SpawnWeightBeach` |  |
| `spawn_weight_default` | `0.3` | `SimConfig.Character.SpawnWeightDefault` | volcanic and anything unlisted |
| `succession_crisis_years` | `10` | `SimConfig.Character.SuccessionCrisisYears` | how long the crisis lasts after founder death |
| `succession_crisis_decay_mult` | `2.5` | `SimConfig.Character.SuccessionCrisisDecayMult` | decay rate multiplier for settlements beyond stable radius during crisis |
| `succession_stable_radius` | `15` | `SimConfig.Character.SuccessionStableRadius` | tile radius from capital immune to succession instability |
| `wellbeing_goal_gain_rate` | `0.015` | `SimConfig.Character.WellbeingGoalGainRate` | per tick when a goal is progressing (raised from 0.01 — allows positive equilibrium) |
| `wellbeing_companion_boost` | `0.005` | `SimConfig.Character.WellbeingCompanionBoost` | per tick co-located with Bond target |
| `wellbeing_hunger_drain` | `0.02` | `SimConfig.Character.WellbeingHungerDrain` | max drain at zero food |
| `wellbeing_hunger_threshold` | `0.3` | `SimConfig.Character.WellbeingHungerThreshold` | food need below this triggers hunger wellbeing drain |
| `wellbeing_mean_reversion_rate` | `0.008` | `SimConfig.Character.WellbeingMeanReversionRate` | pull toward 0 each tick (raised from 0.005 — prevents permanent distress equilibrium) |
| `flourishing_threshold` | `0.6` | `SimConfig.Character.FlourishingThreshold` | Wellbeing ≥ this → Flourishing (lowered from 0.7 — more reachable) |
| `spiral_threshold` | `-0.7` | `SimConfig.Character.SpiralThreshold` | Wellbeing ≤ this → Spiraling |
| `distressed_social_suppression` | `0.4` | `SimConfig.Character.DistressedSocialSuppression` | social action score multiplier when Wellbeing < -0.3 |
| `grief_drain_rate` | `0.015` | `SimConfig.Character.GriefDrainRate` | Wellbeing drain per tick per Grieve goal |
| `grief_decay_rate` | `0.004` | `SimConfig.Character.GriefDecayRate` | grief intensity decay per tick (raised from 0.002 — was ~31yr grief period, now ~15yr) |
| `grief_wellbeing_shock` | `0.4` | `SimConfig.Character.GriefWellbeingShock` | fraction of grief intensity applied as immediate Wellbeing shock on mourning |
| `grief_completion_threshold` | `0.05` | `SimConfig.Character.GriefCompletionThreshold` | grief Intensity below this → grief goal auto-completes (grief resolved) |
| `grief_spouse_multiplier` | `1.6` | `SimConfig.Character.GriefSpouseMultiplier` | multiplies Bond intensity when the deceased was IsMarried |
| `grief_family_multiplier` | `1.3` | `SimConfig.Character.GriefFamilyMultiplier` | multiplies Bond intensity when the deceased was IsFamily (not spouse) |
| `grief_stranger_multiplier` | `1.0` | `SimConfig.Character.GriefStrangerMultiplier` | baseline: an ordinary bonded companion, no Family/Married flag |
| `wellbeing_endure_multiplier` | `0.5` | `SimConfig.Character.WellbeingEndureMultiplier` | Endure: slow draining perseverance |
| `wellbeing_survive_multiplier` | `0.3` | `SimConfig.Character.WellbeingSurviveMultiplier` | Survive: urgent pressure but lower emotional weight |
| `wellbeing_flee_multiplier` | `0.4` | `SimConfig.Character.WellbeingFleeMultiplier` | Flee: flight-or-fight stress |
| `stagnation_threshold_ticks` | `80` | `SimConfig.Character.StagnationThresholdTicks` | ticks before inactive goal drains wellbeing (~5 years at 16 ticks/year) |
| `stagnation_drain_rate` | `0.002` | `SimConfig.Character.StagnationDrainRate` | per tick for goals stuck without progress |
| `purpose_drought_drain` | `0.001` | `SimConfig.Character.PurposeDroughtDrain` | per tick when character has no flourishing goals (Create/Bond/FoundCity/Protect) |
| `tier2_per_population` | `10` | `SimConfig.Character.Tier2PerPopulation` | Tier 2 sub-tuning (appended to [character] section — Tomlyn merges same-section keys) NOTE: tier2_* keys previously failed to bind due to digit→uppercase PascalToSnakeCase bug (fixed in B2). Dead-vs-live disagreements resolved: tier2_notable_cooldown_ticks TOML was 64, live was 32 → kept 32. tier2_exceptional_work_chance TOML was 0.001, live was 0.002 → kept 0.002. |
| `tier2_max_age_seasons_min` | `600` | `SimConfig.Character.Tier2MaxAgeSeasonsMin` | ~38 years at 16 ticks/year (was 60 — 4 years, broken) |
| `tier2_max_age_seasons_max` | `1200` | `SimConfig.Character.Tier2MaxAgeSeasonsMax` | ~75 years (was 120 — also broken) |
| `tier2_crystal_chance` | `0.001` | `SimConfig.Character.Tier2CrystalChance` |  |
| `tier2_needs_decay_food` | `0.06` | `SimConfig.Character.Tier2NeedsDecayFood` |  |
| `tier2_needs_decay_safety` | `0.04` | `SimConfig.Character.Tier2NeedsDecaySafety` |  |
| `tier2_needs_decay_belonging` | `0.03` | `SimConfig.Character.Tier2NeedsDecayBelonging` |  |
| `tier2_needs_decay_status` | `0.04` | `SimConfig.Character.Tier2NeedsDecayStatus` |  |
| `tier2_ambient_food_recovery` | `0.05` | `SimConfig.Character.Tier2AmbientFoodRecovery` | lower food web |
| `tier2_ambient_safety_recovery` | `0.04` | `SimConfig.Character.Tier2AmbientSafetyRecovery` | ambient safety |
| `tier2_settlement_belonging_recovery` | `0.05` | `SimConfig.Character.Tier2SettlementBelongingRecovery` | extra Belonging recovery while on a settlement tile |
| `tier2_settlement_status_recovery_base` | `0.03` | `SimConfig.Character.Tier2SettlementStatusRecoveryBase` | Status recovery at settlement = this × Diligence |
| `tier2_crystal_ambition_threshold` | `0.8` | `SimConfig.Character.Tier2CrystalAmbitionThreshold` | min Ambition to roll for crystallization into Tier1 |
| `tier2_crystal_status_threshold` | `0.7` | `SimConfig.Character.Tier2CrystalStatusThreshold` | min Status to roll for crystallization into Tier1 |
| `tier2_notability_gain_per_event` | `0.15` | `SimConfig.Character.Tier2NotabilityGainPerEvent` | added to Notability per targeted event |
| `tier2_notability_decay_rate` | `0.01` | `SimConfig.Character.Tier2NotabilityDecayRate` | per tick, mirrors Needs decay |
| `tier2_crystal_notability_threshold` | `0.6` | `SimConfig.Character.Tier2CrystalNotabilityThreshold` | OR'd with tier2_crystal_status_threshold above |
| `tier2_crystal_notability_chance_bonus` | `0.01` | `SimConfig.Character.Tier2CrystalNotabilityChanceBonus` | × Notability, added to tier2_crystal_chance |
| `masterwork_quality_base` | `0.6` | `SimConfig.Character.MasterworkQualityBase` | Masterwork artifact quality: base + exceptional-work roll × rollScale, clamped [0,1] |
| `masterwork_quality_roll_scale` | `0.4` | `SimConfig.Character.MasterworkQualityRollScale` |  |
| `merchant_trade_chance` | `0.15` | `SimConfig.Character.MerchantTradeChance` | per-tick probability a merchant attempts a trade |
| `merchant_trade_transfer` | `0.1` | `SimConfig.Character.MerchantTradeTransfer` | fraction of home's surplus transferred per trade |
| `merchant_ally_opportunity_bonus` | `0.3` | `SimConfig.Character.MerchantAllyOpportunityBonus` | opportunity-score bonus (fraction of surplus) for allied destinations |
| `merchant_trade_status_gain` | `0.05` | `SimConfig.Character.MerchantTradeStatusGain` | merchant Status gain on completing a trade |
| `trade_income_bonus_scale` | `1.0` | `SimConfig.Character.TradeIncomeBonusScale` | M9 9.1: bonus_trade_income (Scholar Mathematics discovery) scales transfer fraction; capped. |
| `trade_income_bonus_cap` | `0.5` | `SimConfig.Character.TradeIncomeBonusCap` |  |
| `merchant_max_demand_weight` | `3.0` | `SimConfig.Character.MerchantMaxDemandWeight` | M9 9.1: cap on per-capita-demand routing weight — how much a starved destination's deficit can amplify raw opportunity score over a larger absolute surplus elsewhere. |
| `merchant_specialization_bonus_scale` | `0.5` | `SimConfig.Character.MerchantSpecializationBonusScale` | M9 9.2: export opportunity bonus when the traded resource matches the home settlement's Specialization: opportunity *= 1 + SpecializationStrength × this scale. |
| `general_guard_safety_bonus` | `0.03` | `SimConfig.Character.GeneralGuardSafetyBonus` | Safety bonus applied to a co-located Tier1 employer each tick |
| `physician_heal_fraction` | `0.1` | `SimConfig.Character.PhysicianHealFraction` | fraction of MaxHealth healed per treatment tick |
| `artisan_craft_chance` | `0.25` | `SimConfig.Character.ArtisanCraftChance` | per-tick probability of producing a notable (vs. silent) good |
| `artisan_cohesion_bonus` | `0.01` | `SimConfig.Character.ArtisanCohesionBonus` | settlement cohesion bonus added to ResourceStores per notable craft |
| `scholar_discovery_chance` | `0.04` | `SimConfig.Character.ScholarDiscoveryChance` | per-tick chance = this × Rationality |
| `scholar_discovery_bonus_amount` | `0.05` | `SimConfig.Character.ScholarDiscoveryBonusAmount` | bonus added to settlement ResourceStore key per discovery |
| `physician_settlement_heal_rate` | `0.5` | `SimConfig.Character.PhysicianSettlementHealRate` | health restored per tick to infected settlement = this × Rationality |
| `tier2_notable_cooldown_ticks` | `32` | `SimConfig.Character.Tier2NotableCooldownTicks` | minimum ticks between notable events per character (2 years; was 64 in dead TOML — live value 32 preserved) |
| `tier2_exceptional_work_chance` | `0.002` | `SimConfig.Character.Tier2ExceptionalWorkChance` | per-tick chance of exceptional work (~1 per lifetime; was 0.001 in dead TOML — live value 0.002 preserved) |
| `create_goal_cooldown_ticks` | `80` | `SimConfig.Character.CreateGoalCooldownTicks` | ticks after completing a Create goal before a new one can form (5 years) |
| `artwork_cooldown_years` | `10` | `SimConfig.Character.ArtworkCooldownYears` | one artwork event per character per decade at most |

## `[character.biome_shelter_recovery]` {#characterbiome-shelter-recovery}

_Natural shelter recovery per tick when NOT on a settlement tile, by biome. Dense canopy and rocky terrain provide real cover; open plains and desert offer very little. Any biome not listed falls back to default_biome_shelter_recovery above._

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `temperate_forest` | `0.05` | `SimConfig.Character.BiomeShelterRecovery.TemperateForest` | canopy + deadfall = functional camp; near-settlement recovery |
| `tropical_rainforest` | `0.05` | `SimConfig.Character.BiomeShelterRecovery.TropicalRainforest` |  |
| `boreal_forest` | `0.04` | `SimConfig.Character.BiomeShelterRecovery.BorealForest` | good shelter, brutal temperature (offset by cold pressure) |
| `swamp` | `0.03` | `SimConfig.Character.BiomeShelterRecovery.Swamp` | cover but wet; net mediocre |
| `mountain` | `0.04` | `SimConfig.Character.BiomeShelterRecovery.Mountain` | rock faces, overhangs, natural caves |
| `high_mountain` | `0.02` | `SimConfig.Character.BiomeShelterRecovery.HighMountain` | excluded from movement so characters won't be there; kept for completeness |
| `grassland` | `0.02` | `SimConfig.Character.BiomeShelterRecovery.Grassland` | can make a lean-to or windbreak but nothing substantial |
| `plains` | `0.015` | `SimConfig.Character.BiomeShelterRecovery.Plains` |  |
| `savanna` | `0.015` | `SimConfig.Character.BiomeShelterRecovery.Savanna` |  |
| `tundra` | `0.015` | `SimConfig.Character.BiomeShelterRecovery.Tundra` |  |
| `beach` | `0.01` | `SimConfig.Character.BiomeShelterRecovery.Beach` | exposed / hostile — minimal shelter |
| `desert` | `0.01` | `SimConfig.Character.BiomeShelterRecovery.Desert` |  |
| `volcanic` | `0.01` | `SimConfig.Character.BiomeShelterRecovery.Volcanic` |  |

## `[settlement]` {#settlement}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `settlement_start_pop` | `500` | `SimConfig.Settlement.SettlementStartPop` | founding group size (tribe/clan migrating with the leader); at 100 km²/tile this puts |
| `pop_growth_rate` | `0.5` | `SimConfig.Settlement.PopGrowthRate` | population added per season per fertility unit (fertility/255 × this) |
| `pop_decay_rate` | `0.05` | `SimConfig.Settlement.PopDecayRate` | population lost per season unconditionally (hunger/disease baseline) |
| `starvation_decay_rate` | `0.3` | `SimConfig.Settlement.StarvationDecayRate` | extra decay per unit food deficit when food < shortage_threshold |
| `famine_decay_rate` | `0.8` | `SimConfig.Settlement.FamineDecayRate` | extra decay per unit food deficit when food < crisis_threshold |
| `pop_min_viable` | `5` | `SimConfig.Settlement.PopMinViable` | below this → settlement abandoned |
| `pop_max` | `50000` | `SimConfig.Settlement.PopMax` | hard cap; tiles = 100 km² each; 50k is a large early-medieval city + hinterland (early-medieval Paris ~30k) |
| `fertility_variance` | `0.15` | `SimConfig.Settlement.FertilityVariance` | per-settlement founding-time multiplier range (±this around 1.0) |
| `carry_cap_minimum` | `100` | `SimConfig.Settlement.CarryCapMinimum` | floor: newly-founded settlement with tiny territory never hits zero instantly |
| `capacity_smoothing_alpha` | `0.05` | `SimConfig.Settlement.CapacitySmoothingAlpha` | EMA smoothing alpha for the derived capacity. Damps territory-population feedback oscillation. 0 = frozen (never updates), 1 = raw each tick. At 0.05: half-life ~13 ticks (≈0.8 years). |
| `disease_base_chance` | `0.01` | `SimConfig.Settlement.DiseaseBaseChance` | ~1 outbreak per 150 years per average settlement (was 0.02 — too frequent) |
| `disease_density_mult` | `3.0` | `SimConfig.Settlement.DiseaseDensityMult` | large dense cities still vulnerable: ~4× at pop/cap=1.0 |
| `disease_contact_mult` | `1.5` | `SimConfig.Settlement.DiseaseContactMult` | 50% extra risk when civ has trade/war contact with another civ |
| `disease_famine_mult` | `2.0` | `SimConfig.Settlement.DiseaseFamineMult` | 2× outbreak risk while in food crisis |
| `disease_famine_threshold` | `0.70` | `SimConfig.Settlement.DiseaseFamineThreshold` | FoodPressureRatio below which famine factor fires (matches crisis threshold) |
| `disease_mortality_per_year` | `0.05` | `SimConfig.Settlement.DiseaseMortalityPerYear` | meaningful but not instantly lethal |
| `disease_min_pop` | `30` | `SimConfig.Settlement.DiseaseMinPop` | outbreaks cannot start below this population |
| `disease_spread_radius` | `12` | `SimConfig.Settlement.DiseaseSpreadRadius` | tile radius within which disease can spread annually |
| `disease_spread_chance` | `0.08` | `SimConfig.Settlement.DiseaseSpreadChance` | reduced from 0.20 — disease is regional, not instantly global |
| `disease_max_duration_years` | `8` | `SimConfig.Settlement.DiseaseMaxDurationYears` | infection auto-clears after this many years |
| `disease_recovery_chance` | `0.30` | `SimConfig.Settlement.DiseaseRecoveryChance` | natural recovery probability per year |
| `disease_resistance_bonus_scale` | `1.0` | `SimConfig.Settlement.DiseaseResistanceBonusScale` | bonus_disease_resistance (Scholar Medicine discovery): outbreakChance *= 1 - min(cap, store × scale) |
| `disease_resistance_bonus_cap` | `0.5` | `SimConfig.Settlement.DiseaseResistanceBonusCap` |  |
| `health_recovery_per_tick` | `1` | `SimConfig.Settlement.HealthRecoveryPerTick` | HP regained per tick; raids deal 10–30 HP so recovery takes weeks to months |
| `max_health` | `100` | `SimConfig.Settlement.MaxHealth` | maximum settlement health |
| `wildlife_attack_base_chance` | `0.05` | `SimConfig.Settlement.WildlifeAttackBaseChance` | reduced from 0.08; biome multiplier then adjusts (0.3x–2.0x) |
| `wildlife_attack_damage` | `0.08` | `SimConfig.Settlement.WildlifeAttackDamage` | reduced from 0.10 |
| `wildlife_defense_pop_scale` | `300` | `SimConfig.Settlement.WildlifeDefensePopScale` | settlements at this population have 80% reduced vulnerability |
| `emigration_threshold` | `0.75` | `SimConfig.Settlement.EmigrationThreshold` | fraction of carrying cap above which emigration activates |
| `emigration_bonus_chance` | `0.08` | `SimConfig.Settlement.EmigrationBonusChance` | additional annual spawn probability when over threshold (scaled by pressure) |
| `emigrant_pop_cost` | `20` | `SimConfig.Settlement.EmigrantPopCost` | population deducted from parent settlement per emigrant spawned |
| `crystal_pop_artisan` | `200` | `SimConfig.Settlement.CrystalPopArtisan` |  |
| `crystal_pop_scholar` | `300` | `SimConfig.Settlement.CrystalPopScholar` |  |
| `crystal_pop_physician` | `500` | `SimConfig.Settlement.CrystalPopPhysician` |  |
| `crystal_pop_merchant` | `1000` | `SimConfig.Settlement.CrystalPopMerchant` |  |
| `growth_event_threshold_pct` | `0.05` | `SimConfig.Settlement.GrowthEventThresholdPct` | 5% growth vs start-of-tick pop; fires on annual tick only |
| `shrink_event_threshold_pct` | `0.05` | `SimConfig.Settlement.ShrinkEventThresholdPct` | 5% decline; catches disease/disaster hits each year |
| `ruin_min_pop_threshold` | `0` | `SimConfig.Settlement.RuinMinPopThreshold` | all settlements start at 500 pop; checking pop at death incorrectly skips starved-out settlements (was 75) |
| `ruin_decay_start_years` | `300` | `SimConfig.Settlement.RuinDecayStartYears` | ruins older than this are eligible for decay |
| `ruin_decay_chance_per_year` | `0.02` | `SimConfig.Settlement.RuinDecayChancePerYear` | 2% annual chance of removal once eligible |

## `[resource_pressure]` {#resource-pressure}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `shortage_threshold` | `0.6` | `SimConfig.ResourcePressure.ShortageThreshold` | food ratio below this = shortage; seeds Acquire goals |
| `crisis_threshold` | `0.3` | `SimConfig.ResourcePressure.CrisisThreshold` | food ratio below this = crisis; also seeds Flee goals |
| `acquire_goal_intensity` | `0.7` | `SimConfig.ResourcePressure.AcquireGoalIntensity` |  |
| `flee_goal_intensity` | `0.5` | `SimConfig.ResourcePressure.FleeGoalIntensity` |  |
| `strain_event_cooldown` | `8` | `SimConfig.ResourcePressure.StrainEventCooldown` | ticks between SettlementStraining events per settlement |
| `people_per_tile_peak` | `50.0` | `SimConfig.ResourcePressure.PeoplePerTilePeak` | D1 recalibration: target food ratio 2-5 by year 300 so drought/disaster can push below 1.0. |
| `food_moisture_floor` | `0.25` | `SimConfig.ResourcePressure.FoodMoistureFloor` | proportional floor: fraction of BaseMoisture always available (wells, root storage, irrigation) |
| `food_moisture_absolute_floor` | `0.35` | `SimConfig.ResourcePressure.FoodMoistureAbsoluteFloor` | absolute floor applied after proportional; ensures low-BaseMoisture tiles still produce food from groundwater/rivers rather than zeroing out in drought |
| `frost_temperature_threshold` | `45` | `SimConfig.ResourcePressure.FrostTemperatureThreshold` | effective temp (byte) below which the cold-hardy floor applies |
| `cold_hardy_food_floor` | `0.70` | `SimConfig.ResourcePressure.ColdHardyFoodFloor` | food fraction available below frost threshold (herding, fishing, cold-adapted crops); 0.70 gives tundra ~14-tile viability at initial territory |
| `optimal_temperature_low` | `100` | `SimConfig.ResourcePressure.OptimalTemperatureLow` | lower bound of peak growing band; below this food scales up from 0 |
| `optimal_temperature_high` | `200` | `SimConfig.ResourcePressure.OptimalTemperatureHigh` | upper bound of peak growing band; above this heat stress begins |
| `heat_stress_factor` | `0.7` | `SimConfig.ResourcePressure.HeatStressFactor` | multiplier at extreme heat (255); tropical-desert crops still viable but stressed |
| `biome_food_bonus_scale` | `1.0` | `SimConfig.ResourcePressure.BiomeFoodBonusScale` | 0=all biomes equal, 1.0=full biome differentiation (grassland 2x, desert 0.3x) |
| `store_accumulate_rate` | `0.4` | `SimConfig.ResourcePressure.StoreAccumulateRate` | D1 recalibration: fraction of per-tick surplus that goes into stores (was 0.6; reduced so stores drain more in drought) |
| `store_max_seasons_per_k_pop` | `2.0` | `SimConfig.ResourcePressure.StoreMaxSeasonsPerKPop` | max vital store depth in seasons per 1000 population (was 4.0; reduced to let drought deplete stores faster) |
| `store_min_seasons` | `1.0` | `SimConfig.ResourcePressure.StoreMinSeasons` | hard floor on vital store capacity; small settlements buffer 1 season (was 2.0; reduced so drought can push ratio below 1.0) |
| `food_spoilage_rate` | `0.002` | `SimConfig.ResourcePressure.FoodSpoilageRate` | ~500 ticks (35y) to fully spoil if never drawn |
| `water_spoilage_rate` | `0.010` | `SimConfig.ResourcePressure.WaterSpoilageRate` | cisterns evaporate faster; settlements need continuous rainfall |
| `wealth_spoilage_rate` | `0.0001` | `SimConfig.ResourcePressure.WealthSpoilageRate` | gold/gems essentially permanent |
| `stockpile_spoilage_rate` | `0.0005` | `SimConfig.ResourcePressure.StockpileSpoilageRate` | iron/copper/timber decay slowly |
| `wealth_accumulate_rate` | `0.2` | `SimConfig.ResourcePressure.WealthAccumulateRate` | fraction of non-vital ledger supply that banks into stores per tick |
| `store_raid_destruction_per_damage` | `0.008` | `SimConfig.ResourcePressure.StoreRaidDestructionPerDamage` | fraction of ALL stores destroyed per point of raid damage (granaries/vaults burn) |
| `non_vital_demand_per_capita` | `0.05` | `SimConfig.ResourcePressure.NonVitalDemandPerCapita` | M9 9.1: per-capita demand model for non-vital resources (minerals, timber, gold, ...). ledger[key] becomes supply / (population × this rate) instead of raw tile yield — one generic rate rather than a per-resource table. Tuned so a mid-size settlement's typical mineral/timber tile yield lands ratio ~1.0-2.0 under current world-gen densities (see balance sweep notes). |
| `food_yield_bonus_scale` | `1.0` | `SimConfig.ResourcePressure.FoodYieldBonusScale` | bonus_food_yield (Scholar Agriculture discovery): foodContrib *= 1 + min(cap, store × scale). |
| `food_yield_bonus_cap` | `0.5` | `SimConfig.ResourcePressure.FoodYieldBonusCap` |  |
| `specialization_smoothing_alpha` | `0.05` | `SimConfig.ResourcePressure.SpecializationSmoothingAlpha` | M9 9.2: settlement specialization — EMA-tracked dominant non-vital resource. A resource must clear this per-capita ratio to be a specialization candidate; strength grows/decays via EMA at the given alpha (same damping rationale as capacity_smoothing_alpha) and grants a production multiplier on the specialized resource, capped. |
| `specialization_min_ratio` | `1.0` | `SimConfig.ResourcePressure.SpecializationMinRatio` |  |
| `specialization_bonus_scale` | `0.6` | `SimConfig.ResourcePressure.SpecializationBonusScale` |  |
| `specialization_bonus_cap` | `0.5` | `SimConfig.ResourcePressure.SpecializationBonusCap` |  |

## `[territory]` {#territory}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `claim_tiles_per_person` | `8` | `SimConfig.Territory.ClaimTilesPerPerson` | 1 tile per N people; city of 800 → 100 tiles (~radius 5) |
| `min_city_tiles` | `7` | `SimConfig.Territory.MinCityTiles` | radius-1 circle, always retained |
| `max_city_tiles` | `120` | `SimConfig.Territory.MaxCityTiles` | absolute upper bound on tile count (~radius-6 circle; 120×100km²=12,000km²) |
| `max_territory_radius` | `7` | `SimConfig.Territory.MaxTerritoryRadius` | hard Euclidean radius cap; 7 tiles = ~70km from city center |
| `min_territory_radius` | `2` | `SimConfig.Territory.MinTerritoryRadius` | guaranteed minimum radius regardless of population (~12 tiles) |
| `pop_per_territory_radius_tile` | `300` | `SimConfig.Territory.PopPerTerritoryRadiusTile` | people per additional tile of radius above the minimum |
| `territory_growth_per_year` | `4` | `SimConfig.Territory.TerritoryGrowthPerYear` | max tiles claimed per city per year (prevents instant snowball) |
| `initial_city_claim_radius` | `2` | `SimConfig.Territory.InitialCityClaimRadius` | tiles claimed at founding (~13 tiles) |

## `[improvements]` {#improvements}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `farm_food_multiplier` | `2.0` | `SimConfig.Improvements.FarmFoodMultiplier` | Farm on tile: food contribution × this |
| `mine_yield_multiplier` | `3.0` | `SimConfig.Improvements.MineYieldMultiplier` | Mine on tile: mineral yield × this |
| `logging_yield_multiplier` | `2.5` | `SimConfig.Improvements.LoggingYieldMultiplier` | LoggingCamp: timber × this |
| `pasture_multiplier` | `1.5` | `SimConfig.Improvements.PastureMultiplier` | Pasture (grassland/savanna only): food × this |
| `fishery_multiplier` | `2.0` | `SimConfig.Improvements.FisheryMultiplier` | Fishery (coastal/river only): food × this |
| `improvement_build_ticks` | `8` | `SimConfig.Improvements.ImprovementBuildTicks` | ticks character must remain on tile to build (= half a year) |

## `[seafaring]` {#seafaring}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `ocean_crossing_enabled` | `true` | `SimConfig.Seafaring.OceanCrossingEnabled` | master toggle for M11 sea voyages; false = characters never leave their landmass |
| `max_voyage_tiles` | `12` | `SimConfig.Seafaring.MaxVoyageTiles` | max shallow-ocean tiles a route may cross to the far shore |

## `[settlement_names]` {#settlement-names}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `prefixes` | `Iron, Stone, Green, Dark, Swift, High, Old, Black, White, Red, Cold, Bright, Fair, Lone, Ash, Ember, Frost, Gold, Mist, Reed, Crag, Deep, Tall, Hard, Flint, Silver, Amber, Hollow, Sharp, Broad` | `SimConfig.SettlementNames.Prefixes` |  |
| `suffixes` | `ford, hold, wick, vale, mere, fell, gate, haven, reach, moor, cliff, pass, watch, crest, grove, hollow, peak, ridge, bridge, mill, shore, keep, mark, lea` | `SimConfig.SettlementNames.Suffixes` |  |

## `[cultural_traits]` {#cultural-traits}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `militaristic_min_wars` | `3` | `SimConfig.CulturalTraits.MilitaristicMinWars` | reduced from 10; avg civ gets 1.45 wars total |
| `militaristic_wars_per_decade` | `2.0` | `SimConfig.CulturalTraits.MilitaristicWarsPerDecade` | AND average more than this many wars per decade |
| `expansionist_founding_rate` | `1.0` | `SimConfig.CulturalTraits.ExpansionistFoundingRate` | Expansionist: civ must average > this many settlement foundings per 10 years |
| `expansionist_sustained_years` | `20` | `SimConfig.CulturalTraits.ExpansionistSustainedYears` | reduced from 30 |
| `war_weary_min_repeat_wars` | `2` | `SimConfig.CulturalTraits.WarWearyMinRepeatWars` | reduced from 3; same-enemy repeat wars are rare |
| `resilient_min_near_collapse_count` | `1` | `SimConfig.CulturalTraits.ResilientMinNearCollapseCount` | Resilient: civ must have survived this many near-collapse episodes |
| `resilient_near_collapse_pop_threshold` | `50` | `SimConfig.CulturalTraits.ResilientNearCollapsePopThreshold` | Near-collapse = total population dropped below this threshold Raised from 20 → 50: with min_viable_pop=5, TotalPopulation never reaches 5-19 while a settlement still exists. 50 captures civs with a single struggling hamlet. |
| `scholarly_min_discoveries` | `5` | `SimConfig.CulturalTraits.ScholarlyMinDiscoveries` | Scholarly: civ members must have made this many total scholar discoveries |
| `unstable_throne_min_successions` | `5` | `SimConfig.CulturalTraits.UnstableThroneMinSuccessions` | UnstableThrone: this many successions in the rolling window qualifies |
| `unstable_throne_years` | `50` | `SimConfig.CulturalTraits.UnstableThroneYears` | Rolling window size in years |

## `[emissary]` {#emissary}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `knowledge_spread_radius` | `30` | `SimConfig.Emissary.KnowledgeSpreadRadius` | tiles; civs within this range gain Rumor contact (vs WarProximityRadius ~8) |
| `rumor_confidence_gain` | `0.15` | `SimConfig.Emissary.RumorConfidenceGain` | confidence added per year of proximity rumor |
| `encounter_confidence_gain` | `0.35` | `SimConfig.Emissary.EncounterConfidenceGain` | confidence added on character cross-civ encounter |
| `confidence_decay_per_year` | `0.05` | `SimConfig.Emissary.ConfidenceDecayPerYear` | lost per year without contact |
| `rumor_chain_probability` | `0.05` | `SimConfig.Emissary.RumorChainProbability` | annual chance Civ A passes knowledge of Civ C to Civ B (per pair) |
| `rumor_chain_confidence_factor` | `0.5` | `SimConfig.Emissary.RumorChainConfidenceFactor` | chained rumors arrive at fraction of source confidence |
| `dispatch_check_years` | `5` | `SimConfig.Emissary.DispatchCheckYears` | ruler considers dispatching per this many years |
| `max_active_emissaries_per_civ` | `3` | `SimConfig.Emissary.MaxActiveEmissariesPerCiv` | cap on simultaneous in-transit emissaries |
| `emissary_travel_speed_tiles_per_year` | `8.0` | `SimConfig.Emissary.EmissaryTravelSpeedTilesPerYear` | how fast emissaries travel; affects delay and mortality |
| `trade_dispatch_min_trust` | `-0.1` | `SimConfig.Emissary.TradeDispatchMinTrust` | min character trust to send trade emissary |
| `diplomacy_dispatch_min_trust` | `0.6` | `SimConfig.Emissary.DiplomacyDispatchMinTrust` | min trust for diplomatic mission (must stay above trade_dispatch_min_trust, checked second) |
| `spy_dispatch_max_trust` | `0.2` | `SimConfig.Emissary.SpyDispatchMaxTrust` | spy missions target civs you don't trust well |
| `emissary_death_per_tile` | `0.008` | `SimConfig.Emissary.EmissaryDeathPerTile` | cumulative per-tile mortality rate |
| `emissary_min_survival_chance` | `0.2` | `SimConfig.Emissary.EmissaryMinSurvivalChance` | floor: even a 200-tile journey has 20% success |
| `trade_trust_gain` | `0.08` | `SimConfig.Emissary.TradeTrustGain` | Emissary outcomes (on arrival) |
| `trade_min_pop_for_goods` | `50` | `SimConfig.Emissary.TradeMinPopForGoods` | both civs need this pop to meaningfully trade |
| `diplomacy_alliance_min_trust` | `0.25` | `SimConfig.Emissary.DiplomacyAllianceMinTrust` | trust required after emissary to trigger AllianceFormed |
| `spy_confidence_boost` | `0.4` | `SimConfig.Emissary.SpyConfidenceBoost` | how much the contact confidence improves from spy intel |
| `religious_spread_awe_boost` | `0.3` | `SimConfig.Emissary.ReligiousSpreadAweBoost` | awe modifier added to target-civ chars on religious emissary |
| `confidant_trust_credit` | `0.7` | `SimConfig.Emissary.ConfidantTrustCredit` | fraction of a confidant friendship's Trust credited toward ruler trust |

## `[war]` {#war}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `max_war_duration_years` | `15` | `SimConfig.War.MaxWarDurationYears` | wars end by truce after this many years (was in [character]) |
| `max_active_wars` | `2` | `SimConfig.War.MaxActiveWars` | hard cap: a civ cannot fight more than this many simultaneous wars |
| `peace_cooldown_years` | `10` | `SimConfig.War.PeaceCooldownYears` | years of non-aggression after a war ends |
| `war_exhaustion_years_per_war` | `5` | `SimConfig.War.WarExhaustionYearsPerWar` | extra cooldown per prior war (1st=10yr, 2nd=15yr, 3rd=20yr) |
| `raid_damage_min` | `15` | `SimConfig.War.RaidDamageMin` | min HP damage to a settlement per character raid |
| `raid_damage_max` | `40` | `SimConfig.War.RaidDamageMax` | max HP damage to a settlement per character raid |
| `war_conquest_health_threshold` | `35` | `SimConfig.War.WarConquestHealthThreshold` | settlement health ≤ this at war expiry → conquest not truce |
| `war_surrender_pop_threshold` | `5` | `SimConfig.War.WarSurrenderPopThreshold` | civ total population below this → sue for peace (surrender) |
| `war_aggression_threshold` | `0.5` | `SimConfig.War.WarAggressionThreshold` | minimum ruler Aggression to consider DeclareWar (was in [character]) |
| `war_proximity_radius` | `40` | `SimConfig.War.WarProximityRadius` | tile radius within which settlements accumulate tension |
| `tension_accrual_per_pair` | `0.05` | `SimConfig.War.TensionAccrualPerPair` | tension per close settlement pair per year (× proximity × ruler Aggression; was 0.08) |
| `tension_decay_rate` | `0.20` | `SimConfig.War.TensionDecayRate` | fraction of tension lost when no proximate settlements exist (was 0.15) |
| `tension_war_threshold` | `1.4` | `SimConfig.War.TensionWarThreshold` | accumulated tension that triggers war when ruler is aggressive enough (was 1.2) |
| `personal_war_tension_fraction` | `0.6` | `SimConfig.War.PersonalWarTensionFraction` | fraction of tension_war_threshold at which a personal encounter justifies war |
| `territory_tension_per_adjacent_pair` | `0.015` | `SimConfig.War.TerritoryTensionPerAdjacentPair` | Campaign battles (M4 Phase 2) Border tension accrued per year for each adjacent territory tile pair between different civs. |
| `campaign_battle_damage` | `15` | `SimConfig.War.CampaignBattleDamage` | health damage dealt to target settlement per campaign victory |
| `campaign_battle_base_strength` | `0.5` | `SimConfig.War.CampaignBattleBaseStrength` | attacker strength when no character combatant is available |
| `military_strength_bonus_scale` | `1.0` | `SimConfig.War.MilitaryStrengthBonusScale` | bonus_military_strength (Scholar Metallurgy discovery): additive from each side's capital stockpile in campaign battle rolls, capped so it nudges rather than decides battles. |
| `military_strength_bonus_cap` | `0.3` | `SimConfig.War.MilitaryStrengthBonusCap` |  |
| `tiles_per_battle_win` | `2` | `SimConfig.War.TilesPerBattleWin` | tiles transferred per net battle victory (aWins - bWins) |
| `max_tiles_transferred_per_war` | `12` | `SimConfig.War.MaxTilesTransferredPerWar` | cap; prevents one decisive war from reshaping the world |
| `opportunistic_war_aggression_threshold` | `0.55` | `SimConfig.War.OpportunisticWarAggressionThreshold` | slightly higher bar for opportunistic wars |
| `succession_crisis_war_tension_mult` | `2.0` | `SimConfig.War.SuccessionCrisisWarTensionMult` | tension accrual × this when target is in succession crisis |
| `weak_neighbor_settlement_fraction` | `0.4` | `SimConfig.War.WeakNeighborSettlementFraction` | fraction of target settlements that must be infected/starving |
| `war_weak_neighbor_food_threshold` | `0.70` | `SimConfig.War.WarWeakNeighborFoodThreshold` | FoodPressureRatio below which a settlement counts as "weak" |
| `weak_neighbor_tension_bonus` | `0.25` | `SimConfig.War.WeakNeighborTensionBonus` | extra tension/year when target qualifies as weak neighbor |
| `resource_shortage_war_food_threshold` | `0.75` | `SimConfig.War.ResourceShortageWarFoodThreshold` | aggressor mean food ratio below this → it gains bonus tension |
| `resource_shortage_tension_bonus` | `0.20` | `SimConfig.War.ResourceShortageTensionBonus` | extra tension/year when aggressor is in food shortage |
| `coalition_trust_bonus` | `0.20` | `SimConfig.War.CoalitionTrustBonus` | trust boost per civ on each war declaration |
| `war_min_civ_pop` | `300` | `SimConfig.War.WarMinCivPop` | population floor: war probability is 0 below this (bottleneck civ) |
| `war_pop_ramp_range` | `700` | `SimConfig.War.WarPopRampRange` | population range over which war probability ramps 0→full (floor+range = fully unlocked) |
| `friendship_trust_threshold` | `0.6` | `SimConfig.War.FriendshipTrustThreshold` | min Trust between a non-ruler pair (one per civ) to count as a cross-civ friendship |
| `friendship_war_dampen_min` | `0.4` | `SimConfig.War.FriendshipWarDampenMin` | tension-accrual multiplier floor when the strongest such friendship is at max Trust |

## `[religion]` {#religion}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `spiritual_founding_threshold` | `0.75` | `SimConfig.Religion.SpiritualFoundingThreshold` | Spiritual need level to trigger FoundReligion goal |
| `piety_founding_threshold` | `0.50` | `SimConfig.Religion.PietyFoundingThreshold` | Piety skill floor to qualify as a founder |
| `wonder_founding_threshold` | `0.60` | `SimConfig.Religion.WonderFoundingThreshold` | Wonder personality trait floor |
| `religion_founding_progress_per_year` | `0.35` | `SimConfig.Religion.ReligionFoundingProgressPerYear` | progress per year; ~3 years to complete |
| `religion_founding_cooldown_years` | `50` | `SimConfig.Religion.ReligionFoundingCooldownYears` | per character: min years between foundings |

## `[family]` {#family}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `marriage_trust_threshold` | `0.6` | `SimConfig.Family.MarriageTrustThreshold` | Bond-goal trust required before either party proposes marriage |
| `marriage_compassion_threshold` | `0.4` | `SimConfig.Family.MarriageCompassionThreshold` | Compassion personality floor for proposing marriage |
| `marriage_min_age_seasons` | `240` | `SimConfig.Family.MarriageMinAgeSeasons` | minimum AgeSeason before a character can marry (15 years at 16 ticks/year) |
| `childbirth_chance_per_year` | `0.15` | `SimConfig.Family.ChildbirthChancePerYear` | per-annual-tick chance a co-located married couple conceives |
| `max_children_per_couple` | `4` | `SimConfig.Family.MaxChildrenPerCouple` | living-children cap before childbirth stops rolling for a couple |
| `trait_inheritance_weight` | `0.4` | `SimConfig.Family.TraitInheritanceWeight` | 0 = pure ancestry bias, 1 = pure parent-average trait roll |
| `newborn_family_loyalty` | `0.8` | `SimConfig.Family.NewbornFamilyLoyalty` | starting Loyalty for a newborn's Family membership |
| `kin_in_enemy_civ_war_dampen_min` | `0.2` | `SimConfig.Family.KinInEnemyCivWarDampenMin` | War/Raid score multiplier floor when a Family relative lives in the target civ |
| `estrangement_trust_threshold` | `0.65` | `SimConfig.Family.EstrangementTrustThreshold` | M13 13.5: annual check — married-edge Trust at/below this ends the marriage |
| `marriage_hardship_need_threshold` | `0.4` | `SimConfig.Family.MarriageHardshipNeedThreshold` | either spouse's Food/Safety below this counts as hardship |
| `marriage_hardship_trust_drain` | `0.5` | `SimConfig.Family.MarriageHardshipTrustDrain` | annual Trust drain on the marriage edge during hardship (raised 2026-08-03, see threshold comment above) |
| `childbirth_trust_gain` | `0.05` | `SimConfig.Family.ChildbirthTrustGain` | marital Trust bump per successful childbirth |

## `[debt]` {#debt}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `aid_trust_threshold` | `0.4` | `SimConfig.Debt.AidTrustThreshold` | granter must already trust the recipient this much before offering aid |
| `aid_need_threshold` | `0.3` | `SimConfig.Debt.AidNeedThreshold` | recipient's Food or Safety must be below this to qualify for aid |
| `aid_debt_increment` | `0.3` | `SimConfig.Debt.AidDebtIncrement` | \|Debt\| added toward the granter per GrantAid |
| `aid_trust_gain` | `0.15` | `SimConfig.Debt.AidTrustGain` | Trust gained by both parties from the exchange |
| `aid_need_restore` | `0.25` | `SimConfig.Debt.AidNeedRestore` | recipient's triggering need restored by this much |
| `debt_war_dampen_min` | `0.3` | `SimConfig.Debt.DebtWarDampenMin` | War/Raid score multiplier floor when maximally indebted to someone in the target civ |
| `forgive_trust_threshold` | `0.6` | `SimConfig.Debt.ForgiveTrustThreshold` | creditor must trust the debtor this much before forgiving |
| `forgive_min_debt` | `0.2` | `SimConfig.Debt.ForgiveMinDebt` | minimum \|Debt\| owed before forgiveness is considered |
| `forgive_trust_gain` | `0.2` | `SimConfig.Debt.ForgiveTrustGain` | Trust gained by both parties when debt is forgiven |
| `oath_break_trust_penalty` | `0.4` | `SimConfig.Debt.OathBreakTrustPenalty` | M13 13.5: Trust lost on the violated edge when a debtor wars/raids their own creditor's civ instead |
| `tier1_aid_priority_bonus` | `1.2` | `SimConfig.Debt.Tier1AidPriorityBonus` | 2026-08-03: a Tier1-Tier1 aid candidate scores identically to the far more common Tier2 shortcut, so whichever tier a granter's radius scan reaches first wins the tick's single best-candidate slot — Tier1-Tier1 Debt never won that tie. Breaks the tie in the Tier1 pair's favor. |
| `tier1_aid_need_threshold` | `0.9` | `SimConfig.Debt.Tier1AidNeedThreshold` | Separate, milder need threshold for the same-civ Tier1-Tier1 shortcut, so the already-calibrated Tier2 Debt volume (aid_need_threshold above) isn't touched — two same-civ Tier1s co-located AND one hitting a full crisis (<0.3) simultaneously proved too rare to ever fire in calibration. |

## `[fear]` {#fear}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `rivalry_base_fear_increment` | `0.1` | `SimConfig.Fear.RivalryBaseFearIncrement` | Fear added when a rivalry first forms (matches the original hardcoded value) |
| `rivalry_fear_power_scale` | `0.5` | `SimConfig.Fear.RivalryFearPowerScale` | extra Fear from the target's Combat/Aggression edge over the declarer's |
| `placate_fear_threshold` | `0.05` | `SimConfig.Fear.PlacateFearThreshold` | minimum Fear toward an existing rival before appeasement becomes attractive |
| `placate_aggression_max` | `0.4` | `SimConfig.Fear.PlacateAggressionMax` | only low-Aggression characters placate; aggressive ones confront despite fear |
| `placate_fear_reduction` | `0.05` | `SimConfig.Fear.PlacateFearReduction` | Fear reduced per successful Placate — several placations drain a rivalry, not one |
| `placate_trust_gain` | `0.2` | `SimConfig.Fear.PlacateTrustGain` | Trust nudge from successful placation |
| `fear_war_dampen_min` | `0.3` | `SimConfig.Fear.FearWarDampenMin` | War/Raid score multiplier floor when maximally feared of someone in the target civ |
| `reconciliation_fear_threshold` | `0.1` | `SimConfig.Fear.ReconciliationFearThreshold` | M13 13.5: Placate ends the rivalry outright once Fear drops to/below this... |
| `reconciliation_trust_threshold` | `0.3` | `SimConfig.Fear.ReconciliationTrustThreshold` | ...and Trust has risen to/above this |
| `feud_trust_penalty` | `0.2` | `SimConfig.Fear.FeudTrustPenalty` | extra Trust lost when a rivalry is re-declared while already active |
| `feud_fear_increment` | `0.15` | `SimConfig.Fear.FeudFearIncrement` | extra Fear added on the same escalation |

## `[defection]` {#defection}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `confidant_trust_threshold` | `0.7` | `SimConfig.Defection.ConfidantTrustThreshold` | min Trust with a co-located foreign confidant to be considered for defection |
| `wellbeing_crisis_threshold` | `-0.3` | `SimConfig.Defection.WellbeingCrisisThreshold` | Wellbeing must be at or below this before asylum-seeking becomes attractive |
| `post_defection_trust_gain` | `0.2` | `SimConfig.Defection.PostDefectionTrustGain` | Trust gained between defector and confidant once the defection succeeds |
| `defection_cooldown_ticks` | `64` | `SimConfig.Defection.DefectionCooldownTicks` | ticks before the same character can defect again (4 years at 16 ticks/year) |

## `[unrest]` {#unrest}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `unrest_comfort_radius` | `15` | `SimConfig.Unrest.UnrestComfortRadius` | tiles; within this range, distance driver is zero |
| `unrest_distance_per_tile` | `0.005` | `SimConfig.Unrest.UnrestDistancePerTile` | unrest/tile/year beyond comfort_radius (was 0.003) |
| `unrest_soft_city_threshold` | `4` | `SimConfig.Unrest.UnrestSoftCityThreshold` | cities before size-driver kicks in (was 6; reduced 2026-07-21) |
| `unrest_per_excess_city` | `0.035` | `SimConfig.Unrest.UnrestPerExcessCity` | unrest/yr per excess city above threshold (was 0.05 → 0.035; overtuned) |
| `unrest_famine_bonus` | `0.15` | `SimConfig.Unrest.UnrestFamineBonus` | extra unrest/yr when settlement is in food crisis |
| `unrest_succession_mult` | `1.5` | `SimConfig.Unrest.UnrestSuccessionMult` | multiplier on all unrest sources during crisis |
| `cohesion_bonus_scale` | `1.0` | `SimConfig.Unrest.CohesionBonusScale` | bonus_civ_cohesion (Artisan notable work): subtracted from accrual before clamping, capped. |
| `cohesion_bonus_cap` | `0.1` | `SimConfig.Unrest.CohesionBonusCap` |  |
| `unrest_decay_rate` | `0.10` | `SimConfig.Unrest.UnrestDecayRate` | fraction decayed per year (10%/yr → half-life ~7 years) |
| `unrest_secession_threshold` | `0.80` | `SimConfig.Unrest.UnrestSecessionThreshold` | unrest level that makes a settlement eligible to secede (was 0.70; overtuned) |
| `unrest_secession_chance` | `0.30` | `SimConfig.Unrest.UnrestSecessionChance` | annual probability of secession when above threshold (was 0.40) |
| `unrest_cluster_radius` | `25` | `SimConfig.Unrest.UnrestClusterRadius` | tile radius; nearby same-civ high-unrest settlements may join (was 15; S4 — |
| `unrest_cluster_min_unrest` | `0.30` | `SimConfig.Unrest.UnrestClusterMinUnrest` | min unrest for a neighbour to join the seceding cluster (was 0.50; S4) |
| `splinter_initial_tension` | `0.40` | `SimConfig.Unrest.SplinterInitialTension` | initial BorderTension the new civ has toward its parent (was 0.60; overtuned) |
| `secession_min_civ_pop` | `500` | `SimConfig.Unrest.SecessionMinCivPop` | population floor: secession probability is 0 below this |
| `secession_pop_ramp_range` | `500` | `SimConfig.Unrest.SecessionPopRampRange` | population range over which secession probability ramps 0→full (floor+range = fully unlocked) |

## `[utility_affinity.goal_affinity]` {#utility-affinitygoal-affinity}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `survive` | `{ rest = 0.8, travel = 0.4 }` | `SimConfig.UtilityAffinity.GoalAffinity.Survive` | Survive goal: rest is core survival, travel can find food/safety |
| `dominance` | `{ war = 1.0, raid = 0.8 }` | `SimConfig.UtilityAffinity.GoalAffinity.Dominance` | Dominance goal: war is the primary expression; raid is an alternative |
| `alliance` | `{ ally = 1.0, negotiate = 0.5 }` | `SimConfig.UtilityAffinity.GoalAffinity.Alliance` | Alliance goal: formal ally pact is ideal; negotiation builds toward it |
| `create` | `{ create = 1.0 }` | `SimConfig.UtilityAffinity.GoalAffinity.Create` | Create goal: creation action is the only outlet |
| `bond` | `{ ally = 1.0, negotiate = 0.4, marry = 0.9 }` | `SimConfig.UtilityAffinity.GoalAffinity.Bond` | Bond goal: allying with a specific person is the goal; negotiation also builds bond |
| `avenge` | `{ raid = 0.9, war = 0.8 }` | `SimConfig.UtilityAffinity.GoalAffinity.Avenge` | Avenge goal: raid is primary vengeance; war is the larger-scale option |
| `acquire` | `{ raid = 0.7, travel = 0.5 }` | `SimConfig.UtilityAffinity.GoalAffinity.Acquire` | Acquire goal: raid to take; travel to find |
| `flee` | `{ flee = 1.0, travel = 0.6, defect = 0.5 }` | `SimConfig.UtilityAffinity.GoalAffinity.Flee` | Flee goal: fleeing is direct; travel moves toward safety Flee goal also covers Defect (M13 13.4): fleeing one's own civ for a trusted foreign confidant is a form of escape, distinct from FleeRegion's disaster-avoidance sense of the same goal. |
| `grieve` | `{ rest = 0.7 }` | `SimConfig.UtilityAffinity.GoalAffinity.Grieve` | Grieve goal: rest and withdrawal (stays put, processes loss) |
| `endure` | `{ rest = 0.9 }` | `SimConfig.UtilityAffinity.GoalAffinity.Endure` | Endure goal: rest heavily (hunker down through hardship) |
| `protect` | `{ travel = 0.4 }` | `SimConfig.UtilityAffinity.GoalAffinity.Protect` | Protect goal: travel toward the protected person or settlement |
| `build_improvement` | `{ build_improvement = 1.0, rest = 0.3 }` | `SimConfig.UtilityAffinity.GoalAffinity.BuildImprovement` | BuildImprovement goal: build directly; rest also advances (staying put counts as progress) |
| `found_city` | `{ found_city = 1.0, travel = 0.8 }` | `SimConfig.UtilityAffinity.GoalAffinity.FoundCity` | FoundCity goal: founding directly; travel to find a good site |
| `slay_beast` | `{ hunt_beast = 1.0, travel = 0.3 }` | `SimConfig.UtilityAffinity.GoalAffinity.SlayBeast` |  |
| `sea_voyage` | `{ sea_voyage = 1.0, travel = 0.3 }` | `SimConfig.UtilityAffinity.GoalAffinity.SeaVoyage` | SeaVoyage goal (M11): sea_voyage is the direct move-across-water action; ordinary travel is a weaker fallback for the (rare) case where the voyage step happens to also be a land move. |

## `[utility_affinity.action_needs]` {#utility-affinityaction-needs}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `rest` | `{ safety = 0.20, food = 0.20, shelter = 0.15, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Rest` | Rest: depleted safety or food drives camping; depleted shelter also pushes rest/camp. Original: (2 - safety - food) * 0.2 + (1 - shelter) * 0.15 Decomposed: safety * 0.2 + food * 0.2 + shelter * 0.15 (each need contributes independently). |
| `establish` | `{ shelter = 0.70, status = 0.30, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Establish` | Establish: build a settlement when homeless (shelter) or seeking status |
| `ally` | `{ belonging = 0.60, safety = 0.40, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Ally` | Ally: form bond when lonely (belonging) or unsafe |
| `negotiate` | `{ belonging = 0.50, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Negotiate` | Negotiate: primarily a belonging action (meet needs through talking) |
| `war` | `{ status = 0.70, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.War` | War: status-driven; fighting for dominance satisfies the status need |
| `raid` | `{ status = 0.50, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Raid` | Raid: status-driven like war, but smaller-scale (lower coefficient) |
| `travel` | `{ safety = 0.30, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Travel` | Travel: safety-driven; moving away from danger satisfies safety need |
| `rivalry` | `{ status = 0.40, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Rivalry` | Rivalry: status-driven; establishing dominance over a specific rival |
| `build_improvement` | `{ purpose = 0.50, status = 0.20, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.BuildImprovement` | BuildImprovement: purpose-driven with a status bonus (craftwork has social recognition) |
| `found_city` | `{ shelter = 0.50, status = 0.50, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.FoundCity` | FoundCity: shelter + status (city-founding is the ultimate expression of both) |
| `hunt_beast` | `{ purpose = 0.40, status = 0.30, _default = 0.1 }` | `SimConfig.UtilityAffinity.ActionNeeds.HuntBeast` |  |
| `sea_voyage` | `{ purpose = 0.50, status = 0.50, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.SeaVoyage` | SeaVoyage: purpose + status, same flavor as FoundCity (a civ-level expansion act) |
| `create` | `{ _default = 0.1 }` | `SimConfig.UtilityAffinity.ActionNeeds.Create` | create, flee: no need-based score — rely on goal advancement and personality only _default = 0.1 matches the original _ => 0.1f fallback for all unlisted actions |
| `flee` | `{ _default = 0.1 }` | `SimConfig.UtilityAffinity.ActionNeeds.Flee` |  |
| `marry` | `{ belonging = 0.60, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Marry` | Marry: belonging-driven, same flavor as Ally |
| `grant_aid` | `{ belonging = 0.50, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.GrantAid` | GrantAid (M13 13.2): belonging-driven — helping a trusted, needy companion |
| `forgive_debt` | `{ belonging = 0.40, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.ForgiveDebt` | ForgiveDebt (M13 13.2): belonging-driven — releasing an obligation to repair a bond |
| `placate` | `{ safety = 0.50, belonging = 0.20, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Placate` | Placate (M13 13.1): safety-driven — appeasing a feared rival is fundamentally self-preservation |
| `defect` | `{ belonging = 0.50, safety = 0.30, _default = 0.0 }` | `SimConfig.UtilityAffinity.ActionNeeds.Defect` | Defect (M13 13.4): belonging-driven — seeking asylum with a trusted foreign confidant instead of enduring an unsafe/unwelcoming home civ. |

## `[wildlife_risk]` {#wildlife-risk}

_─── Wildlife raid risk by biome (D3) ───────────────────────────────────────── Per-biome multiplier applied to settlement.wildlife_attack_base_chance. Extracted from BiomeWildlifeRisk() in PopulationDynamicsPhase.cs. All values identical to the previous hardcoded C# switch. Design: dense cover gives predators ambush advantage (>1.0); open terrain gives defenders visibility (<1.0). default_risk: fallback for any biome not listed here (was _ => 0.6f). Tune individual biomes to shift where wildlife raids concentrate. Note: ocean/coastal_water/beach are never settlement tiles but are listed with default values so the table is complete for reference._

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `default_risk` | `0.6` | `SimConfig.WildlifeRisk.DefaultRisk` | fallback for unlisted biomes; matches original _ => 0.6f |

## `[wildlife_risk.biome_risk]` {#wildlife-riskbiome-risk}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `tropical_rainforest` | `2.0` | `SimConfig.WildlifeRisk.BiomeRisk.TropicalRainforest` | dense jungle canopy — hardest to defend; predator's home turf |
| `swamp` | `1.5` | `SimConfig.WildlifeRisk.BiomeRisk.Swamp` | murky terrain, poor visibility, many ambush points |
| `boreal_forest` | `1.6` | `SimConfig.WildlifeRisk.BiomeRisk.BorealForest` | northern conifer forest; wolves/bears are apex threats |
| `temperate_forest` | `1.4` | `SimConfig.WildlifeRisk.BiomeRisk.TemperateForest` | mixed woodland; moderate ambush risk |
| `grassland` | `1.0` | `SimConfig.WildlifeRisk.BiomeRisk.Grassland` | open but with enough grass cover for ambush (baseline) |
| `hills` | `0.9` | `SimConfig.WildlifeRisk.BiomeRisk.Hills` | rolling terrain; some cover, some visibility |
| `mountain` | `0.8` | `SimConfig.WildlifeRisk.BiomeRisk.Mountain` | sparse vegetation; predators visible at range |
| `savanna` | `0.6` | `SimConfig.WildlifeRisk.BiomeRisk.Savanna` | sparse trees; good long-range visibility for defenders |
| `plains` | `0.5` | `SimConfig.WildlifeRisk.BiomeRisk.Plains` | open farmland; predators easily spotted |
| `tundra` | `0.5` | `SimConfig.WildlifeRisk.BiomeRisk.Tundra` | sparse cover; cold limits predator density |
| `desert` | `0.4` | `SimConfig.WildlifeRisk.BiomeRisk.Desert` | near-barren; limited wildlife population to threaten settlers |
| `high_mountain` | `0.3` | `SimConfig.WildlifeRisk.BiomeRisk.HighMountain` | alpine — too inhospitable for most large predators |
| `volcanic` | `0.4` | `SimConfig.WildlifeRisk.BiomeRisk.Volcanic` | hostile terrain limits wildlife population |

## `[artifacts]` {#artifacts}

| Key | Value | C# Property | Description |
|-----|-------|-------------|-------------|
| `base_generation_probability` | `0.05` | `SimConfig.Artifacts.BaseGenerationProbability` | At max skill, probability per crafting task that a legendary artifact is produced. |
| `notable_performance_threshold` | `0.75` | `SimConfig.Artifacts.NotablePerformanceThreshold` | Quality score above which a crafting performance is considered notable for artifact rolls. |
| `covet_threshold` | `0.6` | `SimConfig.Artifacts.CovetThreshold` | Artifact quality score above which ambitious characters will covet the item. |
| `battle_forge_probability` | `0.03` | `SimConfig.Artifacts.BattleForgeProbability` | Chance a decisive battle forges a legendary artifact. |
| `heroic_death_forge_probability` | `0.10` | `SimConfig.Artifacts.HeroicDeathForgeProbability` | Chance a legendary character's combat death forges a legacy artifact. |
| `lost_on_death_probability` | `0.35` | `SimConfig.Artifacts.LostOnDeathProbability` | Probability an artifact becomes Lost (vs. inherited by settlement) on owner death. |
| `covet_ambition_threshold` | `0.55` | `SimConfig.Artifacts.CovetAmbitionThreshold` | Minimum Ambition personality score to form covet-artifact goals (W2 covet & goal-seeking). |
| `covet_max_goals` | `2` | `SimConfig.Artifacts.CovetMaxGoals` | Maximum simultaneous covet-artifact goals per character. |
| `lost_artifact_annual_decay` | `0.008` | `SimConfig.Artifacts.LostArtifactAnnualDecay` | Annual probability a Lost (ownerless) artifact is destroyed — primary sink bounding accumulation. |
| `owned_artifact_annual_decay` | `0.001` | `SimConfig.Artifacts.OwnedArtifactAnnualDecay` | Annual probability an owned artifact is destroyed by accident/disaster/war (owners protect relics). |
| `battle_category_weight_weapon` | `0.5` | `SimConfig.Artifacts.BattleCategoryWeightWeapon` | M9 G-2: weighted category roll for battle-forged artifacts (no CreatedGoodType context exists for combat-triggered forging, so these are independent weights; must sum to ~1.0). |
| `battle_category_weight_armor` | `0.35` | `SimConfig.Artifacts.BattleCategoryWeightArmor` |  |
| `battle_category_weight_regalia` | `0.15` | `SimConfig.Artifacts.BattleCategoryWeightRegalia` |  |
| `heroic_death_category_weight_weapon` | `0.5` | `SimConfig.Artifacts.HeroicDeathCategoryWeightWeapon` | M9 G-2: weighted category roll for heroic-death artifacts (must sum to ~1.0). |
| `heroic_death_category_weight_relic` | `0.3` | `SimConfig.Artifacts.HeroicDeathCategoryWeightRelic` |  |
| `heroic_death_category_weight_regalia` | `0.2` | `SimConfig.Artifacts.HeroicDeathCategoryWeightRegalia` |  |
