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
    public required string PayloadJson { get; init; }
    public string? GeneratedProse { get; init; }  // V2: LLM generation
}
