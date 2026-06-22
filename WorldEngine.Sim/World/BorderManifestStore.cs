using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

public static class BorderManifestStore
{
    /// <summary>
    /// Writes all manifests to a binary file.
    /// Format: [TileCount:int][TileCoord.X:int][TileCoord.Y:int][4 edges × 64 samples × 5 bytes]...
    /// </summary>
    public static void WriteToFile(string path, IEnumerable<(TileCoord Coord, BorderManifest Manifest)> manifests)
    {
        var list = manifests.ToList();
        using var bw = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write));
        bw.Write(list.Count);
        foreach (var (coord, manifest) in list)
        {
            bw.Write(coord.X);
            bw.Write(coord.Y);
            WriteEdge(bw, manifest.North);
            WriteEdge(bw, manifest.South);
            WriteEdge(bw, manifest.East);
            WriteEdge(bw, manifest.West);
        }
    }

    private static void WriteEdge(BinaryWriter bw, BorderManifestSample[] samples)
    {
        foreach (var s in samples)
        {
            bw.Write(s.Elevation);
            bw.Write(s.Moisture);
            bw.Write(s.HasRiverCrossing);
            bw.Write(s.HasRoadCrossing);
            bw.Write(s.FlowVolume);
        }
    }

    // M4 feature — full manifest loading is deferred until Milestone 4
    public static IEnumerable<(TileCoord, BorderManifest)> LoadFromFile(string path)
    {
        throw new NotImplementedException("M4 feature");
    }
}
