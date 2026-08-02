using System.Reflection;
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
/// M13 13.1: Fear activated as a submission/appeasement axis distinct from Trust — before this,
/// RelationshipEdge.Fear was written once (a flat +0.1 on rivalry formation) and never read by
/// anything (see roadmap.md's 2026-07-30 relationship-system audit). Placate gives rivalry an
/// outlet other than Dominance/war; UtilityScorer.FearDampening gives the passive "avoided" half.
/// </summary>
public class FearAppeasementTests
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

    private static Tier1Character SpawnAt(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        return c;
    }

    [Fact]
    public void DeclareRivalry_AgainstStrongerTarget_ScalesFearAboveBaseIncrement()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 31);
        var tile = FindLandTile(world);
        var weak   = SpawnAt(world, tile, 1L);
        var strong = SpawnAt(world, tile, 2L);
        weak.Skills   = weak.Skills   with { Combat = 0.1f };
        strong.Skills = strong.Skills with { Combat = 0.9f };

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(weak.Id, strong.Id), world, pending);

        var rel = world.GetRelationship(weak.Id, strong.Id)!;
        rel.Fear.Should().BeGreaterThan(world.SimConfig.Fear.RivalryBaseFearIncrement,
            "a rivalry against a visibly stronger target should generate more Fear than the flat base increment");
    }

    [Fact]
    public void Placate_ExistingFearedRival_ReducesFearAndRaisesTrust()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 32);
        var tile = FindLandTile(world);
        var c      = SpawnAt(world, tile, 11L);
        var rival  = SpawnAt(world, tile, 12L);

        var rivalryPending = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(c.Id, rival.Id), world, rivalryPending);
        var beforeRel = world.GetRelationship(c.Id, rival.Id)!;
        float fearBefore = beforeRel.Fear;
        float trustBefore = beforeRel.Trust;

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Placate(c.Id, rival.Id), world, pending);

        var rel = world.GetRelationship(c.Id, rival.Id)!;
        rel.Fear.Should().BeLessThan(fearBefore);
        rel.Trust.Should().BeGreaterThan(trustBefore);
        rel.IsRival.Should().BeTrue("placation appeases, it does not itself end the rivalry");
    }

    [Fact]
    public void Placate_NonRival_DoesNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 33);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 21L);
        var other = SpawnAt(world, tile, 22L);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Placate(c.Id, other.Id), world, pending);

        pending.Should().BeEmpty();
    }

    private static float InvokeFearDampening(Tier1Character c, CivId targetCivId, WorldState world)
    {
        var method = typeof(UtilityScorer).GetMethod("FearDampening", BindingFlags.NonPublic | BindingFlags.Static);
        return (float)method!.Invoke(null, new object[] { c, targetCivId, world, world.SimConfig.Fear })!;
    }

    [Fact]
    public void FearDampening_FearedRivalInTargetCiv_DampensProportionallyToFear()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 34);
        var tile = FindLandTile(world);
        var c     = SpawnAt(world, tile, 41L);
        var rival = SpawnAt(world, tile, 42L);

        var enemyCivId = new CivId(world.NextCivId++);
        world.Civilizations[enemyCivId] = new Civilization(enemyCivId, "EnemyCiv", rival.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(rival, enemyCivId, OrganizationRole.Leader, world);

        var rel = world.Relationships.GetOrCreate(c.Id, rival.Id);
        float fear = 0.7f;
        world.Relationships.Upsert(rel with { Fear = fear, Flags = RelationshipFlags.IsRival });

        float dampenMin = world.SimConfig.Fear.FearWarDampenMin;
        float result = InvokeFearDampening(c, enemyCivId, world);
        result.Should().BeLessThan(1f, "an unresolved Fear toward a rival living in the target civ must dampen the score");
        result.Should().BeApproximately(1f - fear * (1f - dampenMin), 0.001f);
    }

    [Fact]
    public void FearDampening_NoRivalInTargetCiv_ReturnsFullScore()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 35);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 51L);

        InvokeFearDampening(c, new CivId(999), world).Should().Be(1f);
    }
}
