using FluentAssertions;
using WorldEngine.Sim.Core;

namespace WorldEngine.Tests.Unit;

public class CoreValueTypeTests
{
    [Fact]
    public void TileCoord_EastWestWrapsAt0()
    {
        var coord = new TileCoord(-1, 0);
        var wrapped = coord.Wrap(400);
        wrapped.X.Should().Be(399);
    }

    [Fact]
    public void TileCoord_EastWestWrapsAtMax()
    {
        var coord = new TileCoord(400, 0);
        var wrapped = coord.Wrap(400);
        wrapped.X.Should().Be(0);
    }

    [Fact]
    public void TileCoord_ChebyshevDistanceIsSymmetric()
    {
        var a = new TileCoord(0, 0);
        var b = new TileCoord(5, 3);

        var distAB = a.ChebyshevDistance(b);
        var distBA = b.ChebyshevDistance(a);

        distAB.Should().Be(distBA);
    }

    [Fact]
    public void TileCoord_CardinalNeighborsReturnFourCoords()
    {
        var coord = new TileCoord(10, 10);

        var north = coord.North();
        var south = coord.South();
        var east = coord.East(400);
        var west = coord.West(400);

        var neighbors = new[] { north, south, east, west };
        neighbors.Should().HaveCount(4);
        neighbors.Distinct().Should().HaveCount(4);
    }

    [Fact]
    public void TileCoord_RecordEqualityByValue()
    {
        var coord1 = new TileCoord(5, 10);
        var coord2 = new TileCoord(5, 10);

        coord1.Should().Be(coord2);
    }

    [Fact]
    public void EntityId_NewIsUnique()
    {
        var id1 = EntityId.New();
        var id2 = EntityId.New();

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void WorldRng_DeterministicSameInputs()
    {
        const int seed = 42;
        const long tick = 100;
        const int x = 5;
        const int y = 10;
        const int salt = 0;

        var value1 = WorldRng.FloatAt(seed, tick, x, y, salt);
        var value2 = WorldRng.FloatAt(seed, tick, x, y, salt);

        value1.Should().Be(value2);
    }

    [Fact]
    public void WorldRng_DifferentSaltsDifferentOutputs()
    {
        const int seed = 42;
        const long tick = 100;
        const int x = 5;
        const int y = 10;

        var value1 = WorldRng.FloatAt(seed, tick, x, y, salt: 0);
        var value2 = WorldRng.FloatAt(seed, tick, x, y, salt: 1);

        value1.Should().NotBe(value2);
    }

    [Fact]
    public void WorldRng_DifferentCoordsDifferentOutputs()
    {
        const int seed = 42;
        const long tick = 100;
        const int salt = 0;

        var value1 = WorldRng.FloatAt(seed, tick, x: 0, y: 0, salt);
        var value2 = WorldRng.FloatAt(seed, tick, x: 1, y: 0, salt);

        value1.Should().NotBe(value2);
    }

    [Fact]
    public void WorldRng_OutputInRange0To1()
    {
        const int seed = 42;
        const int salt = 0;

        for (int i = 0; i < 1000; i++)
        {
            var value = WorldRng.FloatAt(seed, tick: i, x: i % 100, y: i / 100, salt);
            value.Should().BeGreaterThanOrEqualTo(0.0f);
            value.Should().BeLessThan(1.0f);
        }
    }

    [Fact]
    public void WorldConfig_TileCountsDerivedFromKm()
    {
        var config = new WorldConfig { Seed = 1, WidthKm = 4000, HeightKm = 3000, TileWidthKm = 10 };

        config.TileWidth.Should().Be(400);
        config.TileHeight.Should().Be(300);
    }
}
