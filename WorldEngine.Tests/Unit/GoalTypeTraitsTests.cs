using FluentAssertions;
using WorldEngine.Sim.Entities.Characters;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class GoalTypeTraitsTests
{
    /// <summary>
    /// Exhaustiveness guard, mirroring EventTypeTests.VerbClassification_AllEventTypesHaveMapping:
    /// a new GoalType added without a GoalTypeTraits row would otherwise throw at runtime the first
    /// time a character formed that goal.
    /// </summary>
    [Fact]
    public void AllGoalTypesHaveTraits()
    {
        foreach (var t in Enum.GetValues<GoalType>())
        {
            var act = () => GoalTypeTraits.Of(t);
            act.Should().NotThrow($"GoalTypeTraits must classify {t}");
        }
    }

    /// <summary>
    /// Pins the transcription of the five lists GoalManager used to keep by hand (M15.9), so the
    /// refactor cannot silently drift from the behavior it replaced.
    /// </summary>
    [Fact]
    public void TraitSetsMatchTheOriginalGoalManagerLists()
    {
        Enum.GetValues<GoalType>().Where(t => GoalTypeTraits.IsNotable(t))
            .Should().BeEquivalentTo(new[]
            {
                GoalType.Bond, GoalType.Avenge, GoalType.Create, GoalType.Dominance,
                GoalType.Alliance, GoalType.FoundCity, GoalType.BuildImprovement,
                GoalType.SlayBeast, GoalType.CovetArtifact, GoalType.SeaVoyage,
                GoalType.Pilgrimage
            });

        Enum.GetValues<GoalType>().Where(t => GoalTypeTraits.IsLongRunning(t))
            .Should().BeEquivalentTo(new[]
            {
                GoalType.Bond, GoalType.Create, GoalType.FoundCity, GoalType.SlayBeast,
                GoalType.BuildImprovement, GoalType.Alliance, GoalType.CovetArtifact,
                GoalType.SeaVoyage, GoalType.Pilgrimage
            });

        Enum.GetValues<GoalType>().Where(t => GoalTypeTraits.IsDiscretionary(t))
            .Should().BeEquivalentTo(new[]
            {
                GoalType.Dominance, GoalType.Alliance, GoalType.Bond, GoalType.Create,
                GoalType.BuildImprovement, GoalType.SlayBeast, GoalType.CovetArtifact
            });

        Enum.GetValues<GoalType>().Where(t => GoalTypeTraits.IsFlourishing(t))
            .Should().BeEquivalentTo(new[]
            {
                GoalType.Create, GoalType.Bond, GoalType.FoundCity, GoalType.Protect
            });
    }
}
