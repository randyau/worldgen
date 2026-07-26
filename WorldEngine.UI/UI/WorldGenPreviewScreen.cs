using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.UI.Rendering;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

// MAP: M10 10.1 — worldgen preview screen: per-layer thumbnails (WorldGenPreviewRenderer),
// sea-level parameter field, "rerun from layer" (WorldGenPipeline.RerunFromAsync), Commit.
public sealed class WorldGenPreviewScreen : IDisposable
{
    private static readonly int LayerCount = WorldGenPipeline.LayerCount;
    private const int ThumbWidth = 96;
    private const int ThumbHeight = 72;

    public readonly Panel Root;

    private readonly WorldGenPipeline _pipeline = new();

    // Progress state (shown while the initial full run or a rerun is in flight)
    private readonly Panel _genPanel;
    private readonly Label _genStatusLabel;
    private readonly HorizontalProgressBar _genProgressBar;

    // Preview state (shown once the initial run has produced every layer)
    private readonly Panel _previewPanel;
    private readonly Label[] _statusLabels = new Label[LayerCount];
    private readonly Panel[] _thumbSlots = new Panel[LayerCount];
    private readonly Texture2D?[] _thumbTextures = new Texture2D?[LayerCount];
    private readonly bool[] _layerDone = new bool[LayerCount];
    private readonly WeField _seaLevelField;
    private readonly WeDropdown<int> _rerunLayerDropdown;
    private readonly WeButton _rerunButton;
    private readonly WeButton _commitButton;
    private readonly Label _rerunStatusLabel;

    private GraphicsDevice? _gd;
    private WorldGenContext? _ctx;
    private Task<WorldGenContext>? _genTask;
    private Task<WorldGenContext>? _rerunTask;
    private int _rerunFromIndex;
    private bool _busy;
    private WorldState? _pendingCommit;

    public WorldGenPreviewScreen()
    {
        _genStatusLabel = new Label { Text = "Initializing...", TextColor = UiTheme.TextSecondary };
        _genProgressBar = new HorizontalProgressBar { Width = 400, Value = 0f };
        var genStack = new VerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing = 10
        };
        genStack.Widgets.Add(new Label { Text = "Generating World...", TextColor = UiTheme.HeaderText });
        genStack.Widgets.Add(_genProgressBar);
        genStack.Widgets.Add(_genStatusLabel);
        _genPanel = new Panel();
        _genPanel.Widgets.Add(genStack);

        var layerList = new VerticalStackPanel { Spacing = UiTheme.Space.Sm };
        for (int i = 0; i < LayerCount; i++)
        {
            var row = new HorizontalStackPanel { Spacing = UiTheme.Space.Sm };

            var thumb = new Panel
            {
                Width      = ThumbWidth,
                Height     = ThumbHeight,
                Background = new Myra.Graphics2D.Brushes.SolidBrush(UiTheme.SurfaceRaised)
            };
            _thumbSlots[i] = thumb;
            row.Widgets.Add(thumb);

            row.Widgets.Add(new Label { Text = WorldGenPipeline.LayerNames[i], TextColor = UiTheme.TextPrimary, Width = 90 });

            var status = new Label { Text = "Pending", TextColor = UiTheme.TextSecondary };
            _statusLabels[i] = status;
            row.Widgets.Add(status);

            layerList.Widgets.Add(row);
        }

        _seaLevelField = new WeField("Sea level (0-1)");

        _rerunLayerDropdown = new WeDropdown<int>();
        _rerunLayerDropdown.Render(i => WorldGenPipeline.LayerNames[i]);
        _rerunLayerDropdown.SetItems(Enumerable.Range(0, LayerCount));
        _rerunLayerDropdown.Selected = 2; // Ocean — the parameter field above governs it

        _rerunButton = new WeButton("Rerun from layer", OnRerunClicked) { Enabled = false };
        _commitButton = new WeButton("▶  Commit", OnCommitClicked, WeButtonVariant.Primary) { Enabled = false };
        _rerunStatusLabel = new Label { Text = "", TextColor = UiTheme.TextSecondary };

        var controlsRow = new HorizontalStackPanel { Spacing = UiTheme.Space.Md };
        controlsRow.Widgets.Add(_seaLevelField.Root);
        controlsRow.Widgets.Add(_rerunLayerDropdown.Root);
        controlsRow.Widgets.Add(_rerunButton.Root);

