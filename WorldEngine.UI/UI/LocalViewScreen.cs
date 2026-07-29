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

// MAP: M11 11.7 — local-view: [View Local] on TileInspectorPanel swaps the MapCanvas region's
// content (only) to a pannable/zoomable 10m-resolution render of one world tile, chunk-loaded
// lazily around the camera, with the delta overlay (11.5) applied on top of freshly-regenerated
// base terrain (11.3/11.4). Unlike WorldGenPreviewScreen (a full-screen takeover, appropriate
// pre-sim), this deliberately does NOT hide MainUI — TopBar (time controls) and RightDock
// (TileInspector/contextual panels) stay live and driven by the same SelectionBus/WorldSnapshot
// pipeline as the main map, per the user's "reuse the map view UI" request: clicking a character/
// beast marker here selects it exactly like clicking one on the main map would, and whatever
// contextual panel that selection shows (with its own working [Watch] button) comes for free.
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
        _titleLabel  = new Label { Text = "Local View", TextColor = UiTheme.HeaderText };
        _statsLabel  = new Label { TextColor = UiTheme.TextSecondary };
        _closeButton = new WeButton("[Back to World Map]", () => OnClose?.Invoke(), WeButtonVariant.Ghost);

        // A slim strip anchored inside the MapCanvas region (Game1 positions Root there each
        // frame via SetBounds) — TopBar above it keeps showing the real time controls/overlay
        // bar, RightDock keeps showing whatever's selected. This is a HUD overlay, not a
        // full-screen takeover.
        var header = new HorizontalStackPanel
        {
            Spacing = UiTheme.Space.Md,
            Background = new Myra.Graphics2D.Brushes.SolidBrush(UiTheme.SurfacePanel),
            Padding = new Myra.Graphics2D.Thickness(UiTheme.Space.Sm)
        };
        header.Widgets.Add(_titleLabel);
        header.Widgets.Add(new Label { Text = "Right-drag pan · Scroll zoom · Click marker to inspect · [Esc]", TextColor = UiTheme.TextSecondary });
        header.Widgets.Add(_statsLabel);
        header.Widgets.Add(_closeButton.Root);

        Root = new Panel { Visible = false };
        Root.Widgets.Add(header);
    }

    /// <summary>Call once after GraphicsDevice is available.</summary>
    public void Initialize(GraphicsDevice gd) => _renderer = new LocalTileMapRenderer(gd, _camera);

    /// <summary>Positions the HUD strip within the MapCanvas region — call from Game1.ApplyLayout every frame, mirroring how TopBar/RightDock are positioned.</summary>
    public void SetBounds(Rectangle mapCanvasBounds)
    {
        Root.Left = mapCanvasBounds.X;
        Root.Top  = mapCanvasBounds.Y;
    }

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

    /// <summary>
    /// Hit-tests a screen point against the current marker list (character/beast only —
    /// settlements have no distinct selectable ID, see LocalEntityMarker's doc comment), within a
    /// generous fixed pixel radius so small markers stay easy to click at low zoom.
    /// </summary>
    public bool TryPickEntity(Vector2 screenPos, out long id, out EntityKind kind)
    {
        const float pickRadiusPx = 14f;
        float bestDistSq = pickRadiusPx * pickRadiusPx;
        long bestId = 0;
        EntityKind bestKind = default;
        bool found = false;

        foreach (var m in _markers)
        {
            if (m.Id is not { } markerId) continue;
            var markerScreen = _camera.LocalTileToScreen(m.X, m.Y) + new Vector2(_camera.Zoom * 0.5f);
            float distSq = Vector2.DistanceSquared(screenPos, markerScreen);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                bestId = markerId;
                bestKind = m.Kind;
                found = true;
            }
        }

        id = bestId;
        kind = bestKind;
        return found;
    }

    /// <summary>Chunks generated per Update() call — bounds a single frame's worst-case cost so opening/panning the view doesn't stall a frame.</summary>
    private const int MaxChunksPerFrame = 12;

    /// <summary>
    /// Loads every chunk that overlaps the camera's actual visible viewport, plus a
    /// ViewDistanceChunks preload margin on each side (nearest-to-viewport-center first, throttled
    /// to MaxChunksPerFrame/call). Chunks farther than the margin plus a 1-chunk hysteresis band
    /// are discarded — the margin prevents evict/reload thrashing right at the edge. Also
    /// refreshes the marker list from the live snapshot. Call once per frame while visible.
    /// </summary>
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

    /// <summary>Rebuilds the marker list from whatever's currently located at _worldTile in the snapshot.</summary>
    private void RefreshEntities(WorldSnapshot? snapshot)
    {
        _markers.Clear();
        if (snapshot is null || _config is null) return;

        int n = _config.LocalTilesPerWorldTileEdge;

        // Settlement population scales the marker's visual size — a bigger village really is a
        // bigger blip, even without full building-layout rendering (V2: procedural village layout).
        if (snapshot.Settlements.TryGetValue(_worldTile, out var settlement))
            _markers.Add(new LocalEntityMarker(n / 2, n / 2, EntityKind.Settlement, false, null, settlement.Population));

        foreach (var (id, snap) in snapshot.EntitySnapshots)
        {
            if (!snap.IsAlive || snap.Location != _worldTile) continue;
            if (snap.Kind is not (EntityKind.Tier1Character or EntityKind.Tier2Character or EntityKind.LegendaryBeast)) continue;

            var (px, py) = PseudoLocalPosition(id.Value, n);
            _markers.Add(new LocalEntityMarker(px, py, snap.Kind, snap.IsLegendary, id.Value));
        }
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
