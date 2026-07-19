# Sim Tuning Reference

**Status:** Current as of 2026-07-19 (Phases D1-D5 implemented, 2026-07-18 balance cleanup)

**Format:** knob / current value / safe range / too-low symptom / too-high symptom / metric to watch
**Verification tool:** `python3 scripts/balance-run.py --seed-list 42,777,9999 --years 300 --label my-test`
**Target bands:** `config/balance_invariants.toml` (see `docs/balance_invariants.md`)
**For context on the tuning work:** See archived `docs/archive/tuning_balance_review_2026-07-18.md` for detailed rationale.

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

## Carrying Capacity (settlement section — D1 unified model)

**D1 completed 2026-07-18.** Carrying capacity is now derived entirely from the food model:
`capacity = Σ (PeoplePerTilePeak × fertility × moisture × growing_season × biome_mult)` over territory tiles.
The old `carry_cap_grassland … carry_cap_default` per-biome table (13 keys) has been removed.
Biome differentiation lives in `BiomeFoodMultiplier` (ResourcePressurePhase) + `biome_food_bonus_scale`.
Capacity is EMA-smoothed each tick to damp territory-population feedback oscillation.

To diagnose why a settlement's population is capped, use `--audit-food` (see D2 section below).

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `carry_cap_minimum` | 100 | 20–500 | Very harsh biomes impossible to settle | Floor dominates; biome differentiation erased | `active_civs`, `settlements_total` |
| `capacity_smoothing_alpha` | 0.05 | 0.01–0.5 | Capacity frozen; doesn't respond to conquest/drought | Capacity jumps every tick; oscillation if territory grows with pop | `world_population`, `mean_food_ratio` |
| `people_per_tile_peak` (in resource_pressure) | 500 | 200–1500 | Population ceiling too low | Population explodes, Malthusian collapses | `world_population`, `mean_food_ratio` |

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
| `disease_contact_mult` | 1.5 | 1.0–3.0 | No extra outbreak risk from trade/war contact | Disease spreads explosively when civs are in contact | `active_diseases`, `deaths_disease` |
| `disease_famine_mult` | 2.0 | 1.0–5.0 | Famine doesn't increase disease vulnerability | Famine always triggers immediate outbreak | `active_diseases`, `deaths_disease` |
| `disease_famine_threshold` | 0.7 | 0.3–0.9 | "Famine" triggers too late (only severe shortages count) | Any food dip counts as famine; contact/famine factors always active | `active_diseases`, `mean_food_ratio` |

**D4 completed 2026-07-18.** Outbreak probability = `base_chance × density_factor × contact_factor × famine_factor`.
Contact factor fires when the civ has EmissaryExchange+ contact or is at war.
Famine factor fires when `food_pressure_ratio < disease_famine_threshold`.

---

## War / Tension ([war] section — D5 consolidated)

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
| `opportunistic_war_aggression_threshold` | 0.55 | 0.3–0.9 | Passive civs never launch opportunistic wars | High-aggression civs always declare on any weak neighbor | `wars_declared_ytd`, `war_causes` |
| `succession_crisis_war_tension_mult` | 2.0 | 1.0–5.0 | Succession crises confer no diplomatic vulnerability | Succession crisis immediately causes war with every neighbor | `wars_declared_ytd` |
| `weak_neighbor_settlement_fraction` | 0.4 | 0.1–0.9 | Very few settlements must be sick/starving before triggering | Any single sick settlement makes a civ a "weak neighbor" | `wars_declared_ytd`, `active_diseases` |
| `war_weak_neighbor_food_threshold` | 0.70 | 0.3–0.9 | Only near-famine settlements count as "starving" | Any below-peak food ratio triggers weak-neighbor flag | `settlements_in_crisis` |
| `weak_neighbor_tension_bonus` | 0.25 | 0.0–1.0 | Weak-neighbor condition doesn't accelerate war | Opportunistic wars fire constantly whenever a neighbor has any illness | `wars_declared_ytd` |
| `resource_shortage_war_food_threshold` | 0.75 | 0.3–0.9 | Aggressor must be severely starving to get shortage bonus | Any food dip triggers resource-shortage war cause | `wars_declared_ytd`, `mean_food_ratio` |
| `resource_shortage_tension_bonus` | 0.20 | 0.0–1.0 | Food shortage doesn't push civs to war | Civs go to war any time food dips | `wars_declared_ytd` |

