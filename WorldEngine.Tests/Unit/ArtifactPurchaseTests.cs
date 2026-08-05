using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Economy;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M14 14.3 — goal fulfillment via trade: <see cref="PurchaseArtifact"/>/
/// <see cref="ArtifactPurchaseResolver"/>, extending GoalManager's CovetArtifact resolution with a
/// purchase path alongside (not replacing) the existing claim-if-Lost path. See
/// docs/phases/m14_economy_independent_wealth.md decisions 3, 7, 8.
///
/// **Instrument-first finding (2026-08-05):** a full-worldgen run (5 seeds, 300 years,
/// TestSimConfig.Default()) initially showed <c>ArtifactPurchased</c> firing 0 times in every seed —
/// not because the purchase logic was wrong, but because no living Tier1Character ever held any
/// Wealth at all. Root cause: 14.1's <c>Tier2BehaviorPhase.ResolveMerchantTrade</c> only debited a
/// destination's gold/silver/gems reserves, and those three commodities essentially never populate
/// any settlement's ResourceStores at this world-generation scale (gold deposit tiles exist but
/// never land inside a settlement's owned territory) — so Wealth never entered circulation at all,
/// for Tier1 or Tier2. Two fixes, both landed in this same pass: (1) broadened
/// EconomyConfig.MoneyEquivalentCommodities to include iron/copper (far more commonly mined,
/// confirmed by MerchantTradeCompleted's high fire counts), which made TradePaid/Wealth-as-a-whole
/// reachable; (2) extended Tier2BehaviorPhase's dead-Tier2 handling to drop a dying merchant's
/// Wealth as a WealthDrop (decision 5's existing mechanism, previously scoped to Tier1Character
/// death only) so Wealth can reach a Tier1 CovetArtifact buyer at all. After both fixes plus
/// lowering EconomyConfig.ArtifactValueMultiplier (3.0 → 1.5, since GlobalPriceIndex is
/// floor-pinned at 0.25 for the first few centuries per decision 8's documented warm-up transient,
/// and the still-small early Wealth pool made even the cheapest artifact category unaffordable at
/// 3.0), a re-run fired ArtifactPurchased in 3 of 5 seeds (42, 9999, 123 — up to 4 times in one
/// seed), with the other two seeds (777, 55555) landing at 0 for plausible reasons unrelated to the
/// purchase gate itself (777 had zero Wealth in circulation at all that run; 55555 had a small
/// nonzero pool but apparently no CovetArtifact goal ever lined up against a live, affordable,
/// willing target in that particular seed/run). This 3-of-5 partial-seed reachability evidence
/// mirrows the precedent already accepted for OathBroken (M13RelationshipEventBalanceTests: "confirmed
/// firing... in 4 of 8 other seeds sampled" while landing at 0 in the three canonical seeds) — full
/// calibration of the fire rate is 14.5's job, not this phase's; what matters here is confirming the
/// mechanic is reachable at all, not tuning its exact frequency.
/// </summary>
public class ArtifactPurchaseTests
{
    private static TileCoord FindLandTile(WorldState world)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        throw new InvalidOperationException("no land tile found");
    }

    private static Tier1Character SpawnAt(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        return c;
    }

    /// <summary>Personality is set-once at construction (no setter, no `with`-friendly mutation
    /// path exposed on Tier1Character) — spawn directly with a chosen Compassion for willingness
    /// tests rather than mutating a CharacterFactory-spawned instance.</summary>
    private static Tier1Character SpawnWithCompassion(WorldState world, TileCoord tile, long id, float compassion)
    {
        var identity = new IdentityData("TestOwner", "", "human", null, null, world.CurrentYear, 0);
        var personality = PersonalityVector.Default with { Compassion = compassion };
        var c = new Tier1Character(new EntityId(id), tile, personality, AptitudeVector.Default, SkillVector.Default,
            identity, maxHealth: 100, maxAgeSeason: 1000);
        world.Entities.Add(c);
        return c;
    }

    private static Artifact MakeArtifact(WorldState world, ArtifactOwner owner, ArtifactCategory category = ArtifactCategory.Relic, float quality = 0.8f) =>
        ArtifactRegistry.Create(world, "Test Artifact", category, world.CurrentYear, 0, "nobody", "masterwork", quality, owner);

    // ─── Pricing formula ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ArtifactEffectivePrice_MatchesFormula_CategoryTimesQualityTimesMultiplierTimesIndex()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 61);
        var cfg = world.SimConfig.Economy;
        var artifact = MakeArtifact(world, ArtifactOwner.Lost, ArtifactCategory.Tome, quality: 0.75f);

        float expectedBase = cfg.GetArtifactCategoryBaseValue(ArtifactCategory.Tome) * 0.75f;
        PricingService.ArtifactBaseValue(artifact, cfg).Should().BeApproximately(expectedBase, 0.0001f);

        float expectedPrice = expectedBase * cfg.ArtifactValueMultiplier * 0.6f;
        PricingService.ArtifactEffectivePrice(artifact, cfg, globalPriceIndex: 0.6f).Should().BeApproximately(expectedPrice, 0.0001f);
    }

    [Fact]
    public void GetArtifactCategoryBaseValue_UnlistedCategory_FallsBackToDefault()
    {
        var cfg = new WorldEngine.Sim.Config.EconomyConfig();
        cfg.ArtifactCategoryBaseValue.Clear();
        cfg.DefaultArtifactCategoryBaseValue = 42f;

        cfg.GetArtifactCategoryBaseValue(ArtifactCategory.Weapon).Should().Be(42f);
    }

    // ─── Purchase resolution: happy path (Character owner) ─────────────────────────────────────

    [Fact]
    public void TryResolve_CharacterOwner_TransfersWealthExactlyAndOwnership()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 62);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 1L);
        var owner = SpawnWithCompassion(world, tile, 20002, compassion: 1f); // always willing

        var artifact = MakeArtifact(world, ArtifactOwner.OfCharacter(owner.Id), ArtifactCategory.Artwork, quality: 0.5f);
        float price = PricingService.ArtifactEffectivePrice(artifact, cfg, world.GlobalPriceIndex);
        buyer.AddWealth(price + 5f); // comfortably affordable

        var pending = new List<PendingEvent>();
        var ok = ArtifactPurchaseResolver.TryResolve(new PurchaseArtifact(buyer.Id, artifact.Id), world, cfg, pending);

        ok.Should().BeTrue();
        buyer.Wealth.Should().BeApproximately(5f, 0.001f, "buyer should lose exactly the price, nothing more or less");
        owner.Wealth.Should().BeApproximately(price, 0.001f, "owner should gain exactly what the buyer paid — a real two-sided transfer");
        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Character);
        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(buyer.Id.Value);
        pending.Should().Contain(pe => pe.Type == EventType.ArtifactPurchased);
    }

    [Fact]
    public void TryResolve_SettlementOwner_CreditsResourceStores_AndTransfersOwnership()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 63);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 3L);

        var settlementTile = FindLandTile(world);
        world.Settlements[settlementTile] = new SettlementStub(
            FounderId: new EntityId(999), CivId: default, Tile: settlementTile, FoundedYear: 0,
            Population: 50, Health: 100, Name: "TestTown");

        var artifact = MakeArtifact(world, ArtifactOwner.OfSettlement(settlementTile), ArtifactCategory.Regalia, quality: 0.6f);
        float price = PricingService.ArtifactEffectivePrice(artifact, cfg, world.GlobalPriceIndex);
        buyer.AddWealth(price + 1f);

        var pending = new List<PendingEvent>();
        var ok = ArtifactPurchaseResolver.TryResolve(new PurchaseArtifact(buyer.Id, artifact.Id), world, cfg, pending);

        ok.Should().BeTrue();
        buyer.Wealth.Should().BeApproximately(1f, 0.001f);
        float expectedGoldUnits = price / cfg.GetBaseValue("gold");
        world.Settlements[settlementTile].GetStore("gold").Should().BeApproximately(expectedGoldUnits, 0.001f,
            "the buyer's Wealth should convert into the settlement's gold-equivalent ResourceStores (decision 4's reversed conversion)");
        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(buyer.Id.Value);
    }

    // ─── Gates ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryResolve_BuyerLacksWealth_Blocked_NothingMutated()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 64);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 4L);
        var owner = SpawnWithCompassion(world, tile, 20005, compassion: 1f);

        var artifact = MakeArtifact(world, ArtifactOwner.OfCharacter(owner.Id), ArtifactCategory.Relic, quality: 1f);
        float price = PricingService.ArtifactEffectivePrice(artifact, cfg, world.GlobalPriceIndex);
        buyer.AddWealth(Math.Max(0f, price - 1f)); // just short

        var pending = new List<PendingEvent>();
        var ok = ArtifactPurchaseResolver.TryResolve(new PurchaseArtifact(buyer.Id, artifact.Id), world, cfg, pending);

        ok.Should().BeFalse();
        owner.Wealth.Should().Be(0f);
        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(owner.Id.Value, "ownership must not change when the buyer can't afford it");
        pending.Should().NotContain(pe => pe.Type == EventType.ArtifactPurchased);
    }

    [Fact]
    public void TryResolve_OwnerUnwilling_Blocked_EvenWithSufficientWealth()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 65);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 6L);
        var owner = SpawnWithCompassion(world, tile, 20007, compassion: 0f); // unwilling, no relationship Trust either

        var artifact = MakeArtifact(world, ArtifactOwner.OfCharacter(owner.Id), ArtifactCategory.Relic, quality: 0.5f);
        float price = PricingService.ArtifactEffectivePrice(artifact, cfg, world.GlobalPriceIndex);
        buyer.AddWealth(price + 1000f); // affordability is not the blocker here

        var pending = new List<PendingEvent>();
        var ok = ArtifactPurchaseResolver.TryResolve(new PurchaseArtifact(buyer.Id, artifact.Id), world, cfg, pending);

        ok.Should().BeFalse();
        owner.Wealth.Should().Be(0f);
        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(owner.Id.Value);
    }

    [Fact]
    public void TryResolve_ArtifactIsLost_Blocked_ClaimPathHandlesItInstead()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 66);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 8L);
        buyer.AddWealth(1000f);

        var artifact = MakeArtifact(world, ArtifactOwner.Lost);

        var pending = new List<PendingEvent>();
        var ok = ArtifactPurchaseResolver.TryResolve(new PurchaseArtifact(buyer.Id, artifact.Id), world, cfg, pending);

        ok.Should().BeFalse();
        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Lost);
    }

    [Fact]
    public void TryResolve_AlreadyOwnedByBuyer_Blocked()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 67);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 9L);
        buyer.AddWealth(1000f);

        var artifact = MakeArtifact(world, ArtifactOwner.OfCharacter(buyer.Id));

        var pending = new List<PendingEvent>();
        var ok = ArtifactPurchaseResolver.TryResolve(new PurchaseArtifact(buyer.Id, artifact.Id), world, cfg, pending);

        ok.Should().BeFalse();
    }

    // ─── GoalManager integration: additive, does not disturb the claim-if-Lost path ─────────────

    [Fact]
    public void UpdateGoals_PurchasesArtifact_WhenAffordableAndWilling_CompletesGoal()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 68);
        var cfg = world.SimConfig.Economy;
        var tile = FindLandTile(world);
        var buyer = SpawnAt(world, tile, 10L);
        var owner = SpawnWithCompassion(world, tile, 20011, compassion: 1f);

        var artifact = MakeArtifact(world, ArtifactOwner.OfCharacter(owner.Id), ArtifactCategory.Artwork, quality: 0.5f);
        float price = PricingService.ArtifactEffectivePrice(artifact, cfg, world.GlobalPriceIndex);
        buyer.AddWealth(price + 1f);

        buyer.Goals.Add(new GoalData
        {
            Type = GoalType.CovetArtifact, Object = GoalObject.Artifact,
            CovetedArtifactId = artifact.Id, Priority = 0.8f, Intensity = 0.8f,
            StaleSince = 0, FormedTick = 0
        });

        var pending = new List<PendingEvent>();
        GoalManager.UpdateGoals(buyer, world, currentTick: 0, world.SimConfig.Character, pending);

        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(buyer.Id.Value);
        buyer.Goals.Should().Contain(g => g.Type == GoalType.CovetArtifact && g.IsComplete);
        pending.Should().Contain(pe => pe.Type == EventType.ArtifactPurchased);
    }

    [Fact]
    public void UpdateGoals_LostArtifactClaimPath_StillWorks_Unaffected()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 69);
        var tile = FindLandTile(world);
        var claimant = SpawnAt(world, tile, 12L);

        var artifact = MakeArtifact(world, ArtifactOwner.Lost);
        claimant.Goals.Add(new GoalData
        {
            Type = GoalType.CovetArtifact, Object = GoalObject.Artifact,
            CovetedArtifactId = artifact.Id, Priority = 0.8f, Intensity = 0.8f,
            StaleSince = 0, FormedTick = 0
        });

        var pending = new List<PendingEvent>();
        GoalManager.UpdateGoals(claimant, world, currentTick: 0, world.SimConfig.Character, pending);

        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Character);
        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(claimant.Id.Value);
        claimant.Goals.Should().Contain(g => g.Type == GoalType.CovetArtifact && g.IsComplete);
    }

    // ─── Instrument-first integration: the mechanic actually fires in a real simulated run ──────
    // Per the phase doc's explicit warning (M13.8 Estrangement/OathBroken lesson): a full-worldgen,
    // multi-seed, 300-year run must show ArtifactPurchased firing at least once in a plausible
    // fraction of seeds — not just be logically correct in a hand-built unit test. See this class's
    // doc comment for the diagnosis/fix history that got this from 0/5 to 3/5 seeds.
    // Marked Balance (not run by scripts/test-fast.sh) since it's a 5-seed × 300-year full-worldgen
    // run (~90s), matching the cost profile of the other full-sim instrument tests in
    // WorldEngine.Tests/Balance/ rather than the rest of this file's sub-second unit tests.
    [Fact]
    [Trait("Category", "Balance")]
    public void ShortRun_ArtifactPurchased_FiresAcrossAPlausibleFractionOfSeeds()
    {
        int[] seeds = [42, 777, 9999, 123, 55555];
        int seedsWithAtLeastOnePurchase = 0;

        foreach (int seed in seeds)
        {
            using var eventStore = RunSim(seed);
            if (eventStore.CountEventsOfType(EventType.ArtifactPurchased.ToString()) > 0)
                seedsWithAtLeastOnePurchase++;
        }

        seedsWithAtLeastOnePurchase.Should().BeGreaterThanOrEqualTo(2,
            $"ArtifactPurchased should fire in a plausible fraction of seeds (observed 3/5 at calibration time), " +
            $"not be structurally unreachable — only {seedsWithAtLeastOnePurchase}/{seeds.Length} seeds fired");
    }

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
        phaseRunner.FlushPendingEvents(world);

        return eventStore;
    }
}
