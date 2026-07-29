namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// Sub-tile flavor decoration for <see cref="LocalTileData"/>.DecorationType (M11 11.7) — the
/// "small pond, tree patch, rocky outcropping" detail a 100sqkm chunk of land should have, so
/// local view isn't a flat wash of one biome color. Purely cosmetic: no gameplay behavior reads
/// this yet (matches DecorationType's original "populated 11.3+" scoping note, just later).
/// </summary>
public enum LocalDecorationType : byte
{
    None            = 0,
    TreeStand       = 1,
    RockOutcropping = 2,
    Shrub           = 3,
    Wetland         = 4,
    SandDune        = 5,
}
