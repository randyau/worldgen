using System.Text.Json;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Civ History panel: civ selector + rulers/wars/major-events (M8.3.3).
/// <summary>Full arc of a civilization — rulers, key wars, major events, cultural traits.</summary>
public sealed class CivHistoryPanel : IToggleablePanel
{
    private readonly IHistoryQuery _history;
    private readonly AncestryRegistry? _ancestries;
    private readonly WeDropdown<long> _civCombo = new();
    private readonly WeVStack _content = new(UiTheme.Space.Xs);
    private readonly List<CivSummary> _civSummaries = new();
    private PanelContext _ctx;

    public string Id => "civ";
    public string Title => "Civ History";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public CivHistoryPanel(IHistoryQuery history, AncestryRegistry? ancestries = null)
    {
        _history    = history;
        _ancestries = ancestries;
        _civCombo.OnChanged += PopulateCivContent;
    }

    public Widget Build()
    {
        var body = new WeVStack(UiTheme.Space.Sm);
        body.Add(_civCombo);
        body.Add(_content);
        return PanelFrame.Build(Title, body.Root, new PanelFrameOptions { OnClose = Hide });
    }

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public EmptyStateSpec? EmptyFor(PanelContext ctx) =>
        _civSummaries.Count == 0
            ? new EmptyStateSpec(EmptyStateKind.NotBuiltYet, "No civ summaries yet.", "Summaries build every 50 in-game years.")
            : null;

    public void Show() { RefreshCivList(); IsVisible = true; }
    public void Hide() { IsVisible = false; }

    /// <summary>Selects and shows a specific civilization. Called by the selection bus (M8.2.1).</summary>
    public void ShowCiv(long civId)
    {
        Show(); // rebuilds the dropdown item list and clears _content
        var match = _civSummaries.FirstOrDefault(c => c.CivId == civId);
        if (match is not null) _civCombo.Selected = civId;
        // BUG FIX: don't rely solely on the dropdown's SelectedIndexChanged to populate content —
        // if the computed index equals the ComboBox's already-current SelectedIndex (e.g. Show()
        // just rebuilt the list and auto-selected the same slot), the underlying Myra ComboBox does
        // not re-fire the event, so PopulateCivContent silently never ran and the pane stayed blank
        // even though the dropdown visibly showed the right civ selected.
        PopulateCivContent(civId);
    }

    public void Refresh() { /* content is rebuilt on dropdown change, not per-frame */ }

