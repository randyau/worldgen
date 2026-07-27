# M11 Phase 11.1 — Voyage command & resolution

**Status:** DONE — 2026-07-27 (build+test verified).
**Milestone:** M11 — Character Water Crossings (`docs/phases/m11_water_crossings.md`)
**Depends on:** 11.0 (`Port` improvement, `SeafaringConfig`, `IsShallowOcean`)

## What shipped

`GoalType.SeaVoyage`. `WorldState.GetLandmassId` (new, not originally scoped for 11.0 — needed to
tell "still my landmass" apart from a genuine far shore; lazy flood-fill, cached, added to
`IWorldStateReadOnly`). `UtilityScorer.FindVoyageDestination` (public): BFS over
`IsShallowOcean` tiles bounded by `SeafaringConfig.MaxVoyageTiles`, returns the nearest
different-landmass coastal tile or null; cached per origin. `UtilityScorer.StepTowardAllowingShallowOcean`
+ a `BuildCandidates` branch scoring `ActionType.SeaVoyage` (gated on
`SeafaringConfig.OceanCrossingEnabled`). `CharacterBehaviorPhase.ResolveMoveWithVoyageTracking`
wraps `ResolveMove` (approach (b) from this doc — no new `ICommand`): detects the land/water
boundary crossing and emits `EventType.SeaVoyageEmbarked`/`SeaVoyageCompleted`, marking the goal
complete on arrival. `GoalManager` exempts `SeaVoyage` from the short staleness-prune window
(same "travel takes years" treatment as `FoundCity`/`SlayBeast`) and adds it to `NotableGoalTypes`.
New `[utility_affinity]` TOML entries so a `SeaVoyage` goal actually outweighs ordinary `Travel`.

**Correction made during this phase:** `IsShallowOcean` (11.0) as strict 1-tile land-adjacency
only classified the immediate coastal fringe, making any strait wider than ~2 tiles unbridgeable
regardless of `MaxVoyageTiles` — a real strait's middle tiles touch neither shore. Widened to a
radius-2 neighborhood (`WorldState.ShallowOceanRadius`, not config-exposed — a geometric
definition, not a tuning knob). See the `// DECISION:` comment on `IsShallowOcean` in
`WorldState.cs`.

Tests: `WorldEngine.Tests/Integration/SeaVoyageTests.cs` (6 tests, on a fully synthetic hand-built
world — real worldgen output can't guarantee two hand-picked tiles are genuinely separate
landmasses) — route found across a narrow strait, route null beyond `MaxVoyageTiles`, route null
with no second landmass, full multi-tick crossing with both events firing and goal completion,
crossing never happens with the config toggle off, same-seed reproducibility.

## Goal

A character who already has a `SeaVoyage` goal and a route can now actually cross open water,
one tile per tick like every other move — no new "multi-tile-per-tick" mechanic, no new
per-character persistent voyage state. This phase does *not* decide who gets the goal (11.2);
it assumes the goal already exists and makes it executable.

## Scope

### 1. `GoalType.SeaVoyage`

Add to `GoalData.cs`'s `GoalType` enum, in the "M3+ city-state" or a new "M11 seafaring" grouping
comment block. `TargetTile` carries the far-shore destination (same field `FoundCity`/`SlayBeast`
already use for their target).

### 2. Route computation

A route is: from the character's current position (assumed adjacent to/at a `Port`-improved
coastal tile of their own civ), BFS/flood-fill over `world.IsShallowOcean` tiles (from 11.0),
bounded by `SeafaringConfig.MaxVoyageTiles`, to the nearest coastal land tile reachable that is
**not** part of the character's current landmass (i.e., not reachable via an all-`IsLand` walk
from their start — reuse whatever connectivity check 11.2 needs anyway; if none exists yet, a
simple flood-fill cache keyed by landmass is the minimal version, not a general graph library).

Cache the result the same way `ComputeRouteBonus`/`_routeCache` is cached in `UtilityScorer` —
keyed by origin tile, invalidated on the same settlement-count-change signal (`SyncCaches`) since
new `Port`s or new settlements change what's reachable.

