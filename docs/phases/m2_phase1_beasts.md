# Phase 2.1 — Legendary Beasts

**Milestone:** 2 — The Character System  
**Status:** IN PROGRESS  
**Goal:** Introduce the first entities into the simulation. Beasts are intentionally simple: needs, territory, and survival behaviours only. No politics, no goals beyond survival. This phase validates the entity model, EntityRegistry, RelationshipGraph, and character event types in complete isolation before any human character complexity is added.

**Companion docs:**
- `docs/implementation_decisions_v0.3.md` §9 (Entity Model), §16 (Character Decision-Making), §17 (Trait System)
- `docs/interface_contracts.md` — IEntity contract
- `docs/snippets/patterns.md` — Command pattern, WorldRng

---

## What a Legendary Beast Is

- A named, unique entity (e.g. "The Pale Serpent of Ashenveil")
- Has a home tile (territory centre) and a wander radius
- Has a `NeedsVector` subset: Safety, Food only (no Belonging/Status/Purpose/Spiritual)
- Has a `PersonalityVector` subset: Aggression, Curiosity only (drives roam vs rest decisions)
- Has Health (can be injured and die)
- Has a species type (`BeastSpecies` enum) that sets baseline stats
- Can reproduce rarely (produce a child beast with inherited traits + noise)
- Dies of age, starvation, or combat

Beasts do NOT: hold territory politically, form goals, have relationships beyond "encountered" or "attacked", or interact with settlements.

---

## Stories

### Story 2.1.1 — IEntity interface and EntityRegistry

**What:** Define `IEntity`, `EntityId`, `EntityKind`, and `EntityRegistry`. Wire `EntityRegistry` into `WorldState`. Add `EntityRegistry` to `WorldSnapshot` / `TileDisplayData` so the UI can see entity locations.

**IEntity contract:**
```csharp
public interface IEntity
{
    EntityId Id { get; }
    EntityKind Kind { get; }
    TileCoord Location { get; }
    bool IsAlive { get; }
    IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);
}
```

**EntityId:** `readonly record struct EntityId(int Value)`. Auto-incrementing counter on `EntityRegistry`, starting at 1.

**EntityKind enum:** `LegendaryBeast, Tier1Character, Tier2Character, Settlement` (all four defined now even though only Beast is implemented).

**EntityRegistry:**
- `Dictionary<EntityId, IEntity>` — canonical store
- `Dictionary<TileCoord, HashSet<EntityId>>` — spatial index (updated on move)
- `List<LegendaryBeast>` — typed fast-iteration list
- `Add(IEntity)`, `Remove(EntityId)`, `GetAt(TileCoord)`, `GetAllOfKind<T>()`

**TileDisplayData:** add `EntityId[] EntitiesPresent` (empty array if none; avoids null).

**WorldSnapshot:** snapshot builder iterates `EntityRegistry` and populates per-tile entity lists.

**Tests:**
- Registry add/remove/spatial index stays consistent
- Snapshot captures entity locations

---

### Story 2.1.2 — LegendaryBeast entity

**What:** Implement `LegendaryBeast` as a concrete `IEntity`. Implement `BeastSpecies` enum and baseline stat table in `SimConfig`.

**LegendaryBeast fields:**
```csharp
public sealed class LegendaryBeast : IEntity
{
    public EntityId Id { get; }
    public EntityKind Kind => EntityKind.LegendaryBeast;
    public TileCoord Location { get; private set; }
    public TileCoord HomeTile { get; }          // territory centre
    public bool IsAlive { get; private set; } = true;

    public BeastSpecies Species { get; }
    public string Name { get; }                  // generated at spawn

    // Component data
    public NeedsVector Needs;       // Safety, Food only (others always 1.0)
    public PersonalityVector Personality; // Aggression, Curiosity only
    public HealthData Health;

    public int Age { get; private set; }         // in seasons
    public int MaxAge { get; }                   // from species config + noise
}
```

**BeastSpecies enum:** `Serpent, Drake, Wurm, Colossus, Phantom` — five types. Stats in `SimConfig.Beasts.Species[BeastSpecies]`.

