# Phase 2.1 — Legendary Beasts

**Milestone:** 2 — The Character System  
**Status:** COMPLETE — 2026-06-22  
**Goal:** Introduce the first entities into the simulation. Beasts are intentionally simple — needs, territory, and survival behaviour only. No politics, no goals beyond survival. This phase validates the entity model, EntityRegistry, command/resolver pipeline, and beast event types in complete isolation before any human character complexity is added.

**Companion docs:**
- `docs/design/beast_design.md` — full beast catalog, stat block schema, DS-E1 through DS-E8
- `config/beasts.toml` — all species definitions (26 normal predators, 18 mythological)
- `docs/implementation_decisions_v0.3.md` §9 (Entity Model), §16 (Character Decision-Making)
- `docs/interface_contracts.md` v0.4 — IEntity, EntitySnapshot, EntityId, VerbClass
- `docs/snippets/patterns.md` — Command pattern, WorldRng

---

## What a Beast Is

Every beast — whether a mundane wolf pack or a Dragon — is a `LegendaryBeast` instance backed by a species entry in `config/beasts.toml`. "Legendary" in the class name refers to the entity tier (named, tracked, historically significant), not always that it is a legendary specimen of its type.

Two kinds of beast (see DS-E1 in `docs/design/beast_design.md`):
- **Normal predator** — species `category = "predator"`. Common specimens form packs. A rare one gets `IsLegendary = true` with boosted stats and a unique name.
- **Mythological** — species `category = "mythological"`. Always rare and named. Stats are always legendary-quality. 20% present at world start; 80% emerge as `BeastAwakened` events.

All beasts:
- Are named entities in `EntityRegistry`
- Have a home tile (territory centre) and wander radius
- Have `NeedsVector` (Food + Safety only; other needs always 1.0)
- Have `HealthData` (can be injured and die)
- Die from old age, starvation, or combat
- Emit commands; state is mutated only by `CommandResolver`

Beasts do NOT: hold territory politically, form goals, interact with settlements (M2.4+), or use ability mechanics beyond simple tags (M2.2+).

---

## Stories

### Story 2.1.1 — IEntity, EntityId, and EntityRegistry

**What:** Define `IEntity`, `EntityId`, `EntityKind`, and `EntityRegistry`. Wire registry into `WorldState`. Update `WorldSnapshot` and `TileDisplayData` to carry entity data (per interface_contracts.md v0.4).

**EntityId** (already specced in interface_contracts.md):
```csharp
public readonly record struct EntityId(long Value)
{
    public static EntityId New() => new(IdGenerator.Next());
}
```

**IEntity** (already in interface_contracts.md — implement exactly):
```csharp
public interface IEntity
{
    EntityId Id { get; }
    TileCoord Location { get; }
    EntityKind Kind { get; }
    bool IsAlive { get; }
    IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);
    EntitySnapshot ToSnapshot();
}
```

**EntityRegistry:**
```csharp
public sealed class EntityRegistry
{
    private readonly Dictionary<EntityId, IEntity> _all = new();
    private readonly Dictionary<TileCoord, HashSet<EntityId>> _spatial = new();
    private readonly List<LegendaryBeast> _beasts = new();

    public void Add(IEntity entity);
    public void Remove(EntityId id);
    public IEntity? Get(EntityId id);
    public IEnumerable<IEntity> GetAt(TileCoord coord);
    public IEnumerable<IEntity> GetInRadius(TileCoord center, int radius);
    public IReadOnlyList<LegendaryBeast> Beasts => _beasts;
    public IReadOnlyDictionary<EntityId, IEntity> All => _all;

    public void UpdateLocation(EntityId id, TileCoord oldCoord, TileCoord newCoord);
}
```
`UpdateLocation` keeps the spatial index consistent on every move.

**TileDisplayData** — add `EntityId[] EntitiesPresent` (per interface_contracts.md v0.4). `SnapshotBuilder` populates it from the spatial index.

**WorldSnapshot** — add `IReadOnlyDictionary<EntityId, EntitySnapshot> EntitySnapshots`. `SnapshotBuilder` calls `entity.ToSnapshot()` for every live entity.

**IWorldStateReadOnly** — implement the three entity lookup methods now live in v0.4:
```csharp
IEntity? GetEntity(EntityId id);
IEnumerable<IEntity> GetEntitiesAt(TileCoord coord);
IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius);
```

**Tests:**
- Registry add/remove keeps spatial index consistent
- `UpdateLocation` moves entity between spatial buckets correctly
- Snapshot contains entity for each live entity in registry

