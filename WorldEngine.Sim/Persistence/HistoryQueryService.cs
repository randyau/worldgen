using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// SQLite-backed implementation of <see cref="IHistoryQuery"/>. Queries pre-indexed
/// summary tables built by <see cref="SummaryBuilder"/>. Maintains small in-memory
/// caches for frequently accessed civs and characters (≤20 entries each, LRU-evict).
/// Obtain via <see cref="EventStore.GetHistoryQuery"/>.
/// </summary>
public sealed class HistoryQueryService : IHistoryQuery
{
    private readonly SqliteConnection _conn;

    private const int CacheMaxSize = 20;

    // Simple LRU-evict caches (evict oldest-inserted when full)
    private readonly Dictionary<long, CivSummary>       _civCache  = new();
    private readonly Dictionary<long, CharacterSummary> _charCache = new();

    internal HistoryQueryService(SqliteConnection conn)
    {
        _conn = conn;
    }

    // ── IHistoryQuery ───────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public CivSummary? GetCivSummary(CivId civId)
    {
        long id = civId.Value;
        if (_civCache.TryGetValue(id, out var cached)) return cached;

        var row = _conn.QueryFirstOrDefault<CivSummaryRow>(
            "SELECT * FROM CivSummaries WHERE CivId = @id", new { id });
        if (row is null) return null;

        var summary = MapCivSummary(row);
        PutCivCache(id, summary);
        return summary;
    }

