<!-- AUTO-GENERATED — do not edit. Run: python3 scripts/gen-interface-contracts.py -->
<!-- Generated: 2026-07-19T18:46:35Z -->

# Interface Contracts Snapshot — core

## PendingEvent
**File:** `WorldEngine.Sim/World/PendingEvent.cs:10`  
**Kind:** `sealed record`

```csharp
public sealed record PendingEvent(
    EventType Type,
    TileCoord? Location,
    EventId? CauseEventId,
    string PayloadJson,
    IReadOnlyList<long>? PrimaryEntityIds = null,
    IReadOnlyList<long>? SecondaryEntityIds = null,
    long ActorId = 0,
    string? ActorName = null,
    long CivId = 0,
    string? SettlementName = null
);
```

## IEntity
**File:** `WorldEngine.Sim/Entities/IEntity.cs:11`  
**Kind:** `interface`

```csharp
public interface IEntity
{
    EntityId Id { get; }
    TileCoord Location { get; }
    EntityKind Kind { get; }
    bool IsAlive { get; }

    IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);

    EntitySnapshot ToSnapshot();
}
```

## ICommand
**File:** `WorldEngine.Sim/Core/ICommand.cs:8`  
**Kind:** `interface`

```csharp
public interface ICommand { }
```

## IWorldStateReadOnly
**File:** `WorldEngine.Sim/World/IWorldStateReadOnly.cs:15`  
**Kind:** `interface`

```csharp
public interface IWorldStateReadOnly
{
    // === TIME ===
    int CurrentYear { get; }
    Season CurrentSeason { get; }
    long CurrentTick { get; }

    // === TILE ACCESS ===
    TileData GetTile(TileCoord coord);
    bool IsLand(TileCoord coord);
    IEnumerable<TileCoord> GetTilesInRadius(TileCoord center, int radius);

    // === WORLD CONFIG ===
    WorldConfig Config { get; }
    SimConfig SimConfig { get; }

    // === DETERMINISTIC RNG (for entity decisions in M2+) ===
    float GetRandomFloat(EntityId entityId, int salt = 0);
    int GetRandomInt(EntityId entityId, int min, int max, int salt = 0);

    // === DRIFT PARAMETERS (readable by entity decision logic) ===
    float GlobalTemperatureAnomaly { get; }
    float CurrentSeaLevel { get; }

    // === ENTITY ACCESS (M2+) ===
    IEntity? GetEntity(EntityId id);
    IEnumerable<IEntity> GetEntitiesAt(TileCoord coord);
    IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius);

    // === CIVILIZATION / CHARACTER (Phase 2.2+) ===
    IReadOnlyDictionary<TileCoord, SettlementStub>  Settlements    { get; }
    IReadOnlyDictionary<TileCoord, RuinRecord>      Ruins          { get; }
    IReadOnlySet<EntityId>                          ActiveFounders { get; }
    IReadOnlyDictionary<TileCoord, IReadOnlyList<ResourceDeposit>> ResourceDeposits { get; }
    IReadOnlyDictionary<TileCoord, TileCoord>       TerritoryMap   { get; }
    IReadOnlyDictionary<TileCoord, TileImprovement> ImprovementMap { get; }
    Civilization? GetCivilization(CivId civId);
    RelationshipEdge? GetRelationship(EntityId a, EntityId b);
    int CountAlliances(EntityId id);
    int CountRivals(EntityId id);

    // === RELATIONSHIPS / HISTORY (M3+) ===
    // float GetRelationshipTrust(EntityId from, EntityId to);
    // IEnumerable<SimEvent> GetRecentEvents(int withinYears);
    // float GetAuthorityAt(TileCoord coord, CivId civId);
}
```

## StateCache
**File:** `WorldEngine.Sim/World/StateCache.cs:7`  
**Kind:** `class`

```csharp
public sealed class StateCache
{
    public void Commit(WorldSnapshot snapshot)
    public WorldSnapshot? Read()
}
```

<!-- content-hash: 33d837b0ee0a40a3 -->
