# Sim Tuning Reference

**Format:** knob / current value / safe range / too-low symptom / too-high symptom / metric to watch
**Verification tool:** `python3 scripts/balance-run.py --seed-list 42,777,9999 --years 300 --label my-test`
**Target bands:** `config/balance_invariants.toml` (see `docs/balance_invariants.md`)

Ranges marked **untested** mean no calibration data exists; change carefully and measure.

---

## Food / Population (resource_pressure + settlement sections)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `people_per_tile_peak` | 500 | 200–1500 | Population ceiling too low, settlements cap at tiny sizes | Population explodes, Malthusian collapses every generation | `world_population`, `mean_food_ratio` |
| `settlement_start_pop` | 500 | 50–2000 | Too-easy founding (tundra/desert civs flourish unrealistically) | Nobody founds cities (food ratio at founding too low) | `settlements_total`, `active_civs` |
| `pop_growth_rate` | 0.5 | 0.1–2.0 | Slow growth, undershoots carrying cap; wars/disasters have lasting impact | Rapid Malthusian cycles; high settlement turnover | `world_population`, `settlements_in_shortage` |
| `pop_decay_rate` | 0.05 | 0.01–0.15 | Population never falls; empties don't empty | Constant attrition; hard to maintain population | `world_population`, `deaths_other` |
| `starvation_decay_rate` | 0.3 | 0.1–1.0 | Starvation too gentle; civs limp along in crisis indefinitely | Instant abandonment on any food shortage | `settlements_in_crisis`, `settlements_abandoned_ytd` |
| `food_moisture_floor` | 0.25 | 0.0–0.5 | Drought zeroes out tile food → mass abandonment in dry biomes | Dry biomes produce too much food; desert civs never struggle | `mean_food_ratio`, `settlements_in_crisis` |
| `food_moisture_absolute_floor` | 0.35 | 0.1–0.6 | Low-moisture tiles (deserts, tundra) zero out food production | Desert/tundra food ratio unrealistically high | `settlements_in_crisis`, `world_population` |
| `cold_hardy_food_floor` | 0.70 | 0.3–0.9 | Tundra civs starve immediately; too few high-lat settlements | Tundra as productive as temperate; no biome penalty | `settlements_in_crisis`, `active_civs` |
| `shortage_threshold` | 0.6 | 0.4–0.9 | Shortage goals never fire; characters don't respond to food scarcity | Constant "shortage" state even in healthy settlements | `goals_formed_ytd`, `settlements_in_shortage` |
| `crisis_threshold` | 0.3 | 0.1–0.5 | Flee goals never fire; settlements stay in crisis too long | Flee goals firing constantly; too much population flux | `goals_formed_ytd`, `settlements_in_crisis` |
| `store_accumulate_rate` | 0.6 | 0.2–1.0 | Surplus wasted; no buffer for winter/drought → volatile ratios | Stores absorb everything; demand always met even in drought | `mean_food_ratio`, `min_food_ratio` |
| `biome_food_bonus_scale` | 1.0 | 0.0–2.0 | All biomes equal; no habitat advantage/disadvantage | Extreme biome differentiation; desert/tundra always collapse | `mean_food_ratio`, `active_civs` |

---

## Carrying Capacity (settlement.carry_cap_*)

These are soft population ceilings per territory tile per biome.
D1 (pop-ceiling unification) will replace all of these with a single derived formula.

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `carry_cap_grassland` | 80 | 40–200 | Grassland civs underperform | Grassland civs dominate; monoculture history | `world_population`, `settlements_total` |
| `carry_cap_desert` | 6 | 2–20 | Desert civs always collapse | Desert as productive as plains | `active_civs`, `collapsed_civs` |
| `carry_cap_minimum` | 100 | 20–500 | Very harsh biomes impossible to settle | Floor dominates; biome differentiation erased | `active_civs`, `settlements_total` |

---

## Disease (settlement section)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `disease_base_chance` | 0.01 | 0.001–0.05 | No outbreaks; disease never appears in history | Outbreak every few years per settlement; dominates death count | `active_diseases`, `deaths_disease` |
| `disease_density_mult` | 3.0 | 1.0–10.0 | Dense cities no more vulnerable than villages | Dense cities always diseased | `active_diseases`, `settlements_in_crisis` |
| `disease_mortality_per_year` | 0.05 | 0.01–0.3 | Disease has no demographic effect | Disease is lethal; rapid settlement collapse | `deaths_disease`, `world_population` |
| `disease_spread_chance` | 0.08 | 0.01–0.5 | Outbreaks self-contained; no regional spread | One outbreak infects the entire world | `active_diseases` |
| `disease_recovery_chance` | 0.30 | 0.05–0.8 | Outbreaks last 8 years (hit max duration); chronic disease | Disease resolves in a single year; no lasting impact | `active_diseases`, `deaths_disease` |
| `disease_max_duration_years` | 8 | 3–20 | untested | untested | `active_diseases` |

---

