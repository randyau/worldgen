# Global Code Patterns
**Load this file when you need boilerplate.** These are the recurring patterns across the codebase.
Do not deviate from these patterns without updating GUARDRAILS.md.

---

## 1. ICommand Implementation

```csharp
// CORRECT — sealed record, value types only
public sealed record MoveTo(EntityId EntityId, TileCoord Destination) : ICommand;
public sealed record ClaimArtifact(EntityId EntityId, ArtifactId ArtifactId, TileCoord Location) : ICommand;
public sealed record DeclareWar(EntityId DeclaringLeader, CivId TargetCiv) : ICommand;

// WRONG — no callbacks, no mutable references
public sealed record MoveTo(EntityId EntityId, TileCoord Destination, Action<WorldState> callback) : ICommand;
```

---

## 2. IEntity.EmitCommands Pattern

```csharp
public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
{
    if (phase != SimPhase.EntityBehavior) yield break;

    var target = ChooseTarget(world);    // pure read, no mutation
    if (target.HasValue)
        yield return new MoveTo(Id, target.Value);
}

// WRONG — never mutate world inside EmitCommands
public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
{
    world.MoveEntity(Id, target);   // NO
    yield break;
}
```

---

## 3. WorldRng Usage (Tile-Level Randomness)

```csharp
// Always use WorldRng for tile-level probability checks in Phase 1
// Never use System.Random or new Random() in sim logic

// For a one-time disaster check at a tile:
float roll = WorldRng.FloatAt(_worldSeed, _currentTick, coord.X, coord.Y, DisasterSalts.Wildfire);
if (roll < _config.Disasters.WildfireIgnitionProbabilityPerTick)
{
    // ignite
}

// For spread check (different salt = statistically independent):
float spreadRoll = WorldRng.FloatAt(_worldSeed, _currentTick, coord.X, coord.Y, DisasterSalts.WildfireSpread);

// For an annual check (tick advances 4× per year — use year as tick to ensure stable per-year result):
long yearTick = _currentYear * 4L;   // or derive from tick / 4
float annualRoll = WorldRng.FloatAt(_worldSeed, yearTick, coord.X, coord.Y, DisasterSalts.DroughtCheck);
```

---

## 4. SimConfig Extension (Add a New Constant)

**Step 1 — Add to the C# config class:**
```csharp
// In the appropriate config section class:
public class DisasterConfig
{
    public float WildfireIgnitionProbabilityPerTick { get; set; } = 0.0001f;
    public float WildfireSpreadProbabilityPerTick { get; set; } = 0.15f;
    public int WildfireMaxTicks { get; set; } = 12;
    // New constant:
    public float FloodIgnitionProbabilityPerTick { get; set; } = 0.00005f;
}
```

**Step 2 — Add to sim_config.toml (with comment explaining what it controls):**
```toml
[disasters]
wildfire_ignition_probability_per_tick = 0.0001   # probability per tick any forest tile ignites
wildfire_spread_probability_per_tick = 0.15       # probability fire spreads to adjacent forest
wildfire_max_ticks = 12                           # max seasonal ticks before wildfire burns out
flood_ignition_probability_per_tick = 0.00005     # probability per tick river tile floods
```

**Step 3 — Inject via constructor:**
```csharp
public sealed class DisasterSystem(SimConfig config)
{
    private readonly DisasterConfig _cfg = config.Disasters;
}
```

---

## 5. Phase 1 Tile Iteration with Chunk Skip

```csharp
// Standard Phase 1 tile iteration pattern with chunk-level skip
private void ProcessDisasters(WorldState world)
{
    foreach (var chunk in world.TileGrid.AllChunks())
    {
        if (chunk is null) continue;                           // null = ocean chunk
        if (!chunk.SummaryFlags.HasFlag(ChunkSummaryFlags.HasForestTile)) continue;  // skip non-forest chunks

        foreach (var (coord, tile) in chunk.AllTiles())
        {
            if (tile.BiomeType is not (BiomeType)BiomeTypeValue.TemperateForest
                and not (BiomeType)BiomeTypeValue.TropicalRainforest
                and not (BiomeType)BiomeTypeValue.BorealForest)
                continue;

            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick, coord.X, coord.Y, DisasterSalts.Wildfire);
            if (roll < _cfg.WildfireIgnitionProbabilityPerTick)
                IgniteWildfire(world, coord, tile);
        }
    }
}
```

---

## 6. PendingEvent Emission (Phase 1 → Phase 7)

