# World Engine — Implementation Decisions
**Version:** 0.3  
**Date:** June 18, 2026  
**Status:** All Tier A and Tier B decisions complete  
**Changes from v0.2:** Section 23 (B5 Significance Scoring) fully specced. Section 24 (Event Gate and Player Filters) added. "Decisions Pending" section removed — all decisions are now made.  
**Companion document:** World Engine Design Specification v0.3

---

## Table of Contents

1. Language and Platform
2. Architecture: Headless Sim Core
3. Concurrency Model
4. The Command Pattern
5. State Cache and UI Boundary
6. Time Controls
7. Framework and Library Stack
8. Project Structure
9. Entity Model
10. Tile Grid Memory Layout
11. Two-Scale World Architecture
12. WorldState Persistence and Database
13. Save and Load Model
14. The Event Log
15. Spatial Buffer Implementation
16. Character Decision-Making
17. Character Trait System
18. Specialist NPCs
19. Administrative Distance Penalty
20. Cultural Trait Modifier Storage
21. World Generation Layer Handoff
22. Simulation Configuration
23. Event Significance and History Filtering
24. Patterns Reference

---

## 1. Language and Platform

### Decision: C# on .NET 8

**Chosen over:** Rust, Python, Kotlin/JVM, Go

**Why C#:**
- .NET 8 is genuinely cross-platform (Windows/Linux/Mac as first-class targets)
- No classpath, no daemon, simple `dotnet` CLI
- Strong type system catches design drift during long Claude Code sessions
- True parallelism (no GIL)
- Managed memory handles large heaps and cyclic entity references
- Excellent library ecosystem for the specific domains needed
- Self-contained executables via `dotnet publish --self-contained`

**Why not Rust:** Borrow checker creates sustained friction for graph structures and entities referencing each other. The command pattern mitigates the worst of it but data model problems remain.

**Why not Python:** GIL prevents true parallelism in Phase 4/5. Python objects carry 50-200 bytes overhead — tile grids and causal graphs at scale hit memory ceilings.

**Why not Kotlin/JVM:** JVM classpath complexity and version conflicts create unpredictable friction. .NET 8 cross-platform story is cleaner.

**Why not Go:** Weak plugin/modding story. Go plugins are notoriously painful on non-Linux.

### Cross-Platform Target
Windows/Linux/Mac. `MonoGame.Framework.DesktopGL` (OpenGL backend) — never the DirectX backend. `dotnet publish -r <rid> --self-contained` for distribution.

---

## 2. Architecture: Headless Sim Core

### Decision: Strict separation between sim core and UI, enforced at the project level

**The Rule:** `WorldEngine.Sim` has zero references to any UI library, rendering library, or input library. Ever.

The UI project references the Sim project. The Sim project does not know the UI exists.

### Three Rules That Minimize Extraction Cost
1. Save files are plain JSON/MessagePack with a schema you define
2. SimCore has zero UI dependencies (enforced by project reference structure)
3. Commands and Events are plain data structs (trivially portable)

### The Boundary
Two shared structures cross the sim/UI boundary. Nothing else does:
- **StateCache** — sim writes after each tick; UI reads each frame
- **CommandQueue** — UI writes player actions; sim drains at tick start

---

## 3. Concurrency Model

### Decision: Two threads, shared only via StateCache and CommandQueue

```
SIM THREAD                          UI THREAD
Owns WorldState          ──────▶   Reads WorldSnapshot
Runs tick loop           StateCache Renders map + panels
Produces snapshots                  Handles mouse/keyboard
Drains CommandQueue      ◀──────   Writes Commands
```

**Primitives:** `System.Threading.Channels` (built into .NET) for both the CommandQueue and snapshot passing.

**Thread ownership rules:**
- `WorldState` owned exclusively by the sim thread
- `WorldSnapshot` is immutable once created — UI reads freely, no locking
- Causal history graph is append-only — safely exposed as read-only reference to UI thread

---

## 4. The Command Pattern

### Decision: All entity actions and player inputs are Commands — plain data structs pushed to a queue, resolved in a separate pass

Commands are sealed records — immutable plain data describing intent, never references or callbacks.

### The Tick Phase Loop
Each phase executes three steps in strict order:
1. **READ** — entities read current WorldState (immutable)
2. **EMIT** — entities produce `List<ICommand>` (no world mutation)
3. **RESOLVE** — command queue drains, WorldState mutates (single writer)

### Contention Triage Protocol
Runs at end of Phases 4 and 5 before atomic commit. When multiple commands target the same unique object:
1. Identify contention — scan for duplicate targets on unique objects
2. Determine priority — relevant stat first, then `Hash(entityId + worldSeed + currentTick)` as deterministic tiebreak
3. Resolve — winning command applies; losers become `FailedAttempt` events
4. Record failures — feed Phase 6 and Phase 7 as narrative events

Contention involving violence routes directly to Phase 6.

### Player Actions as Commands
Player inputs (Spotlight, God Mode, time controls) are the same `ICommand` type pushed to CommandQueue. No special handling.

---

## 5. State Cache and UI Boundary

### Decision: Double-buffered WorldSnapshot exposed to UI via StateCache

`WorldState` is the full simulation state — lives exclusively in the sim thread.

`WorldSnapshot` is an immutable projection — everything the UI needs to render. `AllTiles` is a flat `TileDisplayData[]` covering the full world grid (indexed `y * WorldTileWidth + x`). The sim builds it unconditionally each tick; the renderer filters to the visible region using the camera on the UI thread.

StateCache holds the latest snapshot behind a `ReaderWriterLockSlim`. Lock held for microseconds (single pointer assignment). UI rendering is never meaningfully blocked.

