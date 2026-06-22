using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.World;
using Xunit;

namespace WorldEngine.Tests.Integration;

public class EventStoreTests
{
    private static SimEvent MakeEvent(
        EventType type = EventType.WildfireOccurred,
        int year = 1,
        EventTier tier = EventTier.Regional) =>
        new()
        {
            Id = new EventId(0), // assigned by DB
            Type = type,
            Year = year,
            Season = Season.Spring,
            Tick = 0,
            TierInvolvement = tier,
            VerbClass = VerbClass.Destruction,
            PopulationImpact = PopulationImpact.Minor,
            IsFirstOfKind = false,
            IsGodMode = false,
            PayloadJson = "{}"
        };

    [Fact]
    public void EventStore_SchemaCreatedWithAllTables()
    {
        using var store = new EventStore(":memory:");
        // Round-trip works only if the Events + CausalEdges tables exist.
        var inserted = store.BatchInsert(new[] { MakeEvent() });
        store.InsertCausalEdges(new[] { (inserted[0].Id.Value, inserted[0].Id.Value) });
        inserted.Should().HaveCount(1);
    }

    [Fact]
    public void EventStore_SchemaCreatedWithAllIndexes()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        conn.Execute("PRAGMA foreign_keys=ON;");
        conn.Execute(DatabaseSchema.CreateEvents);
        conn.Execute(DatabaseSchema.CreateIndexYear);
        conn.Execute(DatabaseSchema.CreateIndexType);
        conn.Execute(DatabaseSchema.CreateIndexTier);
        conn.Execute(DatabaseSchema.CreateIndexLocation);

        var indexes = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_events_%';").ToList();
        indexes.Should().Contain(new[]
        {
            "idx_events_year", "idx_events_type", "idx_events_tier", "idx_events_location"
        });
    }

    [Fact]
    public void EventStore_RoundTripSingleEvent()
    {
        using var store = new EventStore(":memory:");
        var inserted = store.BatchInsert(new[]
        {
            MakeEvent(EventType.VolcanicEruption, year: 5) with { Location = new TileCoord(3, 7) }
        });

        var fetched = store.GetEvent(inserted[0].Id);
        fetched.Should().NotBeNull();
        fetched!.Type.Should().Be(EventType.VolcanicEruption);
        fetched.Year.Should().Be(5);
        fetched.Location.Should().Be(new TileCoord(3, 7));
    }

    [Fact]
    public void EventStore_BatchInsert1000Events()
    {
        using var store = new EventStore(":memory:");
        var events = Enumerable.Range(1, 1000).Select(i => MakeEvent(year: i)).ToList();
        var inserted = store.BatchInsert(events);

        inserted.Should().HaveCount(1000);
        inserted.Select(e => e.Id.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EventStore_AssignedIdIsPositive()
    {
        using var store = new EventStore(":memory:");
        var inserted = store.BatchInsert(new[] { MakeEvent() });
        inserted[0].Id.Value.Should().BePositive();
    }

    [Fact]
    public void EventStore_CausalEdgeInserted()
    {
        using var store = new EventStore(":memory:");
        var inserted = store.BatchInsert(new[] { MakeEvent(year: 1), MakeEvent(year: 2) });
        store.InsertCausalEdges(new[] { (inserted[0].Id.Value, inserted[1].Id.Value) });

        var successors = store.GetCausalSuccessors(inserted[0].Id).ToList();
        successors.Should().ContainSingle().Which.Id.Should().Be(inserted[1].Id);

        var predecessors = store.GetCausalPredecessors(inserted[1].Id).ToList();
        predecessors.Should().ContainSingle().Which.Id.Should().Be(inserted[0].Id);
    }

    [Fact]
    public void EventStore_QueryByYearReturnsCorrect()
    {
        using var store = new EventStore(":memory:");
        store.BatchInsert(new[] { MakeEvent(year: 10), MakeEvent(year: 10), MakeEvent(year: 20) });

        store.GetEventsByYear(10).Should().HaveCount(2);
        store.GetEventsByYear(20).Should().HaveCount(1);
        store.GetEventsByYear(99).Should().BeEmpty();
    }

    [Fact]
    public void EventStore_QueryByTypeReturnsCorrect()
    {
        using var store = new EventStore(":memory:");
        store.BatchInsert(new[]
        {
            MakeEvent(EventType.WildfireOccurred),
            MakeEvent(EventType.VolcanicEruption),
            MakeEvent(EventType.WildfireOccurred)
        });

        store.GetEventsByType(EventType.WildfireOccurred).Should().HaveCount(2);
        store.GetEventsByType(EventType.VolcanicEruption).Should().HaveCount(1);
    }

    [Fact]
    public void EventStore_QueryByTierReturnsCorrect()
    {
        using var store = new EventStore(":memory:");
        store.BatchInsert(new[]
        {
            MakeEvent(tier: EventTier.Headline),
            MakeEvent(tier: EventTier.Regional),
            MakeEvent(tier: EventTier.Headline)
        });

        store.GetEventsByTier(EventTier.Headline).Should().HaveCount(2);
        store.GetEventsByTier(EventTier.Regional).Should().HaveCount(1);
    }
}
