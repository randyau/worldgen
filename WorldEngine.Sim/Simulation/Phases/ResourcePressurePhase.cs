using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Computes food and water pressure per settlement each tick.
/// Shortages seed Acquire and Flee goals on resident characters,
/// which the UtilityScorer will translate into migration or raid behavior.
/// </summary>
public sealed class ResourcePressurePhase
{
    private readonly ResourcePressureConfig _cfg;

    public ResourcePressurePhase(SimConfig cfg) => _cfg = cfg.ResourcePressure;

    public List<PendingEvent> Execute(WorldState world, long tick)
    {
        var pending = new List<PendingEvent>();

        foreach (var (coord, stub) in world.Settlements.ToList())
        {
            var tile = world.TileGrid.GetTile(coord);

            float foodRatio  = ComputeFoodRatio(tile, stub);
            float waterRatio = ComputeWaterRatio(tile, stub);

            world.Settlements[coord] = stub with
            {
                FoodPressureRatio  = foodRatio,
                WaterPressureRatio = waterRatio
            };

            if (foodRatio < _cfg.ShortageThreshold)
            {
                SeedResourceGoals(coord, GoalObject.Food, foodRatio, world, tick);

                // Rate-limited SettlementStraining event
                if (tick - stub.LastStrainEventTick > _cfg.StrainEventCooldown)
                {
                    var impact = foodRatio < _cfg.CrisisThreshold ? "crisis" : "shortage";
                    var payload = JsonSerializer.Serialize(new
                    {
                        settlementTile = new[] { coord.X, coord.Y },
                        civId          = stub.CivId.Value,
                        resource       = "food",
                        ratio          = foodRatio,
                        impact
                    });
                    pending.Add(new PendingEvent(EventType.SettlementStraining, coord, null, payload));
                    world.Settlements[coord] = world.Settlements[coord] with
                    {
                        LastStrainEventTick = (int)tick
                    };
                }
            }

            if (waterRatio < _cfg.ShortageThreshold)
                SeedResourceGoals(coord, GoalObject.Water, waterRatio, world, tick);
        }

        return pending;
    }

    private float ComputeFoodRatio(TileData tile, SettlementStub stub)
    {
        // Supply: fertility × moisture (both 0–255 normalized to 0–1)
        float supply = (tile.Fertility / 255f) * (tile.CurrentMoisture / 255f);
        float demand = stub.Population / _cfg.PopulationCapPerTile;
        return demand > 0f ? supply / demand : 1f;
    }

    private static float ComputeWaterRatio(TileData tile, SettlementStub stub)
    {
        float supply = tile.CurrentMoisture / 255f;
        float demand = stub.Population / 100f;
        return demand > 0f ? supply / demand : 1f;
    }

    private void SeedResourceGoals(
        TileCoord coord, GoalObject resource, float ratio,
        WorldState world, long tick)
    {
        foreach (var entity in world.GetEntitiesAt(coord))
        {
            if (entity is not Tier1Character c || !c.IsAlive) continue;

            bool hasAcquire = c.Goals.Any(g => g.Type == GoalType.Acquire && g.Object == resource);
            if (!hasAcquire)
            {
                c.Goals.Add(new GoalData
                {
                    Type      = GoalType.Acquire,
                    Object    = resource,
                    Priority  = _cfg.AcquireGoalIntensity * (1f - ratio),
                    Intensity = _cfg.AcquireGoalIntensity,
                    FormedTick = (int)tick,
                    StaleSince = (int)tick
                });
            }

            // Crisis: also seed a Flee goal for low-stability characters
            if (ratio < _cfg.CrisisThreshold && c.Personality.Stability < 0.5f
                && !c.Goals.Any(g => g.Type == GoalType.Flee))
            {
                c.Goals.Add(new GoalData
                {
                    Type      = GoalType.Flee,
                    Object    = GoalObject.Region,
                    Priority  = _cfg.FleeGoalIntensity,
                    Intensity = _cfg.FleeGoalIntensity,
                    FormedTick = (int)tick,
                    StaleSince = (int)tick
                });
            }
        }
    }
}
