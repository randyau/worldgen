using System.Text.Json;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

/// <summary>
/// Myra panel showing a structured character profile card.
/// Populated entirely from IHistoryQuery — no prose generation.
/// </summary>
public sealed class CharacterProfilePanel
{
    private readonly IHistoryQuery _history;
    private readonly AncestryRegistry? _ancestries;
    private readonly VerticalStackPanel _content;

    public Widget Root { get; }
    public bool IsVisible { get; private set; }

    public CharacterProfilePanel(IHistoryQuery history, AncestryRegistry? ancestries = null)
    {
        _history    = history;
        _ancestries = ancestries;
        _content    = new VerticalStackPanel { Spacing = 2 };

        var scroll = new ScrollViewer { Content = _content, Width = 330, Height = 460 };

        var outer = new Panel { Width = 330, Visible = false };
        outer.Widgets.Add(scroll);
        Root = outer;
    }

    /// <summary>Populates the panel with data for the given character and makes it visible.</summary>
    public void ShowCharacter(long characterId)
    {
        var summary = _history.GetCharacterSummary(new EntityId(characterId));
        if (summary is null) return;

        _content.Widgets.Clear();
        IsVisible      = true;
        Root.Visible   = true;

        // ── Header ──────────────────────────────────────────────────────────
        string nameStr = summary.NameOrdinal > 0
            ? $"{summary.Name} {ToRoman(summary.NameOrdinal)}"
            : summary.Name;
        if (summary.Epithet is not null)
            nameStr += $" the {summary.Epithet}";
        AddLine(nameStr, Color.Gold);

        // Ancestry + life span
        string ancestry = summary.AncestryId ?? "Unknown";
        string life = $"{ancestry}  |  Born Year {summary.BirthYear}";
        if (summary.DeathYear > 0)
        {
            life += $"  |  Died Year {summary.DeathYear}";
            if (summary.DeathCause is not null) life += $" ({summary.DeathCause})";
        }
        else
        {
            life += "  |  Alive";
        }
        AddLine(life, Color.LightGray);

        // Cultural descriptor from ancestry (M3.5)
        if (_ancestries is not null && summary.AncestryId is not null)
        {
            var anc = _ancestries.Get(summary.AncestryId);
            if (anc is not null)
            {
                var descriptors = new List<string>();
                if (!string.IsNullOrEmpty(anc.ArchitecturalStyle))
                    descriptors.Add(anc.ArchitecturalStyle + " culture");
                if (anc.ArtisticTraditions.Length > 0)
                    descriptors.Add("traditions: " + string.Join(", ", anc.ArtisticTraditions));
                if (descriptors.Count > 0)
                    AddLine("  " + string.Join("  |  ", descriptors), Color.DarkGray);
            }
        }

        // Ruler info
        if (summary.RulerOrdinal > 0)
            AddLine($"Ruler of {summary.CivName ?? "?"} ({OrdinalLabel(summary.RulerOrdinal)} ruler)", Color.LightBlue);

        AddSeparator();

        // ── Life Events ──────────────────────────────────────────────────────
        AddLine("LIFE EVENTS", Color.White);
        var events = _history.GetCharacterHistory(new EntityId(characterId));

        // Sort by significance (desc), then year (desc) to pick top 10, then sort chrono for display
        var top10 = events
            .OrderByDescending(e => e.SignificanceScore > 0f ? (double)e.SignificanceScore : 0.0)
            .ThenByDescending(e => e.Year)
            .Take(10)
            .OrderBy(e => e.Year)
            .ToList();

        if (top10.Count == 0)
            AddLine("  (no events recorded)", Color.DarkGray);
        else
            foreach (var ev in top10)
                AddLine($"  Year {ev.Year} — {DescribeEvent(ev.Type)}", Color.LightGray);

        // ── Relationships ────────────────────────────────────────────────────
        var bonds    = events.Where(e => e.Type == EventType.GoalFormed   && IsGoalType(e.PayloadJson, "Bond")).ToList();
        var rivalries = events.Where(e => e.Type == EventType.RivalryFormed).ToList();

        if (bonds.Count > 0 || rivalries.Count > 0)
        {
            AddSeparator();
            AddLine("RELATIONSHIPS", Color.White);
            foreach (var b in bonds)
                AddLine($"  Bonded with: {ExtractGoalObject(b.PayloadJson)}", Color.LightGreen);
            foreach (var r in rivalries)
                AddLine($"  Rival: {ExtractTargetName(r.PayloadJson)}", Color.OrangeRed);
        }

        AddSeparator();

        // ── Narrative hook (V2 stub) ─────────────────────────────────────────
        var narrativeBtn = new TextButton { Text = "Generate Narrative", Enabled = false };
        // V2: LLM_PROSE_HOOK — pass summary + events to LLM prose generation service
        _content.Widgets.Add(narrativeBtn);

        // ── Close button ─────────────────────────────────────────────────────
        var closeBtn = new TextButton { Text = "[Close]" };
        closeBtn.Click += (_, _) => Hide();
        _content.Widgets.Add(closeBtn);
    }

