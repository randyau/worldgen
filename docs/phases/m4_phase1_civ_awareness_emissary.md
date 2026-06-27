# Phase 4.1 — Civ Awareness & Emissary System

**Milestone:** 4 — World Dynamics
**Status:** PLANNED — 2026-06-27
**Goal:** Civilizations learn about each other through proximity, wanderer encounters, and
rumor chains, then act on that knowledge by dispatching abstract emissaries for trade,
diplomacy, espionage, and religious outreach. This unblocks the majority of the simulation
balance issues catalogued in `SIMULATION_ISSUES_ANALYSIS.md` — particularly the isolation
problem that suppresses wars, alliances, trade, and religion spread.

---

## Background & Motivation

The existing `RunBorderTension` system only fires when two civs' settlements are within
`WarProximityRadius` tiles of each other. With 20 civs spread across a world map, most
civs are never physically adjacent, so inter-civ tension, diplomacy, trade, and alliance
events are near-zero. There is no mechanism for a civ to **know another civ exists** unless
one of their wandering characters stumbles into that civ's territory at random.

**Evidence from a 2150-year production run:**
- WarDeclared: 29 (0.013/civ/year; goal: 1-3/civ/century)
- AllianceFormed: 10 (goal: 50+)
- MerchantTradeCompleted: 85 (goal: 10-50/civ/year with routes)
- ReligionFounded: 0 (subsystem requires spread vectors)
- CivTraitAcquired: 0 (militaristic threshold requires 10 wars/civ; impossible at current rate)

This phase introduces three interlocked systems: a **knowledge registry** on each civ,
a **propagation engine** that fills it, and an **emissary dispatch+resolution** loop that
acts on it.

---

## Design Decisions (confirmed 2026-06-27)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Capital location precision | Exact tile | Simpler than direction-based; precision question is Spotlight-mode concern (M5) |
| Emissary mortality | Yes — per-tile-distance death roll, capped at 80% max mortality (always ≥20% success) | Narrative value; lost envoys are historically real |
| Mortality cap config key | `emissary_min_survival_chance = 0.2` | Tunable without recompile |
| Character movement toward foreign civs | Punt to future story | Emissaries abstract the travel; individual characters stay local |
| Rumor chaining | Yes, included | Lets knowledge of distant civs propagate across the world |

---

## Scope

**In scope:**
- `CivContact` record and `KnownCivs` dict on `Civilization`
- Proximity-rumor propagation (settlement scan, annual)
- Character-encounter contact seeding (hook into existing encounter handler)
- Rumor chaining (indirect knowledge propagation, annual)
- `PendingEmissary` record in `WorldState`
- Emissary dispatch logic in `CivTracker.RunAnnualDiplomacy`
- Emissary resolution: trade, diplomacy, spy, religious outcomes
- Emissary mortality: `EmissaryLost` event
- New config keys in `[emissary]` section of `sim_config.toml`
- Serialization: `KnownCivs` and `PendingEmissaries` added to `WorldStateDto`
- Tests for each story

**Out of scope:**
- Characters physically pathing toward foreign civs (future story)
- Full diplomatic UI (M5 God Mode)
- Trade route simulation (future)
- Emissary portraits or named envoys (Spotlight, M5)

---

## Data Model

### `CivContact` (new record)

```csharp
/// <summary>
/// What one civ knows about another — how they learned of it, where the capital is,
/// and how confident the knowledge is. Confidence decays without refresh.
/// </summary>
public sealed record CivContact(
    CivId KnownCivId,
    int YearFirstContact,
    int YearLastContact,
    CivContactSource BestSource,   // highest-fidelity source seen so far
    TileCoord CapitalTile,         // exact tile — updated on EmissaryExchange or War contact
    float Confidence               // 0.0 = rumor almost forgotten, 1.0 = well-known
);

public enum CivContactSource
{
    Rumor          = 0,   // heard via proximity or chaining; lowest fidelity
    WandererMet    = 1,   // a wandering character had a cross-civ encounter
    EmissaryExchange = 2, // a dispatched emissary returned with direct knowledge
    War            = 3,   // at war with this civ; highest confidence
}
```

