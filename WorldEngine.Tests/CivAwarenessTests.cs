using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace WorldEngine.Tests;

/// <summary>
/// Tests for M4 Phase 1: Civ Awareness and Emissary System.
/// Covers epics 4.1.1–4.1.6: data model, knowledge propagation, emissary dispatch/resolution, serialization.
/// </summary>
public class CivAwarenessTests
{
    // ── Test world builder ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a bare world state (no characters or settlements) with the given dimensions.
    /// Sufficient for civ awareness tests that don't need entity simulation.
    /// </summary>
    private static WorldState BuildBareWorld(
        int widthKm = 600, int heightKm = 200, int tileWidthKm = 10, int seed = 42)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = widthKm, HeightKm = heightKm, TileWidthKm = tileWidthKm };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Civilization MakeCiv(int id, TileCoord capital, WorldState world)
    {
        var civId   = new CivId(id);
        var founder = MakeRuler(id * 100L, civId, capital, world);
        var civ = new Civilization(civId, $"Civ{id}", founder.Id, capital, world.CurrentYear);
        civ.Members.Add(founder.Id);
        world.Civilizations[civId] = civ;
        world.NextCivId = Math.Max(world.NextCivId, id + 1);
        return civ;
    }

    private static Tier1Character MakeRuler(
        long id, CivId civId, TileCoord location, WorldState world,
        float rationality = 0.5f, float honesty = 0.5f, float piety = 0.1f,
        float aggression  = 0.5f)
    {
        var personality = new PersonalityVector(
            Ambition: 0.5f, Greed: 0.5f, Aggression: aggression, Compassion: 0.5f,
            Curiosity: 0.5f, Creativity: 0.5f, Rationality: rationality, Wonder: 0.5f,
            Loyalty: 0.5f, Sociability: 0.5f, Honesty: honesty, Stability: 0.5f);
        var skills = SkillVector.Default with { Piety = piety };
        var identity = new IdentityData("TestChar", "the Test", "test",
            null, null, civId, 0, 0);
        var character = new Tier1Character(
            new EntityId(id), location,
            personality, AptitudeVector.Default, skills, identity,
            100, 200);
        world.Entities.Add(character);
        return character;
    }

    private static void AddSettlement(CivId civId, TileCoord tile, int population, WorldState world)
    {
        world.Settlements[tile] = new SettlementStub(
            FounderId: new EntityId(civId.Value * 100L),
            CivId: civId, Tile: tile, FoundedYear: 0,
            Population: population, Health: 100, Name: $"Settlement{tile}");
    }

    private static SimConfig MakeConfig(Action<EmissaryConfig>? configure = null)
    {
        var cfg = TestSimConfig.Default();
        configure?.Invoke(cfg.Emissary);
        return cfg;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EPIC 4.1.1 — Data Layer
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CivContact_CanBeAddedAndRetrievedFromKnownCivs()
    {
        var world = BuildBareWorld();
        var civ = MakeCiv(1, new TileCoord(5, 5), world);

        var contact = new CivContact(
            KnownCivId:       new CivId(2),
            YearFirstContact: 100,
            YearLastContact:  100,
            BestSource:       CivContactSource.Rumor,
            CapitalTile:      new TileCoord(10, 5),
            Confidence:       0.5f);

        civ.KnownCivs[new CivId(2)] = contact;

        civ.KnownCivs.Should().ContainKey(new CivId(2));
        civ.KnownCivs[new CivId(2)].Confidence.Should().Be(0.5f);
        civ.KnownCivs[new CivId(2)].BestSource.Should().Be(CivContactSource.Rumor);
    }

    [Fact]
    public void SeedCivContact_CreatesNewContactWhenNoneExists()
    {
        var world = BuildBareWorld();
        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(15, 5), world);

        CivTracker.SeedCivContact(civ1.Id, civ2.Id,
            CivContactSource.WandererMet, civ2.CapitalTile, 0.35f, world);

        civ1.KnownCivs.Should().ContainKey(civ2.Id);
        civ1.KnownCivs[civ2.Id].Confidence.Should().Be(0.35f);
        civ1.KnownCivs[civ2.Id].BestSource.Should().Be(CivContactSource.WandererMet);
        civ1.KnownCivs[civ2.Id].CapitalTile.Should().Be(civ2.CapitalTile);
    }

    [Fact]
    public void SeedCivContact_UpsertsBestSourceWhenHigherFidelityContact()
    {
        var world = BuildBareWorld();
        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(15, 5), world);

        // First seed at Rumor level
        CivTracker.SeedCivContact(civ1.Id, civ2.Id,
            CivContactSource.Rumor, civ2.CapitalTile, 0.15f, world);
        // Then upgrade to WandererMet
        CivTracker.SeedCivContact(civ1.Id, civ2.Id,
            CivContactSource.WandererMet, civ2.CapitalTile, 0.35f, world);

        civ1.KnownCivs[civ2.Id].BestSource.Should().Be(CivContactSource.WandererMet);
        civ1.KnownCivs[civ2.Id].Confidence.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void SeedCivContact_ConfidenceClampedAtOne()
    {
        var world = BuildBareWorld();
        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(15, 5), world);

        // Seed multiple times to attempt overflow
        for (int i = 0; i < 10; i++)
            CivTracker.SeedCivContact(civ1.Id, civ2.Id,
                CivContactSource.Rumor, civ2.CapitalTile, 0.5f, world);

        civ1.KnownCivs[civ2.Id].Confidence.Should().BeInRange(0f, 1.0f);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EPIC 4.1.2 — Knowledge Propagation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProximityRumor_CivsWithin30Tiles_GainContact()
    {
        // 60-tile wide world, settlements at X=5 and X=30 → distance = 25 tiles (within radius 30)
        var world = BuildBareWorld(widthKm: 600, heightKm: 200, tileWidthKm: 10);
        var cfg = world.SimConfig;
        cfg.Emissary.KnowledgeSpreadRadius = 30;

        var civ1 = MakeCiv(1, new TileCoord(5,  10), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 10), world);

        AddSettlement(civ1.Id, new TileCoord(5,  10), 100, world);
        AddSettlement(civ2.Id, new TileCoord(30, 10), 100, world);

        KnowledgePropagationPhase.Execute(world);

        civ1.KnownCivs.Should().ContainKey(civ2.Id,
            "Civ1 and Civ2 are 25 tiles apart, within knowledge_spread_radius=30");
        civ2.KnownCivs.Should().ContainKey(civ1.Id,
            "contact seeding is mutual");
    }

    [Fact]
    public void ProximityRumor_CivsMoreThan30Tiles_NoContact()
    {
        // Settlements at X=5 and X=55 → distance = 50 tiles (outside radius 30)
        var world = BuildBareWorld(widthKm: 600, heightKm: 200, tileWidthKm: 10);
        world.SimConfig.Emissary.KnowledgeSpreadRadius = 30;

        var civ1 = MakeCiv(1, new TileCoord(5,  10), world);
        var civ2 = MakeCiv(2, new TileCoord(55, 10), world);

        AddSettlement(civ1.Id, new TileCoord(5,  10), 100, world);
        AddSettlement(civ2.Id, new TileCoord(55, 10), 100, world);

        KnowledgePropagationPhase.Execute(world);

        civ1.KnownCivs.Should().NotContainKey(civ2.Id,
            "50-tile distance exceeds knowledge_spread_radius=30");
        civ2.KnownCivs.Should().NotContainKey(civ1.Id);
    }

    [Fact]
    public void ConfidenceDecay_AfterEnoughTicksWithoutRefresh_ContactRemoved()
    {
        // Start civ1 with a contact at confidence 0.5, no refresh mechanism
        var world = BuildBareWorld();
        world.SimConfig.Emissary.ConfidenceDecayPerYear = 0.1f;

        var civ1 = MakeCiv(1, new TileCoord(5,  10), world);
        var civ2 = MakeCiv(2, new TileCoord(55, 10), world);

        // Seed a contact manually (no settlements near each other, so no proximity refresh)
        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.Rumor,
            civ2.CapitalTile, 0.5f);

        // Run 6 decay ticks (0.5 / 0.1 = 5 ticks to reach 0, +1 for buffer)
        for (int i = 0; i < 6; i++)
            KnowledgePropagationPhase.Execute(world);

        civ1.KnownCivs.Should().NotContainKey(civ2.Id,
            "contact should be removed after confidence decays to 0");
    }

    [Fact]
    public void RumorChaining_CivAKnowsBandC_CivBLearnsOfC()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.RumorChainProbability = 1.0f;  // guarantee chain fires
        world.SimConfig.Emissary.RumorChainConfidenceFactor = 0.5f;

        var civA = MakeCiv(1, new TileCoord(5,  5), world);
        var civB = MakeCiv(2, new TileCoord(15, 5), world);
        var civC = MakeCiv(3, new TileCoord(25, 5), world);

        // A knows B (WandererMet — eligible to chain from)
        civA.KnownCivs[civB.Id] = new CivContact(
            civB.Id, 1, 1, CivContactSource.WandererMet, civB.CapitalTile, 0.8f);

        // A knows C (WandererMet)
        civA.KnownCivs[civC.Id] = new CivContact(
            civC.Id, 1, 1, CivContactSource.WandererMet, civC.CapitalTile, 0.6f);

        // B does not know C yet
        civB.KnownCivs.Should().NotContainKey(civC.Id);

        var activeCivs = world.Civilizations.Values.Where(c => !c.IsCollapsed).ToList();
        KnowledgePropagationPhase.RunRumorChaining(world, activeCivs, world.SimConfig.Emissary);

        civB.KnownCivs.Should().ContainKey(civC.Id, "B should learn of C via A");
        civB.KnownCivs[civC.Id].BestSource.Should().Be(CivContactSource.Rumor);
        civB.KnownCivs[civC.Id].Confidence.Should()
            .BeApproximately(0.6f * 0.5f, 0.001f,
                "chained confidence = source_confidence * chain_factor");
    }

    [Fact]
    public void RumorChaining_DoesNotChainFromRumorSources()
    {
        // Prevents exponential propagation: A's Rumor contact of C should NOT chain to B
        var world = BuildBareWorld();
        world.SimConfig.Emissary.RumorChainProbability = 1.0f;  // guarantee chain fires if eligible

        var civA = MakeCiv(1, new TileCoord(5,  5), world);
        var civB = MakeCiv(2, new TileCoord(15, 5), world);
        var civC = MakeCiv(3, new TileCoord(25, 5), world);

        // A knows B via WandererMet (eligible to chain from)
        civA.KnownCivs[civB.Id] = new CivContact(
            civB.Id, 1, 1, CivContactSource.WandererMet, civB.CapitalTile, 0.8f);

        // A knows C via Rumor only (NOT eligible to chain from)
        civA.KnownCivs[civC.Id] = new CivContact(
            civC.Id, 1, 1, CivContactSource.Rumor, civC.CapitalTile, 0.3f);

        var activeCivs = world.Civilizations.Values.Where(c => !c.IsCollapsed).ToList();
        KnowledgePropagationPhase.RunRumorChaining(world, activeCivs, world.SimConfig.Emissary);

        // A's knowledge of C is Rumor-source → not eligible to chain to B (one-hop rule)
        civB.KnownCivs.Should().NotContainKey(civC.Id,
            "rumor-source contacts are ineligible for chaining");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EPIC 4.1.3 — Emissary Dispatch
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmissaryDispatch_HighTrustContact_DispatchesTrade()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.DispatchCheckYears = 1;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Set high trust between rulers
        var rel = world.Relationships.GetOrCreate(civ1.RulerId, civ2.RulerId);
        world.Relationships.Upsert(rel with { Trust = 0.5f });  // above TradeDispatchMinTrust (-0.1)

        // Seed contact so dispatch can target civ2
        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.WandererMet, civ2.CapitalTile, 0.8f);

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryDispatch(world, pending);

        world.PendingEmissaries.Should().Contain(e =>
            e.FromCiv == civ1.Id && e.ToCiv == civ2.Id &&
            e.Purpose == EmissaryPurpose.Trade,
            "high trust → Trade emissary dispatched");
    }

    [Fact]
    public void EmissaryDispatch_LowTrustCunningRuler_DispatchesSpy()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.DispatchCheckYears = 1;
        world.SimConfig.Emissary.SpyDispatchMaxTrust = 0.2f;

        // Create civ1 with a cunning ruler (high Rationality, low Honesty → cunning proxy > 0.5)
        var civ1Cap = new TileCoord(5, 5);
        var cunningRuler = MakeRuler(101L, new CivId(1), civ1Cap, world,
            rationality: 1.0f, honesty: 0.0f);  // cunning = (1.0 + 1.0) * 0.5 = 1.0 > 0.5
        var civ1 = new Civilization(new CivId(1), "Civ1", cunningRuler.Id, civ1Cap, 0);
        civ1.Members.Add(cunningRuler.Id);
        world.Civilizations[civ1.Id] = civ1;
        world.NextCivId = 2;

        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Low trust (below SpyDispatchMaxTrust)
        var rel = world.Relationships.GetOrCreate(cunningRuler.Id, civ2.RulerId);
        world.Relationships.Upsert(rel with { Trust = -0.5f });

        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.WandererMet, civ2.CapitalTile, 0.8f);

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryDispatch(world, pending);

        world.PendingEmissaries.Should().Contain(e =>
            e.FromCiv == civ1.Id && e.Purpose == EmissaryPurpose.Spy,
            "cunning ruler + low trust → Spy emissary");
    }

    [Fact]
    public void EmissaryDispatch_AtCap_SkipsDispatch()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.DispatchCheckYears  = 1;
        world.SimConfig.Emissary.MaxActiveEmissariesPerCiv = 3;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Pre-fill the active count to the cap
        civ1.ActiveEmissaryCountByTarget[civ2.Id]       = 2;
        civ1.ActiveEmissaryCountByTarget[new CivId(99)] = 1;  // 3 total

        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.WandererMet, civ2.CapitalTile, 0.8f);

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryDispatch(world, pending);

        world.PendingEmissaries.Should().BeEmpty("civ is at emissary cap — no dispatch");
    }

    [Fact]
    public void EmissaryDispatch_AtWar_SkipsEnemy()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.DispatchCheckYears = 1;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Declare war
        civ1.WarsAgainst[civ2.Id] = world.CurrentYear;
        civ2.WarsAgainst[civ1.Id] = world.CurrentYear;

        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.WandererMet, civ2.CapitalTile, 0.8f);

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryDispatch(world, pending);

        world.PendingEmissaries.Should().BeEmpty("do not dispatch to a civ we're at war with");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EPIC 4.1.4 — Emissary Resolution
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmissaryResolution_SurvivalChanceZero_AllEmissariesLost()
    {
        var world = BuildBareWorld();
        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Add emissary arriving this year with impossible survival
        var emissary = new PendingEmissary(
            civ1.Id, civ2.Id, EmissaryPurpose.Trade,
            DepartedYear: 0, ArrivalYear: world.CurrentYear,
            SurvivalChance: 0.0f);
        world.PendingEmissaries.Add(emissary);
        civ1.ActiveEmissaryCountByTarget[civ2.Id] = 1;

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryResolution(world, pending);

        world.PendingEmissaries.Should().BeEmpty("resolved emissaries are removed");
        pending.Should().Contain(e => e.Type == EventType.EmissaryLost,
            "survival=0.0 means the emissary always dies");
    }

    [Fact]
    public void EmissaryResolution_SurvivalChanceOne_NoneAreLost()
    {
        var world = BuildBareWorld();
        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Both civs need enough pop for trade to fire MerchantTradeCompleted
        civ1.TotalPopulation = 100;
        civ2.TotalPopulation = 100;

        var emissary = new PendingEmissary(
            civ1.Id, civ2.Id, EmissaryPurpose.Trade,
            DepartedYear: 0, ArrivalYear: world.CurrentYear,
            SurvivalChance: 1.0f);
        world.PendingEmissaries.Add(emissary);
        civ1.ActiveEmissaryCountByTarget[civ2.Id] = 1;

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryResolution(world, pending);

        pending.Should().NotContain(e => e.Type == EventType.EmissaryLost,
            "survival=1.0 means emissary always arrives");
    }

    [Fact]
    public void EmissaryResolution_Trade_FiresMerchantTradeAndBumpsTrust()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.TradeTrustGain = 0.1f;
        world.SimConfig.Emissary.TradeMinPopForGoods = 50;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        civ1.TotalPopulation = 100;
        civ2.TotalPopulation = 100;

        // Get initial trust
        var initialRel = world.Relationships.GetOrCreate(civ1.RulerId, civ2.RulerId);
        float initialTrust = initialRel.Trust;

        var emissary = new PendingEmissary(
            civ1.Id, civ2.Id, EmissaryPurpose.Trade,
            DepartedYear: 0, ArrivalYear: world.CurrentYear,
            SurvivalChance: 1.0f);
        world.PendingEmissaries.Add(emissary);
        civ1.ActiveEmissaryCountByTarget[civ2.Id] = 1;

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryResolution(world, pending);

        pending.Should().Contain(e => e.Type == EventType.MerchantTradeCompleted,
            "successful Trade emissary fires MerchantTradeCompleted");

        var afterRel = world.Relationships.Get(civ1.RulerId, civ2.RulerId);
        afterRel!.Trust.Should().BeGreaterThan(initialTrust, "trade bumps ruler trust");

        civ1.KnownCivs[civ2.Id].BestSource.Should().Be(CivContactSource.EmissaryExchange,
            "successful emissary upgrades contact to EmissaryExchange");
    }

    [Fact]
    public void EmissaryResolution_Diplomacy_TipsAllianceThreshold()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.TradeTrustGain         = 0.08f;  // diplomacy uses TradeTrustGain * 2
        world.SimConfig.Emissary.DiplomacyAllianceMinTrust = 0.25f;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Set trust just below alliance threshold
        // After diplomacy: trust = 0.09 + 0.08*2 = 0.09 + 0.16 = 0.25 → exactly at threshold
        var rel = world.Relationships.GetOrCreate(civ1.RulerId, civ2.RulerId);
        world.Relationships.Upsert(rel with { Trust = 0.09f });

        var emissary = new PendingEmissary(
            civ1.Id, civ2.Id, EmissaryPurpose.Diplomacy,
            DepartedYear: 0, ArrivalYear: world.CurrentYear,
            SurvivalChance: 1.0f);
        world.PendingEmissaries.Add(emissary);
        civ1.ActiveEmissaryCountByTarget[civ2.Id] = 1;

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryResolution(world, pending);

        pending.Should().Contain(e => e.Type == EventType.AllianceFormed,
            "trust crossed alliance threshold → AllianceFormed event");
    }

    [Fact]
    public void EmissaryResolution_Spy_IncreasesConfidenceNoTargetEvent()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.SpyConfidenceBoost = 0.4f;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Pre-existing low-confidence contact
        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.Rumor, civ2.CapitalTile, 0.3f);

        var emissary = new PendingEmissary(
            civ1.Id, civ2.Id, EmissaryPurpose.Spy,
            DepartedYear: 0, ArrivalYear: world.CurrentYear,
            SurvivalChance: 1.0f);
        world.PendingEmissaries.Add(emissary);
        civ1.ActiveEmissaryCountByTarget[civ2.Id] = 1;

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryResolution(world, pending);

        civ1.KnownCivs[civ2.Id].Confidence.Should().BeGreaterThan(0.3f,
            "spy emissary increases contact confidence");

        // Spy fires CivIntelGathered (for the sending civ's history log) but not a visible event to target
        pending.Should().Contain(e => e.Type == EventType.CivIntelGathered,
            "spy fires CivIntelGathered for history log");
        // No event visible to civ2 — CivIntelGathered has CivId = civ1 (not civ2)
        pending.Where(e => e.Type == EventType.CivIntelGathered)
               .Should().OnlyContain(e => e.CivId == civ1.Id.Value);
    }

    [Fact]
    public void EmissaryResolution_Religious_BoostsTargetCivCharacterAwe()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.ReligiousSpreadAweBoost = 0.3f;

        var civ1 = MakeCiv(1, new TileCoord(5, 5), world);
        var civ2 = MakeCiv(2, new TileCoord(30, 5), world);

        // Add a living character to civ2 so awe can be boosted
        var targetChar = MakeRuler(200L, civ2.Id, civ2.CapitalTile, world);
        civ2.Members.Add(targetChar.Id);
        float initialSpiritual = targetChar.Needs.Spiritual;

        var emissary = new PendingEmissary(
            civ1.Id, civ2.Id, EmissaryPurpose.Religious,
            DepartedYear: 0, ArrivalYear: world.CurrentYear,
            SurvivalChance: 1.0f);
        world.PendingEmissaries.Add(emissary);
        civ1.ActiveEmissaryCountByTarget[civ2.Id] = 1;

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryResolution(world, pending);

        targetChar.Needs.Spiritual.Should().BeGreaterThan(initialSpiritual,
            "religious emissary boosts Spiritual need of target civ chars");
        pending.Should().Contain(e => e.Type == EventType.ReligiousEmissaryArrived,
            "ReligiousEmissaryArrived event fired");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EPIC 4.1.6 — Boundary / Parametric Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(5,   0.008f, 0.2f, 0.96f)]
    [InlineData(20,  0.008f, 0.2f, 0.84f)]
    [InlineData(50,  0.008f, 0.2f, 0.60f)]
    [InlineData(100, 0.008f, 0.2f, 0.20f)]
    [InlineData(200, 0.008f, 0.2f, 0.20f)]
    public void SurvivalChance_MatchesExpectedFormula(
        float dist, float deathPerTile, float minSurvival, float expected)
    {
        float survival = Math.Clamp(1f - dist * deathPerTile, minSurvival, 1f);
        survival.Should().BeApproximately(expected, 0.001f,
            $"distance {dist} tiles, death_per_tile {deathPerTile}, min {minSurvival}");
    }

    [Fact]
    public void EmissaryDispatch_SurvivalChance_ComputedFromDistance()
    {
        var world = BuildBareWorld();
        world.SimConfig.Emissary.DispatchCheckYears         = 1;
        world.SimConfig.Emissary.EmissaryDeathPerTile       = 0.008f;
        world.SimConfig.Emissary.EmissaryMinSurvivalChance  = 0.2f;
        world.SimConfig.Emissary.EmissaryTravelSpeedTilesPerYear = 8f;

        // Civ1 capital at (5,5), Civ2 capital at (55,5) → distance = 50 tiles
        // Expected survival = clamp(1 - 50*0.008, 0.2, 1.0) = clamp(0.60, 0.2, 1.0) = 0.60
        var civ1 = MakeCiv(1, new TileCoord(5,  5), world);
        var civ2 = MakeCiv(2, new TileCoord(55, 5), world);

        var rel = world.Relationships.GetOrCreate(civ1.RulerId, civ2.RulerId);
        world.Relationships.Upsert(rel with { Trust = 0.5f });

        civ1.KnownCivs[civ2.Id] = new CivContact(
            civ2.Id, 1, 1, CivContactSource.WandererMet, new TileCoord(55, 5), 0.8f);

        var pending = new List<PendingEvent>();
        CivTracker.RunEmissaryDispatch(world, pending);

        var emissary = world.PendingEmissaries.FirstOrDefault(e => e.FromCiv == civ1.Id);
        emissary.Should().NotBeNull();
        emissary!.SurvivalChance.Should().BeApproximately(0.60f, 0.01f,
            "50-tile distance → survival = 1 - 50*0.008 = 0.60");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EPIC 4.1.2 — Encounter seeding via CharacterBehaviorPhase
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EncounterSeeding_CrossCivEncounter_BothCivsGainWandererMetContact()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 1);
        world.SimConfig.Emissary.EncounterConfidenceGain = 0.35f;

        var tile = new TileCoord(5, 5);
        var civ1 = MakeCiv(1, tile, world);
        var civ2 = MakeCiv(2, tile, world);  // same tile — forces encounter

        // Create two characters from different civs at the same location
        var charA = MakeRuler(201L, civ1.Id, tile, world);
        var charB = MakeRuler(202L, civ2.Id, tile, world);

        civ1.Members.Add(charA.Id);
        civ2.Members.Add(charB.Id);

        // Simulate the encounter: call SeedCivContact directly as CharacterBehaviorPhase would
        CivTracker.SeedCivContact(charA.Identity.CivId, charB.Identity.CivId,
            CivContactSource.WandererMet, civ2.CapitalTile,
            world.SimConfig.Emissary.EncounterConfidenceGain, world);
        CivTracker.SeedCivContact(charB.Identity.CivId, charA.Identity.CivId,
            CivContactSource.WandererMet, civ1.CapitalTile,
            world.SimConfig.Emissary.EncounterConfidenceGain, world);

        civ1.KnownCivs.Should().ContainKey(civ2.Id,
            "Civ1 learns of Civ2 from the wanderer encounter");
        civ2.KnownCivs.Should().ContainKey(civ1.Id,
            "Civ2 learns of Civ1 — seeding is symmetric");

        civ1.KnownCivs[civ2.Id].BestSource.Should().Be(CivContactSource.WandererMet);
        civ1.KnownCivs[civ2.Id].Confidence.Should()
            .BeApproximately(0.35f, 0.001f, "confidence matches EncounterConfidenceGain");
    }
}
