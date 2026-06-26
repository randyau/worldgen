using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// An event in the simulation history log. Immutable once written.
/// Created by Phase 7 (EventGeneration) after enriching a PendingEvent.
/// </summary>
public sealed record SimEvent
{
    public required EventId Id { get; init; }
    public required EventType Type { get; init; }
    public required string TypeName { get; init; }
    public required string Domain { get; init; }
    public required int Year { get; init; }
    public required Season Season { get; init; }
    public required long Tick { get; init; }
    public TileCoord? Location { get; init; }
    public IReadOnlyList<EntityId> PrimaryEntities { get; init; } = Array.Empty<EntityId>();
    public IReadOnlyList<EntityId> SecondaryEntities { get; init; } = Array.Empty<EntityId>();
    public required EventTier TierInvolvement { get; init; }
    public required VerbClass VerbClass { get; init; }
    public required PopulationImpact PopulationImpact { get; init; }
    public required bool IsFirstOfKind { get; init; }
    public required bool IsGodMode { get; init; }
    public long ActorId { get; init; }
    public string? ActorName { get; init; }
    public long CivId { get; init; }
    public string? SettlementName { get; init; }
    public required string PayloadJson { get; init; }
    /// <summary>
    /// Float significance score (0.0–1.0). Populated by SignificanceRescoringPass after simulation.
    /// Enables precise ranking of events within a tier (e.g. "top 10 events of a character's life").
    /// </summary>
    public float SignificanceScore { get; init; } = 0f;
    public string? GeneratedProse { get; init; }  // V2: LLM generation
}
