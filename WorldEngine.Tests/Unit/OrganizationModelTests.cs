using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M12 phase 12.0: Organization core entity + registry. A newly founded civilization gets a
/// matching Organization (Kind: Civilization), with the founder as its sole Leader member.
/// </summary>
public class OrganizationModelTests
{
    private static (WorldState world, Civilization civ) FoundCiv(int seed = 42) =>
        FoundCivIn(WorldTestHelper.CreateSmallWorld(seed: seed), seedOffset: 0L);

    private static (WorldState world, Civilization civ) FoundCivIn(WorldState world, long seedOffset)
    {
        TileCoord cityTile = default;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (world.TileGrid.GetTile(c).Fertility < 10) continue;
            if (world.Settlements.ContainsKey(c)) continue;
            cityTile = c;
            goto Found;
        }
        Found:
        var biome   = (BiomeType)world.TileGrid.GetTile(cityTile).BiomeType;
        var founder = CharacterFactory.Spawn(cityTile, biome, world.WorldSeed, 1L + seedOffset, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);

        int savedMinDist = world.SimConfig.Character.GlobalSettlementMinDist;
        world.SimConfig.Character.GlobalSettlementMinDist = 0;
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, cityTile), world, pending, world.SimConfig.SettlementNames);
        world.SimConfig.Character.GlobalSettlementMinDist = savedMinDist;

        var civId = world.Settlements[cityTile].CivId;
        return (world, world.Civilizations[civId]);
    }

    [Fact]
    public void FoundingSettlement_CreatesMatchingOrganization()
    {
        var (world, civ) = FoundCiv();

        civ.OrgId.Should().NotBeNull();
        world.Organizations.Should().ContainKey(civ.OrgId!.Value);

        var org = world.Organizations[civ.OrgId.Value];
        org.Kind.Should().Be(OrganizationKind.Civilization);
        org.Name.Should().Be(civ.Name);
        org.LeaderId.Should().Be(civ.RulerId);
        org.Members.Should().ContainKey(civ.RulerId);
        org.Members[civ.RulerId].Role.Should().Be(OrganizationRole.Leader);
    }

    [Fact]
    public void EachCivilization_GetsADistinctOrganizationId()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var (_, civ1) = FoundCivIn(world, seedOffset: 0L);
        var (_, civ2) = FoundCivIn(world, seedOffset: 1L);

        civ1.OrgId.Should().NotBe(civ2.OrgId);
    }
}
