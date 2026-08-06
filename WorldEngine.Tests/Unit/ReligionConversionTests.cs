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

/// <summary>
/// M15 15.1 — religion conversion via exposure. See docs/phases/m15_religion_deepened.md
/// "Long-run balance constraints".
/// </summary>
public class ReligionConversionTests
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

    /// <summary>Builds a character with an explicit PersonalityVector — Tier1Character.Personality
    /// has no setter, so a custom-receptivity test character must be constructed directly rather
    /// than mutated after CharacterFactory.Spawn.</summary>
    private static Tier1Character SpawnWithPersonality(
        WorldState world, TileCoord tile, long id, float piety, float wonder, float curiosity, float rationality)
    {
        var personality = new PersonalityVector(
            Ambition: 0.5f, Greed: 0.5f, Aggression: 0.5f, Compassion: 0.5f,
            Curiosity: curiosity, Creativity: 0.5f, Rationality: rationality, Wonder: wonder,
            Loyalty: 0.5f, Sociability: 0.5f, Honesty: 0.5f, Stability: 0.5f);
        var skills = SkillVector.Default with { Piety = piety };
        var identity = new IdentityData("Seeker", "the Test", "test", null, null, 0, 0);
        var c = new Tier1Character(new EntityId(id), tile, personality, AptitudeVector.Default,
            skills, identity, 100, 400);
        world.Entities.Add(c);
        return c;
    }

    private static List<PendingEvent> RunConversion(CharacterBehaviorPhase phase, List<Tier1Character> chars, WorldState world)
    {
        var pending = new List<PendingEvent>();
        typeof(CharacterBehaviorPhase)
            .GetMethod("ProcessAnnualReligionConversion", BindingFlags.NonPublic | BindingFlags.Instance)!
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


    [Fact]
    public void LowReceptivityCharacter_NeverConverts_EvenWithMaximalPullScale()
    {
        var (world, tile, civ) = SetupCiv(seed: 201);
        world.SimConfig.Religion.ConversionBasePullScale = 1000f; // would force conversion if the gate didn't hold first

        var founder = SpawnAt(world, tile, 1L);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", founder.Id, tile);
        var org = world.Organizations[orgId];
        var founderMembership = new Membership(orgId, OrganizationRole.Leader, 1f);
        org.Members[founder.Id] = founderMembership;
        founder.Memberships.Add(founderMembership);
        founder.WithCiv(civ);

        var skeptic = SpawnWithPersonality(world, tile, 2L, piety: 0f, wonder: 0f, curiosity: 0f, rationality: 1f); // receptivity clamps to 0
        skeptic.WithCiv(civ);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = RunConversion(phase, new List<Tier1Character> { founder, skeptic }, world);

        pending.Should().BeEmpty("a character whose receptivity gate is 0 must never convert, regardless of pull scale");
        skeptic.Memberships.Should().NotContain(m => m.OrganizationId == orgId);
    }

    [Fact]
    public void HighReceptivityUnaffiliatedCharacter_ConvertsToPresentReligion_WhenPullSaturated()
    {
        var (world, tile, civ) = SetupCiv(seed: 202);
        world.SimConfig.Religion.ConversionBasePullScale = 1000f; // saturate pull so the roll always succeeds

        var founder = SpawnAt(world, tile, 1L);
        var orgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Test Faith", founder.Id, tile);
        var org = world.Organizations[orgId];
        var founderMembership = new Membership(orgId, OrganizationRole.Leader, 1f);
        org.Members[founder.Id] = founderMembership;
        founder.Memberships.Add(founderMembership);
        founder.WithCiv(civ);

        var seeker = SpawnWithPersonality(world, tile, 2L, piety: 1f, wonder: 1f, curiosity: 1f, rationality: 0f); // maximal receptivity
        seeker.WithCiv(civ);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = RunConversion(phase, new List<Tier1Character> { founder, seeker }, world);

        pending.Should().ContainSingle(e => e.Type == EventType.CharacterConvertedReligion);
        seeker.Memberships.Should().Contain(m => m.OrganizationId == orgId);
        org.Members.Should().ContainKey(seeker.Id);
    }

    [Fact]
    public void ExistingHighLoyalty_ResistsConversion_ThatLowLoyaltyWouldAccept()
    {
        // Two independent worlds with identical seed/character-id/year so the conversion roll
        // value is identical between them — only Loyalty differs, isolating its effect.
        (WorldState world, Tier1Character mover, OrganizationId oldOrgId) BuildScenario(float existingLoyalty)
        {
            var (world, tile, civ) = SetupCiv(seed: 303);

            var oldFounder = SpawnAt(world, tile, 1L);
            var oldOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "Old Faith", oldFounder.Id, tile);
            var oldOrg = world.Organizations[oldOrgId];
            var oldFounderM = new Membership(oldOrgId, OrganizationRole.Leader, 1f);
            oldOrg.Members[oldFounder.Id] = oldFounderM;
            oldFounder.Memberships.Add(oldFounderM);
            oldFounder.WithCiv(civ);

            var newFounder = SpawnAt(world, tile, 2L);
            var newOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Religion, "New Faith", newFounder.Id, tile);
            var newOrg = world.Organizations[newOrgId];
            var newFounderM = new Membership(newOrgId, OrganizationRole.Leader, 1f);
            newOrg.Members[newFounder.Id] = newFounderM;
            newFounder.Memberships.Add(newFounderM);
            newFounder.WithCiv(civ);

            var mover = SpawnWithPersonality(world, tile, 3L, piety: 1f, wonder: 1f, curiosity: 1f, rationality: 0f);
            mover.WithCiv(civ);
            var moverMembership = new Membership(oldOrgId, OrganizationRole.Member, existingLoyalty);
            oldOrg.Members[mover.Id] = moverMembership;
            mover.Memberships.Add(moverMembership);

            return (world, mover, oldOrgId);
        }

        var (lowLoyaltyWorld, lowLoyaltyMover, lowOldOrgId) = BuildScenario(existingLoyalty: 0f);
        var (highLoyaltyWorld, highLoyaltyMover, highOldOrgId) = BuildScenario(existingLoyalty: 1f);

        // Derive the exact roll this scenario produces (same worldSeed/mover id/year in both
        // worlds) and pick a pull scale that straddles it: pull_low (resistance factor 1, from
        // zero Loyalty) lands just above the roll; pull_high (resistance factor 0.4, from Loyalty
        // 1 at ExistingLoyaltyResistance=0.6) lands below it. presence=1/3, receptivity=0.75,
        // zealBonus=1 for these hand-built orgs (no archetype set) — pull = (1/3)*0.75*scale.
        float roll = WorldEngine.Sim.Core.WorldRng.FloatAt(
            lowLoyaltyWorld.WorldSeed, lowLoyaltyWorld.CurrentYear,
            (int)(lowLoyaltyMover.Id.Value & 0x7FFFFFFF), 0,
            WorldEngine.Sim.Simulation.SimRngSalts.ReligionConversionRoll);
        float scale = (roll + 0.01f) / 0.25f;
        lowLoyaltyWorld.SimConfig.Religion.ConversionBasePullScale  = scale;
        highLoyaltyWorld.SimConfig.Religion.ConversionBasePullScale = scale;

        var phase1 = new CharacterBehaviorPhase(lowLoyaltyWorld.SimConfig);
        RunConversion(phase1, lowLoyaltyWorld.Entities.Characters.Cast<Tier1Character>().ToList(), lowLoyaltyWorld);

        var phase2 = new CharacterBehaviorPhase(highLoyaltyWorld.SimConfig);
        RunConversion(phase2, highLoyaltyWorld.Entities.Characters.Cast<Tier1Character>().ToList(), highLoyaltyWorld);

        var lowReligionMembership  = lowLoyaltyMover.Memberships.First(m => m.OrganizationId != OrganizationId.None);
        var highReligionMembership = highLoyaltyMover.Memberships.First(m => m.OrganizationId != OrganizationId.None);
        bool lowConverted  = lowReligionMembership.OrganizationId != lowOldOrgId;
        bool highConverted = highReligionMembership.OrganizationId != highOldOrgId;

        // Weaker existing-religion resistance (Loyalty 0) can only make conversion at least as
        // likely as stronger resistance (Loyalty 1) for an identical underlying roll.
        if (highConverted) lowConverted.Should().BeTrue();
        lowConverted.Should().BeTrue("with zero existing Loyalty and maximal receptivity/presence, conversion should succeed");
        highConverted.Should().BeFalse("full existing Loyalty should suppress the same pull that zero Loyalty accepts");
    }
}
