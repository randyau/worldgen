namespace WorldEngine.Sim.Persistence;

public static class DatabaseSchema
{
    public const string CreateEvents = """
        CREATE TABLE IF NOT EXISTS Events (
            Id               INTEGER PRIMARY KEY,
            Type             INTEGER NOT NULL,
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
            PayloadJson      TEXT    NOT NULL
        );
        """;

    public const string CreateIndexYear     = "CREATE INDEX IF NOT EXISTS idx_events_year ON Events(Year);";
    public const string CreateIndexType     = "CREATE INDEX IF NOT EXISTS idx_events_type ON Events(Type);";
    public const string CreateIndexTier     = "CREATE INDEX IF NOT EXISTS idx_events_tier ON Events(TierInvolvement);";
    public const string CreateIndexLocation = "CREATE INDEX IF NOT EXISTS idx_events_location ON Events(LocationX, LocationY) WHERE LocationX IS NOT NULL;";

    public const string CreateCausalEdges = """
        CREATE TABLE IF NOT EXISTS CausalEdges (
            PredecessorId INTEGER NOT NULL REFERENCES Events(Id),
            SuccessorId   INTEGER NOT NULL REFERENCES Events(Id),
            PRIMARY KEY (PredecessorId, SuccessorId)
        );
        """;

    public const string CreateEventEntities = """
        CREATE TABLE IF NOT EXISTS EventEntities (
            EventId    INTEGER NOT NULL REFERENCES Events(Id),
            EntityId   INTEGER NOT NULL,
            PRIMARY KEY (EventId, EntityId)
        );
        """;

    public const string CreateIndexEventEntities =
        "CREATE INDEX IF NOT EXISTS idx_event_entities_entity ON EventEntities(EntityId);";
}
