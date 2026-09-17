using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>M15 15.2 — religion schism. See docs/phases/archive/m15_religion_deepened.md.</summary>
public class ReligionSchismTests
{
    private static List<PendingEvent> RunSchism(CharacterBehaviorPhase phase, WorldState world)
    {
        var pending = new List<PendingEvent>();
        phase.ProcessAnnualReligionSchism(world, pending);
        return pending;
    }

    /// <summary>Builds a Religion org with a leader plus <paramref name="followerCount"/> followers, each given the same Loyalty.</summary>
    private static (Organization org, Tier1Character leader, List<Tier1Character> followers) BuildReligion(
        WorldState world, TileCoord tile, int followerCount, float followerLoyalty, long idBase)
    {
        var leader = WorldGenTestHelpers.SpawnAt(world, tile, idBase);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", leader.Id, tile);
        var org = world.Organizations[orgId];
        var leaderM = new Membership(orgId, OrganizationRole.Leader, 1f);
        org.Members[leader.Id] = leaderM;
        leader.Memberships.Add(leaderM);

        var followers = new List<Tier1Character>();
        for (int i = 0; i < followerCount; i++)
        {
            var f = WorldGenTestHelpers.SpawnAt(world, tile, idBase + i + 1);
            var m = new Membership(orgId, OrganizationRole.Member, followerLoyalty);
            org.Members[f.Id] = m;
            f.Memberships.Add(m);
            followers.Add(f);
        }
        return (org, leader, followers);
    }

    [Fact]
    public void BelowMinMembers_NeverSchisms()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 401);
        var tile = WorldGenTestHelpers.FindLandTile(world);
        world.SimConfig.Religion.SchismMinMembers = 6;
        world.SimConfig.Religion.SchismBaseChance = 1000f; // would force schism if eligible

        BuildReligion(world, tile, followerCount: 2, followerLoyalty: 0.1f, idBase: 1); // 3 living total < 6

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = RunSchism(phase, world);

        pending.Should().BeEmpty();
        world.Organizations.Values.Count(o => o.Kind == OrganizationKind.Religion).Should().Be(1);
    }

    [Fact]
    public void HighAverageLoyalty_NeverSchisms()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 402);
        var tile = WorldGenTestHelpers.FindLandTile(world);
        world.SimConfig.Religion.SchismMinMembers = 3;
        world.SimConfig.Religion.SchismAvgLoyaltyThreshold = 0.5f;
        world.SimConfig.Religion.SchismBaseChance = 1000f;

        BuildReligion(world, tile, followerCount: 6, followerLoyalty: 0.9f, idBase: 1); // devout followers

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = RunSchism(phase, world);

        pending.Should().BeEmpty("high average Loyalty means no doctrinal-tension eligibility, regardless of roll chance");
    }

    [Fact]
    public void LowAverageLoyalty_WithSaturatedChance_SchismsIntoNewOrganization()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 403);
        var tile = WorldGenTestHelpers.FindLandTile(world);
        world.SimConfig.Religion.SchismMinMembers = 3;
        world.SimConfig.Religion.SchismAvgLoyaltyThreshold = 0.5f;
        world.SimConfig.Religion.SchismBaseChance = 1000f; // saturate so the roll always succeeds

        var (org, leader, followers) = BuildReligion(world, tile, followerCount: 6, followerLoyalty: 0.1f, idBase: 1);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = RunSchism(phase, world);

        pending.Should().ContainSingle(e => e.Type == EventType.ReligionSchism);
        world.Organizations.Values.Count(o => o.Kind == OrganizationKind.Religion).Should().Be(2);

        // The original leader and org must survive with at least themselves.
        org.Members.Should().ContainKey(leader.Id);
        leader.Memberships.Should().Contain(m => m.OrganizationId == org.Id);

        // All formerly-uniform-Loyalty followers were at-or-below the average, so all of them seceded.
        var newOrg = world.Organizations.Values.Single(o => o.Kind == OrganizationKind.Religion && o.Id != org.Id);
        foreach (var f in followers)
        {
            newOrg.Members.Should().ContainKey(f.Id);
            org.Members.Should().NotContainKey(f.Id);
            f.Memberships.Should().Contain(m => m.OrganizationId == newOrg.Id);
        }
        newOrg.LeaderId.Should().Be(followers.OrderBy(f => f.Id.Value).First().Id,
            "the dissenter (lowest Loyalty, tie-broken by EntityId) leads the new sect");
    }
}
