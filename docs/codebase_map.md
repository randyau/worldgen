# Codebase Map
<!-- GENERATED from <summary> XML docs — edit the source <summary>, not this file. -->
<!-- Regenerate: python3 scripts/gen-map.py -->
One-line description of every non-trivial source file. Check here before running `find`. Updated when files are added/removed.

## WorldEngine.Sim/Civilizations/
- `CivTracker.Diplomacy.cs` — Resolves character commands that affect civilizations, settlements, and relationships. Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
- `CivTracker.Naming.cs` — Resolves character commands that affect civilizations, settlements, and relationships. Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
- `CivTracker.Unrest.cs` — Resolves character commands that affect civilizations, settlements, and relationships. Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
- `CivTracker.War.cs` — Resolves character commands that affect civilizations, settlements, and relationships. Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
- `CivTracker.cs` — Resolves character commands that affect civilizations, settlements, and relationships. Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
- `Civilization.cs` — Mutable civ class: ruler, members, war state, border tension; M3.5: CulturalProfile.
- `CulturalProfile.cs` — Immutable cultural snapshot for a civilization, derived from its founding ancestry and any acquired cultural traits (Phase 3.2). Computed once at civ founding and updated when new traits are acquired via CivTracker.BuildCulturalProfile.
- `EmissaryTypes.cs` — How one civilization learned of another — ranked by fidelity (higher = better known).
- `SettlementStub.cs` — Lightweight settlement record. Population is dynamic from Phase 2.4 onward.

## WorldEngine.Sim/Commands/
- `PlayerCommands.cs` — UI-to-sim command records: SetSimSpeed, PauseToggle, StepOneTick, SetViewport (no-op); routed via CommandQueue.

