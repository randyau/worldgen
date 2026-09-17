using System.Reflection;
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

/// <summary>M15 15.3 — heresy & persecution (soft consequences only). See
/// docs/phases/archive/m15_religion_deepened.md.</summary>
public class ReligionPersecutionTests
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

    private static List<PendingEvent> RunPersecution(CharacterBehaviorPhase phase, List<Tier1Character> chars, WorldState world)
    {
        var pending = new List<PendingEvent>();
        typeof(CharacterBehaviorPhase)
            .GetMethod("ProcessAnnualReligionPersecution", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { chars, world, pending });
        return pending;
    }

    private static (WorldState world, TileCoord tile, CivId civ) SetupCiv(int seed)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: seed);
        var tile  = FindLandTile(world);
        var civ   = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        return (world, tile, civ);
    }

    private static Organization MakeReligion(WorldState world, TileCoord tile, string archetypeId, long leaderId)
    {
        var leader = SpawnAt(world, tile, leaderId);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, $"Faith {archetypeId}", leader.Id, tile);
        var org = world.Organizations[orgId];
        org.ReligionArchetypeId = archetypeId;
        var m = new Membership(orgId, OrganizationRole.Leader, 1f);
        org.Members[leader.Id] = m;
        leader.Memberships.Add(m);
        return org;
    }

    [Fact]
    public void TolerantStateReligion_NeverPersecutes()
    {
        // compassion_cult has negative Zealotry — must never persecute regardless of chance scale.
        var (world, tile, civ) = SetupCiv(seed: 501);
        world.SimConfig.Religion.PersecutionBaseChance = 1000f;
        world.SimConfig.Religion.HeresyStateReligionMinShare = 0f;

        var stateOrg = MakeReligion(world, tile, "compassion_cult", 1L);
        var stateLeader = (Tier1Character)world.GetEntity(stateOrg.LeaderId)!;
        stateLeader.WithCiv(civ);

        var heretic = SpawnAt(world, tile, 10L);
        heretic.WithCiv(civ);
        var heresyOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Minority Faith", heretic.Id, tile);
        var heresyOrg = world.Organizations[heresyOrgId];
        heresyOrg.ReligionArchetypeId = "war_cult";
        var hereticMembership = new Membership(heresyOrgId, OrganizationRole.Leader, 0.5f);
        heresyOrg.Members[heretic.Id] = hereticMembership;
        heretic.Memberships.Add(hereticMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var chars = world.Entities.Characters.Cast<Tier1Character>().ToList();
        var pending = RunPersecution(phase, chars, world);

        pending.Should().BeEmpty("compassion_cult's negative Zealotry must gate out persecution entirely");
    }

    [Fact]
    public void ZealousStateReligion_PersecutesMinorityHeretic()
    {
        var (world, tile, civ) = SetupCiv(seed: 502);
        world.SimConfig.Religion.PersecutionBaseChance = 1000f; // saturate the hit roll
        world.SimConfig.Religion.HeresyStateReligionMinShare = 0f;
        world.SimConfig.Religion.PersecutionMinZealotry = 0.1f;

        var stateOrg = MakeReligion(world, tile, "war_cult", 1L); // Zealotry 0.8, well above threshold
        var stateLeader = (Tier1Character)world.GetEntity(stateOrg.LeaderId)!;
        stateLeader.WithCiv(civ);

        var heretic = SpawnAt(world, tile, 10L);
        heretic.WithCiv(civ);
        var heresyOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Minority Faith", heretic.Id, tile);
        var heresyOrg = world.Organizations[heresyOrgId];
        heresyOrg.ReligionArchetypeId = "compassion_cult";
        var hereticMembership = new Membership(heresyOrgId, OrganizationRole.Leader, 0.5f);
        heresyOrg.Members[heretic.Id] = hereticMembership;
        heretic.Memberships.Add(hereticMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var chars = world.Entities.Characters.Cast<Tier1Character>().ToList();
        var pending = RunPersecution(phase, chars, world);

        pending.Should().ContainSingle(e => e.Type == EventType.PersecutionOccurred);

        // Either outcome (forced_conversion or resisted) is a valid soft consequence; verify the
        // heretic ends up either in the state religion, or penalized while remaining a heretic —
        // never removed from the simulation, never a violent/civil-war event.
        bool inStateReligion = heretic.Memberships.Any(m => m.OrganizationId == stateOrg.Id);
        bool stillHeretic = heretic.Memberships.Any(m => m.OrganizationId == heresyOrgId);
        (inStateReligion || stillHeretic).Should().BeTrue();
        heretic.IsAlive.Should().BeTrue("persecution must never kill — soft consequences only");
    }

    [Fact]
    public void FragmentedReligiousPopulation_BelowShareThreshold_HasNoStateReligion()
    {
        var (world, tile, civ) = SetupCiv(seed: 503);
        world.SimConfig.Religion.PersecutionBaseChance = 1000f;
        world.SimConfig.Religion.HeresyStateReligionMinShare = 0.9f; // near-unanimous required

        var orgA = MakeReligion(world, tile, "war_cult", 1L);
        var leaderA = (Tier1Character)world.GetEntity(orgA.LeaderId)!;
        leaderA.WithCiv(civ);

        var orgBId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Rival Faith", new EntityId(20), tile);
        var leaderB = SpawnAt(world, tile, 20L);
        var orgB = world.Organizations[orgBId];
        orgB.ReligionArchetypeId = "war_cult";
        var mB = new Membership(orgBId, OrganizationRole.Leader, 1f);
        orgB.Members[leaderB.Id] = mB;
        leaderB.Memberships.Add(mB);
        leaderB.WithCiv(civ);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var chars = world.Entities.Characters.Cast<Tier1Character>().ToList();
        var pending = RunPersecution(phase, chars, world);

        pending.Should().BeEmpty("a 50/50 split between two religions of the same civ never reaches a 0.9 plurality share");
    }
}