**D5 completed 2026-07-18.** All war knobs consolidated from `[character]` → `[war]`.
Typed `WarOutcome` constants (Truce/Conquest/Surrender/Destruction) replace the old `"Outcome":null` JSON pattern.
Typed `WarCause` constants added; opportunistic causes fire as tension bonuses in `RunBorderTension`.
Observed war cause distribution (3 seeds × 300y): `border_tension` dominant; `weak_neighbor` in all seeds; `character_encounter` in all seeds; `resource_shortage` in seed 777.

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

## Unrest / Secession ([unrest] section — S2/S4)

Unrest accumulates on settlements annually from three independent drivers (distance, size, famine) and decays at a fixed rate. When a settlement's unrest crosses `unrest_secession_threshold` it has an annual chance to splinter into a new civ, dragging nearby high-unrest settlements with it.

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `unrest_comfort_radius` | 35 | 15–60 | All distant settlements always in unrest; constant premature secessions | Distance driver never fires; civs grow without unrest | `secessions_ytd`, `active_civs` |
| `unrest_distance_per_tile` | 0.003 | 0.001–0.02 | Distant cities barely accumulate unrest | Even slightly-distant cities reach threshold in a few years | `mean_unrest`, `secessions_ytd` |
| `unrest_soft_city_threshold` | 6 | 4–10 | Size driver fires too early; splinter after 4 cities | Empires can grow to 10+ cities without unrest | `max_cities_per_civ_actual`, `secessions_ytd` |
| `unrest_per_excess_city` | 0.04 | 0.01–0.15 | Size driver too weak; civs grow past threshold without fracturing | Any civ past threshold immediately spirals to secession | `mean_unrest`, `max_cities_per_civ_actual` |
| `unrest_famine_bonus` | 0.15 | 0.05–0.4 | Famine doesn't drive unrest; starving cities stay calm | Famine always causes immediate secession | `secessions_ytd`, `settlements_in_crisis` |
| `unrest_succession_bonus` | 0.20 | 0.05–0.5 | Leadership crises have no effect | Any succession causes immediate secession | `secessions_ytd` |
| `unrest_decay_rate` | 0.10 | 0.02–0.3 | Unrest accumulates permanently; all distant cities eventually secede | Unrest never builds; splinter mechanic dormant | `mean_unrest` |
| `unrest_secession_threshold` | 0.70 | 0.5–0.95 | Secessions fire too easily (threshold too low) | Secession impossible in practice | `secessions_ytd`, `active_civs` |
| `unrest_secession_chance` | 0.40 | 0.1–0.8 | Low annual probability; cities linger just over threshold for years | First year over threshold always fires | `secessions_ytd` |
| `unrest_cluster_radius` | 25 | 10–50 | Seceded civ spawns as a single isolated city | Entire continent joins the new civ | `active_civs`, `settlements_total` |
| `unrest_cluster_min_unrest` | 0.30 | 0.1–0.6 | Only the seceding city leaves (no cluster) | Moderately unhappy cities always join secession | `active_civs`, `secessions_ytd` |
| `splinter_initial_tension` | 0.60 | 0.2–0.9 | Parent civ immediately attacks seceded state | Seceded civ is immediately allied with parent | `wars_active`, `wars_declared_ytd` |

**Tuning notes (S4 2026-07-18):** Comfort radius bumped 20→35 because at 20 tiles, second cities in distant geography were accumulating unrest within 50 years and splintering before civs had time to grow. At 35, the secession life-cycle operates at the correct cadence (Y74–Y590 observed first secessions). The cluster radius (15→25) and min unrest (0.50→0.30) together mean seceded civs arrive with 2–3 cities rather than just 1, giving them enough economic base to survive.

---

## Expansion / Settlement Founding ([character] section — S1/S4)

