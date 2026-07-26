# M10 Phase 10.3 — Data modding

**Status: COMPLETE — 2026-07-26 (build+test verified).**
**Milestone:** M10 — Worldgen Preview & Modding (`docs/phases/m10_worldgen_preview_modding.md`)
**Depends on:** — (parallel-safe)

## What shipped

Per the M10 index doc's DECISION (10.3), scope is `config/ancestries.toml` and `config/beasts.toml`
— the two files that were already TOML-driven, data-only catalogs. Biomes/resources remain
hardcoded enums and are explicitly out of scope (would need their own phase).

**`WorldEngine.Sim/Config/AncestryValidator.cs`** (new, sim-only, headless) — validates every
`AncestryConfig` after deserialization: non-blank/unique `id`, non-blank `display_name`,
`min_lifespan_seasons` > 0 and ≤ `max_lifespan_seasons`, `spawn_weights` keys are known
`BiomeType` snake_case names with non-negative weights, `first_meeting_trust`/`cultural_distance`
keys reference an ancestry that exists in the file and fall within `[-1, 1]` / `[0, 1]`
respectively. Wired into `AncestryLoader.LoadOrDefault` right after `Toml.ToModel`.

**`WorldEngine.Sim/Entities/Beasts/BeastCatalogValidator.cs`** (new, sim-only, headless) —
validates `[beast_spawn]` (density/fraction/years/recovery ranges) and every `BeastSpeciesConfig`:
non-blank/unique `id`, `category` ∈ {predator, mythological}, `biomes` non-empty and each entry
either `"any"` or a known `BiomeType` name, `pack_size_min ≤ pack_size_max`,
`age_min_seasons ≤ age_max_seasons`, non-negative stat fields, `[0,1]`-bounded probability fields,
and category-appropriate name lists non-empty (mythological needs `name_adjectives`/`name_nouns`;
predator needs the `legendary_*` pair). Wired into `BeastCatalogLoader.LoadOrCreateDefault`.

Both validators mirror the existing `SimConfigValidator` pattern exactly: collect every violation
into a list, throw one exception (`AncestryValidationException` / `BeastCatalogValidationException`)
listing all of them — a load-time gate per CLAUDE.md's "fail fast, not silent degradation" rule for
mod data, matching the M10 index doc's non-negotiable constraint #5.

**`docs/modding.md`** (new) — documents all three moddable/tunable TOML files
(`sim_config.toml`, `ancestries.toml`, `beasts.toml`), their schemas, the validation rules each
field is subject to, and explicitly scopes out biomes/resources with a pointer to why.

## Non-negotiables checked

- Both validators live in `WorldEngine.Sim` (`Config/` and `Entities/Beasts/`), reference nothing
  outside `System.*`/sim types — headless, unit-testable without any UI project reference.
- Validation is a load-time gate (runs unconditionally inside `LoadOrDefault`/
  `LoadOrCreateDefault`, not behind a debug/strict flag) — invalid mod data throws before the sim
  ever starts, per constraint #5 in the M10 index doc.
- No new plugin/scripting surface — TOML edit + restart only, consistent with "no
  modding/plugin system" in CLAUDE.md's "What NOT to Build" (interpreted as: no *code* modding;
  the M8 `// MOD SEAM:` data-registry pattern this phase extends is explicitly the sanctioned
  scope, per the M10 index doc's framing of item 3).

## Tests

- `WorldEngine.Tests/Unit/AncestryValidatorTests.cs` — 8 tests: accepts a well-formed list, rejects
  duplicate/blank ids, rejects inverted lifespan range, rejects an unknown biome key in
  `spawn_weights`, rejects a dangling ancestry reference in `first_meeting_trust`, rejects
  out-of-range trust and cultural-distance values, and confirms the real `ancestries.toml` passes.
- `WorldEngine.Tests/Unit/BeastCatalogValidatorTests.cs` — 9 tests: accepts a well-formed catalog,
  rejects duplicate ids, rejects an invalid `category`, rejects an unknown biome (while accepting
  the `"any"` keyword), rejects an inverted pack-size range, rejects a mythological species missing
  its name lists, rejects an out-of-range `myth_start_fraction`, and confirms the real
  `beasts.toml` passes.

## Verification

- `dotnet build WorldEngine.sln` — 0 warnings, 0 errors.
- `dotnet test` filtered to the new validator tests plus the existing `AncestryConfigTests`/
  `BeastCatalogTests` — all pass, confirming the real `config/ancestries.toml` and
  `config/beasts.toml` files (which drive normal gameplay) pass validation unchanged.
- Full `scripts/test-fast.sh` run — doc-check clean, full suite green.