### Changes to `Civilization`

```csharp
// Add to Civilization.cs:

/// <summary>
/// Civs this civ has knowledge of. Keyed by the known civ's id.
/// Populated by KnowledgePropagationPhase; read by emissary dispatch.
/// </summary>
public Dictionary<CivId, CivContact> KnownCivs { get; } = new();

/// <summary>
/// Emissaries currently in transit dispatched by this civ.
/// Stored here for per-civ cap checks; canonical list is WorldState.PendingEmissaries.
/// </summary>
public Dictionary<CivId, int> ActiveEmissaryCountByTarget { get; } = new();
```

### `PendingEmissary` (new record, stored in `WorldState`)

```csharp
public sealed record PendingEmissary(
    CivId FromCiv,
    CivId ToCiv,
    EmissaryPurpose Purpose,
    int DepartedYear,
    int ArrivalYear,      // DepartedYear + ceil(distance / emissary_travel_speed_tiles_per_year)
    float SurvivalChance  // pre-computed at dispatch; clamp(1 - dist * death_per_tile, min_survival)
);

public enum EmissaryPurpose { Trade, Diplomacy, Spy, Religious }
```

### `WorldState` additions

```csharp
// In WorldState:
public List<PendingEmissary> PendingEmissaries { get; } = new();
```

---

## Config (`sim_config.toml` — new `[emissary]` section)

```toml
[emissary]
# Knowledge propagation
knowledge_spread_radius         = 30    # tiles; civs within this range gain Rumor contact (vs WarProximityRadius ~8)
rumor_confidence_gain           = 0.15  # confidence added per year of proximity rumor
encounter_confidence_gain       = 0.35  # confidence added on character cross-civ encounter
confidence_decay_per_year       = 0.05  # lost per year without contact
rumor_chain_probability         = 0.05  # annual chance Civ A passes knowledge of Civ C to Civ B (per pair)
rumor_chain_confidence_factor   = 0.5   # chained rumors arrive at fraction of source confidence

# Emissary dispatch
dispatch_check_years            = 5     # ruler considers dispatching per this many years
max_active_emissaries_per_civ   = 3     # cap on simultaneous in-transit emissaries
emissary_travel_speed_tiles_per_year = 8.0  # how fast emissaries travel; affects delay and mortality
trade_dispatch_min_trust        = -0.1  # min character trust to send trade emissary
diplomacy_dispatch_min_trust    = 0.1   # min trust for diplomatic mission
spy_dispatch_max_trust          = 0.2   # spy missions target civs you don't trust well

# Emissary mortality
emissary_death_per_tile         = 0.008  # cumulative per-tile mortality rate
emissary_min_survival_chance    = 0.2    # floor: even a 200-tile journey has 20% success
                                         # effective formula: clamp(1 - dist * death_per_tile, min_survival, 1.0)

# Emissary outcomes (on arrival)
trade_trust_gain                = 0.08
trade_min_pop_for_goods         = 50     # both civs need this pop to meaningfully trade
diplomacy_alliance_min_trust    = 0.25   # trust required after emissary to trigger AllianceFormed
spy_confidence_boost            = 0.4    # how much the contact confidence improves from spy intel
religious_spread_awe_boost      = 0.3    # awe modifier added to target-civ chars on religious emissary
```

---

## Epic 4.1.1 — CivContact Data Layer

**Goal:** Add `KnownCivs` and `PendingEmissaries` to the data model and wire serialization.

### Story 4.1.1.1 — `CivContact` record + `KnownCivs` on `Civilization`

Add `CivContact`, `CivContactSource`, and `KnownCivs` to `Civilization.cs`.
Add `ActiveEmissaryCountByTarget` to `Civilization.cs`.
Add `PendingEmissary`, `EmissaryPurpose` to a new file `WorldEngine.Sim/Civilizations/EmissaryTypes.cs`.
Add `PendingEmissaries` list to `WorldState.cs`.

