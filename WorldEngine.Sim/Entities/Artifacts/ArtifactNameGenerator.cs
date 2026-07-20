using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Artifacts;

/// <summary>
/// Deterministic legendary-item name generator seeded via WorldRng.
/// Same world seed + same invocation parameters produce identical names.
/// Style: "&lt;Epithet&gt; &lt;Noun&gt;" — e.g. "Dawnbreaker", "The Sundered Crown".
/// </summary>
public static class ArtifactNameGenerator
{
    // Salt used for name-gen RNG calls — keeps artifact naming independent of other rolls.
    private const int NameSalt = 0x4172_7446; // "ArtF" in ASCII-hex

    // ─── Noun pools by category ────────────────────────────────────────────────

    private static readonly string[] WeaponNouns =
    [
        "Blade", "Edge", "Fang", "Shard", "Cleaver", "Reaper", "Striker",
        "Piercer", "Breaker", "Slayer", "Render", "Spike", "Talon", "Tooth"
    ];

    private static readonly string[] ArmorNouns =
    [
        "Shield", "Plate", "Mantle", "Vambrace", "Aegis", "Carapace",
        "Bulwark", "Bastion", "Shroud", "Cuirass", "Hauberk", "Ward"
    ];

    private static readonly string[] RegaliaNouns =
    [
        "Crown", "Scepter", "Signet", "Circlet", "Diadem", "Orb",
        "Regalia", "Insignia", "Seal", "Banner", "Standard", "Throne"
    ];

    private static readonly string[] TomeNouns =
    [
        "Tome", "Codex", "Chronicle", "Scroll", "Grimoire", "Compendium",
        "Ledger", "Treatise", "Manuscript", "Cipher", "Libram"
    ];

    private static readonly string[] RelicNouns =
    [
        "Relic", "Shard", "Fragment", "Totem", "Talisman", "Idol",
        "Bone", "Stone", "Heart", "Eye", "Vessel", "Urn", "Ossuary"
    ];

    private static readonly string[] JewelryNouns =
    [
        "Ring", "Amulet", "Pendant", "Brooch", "Bracelet", "Torque",
        "Gem", "Jewel", "Clasp", "Medallion", "Locket", "Anklet"
    ];

    private static readonly string[] ArtworkNouns =
    [
        "Tapestry", "Fresco", "Sculpture", "Relief", "Mosaic", "Portrait",
        "Mural", "Idol", "Effigy", "Frieze", "Bust", "Vessel"
    ];

    // ─── Epithet pool — shared across all categories ───────────────────────────

    private static readonly string[] Epithets =
    [
        "Dawn", "Dusk", "Shadow", "Sundered", "Eternal", "Crimson",
        "Ashen", "Iron", "Jade", "Obsidian", "Silver", "Golden", "Ancient",
        "Fractured", "Forgotten", "Radiant", "Burning", "Frozen", "Bitter",
        "Hollow", "Cursed", "Hallowed", "Woven", "Shattered", "Undying",
        "Storm", "Blood", "Ember", "Star", "Fell", "Void", "Sacred", "Lost",
        "True", "Pale", "Dire", "Solemn", "Bright", "Grim", "Exalted"
    ];

    // ─── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a legendary name for an artifact of the given category.
    /// Deterministic: the same world seed, tick, and <paramref name="artifactIndex"/>
    /// always produce the same name.
    /// </summary>
    /// <param name="w">World state providing seed and current tick.</param>
    /// <param name="category">Artifact category that selects the noun pool.</param>
    /// <param name="artifactIndex">
    /// Monotonically increasing index (e.g. world.Artifacts.Count at creation time)
    /// used as the X coordinate in the RNG so concurrent artifacts on the same tick
    /// get distinct names.
    /// </param>
    public static string Generate(WorldState w, ArtifactCategory category, int artifactIndex)
    {
        string[] nouns = NounsFor(category);

        // Two independent RNG draws: one for epithet, one for noun.
        float epithetRoll = Core.WorldRng.FloatAt(
            w.WorldSeed, w.CurrentTick, artifactIndex, 0, NameSalt);
        float nounRoll = Core.WorldRng.FloatAt(
            w.WorldSeed, w.CurrentTick, artifactIndex, 1, NameSalt);

        string epithet = Epithets[(int)(epithetRoll * Epithets.Length)];
        string noun    = nouns[(int)(nounRoll * nouns.Length)];

        return $"The {epithet} {noun}";
    }

    // ─── Private helpers ───────────────────────────────────────────────────────

    private static string[] NounsFor(ArtifactCategory cat) => cat switch
    {
        ArtifactCategory.Weapon   => WeaponNouns,
        ArtifactCategory.Armor    => ArmorNouns,
        ArtifactCategory.Regalia  => RegaliaNouns,
        ArtifactCategory.Tome     => TomeNouns,
        ArtifactCategory.Relic    => RelicNouns,
        ArtifactCategory.Jewelry  => JewelryNouns,
        ArtifactCategory.Artwork  => ArtworkNouns,
        _                         => WeaponNouns
    };
}
