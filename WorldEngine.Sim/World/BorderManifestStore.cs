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

    /// <summary>Reads manifests previously written by <see cref="WriteToFile"/>.</summary>
    public static IEnumerable<(TileCoord, BorderManifest)> LoadFromFile(string path)
    {
        using var br = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read));
        int count = br.ReadInt32();
        var list = new List<(TileCoord, BorderManifest)>(count);

        for (int i = 0; i < count; i++)
        {
            var coord = new TileCoord(br.ReadInt32(), br.ReadInt32());
            var manifest = new BorderManifest();
            ReadEdge(br, manifest.North);
            ReadEdge(br, manifest.South);
            ReadEdge(br, manifest.East);
            ReadEdge(br, manifest.West);
            list.Add((coord, manifest));
        }

        return list;
    }

    private static void ReadEdge(BinaryReader br, BorderManifestSample[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i].Elevation        = br.ReadByte();
            samples[i].Moisture         = br.ReadByte();
            samples[i].HasRiverCrossing = br.ReadByte();
            samples[i].HasRoadCrossing  = br.ReadByte();
            samples[i].FlowVolume       = br.ReadByte();
        }
    }
}