**Test:** Construct a `Civilization`, add a `CivContact`, assert it round-trips through
the dict and confidence clamps correctly.

### Story 4.1.1.2 — Serialization

Add `KnownCivs` and `PendingEmissaries` to `WorldStateDto` and the source-gen
serialization context (`WorldStateSerializerContext`). `CivId` is already a
serializable key type; `TileCoord` has converters.

**Test:** Add `KnownCivs` and `PendingEmissaries` to the existing `SaveLoad_ProducesIdenticalState`
round-trip test assertions.

### Story 4.1.1.3 — Config keys

Add `[emissary]` section to `sim_config.toml` with the values above.
Add `EmissaryConfig` record to `WorldEngine.Sim/Config/` and wire into `SimConfig`.
Verify `dotnet build` zero warnings.

---

## Epic 4.1.2 — Knowledge Propagation

**Goal:** Fill `KnownCivs` via three mechanisms: proximity rumor, character encounter, and
rumor chaining. Runs annually in a new `KnowledgePropagationPhase` called from the sim loop
after `CivTracker.RunAnnualDiplomacy`.

### Story 4.1.2.1 — Proximity Rumor Spread

```csharp
// KnowledgePropagationPhase.RunProximityRumors(world)
// For each pair of non-collapsed civs:
//   If any settlement of A is within knowledge_spread_radius tiles of any settlement of B:
//     A.KnownCivs.AddOrRefresh(B.Id, source=Rumor, capital=B.CapitalTile, +rumor_confidence_gain)
//     B.KnownCivs.AddOrRefresh(A.Id, source=Rumor, capital=A.CapitalTile, +rumor_confidence_gain)
// Cap confidence at 1.0.
// For all existing contacts without proximity refresh this year: decay confidence by confidence_decay_per_year.
// Remove contacts where confidence ≤ 0.
```

The settlement-pair scan is the same O(n_civs²) loop as `RunBorderTension`, but with a
larger radius. Reuse the `byCiv` dict already built there, or factor it out into a shared
helper.

**Test:**
- Two civs with settlements 25 tiles apart (within `knowledge_spread_radius = 30`): both gain `Rumor` contact after one propagation tick.
- Civs 50 tiles apart: no contact gained.
- After 10 ticks without proximity: confidence decays to zero and contact is removed.

### Story 4.1.2.2 — Character Encounter Contact Seeding

When two characters from different civs have a cross-civ encounter (the existing handler in
`CharacterBehaviorPhase` at the trust-drain/first-meeting point), add:

```csharp
// Both civs learn of each other at WandererMet confidence
SeedCivContact(charA.Identity.CivId, charB.Identity.CivId, CivContactSource.WandererMet,
               world.Civilizations[charB.Identity.CivId].CapitalTile,
               cfg.Emissary.EncounterConfidenceGain, world);
// symmetric
SeedCivContact(charB.Identity.CivId, charA.Identity.CivId, CivContactSource.WandererMet,
               world.Civilizations[charA.Identity.CivId].CapitalTile,
               cfg.Emissary.EncounterConfidenceGain, world);
```

`SeedCivContact` upserts: if contact already exists, upgrade `BestSource` if new source is
higher-fidelity, add confidence, update `YearLastContact`.

**Test:** Two characters from different civs on adjacent tiles → both civs gain `WandererMet`
contact with correct confidence and capital tile.

### Story 4.1.2.3 — Rumor Chaining

```csharp
// KnowledgePropagationPhase.RunRumorChaining(world, rng)
// For each civ A:
//   For each (CivB, contactAB) in A.KnownCivs:
//     For each (CivC, contactAC) in A.KnownCivs where C != B:
//       If B.KnownCivs does not contain C:
//         Roll rng < rumor_chain_probability:
//           B gains Rumor of C at confidence = contactAC.Confidence * rumor_chain_confidence_factor
```

