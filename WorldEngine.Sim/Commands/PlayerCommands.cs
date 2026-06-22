using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Commands;

public sealed record SetSimSpeed(SimSpeed Speed) : ICommand;
public sealed record PauseToggle : ICommand;
public sealed record StepOneTick : ICommand;
public sealed record SetViewport(int X, int Y, int Width, int Height) : ICommand;
public sealed record SetInspectedTile(TileCoord? Coord) : ICommand;
public sealed record SetActiveOverlay(OverlayType Overlay) : ICommand;
