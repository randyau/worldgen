using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Core;

namespace WorldEngine.Tests.Unit;

public class TileDataTests
{
    [Fact]
    public void TileData_SizeIsExactly14Bytes()
    {
        Marshal.SizeOf<TileData>().Should().Be(14);
    }

    [Fact]
    public void TileData_DefaultIsAllZero()
    {
        var tile = new TileData();

        tile.Elevation.Should().Be(0);
        tile.Fertility.Should().Be(0);
        tile.BaseTemperature.Should().Be(0);
        tile.BaseMoisture.Should().Be(0);
        tile.MagicIntensity.Should().Be(0);
        tile.BiomeType.Should().Be(0);
        tile.PlateId.Should().Be(0);
        tile.StaticFlags.Should().Be(TileStaticFlags.None);
        tile.CurrentMoisture.Should().Be(0);
        tile.DynFlags.Should().Be(TileDynFlags.None);
        tile.RoadLevel.Should().Be(0);
        tile.CivControl.Should().Be(0);
    }

    [Fact]
    public void TileStaticFlags_OrthogonalBits()
    {
        var flags = Enum.GetValues(typeof(TileStaticFlags))
            .Cast<TileStaticFlags>()
            .Where(f => f != TileStaticFlags.None)
            .ToList();

        for (int i = 0; i < flags.Count; i++)
        {
            for (int j = i + 1; j < flags.Count; j++)
            {
                (flags[i] & flags[j]).Should().Be(0, $"{flags[i]} and {flags[j]} should not share bits");
            }
        }
    }

    [Fact]
    public void TileDynFlags_OrthogonalBits()
    {
        var flags = Enum.GetValues(typeof(TileDynFlags))
            .Cast<TileDynFlags>()
            .Where(f => f != TileDynFlags.None)
            .ToList();

        for (int i = 0; i < flags.Count; i++)
        {
            for (int j = i + 1; j < flags.Count; j++)
            {
                (flags[i] & flags[j]).Should().Be(0, $"{flags[i]} and {flags[j]} should not share bits");
            }
        }
    }

    [Fact]
    public void TileStaticFlags_IsUshort()
    {
        Unsafe.SizeOf<TileStaticFlags>().Should().Be(2);
    }

    [Fact]
    public void TileDynFlags_IsByte()
    {
        Unsafe.SizeOf<TileDynFlags>().Should().Be(1);
    }

    [Fact]
    public void SeasonalProfile_SizeIsExactly8Bytes()
    {
        Marshal.SizeOf<SeasonalProfile>().Should().Be(8);
    }

    [Fact]
    public void BiomeType_HasExactly16Values()
    {
        Enum.GetValues(typeof(BiomeType)).Length.Should().Be(16);
    }

    [Fact]
    public void ChunkSummaryFlags_OrthogonalBits()
    {
        var flags = Enum.GetValues(typeof(ChunkSummaryFlags))
            .Cast<ChunkSummaryFlags>()
            .Where(f => f != ChunkSummaryFlags.None)
            .ToList();

        for (int i = 0; i < flags.Count; i++)
        {
            for (int j = i + 1; j < flags.Count; j++)
            {
                (flags[i] & flags[j]).Should().Be(0, $"{flags[i]} and {flags[j]} should not share bits");
            }
        }
    }
}
