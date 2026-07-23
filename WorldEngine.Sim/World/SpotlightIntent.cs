using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Sim.World;

/// <summary>Current spotlight player intent: what the player wants the spotlit character to do.</summary>
public sealed class SpotlightIntent
{
    public TileCoord? MoveTarget   { get; set; }
    public GoalType?  GoalIntent   { get; set; }
    public EntityId?  SocialTarget { get; set; }

    public void Clear() { MoveTarget = null; GoalIntent = null; SocialTarget = null; }
}
