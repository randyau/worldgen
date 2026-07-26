using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

// MAP: Top toolbar: speed buttons, year/season label.
public sealed class TimeControlsPanel
{
    public readonly HorizontalStackPanel Root;
    private readonly Label _timeLabel;
    private readonly Label _statsLabel;
    private readonly WeButton _stepBtn;

    public TimeControlsPanel(CommandQueue queue)
    {
        _timeLabel  = new Label { Text = "Year 1 — Spring" };
        _statsLabel = new Label { Text = "TPS: --  FPS: --" };
        _stepBtn    = new WeButton("▶|", () => queue.Enqueue(new StepOneTick())) { Enabled = false };

        var speeds = new (string label, SimSpeed speed)[]
        {
            ("||", SimSpeed.Paused), ("▶", SimSpeed.Slow),
            ("▶▶", SimSpeed.Normal), ("▶▶▶", SimSpeed.Fast),
            ("▶▶▶▶", SimSpeed.Ultrafast)
        };

        Root = new HorizontalStackPanel { Spacing = UiTheme.PanelSpacing };
        foreach (var (label, speed) in speeds)
        {
            var captured = speed;
            var btn = new WeButton(label, () => queue.Enqueue(new SetSimSpeed(captured)));
            Root.Widgets.Add(btn.Root);
        }
        Root.Widgets.Add(_timeLabel);
        Root.Widgets.Add(_stepBtn.Root);
        Root.Widgets.Add(_statsLabel);
    }

    public void Update(WorldSnapshot snapshot)
    {
        _timeLabel.Text = $"Year {snapshot.CurrentYear} — {snapshot.CurrentSeason}";
        _stepBtn.Enabled = snapshot.IsPaused;
        _statsLabel.Text = $"TPS: {snapshot.TicksPerSecond}";
    }
}
