# M11 — Character Water Crossings (index)

**Milestone:** M11 — Scale & Distribution (`docs/roadmap.md` § "M11")
**Status:** NOT STARTED — scoped 2026-07-27.
**Roadmap:** filed under the M11 cross-cutting backlog item "Character water crossings"
(`docs/roadmap.md`, Cross-cutting backlog section).

> Every phase worker reads this file first, then only their phase doc.

---

## What this story is

Characters currently cannot leave the landmass they're born on: `UtilityScorer.BestAdjacentTile`
and `StepToward` both hard-gate on `world.IsLand(coord)`, and `LegendaryBeast` pathfinding has the
same gate. Continents separated by ocean are permanently isolated — no character, migration, or
civ ever crosses. This story gives characters a way to cross water, without touching beast
movement (out of scope — see "Design decisions" below).

## Design decisions (resolved 2026-07-27, with the user, before scoping phases)

### DECISION: gated behind capability, not ambient behavior

Three shapes were on the table: (a) any coastal character can hop a narrow strait for free, (b)
general shallow-water pathfinding available to everyone, (c) crossing requires a civ to have built
port infrastructure first. **Chosen: (c).** A civ builds a `Port` improvement on a coastal
settlement; only characters of that civ can then be assigned a sea voyage, and only when a ruler
delegates one (mirrors the existing `FoundCity`/emigration delegation pattern — see 11.2). This
makes sea voyages a deliberate civ-level investment, consistent with how founding a city already
works, rather than a background character quirk.

### DECISION: characters only — beast movement is untouched

`LegendaryBeast` keeps its current `IsLand`-only wander/flee/hunt pathfinding
(`Entities/Beasts/LegendaryBeast.cs`). Smaller blast radius; most legendary beasts aren't
thematically sea-faring anyway. Revisit only if a future beast species is explicitly designed to
be aquatic/amphibious.

### DECISION: crossings always succeed (for now)

No failure/drowning mechanic in this story. `// V2: sea voyage failure (weather, sea monsters)` —
leave the hook as a comment where the voyage resolves (11.1), per `CLAUDE.md`'s stub-don't-build
rule for future features. A later milestone can wire storms/sea-monster encounters into that seam
without touching the voyage command's shape.

### DECISION: on by default

`SeafaringConfig.OceanCrossingEnabled` defaults to `true` (config-gated, not hardcoded — Mandatory
Pattern #2). New worlds get seafaring immediately; this changes baseline history generation for new
seeds, which is expected and fine for a new sim mechanic (not an authoring/God-Mode action, so the
CLAUDE.md determinism guarantee that "a run with no authoring reproduces byte-for-byte" doesn't
apply here — this changes the un-authored baseline itself, same as any other new mechanic would).

## Phase sequence

| Phase | Depends on | One-line deliverable |
|-------|-----------|----------------------|
| 11.0 | — | `Port` improvement type (coastal-only), `SeafaringConfig`, and a shallow-ocean tile classification helper on `WorldState`. No behavior yet — just the substrate. |
| 11.1 | 11.0 | `EmbarkVoyage` command + `GoalType.SeaVoyage`, route computation over shallow-ocean tiles, resolution wired into `CharacterBehaviorPhase`, new `SeaVoyage*` events. A character with the goal and a route can now actually cross water tile-by-tile. |
| 11.2 | 11.1 | Ruler delegation (overcapacity + landlocked civ + owns a Port → seed `SeaVoyage` goal, mirroring the existing emigration/`FoundCity` seed in `CharacterBehaviorPhase`), `UtilityScorer` candidate wiring, Spotlight intent bias case. |
| 11.3 | 11.2 | Persistence round-trip, event log display, doc regeneration, close-out. |

Do not start a phase until its dependencies are merged and green (`scripts/test-fast.sh`).

## Non-negotiable constraints (every phase)

From `CLAUDE.md`:
1. All new tunables (crossing toggle, max voyage range, Port build cost) go in `SimConfig`/
   `sim_config.toml` — never hardcoded.
2. `EmbarkVoyage` is a sealed record, value-type fields only, resolved only in the RESOLVE step
   (Mandatory Pattern #1/#5) — no direct `WorldState` mutation from behavior/scoring code.
3. `WorldEngine.Sim` stays headless throughout; only 11.3's event-log display touches
   `WorldEngine.UI`.
4. Every changed/added behavior needs a test; the reproducibility test must still pass with
   seafaring enabled (same seed ⇒ same voyages, same routes, same outcomes).
5. New `EventType` values are additive only — never renumber existing ranges (locked comment at
   the top of the enum).

## Open implementation details left to each phase

Per `CLAUDE.md`'s "How to Handle Ambiguity" — these are within-phase judgment calls, not
cross-cutting ones, so each phase makes the simplest reasonable choice and leaves a `// DECISION:`
comment rather than blocking:

- **11.0:** exact shallow-ocean classification (ocean/coastal-water tile adjacent to ≥1 land
  tile, computed via existing tile-radius helpers vs. a new cached flag) and `Port` build
  cost/time relative to the existing `Farm`/`Mine`/etc. improvements.
- **11.1:** route search bound (max shallow-ocean tiles from the Port's shoreline to the nearest
  reachable far-shore coastal tile) and which `EventType` numeric range to claim (propose a new
  5100-series, adjacent to the existing 5000-series emissary events, since a sea voyage is
  thematically a long-range journey like an emissary dispatch).
- **11.2:** exact "landlocked" test (civ has no reachable unclaimed frontier tile on its own
  landmass) — reuse whatever reachability/flood-fill utility already backs
  `ColonyMinDistance`/`GlobalSettlementMinDist` checks if one exists, otherwise add the minimal
  one needed.
