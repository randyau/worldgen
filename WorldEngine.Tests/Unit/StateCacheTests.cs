using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.Tests.Unit;

public class StateCacheTests
{
    private static WorldSnapshot MakeSnap(int year) => new WorldSnapshot(
        CurrentYear: year, CurrentSeason: Season.Spring, CurrentSpeed: SimSpeed.Normal,
        IsPaused: false, TicksPerSecond: 4,
        AllTiles: Array.Empty<TileDisplayData>(),
        ActiveOverlay: OverlayType.Biome,
        WorldTileWidth: 50, WorldTileHeight: 40,
        RecentEvents: Array.Empty<SimEvent>(),
        InspectedTile: null,
        EntitySnapshots: new Dictionary<EntityId, EntitySnapshot>(),
        Settlements: new Dictionary<TileCoord, SettlementSnapshot>(),
        GlobalTemperatureAnomaly: 0f,
        GlobalPrecipitationMultiplier: 1f,
        StormCorridorNormalizedLat: 0.35f
    );

    [Fact]
    public void StateCache_ReadBeforeFirstCommitReturnsNull()
    {
        var cache = new StateCache();
        cache.Read().Should().BeNull("no snapshot committed yet");
    }

    [Fact]
    public void StateCache_ReadReturnsLastCommittedSnapshot()
    {
        var cache = new StateCache();
        var snap1 = MakeSnap(1);
        var snap2 = MakeSnap(2);

        cache.Commit(snap1);
        cache.Commit(snap2);

        cache.Read().Should().BeSameAs(snap2, "Read() must return the most recently committed snapshot");
    }

    [Fact]
    public void StateCache_ThreadSafetyUnderConcurrentAccess()
    {
        var cache = new StateCache();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        const int writerCount = 4;
        const int readerCount = 8;
        const int iterations  = 5000;

        var writers = Enumerable.Range(0, writerCount).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                try { cache.Commit(MakeSnap(i)); }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();

        var readers = Enumerable.Range(0, readerCount).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                try { var _ = cache.Read(); }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(writers.Concat(readers).ToArray());

        errors.Should().BeEmpty("concurrent reads and writes must not throw or corrupt state");
    }
}
