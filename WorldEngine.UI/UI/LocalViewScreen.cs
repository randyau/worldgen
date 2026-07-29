using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.UI.Rendering;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI;

// MAP: M11 11.7 — local-view screen: "[View Local]" on TileInspectorPanel opens this full-screen
// view of one world tile at 10m resolution, chunk-loaded lazily around a pannable/zoomable
// camera, with the persisted delta overlay (11.5) applied on top of freshly-regenerated base
// terrain (11.3/11.4). Mirrors WorldGenPreviewScreen's shape: a Root Panel toggled visible/hidden
// alongside MainUI, owned as a single Game1 instance.
public sealed class LocalViewScreen : IDisposable
{
    public readonly Panel Root;

    /// <summary>Invoked when the user closes the local view (Back button or Escape).</summary>
    public Action? OnClose;

    private readonly Label _titleLabel;
    private readonly Label _statsLabel;
    private readonly WeButton _closeButton;

    private readonly LocalCamera2D _camera = new();
    private LocalTileMapRenderer? _renderer;

    private readonly Dictionary<ChunkCoord, LocalChunk> _chunks = new();

    private TileCoord _worldTile;
    private TileData _parentTile;
    private BorderManifest? _manifest;
    private int _worldSeed;
    private LocalGenConfig? _config;
    private EventStore? _eventStore;

    public bool IsVisible => Root.Visible;

