# Milestone 1 Implementation Plan
**Date:** June 2026  
**Status:** COMPLETE — 2026-06-22  
**See:** `docs/phases/archive/` for all phase docs. `docs/testing/runbook_m1.md` for manual test cases.

---

## Phase Overview

| Phase | Epic | Stories | Key Milestone | Blocks |
|---|---|---|---|---|
| 1 | 1.1 Project Foundation | 5 | Solution builds | Everything |
| 2 | 1.2 Tile Data Structures | 4 | TileData locked (14 bytes) | Phase 3 |
| 3 | 1.3 World Generation | 8 | `SameSeedProducesSameWorld` passes | Phase 4, 5 |
| 4 | 1.4 Simulation Loop | 7 | SimLoop ticks + StateCache + Commands | Phase 5, 6, 7 |
| 5 | 1.5 Environmental Sim | 5 | Disasters + climate drift in Phase 1 | Phase 6 |
| 6 | 1.6 Event System | 5 | world.db writing + classification | Phase 7 |
| 7 | 1.7 Basic UI | 7 | Manual test: full interactive session | — |

**Total:** 41 stories across 7 phases. Phases 1–3 are prerequisites for all later work.

---

## Critical Path

```
Phase 1 ──► Phase 2 ──► Phase 3 ──► Phase 4 ──► Phase 5 ──► Phase 6 ──► Phase 7
  (sol)       (tiles)    (worldgen)  (simloop)    (env)       (events)    (ui)
                                ↑
                    SameSeedProducesSameWorld
                    (the non-negotiable gate)
```

No phase can start until the previous one passes all its tests. The gates are hard.

---

## Non-Negotiable Tests (Red Line)

These tests must pass before their epic is considered done. Failures here block everything downstream.

| Test | Phase Gate | What It Proves |
|---|---|---|
| `SameSeedProducesSameWorld` | Phase 3 | World gen is deterministic — save/load, replay, and testing all depend on this |
| `StateCache_ThreadSafetyUnderConcurrentAccess` | Phase 4 | The sim/UI boundary is race-condition-free |
| `EventStore_SchemaCreatedWithAllTables` | Phase 6 | SQLite schema matches what all queries assume |

---

## Story Ordering Within Phases

Within each phase, stories have a suggested order based on dependencies:

**Phase 1:**
1.1.1 (solution) → 1.1.2 (simconfig) → 1.1.3 (value types) → 1.1.4 (entry point) → 1.1.5 (test harness)

**Phase 2:**
1.2.1 (TileData) → 1.2.2 (TileGrid) → 1.2.3 (manifests) → 1.2.4 (IWorldStateReadOnly)

**Phase 3:**
1.3.1 (pipeline) → 1.3.2 (tectonics) → 1.3.3 (elevation+ocean) → 1.3.4 (rivers) → 1.3.5 (magic) → 1.3.6 (climate) → 1.3.7 (biome+resource) → 1.3.8 (assembly)
(1.3.5 magic can run in parallel with 1.3.4 rivers — no dependency)

**Phase 4:**
1.4.1 (WorldState) → 1.4.2 (WorldSnapshot) → 1.4.3 (StateCache) → 1.4.4 (CommandQueue) → 1.4.5 (PhaseRunner) → 1.4.6 (SimLoop) → 1.4.7 (time advancement — bundled with 1.4.6)

**Phase 5:**
1.5.1 (seasonal) → 1.5.2 (drift) → 1.5.3 (disasters) → 1.5.4 (resource dynamics) → 1.5.5 (sea level)

**Phase 6:**
1.6.1 (SimEvent+EventType) → 1.6.2 (EventGate) → 1.6.3 (SQLite+EventStore) → 1.6.4 (classifier) → 1.6.5 (EventCache + Phase 7 wiring)

**Phase 7:**
1.7.1 (window) → 1.7.2 (world gen screen) → 1.7.3 (camera+map) → 1.7.4 (overlays) → 1.7.5 (inspector) → 1.7.6 (event log) → 1.7.7 (time controls)

---

## What Each Phase Produces

**After Phase 1:** `dotnet build` green. SimConfig loads. WorldRng is deterministic. Core types exist.

**After Phase 2:** TileData struct locked at 14 bytes. All flag enums defined. TileGrid wraps correctly. IWorldStateReadOnly defined.

