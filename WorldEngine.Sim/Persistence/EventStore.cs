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
    /// Creates tables, indexes, views, and sets pragmas. Idempotent — safe to call repeatedly.
    /// </summary>
    public void InitializeSchema()
    {
        _conn.Execute("PRAGMA journal_mode=WAL;");
        _conn.Execute("PRAGMA synchronous=NORMAL;");
        _conn.Execute("PRAGMA foreign_keys=ON;");
        _conn.Execute("PRAGMA cache_size=-65536;");
        _conn.Execute("PRAGMA mmap_size=67108864;");
        _conn.Execute("PRAGMA temp_store=memory;");
        _conn.Execute("PRAGMA wal_autocheckpoint=1000;");

        _conn.Execute(DatabaseSchema.CreateEvents);
        _conn.Execute(DatabaseSchema.CreateCausalEdges);
        _conn.Execute(DatabaseSchema.CreateEventEntities);
        _conn.Execute(DatabaseSchema.CreateCharacterSummaries);
        _conn.Execute(DatabaseSchema.CreateCivSummaries);
        _conn.Execute(DatabaseSchema.CreateEras);
        _conn.Execute(DatabaseSchema.CreateSuccessionChain);
        _conn.Execute(DatabaseSchema.CreateDynasties);
        _conn.Execute(DatabaseSchema.CreateCivTraits);
        _conn.Execute(DatabaseSchema.CreateViewReadable);
        _conn.Execute(DatabaseSchema.CreateIndexYear);
        _conn.Execute(DatabaseSchema.CreateIndexType);
        _conn.Execute(DatabaseSchema.CreateIndexTier);
        _conn.Execute(DatabaseSchema.CreateIndexLocation);
        _conn.Execute(DatabaseSchema.CreateIndexCivId);
        _conn.Execute(DatabaseSchema.CreateIndexActorId);
        _conn.Execute(DatabaseSchema.CreateIndexEventEntities);

        // Migration: add SignificanceScore column to existing databases that predate Phase 3.2.
        // SQLite does not support ALTER TABLE ADD COLUMN IF NOT EXISTS, so we catch the error.
        try { _conn.Execute(DatabaseSchema.MigrateEventsAddSignificanceScore); }
        catch (Microsoft.Data.Sqlite.SqliteException) { /* column already exists */ }
    }

    /// <summary>
    /// Writes the entire classified event batch (events + causal edges + entity cross-refs)
    /// in a single SQLite transaction. Returns copies of the events with DB-assigned Ids.
    /// </summary>
    public IReadOnlyList<SimEvent> BatchWriteAll(IReadOnlyList<(PendingEvent Pe, SimEvent Ev)> batch)
    {
        if (batch.Count == 0) return Array.Empty<SimEvent>();

        var result = new List<SimEvent>(batch.Count);
        using var tx = _conn.BeginTransaction();

        foreach (var (_, ev) in batch)
        {
            long id = _conn.ExecuteScalar<long>(_insertEventSql, new
            {
                Type             = (int)ev.Type,
                ev.TypeName,
                ev.Domain,
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
                ActorId          = ev.ActorId == 0 ? (long?)null : ev.ActorId,
                ev.ActorName,
                CivId             = ev.CivId == 0 ? (long?)null : ev.CivId,
                ev.SettlementName,
                ev.PayloadJson,
                ev.SignificanceScore
            }, tx);
            result.Add(ev with { Id = new EventId(id) });
        }

        for (int i = 0; i < batch.Count; i++)
        {
            var pe    = batch[i].Pe;
            long evId = result[i].Id.Value;
            if (pe.CauseEventId is { } causeId && causeId.Value > 0)
                _conn.Execute(_insertEdgeSql,
                    new { PredecessorId = causeId.Value, SuccessorId = evId }, tx);
        }

        for (int i = 0; i < batch.Count; i++)
        {
            var pe    = batch[i].Pe;
            long evId = result[i].Id.Value;

            if (pe.PrimaryEntityIds is { Count: > 0 } primary)
                foreach (long eid in primary)
                    _conn.Execute(_insertEntitySql,
                        new { EventId = evId, EntityId = eid, Role = "Primary" }, tx);

            if (pe.SecondaryEntityIds is { Count: > 0 } secondary)
                foreach (long eid in secondary)
                    _conn.Execute(_insertEntitySql,
                        new { EventId = evId, EntityId = eid, Role = "Secondary" }, tx);
        }

        tx.Commit();
        return result;
    }

    /// <summary>
    /// Inserts events in a single transaction. Returns copies with DB-assigned Ids.
    /// Prefer <see cref="BatchWriteAll"/> when causal edges and entity refs are also needed.
    /// </summary>
    public IReadOnlyList<SimEvent> BatchInsert(IEnumerable<SimEvent> events)
    {
        var result = new List<SimEvent>();
        using var tx = _conn.BeginTransaction();

        foreach (var ev in events)
        {
            long id = _conn.ExecuteScalar<long>(_insertEventSql, new
            {
                Type             = (int)ev.Type,
                ev.TypeName,
                ev.Domain,
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
                ActorId          = ev.ActorId == 0 ? (long?)null : ev.ActorId,
                ev.ActorName,
                CivId             = ev.CivId == 0 ? (long?)null : ev.CivId,
                ev.SettlementName,
                ev.PayloadJson,
                ev.SignificanceScore
            }, tx);
            result.Add(ev with { Id = new EventId(id) });
        }

        tx.Commit();
        return result;
    }

    private const string _insertEventSql = """
        INSERT INTO Events
            (Type, TypeName, Domain, Year, Season, Tick, LocationX, LocationY,
             TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode,
             ActorId, ActorName, CivId, SettlementName, PayloadJson, SignificanceScore)
        VALUES
            (@Type, @TypeName, @Domain, @Year, @Season, @Tick, @LocationX, @LocationY,
             @TierInvolvement, @VerbClass, @PopulationImpact, @IsFirstOfKind, @IsGodMode,
             @ActorId, @ActorName, @CivId, @SettlementName, @PayloadJson, @SignificanceScore);
        SELECT last_insert_rowid();
        """;

    private const string _insertEdgeSql = """
        INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId)
        VALUES (@PredecessorId, @SuccessorId);
        """;

    private const string _insertEntitySql = """
        INSERT OR IGNORE INTO EventEntities (EventId, EntityId, Role)
        VALUES (@EventId, @EntityId, @Role);
        """;

    public void InsertCausalEdges(IEnumerable<(long PredecessorId, long SuccessorId)> edges)
    {
        using var tx = _conn.BeginTransaction();
        foreach (var (pred, succ) in edges)
            _conn.Execute(_insertEdgeSql, new { PredecessorId = pred, SuccessorId = succ }, tx);
        tx.Commit();
    }

    public void InsertEventEntities(IEnumerable<(long EventId, long EntityId)> pairs)
    {
        using var tx = _conn.BeginTransaction();
        foreach (var (evId, entId) in pairs)
            _conn.Execute(_insertEntitySql, new { EventId = evId, EntityId = entId, Role = "Primary" }, tx);
        tx.Commit();
    }

    /// <summary>
    /// Builds all pre-aggregated summary tables (CharacterSummaries, CivSummaries, Eras,
    /// SuccessionChain, Dynasties) and populates inferred CausalEdges from event patterns.
    /// Also runs the retroactive significance rescore pass.
    /// Call once at end of sim or on demand.
    /// </summary>
    public void BuildSummaries()
    {
        SummaryBuilder.BuildCharacterSummaries(_conn);
        SummaryBuilder.BuildCivSummaries(_conn);
        SummaryBuilder.BuildSuccessionChain(_conn);
        SummaryBuilder.BuildDynasties(_conn);
        SummaryBuilder.BuildEras(_conn);
        CausalEdgeBuilder.BuildCausalEdges(_conn);
        SignificanceRescoringPass.Run(_conn);
    }

    /// <summary>
    /// Writes a cultural trait assignment for a civilization into the CivTraits table.
    /// Called by CivTracker when a new trait is assigned so the DB stays in sync.
    /// </summary>
    public void WriteCivTrait(long civId, string trait, int year)
    {
        _conn.Execute("""
            INSERT OR IGNORE INTO CivTraits (CivId, Trait, YearSet)
            VALUES (@CivId, @Trait, @Year);
            """, new { CivId = civId, Trait = trait, Year = year });
    }

    /// <summary>
    /// Returns a <see cref="IHistoryQuery"/> backed by the current database connection.
    /// Call <see cref="BuildSummaries"/> before using the returned service to ensure
    /// summary tables are populated.
    /// </summary>
    public IHistoryQuery GetHistoryQuery() => new HistoryQueryService(_conn);

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
        var visited  = new HashSet<long>();
        var ordered  = new List<SimEvent>();
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
    /// </summary>
    public void Truncate()
    {
        _conn.Execute("DELETE FROM EventEntities;");
        _conn.Execute("DELETE FROM CausalEdges;");
        _conn.Execute("DELETE FROM Events;");
        _conn.Execute("DELETE FROM CharacterSummaries;");
        _conn.Execute("DELETE FROM CivSummaries;");
        _conn.Execute("DELETE FROM Eras;");
        _conn.Execute("DELETE FROM SuccessionChain;");
        _conn.Execute("DELETE FROM Dynasties;");
        _conn.Execute("DELETE FROM CivTraits;");
        _conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    public void Dispose() => _conn.Dispose();

    // ---- helpers ----

    private const string SelectColumns =
        "SELECT Id, Type, TypeName, Domain, Year, Season, Tick, LocationX, LocationY, " +
        "TierInvolvement, VerbClass, PopulationImpact, IsFirstOfKind, IsGodMode, " +
        "ActorId, ActorName, CivId, SettlementName, PayloadJson, SignificanceScore FROM Events";

    private IEnumerable<SimEvent> Query(string sql, object param) =>
        _conn.Query<EventRow>(sql, param).Select(MapRow).ToList();

    private static SimEvent MapRow(EventRow r) => new()
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

    private sealed class EventRow
    {
        public long Id { get; init; }
        public int Type { get; init; }
        public string TypeName { get; init; } = "";
        public string Domain { get; init; } = "";
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
        public long? ActorId { get; init; }
        public string? ActorName { get; init; }
        public long? CivId { get; init; }
        public string? SettlementName { get; init; }
        public string PayloadJson { get; init; } = "{}";
        public float SignificanceScore { get; init; }
    }
}