---

### Story 2.1.2 — BeastCatalog and LegendaryBeast entity

**What:** Load `config/beasts.toml` into `BeastCatalog`. Implement `LegendaryBeast` as a concrete `IEntity` driven by catalog data, not a hardcoded enum.

**BeastSpeciesConfig** (one entry per `[[beasts]]` block in beasts.toml):
```csharp
public sealed class BeastSpeciesConfig
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";   // "predator" | "mythological"
    public string[] Biomes { get; init; } = [];
    public int MaxPerWorld { get; init; }
    public int PackSizeMin { get; init; }
    public int PackSizeMax { get; init; }
    public bool PrefersCompany { get; init; }
    public bool Hibernates { get; init; }
    public string[] Abilities { get; init; } = [];

    public int Health { get; init; }
    public int Strength { get; init; }
    public int Speed { get; init; }
    public float Aggression { get; init; }
    public int TerritoryRadius { get; init; }
    public float FoodDepletion { get; init; }
    public float FoodFromHunt { get; init; }
    public float FoodFromGraze { get; init; }
    public int AgeMinSeasons { get; init; }
    public int AgeMaxSeasons { get; init; }
    public int ReproductionMinAge { get; init; }
    public float ReproductionFoodThreshold { get; init; }
    public float ReproductionChance { get; init; }

    // Predator legendary variant (ignored for mythological category)
    public float LegendaryChance { get; init; }
    public float LegendaryHealthMult { get; init; }
    public float LegendaryStrengthMult { get; init; }
    public float LegendaryAgeMult { get; init; }
    public float LegendaryTerritoryMult { get; init; }
    public string[] LegendaryNameAdjectives { get; init; } = [];
    public string[] LegendaryNameNouns { get; init; } = [];

    // Mythological creature names (replaces legendary_ prefix for this category)
    public string[] NameAdjectives { get; init; } = [];
    public string[] NameNouns { get; init; } = [];
}
```

**BeastCatalog** — loaded once at startup, injected into `BeastFactory`:
```csharp
public sealed class BeastCatalog
{
    public IReadOnlyList<BeastSpeciesConfig> AllSpecies { get; }
    public BeastSpeciesConfig? Get(string id);
    public IEnumerable<BeastSpeciesConfig> ByCategory(string category);
    public IEnumerable<BeastSpeciesConfig> ByBiome(string biome);

    public static BeastCatalog Load(string tomlPath);
}
```

**LegendaryBeast:**
```csharp
public sealed class LegendaryBeast : IEntity
{
    public EntityId Id { get; }
    public EntityKind Kind => EntityKind.LegendaryBeast;
    public TileCoord Location { get; internal set; }
    public TileCoord HomeTile { get; }
    public bool IsAlive { get; internal set; } = true;

    public string SpeciesId { get; }          // matches BeastSpeciesConfig.Id
    public string Name { get; }               // generated at spawn
    public bool IsLegendary { get; }          // legendary specimen or mythological

    public int MaxHealth { get; }
    public int Health { get; internal set; }
    public int Strength { get; }
    public int Speed { get; }
    public float Aggression { get; }
    public int TerritoryRadius { get; }
    public string[] Abilities { get; }        // tags — no mechanical effect in M2.1

    public float FoodNeed { get; internal set; }    // 0.0–1.0
    public float SafetyNeed { get; internal set; }  // 0.0–1.0
    public int AgeSeason { get; internal set; }
    public int MaxAgeSeason { get; }

    // Derived from species config — carried for hot-path access
    public float FoodDepletion { get; }
    public float FoodFromHunt { get; }
    public float FoodFromGraze { get; }
    public float ReproductionChance { get; }
    public int ReproductionMinAge { get; }
    public float ReproductionFoodThreshold { get; }
    public bool Hibernates { get; }

    public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);
    public EntitySnapshot ToSnapshot();
}
```

**Tests:**
- `BeastCatalog.Load` parses wolf entry correctly
- Constructed `LegendaryBeast` carries stats from species config

---

### Story 2.1.3 — Beast spawning at world start

**What:** Spawn beasts after world gen completes, before the first sim tick, using `BeastFactory` + `BeastSpawner`.

**Spawn budget** from `beasts.toml` `[beast_spawn]`:
- `target_density_per_10k_tiles` → total beast count scales with world size
- `myth_start_fraction` = 0.20 → 20% of mythological creatures present at year 0; others scheduled via `BeastEmergenceSchedule`

