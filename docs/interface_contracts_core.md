<!-- contract-snapshot-hash: 4a3a3174fe60bcb6 -->
# Interface Contracts — Core Interfaces
**Parent:** `interface_contracts.md` | **Version:** 0.9 | **Status:** M3 complete

Covers: PendingEvent, IEntity, ICommand, IWorldStateReadOnly, IWorldGenLayer, StateCache.

---

## PendingEvent

Produced by any sim phase. Consumed by Phase 7 (EventGeneration) which enriches into a full `SimEvent`, applies the gate, and writes to the database.

```csharp
/// <summary>
/// Lightweight event record produced during simulation phases.
/// Phase 7 assigns Id, Year, Season, Tick, runs significance classification,
/// applies the event gate, and writes to SQLite + EventCache.
/// PrimaryEntityIds / SecondaryEntityIds map to EventEntities rows with Role='Primary'/'Secondary'.
/// ActorId, ActorName, CivId, SettlementName are written directly to denormalized Events columns.
/// </summary>
public sealed record PendingEvent(
    EventType Type,
    TileCoord? Location,
    EventId? CauseEventId,                          // null = root event; set = CausalEdge will be created
    string PayloadJson,
    IReadOnlyList<long>? PrimaryEntityIds   = null, // entity IDs with Role='Primary'
    IReadOnlyList<long>? SecondaryEntityIds = null, // entity IDs with Role='Secondary'
    long     ActorId        = 0,                    // denormalized primary actor ID
    string?  ActorName      = null,                 // denormalized primary actor name
    long     CivId          = 0,                    // denormalized civ association
    string?  SettlementName = null                  // denormalized settlement name
);
```

---

## IEntity

```csharp
/// <summary>
/// The core simulation entity interface. Every simulated object implements this.
/// Entities NEVER mutate world state directly. They emit ICommand instances
/// during the EMIT step which are resolved by CommandResolver in the RESOLVE step.
/// </summary>
public interface IEntity
{
    EntityId Id { get; }
    TileCoord Location { get; }
    EntityKind Kind { get; }
    bool IsAlive { get; }

    /// <summary>
    /// Emit commands for this tick phase. Must not have side effects.
    /// </summary>
    IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);

    EntitySnapshot ToSnapshot();
}
```

---

## ICommand

```csharp
/// <summary>
/// Marker interface for simulation commands.
/// All implementations must be sealed records with value-type fields only.
/// No callbacks, delegates, or mutable object references.
/// </summary>
public interface ICommand { }
```

---

## IWorldStateReadOnly

The read-only view passed to entity decision-making. All phases that read but don't need to mutate receive this interface.

```csharp
/// <summary>
/// Read-only view of world state for entity decision-making.
/// </summary>
public interface IWorldStateReadOnly
{
    // === TIME ===
    int CurrentYear { get; }
    Season CurrentSeason { get; }
    long CurrentTick { get; }

    // === TILE ACCESS ===
    /// <summary>Get tile data. Applies East-West cylinder wrapping.</summary>
    TileData GetTile(TileCoord coord);
    bool IsLand(TileCoord coord);
    IEnumerable<TileCoord> GetTilesInRadius(TileCoord center, int radius);

    // === WORLD CONFIG ===
    WorldConfig Config { get; }
    SimConfig SimConfig { get; }

    // === DETERMINISTIC RNG ===
    /// <summary>
    /// Deterministic random value for a specific entity this tick.
    /// Internally uses WorldRng.FloatAt(worldSeed, tick, entityId.Value, 0, salt).
    /// Do NOT use System.Random in entity logic.
    /// </summary>
    float GetRandomFloat(EntityId entityId, int salt = 0);
    int GetRandomInt(EntityId entityId, int min, int max, int salt = 0);

    // === DRIFT PARAMETERS ===
    float GlobalTemperatureAnomaly { get; }
    float CurrentSeaLevel { get; }

    // === ENTITY ACCESS ===
    IEntity? GetEntity(EntityId id);
    IEnumerable<IEntity> GetEntitiesAt(TileCoord coord);
    IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius);

    // === CIVILIZATION / SETTLEMENT ===
    IReadOnlyDictionary<TileCoord, SettlementStub>  Settlements    { get; }
    IReadOnlyDictionary<TileCoord, RuinRecord>      Ruins          { get; }
    IReadOnlySet<EntityId>                          ActiveFounders { get; }  // O(1) isFounder lookup
    IReadOnlyDictionary<TileCoord, IReadOnlyList<ResourceDeposit>> ResourceDeposits { get; }
    /// <summary>Tile → owning city tile. Absent = unclaimed. City tiles map to themselves.</summary>
    IReadOnlyDictionary<TileCoord, TileCoord>       TerritoryMap   { get; }
    /// <summary>Tile → improvement record. One improvement per tile.</summary>
    IReadOnlyDictionary<TileCoord, TileImprovement> ImprovementMap { get; }
    Civilization? GetCivilization(CivId civId);

    // === RELATIONSHIPS ===
    RelationshipEdge? GetRelationship(EntityId a, EntityId b);   // null if no edge exists yet
    int CountAlliances(EntityId id);
    int CountRivals(EntityId id);
}
```

**`ActiveFounders`** is a `HashSet<EntityId>` of characters who currently have a live settlement they founded. Maintained by `CivTracker`: added on `EstablishSettlement`, removed on `RegisterRuin`. Use `world.ActiveFounders.Contains(c.Id)` — O(1) vs O(n) scan.

**Note:** There is no `Civilizations` dictionary on this interface. Use `GetCivilization(civId)` for individual lookups. Civ iteration is internal to sim phases only.

---

## IWorldGenLayer

```csharp
/// <summary>
/// World generation layer interface. Stateless — all state in WorldGenContext.
/// </summary>
public interface IWorldGenLayer<TResult>
{
    TResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default);
}
```

---

## StateCache

```csharp
/// <summary>
/// Thread-safe snapshot bridge. Sim thread calls Commit() after each tick.
/// UI thread calls Read() every frame. Lock held for microseconds only.
/// </summary>
public sealed class StateCache
{
    public void Commit(WorldSnapshot snapshot);
    public WorldSnapshot? Read();   // null until first Commit
}
```
