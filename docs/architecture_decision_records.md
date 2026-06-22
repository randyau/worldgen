# World Engine — Architecture Decision Records
**Version:** 0.1  
**Date:** June 18, 2026  
**Purpose:** Quick-reference summary of every major architectural decision and its rationale. Read this to understand WHY the codebase is structured the way it is, without reading the full implementation decisions document.

For full rationale and detail, see `docs/world_engine_implementation_decisions_v0.3.md`.

---

## ADR-001: Language — C# on .NET 8

**Decision:** C# on .NET 8.

**Rejected:** Rust (borrow checker friction for graph/entity patterns), Python (GIL, memory overhead at scale), Kotlin/JVM (classpath complexity), Go (poor plugin/modding story).

**Rationale:** Strong type system, true parallelism, excellent Claude Code support, cross-platform, self-contained executables, managed memory handles cyclic entity references without manual management.

---

## ADR-002: Headless Sim Core

**Decision:** `WorldEngine.Sim` has zero UI references. Enforced via project references.

**Rationale:** Testability (sim runs without graphics), extraction insurance (swap UI or language if needed), headless batch processing for long pre-generation runs.

**Rule:** UI references Sim. Sim never references UI. Violating this is a build error, not a style issue.

---

## ADR-003: Command Pattern for Entity Behavior

**Decision:** Entities emit `ICommand` sealed records. World state mutates only in the RESOLVE step via `CommandResolver`. Three steps per phase: READ → EMIT → RESOLVE.

**Rejected:** Direct mutation during entity update (causes concurrent modification problems), event-sourcing-only approach (too complex).

**Rationale:** Eliminates race conditions in parallel resolution. Makes contention triage possible. Makes player actions indistinguishable from entity actions (both are commands). Dramatically improves testability.

---

## ADR-004: Two-Thread Concurrency Model

**Decision:** Sim thread owns `WorldState` exclusively. UI thread owns rendering. Communication via `StateCache` (snapshot) and `CommandQueue` (commands) only.

**Rationale:** Sim ticks and UI frames decouple — slow tick doesn't freeze UI. Player input never blocks a tick. Always have something to render. Simple and auditable — any code touching `WorldState` is sim-thread-only.

---

## ADR-005: SQLite as the History Database

**Decision:** `world.db` is a SQLite database. Written every tick via Phase 7 transaction. WAL mode enabled. Disk is the system of record.

**Rejected:** In-memory graph only (too large for 10k year runs), external graph database (network overhead on hot path), pure JSON files (unqueryable at scale).

**Rationale:** SQLite handles millions of rows. WAL mode means reads never block writes (UI queries history while sim writes). Recursive CTEs handle causal graph traversal natively. Free tooling for exploration (DB Browser, sqlite3 CLI). Single file, zero server, cross-platform.

---

## ADR-006: MessagePack for Operational State

**Decision:** `state.bin` is a MessagePack binary file containing current operational state. Written every N ticks. Separate from the history database.

**Rationale:** The operational state (active entities, relationships, tile deltas) is small (5-50MB) and changes every tick. MessagePack is 5-10x faster and more compact than JSON for this use case. SQLite is wrong here — this is a snapshot, not a queryable store.

---

## ADR-007: Chunked Tile Grid (16×16 chunks)

**Decision:** World tile grid stored as 16×16 chunks. Null chunks for ocean tiles. Chunk-level dirty flags.

**Rejected:** Flat array (no sparse processing), dictionary/hashmap (overhead exceeds benefit for dense spatial data).

**Rationale:** Chunk-level dirty flags allow skipping entire 16×16 blocks with nothing happening — critical for performance on large worlds. Null ocean chunks save memory on ocean-heavy maps. Cache-friendly within a chunk (3.5KB = fits in L1).

---

## ADR-008: Cylinder World Shape (Not Torus)

**Decision:** World wraps East-West only. North and South edges are polar boundaries (impassable).

**Rejected:** Torus (East-West AND North-South wrapping).

**Rationale:** On a torus, the North Pole wraps directly into the South Pole, breaking the North-South temperature gradient and all climate systems. Cylinder preserves clean climate simulation while still allowing East-West trade route continuity.

---

## ADR-009: World Scale in KM, Not Tiles

**Decision:** `WorldConfig` specifies `WidthKm` and `HeightKm`. Tile count is derived from tile size.

**Rationale:** The "right" tile count depends on tile size, which depends on what narrative granularity is needed. Specifying in km decouples world size from implementation detail. At 10km tiles, Europe-scale is 400×300 = 120,000 tiles. Changing tile size doesn't change the world's geographic scale.