**SimConfig additions** (`[beasts]` section):
```toml
[beasts]
spawn_count           = 8      # Beasts alive at world start
wander_radius_tiles   = 15     # Max tiles from home tile per season
food_depletion_rate   = 0.15   # Needs.Food depletes this much per season
food_from_fertile_tile = 0.4   # Grazing: Food restored per season on fertile tile
starvation_health_loss = 0.2   # Health lost per season when Food < 0.2
old_age_min_seasons   = 80
old_age_max_seasons   = 200
reproduction_min_age  = 20
reproduction_food_threshold = 0.7
reproduction_chance   = 0.04   # Per season when conditions met
```

**Tests:**
- Beast construction sets correct species stats
- Age increments each season tick

---

### Story 2.1.3 — Beast spawning at world start

**What:** Spawn `N` beasts during world init (after world gen completes, before first sim tick). Beasts are placed on valid land tiles with biome appropriate to their species. Beast names are procedurally generated from a seeded word list.

**Spawning logic** (in `WorldInitializer` or similar, called from `SimLoop` init path):
1. Collect candidate tiles: land, not ocean, not HighMountain, fertility ≥ threshold
2. Filter by species biome affinity (Serpent prefers coastal/swamp, Drake prefers mountain, etc.)
3. Pick N tiles with minimum separation (Poisson-disc style, reuse `WorldRng`)
4. Generate beast via `BeastFactory.Spawn(species, tile, seed)`
5. Register in `EntityRegistry`, add to spatial index

**Name generation:** Two-part: adjective + noun from small seeded word lists. `WorldRng.IntAt(seed, 0, entitySeq, 0, SaltName) % wordList.Length`. Deterministic from seed.

**Tests:**
- Spawned beasts are on valid tiles
- Same seed produces same beasts at same locations

---

### Story 2.1.4 — Beast behaviour (EmitCommands)

**What:** Implement `LegendaryBeast.EmitCommands()` producing `MoveToTile`, `Rest`, `Graze`, `Attack` commands. Simple priority-based selection, no utility scoring needed (beasts are not complex enough).

**Behaviour priority (checked in order):**
1. **Safety threat** — if another entity with `IsAggressive` is on same or adjacent tile: emit `Flee` (move away from threat, toward home if possible)
2. **Starving** — if `Needs.Food < 0.2`: move toward highest-fertility adjacent tile
3. **Graze** — if on fertile tile and `Needs.Food < 0.8`: emit `Graze` (restore food)
4. **Wander** — if within wander radius: pick random adjacent tile weighted by biome affinity, emit `MoveToTile`
5. **Return** — if outside wander radius: emit `MoveToTile` toward home tile
6. **Rest** — otherwise: emit `Rest`

**Commands needed** (add to `PlayerCommands.cs` or a new `EntityCommands.cs`):
```csharp
public sealed record MoveToTile(EntityId EntityId, TileCoord Destination) : ICommand;
public sealed record Graze(EntityId EntityId) : ICommand;
public sealed record Rest(EntityId EntityId) : ICommand;
public sealed record Attack(EntityId Attacker, EntityId Target) : ICommand;
public sealed record Flee(EntityId EntityId, TileCoord AwayFrom) : ICommand;
```

**CommandResolver additions:** Resolve each command. `MoveToTile` → update `Location`, update spatial index. `Graze` → increment `Needs.Food`. `Attack` → reduce target `Health`. `Flee` → move to safest adjacent tile.

**Tests:**
- Beast on fertile tile with low food emits Graze
- Beast outside wander radius emits MoveToTile toward home
- MoveToTile resolution updates spatial index correctly

---

### Story 2.1.5 — Beast needs and health resolution

**What:** Implement the seasonal needs update and health consequences. Runs in `EnvironmentalPhase` (or a new `EntityPhase` — see decision below).

**// DECISION:** Beast needs are updated in a new `EntityPhase` that runs after `EnvironmentalPhase`. This keeps entity logic out of the environmental update and sets up the slot for all future character phases.

**Each season tick per living beast:**
1. `Needs.Food -= cfg.FoodDepletionRate`
2. If on fertile tile and emitted Graze last tick: `Needs.Food += cfg.FoodFromFertileTile`
3. If `Needs.Food < 0.2`: `Health.Value -= cfg.StarvationHealthLoss`
4. `Age += 1`
5. If `Age >= MaxAge`: mark dead, emit `BeastDied` event
6. If `Health.Value <= 0`: mark dead, emit `BeastDied` event
7. Reproduction check: if `Age >= ReproductionMinAge && Needs.Food >= FoodThreshold && WorldRng < ReproductionChance`: spawn child beast near home tile, emit `BeastReproduced` event

