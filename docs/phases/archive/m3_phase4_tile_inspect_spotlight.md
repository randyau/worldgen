# Phase 3.4 — Tile Inspect & Character Watch

**Milestone:** 3 — Narrative Exploration
**Status:** COMPLETE — 2026-06-26
**Goal:** Let the player click any tile and see what is actually in it — territory owner,
improvement, characters present, deposits, and what has happened there — and open a
read-only character watch panel for any named character. This is the M3-safe foundation
for M4 Spotlight (player-controlled character).

Depends on Phase 3.0 (TerritoryMap, ImprovementMap) and Phase 3.1 (HistoryQuery API).

---

## Epic 3.4.1 — Territory & Improvement Map Overlay

**Goal:** The tile map can show territory ownership as civ-colored fills, and individual
tile improvements as small icons, so the player can see at a glance who owns what.

### Stories

**3.4.1.1 — Territory color overlay**

Add `OverlayType.Territory` to the overlay enum. When active, each tile is tinted with
the owning civ's color (derived deterministically from CivId). Unclaimed land is neutral
(transparent overlay). City tiles get a slightly brighter center dot.

Implementation: `TileMapRenderer` reads `WorldSnapshot.TerritorySnapshot` (a new
`IReadOnlyDictionary<TileCoord, (CivId, string CivName)>` added to the snapshot) and
applies the tint in the Draw pass. Tint alpha ~0.35 so biome colors remain visible.

**3.4.1.2 — Improvement icons on tile map**

When the territory overlay is active, tiles with an improvement draw a small icon (8×8
sprite) in the top-left corner of the tile. Icons per `ImprovementType`:
Farm, Mine, LoggingCamp, Pasture, Fishery. Sprites are simple colored glyphs — a
tilted square for Farm, a triangle for Mine, etc. (No art assets required; geometric
sprites generated at startup from `Texture2D`.)

**3.4.1.3 — Keybind and toggle**

Bind the territory overlay to key `T` (consistent with existing overlay key pattern).
Add a UI checkbox in the sidebar overlay section labeled "Territory."

---

## Epic 3.4.2 — Tile Inspect Panel

**Goal:** Clicking a tile opens an expanded inspector panel showing everything the sim
knows about that specific 10 sq km tile — static, dynamic, and historical.

### What it shows

```
┌──────────────────────────────────────────┐
│ Tile (134, 58) — Grassland               │
│ Fertility: 202  Moisture: 59  Temp: 45°C │
├──────────────────────────────────────────┤
│ TERRITORY                                │
│  Grixal's Domain (city: Veth, 2.1 km E) │
│  Improvement: Farm (built Year 47        │
│               by Thaela the Builder)     │
├──────────────────────────────────────────┤
│ RESOURCES                                │
│  Iron deposit (quality 0.82, depth 0.34) │
├──────────────────────────────────────────┤
│ CHARACTERS HERE                          │
│  Oren the Wanderer  [watch]              │
├──────────────────────────────────────────┤
│ HISTORY AT THIS TILE                     │
│  Year 14 — SettlementFounded (Veth)      │
│  Year 312 — WildlifeRaid                 │
│  Year 601 — ImprovementBuilt (Farm)      │
└──────────────────────────────────────────┘
```

### Stories

**3.4.2.1 — TileInspectPanel expansion**

Extend the existing `TileInspectorData` / sidebar to show the territory and improvement
sections. `TileInspectData` (already sent to UI via snapshot) gains:
```csharp
public string?          TerritoryOwnerName  { get; init; }
public string?          TerritoryCityName   { get; init; }
public TileCoord?       TerritoryCityTile   { get; init; }
public ImprovementType? Improvement         { get; init; }
public int              ImprovementBuiltYear{ get; init; }
public string?          ImprovementBuilderName { get; init; }
```

**3.4.2.2 — History at this tile**

Add `GetTileHistory(TileCoord coord, int maxEvents = 10)` to `IHistoryQuery`. Backed by
a SQLite query: `SELECT * FROM Events WHERE LocationX=? AND LocationY=? ORDER BY Year DESC LIMIT ?`.
Display the last 10 events at the tile in the inspector, newest first.

**3.4.2.3 — Character list with Watch link**

Characters at the tile are listed by name (already tracked on snapshot). Each name is
a clickable link that opens the Character Watch panel (Epic 3.4.3).

---

## Epic 3.4.3 — Character Watch Panel

**Goal:** A read-only live panel for any named character showing their current state
and recent history. This is the precursor to M4 Spotlight — everything read-only,
no player-issued commands.

### What it shows

```
┌──────────────────────────────────────────┐
│ Oren the Wanderer                        │
│ Civ: Grixal's Domain  Age: 34            │
│ Location: (134, 58) — Grassland          │
├──────────────────────────────────────────┤
│ NEEDS (live)                             │
│  Food    ████████░░  0.82                │
│  Safety  █████░░░░░  0.51                │
│  Shelter ███░░░░░░░  0.31  ← low         │
│  Status  ██████░░░░  0.64                │
├──────────────────────────────────────────┤
│ ACTIVE GOALS                             │
│  BuildImprovement (priority 0.9)         │
│  Survive          (priority 0.6)         │
├──────────────────────────────────────────┤
│ PERSONALITY                              │
│  Ambition ████████░░  Curiosity ██████░░ │
├──────────────────────────────────────────┤
│ RECENT EVENTS                            │
│  Year 840 — ImprovementBuilt at (134,58) │
│  Year 831 — CharacterBorn                │
└──────────────────────────────────────────┘
│ [Full Profile ↗]                         │
└──────────────────────────────────────────┘
```

"Full Profile" opens the Character Profile Card from Phase 3.3.1.

### Stories

**3.4.3.1 — CharacterWatchSnapshot**

Add a `CharacterWatchSnapshot` to `WorldSnapshot` keyed by the watched `EntityId`
(nullable — only populated when a watch target is set):
```csharp
public sealed record CharacterWatchSnapshot(
    EntityId    Id,
    string      Name,
    string?     Epithet,
    CivId       CivId,
    string      CivName,
    TileCoord   Location,
    int         AgeSeasons,
    NeedsVector Needs,
    IReadOnlyList<GoalData> Goals,
    PersonalityVector Personality);
```

Populated in `SnapshotBuilder` when `world.WatchedCharacterId` is set (new nullable
field on WorldState, set via a new `WatchCharacter(EntityId)` UI command).

**3.4.3.2 — CharacterWatchPanel (Myra)**

New collapsible panel in the sidebar. Renders all fields from `CharacterWatchSnapshot`
with bar graphs for needs and personality. Visible only when a character is being watched.

**3.4.3.3 — Watch target wiring**

Clicking a character name anywhere (tile inspect panel, event log actor name) enqueues
`WatchCharacter(entityId)` on the command queue. The sidebar watch panel appears.
A close/X button enqueues `WatchCharacter(EntityId.None)` to clear.

---

## Definition of Done

- Territory overlay renders civ-color fills; improvement icons visible on map
- Clicking any tile shows territory owner, improvement, deposits, characters, and last 10 events
- Clicking a character name opens the watch panel with live needs/goals updating each tick
- Watch panel "Full Profile" link opens the character profile card (Phase 3.3.1)
- All fields gracefully absent when tile is unclaimed / unimproved
- All tests pass
