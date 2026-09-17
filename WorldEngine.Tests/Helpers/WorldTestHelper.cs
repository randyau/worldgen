using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Helpers;

public static class WorldTestHelper
{
    public static WorldState CreateSmallWorld(int seed = 42)
    {
        // Reset the process-global entity-ID counter so WorldRng draws (keyed off entity ID)
        // don't depend on how many IDs earlier tests in the same run happened to consume.
        IdGenerator.ResetForTests();

        var config = new WorldConfig
        {
            Seed       = seed,
            WidthKm    = 100,
            HeightKm   = 100,
            TileWidthKm = 10
        };
        var simConfig = TestSimConfig.Default();
        var pipeline  = new WorldGenPipeline();
        return pipeline.RunFullAsync(config, simConfig).GetAwaiter().GetResult();
    }
}
