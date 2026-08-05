using System.Linq;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M13.8.1 — Tier2 as an eligible target for DeclareRivalry/Placate, and marriage-to-Tier2 as an
/// auto-crystallization trigger. Built on 13.8.0's isolation guard (Tier2RivalryIsolationTests) —
/// these tests confirm the *positive* path now works, not just that it stays contained.
/// See docs/phases/m13_8_tier2_relationship_exposure.md.
/// </summary>
public class Tier2RivalryAndMarriageTests
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

    private static Tier2Character SpawnTier2At(WorldState world, TileCoord tile, string name)
    {
        var c = new Tier2Character(EntityId.New(), tile, name, PersonalityVector6.Default,
            new LivelihoodData(Tier2Role.Merchant, null, tile, 0.5f), maxHealth: 100, maxAgeSeason: 800);
        world.Entities.Add(c);
        return c;
    }

    [Fact]
    public void DeclareRivalry_AgainstTier2_SetsRivalFlagWithBaseFearOnly()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 301);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 1L);
        var t2    = SpawnTier2At(world, tile, "T2Rival");

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(c.Id, t2.Id), world, pending);

        var rel = world.GetRelationship(c.Id, t2.Id);
        rel.Should().NotBeNull();
        rel!.IsRival.Should().BeTrue();
        rel.Fear.Should().Be(world.SimConfig.Fear.RivalryBaseFearIncrement,
            "a Tier2 has no Skills/Aggression to scale Fear beyond the flat base increment — see CivTracker.TargetPower");
        pending.Should().ContainSingle(e => e.Type == EventType.RivalryFormed);
    }

    [Fact]
    public void DeclareRivalry_ReDeclaredAgainstExistingTier2Rival_EscalatesToFeud()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 302);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 11L);
        var t2    = SpawnTier2At(world, tile, "T2Rival");

        var pending1 = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(c.Id, t2.Id), world, pending1);

        var pending2 = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(c.Id, t2.Id), world, pending2);

        var rel = world.GetRelationship(c.Id, t2.Id)!;
        rel.IsFeud.Should().BeTrue();
        pending2.Should().ContainSingle(e => e.Type == EventType.RivalryEscalatedToFeud);
    }

    [Fact]
    public void Placate_Tier2Rival_ReducesFearAndCanReconcile()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 303);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 21L);
        var t2    = SpawnTier2At(world, tile, "T2Rival");

        var rel = world.Relationships.GetOrCreate(c.Id, t2.Id);
        world.Relationships.Upsert(rel with { Trust = -0.5f, Fear = 0.6f, Flags = RelationshipFlags.IsRival });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Placate(c.Id, t2.Id), world, pending);

        var after = world.GetRelationship(c.Id, t2.Id)!;
        after.Fear.Should().BeLessThan(0.6f);
        after.Trust.Should().BeGreaterThan(-0.5f);
        pending.Should().ContainSingle(e => e.Type == EventType.RivalryPlacated);
    }

    [Fact]
    public void ProposeMarriage_ToTier2BondCompanion_PromotesThenMarries()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 304);
        var tile  = FindLandTile(world);
        var famCfg = world.SimConfig.Family;

        var c  = SpawnAt(world, tile, 31L);
        c.AgeSeason = famCfg.MarriageMinAgeSeasons + 10; // MinRulerAgeSeasons (startAsAdult's floor) < MarriageMinAgeSeasons
        var t2 = SpawnTier2At(world, tile, "T2Beloved");

        // Hand-construct the Bond goal targeting the Tier2 (bypassing GoalManager's formation gate,
        // which is exercised elsewhere) and satisfy ResolveMarriage's preconditions directly.
        c.Goals.Add(new GoalData { Type = GoalType.Bond, Object = GoalObject.Person, TargetEntityId = t2.Id });
        var rel = world.Relationships.GetOrCreate(c.Id, t2.Id);
        world.Relationships.Upsert(rel with { Trust = famCfg.MarriageTrustThreshold + 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(c.Id, t2.Id), world, pending);

        // Matches organic crystallization's own behavior (TryCrystallize): IsAlive flips false
        // immediately; actual removal from the Tier2 collection happens on the next
        // Tier2BehaviorPhase tick pass (mirrors how Tier1 death defers registry cleanup too).
        t2.IsAlive.Should().BeFalse("the Tier2 must be marked dead once promoted, freeing the name/identity");
        pending.Should().Contain(e => e.Type == EventType.CharacterCrystallized);
        pending.Should().Contain(e => e.Type == EventType.CharacterBorn);
        pending.Should().Contain(e => e.Type == EventType.CharacterMarried);

        // PromoteToTier1 (like the organic crystallization path it's shared with) rolls the promoted
        // character an entirely new name — CharacterCrystallizedPayload logs the old and new names
        // as distinct fields deliberately, this isn't preserved. The promoted Tier1's EntityId is
        // deliberately distinct from the dying Tier2's own id (M14 14.4 fix — see PromoteToTier1's
        // doc comment: reusing the dead Tier2's exact EntityId let EntityRegistry's shared
        // Dictionary<EntityId, IEntity> alias the two, so the same-or-next-tick dead-Tier2 sweep
        // deleted the *promoted Tier1* instead of the intended dead Tier2), so find it by identity
        // (the only other living Tier1 besides the proposer) rather than by id or name.
        var promoted = world.Entities.Characters.SingleOrDefault(ch => ch.IsAlive && ch.Id != c.Id);
        promoted.Should().NotBeNull("promotion must spawn a new Tier1 in place of the Tier2");
        promoted!.AgeSeason.Should().BeGreaterThanOrEqualTo(famCfg.MarriageMinAgeSeasons,
            "PromoteForMarriage must floor the newly-rolled age so the marriage isn't rejected the same tick it promotes");

        var marriageRel = world.GetRelationship(c.Id, promoted.Id)!;
        marriageRel.IsMarried.Should().BeTrue();
    }

    [Fact]
    public void ProposeMarriage_ToDeadTier2_DoesNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 305);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 41L);
        var t2    = SpawnTier2At(world, tile, "T2Gone");
        t2.IsAlive = false;

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(c.Id, t2.Id), world, pending);

        pending.Should().BeEmpty();
    }
}