`// DECISION:` pick and record `MaxVoyageTiles`' actual value here (this is the other open
implementation detail flagged in the index doc) — start conservative (e.g. enough tiles to cross
a strait a few tiles wide, not ocean baisins) and tune later; there's no existing km-per-tile
constant in this codebase to derive it from precisely, so pick a round number and leave the
`// DECISION:` explaining the reasoning.

### 3. Movement

Extend `UtilityScorer.StepToward` (or add a `StepTowardAllowingShallowOcean` variant used only
when the active goal is `SeaVoyage`) so it accepts a shallow-ocean tile if — and only if — that
tile is on the character's precomputed route. This mirrors the existing `HuntBeast` pattern
exactly (`StepToward` + `MoveToTile` candidate in `BuildCandidates`), just with the `IsLand` gate
relaxed for this one goal type. `BestAdjacentTile` (used for ordinary wanderlust travel) is
**not** touched — normal travel still never enters water.

`CharacterBehaviorPhase.ResolveCommand`'s `case MoveToTile move: ResolveMove(...)` handles the
resulting move exactly as today (`ResolveMove` doesn't validate land/adjacency — it trusts the
emitting code, same as it does now for every other move source). No new `ICommand` type is
strictly required — `MoveToTile` already carries the data resolution needs — but a
`CrossesOcean` flag needs to reach the event-emitting code (see below) so voyages are
distinguishable in history. Two ways to thread that through without adding a callback/delegate
field (forbidden by Mandatory Pattern #5): either (a) a sealed `EmbarkVoyage(EntityId, TileCoord)`
record used only for the *first* step of a voyage (departure), with ordinary `MoveToTile` for
subsequent steps, or (b) detect "this move crosses water" in `ResolveMove` itself by checking
`!world.IsLand(dest)` and emit the voyage event from there whenever that's true, with no new
command at all. **Prefer (b)** — it's the smaller diff and avoids a second near-duplicate command
type; only reach for (a) if `ResolveMove`'s call sites need to distinguish voyage moves from
ordinary moves for a reason beyond event emission.

### 4. Events

New range, adjacent to the existing 5000-series emissary events:

```csharp
// M11 — sea voyage events (5100-range)
SeaVoyageEmbarked  = 5101,  // character departed a Port on a sea voyage
SeaVoyageCompleted = 5102,  // character reached the far shore
// V2: SeaVoyageLost = 5103 — weather/sea-monster failure hook, not built this phase
```

Add corresponding `VerbClassification` cases (`SeaVoyageEmbarked` → `Creation`/`Transformation`,
`SeaVoyageCompleted` → `Transformation` — match the existing `CharacterExiled`/`GoalResolved`
tone) and whatever tier-classification switch drives event significance (check
`docs/interface_contracts_events.md` for the full list of places `EventType` needs a case —
the compiler will also just fail on any exhaustive switch that's missing one).

## Tests

- Unit test: route computation finds a path across a small synthetic strait, returns null when
  `MaxVoyageTiles` is exceeded or no far shore exists in range.
- Unit test: a character with a `SeaVoyage` goal and a valid route emits `MoveToTile` commands
  that step onto shallow-ocean tiles; a character with any other goal never does, even standing on
  the same tile (regression guard against loosening `BestAdjacentTile` itself by mistake).
- Integration test: full multi-tick crossing — character departs, several ticks stepping over
  water, arrives on the far shore; `SeaVoyageEmbarked`/`SeaVoyageCompleted` events both fire with
  correct payload.
- Reproducibility test: same seed ⇒ same route, same tick-by-tick path, same arrival tile.

## Non-negotiables checked

- No callback/delegate fields on any new record (Mandatory Pattern #5).
- All mutation happens in `ResolveCommand`/`ResolveMove` (RESOLVE step), never during scoring
  (EMIT step) (Mandatory Pattern #1).
- New `EventType` values are additive; existing ranges untouched.
- `SeafaringConfig.OceanCrossingEnabled = false` must fully disable route computation (no
  `SeaVoyage`-eligible candidates ever generated) — check this gate at the top of whatever method
  computes routes, not scattered across call sites.
