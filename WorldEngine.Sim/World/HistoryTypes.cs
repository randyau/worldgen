namespace WorldEngine.Sim.World;

/// <summary>Pre-aggregated profile of a historical character, built by SummaryBuilder.</summary>
public sealed record CharacterSummary(
    long    CharacterId,
    string  Name,
    string? Epithet,
    int     NameOrdinal,
    string? AncestryId,
    long    CivId,
    string? CivName,
    int     RulerOrdinal,
    int     BirthYear,
    int     DeathYear,
    string? DeathCause,
    int     AgeSeasons,
    int     WarsInitiated,
    int     SettlementsFounded,
    int     ArtworksCreated,
    IReadOnlyList<long> SignificantEventIds
);

/// <summary>Pre-aggregated profile of a civilization, built by SummaryBuilder.</summary>
public sealed record CivSummary(
    long    CivId,
    string  Name,
    int     FoundedYear,
    int     CollapseYear,
    bool    IsCollapsed,
    int     PeakSettlements,
    int     TotalRulers,
    int     TotalWarsInitiated,
    int     TotalWarsSuffered,
    int     TotalYearsAtWar,
    string? DominantAncestry,
    IReadOnlyList<string> CulturalTraits,
    string? FirstRulerName,
    string? LastRulerName
);

/// <summary>Summary record for a single war between two civilizations.</summary>
public sealed record ConflictRecord(
    long    WarDeclarationEventId,
    long    CivAId,
    long    CivBId,
    int     DeclaredYear,
    int     EndedYear,
    string  Outcome,
    int     BattleCount,
    int     WarNumber
);
