using System.Linq;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M13 13.0: marriage, household Family Organization, and real childbirth with trait
/// inheritance — the first mechanic to actually populate IdentityData.MotherId/FatherId
/// and RelationshipFlags.IsMarried, both previously-dead schema fields (see
/// docs/phases/m13_generational_domestic_drama.md kickoff scope note).
/// </summary>
public class FamilyFormationTests
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

    private static (Tier1Character a, Tier1Character b) SpawnAdultPair(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var a = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 1L + seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        var b = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 2L + seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        a.AgeSeason = world.SimConfig.Family.MarriageMinAgeSeasons + 10;
        b.AgeSeason = world.SimConfig.Family.MarriageMinAgeSeasons + 10;
        world.Entities.Add(a);
        world.Entities.Add(b);
        return (a, b);
    }

    [Fact]
    public void ProposeMarriage_CreatesHouseholdFamilyOrganization()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 7);
        var tile = FindLandTile(world);
        var (a, b) = SpawnAdultPair(world, tile, seedOffset: 0L);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(a.Id, b.Id), world, pending);

        var rel = world.GetRelationship(a.Id, b.Id);
        rel.Should().NotBeNull();
        rel!.IsMarried.Should().BeTrue();
        rel.IsFamily.Should().BeTrue();

        var aFamilyMemberships = a.Memberships.Where(IsFamilyOrg).ToList();
        var bFamilyMemberships = b.Memberships.Where(IsFamilyOrg).ToList();
        aFamilyMemberships.Should().ContainSingle();
        bFamilyMemberships.Should().ContainSingle();
        var aFamily = aFamilyMemberships[0];
        var bFamily = bFamilyMemberships[0];
        aFamily.OrganizationId.Should().Be(bFamily.OrganizationId, "spouses share one household Organization");

        bool IsFamilyOrg(Membership m) => world.GetOrganization(m.OrganizationId)?.Kind == OrganizationKind.Family;

        var org = world.GetOrganization(aFamily.OrganizationId)!;
        org.Members.Keys.Should().Contain(new[] { a.Id, b.Id });
    }

    [Fact]
    public void ProposeMarriage_BelowMinAge_DoesNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 7);
        var tile = FindLandTile(world);
        var (a, b) = SpawnAdultPair(world, tile, seedOffset: 10L);
        a.AgeSeason = 1; // well below MarriageMinAgeSeasons

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(a.Id, b.Id), world, pending);

        // GetOrCreate (mirroring ResolveAlly's own ordering) creates a neutral edge as a side
        // effect even when the age gate rejects the marriage — assert on IsMarried, not nullity.
        (world.GetRelationship(a.Id, b.Id)?.IsMarried ?? false).Should().BeFalse(
            "marriage below minimum age must not proceed");
        a.Memberships.Should().BeEmpty();
    }

    [Fact]
    public void MarriedCouple_AnnualTick_CanBearAChildWithRealParentLinkage()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 11);
        var tile = FindLandTile(world);
        var (a, b) = SpawnAdultPair(world, tile, seedOffset: 20L);

        var marriagePending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(a.Id, b.Id), world, marriagePending);

        // Force the childbirth roll to always succeed for this test.
        world.SimConfig.Family.ChildbirthChancePerYear = 1.0f;

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.Execute(world, world.CurrentTick, isAnnualTick: true);

        var child = world.Entities.Characters.FirstOrDefault(c =>
            c.Identity.MotherId == a.Id || c.Identity.MotherId == b.Id);
        child.Should().NotBeNull("a married, co-located couple with a forced childbirth chance should produce a child");

        var (mother, father) = child!.Identity.MotherId == a.Id ? (a, b) : (b, a);
        child.Identity.MotherId.Should().Be(mother.Id);
        child.Identity.FatherId.Should().Be(father.Id);
        child.Identity.AncestryId.Should().Be(mother.Identity.AncestryId);

        // Child's personality should sit within the span the ancestry-biased roll and pure
        // parent-average could produce (blend is a weighted mix of the two, never outside it).
        float parentAvgCompassion = (mother.Personality.Compassion + father.Personality.Compassion) / 2f;
        child.Personality.Compassion.Should().BeInRange(
            System.Math.Min(0.1f, parentAvgCompassion) - 0.01f,
            System.Math.Max(0.9f, parentAvgCompassion) + 0.01f);

        var childFamilyMemberships = child.Memberships.Where(IsFamilyOrg).ToList();
        childFamilyMemberships.Should().ContainSingle();
        var childFamilyMembership = childFamilyMemberships[0];
        var motherFamilyMembership = mother.Memberships.First(IsFamilyOrg);

        bool IsFamilyOrg(Membership m) => world.GetOrganization(m.OrganizationId)?.Kind == OrganizationKind.Family;
        childFamilyMembership.OrganizationId.Should().Be(motherFamilyMembership.OrganizationId,
            "the newborn joins the same household Organization as its parents");
    }
}
