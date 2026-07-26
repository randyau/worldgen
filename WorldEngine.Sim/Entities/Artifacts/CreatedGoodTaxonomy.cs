using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Artifacts;

/// <summary>
/// Groups and category-derivation for <see cref="CreatedGoodType"/> (M9 G-1/G-2 unification).
/// Replaces the old role-blind <c>RoleToArtifactCategory</c> map: an artifact's category is
/// derived from the specific good a character was making, weighted across the categories that
/// good plausibly becomes, instead of the creator's Tier2 role.
/// </summary>
public static class CreatedGoodTaxonomy
{
    public static readonly CreatedGoodType[] ArtisanGoods =
    [
        CreatedGoodType.Textiles, CreatedGoodType.Pottery, CreatedGoodType.Metalwork,
        CreatedGoodType.Woodcraft, CreatedGoodType.Leatherwork, CreatedGoodType.Stonework,
    ];

    public static readonly CreatedGoodType[] ArtGoods =
    [
        CreatedGoodType.Monument, CreatedGoodType.Epic, CreatedGoodType.Song,
        CreatedGoodType.Tapestry, CreatedGoodType.Sculpture, CreatedGoodType.Painting,
    ];

    public static readonly CreatedGoodType[] DiscoveryGoods =
    [
        CreatedGoodType.Agriculture, CreatedGoodType.Medicine, CreatedGoodType.Astronomy,
        CreatedGoodType.Mathematics, CreatedGoodType.Engineering, CreatedGoodType.Philosophy,
        CreatedGoodType.Navigation, CreatedGoodType.Metallurgy,
    ];

    /// <summary>Settlement resource-ledger bonus key each discovery applies. Replaces the old
    /// parallel-array lookup keyed by DiscoveryType's numeric index.</summary>
    public static readonly IReadOnlyDictionary<CreatedGoodType, string> DiscoveryBonusKeys =
        new Dictionary<CreatedGoodType, string>
        {
            [CreatedGoodType.Agriculture]  = "bonus_food_yield",
            [CreatedGoodType.Medicine]     = "bonus_disease_resistance",
            [CreatedGoodType.Astronomy]    = "bonus_navigation",
            [CreatedGoodType.Mathematics]  = "bonus_trade_income",
            [CreatedGoodType.Engineering]  = "bonus_construction_speed",
            [CreatedGoodType.Philosophy]   = "bonus_civ_cohesion",
            [CreatedGoodType.Navigation]   = "bonus_exploration_range",
            [CreatedGoodType.Metallurgy]   = "bonus_military_strength",
        };

    // DECISION (M9 9.1): bonus_construction_speed, bonus_navigation, and bonus_exploration_range
    // are intentionally inert — written by Scholar discoveries but never consumed. There is no
    // build-time-over-ticks concept (improvements are placed instantly), no travel-speed concept,
    // and no exploration-range concept anywhere in the sim for these to hook into. Wiring them
    // now would mean inventing a mechanic to justify a config knob rather than the reverse; they
    // cost nothing sitting unused (bonus_* keys never enter ResourcePressurePhase's ledger, so
    // they accumulate unspoiled and unbounded — harmless while unread). Revisit once/if those
    // mechanics land.

    // DECISION: this table is taxonomy *structure* (which categories a good can plausibly
    // become at all), not a tunable rate — same precedent as ArtifactNameGenerator.NounsFor and
    // the old DiscoveryBonusKey array. The weight values are illustrative game-balance numbers
    // with no single "correct" answer; revisit empirically once artifact-category telemetry
    // exists, per M9 phase 9.0 doc.
    public static readonly IReadOnlyDictionary<CreatedGoodType, (ArtifactCategory Category, float Weight)[]> CategoryWeights =
        new Dictionary<CreatedGoodType, (ArtifactCategory, float)[]>
        {
            [CreatedGoodType.Metalwork]   = [(ArtifactCategory.Weapon, 0.55f), (ArtifactCategory.Armor, 0.35f), (ArtifactCategory.Regalia, 0.10f)],
            [CreatedGoodType.Woodcraft]   = [(ArtifactCategory.Weapon, 0.6f), (ArtifactCategory.Artwork, 0.4f)],
            [CreatedGoodType.Leatherwork] = [(ArtifactCategory.Armor, 0.7f), (ArtifactCategory.Jewelry, 0.3f)],
            [CreatedGoodType.Stonework]   = [(ArtifactCategory.Regalia, 0.5f), (ArtifactCategory.Artwork, 0.5f)],
            [CreatedGoodType.Textiles]    = [(ArtifactCategory.Regalia, 0.4f), (ArtifactCategory.Jewelry, 0.3f), (ArtifactCategory.Artwork, 0.3f)],
            [CreatedGoodType.Pottery]     = [(ArtifactCategory.Artwork, 0.7f), (ArtifactCategory.Relic, 0.3f)],

            [CreatedGoodType.Monument]  = [(ArtifactCategory.Regalia, 1.0f)],
            [CreatedGoodType.Epic]      = [(ArtifactCategory.Tome, 1.0f)],
            [CreatedGoodType.Song]      = [(ArtifactCategory.Tome, 0.6f), (ArtifactCategory.Relic, 0.4f)],
            [CreatedGoodType.Tapestry]  = [(ArtifactCategory.Artwork, 1.0f)],
            [CreatedGoodType.Sculpture] = [(ArtifactCategory.Artwork, 1.0f)],
            [CreatedGoodType.Painting]  = [(ArtifactCategory.Artwork, 1.0f)],

            [CreatedGoodType.Agriculture]  = [(ArtifactCategory.Tome, 1.0f)],
            [CreatedGoodType.Medicine]     = [(ArtifactCategory.Relic, 0.7f), (ArtifactCategory.Tome, 0.3f)],
            [CreatedGoodType.Astronomy]    = [(ArtifactCategory.Tome, 1.0f)],
            [CreatedGoodType.Mathematics]  = [(ArtifactCategory.Tome, 1.0f)],
            [CreatedGoodType.Engineering]  = [(ArtifactCategory.Tome, 0.7f), (ArtifactCategory.Relic, 0.3f)],
            [CreatedGoodType.Philosophy]   = [(ArtifactCategory.Tome, 1.0f)],
            [CreatedGoodType.Navigation]   = [(ArtifactCategory.Tome, 0.6f), (ArtifactCategory.Relic, 0.4f)],
            [CreatedGoodType.Metallurgy]   = [(ArtifactCategory.Weapon, 0.5f), (ArtifactCategory.Armor, 0.3f), (ArtifactCategory.Tome, 0.2f)],
        };

    /// <summary>Weighted-roll a single <see cref="ArtifactCategory"/> for the given good, salted
    /// deterministically by the creating entity + tick.</summary>
    public static ArtifactCategory PickCategory(WorldState world, EntityId id, CreatedGoodType good) =>
        WeightedPick(CategoryWeights[good], world.GetRandomFloat(id, SimRngSalts.ArtifactCategoryPick));

    /// <summary>Weighted-roll a category from an arbitrary (category, weight) table using an
    /// already-drawn [0,1) roll — shared by the battle-forged and heroic-death G-2 paths, which
    /// have no CreatedGoodType context (combat-triggered, not production-triggered).</summary>
    public static ArtifactCategory WeightedPick((ArtifactCategory Category, float Weight)[] table, float roll)
    {
        float total = 0f;
        foreach (var (_, weight) in table) total += weight;

        float threshold = roll * total;
        float cumulative = 0f;
        foreach (var (category, weight) in table)
        {
            cumulative += weight;
            if (threshold < cumulative) return category;
        }
        return table[^1].Category;
    }
}
