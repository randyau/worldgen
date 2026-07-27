using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Builds per-tile BorderManifests from completed world-gen layer results, for M11 local-scale
/// generation to sample when amplifying terrain and threading rivers across a tile boundary.
/// Elevation/moisture samples are a flat blend of the two adjacent tiles' own byte values — real
/// sub-tile variation is added by the terrain-amplification algorithm (M11 phase 11.3); this
/// builder's job is only to guarantee both sides of an edge agree.
/// </summary>
public static class BorderManifestBuilder
{
    public static List<(TileCoord Coord, BorderManifest Manifest)> Build(WorldGenContext ctx)
    {
        var elev    = ctx.Elevation!;
        var climate = ctx.Climate!;
        var river   = ctx.River!;
        int w = ctx.TileWidth, h = ctx.TileHeight;

        var manifests = new BorderManifest[ctx.TileCount];
        for (int i = 0; i < ctx.TileCount; i++)
            manifests[i] = new BorderManifest();

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = ctx.IndexOf(x, y);
            var coord = new TileCoord(x, y);

            FillEdge(manifests[i].North, elev, climate, i, y > 0     ? ctx.IndexOf(coord.North())   : null);
            FillEdge(manifests[i].South, elev, climate, i, y < h - 1 ? ctx.IndexOf(coord.South())   : null);
            FillEdge(manifests[i].East,  elev, climate, i, ctx.IndexOf(coord.East(w)));
            FillEdge(manifests[i].West,  elev, climate, i, ctx.IndexOf(coord.West(w)));
        }

        foreach (var crossing in river.Crossings)
        {
            int fromIdx = ctx.IndexOf(crossing.FromTile);
            int toIdx   = ctx.IndexOf(crossing.ToTile);
            ApplyCrossing(manifests[fromIdx].GetEdge(crossing.Edge), crossing);
            ApplyCrossing(manifests[toIdx].GetEdge(crossing.Edge.Opposite()), crossing);
        }

        var result = new List<(TileCoord, BorderManifest)>(ctx.TileCount);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            result.Add((new TileCoord(x, y), manifests[ctx.IndexOf(x, y)]));
        return result;
    }

    private static void FillEdge(
        BorderManifestSample[] samples, ElevationResult elev, ClimateResult climate,
        int selfIdx, int? neighborIdx)
    {
        byte elevation = neighborIdx is { } n
            ? (byte)((elev.Elevation[selfIdx] + elev.Elevation[n]) / 2)
            : elev.Elevation[selfIdx];
        byte moisture = neighborIdx is { } m
            ? (byte)((climate.BaseMoisture[selfIdx] + climate.BaseMoisture[m]) / 2)
            : climate.BaseMoisture[selfIdx];

        for (int k = 0; k < BorderManifest.SampleCount; k++)
        {
            samples[k].Elevation = elevation;
            samples[k].Moisture  = moisture;
        }
    }

    private static void ApplyCrossing(BorderManifestSample[] samples, RiverCrossing crossing)
    {
        int center = Math.Clamp(
            (int)(crossing.Position * BorderManifest.SampleCount), 0, BorderManifest.SampleCount - 1);
        int radius = Math.Max(1, (int)(crossing.Width * BorderManifest.SampleCount / 2));
        byte flowByte = (byte)Math.Clamp(crossing.FlowVolume, 0, 255);

        int lo = Math.Max(0, center - radius);
        int hi = Math.Min(BorderManifest.SampleCount - 1, center + radius);
        for (int k = lo; k <= hi; k++)
        {
            samples[k].HasRiverCrossing = 1;
            samples[k].FlowVolume       = flowByte;
        }
    }
}
