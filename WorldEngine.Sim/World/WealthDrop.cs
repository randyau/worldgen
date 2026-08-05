using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// M14 14.0 (decision 5) — an unclaimed pool of personal Wealth left at a death tile when a
/// character dies with no eligible heir, or when a fraction survives inheritance. Mirrors the
/// Artifact.Owner.Lost + GoalManager co-location-claim pattern from M5, minimally: any living
/// character standing on the same tile can claim the whole pool. Wealth is abstract/personal, not
/// a physical settlement resource, so this deliberately does not touch SettlementStub.ResourceStores.
/// Included in EconomyPhase's TotalMoneySupply sum while unclaimed, and subject to the same
/// EconomyConfig.PersonalWealthSpoilageRate sink as living characters' Wealth (decision 5's revision
/// — a drop pool cannot stand forever, unspoiling and unmeasured).
/// </summary>
public sealed record WealthDrop(TileCoord Location, float Amount, int CreatedTick);
