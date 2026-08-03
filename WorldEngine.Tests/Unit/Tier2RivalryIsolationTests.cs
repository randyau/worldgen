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
/// M13.8.0 — regression guard for a structural invariant found by design review, not something
/// newly built: every consumer of <see cref="RelationshipEdge.IsRival"/> that feeds a civ-level
/// effect (war-declaration hostility, Fear-based War/Raid dampening, Dominance-goal target search,
/// Alliance-goal exclusion, the territorial-pressure gate) already filters its scan to
/// <see cref="Tier1Character"/>, so a Tier2 rivalry is naturally quarantined from war/alliance/
/// territory today — but only as an accident of each loop's type filter, never as a deliberate,
/// protected invariant. These tests turn that accident into an explicit, tested guarantee before
/// M13.8.1 makes Tier2 a valid rivalry target at all.
/// See docs/phases/m13_8_tier2_relationship_exposure.md for the full design discussion.
/// </summary>
public class Tier2RivalryIsolationTests
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

    /// <summary>Builds a Tier1Character with a specific Aggression and mid-range everything else.</summary>
    private static Tier1Character MakeCharacter(WorldState world, float aggression, TileCoord tile)
    {
        var personality = new PersonalityVector(
            Ambition: 0.5f, Greed: 0.5f, Aggression: aggression, Compassion: 0.5f,
            Curiosity: 0.5f, Creativity: 0.5f, Rationality: 0.5f, Wonder: 0.5f,
            Loyalty: 0.5f, Sociability: 0.5f, Honesty: 0.5f, Stability: 0.5f);
        var aptitude = new AptitudeVector(
            Diligence: 0.5f, Focus: 0.5f, Perfectionism: 0.5f,
            Composure: 0.5f, Acuity: 0.5f, Ingenuity: 0.5f);
        var identity = new IdentityData(
            Name: "TestChar", Epithet: "", AncestryId: "",
            MotherId: null, FatherId: null, BirthYear: 1, BirthSeason: 0);

        var c = new Tier1Character(EntityId.New(), tile, personality, aptitude, SkillVector.Default,
            identity, maxHealth: world.SimConfig.Character.MaxHealth, maxAgeSeason: 1200);
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
    public void WarHostility_Tier2RivalDoesNotJustifyWar_ButEquivalentTier1RivalDoes()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 201);
        var tile  = FindLandTile(world);
        var wCfg  = world.SimConfig.War;

        var ruler = MakeCharacter(world, wCfg.WarAggressionThreshold + 0.2f, tile);
        var civA  = new CivId(world.NextCivId++);
        world.Civilizations[civA] = new Civilization(civA, "CivA", ruler.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(ruler, civA, OrganizationRole.Leader, world);

        var civB = new CivId(world.NextCivId++);
        world.Civilizations[civB] = new Civilization(civB, "CivB", EntityId.New(), tile, world.CurrentYear);
        world.Settlements[tile] = new SettlementStub(new EntityId(1), civB, tile, 0, 50, 100);

        var tier2Rival = SpawnTier2At(world, tile, "T2Rival");
        var rel = world.Relationships.GetOrCreate(ruler.Id, tier2Rival.Id);
        world.Relationships.Upsert(rel with { Trust = -0.9f, Flags = RelationshipFlags.IsRival });

        var scorer = new UtilityScorer(world.SimConfig);
        var buildCandidates = typeof(UtilityScorer).GetMethod("BuildCandidates", BindingFlags.NonPublic | BindingFlags.Instance);

        var candidatesWithTier2Rival = (List<UtilityScorer.ScoredAction>)buildCandidates!.Invoke(
            scorer, new object[] { ruler, world, world.SimConfig.Character })!;
        candidatesWithTier2Rival.Should().NotContain(a => a.Command is DeclareWar,
            "a Tier2 rival must be invisible to the war-declaration hostility check");

        // Positive control: an equivalent Tier1 rival living in CivB SHOULD justify war — proves the
        // absence above is due to the Tier1-only type filter, not some other missing precondition
        // (border tension, war cooldown, aggression threshold, etc.) in this test's setup.
        var tier1Rival = MakeCharacter(world, 0.5f, tile);
        CivTracker.SetCharacterCiv(tier1Rival, civB, OrganizationRole.Member, world);
        var rel2 = world.Relationships.GetOrCreate(ruler.Id, tier1Rival.Id);
        world.Relationships.Upsert(rel2 with { Trust = -0.9f, Flags = RelationshipFlags.IsRival });

        var candidatesWithTier1Rival = (List<UtilityScorer.ScoredAction>)buildCandidates!.Invoke(
            scorer, new object[] { ruler, world, world.SimConfig.Character })!;
        candidatesWithTier1Rival.Should().Contain(a => a.Command is DeclareWar,
            "sanity check: an equivalent Tier1 rival in the target civ SHOULD justify war, proving the " +
            "Tier2 case above is isolated by the type filter and not by an unrelated missing precondition");
    }

    [Fact]
    public void FearDampening_Tier2RivalInTargetCiv_ReturnsFullScore()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 202);
        var tile  = FindLandTile(world);
        var c     = MakeCharacter(world, 0.5f, tile);

        var tier2Rival = SpawnTier2At(world, tile, "T2Rival");
        var rel = world.Relationships.GetOrCreate(c.Id, tier2Rival.Id);
        world.Relationships.Upsert(rel with { Fear = 0.9f, Flags = RelationshipFlags.IsRival });

        var enemyCivId = new CivId(world.NextCivId++);
        var method = typeof(UtilityScorer).GetMethod("FearDampening", BindingFlags.NonPublic | BindingFlags.Static);
        float result = (float)method!.Invoke(null, new object[] { c, enemyCivId, world, world.SimConfig.Fear })!;

        result.Should().Be(1f,
            "a Tier2 rival must be invisible to FearDampening regardless of Fear — Tier2Character has " +
            "no CivId at all, and the rival lookup itself is Tier1Character-only");
    }

    [Fact]
    public void FindNearbyRival_SkipsTier2Rival()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 203);
        var tile  = FindLandTile(world);
        var c     = MakeCharacter(world, 0.8f, tile);

        var tier2Rival = SpawnTier2At(world, tile, "T2Rival");
        var rel = world.Relationships.GetOrCreate(c.Id, tier2Rival.Id);
        world.Relationships.Upsert(rel with { Flags = RelationshipFlags.IsRival });

        var method = typeof(GoalManager).GetMethod("FindNearbyRival", BindingFlags.NonPublic | BindingFlags.Static);
        var result = (EntityId?)method!.Invoke(null,
            new object[] { c, world, world.SimConfig.Character.RivalSearchRadius });

        result.Should().BeNull(
            "a Tier2 rival must be invisible to the Dominance goal's rival search, which only scans Tier1Character candidates");
    }

    [Fact]
    public void FindNearbyNeutral_SkipsTier2Entirely()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 204);
        var tile  = FindLandTile(world);
        var c     = MakeCharacter(world, 0.3f, tile);
        SpawnTier2At(world, tile, "T2Bystander"); // no relationship edge — "neutral" by definition if it were eligible at all

        var method = typeof(GoalManager).GetMethod("FindNearbyNeutral", BindingFlags.NonPublic | BindingFlags.Static);
        var result = (EntityId?)method!.Invoke(null,
            new object[] { c, world, world.SimConfig.Character.AllianceSearchRadius });

        result.Should().BeNull(
            "a Tier2 must be invisible to the Alliance goal's neutral-candidate search entirely — not offered as an ally candidate any more than as a rival");
    }

    [Fact]
    public void TerritorialPressure_SkipsCoLocatedTier2_TrustUnchanged()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 205);
        var tile  = FindLandTile(world);
        var cfg   = world.SimConfig.Character;
        var c     = MakeCharacter(world, cfg.TerritorialAggressionMin + 0.2f, tile);

        var civA = new CivId(world.NextCivId++);
        world.Civilizations[civA] = new Civilization(civA, "CivA", c.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(c, civA, OrganizationRole.Leader, world);
        world.Settlements[tile] = new SettlementStub(new EntityId(1), civA, tile, 0, 50, 100);

        var tier2 = SpawnTier2At(world, tile, "T2Bystander");
        var relBefore = world.Relationships.GetOrCreate(c.Id, tier2.Id);
        float trustBefore = relBefore.Trust;

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var method = typeof(CharacterBehaviorPhase).GetMethod("ApplyTerritorialPressure", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(phase, new object[] { c, world, world.CurrentTick });

        var relAfter = world.Relationships.Get(c.Id, tier2.Id);
        relAfter!.Trust.Should().Be(trustBefore,
            "the territorial-pressure gate only drains cross-civ Tier1-Tier1 pairs; a co-located Tier2 must be untouched");
    }
}
