# World Engine — MVP Specification
**Version:** 0.3  
**Date:** June 18, 2026 (updated June 23, 2026)  
**Status:** Milestone 1 COMPLETE (2026-06-22). Milestone 2 COMPLETE (2026-06-23). Milestones 3-4 defined at summary level only.

---

## Milestone Overview

| Milestone | Name | Goal |
|---|---|---|
| 1 | The Living World | A world generates, simulates environmental history, produces a queryable log |
| 2 | The Character System | Beasts, then heroes, then lieutenants, then population — layered and playtested |
| 3 | Narrative Exploration | History becomes readable, authorable, and tunable |
| 4 | UI Experience | Polish, Spotlight, God Mode, distribution |

This document specifies **Milestone 1 in full**. Milestones 2-4 are described at summary level — they will be specced in detail when their time comes.

---

## Milestone 1: The Living World

### Goal

A world exists, time passes, things happen, history is recorded. No characters. The world itself is the actor — terrain forms, climate varies, disasters strike, resources deplete and regenerate. A basic UI lets you watch it happen and inspect what occurred.

### Success Criteria

- Generate a world from a seed and config parameters
- Run the simulation forward N years without crashing
- Query the event log and find a coherent history of environmental events
- The same seed always produces the same world and the same history
- A map renders showing the world's terrain and current state

### What Is Explicitly Out Of Scope For Milestone 1

- Any characters (Tier 1, 2, or 3)
- Population dynamics
- Civilizations
- Settlements
- Artifacts
- Religion
- The relationship graph
- Spotlight mode
- God Mode
- Narrative prose generation
- The full query/browse interface
- Save/load (the sim runs in memory for now; persistence comes with characters)

---

## Epic 1.1 — Project Foundation

**Goal:** The project compiles, tests run, and the skeleton architecture is in place. Nothing simulates yet.

**Why first:** Everything else depends on this. The project structure, config system, and test harness need to exist before any simulation code can be written.

### Stories

**1.1.1 — Solution and Project Setup**
Create the solution with three projects: `WorldEngine.Sim`, `WorldEngine.UI`, `WorldEngine.Tests`. Configure project references (UI → Sim, Tests → Sim only). Add all NuGet packages from the implementation decisions doc. Verify the solution builds.

Packages:
- Sim: `FastNoiseLite`, `Microsoft.Data.Sqlite`, `Dapper`, `MessagePack`, `Tomlyn`
- UI: `MonoGame.Framework.DesktopGL`, `Myra`
- Tests: `xunit`, `FluentAssertions`

**1.1.2 — SimConfig and TOML Loading**
Implement `SimConfig` root object and all sub-config classes (`WorldGenConfig`, `EventConfig`, `PerformanceConfig`, etc.). Implement `SimConfigLoader` using Tomlyn. Generate a default `sim_config.toml` if none exists. Write tests verifying config loads correctly and defaults are applied.

This is the first thing built because every subsequent story may need to add config entries.

**1.1.3 — Core Value Types**
Implement `TileCoord` (with East-West cylinder wrapping), `EntityId`, `CivId`, `EventId`. These are used everywhere — establish them early with correct semantics. Tests for TileCoord wrapping behavior specifically.

**1.1.4 — Logging and Headless Entry Point**
Set up structured logging (Microsoft.Extensions.Logging or Serilog — choose the simpler option). Create a headless console entry point in `WorldEngine.Sim` that can be invoked as: `worldengine --seed 12345 --years 500`. This is how the sim runs without a UI, and how most early testing will happen.

**1.1.5 — Test Harness Baseline**
Establish the test project structure (Unit/, Integration/, Reproducibility/ folders). Write one test in each category as a template — even if they just assert `true`. Verify `dotnet test` runs cleanly. Add the baseline reproducibility test structure (will be fleshed out in 1.3).

---

## Epic 1.2 — Tile World Data Structures

**Goal:** The world exists as data structures. No generation yet — just the containers.

**Why second:** World generation (1.3) fills these structures. The sim loop (1.4) reads them. Both need this to exist first.

### Stories

**1.2.1 — TileData Struct**
Implement `TileData` as a 14-byte struct with scaled integer fields (elevation, fertility, magic intensity, temperature, moisture), `BiomeType` enum, `TileFlags` (static bit flags), and `TileDynamicFlags` (dynamic bit flags). Add `RiverData` sub-struct for border crossing points.

