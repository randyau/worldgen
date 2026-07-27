using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// WatchEntity is polymorphic (M11 UX rework: "promote to watch" pattern generalized from
/// Character-only to any registry entity) — the sim resolves the watched entity's EntityKind
/// from the registry when the command is handled, rather than trusting a kind carried on the
/// command itself. These tests cover that resolution, the clear (Id=0) path, and that
/// EnterSpotlight still sets the watch target as a side effect.
/// </summary>
public class WatchTargetTests
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

    private static (SimLoop loop, CommandQueue cmdQueue) MakeLoop(WorldState world)
    {
        var sim = TestSimConfig.Default();
        sim.SimLoop.UltrafastTicksPerSecond = float.MaxValue;
        sim.SimLoop.UltrafastSnapshotIntervalTicks = 1;

        var cmdQueue        = new CommandQueue();
        var cache           = new StateCache();
        var eventStore      = new EventStore();
        var eventCache      = new EventCache();
        var phaseRunner     = new PhaseRunner(sim, eventStore, eventCache);
        var snapshotBuilder = new SnapshotBuilder();
        var loop = new SimLoop(world, cmdQueue, cache, phaseRunner, snapshotBuilder, sim, eventCache);

        cmdQueue.Enqueue(new SetSimSpeed(SimSpeed.Ultrafast));
        return (loop, cmdQueue);
    }

    private static Tier1Character MakeTier1(EntityId id, TileCoord loc) => new(
        id, loc,
        PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
        new IdentityData("Test", "the Tester", "test", null, null, default, 0, 0),
        100, 200);

    private static Tier2Character MakeTier2(EntityId id, TileCoord loc) => new(
        id, loc, "Tester Two", PersonalityVector6.Default,
        new LivelihoodData(Tier2Role.General, null, loc, 0.5f),
        100, 200);

    private static LegendaryBeast MakeBeast(EntityId id, TileCoord loc) => new(
        id, speciesId: "wolf", name: "Test Wolf", location: loc, isLegendary: false,
        maxHealth: 50, strength: 10, speed: 5, aggression: 0.3f, territoryRadius: 3,
        abilities: Array.Empty<string>(), maxAgeSeason: 100,
        foodDepletion: 0.05f, foodFromHunt: 0.3f, foodFromGraze: 0f,
        reproductionChance: 0.1f, reproductionMinAge: 4, reproductionFoodThreshold: 0.6f,
        hibernates: false, prefersCompany: false);

    [Fact]
    public async Task WatchEntity_ResolvesTier1CharacterKind()
    {
        var world = BuildWorld();
        var loc = new TileCoord(5, 5);
        var character = MakeTier1(new EntityId(500), loc);
        world.Entities.Add(character);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new WatchEntity(character.Id));
        await Task.Delay(200);
        loop.Stop();

        world.WatchedEntityId.Should().Be(character.Id);
        world.WatchedEntityKind.Should().Be(EntityKind.Tier1Character);
    }

    [Fact]
    public async Task WatchEntity_ResolvesBeastKind()
    {
        var world = BuildWorld();
        var loc = new TileCoord(6, 6);
        var beast = MakeBeast(new EntityId(501), loc);
        world.Entities.Add(beast);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new WatchEntity(beast.Id));
        await Task.Delay(200);
        loop.Stop();

        world.WatchedEntityId.Should().Be(beast.Id);
        world.WatchedEntityKind.Should().Be(EntityKind.LegendaryBeast);
    }

    [Fact]
    public async Task WatchEntity_ZeroIdClearsWatchTarget()
    {
        var world = BuildWorld();
        var loc = new TileCoord(5, 5);
        var character = MakeTier1(new EntityId(502), loc);
        world.Entities.Add(character);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new WatchEntity(character.Id));
        await Task.Delay(200);
        world.WatchedEntityId.Should().Be(character.Id, "sanity check: watch was set before clearing");

        cmdQueue.Enqueue(new WatchEntity(new EntityId(0)));
        await Task.Delay(200);
        loop.Stop();

        world.WatchedEntityId.Should().BeNull("EntityId with Value 0 clears the watch target");
    }

    [Fact]
    public async Task EnterSpotlight_AlsoSetsWatchTargetToCharacter()
    {
        var world = BuildWorld();
        var loc = new TileCoord(5, 5);
        var character = MakeTier1(new EntityId(503), loc);
        world.Entities.Add(character);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new EnterSpotlight(character.Id));
        await Task.Delay(200);
        loop.Stop();

        world.WatchedEntityId.Should().Be(character.Id);
        world.WatchedEntityKind.Should().Be(EntityKind.Tier1Character);
    }

    // ── Persistence round-trip ────────────────────────────────────────────────

    [Fact]
    public void WatchTarget_RoundTripsCharacterThroughSaveLoad()
    {
        var saveDir = Path.Combine(Path.GetTempPath(), $"watch_test_{Guid.NewGuid():N}");
        try
        {
            var world = BuildWorld();
            var simCfg = TestSimConfig.Default();
            var loc = new TileCoord(5, 5);
            var character = MakeTier1(new EntityId(504), loc);
            world.Entities.Add(character);
            world.WatchedEntityId   = character.Id;
            world.WatchedEntityKind = EntityKind.Tier1Character;

            WorldStateSaver.Save(world, saveDir, simCfg);
            var loaded = WorldStateSaver.Load(saveDir, simCfg);

            loaded.WatchedEntityId.Should().Be(character.Id);
            loaded.WatchedEntityKind.Should().Be(EntityKind.Tier1Character);
        }
        finally
        {
            WorldStateSaver.DeleteSave(saveDir);
        }
    }

    [Fact]
    public void WatchTarget_RoundTripsBeastThroughSaveLoad()
    {
        var saveDir = Path.Combine(Path.GetTempPath(), $"watch_test_{Guid.NewGuid():N}");
        try
        {
            var world = BuildWorld();
            var simCfg = TestSimConfig.Default();
            var loc = new TileCoord(6, 6);
            var beast = MakeBeast(new EntityId(505), loc);
            world.Entities.Add(beast);
            world.WatchedEntityId   = beast.Id;
            world.WatchedEntityKind = EntityKind.LegendaryBeast;

            WorldStateSaver.Save(world, saveDir, simCfg);
            var loaded = WorldStateSaver.Load(saveDir, simCfg);

            loaded.WatchedEntityId.Should().Be(beast.Id);
            loaded.WatchedEntityKind.Should().Be(EntityKind.LegendaryBeast);
        }
        finally
        {
            WorldStateSaver.DeleteSave(saveDir);
        }
    }

    [Fact]
    public void WatchTarget_NullRoundTripsAsNull()
    {
        var saveDir = Path.Combine(Path.GetTempPath(), $"watch_test_{Guid.NewGuid():N}");
        try
        {
            var world = BuildWorld();
            var simCfg = TestSimConfig.Default();
            // WatchedEntityId left null (default) — nothing watched

            WorldStateSaver.Save(world, saveDir, simCfg);
            var loaded = WorldStateSaver.Load(saveDir, simCfg);

            loaded.WatchedEntityId.Should().BeNull();
        }
        finally
        {
            WorldStateSaver.DeleteSave(saveDir);
        }
    }
}