    public LocalViewScreen()
    {
        _titleLabel = new Label { Text = "Local View", TextColor = UiTheme.HeaderText };
        var hint = new Label
        {
            Text      = "Right-drag to pan  ·  Scroll to zoom  ·  [Esc] Back to World Map",
            TextColor = UiTheme.TextSecondary
        };
        _closeButton = new WeButton("[Back to World Map]", () => OnClose?.Invoke(), WeButtonVariant.Ghost);
        _statsLabel = new Label { TextColor = UiTheme.TextSecondary };

        var header = new HorizontalStackPanel
        {
            Spacing            = UiTheme.Space.Md,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Top,
            Padding            = new Myra.Graphics2D.Thickness(UiTheme.Space.Md)
        };
        header.Widgets.Add(_titleLabel);
        header.Widgets.Add(hint);
        header.Widgets.Add(_statsLabel);
        header.Widgets.Add(_closeButton.Root);

        Root = new Panel { Visible = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Root.Widgets.Add(header);
    }

    /// <summary>Call once after GraphicsDevice is available.</summary>
    public void Initialize(GraphicsDevice gd) => _renderer = new LocalTileMapRenderer(gd, _camera);

    /// <summary>
    /// Opens the local view for one world tile. <paramref name="manifest"/> is null when no
    /// border manifest is available (e.g. a save from before 11.1/11.7) — falls back to
    /// LocalTileGenerator.GenerateFlat so the feature degrades instead of failing outright.
    /// </summary>
    public void Show(
        TileCoord worldTile, TileData parentTile, BorderManifest? manifest,
        int worldSeed, LocalGenConfig config, EventStore eventStore,
        int viewportW, int viewportH)
    {
        _worldTile  = worldTile;
        _parentTile = parentTile;
        _manifest   = manifest;
        _worldSeed  = worldSeed;
        _config     = config;
        _eventStore = eventStore;

        _chunks.Clear();
        _titleLabel.Text = manifest is null
            ? $"Local View — Tile ({worldTile.X}, {worldTile.Y})  (no border data — flat placeholder terrain)"
            : $"Local View — Tile ({worldTile.X}, {worldTile.Y})";

        int n = config.LocalTilesPerWorldTileEdge;
        _camera.CenterOn(n / 2f, n / 2f, viewportW, viewportH);

        Root.Visible = true;
    }

    public void Hide() => Root.Visible = false;

    public void Pan(Vector2 delta) => _camera.Pan(delta);
    public void ZoomAt(Vector2 screenPoint, float factor) => _camera.ZoomAt(screenPoint, factor);

    /// <summary>Chunks generated per Update() call — bounds a single frame's worst-case cost so opening/panning the view doesn't stall a frame.</summary>
    private const int MaxChunksPerFrame = 6;

    /// <summary>
    /// Loads chunks within ViewDistanceChunks of the camera's current center (nearest first,
    /// throttled to MaxChunksPerFrame/call), discarding chunks now outside that radius plus a
    /// 1-chunk hysteresis margin so a camera oscillating right at the boundary doesn't
    /// evict-then-immediately-reload the same chunk every frame. Call once per frame while visible.
    /// </summary>
    public void Update(int viewportW, int viewportH)
    {
        if (_config is null) return;

        int chunksPerEdge = _config.ChunksPerWorldTileEdge;
        var (minX, minY, maxX, maxY) = _camera.GetVisibleTileBounds(viewportW, viewportH);
        int centerChunkX = Math.Clamp(((minX + maxX) / 2) / _config.ChunkSizeTiles, 0, chunksPerEdge - 1);
        int centerChunkY = Math.Clamp(((minY + maxY) / 2) / _config.ChunkSizeTiles, 0, chunksPerEdge - 1);
        int viewDist = _config.ViewDistanceChunks;

        // DECISION: local view is scoped to the single world tile it was opened on (11.7) —
        // panning past that tile's own chunk grid shows empty background rather than loading a
        // neighboring world tile's chunks. Cross-world-tile local panning is left to a future
        // milestone; see docs/phases/m11_local_scale_generation.md "Explicitly out of scope".
        List<ChunkCoord>? missing = null;
        for (int cy = centerChunkY - viewDist; cy <= centerChunkY + viewDist; cy++)
        {
            if (cy < 0 || cy >= chunksPerEdge) continue;
            for (int cx = centerChunkX - viewDist; cx <= centerChunkX + viewDist; cx++)
            {
                if (cx < 0 || cx >= chunksPerEdge) continue;
                var coord = new ChunkCoord(_worldTile, cx, cy);
                if (!_chunks.ContainsKey(coord))
                    (missing ??= new List<ChunkCoord>()).Add(coord);
            }
        }

        if (missing is not null)
        {
            missing.Sort((a, b) =>
                Chebyshev(a, centerChunkX, centerChunkY).CompareTo(Chebyshev(b, centerChunkX, centerChunkY)));
            for (int i = 0; i < missing.Count && i < MaxChunksPerFrame; i++)
                _chunks[missing[i]] = GenerateChunk(missing[i]);
        }

        int evictDist = viewDist + 1; // hysteresis margin — avoids evict/reload thrashing at the boundary
        List<ChunkCoord>? toRemove = null;
        foreach (var coord in _chunks.Keys)
        {
            if (Math.Abs(coord.ChunkX - centerChunkX) > evictDist || Math.Abs(coord.ChunkY - centerChunkY) > evictDist)
                (toRemove ??= new List<ChunkCoord>()).Add(coord);
        }
        if (toRemove is not null)
            foreach (var coord in toRemove) _chunks.Remove(coord);

        _statsLabel.Text = $"{_chunks.Count} chunks · zoom {_camera.Zoom:F1} · center chunk ({centerChunkX},{centerChunkY})";
    }

    private static int Chebyshev(ChunkCoord c, int centerX, int centerY) =>
        Math.Max(Math.Abs(c.ChunkX - centerX), Math.Abs(c.ChunkY - centerY));

    private LocalChunk GenerateChunk(ChunkCoord coord)
    {
        LocalChunk chunk;
        if (_manifest is not null)
        {
            chunk = LocalTerrainAmplifier.Amplify(coord, _parentTile, _manifest, _worldSeed, _config!);
            LocalRiverThreader.Thread(chunk, coord, _parentTile, _manifest, _config!);
        }
        else
        {
            chunk = LocalTileGenerator.GenerateFlat(coord, _parentTile, _config!);
        }

        LocalDecorationGenerator.Generate(chunk, coord, (BiomeType)_parentTile.BiomeType, _worldSeed, _config!);

        var deltas = _eventStore?.LoadLocalTileDeltas(coord);
        if (deltas is { Count: > 0 })
            LocalTileDeltaApplier.Apply(chunk, deltas);

        return chunk;
    }

    public void Draw(SpriteBatch sb, int viewportW, int viewportH)
    {
        if (_config is null || _renderer is null) return;
        _renderer.Draw(sb, _chunks, _config.ChunkSizeTiles, viewportW, viewportH);
    }

    public void Dispose() => _renderer?.Dispose();
}
