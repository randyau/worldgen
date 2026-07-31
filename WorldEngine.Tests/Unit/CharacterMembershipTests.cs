using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M12 12.2: IdentityData.CivId was replaced by Tier1Character.Memberships, a real (if
/// currently single-entry) membership list that M13-M15 extend with Family/Guild/Religion
/// memberships. CivTracker.SetCharacterCiv is the sole write path — these tests cover its
/// contract directly, independent of any specific civ-founding/succession call site.
/// </summary>
public class CharacterMembershipTests
{
    private static Tier1Character MakeCharacter(WorldState world, long id = 1L)
    {
        var c = CharacterFactory.Spawn(new TileCoord(0, 0), BiomeType.Plains, world.WorldSeed, id, world.SimConfig, world.CurrentYear);
        world.Entities.Add(c);
        return c;
    }

    [Fact]
    public void NewCharacter_HasNoCivId()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var c = MakeCharacter(world);

        c.Memberships.Should().BeEmpty();
        c.CivId.Should().Be(CivId.None);
    }

    [Fact]
    public void SetCharacterCiv_SelfHealsMissingOrganization()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var civId = new CivId(1);
        var ruler = MakeCharacter(world);
        // Pre-M12-style fixture: civ constructed directly, no Organization.
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", ruler.Id, new TileCoord(0, 0), 0);

        CivTracker.SetCharacterCiv(ruler, civId, OrganizationRole.Leader, world);

        ruler.CivId.Should().Be(civId);
        world.Civilizations[civId].OrgId.Should().NotBeNull("SetCharacterCiv must backfill a missing Organization rather than silently dropping the membership");
        var org = world.Organizations[world.Civilizations[civId].OrgId!.Value];
        org.Members.Should().ContainKey(ruler.Id);
    }

    [Fact]
    public void SetCharacterCiv_SwitchingCivs_RemovesOldMembershipAndOrgEntry()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var civA = new CivId(1);
        var civB = new CivId(2);
        var c = MakeCharacter(world);
        world.Civilizations[civA] = new Civilization(civA, "CivA", c.Id, new TileCoord(0, 0), 0);
        world.Civilizations[civB] = new Civilization(civB, "CivB", c.Id, new TileCoord(1, 1), 0);

        CivTracker.SetCharacterCiv(c, civA, OrganizationRole.Member, world);
        var orgAId = world.Civilizations[civA].OrgId!.Value;
        c.CivId.Should().Be(civA);
        world.Organizations[orgAId].Members.Should().ContainKey(c.Id);

        CivTracker.SetCharacterCiv(c, civB, OrganizationRole.Member, world);

        c.Memberships.Should().ContainSingle("switching civs must not leave a stale second membership");
        c.CivId.Should().Be(civB);
        world.Organizations[orgAId].Members.Should().NotContainKey(c.Id, "the old civ's Organization.Members entry must be removed");
    }

    [Fact]
    public void SetCharacterCiv_InvalidCivId_ClearsMembership()
    {
        var world = WorldTestHelper.CreateSmallWorld();
        var civId = new CivId(1);
        var c = MakeCharacter(world);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", c.Id, new TileCoord(0, 0), 0);
        CivTracker.SetCharacterCiv(c, civId, OrganizationRole.Member, world);
        c.CivId.Should().Be(civId, "sanity check");

        CivTracker.SetCharacterCiv(c, CivId.None, OrganizationRole.Member, world);

        c.Memberships.Should().BeEmpty();
        c.CivId.Should().Be(CivId.None);
    }
}
