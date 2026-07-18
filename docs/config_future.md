# Future Config Sections

**Date:** 2026-07-18 (B2 config hygiene)

These TOML sections were removed from `sim_config.toml` during the B2 purge because they
describe unimplemented systems and bound to nothing (Tomlyn's `IgnoreMissingProperties=true`
silently ignored them). They are preserved here as design intent for when the systems are built.

When implementing a system, move its section back into `sim_config.toml` and bind it to a
new `*Config` class under `SimConfig`. The strict loader (B1) will catch any key that
doesn't bind, so these sections must be fully wired before they can live in the main config.

---

## Administrative Distance Penalty

Controls loyalty decay with distance from the capital, movement costs, and authority anchors.
Needed when Spotlight Mode (M5) or detailed governance mechanics are implemented.

```toml
[admin_distance]
max_distance_penalty        = 0.6     # Maximum loyalty reduction at zero authority
max_cultural_bonus          = 0.2     # Loyalty bonus from cultural alignment
max_religion_bonus          = 0.15    # Loyalty bonus from shared religion
max_personal_bonus          = 0.25    # Loyalty bonus from personal relationship with Tier 1
revolt_threshold            = 0.25    # Loyalty below this enables revolt probability
base_revolt_probability     = 0.02    # Base revolt chance per season at threshold (2%)

[admin_distance.movement_costs]
# Base movement cost (seasons to traverse) by biome
plains          = 1.0
grassland       = 1.0
forest          = 2.0
dense_forest    = 3.0
hills           = 2.0
mountains       = 5.0
high_mountains  = 10.0
desert          = 2.5
swamp           = 3.0
tundra          = 2.0
road_multiplier = 0.4     # Roads reduce movement cost by 60%
river_following = 0.7     # Following a river reduces cost
river_crossing  = 1.5     # Crossing a river increases cost
winter_mult     = 1.8     # Winter slows all movement
monsoon_mult    = 1.6     # Monsoon season slows movement

[admin_distance.anchors.capital]
core_radius     = 3.0     # Seasons of travel = full authority
max_radius      = 15.0    # Seasons of travel = zero authority
decay_rate      = 0.3
strength        = 1.0

[admin_distance.anchors.sub_capital]
core_radius     = 2.0
max_radius      = 10.0
decay_rate      = 0.35
strength        = 0.7

[admin_distance.anchors.tier1_presence]
core_radius     = 1.0
max_radius      = 5.0
decay_rate      = 0.5
strength        = 0.5

[admin_distance.anchors.garrison]
core_radius     = 1.0
max_radius      = 3.0
decay_rate      = 0.6
strength        = 0.3

[admin_distance.anchors.religious_center]
core_radius     = 2.0
max_radius      = 8.0
decay_rate      = 0.4
strength        = 0.4
```

---

## Spatial Buffer (Spotlight Mode)

Daily-resolution zone around the spotlight character. Needed for M5 Spotlight mode.

```toml
[spatial_buffer]
detailed_radius_world_tiles = 1      # World tiles of daily resolution around spotlight (3×3 zone)
buffer_width_world_tiles    = 2      # Width of interpolation buffer ring in world tiles
interpolation_noise_max     = 1      # Maximum tile offset in daily path interpolation
```

---

## Specialists

Population thresholds at which specialist Tier 2 roles crystallize, plus livelihood and
reputation constants. Currently hardcoded in `Tier2Spawner.cs` / `Tier2BehaviorPhase.cs`.

```toml
[specialists]
# Minimum settlement population to support each specialist type
apothecary_threshold        = 200
priest_threshold            = 300
entertainer_threshold       = 500
teacher_threshold           = 500
weaponsmith_threshold       = 800
physician_threshold         = 1000
scholar_threshold           = 2000
alchemist_threshold         = 3000
jeweler_threshold           = 3000
cartographer_threshold      = 5000
advisor_threshold           = 5000
spy_threshold               = 8000
architect_threshold         = 10000

# Livelihood thresholds
subsistence_needs_threshold = 0.3     # Needs below this = Survival state
independent_client_minimum  = 3       # Minimum regular clients for Independent state

# Reputation
reputation_boost_threshold  = 0.7    # Quality above which work boosts reputation
reputation_decay_rate       = 0.005  # Reputation decay per tick without high-quality work
reputation_spread_hops      = 2      # How many relationship hops reputation spreads
```

---

## Artifacts

Crafting artifact generation probabilities. Needed when the artifact/item system is
implemented (V2 roadmap).

