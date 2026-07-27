using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class SnapshotBuilderTests
{
    private static WorldState BuildWorld(int seed = 1)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    private static readonly SnapshotBuilder _builder = new();

    private static WorldSnapshot Snap(WorldState world) =>
        _builder.Build(world, OverlayType.Biome,
            SimSpeed.Normal, paused: false, ticksPerSecond: 4, recentEvents: Array.Empty<SimEvent>());

    [Fact]
    public void SnapshotBuilder_EffectiveTempHigherAtEquator()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var snap = Snap(world);

        int equatorY = h / 2;
        int polarY   = 0;

        float equatorTemp = 0f; int equatorCount = 0;
        float polarTemp   = 0f; int polarCount   = 0;

        for (int x = 0; x < w; x++)
        {
            equatorTemp += snap.AllTiles[equatorY * w + x].EffectiveTemperature; equatorCount++;
            polarTemp   += snap.AllTiles[polarY   * w + x].EffectiveTemperature; polarCount++;
        }

        float meanEquator = equatorCount > 0 ? equatorTemp / equatorCount : 0;
        float meanPolar   = polarCount   > 0 ? polarTemp   / polarCount   : 0;

        meanEquator.Should().BeGreaterThan(meanPolar,
            "equatorial tiles should have higher effective temperature than polar tiles");
    }

    [Fact]
    public void SnapshotBuilder_HasActiveDisasterTrueWhenInRegistry()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var testCoord = new TileCoord(w / 2, h / 2);

        world.ActiveTileDisasters[testCoord] = new List<ActiveDisaster>
        {
            new ActiveDisaster(DisasterType.Wildfire, 0.5f, 5, new EventId(1))
        };

        var snap = Snap(world);

        snap.AllTiles[testCoord.Y * w + testCoord.X].HasActiveDisaster.Should().BeTrue(
            "tile with an entry in ActiveTileDisasters must have HasActiveDisaster=true");
    }

    [Fact]
    public void SnapshotBuilder_HasActiveDisasterFalseWhenNotInRegistry()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var snap = Snap(world);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                if (!world.ActiveTileDisasters.ContainsKey(coord))
                    snap.AllTiles[y * w + x].HasActiveDisaster.Should().BeFalse(
                        $"tile {coord} not in ActiveTileDisasters must have HasActiveDisaster=false");
            }
    }

    [Fact]
    public void SnapshotBuilder_InspectedTilePopulatedWhenSet()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        world.InspectedTile = new TileCoord(w / 2, h / 2);
        var snap = Snap(world);

        snap.InspectedTile.Should().NotBeNull(
            "InspectedTile should be set in the snapshot when world.InspectedTile is set");
        snap.InspectedTile!.Coord.Should().Be(world.InspectedTile.Value);
    }

    [Fact]
    public void SnapshotBuilder_InspectedTileNullWhenNotSet()
    {
        var world = BuildWorld();
        var snap = Snap(world);
        snap.InspectedTile.Should().BeNull("no tile selected → InspectedTile must be null");
    }

    [Fact]
    public void TileDisplayData_IsImmutableRecord()
    {
        // Sealed records implement IEquatable<T> and have a compiler-generated <Clone>$ method.
        typeof(TileDisplayData).Should().Implement<IEquatable<TileDisplayData>>(
            "TileDisplayData must be a record (immutable by convention)");

        var cloneMethod = typeof(TileDisplayData).GetMethod("<Clone>$");
        cloneMethod.Should().NotBeNull("records expose a <Clone>$ method — confirms this is a record type");

        // All properties on a positional record use init-only setters.
        // In reflection, init setters are decorated with IsExternalInit — verify via IsInitOnly flag.
        foreach (var prop in typeof(TileDisplayData).GetProperties())
        {
            bool isInitOnly = prop.SetMethod?.ReturnParameter
                .GetRequiredCustomModifiers()
                .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true;
            isInitOnly.Should().BeTrue(
                $"TileDisplayData.{prop.Name} should use an init-only setter (positional record property)");
        }
    }

    [Fact]
    public void TileInspectorData_ContainsAllDeposits()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a tile that has deposits
        var depositCoord = world.ResourceRegistry.Keys.FirstOrDefault();
        if (depositCoord == default) return; // no deposits in this seed — skip

        world.InspectedTile = depositCoord;
        var snap = Snap(world);

        snap.InspectedTile!.Deposits.Should()
            .BeEquivalentTo(world.ResourceRegistry[depositCoord],
                "inspector data must include all deposits from ResourceRegistry");
    }

    [Fact]
    public void TileInspectorData_ContainsAllDisasters()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var coord = new TileCoord(w / 2, h / 2);

        var disasters = new List<ActiveDisaster>
        {
            new(DisasterType.Wildfire, 0.8f, 3, new EventId(10)),
            new(DisasterType.Flood, 0.3f, 2, new EventId(11))
        };
        world.ActiveTileDisasters[coord] = disasters;
        world.InspectedTile = coord;

        var snap = Snap(world);

        snap.InspectedTile!.Disasters.Should()
            .BeEquivalentTo(disasters, "inspector data must include all disasters from registry");
    }

    [Fact]
    public void TileInspectorData_IsInActiveDroughtCorrect()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Pick a land tile
        TileCoord landCoord = default;
        for (int y = 0; y < h && landCoord == default; y++)
            for (int x = 0; x < w && landCoord == default; x++)
            {
                var c = new TileCoord(x, y);
                if (world.IsLand(c)) landCoord = c;
            }

        var tile = world.GetTile(landCoord);
        var biome = (BiomeType)tile.BiomeType;

        // Create a drought matching this tile's biome
        int latBand = landCoord.Y / (h / 4);
        world.ActiveDroughts.Add(new ActiveDrought(latBand, biome, 0.6f, 2, new EventId(99)));
        world.InspectedTile = landCoord;

        var snap = Snap(world);

        snap.InspectedTile!.IsInActiveDrought.Should().BeTrue(
            "tile whose biome+latitude band matches an ActiveDrought should report IsInActiveDrought=true");
    }

    // ── Spotlight projection (M7) ─────────────────────────────────────────────

    [Fact]
    public void SnapshotBuilder_SpotlightActive_PopulatesCharacterIdAndMoveTarget()
    {
        var world = BuildWorld();
        var character = MakeTier1(world, new EntityId(910));
        world.Entities.Add(character);
        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent { MoveTarget = new TileCoord(3, 4) };

        var snap = Snap(world);

        snap.SpotlightCharacterId.Should().Be(character.Id);
        snap.SpotlightMoveTarget.Should().Be(new TileCoord(3, 4));
    }

    [Fact]
    public void SnapshotBuilder_NoSpotlight_BothSpotlightFieldsNull()
    {
        var world = BuildWorld();
        var snap = Snap(world);

        snap.SpotlightCharacterId.Should().BeNull();
        snap.SpotlightMoveTarget.Should().BeNull();
    }

    [Fact]
    public void SnapshotBuilder_SpotlightActiveWithNoMoveTarget_MoveTargetNull()
    {
        var world = BuildWorld();
        var character = MakeTier1(world, new EntityId(911));
        world.Entities.Add(character);
        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent(); // no move target set

        var snap = Snap(world);

        snap.SpotlightCharacterId.Should().Be(character.Id);
        snap.SpotlightMoveTarget.Should().BeNull();
    }

    // ── Watch panel branching (M11 UX rework: polymorphic watch target) ──────

    private static Tier1Character MakeTier1(WorldState world, EntityId id)
    {
        var loc = new TileCoord(world.TileGrid.TileWidth / 2, world.TileGrid.TileHeight / 2);
        return new Tier1Character(
            id, loc,
            PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
            new IdentityData("Test", "the Tester", "test", null, null, default, 0, 0),
            100, 200);
    }

    private static LegendaryBeast MakeBeast(WorldState world, EntityId id) => new(
        id, speciesId: "wolf", name: "Test Wolf",
        location: new TileCoord(world.TileGrid.TileWidth / 2, world.TileGrid.TileHeight / 2),
        isLegendary: false, maxHealth: 50, strength: 10, speed: 5, aggression: 0.3f, territoryRadius: 3,
        abilities: Array.Empty<string>(), maxAgeSeason: 100,
        foodDepletion: 0.05f, foodFromHunt: 0.3f, foodFromGraze: 0f,
        reproductionChance: 0.1f, reproductionMinAge: 4, reproductionFoodThreshold: 0.6f,
        hibernates: false, prefersCompany: false);

    [Fact]
    public void SnapshotBuilder_WatchedTier1Character_PopulatesRichCharacterSnapshot()
    {
        var world = BuildWorld();
        var character = MakeTier1(world, new EntityId(900));
        world.Entities.Add(character);
        world.WatchedEntityId   = character.Id;
        world.WatchedEntityKind = EntityKind.Tier1Character;

        var snap = Snap(world);

        snap.WatchedCharacter.Should().NotBeNull("watching a Tier1Character must populate the rich needs/goals snapshot");
        snap.WatchedCharacter!.Id.Should().Be(character.Id);
        snap.WatchedBasic.Should().BeNull("only one of WatchedCharacter/WatchedBasic should be populated at a time");
    }

    [Fact]
    public void SnapshotBuilder_WatchedBeast_PopulatesBasicSnapshotNotCharacterSnapshot()
    {
        var world = BuildWorld();
        var beast = MakeBeast(world, new EntityId(901));
        world.Entities.Add(beast);
        world.WatchedEntityId   = beast.Id;
        world.WatchedEntityKind = EntityKind.LegendaryBeast;

        var snap = Snap(world);

        snap.WatchedCharacter.Should().BeNull("a watched beast has no needs/goals — must not populate the character snapshot");
        snap.WatchedBasic.Should().NotBeNull("watching a beast must populate the thin vitals snapshot");
        snap.WatchedBasic!.Id.Should().Be(beast.Id);
        snap.WatchedBasic.Kind.Should().Be(EntityKind.LegendaryBeast);
        snap.WatchedBasic.SpeciesId.Should().Be("wolf");
    }

    [Fact]
    public void SnapshotBuilder_NoWatchTarget_BothWatchSnapshotsNull()
    {
        var world = BuildWorld();
        var snap = Snap(world);

        snap.WatchedCharacter.Should().BeNull();
        snap.WatchedBasic.Should().BeNull();
    }

    [Fact]
    public void SnapshotBuilder_WatchedDeadEntity_BothWatchSnapshotsNull()
    {
        var world = BuildWorld();
        var beast = MakeBeast(world, new EntityId(902));
        beast.IsAlive = false;
        world.Entities.Add(beast);
        world.WatchedEntityId   = beast.Id;
        world.WatchedEntityKind = EntityKind.LegendaryBeast;

        var snap = Snap(world);

        snap.WatchedCharacter.Should().BeNull();
        snap.WatchedBasic.Should().BeNull("a dead watched entity should not populate either watch snapshot");
    }
}
