# Phase 3.2 — Cultural Identity & Significance

**Milestone:** 3 — Narrative Exploration  
**Status:** PLANNED  
**Goal:** Give civilizations emergent cultural identities based on their history, and improve significance classification so the narrative layer can identify what's actually important.

---

## Epic 3.2.1 — Civilisation Cultural Traits

**Goal:** Civs develop descriptive cultural traits over time based on their behavioral history. These traits appear in event records and are queryable by the narrative layer.

**Why:** All 5 surviving civs in the 5876-year run are mechanically identical except for location. Arlen declared 158 wars; Faelindra declared 0. Nothing in the data explains why — no cultural tag marks Arlen as "Militaristic" or Faelindra as "Isolationist." The narrative layer has no basis for characterizing them differently.

### Stories

**3.2.1.1 — CulturalTrait Enum and CivTraits Table**

```csharp
public enum CulturalTrait
{
    Militaristic,    // high war frequency
    Expansionist,    // high settlement founding rate
    Mercantile,      // high merchant trade volume
    Scholarly,       // high scholar discovery rate
    Reclusive,       // low inter-civ contact
    UnstableThrone,  // high succession rate
    WarWeary,        // repeated war exhaustion cooldowns triggered
    Resilient,       // survived multiple near-collapses
}
```

```sql
CREATE TABLE CivTraits (
    CivId   INTEGER,
    Trait   TEXT,
    YearSet INTEGER,   -- year the trait was first assigned
    PRIMARY KEY (CivId, Trait)
);
```

**3.2.1.2 — Annual Trait Evaluation**

In `CivTracker.RunAnnualDiplomacy`, after war resolution, run a trait evaluator each year on each civ. Each trait has a threshold — when a civ's metrics cross the threshold, the trait is assigned and fires a `CivTraitAcquired` event.

Examples:
- `Militaristic`: total wars > 10 and wars-per-decade > 2
- `Expansionist`: settlement founding rate > 1 per 10 years sustained for 30 years
- `WarWeary`: `WarExhaustionYearsPerWar` cooldown has been triggered against the same enemy 3+ times
- `Resilient`: civ survived an episode where `TotalPopulation < 20` (near-collapse)

Traits are permanent once assigned (history doesn't un-happen). Add new `EventType.CivTraitAcquired = 3900+`.

**3.2.1.3 — Trait Propagation to Event Payloads**

When a `WarDeclared` event fires, include the declaring civ's active trait list in the payload: `declarerTraits: ["militaristic", "expansionist"]`. Same for `SuccessionOccurred`, `SettlementFounded`, and major events. This means the narrative layer doesn't need to do a separate trait lookup for common events.

---

## Epic 3.2.2 — Significance Rescoring

**Goal:** The current significance classifier assigns static tiers (Background/Regional/Character/Headline) at event creation time with simple rules. Improve it to retroactively rescore events based on what happened later — a founding event becomes more significant if that settlement grew into a major city.

**Why:** Narrative generation needs to know what the most significant events in a character's life were. The current system often misses the narrative weight of events that only become important in retrospect.

### Stories

**3.2.2.1 — Retroactive Significance Pass**

Run after simulation completes (or on-demand before narrative generation). For each event, check:
- `SettlementFounded`: upgrade to Headline if this settlement is still alive 500 years later with population > 1000
- `CharacterBorn`: upgrade to Character tier if character went on to be a ruler for > 50 years
- `WarDeclared`: upgrade to Headline if the war resulted in conquest
- `ScholarDiscovery`: upgrade to Headline if the discovery type was one that benefited the civ significantly (bonus accumulated > 0.5)

Write upgraded tiers back to the Events table.

**3.2.2.2 — Significance Score (float) on Events**

Add a `SignificanceScore REAL` column to the Events table (0.0–1.0). This is separate from `TierInvolvement` (coarse enum) and enables sorting "what are the 10 most significant events of this character's life" by float value rather than just tier bucket.

Scoring rules (additive):
- Base score by tier: Background=0.1, Regional=0.3, Character=0.5, Headline=0.8
- +0.1 if `IsFirstOfKind`
- +0.1 if the character involved is a civ ruler
- +0.2 if the event has > 2 downstream causal edges
- +0.1 if the event involves a character that later became famous (has Headline events downstream)

---

## Definition of Done

- `CivTraits` table populated with correct trait assignments for all civs in a test run
- `CivTraitAcquired` events fire at the right threshold crossings
- Trait lists appear in `WarDeclared` and `SuccessionOccurred` payloads
- `SignificanceScore` column exists and is populated
- Retroactive rescore changes at least 5% of events in a 5000-year run (sanity check that it's doing something)
- All tests pass
