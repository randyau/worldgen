# Phase 6 — Epic 1.6: Event System
**Status:** COMPLETE — 2026-06-22  
**Requires:** Phase 5 complete (PendingEvents must flow from Phase 1 to Phase 7)  
**Reads required:** `docs/interface_contracts.md` (SimEvent, EventTier, VerbClass, IHistoryGraphReadOnly), `docs/queries/event_log_queries.md` (for validation)

---

## Goal
Wire Phase 7 of the tick: classify PendingEvents into SimEvents, apply the gate, write to SQLite with causal edges, maintain the EventCache ring buffer, and expose history via IHistoryGraphReadOnly.

## Phase 7 Write Order
**This order is non-negotiable** (from GUARDRAILS.md Rule 7):
1. Write `SimEvent` batch to SQLite (world.db) — disk is system of record
2. Write `CausalEdge` rows for events with `CauseEventId` set
3. Add to `EventCache` — UI reads from cache, never from SQLite
4. Update `StateCache` — snapshot picks up new events

---

## Story 1.6.1 — SimEvent + EventType

**Files:**
```
WorldEngine.Sim/Events/SimEvent.cs      # sealed record, from interface_contracts.md
WorldEngine.Sim/Events/EventType.cs     # enum with explicit int values (stable across saves)
WorldEngine.Sim/Events/VerbClass.cs     # enum (or just use the one from Core/)
WorldEngine.Sim/Events/PendingEvent.cs  # sealed record (already defined in DS-C, move here)
```

**EventType must have explicit int values — they are stored in SQLite and must not change:**
```csharp
public enum EventType
{
    // Environmental (1000–1099)
    VolcanicEruption    = 1001,
    EarthquakeOccurred  = 1002,
    WildfireOccurred    = 1003,
    FloodOccurred       = 1004,
    DroughtBegan        = 1005,
    DroughtEnded        = 1006,
    SeaLevelChanged     = 1007,
    BiomeChanged        = 1008,
    ClimateShifted      = 1009,
    ResourceRecovered   = 1010,
    // Future ranges reserved:
    // 2000–2099: Settlement events (M2)
    // 3000–3099: Character events (M2)
    // 4000–4099: Political events (M2)
    // 5000–5099: Conflict events (M2)
    // 6000–6099: Artifact events (M2)
    // 9000–9099: God Mode events (M3)
}
```

**VerbClass to EventType mapping** (used by significance classifier):
```csharp
public static class VerbClassification
{
    public static VerbClass Classify(EventType type) => type switch
    {
        EventType.VolcanicEruption   => VerbClass.Destruction,
        EventType.EarthquakeOccurred => VerbClass.Destruction,
        EventType.WildfireOccurred   => VerbClass.Destruction,
        EventType.FloodOccurred      => VerbClass.Destruction,
        EventType.DroughtBegan       => VerbClass.Destruction,
        EventType.DroughtEnded       => VerbClass.Creation,
        EventType.SeaLevelChanged    => VerbClass.Transformation,
        EventType.BiomeChanged       => VerbClass.Transformation,
        EventType.ClimateShifted     => VerbClass.Transformation,
        EventType.ResourceRecovered  => VerbClass.Maintenance,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No VerbClass mapping")
    };
}
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/EventTypeTests.cs`):
```
EventType_AllValuesAreUnique                   # no two EventType values share an int
EventType_AllValuesInRange                     # all in declared range (1001-1010 for M1)
VerbClassification_AllM1TypesHaveMapping       # no ArgumentOutOfRange for any M1 EventType
SimEvent_IsImmutableRecord                     # cannot mutate fields after construction
SimEvent_PrimaryEntitiesDefaultsToEmptyList    # PrimaryEntities not null when not set
```

**Done when:** Tests pass. EventType int values are locked.

---

## Story 1.6.2 — EventGate

**File:** `WorldEngine.Sim/Events/EventGate.cs`

The gate is a pre-write filter. Events failing the gate are dropped — never reach SQLite.

```csharp
public sealed class EventGate(SimConfig config)
{
    public bool ShouldRecord(EventType type, EventTier tier) { ... }
}
```

**Gate rules:**
1. If `type` is in `config.Events.SuppressedTypes` → reject
2. If `tier < config.Events.MinimumRecordedTier` → reject  
3. If event is flagged `IsGodMode` → always accept (override all rules)
4. Otherwise → accept

