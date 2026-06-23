# Beast System Design

**Status:** Design complete, pending implementation in Phase 2.1  
**Scope:** Normal predators, legendary variants, mythological creatures, abilities, combat, world effects

---

## Design Decisions

### DS-E1: Two tiers of beast — Normal and Mythological

**Normal predators** are real-world (or Earth-analogue) animals. They spawn in groups and can produce a **Legendary variant** — a uniquely named, oversized, long-lived specimen with boosted stats. The Legendary is the same species with multipliers applied; not a separate type.

**Mythological creatures** are always rare, always named, always essentially legendary in power. They do not have a "normal" version. They may have abilities that normal predators cannot.

**Rejected:** A flat rarity system treating all beasts the same — loses the narrative distinction between "there is a legendary wolf terrorising the northern passes" vs "a Dragon has awoken."

---

### DS-E2: Stat block structure

Each species has a base stat block. All values live in `config/beasts.toml`. No hardcoded numbers.

| Stat | Type | Meaning |
|---|---|---|
| `health` | int | Hit points. Dead at 0. |
| `strength` | int | Damage dealt per attack. |
| `speed` | int | Max tiles moved per season. |
| `aggression` | float 0–1 | Probability of attacking when encountering another entity. |
| `territory_radius` | int | Tiles from home tile the beast considers its range. |
| `pack_size` | int range | Min–max individuals spawned together (1–1 = solitary). |
| `food_depletion` | float | Needs.Food lost per season from metabolism. |
| `food_from_hunt` | float | Needs.Food restored by a successful attack on prey. |
| `food_from_graze` | float | Needs.Food restored per season from fertile tile (herbivore/omnivore only). |
| `age_min` | int | Minimum lifespan in seasons. |
| `age_max` | int | Maximum lifespan in seasons. |
| `reproduction_min_age` | int | Earliest season at which beast can reproduce. |
| `reproduction_food_threshold` | float | Minimum Needs.Food to trigger reproduction check. |
| `reproduction_chance` | float | Probability per season when conditions met. |
| `biomes` | string[] | Biome types where this species spawns (see biome list below). |
| `max_per_world` | int | Hard cap on live instances in the world at once. |
| `legendary_chance` | float | Probability that a newly spawned beast of this type becomes Legendary. |

---

### DS-E3: Legendary variant mechanics

When a beast spawns, roll `WorldRng` against `legendary_chance`. If it passes:

- Beast is marked `IsLegendary = true`
- Name is generated as `"{adjective} {species_name}"` (e.g., "The Pale Wolf", "The Iron Bear")
- Multipliers applied to base stats:
  - `legendary_health_mult` × base health
  - `legendary_strength_mult` × base strength
  - `legendary_age_mult` × max age
  - `legendary_territory_mult` × territory radius
- `BeastLegendarySpawned` event emitted (Regional tier)

Legendary beasts are capped at **1 per species** alive at any time. If a Legendary already exists, the roll is skipped.

---

### DS-E4: Abilities system

Abilities are tags on a species. They modify behaviour, combat resolution, or world effects. Implementation is phased — M2.1 implements a subset; full abilities come in M2.2+.

| Ability | Effect | M2.1? |
|---|---|---|
| `flight` | Can cross any terrain; speed not reduced by biome | No |
| `aquatic` | Preferred biome is ocean/coastal; can enter ocean tiles | No |
| `burrowing` | Ignores terrain movement penalties; can appear in any biome | No |
| `venom` | Successful attack causes `Poisoned` debuff — target loses health each season for N seasons | No |
| `fire_breath` | Ranged attack; can target adjacent tile; chance to start wildfire | No |
| `fear_aura` | Settlements within radius suffer Safety need reduction | No |
| `regeneration` | Restores health each season (amount in config) | No |
| `petrification` | Target cannot act for N seasons after a successful attack | No |
| `storm_call` | Low probability each season of triggering a storm event in territory | No |
| `rebirth` | On death, emits `BeastReborn` event and respawns at home tile after N seasons | No |
| `pack_leader` | Other beasts of same species within radius gain +20% strength | No |

**M2.1 scope:** Abilities are stored as tags on the species but produce no mechanical effect yet. The system reads them (logs a `// V2:` stub) so Phase 2.2+ can wire them in without a data model change.

---

### DS-E5: Combat resolution

