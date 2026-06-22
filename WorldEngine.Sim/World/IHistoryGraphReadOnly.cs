using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// Read-only query surface over the persisted history graph (SQLite Events + CausalEdges).
/// </summary>
public interface IHistoryGraphReadOnly
{
    SimEvent? GetEvent(EventId id);
    IEnumerable<SimEvent> GetEventsByYear(int year);
    IEnumerable<SimEvent> GetEventsByYearRange(int fromYear, int toYear);
    IEnumerable<SimEvent> GetHeadlineEvents(int fromYear, int toYear);
    IEnumerable<SimEvent> GetEventsByLocation(TileCoord coord, int radiusWorldTiles = 0);
    IEnumerable<SimEvent> GetCausalPredecessors(EventId eventId);
    IEnumerable<SimEvent> GetCausalSuccessors(EventId eventId);
    IEnumerable<SimEvent> GetCausalChain(EventId eventId, int maxDepth = 10);
    IEnumerable<SimEvent> GetEventsByType(EventType type, int fromYear = 0, int toYear = int.MaxValue);
    IEnumerable<SimEvent> GetEventsByTier(EventTier tier, int fromYear = 0, int toYear = int.MaxValue);
    IEnumerable<SimEvent> GetEventsByVerbClass(VerbClass verbClass, int fromYear = 0, int toYear = int.MaxValue);
    IEnumerable<SimEvent> GetFirstOfKindEvents(int fromYear = 0, int toYear = int.MaxValue);
}
