using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Sim.Commands;

/// <summary>
/// Player spotlight commands (M7+). Each command represents a player intent for
/// controlling or influencing a specific character. All fields are value-type only.
/// </summary>

/// <summary>Enter spotlight mode controlling the given character.</summary>
public sealed record EnterSpotlight(EntityId CharacterId) : ICommand;

/// <summary>Exit spotlight mode and return the character to autonomous AI control.</summary>
public sealed record ExitSpotlight : ICommand;

/// <summary>Set the player's movement intent: the spotlit character should move toward this tile.</summary>
public sealed record SetSpotlightMoveIntent(TileCoord Target) : ICommand;

/// <summary>Set the player's goal intent: bias the spotlit character toward this goal type.</summary>
public sealed record SetSpotlightGoalIntent(GoalType Goal) : ICommand;

/// <summary>Set the player's social intent: bias the spotlit character toward interacting with this character.</summary>
public sealed record SetSpotlightSocialIntent(EntityId TargetCharacterId) : ICommand;
