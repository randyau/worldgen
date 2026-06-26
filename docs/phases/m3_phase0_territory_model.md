# Phase 3.0 — City-State Territory Model

**Milestone:** 3 — Narrative Exploration (prerequisite refactor)
**Status:** IN PROGRESS
**Goal:** Replace the expansion-blob settlement system with a city-state model where
cities are rare, large, and own surrounding territory tiles rather than spawning new
settlement entities to claim land. Lays the data foundation that Milestone 3 spotlight
(deep-dive into a single tile) requires.

---

## Motivation

At 10 sq km per tile, a "settlement" spanning 20+ adjacent tiles is incoherent.
The current model where every expansion-goal character founds a new SettlementStub
produces dense blobs and prevents meaningful territory boundaries.

The new model:
- **Cities are rare** (1–5 per civ over hundreds of years)
- **Cities own territory** (a set of claimed tiles around them, not new stubs)
- **Tile improvements** (Farm, Mine, etc.) are placed on claimed territory by characters
- **Rulers decide** to found new cities; a delegated character carries it out
- **Milestone 3 spotlight** assembles tile content from: biome + territory owner + improvement + characters present + event history — all of which exist after this phase

---

## What changes vs. what stays

**Removed / retired:**
- `EstablishSettlement` command for expansion/colonize goal characters
- `GoalType.Expansion` and `GoalType.Colonize` character goals (expansion is now city-driven)
- `ReachRadius()` on SettlementStub (replaced by explicit territory set)
- `HinterlandFactor` / hinterland cache in UtilityScorer
- `IsColony` flag on SettlementStub (all cities are cities; no distinction needed)
- `MaxSettlementsPerCiv`, `MaxColoniesPerCiv` config keys (replaced by `MaxCitiesPerCiv`)

**Kept intact:**
- All population dynamics (growth, disease, starvation, crystallization, abandonment)
- War, raid, diplomacy mechanics (territory tiles transfer on conquest)
- Character needs, personality, skills, relationships, goals (non-expansion)
- Event logging and history graph
- Tier2 specialist crystallization

**New systems:**
- `TerritoryMap`: `Dictionary<TileCoord, TileCoord>` — tile → owning city tile (on WorldState)
- `ImprovementMap`: `Dictionary<TileCoord, TileImprovement>` (on WorldState)
- `TerritoryPhase`: annual city-driven territory expansion/contraction
- Ruler delegation: annual pass where rulers decide to send a city-founding mission
- `BuildImprovement` command + goal
- `FoundCity` mandate goal (replaces Colonize)
- ResourcePressurePhase port: iterate territory tiles instead of ReachRadius circle

---

## Data Structures

### TerritoryMap (WorldState)

```csharp
// Key: any tile coord. Value: the city tile that owns it.
// A tile not in this dict is unclaimed.
public Dictionary<TileCoord, TileCoord> TerritoryMap { get; } = new();
```

City tiles own themselves (`TerritoryMap[cityTile] = cityTile`).
When a city is destroyed, its territory tiles are removed (revert to unclaimed).
When a city is conquered, all tiles with `value == oldCityTile` update to new owner's city tile.

### ImprovementMap (WorldState)

```csharp
public Dictionary<TileCoord, TileImprovement> ImprovementMap { get; } = new();
```

```csharp
public enum ImprovementType { Farm, Mine, LoggingCamp, Pasture, Fishery }

public sealed record TileImprovement(
    ImprovementType Type,
    TileCoord       CityTile,   // which city built/owns this
    int             BuiltYear,
    EntityId        BuilderId); // character who built it (for event attribution)
```

One improvement per tile. A second slot is a future extension.

### SettlementStub additions

```csharp
// Replace ReachRadius()-based hinterland with an explicit tile set.
// Stored as a HashSet on the mutable Civilization, not on the immutable record stub,
// to avoid record-copy cost every time the set changes.
```

Territory is stored **on Civilization** (not SettlementStub) as:

```csharp
// In Civilization.cs:
public Dictionary<TileCoord, HashSet<TileCoord>> CityTerritories { get; } = new();
// Key: city tile. Value: set of tiles that city owns (including itself).
```

This keeps SettlementStub a plain data record and avoids copying large sets every tick.

---

## Epic 3.0.1 — TerritoryMap + claim at founding

**Stories:**
1. Add `TerritoryMap` dict to WorldState and IWorldStateReadOnly
2. Add `CityTerritories` dict to Civilization
3. On `EstablishSettlement` resolution: claim the N highest-fertility unclaimed tiles
   within radius 2 around the new city tile (capped by `InitialCityClaimRadius = 2`).
   Write these to `TerritoryMap` and `Civilization.CityTerritories[cityTile]`.