**BeastFactory.Spawn:**
```csharp
public static LegendaryBeast Spawn(
    BeastSpeciesConfig species,
    TileCoord home,
    bool isLegendary,
    WorldRng rng,
    long entitySeq)
```
1. Roll `isLegendary` check if `category == "predator"`: `rng.FloatAt(worldSeed, 0, entitySeq, 0, SaltLegendary) < species.LegendaryChance`
2. Apply legendary multipliers to health, strength, age, territory radius if legendary
3. Generate name: pick adjective-form or noun-form at 50/50 if both lists non-empty; prepend "The"
4. `FoodNeed` starts at `0.8f`, `SafetyNeed` starts at `1.0f`

**BeastSpawner.SpawnAll** (called from `WorldInitializer`):
1. For each predator species: collect biome-matching land tiles → Poisson-disc select N spawn points (N ≈ `maxPerWorld × startFraction`, min 1) → call `BeastFactory.Spawn` per beast, register in `EntityRegistry`
2. For mythological species: roll spawn count from `myth_start_fraction` (floor/ceil); remaining go into `BeastEmergenceSchedule` — a `List<(int EmergenceYear, string SpeciesId)>` stored on `WorldState`, year seeded deterministically
3. Emit `BeastSpawned` event for each beast placed at world start; emit `BeastScheduledEmergence` (Background, not shown in event log) for each deferred one

**BeastEmergenceSchedule** processed each annual tick in `EntityBehaviorPhase`: if `CurrentYear >= entry.EmergenceYear`, spawn the beast and emit `BeastAwakened` (Regional tier).

**Tests:**
- Same seed produces same beast positions
- Spawned beasts are on valid biome-matching tiles
- Mythological count at spawn respects `myth_start_fraction`

---

### Story 2.1.4 — Beast behaviour (EmitCommands)

**What:** Implement `LegendaryBeast.EmitCommands()`. Priority-based, not utility-scored — beasts are not complex enough to need softmax.

**Entity commands** (new file `WorldEngine.Sim/Entities/EntityCommands.cs`):
```csharp
public sealed record MoveToTile(EntityId EntityId, TileCoord Destination) : ICommand;
public sealed record Graze(EntityId EntityId) : ICommand;
public sealed record Rest(EntityId EntityId) : ICommand;
public sealed record Attack(EntityId Attacker, EntityId Target) : ICommand;
public sealed record Flee(EntityId EntityId, TileCoord AwayFrom) : ICommand;
```

**Behaviour priority (checked top to bottom, first match wins):**
1. **Hibernation check** — if `Hibernates && world.CurrentSeason == Season.Winter`: emit `Rest`, done
2. **Safety threat** — if any entity with `IsAggressive(e)` is on the same or adjacent tile and is not same species: emit `Flee(this.Id, threatCoord)`
3. **Starving** — if `FoodNeed < 0.2f`: move toward highest-fertility adjacent non-ocean tile
4. **Hunt opportunity** — if `FoodNeed < 0.7f` and `Aggression > 0.4f` and a huntable entity (small beast, no `IsAlive` check needed — resolver handles) is adjacent: emit `Attack(this.Id, target.Id)`
5. **Graze** — if on tile with `Fertility > 80` and `FoodFromGraze > 0` and `FoodNeed < 0.8f`: emit `Graze(this.Id)`
6. **Wander** — if within `TerritoryRadius` of `HomeTile`: pick weighted-random adjacent land tile (weight = biome affinity from species config), emit `MoveToTile`
7. **Return home** — if outside `TerritoryRadius`: emit `MoveToTile` toward `HomeTile` (step along direction vector)
8. **Rest** — default

`IsAggressive(IEntity e)` helper: returns `true` if `e` is a `LegendaryBeast` with `Aggression > 0.4f`. In M2.2+ this will cover characters too.

**Tests:**
- Beast on fertile tile with `FoodNeed < 0.8` and `FoodFromGraze > 0` emits `Graze`
- Beast outside territory radius emits `MoveToTile` toward home
- Hibernating beast in winter emits `Rest` unconditionally

---

### Story 2.1.5 — Combat resolution (CommandResolver additions)

**What:** Resolve `Attack`, `MoveToTile`, `Graze`, `Rest`, `Flee` commands. Combat is a **multi-round sub-process within a single tick** (DS-E5).

**MoveToTile:** Update `Location`, call `EntityRegistry.UpdateLocation`.

**Graze:** `FoodNeed = Math.Min(1.0f, FoodNeed + species.FoodFromGraze)`.

**Rest:** No state change. Placeholder for future fatigue system.