**Camera viewport is UI-thread-only.** `TileMapRenderer` calls `Camera2D.ScreenToTile()` each frame to compute the visible tile range — no `SetViewport` command, no sim-thread round-trip. Pan and zoom are immediately responsive regardless of sim tick rate. `Game1` skips Myra widget rebuilds when the snapshot reference is unchanged (reference equality on `StateCache.Read()`), so label reflows only happen when the sim commits new data.

### History Graph Access
The causal event graph is append-only. Exposed to the UI as `IHistoryGraphReadOnly` — no locks needed. UI traverses it freely for profile cards, timeline scrubbing, story thread queries.

---

## 6. Time Controls

### Decision: Time controls are Commands sent from UI to sim via CommandQueue

```csharp
public enum SimSpeed { Paused, Slow, Normal, Fast, Ultrafast }
public sealed record SetSimSpeed(SimSpeed Speed) : ICommand;
public sealed record StepOneTick() : ICommand;
public sealed record RunYears(int Count) : ICommand;
```

In `Ultrafast` mode, snapshots push every N simulation years rather than every tick.

### Spotlight Performance Contract
During Spotlight:
1. Run 1 global seasonal tick (whole world, lightweight)
2. Run N daily ticks for local region + Spatial Buffer

---

## 7. Framework and Library Stack

### Sim Core Dependencies

| Library | NuGet Package | Purpose |
|---|---|---|
| FastNoiseLite | `FastNoiseLite` | Procedural noise for world gen |
| Microsoft.Data.Sqlite | `Microsoft.Data.Sqlite` | SQLite database driver |
| Dapper | `Dapper` | Lightweight SQL object mapping |
| MessagePack | `MessagePack` | Binary serialization for state.bin and snapshots |
| Tomlyn | `Tomlyn` | TOML config file parsing |
| System.Text.Json | Built into .NET | Human-readable JSON for config and metadata |
| System.Threading.Channels | Built into .NET | CommandQueue and snapshot channel |

### UI Dependencies

| Library | NuGet Package | Purpose |
|---|---|---|
| MonoGame.Framework.DesktopGL | `MonoGame.Framework.DesktopGL` | Cross-platform 2D rendering, OpenGL |
| Myra | `Myra` | UI widget toolkit for MonoGame |
| SixLabors.ImageSharp | `SixLabors.ImageSharp` | Debug only — render world gen layers to PNG |

### Test Dependencies

| Library | NuGet Package | Purpose |
|---|---|---|
| xunit | `xunit` | Test framework |
| FluentAssertions | `FluentAssertions` | Readable assertion syntax |

### Explicitly Not Used
- No ECS framework — command pattern solves parallel resolution without it
- No ORM — Dapper is sufficient
- QuikGraph removed — relationship graph is a simple in-memory adjacency list; causal graph lives in SQLite
- No Lua embedding — modding story is V2

---

## 8. Project Structure

