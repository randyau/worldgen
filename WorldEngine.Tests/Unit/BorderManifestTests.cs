using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Tests.Unit;

public class BorderManifestTests
{
    [Fact]
    public void BorderManifest_Has4Edges()
    {
        var manifest = new BorderManifest();

        manifest.North.Should().NotBeNull();
        manifest.South.Should().NotBeNull();
        manifest.East.Should().NotBeNull();
        manifest.West.Should().NotBeNull();
    }

    [Fact]
    public void BorderManifest_EachEdgeHas64Samples()
    {
        var manifest = new BorderManifest();

        manifest.North.Length.Should().Be(BorderManifest.SampleCount);
        manifest.South.Length.Should().Be(BorderManifest.SampleCount);
        manifest.East.Length.Should().Be(BorderManifest.SampleCount);
        manifest.West.Length.Should().Be(BorderManifest.SampleCount);
    }

    [Fact]
    public void BorderManifest_WriteReadRoundTrip()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var manifests = new List<(TileCoord, BorderManifest)>();

            for (int i = 0; i < 10; i++)
            {
                var coord = new TileCoord(i, i * 2);
                var manifest = new BorderManifest();
                manifest.North[0].Elevation = (byte)(10 + i);
                manifest.North[0].Moisture = (byte)(20 + i);
                manifest.North[0].HasRiverCrossing = (byte)(i % 2);
                manifest.North[0].HasRoadCrossing = 0;
                manifest.North[0].FlowVolume = (byte)(30 + i);
                manifests.Add((coord, manifest));
            }

            BorderManifestStore.WriteToFile(tempFile, manifests);

            using var br = new BinaryReader(File.Open(tempFile, FileMode.Open, FileAccess.Read));
            int tileCount = br.ReadInt32();
            tileCount.Should().Be(10);

            int firstX = br.ReadInt32();
            int firstY = br.ReadInt32();
            firstX.Should().Be(0);
            firstY.Should().Be(0);

            byte firstElevation = br.ReadByte();
            firstElevation.Should().Be(10);

            byte firstMoisture = br.ReadByte();
            firstMoisture.Should().Be(20);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void BorderManifestStore_LoadThrowsNotImplemented()
    {
        var action = () => BorderManifestStore.LoadFromFile("any.bin");

        action.Should().Throw<NotImplementedException>();
    }
}
