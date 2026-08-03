using System.Reflection;
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
/// M13 13.6: the same-civ Trust economy. Cross-civ Tier1 contact always had both a source
/// (first-meeting/AllyWith) and a sink (cultural distance/personality mismatch/territorial
/// pressure); ordinary same-civ pairs had neither, so Trust sat frozen at 0 forever unless an
/// explicit command touched it — and those commands themselves mostly required Trust already
/// being at a level nothing built (a chicken-and-egg dead end found during the M13 balance pass).
/// <see cref="CharacterBehaviorPhase.ApplySameCivFamiliarity"/> is the general per-tick source
/// (warmth) and sink (clash) for co-located same-civ pairs; the marriage-specific hardship sink
/// and childbirth source layer on top of it for Estrangement specifically.
/// </summary>
public class SameCivTrustEconomyTests
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

    private static void InvokeApplySameCivFamiliarity(WorldState world, Tier1Character c)
    {
        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var method = typeof(CharacterBehaviorPhase).GetMethod("ApplySameCivFamiliarity", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(phase, new object[] { c, world });
    }

    [Fact]
    public void CoLocatedSameCivPair_TrustMovesFromZero()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 101);
        var tile = FindLandTile(world);
        var (_, civId) = SpawnRuler(world, tile, 1L, "CivA");
        var a = SpawnMember(world, tile, 2L, civId);
        var b = SpawnMember(world, tile, 3L, civId);

        world.Relationships.Get(a.Id, b.Id).Should().BeNull("sanity check: no edge exists yet");

        InvokeApplySameCivFamiliarity(world, a);

        var rel = world.Relationships.Get(a.Id, b.Id);
        rel.Should().NotBeNull("a co-located same-civ pair should now get an edge with real movement");
        rel!.Trust.Should().NotBe(0f, "same-civ Trust must no longer sit frozen at its default");
    }

    [Fact]
    public void CrossCivPair_UnaffectedBySameCivFamiliarity()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 102);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 11L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 12L, "CivB");
        var a = SpawnMember(world, tile, 13L, civA);
        var b = SpawnMember(world, tile, 14L, civB);

        InvokeApplySameCivFamiliarity(world, a);

        world.Relationships.Get(a.Id, b.Id).Should().BeNull(
            "ApplySameCivFamiliarity must not touch cross-civ pairs — that's ApplyPassiveDrains' job");
    }

    [Fact]
    public void FeudPair_TrustUnaffected()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 103);
        var tile = FindLandTile(world);
        var (_, civId) = SpawnRuler(world, tile, 21L, "CivA");
        var a = SpawnMember(world, tile, 22L, civId);
        var b = SpawnMember(world, tile, 23L, civId);

        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with { Trust = -0.6f, Flags = RelationshipFlags.IsRival | RelationshipFlags.IsFeud });

        InvokeApplySameCivFamiliarity(world, a);

        world.Relationships.Get(a.Id, b.Id)!.Trust.Should().Be(-0.6f,
            "a fully-escalated Feud is only resolved by Reconciliation, not ambient companionship drift");
    }

    [Fact]
    public void MarriedCouple_Hardship_DrainsTrustAndCanEstrange()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 104);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 31L);
        var b = SpawnAt(world, tile, 32L);

        var famCfg = world.SimConfig.Family;
        a.Needs = a.Needs with { Food = famCfg.MarriageHardshipNeedThreshold - 0.1f };
        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with
        {
            Trust = famCfg.EstrangementTrustThreshold + famCfg.MarriageHardshipTrustDrain - 0.01f,
            Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily
        });

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var method = typeof(CharacterBehaviorPhase).GetMethod("CheckMarriageEstrangement", BindingFlags.NonPublic | BindingFlags.Instance);
        var pending = new List<PendingEvent>();
        method!.Invoke(phase, new object[] { world, pending });

        var after = world.Relationships.Get(a.Id, b.Id)!;
        after.IsMarried.Should().BeFalse("hardship drain pushed Trust past the Estrangement threshold this year");
        pending.Should().ContainSingle(e => e.Type == EventType.CharacterEstranged);
    }

    [Fact]
    public void MarriedCouple_NoHardship_TrustUnaffectedByHardshipSink()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 105);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 41L);
        var b = SpawnAt(world, tile, 42L);

        var famCfg = world.SimConfig.Family;
        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with { Trust = 0.5f, Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily });

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var method = typeof(CharacterBehaviorPhase).GetMethod("CheckMarriageEstrangement", BindingFlags.NonPublic | BindingFlags.Instance);
        var pending = new List<PendingEvent>();
        method!.Invoke(phase, new object[] { world, pending });

        world.Relationships.Get(a.Id, b.Id)!.Trust.Should().Be(0.5f,
            "with both spouses' needs healthy, the hardship sink must not fire");
    }
}
