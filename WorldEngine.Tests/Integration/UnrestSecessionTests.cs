using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// S2 splinter mechanic tests: unrest accrual math, forced secession end-to-end,
/// and reproducibility of the secession path.
/// </summary>
public class UnrestSecessionTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static WorldState BuildWorld(int seed = 42)
        => WorldTestHelper.CreateSmallWorld(seed);

    /// <summary>Finds two land tiles at least <paramref name="minDist"/> tiles apart.</summary>
    private static (TileCoord a, TileCoord b) FindTwoLandTiles(WorldState world, int minDist)
    {
        var land = new List<TileCoord>();
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;  x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) land.Add(c);
        }

        foreach (var a in land)
        foreach (var b in land)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            if (dx * dx + dy * dy >= minDist * minDist) return (a, b);
        }
        throw new InvalidOperationException("No sufficiently distant land tile pair found");
    }

    /// <summary>
    /// Plants a civ with a capital and one distant second settlement, returns the civ + tiles.
    /// </summary>
    private static (WorldState world, Civilization civ, TileCoord capital, TileCoord distant)
        PlantTwoCityCiv(int seed = 42)
    {
        var world = BuildWorld(seed);
        world.SimConfig.Character.GlobalSettlementMinDist = 3; // small world — allow closer founding

        var (capital, distant) = FindTwoLandTiles(world, 5);

        // Founder establishes the capital (creates the civ)
        var biome   = (BiomeType)world.TileGrid.GetTile(capital).BiomeType;
        var founder = CharacterFactory.Spawn(capital, biome, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, capital),
            world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[capital].CivId];

        // Second member founds the distant settlement under the same civ
        var biome2 = (BiomeType)world.TileGrid.GetTile(distant).BiomeType;
        var member = CharacterFactory.Spawn(distant, biome2, world.WorldSeed, 2L, world.SimConfig, world.CurrentYear);
        CivTracker.SetCharacterCiv(member, civ.Id, OrganizationRole.Member, world);
        civ.Members.Add(member.Id);
        world.Entities.Add(member);
        CivTracker.Resolve(new EstablishSettlement(member.Id, distant),
            world, pending, world.SimConfig.SettlementNames);

        world.Settlements[distant].CivId.Should().Be(civ.Id, "second settlement must belong to same civ");
        return (world, civ, capital, distant);
    }

    // ─── Unrest accrual math ──────────────────────────────────────────────────

    [Fact]
    public void DistantSettlement_AccruesDistanceUnrest()
    {
        var (world, civ, capital, distant) = PlantTwoCityCiv();
        var cfg = world.SimConfig.Unrest;
        cfg.UnrestComfortRadius   = 2;      // small world — everything past 2 tiles is "distant"
        cfg.UnrestDistancePerTile = 0.01f;
        cfg.UnrestSecessionThreshold = 2f;  // never trigger secession in this test

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);

        int dx = distant.X - capital.X, dy = distant.Y - capital.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float expected = (dist - cfg.UnrestComfortRadius) * cfg.UnrestDistancePerTile;

        world.Settlements[distant].Unrest.Should().BeApproximately(expected, 1e-4f,
            "distance driver accrues (dist - comfortRadius) × perTile per year");
        world.Settlements[capital].Unrest.Should().Be(0f, "capital is within its own comfort radius");
    }

    [Fact]
    public void SizeDriver_AddsUnrestAboveSoftCityThreshold()
    {
        var (world, civ, capital, distant) = PlantTwoCityCiv();
        var cfg = world.SimConfig.Unrest;
        cfg.UnrestComfortRadius      = 99;   // disable distance driver
        cfg.UnrestSoftCityThreshold  = 1;    // 2 cities → 1 excess city
        cfg.UnrestPerExcessCity      = 0.05f;
        cfg.UnrestSecessionThreshold = 2f;

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);

        world.Settlements[distant].Unrest.Should().BeApproximately(0.05f, 1e-4f,
            "1 excess city × 0.05 unrest per excess city");
        world.Settlements[capital].Unrest.Should().Be(0f, "size driver never applies to the capital");
    }

    [Fact]
    public void Unrest_DecaysWhenNoDriversApply()
    {
        var (world, civ, capital, distant) = PlantTwoCityCiv();
        var cfg = world.SimConfig.Unrest;
        cfg.UnrestComfortRadius      = 99;   // no distance driver
        cfg.UnrestSoftCityThreshold  = 99;   // no size driver
        cfg.UnrestDecayRate          = 0.10f;
        cfg.UnrestSecessionThreshold = 2f;

        world.Settlements[distant] = world.Settlements[distant] with { Unrest = 0.50f };

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);

        world.Settlements[distant].Unrest.Should().BeApproximately(0.45f, 1e-4f,
            "unrest decays by 10% per calm year");
    }

    // ─── Forced splinter (integration) ────────────────────────────────────────

    private static (WorldState world, List<PendingEvent> pending, Civilization parent,
                    TileCoord capital, TileCoord distant)
        ForceSplinter(int seed = 42)
    {
        var (world, civ, capital, distant) = PlantTwoCityCiv(seed);
        var cfg = world.SimConfig.Unrest;
        cfg.UnrestSecessionThreshold = 0.5f;
        cfg.UnrestSecessionChance    = 1.0f;  // deterministic trigger
        cfg.UnrestComfortRadius      = 99;    // drivers irrelevant — unrest set manually
        cfg.UnrestSoftCityThreshold  = 99;

        world.Settlements[distant] = world.Settlements[distant] with { Unrest = 0.9f };

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);
        return (world, pending, civ, capital, distant);
    }

    [Fact]
    public void ForcedSplinter_CreatesNewCivWithTransferredSettlement()
    {
        var (world, pending, parent, capital, distant) = ForceSplinter();

        world.Civilizations.Count.Should().Be(2, "secession creates exactly one new civ");
        var newCiv = world.Civilizations.Values.Single(c => c.Id != parent.Id);

        world.Settlements[distant].CivId.Should().Be(newCiv.Id, "seceded settlement transfers");
        world.Settlements[distant].Unrest.Should().Be(0f, "unrest resets after secession");
        world.Settlements[capital].CivId.Should().Be(parent.Id, "capital stays with parent");

        newCiv.CapitalTile.Should().Be(distant);
        newCiv.SettlementCount.Should().Be(1);
        parent.SettlementCount.Should().Be(1);

        // Ruler exists, is alive, and belongs to the new civ
        var ruler = world.GetEntity(newCiv.RulerId) as Tier1Character;
        ruler.Should().NotBeNull();
        ruler!.CivId.Should().Be(newCiv.Id);
        newCiv.Members.Should().Contain(newCiv.RulerId);

        // Mutual diplomatic tension seeded
        newCiv.BorderTension.Should().ContainKey(parent.Id);
        parent.BorderTension.Should().ContainKey(newCiv.Id);
    }

    [Fact]
    public void ForcedSplinter_FiresCivSplinteredEvent()
    {
        var (world, pending, parent, _, distant) = ForceSplinter();

        var ev = pending.SingleOrDefault(p => p.Type == EventType.CivSplintered);
        ev.Should().NotBeNull("secession must record a CivSplintered event");
        ev!.Location.Should().Be(distant);
        ev.PayloadJson.Should().Contain("\"ParentCivId\":" + parent.Id.Value);
        ev.PayloadJson.Should().Contain("\"SettlementsSeceded\":1");

        pending.Should().Contain(p => p.Type == EventType.CivilizationFounded,
            "the new civ also records a founding event");
    }

    [Fact]
    public void Capital_NeverSecedes()
    {
        var (world, civ, capital, distant) = PlantTwoCityCiv();
        var cfg = world.SimConfig.Unrest;
        cfg.UnrestSecessionThreshold = 0.5f;
        cfg.UnrestSecessionChance    = 1.0f;
        cfg.UnrestComfortRadius      = 99;
        cfg.UnrestSoftCityThreshold  = 99;

        world.Settlements[capital] = world.Settlements[capital] with { Unrest = 0.9f };

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);

        world.Civilizations.Count.Should().Be(1, "the capital itself never secedes");
        world.Settlements[capital].CivId.Should().Be(civ.Id);
    }

    // ─── Reproducibility ──────────────────────────────────────────────────────

    [Fact]
    public void Secession_IsReproducible()
    {
        var (w1, p1, parent1, _, distant1) = ForceSplinter(seed: 1234);
        var (w2, p2, parent2, _, distant2) = ForceSplinter(seed: 1234);

        distant1.Should().Be(distant2);
        w1.Civilizations.Count.Should().Be(w2.Civilizations.Count);

        var new1 = w1.Civilizations.Values.Single(c => c.Id != parent1.Id);
        var new2 = w2.Civilizations.Values.Single(c => c.Id != parent2.Id);
        new1.Name.Should().Be(new2.Name, "same seed produces identical secession outcome");
        new1.CapitalTile.Should().Be(new2.CapitalTile);

        var ev1 = p1.Single(p => p.Type == EventType.CivSplintered);
        var ev2 = p2.Single(p => p.Type == EventType.CivSplintered);
        ev1.PayloadJson.Should().Be(ev2.PayloadJson);
    }
}
