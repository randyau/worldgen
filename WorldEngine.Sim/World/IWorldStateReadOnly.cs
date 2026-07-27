using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using System.Collections.Generic;

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
    /// <summary>True for an Ocean/CoastalWater tile with at least one land neighbor — the walkable
    /// set for M11 sea voyages. Open ocean with no nearby land is never shallow.</summary>
    bool IsShallowOcean(TileCoord coord);
    IEnumerable<TileCoord> GetTilesInRadius(TileCoord center, int radius);

    // === WORLD CONFIG ===
    WorldConfig Config { get; }
    /// <summary>Simulation configuration — all constants used by entity logic.</summary>
    SimConfig SimConfig { get; }

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

    // === ENTITY ACCESS (M2+) ===
    IEntity? GetEntity(EntityId id);
    IEnumerable<IEntity> GetEntitiesAt(TileCoord coord);
    IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius);

    // === ARTIFACTS (M5+) ===
    /// <summary>All artifacts in the world (active and destroyed). Use ArtifactRegistry.Active() for non-destroyed only.</summary>
    IReadOnlyDictionary<ArtifactId, Artifact> Artifacts { get; }

    // === CIVILIZATION / CHARACTER (Phase 2.2+) ===
    IReadOnlyDictionary<TileCoord, SettlementStub>  Settlements    { get; }
    IReadOnlyDictionary<TileCoord, RuinRecord>      Ruins          { get; }
    IReadOnlySet<EntityId>                          ActiveFounders { get; }
    IReadOnlyDictionary<TileCoord, IReadOnlyList<ResourceDeposit>> ResourceDeposits { get; }
    /// <summary>Tile → owning city tile. Absent = unclaimed. City tiles map to themselves.</summary>
    IReadOnlyDictionary<TileCoord, TileCoord>       TerritoryMap   { get; }
    /// <summary>Tile → improvement record. One improvement per tile.</summary>
    IReadOnlyDictionary<TileCoord, TileImprovement> ImprovementMap { get; }
    Civilization? GetCivilization(CivId civId);
    RelationshipEdge? GetRelationship(EntityId a, EntityId b);
    int CountAlliances(EntityId id);
    int CountRivals(EntityId id);

    // === SPOTLIGHT (M7+) ===
    /// <summary>Character currently under spotlight player control. Null when spotlight is inactive.</summary>
    EntityId? SpotlightCharacterId { get; }
    /// <summary>Player's current move intent for the spotlit character. Null if no move intent is set.</summary>
    TileCoord? SpotlightMoveTarget { get; }
    /// <summary>Player's current goal intent for the spotlit character. Null if no goal intent is set.</summary>
    GoalType?  SpotlightGoalIntent { get; }

    // === RELATIONSHIPS / HISTORY (M3+) ===
    // float GetRelationshipTrust(EntityId from, EntityId to);
    // IEnumerable<SimEvent> GetRecentEvents(int withinYears);
    // float GetAuthorityAt(TileCoord coord, CivId civId);
}
