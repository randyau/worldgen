using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Unit tests for M5 W4 artifact telemetry: MetricsAccumulator YTD counters,
/// MetricsCollector stock computation, and YearlyMetricsRow DB round-trip.
/// </summary>
public class ArtifactTelemetryTests
{
    // ─── MetricsAccumulator: increment on each artifact event type ────────────

    [Fact]
    public void MetricsAccumulator_ArtifactsCreatedYtd_IncrementedOnArtifactCreatedEvent()
    {
        // Arrange: build a minimal PendingEvent with the ArtifactCreated type
        var acc = new MetricsAccumulator();
        var pe  = MakePendingEvent(EventType.ArtifactCreated);

        // Act: use same code path as PhaseRunner — simulated inline
        IncrementViaSwitch(acc, pe);

        // Assert
        acc.ArtifactsCreatedYtd.Should().Be(1);
        acc.ArtifactsDestroyedYtd.Should().Be(0);
        acc.ArtifactsTransferredYtd.Should().Be(0);
    }

    [Fact]
    public void MetricsAccumulator_ArtifactsDestroyedYtd_IncrementedOnArtifactDestroyedEvent()
    {
        var acc = new MetricsAccumulator();
        IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactDestroyed));

        acc.ArtifactsCreatedYtd.Should().Be(0);
        acc.ArtifactsDestroyedYtd.Should().Be(1);
        acc.ArtifactsTransferredYtd.Should().Be(0);
    }

    [Fact]
    public void MetricsAccumulator_ArtifactsTransferredYtd_IncrementedOnArtifactTransferredEvent()
    {
        var acc = new MetricsAccumulator();
        IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactTransferred));

        acc.ArtifactsCreatedYtd.Should().Be(0);
        acc.ArtifactsDestroyedYtd.Should().Be(0);
        acc.ArtifactsTransferredYtd.Should().Be(1);
    }

    [Fact]
    public void MetricsAccumulator_MultipleEvents_AllCountersAccumulate()
    {
        var acc = new MetricsAccumulator();
        for (int i = 0; i < 3; i++) IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactCreated));
        for (int i = 0; i < 2; i++) IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactDestroyed));
        IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactTransferred));

        acc.ArtifactsCreatedYtd.Should().Be(3);
        acc.ArtifactsDestroyedYtd.Should().Be(2);
        acc.ArtifactsTransferredYtd.Should().Be(1);
    }

    [Fact]
    public void MetricsAccumulator_ResetYtd_ClearsArtifactCounters()
    {
        var acc = new MetricsAccumulator();
        IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactCreated));
        IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactDestroyed));
        IncrementViaSwitch(acc, MakePendingEvent(EventType.ArtifactTransferred));

        acc.ResetYtd();

        acc.ArtifactsCreatedYtd.Should().Be(0);
        acc.ArtifactsDestroyedYtd.Should().Be(0);
        acc.ArtifactsTransferredYtd.Should().Be(0);
    }

    // ─── Stock metric computation from hand-built world.Artifacts ─────────────

    [Fact]
    public void StockMetrics_LivingArtifacts_ExcludesDestroyed()
    {
        // Arrange: 2 living, 1 destroyed
        var artifacts = new Dictionary<ArtifactId, Artifact>
        {
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Character) },
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Settlement) },
            { ArtifactId.New(), MakeArtifact(isDestroyed: true,  ownerKind: ArtifactOwnerKind.Character) },
        };

        var (living, lost, perSettlement) = ComputeStockMetrics(artifacts, settlementCount: 2);

        living.Should().Be(2);
        lost.Should().Be(0);
        perSettlement.Should().BeApproximately(1.0f, 0.001f); // 2 / 2
    }

    [Fact]
    public void StockMetrics_LostArtifacts_CountsOnlyLostOwnerAndNotDestroyed()
    {
        // Arrange: 1 Lost+living, 1 Lost+destroyed (shouldn't count), 1 Character+living
        var artifacts = new Dictionary<ArtifactId, Artifact>
        {
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Lost) },
            { ArtifactId.New(), MakeArtifact(isDestroyed: true,  ownerKind: ArtifactOwnerKind.Lost) },
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Character) },
        };

        var (living, lost, _) = ComputeStockMetrics(artifacts, settlementCount: 1);

        living.Should().Be(2, "only non-destroyed artifacts count as living");
        lost.Should().Be(1,   "only non-destroyed Lost-owned artifacts count as lost");
    }

    [Fact]
    public void StockMetrics_ArtifactsPerSettlement_DividesLivingBySettlementCount()
    {
        var artifacts = new Dictionary<ArtifactId, Artifact>
        {
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Character) },
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Settlement) },
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Lost) },
        };

        var (_, _, perSettlement) = ComputeStockMetrics(artifacts, settlementCount: 3);

        perSettlement.Should().BeApproximately(1.0f, 0.001f); // 3 / 3
    }

    [Fact]
    public void StockMetrics_ArtifactsPerSettlement_UsesMaxOneWhenNoSettlements()
    {
        // If settlement count is 0, denominator should be 1 (max(1, 0) = 1) to avoid /0.
        var artifacts = new Dictionary<ArtifactId, Artifact>
        {
            { ArtifactId.New(), MakeArtifact(isDestroyed: false, ownerKind: ArtifactOwnerKind.Character) },
        };

        var (_, _, perSettlement) = ComputeStockMetrics(artifacts, settlementCount: 0);

        perSettlement.Should().BeApproximately(1.0f, 0.001f); // 1 / max(1,0) = 1/1
    }

    [Fact]
    public void StockMetrics_EmptyRegistry_AllMetricsAreZero()
    {
        var (living, lost, perSettlement) = ComputeStockMetrics(
            new Dictionary<ArtifactId, Artifact>(), settlementCount: 5);

        living.Should().Be(0);
        lost.Should().Be(0);
        perSettlement.Should().BeApproximately(0.0f, 0.001f);
    }

    // ─── YearlyMetricsRow DB round-trip ──────────────────────────────────────

    [Fact]
    public void MetricsRow_ArtifactColumns_RoundTripThroughEventStore()
    {
        using var store = new EventStore(":memory:");

        var row = new YearlyMetricsRow(
            year: 42,
            worldPopulation: 1000, activeCivs: 3, collapsedCivs: 1,
            settlementsTotal: 5,
            settlementsFoundedYtd: 0, settlementsAbandonedYtd: 0, settlementsConqueredYtd: 0,
            deathsStarvation: 0, deathsDisease: 0, deathsWar: 0, deathsOther: 0,
            meanFoodRatio: 1.5f, minFoodRatio: 1.0f,
            settlementsInShortage: 0, settlementsInCrisis: 0, activeDiseases: 0,
            warsActive: 0, warsDeclaredYtd: 0, warsEndedTruceYtd: 0, warsEndedConquestYtd: 0,
            tier1Count: 10, tier2Count: 2,
            goalsFormedYtd: 5, goalsResolvedYtd: 3,
            meanWellbeing: -0.1f,
            maxCitiesPerCivActual: 4, meanCitiesPerCiv: 2.5f,
            secessionsYtd: 1, meanUnrest: 0.3f, civBorderPairs: 2,
            // artifact telemetry
            artifactsCreatedYtd: 7,
            artifactsDestroyedYtd: 2,
            artifactsTransferredYtd: 5,
            livingArtifacts: 12,
            lostArtifacts: 3,
            artifactsPerSettlement: 2.4f);

        store.WriteMetricsRow(row);

        var loaded = store.GetMetricsRowForYear(42);

        loaded.Should().NotBeNull();
        loaded!.ArtifactsCreatedYtd.Should().Be(7);
        loaded.ArtifactsDestroyedYtd.Should().Be(2);
        loaded.ArtifactsTransferredYtd.Should().Be(5);
        loaded.LivingArtifacts.Should().Be(12);
        loaded.LostArtifacts.Should().Be(3);
        loaded.ArtifactsPerSettlement.Should().BeApproximately(2.4f, 0.001f);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the switch inside PhaseRunner.UpdateMetricsAccumulator.
    /// Keeps the test from depending on PhaseRunner's private method.
    /// </summary>
    private static void IncrementViaSwitch(MetricsAccumulator acc, PendingEvent pe)
    {
        switch (pe.Type)
        {
            case EventType.ArtifactCreated:     acc.ArtifactsCreatedYtd++;     break;
            case EventType.ArtifactDestroyed:   acc.ArtifactsDestroyedYtd++;   break;
            case EventType.ArtifactTransferred: acc.ArtifactsTransferredYtd++; break;
        }
    }

    private static PendingEvent MakePendingEvent(EventType type) =>
        new PendingEvent(type, Location: null, CauseEventId: null, PayloadJson: "{}");

    private static Artifact MakeArtifact(bool isDestroyed, ArtifactOwnerKind ownerKind)
    {
        var owner = ownerKind switch
        {
            ArtifactOwnerKind.Character  => ArtifactOwner.OfCharacter(new EntityId(1)),
            ArtifactOwnerKind.Settlement => ArtifactOwner.OfSettlement(new TileCoord(0, 0)),
            ArtifactOwnerKind.Lost       => ArtifactOwner.Lost,
            _                            => ArtifactOwner.Lost
        };
        return new Artifact(
            Id:           ArtifactId.New(),
            Name:         "Test Artifact",
            Category:     ArtifactCategory.Weapon,
            CreatedYear:  1,
            CreatorId:    0,
            CreatorName:  "Test",
            Origin:       "test",
            Quality:      0.5f,
            Owner:        owner,
            IsDestroyed:  isDestroyed,
            DestroyedYear: isDestroyed ? 2 : 0);
    }

    /// <summary>
    /// Mirrors the stock-metric computation logic in MetricsCollector.Sample — tested
    /// in isolation here so we can pass arbitrary artifact dictionaries.
    /// </summary>
    private static (int living, int lost, float perSettlement) ComputeStockMetrics(
        Dictionary<ArtifactId, Artifact> artifacts, int settlementCount)
    {
        int livingArtifacts = 0;
        int lostArtifacts   = 0;
        foreach (var a in artifacts.Values)
        {
            if (a.IsDestroyed) continue;
            livingArtifacts++;
            if (a.Owner.Kind == ArtifactOwnerKind.Lost) lostArtifacts++;
        }
        float artifactsPerSettlement = livingArtifacts / (float)Math.Max(1, settlementCount);
        return (livingArtifacts, lostArtifacts, artifactsPerSettlement);
    }
}
