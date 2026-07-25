using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

// MAP: Top-bar menu row — summons the major panels + shows Spotlight status, off the right dock.
/// <summary>
/// Row of buttons that toggle the Summoned panels (Watch, Character, Civ History, God Mode,
/// Settings, Help) directly from the top bar, highlighting whichever are open, plus a Spotlight
/// status/exit indicator. Moves primary panel access off the fixed right dock (playtest feedback:
/// "that way we move away from everything being locked to the fixed right panel").
/// </summary>
public sealed class PanelMenuBar
{
    public readonly Widget Root;
    private readonly SimWorkspace _workspace;
    private readonly Dictionary<string, TextButton> _buttons = new();
    private readonly Label _spotlightLabel;
    private readonly TextButton _exitSpotlightBtn;

    public PanelMenuBar(SimWorkspace workspace, CommandQueue queue)
    {
        _workspace = workspace;

        var row = new HorizontalStackPanel { Spacing = UiTheme.PanelSpacing };

        AddToggle(row, "watch",     "Watch");
        AddToggle(row, "character", "Character");
        AddToggle(row, "civ",       "Civ History");
        AddToggle(row, "godmode",   "God Mode");
        AddToggle(row, "settings",  "Settings");
        AddToggle(row, "help",      "Help (?)");

        _spotlightLabel = new Label { Text = "", TextColor = UiTheme.AccentSpotlight, Visible = false };
        _exitSpotlightBtn = new TextButton { Text = "[Exit Spotlight]", Visible = false };
        _exitSpotlightBtn.Click += (_, _) => queue.Enqueue(new ExitSpotlight());
        row.Widgets.Add(_spotlightLabel);
        row.Widgets.Add(_exitSpotlightBtn);

        Root = row;
    }

    private void AddToggle(HorizontalStackPanel row, string id, string label)
    {
        var btn = new TextButton { Text = label };
        btn.Click += (_, _) => _workspace.ToggleSummoned(id);
        _buttons[id] = btn;
        row.Widgets.Add(btn);
    }

    /// <summary>
    /// Refreshes which buttons are highlighted open/closed. UI interaction state, not sim data —
    /// call every render frame regardless of sim tick cadence (a click must highlight instantly,
    /// including while paused; see the SimWorkspace.SyncVisibility bug note for the same class of
    /// issue this mirrors).
    /// </summary>
    public void RefreshHighlights()
    {
        foreach (var (id, btn) in _buttons)
            btn.TextColor = _workspace.IsSummonedVisible(id) ? UiTheme.Accent : UiTheme.BodyText;
    }

    /// <summary>Reflects Spotlight state — genuine sim data, fine to update only on a fresh snapshot.</summary>
    public void UpdateSpotlightStatus(string? spotlightCharacterName)
    {
        bool spotlightActive = spotlightCharacterName is not null;
        _spotlightLabel.Visible   = spotlightActive;
        _exitSpotlightBtn.Visible = spotlightActive;
        if (spotlightActive)
            _spotlightLabel.Text = $"Spotlight: {spotlightCharacterName}";
    }
}