| Knob | Current | Safe range | Too-low symptom | Too-high symptom | Metric to watch |
|---|---|---|---|---|---|
| `global_settlement_min_dist` | 3 | 2–8 | Settlements cluster unrealistically close (3 tiles = 30 km at 10 km/tile) | Tile space exhausted too quickly; growth stalls | `settlements_total`, `mean_cities_per_civ` |
| `min_fertility_to_settle` | 17 | 5–50 | Any marginal land settled; too many barren outposts | Growth stalls in geographies with moderate-fertility biomes | `settlements_total`, `active_civs` |
| `colony_min_distance` | 10 | 5–25 | Colonize goals form next to parent city (no real expansion) | Long-range colonization never attempted | `settlements_total`, `goals_formed_ytd` |
| `max_settlements_per_civ` | 15 | 8–25 | Civs hit ceiling before splinter fires; ceiling+splinter interact | untested | `max_cities_per_civ_actual` |
| `civ_floor_count` | 4 | 2–8 | World converges to 1–2 civs (no floor protection) | Floor mechanic creates constant founder-spam | `active_civs`, `goals_formed_ytd` |
| `civ_floor_spawn_chance` | 0.30 | 0.05–0.7 | Founder rarely spawns even when floor is breached | One missing civ slot always spawns a founder annually | `active_civs` |

**Tuning notes (S4 2026-07-18):** `min_fertility_to_settle` 30→17 unlocked settlement founding in seed 777's moderate-fertility geography that was stalling at 30. `global_settlement_min_dist` 4→3 allowed denser but still historically reasonable city spacing (30 km). `civ_floor_count` 5→4 stops the floor mechanic from spawning excess civs when the world naturally sustains 4–5 stable civs.

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

- **D1 complete (2026-07-18):** `carry_cap_*` per-biome table removed; capacity is now food-ledger derived with EMA smoothing. `people_per_tile_peak` is the single world-population scale knob. Balance invariants re-anchored at post-D1 calibration. Food now binds (min_food_ratio < 1.0 observed) — the carry-cap ceiling no longer silently dominates.
- **D2 (food audit):** Run `dotnet run --project WorldEngine.Sim -c Release -- --seed N --years Y --audit-food all` to print per-tile factor breakdown and per-settlement totals. Useful for diagnosing "why is this biome struggling" without a SQL session.
- **D3 complete (2026-07-18):** Three behavior tables extracted from C# switches to TOML — now tunable without recompiling:
  - `[utility_affinity.goal_affinity]` — 13 goal types × 12 actions: how strongly each action advances each goal. Tune to change character priorities (e.g. make Avenge goals pursue War more than Raid).
  - `[utility_affinity.action_needs]` — per-action need→coefficient map: how much each unfilled need drives an action. Tune to change what needs make characters want to fight, rest, build, etc. Rest's `(2-safety-food)*0.2` decomposed to `safety=0.20, food=0.20` coefficients.
  - `[wildlife_risk.biome_risk]` — per-biome wildlife raid multipliers (× `wildlife_attack_base_chance`). Dense cover biomes (forest 1.4–2.0×) vs. open terrain (plains 0.5×). Tune `default_risk` for the fallback on unlisted biomes.
  All values are identical to the previous hardcoded C# literals — no balance change. 39 regression-pin tests added.
- **S1–S4 expansion/splinter arc complete (2026-07-18):** Growth throttles opened, splinter mechanic added, biome spawn weights added, border-pair metric added. World is now multipolar with active secession/war. Balance bands re-anchored post-S4. Key 600-year observations: active_civs stabilises at 7–8 by Y600; 4–15 cumulative secessions per seed; `civ_border_pairs` 1–10 at Y600 (sustained territorial contact). War system fires regularly (3–5 wars active at Y300).
- **D4 complete (2026-07-18):** Structural disease model — outbreak probability = `base_chance × density_factor × contact_factor × famine_factor`. Contact factor (1.5×) fires when civ has EmissaryExchange+ or is at war. Famine factor (2.0×) fires when `food_pressure_ratio < 0.7`. Balance: `active_diseases` now reaches 1–3 in 300-year runs; `deaths_disease` non-zero. See Disease section above for new knobs.
- **D5 complete (2026-07-18):** All war knobs consolidated from `[character]` into `[war]`. Typed `WarOutcome`/`WarCause` constants. Three opportunistic war causes: `SuccessionCrisis`, `WeakNeighbor`, `ResourceShortage` fire as tension bonuses. Observed: all three appear in 3-seed × 300y balance sweep. See War/Tension section above for new knobs.
