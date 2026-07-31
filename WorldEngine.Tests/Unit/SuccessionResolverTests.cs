using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M12 12.3: SuccessionResolver.SelectSuccessor is the generalized heir-selection kernel — it
/// operates purely on Organization.Members, with no Civilization-specific assumptions, so
/// M13-M15 can reuse it for family/guild/religious-leader seats. These tests exercise it directly
/// against a bare Organization, without a backing Civilization at all, to prove that.
/// </summary>
public class SuccessionResolverTests
{
    private static Tier1Character MakeMember(WorldState world, OrganizationId orgId, long id, int ageSeason, float leadership)
    {
        var c = CharacterFactory.Spawn(new TileCoord(0, 0), BiomeType.Plains, world.WorldSeed, id, world.SimConfig, world.CurrentYear, startAsAdult: true);
        c.AgeSeason = ageSeason;
        c.Skills = c.Skills with { Leadership = leadership };
        c.Memberships.Add(new Membership(orgId, OrganizationRole.Member, 1f));
        world.Entities.Add(c);
        return c;
    }

    [Fact]
    public void SelectSuccessor_PicksHighestScoringEligibleMember_NoCivilizationInvolved()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var orgId = new OrganizationId(1);
        var org = new Organization(orgId, OrganizationKind.Family, "TestFamily", new EntityId(0), 0);
        world.Organizations[orgId] = org;

        var weak   = MakeMember(world, orgId, 1L, ageSeason: 2000, leadership: 0.2f);
        var strong = MakeMember(world, orgId, 2L, ageSeason: 2000, leadership: 0.9f);
        org.Members[weak.Id]   = new Membership(orgId, OrganizationRole.Member, 1f);
        org.Members[strong.Id] = new Membership(orgId, OrganizationRole.Member, 1f);

        var result = SuccessionResolver.SelectSuccessor(org, world, minAgeSeasons: 500,
            member => member.Skills.Leadership);

        result.Should().Be(strong.Id);
    }

    [Fact]
    public void SelectSuccessor_SkipsUnderageMembers()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var orgId = new OrganizationId(1);
        var org = new Organization(orgId, OrganizationKind.Guild, "TestGuild", new EntityId(0), 0);
        world.Organizations[orgId] = org;

        var infant = MakeMember(world, orgId, 1L, ageSeason: 0, leadership: 1.0f);
        var adult  = MakeMember(world, orgId, 2L, ageSeason: 2000, leadership: 0.1f);
        org.Members[infant.Id] = new Membership(orgId, OrganizationRole.Member, 1f);
        org.Members[adult.Id]  = new Membership(orgId, OrganizationRole.Member, 1f);

        var result = SuccessionResolver.SelectSuccessor(org, world, minAgeSeasons: 500,
            member => member.Skills.Leadership);

        result.Should().Be(adult.Id, "the infant outscores the adult but is below the minimum age");
    }

    [Fact]
    public void SelectSuccessor_NoEligibleMembers_ReturnsNull()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var orgId = new OrganizationId(1);
        var org = new Organization(orgId, OrganizationKind.Religion, "TestReligion", new EntityId(0), 0);
        world.Organizations[orgId] = org;

        var result = SuccessionResolver.SelectSuccessor(org, world, minAgeSeasons: 500,
            member => member.Skills.Leadership);

        result.Should().BeNull();
    }
}