**New SimConfig entries (`[events]`):**
```toml
[events]
minimum_recorded_tier = 0        # 0=Background, 1=Character, 2=Regional, 3=Headline
# Types to suppress (not record to DB at all). Defaults empty. Add noisy types here after tuning.
suppressed_types = []
headline_threshold = 3           # minimum EventTier to include in WorldSnapshot.RecentEvents
recent_event_cache_size = 500    # max events in EventCache ring buffer
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/EventGateTests.cs`):
```
EventGate_SuppressedTypeRejected               # type in suppressed_types → ShouldRecord=false
EventGate_BelowMinimumTierRejected             # tier < minimum → ShouldRecord=false
EventGate_GodModeAlwaysAccepted                # IsGodMode overrides suppressed+tier rules
EventGate_NormalEventAccepted                  # non-suppressed, valid tier → ShouldRecord=true
EventGate_EmptySuppressedListAcceptsAll        # default config accepts all non-god-mode events
```

**Done when:** Tests pass.

---

## Story 1.6.3 — SQLite Schema + EventStore

**Files:**
```
WorldEngine.Sim/Persistence/DatabaseSchema.cs   # DDL strings
WorldEngine.Sim/Persistence/EventStore.cs       # batch insert, causal edge insert, queries
```

**Schema (create in DatabaseSchema.cs as string constants):**
```sql
CREATE TABLE IF NOT EXISTS Events (
    Id          INTEGER PRIMARY KEY,
    Type        INTEGER NOT NULL,
    Year        INTEGER NOT NULL,
    Season      INTEGER NOT NULL,
    Tick        INTEGER NOT NULL,
    LocationX   INTEGER,
    LocationY   INTEGER,
    TierInvolvement INTEGER NOT NULL,
    VerbClass       INTEGER NOT NULL,
    PopulationImpact INTEGER NOT NULL,
    IsFirstOfKind   INTEGER NOT NULL,
    IsGodMode       INTEGER NOT NULL,
    PayloadJson     TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_events_year ON Events(Year);
CREATE INDEX IF NOT EXISTS idx_events_type ON Events(Type);
CREATE INDEX IF NOT EXISTS idx_events_tier ON Events(TierInvolvement);
CREATE INDEX IF NOT EXISTS idx_events_location ON Events(LocationX, LocationY) WHERE LocationX IS NOT NULL;

CREATE TABLE IF NOT EXISTS CausalEdges (
    PredecessorId INTEGER NOT NULL REFERENCES Events(Id),
    SuccessorId   INTEGER NOT NULL REFERENCES Events(Id),
    PRIMARY KEY (PredecessorId, SuccessorId)
);
```

**SQLite connection settings (apply once on open):**
```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
```

**EventStore public API:**
```csharp
public sealed class EventStore(string dbPath)
{
    public void InitializeSchema();
    public IReadOnlyList<SimEvent> BatchInsert(IEnumerable<SimEvent> events);  // returns with assigned IDs
    public void InsertCausalEdges(IEnumerable<(long PredecessorId, long SuccessorId)> edges);
    public IEnumerable<SimEvent> QueryByYear(int year);
    public IEnumerable<SimEvent> QueryByType(EventType type, int fromYear = 0, int toYear = int.MaxValue);
    public IEnumerable<SimEvent> QueryByTier(EventTier tier, int fromYear = 0, int toYear = int.MaxValue);
}
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Integration/EventStoreTests.cs`):
These are INTEGRATION tests — use a real temp SQLite file, not mocks.
```
EventStore_SchemaCreatedWithAllTables          # Events + CausalEdges tables exist
EventStore_SchemaCreatedWithAllIndexes         # all 4 indexes on Events exist
EventStore_RoundTripSingleEvent                # insert 1 event, query by year, get it back
EventStore_BatchInsert1000Events               # no timeout, no error, count=1000 in DB
EventStore_AssignedIdIsPositive                # returned events have Id > 0
EventStore_CausalEdgeInserted                  # edge inserted, appears in CausalEdges
EventStore_QueryByYearReturnsCorrect           # only events from queried year returned
EventStore_QueryByTypeReturnsCorrect           # only events of queried type returned
EventStore_QueryByTierReturnsCorrect           # only events at queried tier returned
```

**Done when:** All integration tests pass against a real SQLite file.

---

## Story 1.6.4 — SignificanceClassifier

**File:** `WorldEngine.Sim/Events/SignificanceClassifier.cs`

**Classification rules (max across all three — not weighted average):**
1. **Tier from VerbClass:**
   - `Destruction` → minimum `Regional`
   - `Transformation` → minimum `Character`
   - `Creation`, `Maintenance`, `Transfer`, `Conflict` → minimum `Background`
2. **Tier from PopulationImpact:**
   - `Catastrophic` → `Headline`
   - `Major` → `Regional`
   - `Moderate` → `Character`
   - `Minor`, `None` → `Background`
3. **IsFirstOfKind flag:** if this is the first event of this `EventType` in the world's history (check `EventCache.ContainsType(eventType)`) → bump tier by 1 (Background→Character, Character→Regional, Regional→Headline)
4. **Final tier** = max(rule1result, rule2result) + IsFirstOfKind bump

