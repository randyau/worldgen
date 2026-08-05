using System.Linq;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Economic Ledger panel: read-only overview of M14's economy (14.5).
/// <summary>
/// Read-only economic ledger: the world's <see cref="WorldSnapshot.GlobalPriceIndex"/>, every
/// settlement's precious-commodity reserves and local scarcity multipliers, every Guild/Civ
/// Organization's treasury and recent trade activity, and the wealthiest living characters. Built
/// entirely from <see cref="WorldSnapshot"/> — no history query, no selection dependency (unlike
/// <see cref="CharacterProfilePanel"/>/<see cref="BeastProfilePanel"/>, this panel isn't about any
/// one entity, so it needs no Show&lt;Thing&gt;(id) entry point — Show() alone is enough to
/// populate it from whatever is already bound).
/// </summary>
public sealed class EconomicLedgerPanel : IToggleablePanel
{
    private readonly WeVStack _content = new(UiTheme.Space.Xs);
    private PanelContext _ctx;

    public string Id => "ledger";
    public string Title => "Economic Ledger";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public void Show()
    {
        IsVisible = true;
        // Content is entirely WorldSnapshot-driven and needs no selection — populate immediately
        // from the already-bound context (Mandatory Pattern #4), same as CharacterProfilePanel/
        // BeastProfilePanel's ShowXxx(id) methods do, rather than waiting for the next tick-gated
        // RefreshVisible() to arrive (which would leave the panel blank on first open while paused).
        Refresh();
    }

    public void Hide() => IsVisible = false;

    public void Refresh()
    {
        _content.Clear();

        var snap = _ctx.Snapshot;
        if (snap is null)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "No world data yet."));
            return;
        }

        _content.Add(SectionHeader.Build("World Economy"));
        var header = new KeyValueGrid();
        header.Add("Global Price Index", $"{snap.GlobalPriceIndex:F2}");
        _content.Add(header);

        // ── Settlements ──────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Settlements"));
        var settlements = snap.Settlements.Values
            .Select(s => (Snapshot: s, Reserves: PreciousReserveTotal(s)))
            .OrderByDescending(t => t.Reserves)
            .Take(15)
            .ToList();

        if (settlements.Count == 0)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(no settlements founded yet)"));
        }
        else
        {
            foreach (var (s, reserves) in settlements)
            {
                _content.Add(new WeText($"  {s.Name} [{s.CivName}] — reserves {reserves:F1}",
                    color: UiTheme.ColorRole.TextSecondary));
                if (s.LocalScarcityMultipliers is { Count: > 0 } mults)
                {
                    string multStr = string.Join(", ", mults
                        .Where(kv => Math.Abs(kv.Value - 1f) > 0.01f) // only show commodities off-parity — a flat 1.0x for everything is noise
                        .Select(kv => $"{kv.Key} {kv.Value:F2}x"));
                    if (multStr.Length > 0)
                        _content.Add(new WeText($"    scarcity: {multStr}", color: UiTheme.ColorRole.TextMuted));
                }
            }
        }

        // ── Guilds & Treasuries ──────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Guilds & Treasuries"));
        var guilds = (snap.Guilds ?? Array.Empty<GuildSnapshot>())
            .OrderByDescending(g => g.Treasury)
            .Take(15)
            .ToList();

        if (guilds.Count == 0)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(no organization has a treasury yet)"));
        }
        else
        {
            foreach (var g in guilds)
            {
                string home = g.HomeSettlementCoord is { } h ? $" @ ({h.X},{h.Y})" : "";
                _content.Add(new WeText(
                    $"  {g.Name} ({g.Kind}){home} — treasury {g.Treasury:F1}, {g.MemberCount} members, {g.RecentTradeEventCount} recent trades",
                    color: g.Treasury < 0f ? UiTheme.ColorRole.StateNegative : UiTheme.ColorRole.TextSecondary));
            }
        }

        // ── Wealthiest characters ────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Wealthiest Characters"));
        var wealthiest = snap.EntitySnapshots.Values
            .Where(e => e.IsAlive && e.Wealth > 0.01f)
            .OrderByDescending(e => e.Wealth)
            .Take(10)
            .ToList();

        if (wealthiest.Count == 0)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(no living character holds any Wealth yet)"));
        }
        else
        {
            foreach (var e in wealthiest)
                _content.Add(new WeText($"  {e.Name} — {e.Wealth:F1}", color: UiTheme.ColorRole.TextSecondary));
        }
    }

    /// <summary>Sum of a settlement's ResourceStores value in every money-equivalent commodity the
    /// UI can see (the dictionary keys already present in ResourceStores — the UI has no
    /// EconomyConfig reference to enumerate MoneyEquivalentCommodities itself, so this sums
    /// whatever commodities LocalScarcityMultipliers was computed for on the sim side, which is
    /// exactly that same set).</summary>
    private static float PreciousReserveTotal(SettlementSnapshot s)
    {
        if (s.ResourceStores is null || s.LocalScarcityMultipliers is null) return 0f;
        float total = 0f;
        foreach (var commodity in s.LocalScarcityMultipliers.Keys)
            if (s.ResourceStores.TryGetValue(commodity, out float units))
                total += units; // raw unit count — a per-unit-value-weighted total would need
                                 // EconomyConfig.BaseValuePerUnit, which isn't projected to the UI;
                                 // unit count alone is still a meaningful "does this settlement
                                 // have any reserves at all" ranking signal for this read-only view.
        return total;
    }
}