    /// <inheritdoc/>
    public CharacterSummary? GetCharacterSummary(EntityId charId)
    {
        long id = charId.Value;
        if (_charCache.TryGetValue(id, out var cached)) return cached;

        var row = _conn.QueryFirstOrDefault<CharacterSummaryRow>(
            "SELECT * FROM CharacterSummaries WHERE CharacterId = @id", new { id });
        if (row is null) return null;

        var summary = MapCharSummary(row);
        PutCharCache(id, summary);
        return summary;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CharacterSummary> GetRulersOfCiv(CivId civId)
    {
        var charIds = _conn.Query<long>(
            "SELECT CharId FROM SuccessionChain WHERE CivId = @id ORDER BY Ordinal",
            new { id = civId.Value }).ToList();

        var results = new List<CharacterSummary>(charIds.Count);
        foreach (long charId in charIds)
        {
            var summary = GetCharacterSummary(new EntityId(charId));
            if (summary is not null) results.Add(summary);
        }
        return results;
    }

    /// <inheritdoc/>
    public CharacterSummary? GetRulerAtYear(CivId civId, int year)
    {
        // Find the ruler whose reign spans the given year
        long? charId = _conn.QueryFirstOrDefault<long?>(
            """
            SELECT CharId FROM SuccessionChain
            WHERE CivId = @civId
              AND (TookThroneYear IS NULL OR TookThroneYear <= @year)
              AND (LostThroneYear IS NULL OR LostThroneYear > @year)
            ORDER BY Ordinal DESC
            LIMIT 1
            """,
            new { civId = civId.Value, year });

        return charId.HasValue ? GetCharacterSummary(new EntityId(charId.Value)) : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimEvent> GetCivHistory(CivId civId, int startYear, int endYear)
    {
        return _conn.Query<EventRow>(
            $"{SelectCols} WHERE CivId = @civId AND Year BETWEEN @startYear AND @endYear ORDER BY Year, Season, Tick",
            new { civId = (long)civId.Value, startYear, endYear })
            .Select(MapEvent).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimEvent> GetCharacterHistory(EntityId charId)
    {
        return _conn.Query<EventRow>(
            $"""
            {SelectCols}
            WHERE Id IN (
                SELECT EventId FROM EventEntities WHERE EntityId = @charId
            )
            ORDER BY Year, Season, Tick
            """,
            new { charId = charId.Value })
            .Select(MapEvent).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimEvent> GetSignificantEvents(int startYear, int endYear, EventTier minTier)
    {
        return _conn.Query<EventRow>(
            $"{SelectCols} WHERE TierInvolvement >= @tier AND Year BETWEEN @startYear AND @endYear ORDER BY Year",
            new { tier = (int)minTier, startYear, endYear })
            .Select(MapEvent).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ConflictRecord> GetConflictHistory(CivId civA, CivId civB)
    {
        long a = civA.Value, b = civB.Value;

        // Find WarDeclared events between these two civs (explicit column selection for safe tuple mapping)
        var warDeclRows = _conn.Query<WarLightRow>(
            "SELECT Id, Year, PayloadJson FROM Events WHERE Type = @type AND CivId IS NOT NULL ORDER BY Year",
            new { type = 3103 }).ToList();

        // Filter to only wars between these two civs
        var warDecls = warDeclRows
            .Where(row =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("DeclarerCivId", out var dc)) return false;
                    if (!root.TryGetProperty("TargetCivId",   out var tc)) return false;
                    long declarer = dc.GetInt64(), target = tc.GetInt64();
                    return (declarer == a && target == b) || (declarer == b && target == a);
                }
                catch { return false; }
            }).ToList();

        if (warDecls.Count == 0) return Array.Empty<ConflictRecord>();

        // Load WarEnded events keyed by WarNumber
        var warEnds = new Dictionary<int, (int Year, string Outcome)>();
        foreach (var row in _conn.Query<WarLightRow>(
            "SELECT Id, Year, PayloadJson FROM Events WHERE Type = @type ORDER BY Year",
            new { type = 3104 }))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (!root.TryGetProperty("CivAId",    out var ca)) continue;
                if (!root.TryGetProperty("CivBId",    out var cb)) continue;
                if (!root.TryGetProperty("WarNumber", out var wn)) continue;
                long civAId = ca.GetInt64(), civBId = cb.GetInt64();
                if (!((civAId == a && civBId == b) || (civAId == b && civBId == a))) continue;
                int  num     = wn.GetInt32();
                string outcome = root.TryGetProperty("Outcome", out var o) ? o.GetString() ?? "unknown" : "unknown";
                warEnds.TryAdd(num, (row.Year, outcome));
            }
            catch { /* skip */ }
        }

        var results = new List<ConflictRecord>(warDecls.Count);
        foreach (var wd in warDecls)
        {
            int warNumber = 0;
            try
            {
                using var doc = JsonDocument.Parse(wd.PayloadJson ?? "{}");
                if (doc.RootElement.TryGetProperty("WarNumber", out var wn)) warNumber = wn.GetInt32();
            }
            catch { /* skip */ }

            int    endYear = warEnds.TryGetValue(warNumber, out var we) ? we.Year : wd.Year;
            string outcome = warEnds.TryGetValue(warNumber, out we)     ? we.Outcome : "ongoing";

            int battleCount = _conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Events WHERE Type = @type AND (CivId = @a OR CivId = @b) AND Year BETWEEN @s AND @e",
                new { type = 3105, a, b, s = wd.Year, e = endYear });

            results.Add(new ConflictRecord(
                WarDeclarationEventId: wd.Id,
                CivAId:       a,
                CivBId:       b,
                DeclaredYear: wd.Year,
                EndedYear:    endYear,
                Outcome:      outcome,
                BattleCount:  battleCount,
                WarNumber:    warNumber));
        }

        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CharacterSummary> FindCharactersByName(string name)
    {
        var rows = _conn.Query<CharacterSummaryRow>(
            "SELECT * FROM CharacterSummaries WHERE LOWER(Name) = LOWER(@name) ORDER BY BirthYear",
            new { name }).ToList();
        return rows.Select(MapCharSummary).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CivSummary> GetAllCivSummaries()
    {
        var rows = _conn.Query<CivSummaryRow>(
            "SELECT * FROM CivSummaries ORDER BY FoundedYear").ToList();
        return rows.Select(MapCivSummary).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<(long CauseEventId, SimEvent CauseEvent, string EdgeType)> GetCausalChain(long effectEventId, int maxDepth = 3)
    {
        var result  = new List<(long, SimEvent, string)>();
        var visited = new HashSet<long> { effectEventId };
        var queue   = new Queue<(long Id, int Depth)>();
        queue.Enqueue((effectEventId, 0));

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;

            var edges = _conn.Query<CausalEdgeRow>(
                "SELECT PredecessorId, EdgeType FROM CausalEdges WHERE SuccessorId = @id",
                new { id = currentId }).ToList();

            foreach (var edge in edges)
            {
                if (!visited.Add(edge.PredecessorId)) continue;

                var evRow = _conn.QueryFirstOrDefault<EventRow>(
                    $"{SelectCols} WHERE Id = @id", new { id = edge.PredecessorId });
                if (evRow is not null)
                {
                    result.Add((edge.PredecessorId, MapEvent(evRow), edge.EdgeType ?? "caused"));
                    queue.Enqueue((edge.PredecessorId, depth + 1));
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public Dictionary<int, int> GetEventCountByDecade(int startYear, int endYear)
    {
        return _conn.Query<(int Decade, int Count)>(
            """
            SELECT (Year / 10) * 10 AS Decade, COUNT(*) AS Count
            FROM Events
            WHERE Year BETWEEN @start AND @end
            GROUP BY Decade
            ORDER BY Decade
            """,
            new { start = startYear, end = endYear })
            .ToDictionary(r => r.Decade, r => r.Count);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimEvent> GetTileHistory(TileCoord coord, int maxEvents = 10)
    {
        return _conn.Query<EventRow>(
            $"{SelectCols} WHERE LocationX = @x AND LocationY = @y ORDER BY Year DESC, Season DESC, Tick DESC LIMIT @limit",
            new { x = coord.X, y = coord.Y, limit = maxEvents })
            .Select(MapEvent).ToList();
    }

    // ── Mapping ─────────────────────────────────────────────────────────────────────────────────

    private static CivSummary MapCivSummary(CivSummaryRow r) => new(
        CivId:              r.CivId,
        Name:               r.Name,
        FoundedYear:        r.FoundedYear,
        CollapseYear:       r.CollapseYear,
        IsCollapsed:        r.IsCollapsed != 0,
        PeakSettlements:    r.PeakSettlements,
        TotalRulers:        r.TotalRulers,
        TotalWarsInitiated: r.TotalWarsInitiated,
        TotalWarsSuffered:  r.TotalWarsSuffered,
        TotalYearsAtWar:    r.TotalYearsAtWar,
        DominantAncestry:   r.DominantAncestry,
        CulturalTraits:     ParseStringArray(r.CulturalTraits),
        FirstRulerName:     r.FirstRulerName,
        LastRulerName:      r.LastRulerName
    );

    private static CharacterSummary MapCharSummary(CharacterSummaryRow r) => new(
        CharacterId:        r.CharacterId,
        Name:               r.Name,
        Epithet:            r.Epithet,
        NameOrdinal:        r.NameOrdinal,
        AncestryId:         r.AncestryId,
        CivId:              r.CivId,
        CivName:            r.CivName,
        RulerOrdinal:       r.RulerOrdinal,
        BirthYear:          r.BirthYear,
        DeathYear:          r.DeathYear,
        DeathCause:         r.DeathCause,
        AgeSeasons:         r.AgeSeasons,
        WarsInitiated:      r.WarsInitiated,
        SettlementsFounded: r.SettlementsFounded,
        ArtworksCreated:    r.ArtworksCreated,
        SignificantEventIds: ParseLongArray(r.SignificantEvents)
    );

    private static SimEvent MapEvent(EventRow r) => new()
    {
        Id               = new EventId(r.Id),
        Type             = (EventType)r.Type,
        TypeName         = r.TypeName,
        Domain           = r.Domain,
        Year             = r.Year,
        Season           = (Season)r.Season,
        Tick             = r.Tick,
        Location         = r.LocationX.HasValue && r.LocationY.HasValue
                               ? new TileCoord(r.LocationX.Value, r.LocationY.Value)
                               : null,
        TierInvolvement  = (EventTier)r.TierInvolvement,
        VerbClass        = (VerbClass)r.VerbClass,
        PopulationImpact = (PopulationImpact)r.PopulationImpact,
        IsFirstOfKind    = r.IsFirstOfKind != 0,
        IsGodMode        = r.IsGodMode != 0,
        ActorId          = r.ActorId ?? 0,
        ActorName        = r.ActorName,
        CivId            = r.CivId ?? 0,
        SettlementName   = r.SettlementName,
        PayloadJson      = r.PayloadJson,
        SignificanceScore = r.SignificanceScore
    };

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<long> ParseLongArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<long>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetInt64())
                .ToList();
        }
        catch { return Array.Empty<long>(); }
    }

    // ── Cache helpers ────────────────────────────────────────────────────────────────────────────

    private void PutCivCache(long id, CivSummary summary)
    {
        if (_civCache.Count >= CacheMaxSize)
        {
            var oldest = _civCache.Keys.First();
            _civCache.Remove(oldest);
        }
        _civCache[id] = summary;
    }

    private void PutCharCache(long id, CharacterSummary summary)
    {
        if (_charCache.Count >= CacheMaxSize)
        {
            var oldest = _charCache.Keys.First();
            _charCache.Remove(oldest);
        }
        _charCache[id] = summary;
    }

    // ── SQL constants ────────────────────────────────────────────────────────────────────────────

    private const string SelectCols =
        "SELECT Id, Type, TypeName, Domain, Year, Season, Tick, LocationX, LocationY, " +
        "TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode, " +
        "ActorId, ActorName, CivId, SettlementName, PayloadJson, SignificanceScore FROM Events";

    // ── Row types ────────────────────────────────────────────────────────────────────────────────

    private sealed class CivSummaryRow
    {
        public long    CivId               { get; init; }
        public string  Name                { get; init; } = "";
        public int     FoundedYear         { get; init; }
        public int     CollapseYear        { get; init; }
        public int     IsCollapsed         { get; init; }
        public int     PeakSettlements     { get; init; }
        public int     TotalRulers         { get; init; }
        public int     TotalWarsInitiated  { get; init; }
        public int     TotalWarsSuffered   { get; init; }
        public int     TotalYearsAtWar     { get; init; }
        public string? DominantAncestry    { get; init; }
        public string? CulturalTraits      { get; init; }
        public string? FirstRulerName      { get; init; }
        public string? LastRulerName       { get; init; }
    }

    private sealed class CharacterSummaryRow
    {
        public long    CharacterId        { get; init; }
        public string  Name               { get; init; } = "";
        public string? Epithet            { get; init; }
        public int     NameOrdinal        { get; init; }
        public string? AncestryId         { get; init; }
        public long    CivId              { get; init; }
        public string? CivName            { get; init; }
        public int     RulerOrdinal       { get; init; }
        public int     BirthYear          { get; init; }
        public int     DeathYear          { get; init; }
        public string? DeathCause         { get; init; }
        public int     AgeSeasons         { get; init; }
        public int     WarsInitiated      { get; init; }
        public int     SettlementsFounded { get; init; }
        public int     ArtworksCreated    { get; init; }
        public string? SignificantEvents  { get; init; }
    }

    private sealed class WarLightRow
    {
        public long   Id          { get; init; }
        public int    Year        { get; init; }
        public string PayloadJson { get; init; } = "{}";
    }

    private sealed class EventRow
    {
        public long    Id               { get; init; }
        public int     Type             { get; init; }
        public string  TypeName         { get; init; } = "";
        public string  Domain           { get; init; } = "";
        public int     Year             { get; init; }
        public int     Season           { get; init; }
        public long    Tick             { get; init; }
        public int?    LocationX        { get; init; }
        public int?    LocationY        { get; init; }
        public int     TierInvolvement  { get; init; }
        public int     VerbClass        { get; init; }
        public int     PopulationImpact { get; init; }
        public int     IsFirstOfKind    { get; init; }
        public int     IsGodMode        { get; init; }
        public long?   ActorId          { get; init; }
        public string? ActorName        { get; init; }
        public long?   CivId            { get; init; }
        public string? SettlementName   { get; init; }
        public string  PayloadJson      { get; init; } = "{}";
        public float   SignificanceScore { get; init; }
    }

    private sealed class CausalEdgeRow
    {
        public long    PredecessorId { get; init; }
        public string? EdgeType      { get; init; }
    }
}
