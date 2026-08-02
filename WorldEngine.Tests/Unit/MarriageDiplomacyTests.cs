using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M13 13.3: ruler cross-civ marriage as a real diplomatic lever — reuses the same
/// Organization-to-Organization alliance fact ResolveAlly forms (M12 design decision 1), so an
/// arranged marriage's alliance survives either ruler's later death or succession instead of
/// evaporating with the personal RelationshipEdge.
/// </summary>
public class MarriageDiplomacyTests
{
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

    private static (Tier1Character ruler, CivId civId) SpawnRuler(WorldState world, TileCoord tile, long seedOffset, string civName)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var ruler = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        ruler.AgeSeason = world.SimConfig.Family.MarriageMinAgeSeasons + 10;
        world.Entities.Add(ruler);

        var civId = new CivId(world.NextCivId++);
        world.Civilizations[civId] = new Civilization(civId, civName, ruler.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(ruler, civId, OrganizationRole.Leader, world);
        return (ruler, civId);
    }

    private static Organization GetCivOrg(WorldState world, CivId civId) =>
        world.Organizations[world.Civilizations[civId].OrgId!.Value];

    [Fact]
    public void RulerMarriage_CrossCivAtPeace_FormsOrgAlliance()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 71);
        var tile = FindLandTile(world);
        var (rulerA, civA) = SpawnRuler(world, tile, 1L, "CivA");
        var (rulerB, civB) = SpawnRuler(world, tile, 2L, "CivB");

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(rulerA.Id, rulerB.Id), world, pending);

        var orgA = GetCivOrg(world, civA);
        var orgB = GetCivOrg(world, civB);
        orgA.Allies.Should().Contain(orgB.Id);
        orgB.Allies.Should().Contain(orgA.Id);
    }

    [Fact]
    public void RulerMarriage_CivsAtWar_DoesNotFormAlliance()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 72);
        var tile = FindLandTile(world);
        var (rulerA, civA) = SpawnRuler(world, tile, 11L, "CivA");
        var (rulerB, civB) = SpawnRuler(world, tile, 12L, "CivB");
        world.Civilizations[civA].WarsAgainst[civB] = world.CurrentYear;

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(rulerA.Id, rulerB.Id), world, pending);

        var orgA = GetCivOrg(world, civA);
        var orgB = GetCivOrg(world, civB);
        orgA.Allies.Should().NotContain(orgB.Id, "an arranged marriage between civs already at war should not silently cement an alliance");

        // The marriage itself still proceeds — only the civ-level alliance fact is gated on peace.
        world.GetRelationship(rulerA.Id, rulerB.Id)!.IsMarried.Should().BeTrue();
    }

    [Fact]
    public void Marriage_NonRulerSpouse_DoesNotFormOrgAlliance()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 73);
        var tile = FindLandTile(world);
        var (rulerA, civA) = SpawnRuler(world, tile, 21L, "CivA");
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var commonerB = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 22L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        commonerB.AgeSeason = world.SimConfig.Family.MarriageMinAgeSeasons + 10;
        world.Entities.Add(commonerB);
        var civB = new CivId(world.NextCivId++);
        world.Civilizations[civB] = new Civilization(civB, "CivB", new EntityId(999_999), tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(commonerB, civB, OrganizationRole.Member, world);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(rulerA.Id, commonerB.Id), world, pending);

        var orgA = GetCivOrg(world, civA);
        orgA.Allies.Should().BeEmpty("only a marriage between two current rulers cements a civ-level alliance");
    }
}
