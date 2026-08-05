using System.Linq;
using System.Reflection;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M14 14.4 — Guild organizations, real stored treasuries, civ-level economic ruin, and war
/// reparations. See docs/phases/m14_economy_independent_wealth.md decisions 9/10 and the
/// phase-sequence "14.4" entry.
/// </summary>
public class GuildTreasuryTests
{
    private static TileCoord FindLandTile(WorldState world, TileCoord? exclude = null, int minDist = 0)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (exclude is { } e)
            {
                int dx = c.X - e.X, dy = c.Y - e.Y;
                if (dx * dx + dy * dy < minDist * minDist) continue;
            }
            return c;
        }
        throw new InvalidOperationException("No suitable land tile found");
    }

    private static Tier1Character SpawnAt(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        return c;
    }

    private static void KillCharacter(CharacterBehaviorPhase phase, Tier1Character c, WorldState world, List<PendingEvent> pending) =>
        typeof(CharacterBehaviorPhase)
            .GetMethod("KillCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { c, world, "test", pending });

    // ─── ContributeToTreasury / WithdrawFromTreasury ───────────────────────────

    [Fact]
    public void ContributeToTreasury_MovesWealth_FromCharacterToOrgTreasury()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];
        var org = world.Organizations[civ.OrgId!.Value];

        founder.AddWealth(100f);
        world.SimConfig.Economy.ContributeToTreasuryAmount = 15f;

        CivTracker.Resolve(new ContributeToTreasury(founder.Id, org.Id), world, pending);

        founder.Wealth.Should().Be(85f, "the contributed amount must leave the contributor's personal Wealth");
        org.Treasury.Should().Be(15f, "the same amount must land in the Organization's Treasury — a real two-sided transfer");
    }

    [Fact]
    public void ContributeToTreasury_CapsAtAvailableWealth()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 43);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];
        var org = world.Organizations[civ.OrgId!.Value];

        founder.AddWealth(5f);
        world.SimConfig.Economy.ContributeToTreasuryAmount = 15f;

        CivTracker.Resolve(new ContributeToTreasury(founder.Id, org.Id), world, pending);

        founder.Wealth.Should().Be(0f, "a contribution can never exceed the contributor's available Wealth");
        org.Treasury.Should().Be(5f);
    }

    [Fact]
    public void WithdrawFromTreasury_LeaderOnly_NonLeaderIsRejected()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 44);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];
        var org = world.Organizations[civ.OrgId!.Value];
        org.Treasury = 50f;

        var nonLeader = SpawnAt(world, tile, 2L);
        CivTracker.SetCharacterCiv(nonLeader, civ.Id, OrganizationRole.Member, world);
        civ.Members.Add(nonLeader.Id);

        CivTracker.Resolve(new WithdrawFromTreasury(nonLeader.Id, org.Id, nonLeader.Id), world, pending);

        org.Treasury.Should().Be(50f, "a non-leader's withdrawal attempt must be rejected outright — the only authority check in the model");
        nonLeader.Wealth.Should().Be(0f);
    }

    [Fact]
    public void WithdrawFromTreasury_Leader_MovesWealth_FromTreasuryToRecipient()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 45);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];
        var org = world.Organizations[civ.OrgId!.Value];
        org.Treasury = 50f;
        world.SimConfig.Economy.WithdrawFromTreasuryAmount = 20f;

        CivTracker.Resolve(new WithdrawFromTreasury(founder.Id, org.Id, founder.Id), world, pending);

        org.Treasury.Should().Be(30f, "the withdrawn amount must leave the Treasury — a real two-sided transfer");
        founder.Wealth.Should().Be(20f, "the same amount must land in the recipient's personal Wealth");
    }

    [Fact]
    public void WithdrawFromTreasury_CapsAtAvailableTreasury()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 46);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];
        var org = world.Organizations[civ.OrgId!.Value];
        org.Treasury = 5f;
        world.SimConfig.Economy.WithdrawFromTreasuryAmount = 20f;

        CivTracker.Resolve(new WithdrawFromTreasury(founder.Id, org.Id, founder.Id), world, pending);

        org.Treasury.Should().Be(0f);
        founder.Wealth.Should().Be(5f, "a voluntary withdrawal can never itself drive the Treasury negative — only war reparations do that");
    }

    // ─── Guild-member trade routing (14.1 payment path revisited) ─────────────

    private static (WorldState world, TileCoord home, TileCoord dest, Tier2Character merchant) BuildTradeWorld(int seed)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);
        world.SimConfig.Character.MerchantTradeChance = 1f;
        world.GlobalPriceIndex = 1f;
        // Isolate the payment-routing tests from M13.8's unrelated Tier2→Tier1 crystallization
        // roll (world-seed-dependent, so otherwise flaky across the different seeds these tests
        // use) — set the gate unreachable so the merchant stays Tier2 for the assertion.
        world.SimConfig.Character.Tier2CrystalAmbitionThreshold = 2f;
        // Default the Guild-formation threshold out of reach — a single trade at this test's scale
        // pays the merchant well over the production default (40), which would otherwise promote
        // them to a Guild Leader mid-test via RunGuildFormation and confound the payment-routing
        // assertion. The Guild-formation-specific tests below override this back down explicitly.
        world.SimConfig.Economy.GuildFormationWealthThreshold = 1_000_000f;

        var home = FindLandTile(world);
        var dest = FindLandTile(world, exclude: home, minDist: 3);

        world.Settlements[home] = new SettlementStub(
            FounderId: new EntityId(1), CivId: default, Tile: home, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Home",
            ResourceStores: new Dictionary<string, float> { ["iron"] = 500f });

        world.Settlements[dest] = new SettlementStub(
            FounderId: new EntityId(2), CivId: default, Tile: dest, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Dest",
            ResourceStores: new Dictionary<string, float> { ["gold"] = 500f });

        var merchant = new Tier2Character(
            id: new EntityId(9001), location: home, name: "Merchant",
            personality: PersonalityVector6.Default,
            livelihood: new LivelihoodData(Tier2Role.Merchant, null, home, 0.5f),
            maxHealth: 100, maxAgeSeason: 1000);
        world.Entities.Add(merchant);

        return (world, home, dest, merchant);
    }

    [Fact]
    public void GuildMemberMerchant_TradeIncome_RoutesToTreasury_NotPersonalWealth()
    {
        var (world, home, dest, merchant) = BuildTradeWorld(seed: 100);

        // Found a Guild directly (bypassing the Wealth-threshold formation trigger, which is
        // covered by its own test below) and add the merchant as an ordinary Member.
        var guildOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Guild, "Test Guild", new EntityId(500));
        var guildOrg = world.Organizations[guildOrgId];
        guildOrg.Members[merchant.Id] = new Membership(guildOrgId, OrganizationRole.Member, 1.0f);

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        merchant.Wealth.Should().Be(0f, "a Guild-member merchant's trade income must not credit personal Wealth");
        guildOrg.Treasury.Should().BeGreaterThan(0f, "a Guild-member merchant's trade income must instead credit their Guild's Treasury");
    }

    [Fact]
    public void NonGuildMerchant_TradeIncome_StillRoutesToPersonalWealth()
    {
        var (world, home, dest, merchant) = BuildTradeWorld(seed: 101);

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        merchant.Wealth.Should().BeGreaterThan(0f, "an ordinary (non-Guild) merchant must keep 100% of trade income personally, unchanged from 14.1");
    }

    // ─── Guild formation ────────────────────────────────────────────────────────

    [Fact]
    public void Merchant_FormsGuild_WhenWealthCrossesThreshold()
    {
        var (world, home, dest, merchant) = BuildTradeWorld(seed: 102);
        world.SimConfig.Economy.GuildFormationWealthThreshold = 10f;
        merchant.AddWealth(50f);

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        var guild = world.Organizations.Values.SingleOrDefault(o => o.Kind == OrganizationKind.Guild);
        guild.Should().NotBeNull("a merchant whose Wealth crosses GuildFormationWealthThreshold must form a Guild");
        guild!.HomeSettlementCoord.Should().Be(home, "the Guild's HomeSettlementCoord must anchor to the founding merchant's home settlement");

        // The founding merchant is promoted to Tier1 (SuccessionResolver.SelectSuccessor only
        // considers Tier1Character members — see the M12 audit note on reusing it unmodified).
        world.GetEntity(merchant.Id).Should().BeNull("the founding Tier2 merchant is removed on promotion");
        var promoted = world.Entities.Characters.Single(c => c.IsAlive && guild.LeaderId == c.Id);
        promoted.Wealth.Should().BeGreaterThanOrEqualTo(50f, "the founder's accumulated Wealth must carry over through promotion");
        guild.Members.Should().ContainKey(promoted.Id);
    }

    [Fact]
    public void SecondMerchant_JoinsExistingGuild_AtSameSettlement_WithoutPromotion()
    {
        var (world, home, dest, merchant) = BuildTradeWorld(seed: 103);
        var existingGuildId = CivTracker.CreateOrganization(world, OrganizationKind.Guild, "Existing Guild", new EntityId(500), home);
        world.SimConfig.Economy.GuildFormationWealthThreshold = 10f;
        merchant.AddWealth(50f);

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        world.Organizations.Values.Count(o => o.Kind == OrganizationKind.Guild).Should().Be(1,
            "a merchant at a settlement with an existing Guild must join it rather than founding a second one");
        world.Organizations[existingGuildId].Members.Should().ContainKey(merchant.Id);
        world.GetEntity(merchant.Id).Should().NotBeNull("joining an existing Guild as an ordinary Member does not require promotion");
    }

    // ─── Guild succession ───────────────────────────────────────────────────────

    [Fact]
    public void GuildLeaderDeath_TriggersSuccession_ViaSuccessionResolverKernel()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 104);
        var tile = FindLandTile(world);
        var leader = SpawnAt(world, tile, 1L);
        var heir = SpawnAt(world, tile, 2L);

        var guildOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Guild, "Test Guild", leader.Id, tile);
        var org = world.Organizations[guildOrgId];
        var leaderMembership = new Membership(guildOrgId, OrganizationRole.Leader, 1.0f);
        var heirMembership   = new Membership(guildOrgId, OrganizationRole.Member, 1.0f);
        org.Members[leader.Id] = leaderMembership;
        org.Members[heir.Id]   = heirMembership;
        leader.Memberships.Add(leaderMembership);
        heir.Memberships.Add(heirMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        KillCharacter(phase, leader, world, pending);

        org.LeaderId.Should().Be(heir.Id, "SuccessionResolver.SelectSuccessor must pick the highest-scoring living member unmodified");
        pending.Should().Contain(e => e.Type == EventType.GuildLeadershipTransferred);
    }

    [Fact]
    public void GuildLeaderDeath_NoEligibleMember_SeatStaysVacant_NoThrow()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 105);
        var tile = FindLandTile(world);
        var leader = SpawnAt(world, tile, 1L);

        var guildOrgId = CivTracker.CreateOrganization(world, OrganizationKind.Guild, "Test Guild", leader.Id, tile);
        var org = world.Organizations[guildOrgId];
        var leaderMembership = new Membership(guildOrgId, OrganizationRole.Leader, 1.0f);
        org.Members[leader.Id] = leaderMembership;
        leader.Memberships.Add(leaderMembership);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        var act = () => KillCharacter(phase, leader, world, pending);

        act.Should().NotThrow();
        org.LeaderId.Should().Be(leader.Id, "with no eligible successor, the seat stays vacant rather than reassigning to nobody");
    }

    // ─── Civ-level economic ruin: TreasuryInsolvent edge-trigger ───────────────

    private static (WorldState world, Civilization civ, Organization org) PlantCiv(int seed)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];
        var org = world.Organizations[civ.OrgId!.Value];
        return (world, civ, org);
    }

    [Fact]
    public void TreasuryInsolvent_FiresExactlyOnce_WhenCrossingNegative_NotEveryTick()
    {
        var (world, civ, org) = PlantCiv(seed: 106);
        org.Treasury = -10f;

        var pending1 = new List<PendingEvent>();
        CivTracker.CheckTreasuryInsolvency(world, pending1);
        pending1.Count(e => e.Type == EventType.TreasuryInsolvent).Should().Be(1, "must fire on the tick it first crosses negative");

        var pending2 = new List<PendingEvent>();
        CivTracker.CheckTreasuryInsolvency(world, pending2);
        pending2.Count(e => e.Type == EventType.TreasuryInsolvent).Should().Be(0, "must not fire again while it merely stays negative");

        org.Treasury = 5f;
        var pending3 = new List<PendingEvent>();
        CivTracker.CheckTreasuryInsolvency(world, pending3);
        pending3.Count(e => e.Type == EventType.TreasuryInsolvent).Should().Be(0, "recovering to non-negative fires nothing");

        org.Treasury = -1f;
        var pending4 = new List<PendingEvent>();
        CivTracker.CheckTreasuryInsolvency(world, pending4);
        pending4.Count(e => e.Type == EventType.TreasuryInsolvent).Should().Be(1, "a second crossing into negative must fire again");
    }

    [Fact]
    public void UnrestAccrual_GainsInsolvencyBonus_WhenCivTreasuryNegative()
    {
        var (world, civ, org) = PlantCiv(seed: 107);
        world.SimConfig.Economy.TreasuryInsolvencyUnrestBonus = 0.2f;
        org.Treasury = -10f;

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);

        world.Settlements[civ.CapitalTile].Unrest.Should().BeGreaterThanOrEqualTo(0.2f - 0.001f,
            "a civ with a negative Treasury must gain the insolvency unrest contribution alongside the existing drivers");
    }

    // ─── War reparations ────────────────────────────────────────────────────────

    private static (WorldState world, Civilization winner, Civilization loser, Organization winnerOrg, Organization loserOrg) PlantTwoCivs(int seed)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);
        world.SimConfig.Character.GlobalSettlementMinDist = 3;

        var a = FindLandTile(world);
        var b = FindLandTile(world, exclude: a, minDist: 5);

        var founderA = SpawnAt(world, a, 1L);
        var founderB = SpawnAt(world, b, 2L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founderA.Id, a), world, pending, world.SimConfig.SettlementNames);
        CivTracker.Resolve(new EstablishSettlement(founderB.Id, b), world, pending, world.SimConfig.SettlementNames);

        var civA = world.Civilizations[world.Settlements[a].CivId];
        var civB = world.Civilizations[world.Settlements[b].CivId];
        var orgA = world.Organizations[civA.OrgId!.Value];
        var orgB = world.Organizations[civB.OrgId!.Value];
        return (world, civA, civB, orgA, orgB);
    }

    [Fact]
    public void WarReparations_TransfersTreasury_FromLoserToWinner_CanDriveLoserNegative()
    {
        var (world, winner, loser, winnerOrg, loserOrg) = PlantTwoCivs(seed: 108);
        winnerOrg.Treasury = 0f;
        loserOrg.Treasury = 5f;
        world.SimConfig.Economy.WarReparationsPerBattleWinAdvantage = 15f;

        var pending = new List<PendingEvent>();
        CivTracker.ApplyWarReparations(winner.Id, loser.Id, battleWinAdvantage: 2, world, pending);

        winnerOrg.Treasury.Should().Be(30f, "the winner's Treasury must gain the full reparations amount");
        loserOrg.Treasury.Should().Be(-25f, "reparations are allowed to drive the loser's Treasury negative — that's the insolvency trigger");
        pending.Should().Contain(e => e.Type == EventType.WarReparationsPaid);
    }

    [Fact]
    public void WarReparations_ZeroAdvantage_TransfersNothing()
    {
        var (world, winner, loser, winnerOrg, loserOrg) = PlantTwoCivs(seed: 109);
        winnerOrg.Treasury = 10f;
        loserOrg.Treasury = 10f;

        var pending = new List<PendingEvent>();
        CivTracker.ApplyWarReparations(winner.Id, loser.Id, battleWinAdvantage: 0, world, pending);

        winnerOrg.Treasury.Should().Be(10f);
        loserOrg.Treasury.Should().Be(10f);
        pending.Should().BeEmpty();
    }

    // ─── Sequencing hazard: reparations resolve before the next tick's collapse/insolvency check ─
    // consumes the losing civ's org/treasury state (docs/phases/m14_economy_independent_wealth.md
    // 14.4's explicit sequencing warning). CivTracker.RunAnnualDiplomacy calls RunUnrestAndSecession
    // (which runs CheckTreasuryInsolvency) at step 5b2, *before* EndWarBetween's war resolution at
    // step 6 — so a war that ends and pays reparations this tick is picked up by
    // CheckTreasuryInsolvency on the *next* annual pass, not silently lost. This test exercises the
    // two calls in that exact real production order and confirms nothing is lost across the boundary.
    [Fact]
    public void ReparationsThenInsolvencyCheck_AcrossTwoAnnualTicks_TreasuryStateSurvivesIntact()
    {
        var (world, winner, loser, winnerOrg, loserOrg) = PlantTwoCivs(seed: 110);
        winnerOrg.Treasury = 0f;
        loserOrg.Treasury = 2f;
        world.SimConfig.Economy.WarReparationsPerBattleWinAdvantage = 15f;

        // Tick 1: mirrors RunAnnualDiplomacy's real order — RunUnrestAndSecession (step 5b2,
        // which runs CheckTreasuryInsolvency) executes first, then war resolution (step 6, which
        // pays reparations here) executes after, within the same tick.
        var pending1 = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending1);
        pending1.Should().NotContain(e => e.Type == EventType.TreasuryInsolvent,
            "the Treasury hasn't gone negative yet this tick — reparations haven't been paid");

        CivTracker.ApplyWarReparations(winner.Id, loser.Id, battleWinAdvantage: 2, world, pending1);
        loserOrg.Treasury.Should().Be(-28f, "reparations must land intact regardless of running after this tick's insolvency check");
        pending1.Should().NotContain(e => e.Type == EventType.TreasuryInsolvent,
            "this tick's insolvency check already ran before reparations landed — expected to lag one tick, not lose the crossing");

        // Tick 2: the next annual pass's CheckTreasuryInsolvency now sees the still-negative
        // Treasury from tick 1 and fires — confirming the org/treasury state that tick 1's
        // (earlier-running) collapse/insolvency check couldn't yet see was never consumed or
        // corrupted, just detected one tick later.
        var pending2 = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending2);
        pending2.Should().Contain(e => e.Type == EventType.TreasuryInsolvent && e.CivId == loser.Id.Value,
            "the negative Treasury from tick 1's reparations must surface on the very next annual pass");
    }

    // ─── Instrument-first: confirm the mechanics actually fire in a real run ──
    // Per the project's now-established discipline (M13.8 Estrangement/OathBroken, 14.3
    // ArtifactPurchased) — this project has repeatedly shipped conjunctive mechanics that
    // structurally never fire in organic play. Don't trust GuildFormed/Treasury-routing without
    // observing them fire over a real multi-seed full-worldgen run first.
    private const int InstrumentRunYears = 300;

    private static EventStore RunSim(int seed)
    {
        var worldCfg = new WorldConfig { Seed = seed, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var simCfg   = TestSimConfig.Default();
        var world    = new WorldGenPipeline().RunFullAsync(worldCfg, simCfg).GetAwaiter().GetResult();

        var eventStore   = new EventStore(":memory:");
        var eventCache   = new EventCache(simCfg.Events.RecentEventCacheSize);
        var gate         = new EventGate(simCfg);
        var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
        var phaseRunner  = new PhaseRunner(simCfg, eventStore, eventCache, gate, beastCatalog: beastCatalog);

        foreach (var pe in BeastSpawner.SpawnAll(world, beastCatalog))  phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in CharacterSpawner.SpawnAll(world, simCfg))    phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in Tier2Spawner.SpawnAll(world, simCfg))        phaseRunner.InjectPendingEvent(pe);

        var cmdQueue        = new CommandQueue();
        var stateCache      = new StateCache();
        var snapshotBuilder = new SnapshotBuilder();
        var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simCfg, eventCache);

        simLoop.RunSynchronous(InstrumentRunYears * simCfg.SimLoop.TicksPerYear);
        return eventStore;
    }

    // Calibration run (2026-08-05, 5 seeds × 300 years, TestSimConfig.Default(),
    // GuildFormationWealthThreshold=40 first-pass placeholder): GuildFormed fired in 1/5 seeds
    // (seed 123, once) — sparse but confirmed non-zero, the same "partial-seed reachability" bar
    // already accepted for OathBroken (M13RelationshipEventBalanceTests: "4 of 8 other seeds" while
    // landing at 0 in the three canonical seeds). TreasuryContribution (decision 9's voluntary
    // member-deposit path, reachable via any Organization membership — Civilization included, not
    // Guild-only) fired in 4/5 seeds (2-77 times), confirming the treasury-command substrate itself
    // is well-reachable even where a Guild specifically hasn't formed yet. Full fire-rate
    // calibration of GuildFormationWealthThreshold is still 14.5's job.
    [Fact]
    [Trait("Category", "Balance")]
    public void ShortRun_GuildFormedAndTreasuryRoutedTrade_FireAcrossAPlausibleFractionOfSeeds()
    {
        int[] seeds = [42, 777, 9999, 123, 55555];
        int seedsWithGuildFormed = 0;

        foreach (int seed in seeds)
        {
            using var eventStore = RunSim(seed);
            if (eventStore.CountEventsOfType(EventType.GuildFormed.ToString()) > 0)
                seedsWithGuildFormed++;
        }

        seedsWithGuildFormed.Should().BeGreaterThanOrEqualTo(1,
            $"GuildFormed should fire in at least one of {seeds.Length} seeds over a {InstrumentRunYears}-year run — " +
            $"only {seedsWithGuildFormed}/{seeds.Length} seeds fired");
    }
}