**PopulationImpact calculation for environmental events (M1 only):**
- `VolcanicEruption`: intensity > 0.8 → Catastrophic; > 0.5 → Major; else → Moderate
- `FloodOccurred`: intensity > 0.7 → Major; else → Minor
- `WildfireOccurred`: > 0.6 → Moderate; else → Minor
- `EarthquakeOccurred`: intensity > 0.9 → Catastrophic; > 0.6 → Major; else → Moderate
- `DroughtBegan`: always Moderate
- `SeaLevelChanged`: delta > 5m → Catastrophic; else → Major
- `BiomeChanged`, `ClimateShifted`, `ResourceRecovered`, `DroughtEnded` → None

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/SignificanceClassifierTests.cs`):
```
Classifier_SeaLevelChangedIsHeadline           # large sea level change → Headline
Classifier_DestructionVerbIsAtLeastRegional    # Destruction → Regional floor
Classifier_CatastrophicImpactIsHeadline        # Catastrophic impact → Headline
Classifier_FirstVolcanicEruptionBumped         # first eruption in history bumps 1 tier
Classifier_SecondVolcanicEruptionNotBumped     # IsFirstOfKind=false for second eruption
Classifier_PopulationImpactNoneIsBackground    # BiomeChanged with no entities = Background
Classifier_MaxAcrossAllRules                   # verify it's max, not sum
```

**Done when:** Tests pass.

---

## Story 1.6.5 — EventCache + Phase 7 Integration

**File:** `WorldEngine.Sim/Events/EventCache.cs`

```csharp
public sealed class EventCache(int maxSize)  // fixed-capacity ring buffer
{
    public void Add(SimEvent evt);
    public IReadOnlyList<SimEvent> GetRecent(int count);
    public bool ContainsType(EventType type);          // used by IsFirstOfKind check
    public IReadOnlyList<SimEvent> GetByType(EventType type);
    // Thread-safe: sim thread calls Add(); UI thread calls GetRecent() via snapshot
}
```

**EventCache is NOT thread-safe by default** — Add() is only called from the sim thread. GetRecent() is called during snapshot build (also sim thread). Thread-safety is provided by StateCache, which wraps the entire snapshot. EventCache itself needs no lock.

**Phase 7 integration** — add to PhaseRunner.RunEventGeneration():
```
1. For each PendingEvent in the Phase 1 pending list:
   a. Compute PopulationImpact
   b. Check IsFirstOfKind via EventCache.ContainsType()
   c. SignificanceClassifier → EventTier
   d. EventGate.ShouldRecord() → if false, discard
   e. Build SimEvent (with EventId = EventId(nextId++), current year/season/tick)
   f. Collect for batch insert

2. Assign OriginEventIds: fill in the provisional ActiveDisaster.OriginEventId 
   (set to EventId.None during Phase 1; set to the real EventId now)

3. EventStore.BatchInsert(batch) → returns events with real Ids

4. Collect CausalEdges: for each event where PendingEvent.CauseEventId was set,
   create (PredecessorId=CauseEventId.Value, SuccessorId=event.Id) edge

5. EventStore.InsertCausalEdges(edges)

6. EventCache.Add() for each inserted event
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/EventCacheTests.cs`):
```
EventCache_OldestDroppedWhenFull               # 501 events in cache of size 500 → first one gone
EventCache_ContainsTypeAfterAdd                # add VolcanicEruption, ContainsType(Volcanic)=true
EventCache_ContainsTypeFalseBeforeAdd          # ContainsType(WildfireOccurred) = false on empty cache
EventCache_GetRecentReturnsLatestN             # add 100, GetRecent(10) returns last 10
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Integration/Phase7IntegrationTests.cs`):
```
Phase7_PendingEventsWrittenToDatabase          # after PhaseRunner.RunTick with pending events, DB has rows
Phase7_CausalEdgesInsertedForLinkedEvents      # pending event with CauseEventId → edge in CausalEdges
Phase7_GatedEventsNotInDatabase                # suppressed type not in Events table
Phase7_EventCacheContainsInsertedEvents        # after tick, EventCache.ContainsType = true
Phase7_WriteOrderDbBeforeCache                 # verify DB written before EventCache updated
                                               # (test by killing after DB write and verifying cache empty)
```

**Done when:** Phase 7 fully wired. Events flow: PendingEvent → Classifier → Gate → DB → CausalEdges → Cache → Snapshot.

---

## Phase 6 Done Criteria

- `dotnet test` — all tests pass (unit + integration)
- `world.db` has Events and CausalEdges tables with WAL mode active
- IsFirstOfKind correctly set to true for first event of each type, false thereafter
- Event significance classification matches rules in this doc
- EventGate config-driven (suppressed_types can filter events)
- Phase 7 write order: DB → CausalEdges → Cache (verified by integration test)
- EventCache does not require a lock (sim-thread-only usage verified by design)
- Run validation queries from `docs/queries/event_log_queries.md` to manually verify history coherence