Cap rumor chaining at one hop (don't chain from already-chained rumor contacts whose
source is `Rumor` from a previous chain tick). This prevents unbounded propagation while
still connecting distant networks.

**Test:**
- Civ A knows Civ B (WandererMet) and Civ C (WandererMet).
- After N ticks with 100% chain probability (test override): Civ B eventually learns of Civ C at reduced confidence.
- Confidence of chained rumor is `source_confidence * chain_factor`.

---

## Epic 4.1.3 — Emissary Dispatch

**Goal:** Rulers decide annually (every `dispatch_check_years`) whether to send an emissary
to a known civ, choosing purpose based on trust and civ state. Dispatching adds a
`PendingEmissary` to `WorldState.PendingEmissaries`.

### Story 4.1.3.1 — Dispatch Decision Logic

Add `RunEmissaryDispatch(world, pending)` to `CivTracker.Diplomacy.cs`, called from
`RunAnnualDiplomacy` when `world.CurrentYear % cfg.Emissary.DispatchCheckYears == 0`.

```
For each non-collapsed civ C:
  If C.ActiveEmissaryCountByTarget.Values.Sum() >= max_active_emissaries_per_civ: skip

  For each (KnownCivId, contact) in C.KnownCivs where contact.Confidence > 0.1:
    If target civ is collapsed: skip
    If already have active emissary to this target: skip
    If at war with target: skip (war contact updates happen differently)

    Determine purpose:
      ruler = GetRuler(C)
      trust = world.Relationships.Get(C.RulerId, targetCiv.RulerId)?.Trust ?? 0f

      if trust < spy_dispatch_max_trust && ruler.Personality.Cunning > 0.5 → Spy
      elif trust >= trade_dispatch_min_trust → Trade  (most common)
      elif trust >= diplomacy_dispatch_min_trust && !C.IsAtWarWith(target) → Diplomacy
      elif ruler.Personality.Piety > 0.6 → Religious
      else → skip (no suitable mission type)

    Compute distance from C.CapitalTile to contact.CapitalTile
    survivalChance = clamp(1f - dist * emissary_death_per_tile, min_survival_chance, 1f)
    arrivalYear = world.CurrentYear + (int)ceil(dist / emissary_travel_speed)

    Create PendingEmissary; add to WorldState.PendingEmissaries
    Increment C.ActiveEmissaryCountByTarget[targetCivId]
```

**Test:**
- Civ with high-trust known contact dispatches Trade emissary.
- Civ with low-trust known contact and cunning ruler dispatches Spy.
- Civ at cap (3 active) skips dispatch.
- Civ at war skips the enemy.

### Story 4.1.3.2 — `EmissaryDispatched` Event

Add event type `EmissaryDispatched` to `EventType` enum (range 5001+, new Diplomatic band).
Emit it when dispatch decision fires. Payload: `FromCivId`, `ToCivId`, `Purpose`, `ArrivalYear`, `SurvivalChance`.

This event is primarily for history log transparency — players can query "when did Civ A
first send an envoy to Civ B?"

---

## Epic 4.1.4 — Emissary Resolution

**Goal:** Each annual tick, check `PendingEmissaries` for arrivals and resolve outcomes —
mortality first, then purpose-specific effects.

### Story 4.1.4.1 — Mortality Roll

```csharp
// In CivTracker.RunAnnualDiplomacy (or a new RunEmissaryResolution helper):
foreach (var emissary in world.PendingEmissaries.Where(e => e.ArrivalYear == world.CurrentYear).ToList())
{
    world.PendingEmissaries.Remove(emissary);
    fromCiv.ActiveEmissaryCountByTarget[emissary.ToCiv]--;

    if (world.Rng.NextFloat() > emissary.SurvivalChance)
    {
        pending.Add(new PendingEvent(EventType.EmissaryLost, new EmissaryLostPayload(
            emissary.FromCiv, emissary.ToCiv, emissary.Purpose)));
        continue;
    }

    ResolveEmissaryArrival(emissary, fromCiv, toCiv, world, pending);
}
```

