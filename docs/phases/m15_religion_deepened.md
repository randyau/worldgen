# M15 — Religion, Deepened

**Status:** IN PROGRESS — started 2026-08-06. 15.0 (Religion becomes a real Organization,
`config/religions.toml` archetypes, leader succession, extinction sink) shipped 2026-08-06. 15.1
(conversion via exposure — personal-receptivity gate, Zealotry-weighted pull, existing-Loyalty
resistance) shipped 2026-08-06. 15.2 (schism — low-average-Loyalty membership splits off under a
dissenting leader into a newly-founded sect, reusing the founding identity-roll so schismatic
sects can genuinely doctrinally drift to a different archetype) shipped 2026-08-06. 15.3
(heresy & persecution — civ state-religion plurality determination, Zealotry-gated soft
consequences only: civ-Loyalty penalty or forced conversion, never violence) shipped 2026-08-06.
15.4 (pilgrimage — new GoalType.Pilgrimage built like M11's SeaVoyage; devout members travel to
their religion's HomeSettlementCoord for a Spiritual/Loyalty boost) shipped 2026-08-06. 15.5
(religious leader UI/event-log parity — live "Religion: X (Leader/Member)" line in the Watch
panel mirroring the existing CivName line, plus event labels for all M15 events across
CharacterProfilePanel/EventLogPanel/Presenter) shipped 2026-08-06.

See `docs/roadmap.md` § "M15" for the one-line scope statement: schism/heresy/pilgrimage;
religious leaders as a third power track alongside rulers/merchants.

## Kickoff decisions (2026-08-06)

- **Membership/conversion:** exposure + chance, moderated by character stats (Piety, Wonder,
  Curiosity favor conversion; existing-religion Loyalty resists it) — not automatic enrollment,
  not a player-facing goal. A civ/settlement can host multiple coexisting religions; a character
  converts to one of the religions they're exposed to, weighted by local presence.
- **Heresy/persecution:** soft consequences only (unrest/loyalty/trust penalties, exile) —
  explicitly short of holy war, per roadmap.
- **Political power:** narrative/event-only this milestone — religious leaders get succession,
  schism, pilgrimage, and UI parity with Guild heads, but don't yet mechanically move civ
  stability/unrest. Mirrors M14 explicitly deferring "wealth buys political influence."
- **Content:** new `config/religions.toml` with authored archetypes (deity names, tenets,
  naming style) — analogous to `ancestries.toml`, not purely procedural.

## Long-run balance constraints (added 2026-08-06, binding on 15.1-15.3)

Same "sinks and sources" framing as M14's economy: religion population dynamics need an explicit
source *and* sink or a 10,000-year run degenerates into either "everyone always agnostic" or "one
religion converts the world." Four constraints, each mapping to a specific mechanism:

1. **Agnosticism must be a stable end-state, not a slow-converging transient.** Conversion chance
   needs a hard personal-receptivity gate (Piety/Wonder/Curiosity pulling up, Rationality and a
   baseline-skepticism term pulling down) that clamps to exactly 0, not asymptotically small — a
   population of low-Piety/high-Rationality characters must be able to stay genuinely unaffiliated
   forever, not merely "very slowly" convert.
2. **Coexistence needs symmetric resistance, not first-mover-wins.** A character's existing
   religion Loyalty (grows with tenure/pilgrimage, 15.4) resists conversion pressure from exposure
   to other religions. Archetype affinity (already in `ReligionArchetypeConfig`) means a
   heterogeneous population splits across multiple archetypes rather than converging on whichever
   religion happened to reach them first — this is the main lever for multi-religion settlements.
3. **No global monoculture.** Two structural sinks: (a) exposure is local (same-settlement/civ
   population), so a religion can't reach a distant civ without a contact vector — reuse the
   existing `ReligiousEmissaryArrived` (5003) event as the cross-civ seeding mechanism rather than
   assuming instant global exposure; (b) `ReligionExtinct` (4004, currently unused) is wired as a
   real sink when membership hits zero; (c) schism (15.2) probability scales with the membership's
   average archetype-affinity mismatch (people who joined via exposure pressure rather than true
   personality fit) — large religions accumulate mismatch and fragment, which is also thematically
   the right trigger for schism.
4. **Archetype `Zealotry` (-1 tolerant/syncretic .. +1 militant/exclusive)** modulates both
   conversion behavior and persecution intensity (15.3): high-Zealotry religions pressure/suppress
   rivals within their home civ more but alienate outside populations (weaker cross-civ appeal via
   the emissary vector above); low-Zealotry religions spread more gently but never suppress rivals,
   which is exactly what sustains long-term coexistence for that archetype family.

15.6's balance pass instrumented long-run test is where these four get empirically checked (target
bands: nonzero permanently-agnostic population share, 2+ religions coexisting per large civ,
no single religion above some world-population-share ceiling after 3000+ years).

## Phase sequence

- **15.0 — Religion becomes a real Organization.** `ReligionFounded` constructs an
  `Organization(Kind: Religion)` (previously a bare flavor event — `OrganizationKind.Religion`
  existed since M12 but nothing ever instantiated it). New `config/religions.toml` archetypes.
  Leader succession via `SuccessionResolver` (unmodified, per the M12 audit note this milestone
  is bound by). New `EventType.ReligiousLeadershipTransferred`.
- **15.1 — Conversion via exposure.** Per-settlement religion presence scan; stat-moderated
  conversion rolls; multi-religion coexistence within one civ/settlement.
- **15.2 — Schism.** Reuses the `CivSplintered` pattern at the Organization-membership level.
- **15.3 — Heresy & persecution.** State-religion plurality per civ; soft consequences for
  minority-religion members.
- **15.4 — Pilgrimage goal.** New `GoalType.Pilgrimage`, built like M11's `SeaVoyage`.
- **15.5 — Religious leader UI + power-track parity.** Religion panel, UI/history parity with
  Guild heads.
- **15.6 — Balance pass + invariant tests.**
