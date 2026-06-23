using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities;

// All entity commands are sealed records with value-type fields only.
// No callbacks, delegates, or mutable object references (CLAUDE.md Mandatory Pattern #4).

public sealed record MoveToTile(EntityId EntityId, TileCoord Destination) : ICommand;
public sealed record Graze(EntityId EntityId) : ICommand;
public sealed record Rest(EntityId EntityId) : ICommand;
public sealed record Attack(EntityId Attacker, EntityId Target) : ICommand;
public sealed record Flee(EntityId EntityId, TileCoord AwayFrom) : ICommand;
