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
- `AuthoringCommands.cs` — Player-authored God Mode commands. Each represents a single intentional act that bypasses normal simulation probability and is stamped IsGodMode = true in the resulting SimEvent. All fields are value-type only (no callbacks/delegates).
- `PlayerCommands.cs` — UI-to-sim command records: SetSimSpeed, PauseToggle, StepOneTick, SetViewport (no-op); routed via CommandQueue.
- `SpotlightCommands.cs` — Player spotlight commands (M7+). Each command represents a player intent for controlling or influencing a specific character. All fields are value-type only.

## WorldEngine.Sim/Config/
- `AncestryConfig.cs` — All data for one ancestry, loaded from config/ancestries.toml. Personality and aptitude fields are bias offsets added to the Gaussian mean (base 0.5).
- `AncestryLoader.cs` — Loads ancestries.toml into AncestryConfig instances.
- `AncestryRegistry.cs` — Loaded set of all ancestry configs. Accessible via world.SimConfig.AncestryRegistry. Provides biome-weighted ancestry sampling and cross-ancestry trust lookups.
- `AncestryValidator.cs` — Validates ancestries.toml after deserialization. Called automatically by WorldEngine.Sim.Config.AncestryLoader.LoadOrDefault. Throws WorldEngine.Sim.Config.AncestryValidationException listing every violation found — a load-time gate per M10 10.3 (fail fast, not silent degradation of simulation behavior).
- `ArtifactConfig.cs` — Artifact generation and ownership constants. Loaded from the [artifacts] section of sim_config.toml.
- `BeastsSimConfig.cs` — Beast lifecycle constants from the [beasts] section of sim_config.toml. Species-specific values (health, strength, etc.) live in config/beasts.toml.
- `BiomeThresholdConfig.cs` — Elevation, temperature, and moisture thresholds for biome classification.
- `CharacterNamesConfig.cs` — Name pools for procedurally naming characters (first and last names).
- `CharacterSimConfig.cs` — Character behavior constants: needs decay, skill growth, diplomacy (war knobs in WarConfig).
- `ClimateConfig.cs` — Temperature/moisture gradients, storm corridors, and climate drift constants.
- `ConfigRegistry.cs` — Value kinds the generic settings UI knows how to render (M10 10.2, ui_design_framework.md §9.3).
- `CulturalTraitsConfig.cs` — Thresholds that govern when a civilization acquires a permanent cultural trait. All constants loaded from [cultural_traits] section in sim_config.toml.
- `DisasterConfig.cs` — Disaster probability and damage constants (wildfire, flood, eruption, earthquake, drought).
- `ElevationConfig.cs` — FastNoiseLite terrain noise and mountain/tectonic thresholds for world generation.
- `EmissaryConfig.cs` — All constants governing the civ awareness and emissary system (M4.1). Loaded from the [emissary] section of sim_config.toml.
- `EventsConfig.cs` — Nested config under [events.gate] in sim_config.toml. Controls which event types are always or never recorded, independent of tier.
- `ImprovementsConfig.cs` — Tile improvement food/production multipliers and build-cost constants.
- `LocalGenConfig.cs` — Local-scale (10m-resolution) generation parameters: chunk size and world-tile subdivision (M11).
- `ResourcePressureConfig.cs` — Food/water/resource pressure constants: shortage threshold, famine onset, and carrying-capacity weights.
- `ResourcesConfig.cs` — Per-resource deposit density fractions used during world generation (iron, copper, tin, precious metals).
- `RiversConfig.cs` — River flow accumulation threshold and lake detection constants for world generation.
- `SeafaringConfig.cs` — Constants governing character sea voyages (M11 — character water crossings). Loaded from the [seafaring] section of sim_config.toml.
- `SettlementConfig.cs` — Population growth rates, carrying capacity, and crystallisation threshold constants.
- `SettlementNamesConfig.cs` — Prefix/suffix pools for procedural settlement name generation.
- `SimConfig.cs` — Root config container; all subsections loaded from sim_config.toml.
- `SimConfigLoader.cs` — Tomlyn-based TOML loader; strict mode detects unbound keys; supports profile overlays and --set overrides.
- `SimConfigValidator.cs` — Validates a loaded SimConfig for range, ordering, and cross-field invariants. Called automatically by SimConfigLoader after deserialization. Throws WorldEngine.Sim.Config.SimConfigValidationException listing every violation found.
- `SimLoopConfig.cs` — Adds TicksPerSeason (alias) and TicksPerYear (= TicksPerSeasonalChange × 4) derived properties; use these everywhere instead of hardcoded 16.
- `TectonicsConfig.cs` — Tectonic plate count, separation, and continental/oceanic ratio constants for world generation.
- `UnrestConfig.cs` — Configuration for the settlement unrest / secession mechanic (S2 splinter). All tunable constants for unrest accumulation, decay, and the secession trigger. Bound from the section of .
- `UtilityAffinityConfig.cs` — Configures the two UtilityScorer lookup tables: 1. Goal → action affinity weights (how well each action advances each goal type). 2. Action base-score need-weights (how much each need's deficit drives each action). TOML section: [utility_affinity] Sub-tables: [utility_affinity.goal_affinity] — goal-name → { action-name = weight } [utility_affinity.action_needs] — action-name → { need-name = coefficient, _default = fallback } Unmapped (goal, action) pairs default to 0.0. Unmapped action need-weights use _default (0.1 for the original fallback, 0.0 for unlisted actions).
- `WarConfig.cs` — All war-system configuration — consolidated from [character] + [war] in D5. Loaded from the [war] section of sim_config.toml.
- `WildlifeRiskConfig.cs` — Per-biome wildlife-raid risk multiplier table (D3 extraction from PopulationDynamicsPhase). TOML section: [wildlife_risk] Each key is a BiomeType name in snake_case (e.g. tropical_rainforest, boreal_forest). The value is a float multiplier applied to WorldEngine.Sim.Config.SettlementConfig.WildlifeAttackBaseChance. Design rationale (from PopulationDynamicsPhase comment): Dense cover (forest, swamp) gives predators ambush advantage — multipliers above 1.0. Open terrain (plains, savanna, desert) provides visibility — multipliers below 1.0. The default for unlisted biomes is 0.6 (matches the original _ => 0.6f fallback). TOML section: [wildlife_risk]
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

## WorldEngine.Sim/Entities/Artifacts/
- `Artifact.cs` — Category of legendary item — drives name generation and narrative significance.
- `ArtifactNameGenerator.cs` — Deterministic legendary-item name generator seeded via WorldRng. Same world seed + same invocation parameters produce identical names. Style: "<Epithet> <Noun>" — e.g. "Dawnbreaker", "The Sundered Crown".
- `ArtifactRegistry.cs` — Static operations helper for the artifact registry on WorldEngine.Sim.World.WorldState. All methods mutate only — they do NOT emit events. Callers are responsible for emitting the corresponding WorldEngine.Sim.Events payload.
- `CreatedGoodTaxonomy.cs` — Groups and category-derivation for WorldEngine.Sim.Core.CreatedGoodType (M9 G-1/G-2 unification). Replaces the old role-blind map: an artifact's category is derived from the specific good a character was making, weighted across the categories that good plausibly becomes, instead of the creator's Tier2 role.

## WorldEngine.Sim/Entities/Beasts/
- `BeastCatalog.cs` — Queryable, in-memory view of the beast species catalog loaded from config/beasts.toml.
- `BeastCatalogFile.cs` — Top-level wrapper for beasts.toml deserialization. Tomlyn maps [[beasts]] arrays to the Beasts list via snake_case conversion.
- `BeastCatalogLoader.cs` — Loads beasts.toml into BeastCatalog instances.
- `BeastCatalogValidator.cs` — Validates beasts.toml after deserialization. Called automatically by WorldEngine.Sim.Entities.Beasts.BeastCatalogLoader.LoadOrCreateDefault. Throws WorldEngine.Sim.Entities.Beasts.BeastCatalogValidationException listing every violation found — a load-time gate per M10 10.3 (fail fast, not silent degradation of simulation behavior).
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
- `UtilityScorer.cs` — Scores candidate actions for a Tier 1 character and selects one via softmax. Holds pre-baked lookup tables (goal→action affinity, action need-weights) built once at construction from WorldEngine.Sim.Config.UtilityAffinityConfig.

## WorldEngine.Sim/Events/
- `EventGate.cs` — Pre-write gate deciding whether an event is recorded to the history log. God Mode events are always recorded; otherwise suppressed types and sub-minimum-tier events are dropped.
- `SignificanceClassifier.cs` — Maps a (type, payload, isFirstOfKind) tuple to an WorldEngine.Sim.Core.EventTier and WorldEngine.Sim.Core.PopulationImpact. The final tier is the max of the verb-class floor and the impact-derived tier, bumped one level when the event is first of its kind.

## WorldEngine.Sim/Persistence/
- `CausalEdgeBuilder.cs` — Post-sim pass that infers causal relationships between events and writes them to the CausalEdges table with typed EdgeType labels.
- `DatabaseSchema.cs` — Schema DDL for SQLite: Events, SignificanceScore, CausalEdges, CharacterSummaries, CivSummaries, Eras, SuccessionChain, Dynasties, CivTraits, yearly_metrics, LocalTileDeltas.
- `EventStore.cs` — SQLite-backed event store. Holds a single persistent connection (required so that an in-memory database survives between calls). Implements WorldEngine.Sim.World.IHistoryGraphReadOnly.
- `HistoryQueryService.cs` — SQLite-backed implementation of WorldEngine.Sim.World.IHistoryQuery. Queries pre-indexed summary tables built by WorldEngine.Sim.Persistence.SummaryBuilder. Maintains small in-memory caches for frequently accessed civs and characters (≤20 entries each, LRU-evict). Obtain via WorldEngine.Sim.Persistence.EventStore.GetHistoryQuery.
- `SignificanceRescoringPass.cs` — Post-sim pass: upgrades event tiers based on downstream outcomes and computes final SignificanceScore for all events. Run once after the simulation ends (or on-demand before narrative generation) via WorldEngine.Sim.Persistence.EventStore.BuildSummaries.
- `SummaryBuilder.cs` — Post-sim pass that scans the event log and populates pre-aggregated summary tables: CharacterSummaries, CivSummaries, SuccessionChain, Dynasties, and Eras. Call via WorldEngine.Sim.Persistence.EventStore.BuildSummaries after the simulation ends.
- `WorldStateDto.cs` — Lightweight save metadata written to meta.json. Checked on load for version compat.
- `WorldStateSaver.cs` — Saves and loads WorldState to/from a save directory. Format: meta.json (version/summary), state.bin (full world state JSON), config_snapshot/.
- `YearlyMetricsRow.cs` — One row of the table. Mutable class with settable properties so Dapper can materialize it from SQLite query results (SQLite returns INTEGER as Int64 and REAL as Double; Dapper requires a matching default constructor for property-based deserialization). Written by WorldEngine.Sim.Simulation.MetricsCollector once per in-game year.

## WorldEngine.Sim/Simulation/
- `EventCache.cs` — Fixed-capacity ring buffer of recent SimEvents. Add() and GetRecent() are sim-thread-only. Thread safety via StateCache wrapping snapshots.
- `FoodAuditSink.cs` — Optional audit sink for per-tile food factor breakdowns. Passed to ResourcePressurePhase when is requested. Null on the normal (hot) path — zero overhead when not auditing.
- `MetricsCollector.cs` — Samples world state once per in-game year and writes a row to the table in world.db. Runs on the sim thread only; all reads are direct WorldState accesses — no LINQ over tiles, no DB reads, no cross-thread calls. Called by WorldEngine.Sim.Simulation.PhaseRunner at the annual tick boundary, after all phases have run for that year, when in config. Columns that cannot be computed cheaply without restructuring phases are omitted and annotated with // DECISION comments below.
- `PhaseRunner.cs` — Runs the 7 simulation phases in order each tick. Phase 1 (Environmental) produces PendingEvents consumed by Phase 7 (EventGeneration). All other phases are stubs in M1.
- `SimLoop.cs` — Background simulation thread. Ticks WorldState, builds snapshots, commits to StateCache. Only the background thread touches WorldState. UI thread only reads StateCache.

## WorldEngine.Sim/Simulation/Phases/
- `ArtifactDecayPhase.cs` — Annual destruction sink for artifacts. Without a sink, the artifact stock is monotonic (created but never destroyed) and grows unbounded over thousand-year histories. Each year every living artifact rolls a small destruction chance — high for Lost (ownerless) items that no one safeguards, very low for owned items. This drives the stock toward an equilibrium of roughly (annual creation rate ÷ decay rate) rather than growing forever. Runs on annual ticks only; emits WorldEngine.Sim.Core.EventType.ArtifactDestroyed per loss.
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

## WorldEngine.Sim/Tiles/LocalScale/
- `LocalChangeType.cs` — Kind of change a WorldEngine.Sim.Tiles.LocalScale.LocalTileDelta represents. Starts with a single generic value per 11.5's "start minimal" scoping — split into more specific kinds only once a real gameplay system needs to distinguish them.
- `LocalChunk.cs` — A Size×Size grid of local terrain. Always derivable from (WorldSeed, ChunkCoord, parent TileData, border manifests) — never itself persisted; see docs/phases/m11_local_scale_generation.md "regenerate base terrain on demand".
- `LocalCoordMath.cs` — Pure conversions between (ChunkCoord, LocalTileCoord) and an "absolute" local coordinate — the cell's position counted in local tiles from world origin (0,0), ignoring world-tile and chunk boundaries entirely. Absolute coordinates are what 11.3's noise sampling keys off of, so the same seed produces continuous terrain across a chunk or world-tile edge instead of a per-tile noise domain that restarts (and seams) at every boundary.
- `LocalTileData.cs` — Minimal per-cell local-scale terrain data — flavor terrain only, no civ/economy fields (compare WorldEngine.Sim.Tiles.TileData, the much larger world-scale equivalent).
- `LocalTileDelta.cs` — One persisted, sparse override of a single local tile — the only local-scale state that survives a chunk being discarded and later regenerated (see docs/phases/m11_local_scale_generation.md "regenerate base terrain on demand; persist only modifications"). Keyed by (Chunk, Local); writing a second delta for the same cell replaces the first rather than layering.
- `LocalTileDeltaPayload.cs` — JSON payload shape for a WorldEngine.Sim.Tiles.LocalScale.LocalChangeType.CellOverride delta — same PendingEvent/SimEvent payload pattern (a JSON string on the persisted record, a typed shape for producers/consumers to agree on). Each field is independently optional so a delta can override just the fields a future change actually touches.
- `LocalTileFlags.cs` — Bit flags for WorldEngine.Sim.Tiles.LocalScale.LocalTileData.Flags. Only River (11.4) is assigned so far.

## WorldEngine.Sim/World/
- `ActiveDisaster.cs` — An ongoing disaster affecting a specific tile. Created by Phase 1 (Environmental). Cleared by Phase 1 when resolved. OriginEventId links to the SimEvent that started this disaster for causal graph.
- `ActiveDrought.cs` — A drought affecting all tiles in a (LatitudeBand, Biome) region. Membership is computed at runtime: ActiveDroughts.Any(d => tile matches d). No per-tile registry entry — the region can contain thousands of tiles.
- `BorderManifest.cs` — Per-tile border sampling data (North/South/East/West edges, 64 samples each) for civ contact detection.
- `BorderManifestSample.cs` — 5-byte border sample struct: elevation, moisture, river/road crossing flags, and ownership.
- `EdgeDirection.cs` — Which side of a world tile a border-manifest edge (or river crossing) refers to.
- `HistoryTypes.cs` — Pre-aggregated profile of a historical character, built by SummaryBuilder.
- `IHistoryGraphReadOnly.cs` — Read-only query surface over the persisted history graph (SQLite Events + CausalEdges).
- `IHistoryQuery.cs` — Pre-indexed historical query API. Backed by SQLite summary tables built by WorldEngine.Sim.Persistence.SummaryBuilder. Use WorldEngine.Sim.Persistence.EventStore.GetHistoryQuery to obtain an instance.
- `IWorldStateReadOnly.cs` — Read-only view of world state for entity decision-making (M2+). In M1, the Environmental phase reads WorldState directly as a mutator.
- `PendingEvent.cs` — Lightweight event record produced during simulation phases. Phase 7 assigns Id, Year, Season, Tick, runs significance classification, applies the event gate, and writes to SQLite + EventCache.
- `ResourceDeposit.cs` — A mineral or resource deposit at a tile. Multiple deposits can stack at one location (e.g., quarry slate over a placer gold seam). List ordered by depth (surface first).
- `RuinRecord.cs` — Records the history of a tile that once held a settlement. Persists when a settlement is destroyed or abandoned; accumulates each time the same tile cycles.
- `SimEvent.cs` — An event in the simulation history log. Immutable once written. Created by Phase 7 (EventGeneration) after enriching a PendingEvent.
- `SnapshotBuilder.cs` — Constructs a WorldSnapshot from WorldState. Called by the sim thread at the end of each tick. Only touches WorldState — never called from the UI thread.
- `SpotlightIntent.cs` — Current spotlight player intent: what the player wants the spotlit character to do.
- `StateCache.cs` — Thread-safe snapshot bridge. Sim thread calls Commit() after each tick. UI thread calls Read() every frame. Lock held for microseconds only.
- `TileDisplayData.cs` — Per-tile rendering data in WorldSnapshot.AllTiles (index: y * WorldTileWidth + x). Contains effective (current) values, not genesis base values. Created by the sim thread for the full world grid each tick. HasActiveDisaster is computed from ActiveTileDisasters registry.
- `TileImprovement.cs` — ImprovementType enum (Farm/Mine/etc.) and TileImprovement record for territory-based improvements (M3.0).
- `TileInspectorData.cs` — Complete tile data for the inspector panel. Created by sim thread on demand. Contains base values, seasonal profiles, and all registry data for the tile.
- `WorldSnapshot.cs` — Snapshot entry for one territory tile: which city owns it and which civ that city belongs to. Keyed by tile coord in WorldSnapshot.TerritoryMap.
- `WorldState.cs` — The complete mutable world state. Owned by the sim thread — never accessed from the UI thread. The UI reads WorldSnapshot via StateCache.

## WorldEngine.Sim/WorldGen/
- `BiomeClassifier.cs` — Pure static function that maps (temperature, moisture, elevation, flags) → BiomeType. Priority rules applied top-to-bottom; first match wins. All thresholds come from SimConfig.WorldGen.BiomeThresholds.
- `BiomeResult.cs` — Per-tile biome classification and fertility produced by BiomeLayer.
- `BorderManifestBuilder.cs` — Builds per-tile BorderManifests from completed world-gen layer results, for M11 local-scale generation to sample when amplifying terrain and threading rivers across a tile boundary. Elevation/moisture samples are a flat blend of the two adjacent tiles' own byte values — real sub-tile variation is added by the terrain-amplification algorithm (M11 phase 11.3); this builder's job is only to guarantee both sides of an edge agree.
- `ClimateResult.cs` — Per-tile climate data produced by ClimateLayer.
- `ElevationResult.cs` — Per-tile elevation data (0–255) produced by ElevationLayer.
- `LayerSeeds.cs` — Per-layer seed constants XOR'd with worldSeed when initializing FastNoiseLite. All values must be unique — LayerSeeds_AllValuesAreUnique test enforces this.
- `LocalRiverThreader.cs` — Carves a river channel through an already-generated WorldEngine.Sim.Tiles.LocalScale.LocalChunk (post-process pass after WorldEngine.Sim.WorldGen.LocalTerrainAmplifier.Amplify), connecting the tile's river boundary crossing(s) recovered from its WorldEngine.Sim.World.BorderManifest. Pure function of (ChunkCoord, parent TileData, parent BorderManifest, LocalGenConfig) — deterministic and not persisted, same rationale as WorldEngine.Sim.WorldGen.LocalTerrainAmplifier. A crossing's position/width is recovered directly from the manifest edge's WorldEngine.Sim.World.BorderManifestSample.HasRiverCrossing run, not from the raw WorldEngine.Sim.WorldGen.RiverCrossing record — WorldEngine.Sim.WorldGen.BorderManifestBuilder stamps both sides of a shared edge from the exact same crossing, so both adjacent tiles recover byte-identical position/width, matching per the phase requirement without needing to re-derive or share state.
- `LocalTerrainAmplifier.cs` — Deterministic local-chunk terrain generator: a pure function of (worldSeed, ChunkCoord, parent TileData, parent BorderManifest, LocalGenConfig) — same inputs always produce the same chunk, so nothing here is persisted (see docs/phases/m11_local_scale_generation.md). Elevation blends from the parent tile's own byte value toward the shared BorderManifest edge sample within EdgeBlendBandTiles of each world-tile edge, then adds FastNoiseLite detail sampled in absolute local-tile coordinates so the detail layer is automatically continuous across chunk/tile boundaries (same seed + same absolute coordinate = same noise value regardless of which chunk is generating).
- `LocalTileDeltaApplier.cs` — Applies persisted WorldEngine.Sim.Tiles.LocalScale.LocalTileDelta overrides on top of an already-generated WorldEngine.Sim.Tiles.LocalScale.LocalChunk — the last post-process pass in the local-gen pipeline (after WorldEngine.Sim.WorldGen.LocalTerrainAmplifier.Amplify and WorldEngine.Sim.WorldGen.LocalRiverThreader.Thread), since a player-caused modification must win over regenerated base terrain.
- `LocalTileGenerator.cs` — Placeholder (flat/uniform) local-chunk generator — unblocks chunk-loading/UI work ahead of 11.3's real noise-based terrain amplification. Every cell in the chunk copies the parent world tile's own Elevation/BiomeType verbatim; no sub-tile variation, no border-manifest blending yet.
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
- `LocalCamera2D.cs` — M11 11.7 — pan/zoom camera for the local-view screen. Mirrors Camera2D's API shape at
- `LocalTileMapRenderer.cs` — M11 11.7 — draws already-generated LocalChunks via a LocalCamera2D. Same solid-pixel-rect
- `OverlayRenderer.cs` — Per-tile color for each OverlayType (Biome/Elevation/Temp/Moisture/Resources/Magic/Territory).
- `TileMapRenderer.cs` — Draws tiles + entity/settlement/ruin markers; M3.4: territory civ-color tint + improvement icons.
- `WorldGenPreviewRenderer.cs` — Builds a per-tile thumbnail color buffer for a single worldgen layer, straight from the in-progress <see cref="WorldGenContext"/> (before <see cref="TileGridAssembler"/> has run). Reuses <see cref="OverlayRenderer.GetColor"/> — the same palette functions the live map uses — by feeding it a minimal <see cref="TileDisplayData"/> built from whatever layer results are available so far (per M10 10.1 design decision: don't fork the palette).

## WorldEngine.UI/UI/
- `FirstRunOverlay.cs` — Dismissible first-run orientation dialog shown once when the simulation starts for the first time. Points the player at the time controls, overlays, and event log.
- `LocalViewScreen.cs` — M11 11.7 — local-view screen: "[View Local]" on TileInspectorPanel opens this full-screen
- `OverlayBar.cs` — Top-bar "Map Display" control (M6 Epic 6.1.1; collapsed to a dropdown per playtest feedback — was a 7-button, 2-row grid). Selecting an overlay enqueues <c>SetActiveOverlay</c> — the same command the accelerator keys fire — and the dropdown reflects <c>WorldSnapshot.ActiveOverlay</c>.
- `PanelMenuBar.cs` — Row of buttons that toggle the Summoned panels (Watch, Character, Civ History, God Mode, Settings, Help) directly from the top bar, highlighting whichever are open, plus a Spotlight status/exit indicator. Moves primary panel access off the fixed right dock (playtest feedback: "that way we move away from everything being locked to the fixed right panel").
- `TimeControlsPanel.cs` — Top toolbar: speed buttons, year/season label.
- `TimelineBar.cs` — Timeline scrubber bar drawn via SpriteBatch at the bottom of the map area. Shows event density heatmap and allows scrubbing to any historical year. The ScrubLabel is a Myra Label — add it to the root overlay panel in Game1.
- `WorldGenPreviewScreen.cs` — M10 10.1 — worldgen preview screen: per-layer thumbnails (WorldGenPreviewRenderer),

## WorldEngine.UI/UI/Input/
- `CommandRegistry.cs` — The set of all named user actions. <see cref="KeybindRegistry"/> binds keys to command ids and invokes them here; UI buttons can invoke the same id directly.
- `KeybindEditor.cs` — Renders every <see cref="CommandRegistry"/> command grouped by category, each with its current key and [Rebind]/[Reset] affordances. Capture-next-keypress is driven externally via <see cref="TryCaptureKey"/> (the host feeds keyboard input from its own per-frame poll).
- `SimConfigEditor.cs` — Renders one <see cref="ConfigRegistry"/> group at a time (picked via dropdown) as <see cref="WeField"/>/<see cref="WeCheckBox"/> rows bound directly to the live <see cref="SimConfig"/> the running sim reads — edits apply immediately, same as the M10 10.1 worldgen-preview sea-level field. Values are not written back to sim_config.toml; the file stays the tuned baseline (see the M10 index doc DECISION on "default").

## WorldEngine.UI/UI/Kit/
- `EntityLink.cs` — Layer 2 — clickable entity reference; every nameable thing should render through this.
- `IWeWidget.cs` — Layer 1 — common surface so Layer 2 composites can nest any We* widget.
- `Meter.cs` — Layer 2 — labeled n-segment bar with numeric readout, serves needs/traits/health meters.
- `SectionHeader.cs` — Layer 2 — tokenized section divider, replaces the AddLine("--- X ---") idiom.
- `StatRow.cs` — Layer 2 — aligned label/value row, replaces ad-hoc "Label: value" AddLine calls.
- `Tooltip.cs` — Standard hover tooltip (framework §4.2/§7.3): consistent delay, cursor-follow, and viewport clamping so no tooltip can render off-screen (the timeline tooltip overflow bug).
- `WeCheckBox.cs` — Layer 1 — labeled toggle wrapping the CheckBox compat shim.
- `WeDropdown.cs` — Layer 1 — typed dropdown wrapping the ComboBox compat shim.
- `WeIcon.cs` — Icon glyph with a mandatory tooltip/label (framework §4.1: "never icon-only for anything non-obvious"). No icon font is currently pinned in the project — renders a short text glyph until one is added.
- `WeList.cs` — Vertical list of rows built from a data source. Non-virtualized — fine at current scale.
- `WeScroll.cs` — Scroll container wrapping <see cref="ScrollViewer"/>. Content width is always <c>available width - UiTheme.ScrollReserve</c>, and the viewer clamps to its assigned height rather than growing past it — the single fix for the scrollbar-obstruction bug (framework §3.2).
- `WeStack.cs` — Layer 1 — tokenized-spacing vertical/horizontal stacks wrapping Myra StackPanels.
- `WeText.cs` — The only way to render text in the M8 kit. Wraps <see cref="Label"/> and accepts only tokenized <see cref="UiTheme.TypographyRole"/> / <see cref="UiTheme.ColorRole"/> — a caller cannot pass a raw <see cref="Microsoft.Xna.Framework.Color"/> (framework §4.1).

## WorldEngine.UI/UI/Layout/
- `CommandGateway.cs` — The "change the world" half of the two-bus model (framework §7.1) — panels enqueue <see cref="ICommand"/>s through this instead of holding a <see cref="CommandQueue"/> directly.
- `IWorkspacePanel.cs` — Per-frame context handed to every panel (framework §6): the current snapshot, the selection sink, the formatting service, and the command gateway. A panel needs nothing else.
- `InputRouter.cs` — Arbitrates one pointer event per frame, top-down by z-band (framework §5.1). A Modal region with content assigned captures unconditionally. Float/Transient regions are click-through by design (legends, tooltips, toasts never block the map). Chrome regions consume when opaque and hit. If nothing consumes, the caller (map/camera) gets the event.
- `LayoutHost.cs` — Owns every screen rectangle, z-band, and (via <see cref="RegionSlot"/>) hit-test precedence (framework §3.2, §5.1). Panels declare content into a <see cref="Region"/>; they never see or set a raw <c>Top/Left/Width/Height</c>. Fixed grid: <c>TopBar</c> (full width, top chrome strip), <c>Timeline</c> (full width minus dock, bottom strip), <c>RightDock</c> (right column, below TopBar), <c>MapCanvas</c> (remaining — non-opaque, camera/tile-pick owns it). <c>Float</c>/<c>Modal</c> are viewport-sized overlays, not part of the grid.
- `ModalHost.cs` — The single modal surface (framework §5.5): dims the app with <see cref="UiTheme.SurfaceModalScrim"/>, centers content, and captures all input while open (the <see cref="InputRouter"/> treats a Modal region with content as an unconditional catch). Closes on <see cref="Close"/> or Esc.
- `SimWorkspace.cs` — Owns the <see cref="RegionSlot.RightDock"/> content: a pinned zone (always-visible panels, stacked) and a contextual tab zone where exactly one panel is visible at a time — no cross-panel overflow, no stacking (framework §5.2-5.3). Also drives the Float region's summoned panels (God Mode, Help).

## WorldEngine.UI/UI/Panels/
- `BeastProfilePanel.cs` — Structured beast profile card populated from the live <see cref="EntitySnapshot"/> — beasts have no derived history-query summary the way characters do, so unlike <see cref="CharacterProfilePanel"/> this reads sim-snapshot data directly instead of an <c>IHistoryQuery</c>.
- `CharacterProfilePanel.cs` — Structured character profile card populated entirely from <see cref="IHistoryQuery"/> — no prose generation. Shows the currently *selected* character's life summary; the live-tracked *watched* character's needs/goals/spotlight controls stay on the separate Watch panel.
- `CharacterWatchPanel.cs` — Live panel tracking a single watched entity. Tier1Character gets the rich needs/goals/ spotlight HUD (<see cref="WorldSnapshot.WatchedCharacter"/>); any other watchable kind (Tier2Character, LegendaryBeast, ...) gets the thinner vitals-only card (<see cref="WorldSnapshot.WatchedBasic"/>) — the same single watch slot, rendered differently depending on what's in it. When spotlighted (M7 Phase 7.4) exposes intent controls: enter/exit spotlight, move-to, goal nudges — spotlight only ever applies to a Tier1Character.
- `CivHistoryPanel.cs` — Layer 3 — Summoned Civ History panel: civ selector + rulers/wars/major-events (M8.3.3).
- `EventLogPanel.cs` — Pinned panel showing recent simulation events. Supports focus lens dimming and routes actor/ civ/cause-chain clicks immediately (M8.2.2) instead of a consume-once poll.
- `FilterPanel.cs` — Immutable snapshot of the active event-log filter criteria. Passed to <see cref="EventLogPanel"/> each frame via <see cref="FilterPanel.CurrentFilter"/>.
- `GodModePanel.cs` — God Mode panel — allows paused-only authoring actions: place artifact, trigger disaster, spawn character, nudge character. Dialogs route through the shared <see cref="ModalHost"/>.
- `HelpPanel.cs` — "?"-toggled panel listing every command via <see cref="KeybindEditor"/>, plus the God-Mode/ Spotlight workflow cards. Rendered directly from <see cref="CommandRegistry"/>/ <see cref="KeybindRegistry"/> so it can never drift from actual input handling.
- `SettingsPanel.cs` — Settings shell: a left tab list (Display / Controls / Simulation) + right content, both inside a <see cref="PanelFrame"/>. The Simulation tab is optional — omitted (constructor overload) before a world/SimConfig exists, e.g. from the worldgen preview screen.
- `TileInspectorPanel.cs` — Layer 3 — Contextual (Tile) panel: ruin/settlement/tile facts/seasonal/resources/

## WorldEngine.UI/UI/Present/
- `Presenter.cs` — Converts sim data to display strings for every panel and the event log (framework §8.1, P7). No panel formats sim internals itself; unit thresholds and enum→prose mappings live here so they can be retuned in one place. Instance-based (not static) as a localization seam.

## WorldEngine.UI/UI/Selection/
- `EntityRef.cs` — Small reference to a selectable entity — the payload EntityLink hands to ISelectionSink.
- `SelectionBus.cs` — Immutable snapshot of the current selection, broadcast by <see cref="SelectionBus"/>.</summary> public readonly record struct SelectionSnapshot(SelectionKind Kind, long Id, TileCoord Coord); // MAP: The one "what am I looking at" channel (framework §7.1) — replaces SelectionRouter and // every consume-once navigation poller that used to live in Game1/panels (M8 phase 8.2). <summary> Single selection bus for the UI. Panels/composites call <see cref="Select"/> directly at the click site instead of setting a per-panel pending field for <c>Game1</c> to poll each frame. UI-only state — see the determinism note on <see cref="SelectionState"/>: tile inspection still round-trips a sim command because the snapshot must carry tile detail, but "what is selected" needs no sim round-trip.

## WorldEngine.UI/UI/Settings/
- `UiPrefs.cs` — Persisted UI preferences: display tuning + keybind overrides. Global (not per-world), stored next to the first-run flag file. Sim tuning stays in <c>config/*.toml</c> — this is UI-only.

## WorldEngine.UI/UI/Theme/
- `UiTheme.cs` — Single source of truth for the UI's visual language: colors, spacing, and metrics. Panels pull named tokens from here instead of hardcoding <see cref="Color"/> literals and pixel widths, so the whole UI can be retuned from one place (M6 Epic 6.2.1).

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
- `gen-map.py` — GENERATED: regenerates docs/codebase_map.md from XML doc summaries; run after adding/removing types
- `gen-config-ref.py` — GENERATED: regenerates docs/config_reference.md from config/sim_config.toml
- `gen-enum-tables.py` — GENERATED: regenerates the enum tables in docs/queries/event_log_queries.md from Enumerations.cs
- `doc-check.py` — lints authored docs for broken refs; checks generated files are in sync; used as CI gate
- `doc-check-allowlist.txt` — allowlist for doc-check.py: legitimate exceptions that should not be flagged

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