    public void Hide()
    {
        IsVisible    = false;
        Root.Visible = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

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
        11 => "XI", 12 => "XII", 13 => "XIII", 14 => "XIV", 15 => "XV",
        _ => n.ToString()
    };

    private static string OrdinalLabel(int n)
    {
        string suffix = (n % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
        };
        return $"{n}{suffix}";
    }

    private static string DescribeEvent(EventType type) => type switch
    {
        EventType.CharacterBorn           => "Born",
        EventType.CharacterDied           => "Died",
        EventType.CharacterMarried        => "Married",
        EventType.CharacterExiled         => "Exiled",
        EventType.CharacterGrieved        => "Grieved a loss",
        EventType.CharacterFlourishing    => "Flourishing",
        EventType.CharacterSpiraling      => "Spiraling",
        EventType.WarDeclared             => "Declared war",
        EventType.WarEnded                => "War ended",
        EventType.BattleOccurred          => "Fought in battle",
        EventType.AllianceFormed          => "Formed alliance",
        EventType.AllianceBroken          => "Alliance broken",
        EventType.RivalryFormed           => "Formed rivalry",
        EventType.Negotiated              => "Negotiated",
        EventType.GoalFormed              => "Formed important goal",
        EventType.GoalResolved            => "Goal resolved",
        EventType.ArtworkCreated          => "Created artwork",
        EventType.SettlementFounded       => "Founded settlement",
        EventType.SuccessionOccurred      => "Succession / took throne",
        EventType.CivilizationFounded     => "Founded civilization",
        EventType.SettlementConquered     => "Settlement conquered",
        EventType.SuccessionCrisis        => "Succession crisis",
        EventType.AppointedToRole         => "Appointed to role",
        EventType.DismissedFromRole       => "Dismissed from role",
        EventType.MerchantTradeCompleted  => "Completed trade",
        EventType.ScholarDiscovery        => "Made discovery",
        EventType.PhysicianHealed         => "Healed someone",
        EventType.BeastSlain              => "Slew a beast",
        EventType.BeastAttackedChar       => "Attacked by beast",
        _                                 => type.ToString()
    };

    private static bool IsGoalType(string payloadJson, string goalType)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("GoalType", out var gt))
                return string.Equals(gt.GetString(), goalType, StringComparison.OrdinalIgnoreCase);
        }
        catch { /* ignore malformed JSON */ }
        return false;
    }

    private static string ExtractGoalObject(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("GoalObject", out var go))
                return go.GetString() ?? "Unknown";
        }
        catch { /* ignore */ }
        return "Unknown";
    }

    private static string ExtractTargetName(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("TargetName", out var tn))
                return tn.GetString() ?? "Unknown";
        }
        catch { /* ignore */ }
        return "Unknown";
    }
}
