using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M14 14.1 — wires Wealth into Tier2BehaviorPhase.RunMerchant's existing one-shot trade as a
/// real, priced source/sink (see docs/phases/m14_economy_independent_wealth.md, decisions 4, 7,
/// 8, 9, and the Opus-review MerchantHomeCutFraction addition). Covers: priced debit of the
/// destination's precious-commodity ResourceStores, credit of the merchant's personal Wealth net
/// of the home-settlement recirculation cut, GlobalPriceIndex/LocalScarcityMultiplier sensitivity,
/// the natural scarcity gate (never overdraws / never goes negative), and — per the M13.8
/// Estrangement/OathBroken lesson explicitly flagged in the phase doc — an integration check that
/// a merchant's Wealth actually increases over a short simulated run.
/// </summary>
public class MerchantTradeWealthTests
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

    /// <summary>
    /// Builds a minimal two-settlement world: home has a surplus of exactly one resource ("iron")
    /// so RunMerchant's routing deterministically picks it and the sole other settlement as the
    /// trade destination, and forces MerchantTradeChance so the trade always fires.
    /// </summary>
    private static (WorldState world, TileCoord home, TileCoord dest, Tier2Character merchant) BuildTradeWorld(
        float homeIron, float destGold, float? globalPriceIndex = null,
        IReadOnlyDictionary<string, float>? destLedger = null)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        world.SimConfig.Character.MerchantTradeChance = 1f;
        if (globalPriceIndex is { } gpi) world.GlobalPriceIndex = gpi;
        // M14 14.4 — this suite tests the 14.1 payment path in isolation; keep the orthogonal
        // Guild-formation mechanic (RunGuildFormation) from promoting the merchant away mid-test
        // (a single trade at this test's deliberately large ResourceStores scale easily clears the
        // production default). See GuildTreasuryTests for Guild-formation's own coverage.
        world.SimConfig.Economy.GuildFormationWealthThreshold = float.MaxValue;

        var home = FindLandTile(world);
        var dest = FindLandTile(world, exclude: home, minDist: 3);

        world.Settlements[home] = new SettlementStub(
            FounderId: new EntityId(1), CivId: default, Tile: home, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Home",
            ResourceStores: new Dictionary<string, float> { ["iron"] = homeIron });

        world.Settlements[dest] = new SettlementStub(
            FounderId: new EntityId(2), CivId: default, Tile: dest, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Dest",
            ResourceStores: new Dictionary<string, float> { ["gold"] = destGold },
            ResourceLedger: destLedger);

        var merchant = new Tier2Character(
            id: new EntityId(9001), location: home, name: "Merchant",
            personality: PersonalityVector6.Default,
            livelihood: new LivelihoodData(Tier2Role.Merchant, null, home, 0.5f),
            maxHealth: 100, maxAgeSeason: 1000);
        world.Entities.Add(merchant);

        return (world, home, dest, merchant);
    }

    [Fact]
    public void RunMerchant_PaysMerchantWealth_AndRecirculatesHomeCut()
    {
        var (world, home, dest, merchant) = BuildTradeWorld(homeIron: 100f, destGold: 100f, globalPriceIndex: 0.5f);
        var cfg = world.SimConfig.Economy;

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        // transfer = 100 * MerchantTradeTransfer(0.1) = 10 units of iron
        float transfer = 100f * world.SimConfig.Character.MerchantTradeTransfer;
        float unitPrice = cfg.GetBaseValue("iron") * 1f /* neutral scarcity, no dest ledger entry */ * 0.5f;
        float totalValue = unitPrice * transfer;
        float goldUnitsDebited = totalValue / cfg.GetBaseValue("gold");
        float homeCutValue = totalValue * cfg.MerchantHomeCutFraction;
        float merchantShare = totalValue - homeCutValue;

        world.Settlements[dest].GetStore("gold").Should().BeApproximately(100f - goldUnitsDebited, 0.001f,
            "destination should be debited exactly the priced value of the traded iron, in gold units");
        merchant.Wealth.Should().BeApproximately(merchantShare, 0.001f,
            "merchant's personal Wealth should be credited the paid value net of the home-settlement cut");
        world.Settlements[home].GetStore("gold").Should().BeApproximately(homeCutValue / cfg.GetBaseValue("gold"), 0.001f,
            "home settlement should recirculate MerchantHomeCutFraction of the paid value, in the same commodity debited");
    }

    [Fact]
    public void RunMerchant_InsufficientDestinationGold_PaysPartial_NeverNegative()
    {
        // Dest has far less gold than the trade's full price requires.
        var (world, home, dest, merchant) = BuildTradeWorld(homeIron: 100f, destGold: 0.1f, globalPriceIndex: 0.5f);
        var cfg = world.SimConfig.Economy;

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        world.Settlements[dest].GetStore("gold").Should().BeGreaterThanOrEqualTo(0f,
            "destination's gold store must never go negative even when it can't fully afford the trade");
        world.Settlements[dest].GetStore("gold").Should().BeApproximately(0f, 0.001f,
            "destination should spend all of its available gold rather than overdraw or underpay");

        // Full price would require far more gold value than 0.1 units of gold provide, so the
        // merchant's credited share must be capped at what the destination could actually pay.
        float fullPrice = cfg.GetBaseValue("iron") * 1f * 0.5f * (100f * world.SimConfig.Character.MerchantTradeTransfer);
        float availableValue = 0.1f * cfg.GetBaseValue("gold");
        merchant.Wealth.Should().BeApproximately(availableValue * (1f - cfg.MerchantHomeCutFraction), 0.001f,
            "merchant should only be paid out of what the destination could actually afford");
        merchant.Wealth.Should().BeLessThan(fullPrice,
            "a scarcity-gated partial payment must be strictly less than the full formulaic price");
    }

    [Fact]
    public void RunMerchant_PriceScalesWith_GlobalPriceIndex_AndLocalScarcity()
    {
        // Higher GlobalPriceIndex -> strictly higher paid value (more gold debited from a
        // destination with ample reserves) for an otherwise-identical trade.
        var (lowIdxWorld, _, lowIdxDest, lowIdxMerchant) =
            BuildTradeWorld(homeIron: 100f, destGold: 1000f, globalPriceIndex: 0.25f);
        new Tier2BehaviorPhase(lowIdxWorld.SimConfig).Execute(lowIdxWorld, tick: 0);

        var (highIdxWorld, _, highIdxDest, highIdxMerchant) =
            BuildTradeWorld(homeIron: 100f, destGold: 1000f, globalPriceIndex: 2.0f);
        new Tier2BehaviorPhase(highIdxWorld.SimConfig).Execute(highIdxWorld, tick: 0);

        highIdxMerchant.Wealth.Should().BeGreaterThan(lowIdxMerchant.Wealth,
            "a higher GlobalPriceIndex should scale up the priced trade value and thus the merchant's payout");

        // A destination in local *surplus* of the traded resource (ledger ratio > 1) should pay
        // less than one in local *deficit* (ratio < 1), all else equal — LocalScarcityMultiplier
        // is inversely related to the ledger ratio.
        var (surplusWorld, _, _, surplusMerchant) = BuildTradeWorld(
            homeIron: 100f, destGold: 1000f, globalPriceIndex: 1f,
            destLedger: new Dictionary<string, float> { ["iron"] = 2.0f }); // surplus -> low multiplier
        new Tier2BehaviorPhase(surplusWorld.SimConfig).Execute(surplusWorld, tick: 0);

        var (deficitWorld, _, _, deficitMerchant) = BuildTradeWorld(
            homeIron: 100f, destGold: 1000f, globalPriceIndex: 1f,
            destLedger: new Dictionary<string, float> { ["iron"] = 0.1f }); // deficit -> high multiplier
        new Tier2BehaviorPhase(deficitWorld.SimConfig).Execute(deficitWorld, tick: 0);

        deficitMerchant.Wealth.Should().BeGreaterThan(surplusMerchant.Wealth,
            "a settlement genuinely short on the traded good should pay more (LocalScarcityMultiplier)");
    }

    // ── Integration: verify the money actually moves over a short run ──────────────────────────
    // Per the phase doc's explicit warning (informed by the M13.8 Estrangement/OathBroken lesson):
    // "verify the money actually moves before assuming the formula is reachable."
    [Fact]
    public void MerchantWealth_ActuallyIncreases_OverShortSimulatedRun()
    {
        var (world, home, dest, merchant) = BuildTradeWorld(homeIron: 500f, destGold: 500f, globalPriceIndex: 0.5f);
        var phase = new Tier2BehaviorPhase(world.SimConfig);

        float initialWealth = merchant.Wealth;
        for (long tick = 0; tick < 20 && merchant.IsAlive; tick++)
        {
            // Replenish home's iron each tick so repeated trades keep firing rather than
            // exhausting the source after one transfer (this test targets the payment path,
            // not the pre-existing physical-goods-supply mechanic).
            var homeStub = world.Settlements[home];
            var stores = new Dictionary<string, float>(homeStub.ResourceStores!) { ["iron"] = 500f };
            world.Settlements[home] = homeStub with { ResourceStores = stores };

            phase.Execute(world, tick);
        }

        merchant.Wealth.Should().BeGreaterThan(initialWealth,
            "a merchant running repeated trades against a destination with gold reserves must " +
            "actually accumulate Wealth — the mechanic must be reachable in the real simulation, " +
            "not just correct in isolation (M13.8 Estrangement/OathBroken lesson)");
    }
}
