using System.Reflection;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Economy;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M14 14.0 — Wealth substrate: seeded pricing, GlobalPriceIndex EMA, personal balances, death
/// disposition. See docs/phases/m14_economy_independent_wealth.md.
/// </summary>
public class EconomyWealthSubstrateTests : IDisposable
{
    private readonly string _saveDir = Path.Combine(Path.GetTempPath(), $"m14_wealth_save_test_{Guid.NewGuid():N}");

    public void Dispose() => WorldStateSaver.DeleteSave(_saveDir);

    private static TileCoord FindLandTile(WorldState world)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        throw new Exception("no land tile found");
    }

    private static Tier1Character SpawnAt(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        return c;
    }

    // ─── DTO round-trip: Wealth (Tier1 + Tier2) and Notability (Tier2) ─────────────────────────

    [Fact]
    public void Tier1Wealth_RoundTripsThroughSaveLoad()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 501);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 1L);
        c.AddWealth(42.5f);

        WorldStateSaver.Save(world, _saveDir, world.SimConfig);
        var loaded = WorldStateSaver.Load(_saveDir, world.SimConfig);

        var loadedChar = loaded.Entities.Characters.First(ch => ch.Id == c.Id);
        loadedChar.Wealth.Should().BeApproximately(42.5f, 0.001f);
    }

    [Fact]
    public void Tier2WealthAndNotability_RoundTripThroughSaveLoad()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 502);
        var tile = FindLandTile(world);
        var t2 = new Tier2Character(EntityId.New(), tile, "T2Wealthy", PersonalityVector6.Default,
            new LivelihoodData(Tier2Role.Merchant, null, tile, 0.5f), maxHealth: 100, maxAgeSeason: 800);
        t2.AddWealth(17f);
        t2.GainNotability(0.4f);
        world.Entities.Add(t2);

        WorldStateSaver.Save(world, _saveDir, world.SimConfig);
        var loaded = WorldStateSaver.Load(_saveDir, world.SimConfig);

        var loadedT2 = loaded.Entities.Tier2Chars.First(ch => ch.Id == t2.Id);
        loadedT2.Wealth.Should().BeApproximately(17f, 0.001f);
        loadedT2.Notability.Should().BeApproximately(0.4f, 0.001f,
            "M13.8.2's Notability was never given DTO/mapper coverage before M14 14.0 — confirmed bug fixed alongside Wealth");
    }

    [Fact]
    public void OrganizationTreasuryAndHomeSettlement_RoundTripThroughSaveLoad()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 503);
        var tile = FindLandTile(world);
        var founder = SpawnAt(world, tile, 1L);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending);

        var civ = world.Civilizations.Values.First();
        var org = world.Organizations[civ.OrgId!.Value];
        org.Treasury = 123f;

        WorldStateSaver.Save(world, _saveDir, world.SimConfig);
        var loaded = WorldStateSaver.Load(_saveDir, world.SimConfig);

        var loadedOrg = loaded.Organizations[org.Id];
        loadedOrg.Treasury.Should().Be(123f);
        loadedOrg.HomeSettlementCoord.Should().Be(tile, "the founding tile is known at civ-founding time");
    }

    [Fact]
    public void GlobalPriceIndex_RoundTripsThroughSaveLoad()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 504);
        var setter = typeof(WorldState).GetProperty(nameof(WorldState.GlobalPriceIndex))!;
        setter.SetValue(world, 1.75f);

        WorldStateSaver.Save(world, _saveDir, world.SimConfig);
        var loaded = WorldStateSaver.Load(_saveDir, world.SimConfig);

        loaded.GlobalPriceIndex.Should().BeApproximately(1.75f, 0.0001f);
    }

    // ─── LocalScarcityMultiplier clamping ───────────────────────────────────────────────────────

    [Fact]
    public void LocalScarcityMultiplier_SurplusResource_ClampsAtMin()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 505);
        var cfg = world.SimConfig.Economy;
        var stub = new SettlementStub(EntityId.New(), new CivId(1), new TileCoord(0, 0), 1, 100, 100,
            ResourceLedger: new Dictionary<string, float> { ["gold"] = 100f }); // huge surplus ratio

        float mult = PricingService.LocalScarcityMultiplier(stub, "gold", cfg);
        mult.Should().Be(cfg.LocalScarcityMultiplierMin);
    }

    [Fact]
    public void LocalScarcityMultiplier_DeficitResource_ClampsAtMax()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 506);
        var cfg = world.SimConfig.Economy;
        var stub = new SettlementStub(EntityId.New(), new CivId(1), new TileCoord(0, 0), 1, 100, 100,
            ResourceLedger: new Dictionary<string, float> { ["gold"] = 0.001f }); // severe deficit

        float mult = PricingService.LocalScarcityMultiplier(stub, "gold", cfg);
        mult.Should().Be(cfg.LocalScarcityMultiplierMax);
    }

    [Fact]
    public void LocalScarcityMultiplier_BalancedRatio_ReturnsNearOne()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 507);
        var cfg = world.SimConfig.Economy;
        var stub = new SettlementStub(EntityId.New(), new CivId(1), new TileCoord(0, 0), 1, 100, 100,
            ResourceLedger: new Dictionary<string, float> { ["gold"] = 1f });

        float mult = PricingService.LocalScarcityMultiplier(stub, "gold", cfg);
        mult.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void LocalScarcityMultiplier_MissingResourceKey_DefaultsToRatioOne()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 508);
        var cfg = world.SimConfig.Economy;
        var stub = new SettlementStub(EntityId.New(), new CivId(1), new TileCoord(0, 0), 1, 100, 100,
            ResourceLedger: new Dictionary<string, float>());

        float mult = PricingService.LocalScarcityMultiplier(stub, "gold", cfg);
        mult.Should().BeApproximately(1f, 0.001f);
    }

    // ─── GlobalPriceIndex EMA ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EconomyPhase_RunAnnual_MovesIndexTowardTarget()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 509);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 1L);
        // High per-capita Wealth relative to ReferenceMoneySupplyPerCapita pushes the target above
        // the seeded starting index.
        c.AddWealth(world.SimConfig.Economy.ReferenceMoneySupplyPerCapita * 10f);

        float before = world.GlobalPriceIndex;
        EconomyPhase.RunAnnual(world, world.SimConfig);
        float after = world.GlobalPriceIndex;

        after.Should().BeGreaterThan(before, "a high money-supply-per-capita should pull the index upward");
    }

    [Fact]
    public void EconomyPhase_RunAnnual_StaysWithinClampBand()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 510);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 1L);
        c.AddWealth(1_000_000f); // absurdly large — target would blow past PriceIndexMax without the clamp

        for (int i = 0; i < 50; i++)
            EconomyPhase.RunAnnual(world, world.SimConfig);

        world.GlobalPriceIndex.Should().BeLessThanOrEqualTo(world.SimConfig.Economy.PriceIndexMax);
        world.GlobalPriceIndex.Should().BeGreaterThanOrEqualTo(world.SimConfig.Economy.PriceIndexMin);
    }

    [Fact]
    public void EconomyPhase_RunAnnual_AppliesPersonalWealthSpoilage()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 511);
        var tile = FindLandTile(world);
        var c = SpawnAt(world, tile, 1L);
        c.AddWealth(100f);

        EconomyPhase.RunAnnual(world, world.SimConfig);

        float expected = 100f * (1f - world.SimConfig.Economy.PersonalWealthSpoilageRate);
        c.Wealth.Should().BeApproximately(expected, 0.01f);
    }

    [Fact]
    public void EconomyPhase_TotalMoneySupply_IncludesWealthDrops()
    {
        // A standing WealthDrop must move the index the same way personal Wealth would — confirms
        // decision 5's revision (drops counted in TotalMoneySupply, not a measurement leak).
        var world = WorldTestHelper.CreateSmallWorld(seed: 512);
        var tile = FindLandTile(world);
        world.WealthDrops.Add(new WealthDrop(tile, world.SimConfig.Economy.ReferenceMoneySupplyPerCapita * 10f, 0));
        // Give the world at least one population unit so per-capita isn't dividing by the drop's
        // own nonexistent population.
        SpawnAt(world, tile, 1L);

        float before = world.GlobalPriceIndex;
        EconomyPhase.RunAnnual(world, world.SimConfig);

        world.GlobalPriceIndex.Should().BeGreaterThan(before, "a large standing WealthDrop must count toward TotalMoneySupply");
    }

    [Fact]
    public void EconomyPhase_RunAnnual_SpoilsAndPrunesWealthDrops()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 513);
        var tile = FindLandTile(world);
        world.WealthDrops.Add(new WealthDrop(tile, 100f, 0));

        EconomyPhase.RunAnnual(world, world.SimConfig);

        world.WealthDrops.Should().ContainSingle();
        float expected = 100f * (1f - world.SimConfig.Economy.PersonalWealthSpoilageRate);
        world.WealthDrops[0].Amount.Should().BeApproximately(expected, 0.01f);
    }

    [Fact]
    public void EconomyPhase_RunAnnual_PrunesNegligibleWealthDrops()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 514);
        var tile = FindLandTile(world);
        world.WealthDrops.Add(new WealthDrop(tile, 0.005f, 0)); // below the 0.01 prune floor

        EconomyPhase.RunAnnual(world, world.SimConfig);

        world.WealthDrops.Should().BeEmpty();
    }

    // ─── TransferWealthOnDeath ──────────────────────────────────────────────────────────────────

    private static void KillViaReflection(CharacterBehaviorPhase phase, Tier1Character c, WorldState world, List<PendingEvent> pending) =>
        typeof(CharacterBehaviorPhase)
            .GetMethod("KillCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(phase, new object[] { c, world, "test", pending });

    [Fact]
    public void TransferWealthOnDeath_WithEligibleHeir_SplitsByInheritanceShare()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 515);
        var tile = FindLandTile(world);
        var deceased = SpawnAt(world, tile, 61L);
        var spouse   = SpawnAt(world, tile, 62L);
        deceased.AgeSeason = world.SimConfig.Family.MarriageMinAgeSeasons + 10;
        spouse.AgeSeason   = world.SimConfig.Family.MarriageMinAgeSeasons + 10;

        CivTracker.Resolve(new ProposeMarriage(deceased.Id, spouse.Id), world, new List<PendingEvent>());
        deceased.AddWealth(100f);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        KillViaReflection(phase, deceased, world, pending);

        var econCfg = world.SimConfig.Economy;
        spouse.Wealth.Should().BeApproximately(100f * econCfg.WealthInheritanceShare, 0.01f);
        deceased.Wealth.Should().Be(0f);

        float expectedDrop = 100f * (1f - econCfg.WealthInheritanceShare);
        world.WealthDrops.Should().ContainSingle(d => d.Location == deceased.Location);
        world.WealthDrops[0].Amount.Should().BeApproximately(expectedDrop, 0.01f);
    }

    [Fact]
    public void TransferWealthOnDeath_NoEligibleHeir_DropsInFull()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 516);
        var tile = FindLandTile(world);
        var deceased = SpawnAt(world, tile, 71L);
        deceased.AddWealth(50f); // no family/spouse — FindHeir returns null

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        KillViaReflection(phase, deceased, world, pending);

        deceased.Wealth.Should().Be(0f);
        world.WealthDrops.Should().ContainSingle(d => d.Location == deceased.Location);
        world.WealthDrops[0].Amount.Should().BeApproximately(50f, 0.01f);
    }

    [Fact]
    public void TransferWealthOnDeath_ZeroWealth_IsNoOp()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 517);
        var tile = FindLandTile(world);
        var deceased = SpawnAt(world, tile, 81L);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var pending = new List<PendingEvent>();
        KillViaReflection(phase, deceased, world, pending);

        world.WealthDrops.Should().BeEmpty();
    }

    [Fact]
    public void ClaimWealthDrops_CoLocatedLivingCharacter_ClaimsWholePool()
    {
        // Invoke the claim step directly (rather than a full phase.Execute tick) so this test
        // isn't at the mercy of the utility scorer choosing to move the character away from the
        // drop's tile before the claim check runs later in the same tick.
        var world = WorldTestHelper.CreateSmallWorld(seed: 518);
        var tile = FindLandTile(world);
        world.WealthDrops.Add(new WealthDrop(tile, 30f, 0));
        var claimant = SpawnAt(world, tile, 91L);

        typeof(CharacterBehaviorPhase)
            .GetMethod("ClaimWealthDrops", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { world });

        claimant.Wealth.Should().BeApproximately(30f, 0.01f);
        world.WealthDrops.Should().BeEmpty();
    }
}
