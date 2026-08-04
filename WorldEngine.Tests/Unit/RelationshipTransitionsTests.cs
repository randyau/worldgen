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
/// M13 13.5: new relationship-transition events, reusing the existing Placate/DeclareRivalry/
/// marriage/Debt substrates rather than new ones (roadmap proposal #5) — Reconciliation (Placate
/// cools a rivalry past thresholds and it ends outright), Feud (re-declaring rivalry against an
/// already-active rival escalates instead of no-op'ing), Estrangement (a married edge's Trust
/// decaying far enough ends the marriage), and Oath-breaking (a debtor wars/raids their own
/// creditor's civ instead of honoring the debt).
/// </summary>
public class RelationshipTransitionsTests
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

    // ─── Reconciliation ─────────────────────────────────────────────────────

    [Fact]
    public void Placate_CoolsRivalryPastThresholds_EndsItOutright()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 91);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 1L);
        var b = SpawnAt(world, tile, 2L);

        var cfg = world.SimConfig.Fear;
        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with
        {
            Fear  = cfg.ReconciliationFearThreshold + cfg.PlacateFearReduction,
            Trust = cfg.ReconciliationTrustThreshold - cfg.PlacateTrustGain,
            Flags = RelationshipFlags.IsRival
        });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Placate(a.Id, b.Id), world, pending);

        var after = world.Relationships.Get(a.Id, b.Id)!;
        after.IsRival.Should().BeFalse("Fear/Trust both crossed the reconciliation thresholds");
        pending.Should().Contain(e => e.Type == EventType.RivalsReconciled);
    }

    [Fact]
    public void Placate_PartialCooling_KeepsRivalryActive()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 92);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 11L);
        var b = SpawnAt(world, tile, 12L);

        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with { Fear = 0.9f, Trust = -0.9f, Flags = RelationshipFlags.IsRival });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new Placate(a.Id, b.Id), world, pending);

        var after = world.Relationships.Get(a.Id, b.Id)!;
        after.IsRival.Should().BeTrue("one Placate isn't enough to cross both reconciliation thresholds from this far");
        pending.Should().NotContain(e => e.Type == EventType.RivalsReconciled);
    }

    // ─── Feud ───────────────────────────────────────────────────────────────

    [Fact]
    public void DeclareRivalry_AgainstExistingRival_EscalatesToFeud()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 93);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 21L);
        var b = SpawnAt(world, tile, 22L);

        var pending1 = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(a.Id, b.Id), world, pending1);
        var afterFirst = world.Relationships.Get(a.Id, b.Id)!;
        afterFirst.IsRival.Should().BeTrue();
        afterFirst.IsFeud.Should().BeFalse();

        var pending2 = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(a.Id, b.Id), world, pending2);

        var afterSecond = world.Relationships.Get(a.Id, b.Id)!;
        afterSecond.IsFeud.Should().BeTrue();
        afterSecond.Trust.Should().BeLessThan(afterFirst.Trust);
        afterSecond.Fear.Should().BeGreaterThan(afterFirst.Fear);
        pending2.Should().Contain(e => e.Type == EventType.RivalryEscalatedToFeud);
        pending2.Should().NotContain(e => e.Type == EventType.RivalryFormed);
    }

    [Fact]
    public void DeclareRivalry_AgainstExistingFeud_IsNoOp()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 94);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 31L);
        var b = SpawnAt(world, tile, 32L);

        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with { Flags = RelationshipFlags.IsRival | RelationshipFlags.IsFeud });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new DeclareRivalry(a.Id, b.Id), world, pending);

        pending.Should().BeEmpty("a fully-escalated Feud has nothing further to escalate to");
    }

    // ─── Estrangement ───────────────────────────────────────────────────────

    private static void InvokeCheckMarriageEstrangement(WorldState world, List<PendingEvent> pending)
    {
        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var method = typeof(CharacterBehaviorPhase).GetMethod("CheckMarriageEstrangement", BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(phase, new object[] { world, pending });
    }

    [Fact]
    public void MarriedCouple_TrustDecayedBelowThreshold_Estranges()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 95);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 41L);
        var b = SpawnAt(world, tile, 42L);

        var famCfg = world.SimConfig.Family;
        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with
        {
            Trust = famCfg.EstrangementTrustThreshold - 0.1f,
            Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily
        });

        var pending = new List<PendingEvent>();
        InvokeCheckMarriageEstrangement(world, pending);

        var after = world.Relationships.Get(a.Id, b.Id)!;
        after.IsMarried.Should().BeFalse();
        after.IsFamily.Should().BeFalse();
        pending.Should().ContainSingle(e => e.Type == EventType.CharacterEstranged);
    }

    [Fact]
    public void MarriedCouple_HealthyTrust_StaysMarried()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 96);
        var tile = FindLandTile(world);
        var a = SpawnAt(world, tile, 51L);
        var b = SpawnAt(world, tile, 52L);

        var rel = world.Relationships.GetOrCreate(a.Id, b.Id);
        world.Relationships.Upsert(rel with
        {
            Trust = 0.9f,
            Flags = RelationshipFlags.IsMarried | RelationshipFlags.IsFamily
        });

        var pending = new List<PendingEvent>();
        InvokeCheckMarriageEstrangement(world, pending);

        var after = world.Relationships.Get(a.Id, b.Id)!;
        after.IsMarried.Should().BeTrue();
        pending.Should().BeEmpty();
    }

    // ─── Oath-breaking ──────────────────────────────────────────────────────

    [Fact]
    public void Raid_AgainstCreditorsCiv_BreaksTheDebt()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 97);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 61L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 62L, "CivB");
        var raider = SpawnMember(world, tile, 63L, civA);
        var creditor = SpawnMember(world, tile, 64L, civB);
        world.Settlements[tile] = new SettlementStub(
            FounderId: creditor.Id, CivId: civB, Tile: tile, FoundedYear: 0,
            Population: 10, Health: 100, Name: "Debtholm");

        var rel = world.Relationships.GetOrCreate(raider.Id, creditor.Id);
        // raider owes creditor: sign is positive when raider lands as the canonical From
        float sign = raider.Id == rel.From ? 1f : -1f;
        world.Relationships.Upsert(rel with { Debt = 0.6f * sign, Trust = 0.5f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new RaidSettlement(raider.Id, tile), world, pending);

        var after = world.Relationships.Get(raider.Id, creditor.Id)!;
        after.Debt.Should().Be(0f);
        after.Trust.Should().BeLessThan(0.5f);
        pending.Should().Contain(e => e.Type == EventType.OathBroken);
    }

    [Fact]
    public void Raid_NoOutstandingDebtToDefenderCiv_NoOathBreaking()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 98);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 71L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 72L, "CivB");
        var raider = SpawnMember(world, tile, 73L, civA);
        var defender = SpawnMember(world, tile, 74L, civB);
        world.Settlements[tile] = new SettlementStub(
            FounderId: defender.Id, CivId: civB, Tile: tile, FoundedYear: 0,
            Population: 10, Health: 100, Name: "Debtfree");

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new RaidSettlement(raider.Id, tile), world, pending);

        pending.Should().NotContain(e => e.Type == EventType.OathBroken);
    }

    /// <summary>
    /// 2026-08-03 rebalance: OathBroken no longer requires the war/raid target to be specifically
    /// the creditor's own civ — calibration found that triple coincidence (a rare Tier1-Tier1 debt
    /// edge existing, AND the debtor specifically warring THAT creditor's civ, AND before the debt
    /// got forgiven) never fired in 300 years × 3 seeds. Any war/raid declared while any debt is
    /// outstanding now breaks it, wherever the war is aimed — see CheckOathBreaking's updated doc.
    /// </summary>
    [Fact]
    public void Raid_AgainstUnrelatedThirdCiv_StillBreaksOutstandingDebtToADifferentCiv()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 99);
        var tile = FindLandTile(world);
        var (_, civA) = SpawnRuler(world, tile, 81L, "CivA");
        var (_, civB) = SpawnRuler(world, tile, 82L, "CivB");
        var (_, civC) = SpawnRuler(world, tile, 83L, "CivC");
        var raider   = SpawnMember(world, tile, 84L, civA);
        var creditor = SpawnMember(world, tile, 85L, civB);
        var defender = SpawnMember(world, tile, 86L, civC);
        world.Settlements[tile] = new SettlementStub(
            FounderId: defender.Id, CivId: civC, Tile: tile, FoundedYear: 0,
            Population: 10, Health: 100, Name: "ThirdPartyTown");

        var rel = world.Relationships.GetOrCreate(raider.Id, creditor.Id);
        float sign = raider.Id == rel.From ? 1f : -1f;
        world.Relationships.Upsert(rel with { Debt = 0.6f * sign, Trust = 0.5f });

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new RaidSettlement(raider.Id, tile), world, pending);

        var after = world.Relationships.Get(raider.Id, creditor.Id)!;
        after.Debt.Should().Be(0f, "debt to civB breaks even though the raid targeted unrelated civC");
        pending.Should().Contain(e => e.Type == EventType.OathBroken);
    }
}
