using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Tests.Unit;

public class RegistryTypeTests
{
    [Fact]
    public void ResourceDeposit_ValueEquality()
    {
        var deposit1 = new ResourceDeposit("Iron", 200, 50);
        var deposit2 = new ResourceDeposit("Iron", 200, 50);

        deposit1.Should().Be(deposit2);
    }

    [Fact]
    public void ResourceDeposit_SupportsListStacking()
    {
        var deposits = new List<ResourceDeposit>
        {
            new ResourceDeposit("Iron", 200, 50),
            new ResourceDeposit("Gold", 150, 100)
        };

        deposits.Should().HaveCount(2);
    }

    [Fact]
    public void ActiveDisaster_ImmutableRecord()
    {
        var originId = new EventId(12345);
        var disaster1 = new ActiveDisaster(DisasterType.Wildfire, 0.5f, 10, originId);
        var disaster2 = disaster1 with { Intensity = 0.9f };

        disaster1.Intensity.Should().Be(0.5f);
        disaster2.Intensity.Should().Be(0.9f);
    }

    [Fact]
    public void ActiveDisaster_TicksRemainingNegativeOneValid()
    {
        var originId = new EventId(67890);
        var disaster = new ActiveDisaster(DisasterType.VolcanicAsh, 0.3f, -1, originId);

        disaster.TicksRemaining.Should().Be(-1);
    }

    [Fact]
    public void ActiveDrought_MatchesByLatitudeBandAndBiome()
    {
        var originId1 = new EventId(111);
        var originId2 = new EventId(222);

        var drought1 = new ActiveDrought(5, BiomeType.Grassland, 0.6f, 8, originId1);
        var drought2 = new ActiveDrought(5, BiomeType.Grassland, 0.7f, 10, originId2);
        var drought3 = new ActiveDrought(6, BiomeType.Grassland, 0.6f, 8, originId1);

        drought1.Should().NotBe(drought2);
        drought1.Should().NotBe(drought3);
    }

    [Fact]
    public void DisasterType_HasFourValues()
    {
        Enum.GetValues(typeof(DisasterType)).Length.Should().Be(4);
    }
}
