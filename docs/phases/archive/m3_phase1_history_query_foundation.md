# Phase 3.1 — History Query Foundation

**Milestone:** 3 — Narrative Exploration  
**Status:** PLANNED  
**Goal:** Build the data infrastructure that narrative generation requires: pre-indexed summaries, succession chains, causal edges, and the query API that the narrative layer calls.

Narrative generation cannot work over 600K+ raw events. This phase builds the aggregation and indexing layer that converts the raw event log into structured, queryable history. Everything in Phase 3.2 (UI) and beyond depends on this.

---

## Prerequisites (already done before Phase 3.1 begins)

The following were implemented as "before M3" fixes during the M2→M3 transition:

- **Name ordinals**: characters carry `IdentityData.NameOrdinal` (0 = first bearer, 1 = II, etc.) and `RulerOrdinal` (Nth ruler of their civ). Both assigned at spawn time via `WorldState.ClaimNameOrdinal()` and `Civilization.RulerCount`.
- **SuccessionOccurred payload**: now includes `predecessorRulerOrdinal` and `successorRulerOrdinal`.
- **WarDeclared payload**: includes `causeDescription` (human-readable) and `warNumber` (Nth war between this pair).
- **WarEnded payload**: includes `outcome` (truce/surrender/conquest/destruction) and `warNumber`.
- **BattleOccurred payload**: includes `raidOutcome` (damaged/critical_damage/conquest), `raiderWounded`, `raiderHealthPct`.
- **ScholarDiscovery payload**: includes `discoveryType` (enum) and `bonusKey`.
- **ArtworkCreated payload**: includes `artType` (enum).

---

## Epic 3.1.1 — CharacterSummary and CivSummary Tables

**Goal:** Pre-aggregate per-character and per-civ statistics into SQLite tables so the narrative layer can retrieve a character's full profile in a single indexed query.

**Why:** The event log is a flat time-series. Reconstructing "who was the most important ruler of Arlen?" requires scanning 600K events. These tables pre-compute that.

### Stories

**3.1.1.1 — CharacterSummary Table**

Schema (SQLite):
```sql
CREATE TABLE CharacterSummaries (
    CharacterId     INTEGER PRIMARY KEY,
    Name            TEXT NOT NULL,
    Epithet         TEXT,
    NameOrdinal     INTEGER DEFAULT 0,   -- 0 = first, 1 = "II", etc.
    AncestryId      TEXT,
    CivId           INTEGER,
    CivName         TEXT,
    RulerOrdinal    INTEGER DEFAULT 0,   -- Nth ruler of their civ (0 = not a ruler)
    BirthYear       INTEGER,
    DeathYear       INTEGER,
    DeathCause      TEXT,
    AgeSeasons      INTEGER,
    WarsInitiated   INTEGER DEFAULT 0,
    SettlementsFounded INTEGER DEFAULT 0,
    ArtworksCreated INTEGER DEFAULT 0,
    SignificantEvents TEXT  -- JSON array of top EventIds by significance
);
```

Populate from event log at end-of-sim or on demand. Maintain incrementally in a future optimization pass.

**3.1.1.2 — CivSummary Table**

Schema:
```sql
CREATE TABLE CivSummaries (
    CivId               INTEGER PRIMARY KEY,
    Name                TEXT NOT NULL,
    FoundedYear         INTEGER,
    CollapseYear        INTEGER,
    IsCollapsed         INTEGER,
    PeakSettlements     INTEGER,
    TotalRulers         INTEGER,
    TotalWarsInitiated  INTEGER,
    TotalWarsSuffered   INTEGER,
    TotalYearsAtWar     INTEGER,
    DominantAncestry    TEXT,
    CulturalTraits      TEXT,  -- JSON array of trait tags (see Phase 3.3)
    FirstRulerName      TEXT,
    LastRulerName       TEXT
);
```

**3.1.1.3 — EraTag System**

Assign named eras to time ranges based on event density and character activity. Examples: "The Founding Age" (Years 1–50), "The Plague Decades" (dense disease cluster), "The Long Silence" (sparse events), "The War of Endless Truces" (Arlen/Pella era).

- Run as a post-sim pass, stored in a new `Eras` table.
- Era names generated deterministically from event cluster analysis (no LLM needed — rule-based).
- Characters and events get an `EraTag` FK so narrative can say "during the Founding Age."

---

## Epic 3.1.2 — Succession Chain Index

**Goal:** Pre-index the succession chain per civ into a queryable linked list so "the 47th ruler of Arlen's Domain" is a fast lookup.

### Stories

**3.1.2.1 — SuccessionChain Table**

```sql
CREATE TABLE SuccessionChain (
    CivId       INTEGER,
    Ordinal     INTEGER,   -- 1 = founder, 2 = second ruler, ...
    CharId      INTEGER,
    Name        TEXT,
    BirthYear   INTEGER,
    TookThroneYear  INTEGER,
    LostThroneYear  INTEGER,
    LostThroneReason TEXT,  -- "death_old_age", "death_wounds", "conquest", "abdication"
    PRIMARY KEY (CivId, Ordinal)
);
```

Built from `SuccessionOccurred` and `CharacterDied` events. The `RulerOrdinal` field on `IdentityData` (set at succession time) makes this trivial to populate.

