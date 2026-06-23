using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Beasts;

namespace WorldEngine.Tests.Unit;

public class EntityRegistryTests
{
    private static LegendaryBeast MakeBeast(TileCoord coord, string speciesId = "wolf") =>
        new(EntityId.New(), speciesId, "The Pale Wolf", coord, isLegendary: true,
            maxHealth: 80, strength: 12, speed: 4, aggression: 0.65f, territoryRadius: 10,
            abilities: [], maxAgeSeason: 60, foodDepletion: 0.12f, foodFromHunt: 0.55f,
            foodFromGraze: 0f, reproductionChance: 0.08f, reproductionMinAge: 12,
            reproductionFoodThreshold: 0.65f, hibernates: false, prefersCompany: false);

    [Fact]
    public void Add_PopulatesAllAndSpatialIndex()
    {
        var registry = new EntityRegistry();
        var coord = new TileCoord(5, 5);
        var beast = MakeBeast(coord);

        registry.Add(beast);

        registry.All.Should().ContainKey(beast.Id);
        registry.GetAt(coord).Should().Contain(beast);
        registry.Beasts.Should().Contain(beast);
    }

    [Fact]
    public void Remove_ClearsAllAndSpatialIndex()
    {
        var registry = new EntityRegistry();
        var coord = new TileCoord(3, 3);
        var beast = MakeBeast(coord);

        registry.Add(beast);
        registry.Remove(beast.Id);

        registry.All.Should().NotContainKey(beast.Id);
        registry.GetAt(coord).Should().BeEmpty();
        registry.Beasts.Should().NotContain(beast);
    }

    [Fact]
    public void UpdateLocation_MovesBetweenSpatialBuckets()
    {
        var registry = new EntityRegistry();
        var old = new TileCoord(1, 1);
        var next = new TileCoord(2, 2);
        var beast = MakeBeast(old);

        registry.Add(beast);
        registry.UpdateLocation(beast.Id, old, next);

        registry.GetAt(old).Should().BeEmpty("entity moved away");
        registry.GetAt(next).Should().ContainSingle(e => e.Id == beast.Id);
    }

    [Fact]
    public void GetAt_EmptyCoord_ReturnsEmpty()
    {
        var registry = new EntityRegistry();
        registry.GetAt(new TileCoord(99, 99)).Should().BeEmpty();
    }

    [Fact]
    public void CountBySpecies_CountsOnlyLiveSpecies()
    {
        var registry = new EntityRegistry();
        var wolf1 = MakeBeast(new TileCoord(0, 0), "wolf");
        var wolf2 = MakeBeast(new TileCoord(1, 0), "wolf");
        var bear  = MakeBeast(new TileCoord(2, 0), "brown_bear");

        registry.Add(wolf1);
        registry.Add(wolf2);
        registry.Add(bear);

        registry.CountBySpecies("wolf").Should().Be(2);
        registry.CountBySpecies("brown_bear").Should().Be(1);
        registry.CountBySpecies("lion").Should().Be(0);
    }

    [Fact]
    public void MultipleEntitiesOnSameTile_AllVisible()
    {
        var registry = new EntityRegistry();
        var coord = new TileCoord(4, 4);
        var a = MakeBeast(coord, "wolf");
        var b = MakeBeast(coord, "wolf");

        registry.Add(a);
        registry.Add(b);

        var atCoord = registry.GetAt(coord).ToList();
        atCoord.Should().HaveCount(2);
    }
}
