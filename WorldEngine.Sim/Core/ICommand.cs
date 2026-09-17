namespace WorldEngine.Sim.Core;

/// <summary>
/// Marker interface for simulation commands.
/// All implementations must be sealed records with value-type fields only.
/// No callbacks, delegates, or mutable object references.
/// </summary>
public interface ICommand { }

/// <summary>
/// Marker for the commands resolved by <c>CivTracker.Resolve</c>. Implementing it is the single
/// thing a new civ-level command must do to be dispatched: <c>CharacterBehaviorPhase
/// .ResolveCommand</c> matches on <c>ICivCommand</c> and delegates, so the command type no longer
/// has to be listed in two switches (a mismatch used to make the command silently no-op forever).
/// </summary>
public interface ICivCommand : ICommand { }
