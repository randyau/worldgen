using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class OrganizationMembershipTests
{
    private static Tier1Character MakeCharacter(long id) => new(
        new EntityId(id), new TileCoord(0, 0),
        PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
        new IdentityData($"C{id}", "the Test", "test", null, null, 0, 0),
        100, 200);

    private static Organization MakeOrg(int id, OrganizationKind kind) =>
        new(new OrganizationId(id), kind, $"Org{id}", new EntityId(999L), 0);

    [Fact]
    public void Join_WritesBothSides()
    {
        var c = MakeCharacter(1);
        var org = MakeOrg(1, OrganizationKind.Guild);

        OrganizationMembership.Join(c, org, OrganizationRole.Member, 0.5f);

        c.Memberships.Should().ContainSingle().Which.OrganizationId.Should().Be(org.Id);
        org.Members.Should().ContainKey(c.Id);
        org.Members[c.Id].Loyalty.Should().Be(0.5f);
    }

    [Fact]
    public void Leave_ClearsBothSides()
    {
        var c = MakeCharacter(1);
        var org = MakeOrg(1, OrganizationKind.Religion);
        OrganizationMembership.Join(c, org, OrganizationRole.Member, 0.5f);

        OrganizationMembership.Leave(c, org);

        c.Memberships.Should().BeEmpty();
        org.Members.Should().BeEmpty();
    }

    /// <summary>
    /// M15.9 — the approved fix for the reorder bug: loyalty updates used to be
    /// List.Remove + List.Add, moving the membership to the end. Tier1Character.CivId resolves to
    /// the *first* civ-valid entry, so Memberships ordering is load-bearing.
    /// </summary>
    [Fact]
    public void SetLoyalty_UpdatesInPlace_PreservingMembershipOrder()
    {
        var c = MakeCharacter(1);
        var civOrg    = MakeOrg(1, OrganizationKind.Civilization);
        var familyOrg = MakeOrg(2, OrganizationKind.Family);
        var relOrg    = MakeOrg(3, OrganizationKind.Religion);

        var civId = new CivId(7);
        OrganizationMembership.Join(c, civOrg, OrganizationRole.Member, 1.0f, civId);
        OrganizationMembership.Join(c, familyOrg, OrganizationRole.Member, 1.0f);
        OrganizationMembership.Join(c, relOrg, OrganizationRole.Member, 0.2f);

        OrganizationMembership.SetLoyalty(c, civOrg, 0.3f);
        OrganizationMembership.SetLoyalty(c, relOrg, 0.9f);

        c.Memberships.Select(m => m.OrganizationId)
            .Should().Equal(new[] { civOrg.Id, familyOrg.Id, relOrg.Id }, "loyalty updates must not reorder");
        c.CivId.Should().Be(civId);
        c.Memberships[0].Loyalty.Should().Be(0.3f);
        c.Memberships[2].Loyalty.Should().Be(0.9f);

        // Both sides stay in agreement.
        civOrg.Members[c.Id].Loyalty.Should().Be(0.3f);
        relOrg.Members[c.Id].Loyalty.Should().Be(0.9f);
    }

    [Fact]
    public void SetLoyalty_ReturnsNull_WhenNotAMember()
    {
        var c = MakeCharacter(1);
        var org = MakeOrg(1, OrganizationKind.Guild);

        OrganizationMembership.SetLoyalty(c, org, 0.5f).Should().BeNull();
        org.Members.Should().BeEmpty();
    }
}