4. On `AbandonSettlement` / city destruction: release all territory tiles (remove from
   TerritoryMap and CityTerritories). Fire `TerritoryLost` event.
5. On war conquest (CivTracker.War.cs): reassign territory tiles to conquering civ's
   nearest city. Update both TerritoryMap and both Civilization.CityTerritories dicts.
6. Add `TerritoryExpanded` and `TerritoryLost` EventType entries.

**Done when:** A newly founded city claims its starting territory; abandoned/conquered
cities release or transfer tiles correctly. Tests verify tile ownership round-trips.

---

## Epic 3.0.2 — TerritoryPhase (annual expansion/contraction)

Annual pass (runs in PhaseRunner once per year, Spring tick):

**Expansion:** For each live city, compute `maxTiles = clamp(Population / ClaimTilesPerPerson, MinCityTiles, MaxCityTiles)`.
If `ownedTiles.Count < maxTiles`, find the highest-fertility unclaimed tile adjacent to
any owned tile and claim it. At most `TerritoryGrowthPerYear = 2` tiles per city per year.

**Contraction:** If `ownedTiles.Count > maxTiles` (population dropped), remove the
`ownedTiles.Count - maxTiles` tiles with the greatest distance from the city center.
Fire `TerritoryLost` events for each released tile.

**Config additions** (`sim_config.toml`):
```toml
[territory]
claim_tiles_per_person   = 8     # 1 tile per 8 people; city of 800 → 100 tiles (~radius 5)
min_city_tiles           = 7     # radius-1 circle, always retained
max_city_tiles           = 120   # ~radius-6; absolute upper bound
territory_growth_per_year = 2    # max tiles claimed per city per year (prevents instant snowball)
initial_city_claim_radius = 2    # tiles claimed at founding (13 tiles)
```

**Done when:** Over a 200-year test run, city territories grow organically; a city
halved in population visibly releases its outer ring. `TerritoryMap` stays consistent.

---

## Epic 3.0.3 — Port ResourcePressure to territory

Replace the `stub.ReachRadius()` iteration in `ResourcePressurePhase.BuildLedger()` with
iteration over `Civilization.CityTerritories[cityTile]`.

Apply improvement multipliers per tile:
- `Farm` on tile: food contribution × `FarmFoodMultiplier = 2.0`
- `Mine` on tile: mineral yield × `MineYieldMultiplier = 3.0`
- `LoggingCamp`: timber yield × `LoggingYieldMultiplier = 2.5`
- `Pasture`: food × `PastureMultiplier = 1.5` (grassland/savanna only)
- `Fishery`: food × `FisheryMultiplier = 2.0` (coastal/river only)

Remove `ReachRadius()` from SettlementStub after ResourcePressure and
PopulationDynamicsPhase both port successfully.

**Config additions:**
```toml
[improvements]
farm_food_multiplier    = 2.0
mine_yield_multiplier   = 3.0
logging_yield_multiplier = 2.5
pasture_multiplier      = 1.5
fishery_multiplier      = 2.0
```

**Done when:** ResourcePressure reads territory tiles; carrying capacity tracks
owned tile count × biome cap (same formula, different iteration source). Existing
growth/starvation tests still pass.

---

## Epic 3.0.4 — ImprovementMap + BuildImprovement

**New command:**
```csharp
public sealed record BuildImprovement(
    EntityId      CharacterId,
    TileCoord     TargetTile,
    ImprovementType ImprovementType) : ICommand;
```

**New goal type:** `GoalType.BuildImprovement` — seeded when:
- A ruler "delegates" a build task (ruler has surplus food/timber AND tile is in own territory AND no improvement there yet)
- A character with high Ingenuity/Diligence forms it spontaneously while on an owned unimproved tile

**UtilityScorer:** `BuildImprovement` action available when character is ON an owned territory tile with no existing improvement and character has `BuildImprovement` goal. Score driven by Diligence aptitude + Purpose need.

**Resolution (CivTracker or new ImprovementResolver):**
- Character must be on target tile for `ImprovementBuildTicks` ticks (= 8; half a year)
- On completion: write to `ImprovementMap`, fire `ImprovementBuilt` event with builder attribution

**Done when:** A high-Diligence character travels to an unimproved city territory tile,
spends 8 ticks there, and an improvement appears. Event fires. ResourcePressure picks
it up next tick.

---

## Epic 3.0.5 — Ruler city-founding delegation

Annual pass in `RunAnnualDiplomacy` (or a new `RunCityExpansionDecisions` call):

**Decision logic per ruler:**

```
shouldFoundCity =
    civ.CityCount < MaxCitiesPerCiv                         // room for more cities
    AND city.Population > CityFoundingMinPop                // large enough to spare settlers
    AND city.FoodPressureRatio > CityFoundingFoodThreshold  // surplus food
    AND roll < FoundingProbability(ruler)
```

