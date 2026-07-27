using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// M7 spotlight commands beyond EnterSpotlight had zero test coverage — ExitSpotlight and the
/// three SetSpotlight*Intent commands are exercised here via the real SimLoop/CommandQueue path
/// (SimLoop's command switch is private, unlike AuthoringResolver/AuthoringValidator).
/// </summary>
public class SpotlightCommandTests
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

    [Fact]
    public async Task ExitSpotlight_ClearsSpotlightIdAndIntent()
    {
        var world = BuildWorld();
        var character = MakeTier1(new EntityId(800), new TileCoord(5, 5));
        world.Entities.Add(character);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new EnterSpotlight(character.Id));
        await Task.Delay(150);
        world.SpotlightCharacterId.Should().Be(character.Id, "sanity check: spotlight was entered");

        cmdQueue.Enqueue(new ExitSpotlight());
        await Task.Delay(150);
        loop.Stop();

        world.SpotlightCharacterId.Should().BeNull();
        world.SpotlightIntent.Should().BeNull();
    }

    [Fact]
    public async Task SetSpotlightMoveIntent_SetsMoveTargetOnActiveIntent()
    {
        var world = BuildWorld();
        var character = MakeTier1(new EntityId(801), new TileCoord(5, 5));
        world.Entities.Add(character);
        var target = new TileCoord(10, 10);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new EnterSpotlight(character.Id));
        await Task.Delay(150);

        cmdQueue.Enqueue(new SetSpotlightMoveIntent(target));
        await Task.Delay(150);
        loop.Stop();

        world.SpotlightIntent.Should().NotBeNull();
        world.SpotlightIntent!.MoveTarget.Should().Be(target);
    }

    [Fact]
    public async Task SetSpotlightGoalIntent_SetsGoalIntentOnActiveIntent()
    {
        var world = BuildWorld();
        var character = MakeTier1(new EntityId(802), new TileCoord(5, 5));
        world.Entities.Add(character);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new EnterSpotlight(character.Id));
        await Task.Delay(150);

        cmdQueue.Enqueue(new SetSpotlightGoalIntent(GoalType.FoundCity));
        await Task.Delay(150);
        loop.Stop();

        world.SpotlightIntent.Should().NotBeNull();
        world.SpotlightIntent!.GoalIntent.Should().Be(GoalType.FoundCity);
    }

    [Fact]
    public async Task SetSpotlightSocialIntent_SetsSocialTargetOnActiveIntent()
    {
        var world = BuildWorld();
        var character = MakeTier1(new EntityId(803), new TileCoord(5, 5));
        var other     = MakeTier1(new EntityId(804), new TileCoord(6, 6));
        world.Entities.Add(character);
        world.Entities.Add(other);

        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();
        cmdQueue.Enqueue(new EnterSpotlight(character.Id));
        await Task.Delay(150);

        cmdQueue.Enqueue(new SetSpotlightSocialIntent(other.Id));
        await Task.Delay(150);
        loop.Stop();

        world.SpotlightIntent.Should().NotBeNull();
        world.SpotlightIntent!.SocialTarget.Should().Be(other.Id);
    }

    [Fact]
    public async Task SetSpotlightMoveIntent_NoOpsWhenNoSpotlightActive()
    {
        var world = BuildWorld();
        var (loop, cmdQueue) = MakeLoop(world);
        loop.Start();

        cmdQueue.Enqueue(new SetSpotlightMoveIntent(new TileCoord(1, 1)));
        await Task.Delay(150);
        loop.Stop();

        world.SpotlightIntent.Should().BeNull("a move-intent command with no active spotlight must not create one");
    }

    [Fact]
    public void SpotlightedCharacterDeath_ClearsSpotlightIdAndIntent()
    {
        var world = BuildWorld();
        var character = MakeTier1(new EntityId(805), new TileCoord(5, 5));
        world.Entities.Add(character);

        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent { MoveTarget = new TileCoord(9, 9) };

        character.Health = 0; // force death this tick

        var phase = new CharacterBehaviorPhase(TestSimConfig.Default());
        phase.Execute(world, tick: 1L, isAnnualTick: false);

        character.IsAlive.Should().BeFalse("sanity check: the character actually died this tick");
        world.SpotlightCharacterId.Should().BeNull("no state may leak past a spotlighted character's death");
        world.SpotlightIntent.Should().BeNull();
    }

    [Fact]
    public void NonSpotlightedCharacterDeath_DoesNotTouchUnrelatedSpotlight()
    {
        var world = BuildWorld();
        var spotlighted = MakeTier1(new EntityId(806), new TileCoord(5, 5));
        var dying       = MakeTier1(new EntityId(807), new TileCoord(6, 6));
        world.Entities.Add(spotlighted);
        world.Entities.Add(dying);

        world.SpotlightCharacterId = spotlighted.Id;
        world.SpotlightIntent      = new SpotlightIntent { MoveTarget = new TileCoord(9, 9) };

        dying.Health = 0;

        var phase = new CharacterBehaviorPhase(TestSimConfig.Default());
        phase.Execute(world, tick: 1L, isAnnualTick: false);

        dying.IsAlive.Should().BeFalse();
        world.SpotlightCharacterId.Should().Be(spotlighted.Id, "an unrelated character's death must not clear an active spotlight");
        world.SpotlightIntent.Should().NotBeNull();
    }
}
