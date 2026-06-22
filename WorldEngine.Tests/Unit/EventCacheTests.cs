using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class EventCacheTests
{
    private static SimEvent Make(EventType type, int year) => new()
    {
        Id = new EventId(year),
        Type = type,
        Year = year,
        Season = Season.Spring,
        Tick = year,
        TierInvolvement = EventTier.Regional,
        VerbClass = VerbClass.Destruction,
        PopulationImpact = PopulationImpact.Minor,
        IsFirstOfKind = false,
        IsGodMode = false,
        PayloadJson = "{}"
    };

    [Fact]
    public void EventCache_OldestDroppedWhenFull()
    {
        var cache = new EventCache(500);
        for (int i = 1; i <= 501; i++)
            cache.Add(Make(EventType.WildfireOccurred, i));

        var all = cache.GetRecent(1000);
        all.Should().HaveCount(500);
        all.Should().NotContain(e => e.Year == 1, "the oldest (501st-from-newest) event is evicted");
        all.First().Year.Should().Be(2);
        all.Last().Year.Should().Be(501);
    }

    [Fact]
    public void EventCache_ContainsTypeAfterAdd()
    {
        var cache = new EventCache(10);
        cache.Add(Make(EventType.VolcanicEruption, 1));
        cache.ContainsType(EventType.VolcanicEruption).Should().BeTrue();
    }

    [Fact]
    public void EventCache_ContainsTypeFalseBeforeAdd()
    {
        var cache = new EventCache(10);
        cache.ContainsType(EventType.FloodOccurred).Should().BeFalse();
    }

    [Fact]
    public void EventCache_GetRecentReturnsLatestN()
    {
        var cache = new EventCache(10);
        for (int i = 1; i <= 5; i++)
            cache.Add(Make(EventType.WildfireOccurred, i));

        var recent = cache.GetRecent(3);
        recent.Select(e => e.Year).Should().Equal(3, 4, 5);
    }
}
