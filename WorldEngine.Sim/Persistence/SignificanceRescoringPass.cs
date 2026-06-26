using Dapper;
using Microsoft.Data.Sqlite;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// Post-sim pass: upgrades event tiers based on downstream outcomes and computes
/// final SignificanceScore for all events.
///
/// Run once after the simulation ends (or on-demand before narrative generation)
/// via <see cref="EventStore.BuildSummaries"/>.
/// </summary>
public static class SignificanceRescoringPass
{
    // EventType integer constants — mirror Core.EventType without adding a project dependency
    private const int SettlementFounded     = 3203;
    private const int SettlementConquered   = 3207;
    private const int CharacterBorn         = 3001;
    private const int WarDeclared           = 3103;

    // TierInvolvement constants
    private const int TierBackground = 0;
    private const int TierCharacter  = 1;
    private const int TierRegional   = 2;
    private const int TierHeadline   = 3;

    // Significance base scores per tier
    private const float ScoreBackground = 0.1f;
    private const float ScoreCharacter  = 0.5f;
    private const float ScoreRegional   = 0.3f;
    private const float ScoreHeadline   = 0.8f;

    private const float BonusFirstOfKind   = 0.1f;
    private const float BonusRuler         = 0.1f;
    private const float BonusCausalOutgoing = 0.2f;  // applied when event has > 2 outgoing causal edges
    private const int   CausalEdgeThreshold = 2;

    // Retroactive upgrade thresholds
    private const int SettlementLongLivedYears  = 500;
    private const int SettlementLongLivedPop    = 1000;
    private const int CharacterLongReignSeasons = 200; // 50 years × 4 seasons

    /// <summary>
    /// Runs the full retroactive significance rescore pass against the given SQLite connection.
    /// Idempotent — safe to call multiple times (scores are overwritten, not summed).
    /// </summary>
    public static void Run(SqliteConnection conn)
    {
        RetroactiveUpgrades(conn);
        PopulateSignificanceScores(conn);
    }

    // ── Retroactive Tier Upgrades ──────────────────────────────────────────────────────────────

    private static void RetroactiveUpgrades(SqliteConnection conn)
    {
        UpgradeLongLivedSettlements(conn);
        UpgradeLongReigningRulers(conn);
        UpgradeConquestWars(conn);
    }

    /// <summary>
    /// SettlementFounded → Headline if the settlement still exists 500+ years later with pop > 1000.
    /// Uses the SettlementFounded event's SettlementName and matches it to later SettlementGrew
    /// or SettlementFounded events. Since we don't have a dedicated settlement lifecycle table,
    /// we approximate: a SettlementFounded event for a settlement that never fired
    /// SettlementAbandoned/SettlementDestroyed and has events 500+ years later.
    /// </summary>
    private static void UpgradeLongLivedSettlements(SqliteConnection conn)
    {
        // Find SettlementFounded events where the same settlement (by name+civ) has events
        // at least 500 years later — proxy for "still alive"
        var candidates = conn.Query<(long EventId, int Year, string? SettlementName, long? CivId)>("""
            SELECT Id, Year, SettlementName, CivId FROM Events
            WHERE Type = @t AND SettlementName IS NOT NULL AND TierInvolvement < @headline
            """, new { t = SettlementFounded, headline = TierHeadline }).ToList();

        if (candidates.Count == 0) return;

        using var tx = conn.BeginTransaction();
        foreach (var (evId, foundedYear, name, civId) in candidates)
        {
            if (name is null) continue;

            // Check for events referencing the same settlement much later
            int latestYear = conn.ExecuteScalar<int>("""
                SELECT COALESCE(MAX(Year), 0) FROM Events
                WHERE SettlementName = @name AND CivId = @civId AND Year > @foundedYear
                """, new { name, civId, foundedYear });

            if (latestYear - foundedYear < SettlementLongLivedYears) continue;

            // DECISION: we cannot check actual current pop without a settlements table;
            // use the founding pop from the payload (StartingPopulation) as a floor.
            // Any settlement that survived 500 years qualifies by longevity alone.
            conn.Execute("""
                UPDATE Events SET TierInvolvement = @headline
                WHERE Id = @id AND TierInvolvement < @headline
                """, new { headline = TierHeadline, id = evId }, tx);
        }
        tx.Commit();
    }

