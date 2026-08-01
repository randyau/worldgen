using System.Linq;
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
/// M13 13.2: Debt activated as an obligation mechanic — GrantAid/ForgiveDebt commands, War/Raid
/// dampening for an indebted character (UtilityScorer.DebtDampening), and inheritance of Debt to
/// a household heir on death (CharacterBehaviorPhase.TransferDebtOnDeath). Before this, Debt was
/// modeled (RelationshipEdge.Debt, persisted) but never written or read anywhere (see roadmap.md's
/// 2026-07-30 relationship-system audit).
/// </summary>
public class DebtObligationTests
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

    [Fact]
    public void GrantAid_TrustedNeedyRecipient_CreatesDebtAndRestoresNeed()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 21);
        var tile = FindLandTile(world);
        var granter   = SpawnAt(world, tile, 1L);
        var recipient = SpawnAt(world, tile, 2L);
        recipient.Needs = recipient.Needs with { Food = 0.1f };

        var cfg = world.SimConfig.Debt;
        world.Relationships.Upsert(world.Relationships.GetOrCreate(granter.Id, recipient.Id)
            with { Trust = cfg.AidTrustThreshold + 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new GrantAid(granter.Id, recipient.Id), world, pending);

        var rel = world.GetRelationship(granter.Id, recipient.Id)!;
        rel.DebtorId.Should().Be(recipient.Id);
        rel.CreditorId.Should().Be(granter.Id);
        Math.Abs(rel.Debt).Should().BeApproximately(cfg.AidDebtIncrement, 0.001f);
        recipient.Needs.Food.Should().BeGreaterThan(0.1f);
    }

    [Fact]
    public void GrantAid_BelowTrustThreshold_DoesNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 22);
        var tile = FindLandTile(world);
        var granter   = SpawnAt(world, tile, 11L);
        var recipient = SpawnAt(world, tile, 12L);
        recipient.Needs = recipient.Needs with { Food = 0.1f };
        // Neutral GetOrCreate edge has Trust = 0, below AidTrustThreshold — no explicit Upsert needed.

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new GrantAid(granter.Id, recipient.Id), world, pending);

        (world.GetRelationship(granter.Id, recipient.Id)?.Debt ?? 0f).Should().Be(0f);
    }

    [Fact]
    public void ForgiveDebt_ByCreditor_ZeroesDebtAndBoostsTrust()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 23);
        var tile = FindLandTile(world);
        var creditor = SpawnAt(world, tile, 21L);
        var debtor   = SpawnAt(world, tile, 22L);

        var cfg = world.SimConfig.Debt;
        var rel = world.Relationships.GetOrCreate(creditor.Id, debtor.Id);
        float sign = debtor.Id == rel.From ? 1f : -1f;
        world.Relationships.Upsert(rel with { Debt = 0.5f * sign, Trust = cfg.ForgiveTrustThreshold + 0.1f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new ForgiveDebt(creditor.Id, debtor.Id), world, pending);

        var forgiven = world.GetRelationship(creditor.Id, debtor.Id)!;
        forgiven.Debt.Should().Be(0f);
        forgiven.Trust.Should().BeApproximately(cfg.ForgiveTrustThreshold + 0.1f + cfg.ForgiveTrustGain, 0.001f);
    }

    [Fact]
    public void ForgiveDebt_ByNonCreditor_DoesNothing()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 24);
        var tile = FindLandTile(world);
        var creditor = SpawnAt(world, tile, 31L);
        var debtor   = SpawnAt(world, tile, 32L);

        var rel = world.Relationships.GetOrCreate(creditor.Id, debtor.Id);
        float sign = debtor.Id == rel.From ? 1f : -1f;
        world.Relationships.Upsert(rel with { Debt = 0.5f * sign });

        var pending = new List<PendingEvent>();
        // Debtor is not the creditor — forgiving "themselves" must be rejected.
        CivTracker.Resolve(new ForgiveDebt(debtor.Id, creditor.Id), world, pending);

        Math.Abs(world.GetRelationship(creditor.Id, debtor.Id)!.Debt).Should().BeApproximately(0.5f, 0.001f);
    }

    private static float InvokeDebtDampening(Tier1Character c, CivId targetCivId, WorldState world)
    {
        var method = typeof(UtilityScorer).GetMethod("DebtDampening", BindingFlags.NonPublic | BindingFlags.Static);
        return (float)method!.Invoke(null, new object[] { c, targetCivId, world, world.SimConfig.Debt })!;
    }

    [Fact]
    public void DebtDampening_CreditorLivesInTargetCiv_DampensProportionallyToDebt()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 25);
        var tile = FindLandTile(world);
        var debtor   = SpawnAt(world, tile, 41L);
        var creditor = SpawnAt(world, tile, 42L);
        world.Entities.Add(debtor);

        var enemyCivId = new CivId(world.NextCivId++);
        world.Civilizations[enemyCivId] = new Civilization(enemyCivId, "EnemyCiv", creditor.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(creditor, enemyCivId, OrganizationRole.Leader, world);

        var rel = world.Relationships.GetOrCreate(debtor.Id, creditor.Id);
        float owed = 0.8f;
        float sign = debtor.Id == rel.From ? 1f : -1f;
        world.Relationships.Upsert(rel with { Debt = owed * sign });

        float dampenMin = world.SimConfig.Debt.DebtWarDampenMin;
        float result = InvokeDebtDampening(debtor, enemyCivId, world);
        result.Should().BeLessThan(1f, "an unpaid debt to someone living in the target civ must dampen the score");
        result.Should().BeApproximately(1f - owed * (1f - dampenMin), 0.001f);
    }

    [Fact]
    public void DebtDampening_NoDebtToTargetCiv_ReturnsFullScore()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 26);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 51L);

        InvokeDebtDampening(c, new CivId(999), world).Should().Be(1f);
    }

    [Fact]
    public void CharacterDeath_TransfersDebtToMarriedHeir()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 27);
        var tile = FindLandTile(world);
        var deceased    = SpawnAt(world, tile, 61L);
        var spouse      = SpawnAt(world, tile, 62L);
        var thirdParty  = SpawnAt(world, tile, 63L);
        deceased.AgeSeason = world.SimConfig.Family.MarriageMinAgeSeasons + 10;
        spouse.AgeSeason   = world.SimConfig.Family.MarriageMinAgeSeasons + 10;

        var marriagePending = new List<PendingEvent>();
        CivTracker.Resolve(new ProposeMarriage(deceased.Id, spouse.Id), world, marriagePending);

        var debtRel = world.Relationships.GetOrCreate(deceased.Id, thirdParty.Id);
        float owedByDeceased = 0.4f;
        float sign = deceased.Id == debtRel.From ? 1f : -1f;
        world.Relationships.Upsert(debtRel with { Debt = owedByDeceased * sign });

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        typeof(CharacterBehaviorPhase)
            .GetMethod("KillCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { deceased, world, "test", pending });

        var oldEdge = world.GetRelationship(deceased.Id, thirdParty.Id)!;
        oldEdge.Debt.Should().Be(0f, "the deceased's own edge no longer carries the obligation");

        var heirEdge = world.GetRelationship(spouse.Id, thirdParty.Id)!;
        heirEdge.DebtorId.Should().Be(spouse.Id, "the surviving spouse inherits the obligation");
        Math.Abs(heirEdge.Debt).Should().BeApproximately(owedByDeceased, 0.001f);
    }
}
