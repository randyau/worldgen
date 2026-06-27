using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

/// <summary>
/// Read-only live panel tracking a single named character's current state.
/// Updated each tick from WorldSnapshot.WatchedCharacter.
/// Precursor to M4 Spotlight — everything read-only, no sim commands except WatchCharacter.
/// </summary>
public sealed class CharacterWatchPanel
{
    private readonly VerticalStackPanel _content;

    public Widget Root { get; }
    public bool IsVisible { get; private set; }

    // Consume-once flag set when "Full Profile" is clicked
    // (reserved for future M3.3 integration — currently a no-op stub)
    private long _pendingProfileCharacterId;
    public long ConsumePendingProfile()
    {
        var id = _pendingProfileCharacterId;
        _pendingProfileCharacterId = 0;
        return id;
    }

    public CharacterWatchPanel()
    {
        _content = new VerticalStackPanel { Spacing = 2 };

        var scroll = new ScrollViewer { Content = _content, Width = 300, Height = 420 };

        var outer = new Panel { Width = 300, Visible = false };
        outer.Widgets.Add(scroll);
        Root = outer;
    }

    /// <summary>Makes the panel visible (called when the player first hits W or clicks Watch).</summary>
    public void Show() { Root.Visible = true; IsVisible = true; }

    /// <summary>Hides the panel.</summary>
    public void Hide() { Root.Visible = false; IsVisible = false; }

    /// <summary>
    /// Refreshes displayed data from the snapshot. Called each frame when IsVisible.
    /// Does nothing if no character is being watched.
    /// </summary>
    public void Refresh(WorldSnapshot snapshot)
    {
        if (!IsVisible) return;
        var watch = snapshot.WatchedCharacter;
        if (watch is null) { _content.Widgets.Clear(); return; }

        _content.Widgets.Clear();

        // ── Header ──────────────────────────────────────────────────────────
        string epithet = watch.Epithet.Length > 0 ? $" the {watch.Epithet}" : "";
        AddLine($"{watch.Name}{epithet}", Color.Gold);
        AddLine($"Civ: {watch.CivName}  |  Age: {watch.AgeSeasons}s  ({watch.AgeSeasons / 4} yrs)", Color.LightGray);
        AddLine($"Location: ({watch.Location.X}, {watch.Location.Y}) — {watch.BiomeName}", Color.LightGray);

        AddSeparator();

        // ── Wellbeing ────────────────────────────────────────────────────────
        string wbLabel = watch.Wellbeing switch
        {
            >= 0.7f  => "Flourishing",
            >= 0.3f  => "Content",
            >= -0.3f => "Neutral",
            >= -0.7f => "Distressed",
            _        => "Spiraling"
        };
        var wbColor = watch.Wellbeing >= 0.3f ? Color.LightGreen
                    : watch.Wellbeing >= -0.3f ? Color.LightGray
                    : Color.OrangeRed;
        AddLine($"Wellbeing: {wbLabel} ({watch.Wellbeing:+0.00;-0.00;0.00})", wbColor);

        AddSeparator();

        // ── Needs (live) ─────────────────────────────────────────────────────
        AddLine("NEEDS", Color.White);
        var n = watch.Needs;
        AddNeedBar("Food",      n.Food);
        AddNeedBar("Safety",    n.Safety);
        AddNeedBar("Shelter",   n.Shelter);
        AddNeedBar("Belonging", n.Belonging);
        AddNeedBar("Status",    n.Status);
        AddNeedBar("Purpose",   n.Purpose);
        AddNeedBar("Spiritual", n.Spiritual);

        AddSeparator();

        // ── Active Goals ─────────────────────────────────────────────────────
        AddLine("ACTIVE GOALS", Color.White);
        if (watch.Goals.Count == 0)
        {
            AddLine("  (none)", Color.LightGray);
        }
        else
        {
            foreach (var g in watch.Goals)
                AddLine($"  {g.Description,-20} (priority {g.Priority:F2})", Color.LightGray);
        }

        AddSeparator();

        // ── Personality ──────────────────────────────────────────────────────
        AddLine("PERSONALITY", Color.White);
        var pers = watch.Personality;
        AddLine($"  Ambition   {PersTick(pers.Ambition)}  Compassion {PersTick(pers.Compassion)}", Color.LightGray);
        AddLine($"  Curiosity  {PersTick(pers.Curiosity)}  Creativity {PersTick(pers.Creativity)}", Color.LightGray);
        AddLine($"  Loyalty    {PersTick(pers.Loyalty)}  Aggression {PersTick(pers.Aggression)}", Color.LightGray);

        AddSeparator();

        // ── Full Profile link (stub — connects to CharacterProfilePanel in M3.3+) ──
        long capturedId = watch.Id.Value;
        var profileBtn = new TextButton
        {
            Text    = "[Full Profile]",
            Padding = new Myra.Graphics2D.Thickness(4)
        };
        profileBtn.Click += (_, _) => _pendingProfileCharacterId = capturedId;
        _content.Widgets.Add(profileBtn);

        // Close button
        var closeBtn = new TextButton
        {
            Text    = "[Close]",
            Padding = new Myra.Graphics2D.Thickness(4)
        };
        closeBtn.Click += (_, _) => Hide();
        _content.Widgets.Add(closeBtn);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NeedBar(float v)
    {
        int filled = (int)(v * 10);
        return $"[{new string('#', filled)}{new string('.', 10 - filled)}] {v:F2}";
    }

    private static string PersTick(float v)
    {
        // Convert 0-1 to 5-char block bar
        int n = (int)(v * 5);
        return $"[{new string('#', n)}{new string('.', 5 - n)}]";
    }

    private void AddNeedBar(string label, float value)
    {
        string lowTag = value < 0.25f ? " !" : "";
        var color = value < 0.25f ? Color.OrangeRed : Color.LightGray;
        AddLine($"  {label,-10} {NeedBar(value)}{lowTag}", color);
    }

    private void AddLine(string text, Color? color = null)
    {
        var lbl = new Label { Text = text };
        if (color.HasValue) lbl.TextColor = color.Value;
        _content.Widgets.Add(lbl);
    }

    private void AddSeparator() =>
        _content.Widgets.Add(new Label { Text = new string('-', 36) });
}