```
WorldEngine/
├── WorldEngine.sln
├── config/
│   ├── sim_config.toml             # Active simulation constants
│   └── profiles/
│       ├── brutal.toml
│       ├── sandbox.toml
│       └── historical.toml
├── docs/                           # All design and specification documents
│
├── WorldEngine.Sim/                # Headless sim core — ZERO UI references
│   ├── WorldEngine.Sim.csproj
│   ├── Config/
│   │   ├── SimConfig.cs
│   │   ├── AdminDistanceConfig.cs
│   │   ├── TraitConfig.cs
│   │   ├── NeedsConfig.cs
│   │   ├── GoalConfig.cs
│   │   ├── UtilityConfig.cs
│   │   ├── CivConfig.cs
│   │   ├── SpecialistConfig.cs
│   │   ├── ArtifactConfig.cs
│   │   ├── EventConfig.cs
│   │   ├── WorldGenConfig.cs
│   │   ├── CulturalModifierConfig.cs
│   │   ├── PerformanceConfig.cs
│   │   └── SimConfigLoader.cs
│   ├── Loop/
│   │   ├── SimLoop.cs
│   │   ├── PhaseRunner.cs
│   │   ├── CommandResolver.cs
│   │   └── CommandQueue.cs
│   ├── World/
│   │   ├── WorldState.cs
│   │   ├── WorldSnapshot.cs
│   │   ├── StateCache.cs
│   │   ├── WorldContext.cs
│   │   └── TileGrid.cs
│   ├── Generation/
│   │   ├── WorldGenPipeline.cs
│   │   ├── WorldGenContext.cs
│   │   ├── IWorldGenLayer.cs
│   │   ├── LayerSeeds.cs
│   │   ├── Layers/
│   │   │   ├── TectonicLayer.cs
│   │   │   ├── ElevationLayer.cs
│   │   │   ├── OceanLayer.cs
│   │   │   ├── RiverLayer.cs
│   │   │   ├── MagicLayer.cs
│   │   │   ├── ClimateLayer.cs
│   │   │   ├── BiomeLayer.cs
│   │   │   ├── ResourceLayer.cs
│   │   │   └── PoiCandidateLayer.cs
│   │   └── Results/
│   │       ├── TectonicResult.cs
│   │       ├── ElevationResult.cs
│   │       ├── OceanResult.cs
│   │       ├── RiverResult.cs
│   │       ├── MagicResult.cs
│   │       ├── ClimateResult.cs
│   │       ├── BiomeResult.cs
│   │       ├── ResourceResult.cs
│   │       └── PoiResult.cs
│   ├── Tiles/
│   │   ├── TileData.cs
│   │   ├── TileChunk.cs
│   │   ├── TileCoord.cs
│   │   ├── TileFlags.cs
│   │   ├── TileDynamicFlags.cs
│   │   ├── BorderManifest.cs
│   │   └── LocalScale/
│   │       ├── LocalTileGrid.cs
│   │       ├── LocalTileData.cs
│   │       ├── LocalTileGenerator.cs
│   │       └── ActiveRegion.cs
│   ├── Entities/
│   │   ├── IEntity.cs
│   │   ├── ISpotlightable.cs
│   │   ├── EntityId.cs
│   │   ├── EntityKind.cs
│   │   ├── EntityRegistry.cs
│   │   ├── Characters/
│   │   │   ├── Tier1Character.cs
│   │   │   ├── Tier2Character.cs
│   │   │   ├── Tier2Class.cs
│   │   │   └── Components/
│   │   │       ├── IdentityData.cs
│   │   │       ├── PersonalityVector.cs
│   │   │       ├── AptitudeVector.cs
│   │   │       ├── SkillVector.cs
│   │   │       ├── NeedsVector.cs
│   │   │       ├── HealthData.cs
│   │   │       ├── LoyaltyData.cs
│   │   │       ├── GoalData.cs
│   │   │       ├── AweData.cs
│   │   │       └── Tier2State.cs
│   │   ├── Specialists/
│   │   │   ├── SpecialistRole.cs
│   │   │   ├── SpecialistRoleType.cs
│   │   │   ├── SpecialistLivelihood.cs
│   │   │   ├── LivelihoodState.cs
│   │   │   ├── SpecialistGood.cs
│   │   │   └── SpecialistGoodType.cs
│   │   └── Special/
│   │       ├── Settlement.cs
│   │       ├── Army.cs
│   │       ├── TradeCaravan.cs
│   │       ├── RefugeeGroup.cs
│   │       ├── DiseaseOutbreak.cs
│   │       └── LegendaryBeast.cs
│   ├── Commands/
│   │   ├── ICommand.cs
│   │   ├── MovementCommands.cs
│   │   ├── CombatCommands.cs
│   │   ├── PoliticalCommands.cs
│   │   ├── ArtifactCommands.cs
│   │   ├── RelationshipCommands.cs
│   │   ├── SpecialistCommands.cs
│   │   ├── TimeControlCommands.cs
│   │   └── GodModeCommands.cs
│   ├── Decisions/
│   │   ├── DecisionContext.cs
│   │   ├── PerceivedWorldState.cs
│   │   ├── Actions/
│   │   │   ├── IAction.cs
│   │   │   ├── ActionType.cs
│   │   │   ├── ActionLibrary.cs
│   │   │   └── ActionSelector.cs
│   │   ├── Goals/
│   │   │   ├── Goal.cs
│   │   │   ├── GoalPriority.cs
│   │   │   └── GoalSpawnRules.cs
│   │   ├── UtilityComputer.cs
│   │   ├── NeedsUpdater.cs
│   │   └── GoalPriorityComputer.cs
│   ├── Relationships/
│   │   ├── RelationshipGraph.cs
│   │   ├── RelationshipEdge.cs
│   │   └── RelationshipType.cs
│   ├── Events/
│   │   ├── SimEvent.cs
│   │   ├── EventType.cs
│   │   ├── EventGate.cs
│   │   ├── EventGateConfig.cs
│   │   ├── SignificanceClassifier.cs
│   │   ├── EventCache.cs
│   │   └── Payloads/
│   │       └── (one file per major event type group)
│   ├── History/
│   │   ├── IHistoryGraphReadOnly.cs
│   │   ├── HistoryQuery.cs
│   │   └── SnapshotStore.cs
│   ├── Civilizations/
│   │   ├── CivilizationTracker.cs
│   │   ├── CivState.cs
│   │   ├── AuthorityAnchor.cs
│   │   ├── AuthorityAnchorRegistry.cs
│   │   ├── InfluenceMap.cs
│   │   └── InfluenceMapCache.cs
│   ├── Culture/
│   │   ├── CulturalTraitModifier.cs
│   │   ├── ModifierTarget.cs
│   │   ├── ModifierEffect.cs
│   │   ├── ModifierType.cs
│   │   ├── ActiveModifierRegistry.cs
│   │   └── ModifierInjectionRules.cs
│   ├── Religion/
│   │   └── ReligionTracker.cs
│   ├── Artifacts/
│   │   └── ArtifactTracker.cs
│   ├── Voxels/
│   │   ├── VoxelGenerator.cs
│   │   └── VoxelCache.cs
│   └── Persistence/
│       ├── PersistenceManager.cs
│       ├── SimState.cs
│       ├── DatabaseSchema.cs
│       ├── EventStore.cs
│       ├── CharacterArchive.cs
│       ├── ArtifactStore.cs
│       ├── SnapshotStore.cs
│       └── ModifierStore.cs
│
├── WorldEngine.UI/
│   ├── WorldEngine.UI.csproj
│   ├── Game1.cs
│   ├── Rendering/
│   │   ├── TileMapRenderer.cs
│   │   ├── EntityRenderer.cs
│   │   ├── TileAtlas.cs
│   │   └── LayerToggles.cs
│   ├── Panels/
│   │   ├── InfoPanel.cs
│   │   ├── EventLog.cs
│   │   ├── CommandMenu.cs
│   │   ├── TimelinePanel.cs
│   │   └── IndexPanel.cs
│   ├── Input/
│   │   ├── InputHandler.cs
│   │   ├── ContextMenuBuilder.cs
│   │   └── KeyboardShortcuts.cs
│   └── GodMode/
│       ├── GodModeOverlay.cs
│       └── ParameterPrompt.cs
│
└── WorldEngine.Tests/
    ├── WorldEngine.Tests.csproj
    ├── Unit/
    ├── Integration/
    └── Reproducibility/
```

