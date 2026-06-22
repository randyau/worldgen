using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

public sealed class TimeControlsPanel
{
    public readonly HorizontalStackPanel Root;
    private readonly Label _timeLabel;
    private readonly Label _statsLabel;
    private readonly TextButton _stepBtn;

    public TimeControlsPanel(CommandQueue queue)
    {
        _timeLabel  = new Label { Text = "Year 1 — Spring" };
        _statsLabel = new Label { Text = "TPS: --  FPS: --" };
        _stepBtn    = new TextButton { Text = "▶|", Enabled = false };

        _stepBtn.Click += (_, _) => queue.Enqueue(new StepOneTick());

        var speeds = new (string label, SimSpeed speed)[]
        {
            ("||", SimSpeed.Paused), ("▶", SimSpeed.Slow),
            ("▶▶", SimSpeed.Normal), ("▶▶▶", SimSpeed.Fast),
            ("▶▶▶▶", SimSpeed.Ultrafast)
        };

        Root = new HorizontalStackPanel { Spacing = 6 };
        foreach (var (label, speed) in speeds)
        {
            var btn = new TextButton { Text = label };
            var captured = speed;
            btn.Click += (_, _) => queue.Enqueue(new SetSimSpeed(captured));
            Root.Widgets.Add(btn);
        }
        Root.Widgets.Add(_timeLabel);
        Root.Widgets.Add(_stepBtn);
        Root.Widgets.Add(_statsLabel);
    }

    public void Update(WorldSnapshot snapshot)
    {
        _timeLabel.Text = $"Year {snapshot.CurrentYear} — {snapshot.CurrentSeason}";
        _stepBtn.Enabled = snapshot.IsPaused;
        _statsLabel.Text = $"TPS: {snapshot.TicksPerSecond}";
    }
}