**Removal:** Dead beasts are removed from `EntityRegistry` at end of tick (after all commands resolved, before snapshot).

**Tests:**
- Beast with low food loses health each season
- Beast dies when health reaches zero; removed from registry
- Reproduction only fires when conditions met

---

### Story 2.1.6 — Beast event types

**What:** Add beast-related `EventType` values and emit them from `EntityPhase`.

**New EventType values:**
- `BeastSpawned` — emitted at world init for each beast
- `BeastDied` — natural death (age or starvation); payload includes `{BeastId, Name, Species, Cause, Age}`
- `BeastSlain` — killed by another entity; payload includes attacker
- `BeastReproduced` — payload: `{ParentId, ChildId, ChildName}`
- `BeastEncounter` — two beasts on same tile (informational, Background tier)
- `BeastMigrated` — beast home tile shifted (if we implement home drift)

**VerbClass assignments:**
- `BeastDied/Slain` → `Death`
- `BeastReproduced` → `Creation`
- `BeastEncounter` → `Interaction`
- `BeastSpawned` → `Creation`

**Tier defaults:**
- `BeastSpawned` → Background
- `BeastDied/Slain` → Regional (notable named entity dies)
- `BeastReproduced` → Background

**Tests:**
- BeastDied event is recorded to DB with correct payload
- Tier classification matches spec

---

### Story 2.1.7 — Beast display in UI

**What:** Show beast locations on the tile map. Show beast info in tile inspector when a beast is present.

**TileMapRenderer:** After drawing tile biome color, draw a small symbol (a 4×4 or 6×6 pixel colored square) for each entity on the tile. Beast = dark red square. Position it slightly offset from tile center to avoid covering the biome color entirely.

**TileInspectorPanel:** When `snapshot.InspectedTile` has entities, add a section below terrain info:
```
Entities (1):
  The Pale Serpent [Serpent, Age 34, Health 0.82, Food 0.61]
```
Use existing `TileDisplayData.EntitiesPresent` array (added in 2.1.1). The inspector reads entity data from the snapshot.

**WorldSnapshot additions:** `EntitySnapshot` record per entity: `(EntityId Id, EntityKind Kind, string Name, TileCoord Location, float Health, float Food, int Age)`. Snapshot builder populates these from `EntityRegistry`.

**Tests:** No new tests needed here — this is UI-only. Manual testing: beasts visible on map, inspector shows correct info.

---

## Definition of Done

- [ ] `IEntity`, `EntityId`, `EntityKind`, `EntityRegistry` implemented and tested
- [ ] `LegendaryBeast` with `NeedsVector`, `PersonalityVector`, `HealthData` implemented
- [ ] Beast spawning at world init — deterministic from seed
- [ ] `EntityPhase` added to sim loop, processes beasts each tick
- [ ] Beast commands: `MoveToTile`, `Graze`, `Rest`, `Attack`, `Flee` implemented and resolved
- [ ] Needs/health/age/death/reproduction resolved each season
- [ ] Beast event types added; `BeastDied`, `BeastSlain`, `BeastReproduced` recorded to DB
- [ ] Beast locations visible on tile map
- [ ] Tile inspector shows beast info
- [ ] All new `SimConfig` values in `sim_config.toml`
- [ ] All tests pass, zero warnings
- [ ] Reproducibility test: same seed → same beast names, locations, deaths

---

## Open Questions (resolve during implementation)

1. **Beast–beast combat:** When two beasts occupy the same tile and one is aggressive, does combat resolve immediately or does each emit `Attack` and the resolver handles contention? → Prefer the command/resolver pattern for consistency with the rest of the sim.

2. **Beast–tile damage:** Should aggressive beasts reduce tile fertility slightly? → Defer to M3; mark with `// V2: beast terrain damage`.

3. **Beast home tile drift:** Should a beast's home tile shift slowly toward where it feeds? → Not for 2.1; too much complexity for the validation phase.
