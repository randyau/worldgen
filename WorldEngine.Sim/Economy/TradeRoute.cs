using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;

namespace WorldEngine.Sim.Economy;

/// <summary>
/// M14 14.2 — canonical, order-independent key for a settlement-pair trade route. Two merchants
/// trading Home→Dest and Dest→Home refer to the same physical route, so the pair is normalized
/// (lower tile first, by (X, Y)) rather than keyed directionally.
/// </summary>
public readonly record struct TradeRouteKey(TileCoord TileA, TileCoord TileB)
{
    public static TradeRouteKey Of(TileCoord t1, TileCoord t2)
    {
        bool t1First = t1.X < t2.X || (t1.X == t2.X && t1.Y <= t2.Y);
        return t1First ? new TradeRouteKey(t1, t2) : new TradeRouteKey(t2, t1);
    }
}

/// <summary>Lifecycle state of a persistent <see cref="TradeRoute"/>.</summary>
public enum TradeRouteStatus
{
    /// <summary>Open for caravan traffic.</summary>
    Active,
    /// <summary>Closed by war between the endpoints' civs, a lost endpoint settlement, or
    /// sustained caravan losses (see <see cref="TradeRoute.ConsecutiveCaravanLosses"/>). Reopens
    /// automatically once the severing condition clears and a cooldown elapses — see
    /// Tier2BehaviorPhase.RunTradeRoutes.</summary>
    Severed
}

/// <summary>
/// M14 14.2 — a caravan in transit between a route's two endpoints. Goods and their implied value
/// leave the home settlement's ResourceStores at <see cref="DepartTick"/> and are delivered (or
/// lost — see interception/disaster/piracy in Tier2BehaviorPhase.RunTradeRoutes) at
/// <see cref="ArrivalTick"/>. Mirrors the shape of Civilizations.PendingEmissary (departure tick +
/// precomputed arrival tick, resolved when reached) — the actual existing in-transit/ETA precedent
/// in this codebase; M11's SeaVoyage is stepwise per-tile character movement with no separate
/// duration/ETA record, so it does not offer a data shape to reuse directly.
/// </summary>
public sealed record Caravan(
    EntityId  MerchantId,
    TileCoord HomeTile,
    TileCoord DestTile,
    string    Resource,
    float     Quantity,
    long      DepartTick,
    long      ArrivalTick);

/// <summary>
/// M14 14.2 — a persistent trade route between two settlements, formed once
/// EconomyConfig.TradeRouteFormationThreshold one-shot RunMerchant trades have succeeded between
/// the same pair (see Tier2BehaviorPhase.MaybeFormTradeRoute). Mutable (mirrors Organization, not
/// a `with`-replaced record) because status/loss-streak/in-flight-caravan all change in place every
/// tick without the rest of the route's identity changing.
/// </summary>
public sealed class TradeRoute
{
    public TradeRouteKey Key { get; }
    public TileCoord TileA => Key.TileA;
    public TileCoord TileB => Key.TileB;
    public TradeRouteStatus Status { get; internal set; } = TradeRouteStatus.Active;
    public int FormedYear { get; internal set; }

    /// <summary>Consecutive lost caravans (any cause — war interception, disaster, piracy) since
    /// the last successful arrival. Reset to 0 on any successful delivery. Reaching
    /// EconomyConfig.TradeRouteSeverThreshold severs the route (cause "losses").</summary>
    public int ConsecutiveCaravanLosses { get; internal set; }

    /// <summary>Tick the route most recently became Severed. -1 if never severed. Used by the
    /// reopen-cooldown check in Tier2BehaviorPhase.RunTradeRoutes.</summary>
    public long SeveredSinceTick { get; internal set; } = -1;

    /// <summary>At most one caravan in flight per route at a time (explicit M14 14.2 scope
    /// decision — see phase doc: "a single 'one caravan in flight at a time per route'
    /// simplification is reasonable"). Null when the route has no caravan currently in transit.</summary>
    public Caravan? InTransit { get; internal set; }

    public TradeRoute(TradeRouteKey key, int formedYear)
    {
        Key = key;
        FormedYear = formedYear;
    }
}