**The time-scale problem:** A sim tick is one season (≈3 months). A real fight lasts hours. Resolving combat as a single roll per tick means "the wolf pack spent the entire summer deciding whether to bite the bear once." Instead, combat is a **multi-round sub-process that runs to resolution within the tick**. The tick records the outcome; the rounds are the mechanism.

**Model:**
1. Two entities on the same tile, one rolls aggression → if passes, combat begins
2. Run rounds until one side retreats or dies (max `CombatMaxRoundsPerTick`, configurable):
   - Each round: attacker rolls `attack = strength × WorldRng.FloatAt(seed, tick, attackerId, targetId, round)`
   - Defender rolls `defend = (health/maxHealth) × WorldRng.FloatAt(seed, tick, targetId, attackerId, round)`
   - If `attack > defend`: target loses `strength` health
   - Defender retaliates if still alive (same formula reversed)
   - Retreat check: if either entity's health drops below `RetreatHealthFraction` (config), they flee to an adjacent tile
3. Fight ends: winner stays, loser is dead or on a different tile

**Pack participation:** All pack members on the tile participate, but attack in sequence within each round (strongest first). Only `min(packCount, CombatMaxGangSize)` members can reach the target per round — models physical space constraints. `CombatMaxGangSize` defaults to 3. A 15-wolf pack vs a bear: 3 wolves attack per round, bear retaliates against one randomly; fight resolves in a few rounds. Still dangerous, not instantly lethal.

**Between-season attrition:** If neither side dies and neither retreats after `CombatMaxRoundsPerTick` rounds, combat ends for this tick. Both entities stay on the tile. Next tick: aggression check fires again. This produces natural "prolonged conflict" — a bear den with an encircling pack gradually losing members over multiple seasons.

**Combat is resolved in CommandResolver**, not in EmitCommands. EmitCommands emits `Attack(attacker, target)`. Resolver runs the round loop.

**Mythological vs Normal:** No special cases in the formula. Stats reflect the power differential — a Dragon has 10× health and strength of a Wolf. The numbers speak.

**Config additions needed:**
```toml
[combat]
max_rounds_per_tick       = 8     # rounds before "standoff" for this tick
max_gang_size             = 3     # max pack members attacking per round
retreat_health_fraction   = 0.25  # flee when health drops below 25%
```

---

### DS-E6: Beast effects on settlements (deferred to Phase 2.4)

Beasts near settlements affect population Safety need and potentially raid food stores. This requires settlements to exist. Deferred.

For now: `fear_aura` ability tag is stored but has no mechanical effect. Mark with `// V2: fear_aura settlement effect`.

---

### DS-E7: Pack mechanics

Packs are modelled as **multiple individual entities with the same home tile**, not as a single group entity. This keeps the entity model uniform — the sim loop just sees `IEntity[]`, each acting independently.

Pack coherence: wolf pack members have a weak pull toward each other in the movement priority (step 5 of behaviour: prefer tiles containing packmates within territory radius). This is optional and can be a config flag `prefers_company`.

---

### DS-E8: Biome affinity list

Beasts' `biomes` field references these biome names (matching `BiomeType` enum):

`ocean`, `coastal_water`, `beach`, `tundra`, `boreal_forest`, `temperate_forest`, `tropical_rainforest`, `grassland`, `savanna`, `desert`, `swamp`, `mountain`, `high_mountain`, `plains`, `volcanic`

Special value `any` = spawns in any non-ocean, non-polar biome.

---

## Normal Predator Catalog

### Tundra / Polar

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `wolf` | Wolf | 2–8 | tundra, boreal_forest, grassland | common | Pack hunter; legendary = massive alpha |
| `polar_bear` | Polar Bear | 1–2 | tundra | uncommon | Solitary; aquatic-adjacent |
| `snow_leopard` | Snow Leopard | 1–1 | tundra, mountain | uncommon | Ambush; high strength relative to size |

### Boreal Forest

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `brown_bear` | Brown Bear | 1–2 | boreal_forest, mountain | common | Omnivore; high health |
| `wolverine` | Wolverine | 1–1 | boreal_forest, tundra | uncommon | Extremely aggressive; punches above weight |
| `lynx` | Lynx | 1–2 | boreal_forest, temperate_forest | common | Fast; low health |

### Temperate Forest

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `mountain_lion` | Mountain Lion | 1–1 | temperate_forest, mountain, grassland | common | Solitary ambush predator |
| `black_bear` | Black Bear | 1–2 | temperate_forest, boreal_forest | common | Omnivore |
| `wild_boar` | Wild Boar | 2–6 | temperate_forest, grassland | common | Aggressive when threatened; not a top predator but dangerous |

