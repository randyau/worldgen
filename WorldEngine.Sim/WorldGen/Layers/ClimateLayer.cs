using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates base temperature (latitude cosine + lapse rate) and moisture
/// via two-band wind sweep (trade winds tropical, westerlies mid-lat) with rain shadow.
/// Also assigns storm corridor flag and computes per-tile SeasonalProfiles.
/// </summary>
public sealed class ClimateLayer : IWorldGenLayer<ClimateResult>
{
    public ClimateResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // DECISION: Stub — real implementation in story 1.3.6
        var result = new ClimateResult(ctx.TileCount);
        // Ensure SeasonalProfiles are initialized to zero (value struct default)
        Array.Clear(result.SeasonalProfiles, 0, result.SeasonalProfiles.Length);
        progress?.Report(1.0f);
        return result;
    }
}
