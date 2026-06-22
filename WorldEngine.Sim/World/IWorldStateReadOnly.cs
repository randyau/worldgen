using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// Read-only view of world state for entity decision-making (M2+).
/// In M1, the Environmental phase reads WorldState directly as a mutator.
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

    // === DETERMINISTIC RNG (for entity decisions in M2+) ===
    /// <summary>
    /// Deterministic random value for a specific entity this tick.
    /// Internally uses WorldRng.FloatAt(worldSeed, tick, entityId.Value, 0, salt).
    /// Do NOT use System.Random in entity logic.
    /// </summary>
    float GetRandomFloat(EntityId entityId, int salt = 0);
    int GetRandomInt(EntityId entityId, int min, int max, int salt = 0);

    // === DRIFT PARAMETERS (readable by entity decision logic) ===
    float GlobalTemperatureAnomaly { get; }
    float CurrentSeaLevel { get; }

    // Milestone 2+ additions (uncomment when implementing):
    // IEntity? GetEntity(EntityId id);
    // IEnumerable<IEntity> GetEntitiesAt(TileCoord coord);
    // IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius);
    // float GetRelationshipTrust(EntityId from, EntityId to);
    // IEnumerable<SimEvent> GetRecentEvents(int withinYears);
    // float GetAuthorityAt(TileCoord coord, CivId civId);
}
