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
/// M13 13.4: non-ruler bonds reach the wider world — before this, only the ruler's personal
/// RelationshipEdge ever escaped the character layer (reused verbatim as civ diplomacy). A
/// trusted confidant can now credit toward emissary-purpose selection (ConfidantTrustCredit), a
/// cross-civ friendship dampens border-tension accrual between the two civs (FriendshipDampening),
/// and a character in personal crisis can seek asylum with a co-located foreign confidant (Defect).
/// See roadmap.md's 2026-07-30 relationship-system audit and proposal #4.
/// </summary>
public class NonRulerBondsTests
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

    private static (Tier1Character ruler, CivId civId) SpawnRuler(WorldState world, TileCoord tile, long seedOffset, string civName)
    {
        var ruler = SpawnAt(world, tile, seedOffset);
        var civId = new CivId(world.NextCivId++);
        world.Civilizations[civId] = new Civilization(civId, civName, ruler.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(ruler, civId, OrganizationRole.Leader, world);
        return (ruler, civId);
    }

    private static Tier1Character SpawnMember(WorldState world, TileCoord tile, long seedOffset, CivId civId)
    {
        var c = SpawnAt(world, tile, seedOffset);
        CivTracker.SetCharacterCiv(c, civId, OrganizationRole.Member, world);
        return c;
    }

    // ─── FriendshipDampening ────────────────────────────────────────────────

    private static float InvokeFriendshipDampening(Civilization from, Civilization to, WorldState world)
    {
        var method = typeof(CivTracker).GetMethod("FriendshipDampening", BindingFlags.NonPublic | BindingFlags.Static);
        return (float)method!.Invoke(null, new object[] { from, to, world, world.SimConfig.War })!;
    }

    [Fact]
    public void FriendshipDampening_NoCrossCivFriendship_ReturnsFullMultiplier()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 81);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 1L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 2L, "CivB");

        InvokeFriendshipDampening(world.Civilizations[civA], world.Civilizations[civB], world)
            .Should().Be(1f);
    }

    [Fact]
    public void FriendshipDampening_StrongCrossCivFriendship_DampensProportionallyToTrust()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 82);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 11L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 12L, "CivB");
        var memberA = SpawnMember(world, tile, 13L, civA);
        var memberB = SpawnMember(world, tile, 14L, civB);

        float trust = 0.8f;
        var rel = world.Relationships.GetOrCreate(memberA.Id, memberB.Id);
        world.Relationships.Upsert(rel with { Trust = trust });

        float dampenMin = world.SimConfig.War.FriendshipWarDampenMin;
        float result = InvokeFriendshipDampening(world.Civilizations[civA], world.Civilizations[civB], world);
        result.Should().BeLessThan(1f, "a strong cross-civ friendship should dampen tension accrual");
        result.Should().BeApproximately(1f - trust * (1f - dampenMin), 0.001f);
    }

    // ─── ConfidantTrustCredit ───────────────────────────────────────────────

    private static float InvokeConfidantTrustCredit(Civilization civ, CivId targetCivId, WorldState world)
    {
        var method = typeof(CivTracker).GetMethod("ConfidantTrustCredit", BindingFlags.NonPublic | BindingFlags.Static);
        return (float)method!.Invoke(null, new object[] { civ, targetCivId, world, world.SimConfig.Emissary })!;
    }

    [Fact]
    public void ConfidantTrustCredit_NoNonRulerFriendship_ReturnsZero()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 83);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 21L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 22L, "CivB");

        InvokeConfidantTrustCredit(world.Civilizations[civA], civB, world).Should().Be(0f);
    }

    [Fact]
    public void ConfidantTrustCredit_StrongNonRulerFriendship_CreditsScaledTrust()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 84);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 31L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 32L, "CivB");
        var memberA = SpawnMember(world, tile, 33L, civA);
        var memberB = SpawnMember(world, tile, 34L, civB);

        float trust = 0.9f;
        var rel = world.Relationships.GetOrCreate(memberA.Id, memberB.Id);
        world.Relationships.Upsert(rel with { Trust = trust });

        float credit = InvokeConfidantTrustCredit(world.Civilizations[civA], civB, world);
        credit.Should().BeApproximately(trust * world.SimConfig.Emissary.ConfidantTrustCredit, 0.001f);
    }

    // ─── Defect ─────────────────────────────────────────────────────────────

    [Fact]
    public void Defect_DistressedNonRulerWithTrustedConfidant_JoinsConfidantsCiv()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 85);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 41L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 42L, "CivB");
        var member = SpawnMember(world, tile, 43L, civA);
        var confidant = SpawnMember(world, tile, 44L, civB);

        var rel = world.Relationships.GetOrCreate(member.Id, confidant.Id);
        world.Relationships.Upsert(rel with { Trust = world.SimConfig.Defection.ConfidantTrustThreshold + 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Defect(member.Id, confidant.Id), world, pending);

        member.CivId.Should().Be(civB);
        pending.Should().ContainSingle(e => e.Type == EventType.CharacterDefected);
    }

    [Fact]
    public void Defect_Ruler_DoesNotDefect()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 86);
        var tile = FindLandTile(world);
        var (rulerA, civA) = SpawnRuler(world, tile, 51L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 52L, "CivB");
        var confidant = SpawnMember(world, tile, 53L, civB);

        var rel = world.Relationships.GetOrCreate(rulerA.Id, confidant.Id);
        world.Relationships.Upsert(rel with { Trust = world.SimConfig.Defection.ConfidantTrustThreshold + 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Defect(rulerA.Id, confidant.Id), world, pending);

        rulerA.CivId.Should().Be(civA, "a ruler cannot abandon their own civ mid-command");
        pending.Should().BeEmpty();
    }

    [Fact]
    public void Defect_InsufficientTrust_DoesNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 87);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 61L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 62L, "CivB");
        var member = SpawnMember(world, tile, 63L, civA);
        var confidant = SpawnMember(world, tile, 64L, civB);

        var rel = world.Relationships.GetOrCreate(member.Id, confidant.Id);
        world.Relationships.Upsert(rel with { Trust = world.SimConfig.Defection.ConfidantTrustThreshold - 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Defect(member.Id, confidant.Id), world, pending);

        member.CivId.Should().Be(civA);
        pending.Should().BeEmpty();
    }

    [Fact]
    public void Defect_CivsAtWar_DoesNotDefect()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 88);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 71L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 72L, "CivB");
        var member = SpawnMember(world, tile, 73L, civA);
        var confidant = SpawnMember(world, tile, 74L, civB);
        world.Civilizations[civA].WarsAgainst[civB] = world.CurrentYear;

        var rel = world.Relationships.GetOrCreate(member.Id, confidant.Id);
        world.Relationships.Upsert(rel with { Trust = world.SimConfig.Defection.ConfidantTrustThreshold + 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Defect(member.Id, confidant.Id), world, pending);

        member.CivId.Should().Be(civA, "asylum, not treason — can't defect to an enemy civ mid-war");
        pending.Should().BeEmpty();
    }
}