---

## ADR-010: Two-Scale World (Global Sim + Lazy Local)

**Decision:** Global 2D sim at 10km/tile. Local scale (~10m/tile) generated lazily from global tile data when the player zooms in.

**Rejected:** Full 3D voxel simulation like DF (computational budget incompatible with 10k year runs), 2D-only with no local detail (insufficient for character control).

**Rationale:** History simulation doesn't need voxel resolution — it needs geographic resolution. Local detail only matters when a player is interacting at ground level. Deterministic local generation from global data means no storage cost for local grids.

---

## ADR-011: Border Manifests for Local Continuity

**Decision:** Each world tile stores 64-sample border manifests for all 4 edges, encoding elevation, moisture, water crossings, road crossings. Local generation reads these to produce seamless transitions.

**Rationale:** Without border manifests, independently-generated local regions have visible discontinuities at tile edges. Manifests encode exactly where rivers, roads, and terrain features cross tile boundaries. Computed once at world gen; accessed only during local generation.

---

## ADR-012: Immutable Layer Results in World Gen Pipeline

**Decision:** Each generation layer produces an immutable result object. Results accumulate in `WorldGenContext`. Each layer reads only from committed predecessors.

**Rejected:** Shared mutable state object passed through layers (no clear contract), builder pattern (more framework than problem needs).

**Rationale:** Crystal clear what each layer needs and produces. Easy to test layers in isolation. Supports layered preview (run up to Layer N, show result, let player adjust, rerun from Layer N+1). Layer result objects are GC'd after TileGrid assembly.

---

## ADR-013: Entity Model — Interface + Components

**Decision:** `IEntity` behavioral interface. Component data structs (PersonalityVector, SkillVector, etc.) within concrete entity classes. `EntityRegistry` with typed parallel indexes.

**Rejected:** Pure ECS (overkill for entity counts, fights readable domain modeling), enum-based entity types (breaks extensibility), inheritance hierarchy (too rigid).

**Rationale:** Uniform sim loop (`registry.All.ForEach(e => e.EmitCommands(...))`) with typed queries (`registry.AllTier1`). New entity types = new class + register, zero existing code changes. Component structs are serializable and testable in isolation.

---

## ADR-014: Centralized Relationship Graph

**Decision:** Relationships stored in a centralized `RelationshipGraph`, not on entity objects.

**Rejected:** Relationships as dictionary field on each entity (inefficient for "who trusts Aldric" queries, split state), external graph database on hot path (network/IPC overhead).

**Rationale:** In-memory bidirectional adjacency list for active entities is efficient for all query patterns ("who does X trust", "who trusts X", "trust between X and Y"). Relationship mutations go through command pattern. Archived to SQLite with retired characters.

---

## ADR-015: 12 Personality Traits + 6 Aptitude Traits + 8 Skills

**Decision:** Three distinct character data layers. Personality (12): how character relates to world emotionally. Aptitude (6): how character works. Skills (8): what character can do.

**Rejected:** Six traits (insufficient — missing Stability, Honesty, Ambition, Compassion, Sociability), DF's 50 traits (most invisible in behavior), The Sims' 5 discrete traits (too few, not continuous).

**Rationale:** Every one of the 12 personality traits does specific visible work in the utility function. Aptitude is genuinely distinct from personality (a lazy genius and a diligent mediocrity both have high Curiosity but different Diligence). Skills are dynamic — they grow — which personality and aptitude are not. 26 fields total per character.

---

## ADR-016: Utility Scoring with Softmax Selection

**Decision:** Character decisions use utility scoring (weighted sum of needs satisfaction + goal advancement + personality fit + relationship effects + cultural modifier biases), then softmax-weighted random selection.

**Rejected:** Behavior trees (too hand-authored), pure random (no character consistency), pure utility maximization (robotic, predictable).

**Rationale:** Utility scoring produces believable, personality-consistent, situation-appropriate behavior that varies over time without being random. Softmax means high-utility actions are most likely but not certain — produces the "close call" moments that make good history. Temperature varies by Curiosity trait so curious characters are less predictable.

---

## ADR-017: Stability Trait Modifies Utility Function Itself

**Decision:** `Stability` personality trait is architecturally special — it modifies how the utility function works under stress, not just which actions score higher.

