using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests;

/// <summary>Tests for M4 Phase 3: religion founding (4.3.1) and specialist replacement (4.3.2).</summary>
public sealed class ReligionSpecialistTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static SimConfig MakeConfig(Action<ReligionConfig>? rel = null)
    {
        var cfg = SimConfigLoader.LoadOrCreateDefault();
        rel?.Invoke(cfg.Religion);
        return cfg;
    }

    private static (WorldState world, TileCoord tile) SetupWorld()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile  = FindLandTile(world);
        var civ   = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        return (world, tile);
    }

    private static Tier1Character AddCharacter(
        WorldState world, TileCoord tile, long id,
        float spiritual = 0.0f, float piety = 0.0f, float wonder = 0.0f)
    {
        var civ = new CivId(1);
        var personality = new PersonalityVector(
            Ambition: 0.5f, Greed: 0.5f, Aggression: 0.5f, Compassion: 0.5f,
            Curiosity: 0.5f, Creativity: 0.5f, Rationality: 0.5f, Wonder: wonder,
            Loyalty: 0.5f, Sociability: 0.5f, Honesty: 0.5f, Stability: 0.5f);
        var skills   = SkillVector.Default with { Piety = piety };
        var identity = new IdentityData("Prophet", "the Seer", "test", null, null, civ, 0, 0);
        var c = new Tier1Character(new EntityId(id), tile, personality, AptitudeVector.Default,
            skills, identity, 100, 400);
        c.Needs = c.Needs with { Spiritual = spiritual };
        world.Entities.Add(c);
        return c;
    }

    private static TileCoord FindLandTile(WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        return new TileCoord(0, 0);
    }

    // ─── Epic 4.3.1 — FoundReligion goal ─────────────────────────────────────

    [Fact]
    public void FoundReligion_QualifyingCharacter_GoalForms_OnAnnualTick()
    {
        var cfg = MakeConfig(r =>
        {
            r.SpiritualFoundingThreshold = 0.75f;
            r.PietyFoundingThreshold     = 0.50f;
            r.WonderFoundingThreshold    = 0.60f;
        });
        var (world, tile) = SetupWorld();
        var c = AddCharacter(world, tile, 101, spiritual: 0.80f, piety: 0.60f, wonder: 0.70f);

        var phase = new CharacterBehaviorPhase(cfg);
        // Annual tick (Spring = tick that is multiple of ticks_per_year)
        phase.Execute(world, tick: 1L, isAnnualTick: true);

        c.Goals.Should().Contain(g => g.Type == GoalType.FoundReligion && !g.IsComplete,
            "qualifying character should form FoundReligion goal on annual tick");
    }

    [Fact]
    public void FoundReligion_LowSpiritual_NoGoal()
    {
        var cfg = MakeConfig(r => r.SpiritualFoundingThreshold = 0.75f);
        var (world, tile) = SetupWorld();
        var c = AddCharacter(world, tile, 102, spiritual: 0.60f, piety: 0.70f, wonder: 0.70f);

        var phase = new CharacterBehaviorPhase(cfg);
        phase.Execute(world, tick: 1L, isAnnualTick: true);

        c.Goals.Should().NotContain(g => g.Type == GoalType.FoundReligion,
            "character with Spiritual below threshold should not form religion goal");
    }

    [Fact]
    public void FoundReligion_Goal_ProgressesAnnually_AndFiresEvent()
    {
        var cfg = MakeConfig(r =>
        {
            r.SpiritualFoundingThreshold     = 0.75f;
            r.PietyFoundingThreshold         = 0.50f;
            r.WonderFoundingThreshold        = 0.60f;
            r.ReligionFoundingProgressPerYear = 0.40f;   // 3 ticks to complete
            r.ReligionFoundingCooldownYears   = 0;
        });
        var (world, tile) = SetupWorld();
        world.Settlements[tile] = new SettlementStub(
            new EntityId(1), new CivId(1), tile, 1, Population: 50, Health: 100);

        var c = AddCharacter(world, tile, 103, spiritual: 0.90f, piety: 0.70f, wonder: 0.80f);
        var phase = new CharacterBehaviorPhase(cfg);

        List<PendingEvent> allEvents = [];
        bool founded = false;

        for (int year = 0; year < 5 && !founded; year++)
        {
            world.CurrentYear = year;
            var events = phase.Execute(world, tick: (long)year * 16, isAnnualTick: true);
            allEvents.AddRange(events);

            // Keep Spiritual high so goal doesn't abandon
            if (c.IsAlive) c.Needs = c.Needs with { Spiritual = 0.90f };
            founded = allEvents.Any(e => e.Type == EventType.ReligionFounded);
        }

        founded.Should().BeTrue("FoundReligion goal should complete and fire ReligionFounded within 5 years");
        c.LastReligionFoundedYear.Should().BeGreaterThanOrEqualTo(0,
            "LastReligionFoundedYear should be set after founding");
    }

    [Fact]
    public void FoundReligion_Goal_Abandons_WhenSpiritualDrops()
    {
        var cfg = MakeConfig(r =>
        {
            r.SpiritualFoundingThreshold     = 0.75f;
            r.PietyFoundingThreshold         = 0.50f;
            r.WonderFoundingThreshold        = 0.60f;
            r.ReligionFoundingProgressPerYear = 0.30f;
            r.ReligionFoundingCooldownYears   = 0;
        });
        var (world, tile) = SetupWorld();
        var c = AddCharacter(world, tile, 104, spiritual: 0.85f, piety: 0.70f, wonder: 0.75f);

        var phase = new CharacterBehaviorPhase(cfg);

        // Tick 1: goal forms with progress 0.30
        world.CurrentYear = 1;
        phase.Execute(world, tick: 16L, isAnnualTick: true);
        c.Goals.Should().Contain(g => g.Type == GoalType.FoundReligion && !g.IsComplete);

        // Spiritual drops far below threshold
        c.Needs = c.Needs with { Spiritual = 0.40f };

        // Tick 2: goal should be abandoned (IsComplete=true, no event)
        world.CurrentYear = 2;
        var events = phase.Execute(world, tick: 32L, isAnnualTick: true);

        events.Should().NotContain(e => e.Type == EventType.ReligionFounded,
            "goal should be abandoned when Spiritual drops, not fire the event");
        c.Goals.Where(g => g.Type == GoalType.FoundReligion)
            .Should().OnlyContain(g => g.IsComplete,
            "abandoned goal should be marked complete for pruning");
    }

    [Fact]
    public void FoundReligion_Cooldown_Prevents_Immediate_Refounding()
    {
        var cfg = MakeConfig(r =>
        {
            r.SpiritualFoundingThreshold     = 0.75f;
            r.PietyFoundingThreshold         = 0.50f;
            r.WonderFoundingThreshold        = 0.60f;
            r.ReligionFoundingProgressPerYear = 0.40f;  // takes ~3 annual ticks
            r.ReligionFoundingCooldownYears   = 50;
        });
        var (world, tile) = SetupWorld();
        world.Settlements[tile] = new SettlementStub(
            new EntityId(1), new CivId(1), tile, 1, Population: 50, Health: 100);

        var c = AddCharacter(world, tile, 105, spiritual: 0.90f, piety: 0.80f, wonder: 0.80f);
        var phase = new CharacterBehaviorPhase(cfg);

        // Phase 1: drive through first founding using same pattern as progression test
        List<PendingEvent> allEvents = [];
        bool founded = false;
        int foundedYear = -1;

        for (int year = 0; year < 10 && !founded; year++)
        {
            world.CurrentYear = year;
            var events = phase.Execute(world, tick: (long)year * 16, isAnnualTick: true);
            allEvents.AddRange(events);
            if (c.IsAlive) c.Needs = c.Needs with { Spiritual = 0.90f };
            if (allEvents.Any(e => e.Type == EventType.ReligionFounded))
            {
                founded = true;
                foundedYear = year;
            }
        }

        founded.Should().BeTrue("character should found religion within 10 years");
        c.IsAlive.Should().BeTrue("founder must be alive to test cooldown");

        // Phase 2: immediately after founding, clear completed goals and try again in the same year
        c.Goals.RemoveAll(g => g.IsComplete);
        c.Needs = c.Needs with { Spiritual = 0.90f };

        // Run next annual tick (still within cooldown window)
        world.CurrentYear = foundedYear + 1;
        phase.Execute(world, tick: (long)(foundedYear + 1) * 16, isAnnualTick: true);

        c.Goals.Should().NotContain(g => g.Type == GoalType.FoundReligion && !g.IsComplete,
            "cooldown should prevent forming a new FoundReligion goal within 50 years of the last founding");
    }

    // ─── Epic 4.3.2 — Specialist replacement ─────────────────────────────────

    [Fact]
    public void Specialist_Replacement_AfterDeath_SpecialistRespawns()
    {
        var cfg = SimConfigLoader.LoadOrCreateDefault();
        cfg.Settlement.CrystalPopArtisan = 50;
        cfg.Settlement.PopGrowthRate     = 0f;
        cfg.Settlement.PopDecayRate      = 0f;
        cfg.Character.GlobalSettlementMinDist = 0;

        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile  = FindLandTile(world);
        var civ   = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        world.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1,
            Population: 100, Health: 100);

        var popPhase = new PopulationDynamicsPhase(cfg);

        // Tick 1: artisan crystallizes
        popPhase.Execute(world);
        world.Entities.Tier2Chars.Should().Contain(c => c.Livelihood.Role == Tier2Role.Artisan,
            "artisan should crystallize on first tick above threshold");

        var artisan = world.Entities.Tier2Chars.First(c => c.Livelihood.Role == Tier2Role.Artisan);

        // Kill the artisan directly (InternalsVisibleTo allows this from tests)
        artisan.Health  = 0;
        artisan.IsAlive = false;
        artisan.IsAlive.Should().BeFalse();

        // Tick 2: replacement should spawn
        var events = popPhase.Execute(world);
        events.Should().Contain(e => e.Type == EventType.AppointedToRole,
            "replacement artisan should be appointed after previous one dies");
        world.Entities.Tier2Chars.Should().Contain(c =>
            c.Livelihood.Role == Tier2Role.Artisan && c.IsAlive,
            "a living artisan should exist after replacement");
    }

    [Fact]
    public void Specialist_NoDuplicate_WhenLivingSpecialistExists()
    {
        var cfg = SimConfigLoader.LoadOrCreateDefault();
        cfg.Settlement.CrystalPopArtisan = 50;
        cfg.Settlement.PopGrowthRate     = 0f;
        cfg.Settlement.PopDecayRate      = 0f;

        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile  = FindLandTile(world);
        var civ   = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        world.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1,
            Population: 100, Health: 100);

        var popPhase = new PopulationDynamicsPhase(cfg);
        popPhase.Execute(world);
        int countAfterFirst = world.Entities.Tier2Chars.Count(c => c.Livelihood.Role == Tier2Role.Artisan);

        popPhase.Execute(world);
        int countAfterSecond = world.Entities.Tier2Chars.Count(c => c.Livelihood.Role == Tier2Role.Artisan);

        countAfterSecond.Should().Be(countAfterFirst,
            "no duplicate should spawn while artisan is alive");
    }

    [Fact]
    public void Specialist_NoSpawn_WhenPopBelowThreshold()
    {
        var cfg = SimConfigLoader.LoadOrCreateDefault();
        cfg.Settlement.CrystalPopArtisan = 200;
        cfg.Settlement.PopGrowthRate     = 0f;
        cfg.Settlement.PopDecayRate      = 0f;

        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var tile  = FindLandTile(world);
        var civ   = new CivId(1);
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        world.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1,
            Population: 100, Health: 100);   // below threshold

        var popPhase = new PopulationDynamicsPhase(cfg);
        popPhase.Execute(world);

        world.Entities.Tier2Chars.Should().NotContain(c => c.Livelihood.Role == Tier2Role.Artisan,
            "no artisan should spawn when population is below crystal threshold");
    }
}
