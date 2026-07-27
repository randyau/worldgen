using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M7 God Mode authoring commands had zero test coverage. AuthoringResolver/AuthoringValidator
/// are `internal static` — accessible directly from this assembly (InternalsVisibleTo) — so
/// these tests call them directly rather than round-tripping through SimLoop/CommandQueue.
/// Event assertions use PhaseRunner.InjectPendingEvent + RunTick + FlushPendingEvents, the same
/// pattern PhaseRunnerTests.cs uses to verify events reach the EventCache stamped correctly.
/// </summary>
public class AuthoringTests
{
    private static WorldState BuildWorld(int seed = 1)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
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

    private static TileCoord FindLandTile(WorldState world, bool nonVolcanic = true)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new TileCoord(x, y);
                if (!world.IsLand(c)) continue;
                if (nonVolcanic && world.TileGrid.GetTile(c).StaticFlags.HasFlag(TileStaticFlags.IsVolcanic)) continue;
                return c;
            }
        throw new InvalidOperationException("No matching land tile found in test world — widen the search or change seed.");
    }

    private static TileCoord FindOceanTile(WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new TileCoord(x, y);
                if (!world.IsLand(c)) return c;
            }
        throw new InvalidOperationException("No ocean tile found in test world.");
    }

    private static Tier1Character MakeTier1(TileCoord loc, EntityId id) => new(
        id, loc,
        PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
        new IdentityData("Test", "the Tester", "test", null, null, default, 0, 0),
        100, 200);

    // ── AuthoringValidator ─────────────────────────────────────────────────────

    [Fact]
    public void ValidateCoord_OutOfBounds_Rejected()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        var (valid, reason) = AuthoringValidator.ValidateCoord(new TileCoord(w + 5, h + 5), world);

        valid.Should().BeFalse();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void ValidateCoord_InBounds_Accepted()
    {
        var world = BuildWorld();
        var (valid, _) = AuthoringValidator.ValidateCoord(new TileCoord(0, 0), world);
        valid.Should().BeTrue();
    }

    [Fact]
    public void ValidateLandTile_OceanTile_Rejected()
    {
        var world = BuildWorld();
        var ocean = FindOceanTile(world);

        var (valid, reason) = AuthoringValidator.ValidateLandTile(ocean, world);

        valid.Should().BeFalse();
        reason.Should().Contain("not a land tile");
    }

    [Fact]
    public void ValidateLandTile_LandTile_Accepted()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: false);
        var (valid, _) = AuthoringValidator.ValidateLandTile(land, world);
        valid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCharacterAlive_DeadCharacter_Rejected()
    {
        var world = BuildWorld();
        var loc = FindLandTile(world, nonVolcanic: false);
        var character = MakeTier1(loc, new EntityId(700));
        character.IsAlive = false;
        world.Entities.Add(character);

        var (valid, reason) = AuthoringValidator.ValidateCharacterAlive(character.Id, world);

        valid.Should().BeFalse();
        reason.Should().Contain("not alive");
    }

    [Fact]
    public void ValidateCharacterAlive_MissingEntity_Rejected()
    {
        var world = BuildWorld();
        var (valid, reason) = AuthoringValidator.ValidateCharacterAlive(new EntityId(999999), world);

        valid.Should().BeFalse();
        reason.Should().NotBeNull();
    }

    [Fact]
    public void ValidateCharacterAlive_LivingCharacter_Accepted()
    {
        var world = BuildWorld();
        var loc = FindLandTile(world, nonVolcanic: false);
        var character = MakeTier1(loc, new EntityId(701));
        world.Entities.Add(character);

        var (valid, _) = AuthoringValidator.ValidateCharacterAlive(character.Id, world);
        valid.Should().BeTrue();
    }

    [Fact]
    public void ValidateDisasterApplicable_VolcanicAshOnNonVolcanicTile_Rejected()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: true);

        var (valid, reason) = AuthoringValidator.ValidateDisasterApplicable(land, DisasterType.VolcanicAsh, world);

        valid.Should().BeFalse();
        reason.Should().Contain("not a volcanic tile");
    }

    [Fact]
    public void ValidateDisasterApplicable_WildfireOnAnyTile_Accepted()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: true);
        var (valid, _) = AuthoringValidator.ValidateDisasterApplicable(land, DisasterType.Wildfire, world);
        valid.Should().BeTrue();
    }

    // ── AuthoringResolver — valid commands mutate WorldState + inject a stamped GodMode event ──
    //
    // BUG IN AN EARLIER DRAFT OF THIS TEST: comparing entity-count/Wellbeing before vs. after
    // RunTick() conflated the resolver's own mutation with whatever the tick's normal phases do
    // (first-tick initial population seeding, Wellbeing homeostasis, etc.). checkStateImmediately
    // runs right after Resolve() and before any tick, isolating the resolver's own effect; RunTick
    // + FlushPendingEvents only run afterward, purely to verify the injected event gets stamped.

    private static SimEvent? ResolveThenFindEvent(
        WorldState world, ICommand cmd, EventType expectedType, Action checkStateImmediately)
    {
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), new EventStore(), cache);

        AuthoringResolver.Resolve(cmd, world, runner);
        checkStateImmediately();

        runner.RunTick(world);
        runner.FlushPendingEvents(world);

        var recent = cache.GetRecent(20);
        return recent.FirstOrDefault(e => e.Type == expectedType);
    }

    [Fact]
    public void ResolveArtifact_Valid_AddsArtifactAndInjectsStampedEvent()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: false);
        int before = world.Artifacts.Count;

        var ev = ResolveThenFindEvent(world,
            new AuthorPlaceArtifact(land, ArtifactCategory.Weapon, "Test Blade"),
            EventType.GodModeArtifactPlaced,
            () => world.Artifacts.Count.Should().Be(before + 1, "a valid AuthorPlaceArtifact must add exactly one artifact"));

        ev.Should().NotBeNull("a valid AuthorPlaceArtifact must inject a GodModeArtifactPlaced event");
        ev!.IsGodMode.Should().BeTrue("event types >= 9000 must be stamped IsGodMode = true");
    }

    [Fact]
    public void ResolveArtifact_InvalidCoord_NoOpsSilently()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        int before = world.Artifacts.Count;

        var ev = ResolveThenFindEvent(world,
            new AuthorPlaceArtifact(new TileCoord(w + 100, h + 100), ArtifactCategory.Weapon),
            EventType.GodModeArtifactPlaced,
            () => world.Artifacts.Count.Should().Be(before, "an out-of-bounds AuthorPlaceArtifact must be rejected, not applied"));

        ev.Should().BeNull("a rejected command must not inject any event");
    }

    [Fact]
    public void ResolveDisaster_Valid_AddsActiveDisasterAndInjectsStampedEvent()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: true);

        var ev = ResolveThenFindEvent(world,
            new AuthorTriggerDisaster(land, DisasterType.Wildfire),
            EventType.GodModeDisasterTriggered,
            () =>
            {
                world.ActiveTileDisasters.Should().ContainKey(land);
                world.ActiveTileDisasters[land].Should().Contain(d => d.Type == DisasterType.Wildfire);
            });

        ev.Should().NotBeNull();
        ev!.IsGodMode.Should().BeTrue();
    }

    [Fact]
    public void ResolveDisaster_VolcanicAshOnNonVolcanicTile_NoOpsSilently()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: true);

        var ev = ResolveThenFindEvent(world,
            new AuthorTriggerDisaster(land, DisasterType.VolcanicAsh),
            EventType.GodModeDisasterTriggered,
            () => world.ActiveTileDisasters.Should().NotContainKey(land));

        ev.Should().BeNull();
    }

    [Fact]
    public void ResolveSpawn_Valid_AddsEntityAndInjectsStampedEvent()
    {
        var world = BuildWorld();
        var land = FindLandTile(world, nonVolcanic: false);
        int before = world.Entities.Count;

        var ev = ResolveThenFindEvent(world,
            new AuthorSpawnCharacter(land),
            EventType.GodModeCharacterCreated,
            () => world.Entities.Count.Should().Be(before + 1, "a valid AuthorSpawnCharacter must add exactly one entity"));

        ev.Should().NotBeNull();
        ev!.IsGodMode.Should().BeTrue();
    }

    [Fact]
    public void ResolveSpawn_OceanTile_NoOpsSilently()
    {
        var world = BuildWorld();
        var ocean = FindOceanTile(world);
        int before = world.Entities.Count;

        var ev = ResolveThenFindEvent(world,
            new AuthorSpawnCharacter(ocean),
            EventType.GodModeCharacterCreated,
            () => world.Entities.Count.Should().Be(before, "spawning on an ocean tile must be rejected"));

        ev.Should().BeNull();
    }

    [Fact]
    public void ResolveNudge_RaiseMorale_IncreasesWellbeingAndInjectsStampedEvent()
    {
        var world = BuildWorld();
        var loc = FindLandTile(world, nonVolcanic: false);
        var character = MakeTier1(loc, new EntityId(702));
        character.Wellbeing = 0f;
        world.Entities.Add(character);

        var ev = ResolveThenFindEvent(world,
            new AuthorNudgeCharacter(character.Id, CharacterNudge.RaiseMorale),
            EventType.GodModeCharacterNudged,
            () => character.Wellbeing.Should().BeApproximately(0.4f, 0.001f));

        ev.Should().NotBeNull("GodModeCharacterNudged (event 9006) must be injected for a valid nudge");
        ev!.IsGodMode.Should().BeTrue("event type 9006 >= 9000 must be stamped IsGodMode = true");
    }

    [Fact]
    public void ResolveNudge_SetSettle_AddsFoundCityGoal()
    {
        var world = BuildWorld();
        var loc = FindLandTile(world, nonVolcanic: false);
        var character = MakeTier1(loc, new EntityId(703));
        world.Entities.Add(character);

        AuthoringResolver.Resolve(new AuthorNudgeCharacter(character.Id, CharacterNudge.SetSettle), world,
            new PhaseRunner(TestSimConfig.Default(), new EventStore(), new EventCache()));

        character.Goals.Should().Contain(g => g.Type == GoalType.FoundCity);
    }

    [Fact]
    public void ResolveNudge_DeadCharacter_NoOpsSilently()
    {
        var world = BuildWorld();
        var loc = FindLandTile(world, nonVolcanic: false);
        var character = MakeTier1(loc, new EntityId(704));
        character.IsAlive = false;
        character.Wellbeing = 0f;
        world.Entities.Add(character);

        var ev = ResolveThenFindEvent(world,
            new AuthorNudgeCharacter(character.Id, CharacterNudge.RaiseMorale),
            EventType.GodModeCharacterNudged,
            () => character.Wellbeing.Should().Be(0f, "a nudge to a dead character must be rejected, not applied"));

        ev.Should().BeNull();
    }

    // ── GodMode event stamping — differential check against a non-GodMode event ─────

    [Fact]
    public void RegularEvent_BelowGodModeRange_IsNotStampedGodMode()
    {
        var world = BuildWorld();
        var cache = new EventCache();
        var runner = new PhaseRunner(TestSimConfig.Default(), new EventStore(), cache);

        runner.InjectPendingEvent(new PendingEvent(EventType.WildfireOccurred, null, null, "{}"));
        runner.RunTick(world);
        runner.FlushPendingEvents(world);

        var recent = cache.GetRecent(10);
        recent.Should().Contain(e => e.Type == EventType.WildfireOccurred && !e.IsGodMode,
            "an event type below 9000 must not be stamped IsGodMode");
    }
}
