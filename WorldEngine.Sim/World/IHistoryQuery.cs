using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// Pre-indexed historical query API. Backed by SQLite summary tables built by
/// <see cref="WorldEngine.Sim.Persistence.SummaryBuilder"/>. Use
/// <see cref="WorldEngine.Sim.Persistence.EventStore.GetHistoryQuery"/> to obtain an instance.
/// </summary>
public interface IHistoryQuery
{
    /// <summary>Returns the pre-aggregated civ profile, or null if the civ has no summary row.</summary>
    CivSummary? GetCivSummary(CivId civId);

    /// <summary>Returns the pre-aggregated character profile, or null if not found.</summary>
    CharacterSummary? GetCharacterSummary(EntityId charId);

    /// <summary>Returns all rulers of a civilization ordered by succession ordinal.</summary>
    IReadOnlyList<CharacterSummary> GetRulersOfCiv(CivId civId);

    /// <summary>Returns the ruler of a civ who held the throne during <paramref name="year"/>, or null.</summary>
    CharacterSummary? GetRulerAtYear(CivId civId, int year);

    /// <summary>Returns events for a civ within the given year range, ordered by year.</summary>
    IReadOnlyList<SimEvent> GetCivHistory(CivId civId, int startYear, int endYear);

    /// <summary>Returns all events that involved the given character, ordered by year/season/tick.</summary>
    IReadOnlyList<SimEvent> GetCharacterHistory(EntityId charId);

    /// <summary>Returns events at or above <paramref name="minTier"/> in the given year range.</summary>
    IReadOnlyList<SimEvent> GetSignificantEvents(int startYear, int endYear, EventTier minTier);

    /// <summary>Returns all conflicts between two civilizations, ordered by declaration year.</summary>
    IReadOnlyList<ConflictRecord> GetConflictHistory(CivId civA, CivId civB);

    /// <summary>
    /// Returns all characters with the given name ordered by birth year.
    /// Returns a disambiguation list when multiple characters share the name.
    /// </summary>
    IReadOnlyList<CharacterSummary> FindCharactersByName(string name);

    /// <summary>Returns all civs ordered by founding year. Used to populate the civilization selector UI.</summary>
    IReadOnlyList<CivSummary> GetAllCivSummaries();

    /// <summary>
    /// Walks CausalEdges upstream from <paramref name="effectEventId"/> up to <paramref name="maxDepth"/> levels.
    /// Returns cause events in BFS order (closest causes first).
    /// </summary>
    IReadOnlyList<(long CauseEventId, SimEvent CauseEvent, string EdgeType)> GetCausalChain(long effectEventId, int maxDepth = 3);

    /// <summary>
    /// Returns event count grouped by decade bucket for the timeline density heatmap.
    /// Key = decade start year (e.g. 0, 10, 20 …), Value = event count in that decade.
    /// </summary>
    Dictionary<int, int> GetEventCountByDecade(int startYear, int endYear);

    /// <summary>
    /// Returns up to <paramref name="maxEvents"/> events that occurred at the given tile coordinate,
    /// ordered newest-first. Used by the tile inspect panel history section.
    /// </summary>
    IReadOnlyList<SimEvent> GetTileHistory(TileCoord coord, int maxEvents = 10);
}
