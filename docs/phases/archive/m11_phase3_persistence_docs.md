# M11 Phase 11.3 — Persistence, event log & close-out

**Status:** DONE — 2026-07-27 (build+test verified; no manual UI pass — no display in this environment).
**Milestone:** M11 — Character Water Crossings (`docs/phases/archive/m11_water_crossings.md`)
**Depends on:** 11.2 (full delegate→embark→cross→found flow works and is tested)

## What shipped

Persistence needed no mapper changes — `GoalData` (including `SeaVoyage`'s `TargetTile`) was
already generic through `WorldStateDto`/`WorldStateMapper`; verified with a new
`WorldEngine.Tests/Integration/SeaVoyagePersistenceTests.cs` (a character standing on water
mid-voyage round-trips its location and incomplete `SeaVoyage` goal through save/load). Event
log: `Presenter.EventVerbPhrase` gained `SeaVoyageEmbarked` ("set sail") /
`SeaVoyageCompleted` ("made landfall"); `Presenter.GoalIntentPhrase` gained `SeaVoyage`
("voyaging across the sea") — `EventLogPanel.cs` itself needed no change since it already
consumes `Presenter` generically (and was left untouched deliberately — it's mid-edit from
unrelated in-progress work in this working tree). Docs: `docs/interface_contracts_events.md`
(hand-maintained) gained the Seafaring domain (5100–5199) in the range table, payload table, and
Domain summary table; `docs/modding.md` needed no change (Port is a hardcoded enum value like
every other `ImprovementType`, consistent with existing scope).

## Goal

Make sea voyages durable across save/load and visible in the UI, then close out the story.

## Scope

### 1. Persistence

Check whether `SeaVoyage`'s `GoalData` (with its `TargetTile`) round-trips through
`WorldStateDto`/`WorldStateMapper` for free — `GoalData` is presumably already a generic
serialized field on `Tier1EntityDto` alongside every other goal type, in which case this is a
verification test, not new mapper code. If 11.1 added any *other* new per-character state (it
shouldn't have — route caching was designed to live in `UtilityScorer`'s existing
`_routeCache`-style instance cache, not on the character), that state needs explicit DTO/mapper
wiring here.

- `SaveLoadTests` (`WorldEngine.Tests/Integration/SaveLoadTests.cs`): round-trip a character
  mid-`SeaVoyage` goal (partway across water) — save, load, confirm the goal and location survive
  and the character resumes crossing on the next tick rather than snapping back to land-only
  behavior.

### 2. Event log display

`EventLogPanel` (`WorldEngine.UI/UI/Panels/EventLogPanel.cs`) already has tier color-coding and
type icons/grouping from the M6 polish pass. Add `SeaVoyageEmbarked`/`SeaVoyageCompleted` to
whatever icon/grouping table drives that (check the M6 phase-2 doc or the panel source directly
for the exact table) — this is a data-table addition, not new UI structure.

### 3. Docs

- `docs/codebase_map.md`, `docs/config_reference.md`, and the event-log query enum tables
  regenerate automatically post-commit (per `CLAUDE.md` — do not hand-edit).
- `docs/interface_contracts_core.md` or `_events.md` (whichever lists `ICommand`/`GoalType`/
  `EventType` tables — check both, split by domain) gets a one-line mention of `GoalType.SeaVoyage`
  and the new `EventType` range if those tables are hand-maintained rather than generated (verify
  before editing — if generated, skip, per the "trust without re-checking" rule).
- `docs/modding.md`: note if `Port` shows up in any moddable-data table (it's a hardcoded enum
  value like the other `ImprovementType`s, so likely no change needed — confirm against how the
  other improvement types are/aren't documented there).

### 4. Close-out

- Move all four `m11_phase*_*.md` docs and the `m11_water_crossings.md` index to
  `docs/phases/archive/`, mark each one done with a dated status line (see any archived phase doc
  for the exact wording convention).
- Update `docs/roadmap.md`'s Cross-cutting backlog entry for "Character water crossings" to point
  at the archived index doc instead of describing it as unimplemented.

## Tests

- `SaveLoadTests` addition described above.
- Manual/UI check (per `CLAUDE.md`'s UI-change testing requirement): run the app, watch a civ
  build a `Port`, delegate a voyage, and confirm the event log shows the departure/arrival events
  with correct icons — this phase is the one place in the story that touches
  `WorldEngine.UI`, so it needs the actual browser/app pass, not just green tests.

## Non-negotiables checked

- `scripts/doc-check.py` exits 0 (generated-doc freshness gate).
- All architecture tests still pass.
- `scripts/test-fast.sh` green before archiving.