---

## 9. Entity Model

### Three-Layer Data Model

**Layer 1: Uniform Behavioral Interface (`IEntity`)**
Every entity implements `IEntity`. The sim loop only ever sees `IEntity`. Never knows what kind of entity it's talking to.

**Layer 2: Component Data Structs**
Plain structs — pure data, no behavior, easily serializable:
- `PersonalityVector` — 12 personality traits
- `AptitudeVector` — 6 aptitude traits
- `SkillVector` — 8 skills
- `NeedsVector` — 7 needs
- `IdentityData`, `HealthData`, `LoyaltyData`, `GoalData`, `AweData`

**Layer 3: Concrete Entity Classes**
Each entity type holds the components it needs and implements `IEntity`.

### Entity Storage: EntityRegistry
Maintains: `Dictionary<EntityId, IEntity>` (canonical), typed parallel lists for Tier1/Tier2/Settlements/etc., `Dictionary<TileCoord, HashSet<EntityId>>` spatial index.

### Relationships
Centralized `RelationshipGraph` — NOT on entity objects. Bidirectional adjacency list. Each directed edge: Trust, Fear, Debt, Rivalry floats + RelationshipFlags (IsFamily, IsMarried, IsAlly, etc.).

### Tier 2 → Tier 1 Promotion
Same `EntityId` preserved. All historical references remain valid. Causal graph needs no updates.

### Spotlight Interface
`ISpotlightable : IEntity` — only Tier1Character and Tier2Character implement this.

---

## 10. Tile Grid Memory Layout

### Decision: Chunked Grid with 16×16 Chunks

World size specified in real-world km, tile count derived:
```csharp
public sealed class WorldConfig
{
    public int WidthKm { get; init; }
    public int HeightKm { get; init; }
    public int TileSizeKm { get; init; } = 10;  // default 10km per tile
    public int TileWidth => WidthKm / TileSizeKm;
    public int TileHeight => HeightKm / TileSizeKm;
}
```

At 10km tiles, Europe-scale (4000×3000km) = 400×300 = 120,000 tiles (~1.7MB for tile array).

### TileData Struct (~14 bytes)
Scaled integers not floats — 4× more tiles fit in cache.
Static fields: Biome, Elevation, Fertility, MagicIntensity, BaseTemperature, BaseMoisture, StaticFlags.
Dynamic fields: ControllingCivId, StructureId, CurrentMoisture, DynFlags, RoadLevel.

### Border Manifests
64 samples per edge encoding elevation, moisture, water presence/width/depth, road presence, cliff flags. ~768 bytes per tile for 4 edges. Stored in a **separate** array from TileData — accessed only during local scale generation, never during global sim ticks.

### TileCoord
`readonly record struct`. East-West wrapping (cylinder). North-South bounded. `Wrap()`, `IsInBounds()`, `CardinalNeighbors()`, `AllNeighbors()`, `ChebyshevDistance()`.

### World Shape: Cylinder (Not Torus)
East-West wrapping only. North and South edges are impassable polar boundaries. Torus was rejected because it breaks climate continuity — North Pole wrapping directly into South Pole breaks wind systems.

---

## 11. Two-Scale World Architecture

### World Scale (Global Sim)
- Tile size: 10km × 10km
- Covers full world, always exists
- All history simulation runs here

### Local Scale (Player Scale)
- Tile size: ~10m × 10m
- Generated lazily from world scale data + seed
- Deterministic: same inputs → same local tiles, always
- Only exists for the active region around the spotlight character

### Border Continuity
Border manifests (64 samples per edge) encode exactly where rivers enter/exit, road crossings, elevation at border. Local generation reads these and routes rivers between fixed entry/exit points. Adjacent world tiles produce matching borders because both reference the same crossing point data.

### Active Region
Detailed Zone: 3×3 world tiles around spotlight character (daily resolution).
Buffer Zone: 2-tile-wide ring (interpolated daily positions for incoming Standard-resolution entities).

---

## 12. WorldState Persistence and Database

### Decision: Disk is the system of record, memory is a working cache

**SQLite (`world.db`):** Always-current historical database. Written every tick via Phase 7 transaction. WAL mode enabled — reads never block writes.

**MessagePack (`state.bin`):** Current operational state snapshot. Written every N ticks.

**Memory:** Hot working set only — tile grid, active entities, active relationship graph, recent event cache, active cultural modifiers.

### SQLite Configuration
```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
```

### Core Database Schema
```sql
CREATE TABLE Events (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Type            INTEGER NOT NULL,
    Year            INTEGER NOT NULL,
    Season          INTEGER NOT NULL,
    Tick            INTEGER NOT NULL,
    LocationX       INTEGER,
    LocationY       INTEGER,
    TierInvolvement INTEGER NOT NULL,
    VerbClass       INTEGER NOT NULL,
    PopulationImpact INTEGER NOT NULL,
    IsFirstOfKind   INTEGER NOT NULL,
    IsGodMode       INTEGER NOT NULL DEFAULT 0,
    PayloadJson     TEXT NOT NULL,
    GeneratedProse  TEXT
);

CREATE TABLE CausalEdges (
    PredecessorId   INTEGER NOT NULL REFERENCES Events(Id),
    SuccessorId     INTEGER NOT NULL REFERENCES Events(Id),
    Weight          REAL NOT NULL DEFAULT 1.0,
    PRIMARY KEY (PredecessorId, SuccessorId)
);

-- Added in Milestone 2 when entities exist:
-- CREATE TABLE EventEntities (EventId, EntityId, Role)
-- CREATE TABLE CharacterArchive (Id, Name, Species, BirthYear, DeathYear, DataJson)
-- CREATE TABLE Artifacts (Id, Name, Type, State, DataJson)
-- CREATE TABLE ArtifactProvenance (ArtifactId, Sequence, OwnerId, AcquiredYear)
-- CREATE TABLE Snapshots (Year, DataBlob)
-- CREATE TABLE CulturalModifiers (Id, Type, OriginEventId, TargetJson, EffectJson, ...)
-- CREATE TABLE Religions (Id, Name, FoundedYear, DeityRef, DataJson)
```

