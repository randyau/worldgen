# Phase 3.5 — Ancestry & Cultural Flavour

**Milestone:** 3 — Narrative Exploration
**Status:** PLANNED
**Goal:** Give each civilization a culturally distinct voice — architectural vocabulary,
naming conventions, artistic traditions — derived from their founding ancestry and shaped
by their history. This feeds spotlight tile generation (M3 Phase 3.4) and character
profile prose (M3 Phase 3.3), making "a Dwarven mining settlement" look and feel
different from "an Elven river city" even when the underlying sim mechanics are identical.

Depends on Phase 3.0 (territory and improvements exist), Phase 3.1 (history query),
and Phase 3.2 (cultural traits). Does NOT require LLM — all generation is rule-based
templates filled with ancestry and trait data.

---

## What already exists

`AncestryConfig` (loaded from `ancestries.toml`) contains per-ancestry fields:
- Name, personality/aptitude biases, spawn weights
- `FirstNames[]` and `LastNames[]` name pools (used for character naming)

Missing: architectural vocabulary, cultural descriptors, artistic traditions, biome
adaptation notes. These are the additions this phase makes.

---

## Epic 3.5.1 — Ancestry Cultural Descriptors

**Goal:** Each ancestry entry in `ancestries.toml` gains a cultural descriptor block
used by tile and character generation.

### Stories

**3.5.1.1 — AncestryConfig extensions**

Add to `AncestryConfig`:
```csharp
public string   ArchitecturalStyle   { get; set; } = "";   // e.g. "stone-carved", "timber-framed", "woven-reed"
public string   SettlementDescriptor { get; set; } = "";   // e.g. "hold", "grove", "encampment", "citadel"
public string[] BiomeAdaptations     { get; set; } = [];   // per-biome flavour: ["In forests: ...", "In deserts: ..."]
public string[] ImprovementDescriptors { get; set; } = []; // e.g. Farm → "tended terraces", Mine → "carved shafts"
public string[] ArtisticTraditions   { get; set; } = [];   // e.g. "tapestry", "stonework", "oral saga"
public string   CivNameSuffix        { get; set; } = "Domain"; // used in civ naming: "Grixal's Domain"
```

**3.5.1.2 — ancestries.toml updates**

Add the new fields to each existing ancestry entry. Example:
```toml
[[ancestry]]
id = "mountain_folk"
architectural_style    = "hewn-stone"
settlement_descriptor  = "hold"
biome_adaptations      = [
    "mountain: carved into cliffsides with narrow approach roads",
    "plains:   stone towers rising above the grassland"
]
improvement_descriptors = [
    "farm:    terraced slopes with gravity-fed irrigation",
    "mine:    deep shafts with rope-and-pulley ore lifts",
    "logging: minimal — timber imported from lowland allies"
]
artistic_traditions = ["engraved stonework", "oral chronicle", "metalwork"]
civ_name_suffix = "Hold"
```

---

## Epic 3.5.2 — CulturalProfile per Civilization

**Goal:** Each live civilization has a `CulturalProfile` derived from its dominant
ancestry + acquired cultural traits (Phase 3.2). The profile is a cached read-only
snapshot used by tile generation and narrative display.

### Stories

**3.5.2.1 — CulturalProfile record**

```csharp
public sealed record CulturalProfile(
    string         AncestryId,
    string         ArchitecturalStyle,
    string         SettlementDescriptor,
    string[]       ArtisticTraditions,
    string[]       ActiveTraits,          // from CivTraits (Phase 3.2)
    string         DominantBiome);        // biome of capital tile
```

Computed once per civ after founding and updated when new cultural traits are acquired.
Stored in `Civilization.CulturalProfile` (nullable until computed).

**3.5.2.2 — CulturalProfile builder**

`CivTracker.BuildCulturalProfile(CivId, WorldState)` — reads the civ's `DominantAncestry`
(from CharacterSummary), looks up `AncestryConfig`, and assembles the profile. Called
at civ founding and after each `CivTraitAcquired` event.

**3.5.2.3 — Snapshot propagation**

`CivSnapshot` (used by UI) gains a `CulturalProfile?` field. `SnapshotBuilder` copies
it from `Civilization.CulturalProfile` each tick.

---

## Epic 3.5.3 — Tile Content Description

**Goal:** Given a tile coord, generate a short human-readable description of what is
"in" that tile — terrain, improvements, who lives there, cultural context. Used by the
Tile Inspect panel and as the data source for M4 Spotlight deeper generation.

This is entirely rule-based template fill — no LLM.

### Stories

**3.5.3.1 — TileContentDescriber (static service)**

```csharp
public static class TileContentDescriber
{
    public static TileContentDescription Describe(
        TileCoord coord,
        TileData tile,
        CulturalProfile? ownerCulture,
        ImprovementType? improvement,
        IReadOnlyList<EntitySnapshot> charactersPresent,
        IReadOnlyList<SimEvent> recentHistory);
}

public sealed record TileContentDescription(
    string TerrainSentence,      // "Rolling grassland, warm and well-watered."
    string? TerritoryLine,       // "Claimed by Grixal's Hold — a hewn-stone hold."
    string? ImprovementLine,     // "Terraced slopes with gravity-fed irrigation cover the hillside."
    string? CharacterLine,       // "Oren the Wanderer shelters here tonight."
    string? HistoryLine);        // "This ground was contested in the War of Ember Roads (Year 312)."
```

Template sources:
- `TerrainSentence`: biome → a bank of 3–5 short sentences, selected by tile position hash
- `TerritoryLine`: ancestry `SettlementDescriptor` + `ArchitecturalStyle`
- `ImprovementLine`: ancestry `ImprovementDescriptors[improvementType]`
- `CharacterLine`: character name + epithet + current activity (resting/travelling/building)
- `HistoryLine`: most significant event at this tile from event history

**3.5.3.2 — Wire to Tile Inspect Panel**

Display `TileContentDescription` in the Tile Inspect panel (Phase 3.4.2) as a
short flavour block below the data grid. Italicised text, ~3–5 lines.

**3.5.3.3 — BiomeTerrain sentence banks**

Add `docs/content/biome_terrain_sentences.toml` with 5 sentence variants per biome.
Loaded at startup into a `BiomeTerrainBank` — selected by `(coord.X * 31 + coord.Y) % 5`.
This gives each tile a consistent, deterministic terrain flavour sentence without any
runtime randomness.

---

## Definition of Done

- Every ancestry in `ancestries.toml` has all new cultural descriptor fields populated
- `CulturalProfile` is built for all civs in a test run; accessible on `CivSnapshot`
- Tile Inspect panel shows a 3–5 line flavour block describing terrain, territory, and improvement
- The flavour text visibly differs between Dwarven/Elven/Human settlements in the same biome
- All tests pass; no hardcoded cultural strings in C# (all in TOML/data files)
