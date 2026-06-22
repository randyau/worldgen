namespace WorldEngine.Sim.World;

/// <summary>
/// A mineral or resource deposit at a tile. Multiple deposits can stack at one
/// location (e.g., quarry slate over a placer gold seam). List ordered by depth
/// (surface first).
/// </summary>
public sealed record ResourceDeposit(
    string DepositType,  // open string — "Iron", "Copper", "Tin", "Gold", "Slate", etc.
    byte Quality,        // 0-255
    byte Depth           // 0=surface, 255=deep
);