    private void RefreshCivList()
    {
        _civSummaries.Clear();
        _civSummaries.AddRange(_history.GetAllCivSummaries());
        _civCombo.Render(id =>
        {
            var c = _civSummaries.FirstOrDefault(s => s.CivId == id);
            if (c is null) return id.ToString();
            return c.IsCollapsed ? $"{c.Name}  [collapsed {c.CollapseYear}]" : $"{c.Name}  [active]";
        });
        _civCombo.SetItems(_civSummaries.Select(c => c.CivId));

        _content.Clear();
        if (_civSummaries.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.NotBuiltYet, "No civ summaries yet.", "Summaries build every 50 in-game years."));
    }

    private void PopulateCivContent(long civId)
    {
        _content.Clear();

        var summary = _history.GetCivSummary(new CivId((int)civId));
        if (summary is null)
        {
            _content.Add(EmptyState.Build(EmptyStateKind.NotBuiltYet, "No summary data available."));
            return;
        }

        // ── Header ───────────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build(summary.Name));
        string status = summary.IsCollapsed
            ? $"Founded Year {summary.FoundedYear}  |  Collapsed Year {summary.CollapseYear}"
            : $"Founded Year {summary.FoundedYear}  |  Active";
        _content.Add(new WeText(status, color: UiTheme.ColorRole.TextSecondary));

        string originLine = summary.FoundingOrigin switch
        {
            "Splinter" when summary.ParentCivName is not null => $"Origin: split from {summary.ParentCivName}",
            "Splinter" => "Origin: civil war / secession",
            _ => "Origin: nomads settled"
        };
        _content.Add(new WeText(originLine, color: UiTheme.ColorRole.TextMuted));

        if (summary.DominantAncestry is not null)
        {
            _content.Add(new WeText($"Dominant ancestry: {summary.DominantAncestry}", color: UiTheme.ColorRole.TextSecondary));

            if (_ancestries is not null && _ancestries.Get(summary.DominantAncestry) is { } anc)
            {
                if (!string.IsNullOrEmpty(anc.ArchitecturalStyle))
                    _content.Add(new WeText($"  Cultural style: {anc.ArchitecturalStyle}  |  {anc.SettlementDescriptor}", color: UiTheme.ColorRole.TextMuted));
                if (anc.ArtisticTraditions.Length > 0)
                    _content.Add(new WeText($"  Artistic traditions: {string.Join(", ", anc.ArtisticTraditions)}", color: UiTheme.ColorRole.TextMuted));
            }
        }

        var stats = new KeyValueGrid();
        stats.Add("Peak settlements", summary.PeakSettlements.ToString());
        stats.Add("Rulers", summary.TotalRulers.ToString());
        stats.Add("Wars", (summary.TotalWarsInitiated + summary.TotalWarsSuffered).ToString());
        stats.Add("Yrs at war", summary.TotalYearsAtWar.ToString());
        _content.Add(stats);

        // ── Cultural Traits ──────────────────────────────────────────────────
        if (summary.CulturalTraits.Count > 0)
        {
            _content.Add(SectionHeader.Build("Cultural Traits"));
            _content.Add(new WeText("  " + string.Join(", ", summary.CulturalTraits), color: UiTheme.ColorRole.AccentInteractive));
        }

        // ── Succession ───────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Rulers"));
        var rulers = _history.GetRulersOfCiv(new CivId((int)civId));
        if (rulers.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.NotBuiltYet, "(no succession data)"));
        else
            foreach (var ruler in rulers)
            {
                string nameStr = ruler.NameOrdinal > 0 ? $"{ruler.Name} {_ctx.Present.ToRoman(ruler.NameOrdinal)}" : ruler.Name;
                if (ruler.Epithet is not null) nameStr += $" the {ruler.Epithet}";
                string lifeStr = ruler.DeathYear > 0
                    ? $"  {nameStr}  ({ruler.BirthYear}–{ruler.DeathYear})"
                    : $"  {nameStr}  (b. {ruler.BirthYear})";
                _content.Add(new WeText(lifeStr, color: UiTheme.ColorRole.TextSecondary));
            }

        // ── Key Wars ─────────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Key Wars"));
        var civEvents = _history.GetCivHistory(new CivId((int)civId), 0, int.MaxValue);
        var warEvents = civEvents
            .Where(e => e.Type == EventType.WarDeclared)
            .OrderByDescending(e => e.SignificanceScore)
            .Take(5)
            .ToList();

        if (warEvents.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.NotBuiltYet, "(no wars recorded)"));
        else
            foreach (var war in warEvents)
                _content.Add(new WeText($"  Year {war.Year} — War vs {ExtractWarOpponent(war.PayloadJson, civId)}", color: UiTheme.ColorRole.TextSecondary));

        // ── Major Events ─────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Major Events"));
        var headlineEvents = civEvents
            .Where(e => e.TierInvolvement == EventTier.Headline)
            .OrderBy(e => e.Year)
            .TakeLast(10)
            .ToList();

        if (headlineEvents.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.NotBuiltYet, "(no headline events recorded)"));
        else
            foreach (var ev in headlineEvents)
                _content.Add(new WeText($"  Year {ev.Year} — {ev.TypeName}", color: UiTheme.ColorRole.TextSecondary));
    }

    private static string ExtractWarOpponent(string payloadJson, long civId)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("DeclarerCivId", out var dc) && root.TryGetProperty("TargetCivId", out var tc))
            {
                long declarerId = dc.GetInt64();
                return declarerId == civId
                    ? (root.TryGetProperty("TargetCivName", out var tn) ? tn.GetString() ?? "?" : "?")
                    : (root.TryGetProperty("DeclarerCivName", out var dn) ? dn.GetString() ?? "?" : "?");
            }
        }
        catch { /* ignore */ }
        return "?";
    }
}