### Grassland / Savanna

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `lion` | Lion | 3–12 | savanna, grassland | common | Pride hunter; territorial |
| `cheetah` | Cheetah | 1–3 | savanna, grassland | uncommon | Fastest; low health, avoids conflict |
| `hyena` | Hyena | 4–15 | savanna, grassland | common | Scavenger/hunter; large packs |
| `african_wild_dog` | African Wild Dog | 5–20 | savanna, grassland | uncommon | Endurance hunter; very cooperative packs |

### Desert

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `sand_viper` | Sand Viper | 1–1 | desert | common | Venom ability (M2.2+); ambush |
| `giant_scorpion` | Giant Scorpion | 1–3 | desert | uncommon | Venom; slow |
| `monitor_lizard` | Monitor Lizard | 1–2 | desert, savanna | uncommon | Venom; opportunistic |

### Tropical Rainforest

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `jaguar` | Jaguar | 1–1 | tropical_rainforest, swamp | common | Apex; kills by crushing skull |
| `anaconda` | Anaconda | 1–1 | tropical_rainforest, swamp | uncommon | Very high health; slow |
| `harpy_eagle` | Harpy Eagle | 1–2 | tropical_rainforest | uncommon | Flight (M2.2+); hunts monkeys/sloths |

### Swamp / Coastal

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `crocodile` | Crocodile | 1–4 | swamp, beach, coastal_water | common | Aquatic-adjacent; ambush from water |
| `giant_otter` | Giant Otter | 3–8 | swamp, boreal_forest (rivers) | uncommon | Pack; fish diet; aggressive |

### Mountain / Highland

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `cave_bear` | Cave Bear | 1–2 | mountain, boreal_forest | uncommon | Large; hibernates (reduced activity in winter) |
| `golden_eagle` | Golden Eagle | 1–2 | mountain, tundra, grassland | uncommon | Flight (M2.2+); hunts medium prey |

### Ocean / Deep Water

| ID | Display Name | Pack | Biomes | Rarity | Notable |
|---|---|---|---|---|---|
| `great_white_shark` | Great White Shark | 1–1 | ocean, coastal_water | uncommon | Aquatic; attacks coastal tiles |
| `giant_squid` | Giant Squid | 1–1 | ocean | rare | Aquatic; massive health |

---

## Mythological Creature Catalog

### Draconic

| ID | Display Name | Biomes | Max/World | Abilities | Power |
|---|---|---|---|---|---|
| `dragon` | Dragon | mountain, volcanic | 2 | flight, fire_breath, fear_aura | ★★★★★ |
| `wyvern` | Wyvern | mountain, coastal | 4 | flight, venom | ★★★☆☆ |
| `sea_serpent` | Sea Serpent | ocean, coastal | 3 | aquatic | ★★★★☆ |
| `lindworm` | Lindworm | swamp, boreal_forest | 3 | venom, burrowing | ★★★☆☆ |

### Giant Beasts

| ID | Display Name | Biomes | Max/World | Abilities | Power |
|---|---|---|---|---|---|
| `behemoth` | Behemoth | plains, grassland | 1 | fear_aura | ★★★★★ |
| `roc` | Roc | mountain, any | 2 | flight | ★★★★☆ |
| `kraken` | Kraken | ocean | 1 | aquatic, fear_aura | ★★★★★ |
| `leviathan` | Leviathan | ocean | 1 | aquatic, regeneration | ★★★★★ |

### Hybrid / Chimeric

| ID | Display Name | Biomes | Max/World | Abilities | Power |
|---|---|---|---|---|---|
| `griffin` | Griffin | mountain, grassland | 3 | flight | ★★★☆☆ |
| `manticore` | Manticore | desert, savanna | 3 | venom | ★★★☆☆ |
| `chimera` | Chimera | volcanic, mountain | 2 | fire_breath | ★★★★☆ |
| `cockatrice` | Cockatrice | swamp, desert | 4 | petrification, flight | ★★☆☆☆ |

### Magical / Elemental

