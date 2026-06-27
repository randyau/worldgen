# Codebase Map
One-line description of every non-trivial source file. Check here before running `find`. Updated when files are added/removed.

## WorldEngine.Sim/Core/
- `ICommand.cs` — marker interface; all commands are sealed records implementing this
- `EntityId.cs / CivId.cs / EventId.cs / ModifierId.cs / ArtifactId.cs` — strongly-typed ID wrappers (never use raw ints)
- `Enumerations.cs` — all enums: BiomeType, Season, SimPhase, EntityKind, EventType, EventTier, VerbClass, etc.
- `WorldConfig.cs` — world gen params: seed, tile dimensions
- `WorldRng.cs` — deterministic RNG: `FloatAt(seed, tick, x, y, salt)` — use salts from SimRngSalts
- `CommandQueue.cs` — thread-safe queue for UI→sim commands (SetInspectedTile, etc.)
- `TileCoord.cs` — 2D tile coordinate; X wraps east-west, Y clamps
- `DisasterSalts.cs` — RNG salt constants for disaster phase

## WorldEngine.Sim/Config/
- `SimConfig.cs` — root config container; all subsections loaded from sim_config.toml
- `SimConfigLoader.cs` — Tomlyn-based TOML loader
- `CharacterSimConfig.cs` (~300 lines) — all character behavior constants: needs decay, skill growth, diplomacy, war thresholds
- `AncestryConfig.cs` — per-ancestry personality/aptitude biases, name pools, spawn weights
- `AncestryRegistry.cs` — collection of AncestryConfig; biome-weighted sampling
- `AncestryLoader.cs` — loads ancestries.toml
- `SettlementNamesConfig.cs` — prefix/suffix pools for procedural settlement names
- `EventsConfig.cs` — significance thresholds, headline gate settings
- `ResourcePressureConfig.cs` — food/water/resource pressure constants
- `SettlementConfig.cs` — population growth rates, carrying capacity config
- `BeastsSimConfig.cs / BeastSpawnConfig.cs / CombatConfig.cs` — beast behavior constants
- `CulturalTraitsConfig.cs` — M3.2: thresholds for assigning CulturalTrait values (Militaristic/Expansionist/etc.)
- Other `*Config.cs` — per-system TOML sections (Climate, Elevation, Tectonic, etc.)

## WorldEngine.Sim/Tiles/
- `TileData.cs` — 14-byte tile struct (static+dynamic fields; see interface_contracts_tiles.md)
- `TileGrid.cs` — flat array + chunk indexing; handles east-west cylinder wrapping
- `TileChunk.cs` — chunk struct for disaster skip optimisation (ChunkSummaryFlags)
- `TileStaticFlags.cs / TileDynFlags.cs / ChunkSummaryFlags.cs` — flag enums
- `SeasonalProfile.cs` — 8-byte per-tile seasonal climate deltas
- `TileTemperature.cs` — temperature utility helpers

