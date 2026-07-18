using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Optional audit sink for per-tile food factor breakdowns.
/// Passed to ResourcePressurePhase when <c>--audit-food</c> is requested.
/// Null on the normal (hot) path — zero overhead when not auditing.
/// </summary>
public sealed class FoodAuditSink
{
    /// <summary>One row per territory tile per settlement.</summary>
    public sealed record TileFactors(
        TileCoord       Coord,
        BiomeType       Biome,
        float           FertilityRaw,      // tile.Fertility / 255
        float           MoistureRaw,       // tile.CurrentMoisture / 255
        float           MoistureEffective, // after proportional + absolute floor
        float           GrowingSeasonFactor,
        float           BiomeFoodMultiplier,
        float           ImprovementMultiplier,
        float           TileCapacity,      // PeoplePerTilePeak × all factors × improvement
        float           FoodContribution); // = TileCapacity × improvementMultiplier (== TileCapacity here)

    /// <summary>Rollup per settlement.</summary>
    public sealed record SettlementRollup(
        TileCoord       Coord,
        string          Name,
        int             Population,
        float           RawFoodSupply,     // sum of TileCapacity across territory
        float           SmoothedCapacity,
        float           FoodRatio,
        float           StoreDepth,
        int             TileCount);

    private readonly Dictionary<TileCoord, List<TileFactors>>   _tiles   = new();
    private readonly Dictionary<TileCoord, SettlementRollup>    _rollups = new();

    /// <summary>Request audit for ALL settlements (--audit-food all).</summary>
    public bool AuditAll { get; init; }

    /// <summary>
    /// Request audit for specific settlement tile coordinate
    /// (--audit-food x,y). Empty = all (when AuditAll=true).
    /// </summary>
    public HashSet<TileCoord> TargetCoords { get; } = new();

    /// <summary>Returns true if this settlement should be captured.</summary>
    public bool Captures(TileCoord coord) =>
        AuditAll || TargetCoords.Contains(coord);

    internal void AddTile(TileCoord settlement, TileFactors factors)
    {
        if (!_tiles.TryGetValue(settlement, out var list))
            _tiles[settlement] = list = new List<TileFactors>();
        list.Add(factors);
    }

    internal void AddRollup(TileCoord settlement, SettlementRollup rollup)
        => _rollups[settlement] = rollup;

    /// <summary>
    /// Print the collected audit data to stdout.
    /// Called by Program.cs after the simulation completes (or at final tick).
    /// </summary>
    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.WriteLine("  FOOD AUDIT  (--audit-food)");
        Console.WriteLine("══════════════════════════════════════════════════════════");

        foreach (var (coord, rollup) in _rollups.OrderBy(kv => kv.Value.Name))
        {
            Console.WriteLine();
            Console.WriteLine($"  Settlement: {rollup.Name}  @({coord.X},{coord.Y})");
            Console.WriteLine($"  Population: {rollup.Population:N0}");
            Console.WriteLine($"  Tiles in territory: {rollup.TileCount}");
            Console.WriteLine($"  Raw food supply (people supported): {rollup.RawFoodSupply:F1}");
            Console.WriteLine($"  Smoothed capacity (EMA): {rollup.SmoothedCapacity:F1}");
            Console.WriteLine($"  Food ratio (supply/demand): {rollup.FoodRatio:F3}");
            Console.WriteLine($"  Store depth (seasons of food): {rollup.StoreDepth:F2}");
            Console.WriteLine();
            Console.WriteLine("  Per-tile factor breakdown:");
            Console.WriteLine($"  {"Coord",-12} {"Biome",-20} {"Fert",-6} {"Moist",-6} {"EffMoist",-9} {"GrowSn",-7} {"BiomeMult",-10} {"ImpMult",-8} {"Contribution",-13}");
            Console.WriteLine($"  {new string('-', 95)}");

            var tiles = _tiles.TryGetValue(coord, out var t) ? t : new List<TileFactors>();
            foreach (var tf in tiles.OrderByDescending(x => x.FoodContribution))
            {
                Console.WriteLine(
                    $"  ({tf.Coord.X,3},{tf.Coord.Y,3})     " +
                    $"{tf.Biome,-20} " +
                    $"{tf.FertilityRaw,5:F3} " +
                    $"{tf.MoistureRaw,5:F3} " +
                    $"{tf.MoistureEffective,8:F3} " +
                    $"{tf.GrowingSeasonFactor,6:F3} " +
                    $"{tf.BiomeFoodMultiplier,9:F3} " +
                    $"{tf.ImprovementMultiplier,7:F3} " +
                    $"{tf.FoodContribution,12:F2}");
            }

            // Verify: product check — sum of contributions ≈ RawFoodSupply
            float checkSum = tiles.Sum(tf => tf.FoodContribution);
            Console.WriteLine($"  {new string('-', 95)}");
            Console.WriteLine($"  {"TOTAL",-12} {"",-20} {"",-6} {"",-6} {"",-9} {"",-7} {"",-10} {"",-8} {checkSum,12:F2}");
            if (MathF.Abs(checkSum - rollup.RawFoodSupply) > 0.5f)
                Console.WriteLine($"  ⚠  Tile sum {checkSum:F1} differs from recorded supply {rollup.RawFoodSupply:F1} (tiles captured mid-run vs. final state)");
        }

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════");
    }
}
