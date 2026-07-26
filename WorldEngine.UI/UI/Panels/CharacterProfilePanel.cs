using System.Text.Json;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Character panel: static history-derived profile card (M8.3.2).
/// <summary>
/// Structured character profile card populated entirely from <see cref="IHistoryQuery"/> — no
/// prose generation. Shows the currently *selected* character's life summary; the live-tracked
/// *watched* character's needs/goals/spotlight controls stay on the separate Watch panel.
/// </summary>
// DECISION: the framework's illustrative 8.3.2 merges Watch+Profile into one Live/History panel.
// Kept as two panels instead — Watch tracks whichever character is *watched* (a live sim
// concept, WorldSnapshot.WatchedCharacter) while Profile shows whichever is *selected* (a UI
// concept, the bus); collapsing them changes real behavior with no way to visually verify the
// result in this environment. Both are migrated onto the kit here; the merge is deferred.
// DECISION (post-8.3 bugfix, playtest feedback): originally Contextual, which meant clicking any
// character name replaced the Tile Inspector tab — confusing since you lost the tile you were
// looking at. Moved to Summoned (Float region), same pattern as Civ History, so it layers over
// rather than replacing another panel.
public sealed class CharacterProfilePanel : IToggleablePanel
{
    private readonly IHistoryQuery _history;
    private readonly AncestryRegistry? _ancestries;
    private readonly WeVStack _content = new(UiTheme.Space.Xs);

    private long _characterId;
    private bool _hasCharacter;
    private PanelContext _ctx;

    public string Id => "character";
    public string Title => "Character";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public CharacterProfilePanel(IHistoryQuery history, AncestryRegistry? ancestries = null)
    {
        _history    = history;
        _ancestries = ancestries;
    }

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) => _ctx = ctx;

    public EmptyStateSpec? EmptyFor(PanelContext ctx) =>
        _hasCharacter ? null : new EmptyStateSpec(EmptyStateKind.PreSim, "No character selected.");

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;

    /// <summary>Selects which character's summary to show. Called by the selection bus (M8.2.1).</summary>
    public void ShowCharacter(long characterId)
    {
        _characterId  = characterId;
        _hasCharacter = true;
        Show();
    }

    public void Refresh()
    {
        _content.Clear();
        if (!_hasCharacter) { _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "No character selected.")); return; }

        var summary = _history.GetCharacterSummary(new EntityId(_characterId));
        if (summary is null) { _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "No character selected.")); return; }

        // ── Header ──────────────────────────────────────────────────────────
        string nameStr = summary.NameOrdinal > 0 ? $"{summary.Name} {_ctx.Present.ToRoman(summary.NameOrdinal)}" : summary.Name;
        if (summary.Epithet is not null) nameStr += $" the {summary.Epithet}";
        _content.Add(SectionHeader.Build(nameStr));

        string ancestry = summary.AncestryId ?? "Unknown";
        string life = $"{ancestry}  |  Born Year {summary.BirthYear}";
        life += summary.DeathYear > 0
            ? $"  |  Died Year {summary.DeathYear}" + (summary.DeathCause is not null ? $" ({summary.DeathCause})" : "")
            : "  |  Alive";
        _content.Add(new WeText(life, color: UiTheme.ColorRole.TextSecondary));

        if (_ancestries is not null && summary.AncestryId is not null
            && _ancestries.Get(summary.AncestryId) is { } anc)
        {
            var descriptors = new List<string>();
            if (!string.IsNullOrEmpty(anc.ArchitecturalStyle)) descriptors.Add(anc.ArchitecturalStyle + " culture");
            if (anc.ArtisticTraditions.Length > 0) descriptors.Add("traditions: " + string.Join(", ", anc.ArtisticTraditions));
            if (descriptors.Count > 0)
                _content.Add(new WeText("  " + string.Join("  |  ", descriptors), color: UiTheme.ColorRole.TextMuted));
        }

        if (summary.RulerOrdinal > 0)
            _content.Add(new WeText($"Ruler of {summary.CivName ?? "?"} ({OrdinalLabel(summary.RulerOrdinal)} ruler)", color: UiTheme.ColorRole.AccentInteractive));

        // ── Life Events ──────────────────────────────────────────────────────
        _content.Add(SectionHeader.Build("Life Events"));
        var events = _history.GetCharacterHistory(new EntityId(_characterId));
        var top10 = events
            .OrderByDescending(e => e.SignificanceScore > 0f ? (double)e.SignificanceScore : 0.0)
            .ThenByDescending(e => e.Year)
            .Take(10)
            .OrderBy(e => e.Year)
            .ToList();

        if (top10.Count == 0)
            _content.Add(EmptyState.Build(EmptyStateKind.PreSim, "(no events recorded)"));
        else
            foreach (var ev in top10)
                _content.Add(new WeText($"  Year {ev.Year} — {DescribeEvent(ev.Type)}", color: UiTheme.ColorRole.TextSecondary));

        // ── Relationships ────────────────────────────────────────────────────
        var bonds     = events.Where(e => e.Type == EventType.GoalFormed && IsGoalType(e.PayloadJson, "Bond")).ToList();
        var rivalries = events.Where(e => e.Type == EventType.RivalryFormed).ToList();
        if (bonds.Count > 0 || rivalries.Count > 0)
        {
            _content.Add(SectionHeader.Build("Relationships"));
            foreach (var b in bonds)
                _content.Add(new WeText($"  Bonded with: {ResolveBondTargetName(b.PayloadJson)}", color: UiTheme.ColorRole.StatePositive));
            foreach (var r in rivalries)
                _content.Add(new WeText($"  Rival: {ExtractTargetName(r.PayloadJson)}", color: UiTheme.ColorRole.StateNegative));
        }

        // ── Narrative hook (V2 stub) ─────────────────────────────────────────
        // V2: LLM_PROSE_HOOK — pass summary + events to LLM prose generation service
        _content.Add(new TextButton { Text = "Generate Narrative", Enabled = false });
    }

    // ── Helpers (unchanged from the pre-migration panel) ──────────────────────────────────────

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

    // BUG FIX: the payload's "GoalObject" field is the GoalObject *enum* value (e.g. "Person"),
    // not a name — that's why this always read "Bonded with: Person". The actual bonded
    // character's id is in "TargetId"; resolve it through the history query for a real name.
    private string ResolveBondTargetName(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("TargetId", out var t) && t.ValueKind == JsonValueKind.Number)
            {
                var summary = _history.GetCharacterSummary(new EntityId(t.GetInt64()));
                if (summary is not null) return summary.Name;
            }
        }
        catch { /* ignore malformed JSON */ }
        return "someone";
    }

    private static string ExtractTargetName(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("TargetName", out var tn)) return tn.GetString() ?? "Unknown";
        }
        catch { /* ignore */ }
        return "Unknown";
    }
}
