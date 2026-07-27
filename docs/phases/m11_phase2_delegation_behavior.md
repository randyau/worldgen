# M11 Phase 11.2 — Delegation & behavior integration

**Status:** NOT STARTED.
**Milestone:** M11 — Character Water Crossings (`docs/phases/m11_water_crossings.md`)
**Depends on:** 11.1 (`SeaVoyage` goal is executable once assigned)

## Goal

Decide *who* gets sent on a sea voyage and *when*, wiring `SeaVoyage` into the existing
goal-delegation and scoring machinery instead of leaving it something only a test can assign.

## Scope

### 1. Ruler delegation

Mirror the existing emigration/`FoundCity` seed in `CharacterBehaviorPhase` (the `overCapacity`
block that seeds a `FoundCity` goal on a newly-born emigrant, around line 170). Add the
"landlocked" condition alongside it: a civ whose settlements have no reachable unclaimed frontier
tile on their own landmass (reuse/extend whatever reachability check 11.1's route search needed;
if 11.1 built a landmass-connectivity flood-fill for "is the far shore actually a different
landmass," this is the same primitive) **and** owns at least one `Port` improvement seeds a
`SeaVoyage` goal instead of (or in addition to — a civ might want both regular and overseas
expansion) a `FoundCity` goal.

On arrival at the far shore (`SeaVoyageCompleted`), the character should pick up a `FoundCity`
goal targeting their new location — reuse the exact same goal-seeding shape as the emigrant path,
just triggered by voyage completion instead of birth. This closes the loop: delegate → embark →
cross → found, without inventing a second founding mechanism.

### 2. `UtilityScorer` candidate wiring

Add a `SeaVoyage` branch to `BuildCandidates` alongside `FoundCity`/`SlayBeast` — score it with
the same shape as the existing `HuntBeast`/travel-toward-target pattern (`StepToward` result as a
`MoveToTile` candidate, scored via the existing `Score(...)` helper with a new
`ActionType.SeaVoyage` case). Add `SeaVoyage` to the private `ActionType` enum (note the
`UtilityAffinityTables.TryParseAction` comment — both places need the new index) and to
`PersonalityFit` (propose `Personality.Ambition` similar to `FoundCity`, since it's the same
"ruler-delegated expansion" flavor of act).

### 3. Spotlight intent bias

`UtilityScorer.ApplySpotlightBias`'s `GoalIntent` switch already has a case per goal type
(`FoundCity`, `Flee`, `Bond`, etc.). Add `GoalType.SeaVoyage => ca.Command is MoveToTile` so a
player who spotlights a character with a voyage in progress gets the same intent-biasing behavior
as any other goal — small, consistent addition, not a new subsystem.

### 4. Determinism & balance

- Reproducibility test: a full seed run with seafaring enabled reproduces byte-for-byte across two
  runs (same requirement as every other mechanic per `CLAUDE.md`'s testing section).
- Regression test: a seed with no coastal civs / no `Port`s ever built produces `SeaVoyage`-goal
  count 0 — the feature must be inert when there's no opportunity to use it, not just when the
  config toggle is off.

## Tests

- Integration test: end-to-end delegate → embark → cross → found, asserting the new settlement
  lands on a different landmass than the origin civ's other settlements.
- Unit test: landlocked-but-no-Port civ never seeds `SeaVoyage` (falls back to ordinary `FoundCity`
  behavior, i.e. stays land-bound as today until a `Port` exists).
- Unit test: `ActionType.SeaVoyage` scores per `PersonalityFit`/goal-affinity like the other
  delegated-expansion actions (mirror the existing `FoundCity` scoring tests).

## Non-negotiables checked

- Delegation logic lives entirely in `WorldEngine.Sim` (`CharacterBehaviorPhase`/`CivTracker`),
  no UI involvement.
- No hardcoded thresholds — any new "landlocked" or "has-Port" gating constants belong in
  `SeafaringConfig`/`CharacterSimConfig`.
