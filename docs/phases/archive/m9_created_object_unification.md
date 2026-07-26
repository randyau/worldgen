# M9 — Created-Object Unification & Economic Depth (index)

**Milestone:** M9 — Created-Object Unification & Economic Depth
**Status: COMPLETE — 2026-07-26.** Phase 9.0 (G-1 + G-2) done 2026-07-26 (see
`docs/phases/archive/m9_phase0_taxonomy_unification.md`). Phase 9.1 (economic depth) done
2026-07-26 — see `docs/phases/archive/m9_phase1_economic_depth.md`. Phase 9.2 (settlement
specialization) done 2026-07-26 — see `docs/phases/archive/m9_phase2_settlement_specialization.md`.
No further phase was scoped — see "Post-9.2 note" below for the closeout rationale. Next
milestone: M10 — Worldgen Preview & Modding (`docs/roadmap.md` § "M10").
**Design authority:** `docs/design_session_decisions.md` § "Design Session G — Created Objects &
Artifacts" (G-1 through G-4) — the *why*. This doc set is the *how*.
**Roadmap:** `docs/roadmap.md` § "M9".

> **Every M9 worker reads this file first, then only their phase doc.**

---

## What this milestone is

Pays down the Session G taxonomy debt, then builds economic depth on the cleaned foundation.

Today, four disconnected vocabularies exist for "things characters make": `ArtisanGoodType`
(a bare `string[]`), `ArtType`, `DiscoveryType`, and `ArtifactCategory`. A masterwork's persisted
*type* is chosen by the creator's **role** (`RoleToArtifactCategory`), not by what they were
actually making — so a legendary piece of metalwork can come out "Artwork" instead of Weapon/Armor,
and `ArtifactCategory.Armor` has no reliable spawn source at all (G-2).

G-1's target model: one shared `CreatedGoodType` taxonomy. A creative act yields a *product of
type X*; quality drives persistence — routine work stays a transient economic event (as today),
exceptional work becomes an `Artifact` whose category derives from *that same product type X*,
weighted where a good plausibly yields more than one category (metalwork → Weapon **or** Armor).
G-2 (artifact type variety) folds into this same pass rather than being a piecemeal patch.

Economic depth (goods flow, per-capita demand, richer trade/specialization) is scoped in detail
**after** 9.0 lands — it depends on which shape `CreatedGoodType` actually takes.

## Phase sequence

| Phase | Doc | Depends on | One-line deliverable |
|-------|-----|-----------|----------------------|
| 9.0 | `archive/m9_phase0_taxonomy_unification.md` | — | `CreatedGoodType` taxonomy; weighted good→category persistence; delete `RoleToArtifactCategory`; fix Armor spawn gap (G-1 + G-2). **DONE 2026-07-26.** |
| 9.1 | `archive/m9_phase1_economic_depth.md` | 9.0 | Per-capita demand for non-vital resources; wire 5 of 8 write-only `bonus_*` store keys to real effects; demand-aware merchant routing. **DONE 2026-07-26.** |
| 9.2 | `archive/m9_phase2_settlement_specialization.md` | 9.1 | EMA-tracked settlement resource specialization (production bonus) + matching merchant export-side routing bonus. **DONE 2026-07-26.** |

Do not start a phase until the previous one is merged and green (`scripts/test-fast.sh`).

**Post-9.2 note (closeout, 2026-07-26):** two remaining candidates were considered and rejected as
M9 scope, not deferred by oversight:
- Full trade-network topology (named routes, travel time/caravans, price/currency) — Merchant
  trade stays the existing teleport-style store-to-store transfer. Revisit only if a longer
  balance sweep shows the current model genuinely limiting.
- The 3 still-inert `bonus_*` keys (`bonus_construction_speed`, `bonus_navigation`,
  `bonus_exploration_range`, see `// DECISION` in `CreatedGoodTaxonomy.cs`) — these are blocked on
  mechanics (build-time-over-ticks, travel speed, exploration range) that don't exist anywhere in
  the sim yet. Wiring them now would mean inventing a subsystem to justify a config knob, backwards
  from how 9.1/9.2 worked. Not economic depth — a new-feature design decision, to be scoped
  separately if/when those mechanics land.

**M9 is closed.** Next milestone is M10 — Worldgen Preview & Modding (`docs/roadmap.md` § "M10").

## Non-negotiable constraints (every phase)

From `CLAUDE.md`:
1. All new tunable constants (probabilities, weights that represent genuine game-balance knobs)
   go in `SimConfig`/`sim_config.toml`. Structural taxonomy data (which categories a good type
   *can* become at all — the shape of the mapping, not its weights) may stay as code data with a
   `// DECISION` comment, consistent with existing precedent (`ArtifactNameGenerator.NounsFor`,
   `DiscoveryBonusKey`) — but the actual weight *values* driving randomness are config.
2. `WorldEngine.Sim` stays headless; no UI references.
3. Every changed behavior needs a test; the reproducibility test must still pass.
4. Payload JSON field *names* (`ArtType`, `DiscoveryType`, `GoodType`, `Category`) stay stable —
   only the enum feeding `.ToString()` into them changes — to avoid unnecessary event-log schema
   churn (disk is system of record, Mandatory Pattern #6).