```csharp
// Phase 1 creates PendingEvent records — never SimEvent directly
private List<PendingEvent> _pendingEvents = new();

private void IgniteWildfire(WorldState world, TileCoord coord, TileData tile)
{
    var disaster = new ActiveDisaster(DisasterType.Wildfire, intensity: 0.5f, TicksRemaining: 12, OriginEventId: default);
    
    // Create a provisional disaster (OriginEventId filled in by Phase 7 after DB insert)
    world.ActiveTileDisasters.TryAdd(coord, new List<ActiveDisaster>());
    world.ActiveTileDisasters[coord].Add(disaster);
    
    tile.DynFlags |= TileDynFlags.HasActiveDisaster;
    world.TileGrid.SetTile(coord, tile);
    world.TileGrid.GetChunk(coord)!.SummaryFlags |= ChunkSummaryFlags.HasActiveDisaster;

    _pendingEvents.Add(new PendingEvent(
        EventType.WildfireOccurred,
        Location: coord,
        CauseEventId: null,   // root event
        PayloadJson: JsonSerializer.Serialize(new { Intensity = 0.5f, BiomeType = tile.BiomeType })
    ));
}
```

---

## 7. StateCache Commit Pattern (Sim Thread)

```csharp
// At the end of each tick, after Phase 7:
var snapshot = _snapshotBuilder.Build(world, _commandQueue.GetPendingViewport());
_stateCache.Commit(snapshot);

// StateCache implementation:
public sealed class StateCache
{
    private readonly ReaderWriterLockSlim _lock = new();
    private WorldSnapshot? _snapshot;

    public void Commit(WorldSnapshot snapshot)
    {
        _lock.EnterWriteLock();
        try { _snapshot = snapshot; }
        finally { _lock.ExitWriteLock(); }
    }

    public WorldSnapshot? Read()
    {
        _lock.EnterReadLock();
        try { return _snapshot; }
        finally { _lock.ExitReadLock(); }
    }
}
```

---

## 8. DECISION Comment Convention

```csharp
// When you make a non-obvious implementation choice, leave a DECISION comment:
// DECISION: Using byte for DisasterType enum rather than int to keep ActiveDisaster under 24 bytes.
// The enum has <256 values and this struct is stored in Dictionary values (heap allocated anyway),
// but keeping it compact reduces cache pressure in Phase 1's tight loop.

// For V2 stubs:
// V2: magic physical substrate — currently just noise, not behaviorally active
public byte MagicIntensity;  // generated and stored, not used by any Phase 1-3 system
```

---

## 9. Effective Value Formula (Reference)

```csharp
// These are computed at snapshot build time, NOT stored in TileData
// effectiveTemp = BaseTemperature + SeasonalProfiles[idx].TempDelta(world.CurrentSeason)
//               + (world.GlobalTemperatureAnomaly * latitudeScale(coord, config))

// effectiveMoisture = (BaseMoisture + SeasonalProfiles[idx].MoistureDelta(world.CurrentSeason))
//                   * world.GlobalPrecipitationMultiplier
//                   * (tile.IsStormCorridor ? config.Climate.StormCorridorMoistureBonus : 1.0f)
//                   * (IsMonsoonTile(coord, world) ? world.MonsoonIntensityMultiplier : 1.0f)

// Helpers:
private static float LatitudeScale(TileCoord coord, WorldConfig config)
{
    float normalizedLat = coord.Y / (float)config.TileHeight;
    // Equator (lat 0.5) gets full anomaly; poles get 70% more due to polar amplification
    return 1.0f + MathF.Abs(normalizedLat - 0.5f) * 1.4f;
}
```

---

## 10. TileCoord Cylinder Wrapping

```csharp
// Always use TileCoord methods for neighbor lookup — never raw arithmetic
// X wraps East-West (cylinder). Y is clamped North-South (not a torus).

public readonly record struct TileCoord(int X, int Y)
{
    public TileCoord Wrap(int width) => this with { X = ((X % width) + width) % width };

    // Use these, not raw X±1, Y±1:
    public TileCoord North() => this with { Y = Y - 1 };   // clamped at boundary by TileGrid
    public TileCoord South() => this with { Y = Y + 1 };
    public TileCoord East(int width) => this with { X = (X + 1) % width };
    public TileCoord West(int width) => this with { X = ((X - 1) + width) % width };
}

// TileGrid.GetTile always wraps X before lookup — safe to call with out-of-bounds X
// TileGrid.GetTile clamps Y to [0, height-1] — no null return for in-world Y
```
