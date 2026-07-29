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

    /// <summary>Invoked when the user clicks [Watch] on a character/beast listed for this tile.</summary>
    public Action<long>? OnWatchEntity;

    private readonly Label _titleLabel;
    private readonly Label _statsLabel;
    private readonly WeButton _closeButton;
    private readonly WeVStack _entityList = new(UiTheme.Space.Xs);
    private readonly Panel _entityPanel;

    private readonly LocalCamera2D _camera = new();
    private LocalTileMapRenderer? _renderer;

    private readonly Dictionary<ChunkCoord, LocalChunk> _chunks = new();
    private readonly List<LocalEntityMarker> _markers = new();

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

        // Reuses the same PanelFrame chrome the RightDock's contextual panels use (framework
        // §4.2), so "what's on this tile" reads as the same kind of panel a player already knows
        // from the main map — not a bespoke local-view-only widget.
        _entityPanel = PanelFrame.Build("On This Tile", _entityList.Root);
        _entityPanel.HorizontalAlignment = HorizontalAlignment.Right;
        _entityPanel.VerticalAlignment   = VerticalAlignment.Top;
        _entityPanel.Width   = 260;
        _entityPanel.Visible = false;

        Root = new Panel { Visible = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Root.Widgets.Add(header);
        Root.Widgets.Add(_entityPanel);
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
        _markers.Clear();
        _entityList.Clear();
        _entityPanel.Visible = false;
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
    private const int MaxChunksPerFrame = 12;

    /// <summary>
    /// Loads every chunk that overlaps the camera's actual visible viewport, plus a
    /// ViewDistanceChunks preload margin on each side (nearest-to-viewport-center first, throttled
    /// to MaxChunksPerFrame/call). Chunks farther than the margin plus a 1-chunk hysteresis band
    /// are discarded — the margin prevents evict/reload thrashing right at the edge.
    /// </summary>
    /// <remarks>
    /// BUGFIX (2026-07-29): the original version loaded a *fixed chunk-count* radius from the
    /// center regardless of zoom/viewport size. At low zoom (zoomed out) or on a wide viewport,
    /// that fixed radius covered a screen-space area smaller than the actual visible viewport,
    /// producing a black-bordered/notched loaded region and visible "tearing" while panning (the
    /// generation queue permanently trailing the moving viewport). Deriving the chunk range
    /// directly from GetVisibleTileBounds guarantees the visible area is always the thing that's
    /// requested, at any zoom level.
    /// </remarks>
    public void Update(WorldSnapshot? snapshot, int viewportW, int viewportH)
    {
        if (_config is null) return;

        RefreshEntities(snapshot);

        int chunksPerEdge = _config.ChunksPerWorldTileEdge;
        int chunkSize     = _config.ChunkSizeTiles;
        int margin        = _config.ViewDistanceChunks;

        var (minX, minY, maxX, maxY) = _camera.GetVisibleTileBounds(viewportW, viewportH);
        int minChunkX = Math.Clamp(FloorDiv(minX, chunkSize) - margin, 0, chunksPerEdge - 1);
        int maxChunkX = Math.Clamp(FloorDiv(maxX, chunkSize) + margin, 0, chunksPerEdge - 1);
        int minChunkY = Math.Clamp(FloorDiv(minY, chunkSize) - margin, 0, chunksPerEdge - 1);
        int maxChunkY = Math.Clamp(FloorDiv(maxY, chunkSize) + margin, 0, chunksPerEdge - 1);
        float centerChunkX = (minChunkX + maxChunkX) / 2f;
        float centerChunkY = (minChunkY + maxChunkY) / 2f;

        // DECISION: local view is scoped to the single world tile it was opened on (11.7) —
        // panning past that tile's own chunk grid shows empty background rather than loading a
        // neighboring world tile's chunks. Cross-world-tile local panning is left to a future
        // milestone; see docs/phases/m11_local_scale_generation.md "Explicitly out of scope".
        List<ChunkCoord>? missing = null;
        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                var coord = new ChunkCoord(_worldTile, cx, cy);
                if (!_chunks.ContainsKey(coord))
                    (missing ??= new List<ChunkCoord>()).Add(coord);
            }
        }

        if (missing is not null)
        {
            missing.Sort((a, b) =>
                DistSq(a, centerChunkX, centerChunkY).CompareTo(DistSq(b, centerChunkX, centerChunkY)));
            for (int i = 0; i < missing.Count && i < MaxChunksPerFrame; i++)
                _chunks[missing[i]] = GenerateChunk(missing[i]);
        }

        // Hysteresis: evict only a full chunk beyond the loaded range, so a camera sitting right
        // at the margin's edge doesn't evict-then-immediately-regenerate the same chunk each frame.
        List<ChunkCoord>? toRemove = null;
        foreach (var coord in _chunks.Keys)
        {
            if (coord.ChunkX < minChunkX - 1 || coord.ChunkX > maxChunkX + 1
             || coord.ChunkY < minChunkY - 1 || coord.ChunkY > maxChunkY + 1)
                (toRemove ??= new List<ChunkCoord>()).Add(coord);
        }
        if (toRemove is not null)
            foreach (var coord in toRemove) _chunks.Remove(coord);

        _statsLabel.Text = $"{_chunks.Count} chunks · zoom {_camera.Zoom:F1} · chunk range ({minChunkX}-{maxChunkX},{minChunkY}-{maxChunkY})";
    }

    private static float DistSq(ChunkCoord c, float centerX, float centerY)
    {
        float dx = c.ChunkX - centerX, dy = c.ChunkY - centerY;
        return dx * dx + dy * dy;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    /// <summary>
    /// Rebuilds the marker list and "On This Tile" panel from whatever's currently located at
    /// _worldTile in the snapshot. Characters/beasts get a stable per-entity pseudo-position
    /// (hashed from EntityId) — see LocalEntityMarker's doc comment for why this isn't a real
    /// sub-tile position yet.
    /// </summary>
    private void RefreshEntities(WorldSnapshot? snapshot)
    {
        _markers.Clear();
        _entityList.Clear();
        if (snapshot is null || _config is null) { _entityPanel.Visible = false; return; }

        int n = _config.LocalTilesPerWorldTileEdge;
        bool any = false;

        if (snapshot.Settlements.TryGetValue(_worldTile, out var settlement))
        {
            any = true;
            _markers.Add(new LocalEntityMarker(n / 2, n / 2, EntityKind.Settlement, false));
            _entityList.Add(BuildEntityRow($"{settlement.Name} — {settlement.CivName} (pop {settlement.Population:N0})", null));
        }

        foreach (var (id, snap) in snapshot.EntitySnapshots)
        {
            if (!snap.IsAlive || snap.Location != _worldTile) continue;
            if (snap.Kind is not (EntityKind.Tier1Character or EntityKind.Tier2Character or EntityKind.LegendaryBeast)) continue;

            any = true;
            var (px, py) = PseudoLocalPosition(id.Value, n);
            _markers.Add(new LocalEntityMarker(px, py, snap.Kind, snap.IsLegendary));
            _entityList.Add(BuildEntityRow($"{snap.Name} ({snap.Kind})", id.Value));
        }

        _entityPanel.Visible = any;
    }

    private Widget BuildEntityRow(string label, long? watchId)
    {
        var row = new HorizontalStackPanel { Spacing = UiTheme.Space.Sm };
        row.Widgets.Add(new WeText(label).Root);
        if (watchId is { } id)
            row.Widgets.Add(new WeButton("[Watch]", () => OnWatchEntity?.Invoke(id)) { Padding = new Myra.Graphics2D.Thickness(2) }.Root);
        return row;
    }

    /// <summary>
    /// Stable, deterministic scatter position within [0,n) for an entity with no real sub-tile
    /// location yet — same entity always lands on the same spot within this tile, but it is not
    /// a meaningful position (V2: Tier1Character.LocalPosition once local movement lands).
    /// </summary>
    private static (int X, int Y) PseudoLocalPosition(long entityId, int n)
    {
        ulong h = unchecked((ulong)entityId * 2654435761UL);
        int x = (int)(h % (ulong)n);
        int y = (int)((h / 104729UL) % (ulong)n);
        return (x, y);
    }

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
        _renderer.DrawMarkers(sb, _markers);
    }

    public void Dispose() => _renderer?.Dispose();
}
