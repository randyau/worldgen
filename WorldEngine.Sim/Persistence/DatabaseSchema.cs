namespace WorldEngine.Sim.Persistence;

public static class DatabaseSchema
{
    public const string CreateEvents = """
        CREATE TABLE IF NOT EXISTS Events (
            Id               INTEGER PRIMARY KEY,
            Type             INTEGER NOT NULL,
            TypeName         TEXT    NOT NULL,
            Domain           TEXT    NOT NULL,
            Year             INTEGER NOT NULL,
            Season           INTEGER NOT NULL,
            Tick             INTEGER NOT NULL,
            LocationX        INTEGER,
            LocationY        INTEGER,
            TierInvolvement  INTEGER NOT NULL,
            VerbClass        INTEGER NOT NULL,
            PopulationImpact INTEGER NOT NULL,
            IsFirstOfKind    INTEGER NOT NULL,
            IsGodMode        INTEGER NOT NULL,
            ActorId          INTEGER,
            ActorName        TEXT,
            CivId            INTEGER,
            SettlementName   TEXT,
            PayloadJson      TEXT    NOT NULL
        );
        """;

    public const string CreateEventEntities = """
        CREATE TABLE IF NOT EXISTS EventEntities (
            EventId    INTEGER NOT NULL REFERENCES Events(Id),
            EntityId   INTEGER NOT NULL,
            Role       TEXT    NOT NULL DEFAULT 'Primary',
            PRIMARY KEY (EventId, EntityId)
        );
        """;

    public const string CreateCausalEdges = """
        CREATE TABLE IF NOT EXISTS CausalEdges (
            PredecessorId INTEGER NOT NULL REFERENCES Events(Id),
            SuccessorId   INTEGER NOT NULL REFERENCES Events(Id),
            PRIMARY KEY (PredecessorId, SuccessorId)
        );
        """;

    public const string CreateViewReadable = """
        CREATE VIEW IF NOT EXISTS EventsReadable AS
        SELECT
            e.Id,
            e.TypeName,
            e.Domain,
            e.Year,
            CASE e.Season
                WHEN 0 THEN 'Spring' WHEN 1 THEN 'Summer'
                WHEN 2 THEN 'Autumn' WHEN 3 THEN 'Winter'
            END AS SeasonName,
            CASE e.TierInvolvement
                WHEN 0 THEN 'Background' WHEN 1 THEN 'Character'
                WHEN 2 THEN 'Regional'   WHEN 3 THEN 'Headline'
            END AS Tier,
            CASE e.VerbClass
                WHEN 0 THEN 'Creation'     WHEN 1 THEN 'Destruction'
                WHEN 2 THEN 'Transformation' WHEN 3 THEN 'Transfer'
                WHEN 4 THEN 'Conflict'     WHEN 5 THEN 'Maintenance'
                WHEN 6 THEN 'Interaction'
            END AS Verb,
            CASE e.PopulationImpact
                WHEN 0 THEN 'None'    WHEN 1 THEN 'Minor'
                WHEN 2 THEN 'Moderate' WHEN 3 THEN 'Major'
                WHEN 4 THEN 'Catastrophic'
            END AS PopImpact,
            e.ActorId,
            e.ActorName,
            e.CivId,
            e.SettlementName,
            e.LocationX,
            e.LocationY,
            e.IsFirstOfKind,
            e.IsGodMode,
            e.PayloadJson
        FROM Events e;
        """;

    // Indexes
    public const string CreateIndexYear     = "CREATE INDEX IF NOT EXISTS idx_events_year     ON Events(Year);";
    public const string CreateIndexType     = "CREATE INDEX IF NOT EXISTS idx_events_type     ON Events(Type);";
    public const string CreateIndexTier     = "CREATE INDEX IF NOT EXISTS idx_events_tier     ON Events(TierInvolvement);";
    public const string CreateIndexLocation = "CREATE INDEX IF NOT EXISTS idx_events_location ON Events(LocationX, LocationY) WHERE LocationX IS NOT NULL;";
    public const string CreateIndexCivId    = "CREATE INDEX IF NOT EXISTS idx_events_civid    ON Events(CivId) WHERE CivId IS NOT NULL;";
    public const string CreateIndexActorId  = "CREATE INDEX IF NOT EXISTS idx_events_actorid  ON Events(ActorId) WHERE ActorId IS NOT NULL;";
    public const string CreateIndexEventEntities = "CREATE INDEX IF NOT EXISTS idx_event_entities_entity ON EventEntities(EntityId);";
}
