# M9 Phase 9.0 — CreatedGoodType Unification

**Status:** COMPLETE — 2026-07-26.
**Depends on:** —
**Read first:** `docs/phases/m9_created_object_unification.md` (index), then
`docs/design_session_decisions.md` § Design Session G (G-1, G-2).

## Goal

Replace `ArtisanGoodType` (string[]), `ArtType`, `DiscoveryType`, and the role-blind
`RoleToArtifactCategory` map with one `CreatedGoodType` taxonomy, where an artifact's category is
derived from the specific good that was being made, weighted across plausible categories. Fixes
G-1 (unification) and G-2 (type variety / Armor spawn gap) in one pass, per the design doc's
explicit instruction not to patch G-2 separately.

## Current state (see survey — file:line references)

- `Tier2BehaviorPhase.cs:436-438` — `ArtisanGoodType` bare string array (textiles, pottery,
  metalwork, woodcraft, leatherwork, stonework), used only in `RunArtisan`.
- `Core/Enumerations.cs:4-14` — `DiscoveryType` enum (8 values), used only in
  `Tier2BehaviorPhase.RunScholar`, paired 1:1 by array index with `DiscoveryBonusKey` (lines
  344-353).
- `Core/Enumerations.cs:16-24` — `ArtType` enum (6 values), used only in
  `CharacterBehaviorPhase.ResolveCreateArtwork` (Tier1 Create-goal path).
- `Entities/Artifacts/Artifact.cs:7` — `ArtifactCategory` enum (Weapon, Armor, Regalia, Tome,
  Relic, Jewelry, Artwork) — the persistent taxonomy, stays as-is (it's the *bucket*, not the
  *product*).
- `Tier2BehaviorPhase.cs:241-250` — `RoleToArtifactCategory`, called once at line 204 inside the
  masterwork-forge branch of `TryEmitNotableWork` (lines 180-219). Types every masterwork by the
  creator's Tier2 role, ignoring what they actually made this tick.
- Battle-forged (`CivTracker.War.cs:482`) and heroic-death (`CharacterBehaviorPhase.cs:397`)
  artifacts both hardcode `ArtifactCategory.Weapon` — no variety, no Armor path there either.
- God-Mode authoring (`AuthoringResolver.cs:40`) takes an explicit `Category` from the command —
  out of scope, leave as-is.

## Design

### 1. `CreatedGoodType` enum (`Core/Enumerations.cs`)

Replaces `ArtType` and `DiscoveryType` outright; replaces the `ArtisanGoodType` string array.

```csharp
public enum CreatedGoodType
{
    // Artisan goods (Tier2 Artisan)
    Textiles, Pottery, Metalwork, Woodcraft, Leatherwork, Stonework,
    // Art (Tier1 Create goal)
    Monument, Epic, Song, Tapestry, Sculpture, Painting,
    // Discoveries (Tier2 Scholar)
    Agriculture, Medicine, Astronomy, Mathematics, Engineering, Philosophy, Navigation, Metallurgy,
}
```

### 2. Grouping + weighted category table (new file `Entities/Artifacts/CreatedGoodTaxonomy.cs`)

```csharp
public static class CreatedGoodTaxonomy
{
    public static readonly CreatedGoodType[] ArtisanGoods = { Textiles, Pottery, Metalwork, Woodcraft, Leatherwork, Stonework };
    public static readonly CreatedGoodType[] ArtGoods      = { Monument, Epic, Song, Tapestry, Sculpture, Painting };
    public static readonly CreatedGoodType[] DiscoveryGoods = { Agriculture, Medicine, Astronomy, Mathematics, Engineering, Philosophy, Navigation, Metallurgy };

    // DECISION: this is taxonomy *structure* (which categories a good can plausibly become),
    // not a tunable rate — same precedent as ArtifactNameGenerator.NounsFor and
    // DiscoveryBonusKey. Weight *values* below are still illustrative game-balance numbers with
    // no single "correct" answer; revisit empirically once artifact-category telemetry exists.
    public static readonly IReadOnlyDictionary<CreatedGoodType, (ArtifactCategory Category, float Weight)[]> CategoryWeights = ...;

    public static ArtifactCategory PickCategory(WorldState world, EntityId id, int salt, CreatedGoodType good);

    public static readonly IReadOnlyDictionary<CreatedGoodType, string> DiscoveryBonusKeys = ...; // replaces parallel-array lookup
}
```

Weight table (illustrative, one option is fine for goods with an obvious single category):

| Good | Categories (weight) |
|---|---|
| Metalwork | Weapon .55, Armor .35, Regalia .10 |
| Woodcraft | Weapon .6, Artwork .4 |
| Leatherwork | Armor .7, Jewelry .3 |
| Stonework | Regalia .5, Artwork .5 |
| Textiles | Regalia .4, Jewelry .3, Artwork .3 |
| Pottery | Artwork .7, Relic .3 |
| Monument | Regalia 1.0 |
| Epic | Tome 1.0 |
| Song | Tome .6, Relic .4 |
| Tapestry / Sculpture / Painting | Artwork 1.0 |
| Agriculture / Astronomy / Mathematics / Philosophy | Tome 1.0 |
| Medicine | Relic .7, Tome .3 |
| Engineering / Navigation | Tome .7, Relic .3 (Navigation: .6/.4) |
| Metallurgy | Weapon .5, Armor .3, Tome .2 |

