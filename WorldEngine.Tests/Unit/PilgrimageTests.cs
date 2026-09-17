using System.Reflection;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>M15 15.4 — pilgrimage goal. See docs/phases/archive/m15_religion_deepened.md.</summary>
public class PilgrimageTests
{
    private static TileCoord FindLandTile(WorldState world)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        throw new InvalidOperationException("No suitable land tile found");
    }

    private static Tier1Character SpawnAt(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        return c;
    }

    private static void TryFormPilgrimageGoal(CharacterBehaviorPhase phase, Tier1Character c, WorldState world, List<PendingEvent> pending) =>
        typeof(CharacterBehaviorPhase)
            .GetMethod("TryFormPilgrimageGoal", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { c, world, 0L, pending });

    private static void CompletePilgrimage(Tier1Character c, GoalData goal, WorldState world, List<PendingEvent> pending) =>
        typeof(CharacterBehaviorPhase)
            .GetMethod("CompletePilgrimage", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { c, goal, world, pending });

    private static (WorldState world, TileCoord homeTile, TileCoord farTile, CivId civ) SetupCivWithTwoTiles(int seed)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: seed);
        var homeTile = FindLandTile(world);
        TileCoord farTile = homeTile;
        for (int y = world.TileGrid.TileHeight - 2; y >= 1; y--)
        for (int x = world.TileGrid.TileWidth - 1; x >= 0; x--)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c) && c != homeTile) { farTile = c; goto found; }
        }
        found:
        var civ = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), homeTile, 1);
        return (world, homeTile, farTile, civ);
    }

    [Fact]
    public void DevoutMemberAwayFromHomeSite_FormsPilgrimageGoal()
    {
        var (world, homeTile, farTile, civ) = SetupCivWithTwoTiles(seed: 601);
        world.SimConfig.Religion.PilgrimagePietyThreshold = 0.5f;

        var founder = SpawnAt(world, homeTile, 1L);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", founder.Id, homeTile);
        var m = new Membership(orgId, OrganizationRole.Leader, 1f);
        world.Organizations[orgId].Members[founder.Id] = m;
        founder.Memberships.Add(m);
        founder.WithCiv(civ);

        var pilgrim = SpawnAt(world, farTile, 2L);
        pilgrim.WithCiv(civ);
        pilgrim.Skills = pilgrim.Skills with { Piety = 0.9f };
        var pilgrimMembership = new Membership(orgId, OrganizationRole.Member, 0.5f);
        world.Organizations[orgId].Members[pilgrim.Id] = pilgrimMembership;
        pilgrim.Memberships.Add(pilgrimMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        TryFormPilgrimageGoal(phase, pilgrim, world, pending);

        pilgrim.Goals.Should().Contain(g => g.Type == GoalType.Pilgrimage && !g.IsComplete && g.TargetTile == homeTile);
        pending.Should().ContainSingle(e => e.Type == EventType.PilgrimageEmbarked);
    }

    [Fact]
    public void LowPietyCharacter_NeverFormsPilgrimageGoal()
    {
        var (world, homeTile, farTile, civ) = SetupCivWithTwoTiles(seed: 602);
        world.SimConfig.Religion.PilgrimagePietyThreshold = 0.5f;

        var founder = SpawnAt(world, homeTile, 1L);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", founder.Id, homeTile);
        var m = new Membership(orgId, OrganizationRole.Leader, 1f);
        world.Organizations[orgId].Members[founder.Id] = m;
        founder.Memberships.Add(m);

        var lukewarm = SpawnAt(world, farTile, 2L);
        lukewarm.Skills = lukewarm.Skills with { Piety = 0.1f };
        var lukewarmMembership = new Membership(orgId, OrganizationRole.Member, 0.5f);
        world.Organizations[orgId].Members[lukewarm.Id] = lukewarmMembership;
        lukewarm.Memberships.Add(lukewarmMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        TryFormPilgrimageGoal(phase, lukewarm, world, pending);

        lukewarm.Goals.Should().NotContain(g => g.Type == GoalType.Pilgrimage);
        pending.Should().BeEmpty();
    }

    [Fact]
    public void CompletingPilgrimage_BoostsNeedsAndLoyalty_AndSetsCooldown()
    {
        var (world, homeTile, farTile, civ) = SetupCivWithTwoTiles(seed: 603);
        world.SimConfig.Religion.PilgrimageNeedsBoost = 0.2f;
        world.SimConfig.Religion.PilgrimageLoyaltyBoost = 0.15f;

        var founder = SpawnAt(world, homeTile, 1L);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", founder.Id, homeTile);
        var pilgrim = SpawnAt(world, farTile, 2L);
        var pilgrimMembership = new Membership(orgId, OrganizationRole.Member, 0.5f);
        world.Organizations[orgId].Members[pilgrim.Id] = pilgrimMembership;
        pilgrim.Memberships.Add(pilgrimMembership);

        var goal = new GoalData { Type = GoalType.Pilgrimage, TargetTile = homeTile, Priority = 0.6f };
        pilgrim.Goals.Add(goal);
        pilgrim.Location = homeTile; // simulate arrival

        float spiritualBefore = pilgrim.Needs.Spiritual;
        var pending = new List<PendingEvent>();
        CompletePilgrimage(pilgrim, goal, world, pending);

        goal.IsComplete.Should().BeTrue();
        pilgrim.Needs.Spiritual.Should().BeGreaterThan(spiritualBefore);
        pilgrim.LastPilgrimageYear.Should().Be(world.CurrentYear);
        pilgrim.Memberships.Single(mem => mem.OrganizationId == orgId).Loyalty.Should().BeApproximately(0.65f, 0.001f);
        pending.Should().ContainSingle(e => e.Type == EventType.PilgrimageCompleted);
    }
}