**Test:** With `SurvivalChance = 0.0f` (impossible journey), all emissaries fire `EmissaryLost`.
With `SurvivalChance = 1.0f`, none are lost.

### Story 4.1.4.2 — Trade Resolution

On successful Trade arrival:

```csharp
// Both civs need min pop
if (fromCiv.TotalPopulation < trade_min_pop_for_goods || toCiv.TotalPopulation < ...) → partial skip

// Fire MerchantTradeCompleted (existing event type — reuse)
pending.Add(new PendingEvent(EventType.MerchantTradeCompleted, ...));

// Update contact knowledge: upgrade to EmissaryExchange, bump confidence
SeedCivContact(fromCivId, toCivId, CivContactSource.EmissaryExchange,
               toCiv.CapitalTile, max_confidence, world);

// Trust bump between rulers
var rel = world.Relationships.GetOrCreate(fromCiv.RulerId, toCiv.RulerId);
world.Relationships.Upsert(rel with { Trust = clamp(rel.Trust + trade_trust_gain, -1f, 1f) });
```

**Test:** Successful Trade emissary fires `MerchantTradeCompleted`, bumps ruler trust, upgrades contact source to `EmissaryExchange`.

### Story 4.1.4.3 — Diplomacy Resolution

On successful Diplomacy arrival:

```csharp
// Trust bump (larger than trade)
// Check if trust now >= alliance_threshold → fire AllianceFormed (reuse existing path in CivTracker)
// Also check if the two civs are in BorderTension → emissary can defuse (reduce tension)
```

If trust crosses alliance threshold: call the existing `TryFormAlliance` path to get
all the downstream effects (transitivity, event emission) for free.

**Test:** Diplomacy emissary with rulers at trust 0.2 (just below alliance threshold 0.25)
bumps trust past threshold and fires `AllianceFormed`.

### Story 4.1.4.4 — Spy Resolution

On successful Spy arrival:

```csharp
// Upgrade contact confidence significantly
contact.Confidence = clamp(contact.Confidence + spy_confidence_boost, 0f, 1f);
contact.BestSource = max(contact.BestSource, CivContactSource.EmissaryExchange);

// Optionally: expose a civ trait (fire a CivIntelGathered event for history log)
// Optionally: reduce border tension if the spy observes weakness
```

Spy emissaries do NOT fire a visible event to the target civ — they are silent.

**Test:** Spy emissary increases source confidence. No event visible to target civ.

### Story 4.1.4.5 — Religious Resolution

On successful Religious arrival:

```csharp
// For each character in the target civ:
//   Add awe_boost to their Awe need (if they have one modeled)
//   This raises probability that one of them forms a FoundReligion goal
// Fire ReligiousEmissaryArrived event (new, EventType 5002 or similar)
```

This doesn't directly found a religion — it seeds the conditions that make founding more
likely. The existing religion goal-formation path handles the actual founding.

**Test:** After religious emissary arrival, target-civ characters have elevated awe scores.

---

## Epic 4.1.5 — Event Types & History Log

**Goal:** New events are queryable in history.

### New EventType entries needed

```csharp
// In EventType enum — add a Diplomatic band (5000s)
EmissaryDispatched  = 5001,
EmissaryLost        = 5002,
ReligiousEmissaryArrived = 5003,
CivIntelGathered    = 5004,   // optional spy intel event
```

These need:
- Payload records
- `always_record_types` entries in `sim_config.toml` for `EmissaryLost` (always narrative)
- `EventFormatter` entries for the history query UI

---

## Epic 4.1.6 — Integration & Tuning Tests

### Story 4.1.6.1 — Propagation Integration Test

Run a short sim (50 years, known seed, 4 civs arranged in a row: A-B-C-D where each is
30 tiles from the next). Assert:

- After 10 years: A knows B, B knows A, B knows C, C knows B, C knows D, D knows C
- After 20 years (rumor chaining): A eventually learns of C; C eventually learns of A
- D does not know A within 30 years (too many hops at 5% chain probability)

