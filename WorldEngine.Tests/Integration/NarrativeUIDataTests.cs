using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.World;
using Xunit;
using FluentAssertions;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Integration tests for Phase 3.3 data-layer additions:
/// GetCausalChain, GetAllCivSummaries, GetEventCountByDecade, GetCharacterHistory ordering.
/// UI panels themselves are not unit-testable; these tests cover the Sim-layer API they rely on.
/// </summary>
public class NarrativeUIDataTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static SimEvent MakeEvent(
        EventType type,
        int year         = 1,
        long actorId     = 0,
        string? actorName = null,
        long civId       = 0,
        string? payloadJson = null,
        EventTier tier   = EventTier.Character,
        float significance = 0f) => new()
    {
        Id               = new EventId(0),
        Type             = type,
        TypeName         = type.ToString(),
        Domain           = "Test",
        Year             = year,
        Season           = Season.Spring,
        Tick             = 0,
        TierInvolvement  = tier,
        VerbClass        = VerbClass.Creation,
        PopulationImpact = PopulationImpact.None,
        IsFirstOfKind    = false,
        IsGodMode        = false,
        ActorId          = actorId,
        ActorName        = actorName,
        CivId            = civId,
        PayloadJson      = payloadJson ?? "{}",
        SignificanceScore = significance
    };

    private static string BornPayload(long charId, string name) =>
        JsonSerializer.Serialize(new { CharacterId = charId, CharacterName = name, Ambition = 0.5f, Aggression = 0.3f });

    private static string CivFoundedPayload(long civId, string civName, long founderId, string founderName) =>
        JsonSerializer.Serialize(new { CivId = civId, CivName = civName, FounderId = founderId, FounderName = founderName });

    // ── 3.3.1 GetCharacterHistory — ordered by year ────────────────────────────────────────────

    [Fact]
    public void HistoryQuery_GetCharacterHistory_OrderedByYear()
    {
        using var store = new EventStore(":memory:");

        long charId = 10;
        // Insert in reverse year order to confirm ordering is by year, not insertion order
        var inserted = store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterDied,        year: 200, actorId: charId),
            MakeEvent(EventType.WarDeclared,          year: 150, actorId: charId),
            MakeEvent(EventType.CharacterBorn,        year: 100, actorId: charId,
                payloadJson: BornPayload(charId, "Aldo")),
        });

        // Register the character in EventEntities so GetCharacterHistory can find them
        store.InsertEventEntities(inserted.Select(e => (e.Id.Value, charId)));

        var query  = store.GetHistoryQuery();
        var events = query.GetCharacterHistory(new EntityId(charId));

        events.Should().HaveCount(3);
        events[0].Year.Should().Be(100, because: "born event should come first");
        events[1].Year.Should().Be(150);
        events[2].Year.Should().Be(200, because: "death event should come last");
    }

    // ── 3.3.2 GetConflictHistory — includes battle count ──────────────────────────────────────

    [Fact]
    public void HistoryQuery_GetConflictHistory_IncludesBattleCount()
    {
        using var store = new EventStore(":memory:");

        long civA = 1, civB = 2;
        string warPayload = JsonSerializer.Serialize(new
        {
            DeclarerId = 99L, DeclarerName = "King", DeclarerCivId = civA, DeclarerCivName = "CivA",
            TargetCivId = civB, TargetCivName = "CivB", Cause = "border", CauseDescription = "border tension",
            WarNumber = 1
        });
        string warEndPayload = JsonSerializer.Serialize(new
        {
            CivAId = civA, CivAName = "CivA", CivBId = civB, CivBName = "CivB",
            Outcome = "civA_won", WarNumber = 1
        });

        store.BatchInsert(new[]
        {
            MakeEvent(EventType.WarDeclared, year: 100, civId: civA, payloadJson: warPayload),
            MakeEvent(EventType.BattleOccurred, year: 101, civId: civA),
            MakeEvent(EventType.BattleOccurred, year: 102, civId: civB),
            MakeEvent(EventType.BattleOccurred, year: 103, civId: civA),
            MakeEvent(EventType.WarEnded,    year: 105, civId: civA, payloadJson: warEndPayload),
        });

        var query     = store.GetHistoryQuery();
        var conflicts = query.GetConflictHistory(new CivId((int)civA), new CivId((int)civB));

        conflicts.Should().HaveCount(1);
        conflicts[0].DeclaredYear.Should().Be(100);
        conflicts[0].BattleCount.Should().Be(3, because: "three battle events occurred between year 100 and 105");
        conflicts[0].Outcome.Should().Be("civA_won");
    }

    // ── 3.3.3 GetSignificantEvents — filters by tier ──────────────────────────────────────────

    [Fact]
    public void HistoryQuery_GetSignificantEvents_FiltersCorrectly()
    {
        using var store = new EventStore(":memory:");

        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn,      year: 10, tier: EventTier.Background),
            MakeEvent(EventType.SettlementFounded,  year: 20, tier: EventTier.Regional),
            MakeEvent(EventType.WarDeclared,        year: 30, tier: EventTier.Headline),
            MakeEvent(EventType.CivilizationFounded, year: 40, tier: EventTier.Headline),
        });

        var query = store.GetHistoryQuery();

        var headlineOnly = query.GetSignificantEvents(0, 100, EventTier.Headline);
        headlineOnly.Should().HaveCount(2, because: "only two Headline events were inserted");

        var regionalAndAbove = query.GetSignificantEvents(0, 100, EventTier.Regional);
        regionalAndAbove.Should().HaveCount(3, because: "Regional(2) + Headline(3) tiers qualify");

        var all = query.GetSignificantEvents(0, 100, EventTier.Background);
        all.Should().HaveCount(4);
    }

    // ── 3.3.4 GetCausalChain — returns upstream events up to maxDepth ─────────────────────────

    [Fact]
    public void HistoryQuery_GetCausalChain_ReturnsUpstreamEvents()
    {
        using var store = new EventStore(":memory:");

        // Insert three events: root → middle → effect
        var inserted = store.BatchInsert(new[]
        {
            MakeEvent(EventType.AllianceFormed, year: 10),   // [0] root cause
            MakeEvent(EventType.WarDeclared,    year: 20),   // [1] intermediate cause
            MakeEvent(EventType.BattleOccurred, year: 30),   // [2] effect we trace from
        });

        long rootId   = inserted[0].Id.Value;
        long middleId = inserted[1].Id.Value;
        long effectId = inserted[2].Id.Value;

        // Wire: root → middle → effect
        store.InsertCausalEdges(new[] { (rootId, middleId), (middleId, effectId) });

        var query = store.GetHistoryQuery();
        var chain = query.GetCausalChain(effectId, maxDepth: 3);

        chain.Should().HaveCount(2, because: "two upstream cause events exist");
        chain.Should().Contain(c => c.CauseEventId == middleId && c.CauseEvent.Year == 20);
        chain.Should().Contain(c => c.CauseEventId == rootId   && c.CauseEvent.Year == 10);
    }

    [Fact]
    public void HistoryQuery_GetCausalChain_RespectsMaxDepth()
    {
        using var store = new EventStore(":memory:");

        // Insert four events: A → B → C → D (effect)
        var inserted = store.BatchInsert(new[]
        {
            MakeEvent(EventType.AllianceFormed, year: 1),   // [0] A — depth 3 from D
            MakeEvent(EventType.RivalryFormed,  year: 2),   // [1] B — depth 2 from D
            MakeEvent(EventType.WarDeclared,    year: 3),   // [2] C — depth 1 from D
            MakeEvent(EventType.BattleOccurred, year: 4),   // [3] D — effect
        });

        long idA = inserted[0].Id.Value, idB = inserted[1].Id.Value,
             idC = inserted[2].Id.Value, idD = inserted[3].Id.Value;

        store.InsertCausalEdges(new[] { (idA, idB), (idB, idC), (idC, idD) });

        var query = store.GetHistoryQuery();

        // maxDepth = 2 should return B and C but NOT A (depth 3 from D)
        var chain2 = query.GetCausalChain(idD, maxDepth: 2);
        chain2.Should().HaveCount(2);
        chain2.Should().Contain(c => c.CauseEventId == idC);
        chain2.Should().Contain(c => c.CauseEventId == idB);
        chain2.Should().NotContain(c => c.CauseEventId == idA, because: "A is beyond maxDepth=2");
    }

    // ── 3.3.5 GetAllCivSummaries — returns all civs ──────────────────────────────────────────

    [Fact]
    public void HistoryQuery_GetAllCivSummaries_ReturnsAll()
    {
        using var store = new EventStore(":memory:");

        long civA = 1, civB = 2;
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CivilizationFounded, year: 10, civId: civA,
                payloadJson: CivFoundedPayload(civA, "Stonekeep", 1, "Founder A")),
            MakeEvent(EventType.CivilizationFounded, year: 20, civId: civB,
                payloadJson: CivFoundedPayload(civB, "Rivermarch", 2, "Founder B")),
        });

        store.BuildSummaries();

        var query = store.GetHistoryQuery();
        var civs  = query.GetAllCivSummaries();

        civs.Should().HaveCount(2);
        civs.Should().Contain(c => c.Name == "Stonekeep"  && c.FoundedYear == 10);
        civs.Should().Contain(c => c.Name == "Rivermarch" && c.FoundedYear == 20);
        // Should be ordered by founding year
        civs[0].Name.Should().Be("Stonekeep");
        civs[1].Name.Should().Be("Rivermarch");
    }

    // ── 3.3.6 GetEventCountByDecade — groups correctly ────────────────────────────────────────

    [Fact]
    public void HistoryQuery_GetEventCountByDecade_GroupsCorrectly()
    {
        using var store = new EventStore(":memory:");

        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CharacterBorn, year: 5),    // decade 0
            MakeEvent(EventType.CharacterBorn, year: 8),    // decade 0
            MakeEvent(EventType.CharacterBorn, year: 15),   // decade 10
            MakeEvent(EventType.CharacterBorn, year: 22),   // decade 20
            MakeEvent(EventType.CharacterBorn, year: 29),   // decade 20
            MakeEvent(EventType.CharacterBorn, year: 30),   // decade 30
        });

        var query  = store.GetHistoryQuery();
        var counts = query.GetEventCountByDecade(0, 50);

        counts.Should().ContainKey(0).WhoseValue.Should().Be(2);
        counts.Should().ContainKey(10).WhoseValue.Should().Be(1);
        counts.Should().ContainKey(20).WhoseValue.Should().Be(2);
        counts.Should().ContainKey(30).WhoseValue.Should().Be(1);
    }
}
