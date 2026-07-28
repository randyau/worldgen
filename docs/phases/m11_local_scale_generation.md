# M11 — Local-Scale Generation

**Status:** IN PROGRESS (started 2026-07-27). Phase 11.1 done (2026-07-27) — see below.

## Progress

- **11.1 — COMPLETE (2026-07-27).** `RiverResult` gained `Crossings` (`RiverCrossing`: FromTile/
  ToTile/Edge/Position/Width/FlowVolume), computed in `RiverLayer` from the existing D8 `flowDir`
  data — no new drainage algorithm, just exposing the cross-tile edge each river tile's downstream
  flow already crosses. `BorderManifestBuilder` (new, `WorldEngine.Sim/WorldGen/`) builds one
  `BorderManifest` per tile: elevation/moisture edge samples are a flat blend of the two adjacent
  tiles' own byte values (real sub-tile variation is 11.3's job — this phase only guarantees both
  sides of a shared edge agree, proven by `BorderManifestBuilderTests.Build_AdjacentTilesAgreeOnSharedEdgeElevation`),
  and river crossings are stamped onto both the source tile's edge and the destination tile's
  opposite edge from the same `RiverCrossing` record (no independent re-derivation, so no seam risk).
  `WorldGenPipeline.RunFullWithManifestsAsync` is a new method alongside (not replacing)
  `RunFullAsync` — it returns `(WorldState, Manifests)` so the ~15 existing `RunFullAsync` callers
  in tests were untouched. `Program.cs` (headless runner) now writes `manifests.bin` via the
  existing `BorderManifestStore.WriteToFile` at the end of world gen. `BorderManifestStore.LoadFromFile`
  is implemented (was `NotImplementedException`) as the mirror read of `WriteToFile`'s format.
  New config: `RiversConfig.CrossingMinWidthFraction`/`CrossingMaxWidthFraction`
  (`world_gen.rivers.crossing_min_width_fraction`/`crossing_max_width_fraction` in
  `sim_config.toml`). Crossing position along an edge is deterministic per-tile jitter via
  `WorldRng.FloatAt` (local salt `RiverLayer.SaltCrossingPosition`), not a cross-sim-tick salt so
  it was not added to the global `SimRngSalts` registry.
