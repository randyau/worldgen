# M3 Performance Notes

## Phase 3.1 Gate

M3 Phase 3.1 — Performance gate not yet measured. Profiling planned before narrative UI work begins.

The target is ≥ 400 TPS sustained from year 100 to year 500 on the reference machine,
with ≤ 15 active Tier1 characters and ≤ 30 settlements.

Known candidates for hotspots based on architecture review:
- `O(settlements²)` border tension scan in `CivTracker.Diplomacy.cs`
- `SnapshotBuilder` rebuilding full dictionaries every tick
- `RelationshipGraph` linear scans in character decision-making
- SQLite batch writes potentially blocking the sim thread

Profiling run scheduled for start of Phase 3.2 once sim load is more representative.
