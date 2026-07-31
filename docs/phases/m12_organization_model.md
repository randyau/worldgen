# M12 — Organization Model

**Status:** IN PROGRESS — started 2026-07-31.

See `docs/roadmap.md` § "M12–M18 — Narrative Depth Expansion" for the full design rationale,
the 2026-07-30 user-confirmed decisions, and the code audit this milestone is built on
(`Civilization`/`CivTracker*.cs`/`RelationshipEdge` findings). This doc only tracks phase
sequencing and story-level notes; do not duplicate the rationale here.

Hard prerequisite for M13 (family), M14 (guilds), M15 (religion).

## Phase sequence

- **12.0 — Organization core entity + registry.** `OrganizationId`, `OrganizationKind`
  (Civilization/Guild/Religion/Family), `Organization` class (`LeaderId`, `Members` as
  `Dictionary<EntityId, Membership>` with `Role`/`Loyalty`, generalized org-relationship state:
  `WarsAgainst`/`BorderTension`/`PeaceTreaties`/`Allies`). `Organizations` dictionary on
  `WorldState`. Additive only — no existing `Civilization` behavior migrated yet. One
  `Organization` (Kind: Civilization) auto-created per `Civilization` at founding, kept in sync.
- **12.1 — Migrate civ diplomacy onto Organization relationship state.** Per design decision 1:
  alliance/war/peace becomes an org-to-org fact instead of being derived from the ruler pair's
  personal `RelationshipEdge.Trust`. Ruler trust becomes an input/lever, not the source of
  truth. `CivTracker.Diplomacy.cs`/`CivTracker.War.cs` migrate to read/write
  `Organization` state; `Civilization.WarsAgainst`/`BorderTension`/`PeaceTreaties` are removed
  once callers move over (no dual source of truth, no compat shim per CLAUDE.md).
- **12.2 — Multi-membership schema.** Replace single `IdentityData.CivId` with a membership set
  per character (`OrganizationId`, `Role`, `Loyalty`), migrate readers (UI panels, snapshot,
  persistence, `UtilityScorer`/`NeedsUpdater`). Per design decision 2, the *weighted-loyalty
  conflict-resolution scoring logic* in `UtilityScorer`/`GoalManager` is genuinely new design
  work the roadmap explicitly defers to land alongside M13 (family is the first real test case)
  — 12.2 only needs to land the schema plus a single-membership-equivalent default behavior so
  nothing regresses before that scoring work exists.
- **12.3 — Generalize leadership succession.** Vacant-seat/heir-pool/crisis-window pattern
  (`SuccessionCrisisEndYear`, DB `SuccessionChain`/`Dynasties`) moves to hang off
  `Organization.LeaderId` generically, civ rulers migrated onto it as the existing instance. No
  new consumers this milestone (family/guild/religion heads land in M13–M15) — the point is the
  machinery is reusable when they do.

## Scope boundaries (per roadmap)

`Civilization`-specific mechanics that aren't about membership/leadership/relationships
(territory, `CulturalProfile`, war *mechanics* themselves — combat resolution, territory
transfer) stay on `Civilization`. Only membership, leader seat, and org-relationship *state*
move up into `Organization`. `WorldEngine.Sim`-only — no new UI surface required, though
existing panels reading `CivId` need updating to read the membership set (12.2).
