namespace WorldEngine.Sim.Config;

public sealed class SettlementNamesConfig
{
    public string[] Prefixes { get; set; } =
    [
        "Iron", "Stone", "Green", "Dark", "Swift", "High", "Old", "Black", "White",
        "Red", "Cold", "Bright", "Fair", "Lone", "Ash", "Ember", "Frost", "Gold",
        "Mist", "Reed", "Crag", "Deep", "Tall", "Hard", "Flint", "Silver", "Amber",
        "Hollow", "Sharp", "Broad"
    ];

    public string[] Suffixes { get; set; } =
    [
        "ford", "hold", "wick", "vale", "mere", "fell", "gate", "haven",
        "reach", "moor", "cliff", "pass", "watch", "crest", "grove", "hollow",
        "peak", "ridge", "bridge", "mill", "shore", "keep", "mark", "lea"
    ];
}