**1.2.2 — TileChunk and TileGrid**
Implement `TileChunk` (16×16 tiles, null-able for ocean). Implement `TileGrid` with chunk-based storage, O(1) coordinate lookup, dirty-flag chunk tracking. Implement `WorldConfig` with km-based world dimensions and derived tile counts. Tests for chunk lookup, dirty flagging, coordinate wrapping edge cases.

**1.2.3 — BorderManifest**
Implement `BorderManifest` as a 64-sample-per-edge struct encoding elevation, moisture, water presence/width/depth, road crossing, cliff flags. Store border manifests in a separate array alongside the tile grid (accessed only during local generation, not during sim ticks). Tests for border sample lookup.

**1.2.4 — IWorldStateReadOnly**
Define the `IWorldStateReadOnly` interface — the read-only view of world state that entities and decision systems can see. This is what gets passed to `EmitCommands`. At this stage it exposes only tile access (no entities yet). The interface will grow as later epics add things to it.

---

## Epic 1.3 — World Generation

**Goal:** Given a seed and config, generate a complete world. The output is a populated `TileGrid` with terrain, climate, biomes, and resources.

**Why third:** The sim loop (1.4) needs a world to run on. The UI (1.7) needs a world to display.

### Stories

**1.3.1 — WorldGenContext and Pipeline Orchestrator**
Implement `WorldGenContext` (accumulates committed layer results, enforces commit order). Implement `WorldGenPipeline` with `RunFullAsync`, `RunUpToAsync`, and `RerunFromAsync` methods. Implement `IWorldGenLayer<TResult>` interface. Implement `LayerSeeds` constants. Tests for pipeline ordering enforcement.

**1.3.2 — Layer 1: Tectonics**
Generate tectonic plates using Voronoi partitioning seeded from `worldSeed + LayerSeeds.Tectonic`. Assign per-tile: plate ID, elevation contribution from plate type (oceanic/continental), volcanic zone flag (near subduction boundaries), fault line flag (plate boundaries), metal deposit potential. Produce `TectonicResult`.

**1.3.3 — Layer 2 and 3: Elevation and Ocean**
Generate heightmap from tectonic layer plus FastNoiseLite Simplex noise. Apply sea level threshold from `WorldConfig` to produce land/ocean mask, coastal flags, and ocean depth. Produce `ElevationResult` and `OceanResult`. Tests verify sea level produces correct land/ocean ratio.

**1.3.4 — Layer 4: Rivers**
Run flow accumulation on the committed elevation map. Rivers flow downhill, collect into trunks, terminate at ocean or inland lakes. Generate major river systems with named segments. Record river crossing points per tile edge (position 0.0-1.0 along the edge, width, flow volume) for later border manifest generation. Produce `RiverResult`. Tests verify rivers flow downhill and reach ocean.

**1.3.5 — Layer 5: Magic Intensity (Stub)**
Generate a noise layer with geological weighting — peaks at volcanic regions, river confluences, dramatic terrain. Store as a per-tile byte (0-255). No behavioral effect in Milestone 1. Leave `// V2: magic physical substrate` comment. Produce `MagicResult`.

**1.3.6 — Layer 6: Climate**
Generate temperature (latitude-based with elevation modifier, cylinder topology means clean North-South gradient) and precipitation (prevailing wind model, rain shadows from mountains, monsoon zones, storm corridors). Generate seasonal profiles per tile — how temperature and moisture vary across four seasons. Produce `ClimateResult`. Tests verify rain shadow behavior and polar temperature gradient.

**1.3.7 — Layer 7 and 8: Biomes and Resources**
Assign biomes from temperature + precipitation using Whittaker matrix. Flag transition zones. Derive base fertility from biome and water access. Place metal deposits with geological logic (iron in mountain cores, copper near volcanic zones, tin rare). Place rare resource deposits. Generate POI candidates (high magic intensity, dramatic geography, resource concentration). Produce `BiomeResult`, `ResourceResult`, `PoiResult`.

**1.3.8 — TileGrid Assembly**
Implement `BuildTileGrid` — reads all committed layer results and assembles `TileData` structs in parallel. Then computes border manifests for all tiles (neighbors must exist for this step). Layer result objects are released for GC after assembly. Full reproducibility test: `Generate(seed=12345)` twice → identical `TileGrid`. This is the first real reproducibility test — it must pass before proceeding to 1.4.

---

## Epic 1.4 — Simulation Loop

**Goal:** Time passes. The sim runs, ticks forward, and produces snapshots the UI can display.