### 3. `TryEmitNotableWork` (Tier2BehaviorPhase.cs)

Add an optional `CreatedGoodType? good` parameter. When present (Artisan, Scholar calls), the
masterwork branch calls `CreatedGoodTaxonomy.PickCategory(...)` instead of
`RoleToArtifactCategory(c.Livelihood.Role)`. When absent (General, Governor, Merchant, Physician —
roles whose notable work isn't a "product," it's an act: guarding, ruling, dealing, healing), keep
a small fallback role→category switch (trimmed `RoleToArtifactCategory`, minus Artisan/Scholar
which now always pass `good`). Document this split with a `// DECISION` comment: G-1's model
("product of type X") only applies where a product actually exists.

### 4. Call-site updates

- `RunArtisan`: pick from `CreatedGoodTaxonomy.ArtisanGoods` instead of the string array; pass the
  picked `CreatedGoodType` into `TryEmitNotableWork`.
- `RunScholar`: pick from `CreatedGoodTaxonomy.DiscoveryGoods`; use `DiscoveryBonusKeys[discovery]`
  instead of the parallel `DiscoveryBonusKey[typeIndex]` array; pass the good into
  `TryEmitNotableWork`.
- `ResolveCreateArtwork` (CharacterBehaviorPhase.cs): pick from `CreatedGoodTaxonomy.ArtGoods`
  instead of `Enum.GetValues<ArtType>()`.
- Payload field values become `CreatedGoodType.ToString()` in place of the old enum/string —
  field *names* (`ArtType`, `DiscoveryType`, `GoodType`) stay unchanged (see index doc constraint 4).

### 5. G-2: battle-forged and heroic-death category variety

These have no "good" context (combat-triggered, not production-triggered) — per the design doc's
own example, give each an independent weighted roll instead of deriving from `CreatedGoodType`.
New `sim_config.toml [artifacts]` keys (true tunable weights, not structure):

```
battle_category_weight_weapon   = 0.5
battle_category_weight_armor    = 0.35
battle_category_weight_regalia  = 0.15
heroic_death_category_weight_weapon  = 0.5
heroic_death_category_weight_relic   = 0.3
heroic_death_category_weight_regalia = 0.2
```

Add to `ArtifactConfig`, validate (weights per group sum to 1.0 ± epsilon) in
`SimConfigValidator.cs` alongside the existing artifact checks. Replace the hardcoded
`ArtifactCategory.Weapon` assignments in `CivTracker.War.cs:482` and
`CharacterBehaviorPhase.cs:397` with a weighted roll (reuse the same salted-roll helper pattern
`CreatedGoodTaxonomy.PickCategory` uses, or a small shared weighted-pick utility).

### 6. Cleanup

- Delete `RoleToArtifactCategory`'s Artisan/Scholar arms and its `// FUTURE` marker comment (the
  future is now).
- Delete `ArtType`, `DiscoveryType` enums, the `ArtisanGoodType` string array, and the
  `DiscoveryBonusKey` parallel array.
- `SimRngSalts.cs` — `S.CharArtType` salt stays (still used for the good-type roll), rename if it
  reads oddly once the type is `CreatedGoodType` (optional, low priority).

## Testing

- Unit test for `CreatedGoodTaxonomy.PickCategory`: given a fixed seed, same good type always
  resolves to a category from its weight table (never a category with 0 weight); distribution
  roughly matches weights over many rolls.
- Unit test confirming `ArtifactCategory.Armor` is now reachable from at least one Tier2 masterwork
  good (Metalwork/Leatherwork/Metallurgy) and from the battle-forged path.
- Update/extend the existing artifact integration test(s) for the new category derivation.
- Reproducibility test must still pass unchanged (same seed → same world).
- Re-run `scripts/test-fast.sh`; if a balance sweep script exists for artifact category
  distribution, note in the phase doc's close-out whether a re-sweep against
  `config/balance_invariants.toml [year_300]` was needed (G-4) — category variety should not
  change *totals*, only the category mix, so it likely isn't needed, but confirm via the sweep
  metrics rather than assuming.

## Definition of done

- `ArtType`, `DiscoveryType`, `ArtisanGoodType` string array, and `RoleToArtifactCategory`'s
  Artisan/Scholar arms no longer exist in the codebase.
- Masterwork artifact category derives from the actual good being made, not the role alone.
- Battle-forged and heroic-death artifacts can produce Armor (and other categories) via weighted
  rolls, not a hardcoded `Weapon`.
- Zero warnings, all tests green, `scripts/doc-check.py` clean, architecture tests unaffected.
- Move this doc to `docs/phases/archive/`, update the index doc's status.
