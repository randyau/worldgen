using WorldEngine.Sim.Core;

namespace WorldEngine.UI.UI.Selection;

// MAP: Small reference to a selectable entity — the payload EntityLink hands to ISelectionSink.
/// <summary>A reference to a selectable thing (tile/settlement/character/civ), for <c>EntityLink</c>.</summary>
public readonly record struct EntityRef(SelectionKind Kind, long Id, TileCoord Coord);

// SEAM: ISelectionSink → SelectionBus in 8.2. Composites depend on this narrow interface so
// they don't block on the SelectionBus promotion landing in phase 8.2.
/// <summary>Narrow sink an <c>EntityLink</c> reports clicks to; implemented by <see cref="SelectionState"/> today.</summary>
public interface ISelectionSink
{
    void Select(EntityRef target);
}

/// <summary>Adapts the existing <see cref="SelectionState"/> Select* methods to <see cref="ISelectionSink"/>.</summary>
public sealed class SelectionStateSink : ISelectionSink
{
    private readonly SelectionState _state;

    public SelectionStateSink(SelectionState state) => _state = state;

    public void Select(EntityRef target)
    {
        switch (target.Kind)
        {
            case SelectionKind.Tile:       _state.SelectTile(target.Coord);        break;
            case SelectionKind.Settlement: _state.SelectSettlement(target.Id);     break;
            case SelectionKind.Character:  _state.SelectCharacter(target.Id);      break;
            case SelectionKind.Civ:        _state.SelectCiv(target.Id);            break;
            default:                       _state.Clear();                        break;
        }
    }
}
