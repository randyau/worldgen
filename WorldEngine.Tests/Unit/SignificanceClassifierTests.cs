using System.Text.Json;
using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Events;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class SignificanceClassifierTests
{
    private static string Payload(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public void Classifier_SeaLevelChangedIsHeadline()
    {
        var (tier, impact) = SignificanceClassifier.Classify(
            EventType.SeaLevelChanged, Payload(new { Delta = 0.05f }), isFirstOfKind: false);

        impact.Should().Be(PopulationImpact.Catastrophic);
        tier.Should().Be(EventTier.Headline);
    }

    [Fact]
    public void Classifier_DestructionVerbIsAtLeastRegional()
    {
        // Wildfire low intensity → impact Minor (Background tier), but Destruction floor = Regional.
        var (tier, _) = SignificanceClassifier.Classify(
            EventType.WildfireOccurred, Payload(new { Intensity = 0.1f }), isFirstOfKind: false);

        ((int)tier).Should().BeGreaterThanOrEqualTo((int)EventTier.Regional);
    }

    [Fact]
    public void Classifier_CatastrophicImpactIsHeadline()
    {
        var (tier, impact) = SignificanceClassifier.Classify(
            EventType.EarthquakeOccurred, Payload(new { Intensity = 0.95f }), isFirstOfKind: false);

        impact.Should().Be(PopulationImpact.Catastrophic);
        tier.Should().Be(EventTier.Headline);
    }

    [Fact]
    public void Classifier_FirstVolcanicEruptionBumped()
    {
        var (tier, _) = SignificanceClassifier.Classify(
            EventType.VolcanicEruption, Payload(new { Intensity = 0.3f }), isFirstOfKind: true);

        tier.Should().Be(EventTier.Headline, "Regional base bumped one tier for first-of-kind");
    }

    [Fact]
    public void Classifier_SecondVolcanicEruptionNotBumped()
    {
        var (tier, _) = SignificanceClassifier.Classify(
            EventType.VolcanicEruption, Payload(new { Intensity = 0.3f }), isFirstOfKind: false);

        tier.Should().Be(EventTier.Regional, "no first-of-kind bump");
    }

    [Fact]
    public void Classifier_PopulationImpactNoneIsBackground()
    {
        var (tier, impact) = SignificanceClassifier.Classify(
            EventType.BiomeChanged, "{}", isFirstOfKind: false);

        impact.Should().Be(PopulationImpact.None);
        // BiomeChanged is Transformation → floor Character; impact None → Background. max = Character.
        tier.Should().Be(EventTier.Character);
    }

    [Fact]
    public void Classifier_MaxAcrossAllRules()
    {
        // Volcanic intensity 0.9 → Catastrophic impact → Headline (impact tier),
        // Destruction floor → Regional. Result is max, not sum: Headline, not above.
        var (tier, impact) = SignificanceClassifier.Classify(
            EventType.VolcanicEruption, JsonSerializer.Serialize(new { Intensity = 0.9f }), isFirstOfKind: false);

        impact.Should().Be(PopulationImpact.Catastrophic);
        tier.Should().Be(EventTier.Headline);
    }
}