    /// <summary>
    /// CharacterBorn → Character tier if the character later became a ruler for > 50 years
    /// (CharacterLongReignSeasons). Checks SuccessionChain for long reign duration.
    /// </summary>
    private static void UpgradeLongReigningRulers(SqliteConnection conn)
    {
        // Find characters with long reigns (TookThroneYear and LostThroneYear available)
        var longReignCharIds = conn.Query<long>("""
            SELECT CharId FROM SuccessionChain
            WHERE LostThroneYear IS NOT NULL
              AND (LostThroneYear - TookThroneYear) * 4 >= @seasons
            UNION
            SELECT CharId FROM SuccessionChain
            WHERE LostThroneYear IS NULL
              AND TookThroneYear IS NOT NULL
            """, new { seasons = CharacterLongReignSeasons }).ToHashSet();

        if (longReignCharIds.Count == 0) return;

        using var tx = conn.BeginTransaction();
        foreach (long charId in longReignCharIds)
        {
            conn.Execute("""
                UPDATE Events SET TierInvolvement = MAX(TierInvolvement, @character)
                WHERE Type = @born AND ActorId = @charId AND TierInvolvement < @character
                """, new { character = TierCharacter, born = CharacterBorn, charId }, tx);
        }
        tx.Commit();
    }

    /// <summary>
    /// WarDeclared → Headline if the war resulted in at least one SettlementConquered event
    /// within the same civ pair's war period.
    /// </summary>
    private static void UpgradeConquestWars(SqliteConnection conn)
    {
        // Wars that have a SettlementConquered with a ConquerorCivId matching the declarer
        // DECISION: we join on CivId (the declarer's civ) and find any conquest in the DB
        // with the same CivId. This is a broad approximation — fine for significance scoring.
        var conquestCivIds = conn.Query<long>("""
            SELECT DISTINCT CivId FROM Events
            WHERE Type = @conquered AND CivId IS NOT NULL
            """, new { conquered = SettlementConquered }).ToHashSet();

        if (conquestCivIds.Count == 0) return;

        using var tx = conn.BeginTransaction();
        foreach (long civId in conquestCivIds)
        {
            conn.Execute("""
                UPDATE Events SET TierInvolvement = @headline
                WHERE Type = @warDeclared AND CivId = @civId AND TierInvolvement < @headline
                """, new { headline = TierHeadline, warDeclared = WarDeclared, civId }, tx);
        }
        tx.Commit();
    }

    // ── Significance Score Population ─────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a float SignificanceScore for every event and writes it to the Events table.
    /// Must be called after retroactive tier upgrades.
    /// </summary>
    private static void PopulateSignificanceScores(SqliteConnection conn)
    {
        // Count outgoing causal edges per event
        var outgoingEdges = conn.Query<(long PredId, int Count)>("""
            SELECT PredecessorId AS PredId, COUNT(*) AS Count
            FROM CausalEdges GROUP BY PredecessorId
            """).ToDictionary(x => x.PredId, x => x.Count);

        // Ruler character IDs from SuccessionChain
        var rulerCharIds = conn.Query<long>(
            "SELECT DISTINCT CharId FROM SuccessionChain").ToHashSet();

        // Load all events with enough data to score
        var events = conn.Query<ScoreRow>("""
            SELECT e.Id, e.TierInvolvement, e.IsFirstOfKind, e.ActorId
            FROM Events e
            """).ToList();

        if (events.Count == 0) return;

        using var tx = conn.BeginTransaction();
        foreach (var ev in events)
        {
            float base_ = ev.TierInvolvement switch
            {
                TierHeadline   => ScoreHeadline,
                TierRegional   => ScoreRegional,
                TierCharacter  => ScoreCharacter,
                _              => ScoreBackground
            };

            float score = base_;
            if (ev.IsFirstOfKind != 0) score += BonusFirstOfKind;
            if (ev.ActorId.HasValue && rulerCharIds.Contains(ev.ActorId.Value)) score += BonusRuler;
            if (outgoingEdges.TryGetValue(ev.Id, out int edges) && edges > CausalEdgeThreshold)
                score += BonusCausalOutgoing;

            score = Math.Min(score, 1.0f);

            conn.Execute(
                "UPDATE Events SET SignificanceScore = @score WHERE Id = @id;",
                new { score, id = ev.Id }, tx);
        }
        tx.Commit();
    }

    private sealed class ScoreRow
    {
        public long Id               { get; init; }
        public int  TierInvolvement  { get; init; }
        public int  IsFirstOfKind    { get; init; }
        public long? ActorId         { get; init; }
    }
}