---

## 13. Save and Load Model

### Two Files Per World
```
saves/world_name_3421/
├── state.bin      # MessagePack — current operational state
├── world.db       # SQLite — historical record, always current
└── meta.json      # Human-readable save info for load screen
```

### Load Sequence
1. Read `meta.json` → display save info
2. Read `state.bin` → restore operational state
3. Open `world.db` → database connection
4. Regenerate terrain from WorldConfig.Seed (~1-2 seconds)
5. Apply tile deltas from state.bin
6. Rebuild spatial indexes from entity locations
7. Sim resumes — history queries hit world.db on demand

### Save = Database Flush + File Copy
`world.db` is always current (Phase 7 writes every tick). "Save" flushes the WAL and copies both files to the save directory.

### TileDeltas (Sparse)
Only tiles whose dynamic state differs from the default (unclaimed, no structure, no road) are stored in state.bin. After world gen, most tiles are at default. Very compact.

---

## 14. The Event Log

### Architecture
The event log is the append-only record of every meaningful thing that happens. Everything the history query interface does is ultimately a query against this log.

**Hot cache:** `EventCache` — circular buffer of recent events in memory. Feeds the UI event log panel and the IsFirstOfKind check. No database query for recent events.

**Database:** SQLite Events table — queried for historical queries, timeline scrubbing, causal chain traversal.

**Phase 7 write:** One transaction per tick, batching all events from that tick. Cheap because SQLite transactions handle many rows efficiently.

### Event Type Taxonomy (~90 types)
Organized into domains. Implemented in phases — environmental subset first (Milestone 1), character events in Milestone 2:

**Environmental (Milestone 1):** VolcanicEruption, EarthquakeOccurred, FloodOccurred, DroughtBegan/Ended, WildfireOccurred, SeaLevelChanged, BiomeChanged, ClimateShifted, ResourceDepleted, ResourceRecovered

**Characters (Milestone 2):** CharacterBorn, CharacterDied, CharacterRetired, CharacterPromoted, CharacterMarried, CharacterInjured, CharacterConverted, CharacterExiled, BetrayalCommitted, GrudgeFormed, AllianceFormed/Broken, WarDeclared/Ended, BattleOccurred, SuccessionOccurred, CoupAttempted, RevoltBegan, SchismOccurred, ReligionFounded, ArtifactCreated/Destroyed/Lost/Discovered, SettlementFounded/Destroyed, CivilizationFounded/Collapsed, (and many more)

**God Mode (all milestones):** GodModeDisasterTriggered, GodModeEntitySpawned, GodModeCharacterCreated, GodModeArtifactPlaced, GodModeCivilizationForced. God Mode events are always recorded and always IsGodMode=true.

### Retroactive Causal Rescoring
When a Headline event fires, walk backward through CausalEdges. Ancestors of Headline events are automatically promoted to at least Regional tier. Implemented as a SQLite UPDATE after the triggering event is inserted.

---

## 15. Spatial Buffer Implementation

### Three Zones During Spotlight
**Detailed Zone:** 3×3 world tiles around spotlight character. Full daily simulation. Local scale grid active.

**Buffer Zone:** 2-tile-wide ring outside Detailed Zone. Standard-resolution entities heading toward Detailed Zone are tracked here with interpolated daily positions.

**Standard Zone:** Everything else. Seasonal resolution.

### Entity Interpolation on Buffer Entry
At season start, project all active entities' seasonal paths. Flag entities whose paths intersect the Buffer Zone as Buffer Candidates. Their seasonal path is unpacked into daily positions via deterministic interpolation: `Lerp(seasonStart, seasonEnd, day/90) + seededNoise` clamped to terrain-valid path. Same seed + same entity + same season = same daily path, always.

### Zone Transitions
Triggered at world tile boundary crossings (not local tile). Chunk-aligned. Entities leaving zones pack back to seasonal state. Standard Zone accumulates 90 daily ticks before running full seasonal resolution.

---

## 16. Character Decision-Making

### Architecture: Utility Scoring with Softmax Selection

Every Tier 1 character, every Phase 5:
1. Update needs from world state
2. Recompute goal priorities
3. Prune achieved/obsolete goals
4. Build limited perception (information travels at messenger speed)
5. Get available actions
6. Score each action via utility function
7. Select via softmax-weighted random
8. Execute → emit commands

### The Utility Function
```
Utility = (NeedsSatisfaction × NeedsWeight)
        + (GoalAdvancement × GoalsWeight)
        + (PersonalityFit × PersonalityWeight)
        + (RelationshipEffects × RelationshipWeight)
        + CulturalModifierBias
        × SuccessProbability
        × RiskAversion
```

All weights from `SimConfig.Utility`.

### Softmax Selection
`Temperature = MinTemp + Curiosity × (MaxTemp - MinTemp)`
High-Curiosity characters are more random. Low-Curiosity characters consistently pick the top-scoring action.

### Stability Distortion
Low-Stability characters under stress have their utility function distorted — emotional actions boosted, rational actions penalized. Distortion scales with `stress - Stability`.

