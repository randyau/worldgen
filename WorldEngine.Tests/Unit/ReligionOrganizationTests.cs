using System.Reflection;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M15 15.0 — Religion becomes a real Organization instead of a bare flavor event.
/// See docs/phases/m15_religion_deepened.md.
/// </summary>
public class ReligionOrganizationTests
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

    private static void KillCharacter(CharacterBehaviorPhase phase, Tier1Character c, WorldState world, List<PendingEvent> pending) =>
        typeof(CharacterBehaviorPhase)
            .GetMethod("KillCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { c, world, "test", pending });

    private static void AdvanceFoundReligionGoal(CharacterBehaviorPhase phase, Tier1Character c, WorldState world, List<PendingEvent> pending) =>
        typeof(CharacterBehaviorPhase)
            .GetMethod("AdvanceFoundReligionGoal", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { c, world, pending, 0L });

    // ─── religions.toml content ───────────────────────────────────────────────

    [Fact]
    public void ReligionArchetypeLoader_RealToml_LoadsValidArchetypes()
    {
        var registry = ReligionArchetypeLoader.LoadOrDefault();

        registry.All.Should().NotBeEmpty();
        foreach (var a in registry.All)
        {
            a.DeityNames.Should().NotBeEmpty($"archetype '{a.Id}' must have deity_names");
            a.NameTemplates.Should().NotBeEmpty($"archetype '{a.Id}' must have name_templates");
            a.Tenets.Should().NotBeEmpty($"archetype '{a.Id}' must have tenets");
            a.Zealotry.Should().BeInRange(-1f, 1f, $"archetype '{a.Id}' zealotry should stay within [-1, 1]");
        }
    }

    [Fact]
    public void SelectForFounder_PicksHighestAffinityArchetype()
    {
        var registry = ReligionArchetypeLoader.LoadOrDefault();

        // Strongly aggressive, low compassion founder should land on the war archetype.
        var picked = registry.SelectForFounder(compassion: -1f, aggression: 1f, curiosity: 0f, wonder: 0f, ambition: 0.3f, stability: 0f);

        picked.Should().NotBeNull();
        picked!.Id.Should().Be("war_cult");
    }

    // ─── Founding creates a real Organization ────────────────────────────────

    [Fact]
    public void AdvanceFoundReligionGoal_CreatesOrganization_WithFounderAsLeader()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile = FindLandTile(world);
        var civ = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);

        var founder = SpawnAt(world, tile, 1L);
        founder.Goals.Add(new GoalData
        {
            Type = GoalType.FoundReligion,
            Priority = 0.8f,
            Intensity = 0.9f,
            Progress = 1f, // already complete — next advance call finalizes founding
        });
        founder.Needs = founder.Needs with { Spiritual = 0.9f };

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        AdvanceFoundReligionGoal(phase, founder, world, pending);

        pending.Should().ContainSingle(e => e.Type == EventType.ReligionFounded);

        var religionOrg = world.Organizations.Values.Should().ContainSingle(o => o.Kind == OrganizationKind.Religion)
            .Which;
        religionOrg.LeaderId.Should().Be(founder.Id);
        founder.Memberships.Should().Contain(m => m.OrganizationId == religionOrg.Id && m.Role == OrganizationRole.Leader);
        religionOrg.Members.Should().ContainKey(founder.Id);
        religionOrg.Name.Should().NotBeNullOrWhiteSpace();
    }

    // ─── Leader succession / extinction ──────────────────────────────────────

    [Fact]
    public void ReligionLeaderDeath_TriggersSuccession_ViaSuccessionResolverKernel()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 105);
        var tile = FindLandTile(world);
        var leader = SpawnAt(world, tile, 1L);
        var heir = SpawnAt(world, tile, 2L);

        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", leader.Id, tile);
        var org = world.Organizations[orgId];
        var leaderMembership = new Membership(orgId, OrganizationRole.Leader, 1.0f);
        var heirMembership   = new Membership(orgId, OrganizationRole.Member, 1.0f);
        org.Members[leader.Id] = leaderMembership;
        org.Members[heir.Id]   = heirMembership;
        leader.Memberships.Add(leaderMembership);
        heir.Memberships.Add(heirMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        KillCharacter(phase, leader, world, pending);

        org.LeaderId.Should().Be(heir.Id, "SuccessionResolver.SelectSuccessor must pick the successor unmodified");
        org.IsExtinct.Should().BeFalse();
        pending.Should().Contain(e => e.Type == EventType.ReligiousLeadershipTransferred);
    }

    [Fact]
    public void ReligionLeaderDeath_WithNoOtherLivingMembers_FiresExtinct()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 106);
        var tile = FindLandTile(world);
        var leader = SpawnAt(world, tile, 1L);

        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Lonely Faith", leader.Id, tile);
        var org = world.Organizations[orgId];
        var leaderMembership = new Membership(orgId, OrganizationRole.Leader, 1.0f);
        org.Members[leader.Id] = leaderMembership;
        leader.Memberships.Add(leaderMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        KillCharacter(phase, leader, world, pending);

        org.IsExtinct.Should().BeTrue();
        pending.Should().Contain(e => e.Type == EventType.ReligionExtinct);
        pending.Should().NotContain(e => e.Type == EventType.ReligiousLeadershipTransferred);
    }

    // ─── 15.5 — live religion visibility in the watch snapshot ──────────────

    [Fact]
    public void WatchSnapshot_ForReligionLeader_ShowsLeaderRole()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 107);
        var tile = FindLandTile(world);
        var leader = SpawnAt(world, tile, 1L);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Watched Faith", leader.Id, tile);
        var membership = new Membership(orgId, OrganizationRole.Leader, 1f);
        world.Organizations[orgId].Members[leader.Id] = membership;
        leader.Memberships.Add(membership);

        world.WatchedEntityId   = leader.Id;
        world.WatchedEntityKind = EntityKind.Tier1Character;

        var builder = new WorldEngine.Sim.World.SnapshotBuilder();
        var snap = builder.Build(world, WorldEngine.Sim.Core.OverlayType.Biome,
            WorldEngine.Sim.Core.SimSpeed.Normal, paused: false, ticksPerSecond: 4,
            recentEvents: Array.Empty<WorldEngine.Sim.World.SimEvent>());

        snap.WatchedCharacter.Should().NotBeNull();
        snap.WatchedCharacter!.ReligionName.Should().Be("Watched Faith");
        snap.WatchedCharacter!.ReligionRole.Should().Be("Leader");
    }

    [Fact]
    public void WatchSnapshot_ForUnaffiliatedCharacter_ShowsNoReligion()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 108);
        var tile = FindLandTile(world);
        var agnostic = SpawnAt(world, tile, 1L);

        world.WatchedEntityId   = agnostic.Id;
        world.WatchedEntityKind = EntityKind.Tier1Character;

        var builder = new WorldEngine.Sim.World.SnapshotBuilder();
        var snap = builder.Build(world, WorldEngine.Sim.Core.OverlayType.Biome,
            WorldEngine.Sim.Core.SimSpeed.Normal, paused: false, ticksPerSecond: 4,
            recentEvents: Array.Empty<WorldEngine.Sim.World.SimEvent>());

        snap.WatchedCharacter.Should().NotBeNull();
        snap.WatchedCharacter!.ReligionName.Should().BeEmpty();
    }
}
