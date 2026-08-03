using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M13.8.2 — Notability: a Tier2 targeted by Tier1-driven relationship actions (Bond/Rivalry/
/// Placate/GrantAid/ForgiveDebt) accumulates a decaying counter that feeds TryCrystallize's gate
/// alongside Ambition/Status, kept deliberately distinct from Needs.Status.
/// See docs/phases/m13_8_tier2_relationship_exposure.md.
/// </summary>
public class Tier2NotabilityTests
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
    public void DeclareRivalry_AgainstTier2_BumpsNotability()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 401);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 1L);
        var t2    = SpawnTier2At(world, tile, "T2");

        CivTracker.Resolve(new DeclareRivalry(c.Id, t2.Id), world, new List<PendingEvent>());

        t2.Notability.Should().Be(world.SimConfig.Character.Tier2NotabilityGainPerEvent);
    }

    [Fact]
    public void Placate_Tier2Rival_BumpsNotability()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 402);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 11L);
        var t2    = SpawnTier2At(world, tile, "T2");
        var rel   = world.Relationships.GetOrCreate(c.Id, t2.Id);
        world.Relationships.Upsert(rel with { Trust = -0.5f, Fear = 0.6f, Flags = RelationshipFlags.IsRival });

        CivTracker.Resolve(new Placate(c.Id, t2.Id), world, new List<PendingEvent>());

        t2.Notability.Should().Be(world.SimConfig.Character.Tier2NotabilityGainPerEvent);
    }

    [Fact]
    public void GrantAid_ToTier2Recipient_BumpsNotability()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 403);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 21L);
        var t2    = SpawnTier2At(world, tile, "T2");
        t2.Needs  = t2.Needs with { Food = 0f, Safety = 0f };

        CivTracker.Resolve(new GrantAid(c.Id, t2.Id), world, new List<PendingEvent>());

        t2.Notability.Should().Be(world.SimConfig.Character.Tier2NotabilityGainPerEvent);
    }

    [Fact]
    public void ForgiveDebt_ToTier2Debtor_BumpsNotability()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 404);
        var tile  = FindLandTile(world);
        var c     = SpawnAt(world, tile, 31L);
        var t2    = SpawnTier2At(world, tile, "T2");
        var rel   = world.Relationships.GetOrCreate(c.Id, t2.Id);
        // RelationshipGraph.Upsert canonicalizes storage so From is always the lower EntityId
        // value — the Debt sign must be chosen relative to that canonical order (not whichever
        // order GetOrCreate happened to be called with) for DebtorId/CreditorId to land correctly.
        float sign = t2.Id.Value < c.Id.Value ? 1f : -1f;
        world.Relationships.Upsert(rel with
        {
            Debt = world.SimConfig.Debt.ForgiveMinDebt * 2f * sign,
            Trust = world.SimConfig.Debt.ForgiveTrustThreshold + 0.1f
        });

        CivTracker.Resolve(new ForgiveDebt(c.Id, t2.Id), world, new List<PendingEvent>());

        t2.Notability.Should().Be(world.SimConfig.Character.Tier2NotabilityGainPerEvent);
    }

    [Fact]
    public void BondGoalFormation_TargetingTier2_BumpsNotability()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 407);
        var tile  = FindLandTile(world);
        var cfg   = world.SimConfig;

        // High Compassion (> GoalCompassionThreshold) is the only gate FindHighTrustCompanion's
        // Tier2 "shared homeland" shortcut needs — hand-build the Tier1 so this doesn't depend on
        // CharacterFactory's random personality roll landing above threshold.
        var personality = new PersonalityVector(0.5f, 0.5f, 0.5f, Compassion: 1f,
            0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        var identity = new IdentityData("Test", "", "human", null, null, world.CurrentYear, 0);
        var c = new Tier1Character(EntityId.New(), tile, personality, AptitudeVector.Default,
            SkillVector.Default, identity, maxHealth: 100, maxAgeSeason: 1200);
        world.Entities.Add(c);

        var t2 = SpawnTier2At(world, tile, "T2Companion");

        var pending = new List<PendingEvent>();
        GoalManager.UpdateGoals(c, world, currentTick: 1, cfg.Character, pending);

        c.Goals.Should().Contain(g => g.Type == GoalType.Bond && g.TargetEntityId == t2.Id);
        t2.Notability.Should().Be(cfg.Character.Tier2NotabilityGainPerEvent);
    }

    [Fact]
    public void Notability_DecaysEachTick()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 405);
        var tile  = FindLandTile(world);
        var t2    = SpawnTier2At(world, tile, "T2");
        var cfg   = world.SimConfig;
        t2.GainNotability(0.5f);

        var phase = new Tier2BehaviorPhase(cfg);
        phase.Execute(world, world.CurrentTick);

        t2.Notability.Should().BeApproximately(0.5f - cfg.Character.Tier2NotabilityDecayRate, 0.0001f);
    }

    [Fact]
    public void TryCrystallize_HighNotabilityLowStatus_CanStillCrystallize()
    {
        // Ambition + Notability alone (Status left at its low default) must be able to satisfy the
        // gate — this is 13.8.2's whole point: drama exposure is an alternate path, not a bonus
        // that only matters once Status is already high.
        var world = WorldTestHelper.CreateSmallWorld(seed: 406);
        var tile  = FindLandTile(world);
        var cfg   = world.SimConfig;

        var t2 = new Tier2Character(EntityId.New(), tile, "T2Notable",
            new PersonalityVector6(Ambition: 1f, Loyalty: 0.5f, Diligence: 0.5f, Sociability: 0.5f, Cunning: 0.5f, Rationality: 0.5f),
            new LivelihoodData(Tier2Role.Merchant, null, tile, 0.5f), maxHealth: 100, maxAgeSeason: 800);
        world.Entities.Add(t2);
        t2.GainNotability(cfg.Character.Tier2CrystalNotabilityThreshold);
        t2.Needs = t2.Needs with { Status = 0f };

        var phase = new Tier2BehaviorPhase(cfg);
        bool everCrystallized = false;
        for (int i = 0; i < 20000 && t2.IsAlive; i++)
        {
            phase.Execute(world, i);
            if (!t2.IsAlive) { everCrystallized = true; break; }
            // re-apply Notability each tick so decay doesn't drop it below threshold before the roll lands
            t2.GainNotability(cfg.Character.Tier2NotabilityDecayRate);
        }

        everCrystallized.Should().BeTrue("high Notability with low Status must still be able to crystallize");
    }
}
