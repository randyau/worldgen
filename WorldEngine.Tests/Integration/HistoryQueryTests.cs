using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.World;
using Xunit;
using FluentAssertions;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Integration tests for Phase 3.1: SummaryBuilder, SuccessionChain, and HistoryQueryService.
/// Uses in-memory SQLite databases and EventStore.GetHistoryQuery() to share the connection.
/// </summary>
public class HistoryQueryTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static SimEvent MakeEvent(
        EventType type,
        int year = 1,
        long actorId = 0,
        string? actorName = null,
        long civId = 0,
        string? payloadJson = null,
        EventTier tier = EventTier.Character,
        long tick = 0) => new()
    {
        Id               = new EventId(0),
        Type             = type,
        TypeName         = type.ToString(),
        Domain           = "Test",
        Year             = year,
        Season           = Season.Spring,
        Tick             = tick,
        TierInvolvement  = tier,
        VerbClass        = VerbClass.Creation,
        PopulationImpact = PopulationImpact.None,
        IsFirstOfKind    = false,
        IsGodMode        = false,
        ActorId          = actorId,
        ActorName        = actorName,
        CivId            = civId,
        PayloadJson      = payloadJson ?? "{}"
    };

    private static string BornPayload(long charId, string name, string? epithet = null) =>
        JsonSerializer.Serialize(new
        {
            CharacterId   = charId,
            CharacterName = name,
            Epithet       = epithet,
            Ambition      = 0.5f,
            Aggression    = 0.3f
        });

    private static string DeathPayload(long charId, string name, string cause, int ageSeason) =>
        JsonSerializer.Serialize(new
        {
            CharacterId   = charId,
            CharacterName = name,
            Cause         = cause,
            AgeSeason     = ageSeason
        });

    private static string SuccessionPayload(
        long predId, string predName, int predOrd,
        long succId, string succName, int succOrd) =>
        JsonSerializer.Serialize(new
        {
            PredecessorId       = predId,
            PredecessorName     = predName,
            PredecessorOrdinal  = predOrd,
            SuccessorId         = succId,
            SuccessorName       = succName,
            SuccessorOrdinal    = succOrd
        });

    private static string CivFoundedPayload(long civId, string civName, long founderId, string founderName) =>
        JsonSerializer.Serialize(new
        {
            CivId       = civId,
            CivName     = civName,
            FounderId   = founderId,
            FounderName = founderName
        });

    // ── 3.1.1 CharacterSummaries ────────────────────────────────────────────────────────────────

    [Fact]
    public void SummaryBuilder_PopulatesCharacterSummaries()
    {
        using var store = new EventStore(":memory:");

        long charId = 42;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 100, actorId: charId, actorName: "Aldric",
                payloadJson: BornPayload(charId, "Aldric"))
        });

        store.BuildSummaries();

        var query   = store.GetHistoryQuery();
        var summary = query.GetCharacterSummary(new EntityId(charId));

        summary.Should().NotBeNull();
        summary!.Name.Should().Be("Aldric");
        summary.BirthYear.Should().Be(100);
        summary.CharacterId.Should().Be(charId);
    }

    [Fact]
    public void SummaryBuilder_CharacterSummary_IncludesDeathInfo()
    {
        using var store = new EventStore(":memory:");

        long charId = 7;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 50, actorId: charId, actorName: "Vessa",
                payloadJson: BornPayload(charId, "Vessa")),
            MakeEvent(EventType.CharacterDied, year: 130, actorId: charId, actorName: "Vessa",
                payloadJson: DeathPayload(charId, "Vessa", "old_age", 80 * 4))
        });

        store.BuildSummaries();

        var summary = store.GetHistoryQuery().GetCharacterSummary(new EntityId(charId));

        summary.Should().NotBeNull();
        summary!.DeathYear.Should().Be(130);
        summary.DeathCause.Should().Be("old_age");
        summary.AgeSeasons.Should().Be(320);
    }

    [Fact]
    public void SummaryBuilder_CharacterSummary_CountsWarsArtworksSettlements()
    {
        using var store = new EventStore(":memory:");

        long charId = 99;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 1, actorId: charId, actorName: "Kira",
                payloadJson: BornPayload(charId, "Kira")),
            MakeEvent(EventType.WarDeclared,        year: 10, actorId: charId),
            MakeEvent(EventType.WarDeclared,        year: 20, actorId: charId),
            MakeEvent(EventType.SettlementFounded,  year: 15, actorId: charId),
            MakeEvent(EventType.ArtworkCreated,     year: 12, actorId: charId),
            MakeEvent(EventType.ArtworkCreated,     year: 18, actorId: charId),
        });

        store.BuildSummaries();

        var summary = store.GetHistoryQuery().GetCharacterSummary(new EntityId(charId));

        summary.Should().NotBeNull();
        summary!.WarsInitiated.Should().Be(2);
        summary.SettlementsFounded.Should().Be(1);
        summary.ArtworksCreated.Should().Be(2);
    }

    // ── 3.1.1 CivSummaries ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void SummaryBuilder_PopulatesCivSummaries()
    {
        using var store = new EventStore(":memory:");

        long civId = 1, founderId = 10;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CivilizationFounded, year: 1, civId: civId,
                payloadJson: CivFoundedPayload(civId, "Arlen", founderId, "Aran"))
        });

        store.BuildSummaries();

        var query   = store.GetHistoryQuery();
        var summary = query.GetCivSummary(new CivId((int)civId));

        summary.Should().NotBeNull();
        summary!.Name.Should().Be("Arlen");
        summary.FoundedYear.Should().Be(1);
        summary.FirstRulerName.Should().Be("Aran");
        summary.IsCollapsed.Should().BeFalse();
    }

    // ── 3.1.2 SuccessionChain ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SummaryBuilder_PopulatesSuccessionChain()
    {
        using var store = new EventStore(":memory:");

        long civId = 5, charA = 1, charB = 2, charC = 3;
        store.BatchInsert(new[]
        {
            // Births
            MakeEvent(EventType.CharacterBorn, year: 1,   actorId: charA, actorName: "Aed",
                payloadJson: BornPayload(charA, "Aed")),
            MakeEvent(EventType.CharacterBorn, year: 30,  actorId: charB, actorName: "Bren",
                payloadJson: BornPayload(charB, "Bren")),
            MakeEvent(EventType.CharacterBorn, year: 60,  actorId: charC, actorName: "Cael",
                payloadJson: BornPayload(charC, "Cael")),
            // Civ founded
            MakeEvent(EventType.CivilizationFounded, year: 1, civId: civId,
                payloadJson: CivFoundedPayload(civId, "Pella", charA, "Aed")),
            // Successions
            MakeEvent(EventType.SuccessionOccurred, year: 50, civId: civId,
                payloadJson: SuccessionPayload(charA, "Aed", 1, charB, "Bren", 2)),
            MakeEvent(EventType.SuccessionOccurred, year: 90, civId: civId,
                payloadJson: SuccessionPayload(charB, "Bren", 2, charC, "Cael", 3)),
        });

        store.BuildSummaries();

        var query  = store.GetHistoryQuery();
        var rulers = query.GetRulersOfCiv(new CivId((int)civId));

        // Two SuccessionOccurred events (A→B, B→C) produce 3 entries in the chain: A, B, C
        rulers.Should().HaveCount(3);
        rulers[0].Name.Should().Be("Aed");
        rulers[1].Name.Should().Be("Bren");
    }

    [Fact]
    public void SummaryBuilder_SuccessionChain_RulerAtYear()
    {
        using var store = new EventStore(":memory:");

        long civId = 3, charA = 1, charB = 2;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 1, actorId: charA, actorName: "First",
                payloadJson: BornPayload(charA, "First")),
            MakeEvent(EventType.CharacterBorn, year: 30, actorId: charB, actorName: "Second",
                payloadJson: BornPayload(charB, "Second")),
            MakeEvent(EventType.CivilizationFounded, year: 1, civId: civId,
                payloadJson: CivFoundedPayload(civId, "TestCiv", charA, "First")),
            MakeEvent(EventType.SuccessionOccurred, year: 50, civId: civId,
                payloadJson: SuccessionPayload(charA, "First", 1, charB, "Second", 2)),
        });

        store.BuildSummaries();
        var query = store.GetHistoryQuery();

        // Before succession: charA was predecessor, but since TookThroneYear is NULL for the founder
        // just test that GetRulerAtYear returns something after succession
        var rulerAt60 = query.GetRulerAtYear(new CivId((int)civId), 60);
        rulerAt60.Should().NotBeNull("a ruler should be found at year 60");
        rulerAt60!.Name.Should().Be("Second");
    }

    // ── 3.1.4 HistoryQueryService ──────────────────────────────────────────────────────────────

    [Fact]
    public void HistoryQueryService_GetCharacterHistory_ReturnsEvents()
    {
        using var store = new EventStore(":memory:");

        long charId = 55;
        var inserted = store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn,   year: 10, actorId: charId, actorName: "Ren",
                payloadJson: BornPayload(charId, "Ren")),
            MakeEvent(EventType.ArtworkCreated,  year: 15, actorId: charId),
            MakeEvent(EventType.WarDeclared,     year: 20),  // unrelated event
        });

        // Register first two events as involving charId
        store.InsertEventEntities(new[]
        {
            (inserted[0].Id.Value, charId),
            (inserted[1].Id.Value, charId),
        });

        store.BuildSummaries();

        var history = store.GetHistoryQuery().GetCharacterHistory(new EntityId(charId));

        history.Should().HaveCount(2);
        history.Select(e => e.Type).Should().Contain(EventType.CharacterBorn);
        history.Select(e => e.Type).Should().Contain(EventType.ArtworkCreated);
        history.Select(e => e.Type).Should().NotContain(EventType.WarDeclared);
    }

    [Fact]
    public void HistoryQueryService_FindCharactersByName_ReturnsDisambiguatedList()
    {
        using var store = new EventStore(":memory:");

        long char1 = 1, char2 = 2;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 100, actorId: char1, actorName: "Caelen",
                payloadJson: BornPayload(char1, "Caelen")),
            MakeEvent(EventType.CharacterBorn, year: 250, actorId: char2, actorName: "Caelen",
                payloadJson: BornPayload(char2, "Caelen")),
        });

        store.BuildSummaries();

        var results = store.GetHistoryQuery().FindCharactersByName("Caelen");

        results.Should().HaveCount(2);
        results[0].BirthYear.Should().BeLessThan(results[1].BirthYear, "ordered by birth year");
        results[0].CharacterId.Should().Be(char1);
        results[1].CharacterId.Should().Be(char2);
    }

    [Fact]
    public void HistoryQueryService_FindCharactersByName_CaseInsensitive()
    {
        using var store = new EventStore(":memory:");

        long charId = 3;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 1, actorId: charId, actorName: "Mira",
                payloadJson: BornPayload(charId, "Mira"))
        });

        store.BuildSummaries();

        store.GetHistoryQuery().FindCharactersByName("MIRA").Should().HaveCount(1);
        store.GetHistoryQuery().FindCharactersByName("mira").Should().HaveCount(1);
    }

    [Fact]
    public void HistoryQueryService_GetSignificantEvents_FiltersByTierAndYear()
    {
        using var store = new EventStore(":memory:");

        store.BatchInsert(new[]
        {
            MakeEvent(EventType.WarDeclared,        year: 10, tier: EventTier.Headline),
            MakeEvent(EventType.SettlementFounded,  year: 15, tier: EventTier.Regional),
            MakeEvent(EventType.CharacterBorn,      year: 20, tier: EventTier.Character),
            MakeEvent(EventType.WarDeclared,        year: 50, tier: EventTier.Headline),
        });

        store.BuildSummaries();
        var query = store.GetHistoryQuery();

        var headline = query.GetSignificantEvents(1, 100, EventTier.Headline);
        headline.Should().HaveCount(2);
        headline.All(e => e.TierInvolvement >= EventTier.Headline).Should().BeTrue();

        var regional = query.GetSignificantEvents(1, 25, EventTier.Regional);
        regional.Should().HaveCount(2, "Headline + Regional within years 1-25");
    }

    [Fact]
    public void HistoryQueryService_GetCivHistory_ReturnsOnlyThatCiv()
    {
        using var store = new EventStore(":memory:");

        long civA = 1, civB = 2;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.SettlementFounded, year: 10, civId: civA),
            MakeEvent(EventType.SettlementFounded, year: 15, civId: civB),
            MakeEvent(EventType.SettlementFounded, year: 20, civId: civA),
        });

        store.BuildSummaries();
        var civAHistory = store.GetHistoryQuery().GetCivHistory(new CivId((int)civA), 1, 100);

        civAHistory.Should().HaveCount(2);
        civAHistory.All(e => e.CivId == civA).Should().BeTrue();
    }
}
