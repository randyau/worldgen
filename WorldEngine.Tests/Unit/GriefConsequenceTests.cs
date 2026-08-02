using System.Linq;
using System.Text.Json;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M13 13.3: grief is no longer uniform across relationship types — before this, GoalManager's
/// Bond→Grieve pipeline was the only behavioral consequence of any bond, but it wasn't gated by
/// IsFamily/IsMarried at all (a spouse and a co-located trusted stranger grieved identically); see
/// roadmap.md's 2026-07-30 relationship-system audit.
/// </summary>
public class GriefConsequenceTests
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

    private static void GiveBond(Tier1Character mourner, EntityId targetId, float intensity)
    {
        mourner.Goals.Add(new GoalData
        {
            Type = GoalType.Bond, Object = GoalObject.Person,
            TargetEntityId = targetId, Intensity = intensity, Priority = intensity,
        });
    }

    [Fact]
    public void Grief_SpouseDeath_ScalesAboveStrangerBaseline()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 61);
        var tile = FindLandTile(world);
        var mourner  = SpawnAt(world, tile, 1L);
        var deceased = SpawnAt(world, tile, 2L);
        GiveBond(mourner, deceased.Id, 0.5f);

        var rel = world.Relationships.GetOrCreate(mourner.Id, deceased.Id);
        world.Relationships.Upsert(rel with { Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily });

        var mourners = new List<(EntityId, float)>();
        var pending = new List<PendingEvent>();
        GoalManager.ApplyGriefToMourners(deceased.Id, deceased.Identity.Name, world, world.SimConfig.Character, mourners, pending);

        var grief = mourner.Goals.Single(g => g.Type == GoalType.Grieve);
        grief.Intensity.Should().BeApproximately(0.5f * world.SimConfig.Character.GriefSpouseMultiplier, 0.001f);
    }

    [Fact]
    public void Grief_FamilyDeath_ScalesBetweenStrangerAndSpouse()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 62);
        var tile = FindLandTile(world);
        var mourner  = SpawnAt(world, tile, 11L);
        var deceased = SpawnAt(world, tile, 12L);
        GiveBond(mourner, deceased.Id, 0.5f);

        var rel = world.Relationships.GetOrCreate(mourner.Id, deceased.Id);
        world.Relationships.Upsert(rel with { Flags = RelationshipFlags.IsFamily }); // family, not spouse

        var mourners = new List<(EntityId, float)>();
        var pending = new List<PendingEvent>();
        GoalManager.ApplyGriefToMourners(deceased.Id, deceased.Identity.Name, world, world.SimConfig.Character, mourners, pending);

        var cfg = world.SimConfig.Character;
        var grief = mourner.Goals.Single(g => g.Type == GoalType.Grieve);
        grief.Intensity.Should().BeApproximately(0.5f * cfg.GriefFamilyMultiplier, 0.001f);
        (cfg.GriefFamilyMultiplier).Should().BeInRange(cfg.GriefStrangerMultiplier, cfg.GriefSpouseMultiplier,
            "roadmap ordering: spouse > family > bonded stranger");
    }

    [Fact]
    public void Grief_BondedStranger_UsesBaselineMultiplier()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 63);
        var tile = FindLandTile(world);
        var mourner  = SpawnAt(world, tile, 21L);
        var deceased = SpawnAt(world, tile, 22L);
        GiveBond(mourner, deceased.Id, 0.5f);
        // No IsMarried/IsFamily flags — an ordinary bonded companion.

        var mourners = new List<(EntityId, float)>();
        var pending = new List<PendingEvent>();
        GoalManager.ApplyGriefToMourners(deceased.Id, deceased.Identity.Name, world, world.SimConfig.Character, mourners, pending);

        var grief = mourner.Goals.Single(g => g.Type == GoalType.Grieve);
        grief.Intensity.Should().BeApproximately(0.5f * world.SimConfig.Character.GriefStrangerMultiplier, 0.001f);
    }

    [Fact]
    public void EmitGriefEvent_PayloadReflectsRelationshipScaledIntensity()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 64);
        var tile = FindLandTile(world);
        var mourner  = SpawnAt(world, tile, 31L);
        var deceased = SpawnAt(world, tile, 32L);
        GiveBond(mourner, deceased.Id, 0.5f);

        var rel = world.Relationships.GetOrCreate(mourner.Id, deceased.Id);
        world.Relationships.Upsert(rel with { Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily });

        var mourners = new List<(EntityId, float)>();
        var pending = new List<PendingEvent>();
        GoalManager.ApplyGriefToMourners(deceased.Id, deceased.Identity.Name, world, world.SimConfig.Character, mourners, pending);

        var eventPending = new List<PendingEvent>();
        GoalManager.EmitGriefEvent(mourner, deceased.Id, deceased.Identity.Name, eventPending);

        eventPending.Should().ContainSingle();
        var ev = eventPending[0];
        ev.Type.Should().Be(EventType.CharacterGrieved);
        using var doc = JsonDocument.Parse(ev.PayloadJson);
        float payloadIntensity = doc.RootElement.GetProperty("Intensity").GetSingle();
        payloadIntensity.Should().BeApproximately(0.5f * world.SimConfig.Character.GriefSpouseMultiplier, 0.001f,
            "the emitted event must reflect the Grieve goal's scaled intensity, not the pre-multiplier Bond value");
    }
}