## War / Tension (character + war sections)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `tension_accrual_per_pair` | 0.12 | 0.02–0.5 | Civs never reach war threshold; 0 wars in history | Wars every few years; constant war state | `wars_active`, `wars_declared_ytd` |
| `tension_war_threshold` | 1.0 | 0.3–3.0 | Wars too frequent | Wars extremely rare even when civs are neighbors | `wars_active`, `wars_declared_ytd` |
| `tension_decay_rate` | 0.15 | 0.05–0.5 | Tension builds inexorably toward war regardless of separation | Tension resets too fast; no persistent grievance | `wars_active` |
| `territory_tension_per_adjacent_pair` | 0.015 | 0.001–0.1 | No territory-based tension; wars only from character encounters | Territory contact immediately causes war | `wars_active`, `wars_declared_ytd` |
| `max_war_duration_years` | 15 | 5–50 | Wars end before meaningful territory changes | Endless wars that never resolve | `wars_active`, `wars_ended_truce_ytd` |
| `peace_cooldown_years` | 10 | 2–50 | Same civs cycle in/out of war every few years | Civs that fought once can never fight again | `wars_declared_ytd` |
| `raid_damage_min` / `raid_damage_max` | 15 / 40 | 5–20 / 10–80 | Raids do no damage; siege is pointless | Raids destroy settlements in one hit | `settlements_in_crisis`, settlement health |
| `war_proximity_radius` | 15 | 5–30 | Only immediate neighbors ever build war tension | Far-apart civs get tension from irrelevant neighbors | `wars_active` |

---

## Character Lifecycle (character section)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `max_age_seasons_min` | 80 | 40–160 | Characters die before establishing relationships/goals | Very old characters slow goal resolution | `tier1_count`, `goals_resolved_ytd` |
| `max_age_seasons_max` | 200 | 80–400 | untested | untested | `tier1_count` |
| `civ_birth_chance_per_season` | 0.01 | 0.001–0.1 | Civs never get new Tier1 members; stagnant | Too many characters → performance / narrative dilution | `tier1_count`, `goals_formed_ytd` |
| `civ_birth_min_pop` | 20 | 5–100 | New characters born in tiny struggling settlements | Large cities never produce new characters | `tier1_count` |
| `initial_count` | 14 | 5–50 | Too few initial chars; slow civ formation | Too many initial chars; messy early history | `tier1_count`, `active_civs` |

---

## Disasters (disasters section)

All disaster knobs are per-tick probabilities. Multiply by 16 (ticks/year) × eligible tiles to estimate annual rate.

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `wildfire_ignition_probability_per_tick` | 0.000003 | 0.0000005–0.00005 | No wildfires in history | 12+ wildfires/year dominating DB | Event counts by type |
| `earthquake_probability_per_tick` | 0.000005 | 0.000001–0.0001 | No earthquakes | Constant seismic damage; stone structures irrelevant | Event counts |
| `flood_ignition_probability_per_tick` | 0.000002 | 0.0000005–0.00005 | No floods | Riverside settlements in permanent flood crisis | `settlements_in_crisis` |
| `volcanic_eruption_probability_per_tick` | 0.000005 | 0.0000005–0.0001 | No eruptions | Volcanic regions always ash-covered; unusable | Event counts |
| `drought_probability_per_year` | 0.05 | 0.01–0.3 | No droughts; `food_moisture_floor` never tested | Constant drought; `food_moisture_floor` always active | `settlements_in_shortage` |

---

## Territory (territory section)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `max_territory_radius` | 10 | 3–25 | Cities don't expand; resource base tiny | Single city claims entire continent | `settlements_total`, `world_population` |
| `territory_growth_per_year` | 4 | 1–20 | Slow expansion; founding new cities preferred too quickly | Instant territory snowball; no exploration pressure | `settlements_total` |
| `claim_tiles_per_person` | 8 | 2–30 | Cities with 1000 pop claim 125 tiles = radius 6 (adequate) | Very large cities claim too few tiles; food shortage | `mean_food_ratio` |
| `territory_tension_per_adjacent_pair` | 0.015 | 0.001–0.1 | No war from territory contact | Wars from any territorial proximity | `wars_declared_ytd` |

---

## Goals / Wellbeing (character section)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `needs_decay_food` | 0.08 | 0.02–0.25 | Food need never pressures; no food-driven goals | Constant starvation pressure; nothing else matters | `goals_formed_ytd`, `mean_wellbeing` |
| `needs_decay_safety` | 0.05 | 0.01–0.20 | Safety need never pressures | Safety need dominates everything; chars flee constantly | `goals_formed_ytd`, `mean_wellbeing` |
| `flourishing_threshold` | untested | — | — | — | `mean_wellbeing` |
| `spiraling_threshold` | untested | — | — | — | `mean_wellbeing` |

---

## Notes for Phase D Workers

- **D1 (pop-ceiling unification):** After D1 ships, re-calibrate `world_population`, `settlements_total`, and all `carry_cap_*` bands. The `people_per_tile_peak` knob becomes the single tuning lever; most `carry_cap_*` keys will be removed.
- **D4 (structural disease model):** After D4, disease outbreak rate becomes density/contact-derived. `disease_base_chance` and `disease_density_mult` will either be replaced or supplemented with `disease_density_threshold` (population / carrying capacity fraction). Re-calibrate `active_diseases` and `deaths_disease` bands.
- **D5 (war consolidation):** After D5, consolidate all `[character]` war keys (`tension_accrual_per_pair`, `tension_war_threshold`, `tension_decay_rate`, `max_war_duration_years`, `peace_cooldown_years`, `raid_damage_*`) into `[war]` section alongside the territory tension keys. Re-calibrate `wars_active` bands from 0 baseline.