**3.1.2.2 — Dynastic Group Detection**

Consecutive rulers with the same ancestry and similar personality trait clusters form a "dynasty." Tag succession chain segments as dynasties in a `Dynasties` table. Even without family tracking, shared ancestry + high Loyalty/Stability in consecutive rulers signals a stable lineage.

---

## Epic 3.1.3 — Causal Edge Population

**Goal:** Populate the `CausalEdges` table (currently empty) with meaningful cause-effect relationships.

**Why:** The narrative layer needs to say "because of X, Y happened" — not just list events in year order.

### Stories

**3.1.3.1 — Automatic Causal Edges**

Wire causal edges at event-creation time for high-value relationships:
- `DiseaseOutbreak` → `SettlementAbandoned` (if abandonment follows disease within 3 years)
- `WarDeclared` → `BattleOccurred` (link all battles to the war declaration that preceded them)
- `BattleOccurred` → `SettlementConquered` (if raid leads to conquest)
- `CharacterDied` → `SuccessionOccurred` (immediate successor link)
- `CharacterDied` → `CharacterGrieved` (mourner events caused by the death)
- `GoalFormed(Avenge)` → the death that triggered it

These edges are written to `CausalEdges` in the same `PendingEvent` batch as the triggering event.

**3.1.3.2 — War Causal Chains**

Each war gets a chain: `WarDeclared` → each `BattleOccurred` during the war → `WarEnded`. This chain is the atomic unit for narrative: "The Third War between Arlen and Pella (Year 2140) began when border tension peaked, saw three raids on Pella's capital, and ended in truce after Arlen's ruler was gravely wounded."

---

## Epic 3.1.4 — HistoryQuery API (B7)

**Goal:** Expose a `HistoryQuery` service that pre-indexes and answers structured historical queries without scanning raw events each call.

**Contract (WorldEngine.Sim namespace):**

```csharp
public interface IHistoryQuery
{
    CivSummary?          GetCivSummary(CivId civId);
    CharacterSummary?    GetCharacterSummary(EntityId charId);
    IReadOnlyList<CharacterSummary> GetRulersOfCiv(CivId civId);   // ordered by succession
    CharacterSummary?    GetRulerAtYear(CivId civId, int year);
    IReadOnlyList<SimEvent> GetCivHistory(CivId civId, int startYear, int endYear);
    IReadOnlyList<SimEvent> GetCharacterHistory(EntityId charId);
    IReadOnlyList<SimEvent> GetSignificantEvents(int startYear, int endYear, EventTier minTier);
    IReadOnlyList<ConflictRecord> GetConflictHistory(CivId civA, CivId civB);
    IReadOnlyList<CharacterSummary> FindCharactersByName(string name);   // returns disambiguation list
}
```

**Implementation:** `HistoryQueryService` backed by SQLite queries against the summary and chain tables built in 3.1.1–3.1.3. Not a full ORM — Dapper queries against pre-indexed tables. Cache hot queries (most-recently-accessed civ/character) in a small in-memory LRU.

**Name disambiguation:** `FindCharactersByName("Caelen")` returns all characters named Caelen ordered by birth year, with ordinal and civ for disambiguation. The display name is `"Caelen" + ordinal suffix + epithet` — e.g., "Caelen III the Brave of Arlen's Domain."

---

---

## Epic 3.1.5 — Performance Gate

**Goal:** Establish a minimum TPS baseline before narrative UI work begins. The history
query and narrative layers are only useful if the sim can reach year 2000+ in a
reasonable wall-clock time. Profile the sim at this phase boundary and fix the top
hotspots.

**Target:** ≥ 400 TPS sustained from year 100 to year 500 on the reference machine,
with ≤ 15 active Tier1 characters and ≤ 30 settlements.

### Stories

**3.1.5.1 — Profiling run**

Run a 500-year simulation with profiling enabled (dotnet-trace or BenchmarkDotNet).
Capture a flamegraph. Identify the top 3 hotspots by self-time. Document findings
in a short comment in `docs/perf/notes_m3.md`.

**3.1.5.2 — Fix top hotspot**

Implement the single highest-impact fix identified in 3.1.5.1. Common candidates based
on known architecture:
- `O(settlements²)` border tension scan — bucket settlements by civ pair first
- `SnapshotBuilder` building large dicts every tick — use double-buffer or delta approach
- `RelationshipGraph` linear scans — add secondary index by entity
- SQLite batch writes blocking sim thread — increase batch size or move to pure async

**3.1.5.3 — Re-measure and gate**

Re-run the 500-year profiling run. If ≥ 400 TPS is achieved, mark the gate passed
and proceed to Phase 3.2. If not, pick the next hotspot and repeat. Document the
before/after in `docs/perf/notes_m3.md`.

---

## Definition of Done

- `CharacterSummaries` and `CivSummaries` tables exist and are populated for a 5000-year run
- `SuccessionChain` table correctly orders all rulers for all civs
- `CausalEdges` table contains at least war chains and disease→abandonment edges
- `IHistoryQuery` is implemented and passes integration tests
- `GetCharacterHistory(charId)` returns ordered events with causal context for sampled characters
- `FindCharactersByName` returns correct disambiguation lists for recycled names
- Performance gate: ≥ 400 TPS sustained over years 100–500
- All tests pass; no new sim warnings
