using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// Post-sim pass that scans the event log and populates pre-aggregated summary tables:
/// CharacterSummaries, CivSummaries, SuccessionChain, Dynasties, and Eras.
/// Call via <see cref="EventStore.BuildSummaries"/> after the simulation ends.
/// </summary>
public static class SummaryBuilder
{
    // ── EventType integer constants (mirrors Core.EventType enum without adding a dependency) ──
    private const int CharacterBorn         = 3001;
    private const int CharacterDied         = 3002;
    private const int WarDeclared           = 3103;
    private const int WarEnded              = 3104;
    private const int BattleOccurred        = 3105;
    private const int ArtworkCreated        = 3108;
    private const int CivilizationFounded   = 3201;
    private const int CivilizationCollapsed = 3202;
    private const int SettlementFounded     = 3203;
    private const int SettlementDestroyed   = 3204;
    private const int SuccessionOccurred    = 3205;
    private const int SettlementAbandoned   = 3403;
    private const int DiseaseOutbreak       = 3404;

    // ── CharacterSummaries ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds CharacterSummaries from the event log. Replaces any existing rows.
    /// </summary>
    public static void BuildCharacterSummaries(SqliteConnection conn)
    {
        conn.Execute("DELETE FROM CharacterSummaries;");

        // Load all CharacterBorn events (one per character)
        var borns = conn.Query<EventFlatRow>(
            "SELECT Id, ActorId, ActorName, Year, CivId, PayloadJson FROM Events WHERE Type = @t",
            new { t = CharacterBorn }).ToList();

        if (borns.Count == 0) return;

        // Pre-load lookup tables to avoid N+1 per-character queries
        var deathByActorId = conn.Query<EventFlatRow>(
            "SELECT ActorId, Year, PayloadJson FROM Events WHERE Type = @t",
            new { t = CharacterDied })
            .Where(r => r.ActorId.HasValue)
            .ToDictionary(r => r.ActorId!.Value, r => r);

        var warCountByActorId = conn.Query<(long ActorId, int Count)>(
            "SELECT ActorId, COUNT(*) AS Count FROM Events WHERE Type = @t AND ActorId IS NOT NULL GROUP BY ActorId",
            new { t = WarDeclared })
            .ToDictionary(x => x.ActorId, x => x.Count);

        var settlCountByActorId = conn.Query<(long ActorId, int Count)>(
            "SELECT ActorId, COUNT(*) AS Count FROM Events WHERE Type = @t AND ActorId IS NOT NULL GROUP BY ActorId",
            new { t = SettlementFounded })
            .ToDictionary(x => x.ActorId, x => x.Count);

        var artCountByActorId = conn.Query<(long ActorId, int Count)>(
            "SELECT ActorId, COUNT(*) AS Count FROM Events WHERE Type = @t AND ActorId IS NOT NULL GROUP BY ActorId",
            new { t = ArtworkCreated })
            .ToDictionary(x => x.ActorId, x => x.Count);

        // Parse all SuccessionOccurred payloads to build: SuccessorId → (RulerOrdinal, CivId)
        var successionBySucessorId = new Dictionary<long, (int Ordinal, long CivId)>();
        foreach (var row in conn.Query<EventFlatRow>(
            "SELECT PayloadJson, CivId FROM Events WHERE Type = @t", new { t = SuccessionOccurred }))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("SuccessorId", out var sid) &&
                    root.TryGetProperty("SuccessorOrdinal", out var ord))
                {
                    long successorId = sid.GetInt64();
                    int ordinal      = ord.GetInt32();
                    long civId       = row.CivId ?? 0;
                    successionBySucessorId.TryAdd(successorId, (ordinal, civId));
                }
            }
            catch (JsonException) { /* malformed payload — skip */ }
        }

        // Build CivName lookup from CivilizationFounded payloads
        var civNameById = new Dictionary<long, string>();
        foreach (var row in conn.Query<EventFlatRow>(
            "SELECT CivId, PayloadJson FROM Events WHERE Type = @t", new { t = CivilizationFounded }))
        {
            if (row.CivId is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                if (doc.RootElement.TryGetProperty("CivName", out var cn))
                    civNameById.TryAdd(row.CivId.Value, cn.GetString() ?? "");
            }
            catch (JsonException) { /* skip */ }
        }

        // Top-5 significant event IDs per character (by tier desc)
        var sigByCharId = new Dictionary<long, List<long>>();
        foreach (var row in conn.Query<(long EntityId, long EventId, int Tier)>(
            """
            SELECT ee.EntityId, ee.EventId, e.TierInvolvement AS Tier
            FROM EventEntities ee
            JOIN Events e ON e.Id = ee.EventId
            ORDER BY ee.EntityId, e.TierInvolvement DESC, e.Id
            """))
        {
            if (!sigByCharId.TryGetValue(row.EntityId, out var list))
            {
                list = new List<long>(5);
                sigByCharId[row.EntityId] = list;
            }
            if (list.Count < 5) list.Add(row.EventId);
        }

        using var tx = conn.BeginTransaction();
        foreach (var born in borns)
        {
            if (born.ActorId is null) continue;
            long charId = born.ActorId.Value;

            // Epithet from payload
            string? epithet = null;
            try
            {
                using var doc = JsonDocument.Parse(born.PayloadJson ?? "{}");
                if (doc.RootElement.TryGetProperty("Epithet", out var ep))
                    epithet = ep.GetString();
            }
            catch (JsonException) { /* skip */ }

            // Death info
            int deathYear = 0;
            string? deathCause = null;
            int ageSeasons = 0;
            if (deathByActorId.TryGetValue(charId, out var death))
            {
                deathYear = death.Year;
                try
                {
                    using var doc = JsonDocument.Parse(death.PayloadJson ?? "{}");
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Cause", out var c))    deathCause = c.GetString();
                    if (root.TryGetProperty("AgeSeason", out var a)) ageSeasons = a.GetInt32();
                }
                catch (JsonException) { /* skip */ }
            }

            // Succession info
            int rulerOrdinal = 0;
            long civId = born.CivId ?? 0;
            if (successionBySucessorId.TryGetValue(charId, out var succInfo))
            {
                rulerOrdinal = succInfo.Ordinal;
                if (succInfo.CivId > 0) civId = succInfo.CivId;
            }

            string? civName = civId > 0 && civNameById.TryGetValue(civId, out var cn) ? cn : null;

            // Significant events
            var sigIds = sigByCharId.TryGetValue(charId, out var ids) ? ids : (IEnumerable<long>)Array.Empty<long>();
            string sigJson = "[" + string.Join(",", sigIds) + "]";

            conn.Execute("""
                INSERT OR REPLACE INTO CharacterSummaries
                    (CharacterId, Name, Epithet, NameOrdinal, AncestryId, CivId, CivName,
                     RulerOrdinal, BirthYear, DeathYear, DeathCause, AgeSeasons,
                     WarsInitiated, SettlementsFounded, ArtworksCreated, SignificantEvents)
                VALUES
                    (@CharacterId, @Name, @Epithet, @NameOrdinal, @AncestryId, @CivId, @CivName,
                     @RulerOrdinal, @BirthYear, @DeathYear, @DeathCause, @AgeSeasons,
                     @WarsInitiated, @SettlementsFounded, @ArtworksCreated, @SignificantEvents)
                """, new
            {
                CharacterId       = charId,
                Name              = born.ActorName ?? "Unknown",
                Epithet           = epithet,
                NameOrdinal       = 0,        // DECISION: NameOrdinal is not emitted in current payloads; defaults to 0
                AncestryId        = (string?)null, // DECISION: AncestryId not in event payloads; deferred to Phase 3.3
                CivId             = civId == 0 ? (long?)null : civId,
                CivName           = civName,
                RulerOrdinal      = rulerOrdinal,
                BirthYear         = born.Year,
                DeathYear         = deathYear == 0 ? (int?)null : deathYear,
                DeathCause        = deathCause,
                AgeSeasons        = ageSeasons,
                WarsInitiated     = warCountByActorId.GetValueOrDefault(charId, 0),
                SettlementsFounded = settlCountByActorId.GetValueOrDefault(charId, 0),
                ArtworksCreated   = artCountByActorId.GetValueOrDefault(charId, 0),
                SignificantEvents  = sigJson
            }, tx);
        }
        tx.Commit();
    }

    // ── CivSummaries ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds CivSummaries from the event log. Replaces any existing rows.
    /// </summary>
    public static void BuildCivSummaries(SqliteConnection conn)
    {
        conn.Execute("DELETE FROM CivSummaries;");

        // All distinct civs from CivilizationFounded events
        var founded = conn.Query<CivFoundedRow>(
            "SELECT CivId, Year, PayloadJson FROM Events WHERE Type = @t AND CivId IS NOT NULL",
            new { t = CivilizationFounded }).ToList();

        if (founded.Count == 0) return;

        // Collapse years
        var collapseByC = conn.Query<(long CivId, int Year)>(
            "SELECT CivId, Year FROM Events WHERE Type = @t AND CivId IS NOT NULL",
            new { t = CivilizationCollapsed })
            .ToDictionary(x => x.CivId, x => x.Year);

        // Settlement counts per civ (founded)
        var settlFounded = conn.Query<(long CivId, int Count)>(
            "SELECT CivId, COUNT(*) AS Count FROM Events WHERE Type = @t AND CivId IS NOT NULL GROUP BY CivId",
            new { t = SettlementFounded })
            .ToDictionary(x => x.CivId, x => x.Count);

        // Wars initiated (DeclarerCivId = CivId column on the event row)
        var warsInit = conn.Query<(long CivId, int Count)>(
            "SELECT CivId, COUNT(*) AS Count FROM Events WHERE Type = @t AND CivId IS NOT NULL GROUP BY CivId",
            new { t = WarDeclared })
            .ToDictionary(x => x.CivId, x => x.Count);

        // Wars suffered: parse WarDeclared payloads for TargetCivId
        var warsSuffered = new Dictionary<long, int>();
        foreach (var row in conn.Query<(long? CivId, string PayloadJson)>(
            "SELECT CivId, PayloadJson FROM Events WHERE Type = @t", new { t = WarDeclared }))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                if (doc.RootElement.TryGetProperty("TargetCivId", out var tcid))
                {
                    long targetCiv = tcid.GetInt64();
                    warsSuffered[targetCiv] = warsSuffered.GetValueOrDefault(targetCiv, 0) + 1;
                }
            }
            catch (JsonException) { /* skip */ }
        }

        // War durations: pair WarDeclared with WarEnded by WarNumber + civ pair
        var warDeclaredEvents = conn.Query<(long EventId, int Year, long? CivId, string PayloadJson)>(
            "SELECT Id, Year, CivId, PayloadJson FROM Events WHERE Type = @t", new { t = WarDeclared }).ToList();
        var warEndedEvents = conn.Query<(int Year, string PayloadJson)>(
            "SELECT Year, PayloadJson FROM Events WHERE Type = @t", new { t = WarEnded }).ToList();

        // Build WarNumber+civPair → ended year
        var warEndMap = new Dictionary<(long A, long B, int Num), int>();
        foreach (var we in warEndedEvents)
        {
            try
            {
                using var doc = JsonDocument.Parse(we.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("CivAId", out var a) &&
                    root.TryGetProperty("CivBId", out var b) &&
                    root.TryGetProperty("WarNumber", out var wn))
                {
                    long civA = a.GetInt64(), civB = b.GetInt64();
                    int  num  = wn.GetInt32();
                    var  key  = civA < civB ? (civA, civB, num) : (civB, civA, num);
                    warEndMap.TryAdd(key, we.Year);
                }
            }
            catch (JsonException) { /* skip */ }
        }

        var yearAtWar = new Dictionary<long, int>();
        foreach (var wd in warDeclaredEvents)
        {
            if (wd.CivId is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(wd.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (!root.TryGetProperty("TargetCivId", out var tcid)) continue;
                if (!root.TryGetProperty("WarNumber", out var wn))    continue;
                long declarer = wd.CivId.Value;
                long target   = tcid.GetInt64();
                int  num      = wn.GetInt32();
                var  key      = declarer < target ? (declarer, target, num) : (target, declarer, num);
                int  endYear  = warEndMap.TryGetValue(key, out int ey) ? ey : wd.Year;
                int  duration = Math.Max(0, endYear - wd.Year);
                yearAtWar[declarer] = yearAtWar.GetValueOrDefault(declarer, 0) + duration;
                yearAtWar[target]   = yearAtWar.GetValueOrDefault(target, 0)   + duration;
            }
            catch (JsonException) { /* skip */ }
        }

        // Succession counts and last-ruler names per civ
        var ruleCount    = new Dictionary<long, int>();
        var lastRuler    = new Dictionary<long, string>();
        foreach (var row in conn.Query<(long? CivId, int Year, string PayloadJson)>(
            "SELECT CivId, Year, PayloadJson FROM Events WHERE Type = @t ORDER BY Year", new { t = SuccessionOccurred }))
        {
            if (row.CivId is null) continue;
            long civ = row.CivId.Value;
            ruleCount[civ] = ruleCount.GetValueOrDefault(civ, 0) + 1;
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                if (doc.RootElement.TryGetProperty("SuccessorName", out var sn))
                    lastRuler[civ] = sn.GetString() ?? "";
            }
            catch (JsonException) { /* skip */ }
        }

        using var tx = conn.BeginTransaction();
        foreach (var row in founded)
        {
            if (row.CivId is null) continue;
            long civId = row.CivId.Value;

            string  civName      = "";
            string? firstRuler   = null;
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("CivName",    out var cn)) civName    = cn.GetString() ?? "";
                if (root.TryGetProperty("FounderName", out var fn)) firstRuler = fn.GetString();
            }
            catch (JsonException) { /* skip */ }

            bool collapsed  = collapseByC.ContainsKey(civId);
            int  totalRulers = 1 + ruleCount.GetValueOrDefault(civId, 0); // founder + successors

            conn.Execute("""
                INSERT OR REPLACE INTO CivSummaries
                    (CivId, Name, FoundedYear, CollapseYear, IsCollapsed,
                     PeakSettlements, TotalRulers, TotalWarsInitiated, TotalWarsSuffered,
                     TotalYearsAtWar, DominantAncestry, CulturalTraits, FirstRulerName, LastRulerName)
                VALUES
                    (@CivId, @Name, @FoundedYear, @CollapseYear, @IsCollapsed,
                     @PeakSettlements, @TotalRulers, @TotalWarsInitiated, @TotalWarsSuffered,
                     @TotalYearsAtWar, @DominantAncestry, @CulturalTraits, @FirstRulerName, @LastRulerName)
                """, new
            {
                CivId              = civId,
                Name               = civName,
                FoundedYear        = row.Year,
                CollapseYear       = collapseByC.TryGetValue(civId, out int cy) ? cy : (int?)null,
                IsCollapsed        = collapsed ? 1 : 0,
                PeakSettlements    = settlFounded.GetValueOrDefault(civId, 0), // DECISION: approximation; tracks founded, not net-alive
                TotalRulers        = totalRulers,
                TotalWarsInitiated = warsInit.GetValueOrDefault(civId, 0),
                TotalWarsSuffered  = warsSuffered.GetValueOrDefault(civId, 0),
                TotalYearsAtWar    = yearAtWar.GetValueOrDefault(civId, 0),
                DominantAncestry   = (string?)null, // DECISION: ancestry tracking deferred to Phase 3.3
                CulturalTraits     = "[]",
                FirstRulerName     = firstRuler,
                LastRulerName      = lastRuler.TryGetValue(civId, out string? lr) ? lr : firstRuler
            }, tx);
        }
        tx.Commit();
    }

    // ── SuccessionChain ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the SuccessionChain table from SuccessionOccurred and CharacterBorn events.
    /// </summary>
    public static void BuildSuccessionChain(SqliteConnection conn)
    {
        conn.Execute("DELETE FROM SuccessionChain;");

        // Birth years for BirthYear column
        var birthByCharId = conn.Query<(long CharId, int Year)>(
            "SELECT ActorId AS CharId, Year FROM Events WHERE Type = @t AND ActorId IS NOT NULL",
            new { t = CharacterBorn })
            .ToDictionary(x => x.CharId, x => x.Year);

        // Collect all succession events ordered by year
        var succRows = conn.Query<(long? CivId, int Year, string PayloadJson)>(
            "SELECT CivId, Year, PayloadJson FROM Events WHERE Type = @t ORDER BY Year",
            new { t = SuccessionOccurred }).ToList();

        // Track per-civ: (ordinal → charId, name, tookThrone)
        // Also track predecessor's LostThroneYear and LostThroneReason from the event
        var chainByCiv = new Dictionary<long, List<ChainEntry>>();

        foreach (var row in succRows)
        {
            if (row.CivId is null) continue;
            long civ = row.CivId.Value;

            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                var root = doc.RootElement;

                long predId  = root.TryGetProperty("PredecessorId",      out var pi)  ? pi.GetInt64()  : 0;
                string predN = root.TryGetProperty("PredecessorName",     out var pn)  ? pn.GetString() ?? "" : "";
                int predOrd  = root.TryGetProperty("PredecessorOrdinal",  out var po)  ? po.GetInt32()  : 0;
                long succId  = root.TryGetProperty("SuccessorId",         out var si)  ? si.GetInt64()  : 0;
                string succN = root.TryGetProperty("SuccessorName",       out var sn)  ? sn.GetString() ?? "" : "";
                int succOrd  = root.TryGetProperty("SuccessorOrdinal",    out var so)  ? so.GetInt32()  : 0;

                if (!chainByCiv.TryGetValue(civ, out var chain))
                {
                    chain = new List<ChainEntry>();
                    chainByCiv[civ] = chain;
                }

                // Update predecessor's LostThroneYear in existing chain
                var existing = chain.Find(e => e.CharId == predId && e.Ordinal == predOrd);
                if (existing is not null)
                {
                    existing.LostThroneYear   = row.Year;
                    existing.LostThroneReason = "succession";
                }
                else if (predId != 0)
                {
                    // Predecessor not yet in chain — this is the first succession for this civ
                    chain.Add(new ChainEntry
                    {
                        Ordinal          = predOrd,
                        CharId           = predId,
                        Name             = predN,
                        BirthYear        = birthByCharId.GetValueOrDefault(predId, 0),
                        TookThroneYear   = 0,  // founder's throne year = civ founding year; filled below
                        LostThroneYear   = row.Year,
                        LostThroneReason = "succession"
                    });
                }

                // Add successor
                if (chain.Find(e => e.CharId == succId) is null && succId != 0)
                {
                    chain.Add(new ChainEntry
                    {
                        Ordinal        = succOrd,
                        CharId         = succId,
                        Name           = succN,
                        BirthYear      = birthByCharId.GetValueOrDefault(succId, 0),
                        TookThroneYear = row.Year
                    });
                }
            }
            catch (JsonException) { /* skip malformed payload */ }
        }

        // Fill LostThroneReason for deaths
        var deathByCharId = conn.Query<(long CharId, int Year, string PayloadJson)>(
            "SELECT ActorId AS CharId, Year, PayloadJson FROM Events WHERE Type = @t AND ActorId IS NOT NULL",
            new { t = CharacterDied })
            .ToDictionary(x => x.CharId, x => x);

        foreach (var chain in chainByCiv.Values)
        {
            foreach (var entry in chain)
            {
                if (entry.LostThroneReason is null && deathByCharId.TryGetValue(entry.CharId, out var d))
                {
                    entry.LostThroneYear = d.Year;
                    try
                    {
                        using var doc = JsonDocument.Parse(d.PayloadJson ?? "{}");
                        if (doc.RootElement.TryGetProperty("Cause", out var c))
                            entry.LostThroneReason = c.GetString();
                    }
                    catch (JsonException) { /* skip */ }
                    entry.LostThroneReason ??= "death";
                }
            }
        }

        using var tx = conn.BeginTransaction();
        foreach (var (civId, chain) in chainByCiv)
        {
            foreach (var entry in chain)
            {
                conn.Execute("""
                    INSERT OR REPLACE INTO SuccessionChain
                        (CivId, Ordinal, CharId, Name, BirthYear, TookThroneYear, LostThroneYear, LostThroneReason)
                    VALUES
                        (@CivId, @Ordinal, @CharId, @Name, @BirthYear, @TookThroneYear, @LostThroneYear, @LostThroneReason)
                    """, new
                {
                    CivId            = civId,
                    entry.Ordinal,
                    entry.CharId,
                    entry.Name,
                    entry.BirthYear,
                    entry.TookThroneYear,
                    LostThroneYear   = entry.LostThroneYear == 0 ? (int?)null : entry.LostThroneYear,
                    entry.LostThroneReason
                }, tx);
            }
        }
        tx.Commit();
    }

    // ── Dynasties ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups consecutive SuccessionChain rows by AncestryId into Dynasties.
    /// Must be called after <see cref="BuildSuccessionChain"/> and <see cref="BuildCharacterSummaries"/>.
    /// </summary>
    public static void BuildDynasties(SqliteConnection conn)
    {
        conn.Execute("DELETE FROM Dynasties;");

        // AncestryId per character from CharacterSummaries (may be null)
        var ancestryByChar = conn.Query<(long CharId, string? AncestryId)>(
            "SELECT CharacterId, AncestryId FROM CharacterSummaries")
            .ToDictionary(x => x.CharId, x => x.AncestryId);

        // Succession chains per civ
        var chains = conn.Query<(long CivId, int Ordinal, long CharId, string? Name)>(
            "SELECT CivId, Ordinal, CharId, Name FROM SuccessionChain ORDER BY CivId, Ordinal").ToList();

        var byCiv = chains.GroupBy(x => x.CivId);

        using var tx = conn.BeginTransaction();
        foreach (var civGroup in byCiv)
        {
            long civId    = civGroup.Key;
            var  ordered  = civGroup.OrderBy(x => x.Ordinal).ToList();

            // Consecutive rulers with the same non-null AncestryId form a dynasty
            string? curAncestry  = null;
            int     dynStart     = -1;
            int     dynStartOrd  = 0;
            int     dynastyCount = 0;

            void FlushDynasty(int endOrd)
            {
                if (curAncestry is null || dynStart < 0) return;
                dynastyCount++;
                string dynName = $"{curAncestry} Line";
                conn.Execute("""
                    INSERT INTO Dynasties (CivId, Name, StartOrdinal, EndOrdinal, AncestryId)
                    VALUES (@CivId, @Name, @StartOrdinal, @EndOrdinal, @AncestryId)
                    """, new
                {
                    CivId        = civId,
                    Name         = dynName,
                    StartOrdinal = dynStartOrd,
                    EndOrdinal   = endOrd,
                    AncestryId   = curAncestry
                }, tx);
            }

            foreach (var entry in ordered)
            {
                string? ancestry = ancestryByChar.TryGetValue(entry.CharId, out var a) ? a : null;

                if (ancestry != curAncestry)
                {
                    // End previous dynasty if it had ≥2 rulers (single-ruler "dynasties" not worth tracking)
                    if (curAncestry is not null && entry.Ordinal - dynStartOrd >= 2)
                        FlushDynasty(entry.Ordinal - 1);

                    curAncestry = ancestry;
                    dynStart    = ordered.IndexOf(entry);
                    dynStartOrd = entry.Ordinal;
                }
            }

            // Flush final dynasty
            if (ordered.Count > 0 && curAncestry is not null &&
                ordered[^1].Ordinal - dynStartOrd >= 2)
                FlushDynasty(ordered[^1].Ordinal);
        }
        tx.Commit();
    }

    // ── Eras ────────────────────────────────────────────────────────────────────────────────────

    private const int EraBucketSize = 50; // years per era window

    /// <summary>
    /// Assigns named eras to 50-year windows based on event density and dominant event types.
    /// </summary>
    public static void BuildEras(SqliteConnection conn)
    {
        conn.Execute("DELETE FROM Eras;");

        // Bucket events by 50-year window
        var events = conn.Query<(int Year, int Type)>(
            "SELECT Year, Type FROM Events WHERE Year > 0 ORDER BY Year").ToList();

        if (events.Count == 0) return;

        int minYear = events[0].Year;
        int maxYear = events[^1].Year;

        var buckets = new Dictionary<int, BucketStats>();
        foreach (var ev in events)
        {
            int bucket = (ev.Year - minYear) / EraBucketSize;
            if (!buckets.TryGetValue(bucket, out var stats))
            {
                stats = new BucketStats { StartYear = minYear + bucket * EraBucketSize };
                buckets[bucket] = stats;
            }
            stats.TotalEvents++;
            if (ev.Type == WarDeclared)      stats.Wars++;
            if (ev.Type == DiseaseOutbreak)  stats.Diseases++;
            if (ev.Type == SettlementFounded) stats.Settlements++;
        }

        if (buckets.Count == 0) return;

        double avgEvents = buckets.Values.Average(b => b.TotalEvents);

        using var tx = conn.BeginTransaction();
        int eraOrdinal = 1;

        foreach (var (bucket, stats) in buckets.OrderBy(x => x.Key))
        {
            int startYear = stats.StartYear;
            int endYear   = Math.Min(startYear + EraBucketSize - 1, maxYear);

            // Determine era name and type
            string eraType;
            string eraName;

            if (stats.Wars >= 3)
            {
                eraType = "war";
                eraName = eraOrdinal == 1 ? "the age of conflict" : $"the {Ordinal(eraOrdinal)} age of conflict";
            }
            else if (stats.Diseases >= 2)
            {
                eraType = "plague";
                eraName = eraOrdinal == 1 ? "the plague years" : $"the {Ordinal(eraOrdinal)} plague years";
            }
            else if (stats.Settlements >= 5)
            {
                eraType = "growth";
                eraName = eraOrdinal == 1 ? "the founding age" : $"the {Ordinal(eraOrdinal)} founding age";
            }
            else if (stats.TotalEvents < avgEvents / 2)
            {
                eraType = "silence";
                eraName = "the long silence";
            }
            else
            {
                eraType = "growth";
                eraName = $"the {Ordinal(eraOrdinal)} age";
            }

            conn.Execute("""
                INSERT INTO Eras (Name, StartYear, EndYear, EraType)
                VALUES (@Name, @StartYear, @EndYear, @EraType)
                """, new { Name = eraName, StartYear = startYear, EndYear = endYear, EraType = eraType }, tx);

            eraOrdinal++;
        }
        tx.Commit();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Ordinal(int n) => n switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth",
        6 => "sixth", 7 => "seventh", 8 => "eighth", 9 => "ninth", 10 => "tenth",
        _ => $"{n}th"
    };

    // Flat row used for multi-column event queries
    private sealed class EventFlatRow
    {
        public long? ActorId    { get; init; }
        public string? ActorName { get; init; }
        public int Year         { get; init; }
        public long? CivId      { get; init; }
        public string PayloadJson { get; init; } = "{}";
    }

    private sealed class CivFoundedRow
    {
        public long? CivId      { get; init; }
        public int Year         { get; init; }
        public string PayloadJson { get; init; } = "{}";
    }

    private sealed class ChainEntry
    {
        public int    Ordinal          { get; set; }
        public long   CharId           { get; set; }
        public string Name             { get; set; } = "";
        public int    BirthYear        { get; set; }
        public int    TookThroneYear   { get; set; }
        public int    LostThroneYear   { get; set; }
        public string? LostThroneReason { get; set; }
    }

    private sealed class BucketStats
    {
        public int StartYear    { get; set; }
        public int TotalEvents  { get; set; }
        public int Wars         { get; set; }
        public int Diseases     { get; set; }
        public int Settlements  { get; set; }
    }
}
