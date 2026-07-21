using WorldEngine.Sim.Core;

namespace WorldEngine.UI.UI.Selection;

/// <summary>What kind of thing is currently selected.</summary>
public enum SelectionKind { None, Tile, Settlement, Character, Civ }

/// <summary>
/// The one "selected thing" in the UI (M6 Epic 6.1.4). This is UI-thread state that decides
/// which contextual panel is shown — it is deliberately <b>not</b> sim state. Tile inspection
/// still flows through the <c>SetInspectedTile</c> command (the snapshot must carry tile
/// detail), but the higher-level notion of what is selected lives here and needs no sim
/// round-trip, so it cannot affect determinism.
/// </summary>
// MAP: UI-side "selected thing" (tile/settlement/character/civ); drives contextual panel routing.
public sealed class SelectionState
{
    public SelectionKind Kind { get; private set; } = SelectionKind.None;
    /// <summary>Entity id for Character / Civ / Settlement selections; 0 otherwise.</summary>
    public long Id { get; private set; }
    /// <summary>Tile coordinate for a Tile selection.</summary>
    public TileCoord Coord { get; private set; }
    /// <summary>True when the selection changed and the router has not yet applied it.</summary>
    public bool Dirty { get; private set; }

    public void SelectTile(TileCoord coord)   { Set(SelectionKind.Tile, 0, coord); }
    public void SelectSettlement(long id)      { Set(SelectionKind.Settlement, id, default); }
    public void SelectCharacter(long id)       { Set(SelectionKind.Character, id, default); }
    public void SelectCiv(long id)             { Set(SelectionKind.Civ, id, default); }
    public void Clear()                        { Set(SelectionKind.None, 0, default); }

    /// <summary>Called by the router once it has reacted to the current selection.</summary>
    public void MarkHandled() => Dirty = false;

    private void Set(SelectionKind kind, long id, TileCoord coord)
    {
        Kind  = kind;
        Id    = id;
        Coord = coord;
        Dirty = true;
    }
}

/// <summary>
/// Applies <see cref="SelectionState"/> changes by invoking the wired callbacks — one central
/// dispatch point that replaces the scattered per-panel click handlers in <c>Game1</c>. The
/// callbacks are supplied by <c>Game1</c> (later routed through the panel manager), so this
/// class stays decoupled from concrete panels.
/// </summary>
public sealed class SelectionRouter
{
    private readonly SelectionState _state;

    public Action<TileCoord>? OnTile;
    public Action<long>?      OnSettlement;
    public Action<long>?      OnCharacter;
    public Action<long>?      OnCiv;
    public Action?            OnClear;

    public SelectionRouter(SelectionState state) => _state = state;

    /// <summary>If the selection changed since last frame, dispatch it to the matching callback.</summary>
    public void Apply()
    {
        if (!_state.Dirty) return;
        _state.MarkHandled();
        switch (_state.Kind)
        {
            case SelectionKind.Tile:       OnTile?.Invoke(_state.Coord);   break;
            case SelectionKind.Settlement: OnSettlement?.Invoke(_state.Id); break;
            case SelectionKind.Character:  OnCharacter?.Invoke(_state.Id);  break;
            case SelectionKind.Civ:        OnCiv?.Invoke(_state.Id);        break;
            case SelectionKind.None:       OnClear?.Invoke();               break;
        }
    }
}