**Flee:** Find adjacent tile furthest from `AwayFrom`; emit `MoveToTile` to that tile (resolver processes moves in order, so this effectively chains).

**Attack — multi-round combat loop:**
```csharp
// Collect all Attack commands targeting the same pair this tick
// Run up to CombatMaxRoundsPerTick rounds:
for (int round = 0; round < cfg.CombatMaxRoundsPerTick && attacker.IsAlive && target.IsAlive; round++)
{
    // Attacker side: up to CombatMaxGangSize pack members attack target
    foreach (var gangMember in GetGangMembers(attacker, cfg.CombatMaxGangSize))
    {
        float atkRoll = gangMember.Strength * world.GetRandomFloat(gangMember.Id, round * 10);
        float defRoll = (target.Health / (float)target.MaxHealth) * world.GetRandomFloat(target.Id, round * 10 + 1);
        if (atkRoll > defRoll)
            target.Health -= gangMember.Strength;
    }
    if (target.Health <= 0) { KillEntity(target, attacker, pendingEvents); break; }

    // Target retaliates against one random gang member
    var retaliationTarget = gangMembers[rng.Next(gangMembers.Count)];
    float retRoll = target.Strength * world.GetRandomFloat(target.Id, round * 10 + 2);
    float retDefRoll = (retaliationTarget.Health / (float)retaliationTarget.MaxHealth)
                       * world.GetRandomFloat(retaliationTarget.Id, round * 10 + 3);
    if (retRoll > retDefRoll)
        retaliationTarget.Health -= target.Strength;
    if (retaliationTarget.Health <= 0) KillEntity(retaliationTarget, target, pendingEvents);

    // Retreat check after each round
    if (target.Health < target.MaxHealth * cfg.RetreatHealthFraction)
    {
        RetreatEntity(target, attacker.Location, world);
        break;
    }
}
```
`GetGangMembers(attacker, max)` — returns all `LegendaryBeast` on the same tile as `attacker` with the same `SpeciesId`, up to `max` count, sorted by `Health` descending.

`KillEntity` marks `IsAlive = false`, adds to removal list, queues `BeastSlain` PendingEvent.

**Tests:**
- Two beasts of equal strength: one dies or retreats within `CombatMaxRoundsPerTick` rounds
- Pack of 10 wolves vs bear: at most 3 wolves attack per round (`CombatMaxGangSize = 3`)
- Retreating beast location moves away from attacker

---

### Story 2.1.6 — Needs, health, and lifecycle resolution

**What:** Per-season needs update, starvation, aging, death, reproduction. Runs in `SimPhase.EntityBehavior = 4`.

**Each season tick per live beast (before EmitCommands is called):**
```
1. AgeSeason += 1
2. FoodNeed -= species.FoodDepletion
   if Hibernates && CurrentSeason == Winter: FoodDepletion *= 0.2 (cold-blooded)
3. Clamp FoodNeed to [0, 1]
4. if FoodNeed < 0.2f: Health -= starvation_health_loss (from sim_config.toml [beasts] section)
5. if AgeSeason >= MaxAgeSeason: KillEntity(this, cause=Age)
6. if Health <= 0: already dead (combat resolver marks this), skip
7. Reproduction check:
   if AgeSeason >= ReproductionMinAge
   && FoodNeed >= ReproductionFoodThreshold
   && liveCountBySpecies[SpeciesId] < species.MaxPerWorld
   && rng < species.ReproductionChance:
     child = BeastFactory.Spawn(species, HomeTile, isLegendary=false, rng, nextEntitySeq)
     EntityRegistry.Add(child)
     QueueEvent(BeastReproduced, {ParentId, ChildId, ChildName})
```

**Dead entity cleanup:** after all commands resolved and all events queued for the tick, `EntityRegistry.Remove(id)` for every entity marked `!IsAlive`.

