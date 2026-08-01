using System.Reflection;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M13 13.0's first real consumer of M12 design decision 2 (weighted-loyalty conflict scoring):
/// UtilityScorer.KinDampening dampens War/Raid desirability when the acting character has a
/// Family-org relative living in the target civ. `private static` and a pure function (no side
/// effects), so reflection is the direct way to test it in isolation — same rationale as
/// UtilityScorerSpotlightBiasTests for ApplySpotlightBias.
/// </summary>
public class KinDampeningTests
{
    private static float InvokeKinDampening(Tier1Character c, CivId targetCivId, WorldState world)
    {
        var method = typeof(UtilityScorer).GetMethod("KinDampening", BindingFlags.NonPublic | BindingFlags.Static);
        return (float)method!.Invoke(null, new object[] { c, targetCivId, world, world.SimConfig.Family })!;
    }

    private static TileCoord FindLandTile(WorldState world)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        throw new System.Exception("no land tile found");
    }

    [Fact]
    public void NoFamilyRelativeInTargetCiv_ReturnsFullScore()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 3);
        var tile = FindLandTile(world);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);

        var enemyCivId = new CivId(999);
        InvokeKinDampening(c, enemyCivId, world).Should().Be(1f);
    }

    [Fact]
    public void FamilyRelativeInTargetCiv_DampensProportionallyToLoyalty()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 3);
        var tile = FindLandTile(world);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        var c        = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 10L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        var relative = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 11L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        world.Entities.Add(relative);

        var enemyCivId = new CivId(world.NextCivId++);
        world.Civilizations[enemyCivId] = new Civilization(enemyCivId, "EnemyCiv", relative.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(relative, enemyCivId, OrganizationRole.Leader, world);

        var orgId = new OrganizationId(world.NextOrganizationId++);
        var org = new Organization(orgId, OrganizationKind.Family, "House of Test", c.Id, world.CurrentYear);
        world.Organizations[orgId] = org;

        float highLoyalty = 0.9f;
        var cMembership = new Membership(orgId, OrganizationRole.Leader, highLoyalty);
        var relMembership = new Membership(orgId, OrganizationRole.Member, 1.0f);
        c.Memberships.Add(cMembership);
        relative.Memberships.Add(relMembership);
        org.Members[c.Id] = cMembership;
        org.Members[relative.Id] = relMembership;

        float dampenMin = world.SimConfig.Family.KinInEnemyCivWarDampenMin;
        float result = InvokeKinDampening(c, enemyCivId, world);
        result.Should().BeLessThan(1f, "a Family relative living in the target civ must dampen the score");
        result.Should().BeApproximately(1f - highLoyalty * (1f - dampenMin), 0.001f);
    }
}
