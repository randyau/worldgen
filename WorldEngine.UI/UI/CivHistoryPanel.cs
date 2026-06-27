using System.Text.Json;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

/// <summary>
/// Myra panel showing the full arc of a civilization — rulers, key wars, major events, traits.
/// Includes a civ selector ComboBox at the top.
/// </summary>
public sealed class CivHistoryPanel
{
    private readonly IHistoryQuery _history;
    private readonly AncestryRegistry? _ancestries;
    private readonly VerticalStackPanel _content;
    private readonly ComboBox _civCombo;
    private readonly List<long> _civIds = new();

    public Widget Root { get; }
    public bool IsVisible { get; private set; }

    public CivHistoryPanel(IHistoryQuery history, AncestryRegistry? ancestries = null)
    {
        _history    = history;
        _ancestries = ancestries;

        _civCombo = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        _civCombo.SelectedIndexChanged += OnCivSelected;

        _content = new VerticalStackPanel { Spacing = 2 };
        var scroll = new ScrollViewer { Content = _content, Width = 330, Height = 420 };

        var closeBtn = new TextButton { Text = "[Close]" };
        closeBtn.Click += (_, _) => Hide();

        var stack = new VerticalStackPanel { Spacing = 4, Width = 330 };
        stack.Widgets.Add(new Label { Text = "CIVILIZATION HISTORY", TextColor = Color.Gold });
        stack.Widgets.Add(_civCombo);
        stack.Widgets.Add(scroll);
        stack.Widgets.Add(closeBtn);

        var outer = new Panel { Width = 330, Visible = false };
        outer.Widgets.Add(stack);
        Root = outer;
    }

    /// <summary>Refreshes the civ list from the database and shows the panel.</summary>
    public void Show()
    {
        RefreshCivList();
        IsVisible    = true;
        Root.Visible = true;
    }

    public void Hide()
    {
        IsVisible    = false;
        Root.Visible = false;
    }

    public void ShowCiv(long civId)
    {
        Show();
        int idx = _civIds.IndexOf(civId);
        if (idx >= 0) _civCombo.SelectedIndex = idx;
        else PopulateCivContent(civId);
    }

    // ── Private ────────────────────────────────────────────────────────────────────────────────

    private void RefreshCivList()
    {
        _civIds.Clear();
        _civCombo.Items.Clear();

        var civs = _history.GetAllCivSummaries();
        foreach (var civ in civs)
        {
            string label = civ.IsCollapsed
                ? $"{civ.Name}  [collapsed {civ.CollapseYear}]"
                : $"{civ.Name}  [active]";
            _civCombo.Items.Add(new ListItem(label));
            _civIds.Add(civ.CivId);
        }

        if (_civIds.Count == 0)
        {
            _content.Widgets.Clear();
            AddLine("(No civ summaries yet — run BuildSummaries to populate)", Color.DarkGray);
        }
    }

    private void OnCivSelected(object? sender, EventArgs e)
    {
        int idx = _civCombo.SelectedIndex ?? -1;
        if (idx >= 0 && idx < _civIds.Count)
            PopulateCivContent(_civIds[idx]);
    }

