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
    private static void InvokeApplySameCivFamiliarity(WorldState world, Tier1Character c)
    {
        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.ApplySameCivFamiliarity(c, world);
    }

    [Fact]
    public void CoLocatedSameCivPair_TrustMovesFromZero()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 101);
        var tile = WorldGenTestHelpers.FindLandTile(world);
        var (_, civId) = WorldGenTestHelpers.SpawnRuler(world, tile, 1L, "CivA");
        var a = WorldGenTestHelpers.SpawnMember(world, tile, 2L, civId);
        var b = WorldGenTestHelpers.SpawnMember(world, tile, 3L, civId);

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
        var tile = WorldGenTestHelpers.FindLandTile(world);
        var (_, civA) = WorldGenTestHelpers.SpawnRuler(world, tile, 11L, "CivA");
        var (_, civB) = WorldGenTestHelpers.SpawnRuler(world, tile, 12L, "CivB");
        var a = WorldGenTestHelpers.SpawnMember(world, tile, 13L, civA);
        var b = WorldGenTestHelpers.SpawnMember(world, tile, 14L, civB);

        InvokeApplySameCivFamiliarity(world, a);

        world.Relationships.Get(a.Id, b.Id).Should().BeNull(
            "ApplySameCivFamiliarity must not touch cross-civ pairs — that's ApplyPassiveDrains' job");
    }

    [Fact]
    public void FeudPair_TrustUnaffected()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 103);
        var tile = WorldGenTestHelpers.FindLandTile(world);
        var (_, civId) = WorldGenTestHelpers.SpawnRuler(world, tile, 21L, "CivA");
        var a = WorldGenTestHelpers.SpawnMember(world, tile, 22L, civId);
        var b = WorldGenTestHelpers.SpawnMember(world, tile, 23L, civId);

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
        var tile = WorldGenTestHelpers.FindLandTile(world);
        var a = WorldGenTestHelpers.SpawnAt(world, tile, 31L);
        var b = WorldGenTestHelpers.SpawnAt(world, tile, 32L);

        var famCfg = world.SimConfig.Family;
        a.Needs = a.Needs with { Food = famCfg.MarriageHardshipNeedThreshold - 0.1f };
        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with
        {
            Trust = famCfg.EstrangementTrustThreshold + famCfg.MarriageHardshipTrustDrain - 0.01f,
            Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily
        });

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        phase.CheckMarriageEstrangement(world, pending);

        var after = world.Relationships.Get(a.Id, b.Id)!;
        after.IsMarried.Should().BeFalse("hardship drain pushed Trust past the Estrangement threshold this year");
        pending.Should().ContainSingle(e => e.Type == EventType.CharacterEstranged);
    }

    [Fact]
    public void MarriedCouple_NoHardship_TrustUnaffectedByHardshipSink()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 105);
        var tile = WorldGenTestHelpers.FindLandTile(world);
        var a = WorldGenTestHelpers.SpawnAt(world, tile, 41L);
        var b = WorldGenTestHelpers.SpawnAt(world, tile, 42L);

        var famCfg = world.SimConfig.Family;
        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with { Trust = 0.5f, Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily });

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        phase.CheckMarriageEstrangement(world, pending);

        world.Relationships.Get(a.Id, b.Id)!.Trust.Should().Be(0.5f,
            "with both spouses' needs healthy, the hardship sink must not fire");
    }
}