- **11.2 — COMPLETE (2026-07-28).** New `WorldEngine.Sim/Tiles/LocalScale/` namespace:
  `ChunkCoord(TileCoord WorldTile, int ChunkX, int ChunkY)` with a `Normalize` that rolls
  out-of-range chunk indices into the neighboring world tile (cylinder-wrapped in X like
  `TileCoord.Wrap`, clamped in Y since the world doesn't wrap vertically); `LocalTileCoord(byte X,
  byte Y)`; `LocalTileData` (Elevation/BiomeType/DecorationType/Flags, no civ/economy fields);
  `LocalChunk` (a `Size × Size` grid, never persisted — always regenerable). `LocalCoordMath`
  converts between `(ChunkCoord, LocalTileCoord)` and an absolute `(long X, long Y)` local-tile
  coordinate counted from world origin — this is what 11.3's noise sampling will key off of so
  terrain stays continuous across chunk/tile boundaries instead of restarting its noise domain at
  every edge. `LocalTileGenerator.GenerateFlat` is the placeholder generator: every cell in a
  chunk copies its parent `TileData`'s Elevation/BiomeType verbatim, unblocking chunk-loading/UI
  work ahead of 11.3's real amplification. New config: `LocalGenConfig.ChunkSizeTiles` (default
  40, not the doc's originally-proposed 32 — chosen because it divides `LocalTilesPerWorldTileEdge`
  (1000, i.e. 10km/10m) evenly into 25 chunks per world-tile edge, so chunk coordinates roll over
  cleanly at world-tile boundaries) and `LocalGenConfig.LocalTilesPerWorldTileEdge`
  (`local_gen.chunk_size_tiles`/`local_gen.local_tiles_per_world_tile_edge` in `sim_config.toml`).
- **11.3–11.8 — not started.**

## Problem statement

From `docs/roadmap.md` § M11:

> Local-scale generation: activate the `manifests.bin` border-manifest hook (DS-A2) for
> local/zoomed generation — the long-reserved M4-era capability.

That phrasing assumes a hook already exists. **It doesn't.** A research pass (2026-07-27) found:
- `BorderManifestStore.WriteToFile()` works but `WorldGenPipeline`/`TileGridAssembler` never call
  it — no real world-gen run has ever produced a `manifests.bin`.
- `BorderManifestStore.LoadFromFile()` is `throw new NotImplementedException("M4 feature")`.
- `RiverResult` (the River layer's output) carries no per-edge crossing data (position 0–1 along
  the edge, width, flow volume) despite `mvp_spec.md` always assuming the manifest would be
  populated from it — the raw data this whole feature depends on for rivers doesn't exist yet.
- The UI (`Camera2D`/`TileMapRenderer`) has no zoom/LOD concept at all — it only ever addresses
  the single global 10km-tile grid.

This is a from-scratch subsystem, not an activation. Treat `docs/design_session_decisions.md`
DS-A2, `docs/architecture_decision_records.md` ADR-010/ADR-011, and
`docs/implementation_decisions_v0.3.md` §11 ("Two-Scale World Architecture") as the load-bearing
prior design intent — this doc extends them into an implementable phase sequence, not a redesign.

## Design decisions (resolved 2026-07-27, with the user, before scoping phases)

### DECISION: foundation for future interaction, not read-only flavor

Two shapes were on the table: (a) a purely cosmetic "zoom in and look" view with no hooks for
future gameplay, or (b) the same generation engine built so a later milestone can let a
spotlighted character actually act at 10m resolution without a redesign. **Chosen: (b).** This
phase sequence still ships only the *generation* and *viewing* capability — no local movement,
pathfinding, or combat logic is implemented here (see "Explicitly out of scope" below) — but the
data model (chunk/local-tile coordinate types, a nullable local-presence hook on characters) is
shaped so that a future milestone can wire in interaction against these same types.

### DECISION: any tile, on demand, via Tile Inspector

A `[View Local]` action on the existing `TileInspectorPanel` opens the local view for whichever
tile is currently inspected — not gated behind an active Spotlight. Matches the app's
worldbuilder/writer audience: exploring any location's detail should not require controlling a
character first.

### DECISION: regenerate base terrain on demand; persist only modifications, as a sparse delta overlay

Local terrain is a deterministic function of `(WorldSeed, ChunkCoord, parent TileData, border
manifests)` — same inputs always produce the same output, so the base terrain itself is never
persisted (no storage cost, per ADR-010's rationale). But the user correctly flagged the Minecraft
problem: once a spotlighted character (in a future milestone) can permanently alter local terrain
— build something, burn a forest, dig a channel — those changes must survive a return visit or
the world feels fake. **Chosen:** a sparse `LocalTileDelta` overlay, keyed by
`(ChunkCoord, LocalTileCoord)`, stored in `world.db` (Disk as System of Record, same as every
other authoritative state per CLAUDE.md Mandatory Pattern #6) and applied on top of the
freshly-regenerated base chunk whenever that chunk is loaded. Only tiles that were ever modified
take any storage at all.

### DECISION: chunked, lazy generation (Minecraft-style), not whole-tile eager generation

Not asked of the user directly — flagged here as a call made for engineering reasons, reversible
if it doesn't hold up. A full 10km world tile at 10m resolution is 1000×1000 = 1,000,000 local
tiles; the original design note's "3×3 Detailed Zone + 2-tile Buffer Zone" around a spotlighted
character would be 9,000,000 cells generated eagerly — too much to generate or hold in memory
up front. Instead: local tiles are grouped into fixed-size chunks (`LocalGen.ChunkSizeTiles` in
`SimConfig`, proposed default 32×32), generated lazily as the local-view camera's viewport
approaches them (view-distance radius, same pattern as Minecraft chunk loading), and discarded
(not persisted — see above) once out of range. Only the sparse delta overlay is ever written to
disk.

## Data model overview (introduced across phases 11.1–11.6)

- `ChunkCoord(TileCoord WorldTile, int ChunkX, int ChunkY)` — a chunk's position within its
  parent world tile.
- `LocalTileCoord(byte X, byte Y)` — a cell's position within its chunk (0..ChunkSizeTiles-1).
- *LocalTileData* — minimal per-cell struct (elevation, biome/decoration byte, flags), analogous
  in spirit to `TileData` but far smaller — no civ/economy fields, this is flavor terrain only.
- `LocalChunk` — `LocalTileData[ChunkSizeTiles, ChunkSizeTiles]`, always derivable, never itself
  persisted.
- `LocalTileDelta(ChunkCoord, LocalTileCoord, LocalChangeType, PayloadJson)` — the one thing that
  *is* persisted; a sparse, append-only-per-cell override list.
- Border manifest additions: `RiverResult` gains per-edge crossing points (position 0.0–1.0 along
  the edge, width, flow volume); `BorderManifestSample` gains whatever fields phase 11.1 finds it
  actually needs to drive continuity (do not add speculative fields beyond what the amplification
  algorithm in 11.3 consumes).

## Phase sequence

| Phase | Depends on | One-line deliverable |
|-------|-----------|----------------------|
| 11.1 | M11 phase 0 | Wire real border-manifest computation into `TileGridAssembler`; extend `RiverResult` with per-edge crossing data; implement `BorderManifestStore.LoadFromFile`; write `manifests.bin` at the end of world gen. |
| 11.2 | 11.1 | Local data model: `ChunkCoord`/`LocalTileCoord`/*LocalTileData*/`LocalChunk` types + coordinate math (chunk ↔ world-tile ↔ absolute conversions, wraparound consistent with `TileCoord.Wrap()`). Placeholder (flat/uniform) generator to unblock parallel UI work if desired. |
| 11.3 | 11.2 | Deterministic terrain amplification: noise-based elevation/moisture detail from parent `TileData`, blended to match border-manifest samples at world-tile edges. The cross-tile continuity proof (ADR-011's whole reason for existing) is the centerpiece test here. |
| 11.4 | 11.1, 11.3 | River threading: carve a plausible path through a chunk connecting the entry/exit crossing points from 11.1's manifest data; matches width/position with the neighboring tile's corresponding exit/entry. |
| 11.5 | 11.2 | `LocalTileDelta` overlay: sparse per-cell persistence in `world.db`, an `ICommand` to write one (`ModifyLocalTile`), applied on top of a freshly-regenerated base chunk on load. No real gameplay command produces deltas yet — prove the pipeline with a minimal test/debug command. |
| 11.6 | 11.2 | Entity/position foundation stub: nullable local-presence hook (`ChunkCoord`/`LocalTileCoord`) on `Tier1Character`, persisted, never populated by any current sim logic — `// V2: local-scale character movement/pathfinding`. Proves the addition doesn't perturb existing determinism. |
| 11.7 | 11.3, 11.4, 11.5 | UI: `[View Local]` on `TileInspectorPanel`, new local-view screen with its own camera (mirrors `Camera2D`'s pan/zoom API shape, new coordinate space), lazy chunk loading by view distance, delta overlay rendered on top. |
| 11.8 | all above | Close-out: full persistence round-trip test (save/load preserves deltas, regenerates base identically), reproducibility test extended to cover manifest/local-gen determinism, doc regen, roadmap/CLAUDE.md update. |

Do not start a phase until its dependencies are merged and green (`scripts/test-fast.sh`). Given
the size of this feature, expect each phase to get its own `docs/phases/m11_phase{N}_*.md` doc
(created when that phase's work begins) rather than being detailed further here up front.

## Explicitly out of scope for this phase sequence

Per CLAUDE.md's "What NOT to Build" + the "foundation, not full interaction" decision above:
- Actual character movement, pathfinding, or combat at 10m/local resolution — data shapes are
  laid (11.6) but no behavior. Leave `// V2: local-scale character movement/pathfinding` at the
  seam.
- Settlements/roads rendered at local scale — world-tile-level `RoadLevel`/`CivControl` exist
  today; a local-scale rendering of them is a follow-up, not required for this sequence to be
  "done."
- Any change to Spotlight's existing world-tile-granularity movement — untouched throughout.
- Performance/LOD tuning beyond "functional" — do not speculatively optimize chunk generation
  cost before it's measured (same discipline as M11 phase 0).

## Non-negotiable constraints (every phase)

From `CLAUDE.md`:
1. All new tunables (`ChunkSizeTiles`, view-distance radius, noise parameters, etc.) go in
   `SimConfig`/`sim_config.toml` — never hardcoded.
2. `ModifyLocalTile` (11.5) is a sealed record, value-type fields only, resolved only in the
   RESOLVE step — no direct `WorldState` mutation from behavior/scoring/UI code.
3. `WorldEngine.Sim` stays headless throughout. The terrain-amplification algorithm (11.3) must be
   a pure, stateless function so `WorldEngine.UI` can call it directly against snapshot data
   without violating the Sim/UI boundary — but it lives in `WorldEngine.Sim`, never duplicated
   into `WorldEngine.UI`.
4. Local terrain generation itself is *not* authoritative state (deterministic, regenerable) and
   is not persisted; the delta overlay *is* authoritative and follows Mandatory Pattern #6 (disk
   as system of record) exactly like every other permanent sim fact.
5. UI interaction split (Mandatory Pattern #4) applies to the new local-view screen: opening the
   screen, camera pan/zoom, and chunk-loading-in-progress indicators are interaction state (next
   frame, unconditionally); the chunk terrain data itself and delta overlay are snapshot-driven.
6. Every phase needs tests before being considered complete, per the project's Testing
   Requirements — this feature in particular lives or dies on the continuity proofs (11.1's
   adjacent-tile manifest consistency, 11.3's cross-tile terrain-blend match, 11.4's river
   entry/exit match) since a visible seam at a tile boundary is the one failure mode ADR-011
   exists specifically to prevent.

## Open implementation details left to each phase

Per CLAUDE.md's "How to Handle Ambiguity" — within-phase judgment calls, not cross-cutting:
- **11.1:** exact additional `BorderManifestSample` fields beyond what 11.3/11.4 end up needing
  (don't add width/depth/cliff-flag speculatively if the amplification algorithm doesn't consume
  them).
- **11.2:** `ChunkSizeTiles` default (32 proposed above, not committed) — pick based on what keeps
  a chunk's generation cost low enough for on-demand loading to feel instant.
- **11.3:** which noise algorithm (value noise / Perlin / simplex) and how many octaves — pick the
  simplest one that passes the continuity tests; don't over-engineer visual richness before the
  core seam-matching guarantee is solid.
- **11.5:** exact `LocalChangeType` enumeration and `world.db` table shape — start minimal (one
  generic change type + JSON payload, matching the `PendingEvent`/`SimEvent` payload pattern
  already used elsewhere) rather than pre-designing every future terrain-modification kind.
