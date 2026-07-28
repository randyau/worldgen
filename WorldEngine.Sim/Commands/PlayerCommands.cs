using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Commands;

/// <summary>
/// UI-to-sim command records: SetSimSpeed, PauseToggle, StepOneTick, SetViewport (no-op); routed via CommandQueue.
/// </summary>
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
/// Sets the Watch panel's target — any entity in the registry (character, beast, ...), not just
/// characters. Pass an EntityId with Value 0 to clear. The watched entity's Kind is resolved from
/// the registry when the command is handled, not carried on the command itself, so this same
/// command works for any current or future watchable entity kind.
/// </summary>
public sealed record WatchEntity(EntityId Id) : ICommand;

/// <summary>
/// Requests a save to the given directory. Handled by SimLoop on a background Task.
/// </summary>
public sealed record SaveWorld(string SaveDir) : ICommand;

/// <summary>
/// Writes one sparse local-tile modification to persistent storage (M11 phase 11.5). No real
/// gameplay system produces this yet — it exists to prove the LocalTileDelta pipeline end-to-end
/// ahead of a future milestone wiring in actual local-scale interaction; see
/// docs/phases/m11_local_scale_generation.md "Explicitly out of scope".
/// </summary>
public sealed record ModifyLocalTile(
    ChunkCoord Chunk, LocalTileCoord Local, LocalChangeType ChangeType, string PayloadJson) : ICommand;
