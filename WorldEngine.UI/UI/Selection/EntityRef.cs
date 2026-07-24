using WorldEngine.Sim.Core;

namespace WorldEngine.UI.UI.Selection;

// MAP: Small reference to a selectable entity — the payload EntityLink hands to ISelectionSink.
/// <summary>A reference to a selectable thing (tile/settlement/character/civ), for <c>EntityLink</c>.</summary>
public readonly record struct EntityRef(SelectionKind Kind, long Id, TileCoord Coord);

/// <summary>Narrow sink an <c>EntityLink</c> reports clicks to; implemented by <see cref="SelectionBus"/>.</summary>
public interface ISelectionSink
{
    void Select(EntityRef target);
}
