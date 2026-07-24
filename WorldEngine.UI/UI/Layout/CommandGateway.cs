using WorldEngine.Sim.Core;

namespace WorldEngine.UI.UI.Layout;

// MAP: Cross-cutting — thin wrapper over CommandQueue.Enqueue so panels never touch the queue directly.
/// <summary>
/// The "change the world" half of the two-bus model (framework §7.1) — panels enqueue
/// <see cref="ICommand"/>s through this instead of holding a <see cref="CommandQueue"/> directly.
/// </summary>
public sealed class CommandGateway
{
    private readonly CommandQueue _queue;

    public CommandGateway(CommandQueue queue) => _queue = queue;

    public void Enqueue(ICommand command) => _queue.Enqueue(command);
}
