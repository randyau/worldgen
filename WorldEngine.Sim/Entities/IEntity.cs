using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities;

/// <summary>
/// The core simulation entity interface. Every simulated object implements this.
/// Entities NEVER mutate world state directly. They emit ICommand instances
/// during the EMIT step which are resolved by CommandResolver in the RESOLVE step.
/// </summary>
public interface IEntity
{
    EntityId Id { get; }
    TileCoord Location { get; }
    EntityKind Kind { get; }
    bool IsAlive { get; }

    /// <summary>Emit commands for this tick phase. Must not have side effects.</summary>
    IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);

    EntitySnapshot ToSnapshot();
}
