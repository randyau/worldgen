using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI;

public sealed class WorldGenScreen
{
    public readonly Panel Root;
    private readonly Label _layerLabel;
    private readonly HorizontalProgressBar _progressBar;

    public WorldGenScreen()
    {
        _layerLabel = new Label { Text = "Initializing..." };
        _progressBar = new HorizontalProgressBar { Width = 400, Value = 0f };

        Root = new Panel();
        var stack = new VerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing = 10
        };
        stack.Widgets.Add(new Label { Text = "Generating World..." });
        stack.Widgets.Add(_progressBar);
        stack.Widgets.Add(_layerLabel);
        Root.Widgets.Add(stack);
    }

    public void Update(string layerName, float fraction)
    {
        _layerLabel.Text = layerName;
        _progressBar.Value = fraction;
    }
}
