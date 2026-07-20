using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.Entities.Artifacts;

/// <summary>Category of legendary item — drives name generation and narrative significance.</summary>
public enum ArtifactCategory { Weapon, Armor, Regalia, Tome, Relic, Jewelry, Artwork }

/// <summary>Who (or what) currently holds an artifact.</summary>
public enum ArtifactOwnerKind { Character, Settlement, Lost }

/// <summary>
/// Value-type owner discriminated union. Use the factory methods or <see cref="Lost"/> sentinel.
/// Lost = ownerless (resting in a ruin / wilderness) until re-claimed.
/// </summary>
public readonly record struct ArtifactOwner(
    ArtifactOwnerKind Kind, long CharacterId, TileCoord SettlementTile)
{
    /// <summary>Creates an owner backed by a character entity.</summary>
    public static ArtifactOwner OfCharacter(EntityId id) =>
        new(ArtifactOwnerKind.Character, id.Value, default);

    /// <summary>Creates an owner backed by a settlement at the given tile.</summary>
    public static ArtifactOwner OfSettlement(TileCoord t) =>
        new(ArtifactOwnerKind.Settlement, 0, t);

    /// <summary>Sentinel for an ownerless artifact.</summary>
    public static readonly ArtifactOwner Lost = new(ArtifactOwnerKind.Lost, 0, default);

    /// <summary>Human-readable description used in event payloads.</summary>
    public string Describe() => Kind switch
    {
        ArtifactOwnerKind.Character  => $"Character #{CharacterId}",
        ArtifactOwnerKind.Settlement => $"Settlement ({SettlementTile.X},{SettlementTile.Y})",
        ArtifactOwnerKind.Lost       => "Lost",
        _                            => "Unknown"
    };
}

/// <summary>
/// Immutable data record for a legendary artifact. Artifacts persist through history
/// independently of their creator. Transfer = record replacement via <c>with</c> expression.
/// </summary>
public sealed record Artifact(
    ArtifactId Id,
    string Name,
    ArtifactCategory Category,
    int CreatedYear,
    long CreatorId,       // EntityId.Value of creator char; 0 if world/battle-forged
    string CreatorName,
    string Origin,        // "masterwork" | "battle" | "heroic_death"
    float Quality,        // 0..1 power/property score; drives covet
    ArtifactOwner Owner,
    bool IsDestroyed = false,
    int DestroyedYear = 0);
