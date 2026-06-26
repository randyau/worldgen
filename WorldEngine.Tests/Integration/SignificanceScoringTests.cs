using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.World;
using Xunit;
using FluentAssertions;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Integration tests for Phase 3.2.2: Significance Rescoring.
/// Tests SignificanceClassifier.ComputeSignificanceScore and SignificanceRescoringPass.
/// </summary>
public class SignificanceScoringTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static SimEvent MakeEvent(
        EventType type,
        EventTier tier = EventTier.Character,
        bool isFirstOfKind = false,
        long actorId = 0,
        string? actorName = null,
        long civId = 0,
        int year = 1,
        string? payloadJson = null,
        string? settlementName = null) => new()
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
        IsFirstOfKind    = isFirstOfKind,
        IsGodMode        = false,
        ActorId          = actorId,
        ActorName        = actorName,
        CivId            = civId,
        SettlementName   = settlementName,
        PayloadJson      = payloadJson ?? "{}"
    };

    // ── SignificanceClassifier.ComputeSignificanceScore ────────────────────────────────────────

    [Fact]
    public void ComputeSignificanceScore_Headline_ReturnsHighBase()
    {
        float score = SignificanceClassifier.ComputeSignificanceScore(
            EventTier.Headline, isFirstOfKind: false, isRuler: false);

        score.Should().BeGreaterThanOrEqualTo(0.8f);
    }

    [Fact]
    public void ComputeSignificanceScore_FirstOfKind_GetsBonus()
    {
        float withBonus    = SignificanceClassifier.ComputeSignificanceScore(
            EventTier.Character, isFirstOfKind: true,  isRuler: false);
        float withoutBonus = SignificanceClassifier.ComputeSignificanceScore(
            EventTier.Character, isFirstOfKind: false, isRuler: false);

        withBonus.Should().BeGreaterThan(withoutBonus);
        (withBonus - withoutBonus).Should().BeApproximately(0.1f, 0.001f);
    }

    [Fact]
    public void ComputeSignificanceScore_Ruler_GetsBonus()
    {
        float withRuler    = SignificanceClassifier.ComputeSignificanceScore(
            EventTier.Character, isFirstOfKind: false, isRuler: true);
        float withoutRuler = SignificanceClassifier.ComputeSignificanceScore(
            EventTier.Character, isFirstOfKind: false, isRuler: false);

        withRuler.Should().BeGreaterThan(withoutRuler);
    }

    [Fact]
    public void ComputeSignificanceScore_MaxCappedAtOne()
    {
        float score = SignificanceClassifier.ComputeSignificanceScore(
            EventTier.Headline, isFirstOfKind: true, isRuler: true);

        score.Should().BeLessThanOrEqualTo(1.0f);
    }

    [Fact]
    public void ComputeSignificanceScore_Background_HasLowestBase()
    {
        float bg  = SignificanceClassifier.ComputeSignificanceScore(EventTier.Background, false, false);
        float ch  = SignificanceClassifier.ComputeSignificanceScore(EventTier.Character,  false, false);
        float reg = SignificanceClassifier.ComputeSignificanceScore(EventTier.Regional,   false, false);
        float hl  = SignificanceClassifier.ComputeSignificanceScore(EventTier.Headline,   false, false);

        bg.Should().BeLessThan(ch);
        reg.Should().BeLessThan(hl);
    }

    // ── SignificanceRescoringPass ──────────────────────────────────────────────────────────────

    [Fact]
    public void RescoringPass_SetsSignificanceScoreGreaterThanZero_ForAllEvents()
    {
        using var store = new EventStore(":memory:");

        // Insert a mix of events
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.WarDeclared,       tier: EventTier.Headline),
            MakeEvent(EventType.SettlementFounded, tier: EventTier.Regional),
            MakeEvent(EventType.CharacterBorn,     tier: EventTier.Character),
        });

        // Run just the rescore pass (summary tables may be empty — that's OK)
        store.BuildSummaries();

        // All events should now have a SignificanceScore > 0
        var events = store.GetEventsByYearRange(0, 9999).ToList();
        events.Should().HaveCountGreaterThan(0);
        events.All(e => e.SignificanceScore > 0f).Should().BeTrue(
            "every event should receive a base significance score after the rescore pass");
    }

    [Fact]
    public void RetroactiveRescore_UpgradesSettlementFounded_WhenLongLived()
    {
        using var store = new EventStore(":memory:");

        // Insert a SettlementFounded event at year 1
        var founded = MakeEvent(
            EventType.SettlementFounded,
            tier: EventTier.Regional,
            civId: 1,
            year: 1,
            settlementName: "Ironhold");

        var written = store.BatchInsert(new[] { founded });
        long foundingId = written[0].Id.Value;

        // Insert many later events for the same settlement (to simulate it surviving 500+ years)
        var laterEvents = Enumerable.Range(502, 5).Select(y =>
            MakeEvent(EventType.SettlementGrew, tier: EventTier.Background,
                      civId: 1, year: y, settlementName: "Ironhold")).ToArray();
        store.BatchInsert(laterEvents);

        store.BuildSummaries();

        var upgraded = store.GetEvent(new EventId(foundingId));
        upgraded.Should().NotBeNull();
        upgraded!.TierInvolvement.Should().Be(EventTier.Headline,
            "SettlementFounded should be upgraded to Headline for a settlement with events 500+ years later");
    }

    [Fact]
    public void RescoringPass_HigherTierEvents_HaveHigherScores_ThanLowerTier()
    {
        using var store = new EventStore(":memory:");

        store.BatchInsert(new[]
        {
            MakeEvent(EventType.WarDeclared,       tier: EventTier.Headline, year: 1),
            MakeEvent(EventType.SettlementAbandoned, tier: EventTier.Regional, year: 2),
        });

        store.BuildSummaries();

        var all = store.GetEventsByYearRange(0, 9999).OrderBy(e => e.Year).ToList();
        all.Should().HaveCount(2);

        float headlineScore = all[0].SignificanceScore;
        float regionalScore = all[1].SignificanceScore;

        headlineScore.Should().BeGreaterThan(regionalScore,
            "Headline tier events should have higher significance than Regional tier");
    }

    [Fact]
    public void SignificanceScore_Persisted_AndReadBack()
    {
        using var store = new EventStore(":memory:");

        store.BatchInsert(new[]
        {
            MakeEvent(EventType.CivilizationFounded, tier: EventTier.Headline,
                      isFirstOfKind: true, year: 1),
        });
        store.BuildSummaries();

        var ev = store.GetEventsByYearRange(0, 9999).Single();
        ev.SignificanceScore.Should().BeGreaterThan(0f,
            "SignificanceScore should be persisted and read back from the Events table");
    }
}
