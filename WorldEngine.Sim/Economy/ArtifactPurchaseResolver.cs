using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Economy;

/// <summary>
/// M14 14.3 — resolves <see cref="PurchaseArtifact"/>: goal fulfillment via trade (Wealth's
/// spend-side MVP). An alternative to GoalManager's existing claim-if-Lost path, evaluated only
/// when the coveted artifact is NOT Lost (owned by a living character or a settlement). See
/// docs/phases/m14_economy_independent_wealth.md decisions 3, 7, 8.
/// </summary>
public static class ArtifactPurchaseResolver
{
    /// <summary>
    /// Attempts to resolve a purchase. Returns true if the purchase completed (Wealth and
    /// ownership transferred, an <see cref="EventType.ArtifactPurchased"/> event queued); false if
    /// any gate failed, in which case nothing was mutated. Gates, in order: buyer alive, artifact
    /// exists/not destroyed/not Lost, owner resolvable and alive (Character) or exists (Settlement),
    /// buyer is not already the owner, buyer's Wealth covers the computed price, and the owner's
    /// willingness check passes.
    /// </summary>
    public static bool TryResolve(
        PurchaseArtifact cmd, WorldState world, EconomyConfig cfg, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.BuyerId) is not Tier1Character buyer || !buyer.IsAlive) return false;
        if (!world.Artifacts.TryGetValue(cmd.ArtifactId, out var artifact)) return false;
        if (artifact.IsDestroyed) return false;
        if (artifact.Owner.Kind == ArtifactOwnerKind.Lost) return false;
        if (artifact.Owner.Kind == ArtifactOwnerKind.Character && artifact.Owner.CharacterId == buyer.Id.Value)
            return false; // already owns it

        float price = PricingService.ArtifactEffectivePrice(artifact, cfg, world.GlobalPriceIndex);
        if (price <= 0f) return false;
        if (buyer.Wealth < price) return false;

        string fromDesc = artifact.Owner.Describe();
        string ownerName;

        switch (artifact.Owner.Kind)
        {
            case ArtifactOwnerKind.Character:
            {
                var ownerId = new EntityId(artifact.Owner.CharacterId);
                var ownerEntity = world.GetEntity(ownerId);
                if (ownerEntity is not (Tier1Character or Tier2Character) || !ownerEntity.IsAlive)
                    return false;

                // Tier2Character uses the reduced 6-trait PersonalityVector6, which has no
                // Compassion axis at all (checked directly) — Loyalty is the closest available
                // proxy for "how warmly this person treats someone asking a favor of them."
                float ownerCompassion = ownerEntity switch
                {
                    Tier1Character t1 => t1.Personality.Compassion,
                    Tier2Character t2 => t2.Personality.Loyalty,
                    _                 => 0f
                };
                float relTrust = world.GetRelationship(buyer.Id, ownerId)?.Trust ?? 0f;
                if (!IsWilling(ownerCompassion, relTrust, cfg)) return false;

                // Real two-sided transfer: buyer's Wealth decreases by exactly what the owner gains.
                buyer.AddWealth(-price);
                switch (ownerEntity)
                {
                    case Tier1Character t1: t1.AddWealth(price); ownerName = t1.Identity.Name; break;
                    case Tier2Character t2: t2.AddWealth(price); ownerName = t2.Name; break;
                    default: return false; // unreachable given the pattern match above
                }
                break;
            }
            case ArtifactOwnerKind.Settlement:
            {
                var tile = artifact.Owner.SettlementTile;
                if (!world.Settlements.TryGetValue(tile, out var settlement)) return false;
                // Settlement-held artifacts have no personality to gate a willingness check on —
                // a settlement's collection is public civic property, not a personal attachment,
                // so it is always willing to sell once the price itself is met (EconomyConfig
                // .PurchaseWillingnessThreshold doc comment).

                // Reverses the trade conversion (decision 4): the buyer's Wealth converts into the
                // settlement's precious-commodity ResourceStores, credited as "gold" value —
                // mirrors the home-cut recirculation credit in Tier2BehaviorPhase.ResolveMerchantTrade.
                float goldUnitValue = cfg.GetBaseValue("gold");
                if (goldUnitValue > 0f)
                {
                    var stores = settlement.ResourceStores is null
                        ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, float>(settlement.ResourceStores, StringComparer.OrdinalIgnoreCase);
                    float creditUnits = price / goldUnitValue;
                    stores["gold"] = (stores.TryGetValue("gold", out float g) ? g : 0f) + creditUnits;
                    world.Settlements[tile] = settlement with { ResourceStores = stores };
                }
                buyer.AddWealth(-price);
                ownerName = settlement.Name;
                break;
            }
            default:
                return false;
        }

        var newOwner = ArtifactOwner.OfCharacter(buyer.Id);
        ArtifactRegistry.SetOwner(world, artifact.Id, newOwner);

        var payload = JsonSerializer.Serialize(new ArtifactPurchasedPayload(
            artifact.Id.Value, artifact.Name, buyer.Id.Value, buyer.Identity.Name,
            fromDesc, ownerName, price));
        pending.Add(new PendingEvent(EventType.ArtifactPurchased, buyer.Location, null, payload,
            new[] { buyer.Id.Value },
            ActorId: buyer.Id.Value, ActorName: buyer.Identity.Name));

        return true;
    }

    /// <summary>
    /// Willingness gate (decision 3): Compassion (always populated, no relationship prerequisite)
    /// plus any existing relationship Trust bonus (0 for strangers) must clear
    /// EconomyConfig.PurchaseWillingnessThreshold. See that config field's doc comment for why
    /// this combination — rather than Trust alone — was chosen to avoid the M13.5-era
    /// Estrangement/OathBroken unreachable-threshold failure mode.
    /// </summary>
    private static bool IsWilling(float ownerCompassion, float relTrust, EconomyConfig cfg) =>
        ownerCompassion + Math.Max(0f, relTrust) >= cfg.PurchaseWillingnessThreshold;
}