**Why fourth:** The environmental simulation (1.5) and event system (1.6) need the loop to run in. The UI (1.7) needs snapshots to render.

### Stories

**1.4.1 — WorldState Shell**
Implement `WorldState` as the authoritative simulation state container. At this stage it holds: `TileGrid`, current year/season/tick, `WorldConfig` reference, the seeded RNG state. It will grow as later epics add entities, events, etc. `WorldState` is sim-thread-only — enforce this via internal access modifiers where possible.

**1.4.2 — WorldSnapshot**
Implement `WorldSnapshot` — the lightweight immutable projection the UI reads. At this stage: current year, season, speed, pause state, and `IReadOnlyDictionary<TileCoord, TileDisplayData>` for the visible map region. `TileDisplayData` contains just what the renderer needs (biome, elevation, flags, current moisture).

**1.4.3 — StateCache**
Implement `StateCache` with double-buffered snapshot swap behind a `ReaderWriterLockSlim`. Sim thread calls `Commit(snapshot)` after each tick. UI thread calls `Read()` every frame. Lock held for microseconds only. Tests for thread safety under concurrent read/write.

**1.4.4 — CommandQueue**
Implement `CommandQueue` using `System.Threading.Channels`. UI thread writes `ICommand` instances. Sim thread drains at tick start. Bounded channel with `DropOldest` policy for snapshot channel (UI only needs latest). Unbounded channel for commands (don't drop player inputs).

**1.4.5 — PhaseRunner**
Implement the seven-phase tick execution. At this stage Phases 1 (Environmental) and 7 (Event Generation) are real. Phases 2-6 are empty stubs that will be filled in subsequent epics and milestones. Phase 7 only runs the event gate and database write — no significance scoring yet (that comes in 1.6).

**1.4.6 — SimLoop**
Implement `SimLoop` — the main sim thread loop. Drains `CommandQueue` at tick start. Runs `PhaseRunner`. Commits `WorldSnapshot` to `StateCache`. Throttles to `SimSpeed` setting. Handles pause/resume/step. The loop runs on a dedicated thread. Tests verify tick ordering and speed control commands are respected.

**1.4.7 — Time Control Commands**
Implement `SetSimSpeed`, `StepOneTick`, `PauseToggle` commands. Handle them in the sim loop's command drain step. Implement the `SimSpeed` enum and the throttle logic (Slow = 1 seasonal tick/2 seconds, Normal = 1/second, Fast = 10/second, Ultrafast = annual ticks as fast as possible).

---

## Epic 1.5 — Environmental Simulation

**Goal:** Things happen in the world. Climate varies seasonally. Disasters strike. Resources change. The world has a history even without characters.

**Why fifth:** This is what fills the event log. Without this, the sim runs but nothing happens.

### Stories

**1.5.1 — Seasonal Climate Variation**
Each seasonal tick, per-tile current moisture and temperature update from their base values according to the tile's seasonal profile. Monsoon seasons increase moisture in monsoon zones. Storm corridors have increased weather event probability in their seasons. These are tile dynamic flag and current moisture updates — not events (too granular to log individually).

**1.5.2 — Long-Term Climate Drift**
Over centuries, regional climate slowly shifts. Temperature trends (warming or cooling periods), precipitation pattern shifts, desertification of over-farmed regions, reforestation of abandoned areas. These are slow processes driven by cumulative tile-level changes. When a region's biome changes (e.g., grassland becomes desert after prolonged drought), that IS logged as an event.

**1.5.3 — Natural Disaster System**
Implement probability-based disaster events. Each tick, per relevant tile or region, check disaster probabilities from `SimConfig`. When a disaster fires:
- **Volcanic eruption:** Destroys terrain, creates ash deposits (fertility penalty), may raise elevation, spreads to adjacent tiles
- **Earthquake:** Terrain deformation, may trigger landslides, permanently alters elevation
- **Flood:** Sea level rise event (temporary or permanent), affects coastal and river tiles
- **Wildfire:** Spreads through forest biomes, converts to grassland, respects wind direction
- **Drought:** Extended precipitation deficit, reduces fertility, may trigger desertification
- **Plague (environmental):** Disease outbreak in absence of characters — affects resource productivity

All disasters emit events to the event log. Disaster probabilities in `SimConfig`.

**1.5.4 — Resource Dynamics**
Resources regenerate and deplete over time without population pressure. Forests regrow after wildfire (slow). Fish populations recover in ocean/river tiles. Soil fertility recovers after drought. Metal deposits don't regenerate (they're geological). These are background tile updates — not events unless a significant threshold is crossed (e.g., a region reaches full fertility recovery after a disaster).