### Tier 2 Decision-Making
Execute standing orders with loyalty check. Refuse if `LoyaltyValue < order.Difficulty × 0.5`. Refusal generates `RefuseOrder` event. Autonomous behavior when no orders — role-based defaults.

### Tier 2 State Machine
`Normal → JustifiedRefusal → QuietWithdrawal → RebellionPlanning → ActiveRebellion → DefectionPending → Exiled → AwaitingOpportunity`

---

## 17. Character Trait System

### Three Distinct Layers

**Personality (12 traits) — stable, set at birth, biases decisions:**

| Group | Traits |
|---|---|
| Drive | Ambition, Greed, Aggression, Compassion |
| Mind | Curiosity, Creativity, Rationality, Wonder |
| Social | Loyalty, Sociability, Honesty, Stability |

Stability is architecturally special — modifies the utility function itself under stress.

**Aptitude (6 traits) — stable, set at birth, modifies outcome quality:**
Diligence, Focus, Perfectionism, Composure, Acuity, Ingenuity.

**Skills (8 skills) — dynamic, grow through use:**
Combat, Leadership, Administration, Diplomacy, Crafting, Knowledge, Stealth, Piety.

### Character Generation
Traits drawn from: species baseline + cultural shift (0.3× weight) + parental inheritance (60% pull toward parent average) + Gaussian noise (stdDev ~0.2).

### Needs Vector (7 needs, dynamic per tick)
Safety, Food, Shelter, Belonging, Status, Purpose, Spiritual. Each 0.0-1.0. Unmet low-level needs override everything else in the utility function.

---

## 18. Specialist NPCs

### What They Are
Named Tier 2 characters who produce specific goods and services. Distinct behavioral class from Authority Tier 2.

### Livelihood Spectrum
`Survival → Subsistence → Independent → Contracted → Retained`

Specialists do NOT require patrons. They serve the general population, other Tier 2 characters, trade organizations, and Tier 1 patrons in order of availability and opportunity.

### Specialist Goods
Quality = skill × aptitude modifiers. Consumed goods apply concrete effects: medical treatment heals conditions, entertainment satisfies Belonging need, tutoring adds skill growth bonus, counsel adds temporary Rationality boost, intelligence reports expand perception radius.

### Settlement Population Thresholds (from SimConfig)
Apothecary: 200, Priest: 300, Entertainer/Teacher: 500, Weaponsmith: 800, Physician: 1,000, Scholar: 2,000, Alchemist/Jeweler: 3,000, Cartographer/Advisor: 5,000, Architect: 10,000.

### Artifact Generation
`Probability = Skill.Crafting × BaseProbability × (1 + Perfectionism×0.5) × (1 + Ingenuity×0.3) × (1 + Focus×0.2) × (1 + CircumstanceBonus)`. All from SimConfig.

---

## 19. Administrative Distance Penalty

### Algorithm: Precomputed Influence Maps
Modified Dijkstra from each authority anchor → distance field (float array). O(1) loyalty modifier lookup per tile. Recomputed only on invalidating events, not every tick.

### Travel-Time Edge Weights
Base cost by biome (Plains=1.0, Forest=2.0, Mountains=5.0, etc.) modified by: Road (×0.4), River following (×0.7), River crossing (×1.5), Winter (×1.8), Monsoon (×1.6).

### Authority Anchor Types (all values from SimConfig)

| Type | Core Radius | Max Radius | Strength |
|---|---|---|---|
| Capital | 3 seasons | 15 seasons | 1.0 |
| Sub-Capital | 2 seasons | 10 seasons | 0.7 |
| Tier 1 Presence | 1 season | 5 seasons | 0.5 |
| Garrison | 1 season | 3 seasons | 0.3 |
| Religious Center | 2 seasons | 8 seasons | 0.4 |

### Revolt Probability
Zero when loyalty > `RevoltThreshold`. Quadratic curve from threshold to zero. Amplified by Aggression and low Loyalty personality. Base probability from SimConfig.

### Invalidation Triggers
Tier 1 character moves, road built/destroyed (invalidate all), sub-capital founded, Tier 1 dies.

---

## 20. Cultural Trait Modifier Storage

### Two-Layer Storage
**Active registry (memory):** All modifiers with `CurrentMagnitude > ExpiryThreshold`. Three indexes: spatial (by tile), civilization (by civ ID), inter-culture (by ordered civ pair).

**Database archive (world.db):** Complete historical record. Queried for UI explanations.

### Modifier Targets
RegionalTarget, CivilizationTarget, InterCultureTarget, SettlementTarget. Each implements `Affects(TileCoord)` and `AffectsRelationship(EntityId, EntityId)`.

### Decay Model
Exponential half-life: `M(t) = M₀ × 0.5^(t/halfLife)`. Half-life values in SimConfig by modifier type. Reinforcement slows decay when similar events recur within reinforcement window.

### Application
Additive bias to utility function in Phase 5. Nudges decisions without overriding personality or needs.

---

## 21. World Generation Layer Handoff

### Decision: Immutable Layer Results Accumulated in WorldGenContext

Each layer produces an immutable result object. Results accumulate in `WorldGenContext`. Each layer reads from previously committed results. Commit order enforced — committing Layer N before Layer N-1 throws.

### Layer Results
Plain immutable data classes — float arrays, bool arrays, typed data structures. No behavior.

### Pipeline Runner
`RunFullAsync` — complete generation.
`RunUpToAsync(stopAfter)` — for layered preview checkpoints.
`RerunFromAsync(rerunFrom)` — player adjusts Layer 3 → pipeline reruns from Layer 4 onward without touching earlier layers.

