using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Unit tests for the artifact snapshot projection (W3 — M5 artifacts).
/// Verifies that WorldState.Artifacts is correctly projected into WorldSnapshot.Artifacts
/// with correct OwnerDesc formatting for character / settlement / lost owners.
/// </summary>
public class ArtifactSnapshotTests
{
    private static readonly SnapshotBuilder _builder = new();

    private static WorldSnapshot Snap(WorldState world) =>
        _builder.Build(world, OverlayType.Biome,
            SimSpeed.Normal, paused: false, ticksPerSecond: 4,
            recentEvents: Array.Empty<SimEvent>());

    private static WorldState BuildWorld(int seed = 99) =>
        WorldTestHelper.CreateSmallWorld(seed);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Artifact MakeArtifact(
        string name,
        ArtifactOwner owner,
        ArtifactCategory category = ArtifactCategory.Weapon,
        float quality = 0.8f,
        string creatorName = "Anon",
        int createdYear = 1) =>
        new Artifact(
            Id:          ArtifactId.New(),
            Name:        name,
            Category:    category,
            CreatedYear: createdYear,
            CreatorId:   0,
            CreatorName: creatorName,
            Origin:      "Forged in Testburg, Year 1",
            Quality:     quality,
            Owner:       owner);

    private static void Register(WorldState world, Artifact a) => world.Artifacts[a.Id] = a;

    // ── Test 1: empty registry → empty list ──────────────────────────────────

    [Fact]
    public void Snapshot_Artifacts_IsEmpty_WhenNoArtifacts()
    {
        var world = BuildWorld();
        var snap  = Snap(world);

        // May be null or empty; either is correct for an empty registry
        var count = snap.Artifacts?.Count ?? 0;
        count.Should().Be(0, "no artifacts registered → snapshot should carry an empty list");
    }

    // ── Test 2: settlement owner → "Held at <name>" ──────────────────────────

    [Fact]
    public void Snapshot_Artifacts_OwnerDesc_Settlement()
    {
        var world = BuildWorld();

        // Find a land tile and plant a settlement manually
        var tile = FindLandTile(world);
        var settlementName = "Ironvale";
        PlantSettlementAt(world, tile, settlementName);

        var artifact = MakeArtifact("Blade of Iron", ArtifactOwner.OfSettlement(tile));
        Register(world, artifact);

        var snap = Snap(world);

        snap.Artifacts.Should().NotBeNullOrEmpty();
        var a = snap.Artifacts!.Single(x => x.Name == "Blade of Iron");
        a.OwnerDesc.Should().Be($"Held at {settlementName}",
            "settlement-owned artifact should format OwnerDesc as 'Held at <name>'");
        a.OwnerSettlementTile.Should().Be(tile,
            "OwnerSettlementTile should match the settlement tile coord");
        a.OwnerCharacterId.Should().Be(0L,
            "no character owner → OwnerCharacterId should be 0");
        a.IsDestroyed.Should().BeFalse();
    }

    // ── Test 3: lost owner → "Lost" ───────────────────────────────────────────

    [Fact]
    public void Snapshot_Artifacts_OwnerDesc_Lost()
    {
        var world    = BuildWorld();
        var artifact = MakeArtifact("Ring of Lost Kings", ArtifactOwner.Lost);
        Register(world, artifact);

        var snap = Snap(world);
        var a    = snap.Artifacts!.Single(x => x.Name == "Ring of Lost Kings");

        a.OwnerDesc.Should().Be("Lost",
            "lost artifact should format OwnerDesc as 'Lost'");
        a.OwnerCharacterId.Should().Be(0L);
        a.OwnerSettlementTile.Should().Be(new TileCoord(-1, -1));
    }

    // ── Test 4: destroyed artifact included with IsDestroyed=true ────────────

    [Fact]
    public void Snapshot_Artifacts_IncludesDestroyed_WithFlag()
    {
        var world    = BuildWorld();
        var artifact = MakeArtifact("Shattered Crown", ArtifactOwner.Lost) with { IsDestroyed = true };
        Register(world, artifact);

        var snap = Snap(world);
        var a    = snap.Artifacts!.Single(x => x.Name == "Shattered Crown");

        a.IsDestroyed.Should().BeTrue(
            "destroyed artifacts are included in the snapshot so the UI can show historical context");
    }

    // ── Test 5: multiple artifacts — all projected ────────────────────────────

    [Fact]
    public void Snapshot_Artifacts_ProjectsAll()
    {
        var world = BuildWorld();
        for (int i = 0; i < 5; i++)
            Register(world, 
                MakeArtifact($"Artifact {i}", ArtifactOwner.Lost, createdYear: 10 + i));

        var snap = Snap(world);

        snap.Artifacts!.Count.Should().Be(5,
            "all registered artifacts should appear in the snapshot");
    }

    // ── Test 6: field mapping correctness ────────────────────────────────────

    [Fact]
    public void Snapshot_Artifacts_FieldsMatchSource()
    {
        var world = BuildWorld();
        var artifact = MakeArtifact(
            name:        "Masterwork Bow",
            owner:       ArtifactOwner.Lost,
            category:    ArtifactCategory.Weapon,
            quality:     0.95f,
            creatorName: "Elara",
            createdYear: 42);
        Register(world, artifact);

        var snap = Snap(world);
        var a    = snap.Artifacts!.Single(x => x.Name == "Masterwork Bow");

        a.Id.Should().Be(artifact.Id.Value);
        a.Category.Should().Be("Weapon");
        a.Quality.Should().BeApproximately(0.95f, 0.001f);
        a.CreatorName.Should().Be("Elara");
        a.CreatedYear.Should().Be(42);
        a.Origin.Should().Be("Forged in Testburg, Year 1");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TileCoord FindLandTile(WorldState world)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;      x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        throw new InvalidOperationException("No land tile found in test world.");
    }

    private static void PlantSettlementAt(WorldState world, TileCoord tile, string name)
    {
        // Directly insert a settlement stub so we don't need CivTracker
        var civId = new CivId(1);
        var stub = new WorldEngine.Sim.Civilizations.SettlementStub(
            FounderId:  new EntityId(1),
            CivId:      civId,
            Tile:       tile,
            FoundedYear: 1,
            Population: 100,
            Health:     80,
            Name:       name);
        world.Settlements[tile] = stub;
        world.TerritoryMap[tile] = tile;
    }
}
