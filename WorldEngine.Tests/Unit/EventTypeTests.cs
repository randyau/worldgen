using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class EventTypeTests
{
    [Fact]
    public void EventType_AllValuesAreUnique()
    {
        var values = Enum.GetValues<EventType>().Select(v => (int)v).ToList();
        values.Should().OnlyHaveUniqueItems("each EventType must map to a distinct stable integer");
    }

    [Fact]
    public void EventType_AllM1ValuesInRange()
    {
        EventType[] m1 =
        {
            EventType.VolcanicEruption, EventType.EarthquakeOccurred, EventType.WildfireOccurred,
            EventType.FloodOccurred, EventType.DroughtBegan, EventType.DroughtEnded,
            EventType.SeaLevelChanged, EventType.BiomeChanged, EventType.ClimateShifted,
            EventType.ResourceRecovered
        };

        foreach (var t in m1)
            ((int)t).Should().BeInRange(1001, 1010, $"{t} is an M1 environmental event");
    }

    [Fact]
    public void VerbClassification_AllEventTypesHaveMapping()
    {
        foreach (var t in Enum.GetValues<EventType>())
        {
            var act = () => VerbClassification.Classify(t);
            act.Should().NotThrow($"VerbClassification must map {t}");
        }
    }

    [Fact]
    public void SimEvent_IsImmutableRecord()
    {
        typeof(SimEvent).IsClass.Should().BeTrue();

        // Records expose a compiler-generated EqualityContract; required props are init-only.
        var yearSetter = typeof(SimEvent).GetProperty(nameof(SimEvent.Year))!.SetMethod!;
        var isInitOnly = yearSetter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));
        isInitOnly.Should().BeTrue("SimEvent properties must be init-only (immutable after construction)");
    }

    [Fact]
    public void SimEvent_PrimaryEntitiesDefaultsToEmptyList()
    {
        var ev = new SimEvent
        {
            Id = new EventId(1),
            Type = EventType.WildfireOccurred,
            TypeName = "WildfireOccurred",
            Domain = "Environmental",
            Year = 1,
            Season = Season.Spring,
            Tick = 0,
            TierInvolvement = EventTier.Regional,
            VerbClass = VerbClass.Destruction,
            PopulationImpact = PopulationImpact.Minor,
            IsFirstOfKind = false,
            IsGodMode = false,
            PayloadJson = "{}"
        };

        ev.PrimaryEntities.Should().NotBeNull().And.BeEmpty();
    }
}