### Seed Strategy
`worldSeed + layerConstant` per layer. Each layer has independent reproducible randomness. Changing one layer's implementation doesn't affect other layers.

### Memory Budget
~10-20MB for all layer results simultaneously at Europe scale. Trivially small. Layer results are GC'd after TileGrid assembly.

---

## 22. Simulation Configuration

### Decision: All simulation constants in TOML config file

No hardcoded numeric values in simulation logic. Every balance constant, threshold, weight, and rate lives in `SimConfig` loaded from `config/sim_config.toml`.

Library: **Tomlyn** (`Tomlyn` NuGet).

Config injected into sim systems via constructor — never a global singleton. Tests inject custom configs with extreme values.

Multiple profiles supported in `config/profiles/` — profiles specify only overrides.

**Rule:** Any numeric constant affecting simulation balance or emergent behavior goes in config. Structural constants (save format version, enum values, chunk size once allocated) stay in code.

---

## 23. Event Significance and History Filtering

### The Two Filter Layers

The system uses two independent, complementary filter layers:

**Pre-write gate (EventGate):** Runs in Phase 7 before database insertion. Determines whether an event is recorded at all. Configurable via `SimConfig.Events.Gate`. Start permissive during development, tighten empirically as noise categories are identified.

**Post-write lens (player filters):** Runs at query time. Determines what the player sees right now. Operates on already-recorded events using pre-computed filter tags.

### EventGate

```csharp
public sealed class EventGate
{
    public bool ShouldRecord(SimEvent evt, IWorldStateReadOnly world)
    {
        if (_config.SuppressedTypes.Contains(evt.Type)) return false;
        if (_config.AlwaysRecordTypes.Contains(evt.Type)) return true;
        if (evt.TierInvolvement < _config.MinimumTierToRecord) return false;
        if (_config.SuppressedVerbClasses.Contains(evt.VerbClass)) return false;
        if (evt.PopulationImpact < _config.MinimumPopulationImpactToRecord) return false;
        return true;
    }
}
```

**Always record (regardless of gate):** CharacterBorn, CharacterDied, CivilizationFounded, CivilizationCollapsed, ArtifactCreated, ArtifactDestroyed, ReligionFounded, ReligionExtinct, WarDeclared, WarEnded, all GodMode* types.

**Always suppress (pure noise, no narrative value):** Tier3ResourceTick, SettlementFoodAccounting, PopulationGrowthIncrement, ArmySupplyConsumption, RelationshipDecayTick.

**Start permissive:** `minimum_tier_to_record = "Background"` during development. Tighten empirically by querying event type distribution in `world.db`.

### Significance Classification (Tags on Events)

The classifier runs before the gate check and tags every candidate event with filter fields. These tags are stored in the database and used by the post-write lens.

**Five filter tags per event:**

| Tag | Type | Description |
|---|---|---|
| TierInvolvement | EventTier enum | Background/Character/Regional/Headline based on entity types involved |
| VerbClass | VerbClass enum | Creation/Destruction/Transformation/Transfer/Conflict/Maintenance |
| PopulationImpact | PopulationImpact enum | None/Minor/Moderate/Major/Catastrophic |
| IsFirstOfKind | bool | First time this event type occurred between these parties |
| IsGodMode | bool | Player-authored event |

### Significance Classification Rules

**TierInvolvement (rule-based, checkable):**
- Any Tier 1 character involved → Headline
- Settlement involved → Regional (minimum)
- Any Tier 2 character involved → Character (minimum)
- Tier 3 only → Background

**VerbClass minimum tier floor:**
- Destruction → Regional (losing something always matters more than raw numbers suggest)
- Creation → Character (new things are always worth a footnote)
- Transformation → Character
- Transfer, Conflict, Maintenance → Background (significance comes from who/how-many, not the act itself)

**Final TierInvolvement = maximum(entity tier, verb class floor, population tier)**
No single category can suppress what another established.

**PopulationImpact brackets (from SimConfig):**
- Catastrophic: absolute ≥ 10,000 OR regional fraction ≥ 25%
- Major: absolute ≥ 500 OR regional fraction ≥ 5%
- Moderate: absolute ≥ 50 OR regional fraction ≥ 1%
- Minor: absolute > 0
- None: no population affected

**Elevation rules (applied on top of base classification):**
- IsFirstOfKind AND base tier < Regional → bump to Regional
- Affects 3+ civilizations → bump to Headline
- Is causal ancestor of a Headline event → bump to at least Regional (retroactive)

### Retroactive Rescoring

When a Headline event fires, ancestors in the CausalEdges graph are automatically promoted to at least Regional:

```sql
-- Retroactive promotion of causal ancestors
WITH RECURSIVE ancestors AS (
    SELECT PredecessorId FROM CausalEdges WHERE SuccessorId = @headlineEventId
    UNION ALL
    SELECT ce.PredecessorId FROM CausalEdges ce
    JOIN ancestors a ON ce.SuccessorId = a.PredecessorId
)
UPDATE Events 
SET TierInvolvement = MAX(TierInvolvement, 2)  -- 2 = Regional
WHERE Id IN (SELECT PredecessorId FROM ancestors);
```

No decaying float propagation — simple "ancestor of Headline = at least Regional" rule. Cleaner, debuggable.

### Player-Facing Filter System

Players never see significance scores or classification internals. They see five human-readable filter dimensions:

