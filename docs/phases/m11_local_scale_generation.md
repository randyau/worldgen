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
- **11.3 — COMPLETE (2026-07-28).** New `LocalTerrainAmplifier.Amplify` (`WorldEngine.Sim/WorldGen/`)
  is the real (non-placeholder) local-chunk generator: a pure function of `(worldSeed, ChunkCoord,
  parent TileData, parent BorderManifest, LocalGenConfig)`. Elevation = a macro component blended
  from the parent tile's own byte value toward the shared `BorderManifest` edge sample within
  `EdgeBlendBandTiles` local tiles of a world-tile edge, plus FastNoiseLite (`OpenSimplex2`, FBm)
  detail sampled at the cell's *absolute* local-tile coordinate (`LocalCoordMath.ToAbsolute`) so the
  detail layer is automatically continuous — same seed + same absolute coordinate always produces
  the same noise value, regardless of which chunk/tile is doing the generating, no explicit
  blending needed for that term. The macro blend picks whichever axis (X vs Y) has the larger blend
  weight, ties going to X; since the weight on a shared world-tile edge is always exactly 1.0, this
  makes the entire East/West boundary column resolve to the identical (by construction, per
  `BorderManifestBuilder`) manifest sample on both adjacent tiles — proven exactly by
  `LocalTerrainAmplifierTests.Amplify_EastWestBoundary_IsContinuousAcrossWorldTiles` (the centerpiece
  continuity test). The North/South boundary is continuous everywhere except its two extreme corner
  columns, which are also on an East/West boundary shared with a third, diagonal tile — an inherent
  ambiguity when only two adjacent manifests exist to blend from (not solved here; documented as a
  known limitation in code and covered by `Amplify_NorthSouthBoundary_IsContinuousAwayFromCorners`,
  which checks the non-corner interior of that boundary). `LocalTileGenerator.GenerateFlat` (11.2)
  is untouched and still available as a cheap placeholder; `LocalTerrainAmplifier.Amplify` is the
  generator later phases (11.7's UI) should call. New config:
  `LocalGenConfig.EdgeBlendBandTiles`/`NoiseFrequency`/`NoiseOctaves`/`NoiseAmplitude`
  (`local_gen.edge_blend_band_tiles`/`noise_frequency`/`noise_octaves`/`noise_amplitude` in
  `sim_config.toml`). New `LayerSeeds.LocalTerrain` salt. `DECISION:` moisture amplification is
  deferred — `LocalTileData` (11.2) has no moisture field and nothing downstream consumes one yet,
  so only elevation is amplified this phase; `BiomeType` remains inherited verbatim from the parent
  tile, same as the 11.2 placeholder.
- **11.4 — COMPLETE (2026-07-28).** New `LocalRiverThreader.Thread` (`WorldEngine.Sim/WorldGen/`)
  is a post-process pass over an already-generated `LocalChunk`, carving a river channel that
  connects the parent tile's boundary river crossing(s). A crossing's position/width is recovered
  directly from the contiguous run of `HasRiverCrossing`-marked samples on a manifest edge (not
  from the raw `RiverCrossing` record) — since `BorderManifestBuilder` stamps both sides of a
  shared edge from the identical crossing, both adjacent tiles recover byte-identical
  position/width from their own manifest, satisfying "matches width/position with the neighboring
  tile's corresponding exit/entry" without needing to share state. With two boundary crossings the
  channel is a straight segment connecting them; with one, it connects to the tile's center
  (source/mouth case), tapering to `LocalGenConfig.RiverSourceWidthTiles`; with zero, the tile's
  river is interior-only (e.g. lake-fed) and is not carved this phase (`DECISION:` — no boundary
  anchor exists to key continuity off of); tiles with 3+ crossings connect only the first two found
  (N/S/E/W scan order) — both are documented edge-case simplifications, same spirit as 11.3's
  north/south corner limitation. Carved cells get `LocalTileFlags.River` (new
  `Tiles/LocalScale/LocalTileFlags.cs`, the first bit assigned in `LocalTileData.Flags`) and an
  elevation drop of `RiverChannelDepth`. Continuity is proven at the crossing's own anchor point
  (exactly shared between both tiles' recovered position/width) rather than a full boundary-column
  match — `LocalRiverThreaderTests.Thread_RecoveredAnchor_MatchesAcrossSharedEdge` proves the
  recovered anchors agree exactly, and `Thread_BoundaryColumns_AgreeAtCrossingCenterRow` proves
  both sides' chunks carve the anchor's own row; unlike 11.3's elevation blend, a full-column exact
  match isn't claimed since each tile's channel approaches the shared point from its own interior
  direction. New config: `LocalGenConfig.RiverChannelDepth`/`RiverSourceWidthTiles`
  (`local_gen.river_channel_depth`/`river_source_width_tiles` in `sim_config.toml`).
  `LocalTerrainAmplifier.Amplify` is untouched; `Thread` is meant to run on its output as a second
  pass (not fused into `Amplify`), same relationship 11.2's `GenerateFlat` has to 11.3's `Amplify`.
- **11.5 — COMPLETE (2026-07-28).** New `LocalTileDelta(ChunkCoord, LocalTileCoord, LocalChangeType,
  PayloadJson)` (`Tiles/LocalScale/`) is the sparse per-cell overlay: `LocalChangeType` starts with
  a single `CellOverride` value and `LocalTileDeltaPayload` (Elevation/BiomeType/DecorationType/
  Flags, all nullable) is its JSON payload shape, matching the `PendingEvent`/`SimEvent`
  typed-payload-as-JSON-string pattern per the phase doc's own scoping note. Persistence: new
  `LocalTileDeltas` table in `world.db` (`DatabaseSchema.CreateLocalTileDeltas`), keyed by
  `(WorldTile, Chunk, Local)`; `EventStore.WriteLocalTileDelta` is `INSERT OR REPLACE` (one row per
  modified cell, never a growing log — a second write to the same cell replaces the first) and
  `EventStore.LoadLocalTileDeltas(ChunkCoord)` reads them back for a chunk. `ModifyLocalTile`
  (new `ICommand` in `Commands/PlayerCommands.cs`) is the minimal test/debug command that proves
  the pipeline end-to-end, as scoped ("no real gameplay command produces deltas yet") — resolved in
  `SimLoop.ApplyCommand` via a new `PhaseRunner.WriteLocalTileDelta` passthrough to `EventStore`
  (`SimLoop` has no direct `EventStore` reference, same reason `FlushPendingEvents` lives on
  `PhaseRunner`). `LocalTileDeltaApplier.Apply` (`WorldGen/`) is the fourth and final post-process
  pass — after `LocalTerrainAmplifier.Amplify` (11.3) and `LocalRiverThreader.Thread` (11.4) — so a
  player-caused modification always wins over freshly-regenerated base terrain; it applies only the
  non-null fields in each delta's payload, leaving everything else on the cell untouched.
  `ModifyLocalTileCommandTests` proves the full round trip: command enqueued → `SimLoop` resolves it
  → row lands in `world.db` → `LoadLocalTileDeltas` + `Apply` reproduce the override on a chunk
  regenerated from scratch, mirroring `SpotlightCommandTests`' pattern for exercising `SimLoop`'s
  private command switch through the real `CommandQueue` path. No new `SimConfig` tunables this
  phase — nothing here is a numeric constant.
- **11.6 — COMPLETE (2026-07-28).** `Tier1Character` gains a nullable local-presence hook —
  `ChunkCoord? LocalChunk` and `LocalTileCoord? LocalPosition` — populated by no current sim
  logic (`// V2: local-scale character movement/pathfinding` at the seam, per the phase sequence
  table's exact wording). Persisted through the existing DTO/`WorldStateMapper` round-trip
  pattern: `Tier1EntityDto` gains two nullable string fields (`LocalChunkKey`/`LocalPositionKey`,
  same flattened-key idiom as `TileKey`/`ParseTile` used elsewhere in the mapper for struct
  fields, e.g. `GoalData.TargetTile`) rather than registering the raw structs with the
  System.Text.Json source-gen context. `SaveLoadTests` gained two cases: the common path (an
  ordinary simulated world's characters all round-trip with both fields null) and the populated
  path (a character with both fields explicitly set survives save/load exactly). No existing
  reproducibility/determinism test needed changes or broke — nothing writes these fields during a
  tick, so there's no new source of divergence to prove out. No new `SimConfig` tunables.
- **11.7 — COMPLETE (2026-07-29).** UI: a `[View Local]` button on `TileInspectorPanel`
  (`OnViewLocal`, an `Action<TileCoord, TileData>?` delegate following the same pattern as
  `OnWatch`) invokes the currently-inspected tile's own already-read `RawTile` data — no separate
  `WorldState` lookup needed, since the UI thread never touches `WorldState` directly. New
  `LocalViewScreen` (`WorldEngine.UI/UI/`) mirrors `WorldGenPreviewScreen`'s shape: a `Panel Root`
  added once to the desktop root, toggled visible/hidden alongside `MainUI` (`Game1.ShowLocalView`/
  `CloseLocalView`), drawn directly via `SpriteBatch` (not through Myra `Image` widgets, since it's
  pannable/zoomable like the main map) rather than through the RightDock/panel system. New
  `LocalCamera2D` (`WorldEngine.UI/Rendering/`) mirrors `Camera2D`'s pan/zoom API shape at
  10m-local-tile granularity, operating in within-world-tile local-tile coordinates
  (0..`LocalTilesPerWorldTileEdge`) rather than Camera2D's global 10km-tile space. New
  `LocalTileMapRenderer` mirrors `TileMapRenderer`'s solid-pixel-rect approach, coloring cells via
  `OverlayRenderer.GetBiomeColor` (made `public` so both renderers share one palette) with simple
  elevation shading and a river tint — local view has no overlay-type switcher yet.
  `LocalViewScreen.Update` lazy-loads chunks within `LocalGenConfig.ViewDistanceChunks` (new
  config, default 3) of the camera's center each frame, generating each via
  `LocalTerrainAmplifier.Amplify` + `LocalRiverThreader.Thread` (falling back to
  `LocalTileGenerator.GenerateFlat` when no border manifest is available — see below) then
  `LocalTileDeltaApplier.Apply` with deltas read live from `EventStore.LoadLocalTileDeltas`, and
  evicts chunks now outside that radius — never persisted, regenerated fresh next time they're
  in range, exactly the "chunked, lazy generation... discarded once out of range" design decision.
  `DECISION:` local view is scoped to the single world tile it was opened on — panning past that
  tile's own chunk grid renders empty background rather than loading a neighboring world tile's
  chunks (`ChunkCoord.Normalize`'s cross-tile-boundary support exists but isn't exercised here);
  cross-world-tile local panning is left to a future milestone, consistent with "foundation, not
  full interaction."
  **Manifest-wiring gap closed:** 11.1–11.6 built the local-gen pipeline but nothing in the live
  `Game1` sim-start path ever produced a `BorderManifest` — `WorldGenPreviewScreen.OnCommitClicked`
  now also computes `BorderManifestBuilder.Build(_ctx)` (exposed via new `LastManifests` property,
  read the same frame `Update()` returns the committed `WorldState`) and `Game1.StartSim` persists
  it once, immediately, to `worldsave/manifests.bin` via `BorderManifestStore.WriteToFile` — border
  manifests are deterministic and never regenerated after world gen, so a single write at first
  commit is sufficient (no per-tick or per-save rewrite). The loaded-world path
  (`StartSimFromLoad` → `StartSim(world, spawnInitialEntities: false)`) reads it back via
  `BorderManifestStore.LoadFromFile`; a save that predates 11.1/11.7 has no `manifests.bin`, so
  `_borderManifests` stays empty and `LocalViewScreen` falls back to
  `LocalTileGenerator.GenerateFlat` per-tile (11.2's placeholder generator) instead of failing —
  the title bar says so ("no border data — flat placeholder terrain"). `ResetToNewWorld` clears
  `_borderManifests` (a fresh world invalidates the old one) and `WorldStateSaver.DeleteSave`
  already recursively deletes `manifests.bin` alongside `state.bin`/`meta.json`, so no separate
  cleanup was needed there.
  **Follow-up (2026-07-29, same day):** first manual playtest showed a flat, near-uniform-green
  chunk with no legible sub-tile detail, plus a visible frame stall on opening the screen. Two
  fixes: (1) new `LocalDecorationGenerator` (`WorldEngine.Sim/WorldGen/`) populates
  `LocalTileData.DecorationType` (reserved since 11.2, unused until now) — a low-frequency
  "cluster" `FastNoiseLite` channel places patchy regions of a biome's primary decoration
  (`LocalDecorationType`: TreeStand/RockOutcropping/Shrub/Wetland/SandDune, new enum) and a
  high-frequency "sparse" channel scatters an occasional secondary feature elsewhere, skipping
  river cells; new `LocalGenConfig.DecorationClusterFrequency`/`DecorationClusterThreshold`/
  `DecorationSparseFrequency`/`DecorationSparseThreshold`. Purely cosmetic today (no gameplay reads
  `DecorationType` yet) — `// V2: local decoration → persistent object promotion` at the generator
  notes that a future "mine/collect" interaction needs no new identity scheme: `(ChunkCoord,
  LocalTileCoord)` is already the stable per-cell key `LocalTileDelta` (11.5) uses, so a future
  command would just write a delta for that cell the first time it's touched — the delta overlay
  already *is* the "this location now has tracked, permanent state" registry, per a DECISION made
  with the user before implementing. `LocalTileMapRenderer` now shades by neighbor-relative slope
  (south/east elevation deltas) instead of absolute elevation — the physical `NoiseAmplitude`
  constant (±6 of 255) is far too small a swing to read as relief when compared against 0-255, but
  compared against a neighbor it reads as visible texture — and draws an inset colored rect per
  decorated cell. (2) `LocalViewScreen.Update` was generating every missing chunk in the view
  radius synchronously in one call (up to 49 chunks the first frame) — now throttled to
  `MaxChunksPerFrame = 6`, nearest-chunk-first (Chebyshev distance sort), spread across however
  many frames it takes; eviction now keeps a 1-chunk hysteresis margin (`viewDist + 1`) so a camera
  sitting right at a chunk boundary doesn't evict-then-immediately-regenerate the same chunk every
  frame. A live `{chunks} chunks · zoom · center chunk (...)` stats label was added to the header
  for on-screen diagnosis of both issues. New unit tests: `LocalDecorationGeneratorTests`
  (determinism, forest biomes get patchy TreeStand not full coverage, water biomes and river cells
  never decorate). Full solution build is warning-free; all 668 tests pass (664 prior + 4 new).
  **Second follow-up (2026-07-29, same day):** playtest surfaced three more issues. (1) The chunk
  loader still showed a ragged, black-bordered loaded region and "tearing" while panning — root
  cause was that the loader picked a *fixed chunk-count radius* from the camera's center regardless
  of zoom or viewport size; at lower zoom (or on a wide viewport) that radius covered less screen
  area than the actual visible viewport, so the edges of the visible area were permanently
  unloaded/stale. Fixed by deriving the loaded chunk range directly from
  `LocalCamera2D.GetVisibleTileBounds` (plus `ViewDistanceChunks` as a preload margin on top, not
  the sole radius) — the visible area is now always what's requested, at any zoom. Chunk-priority
  sort changed from Chebyshev-from-a-single-center to Euclidean distance from the visible range's
  midpoint (matches the now-rectangular-not-square load region); `MaxChunksPerFrame` raised 6→12
  since a wide low-zoom viewport can need many more chunks to fully cover. (2) The sim kept
  ticking in the background while the local view was open — nothing in local view can see or
  react to that yet (no local movement/interaction per this phase's scope), so entering now enqueues
  `SetSimSpeed(Paused)` (`Game1.ShowLocalView`) unless the sim is already paused, and closing restores
  whatever speed was running before (`Game1.CloseLocalView`, via a new `_speedBeforeLocalView` field)
  — matches the "ostensibly paused" behavior the local view was designed to imply but didn't
  actually enforce. (3) There was no way to see a character/beast/settlement located on the tile
  being viewed, or to inspect/interact with one. `LocalViewScreen.Update` now takes the current
  `WorldSnapshot` and cross-references `snapshot.EntitySnapshots`/`Settlements` against the open
  world tile; matches get drawn via new `LocalTileMapRenderer.DrawMarkers` using the *same* marker
  glyphs as the main map's `TileMapRenderer` (cross for characters, dot for beasts, square for
  settlements) so the visual vocabulary carries over, and are listed in a new "On This Tile" panel
  built with `PanelFrame.Build` — the same titled/bordered shell the RightDock's contextual panels
  use, per the user's "reuse the map view UI" ask — with a `[Watch]` button per character/beast that
  closes the local view and opens the existing (Summoned) Watch panel on the main map, reusing
  `WatchEntity` rather than building local-view-specific watch behavior. Characters/beasts have no
  real sub-tile position yet (`Tier1Character.LocalPosition` is an unpopulated 11.6 stub), so each
  gets a stable per-entity pseudo-position hashed from its `EntityId` (new `LocalEntityMarker`
  record, `WorldEngine.UI/Rendering/`) — deterministic per entity, but explicitly not a real
  location; documented at the record and left as a `// V2: local-scale character movement` seam,
  same as 11.6. Also fixed: `[View Local]` was placed after the Ruin/Settlement sections in
  `TileInspectorPanel`, so a settlement tile's resource ledger/stores could push it far enough down
  a scrollable sidebar to be effectively undiscoverable for exactly the tiles — settlements — users
  most want to zoom into. Moved to the top of the panel, unconditionally, before any tile-specific
  content. No new tests added this pass (UI wiring/positioning fixes over already-tested code, same
  rationale as the original 11.7 entry); full solution build warning-free, all 668 tests still pass.
  **Third follow-up (2026-07-29, same day):** the second follow-up's "On This Tile" panel and
  bespoke `[Watch]` button were themselves the wrong shape — the user pointed out that clicking
  didn't feel possible in local view at all, the Watch button did nothing (because hiding all of
  `MainUI`, including `RightDock`/`Float`, meant there was nowhere left for the panel it tried to
  summon to render), and there were no time controls even though the sim was supposedly paused.
  Root cause: `ShowLocalView`/`CloseLocalView` toggled the entire `MainUI` panel — which bundles
  TopBar (time controls), RightDock (TileInspector/contextual panels), and Float (Summoned panels)
  as one widget tree — off and on, so *all* of that machinery, not just the map, disappeared.
  Reworked local view from a full-screen takeover into a **MapCanvas-region content swap**:
  `LocalViewScreen.Root` no longer stretches to fill the screen; a new `SetBounds(Rectangle)`
  positions its now-minimal HUD strip (title/hint/stats/`[Back to World Map]`) inside
  `RegionSlot.MapCanvas`'s bounds, called from `Game1.ApplyLayout` (mirroring how `topBarPanel`/
  `_dockScroll` are positioned) and once immediately in `ShowLocalView` so it's correctly placed
  the instant it opens, not just after the next resize. `ShowLocalView`/`CloseLocalView` no longer
  touch `MainUI`'s visibility at all — TopBar and RightDock simply stay live throughout, giving
  working time controls "for free" (the auto-pause-on-entry/resume-on-exit from the second
  follow-up is kept as a sensible default, but the player can now manually resume via the
  still-visible controls if they want the world to keep moving while looking around).
  `Game1.Draw` now scissors the local-view draw to `RegionSlot.MapCanvas.Bounds` exactly like the
  main map's `_tileRenderer.Draw` call, and the Timeline bar draws unconditionally (previously
  skipped whenever local view was open). The bespoke "On This Tile" `PanelFrame`/`[Watch]`
  button/`OnWatchEntity` callback were deleted outright — `LocalEntityMarker` gained a `long? Id`
  (and `int Population` for settlements) and `LocalViewScreen.TryPickEntity(Vector2)` hit-tests a
  screen point against the marker list (14px pick radius); `Game1.HandleInput`'s local-view branch
  now calls `_selectionBus?.Select(new EntityRef(...))` on a marker click exactly like the main
  map's tile click does, so whichever contextual panel that selection kind already shows (with its
  own working `[Watch]` button) appears in the still-visible RightDock automatically — no
  local-view-specific watch/inspect UI needed at all, directly fulfilling the "reuse the map view
  UI" request instead of approximating it. `HandleLocalViewInput` was deleted; its pan/zoom/escape
  logic was folded into `HandleInput` as an `if (localViewOpen) {...} else {...}` branch so
  keybind processing and timeline scrubbing (previously skipped while local view was open) are
  shared between both paths rather than duplicated. Also: settlement markers now scale their
  drawn size with population (`log10`-scaled) instead of a single fixed-size blip regardless of
  village vs. city — `// V2: procedural village/building layout` left at the draw site for the
  user's "could in theory render an actual village" idea, out of scope for this pass. No new tests
  (UI wiring, same rationale as prior follow-ups); full solution build warning-free, all 668 tests
  still pass.
  **Fourth follow-up (2026-07-29, same day):** the third follow-up's MapCanvas-swap still had a
  coordinate-space bug — `LocalCamera2D` was centered/panned using *full window* dimensions while
  `Game1.Draw` scissored (but did not translate) drawing to the smaller MapCanvas rectangle, so the
  camera's idea of where things are and the screen's idea of where MapCanvas actually sits
  disagreed. Symptoms matched exactly: scroll-zoom appearing dead and clicks having no effect
  (`ZoomAt`/`TryPickEntity` were comparing raw window mouse coordinates against camera math done
  in a different frame of reference), header text clipped (`Root` had no explicit size — Myra's
  default stretch tried to fill the full window, not the narrower MapCanvas rect it was supposed to
  fit inside), and terrain not drawing at all while markers still appeared (terrain draws are
  culled by `GetVisibleTileBounds`, which no longer matched where the scissor/camera actually put
  things; `DrawMarkers` has no such culling, so it kept drawing regardless). Fixed properly this
  time: `LocalCamera2D`'s coordinate space is now *MapCanvas-local* (0,0 = MapCanvas's own top-left)
  everywhere — `LocalViewScreen.SetBounds`/`Show` take the actual `RegionSlot.MapCanvas` `Rectangle`
  and use its Width/Height for `CenterOn`/`GetVisibleTileBounds`; a new private `ToLocal(Vector2)`
  converts a raw window/mouse point before it ever touches the camera, applied inside `ZoomAt` and
  `TryPickEntity` (`Pan` needs no conversion — a delta between two points already cancels out any
  constant offset). `Game1.Draw` no longer just scissors; it also `Begin()`s the local-view
  SpriteBatch with `transformMatrix: Matrix.CreateTranslation(mapCanvas.X, mapCanvas.Y, 0)`, so
  everything `LocalViewScreen.Draw` computes in local space lands in the right place on screen
  without every draw call needing its own offset math. `Root`'s header strip now sets
  `HorizontalAlignment.Left`/`VerticalAlignment.Top` explicitly (not Myra's default stretch) with
  `SetBounds` also assigning `Root.Width`/`Height` to the MapCanvas rectangle's own dimensions, so
  it can no longer grow wider than the region it's meant to sit inside. No new tests (coordinate-
  space/positioning fix over already-tested rendering code); full solution build warning-free —
  667/668 tests pass, the one failure (`SimLoop_StepOneTickAdvancesExactlyOne`) is a pre-existing,
  environment-timing-sensitive test in `WorldEngine.Sim/Simulation/SimLoop.cs` untouched by any
  11.7 commit (confirmed via `git log`/`git diff`), not a regression from this work.
- **11.8 — not started.**

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
