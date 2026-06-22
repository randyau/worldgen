# Test Templates
**Load this file when writing tests for a new story.**
All test classes go in `WorldEngine.Tests/`. See the phase doc for story-specific test names.

---

## Reproducibility Test (THE most important test)

Add once in the test suite. Every change that breaks this is a regression. Place in `WorldEngine.Tests/Reproducibility/`.

```csharp
public class ReproducibilityTests
{
    [Fact]
    public async Task SameSeedProducesSameWorld()
    {
        var config = new WorldConfig { Seed = 12345, WidthKm = 500, HeightKm = 400 };
        var simConfig = SimConfigLoader.LoadOrCreateDefault();
        var pipeline = new WorldGenPipeline();

        var world1 = await pipeline.RunFullAsync(config, simConfig);
        var world2 = await pipeline.RunFullAsync(config, simConfig);

        // Assert every tile is byte-identical
        for (int y = 0; y < world1.TileGrid.TileHeight; y++)
        for (int x = 0; x < world1.TileGrid.TileWidth; x++)
        {
            var coord = new TileCoord(x, y);
            world1.TileGrid.GetTile(coord).Should().BeEquivalentTo(
                world2.TileGrid.GetTile(coord),
                $"tile ({x},{y}) differed between runs");
        }

        // Also assert SeasonalProfiles match
        for (int i = 0; i < world1.SeasonalProfiles.Length; i++)
            world1.SeasonalProfiles[i].Should().BeEquivalentTo(world2.SeasonalProfiles[i],
                $"SeasonalProfile[{i}] differed");
    }
}
```

---

## Unit Test Class Template

```csharp
// File: WorldEngine.Tests/Unit/[ClassName]Tests.cs
using FluentAssertions;
using WorldEngine.Sim;
using Xunit;

namespace WorldEngine.Tests.Unit;

public class [ClassName]Tests
{
    // Arrange shared state in constructor or private helpers — not [SetUp]

    [Fact]
    public void [DescribeCondition]_[ExpectedResult]()
    {
        // Arrange
        var sut = new [ClassName](...);

        // Act
        var result = sut.SomeMethod(...);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 255, BiomeType.Tundra)]
    [InlineData(200, 200, BiomeType.Desert)]
    public void Classify_ReturnsCorrectBiome(byte temperature, byte moisture, BiomeType expected)
    {
        var config = TestSimConfig.Default();
        BiomeClassifier.Classify(temperature, moisture, 100, TileStaticFlags.None, config)
            .Should().Be(expected);
    }
}
```

---

## Integration Test Template

```csharp
// File: WorldEngine.Tests/Integration/[EpicName]IntegrationTests.cs
// Integration tests use real objects (no mocks). Allowed to hit real SQLite.

public class [EpicName]IntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public [EpicName]IntegrationTests()
    {
        _dbPath = Path.GetTempFileName();
    }

    [Fact]
    public async Task [PrimaryWorkflow]_CompletesSuccessfully()
    {
        // Arrange — real objects, real database
        var config = WorldConfig.Default();
        var simConfig = SimConfigLoader.LoadOrCreateDefault();

        // Act
        var world = await new WorldGenPipeline().RunFullAsync(config, simConfig);

        // Assert
        world.Should().NotBeNull();
        world.TileGrid.TileWidth.Should().BeGreaterThan(0);
    }

    public void Dispose() => File.Delete(_dbPath);
}
```

---

## Thread-Safety Test Template

```csharp
// Used for StateCache and CommandQueue tests
[Fact]
public async Task [Class]_ThreadSafetyUnderConcurrentAccess()
{
    var sut = new StateCache();
    var snapshot = CreateTestSnapshot();

    // Writer and reader running concurrently for 1 second
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

    var writer = Task.Run(() =>
    {
        while (!cts.Token.IsCancellationRequested)
            sut.Commit(snapshot);
    }, cts.Token);

    var reader = Task.Run(() =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try { _ = sut.Read(); }
            catch (Exception ex) { exceptions.Add(ex); }
        }
    }, cts.Token);

    await Task.WhenAll(writer, reader);
    exceptions.Should().BeEmpty("concurrent access should not throw");
}
```

---

## Struct Size Assertion

```csharp
[Fact]
public void TileData_SizeIsExactly14Bytes()
{
    Marshal.SizeOf<TileData>().Should().Be(14,
        "TileData is a hot struct written to state.bin; size changes break serialization");
}
```

---

## Layer Determinism Test Template (for World Gen Layers)

```csharp
// Every world gen layer must have a determinism test
[Fact]
public void [Layer]_SameSeedSameResult()
{
    var config = new WorldConfig { Seed = 99999, WidthKm = 200, HeightKm = 200 };
    var simConfig = SimConfigLoader.LoadOrCreateDefault();

    var ctx1 = WorldGenContext.CreateForTest(config, simConfig);
    var ctx2 = WorldGenContext.CreateForTest(config, simConfig);

    var result1 = new [Layer]().Generate(ctx1);
    var result2 = new [Layer]().Generate(ctx2);

    result1.Should().BeEquivalentTo(result2,
        "[Layer] must be deterministic for the same seed");
}
```

---

## SimConfig Test Helper

```csharp
// In WorldEngine.Tests/Helpers/TestSimConfig.cs
public static class TestSimConfig
{
    public static SimConfig Default() => SimConfigLoader.LoadOrCreateDefault();

    public static SimConfig With(Action<SimConfig> configure)
    {
        var cfg = Default();
        configure(cfg);
        return cfg;
    }
}

// Usage:
var config = TestSimConfig.With(c => c.Disasters.WildfireIgnitionProbabilityPerTick = 1.0f);
// Now wildfires ignite on every roll — useful for testing disaster mechanics
```

---

## Common FluentAssertions Patterns

```csharp
// Record/struct equality (value-based):
result.Should().BeEquivalentTo(expected);

// Collection contains:
results.Should().Contain(x => x.Type == DisasterType.Wildfire);
results.Should().HaveCountGreaterThan(0);
results.Should().AllSatisfy(r => r.Intensity.Should().BeInRange(0.0f, 1.0f));

// Exception:
act.Should().Throw<InvalidOperationException>().WithMessage("*seed*");

// Null:
result.Should().NotBeNull();
result.Should().BeNull();

// Flag enum:
tile.StaticFlags.Should().HaveFlag(TileStaticFlags.IsVolcanic);
tile.StaticFlags.Should().NotHaveFlag(TileStaticFlags.HasRiver);

// Numeric range:
value.Should().BeInRange(0, 255);
value.Should().BeGreaterThan(0);
value.Should().BeCloseTo(expected, delta: 0.01f);
```
