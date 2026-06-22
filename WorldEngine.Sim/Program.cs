using Microsoft.Extensions.Logging;
using WorldEngine.Sim.Config;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger("WorldEngine");

int seed = 0;
int years = 100;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--seed" && int.TryParse(args[i + 1], out var s)) seed = s;
    if (args[i] == "--years" && int.TryParse(args[i + 1], out var y)) years = y;
}

var config = SimConfigLoader.LoadOrCreateDefault();
logger.LogInformation("WorldEngine starting — seed={Seed}, years={Years}", seed, years);

// WorldGenPipeline.Run() will be called here once implemented (Phase 3)

logger.LogInformation("Done.");
