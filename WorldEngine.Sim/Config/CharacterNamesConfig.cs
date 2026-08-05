namespace WorldEngine.Sim.Config;

/// <summary>
/// Fallback syllable pools and epithets for procedurally naming characters whose ancestry
/// has no name/surname pools of its own (see <see cref="AncestryConfig"/> for the per-ancestry
/// equivalents used by <see cref="WorldEngine.Sim.Entities.Characters.NameGenerator"/>).
/// </summary>
public sealed class CharacterNamesConfig
{
    public string[] NameOnsets { get; set; } =
    [
        "Ael", "Bar", "Cer", "Dav", "Elo", "Far", "Gaw", "Hil",
        "Iso", "Jor", "Kir", "Lar", "Mir", "Nav", "Ory", "Pet",
        "Quin", "Rol", "Sab", "Tor", "Ulr", "Var", "Wren", "Xan",
        "Yev", "Zor", "Ald", "Bryn", "Cael", "Drav"
    ];

    public string[] NameMiddles { get; set; } =
    [
        "an", "el", "in", "or", "ath", "en", "ir", "ol", "ara", "eth"
    ];

    public string[] NameCodas { get; set; } =
    [
        "dra", "ath", "wen", "ric", "ora", "iel", "and", "eth", "ira", "on",
        "ess", "wyn", "ard", "in", "os", "eva", "an", "ith", "ura", "el"
    ];

    public string[] SurnameOnsets { get; set; } =
    [
        "Ash", "Stone", "Black", "White", "Iron", "Gold", "Silver", "Oak",
        "Storm", "Thorn", "Wind", "Frost", "Bright", "Hollow", "Vale", "Wold"
    ];

    public string[] SurnameCodas { get; set; } =
    [
        "wood", "field", "well", "brook", "haven", "worth", "ford", "moor",
        "shade", "crest", "hold", "gate", "reach", "mere", "dale", "borne"
    ];

    public string[] Epithets { get; set; } =
    [
        "the Bold", "the Wise", "the Swift", "the Iron", "the Just",
        "the Wanderer", "the Unyielding", "the Fierce", "the Silent", "the Bright",
        "the Grim", "the Pale", "the Stormbringer", "the Seeker", "the Undying",
        "the Patient", "the Relentless", "the Scholar", "the Cruel", "the Gentle",
        "the Far-Sighted", "the Wolf", "the Bear", "the Hawk", "the Serpent"
    ];
}
