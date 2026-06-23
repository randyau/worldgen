using Dapper;
using Microsoft.Data.Sqlite;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// SQLite-backed event store. Holds a single persistent connection (required so that
/// an in-memory database survives between calls). Implements <see cref="IHistoryGraphReadOnly"/>.
/// </summary>
public sealed class EventStore : IHistoryGraphReadOnly, IDisposable
{
    private readonly SqliteConnection _conn;

    public EventStore(string dbPath = ":memory:")
    {
        var connString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = dbPath == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        }.ToString();

        _conn = new SqliteConnection(connString);
        _conn.Open();
        InitializeSchema();
    }

    /// <summary>
    /// Creates tables, indexes and sets pragmas. Idempotent — safe to call repeatedly.
    /// Invoked automatically by the constructor.
    /// </summary>
    public void InitializeSchema()
    {
        _conn.Execute("PRAGMA journal_mode=WAL;");
        _conn.Execute("PRAGMA synchronous=NORMAL;");
        _conn.Execute("PRAGMA foreign_keys=ON;");

        _conn.Execute(DatabaseSchema.CreateEvents);
        _conn.Execute(DatabaseSchema.CreateIndexYear);
        _conn.Execute(DatabaseSchema.CreateIndexType);
        _conn.Execute(DatabaseSchema.CreateIndexTier);
        _conn.Execute(DatabaseSchema.CreateIndexLocation);
        _conn.Execute(DatabaseSchema.CreateCausalEdges);
        _conn.Execute(DatabaseSchema.CreateEventEntities);
        _conn.Execute(DatabaseSchema.CreateIndexEventEntities);
    }

    /// <summary>
    /// Inserts events in a single transaction. Returns copies with DB-assigned Ids.
    /// </summary>
    public IReadOnlyList<SimEvent> BatchInsert(IEnumerable<SimEvent> events)
    {
        var result = new List<SimEvent>();
        using var tx = _conn.BeginTransaction();

        const string insertSql = """
            INSERT INTO Events
                (Type, Year, Season, Tick, LocationX, LocationY,
                 TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode, PayloadJson)
            VALUES
                (@Type, @Year, @Season, @Tick, @LocationX, @LocationY,
                 @TierInvolvement, @VerbClass, @PopulationImpact, @IsFirstOfKind, @IsGodMode, @PayloadJson);
            SELECT last_insert_rowid();
            """;

        foreach (var ev in events)
        {
            long id = _conn.ExecuteScalar<long>(insertSql, new
            {
                Type             = (int)ev.Type,
                ev.Year,
                Season           = (int)ev.Season,
                ev.Tick,
                LocationX        = ev.Location?.X,
                LocationY        = ev.Location?.Y,
                TierInvolvement  = (int)ev.TierInvolvement,
                VerbClass        = (int)ev.VerbClass,
                PopulationImpact = (int)ev.PopulationImpact,
                IsFirstOfKind    = ev.IsFirstOfKind ? 1 : 0,
                IsGodMode        = ev.IsGodMode ? 1 : 0,
                ev.PayloadJson
            }, tx);

            result.Add(ev with { Id = new EventId(id) });
        }

        tx.Commit();
        return result;
    }

    public void InsertCausalEdges(IEnumerable<(long PredecessorId, long SuccessorId)> edges)
    {
        using var tx = _conn.BeginTransaction();
        const string sql = """
            INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId)
            VALUES (@PredecessorId, @SuccessorId);
            """;
        foreach (var (pred, succ) in edges)
            _conn.Execute(sql, new { PredecessorId = pred, SuccessorId = succ }, tx);
        tx.Commit();
    }

    public void InsertEventEntities(IEnumerable<(long EventId, long EntityId)> pairs)
    {
        using var tx = _conn.BeginTransaction();
        const string sql = """
            INSERT OR IGNORE INTO EventEntities (EventId, EntityId)
            VALUES (@EventId, @EntityId);
            """;
        foreach (var (evId, entId) in pairs)
            _conn.Execute(sql, new { EventId = evId, EntityId = entId }, tx);
        tx.Commit();
    }

    // ---- IHistoryGraphReadOnly ----

    public SimEvent? GetEvent(EventId id)
    {
        var row = _conn.QuerySingleOrDefault<EventRow>(
            $"{SelectColumns} WHERE Id = @Id;", new { Id = id.Value });
        return row is null ? null : MapRow(row);
    }

    public IEnumerable<SimEvent> GetEventsByYear(int year) =>
        Query($"{SelectColumns} WHERE Year = @year ORDER BY Id;", new { year });

    public IEnumerable<SimEvent> GetEventsByYearRange(int fromYear, int toYear) =>
        Query($"{SelectColumns} WHERE Year >= @fromYear AND Year <= @toYear ORDER BY Id;", new { fromYear, toYear });

    public IEnumerable<SimEvent> GetHeadlineEvents(int fromYear, int toYear) =>
        Query($"{SelectColumns} WHERE TierInvolvement = @tier AND Year >= @fromYear AND Year <= @toYear ORDER BY Id;",
            new { tier = (int)EventTier.Headline, fromYear, toYear });

    public IEnumerable<SimEvent> GetEventsByLocation(TileCoord coord, int radiusWorldTiles = 0)
    {
        return Query(
            $"{SelectColumns} WHERE LocationX IS NOT NULL " +
            "AND LocationX >= @minX AND LocationX <= @maxX " +
            "AND LocationY >= @minY AND LocationY <= @maxY ORDER BY Id;",
            new
            {
                minX = coord.X - radiusWorldTiles, maxX = coord.X + radiusWorldTiles,
                minY = coord.Y - radiusWorldTiles, maxY = coord.Y + radiusWorldTiles
            });
    }

    public IEnumerable<SimEvent> GetCausalPredecessors(EventId eventId) =>
        Query($"{SelectColumns} WHERE Id IN (SELECT PredecessorId FROM CausalEdges WHERE SuccessorId = @id) ORDER BY Id;",
            new { id = eventId.Value });

    public IEnumerable<SimEvent> GetCausalSuccessors(EventId eventId) =>
        Query($"{SelectColumns} WHERE Id IN (SELECT SuccessorId FROM CausalEdges WHERE PredecessorId = @id) ORDER BY Id;",
            new { id = eventId.Value });

    public IEnumerable<SimEvent> GetCausalChain(EventId eventId, int maxDepth = 10)
    {
        // Breadth-first walk over successors, bounded by maxDepth.
        var visited = new HashSet<long>();
        var ordered = new List<SimEvent>();
        var frontier = new Queue<(long id, int depth)>();
        frontier.Enqueue((eventId.Value, 0));

        while (frontier.Count > 0)
        {
            var (id, depth) = frontier.Dequeue();
            if (!visited.Add(id)) continue;

            var ev = GetEvent(new EventId(id));
            if (ev is not null) ordered.Add(ev);

            if (depth >= maxDepth) continue;

            var successorIds = _conn.Query<long>(
                "SELECT SuccessorId FROM CausalEdges WHERE PredecessorId = @id ORDER BY SuccessorId;",
                new { id });
            foreach (var s in successorIds)
                if (!visited.Contains(s))
                    frontier.Enqueue((s, depth + 1));
        }

        return ordered;
    }

    public IEnumerable<SimEvent> GetEventsByType(EventType type, int fromYear = 0, int toYear = int.MaxValue) =>
        Query($"{SelectColumns} WHERE Type = @type AND Year >= @fromYear AND Year <= @toYear ORDER BY Id;",
            new { type = (int)type, fromYear, toYear });

    public IEnumerable<SimEvent> GetEventsByTier(EventTier tier, int fromYear = 0, int toYear = int.MaxValue) =>
        Query($"{SelectColumns} WHERE TierInvolvement = @tier AND Year >= @fromYear AND Year <= @toYear ORDER BY Id;",
            new { tier = (int)tier, fromYear, toYear });

    public IEnumerable<SimEvent> GetEventsByVerbClass(VerbClass verbClass, int fromYear = 0, int toYear = int.MaxValue) =>
        Query($"{SelectColumns} WHERE VerbClass = @verbClass AND Year >= @fromYear AND Year <= @toYear ORDER BY Id;",
            new { verbClass = (int)verbClass, fromYear, toYear });

    public IEnumerable<SimEvent> GetFirstOfKindEvents(int fromYear = 0, int toYear = int.MaxValue) =>
        Query($"{SelectColumns} WHERE IsFirstOfKind = 1 AND Year >= @fromYear AND Year <= @toYear ORDER BY Id;",
            new { fromYear, toYear });

    /// <summary>
    /// Removes all events and causal edges while keeping the schema intact.
    /// Use before reusing the same DB file for a new world.
    /// </summary>
    public void Truncate()
    {
        _conn.Execute("DELETE FROM EventEntities;");
        _conn.Execute("DELETE FROM CausalEdges;");
        _conn.Execute("DELETE FROM Events;");
        _conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    public void Dispose() => _conn.Dispose();

    // ---- helpers ----

    private const string SelectColumns =
        "SELECT Id, Type, Year, Season, Tick, LocationX, LocationY, " +
        "TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode, PayloadJson FROM Events";

    private IEnumerable<SimEvent> Query(string sql, object param) =>
        _conn.Query<EventRow>(sql, param).Select(MapRow).ToList();

    private static SimEvent MapRow(EventRow r) => new()
    {
        Id               = new EventId(r.Id),
        Type             = (EventType)r.Type,
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
        PayloadJson      = r.PayloadJson
    };

    private sealed class EventRow
    {
        public long Id { get; init; }
        public int Type { get; init; }
        public int Year { get; init; }
        public int Season { get; init; }
        public long Tick { get; init; }
        public int? LocationX { get; init; }
        public int? LocationY { get; init; }
        public int TierInvolvement { get; init; }
        public int VerbClass { get; init; }
        public int PopulationImpact { get; init; }
        public int IsFirstOfKind { get; init; }
        public int IsGodMode { get; init; }
        public string PayloadJson { get; init; } = "{}";
    }
}