Where:
```
FoundingProbability(ruler) =
    ruler.Personality.Aggression * AggressionFoundingWeight
    + (ruler has Expansion goal ? ExpansionGoalBonus : 0)
    + ruler.Personality.Ambition * AmbitionFoundingWeight
```

**Delegation:** If decision fires, ruler picks the highest-Ambition living civ member
who is not already a founder and not already on a `FoundCity` mandate. Gives them:
```csharp
founder.Goals.Add(new GoalData { Type = GoalType.FoundCity, Priority = 1.0f, ... });
```

**FoundCity goal behaviour (UtilityScorer):**
- Travel scoring: colonize-style frontier bonus (tile far from all same-civ cities)
- EstablishSettlement action: available and scores high when on a distant fertile unclaimed tile
- On founding: new city enters world; ruler's delegation is fulfilled; `CityFounded` event fires

**Config additions:**
```toml
[character]
max_cities_per_civ           = 4
city_founding_min_pop        = 800   # ruler's city must be this large before considering
city_founding_food_threshold = 0.65  # food ratio floor (surplus)
aggression_founding_weight   = 0.25
expansion_goal_bonus         = 0.20
ambition_founding_weight     = 0.30
city_founding_base_chance    = 0.05  # annual base probability when all thresholds met
```

**Done when:** Over a 500-year run, large civs found 1–3 additional cities. Each
founding is traceable to a specific ruler decision and delegate character.

---

## Epic 3.0.6 — Retire expansion blob + UtilityScorer cleanup

- Remove `GoalType.Expansion` and `GoalType.Colonize` from `GoalManager` and `Enumerations.cs`
- Remove `EstablishSettlement` from `BuildCandidates` for non-FoundCity characters
- Remove hinterland cache, compactness cache, frontier cache from UtilityScorer
- Remove `HinterlandFactor` method
- Remove `IsColony` from SettlementStub
- Add `BuildImprovement` to `BuildCandidates` scoring
- Update GoalAdvancement table: remove Expansion/Colonize entries; add BuildImprovement

**Done when:** Zero compilation warnings; old expansion mechanic is fully gone;
`UtilityScorer` has no settlement proximity caches; civ-born characters form
`BuildImprovement` goals instead of Expansion goals.

---

## Epic 3.0.7 — Snapshot propagation for UI

**Goal:** Territory and improvement data reaches the UI via `WorldSnapshot` so Phase
3.4 (tile inspect, territory overlay) can read it without touching WorldState directly.

### Stories

**3.0.7.1 — TerritorySnapshot on WorldSnapshot**

Add to `WorldSnapshot`:
```csharp
public IReadOnlyDictionary<TileCoord, (CivId CivId, string CivName, TileCoord CityTile)>
    TerritorySnapshot { get; init; }
```

`SnapshotBuilder` copies from `world.TerritoryMap`, joining civ name via
`world.Civilizations`. Use a pre-allocated dict that is reused each tick to avoid
GC pressure.

**3.0.7.2 — ImprovementSnapshot on WorldSnapshot**

```csharp
public IReadOnlyDictionary<TileCoord, TileImprovement> ImprovementSnapshot { get; init; }
```

Direct copy of `world.ImprovementMap` — already immutable records, safe to share
across threads.

**3.0.7.3 — TileInspectorData extensions**

`SnapshotBuilder.BuildTileInspectorData()` populates the new territory/improvement
fields on `TileInspectorData` that Phase 3.4.2 will display.

---

## Implementation Order

1. **3.0.1** (data structures + founding claim) — no behaviour change yet
2. **3.0.2** (territory phase) — cities start growing/contracting
3. **3.0.3** (ResourcePressure port) — economics now territory-based
4. **3.0.4** (improvements) — tile improvements appear and boost output
5. **3.0.5** (ruler delegation) — new cities founded by ruler decision
6. **3.0.6** (retire expansion blob) — old mechanic fully removed
7. **3.0.7** (snapshot propagation) — territory/improvement visible to UI

Do 3.0.1–3.0.3 together before removing expansion; expansion and territory can coexist
briefly so tests stay green throughout.

---

## Spotlight tile content (Milestone 3 preview)

After this phase, a tile at coord `(x, y)` carries:
- `TileData`: biome, fertility, moisture, temp, elevation — terrain feel
- `TerritoryMap[(x,y)]` → city tile → civ name, ancestry, culture
- `ImprovementMap[(x,y)]` → improvement type → what structures are present
- `ResourceDeposits[(x,y)]` → what can be mined/found
- `EntityRegistry` at `(x,y)` → characters living here or passing through
- Event history at `(x,y)` (SQLite query) → what happened here

This is sufficient for the M3 spotlight to procedurally assemble: who lives here,
what they do, what the land looks like, what polity governs them, what happened here.
