using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

/// <summary>
/// God Mode panel — allows paused-only authoring actions: place artifact, trigger disaster,
/// spawn character, nudge character. Opens modal dialogs for each action.
/// </summary>
// MAP: God Mode authoring panel (M7 epics 7.2.1–7.2.3); enqueues AuthorXxx commands.
public sealed class GodModePanel : IPanel
{
    private readonly CommandQueue _commandQueue;
    private readonly VerticalStackPanel _content;

    // Context set from Game1 each frame
    private TileCoord?  _targetTileCoord;
    private EntityId?   _targetCharacterId;
    private string?     _targetCharacterName;
    private bool        _isPaused;

    /// <summary>Desktop reference required to show modal dialogs. Set by Game1 after Desktop is created.</summary>
    public Desktop? Desktop { get; set; }

    public Widget Root { get; }
    public bool IsVisible { get; private set; }

    public GodModePanel(CommandQueue commandQueue)
    {
        _commandQueue = commandQueue;
        _content = new VerticalStackPanel { Spacing = 4 };

        var scroll = new ScrollViewer { Content = _content, Width = UiTheme.ScrollWidth, Height = 220 };
        Root = PanelChrome.Wrap("GOD MODE", scroll, Hide);
        Root.Visible = false;
    }

    public void Show()   { Root.Visible = true;  IsVisible = true; }
    public void Hide()   { Root.Visible = false; IsVisible = false; }
    public void Toggle() { if (IsVisible) Hide(); else Show(); }

    /// <summary>Called each frame by Game1 to supply current context.</summary>
    public void SetContext(TileCoord? tile, EntityId? characterId, string? characterName, bool isPaused)
    {
        _targetTileCoord      = tile;
        _targetCharacterId    = characterId;
        _targetCharacterName  = characterName;
        _isPaused             = isPaused;

        if (!IsVisible) return;
        Rebuild();
    }

    // ── Private ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        _content.Widgets.Clear();

        string statusText = _isPaused ? "Paused — ready" : "Pause to use God Mode";
        Color  statusColor = _isPaused ? Color.LightGreen : Color.OrangeRed;
        _content.Widgets.Add(new Label { Text = $"Status: {statusText}", TextColor = statusColor });

        var row1 = new HorizontalStackPanel { Spacing = 4 };
        var placeBtn   = MakeButton("Place Artifact",   () => OpenPlaceArtifactDialog());
        var disasterBtn = MakeButton("Trigger Disaster", () => OpenTriggerDisasterDialog());
        row1.Widgets.Add(placeBtn);
        row1.Widgets.Add(disasterBtn);
        _content.Widgets.Add(row1);

