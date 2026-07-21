using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>A panel that can be shown or hidden under a uniform toggle model.</summary>
public interface IPanel
{
    Widget Root { get; }
    bool IsVisible { get; }
    void Show();
    void Hide();
}

/// <summary>
/// Owns the toggleable panels behind one consistent model (M6 Epic 6.1.2), replacing the
/// ad-hoc H/W key toggles. Renders a visible toggle bar (one button per panel, highlighted
/// when open) and remembers open/closed state across world resets. Keybinds and buttons both
/// call <see cref="Toggle"/>, so the two stay in lock-step.
/// </summary>
// MAP: Unifies show/hide of toggleable panels; visible toggle bar + remembered open state.
public sealed class PanelManager
{
    private readonly Dictionary<string, IPanel> _panels = new();
    private readonly Dictionary<string, TextButton> _buttons = new();
    private readonly HashSet<string> _openState = new();   // survives ResetRegistrations

    /// <summary>The visible toggle bar; add this to the widget tree once (it persists across resets).</summary>
    public HorizontalStackPanel ToggleBar { get; } = new() { Spacing = UiTheme.PanelSpacing };

    /// <summary>
    /// Registers a panel and adds its toggle button. Restores the panel's remembered
    /// open/closed state. Call once per panel after (re)creating it in StartSim.
    /// </summary>
    public void Register(string id, string label, IPanel panel)
    {
        _panels[id] = panel;

        var btn = new TextButton { Text = label };
        btn.Click += (_, _) => Toggle(id);
        _buttons[id] = btn;
        ToggleBar.Widgets.Add(btn);

        if (_openState.Contains(id)) panel.Show(); else panel.Hide();
        RefreshButton(id);
    }

    public void Toggle(string id)
    {
        if (!_panels.TryGetValue(id, out var p)) return;
        if (p.IsVisible) p.Hide(); else p.Show();
        Sync();
    }

    public void Show(string id) { if (_panels.TryGetValue(id, out var p)) { p.Show(); Sync(); } }
    public void Hide(string id) { if (_panels.TryGetValue(id, out var p)) { p.Hide(); Sync(); } }
    public bool IsOpen(string id) => _panels.TryGetValue(id, out var p) && p.IsVisible;

    /// <summary>
    /// Reconciles remembered state and button visuals with the panels' actual visibility.
    /// Call each snapshot so a panel that hid itself via its own [Close] button stays in sync.
    /// </summary>
    public void Sync()
    {
        foreach (var (id, p) in _panels)
        {
            if (p.IsVisible) _openState.Add(id); else _openState.Remove(id);
            RefreshButton(id);
        }
    }

    /// <summary>
    /// Drops registrations and toggle-bar buttons for a world reset, preserving remembered
    /// open/closed state so the next world restores the same panels.
    /// </summary>
    public void ResetRegistrations()
    {
        Sync();   // capture current state into _openState before dropping panels
        _panels.Clear();
        _buttons.Clear();
        ToggleBar.Widgets.Clear();
    }

    private void RefreshButton(string id)
    {
        if (_buttons.TryGetValue(id, out var btn))
            btn.TextColor = IsOpen(id) ? UiTheme.Accent : UiTheme.BodyText;
    }
}