## WorldEngine.Sim/Config/
- `AncestryConfig.cs` — All data for one ancestry, loaded from config/ancestries.toml. Personality and aptitude fields are bias offsets added to the Gaussian mean (base 0.5).
- `AncestryLoader.cs` — Loads ancestries.toml into AncestryConfig instances.
- `AncestryRegistry.cs` — Loaded set of all ancestry configs. Accessible via world.SimConfig.AncestryRegistry. Provides biome-weighted ancestry sampling and cross-ancestry trust lookups.
- `BeastsSimConfig.cs` — Beast lifecycle constants from the [beasts] section of sim_config.toml. Species-specific values (health, strength, etc.) live in config/beasts.toml.
- `BiomeThresholdConfig.cs` — Elevation, temperature, and moisture thresholds for biome classification.
- `CharacterNamesConfig.cs` — Name pools for procedurally naming characters (first and last names).
- `CharacterSimConfig.cs` — Character behavior constants: needs decay, skill growth, diplomacy (war knobs in WarConfig).
- `ClimateConfig.cs` — Temperature/moisture gradients, storm corridors, and climate drift constants.
- `CulturalTraitsConfig.cs` — Thresholds that govern when a civilization acquires a permanent cultural trait. All constants loaded from [cultural_traits] section in sim_config.toml.
- `DisasterConfig.cs` — Disaster probability and damage constants (wildfire, flood, eruption, earthquake, drought).
- `ElevationConfig.cs` — FastNoiseLite terrain noise and mountain/tectonic thresholds for world generation.
- `EmissaryConfig.cs` — All constants governing the civ awareness and emissary system (M4.1). Loaded from the [emissary] section of sim_config.toml.
- `EventsConfig.cs` — Nested config under [events.gate] in sim_config.toml. Controls which event types are always or never recorded, independent of tier.
- `ImprovementsConfig.cs` — Tile improvement food/production multipliers and build-cost constants.
- `ResourcePressureConfig.cs` — Food/water/resource pressure constants: shortage threshold, famine onset, and carrying-capacity weights.
- `ResourcesConfig.cs` — Per-resource deposit density fractions used during world generation (iron, copper, tin, precious metals).
- `RiversConfig.cs` — River flow accumulation threshold and lake detection constants for world generation.
- `SettlementConfig.cs` — Population growth rates, carrying capacity, and crystallisation threshold constants.
- `SettlementNamesConfig.cs` — Prefix/suffix pools for procedural settlement name generation.
- `SimConfig.cs` — Root config container; all subsections loaded from sim_config.toml.
- `SimConfigLoader.cs` — Tomlyn-based TOML loader; strict mode detects unbound keys; supports profile overlays and --set overrides.
- `SimConfigValidator.cs` — Validates a loaded SimConfig for range, ordering, and cross-field invariants. Called automatically by SimConfigLoader after deserialization. Throws
- `SimLoopConfig.cs` — Adds TicksPerSeason (alias) and TicksPerYear (= TicksPerSeasonalChange × 4) derived properties; use these everywhere instead of hardcoded 16.
- `TectonicsConfig.cs` — Tectonic plate count, separation, and continental/oceanic ratio constants for world generation.
- `UnrestConfig.cs` — Configuration for the settlement unrest / secession mechanic (S2 splinter). All tunable constants for unrest accumulation, decay, and the secession trigger. Bound from the
- `UtilityAffinityConfig.cs` — Configures the two UtilityScorer lookup tables: 1. Goal → action affinity weights (how well each action advances each goal type). 2. Action base-score need-weights (how much each need's deficit drives each action). TOML section: [utility_affinity] Sub-tables: [utility_affinity.goal_affinity] — goal-name → { action-name = weight } [utility_affinity.action_needs] — action-name → { need-name = coefficient, _default = fallback } Unmapped (goal, action) pairs default to 0.0. Unmapped action need-weights use _default (0.1 for the original fallback, 0.0 for unlisted actions).
- `WarConfig.cs` — All war-system configuration — consolidated from [character] + [war] in D5. Loaded from the [war] section of sim_config.toml.
- `WildlifeRiskConfig.cs` — Per-biome wildlife-raid risk multiplier table (D3 extraction from PopulationDynamicsPhase). TOML section: [wildlife_risk] Each key is a BiomeType name in snake_case (e.g. tropical_rainforest, boreal_forest). The value is a float multiplier applied to
- `WorldGenConfig.cs` — World generation parameters: tile size, world dimensions, chunk size, and per-subsystem generation configs.

## WorldEngine.Sim/Core/
- `CommandQueue.cs` — Unbounded channel connecting the UI thread (Enqueue) to the sim thread (DrainAll). Thread-safe by Channel design — no additional locking needed.
- `DisasterSalts.cs` — RNG salt constants for disaster phase; keeps disaster rolls reproducible and independent.
- `Enumerations.cs` — All enums: BiomeType, Season, SimPhase, EntityKind, EventType, EventTier, VerbClass, etc.
- `ICommand.cs` — Marker interface for simulation commands. All implementations must be sealed records with value-type fields only. No callbacks, delegates, or mutable object references.
- `WorldConfig.cs` — World generation parameters: seed, tile dimensions, and world size in km.
- `WorldRng.cs` — Deterministic RNG: FloatAt(seed, tick, x, y, salt) — use salts from SimRngSalts.

## WorldEngine.Sim/Entities/
- `EntityCommands.cs` — All entity ICommand records (MoveTo, Rest, etc.) in one file; sealed records with value-type fields only.
- `EntityRegistry.cs` — Canonical store for all live entities. Owned by the sim thread. Maintains a spatial index (tile → entity set) for fast proximity lookups.
- `EntitySnapshot.cs` — Immutable UI-facing summary of one entity. Read by the UI thread from WorldSnapshot. Heavy entity data stays on the sim thread inside EntityRegistry.
- `IEntity.cs` — The core simulation entity interface. Every simulated object implements this. Entities NEVER mutate world state directly. They emit ICommand instances during the EMIT step which are resolved by CommandResolver in the RESOLVE step.
- `SimEntity.cs` — Abstract base for all named, tracked simulation entities. Holds the fields shared across Tier1Character, Tier2Character, and LegendaryBeast — Id, Location, lifecycle state, and health/aging — so subclasses own only their tier-specific behaviour.

## WorldEngine.Sim/Entities/Beasts/
- `BeastCatalog.cs` — Queryable, in-memory view of the beast species catalog loaded from config/beasts.toml.
- `BeastCatalogFile.cs` — Top-level wrapper for beasts.toml deserialization. Tomlyn maps [[beasts]] arrays to the Beasts list via snake_case conversion.
- `BeastCatalogLoader.cs` — Loads beasts.toml into BeastCatalog instances.
- `BeastFactory.cs` — Creates LegendaryBeast instances from a species config and placement parameters. All randomness is seeded via WorldRng for reproducibility.
- `BeastSpawnConfig.cs` — Global beast spawn settings from the [beast_spawn] section of config/beasts.toml.
- `BeastSpawner.cs` — Populates EntityRegistry with initial beasts and builds the BeastEmergenceSchedule for deferred mythological creature spawns. Called once, after world gen, before the first sim tick.
- `BeastSpeciesConfig.cs` — Configuration for one beast species loaded from config/beasts.toml.
- `CombatConfig.cs` — Combat resolution parameters from the [combat] section of config/beasts.toml.
- `LegendaryBeast.cs` — A named, tracked beast entity. "Legendary" refers to the entity tier (named, historically significant), not necessarily to IsLegendary which marks a legendary specimen. All beasts — from a common wolf to a Dragon — are instances of this class.

## WorldEngine.Sim/Entities/Characters/
- `CharacterFactory.cs` — Creates Tier1Character instances with seeded-random traits.
- `CharacterSnapshot.cs` — Immutable UI-facing summary of a Tier 1 character. Carried in WorldSnapshot; only key fields for display purposes.
- `CharacterSpawner.cs` — Populates the world with initial Tier 1 characters at world start. Characters are placed on fertile land tiles, one per tile.
- `GoalData.cs` — GoalType enum and GoalData record: type, target, priority, staleness, and resolution tracking.
- `GoalManager.cs` — Goal formation, priority, staleness, and resolution for Tier1 characters (~357 lines).
- `IdentityData.cs` — Immutable identity record for a character: name, epithet, ancestry, and birth/death metadata.
- `LivelihoodData.cs` — Describes a Tier 2 character's role, affiliation, and economic position.
- `NeedsUpdater.cs` — Applies per-tick need decay and environmental boosts to character NeedsVector.
- `RelationshipEdge.cs` — RelationshipFlags (ally/rival/etc.) and RelationshipEdge record tracking character-to-character bonds.
- `RelationshipGraph.cs` — Centralized relationship store. NOT on entity objects. Canonical key: (Min(a,b), Max(a,b)) — independent of query direction. Maintains a per-entity adjacency index so GetAll/CountAlliances are O(degree) rather than O(all edges), preventing the O(n²) scan that accumulates as the graph grows over long simulations.
- `Tier1Character.cs` — A named Tier 1 character — hero, warlord, or ruler. Makes utility-scored decisions each season via EmitCommands.
- `Tier2Character.cs` — A named Tier 2 character — specialist or authority figure below hero/ruler status. Uses simplified 4-need model and fixed role behaviors instead of utility scoring.
- `Tier2Role.cs` — Specialist role enum for Tier2 characters: General, Governor, Merchant, Scholar, Physician, Artisan.
- `Tier2Spawner.cs` — Populates the world with Tier 2 characters proportional to settlement population at world start.
- `UtilityScorer.cs` — Scores candidate actions for a Tier 1 character and selects one via softmax. Holds pre-baked lookup tables (goal→action affinity, action need-weights) built once at construction from

## WorldEngine.Sim/Events/
- `EventGate.cs` — Pre-write gate deciding whether an event is recorded to the history log. God Mode events are always recorded; otherwise suppressed types and sub-minimum-tier events are dropped.
- `SignificanceClassifier.cs` — Maps a (type, payload, isFirstOfKind) tuple to an

## WorldEngine.Sim/Persistence/
- `CausalEdgeBuilder.cs` — Post-sim pass that infers causal relationships between events and writes them to the CausalEdges table with typed EdgeType labels.
- `DatabaseSchema.cs` — Schema DDL for SQLite: Events, SignificanceScore, CausalEdges, CharacterSummaries, CivSummaries, Eras, SuccessionChain, Dynasties, CivTraits, yearly_metrics.
- `EventStore.cs` — SQLite-backed event store. Holds a single persistent connection (required so that an in-memory database survives between calls). Implements
- `HistoryQueryService.cs` — SQLite-backed implementation of
- `SignificanceRescoringPass.cs` — Post-sim pass: upgrades event tiers based on downstream outcomes and computes final SignificanceScore for all events. Run once after the simulation ends (or on-demand before narrative generation) via
- `SummaryBuilder.cs` — Post-sim pass that scans the event log and populates pre-aggregated summary tables: CharacterSummaries, CivSummaries, SuccessionChain, Dynasties, and Eras. Call via
- `WorldStateDto.cs` — Lightweight save metadata written to meta.json. Checked on load for version compat.
- `WorldStateSaver.cs` — Saves and loads WorldState to/from a save directory. Format: meta.json (version/summary), state.bin (full world state JSON), config_snapshot/.
- `YearlyMetricsRow.cs` — One row of the

## WorldEngine.Sim/Simulation/
- `EventCache.cs` — Fixed-capacity ring buffer of recent SimEvents. Add() and GetRecent() are sim-thread-only. Thread safety via StateCache wrapping snapshots.
- `FoodAuditSink.cs` — Optional audit sink for per-tile food factor breakdowns. Passed to ResourcePressurePhase when
- `MetricsCollector.cs` — Samples world state once per in-game year and writes a row to the
- `PhaseRunner.cs` — Runs the 7 simulation phases in order each tick. Phase 1 (Environmental) produces PendingEvents consumed by Phase 7 (EventGeneration). All other phases are stubs in M1.
- `SimLoop.cs` — Background simulation thread. Ticks WorldState, builds snapshots, commits to StateCache. Only the background thread touches WorldState. UI thread only reads StateCache.

## WorldEngine.Sim/Simulation/Phases/
- `CharacterBehaviorPhase.cs` — Phase 5 — updates all Tier 1 characters each tick: needs decay, goal management, action selection (utility scoring), lifecycle (aging, death), command resolution (settlement, war, etc.).
- `EntityBehaviorPhase.cs` — SimPhase 4 — EntityBehavior. Each season tick: update beast needs/lifecycle, emit commands, resolve them. Beast emergence schedule is checked annually.
- `EnvironmentalPhase.cs` — Phase 1 — Environmental: seasonal climate, annual drift, disaster system, resource dynamics, sea level changes. Direct mutator — never called from UI thread.
- `KnowledgePropagationPhase.cs` — Annual phase (Spring) that fills Civilization.KnownCivs via three mechanisms: 1. Proximity rumor — civs with settlements within knowledge_spread_radius gain contact 2. Decay — existing contacts lose confidence each year without a refresh mechanism 3. Rumor chaining — one-hop indirect propagation of non-Rumor contacts Character-encounter seeding (mechanism 4) is wired in CharacterBehaviorPhase via CivTracker.SeedCivContact at the cross-civ encounter point.
- `PopulationDynamicsPhase.cs` — Phase 3 — per-season settlement population growth/decay, specialist crystallization, and abandonment. Replaces the PopulationDynamics stub.
- `ResourcePressurePhase.cs` — Each tick: 1. Computes each settlement's "reach" — the set of tiles it can exploit. 2. Builds an extensible resource ledger (Dictionary keyed by resource type string) from reach tiles: food (fertility × moisture), timber (forest biomes), and any mineral deposits. New resource types added to config flow through automatically without code changes. 3. Seeds Acquire / Flee goals on resident characters when ledger shows deficits. 4. Emits SettlementStraining events (rate-limited) for significant shortages.
- `TerritoryPhase.cs` — Annual phase (Spring only): grows or contracts each city's territory based on population. Expansion: claims the highest-fertility unclaimed adjacent tile, up to TerritoryGrowthPerYear per city. Contraction: releases the tiles farthest from the city center when population has dropped.
- `Tier2BehaviorPhase.cs` — Phase 5b — updates Tier 2 characters each tick. Needs decay, role behavior (fixed per Tier2Role), lifecycle, crystallization.

## WorldEngine.Sim/Tiles/
- `ChunkSummaryFlags.cs` — Chunk-level summary flags for disaster skip optimisation (HasVolcanicTile, HasRiverTile, etc.).
- `SeasonalProfile.cs` — 8-byte per-tile seasonal climate deltas (temperature + moisture delta per season).
- `TileChunk.cs` — Chunk struct for disaster skip optimisation (16×16 tiles, ChunkSummaryFlags).
- `TileData.cs` — 14-byte tile struct with static (worldgen) and dynamic (sim) fields; see interface_contracts_tiles.md.
- `TileDynFlags.cs` — Dynamic tile state flags set during simulation (HasActiveDisaster, RecentlyBurned, etc.).
- `TileGrid.cs` — Flat array + chunk indexing; handles east-west cylinder wrapping.
- `TileStaticFlags.cs` — Static tile flags set during world generation (IsVolcanic, IsFaultLine, HasRiver, etc.).
- `TileTemperature.cs` — Computes the effective temperature for a tile at a given simulation moment, combining static base temperature, seasonal delta, and global anomaly. Shared by all phases that need per-tile climate conditions.

## WorldEngine.Sim/World/
- `ActiveDisaster.cs` — An ongoing disaster affecting a specific tile. Created by Phase 1 (Environmental). Cleared by Phase 1 when resolved. OriginEventId links to the SimEvent that started this disaster for causal graph.
- `ActiveDrought.cs` — A drought affecting all tiles in a (LatitudeBand, Biome) region. Membership is computed at runtime: ActiveDroughts.Any(d => tile matches d). No per-tile registry entry — the region can contain thousands of tiles.
- `BorderManifest.cs` — Per-tile border sampling data (North/South/East/West edges, 64 samples each) for civ contact detection.
- `BorderManifestSample.cs` — 5-byte border sample struct: elevation, moisture, river/road crossing flags, and ownership.
- `HistoryTypes.cs` — Pre-aggregated profile of a historical character, built by SummaryBuilder.
- `IHistoryGraphReadOnly.cs` — Read-only query surface over the persisted history graph (SQLite Events + CausalEdges).
- `IHistoryQuery.cs` — Pre-indexed historical query API. Backed by SQLite summary tables built by
- `IWorldStateReadOnly.cs` — Read-only view of world state for entity decision-making (M2+). In M1, the Environmental phase reads WorldState directly as a mutator.
- `PendingEvent.cs` — Lightweight event record produced during simulation phases. Phase 7 assigns Id, Year, Season, Tick, runs significance classification, applies the event gate, and writes to SQLite + EventCache.
- `ResourceDeposit.cs` — A mineral or resource deposit at a tile. Multiple deposits can stack at one location (e.g., quarry slate over a placer gold seam). List ordered by depth (surface first).
- `RuinRecord.cs` — Records the history of a tile that once held a settlement. Persists when a settlement is destroyed or abandoned; accumulates each time the same tile cycles.
- `SimEvent.cs` — An event in the simulation history log. Immutable once written. Created by Phase 7 (EventGeneration) after enriching a PendingEvent.
- `SnapshotBuilder.cs` — Constructs a WorldSnapshot from WorldState. Called by the sim thread at the end of each tick. Only touches WorldState — never called from the UI thread.
- `StateCache.cs` — Thread-safe snapshot bridge. Sim thread calls Commit() after each tick. UI thread calls Read() every frame. Lock held for microseconds only.
- `TileDisplayData.cs` — Per-tile rendering data in WorldSnapshot.AllTiles (index: y * WorldTileWidth + x). Contains effective (current) values, not genesis base values. Created by the sim thread for the full world grid each tick. HasActiveDisaster is computed from ActiveTileDisasters registry.
- `TileImprovement.cs` — ImprovementType enum (Farm/Mine/etc.) and TileImprovement record for territory-based improvements (M3.0).
- `TileInspectorData.cs` — Complete tile data for the inspector panel. Created by sim thread on demand. Contains base values, seasonal profiles, and all registry data for the tile.
- `WorldSnapshot.cs` — Snapshot entry for one territory tile: which city owns it and which civ that city belongs to. Keyed by tile coord in WorldSnapshot.TerritoryMap.
- `WorldState.cs` — The complete mutable world state. Owned by the sim thread — never accessed from the UI thread. The UI reads WorldSnapshot via StateCache.

## WorldEngine.Sim/WorldGen/
- `BiomeClassifier.cs` — Pure static function that maps (temperature, moisture, elevation, flags) → BiomeType. Priority rules applied top-to-bottom; first match wins. All thresholds come from SimConfig.WorldGen.BiomeThresholds.
- `BiomeResult.cs` — Per-tile biome classification and fertility produced by BiomeLayer.
- `ClimateResult.cs` — Per-tile climate data produced by ClimateLayer.
- `ElevationResult.cs` — Per-tile elevation data (0–255) produced by ElevationLayer.
- `LayerSeeds.cs` — Per-layer seed constants XOR'd with worldSeed when initializing FastNoiseLite. All values must be unique — LayerSeeds_AllValuesAreUnique test enforces this.
- `MagicResult.cs` — Per-tile magic intensity data produced by MagicLayer.
- `OceanResult.cs` — Per-tile ocean and coast flags produced by OceanLayer.
- `PoiResult.cs` — POI candidate flags produced by PoiCandidateLayer.
- `ResourceResult.cs` — Resource deposit registry produced by ResourceLayer.
- `RiverResult.cs` — Per-tile river and lake data produced by RiverLayer.
- `TectonicResult.cs` — Per-tile tectonic plate data produced by TectonicLayer.
- `TileGridAssembler.cs` — Assembles all layer results into a fully populated WorldState. Runs Parallel.For over Y rows for throughput on large worlds.
- `WorldGenContext.cs` — Accumulates layer results as world generation progresses. Layers read only from completed predecessors — never from layers that haven't run yet.
- `WorldGenPipeline.cs` — Runs the full world generation pipeline and returns a populated WorldState. Each layer receives the WorldGenContext (read-only access to previous results). Progress is reported as (LayerName, fraction) per layer step.

## WorldEngine.Sim/WorldGen/Layers/
- `BiomeLayer.cs` — Classifies each tile's biome using BiomeClassifier and computes Fertility from biome and climate inputs.
- `ClimateLayer.cs` — Generates base temperature and moisture for every tile. Temperature: latitude cosine curve + elevation lapse rate. Moisture: two-band wind sweep — Tropical band: East-to-West sweep (trade winds blow toward equator). Mid-lat + polar: West-to-East sweep (westerlies). Rain shadow: leeward tiles of mountain lose RainShadowLossFraction moisture. Also sets storm corridor flag and computes per-tile SeasonalProfiles.
- `ElevationLayer.cs` — Generates per-tile elevation using FastNoiseLite Simplex noise combined with tectonic contributions: mountain ridges at continental collisions, trenches at subduction zones, and a continental highland bias. Output normalized to byte range 0–255.
- `MagicLayer.cs` — Generates magic intensity using Simplex noise with a volcanic zone weighting. Volcanic tiles get ×2 multiplier. High-magic tiles near volcanic zones are flagged as IsPOICandidate. M1: generates and stores data only — no behavioral effects until M2+.
- `OceanLayer.cs` — Thresholds elevation into ocean/land using DefaultSeaLevel (fraction of tiles that are ocean). Then marks land tiles adjacent to any ocean tile as IsCoastal.
- `PoiCandidateLayer.cs` — Identifies candidate tiles for Points of Interest: river mouths, high-magic volcanic sites, coastal resource tiles, and tectonic fault/junction tiles with high deposit potential. POI selection from candidates happens in a later pass during sim initialization.
- `ResourceLayer.cs` — Assigns mineral and rare resource deposits to tiles based on tectonic and biome context. Writes to ResourceResult.Deposits; HasDeposit/HasRareResource flags applied during assembly.
- `RiverLayer.cs` — Computes drainage networks using D8 flow direction + Priority Flood sink filling (Barnes 2014 algorithm), then accumulates flow to identify rivers and lakes. Cylinder-aware throughout (X wraps, Y clamped).
- `TectonicLayer.cs` — Generates tectonic plate assignments, fault lines, and volcanic zones. Algorithm: Poisson disc plate center sampling → cylinder-aware Voronoi → subduction detection.

## WorldEngine.UI/
- `Game1.cs` — MonoGame entry: update/draw loop, StateCache reads, input routing; H=civ history, W=watch panel, T=territory overlay.

## WorldEngine.UI/Rendering/
- `Camera2D.cs` — Pan/zoom camera for the tile map.
- `OverlayRenderer.cs` — Per-tile color for each OverlayType (Biome/Elevation/Temp/Moisture/Resources/Magic/Territory).
- `TileMapRenderer.cs` — Draws tiles + entity/settlement/ruin markers; M3.4: territory civ-color tint + improvement icons.

## WorldEngine.UI/UI/
- `CharacterProfilePanel.cs` — Myra panel showing a structured character profile card. Populated entirely from IHistoryQuery — no prose generation.
- `CharacterWatchPanel.cs` — Read-only live panel tracking a single named character's current state. Updated each tick from WorldSnapshot.WatchedCharacter. Precursor to M4 Spotlight — everything read-only, no sim commands except WatchCharacter.
- `CivHistoryPanel.cs` — Myra panel showing the full arc of a civilization — rulers, key wars, major events, traits. Includes a civ selector ComboBox at the top.
- `EventLogPanel.cs` — Sidebar panel showing recent simulation events. Supports focus lens filtering (dimming events not involving the focus target) and exposes pending requests for the character profile card and causal chain dialog.
- `TileInspectorPanel.cs` — Sidebar tile inspector; territory/improvement/history sections; [Watch] buttons per character (M3.4).
- `TimeControlsPanel.cs` — Top toolbar: speed buttons, year/season label.
- `TimelineBar.cs` — Timeline scrubber bar drawn via SpriteBatch at the bottom of the map area. Shows event density heatmap and allows scrubbing to any historical year. The ScrubLabel is a Myra Label — add it to the root overlay panel in Game1.
- `WorldGenScreen.cs` — Full-screen world-gen progress overlay.

## WorldEngine.Tests/
- xUnit test suite; mirrors Sim folder structure
- Key files: reproducibility tests, integration tests per phase, world gen tests
- `Integration/HeadlessRunnerTests.cs` — A1: headless runner smoke tests (world.db created, events exist, world gen reproducibility)
- `Integration/MetricsCollectorTests.cs` — A2: yearly_metrics row count, last row fields, gate-independence, final year consistency
- `Integration/HistoryQueryTests.cs` — M3.1: SummaryBuilder, SuccessionChain, and HistoryQueryService integration tests
- `Integration/CulturalTraitsTests.cs` — M3.2: CulturalTrait enum, EvaluateCulturalTraits logic, CivTraitAcquired event generation
- `Integration/SignificanceScoringTests.cs` — M3.2: ComputeSignificanceScore, SignificanceRescoringPass tier upgrades and score population
- `Integration/NarrativeUIDataTests.cs` — M3.3: GetCausalChain, GetAllCivSummaries, GetEventCountByDecade, GetCharacterHistory ordering
- `Integration/SaveLoadTests.cs` — M3.6: WorldStateSaver round-trip tests (8 tests: files created, year, settlements, entities, territory, round-trip, meta, empty world)
- `Unit/BiomeSpawnWeightTests.cs` — S3: biome spawn-weight lookup, harsh-biome under-representation on a generated world, spawn reproducibility
- `Integration/UnrestSecessionTests.cs` — S2: unrest accrual math, forced-splinter integration, capital immunity, reproducibility
- `Integration/TileInspectTests.cs` — M3.4: TileInspectorData territory/improvement population, unclaimed tile returns null
- `Unit/AncestryConfigTests.cs` — M3.5: AncestryConfig field loading, ApplyCulturalSettlementName, GetCivNameSuffix, BuildCulturalProfile
- `Balance/BalanceRegressionTests.cs` — C2: world-health regression harness; 2 seeds × 300 years; [Trait("Category","Balance")]; run via scripts/test-balance.sh







## docs/perf/
- `notes_m3.md` — M3 performance profiling notes and gate status







## scripts/
- `balance-run.py` — A3: multi-seed headless sweep; fans out sim subprocesses, reads yearly_metrics, prints cross-seed mean/min/max table, exports merged CSV; --compare mode diffs two sweep dirs
- `world-sanity.py` — post-run sanity checks against world.db; exits 0 on pass, 1 on fail (CI-ready)
- `character-analysis.py` — ad-hoc character behavior analysis queries
- `civ-history.py` — civ summary queries
- `scip-query.py` — SCIP code navigation (defs, refs, types, impls, stats)







## config/profiles/
- `fast_history.toml` — 1 tick/season (4× faster); halved disease_base_chance; for multi-seed sweeps
- `small_world.toml` — A3: 1 tick/season + halved disease for fast smoke tests (sub-minute runs)
- `balance_invariants.toml` — C1: expected world-health bands at checkpoint years; loaded by BalanceRegressionTests, NOT part of SimConfig







## docs/
- `config_future.md` — TOML sections removed from sim_config.toml during B2 purge; preserved as design intent for unimplemented systems ([admin_distance], [spatial_buffer], [specialists], [artifacts], [cultural_modifiers], [civilization.settler_seeding]); includes dead-vs-live disagreement table
- `tuning_balance_review_2026-07-18.md` — tuning/balance review and Phase A–D improvement plan; Status: IMPLEMENTED
- `balance_invariants.md` — C1: philosophy for balance bands (observed-healthy ± margin), update procedure, Phase D migration notes
- `sim_tuning.md` — C3: sim knob reference (knob / current / safe range / too-low / too-high / metric to watch)







## docs/archive/
- `sim_observations_and_proposals.txt` — archived 2026-07-18; 5876-year run analysis with per-item resolution status for A1–A6 and B1–B7
- `sim_run_5876_characters.txt` — raw character data from the 5876-year reference run; superseded by metrics tooling
- `sim_run_5876_civilizations.txt` — raw civ data from the 5876-year reference run; superseded by metrics tooling
