using WorldEngine.Sim.Core;

namespace WorldEngine.UI.UI.Selection;

/// <summary>Immutable snapshot of the current selection, broadcast by <see cref="SelectionBus"/>.</summary>
public readonly record struct SelectionSnapshot(SelectionKind Kind, long Id, TileCoord Coord);

// MAP: The one "what am I looking at" channel (framework §7.1) — replaces SelectionRouter and
// every consume-once navigation poller that used to live in Game1/panels (M8 phase 8.2).
/// <summary>
/// Single selection bus for the UI. Panels/composites call <see cref="Select"/> directly at the
/// click site instead of setting a per-panel pending field for <c>Game1</c> to poll each frame.
/// UI-only state — see the determinism note on <see cref="SelectionState"/>: tile inspection
/// still round-trips a sim command because the snapshot must carry tile detail, but "what is
/// selected" needs no sim round-trip.
/// </summary>
public sealed class SelectionBus : ISelectionSink
{
    private readonly SelectionState _state = new();

    public SelectionSnapshot Current { get; private set; }

    /// <summary>Fired once per change when <see cref="Apply"/> observes a dirty selection.</summary>
    public event Action<SelectionSnapshot>? Changed;

    public void Select(EntityRef target)
    {
        switch (target.Kind)
        {
            case SelectionKind.Tile:       _state.SelectTile(target.Coord);    break;
            case SelectionKind.Settlement: _state.SelectSettlement(target.Id); break;
            case SelectionKind.Character:  _state.SelectCharacter(target.Id);  break;
            case SelectionKind.Civ:        _state.SelectCiv(target.Id);        break;
            default:                       _state.Clear();                    break;
        }
    }

    public void Clear() => _state.Clear();

    /// <summary>Dispatches the current selection to <see cref="Changed"/>. Call once per frame.</summary>
    public void Apply()
    {
        if (!_state.Dirty) return;
        _state.MarkHandled();
        Current = new SelectionSnapshot(_state.Kind, _state.Id, _state.Coord);
        Changed?.Invoke(Current);
    }
}