**After Phase 3:** Running `WorldGenPipeline.RunFullAsync(config, simConfig)` produces a fully-populated WorldState. `SameSeedProducesSameWorld` passes. manifests.bin written.

**After Phase 4:** Two-thread runtime: SimLoop ticks on background thread, StateCache exposes snapshots, CommandQueue carries player input. `dotnet run` starts and ticks forever without crash.

**After Phase 5:** The world changes: disasters occur, biomes drift, sea level rises. PendingEvents flow to Phase 7.

**After Phase 6:** History is recorded: `world.db` has Events + CausalEdges. Significance is classified. IsFirstOfKind works. EventCache populated.

**After Phase 7:** Interactive simulation: map renders, overlays work, inspector shows tile detail, event log scrolls, time controls respond. Milestone 1 is complete.

---

## SimConfig Sections Summary

All sections that need to exist by Phase 6 done. Add sections during the phase that introduces them:

```toml
[world_gen.tectonics]    # Phase 1 (1.1.2) — add during SimConfig setup
[world_gen.rivers]       # Phase 1 (1.1.2) — add during SimConfig setup
[world_gen.biome_thresholds]  # Phase 3 (1.3.7) — add during BiomeLayer
[climate]                # Phase 3 (1.3.6) — add during ClimateLayer; extended in Phase 5
[disasters]              # Phase 5 (1.5.3) — add during disaster system
[resources]              # Phase 5 (1.5.4) — add during resource dynamics
[simulation]             # Phase 4 (1.4.6) — add during SimLoop
[events]                 # Phase 6 (1.6.2) — add during EventGate
```

---

## Actual Test Suite (as of M1 completion)

192 tests total, all passing.

| Phase | Actual Unit Tests | Actual Integration Tests |
|---|---|---|
| 1–3 | ~60 (config, types, all gen layers) | 1 (SameSeedProducesSameWorld) |
| 4 | ~20 (StateCache, CommandQueue, PhaseRunner) | 3 (SimLoop) |
| 5 | ~50 (seasonal, drift, sea level, disasters) | 0 |
| 6 | ~35 (EventType, EventGate, Classifier, Cache) | 11 (EventStore, Phase7 integration) |
| 7 | 0 | 0 (manual only — see runbook_m1.md) |

**Deviations from original targets:**
- No `WorldGenPipeline` class was implemented. Generation runs via direct layer calls in `Game1.cs` and `TileGridAssembler.Assemble()`. The pipeline was inlined rather than abstracted.
- `SameSeedSameDisasters` reproducibility test was not written separately — covered by the full-world reproducibility test.
- Integration test count exceeded estimates due to Phase 6 EventStore tests.

---

## Session Protocol

This section is now obsolete for M1. For M2 sessions, see `CLAUDE.md` starting protocol.

---

## What NOT to Build in Milestone 1

From CLAUDE.md "What NOT to Build" — these are stubs only:

- **Magic behavior** — generate and store MagicIntensity byte; nothing responds to it. Leave `// V2: magic physical substrate` comment.
- **Entity system** — IWorldStateReadOnly has entity-query methods stubbed with `// Milestone 2+` comments
- **CivControl** — CivControl field in TileData always == 0. No civ logic. 
- **RoadLevel** — RoadLevel field in TileData always == 0. No road logic.
- **BorderManifest loading** — `LoadFromFile()` throws `NotImplementedException("M4 feature")`
- **Spotlight/God Mode** — `world.SuppressedTiles` placeholder (empty `HashSet<TileCoord>`) prepared in WorldState but Phase 1 skips it. No UI for this.
- **LLM prose generation** — `SimEvent.GeneratedProse` is always null. Leave `// V2: LLM generation` comment.

---

## Session Protocol

When starting a coding session:

1. Read `CLAUDE.md` (always loaded)
2. Read `docs/phases/phase_N_name.md` for the current phase
3. Reference `docs/snippets/patterns.md` when you need boilerplate code patterns
4. Reference `docs/interface_contracts.md` when you need interface signatures
5. Run `dotnet test` before starting to confirm clean baseline
6. Write tests **before** writing the code they test
7. When a story is done: run `dotnet test`, verify 0 failures, mark story done

When a phase is complete:
- Move `docs/phases/phase_N_name.md` to `docs/phases/archive/`
- Update the Status field at the top to "COMPLETE — [date]"