**1.5.5 — Sea Level Changes**
Implement gradual sea level change as a slow-moving process. A `SeaLevelChangeEvent` fires when the world sea level shifts by a threshold amount, flooding or exposing coastal tiles. This is a world-spanning event and always Headline tier. Affects tile ocean/land flags and border manifests for affected tiles.

---

## Epic 1.6 — Event System

**Goal:** The history log exists, events get recorded, and the database is queryable.

**Why sixth:** The environmental simulation (1.5) needs somewhere to write events. The UI (1.7) needs to display them.

### Stories

**1.6.1 — SimEvent and EventType**
Implement `SimEvent` record with all fields (Id, Type, Year, Season, Tick, Location, TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode, PayloadJson). Implement `EventType` enum — start with the environmental subset only (disasters, climate events, resource events, biome changes). Full ~90-type taxonomy comes in Milestone 2 when characters arrive. Implement `VerbClass` enum and the `VerbClassification` lookup table for the environmental event types.

**1.6.2 — Event Gate**
Implement `EventGate` with `ShouldRecord(SimEvent)` check. Reads from `SimConfig.Events.Gate` — suppressed types list, always-record types list, minimum tier to record. Start with a permissive gate (record almost everything above Background). Log a counter of gated-vs-recorded events per tick for tuning visibility.

**1.6.3 — SQLite Schema and EventStore**
Create the SQLite database schema (Events, CausalEdges tables — EventEntities comes with characters in Milestone 2). Implement `EventStore` using Dapper for object mapping. Implement `InsertEvent`, `GetEvent`, `QueryByYear`, `QueryByType` methods. Write the Phase 7 commit transaction. Tests verify events round-trip correctly through the database.

**1.6.4 — Significance Classification**
Implement the rule-based classifier. For Milestone 1 (no characters, no population): TierInvolvement classification is simplified (most environmental events are Background or Regional — only world-spanning events like sea level change are Headline). VerbClass classification from the lookup table. PopulationImpact is zero (no population yet). IsFirstOfKind check against recent event cache. Store tags on the event record before writing.

**1.6.5 — Event Hot Cache**
Implement `EventCache` as a circular buffer of recent events (size from `SimConfig.Performance.EventCacheSize`). Phase 7 adds to cache after writing to database. Cache feeds the UI event log panel and the IsFirstOfKind check without database queries.

**1.6.6 — Database Tooling for Exploration**
Not a code feature — a set of documented SQL queries in `docs/event_log_queries.md` that developers can run against `world.db` to explore what the sim is generating. Example queries: event type distribution, events by year range, events by tier, causal chain traversal stub. This is the primary validation tool for Milestone 1 — "is the history log generating coherent content?"

---

## Epic 1.7 — Basic UI

**Goal:** A window opens, you can see the world, watch time pass, and inspect what's happening.

**Why last:** Needs everything else to exist first. The UI consumes WorldSnapshot and queries the EventStore — both must exist.

### Stories

**1.7.1 — MonoGame Window and Game Loop**
Get a MonoGame window open with a basic game loop. Initialize Myra desktop. Hook up the sim thread (start SimLoop on a background thread). Verify the two-thread model works — sim runs on its thread, UI renders on main thread, StateCache bridges them.

**1.7.2 — Tile Map Renderer**
Render the world as a colored tile grid. Color-code by biome (green = forest, tan = desert, blue = water, etc.). Support mouse pan and zoom. `WorldSnapshot.AllTiles` contains the full world grid; `TileMapRenderer` computes the visible range from `Camera2D` each frame so pan and zoom are immediately responsive independent of sim tick rate.

**1.7.3 — Map Overlay Toggles**
Layer toggles for the map: Terrain (biome colors), Elevation (grayscale heightmap), Climate (temperature or moisture), Resources (deposit locations), Magic Intensity (heatmap). Implemented as keyboard shortcuts and a simple Myra toolbar. The snapshot includes the currently active overlay data.

**1.7.4 — Tile Inspector**
Click a tile → a Myra panel shows that tile's data: biome, elevation, fertility, temperature, moisture, magic intensity, current dynamic flags. Reads directly from `WorldState` via a query method — not from the snapshot (tile inspector is a point query, not a bulk update). Since this reads WorldState, it goes through a thread-safe query API rather than direct access.

