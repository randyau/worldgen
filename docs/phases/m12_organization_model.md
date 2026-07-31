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
- **12.1 — Migrate civ diplomacy onto Organization relationship state. DONE (2026-07-31).**
  Scope note: on inspection, `Civilization.WarsAgainst`/`BorderTension`/`PeaceTreaties` were
  already civ-level facts (never derived from the ruler pair), and are deeply entangled with
  territory transfer/conquest/population — squarely "war mechanics themselves" per the roadmap's
  scope boundary, so they stay on `Civilization`. The actual fragility design decision 1 names
  ("assassinate the ruler, alliance evaporates") lives entirely in the *alliance* fact: it was
  read/written as the current ruler pair's `RelationshipEdge.IsAlly`
  (`CivTracker.Diplomacy.cs` `RunBorderTension`'s peace-check, the annual dissolution loop) —
  so a new ruler with no relationship history to the other side looked unallied even though the
  alliance was never broken. Fixed by adding `Organization.Allies` (a `HashSet<OrganizationId>`,
  landed in 12.0) as the independent org-to-org fact: `ResolveDiplomacy` (emissary path) and
  `ResolveAlly` (ruler-pair `AllyWith` case) now call `FormOrgAlliance` alongside the existing
  ruler-edge `IsAlly` flag; `RunBorderTension`'s peace-check reads `Organization.IsAllyOf`
  instead of the ruler edge; a new `RunOrgAllianceDissolution` (called from
  `RunAnnualDiplomacy`) breaks the org-level alliance only when the *current* rulers on both
  sides exist and their trust has decayed below the floor — a vacant/succeeding seat simply
  skips that year instead of the alliance silently lapsing; `StartWarBetween` also breaks the
  org alliance on war declaration. `Organization.LeaderId` is kept mirrored to
  `Civilization.RulerId` at both succession call sites in `CharacterBehaviorPhase.cs` (a
  narrow sync, not the full vacant-seat/heir-pool machinery — that generalization is 12.3).
  See `WorldEngine.Tests/Unit/OrganizationDiplomacyTests.cs` for the succession-survives-alliance
  regression test. `Organization` (including `Allies`) and `Civilization.OrgId` are now
  persisted via `OrganizationDto`/`WorldStateMapper` — see
  `WorldEngine.Tests/Integration/SaveLoadTests.cs`
  `WorldStateSaver_RoundTrip_Organizations` — since the alliance fact is now load-bearing
  behavior, not just additive state, per CLAUDE.md's "disk as system of record" rule.
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