```toml
[artifacts]
# Artifact generation (crafting specialists)
base_generation_probability = 0.05   # At max skill, 5% chance per crafting task
notable_performance_threshold = 0.75 # Quality above which a performance is notable
covet_threshold             = 0.6    # Artifact property score above which NPCs covet it
```

---

## Cultural Modifiers

Persistent regional modifiers (animosity, fear, reverence) that accumulate from events and
decay over time. Needed when the cultural modifier propagation system is built.

```toml
[cultural_modifiers]
expiry_threshold            = 0.03   # Modifier magnitude below this is considered expired
max_active_per_region       = 20     # Performance cap on active modifiers per region
reinforcement_window_years  = 50     # How recent must a similar event be to reinforce decay slowdown
reinforcement_bonus         = 0.15   # Magnitude boost per reinforcing event

[cultural_modifiers.half_life_years]
# How many years for a modifier to halve in magnitude
# Shorter = fades faster, Longer = persists longer
animosity_major_war         = 150
animosity_minor_war         = 60
fear_disaster               = 100
reverence_golden_age        = 120
xenophobia_plague           = 80
religious_fervor            = 200
trade_goodwill              = 40
military_trauma             = 100
cultural_pride              = 90
```

---

## Civilization Settler Seeding

Spontaneous seeding of new civilization founders when world population drops critically low.
Currently implemented as constants in `CivTracker.Diplomacy.cs` (`CivFloor*` keys in
`[character]`), but the settler-seeding subsystem (probability/starting-pop) is unimplemented.

```toml
[civilization.settler_seeding]
global_pop_threshold        = 500     # World population below this triggers spontaneous seeding check
probability_per_century     = 0.15    # 15% chance per century when below threshold
starting_population         = 20      # Initial settler group size
```

---

## Removed Dead Sections (non-future)

The following sections were also purged but are NOT preserved here because their live
equivalents exist under different names in the current config:

| Dead section | Live equivalent |
|---|---|
| `[characters]` | `[character]` |
| `[characters.needs]` | keys under `[character]` (needs_decay_food etc.) |
| `[utility]` | keys under `[character]` (needs_weight, goals_weight, etc.) |
| `[goals]` | keys under `[character]` (goal_*_threshold etc.) |
| `[environment]` | `[disasters]` |
| `[world_gen.climate]` | `[climate]` (runtime section) |
| `[religion]` legacy keys | `[religion]` (M4 keys only remain) |
| `[resources]` (top-level) | `[world_gen.resources]` (fertility keys now bound) |
| `[performance]` | `[sim_loop]` (relevant keys moved); rest unused |
| `[sim_loop.speed]` | keys moved directly under `[sim_loop]` |
| `[sim_loop.persistence]` | key moved directly under `[sim_loop]` |
| `[events]` legacy keys | removed (minimum_tier_to_record etc. ignored) |
| `[events.population_impact_thresholds]` | no C# binding exists |
| `[civilization]` (minus settler_seeding) | partly `[character]` (civ_floor_*) |

### Notable dead-vs-live disagreements found during purge

These keys were in dead sections with VALUES DIFFERENT from the live C# defaults/bindings.
The live values were preserved (no behavior change):

| Dead key | Dead value | Live value | Live location |
|---|---|---|---|
| `[characters.needs] food_decay` | 0.10 | 0.08 | `[character] needs_decay_food` |
| `[utility] needs_weight` | 0.40 | 0.50 | `[character] needs_weight` |
| `[utility] goals_weight` | 0.35 | 0.30 | `[character] goals_weight` |
| `[utility] personality_weight` | 0.15 | 0.20 | `[character] personality_weight` |
| `[civilization] tier2_per_population` | 200 | 10 | `[character] tier2_per_population` |
| `[performance] autosave_interval_ticks` | 40 | 960 | `[sim_loop] auto_save_interval_ticks` |
| `[environment] wildfire_prob` | 0.0005 | 0.000003 | `[disasters] wildfire_ignition_probability_per_tick` |
| `tier2_notable_cooldown_ticks` | 64 (dead — key name mismatch) | 32 | `[character] tier2_notable_cooldown_ticks` |
| `tier2_exceptional_work_chance` | 0.001 (dead — key name mismatch) | 0.002 | `[character] tier2_exceptional_work_chance` |

The last two rows were dead due to a `PascalToSnakeCase` bug that failed to insert `_` before
uppercase letters following a digit (e.g., `Tier2Notable` → `tier2notable` instead of
`tier2_notable`). Fixed in B2 by updating the conversion regex. TOML values set to live values.