| ID | Display Name | Biomes | Max/World | Abilities | Power |
|---|---|---|---|---|---|
| `phoenix` | Phoenix | volcanic, desert | 1 | flight, fire_breath, rebirth | ★★★★☆ |
| `thunderbird` | Thunderbird | mountain, tundra | 2 | flight, storm_call | ★★★★☆ |
| `basilisk` | Basilisk | desert, mountain | 2 | petrification | ★★★☆☆ |
| `hydra` | Hydra | swamp | 2 | regeneration | ★★★★☆ |

### Horror / Undead-Adjacent

| ID | Display Name | Biomes | Max/World | Abilities | Power |
|---|---|---|---|---|---|
| `wendigo` | Wendigo | boreal_forest, tundra | 2 | fear_aura, burrowing | ★★★☆☆ |
| `dire_wolf` | Dire Wolf | any (land) | 3 | pack_leader | ★★★☆☆ |

---

## Config File Structure

Beast catalog lives in `config/beasts.toml`, separate from `sim_config.toml` (which holds simulation balance constants). `SimConfig` loads both.

```toml
# config/beasts.toml
# Full catalog of beast species. Each [[beasts]] entry is one species.
# Legendary variants are described inline via legendary_* fields.

[[beasts]]
id                          = "wolf"
display_name                = "Wolf"
category                    = "predator"       # predator | mythological
biomes                      = ["tundra", "boreal_forest", "grassland"]
max_per_world               = 30
pack_size_min               = 2
pack_size_max               = 8
prefers_company             = true

health                      = 30
strength                    = 12
speed                       = 4
aggression                  = 0.65
territory_radius            = 10
food_depletion              = 0.12
food_from_hunt              = 0.55
food_from_graze             = 0.0
age_min_seasons             = 24
age_max_seasons             = 60
reproduction_min_age        = 12
reproduction_food_threshold = 0.65
reproduction_chance         = 0.08
abilities                   = []

legendary_chance            = 0.04
legendary_health_mult       = 2.8
legendary_strength_mult     = 2.2
legendary_age_mult          = 3.0
legendary_territory_mult    = 2.5
legendary_name_adjectives   = ["Pale", "Gray", "Iron", "Shadow", "Frost"]
```

---

## World Spawn Budget

Total beasts alive at world start is the sum of each species' `spawn_count` (a separate config from `max_per_world`). Default target: **~60 total entities** across all species at world start, scaling with world size.

```toml
[beast_spawn]
target_density_per_10k_tiles = 5   # roughly 60 beasts on a 120k-tile world
legendary_spawn_on_start     = true  # legendary check fires at world gen, not later
```

If a species has no valid biome tiles on this world, it is skipped silently.

---

## Open Decisions (resolve before implementation)

1. **Hibernation** — cave bear and some cold-climate species should be inactive in winter. Model as `speed = 0` and `food_depletion *= 0.2` in winter season. Needs a `hibernates` flag per species.

2. **Aquatic movement** — `aquatic` beasts can enter ocean tiles; non-aquatic cannot. Should the ocean tile boundary be strict, or can a non-aquatic beast wade into `coastal_water` tiles? **Proposed:** coastal_water tiles are wading-accessible to all beasts; ocean tiles require `aquatic` ability.

3. **Legendary name generation** — **Decided:** Both forms, picked randomly. Each species has `legendary_name_adjectives` (produces "The Pale Wolf") and optionally `legendary_name_nouns` (produces "The Frostmaw"). Generator rolls 50/50 between forms if both are present, adjective-only if nouns list is absent.

4. **Pack coordination in combat** — should a pack of wolves all attack the same target in one tick, or only one per turn? **Proposed:** only the pack member who first detects the target attacks that season; others may join in subsequent seasons if still alive. Prevents instant one-tick murder by a 15-wolf hyena pack.

5. **Beast vs Beast territory disputes** — two packs of the same species with overlapping territory: do they fight for the home tile, or avoid each other? **Proposed:** same-species packs avoid each other (wander radius pulls them away). Cross-species: follow aggression roll normally.

6. **Mythological creature spawn timing** — **Decided:** 20% of mythological creatures (configurable, `myth_start_fraction`) spawn at world start. The remaining 80% emerge as `BeastAwakened` events at random points within the first `myth_emergence_years` years (default 200). Emergence tick is seeded deterministically. Makes most mythological appearances historical moments while the world feels inhabited from year 0.

7. **Beast corpse / remains** — when a beast dies, does it leave a tile marker? **Proposed:** no tile marker in M2.1. The death event is recorded in the DB; future archaeology systems can query it.