**sim_config.toml additions** (the few beast lifecycle constants that aren't species-specific):
```toml
[beasts]
starvation_health_loss = 5    # health points lost per season when FoodNeed < 0.2
```

**Tests:**
- Beast with `FoodNeed = 0.0` loses health each season
- Beast reaches `MaxAgeSeason` → marked dead, `BeastDied` event queued
- Reproduction does not fire when species is at `MaxPerWorld` cap

---

### Story 2.1.7 — Beast event types

**What:** Add beast-specific `EventType` values. All beast events are emitted as `PendingEvent` records; Phase 7 enriches them as with all other events.

**New EventType values** (add stable int IDs starting at 2001):
```csharp
BeastSpawned        = 2001,   // world init or BeastFactory.Spawn at runtime
BeastAwakened       = 2002,   // mythological creature emerges from schedule
BeastDied           = 2003,   // natural death (age or starvation)
BeastSlain          = 2004,   // killed by another entity
BeastReproduced     = 2005,   // offspring spawned
BeastEncountered    = 2006,   // two beasts on same tile (informational)
```

**VerbClass and tier assignments:**
| EventType | VerbClass | Default Tier |
|---|---|---|
| BeastSpawned | Creation | Background |
| BeastAwakened | Creation | Regional |
| BeastDied | Destruction | Regional |
| BeastSlain | Destruction | Regional |
| BeastReproduced | Creation | Background |
| BeastEncountered | Interaction | Background |

**Payload schemas** (JSON in `PendingEvent.PayloadJson`):
- `BeastDied/Slain`: `{"beastId": long, "name": str, "speciesId": str, "isLegendary": bool, "ageSeason": int, "cause": "Age"|"Starvation"|"Combat", "killerName": str|null}`
- `BeastAwakened`: `{"beastId": long, "name": str, "speciesId": str, "location": [x,y]}`
- `BeastReproduced`: `{"parentId": long, "childId": long, "childName": str, "speciesId": str}`

**Tests:**
- `BeastDied` event written to DB with correct payload and `VerbClass.Destruction`
- `BeastAwakened` is `EventTier.Regional`

---

### Story 2.1.8 — Beast display in UI

**What:** Render beast positions on the tile map. Show beast data in tile inspector.

**TileMapRenderer:** After drawing all tile fills, iterate `snapshot.AllTiles` — for each tile with `EntitiesPresent.Length > 0`, draw a 5×5 pixel marker at tile centre. Colour: `Color.DarkRed` for normal beasts, `Color.Gold` for `IsLegendary` beasts. Use the existing 1×1 `_pixel` texture.

**TileInspectorPanel:** When `InspectedTile != null` and `EntitiesPresent.Length > 0`, add a section below terrain data:
```
Entities (2):
  The Pale Wolf [Wolf ★, Age 34s, HP 82%, Food 61%]
  Wolf         [Wolf,   Age 12s, HP 100%, Food 78%]
```
`★` indicates `IsLegendary`. Data comes from `snapshot.EntitySnapshots[id]`. Inspector owns no entity state — all reads are from the snapshot.

**Tests:** UI-only; manual test per runbook update.

---

## Definition of Done

- [ ] `IEntity`, `EntityId`, `EntityKind`, `EntityRegistry` implemented; all tests pass
- [ ] `BeastCatalog` loads `config/beasts.toml`; `BeastSpeciesConfig` covers all fields
- [ ] `LegendaryBeast` driven by catalog data; no hardcoded species enum
- [ ] `BeastFactory` + `BeastSpawner` produce deterministic placements from seed
- [ ] `BeastEmergenceSchedule` stored on `WorldState`; mythological beasts emerge on schedule
- [ ] `EntityBehaviorPhase` (SimPhase 4) processes beast lifecycle and EmitCommands each tick
- [ ] Multi-round combat resolver with gang-size cap and retreat logic
- [ ] Beast event types 2001–2006 in `EventType` enum with stable IDs
- [ ] `TileDisplayData.EntitiesPresent` populated; beast markers visible on map
- [ ] Tile inspector shows beast name, species, legendary flag, age, health, food
- [ ] `IWorldStateReadOnly` entity lookup methods implemented
- [ ] `EntitySnapshot.ToSnapshot()` on `LegendaryBeast`
- [ ] All tests pass, zero warnings
- [ ] Reproducibility test: same seed → same beast names, positions, death years

---

## Open Decisions (propose and record with `// DECISION:` comment)

1. **Hibernation food depletion** — `0.2×` multiplier while `Hibernates && Winter`. Confirm this is low enough that hibernators don't starve in a long winter. Tune in beasts.toml if needed.

2. **Aquatic movement** — `aquatic`-tagged beasts can enter ocean tiles; all others can enter `coastal_water` (wading). Enforce in `BeastSpawner` valid-tile check and in `MoveToTile` resolver.

3. **Same-species territory** — beasts of the same `SpeciesId` with overlapping territory: movement priority step 7 (Wander) should avoid tiles already occupied by a same-species beast where possible. Cross-species follows normal aggression roll.

4. **Beast–tile damage** — defer; `// V2: beast terrain damage — aggressive beasts reduce tile Fertility`.
