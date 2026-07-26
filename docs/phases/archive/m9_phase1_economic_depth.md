# M9 Phase 9.1 — Per-Capita Demand & Bonus-Store Consumption

**Status:** COMPLETE — 2026-07-26.
**Depends on:** 9.0 (`docs/phases/archive/m9_phase0_taxonomy_unification.md`) — done.
**Read first:** `docs/phases/archive/m9_created_object_unification.md` (index), then this doc.

## Goal

Roadmap commits 9.1 to "per-capita demand model, richer merchant trade routing"
(`docs/roadmap.md:231`). Two concrete, pre-existing gaps found by survey satisfy exactly that:

1. Non-vital resources (minerals, timber, wealth) have supply but never draw against
   population — `ResourcePressurePhase.cs:266`'s own comment admits it ("demand driven by
   artisans" — but no artisan-side draw exists). Food/water already do this via
   `ApplyVitalStore`; this phase generalizes the same shape to everything else.
2. Eight `bonus_*` store keys exist and are written every tick (`bonus_food_yield`,
   `bonus_trade_income`, `bonus_construction_speed`, `bonus_military_strength`,
   `bonus_disease_resistance`, `bonus_navigation`, `bonus_exploration_range`,
   `bonus_civ_cohesion` — `CreatedGoodTaxonomy.cs:39-46`, plus the Artisan-side
   `bonus_civ_cohesion` write at `Tier2BehaviorPhase.cs:442`) but **none are ever read anywhere**
   (verified: `grep -rn '"bonus_'` across the whole solution matches only the two write sites and
   the `CreatedGoodTaxonomy` key-name table). Scholar discoveries and Artisan routine work
   currently have no mechanical effect on the world at all beyond a generic counter no one looks
   at.

Settlement specialization / richer trade-network topology stays out of scope — explicitly
deferred to a possible 9.2 per the index doc's phase table (`m9_created_object_unification.md:40`).
This phase does not add travel-time, caravans, or a price/currency mechanic — Merchant trade stays
the existing teleport-style store-to-store transfer (`docs/design_session_decisions.md:415`); it
only changes *which* resource/destination pair a merchant prioritizes.

## Current state (file:line)

- `ResourcePressurePhase.cs:99-111` — non-vital resources (everything except food/water)
  accumulate from tile supply with spoilage but **no demand draw**; comment at line 266 flags this
  as a known gap.
- `ResourcePressurePhase.cs:348-375` (private helper `ApplyVitalStore`) — the existing per-capita pattern for
  food/water: normalizes raw tile supply by population (`BuildLedger` lines 264-265), then spoils
  + banks/draws stores, returning an effective ratio consumed by shortage goals
  (`SeedResourceGoals`, lines 379-413) and by `PopulationDynamicsPhase`/disease/unrest.
- `Tier2BehaviorPhase.RunMerchant` (`Tier2BehaviorPhase.cs:252-328`) — picks the destination/resource
  pair maximizing raw `homeAmount - destAmount`, with an ally bonus. No population/demand weighting
  — a rich capital's small absolute surplus of a rare good can lose to a huge raw pile of a common
  one that the destination doesn't actually need per-capita.
- `RunScholar` (`Tier2BehaviorPhase.cs:344-380`) and `RunArtisan`
  (`Tier2BehaviorPhase.cs:425-450`) write `bonus_*` keys into `ResourceStores` every successful
  roll; nothing downstream reads them (confirmed by repo-wide grep).