    private void PopulateCivContent(long civId)
    {
        _content.Widgets.Clear();

        var summary = _history.GetCivSummary(new CivId((int)civId));
        if (summary is null)
        {
            AddLine("(No summary data available)", Color.DarkGray);
            return;
        }

        // ── Header ───────────────────────────────────────────────────────────
        AddLine(summary.Name, Color.Gold);
        string status = summary.IsCollapsed
            ? $"Founded Year {summary.FoundedYear}  |  Collapsed Year {summary.CollapseYear}"
            : $"Founded Year {summary.FoundedYear}  |  Active";
        AddLine(status, Color.LightGray);

        if (summary.DominantAncestry is not null)
        {
            AddLine($"Dominant ancestry: {summary.DominantAncestry}", Color.LightGray);

            // Cultural style from ancestry registry (M3.5)
            if (_ancestries is not null)
            {
                var anc = _ancestries.Get(summary.DominantAncestry);
                if (anc is not null)
                {
                    if (!string.IsNullOrEmpty(anc.ArchitecturalStyle))
                        AddLine($"  Cultural style: {anc.ArchitecturalStyle}  |  {anc.SettlementDescriptor}", Color.DarkGray);
                    if (anc.ArtisticTraditions.Length > 0)
                        AddLine($"  Artistic traditions: {string.Join(", ", anc.ArtisticTraditions)}", Color.DarkGray);
                }
            }
        }

        // Stats
        AddLine($"Peak settlements: {summary.PeakSettlements}  |  Rulers: {summary.TotalRulers}  |  Wars: {summary.TotalWarsInitiated + summary.TotalWarsSuffered}  |  Yrs at war: {summary.TotalYearsAtWar}", Color.LightGray);

        // ── Cultural Traits ──────────────────────────────────────────────────
        if (summary.CulturalTraits.Count > 0)
        {
            AddSeparator();
            AddLine("CULTURAL TRAITS", Color.White);
            AddLine("  " + string.Join(", ", summary.CulturalTraits), Color.Cyan);
        }

        // ── Succession ───────────────────────────────────────────────────────
        AddSeparator();
        AddLine("RULERS", Color.White);
        var rulers = _history.GetRulersOfCiv(new CivId((int)civId));
        if (rulers.Count == 0)
        {
            AddLine("  (no succession data)", Color.DarkGray);
        }
        else
        {
            foreach (var ruler in rulers)
            {
                string nameStr = ruler.NameOrdinal > 0
                    ? $"{ruler.Name} {ToRoman(ruler.NameOrdinal)}"
                    : ruler.Name;
                if (ruler.Epithet is not null) nameStr += $" the {ruler.Epithet}";
                string lifeStr = ruler.DeathYear > 0
                    ? $"  {nameStr}  ({ruler.BirthYear}–{ruler.DeathYear})"
                    : $"  {nameStr}  (b. {ruler.BirthYear})";
                AddLine(lifeStr, Color.LightGray);
            }
        }

        // ── Key Wars ─────────────────────────────────────────────────────────
        AddSeparator();
        AddLine("KEY WARS", Color.White);

        // Extract war events from civ history — WarDeclared events where this civ is involved
        var civEvents = _history.GetCivHistory(new CivId((int)civId), 0, int.MaxValue);
        var warEvents = civEvents
            .Where(e => e.Type == EventType.WarDeclared)
            .OrderByDescending(e => e.SignificanceScore)
            .Take(5)
            .ToList();

        if (warEvents.Count == 0)
        {
            AddLine("  (no wars recorded)", Color.DarkGray);
        }
        else
        {
            foreach (var war in warEvents)
            {
                string opponent = ExtractWarOpponent(war.PayloadJson, civId);
                AddLine($"  Year {war.Year} — War vs {opponent}", Color.LightGray);
            }
        }

        // ── Major Events ─────────────────────────────────────────────────────
        AddSeparator();
        AddLine("MAJOR EVENTS", Color.White);
        var headlineEvents = civEvents
            .Where(e => e.TierInvolvement == EventTier.Headline)
            .OrderBy(e => e.Year)
            .TakeLast(10)
            .ToList();

        if (headlineEvents.Count == 0)
        {
            AddLine("  (no headline events recorded)", Color.DarkGray);
        }
        else
        {
            foreach (var ev in headlineEvents)
                AddLine($"  Year {ev.Year} — {ev.TypeName}", Color.LightGray);
        }
    }

    private void AddLine(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        if (color.HasValue) label.TextColor = color.Value;
        _content.Widgets.Add(label);
    }

    private void AddSeparator() =>
        _content.Widgets.Add(new Label { Text = "─────────────────────────────", TextColor = Color.Gray });

    private static string ToRoman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
        _ => n.ToString()
    };

    private static string ExtractWarOpponent(string payloadJson, long civId)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("DeclarerCivId", out var dc) &&
                root.TryGetProperty("TargetCivId",   out var tc))
            {
                long declarerId = dc.GetInt64();
                long targetId   = tc.GetInt64();
                // Return the other side's name
                if (declarerId == civId)
                    return root.TryGetProperty("TargetCivName", out var tn) ? tn.GetString() ?? "?" : "?";
                else
                    return root.TryGetProperty("DeclarerCivName", out var dn) ? dn.GetString() ?? "?" : "?";
            }
        }
        catch { /* ignore */ }
        return "?";
    }
}