## WorldEngine.Sim/WorldGen/
- `WorldGenPipeline.cs` — orchestrates all layers in order, returns populated WorldState
- `WorldGenContext.cs` — mutable context passed through the pipeline
- `IWorldGenLayer.cs` — layer interface: stateless, all state in WorldGenContext
- `TileGridAssembler.cs` — converts layer results into the final TileGrid
- `LayerSeeds.cs` — deterministic per-layer seed derivation
- `BiomeClassifier.cs` — classifies tiles from climate+elevation into BiomeType
- **Result types** (one per layer; plain data): `ElevationResult`, `ClimateResult`, `BiomeResult`, `TectonicResult`, `RiverResult`, `OceanResult`, `ResourceResult`, `MagicResult`, `PoiResult`
- **Layers/** — one file per generation layer:
  - `ElevationLayer.cs` — FastNoiseLite terrain noise
  - `TectonicLayer.cs` — plate assignment and fault lines (~220 lines)
  - `ClimateLayer.cs` — temperature/moisture gradients and storm corridors (~448 lines)
  - `RiverLayer.cs` — flow accumulation and lake detection (~298 lines)
  - `BiomeLayer.cs / OceanLayer.cs / ResourceLayer.cs / MagicLayer.cs / PoiCandidateLayer.cs`

## WorldEngine.Sim/Civilizations/
- `CivTracker.cs` — `Resolve()` dispatcher + EstablishSettlement, AllyWith, DeclareRivalry, RegisterRuin
- `CivTracker.War.cs` — ResolveWar, StartWarBetween, ResolveRaid, ResolveNegotiate
- `CivTracker.Diplomacy.cs` — RunAnnualDiplomacy, RunBorderTension, RunCivFloorSpawns, EndWarBetween, FireAllianceBroken
- `CivTracker.Naming.cs` — GenerateSettlementName, GenerateFertilityMultiplier, BiasedIndex, FireCivFounded, FireSettlementFounded
- `Civilization.cs` — mutable civ class: ruler, members, war state, border tension
- `SettlementStub.cs` — live settlement record on sim thread

## WorldEngine.Sim/Entities/
- `IEntity.cs` — base entity interface (EmitCommands, ToSnapshot)
- `SimEntity.cs` — abstract base class for entities
- `EntityRegistry.cs` — flat entity list + coord-bucketed lookup
- `EntityCommands.cs` — all entity ICommand records (MoveTo, Rest, etc.) in one file
- `EntitySnapshot.cs` — immutable UI-facing entity summary
- **Characters/**
  - `Tier1Character.cs` — named character: full personality, needs, skills, goals, relationships
  - `Tier2Character.cs` — background specialist: simplified needs/personality, role-based behavior
  - `UtilityScorer.cs` (~680 lines) — static action selection for Tier1: scores all candidate actions, selects best
  - `GoalManager.cs` (~357 lines) — goal formation, priority, staleness, resolution
  - `CharacterFactory.cs` — creates Tier1Character with seeded-random traits
  - `CharacterSpawner.cs` — world-spawn logic (initial population seeding)
  - `Tier2Spawner.cs` — crystallisation: spawns Tier2 when settlement hits population threshold
  - `NeedsVector.cs` — 7 dynamic needs (Tier1); decay each season, restored by actions
  - `NeedsVector4.cs` — 4-need subset (Tier2)
  - `PersonalityVector.cs` — 12 stable personality traits (Tier1)
  - `PersonalityVector6.cs` — 6-trait personality (Tier2)
  - `SkillVector.cs` — 8 dynamic skills; grow through use, cap at 1.0
  - `AptitudeVector.cs` — 6 stable aptitude traits; set at spawn
  - `NeedsUpdater.cs` — applies per-tick need decay and environmental boosts
  - `GoalData.cs / Tier2Role.cs / LivelihoodData.cs / IdentityData.cs / RelationshipEdge.cs / RelationshipGraph.cs / CharacterSnapshot.cs / AptitudeVector.cs`
- **Beasts/**
  - `LegendaryBeast.cs` (~252 lines) — beast entity with HP, aging, territorial behavior
  - `BeastFactory.cs / BeastSpawner.cs` — beast creation and world seeding
  - `BeastCatalog.cs / BeastCatalogLoader.cs / BeastCatalogFile.cs` — loads beasts.toml
  - `BeastSpeciesConfig.cs / BeastSpawnConfig.cs / CombatConfig.cs`

## WorldEngine.Sim/Simulation/
- `SimLoop.cs` — main tick loop: emit→resolve→commit cycle, speed control
- `PhaseRunner.cs` (~224 lines) — runs all 7 phases in order per tick; writes events to DB
- `SimRngSalts.cs` — integer salt constants used with WorldRng for reproducibility
- `EventCache.cs` — in-memory ring buffer of recent SimEvents for snapshot
- **Phases/** — one file per sim phase:
  - `EnvironmentalPhase.cs` (~611 lines) — disasters, climate drift, sea level, wildfire/flood/eruption/drought
  - `CharacterBehaviorPhase.cs` (~580 lines) — Tier1 AI: emit commands via UtilityScorer
  - `EntityBehaviorPhase.cs` (~391 lines) — beast and generic entity behavior
  - `Tier2BehaviorPhase.cs` (~409 lines) — specialist NPC behavior by role
  - `PopulationDynamicsPhase.cs` (~362 lines) — settlement growth, death, crystallisation, collapse
  - `ResourcePressurePhase.cs` (~361 lines) — food/water/resource ledger per settlement (territory-based since M3.0)
  - `TerritoryPhase.cs` — M3.0: annual city territory expansion/contraction

## WorldEngine.Sim/Events/
- `Payloads.cs` — all event payload records (one per EventType); serialised to JSON for storage
- `EventGate.cs` — significance filter: decides what makes it into the event log
- `SignificanceClassifier.cs` — scores events for Tier, VerbClass, PopulationImpact

## WorldEngine.Sim/World/
- `WorldState.cs` — mutable world state; sim thread only; source of truth during sim
- `IWorldStateReadOnly.cs` — read-only interface passed to entity logic
- `IHistoryGraphReadOnly.cs` — history query interface (see interface_contracts_events.md)
- `IHistoryQuery.cs` — M3.1: pre-indexed structured query API (GetCivSummary, GetRulersOfCiv, etc.)
- `HistoryTypes.cs` — M3.1: CharacterSummary, CivSummary, ConflictRecord record types
- `WorldSnapshot.cs` — immutable UI-facing projection; created after each tick
- `StateCache.cs` — thread-safe snapshot bridge between sim and UI threads
- `SnapshotBuilder.cs` (~228 lines) — builds WorldSnapshot from WorldState each tick
- `SimEvent.cs` — history log event record (immutable once written)
- `PendingEvent.cs` — pre-commit event emitted by phases; enriched by Phase 7
- `TileDisplayData.cs / TileInspectorData.cs` — UI tile rendering data
- `TileImprovement.cs` — M3.0: ImprovementType enum + TileImprovement record (Farm/Mine/etc.)
- `ActiveDisaster.cs / ActiveDrought.cs / BorderManifest.cs / BorderManifestStore.cs / BorderManifestSample.cs`
- `RuinRecord.cs / ResourceDeposit.cs`

## WorldEngine.Sim/Persistence/
- `EventStore.cs` — SQLite writes: events, entities, causal edges; BuildSummaries() + GetHistoryQuery(); WriteCivTrait()
- `DatabaseSchema.cs` — schema DDL (Events+SignificanceScore, CausalEdges, CharacterSummaries, CivSummaries, Eras, SuccessionChain, Dynasties, CivTraits)
- `SummaryBuilder.cs` — M3.1: post-sim pass building CharacterSummaries, CivSummaries (with CulturalTraits), SuccessionChain, Dynasties, Eras
- `CausalEdgeBuilder.cs` — M3.1: infers and writes causal edges from event patterns (war chains, disease→abandonment, etc.)
- `HistoryQueryService.cs` — M3.1: IHistoryQuery implementation backed by SQLite summary tables; small LRU cache
- `SignificanceRescoringPass.cs` — M3.2: retroactive significance pass; upgrades tiers for long-lived settlements/conquests; populates SignificanceScore float column

## WorldEngine.Sim/Vendor/
- `FastNoiseLite.cs` (~2505 lines) — **do not read or edit** — vendored noise library

## WorldEngine.UI/
- `Game1.cs` (~380 lines) — MonoGame entry: update/draw loop, StateCache reads, input routing; wires narrative UI panels post-StartSim

## WorldEngine.UI/UI/
- `EventLogPanel.cs` — sidebar event log; supports FocusLensState dimming and "->" cause-chain buttons per row
- `TileInspectorPanel.cs` — sidebar tile inspector showing settlement, beast, character, resource data
- `TimeControlsPanel.cs` — top toolbar: speed buttons, year/season label
- `WorldGenScreen.cs` — full-screen world-gen progress overlay
- `CharacterProfilePanel.cs` — M3.3: Myra panel showing character name, ancestry, life events, relationships; V2 narrative hook stub
- `CivHistoryPanel.cs` — M3.3: Myra panel showing civ arc (rulers, wars, major events, cultural traits); ComboBox civ selector; H key to toggle
- `TimelineBar.cs` — M3.3: SpriteBatch timeline scrubber drawn at bottom of map; event-density heatmap per decade, scrub handle
- `FocusLensState.cs` — M3.3: tracks focus target (character or civ); pre-fetches FocusedEventIds for event log filtering

## WorldEngine.Tests/
- xUnit test suite; mirrors Sim folder structure
- Key files: reproducibility tests, integration tests per phase, world gen tests
- `Integration/HistoryQueryTests.cs` — M3.1: SummaryBuilder, SuccessionChain, and HistoryQueryService integration tests
- `Integration/CulturalTraitsTests.cs` — M3.2: CulturalTrait enum, EvaluateCulturalTraits logic, CivTraitAcquired event generation
- `Integration/SignificanceScoringTests.cs` — M3.2: ComputeSignificanceScore, SignificanceRescoringPass tier upgrades and score population
- `Integration/NarrativeUIDataTests.cs` — M3.3: GetCausalChain, GetAllCivSummaries, GetEventCountByDecade, GetCharacterHistory ordering

## docs/perf/
- `notes_m3.md` — M3 performance profiling notes and gate status