        var previewStack = new VerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing = UiTheme.Space.Md
        };
        previewStack.Widgets.Add(new Label { Text = "World Preview", TextColor = UiTheme.HeaderText });
        previewStack.Widgets.Add(layerList);
        previewStack.Widgets.Add(controlsRow);
        previewStack.Widgets.Add(_rerunStatusLabel);
        previewStack.Widgets.Add(_commitButton.Root);

        _previewPanel = new Panel { Visible = false };
        _previewPanel.Widgets.Add(previewStack);

        Root = new Panel();
        Root.Widgets.Add(_genPanel);
        Root.Widgets.Add(_previewPanel);
    }

    /// <summary>Call once after GraphicsDevice is available.</summary>
    public void Initialize(GraphicsDevice gd) => _gd = gd;

    /// <summary>Shows the loading/progress panel with a fixed message (e.g. "Loading save...").</summary>
    public void ShowMessage(string text)
    {
        Root.Visible = true;
        _genPanel.Visible = true;
        _previewPanel.Visible = false;
        _genStatusLabel.Text = text;
    }

    /// <summary>Starts a fresh full pipeline run for a new world and shows the progress panel.</summary>
    public void BeginGeneration(WorldConfig config, SimConfig simConfig)
    {
        _ctx = null;
        _genTask = null;
        _rerunTask = null;
        _pendingCommit = null;
        _busy = true;

        for (int i = 0; i < LayerCount; i++)
        {
            _layerDone[i] = false;
            _thumbTextures[i]?.Dispose();
            _thumbTextures[i] = null;
        }
        RefreshLayerStatusLabels();

        _seaLevelField.Value = simConfig.WorldGen.Ocean.DefaultSeaLevel.ToString("0.###");
        _rerunButton.Enabled = false;
        _commitButton.Enabled = false;

        Root.Visible = true;
        _genPanel.Visible = true;
        _previewPanel.Visible = false;
        _genStatusLabel.Text = "Initializing...";
        _genProgressBar.Value = 0f;

        var progress = new Progress<(string Layer, float Fraction)>(p =>
        {
            _genStatusLabel.Text = $"{p.Layer}  {p.Fraction:P0}";
            _genProgressBar.Value = p.Fraction;
        });
        _genTask = _pipeline.RunUpToAsync(config, simConfig, LayerCount - 1, progress);
    }

    /// <summary>
    /// Drains completed background tasks. Call once per frame before reading widget state.
    /// Returns the committed WorldState the one frame the player clicks Commit, else null.
    /// </summary>
    public WorldState? Update()
    {
        if (_genTask is { IsCompletedSuccessfully: true })
        {
            _ctx = _genTask.Result;
            _genTask = null;
            _busy = false;

            for (int i = 0; i < LayerCount; i++) _layerDone[i] = true;
            RebuildThumbnails(0, LayerCount - 1);
            RefreshLayerStatusLabels();

            _genPanel.Visible = false;
            _previewPanel.Visible = true;
            _rerunButton.Enabled = true;
            _commitButton.Enabled = true;
        }

        if (_rerunTask is { IsCompletedSuccessfully: true })
        {
            _ctx = _rerunTask.Result;
            _rerunTask = null;
            _busy = false;

            for (int i = _rerunFromIndex; i < LayerCount; i++) _layerDone[i] = true;
            RebuildThumbnails(_rerunFromIndex, LayerCount - 1);
            RefreshLayerStatusLabels();

            _rerunStatusLabel.Text = "Rerun complete.";
            _rerunButton.Enabled = true;
            _commitButton.Enabled = true;
        }

        if (_pendingCommit is { } world)
        {
            _pendingCommit = null;
            return world;
        }
        return null;
    }

    /// <summary>Draws the layer thumbnails. Call between SpriteBatch Begin/End, after Myra layout has run.</summary>
    public void Draw(SpriteBatch sb)
    {
        if (_gd is null || !_previewPanel.Visible) return;

        for (int i = 0; i < LayerCount; i++)
        {
            if (_thumbTextures[i] is not { } tex) continue;
            sb.Draw(tex, _thumbSlots[i].Bounds, Color.White);
        }
    }

    private void OnRerunClicked()
    {
        if (_busy || _ctx is null || _gd is null) return;

        int layerIndex = _rerunLayerDropdown.Selected;

        if (float.TryParse(_seaLevelField.Value, out float seaLevel) && seaLevel is > 0f and < 1f)
            _ctx.SimConfig.WorldGen.Ocean.DefaultSeaLevel = seaLevel;

        for (int i = layerIndex; i < LayerCount; i++) _layerDone[i] = false;
        RefreshLayerStatusLabels();

        _rerunFromIndex = layerIndex;
        _busy = true;
        _rerunButton.Enabled = false;
        _commitButton.Enabled = false;
        _rerunStatusLabel.Text = "Rerunning...";

        var progress = new Progress<(string Layer, float Fraction)>(p =>
            _rerunStatusLabel.Text = $"Rerunning: {p.Layer}  {p.Fraction:P0}");
        _rerunTask = _pipeline.RerunFromAsync(_ctx, layerIndex, progress);
    }

    private void OnCommitClicked()
    {
        if (_busy || _ctx is null) return;
        _pendingCommit = TileGridAssembler.Assemble(_ctx);
    }

    private void RebuildThumbnails(int fromIndex, int toIndex)
    {
        if (_gd is null || _ctx is null) return;

        for (int i = fromIndex; i <= toIndex; i++)
        {
            var colors = WorldGenPreviewRenderer.BuildLayerColors(_ctx, i);
            if (colors is null) continue;

            _thumbTextures[i]?.Dispose();
            var tex = new Texture2D(_gd, _ctx.TileWidth, _ctx.TileHeight);
            tex.SetData(colors);
            _thumbTextures[i] = tex;
        }
    }

    private void RefreshLayerStatusLabels()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            bool done = _layerDone[i];
            _statusLabels[i].Text = done ? "Done" : "Pending";
            _statusLabels[i].TextColor = done ? UiTheme.StatePositive : UiTheme.TextSecondary;
        }
    }

    public void Dispose()
    {
        foreach (var tex in _thumbTextures)
            tex?.Dispose();
    }
}