### Story 4.1.6.2 — Emissary End-to-End Test

Run a short sim (100 years, 2 civs within `knowledge_spread_radius`, high dispatch probability
by setting `dispatch_check_years = 1`). Assert:

- At least one Trade emissary dispatched within 10 years
- At least one `MerchantTradeCompleted` event in history within 20 years
- Ruler trust increased after trade completion

### Story 4.1.6.3 — Mortality Boundary Test

Parametric test across distances [5, 20, 50, 100, 200] tiles:

```
dist=5:   survival = clamp(1 - 5*0.008, 0.2, 1.0) = 0.96
dist=50:  survival = clamp(1 - 50*0.008, 0.2, 1.0) = 0.60
dist=100: survival = clamp(1 - 100*0.008, 0.2, 1.0) = 0.20 (hits floor)
dist=200: survival = 0.20 (capped at floor)
```

Assert the pre-computed `SurvivalChance` on the `PendingEmissary` matches expected values.

---

## File Checklist

| File | Change |
|------|--------|
| `WorldEngine.Sim/Civilizations/Civilization.cs` | Add `KnownCivs`, `ActiveEmissaryCountByTarget` |
| `WorldEngine.Sim/Civilizations/EmissaryTypes.cs` | New: `CivContact`, `CivContactSource`, `PendingEmissary`, `EmissaryPurpose` |
| `WorldEngine.Sim/Simulation/WorldState.cs` | Add `PendingEmissaries` |
| `WorldEngine.Sim/Civilizations/CivTracker.Diplomacy.cs` | Add `RunEmissaryDispatch`, `RunEmissaryResolution`, `SeedCivContact` |
| `WorldEngine.Sim/Simulation/Phases/KnowledgePropagationPhase.cs` | New phase: proximity rumor, encounter seed, rumor chaining |
| `WorldEngine.Sim/Simulation/SimLoop.cs` | Wire `KnowledgePropagationPhase` into annual tick |
| `WorldEngine.Sim/Config/EmissaryConfig.cs` | New config record |
| `WorldEngine.Sim/Config/SimConfig.cs` | Add `Emissary` property |
| `config/sim_config.toml` | Add `[emissary]` section |
| `WorldEngine.Sim/Core/EventType.cs` | Add 5001-5004 entries |
| `WorldEngine.Sim/Events/EventPayloads.cs` | Add payload records for new events |
| `WorldEngine.Sim/Persistence/WorldStateDto.cs` | Add `KnownCivs`, `PendingEmissaries` to DTO |
| `WorldEngine.Sim/Persistence/WorldStateSerializerContext.cs` | Register new types |
| `WorldEngine.UI/History/EventFormatter.cs` | Add formatters for new event types |
| `WorldEngine.Tests/CivAwarenessTests.cs` | New: all tests from epics 4.1.1–4.1.6 |

---

## Risk Notes

- **Performance:** Proximity-rumor scan is O(civs² × settlements²) in the worst case. The
  existing `RunBorderTension` has the same complexity and is already annual (Spring tick only).
  Run `KnowledgePropagationPhase` on the same Spring tick, reusing the `byCiv` dict built there.
  If it's slow in a 50-civ world, add spatial bucketing.

- **Serialization churn:** `KnownCivs` uses `Dictionary<CivId, CivContact>` which needs a
  JSON key converter for `CivId`. Check if `CivId`'s existing converter handles dict keys
  (some do, some only handle values). Write the round-trip test first to catch this early.

- **Rumor chaining runaway:** The "one hop only" rule (don't chain from Rumor-source contacts)
  prevents exponential propagation. Enforce it by checking `contact.BestSource != Rumor`
  before treating A's knowledge of C as eligible to chain to B.

- **Emissary purpose selection:** The priority order (Spy > Trade > Diplomacy > Religious) may
  need tuning after first runs. The dispatch logic should be easy to reorder. Log purpose
  distribution in the integration test to catch imbalances early.
