using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities;

// All entity commands are sealed records with value-type fields only.
// No callbacks, delegates, or mutable object references (CLAUDE.md Mandatory Pattern #4).

// Beast commands
public sealed record MoveToTile(EntityId EntityId, TileCoord Destination) : ICommand;
public sealed record Graze(EntityId EntityId) : ICommand;
public sealed record Rest(EntityId EntityId) : ICommand;
public sealed record Attack(EntityId Attacker, EntityId Target) : ICommand;
public sealed record Flee(EntityId EntityId, TileCoord AwayFrom) : ICommand;

// Character commands (Phase 2.2+)
public sealed record EstablishSettlement(EntityId CharacterId, TileCoord Tile) : ICommand;
public sealed record AllyWith(EntityId CharacterId, EntityId TargetId) : ICommand;
public sealed record DeclareRivalry(EntityId CharacterId, EntityId TargetId) : ICommand;
// War is a civ-level action: the declaring character must be their civ's ruler;
// the target is a civilization, not an individual character.
public sealed record DeclareWar(EntityId CharacterId, CivId TargetCivId) : ICommand;
public sealed record RaidSettlement(EntityId CharacterId, TileCoord SettlementTile) : ICommand;
public sealed record Negotiate(EntityId CharacterId, EntityId TargetId) : ICommand;
public sealed record CreateArtwork(EntityId CharacterId) : ICommand;
public sealed record FleeRegion(EntityId CharacterId, TileCoord Destination) : ICommand;

// Phase 3.0 — city-state territory commands
public sealed record BuildImprovement(
    EntityId        CharacterId,
    TileCoord       TargetTile,
    ImprovementType ImprovementType) : ICommand;
