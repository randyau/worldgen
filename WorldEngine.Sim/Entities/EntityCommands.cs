using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities;

// All entity commands are sealed records with value-type fields only.
// No callbacks, delegates, or mutable object references (CLAUDE.md Mandatory Pattern #4).

// Beast commands
/// <summary>All entity ICommand records (MoveTo, Rest, etc.) in one file; sealed records with value-type fields only.</summary>
public sealed record MoveToTile(EntityId EntityId, TileCoord Destination) : ICommand;
public sealed record Graze(EntityId EntityId) : ICommand;
public sealed record Rest(EntityId EntityId) : ICommand;
public sealed record Attack(EntityId Attacker, EntityId Target) : ICommand;
public sealed record Flee(EntityId EntityId, TileCoord AwayFrom) : ICommand;

// Character commands (Phase 2.2+)
public sealed record EstablishSettlement(EntityId CharacterId, TileCoord Tile) : ICommand;
public sealed record AllyWith(EntityId CharacterId, EntityId TargetId) : ICommand;
public sealed record DeclareRivalry(EntityId CharacterId, EntityId TargetId) : ICommand;
// War is a civ-level action: the declaring character must be their civ's ruler;
// the target is a civilization, not an individual character.
public sealed record DeclareWar(EntityId CharacterId, CivId TargetCivId) : ICommand;
public sealed record RaidSettlement(EntityId CharacterId, TileCoord SettlementTile) : ICommand;
public sealed record Negotiate(EntityId CharacterId, EntityId TargetId) : ICommand;
// M13 13.0 — upgrades a high-trust Bond into marriage: RelationshipFlags.IsMarried|IsFamily
// plus a new Family-kind Organization (the household). See CivTracker.ResolveMarriage.
public sealed record ProposeMarriage(EntityId CharacterId, EntityId TargetId) : ICommand;
// M13 13.2 — GranterId materially aids RecipientId (in need), creating a Debt obligation.
public sealed record GrantAid(EntityId GranterId, EntityId RecipientId) : ICommand;
// M13 13.2 — CreditorId forgives DebtorId's obligation: zeroes Debt, boosts Trust.
public sealed record ForgiveDebt(EntityId CreditorId, EntityId DebtorId) : ICommand;
// M13 13.1 — CharacterId appeases an existing, feared rival: reduces Fear, nudges Trust up.
public sealed record Placate(EntityId CharacterId, EntityId TargetId) : ICommand;
// M13 13.4 — CharacterId defects to ConfidantId's civ, seeking asylum with a trusted foreign friend.
public sealed record Defect(EntityId CharacterId, EntityId ConfidantId) : ICommand;
public sealed record CreateArtwork(EntityId CharacterId) : ICommand;
public sealed record FleeRegion(EntityId CharacterId, TileCoord Destination) : ICommand;

// M14 14.1 — completes the priced-payment side of Tier2BehaviorPhase.RunMerchant's existing
// one-shot trade: the destination settlement pays for Quantity units of Resource (priced via
// PricingService.EffectivePrice at the moment of resolution) out of its own precious-commodity
// ResourceStores, crediting MerchantId's personal Wealth net of EconomyConfig
// .MerchantHomeCutFraction, which instead recirculates into HomeTile's ResourceStores. Resolved
// by Tier2BehaviorPhase itself (not CivTracker.Resolve/CharacterBehaviorPhase.ResolveCommand —
// Tier2 role behavior is phase-driven every tick, not emitted via the Tier1 utility-scorer/
// EMIT-RESOLVE split those two switches police, per ArchitectureRuleTests
// .CivTrackerCommands_AreAllDispatchedFromResolveCommand's scope).
public sealed record CompleteMerchantTrade(
    EntityId  MerchantId,
    TileCoord HomeTile,
    TileCoord DestTile,
    string    Resource,
    float     Quantity) : ICommand;

// M14 14.2 — graduates RunMerchant's one-shot trade scan into a persistent Economy.TradeRoute
// once EconomyConfig.TradeRouteFormationThreshold successful trades have occurred between the
// same settlement pair (see Tier2BehaviorPhase.MaybeFormTradeRoute). Resolved immediately by
// Tier2BehaviorPhase itself, same non-CivTracker/non-ResolveCommand exemption as
// CompleteMerchantTrade above (Tier2 role behavior is phase-driven every tick, not emitted via the
// Tier1 utility-scorer/EMIT-RESOLVE split ArchitectureRuleTests
// .CivTrackerCommands_AreAllDispatchedFromResolveCommand polices).
public sealed record FormTradeRoute(
    EntityId  MerchantId,
    TileCoord TileA,
    TileCoord TileB) : ICommand;

// M14 14.3 — goal fulfillment via trade: BuyerId (a Tier1Character with an active CovetArtifact
// goal) purchases ArtifactId from its current living Character/Settlement owner (not Lost),
// mirroring GrantAid's two-EntityId shape. Priced via PricingService.ArtifactEffectivePrice
// (decisions 7/8) and gated by EconomyConfig.PurchaseWillingnessThreshold on top of the price
// itself. Resolved immediately by ArtifactPurchaseResolver from within
// GoalManager.UpdateGoals's WorldState overload — the same non-CivTracker/non-ResolveCommand
// exemption as CompleteMerchantTrade/FormTradeRoute above (see their doc comments): the existing
// claim-if-Lost path in that same method already mutates WorldState directly with no ICommand at
// all, so a purchase alternative resolved inline via a plain-data command (for testability/event-
// payload shape, per CLAUDE.md's command-pattern convention) rather than round-tripping through
// the Tier1 EMIT/RESOLVE split is consistent with the surrounding code, not a new exemption.
public sealed record PurchaseArtifact(EntityId BuyerId, ArtifactId ArtifactId) : ICommand;

// M14 14.4 — decision 9's two treasury commands, mirroring GrantAid's two-EntityId shape (the
// transferred amount is config-driven at resolution, same as GrantAid's AidDebtIncrement, rather
// than a command field). ContributeToTreasury: any member of OrganizationId, no authority check —
// moves personal Wealth into Organization.Treasury. Resolved by CivTracker.ResolveContributeToTreasury.
public sealed record ContributeToTreasury(EntityId CharacterId, OrganizationId OrganizationId) : ICommand;
// WithdrawFromTreasury: gated on LeaderId == Organization.LeaderId (the only authority check in
// the whole model, per decision 9) — moves Organization.Treasury into RecipientId's personal
// Wealth. RecipientId may be LeaderId themself or any other living member. Resolved by
// CivTracker.ResolveWithdrawFromTreasury.
public sealed record WithdrawFromTreasury(
    EntityId LeaderId, OrganizationId OrganizationId, EntityId RecipientId) : ICommand;

// Phase 3.0 — city-state territory commands
public sealed record BuildImprovement(
    EntityId        CharacterId,
    TileCoord       TargetTile,
    ImprovementType ImprovementType) : ICommand;
