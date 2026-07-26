using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

// MAP: Full-screen world-gen progress overlay with completion state and "Start Simulation" button.
public sealed class WorldGenScreen
{
    public readonly Panel Root;
    private readonly Label _layerLabel;
    private readonly HorizontalProgressBar _progressBar;
    private readonly Panel _completePanel;

    private bool _complete;
    private bool _pendingStart;

    public WorldGenScreen()
    {
        var headerLabel  = new Label { Text = "Generating World...", TextColor = UiTheme.HeaderText };
        _layerLabel      = new Label { Text = "Initializing..." };
        _progressBar     = new HorizontalProgressBar { Width = 400, Value = 0f };

        // Completion panel (initially hidden)
        var readyLabel = new Label { Text = "World ready!", TextColor = UiTheme.HeaderText };
        var startBtn   = new WeButton("▶  Start Simulation", () => _pendingStart = true);

        var completeStack = new VerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing = 10
        };
        completeStack.Widgets.Add(readyLabel);
        completeStack.Widgets.Add(startBtn.Root);

        _completePanel = new Panel { Visible = false };
        _completePanel.Widgets.Add(completeStack);

        // Main progress stack
        var stack = new VerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing = 10
        };
        stack.Widgets.Add(headerLabel);
        stack.Widgets.Add(_progressBar);
        stack.Widgets.Add(_layerLabel);

        Root = new Panel();
        Root.Widgets.Add(stack);
        Root.Widgets.Add(_completePanel);
    }

    public void Update(string layerName, float fraction)
    {
        _layerLabel.Text   = $"{layerName}  {fraction:P0}";
        _progressBar.Value = fraction;
    }

    /// <summary>
    /// Transitions to the completion state (idempotent). Hides progress, shows "Start" button.
    /// </summary>
    public void ShowComplete()
    {
        if (_complete) return;
        _complete = true;
        foreach (var widget in Root.Widgets.OfType<VerticalStackPanel>())
            widget.Visible = false;
        _completePanel.Visible = true;
    }

    /// <summary>
    /// Returns true once when the user clicks "▶  Start Simulation", then resets.
    /// Call from Game1.Update each frame.
    /// </summary>
    public bool ConsumePendingStart()
    {
        var val = _pendingStart;
        _pendingStart = false;
        return val;
    }
}
