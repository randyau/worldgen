namespace WorldEngine.Sim.Config;

public sealed class TerritoryConfig
{
    /// <summary>1 tile per N people; city of 800 → 100 tiles (~radius 5).</summary>
    public int ClaimTilesPerPerson    { get; set; } = 8;

    /// <summary>Radius-1 circle, always retained.</summary>
    public int MinCityTiles           { get; set; } = 7;

    /// <summary>~radius-15; absolute upper bound on tile count regardless of radius.</summary>
    public int MaxCityTiles           { get; set; } = 700;

    /// <summary>
    /// Hard radius cap (Euclidean). Territory expands up to this distance but
    /// effective radius is also capped by <see cref="MinTerritoryRadius"/> + population / <see cref="PopPerTerritoryRadiusTile"/>.
    /// Civs must found new cities to claim land beyond this.
    /// </summary>
    public int MaxTerritoryRadius     { get; set; } = 12;

    /// <summary>Guaranteed minimum radius regardless of population.</summary>
    public int MinTerritoryRadius     { get; set; } = 3;

    /// <summary>Settlement people per additional tile of radius above the minimum.</summary>
    public int PopPerTerritoryRadiusTile { get; set; } = 150;

    /// <summary>Max tiles claimed per city per year (prevents instant snowball).</summary>
    public int TerritoryGrowthPerYear { get; set; } = 8;

    /// <summary>Tiles claimed at founding (~13 tiles at radius 2).</summary>
    public int InitialCityClaimRadius { get; set; } = 2;
}