**1.7.5 — Event Log Panel**
A scrollable Myra panel showing recent events from the `EventCache`. Each entry shows: year, season, event type, location (if any), tier tag. Color-coded by tier (Headline = gold, Regional = white, Character = grey). Auto-scrolls to latest. Player can pause auto-scroll to read. Filter by tier (checkbox toggles).

**1.7.6 — Time Controls**
Myra toolbar with: Pause/Play button, speed selector (Slow/Normal/Fast/Ultrafast), current year/season display, ticks-per-second counter (for performance monitoring). All controls push `ICommand` instances to the `CommandQueue`.

**1.7.7 — World Generation UI**
Before simulation starts, show a world generation screen. Display progress per layer (layer name, progress bar). On completion, show the generated map and a "Start Simulation" button. The layered preview adjustment (player tweaks sea level etc.) is NOT in Milestone 1 — just the progress display and final result.

---

## Milestone 1 Definition of Done — ACHIEVED 2026-06-22

1. `scripts/publish-win.sh` produces a self-contained Windows exe; launching it shows a generated world ✓
2. Time passes visibly (year counter increments, events appear in the log) ✓
3. Disasters fire and appear in the event log with correct tier classification ✓
4. Climate varies seasonally (visible in the climate overlay, keyboard shortcuts B/E/T/M/R/G) ✓
5. `world.db` contains a coherent event history queryable with `docs/queries/event_log_queries.md` ✓
6. The same seed produces the same world (192 tests pass, including SameSeedProducesSameWorld) ✓
7. All tests pass with zero warnings on WorldEngine.Sim ✓

**Manual test runbook:** `docs/testing/runbook_m1.md`

**Notes on story 1.6.6 (Database Tooling):** Completed as `docs/queries/event_log_queries.md` — SQL queries covering health checks, event distribution, temporal/spatial analysis, causal graph validation, and environmental sim validation.

**Notes on 1.7.4 (Tile Inspector):** Reads from `WorldSnapshot.InspectedTile` (not directly from `WorldState`) — the sim thread builds `TileInspectorData` on `SetInspectedTile` command and includes it in the next snapshot. This is consistent with the two-thread model.

**Known divergence from spec:** No `WorldGenPipeline` class — world gen runs via direct layer calls and `TileGridAssembler.Assemble()`. See `WorldEngine.UI/Game1.cs` for the actual sequence.

---

## Milestone 2: The Character System — COMPLETE (2026-06-23)

Characters enter the world in layers, with playtesting between each layer.

**Phase 2.1 — Legendary Beasts** ✓  
Tier 1 entities with simple needs (food, safety, territory) and behaviors (roam, eat, fight, reproduce, die). No politics, no goals beyond survival. Validates the entity model, relationship graph, and character event types in isolation.

**Phase 2.2 — Tier 1 Characters** ✓  
Heroes, rulers, legendary figures. Full personality/aptitude/skills system (12-trait PersonalityVector, 6-trait AptitudeVector, 8-skill SkillVector, 7-need NeedsVector). Goals, utility scoring with softmax temperature, 8-action library. Civilization emergence via CivTracker. EventEntities DB table for character history queries.

**Phase 2.3 — Tier 2 Characters** ✓  
6 specialist roles (General, Governor, Merchant, Scholar, Physician, Artisan). Livelihood system (LivelihoodData: role, employer, settlement). Simplified 6-trait PersonalityVector6, 4-need NeedsVector4. Crystallization to Tier 1 on high Ambition + Status.

**Phase 2.4 — Tier 3 Population** ✓  
Aggregate population dynamics via PopulationDynamicsPhase. Settlement growth (fertility × safetyScore × PopGrowthRate) and decline (PopDecayRate). Specialist crystallization from population pressure (configurable thresholds per role). Civ-born character generation: new Tier 1 heroes emerge from stable settlements proportional to population. Population-driven events (SettlementGrew/Shrank/Abandoned). Post-playtest fixes: balanced growth vs. decay rates; gated EstablishSettlement to prevent multi-settlement founding.

Bug fixes applied post-M2 close: `PhaseRunner` EntityId long truncation (int→long in foreach); `CharacterBehaviorPhase` operator precedence in civ-born seq derivation.

**Post-M2 Character Narrative Improvements** ✓ (June 23, 2026)  
A second pass addressing narrative gaps found during playtest — zero wars/conflicts in 449 simulated years:

