using System.Threading.Channels;

namespace WorldEngine.Sim.Core;

/// <summary>
/// Unbounded channel connecting the UI thread (Enqueue) to the sim thread (DrainAll).
/// Thread-safe by Channel design — no additional locking needed.
/// </summary>
public sealed class CommandQueue
{
    private readonly Channel<ICommand> _channel = Channel.CreateUnbounded<ICommand>(
        new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Called from the UI thread to submit a player command.</summary>
    public void Enqueue(ICommand command) => _channel.Writer.TryWrite(command);

    /// <summary>Called from the sim thread once per tick to consume all pending commands.</summary>
    public IEnumerable<ICommand> DrainAll()
    {
        while (_channel.Reader.TryRead(out var cmd))
            yield return cmd;
    }
}
