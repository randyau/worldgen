# Data Modding Guide

M10 10.3. This documents the data files a modder can safely edit without touching code. Scope is
deliberately narrow — see `docs/phases/archive/m10_worldgen_preview_modding.md` DECISION (10.3) for why
biomes and resource types are out of scope for now.

No plugin or scripting system exists or is planned (see `CLAUDE.md` § "What NOT to Build").
Modding here means: edit a TOML file, restart the sim. Every file below is validated at load time
— invalid data fails fast with a listed set of errors instead of silently corrupting a run.

## `config/sim_config.toml`

All simulation tunables (rates, thresholds, weights). See `docs/config_reference.md` for the full,
auto-generated key list. Validated by `SimConfigValidator` (`WorldEngine.Sim/Config/SimConfigValidator.cs`)
— range/ordering/cross-field checks, one `SimConfigValidationException` listing every violation.
Also surfaced live in-app via the Settings → Simulation tab (M10 10.2, `ConfigRegistry`/
`SimConfigEditor`) — no file edit required for a single-session tweak, though the tab does not
write back to the TOML file.

## `config/ancestries.toml`

One `[[ancestry]]` block per playable ancestry (human, elf, dwarf, etc.). Schema:
`WorldEngine.Sim/Config/AncestryConfig.cs`. Loaded by `AncestryLoader.LoadOrDefault`, validated by
`AncestryValidator` (`WorldEngine.Sim/Config/AncestryValidator.cs`).

Key fields:
- `id`, `display_name` — required, non-blank; `id` must be unique across the file.
- `min_lifespan_seasons` / `max_lifespan_seasons` — seasons (4/year); `min` must be > 0 and ≤ `max`.
- `bias_*` — personality/aptitude offsets added to the Gaussian mean (base 0.5). No hard range
  enforced (values outside roughly [-0.3, 0.3] just produce an unusually extreme ancestry).
- `spawn_weights` — table of `biome_name = weight`. Biome keys are the snake_case form of
  `BiomeType` (`WorldEngine.Sim/Core/Enumerations.cs`), e.g. `temperate_forest`, `boreal_forest`.
  An unknown biome key or a negative weight fails validation. Missing biomes get weight 0
  (ancestry never spawns there).
- `first_meeting_trust`, `cultural_distance` — tables keyed by *another ancestry's* `id`. Both keys
  must reference an ancestry that exists elsewhere in the file. `first_meeting_trust` must be in
  `[-1, 1]`; `cultural_distance` must be in `[0, 1]`.
- `first_names`, `epithets` — name pools; may be empty (falls back to
  `sim_config.toml`'s `[character_names]` pool, see `CharacterFactory.cs`).
- `architectural_style`, `settlement_descriptor`, `biome_adaptations`, `improvement_descriptors`,
  `artistic_traditions`, `civ_name_suffix` — flavor text used by settlement/civ naming and UI
  description text. No structural validation beyond non-blank checks on the required fields above.

## `config/beasts.toml`

Global `[beast_spawn]` / `[combat]` settings plus one `[[beasts]]` block per species. Schema:
`WorldEngine.Sim/Entities/Beasts/BeastSpeciesConfig.cs` and `BeastSpawnConfig.cs`. Loaded by
`BeastCatalogLoader.LoadOrCreateDefault`, validated by `BeastCatalogValidator`
(`WorldEngine.Sim/Entities/Beasts/BeastCatalogValidator.cs`).

Key fields:
- `id`, `display_name` — required, non-blank; `id` must be unique across the file.
- `category` — must be exactly `"predator"` or `"mythological"`. Drives which name-list pair
  (`name_adjectives`/`name_nouns` vs. `legendary_name_adjectives`/`legendary_name_nouns`) is
  required non-empty, and whether the legendary-variant multipliers apply.
- `biomes` — list of snake_case `BiomeType` names, or the literal `"any"` to mean every biome.
  Must not be empty; unknown biome names fail validation.
- `pack_size_min` / `pack_size_max` — `min` must be ≥ 1 and ≤ `max`.
- `age_min_seasons` / `age_max_seasons` — `min` must be ≥ 0 and ≤ `max`.
- `health`, `strength`, `speed`, `territory_radius`, `food_from_hunt`, `food_from_graze` and the
  `legendary_*_mult` multipliers — must be ≥ 0 (`health` must be > 0).
- `aggression`, `food_depletion`, `reproduction_food_threshold`, `reproduction_chance`,
  `legendary_chance` — probabilities, must be in `[0, 1]`.
- `[beast_spawn]` — `target_density_per_10k_tiles` must be > 0; `myth_start_fraction` in `[0, 1]`;
  `myth_emergence_years` and `passive_food_recovery` ≥ 0.

## Validation model

Both validators follow the same shape as `SimConfigValidator`: walk every entry, collect every
violation into a list, then throw one exception (`AncestryValidationException` /
`BeastCatalogValidationException`) containing the full list — a modder sees every problem in their
file in one pass instead of fixing errors one at a time. Validation runs unconditionally (not just
in `StrictMode`, unlike `sim_config.toml`'s unbound-key check) because bad ancestry/beast data
(dangling cross-references, out-of-range probabilities, inverted min/max pairs) would otherwise
either crash later at a confusing call site or silently degrade the simulation — both worse than a
load-time failure with a precise message.

## Out of scope (see M10 10.3 DECISION for why)

Biomes (`BiomeType`) and resource-deposit identity are hardcoded C# enums with switch-statement
logic throughout the sim (`OverlayRenderer`, `ResourceLayer`, `ResourceDeposit`, etc.), not
TOML-driven catalogs. Making those moddable is a schema-plus-loader-plus-every-call-site project,
not a validation pass — it would need its own phase if ever pursued.
