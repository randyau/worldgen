namespace WorldEngine.Sim.Config;

public sealed class TerritoryConfig
{
    /// <summary>1 tile per N people; city of 800 → 100 tiles (~radius 5).</summary>
    public int ClaimTilesPerPerson    { get; set; } = 8;

    /// <summary>Radius-1 circle, always retained.</summary>
    public int MinCityTiles           { get; set; } = 7;

    /// <summary>~radius-6; absolute upper bound on tile count regardless of radius.</summary>
    public int MaxCityTiles           { get; set; } = 120;

    /// <summary>
    /// Hard radius cap (Euclidean) from the city tile. Territory can never expand beyond this
    /// distance, regardless of population. Civs must found a new city to access land beyond it.
    /// </summary>
    public int MaxTerritoryRadius     { get; set; } = 10;

    /// <summary>Max tiles claimed per city per year (prevents instant snowball).</summary>
    public int TerritoryGrowthPerYear { get; set; } = 2;

    /// <summary>Tiles claimed at founding (~13 tiles at radius 2).</summary>
    public int InitialCityClaimRadius { get; set; } = 2;
}
