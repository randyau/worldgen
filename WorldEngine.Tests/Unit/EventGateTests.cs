using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Events;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class EventGateTests
{
    [Fact]
    public void EventGate_SuppressedTypeRejected()
    {
        var cfg = TestSimConfig.With(c => c.Events.SuppressedTypes.Add(nameof(EventType.WildfireOccurred)));
        var gate = new EventGate(cfg);

        gate.ShouldRecord(EventType.WildfireOccurred, EventTier.Headline).Should().BeFalse();
    }

    [Fact]
    public void EventGate_BelowMinimumTierRejected()
    {
        var cfg = TestSimConfig.With(c => c.Events.MinimumRecordedTier = EventTier.Regional);
        var gate = new EventGate(cfg);

        gate.ShouldRecord(EventType.WildfireOccurred, EventTier.Background).Should().BeFalse();
    }

    [Fact]
    public void EventGate_GodModeAlwaysAccepted()
    {
        var cfg = TestSimConfig.With(c =>
        {
            c.Events.MinimumRecordedTier = EventTier.Headline;
            c.Events.SuppressedTypes.Add(nameof(EventType.WildfireOccurred));
        });
        var gate = new EventGate(cfg);

        gate.ShouldRecord(EventType.WildfireOccurred, EventTier.Background, isGodMode: true).Should().BeTrue();
    }

    [Fact]
    public void EventGate_NormalEventAccepted()
    {
        var cfg = TestSimConfig.With(c => c.Events.MinimumRecordedTier = EventTier.Background);
        var gate = new EventGate(cfg);

        gate.ShouldRecord(EventType.VolcanicEruption, EventTier.Regional).Should().BeTrue();
    }

    [Fact]
    public void EventGate_EmptySuppressedListAcceptsAll()
    {
        var cfg = TestSimConfig.With(c =>
        {
            c.Events.SuppressedTypes.Clear();
            c.Events.MinimumRecordedTier = EventTier.Background;
        });
        var gate = new EventGate(cfg);

        foreach (var t in Enum.GetValues<EventType>())
            gate.ShouldRecord(t, EventTier.Background).Should().BeTrue($"{t} should pass an empty suppression list");
    }
}
