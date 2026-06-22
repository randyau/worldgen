using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Identifies candidate tiles for Points of Interest: river mouths, high-magic volcanic sites,
/// coastal resource tiles, and tectonic fault/junction tiles with high deposit potential.
/// POI selection from candidates happens in a later pass during sim initialization.
/// </summary>
public sealed class PoiCandidateLayer : IWorldGenLayer<PoiResult>
{
    public PoiResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var ocean = ctx.Ocean!;
        var tec   = ctx.Tectonic!;
        int n = ctx.TileCount;

        var result = new PoiResult(n);

        for (int i = 0; i < n; i++)
        {
            if (ocean.IsOcean[i]) continue;

            bool isCandidate = false;

            // High-magic volcanic sites
            if (ctx.Magic is { } magic && magic.IsPOICandidate[i])
                isCandidate = true;

            // River-mouth tiles (coastal with a river)
            if (ctx.River is { } river && river.HasRiver[i] && ocean.IsCoastal[i])
                isCandidate = true;

            // High-potential tectonic junctions
            if (tec.IsFaultLine[i] && tec.DepositPotential[i] > 0.7f)
                isCandidate = true;

            result.IsPOICandidate[i] = isCandidate;
        }

        progress?.Report(1.0f);
        return result;
    }
}