- **Settlement map visibility** — `SettlementSnapshot` added to `WorldSnapshot`; map renderer draws colored markers; inspector shows settlement section (civ, pop, health, founded year).
- **Wanderlust system** — `TicksInCurrentTile` counter on `Tier1Character`; travel utility score scales with time stationary. Role multipliers: founders 15%, members 50%, free agents 100%. Curiosity floor ensures even low-curiosity chars occasionally wander.
- **Social action cap** — Changed from one social action per co-located character to one best-target social action per tick total, improving action diversity and travel frequency.
- **Event log suppression** — `Negotiated` events suppressed in config (were 79% of all events, flooding the DB).
- **Territorial trust drain** — Aggressive founders (Aggression > 0.55) on their settlement tile drain trust of foreign-civ visitors by 0.025/tick — seeds conflict that was previously impossible.
- **Beast-character combat** — Aggressive predators on same tile as characters have 15% chance to attack per tick. Added `EventType.BeastAttackedChar = 2007`. Characters can die from beast attacks.
- **Ancestry system** — 6 ancestries (human, elf, dwarf, dark_elf, orc, halfling) loaded from `config/ancestries.toml`. Ancestry selected at spawn by biome weights; personality/aptitude biases shift trait distributions; ancestry-specific name lists; distinct lifespan ranges. Cultural trust drains: first-meeting modifier applied once; per-tick drain scaled by cultural distance + personality stability mismatch. `AncestryId` stored on `IdentityData`, `EntitySnapshot`, `CharacterSnapshot`. Inspector shows ancestry next to name.

**Post-M2 Goal System + Wellbeing** ✓ (June 23, 2026)  
Symmetric emotional model on `Tier1Character.Wellbeing` (float -1 spiraling → +1 flourishing). Five bands: Flourishing (≥0.7), Content (≥0.3), Neutral, Distressed (≤-0.3), Spiraling (≤-0.7). Goal types extended with Bond, Create, Protect, Avenge, Grieve, Acquire, Flee, Endure. Goal objects extended with companion Person targets and resource tags. Grief spirals on trusted-person death; art/joy upward spirals on Flourishing characters. Resource pressure via `ResourcePressurePhase` seeds Acquire/Flee goals. `CharacterGrieved`, `CharacterFlourishing`, `CharacterSpiraling`, `ArtworkCreated`, `GoalFormed`, `GoalResolved`, `SettlementStraining` event types added.

**Post-M2 Settlement Economics** ✓ (June 23, 2026)  
Settlement identity, reach-based resource extraction, and ledger-aware merchant trade:
- **Settlement names** — deterministic `Prefix+Suffix` name generated from world seed + tile coord at founding; biome-biased prefix selection; shown prominently in inspector and event log.
- **Resource ledger** — extensible `Dictionary<string, float>` on `SettlementStub.ResourceLedger` covering food, water, timber, and any mineral deposit type. New resource types flow through automatically from config. Rebuilt from reach tiles each tick by `ResourcePressurePhase`.
- **Settlement reach** — radius `clamp(2 + pop/2000, 2, 5)` tiles; all land tiles within reach contribute resources.
- **Founding score** — characters now consider deposit value + route position bonus (between existing settlements) when evaluating `EstablishSettlement`; valuable deposits allow settling otherwise low-fertility tiles.
- **Merchant trade** — Tier2 merchants pick the most complementary destination (home surplus vs. dest deficit) and transfer 10% of the surplus in the ledger. `MerchantTradeCompleted` events include the traded resource.

---

## Milestone 3: Narrative Exploration (Summary)

History becomes readable and authorable.

- Full significance classification and retroactive rescoring
- The filter panel and focus lens system
- The "tell me what led to this" causal chain view
- Character profile cards and civilization history views
- Timeline scrubber with historical snapshot rendering
- LLM prose generation hook (V2 feature — may or may not land in this milestone)
- God Mode authoring tools

---

## Milestone 4: UI Experience (Summary)

Polish and features for the full experience.

- Spotlight mode (player controlling a character)
- Layered world gen preview with player adjustment
- Full save/load system
- Modding/config exposure to players
- Performance optimization for very long runs (10k+ years)
- Distribution and platform support
- Any remaining God Mode tools

---

*Document Version: 0.4*  
*Last Updated: June 23, 2026 (Milestone 2 complete + goal system + settlement economics)*
