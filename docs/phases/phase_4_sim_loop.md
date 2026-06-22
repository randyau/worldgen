# Phase 4 — Epic 1.4: Simulation Loop
**Status:** NOT STARTED  
**Requires:** Phase 3 complete (WorldState must exist)  
**Reads required:** `docs/interface_contracts.md` (WorldSnapshot, TileDisplayData, TileInspectorData, StateCache, IWorldStateReadOnly), `docs/snippets/patterns.md` (StateCache Commit Pattern, #7)

---

## Goal
Build the two-thread runtime: SimLoop on a background thread ticking WorldState, StateCache bridging to the UI thread, CommandQueue receiving player commands, and WorldSnapshot being built and committed each tick.

## Critical Thread Rule
**WorldState is sim-thread-only.** Every class in this phase that touches WorldState must only be called from the sim thread. StateCache and CommandQueue are the ONLY shared objects.

---

## Story 1.4.1 — WorldState Shell

**File:** `WorldEngine.Sim/World/WorldState.cs`

WorldState is a class (not a record — it's mutable). All fields internal or public; no external mutators.

**Required fields:**
```csharp
// Time
public int CurrentYear { get; internal set; }
public Season CurrentSeason { get; internal set; }
public long CurrentTick { get; internal set; }

// Tile data (from Phase 3)
public TileGrid TileGrid { get; }
public SeasonalProfile[] SeasonalProfiles { get; }

// Registries
public Dictionary<TileCoord, List<ResourceDeposit>> ResourceRegistry { get; }
public Dictionary<TileCoord, List<ActiveDisaster>> ActiveTileDisasters { get; }
public List<ActiveDrought> ActiveDroughts { get; }

// Drift parameters (initialized from SimConfig defaults at genesis)
public float CurrentSeaLevel { get; internal set; }
public float GlobalTemperatureAnomaly { get; internal set; }
public float GlobalPrecipitationMultiplier { get; internal set; }
public float StormCorridorNormalizedLat { get; internal set; }
public float StormCorridorHalfWidth { get; internal set; }
public float MonsoonIntensityMultiplier { get; internal set; }
public float VolcanicActivityMultiplier { get; internal set; }

// Config (read-only after construction)
public WorldConfig Config { get; }
public SimConfig SimConfig { get; }
public int WorldSeed => Config.Seed;

// Inspector selection (set by SetInspectedTile command)
public TileCoord? InspectedTile { get; internal set; }
```

WorldState also implements `IWorldStateReadOnly` (Phase 2 interface).

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/WorldStateTests.cs`):
```
WorldState_ImplementsIWorldStateReadOnly       # typeof(WorldState).IsAssignableTo(typeof(IWorldStateReadOnly))
WorldState_InitialSeasonIsSpring               # CurrentSeason == Season.Spring after construction
WorldState_InitialYearIsOne                    # CurrentYear == 1
WorldState_GetTileWrapsX                       # delegates to TileGrid, wrapping verified
WorldState_DroughtParametersDefaultToGenesis   # GlobalTemperatureAnomaly == 0, multipliers == 1.0
```

**Done when:** Tests pass. WorldState can be constructed from a pipeline-generated TileGrid.

---

## Story 1.4.2 — WorldSnapshot + TileDisplayData + TileInspectorData

**Files:**
```
WorldEngine.Sim/World/WorldSnapshot.cs           # sealed record (from interface_contracts.md)
WorldEngine.Sim/World/TileDisplayData.cs         # sealed record
WorldEngine.Sim/World/TileInspectorData.cs       # sealed record
WorldEngine.Sim/World/SnapshotBuilder.cs         # constructs WorldSnapshot from WorldState
```

**SnapshotBuilder.Build():**
- Takes `WorldState world, ViewportRect viewport, OverlayType activeOverlay`
- Iterates tiles in viewport, builds `TileDisplayData` per tile:
  - Computes `EffectiveTemperature` using the formula from `docs/snippets/patterns.md` (#9)
  - Computes `HasActiveDisaster` from `world.ActiveTileDisasters.ContainsKey(coord)`
- If `world.InspectedTile` is set, builds `TileInspectorData` from full registry lookups
- Returns sealed `WorldSnapshot` record

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/SnapshotBuilderTests.cs`):
```
SnapshotBuilder_EffectiveTempHigherAtEquator   # equatorial tile has higher EffectiveTemp than polar
SnapshotBuilder_HasActiveDisasterTrueWhenInRegistry  # tile in ActiveTileDisasters → HasActiveDisaster=true
SnapshotBuilder_HasActiveDisasterFalseWhenNotInRegistry  # tile NOT in registry → HasActiveDisaster=false
SnapshotBuilder_InspectedTilePopulatedWhenSet  # InspectedTile != null when world.InspectedTile is set
SnapshotBuilder_InspectedTileNullWhenNotSet    # InspectedTile == null when world.InspectedTile is null
TileDisplayData_IsImmutableRecord              # cannot mutate fields after construction
TileInspectorData_ContainsAllDeposits          # all deposits from ResourceRegistry in result
TileInspectorData_ContainsAllDisasters         # all disasters from ActiveTileDisasters in result
TileInspectorData_IsInActiveDroughtCorrect     # drought check uses ActiveDroughts list
```

**Done when:** Tests pass. SnapshotBuilder produces valid snapshots from test WorldState.

---

## Story 1.4.3 — StateCache

**File:** `WorldEngine.Sim/World/StateCache.cs`

Exact implementation from `docs/snippets/patterns.md` (#7). `ReaderWriterLockSlim`, write lock in `Commit()`, read lock in `Read()`. No other logic.

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/StateCacheTests.cs`):
```
StateCache_ReadBeforeFirstCommitReturnsNull    # Read() == null before any Commit()
StateCache_ReadReturnsLastCommittedSnapshot    # Commit(snap1), Commit(snap2), Read() == snap2
StateCache_ThreadSafetyUnderConcurrentAccess  # see docs/snippets/test_templates.md (#thread-safety)
```

**Done when:** Thread-safety test passes without race conditions or deadlocks.

---

## Story 1.4.4 — CommandQueue

**File:** `WorldEngine.Sim/Core/CommandQueue.cs`

Use `System.Threading.Channels.Channel<ICommand>.CreateUnbounded()`. 

**Player commands for M1** (create these sealed records too):
```
WorldEngine.Sim/Commands/PlayerCommands.cs:
  SetSimSpeed(SimSpeed Speed)
  PauseToggle()
  StepOneTick()
  SetViewport(int X, int Y, int Width, int Height)
  SetInspectedTile(TileCoord? Coord)   # null = deselect
  SetActiveOverlay(OverlayType Overlay)
```

**CommandQueue API:**
```csharp
public sealed class CommandQueue
{
    public void Enqueue(ICommand command);          // UI thread calls this
    public IEnumerable<ICommand> DrainAll();        // Sim thread calls once per tick
}
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/CommandQueueTests.cs`):
```
CommandQueue_EnqueuedCommandsReturnedByDrain   # enqueue 3 commands, drain returns all 3
CommandQueue_DrainClearsQueue                  # drain twice, second drain is empty
CommandQueue_DrainOnEmptyQueueReturnsEmpty     # no throw, no hang on empty drain
CommandQueue_PlayerCommandsAreICommands        # SetSimSpeed etc. all implement ICommand
```

**Done when:** Tests pass.

---

## Story 1.4.5 — PhaseRunner

**File:** `WorldEngine.Sim/Simulation/PhaseRunner.cs`

Runs the 7 simulation phases in order. In M1, most phases are stubs. What matters is the execution order contract and the PendingEvent hand-off from Phase 1 to Phase 7.

```csharp
public sealed class PhaseRunner(SimConfig config, EventStore eventStore, EventCache eventCache)
{
    // Phase 1 produces PendingEvents; Phase 7 consumes them
    public void RunTick(WorldState world)
    {
        var pending = RunEnvironmentalPhase(world);    // Phase 1 — returns List<PendingEvent>
        RunResourceProduction(world);                  // Phase 2 — stub
        RunPopulationDynamics(world);                  // Phase 3 — stub
        RunEntityBehavior(world);                      // Phase 4 — stub
        RunCharacterDecisions(world);                  // Phase 5 — stub
        RunConflictResolution(world);                  // Phase 6 — stub
        RunEventGeneration(world, pending);            // Phase 7 — classifies + writes pending
    }
}
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/PhaseRunnerTests.cs`):
```
PhaseRunner_ExecutesPhasesInCorrectOrder       # use a mock recorder to verify order 1→7
PhaseRunner_Phase7ReceivesPendingEventsFromPhase1   # events emitted in Phase 1 reach Phase 7
PhaseRunner_TickAdvancesTickCounter            # world.CurrentTick increments after RunTick
```

**Done when:** Tests pass. Phase 7 receives pending events.

---

## Story 1.4.6 — SimLoop

**File:** `WorldEngine.Sim/Simulation/SimLoop.cs`

```csharp
public sealed class SimLoop(WorldState world, CommandQueue cmdQueue, 
                             StateCache stateCache, PhaseRunner phaseRunner,
                             SnapshotBuilder snapshotBuilder, SimConfig config)
{
    private Thread? _thread;
    public void Start() { _thread = new Thread(Run) { IsBackground = true }; _thread.Start(); }
    public void Stop() { ... }
}
```

**Per-tick sequence:**
1. Drain `CommandQueue.DrainAll()` — apply commands (SetSimSpeed changes throttle, PauseToggle sets pause flag, StepOneTick clears after one tick, SetInspectedTile updates `world.InspectedTile`, SetViewport updates `_viewport`, SetActiveOverlay updates `_overlay`)
2. If paused AND not StepOneTick: `Thread.Sleep(16)`, goto 1
3. `phaseRunner.RunTick(world)` — run 7 phases
4. Advance time: increment tick, advance season every N ticks (config-driven), advance year every 4 seasons
5. Build snapshot: `snapshotBuilder.Build(world, _viewport, _overlay)`
6. Commit: `stateCache.Commit(snapshot)`
7. Throttle: sleep based on `_currentSpeed`

**New SimConfig entries:**
```toml
[simulation]
ticks_per_seasonal_change = 4
ticks_per_second_slow = 1
ticks_per_second_normal = 4
ticks_per_second_fast = 20
ticks_per_second_ultrafast = 200
snapshot_interval_ultrafast_ticks = 40   # only build snapshot every N ticks in Ultrafast
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Integration/SimLoopTests.cs`):
```
SimLoop_RunsForTenTicksWithoutError            # start, let run for 500ms, stop, verify tick >= 10
SimLoop_PauseHaltsTickProgress                 # start, pause, wait 200ms, verify tick not advancing
SimLoop_UnpauseResumesProgress                 # start, pause, wait, unpause, verify tick advances again
SimLoop_StepOneTickAdvancesExactlyOne          # while paused, step once, verify tick+1
SimLoop_SetSpeedAffectsTickRate                # Ultrafast ticks >> Slow ticks in same real time
```

**Done when:** Integration tests pass.

---

## Story 1.4.7 — Tick Time Advancement

Part of SimLoop (extracted for clarity). The tick-to-time mapping:

```
CurrentTick increments every tick (always)
CurrentSeason changes every SimConfig.Simulation.TicksPerSeasonalChange ticks
CurrentYear increments every 4 season changes (one year = Spring → Summer → Autumn → Winter → Spring)
```

This is already handled in Story 1.4.6. No separate story needed. Verify via:
```
SimLoop_SeasonAdvancesCorrectly               # after 4*TicksPerSeasonalChange ticks, season = Spring again
SimLoop_YearAdvancesAfterFourSeasons          # after 4 seasons, year increments
```

Add these tests to the SimLoopTests.cs from 1.4.6.

---

## Phase 4 Done Criteria

- `dotnet test` — all Phase 4 tests pass
- StateCache thread-safety test passes
- SimLoop runs for 10+ ticks without error
- Commands flow: UI enqueues → sim drains → state updates → snapshot reflects change
- WorldSnapshot contains effective (computed) values, not raw base values
- No WorldState access from any class except WorldState itself and classes called only from the sim thread