**History Detail Level (set at world creation, affects gate):**
- Essential: Headline events only
- Standard: Regional+ (default)
- Detailed: Character+ (all named characters' histories)
- Complete: Everything above noise floor (development/research mode)

**Runtime filter panel (affects post-write lens):**
```
WHO WAS INVOLVED
  ☑ Legends & rulers        (TierInvolvement = Headline)
  ☑ Notable figures         (TierInvolvement = Regional)  
  ☑ Named individuals       (TierInvolvement = Character)
  ☐ Background activity     (TierInvolvement = Background)

WHAT KIND OF THING
  ☑ Births & deaths         (VerbClass = Creation | Destruction)
  ☑ Wars & conflicts        (VerbClass = Conflict)
  ☑ Rise & fall of civs     (Destruction | Transformation)
  ☐ Routine events          (VerbClass = Maintenance)

HOW BIG WAS IT
  ☑ World-shaking           (PopulationImpact = Catastrophic)
  ☑ Regionally significant  (PopulationImpact = Major | Moderate)
  ☐ Locally notable         (PopulationImpact = Minor)
  ☐ Individual scale        (PopulationImpact = None)

SPECIAL
  ☑ First of its kind       (IsFirstOfKind = true)
  ☑ Player-authored         (IsGodMode = true)
```

**Focus Lenses:**
Named contexts that lower the display threshold for events involving a specific entity, location, or civ.
- "Full life" — show all events involving this entity regardless of tier
- "Major only" — show Regional+ events involving this entity

Right-click any entity → "Follow this character/location/civ" → creates a focus lens.

**"Tell me what led to this" query:**
Right-click any event → "Tell me what led to this" → walks CausalEdges backward → renders as a simple timeline. No filters to configure. The causal chain view.

### Pruning Policy

```
Prunable if ALL conditions met:
  TierInvolvement = Character OR Background
  AND (currentYear - Year) > RetentionYears  (default 500)
  AND HasCausalSuccessors = false
  AND IsReferencedByLivingEntity = false

Never pruned:
  TierInvolvement = Regional OR Headline
  OR HasCausalSuccessors = true
  OR IsGodMode = true
```

### Empirical Tuning Workflow

1. Run sim 100 years with permissive gate
2. Query: `SELECT Type, COUNT(*) FROM Events GROUP BY Type ORDER BY 2 DESC LIMIT 30`
3. Identify dominant event types with low narrative value
4. Add them to `suppressed_types` in config
5. Repeat until event distribution feels right
6. Lock in a production gate config

The gate config is the primary tuning instrument. The significance classifier thresholds (population brackets, etc.) are secondary. Both are in SimConfig — no code changes needed to tune.

---

## 24. Patterns Reference

### Command Pattern (Core Behavioral Architecture)
Entities emit `ICommand` sealed records. Three steps per phase: READ → EMIT → RESOLVE. Contention triage before resolve. Failed contentions become `FailedAttempt` events. Player inputs are `ICommand` — no special casing.

### Double-Buffered State Cache (Sim→UI Boundary)
`WorldState` in sim thread only. `WorldSnapshot` is a lightweight immutable projection. `StateCache` holds latest snapshot behind a `ReaderWriterLockSlim`. Lock held for microseconds. UI reads every frame — never blocked meaningfully.

### Causal Event Graph (History Log)
Events are nodes in SQLite. CausalEdges are directed edges. Append-only — events never modified once written. Retroactive significance: ancestor of Headline event → promoted to at least Regional via SQL UPDATE. Exposed to UI as `IHistoryGraphReadOnly`.

### Lazy Voxel Generation (3D on Demand)
2D tile grid is the simulation layer — always exists. Voxel grids generated deterministically from 2D tile data + seed when triggered. LRU ring buffer cache (3-5 entries) prevents thrashing.

### Chunked Tile Grid (Memory and Performance)
16×16 tile chunks. Null chunks for unallocated ocean. Chunk-level dirty flags for sparse processing. TileData structs use scaled integers not floats — 14 bytes per tile.

### Deterministic Randomness (Reproducibility)
All randomness: `worldSeed + contextualSalt(entityId, tick, phase, layerConstant)`. Same seed + same player commands = identical world history. Contention tiebreaker: `Hash(entityId + worldSeed + currentTick)`.

### Influence Maps (Administrative Distance)
Precomputed Dijkstra distance fields from each authority anchor. O(1) loyalty modifier lookup per tile. Invalidated and recomputed only on specific trigger events.

### Cultural Trait Modifiers (Historical Memory in NPC Behavior)
Exponential half-life decay. Injected by Phase 7 from Headline events. Additive biases to utility function in Phase 5. Three in-memory indexes for O(1) lookup. Archived to SQLite on expiry.

### Two-Scale World (Global Sim + Local Play)
World scale (10km tiles) runs history sim. Local scale (~10m tiles) generated lazily. Border manifests (64 samples per edge) encode continuity data for seamless transitions.

### Utility Scoring (Character Decisions)
Weighted sum of needs satisfaction + goal advancement + personality fit + relationship effects + cultural modifier biases. Scaled by success probability and risk aversion. Softmax-weighted random selection — temperature varies by Curiosity trait.

### Database-Backed Simulation State (Disk as System of Record)
SQLite (`world.db`) written every tick via Phase 7 transaction — always current. MessagePack (`state.bin`) captures hot operational state every N ticks. Memory holds only the working set.

### Event Gate + Post-Write Lens (History Filtering)
Pre-write gate (EventGate) blocks noise before database insertion — tuned empirically. Post-write lens (player filter panel + focus lenses) filters what's displayed from what's recorded. Five filter tags per event (TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode) expose classification to the player in human-readable form. Player never sees raw scores.

### Simulation Config (Balance Without Recompiling)
All numeric simulation constants in TOML file loaded at startup. Injected into sim systems via constructor. Multiple named profiles supported.

---

*Document Version: 0.3*  
*Last Updated: June 18, 2026*  
*Status: All Tier A and Tier B decisions complete*
