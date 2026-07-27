using FluentAssertions;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// M11 Phase 11.3 — a character mid-SeaVoyage (goal + TargetTile, standing on a water tile)
/// must round-trip through save/load like any other goal, then resume crossing rather than
/// snapping back to land-only behavior.
/// </summary>
public class SeaVoyagePersistenceTests : IDisposable
{
    private readonly string _saveDir = Path.Combine(Path.GetTempPath(), $"seavoyage_save_test_{Guid.NewGuid():N}");

    public void Dispose() => WorldStateSaver.DeleteSave(_saveDir);

    [Fact]
    public void SeaVoyageGoal_RoundTripsThroughSaveLoad()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 5);
        var simCfg = TestSimConfig.Default();

        // Find any ocean tile to stand the character on mid-voyage.
        TileCoord oceanTile = default;
        bool found = false;
        for (int y = 0; y < world.TileGrid.TileHeight && !found; y++)
        for (int x = 0; x < world.TileGrid.TileWidth && !found; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) { oceanTile = c; found = true; }
        }
        found.Should().BeTrue("a small generated world should contain at least one ocean tile");

        var dest = new TileCoord((oceanTile.X + 3) % world.TileGrid.TileWidth, oceanTile.Y);
        var character = CharacterFactory.Spawn(oceanTile, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        character.Goals.Add(new GoalData
        {
            Type       = GoalType.SeaVoyage,
            Priority   = 0.9f,
            TargetTile = dest,
            FormedTick = 0,
            StaleSince = 0,
        });
        world.Entities.Add(character);

        WorldStateSaver.Save(world, _saveDir, simCfg);
        var loaded = WorldStateSaver.Load(_saveDir, simCfg);

        var loadedChar = loaded.Entities.Characters.FirstOrDefault(c => c.Id == character.Id);
        loadedChar.Should().NotBeNull();
        loadedChar!.Location.Should().Be(oceanTile, "mid-voyage location (on water) must survive the round trip");

        var loadedGoal = loadedChar.Goals.FirstOrDefault(g => g.Type == GoalType.SeaVoyage);
        loadedGoal.Should().NotBeNull("the SeaVoyage goal must survive the round trip");
        loadedGoal!.TargetTile.Should().Be(dest);
        loadedGoal.IsComplete.Should().BeFalse();
    }
}
