using System.Collections.Generic;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ArtifactDecayPhase"/> — the annual destruction sink that bounds
/// long-term artifact accumulation.
/// </summary>
public class ArtifactDecayTests
{
    private static Artifact Add(WorldState world, ArtifactOwner owner, string name = "Relic") =>
        ArtifactRegistry.Create(world, name, ArtifactCategory.Relic, 1,
            creatorId: 0, creatorName: "Anon", origin: "test", quality: 0.9f, owner: owner);

    [Fact]
    public void Decay_WithZeroProbability_DestroysNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 7);
        Add(world, ArtifactOwner.Lost);
        var cfg = new ArtifactConfig { LostArtifactAnnualDecay = 0f, OwnedArtifactAnnualDecay = 0f };

        var pending = new List<PendingEvent>();
        ArtifactDecayPhase.Execute(world, pending, cfg);

        world.Artifacts.Values.Should().OnlyContain(a => !a.IsDestroyed);
        pending.Should().BeEmpty();
    }

    [Fact]
    public void Decay_WithCertainProbability_DestroysLostAndEmitsEvent()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 7);
        var art   = Add(world, ArtifactOwner.Lost);
        var cfg   = new ArtifactConfig { LostArtifactAnnualDecay = 1f, OwnedArtifactAnnualDecay = 0f };

        var pending = new List<PendingEvent>();
        ArtifactDecayPhase.Execute(world, pending, cfg);

        world.Artifacts[art.Id].IsDestroyed.Should().BeTrue();
        world.Artifacts[art.Id].DestroyedYear.Should().Be(world.CurrentYear);
        pending.Should().ContainSingle(e => e.Type == EventType.ArtifactDestroyed);
    }

    [Fact]
    public void Decay_LostRateHigherThanOwned_PrefersLostArtifacts()
    {
        // Lost decays with certainty; owned never — only the Lost one should be destroyed.
        var world = WorldTestHelper.CreateSmallWorld(seed: 7);
        var lost  = Add(world, ArtifactOwner.Lost, "Lost Relic");
        var owned = Add(world, ArtifactOwner.OfCharacter(new EntityId(999)), "Held Relic");
        var cfg   = new ArtifactConfig { LostArtifactAnnualDecay = 1f, OwnedArtifactAnnualDecay = 0f };

        ArtifactDecayPhase.Execute(world, new List<PendingEvent>(), cfg);

        world.Artifacts[lost.Id].IsDestroyed.Should().BeTrue();
        world.Artifacts[owned.Id].IsDestroyed.Should().BeFalse();
    }

    [Fact]
    public void Decay_AlreadyDestroyed_IsNotReprocessed()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 7);
        var art   = Add(world, ArtifactOwner.Lost);
        ArtifactRegistry.Destroy(world, art.Id, 5);
        var cfg   = new ArtifactConfig { LostArtifactAnnualDecay = 1f };

        var pending = new List<PendingEvent>();
        ArtifactDecayPhase.Execute(world, pending, cfg);

        // No new event — it was already destroyed; DestroyedYear unchanged.
        world.Artifacts[art.Id].DestroyedYear.Should().Be(5);
        pending.Should().BeEmpty();
    }
}
