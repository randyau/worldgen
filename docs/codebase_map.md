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
- `SimConfigLoader.cs` — Tomlyn-based TOML loader; B1 strict mode detects unbound keys; B3 Validate() wired; B4 Load(profileName?, overrides?) merges profiles and --set overrides
- `SimConfigValidator.cs` — B3: validates ranges (probabilities in [0,1]), cross-field invariants (weight sums, threshold orderings, min≤max pairs); throws SimConfigValidationException
- `SimLoopConfig.cs` — B5: adds TicksPerSeason (alias) and TicksPerYear (= TicksPerSeasonalChange × 4) derived properties; use these everywhere instead of hardcoded 16
- `CharacterSimConfig.cs` (~300 lines) — all character behavior constants: needs decay, skill growth, diplomacy, war thresholds
- `AncestryConfig.cs` — per-ancestry personality/aptitude biases, name pools, spawn weights; M3.5: cultural descriptors (ArchitecturalStyle, SettlementDescriptor, BiomeAdaptations, ImprovementDescriptors, ArtisticTraditions, CivNameSuffix)
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
- `CivTracker.Naming.cs` — GenerateSettlementName, GenerateFertilityMultiplier, BiasedIndex, FireCivFounded, FireSettlementFounded; M3.5: ApplyCulturalSettlementName, GetCivNameSuffix, BuildCulturalProfile
- `CulturalProfile.cs` — M3.5: immutable record (AncestryId, ArchitecturalStyle, SettlementDescriptor, ArtisticTraditions, ActiveTraits, DominantBiome)
- `Civilization.cs` — mutable civ class: ruler, members, war state, border tension; M3.5: CulturalProfile?
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
- `SimLoop.cs` — main tick loop: emit→resolve→commit cycle, speed control; A1: RunSynchronous(ticks) for headless batch runs
- `PhaseRunner.cs` (~224 lines) — runs all 7 phases in order per tick; writes events to DB; A2: drives MetricsAccumulator YTD counts + calls MetricsCollector.Sample on annual tick
- `MetricsCollector.cs` — A2: samples WorldState once/year → writes yearly_metrics row; also defines MetricsAccumulator (YTD event counters) and YearlyMetricsRow moved to Persistence/
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
- `IHistoryQuery.cs` — M3.1: pre-indexed structured query API (GetCivSummary, GetRulersOfCiv, etc.); M3.4: adds GetTileHistory
- `HistoryTypes.cs` — M3.1: CharacterSummary, CivSummary, ConflictRecord record types
- `WorldSnapshot.cs` — immutable UI-facing projection; M3.4 adds CharacterWatchSnapshot + GoalWatchEntry
- `StateCache.cs` — thread-safe snapshot bridge between sim and UI threads
- `SnapshotBuilder.cs` — builds WorldSnapshot from WorldState each tick; M3.4: populates tile inspector territory/improvement/history + CharacterWatchSnapshot
- `SimEvent.cs` — history log event record (immutable once written)
- `PendingEvent.cs` — pre-commit event emitted by phases; enriched by Phase 7
- `TileDisplayData.cs` — UI tile display struct
- `TileInspectorData.cs` — tile inspect panel data; M3.4 adds territory/improvement/history fields
- `TileImprovement.cs` — M3.0: ImprovementType enum + TileImprovement record (Farm/Mine/etc.)
- `ActiveDisaster.cs / ActiveDrought.cs / BorderManifest.cs / BorderManifestStore.cs / BorderManifestSample.cs`
- `RuinRecord.cs / ResourceDeposit.cs`