- Consumer hook points that already exist and are the natural place to read each bonus:
  - `bonus_food_yield` → `ResourcePressurePhase.BuildLedger`, food contribution (line ~197-198),
    before the per-capita normalization at lines 264-265.
  - `bonus_disease_resistance` → `PopulationDynamicsPhase.cs` disease pass, `outbreakChance`
    calculation (the `densityFactor`/`contactFactor`/`famineFactor` product).
  - `bonus_civ_cohesion` → `CivTracker.Unrest.cs:39-63`, subtract from `accrual` before clamping.
  - `bonus_military_strength` → `CivTracker.War.cs:210-211`, additive to `attackerStr`/`defenderStr`
    (read from the attacking/defending civ's capital or nearest settlement store).
  - `bonus_trade_income` → `Tier2BehaviorPhase.RunMerchant`, scales `MerchantTradeTransfer` for
    that merchant's home settlement.
  - `bonus_construction_speed`, `bonus_navigation`, `bonus_exploration_range` → **no existing
    mechanic to hook into** (no build-time-over-ticks concept — improvements are placed
    instantly at `CivTracker.cs:300`; no travel-speed or exploration-range concept found in this
    survey). Do not invent new mechanics just to consume these three keys — leave them
    write-only with a `// DECISION` comment marking them intentionally inert pending those
    mechanics landing (they cost nothing sitting unused; wiring them prematurely would mean
    guessing at mechanics this phase isn't scoped to design).

## Design

### 1. Per-capita demand ratio for non-vital resources

In `ResourcePressurePhase.BuildLedger`, after the existing food/water normalization
(lines 264-265), normalize every other ledger key the same way, using one generic per-capita
demand rate rather than a per-mineral table (avoids a combinatorial config surface):

```csharp
float population = Math.Max(1f, stub.Population);
if (supply.TryGetValue("food",  out float fs)) supply["food"]  = fs / population;
if (supply.TryGetValue("water", out float ws)) supply["water"] = ws / population;
foreach (var key in supply.Keys.ToList())
{
    if (key is "food" or "water") continue;
    if (key.StartsWith("bonus_", StringComparison.OrdinalIgnoreCase)) continue; // not a physical good
    supply[key] = supply[key] / (population * _cfg.NonVitalDemandPerCapita);
}
```

New `[resource_pressure]` config key: `non_vital_demand_per_capita` (float, default tuned so a
mid-size settlement's typical mineral/timber tile yield lands ratio ≈ 1.0–2.0 under current world-gen
densities — derive the default empirically from a headless run rather than guessing; see Testing).

`Execute`'s non-vital accumulation loop (lines 99-111) already reads from `ledger` as "supply
units" and multiplies by `WealthAccumulateRate` — once `ledger[key]` is a ratio instead of an
absolute, that line's *meaning* changes from "bank raw tile yield" to "bank tile yield scaled by
how well it clears per-capita demand." Adjust `WealthAccumulateRate`'s effective scale in config if
this shifts stockpile growth rates outside prior balance bands (check against
`config/balance_invariants.toml [year_300]`, same re-sweep discipline as 9.0).

Do **not** wire this ratio into `SeedResourceGoals`/shortage-driven Acquire goals for minerals in
this phase — food/water crisis-goal-seeding is a Tier1 character behavior with its own tuning
surface; extending it to minerals is genuinely new scope (would need new `GoalObject` values and
new goal-resolution paths) and isn't required to satisfy "per-capita demand model." Flag as future
work if it turns out to matter once 9.1 ships.

### 2. Wire five of the eight bonus-store keys to real effects

Each is a small additive/multiplicative nudge read from the settlement's own `ResourceStores`
(`GetStore(key)` already defaults missing keys to 0 — no null-handling needed). All five values
are genuine tunable weights (Mandatory Pattern #2) — new keys, not existing ones:

| Bonus key | Consumer | New config key | Effect |
|---|---|---|---|
| `bonus_food_yield` | `ResourcePressurePhase.BuildLedger` | `[resource_pressure] food_yield_bonus_scale` | `foodContrib *= 1f + stub.GetStore("bonus_food_yield") * scale` |
| `bonus_disease_resistance` | `PopulationDynamicsPhase` disease pass | `[settlement] disease_resistance_bonus_scale` (or wherever `DiseaseBaseChance` lives) | `outbreakChance *= 1f - min(cap, stub.GetStore(...) * scale)` |
| `bonus_civ_cohesion` | `CivTracker.Unrest.cs` accrual | `[unrest] cohesion_bonus_scale` | `accrual = Math.Max(0f, accrual - stub.GetStore("bonus_civ_cohesion") * scale)` |
| `bonus_military_strength` | `CivTracker.War.cs` battle roll | `[war] military_strength_bonus_scale` | `attackerStr += homeCiv's capital GetStore(...) * scale` (same for defender) |
| `bonus_trade_income` | `Tier2BehaviorPhase.RunMerchant` | `[character] trade_income_bonus_scale` | `transfer = available * (_cfg.MerchantTradeTransfer * (1f + home.GetStore(...) * scale))` |

Cap each multiplier (e.g. `Math.Min(cap, ...)`) so an unbounded accumulation (these bonus stores
still spoil via the existing generic non-vital-resource spoilage loop, but slowly) can't zero out
disease entirely or make battles a foregone conclusion — add a `*_bonus_cap` config key alongside
each `*_bonus_scale` key, validated in `SimConfigValidator` (>= 0).

### 3. Demand-aware merchant trade routing

In `RunMerchant`, weight opportunity by the destination's per-capita demand ratio for that
resource (from part 1) instead of scoring on raw store-amount difference alone:

```csharp
float destRatio = /* dest's normalized ledger ratio for res, or 1.0 if unknown/vital-exempt */;
float demandWeight = destRatio < 1f ? Math.Min(_cfg.MerchantMaxDemandWeight, 1f / Math.Max(0.05f, destRatio)) : 1f;
float opportunity = (homeAmount - destAmount) * demandWeight;
```

This needs the destination's current ledger, not just its stores — `ResourceLedger` is already on
`SettlementStub` and rebuilt every tick before `RunMerchant` runs (Tier2 behavior phase runs after
`ResourcePressurePhase` in phase order — confirm ordering in `PhaseRunner.cs`/`SimLoop.cs` before
relying on this). If ledger for `res` is missing (e.g. a wealth type with no tile source, like
gold accumulated only via trade itself), default `demandWeight` to 1.0 (neutral — don't penalize
resources with no demand model). New config key: `[character] merchant_max_demand_weight` (cap,
default ~3.0, so a merchant doesn't ignore everything else to chase one starved settlement).

Confirm phase execution order (`PhaseRunner.cs`/`SimLoop.cs`) puts `ResourcePressurePhase` before
`Tier2BehaviorPhase` in the same tick before relying on "ledger is fresh" above — if order ever
changes, fall back to reading `ResourceStores`-only (no ratio) rather than a stale ledger.

## Testing

- Unit test: `ApplyVitalStore`-style normalization applied to a non-vital resource — given known
  supply + population, assert the resulting ledger ratio matches `supply / (population *
  NonVitalDemandPerCapita)`.
- Unit test per bonus-consumer: given a settlement with a known `bonus_*` store value, assert the
  consumer's output (food contribution, outbreak chance, unrest accrual, battle strength, trade
  transfer amount) shifts by the expected scaled amount, and that the cap holds at extreme values.
- Unit test: `RunMerchant` picks a lower-raw-surplus resource over a higher-raw-surplus one when
  the lower one's destination demand ratio is more deficient (demand-weighting actually changes
  the choice, not just the score).
- Reproducibility test must still pass unchanged (same seed → same world).
- Run a headless balance sweep before picking `non_vital_demand_per_capita`'s default and each
  `*_bonus_scale`/`*_bonus_cap` default — these are new knobs with no prior value to inherit;
  don't guess without checking `config/balance_invariants.toml [year_300]` stays inside prior
  bands (mineral/timber stockpile growth rate, disease outbreak frequency, war outcome
  distribution, unrest/secession rate are all now touched).

## Definition of done

- `ResourcePressurePhase.cs:266`'s "demand driven by artisans" comment is either made true or
  removed/rewritten to reflect what's actually implemented.
- Five of the eight `bonus_*` keys have real, tested consumers; the remaining three
  (`bonus_construction_speed`, `bonus_navigation`, `bonus_exploration_range`) carry a `// DECISION`
  comment explaining why they're intentionally inert.
- `RunMerchant` routing demonstrably changes destination/resource choice based on per-capita
  demand, not just raw store amounts.
- Zero warnings, all tests green, `scripts/doc-check.py` clean, architecture tests unaffected,
  balance sweep confirms `config/balance_invariants.toml [year_300]` bands still hold (or the
  bands are deliberately updated with rationale).
- Move this doc to `docs/phases/archive/`, update the index doc's status and phase table (mark 9.1
  done, note whether 9.2 settlement-specialization scope still looks warranted given what 9.1
  revealed).
