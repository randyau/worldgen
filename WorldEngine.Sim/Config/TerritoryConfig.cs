namespace WorldEngine.Sim.Config;

public sealed class TerritoryConfig
{
    /// <summary>1 tile per N people; city of 800 → 100 tiles (~radius 5).</summary>
    public int ClaimTilesPerPerson    { get; set; } = 8;

    /// <summary>Radius-1 circle, always retained.</summary>
    public int MinCityTiles           { get; set; } = 7;

    /// <summary>~radius-6; absolute upper bound on tile count (~12,000 km²).</summary>
    public int MaxCityTiles           { get; set; } = 120;

    /// <summary>
    /// Hard radius cap (Euclidean). Territory expands up to this distance but
    /// effective radius is also capped by <see cref="MinTerritoryRadius"/> + population / <see cref="PopPerTerritoryRadiusTile"/>.
    /// Civs must found new cities to claim land beyond this.
    /// </summary>
    public int MaxTerritoryRadius     { get; set; } = 7;

    /// <summary>Guaranteed minimum radius regardless of population (~12 tiles).</summary>
    public int MinTerritoryRadius     { get; set; } = 2;

    /// <summary>Settlement people per additional tile of radius above the minimum.</summary>
    public int PopPerTerritoryRadiusTile { get; set; } = 300;

    /// <summary>Max tiles claimed per city per year (prevents instant snowball).</summary>
    public int TerritoryGrowthPerYear { get; set; } = 4;

    /// <summary>Tiles claimed at founding (~13 tiles at radius 2).</summary>
    public int InitialCityClaimRadius { get; set; } = 2;
}