## WorldEngine.Sim/Persistence/
- `EventStore.cs` — SQLite writes: events, entities, causal edges; BuildSummaries() + GetHistoryQuery(); WriteCivTrait(); A2: WriteMetricsRow/GetLastMetricsRow/GetMetricsRowCount for yearly_metrics
- `DatabaseSchema.cs` — schema DDL (Events+SignificanceScore, CausalEdges, CharacterSummaries, CivSummaries, Eras, SuccessionChain, Dynasties, CivTraits, yearly_metrics)
- `YearlyMetricsRow.cs` — A2: mutable class for one row of yearly_metrics; Dapper-friendly (parameterless constructor + settable properties)
- `SummaryBuilder.cs` — M3.1: post-sim pass building CharacterSummaries, CivSummaries (with CulturalTraits), SuccessionChain, Dynasties, Eras
- `CausalEdgeBuilder.cs` — M3.1: infers and writes causal edges from event patterns (war chains, disease→abandonment, etc.)
- `HistoryQueryService.cs` — M3.1: IHistoryQuery implementation backed by SQLite summary tables; small LRU cache
- `SignificanceRescoringPass.cs` — M3.2: retroactive significance pass; upgrades tiers for long-lived settlements/conquests; populates SignificanceScore float column
- `WorldStateDto.cs` — M3.6: DTO record tree mirroring WorldState; all fields JSON-serializable; WorldStateSerializerContext source-gen
- `WorldStateMapper.cs` — M3.6: ToDto/FromDto conversion; FromDto regenerates TileGrid from seed + advances EntityId counter
- `WorldStateSaver.cs` — M3.6: Save/Load/HasSave/ReadMeta/DeleteSave; Save writes meta.json + state.bin + config_snapshot/

## WorldEngine.Sim/Vendor/
- `FastNoiseLite.cs` (~2505 lines) — **do not read or edit** — vendored noise library

## WorldEngine.UI/
- `Game1.cs` — MonoGame entry: update/draw loop, StateCache reads, input routing; H=civ history, W=watch panel, T=territory overlay

## WorldEngine.UI/UI/
- `EventLogPanel.cs` — sidebar event log; FocusLensState dimming, cause-chain buttons, character name clickthrough (M3.3)
- `TileInspectorPanel.cs` — sidebar tile inspector; territory/improvement/history sections; [Watch] buttons per character (M3.4)
- `TimeControlsPanel.cs` — top toolbar: speed buttons, year/season label
- `WorldGenScreen.cs` — full-screen world-gen progress overlay
- `CharacterProfilePanel.cs` — M3.3: character name/ancestry/life events/relationships; V2 narrative hook stub
- `CivHistoryPanel.cs` — M3.3: civ arc (rulers, wars, major events, cultural traits); ComboBox civ selector; H key toggle
- `TimelineBar.cs` — M3.3: SpriteBatch timeline scrubber; event-density heatmap per decade, scrub handle
- `FocusLensState.cs` — M3.3: focus target (character or civ); pre-fetches FocusedEventIds for event log filtering
- `CharacterWatchPanel.cs` — M3.4: live character watch panel; needs bars, goals, personality; W key toggle

## WorldEngine.UI/Rendering/
- `TileMapRenderer.cs` — draws tiles + entity/settlement/ruin markers; M3.4: territory civ-color tint + improvement icons
- `OverlayRenderer.cs` — per-tile color for each OverlayType (Biome/Elevation/Temp/Moisture/Resources/Magic/Territory)
- `Camera2D.cs` — pan/zoom camera for the tile map

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
- `Integration/TileInspectTests.cs` — M3.4: TileInspectorData territory/improvement population, unclaimed tile returns null
- `Unit/AncestryConfigTests.cs` — M3.5: AncestryConfig field loading, ApplyCulturalSettlementName, GetCivNameSuffix, BuildCulturalProfile

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

## docs/
- `config_future.md` — TOML sections removed from sim_config.toml during B2 purge; preserved as design intent for unimplemented systems ([admin_distance], [spatial_buffer], [specialists], [artifacts], [cultural_modifiers], [civilization.settler_seeding]); includes dead-vs-live disagreement table
- `tuning_balance_review_2026-07-18.md` — tuning/balance review and Phase A–D improvement plan