        var row2 = new HorizontalStackPanel { Spacing = 4 };
        var spawnBtn = MakeButton("Spawn Character",  () => OpenSpawnCharacterDialog());
        var nudgeBtn = MakeButton("Nudge Character",  () => OpenNudgeCharacterDialog());
        row2.Widgets.Add(spawnBtn);
        row2.Widgets.Add(nudgeBtn);
        _content.Widgets.Add(row2);
    }

    private static TextButton MakeButton(string text, Action onClick)
    {
        var btn = new TextButton { Text = text, Width = 140 };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private bool CheckPaused()
    {
        if (_isPaused) return true;
        ShowPauseWarning();
        return false;
    }

    private void ShowPauseWarning()
    {
        // Re-render with status message already showing; nothing more needed.
    }

    private void OpenPlaceArtifactDialog()
    {
        if (!CheckPaused() || Desktop is null) return;

        var stack = new VerticalStackPanel { Spacing = 6 };
        string tileDesc = _targetTileCoord.HasValue
            ? $"({_targetTileCoord.Value.X}, {_targetTileCoord.Value.Y})"
            : "(no tile selected)";
        stack.Widgets.Add(new Label { Text = $"Target tile: {tileDesc}", TextColor = Color.LightGray });

        var combo = new ComboBox();
        foreach (var cat in Enum.GetValues<ArtifactCategory>())
            combo.Items.Add(new ListItem(cat.ToString()));
        combo.SelectedIndex = 0;
        stack.Widgets.Add(new Label { Text = "Category:", TextColor = UiTheme.MutedText });
        stack.Widgets.Add(combo);

        var nameBox = new TextBox { HintText = "Custom name (optional)", Width = 200 };
        stack.Widgets.Add(new Label { Text = "Name:", TextColor = UiTheme.MutedText });
        stack.Widgets.Add(nameBox);

        var btnRow = new HorizontalStackPanel { Spacing = 6 };
        var win = new Window { Title = "Place Artifact", Content = stack, Width = 280, Height = 220 };

        var confirm = new TextButton { Text = "Confirm" };
        confirm.Click += (_, _) =>
        {
            if (_targetTileCoord.HasValue && combo.SelectedItem is not null)
            {
                var cat  = Enum.Parse<ArtifactCategory>(combo.SelectedItem.Text ?? "Weapon");
                var name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
                _commandQueue.Enqueue(new AuthorPlaceArtifact(_targetTileCoord.Value, cat, name));
            }
            win.Close();
        };
        var cancel = new TextButton { Text = "Cancel" };
        cancel.Click += (_, _) => win.Close();
        btnRow.Widgets.Add(confirm);
        btnRow.Widgets.Add(cancel);
        stack.Widgets.Add(btnRow);

        win.ShowModal(Desktop);
    }

    private void OpenTriggerDisasterDialog()
    {
        if (!CheckPaused() || Desktop is null) return;

        var stack = new VerticalStackPanel { Spacing = 6 };
        string tileDesc = _targetTileCoord.HasValue
            ? $"({_targetTileCoord.Value.X}, {_targetTileCoord.Value.Y})"
            : "(no tile selected)";
        stack.Widgets.Add(new Label { Text = $"Target tile: {tileDesc}", TextColor = Color.LightGray });

        var combo = new ComboBox();
        foreach (var dt in Enum.GetValues<DisasterType>())
            combo.Items.Add(new ListItem(dt.ToString()));
        combo.SelectedIndex = 0;
        stack.Widgets.Add(new Label { Text = "Disaster type:", TextColor = UiTheme.MutedText });
        stack.Widgets.Add(combo);

        var btnRow = new HorizontalStackPanel { Spacing = 6 };
        var win = new Window { Title = "Trigger Disaster", Content = stack, Width = 260, Height = 180 };

        var confirm = new TextButton { Text = "Confirm" };
        confirm.Click += (_, _) =>
        {
            if (_targetTileCoord.HasValue && combo.SelectedItem is not null)
            {
                var dt = Enum.Parse<DisasterType>(combo.SelectedItem.Text ?? "Wildfire");
                _commandQueue.Enqueue(new AuthorTriggerDisaster(_targetTileCoord.Value, dt));
            }
            win.Close();
        };
        var cancel = new TextButton { Text = "Cancel" };
        cancel.Click += (_, _) => win.Close();
        btnRow.Widgets.Add(confirm);
        btnRow.Widgets.Add(cancel);
        stack.Widgets.Add(btnRow);

        win.ShowModal(Desktop);
    }

    private void OpenSpawnCharacterDialog()
    {
        if (!CheckPaused() || Desktop is null) return;

        var stack = new VerticalStackPanel { Spacing = 6 };
        string tileDesc = _targetTileCoord.HasValue
            ? $"({_targetTileCoord.Value.X}, {_targetTileCoord.Value.Y})"
            : "(no tile selected)";
        stack.Widgets.Add(new Label { Text = $"Target tile: {tileDesc}", TextColor = Color.LightGray });

        var ancestryBox = new TextBox { HintText = "ancestry id or blank", Width = 200 };
        stack.Widgets.Add(new Label { Text = "Ancestry ID (optional):", TextColor = UiTheme.MutedText });
        stack.Widgets.Add(ancestryBox);

        var btnRow = new HorizontalStackPanel { Spacing = 6 };
        var win = new Window { Title = "Spawn Character", Content = stack, Width = 260, Height = 180 };

        var confirm = new TextButton { Text = "Confirm" };
        confirm.Click += (_, _) =>
        {
            if (_targetTileCoord.HasValue)
            {
                string? ancestry = string.IsNullOrWhiteSpace(ancestryBox.Text) ? null : ancestryBox.Text.Trim();
                _commandQueue.Enqueue(new AuthorSpawnCharacter(_targetTileCoord.Value, ancestry));
            }
            win.Close();
        };
        var cancel = new TextButton { Text = "Cancel" };
        cancel.Click += (_, _) => win.Close();
        btnRow.Widgets.Add(confirm);
        btnRow.Widgets.Add(cancel);
        stack.Widgets.Add(btnRow);

        win.ShowModal(Desktop);
    }

    private void OpenNudgeCharacterDialog()
    {
        if (!CheckPaused() || Desktop is null) return;

        var stack = new VerticalStackPanel { Spacing = 6 };
        string charDesc = _targetCharacterName is not null
            ? _targetCharacterName
            : _targetCharacterId.HasValue ? $"id {_targetCharacterId.Value.Value}" : "(none)";
        stack.Widgets.Add(new Label { Text = $"Target: {charDesc}", TextColor = Color.LightGray });

        var combo = new ComboBox();
        foreach (var nudge in Enum.GetValues<CharacterNudge>())
            combo.Items.Add(new ListItem(nudge.ToString()));
        combo.SelectedIndex = 0;
        stack.Widgets.Add(new Label { Text = "Nudge:", TextColor = UiTheme.MutedText });
        stack.Widgets.Add(combo);

        var btnRow = new HorizontalStackPanel { Spacing = 6 };
        var win = new Window { Title = "Nudge Character", Content = stack, Width = 260, Height = 180 };

        var confirm = new TextButton { Text = "Confirm" };
        confirm.Click += (_, _) =>
        {
            if (_targetCharacterId.HasValue && combo.SelectedItem is not null)
            {
                var nudge = Enum.Parse<CharacterNudge>(combo.SelectedItem.Text ?? "RaiseMorale");
                _commandQueue.Enqueue(new AuthorNudgeCharacter(_targetCharacterId.Value, nudge));
            }
            win.Close();
        };
        var cancel = new TextButton { Text = "Cancel" };
        cancel.Click += (_, _) => win.Close();
        btnRow.Widgets.Add(confirm);
        btnRow.Widgets.Add(cancel);
        stack.Widgets.Add(btnRow);

        win.ShowModal(Desktop);
    }
}