**Rationale:** Low-Stability characters under stress should have their rational decision-making degrade — emotional actions get boosted, long-term planning gets penalized. This can't be modeled as just another action bias; it requires distorting the scoring mechanism itself. This is the primary source of tragic character arcs emerging naturally from the sim.

---

## ADR-018: Precomputed Influence Maps for Admin Distance

**Decision:** Administrative distance penalty computed via precomputed Dijkstra distance fields from each authority anchor. O(1) tile lookup. Recomputed only on invalidating events.

**Rejected:** Per-tick pathfinding from each tile to nearest capital (far too expensive), Manhattan distance (ignores terrain and roads).

**Rationale:** Travel-time distance (accounting for terrain, roads, rivers, seasons) is the meaningful metric for administrative reach. Precomputing the full distance field from each anchor is cheap (milliseconds) and only needs recomputation when anchors change or roads are built. O(1) lookup is essential since this runs every Phase 5 for every Tier 2 character.

---

## ADR-019: Cultural Trait Modifiers as Historical Memory

**Decision:** High-significance events inject Cultural Trait Modifiers into affected populations. Modifiers have exponential half-life decay. They are additive biases to the utility function.

**Rationale:** NPCs are present-tense creatures — they don't "remember" history. But the consequences of history need to persist in behavior. A war 300 years ago should still produce residual animosity, even when no living NPC witnessed it. Cultural modifiers are the bridge: they store the behavioral echo of historical events and fade naturally over generations.

---

## ADR-020: Event Gate + Pre-Computed Filter Tags

**Decision:** Two-layer filtering. Pre-write gate (EventGate) drops pure noise before database insertion. Post-write lens (player filter panel) uses five pre-computed tags per event (TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode) to filter display.

**Rejected:** "Record everything, filter on read" (database too large at 32M events per 10k years), "score-based threshold at write time" (single score can't serve all filter needs simultaneously).

**Rationale:** The gate removes known noise. The tags expose classification to players in human-readable form without exposing the scoring machinery. Tags are stored in the database, recomputable if classification rules change, and map directly to UI checkboxes. Players never see significance scores or thresholds.

---

## ADR-021: Rule-Based Significance Classification

**Decision:** Significance classified by three independent rules: entity tier involvement, verb class minimum floor, population impact brackets. Final tier = maximum across all three.

**Rejected:** Five-factor weighted formula (weight interactions make tuning non-intuitive, factors dilute each other, hard to debug).

**Rationale:** Each rule is independently checkable — "is a Tier 1 character involved?" is a fact, not a judgment. No single category can suppress what another established (max, not weighted average). Destruction gets a Regional floor because losing things always matters more than raw numbers suggest. The rules map directly to designer intuition.

---

## ADR-022: TOML Config for All Simulation Constants

**Decision:** All simulation balance constants in `config/sim_config.toml`. No hardcoded numbers in sim logic. Injected into systems via constructor.

**Rejected:** Code constants (requires recompile to tune), database-stored config (overkill for this use case), JSON (no comments, less readable).

**Rationale:** Balance tuning is iterative and empirical. Constants that require recompilation to change will not be tuned. TOML is human-readable, supports comments, and round-trips correctly (can write defaults and reload). Multiple profiles enable difficulty/playstyle presets without code changes.

---

## ADR-023: Specialist NPCs Use Livelihood Spectrum (Not Binary Patron Model)

**Decision:** Specialists exist on a spectrum (Survival → Subsistence → Independent → Contracted → Retained). They serve the general population, other Tier 2 characters, factions, and Tier 1 patrons — in that order of availability.

**Rejected:** Binary patron/no-patron model (specialists inert without a named patron, historically wrong).

**Rationale:** Most historical specialists served broad client bases, not individual patrons. A village physician doesn't need a king. The livelihood spectrum produces realistic dynamics: economic disruption pushes specialists toward survival behavior; success pushes them toward independence or patronage. Patron relationships are aspirational for some, irrelevant for others.

---

## ADR-024: Spatial Buffer (Balloon) for Spotlight Mode

**Decision:** Three concentric zones during Spotlight: Detailed (daily), Buffer (interpolated), Standard (seasonal). Entities entering the buffer have their seasonal path unpacked to daily positions via deterministic interpolation.

**Rationale:** Without the buffer, Standard-resolution entities entering the Spotlight zone mid-season have undefined state (frozen? teleporting?). The buffer zone provides a transition layer where entities' daily positions are mathematically derived from their seasonal path — deterministically, so the same seed always produces the same interpolation.

---

*Document Version: 0.1*  
*Last Updated: June 18, 2026*
