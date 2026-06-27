using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Commands;

public sealed record SetSimSpeed(SimSpeed Speed) : ICommand;
public sealed record PauseToggle : ICommand;
public sealed record StepOneTick : ICommand;
// DECISION: SetViewport retained as a no-op ICommand; SimLoop no longer handles it.
// Camera viewport is now computed on the UI thread. Kept so CommandQueueTests can verify
// that arbitrary ICommand subtypes round-trip through the queue without issue.
public sealed record SetViewport(int X, int Y, int Width, int Height) : ICommand;
public sealed record SetInspectedTile(TileCoord? Coord) : ICommand;
public sealed record SetActiveOverlay(OverlayType Overlay) : ICommand;
/// <summary>
/// Sets the character watch target. Pass null EntityId (EntityId with Value 0) to clear.
/// </summary>
public sealed record WatchCharacter(EntityId CharacterId) : ICommand;

/// <summary>
/// Requests a save to the given directory. Handled by SimLoop on a background Task.
/// </summary>
public sealed record SaveWorld(string SaveDir) : ICommand;
