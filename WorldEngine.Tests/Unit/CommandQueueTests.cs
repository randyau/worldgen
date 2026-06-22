using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Tests.Unit;

public class CommandQueueTests
{
    [Fact]
    public void CommandQueue_EnqueuedCommandsReturnedByDrain()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new SetSimSpeed(SimSpeed.Fast));
        queue.Enqueue(new PauseToggle());
        queue.Enqueue(new SetViewport(0, 0, 80, 60));

        var drained = queue.DrainAll().ToList();
        drained.Should().HaveCount(3, "three commands were enqueued");
    }

    [Fact]
    public void CommandQueue_DrainClearsQueue()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new SetSimSpeed(SimSpeed.Slow));

        queue.DrainAll().ToList(); // consume
        var second = queue.DrainAll().ToList();

        second.Should().BeEmpty("drain should consume all items; second drain must be empty");
    }

    [Fact]
    public void CommandQueue_DrainOnEmptyQueueReturnsEmpty()
    {
        var queue = new CommandQueue();
        var result = queue.DrainAll().ToList();
        result.Should().BeEmpty("draining an empty queue must return empty without throwing or hanging");
    }

    [Fact]
    public void CommandQueue_PlayerCommandsAreICommands()
    {
        ICommand[] cmds =
        [
            new SetSimSpeed(SimSpeed.Normal),
            new PauseToggle(),
            new StepOneTick(),
            new SetViewport(0, 0, 80, 60),
            new SetInspectedTile(new TileCoord(5, 10)),
            new SetActiveOverlay(OverlayType.Temperature),
        ];

        cmds.Should().AllBeAssignableTo<ICommand>(
            "all player command types must implement ICommand");
    }
}
