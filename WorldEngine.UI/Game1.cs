using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.UI.Rendering;
using WorldEngine.UI.UI;

namespace WorldEngine.UI;

public sealed class Game1 : Game
{
    private const int SidebarWidth   = 360;   // must match sidebar VerticalStackPanel Width
    private const int TimelineHeight  = 40;    // px reserved at bottom for timeline bar

    private GraphicsDeviceManager _graphics;
    private SpriteBatch? _spriteBatch;
    private Desktop? _desktop;

    // Sim wiring
    private readonly CommandQueue _commandQueue = new();
    private readonly StateCache _stateCache = new();
    private readonly ConcurrentQueue<(string Layer, float Fraction)> _genProgress = new();
    private SimLoop? _simLoop;
    private EventStore? _eventStore;
    private IHistoryQuery? _historyQuery;
    private Task<WorldState>? _genTask;

    // Rendering
    private Camera2D? _camera;
    private TileMapRenderer? _tileRenderer;

    // UI panels (created in LoadContent)
    private WorldGenScreen? _genScreen;
    private TimeControlsPanel? _timeControls;
    private EventLogPanel? _eventLog;
    private TileInspectorPanel? _tileInspector;
    private Panel? _mainUI;       // reference to the mainUI panel for post-sim panel injection

    // Narrative UI panels (created in StartSim, after historyQuery is available)
    private CharacterProfilePanel? _charProfile;
    private CivHistoryPanel?       _civHistory;
    private TimelineBar?           _timeline;
    private FocusLensState?        _focusLens;

    // Per-decade event-bucket refresh throttle
    private int _lastBucketLoadYear = -1;

    // Phase 3.4 panels (created in StartSim)
    private CharacterWatchPanel? _charWatch;
    private Panel? _mainUI;

    // Input
    private MouseState _prevMouse;
    private KeyboardState _prevKb;
    private bool _simStarted;
    private bool _simCrashReported;
    private Label? _crashLabel;
    private WorldSnapshot? _lastSnapshot;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth  = 1280,
            PreferredBackBufferHeight = 720
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        MyraEnvironment.Game = this;

        _camera       = new Camera2D();
        _tileRenderer = new TileMapRenderer(GraphicsDevice, _camera);

        _genScreen    = new WorldGenScreen();
        _timeControls = new TimeControlsPanel(_commandQueue);
        _eventLog     = new EventLogPanel();
        _tileInspector = new TileInspectorPanel();

        var rootPanel = BuildRootPanel();
        _desktop = new Desktop { Root = rootPanel };

        // Kick off world gen
        var worldCfg = new WorldConfig { Seed = 42, WidthKm = 2000, HeightKm = 1600, TileWidthKm = 10 };
        var simCfg   = SimConfigLoader.LoadOrCreateDefault();
        var progress = new Progress<(string, float)>(p => _genProgress.Enqueue(p));
        _genTask = Task.Run(() => GenerateWorld(worldCfg, simCfg, progress));
    }

    private Panel BuildRootPanel()
    {
        var root = new Panel();

        // Gen screen (shown during gen)
        if (_genScreen is not null)
            root.Widgets.Add(_genScreen.Root);

        // Main UI — root Panel so children can overlap the map freely
        var mainUI = new Panel { Visible = false, Id = "MainUI" };
        _mainUI = mainUI;

        // Time controls: full-width bar docked to the top
        if (_timeControls is not null)
        {
            _timeControls.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _timeControls.Root.VerticalAlignment   = VerticalAlignment.Top;
            mainUI.Widgets.Add(_timeControls.Root);
        }

        // Sidebar: fixed 360px wide, docked to top-right, below time controls
        var sidebar = new VerticalStackPanel
        {
            Width                = SidebarWidth,
            Spacing              = 4,
            HorizontalAlignment  = HorizontalAlignment.Right,
            VerticalAlignment    = VerticalAlignment.Top,
            Top                  = 44,   // clear the time controls bar (~40px tall)
        };
        if (_tileInspector is not null) sidebar.Widgets.Add(_tileInspector.Root);
        if (_eventLog      is not null) sidebar.Widgets.Add(_eventLog.Root);
        mainUI.Widgets.Add(sidebar);

        root.Widgets.Add(mainUI);

        // Crash overlay — hidden until sim thread dies
        _crashLabel = new Label
        {
            Text = "",
            TextColor = Color.Red,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Visible = false
        };
        root.Widgets.Add(_crashLabel);

        return root;
    }

    private static WorldState GenerateWorld(WorldConfig cfg, SimConfig simCfg, IProgress<(string, float)> progress)
    {
        var ctx = new WorldGenContext(cfg, simCfg);
        var layers = new (string name, Action run)[]
        {
            ("Tectonics",  () => ctx.Tectonic  = new TectonicLayer().Generate(ctx)),
            ("Elevation",  () => ctx.Elevation  = new ElevationLayer().Generate(ctx)),
            ("Ocean",      () => ctx.Ocean       = new OceanLayer().Generate(ctx)),
            ("Rivers",     () => ctx.River       = new RiverLayer().Generate(ctx)),
            ("Magic",      () => ctx.Magic       = new MagicLayer().Generate(ctx)),
            ("Climate",    () => ctx.Climate     = new ClimateLayer().Generate(ctx)),
            ("Biomes",     () => ctx.Biome       = new BiomeLayer().Generate(ctx)),
            ("Resources",  () => ctx.Resource    = new ResourceLayer().Generate(ctx)),
            ("POI",        () => ctx.Poi         = new PoiCandidateLayer().Generate(ctx)),
        };

        for (int i = 0; i < layers.Length; i++)
        {
            progress.Report((layers[i].name, (float)i / layers.Length));
            layers[i].run();
        }
        progress.Report(("Assembling", 1f));
        return TileGridAssembler.Assemble(ctx);
    }

    protected override void Update(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Escape))
            _commandQueue.Enqueue(new SetInspectedTile(null));

        DrainGenProgress();

        if (!_simStarted && _genTask?.IsCompletedSuccessfully == true)
            StartSim(_genTask.Result);

        // Surface sim thread crashes to a visible label and log file
        if (!_simCrashReported && _simLoop?.LastException is Exception simEx)
        {
            _simCrashReported = true;
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"\n{DateTime.Now:u} [SimThread]\n{simEx}\n");
            if (_crashLabel is not null)
            {
                _crashLabel.Text    = $"Sim crashed: {simEx.Message} — see crash.log";
                _crashLabel.Visible = true;
            }
        }

        var snapshot = _stateCache.Read();
        if (snapshot is not null && _simStarted)
        {
            HandleInput(snapshot);
            // Only rebuild Myra widgets when the sim has committed a new snapshot
            if (!ReferenceEquals(snapshot, _lastSnapshot))
            {
                _lastSnapshot = snapshot;
                _timeControls?.Update(snapshot);
                _eventLog?.Update(snapshot, _focusLens);
                _tileInspector?.Update(snapshot.InspectedTile, snapshot);

                _charWatch?.Refresh(snapshot);

                // Refresh timeline event buckets every 50 sim years
                if (_timeline is not null && _historyQuery is not null
                    && snapshot.CurrentYear - _lastBucketLoadYear >= 50)
                {
                    _timeline.LoadEventBuckets(_historyQuery, snapshot.CurrentYear);
                    _lastBucketLoadYear = snapshot.CurrentYear;
                }
            }

            // Consume Watch button clicks from the tile inspector
            if (_tileInspector is not null)
            {
                long watchId = _tileInspector.ConsumePendingWatch();
                if (watchId != 0)
                {
                    _commandQueue.Enqueue(new WatchCharacter(new EntityId(watchId)));
                    _charWatch?.Show();
                }
            }

            // Process pending event log interactions (consume-once pattern)
            if (_eventLog is not null && _historyQuery is not null && _desktop is not null)
            {
                if (_eventLog.ConsumePendingCharacterProfile() is long charId)
                    _charProfile?.ShowCharacter(charId);

                if (_eventLog.ConsumePendingCauseChain() is long evId)
                    ShowCauseChainDialog(evId);
            }
        }

        _desktop?.UpdateLayout();
        _prevMouse = Mouse.GetState();
        _prevKb    = Keyboard.GetState();
        base.Update(gameTime);
    }

    private void DrainGenProgress()
    {
        while (_genProgress.TryDequeue(out var p))
            _genScreen?.Update(p.Layer, p.Fraction);
    }

    private void StartSim(WorldState world)
    {
        _simStarted = true;
        _genScreen!.Root.Visible = false;

        // Find and show main UI
        if (_desktop?.Root is Panel root)
        {
            foreach (var w in root.Widgets)
                if (w.Id == "MainUI") w.Visible = true;
        }

        var simCfg = SimConfigLoader.LoadOrCreateDefault();
        var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();

        _eventStore = new EventStore("world.db");
        _eventStore.InitializeSchema();
        _historyQuery = _eventStore.GetHistoryQuery();

        var spawnEvents = BeastSpawner.SpawnAll(world, beastCatalog);
        var charSpawnEvents  = CharacterSpawner.SpawnAll(world, simCfg);
        var tier2SpawnEvents = Tier2Spawner.SpawnAll(world, simCfg);

        var eventCache = new EventCache(simCfg.Events.RecentEventCacheSize);
        var gate = new EventGate(simCfg);
        var phaseRunner = new PhaseRunner(simCfg, _eventStore, eventCache, gate,
            beastCatalog: beastCatalog);

        foreach (var pe in spawnEvents)
            phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in charSpawnEvents)
            phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in tier2SpawnEvents)
            phaseRunner.InjectPendingEvent(pe);

        var snapshotBuilder = new SnapshotBuilder();

        _simLoop = new SimLoop(world, _commandQueue, _stateCache, phaseRunner, snapshotBuilder, simCfg, eventCache);
        _simLoop.Start();

        // ── Narrative UI panels (Phase 3.3) ─────────────────────────────────
        _focusLens   = new FocusLensState();
        var ancestries = world.SimConfig.AncestryRegistry;
        _charProfile = new CharacterProfilePanel(_historyQuery, ancestries);
        _civHistory  = new CivHistoryPanel(_historyQuery, ancestries);

        // Timeline bar — SpriteBatch component + Myra label overlay
        _timeline = new TimelineBar();
        _timeline.Initialize(GraphicsDevice);

        // ── Character Watch panel (Phase 3.4) ───────────────────────────────
        _charWatch = new CharacterWatchPanel();

        if (_mainUI is not null && _desktop is not null)
        {
            _charProfile.Root.HorizontalAlignment = HorizontalAlignment.Left;
            _charProfile.Root.VerticalAlignment   = VerticalAlignment.Top;
            _charProfile.Root.Top  = 44;
            _charProfile.Root.Left = 4;
            _mainUI.Widgets.Add(_charProfile.Root);

            _civHistory.Root.HorizontalAlignment = HorizontalAlignment.Left;
            _civHistory.Root.VerticalAlignment   = VerticalAlignment.Top;
            _civHistory.Root.Top  = 44;
            _civHistory.Root.Left = 4;
            _mainUI.Widgets.Add(_civHistory.Root);

            _charWatch.Root.HorizontalAlignment = HorizontalAlignment.Left;
            _charWatch.Root.VerticalAlignment   = VerticalAlignment.Top;
            _charWatch.Root.Top  = 44;
            _charWatch.Root.Left = 4;
            _mainUI.Widgets.Add(_charWatch.Root);

            if (_desktop.Root is Panel rootPanel)
                rootPanel.Widgets.Add(_timeline.ScrubLabel);
        }
    }

    private void HandleInput(WorldSnapshot snapshot)
    {
        if (_camera is null) return;
        var mouse = Mouse.GetState();
        var kb    = Keyboard.GetState();

        // Right-drag to pan
        if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Pressed)
        {
            var delta = new Vector2(mouse.X - _prevMouse.X, mouse.Y - _prevMouse.Y);
            _camera.Pan(-delta);
        }

        // Scroll wheel zoom
        int scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scrollDelta != 0)
        {
            float factor = scrollDelta > 0 ? 1.15f : 1f / 1.15f;
            _camera.ZoomAt(new Vector2(mouse.X, mouse.Y), factor);
        }

        // Left-click → inspect tile (only if click is in the map area, not the sidebar or Myra widgets)
        if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
        {
            int   mapWidth  = GraphicsDevice.Viewport.Width - SidebarWidth;
            int   mapHeight = GraphicsDevice.Viewport.Height - TimelineHeight;
            bool inMapArea  = mouse.X >= 0 && mouse.X < mapWidth
                           && mouse.Y >= 0 && mouse.Y < mapHeight;
            bool overGui   = _desktop?.IsMouseOverGUI == true;
            if (inMapArea && !overGui)
            {
                var coord = _camera.ScreenToTile(new Vector2(mouse.X, mouse.Y));
                // Discard clicks that land outside the valid tile grid (zoomed-out empty space)
                if (coord.X < 0 || coord.X >= snapshot.WorldTileWidth
                 || coord.Y < 0 || coord.Y >= snapshot.WorldTileHeight)
                    return;
                _commandQueue.Enqueue(new SetInspectedTile(coord));
            }
        }

        // Overlay shortcuts
        // DECISION: T is Territory overlay (M3 Phase 3.4). Temperature moved off keyboard.
        if (kb.IsKeyDown(Keys.B)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Biome));
        if (kb.IsKeyDown(Keys.E)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Elevation));
        if (kb.IsKeyDown(Keys.T)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Territory));
        if (kb.IsKeyDown(Keys.M)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Moisture));
        if (kb.IsKeyDown(Keys.R)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Resources));
        if (kb.IsKeyDown(Keys.G)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.MagicIntensity));

        // H = toggle civ history panel; W = toggle character watch panel
        if (kb.IsKeyDown(Keys.H) && !_prevKb.IsKeyDown(Keys.H))
        {
            if (_civHistory?.IsVisible == true) _civHistory.Hide();
            else _civHistory?.Show();
        }
        if (kb.IsKeyDown(Keys.W) && !_prevKb.IsKeyDown(Keys.W))
        {
            if (_charWatch?.IsVisible == true) _charWatch.Hide();
            else _charWatch?.Show();
        }

        // N = new world (press-edge only, not hold)
        if (kb.IsKeyDown(Keys.N) && !_prevKb.IsKeyDown(Keys.N) && _simStarted) ResetToNewWorld();

        // Timeline scrubber
        if (_timeline is not null)
        {
            var vp = GraphicsDevice.Viewport;
            var timelineRect = new Rectangle(0, vp.Height - TimelineHeight, vp.Width - SidebarWidth, TimelineHeight);
            _timeline.Update(snapshot.CurrentYear, mouse, _prevMouse, timelineRect);
        }
    }

    private void ShowCauseChainDialog(long effectEventId)
    {
        if (_historyQuery is null || _desktop is null) return;

        var chain = _historyQuery.GetCausalChain(effectEventId, maxDepth: 3);

        var content = new VerticalStackPanel { Spacing = 4 };
        if (chain.Count == 0)
        {
            content.Widgets.Add(new Label { Text = "(No recorded causes found)" });
        }
        else
        {
            content.Widgets.Add(new Label { Text = $"Upstream causes ({chain.Count}):", TextColor = Color.White });
            foreach (var (causeId, causeEv, edgeType) in chain)
                content.Widgets.Add(new Label
                {
                    Text      = $"  [{edgeType}] Year {causeEv.Year} — {causeEv.TypeName}",
                    TextColor = Color.LightGray
                });
        }

        var window = new Window
        {
            Title   = "Cause Chain",
            Content = content,
            Width   = 380,
            Height  = 260
        };
        window.ShowModal(_desktop);
    }

    private void ResetToNewWorld()
    {
        // Stop sim thread, then dispose connection before deleting the file.
        // Disposing first releases all WAL locks so File.Delete succeeds on Windows.
        _simLoop?.Stop();
        _simLoop = null;
        _eventStore?.Dispose();
        _eventStore    = null;
        _historyQuery  = null;

        const string DbPath = "world.db";
        foreach (var f in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);

        // Reset state flags
        _simStarted         = false;
        _simCrashReported   = false;
        _lastBucketLoadYear = -1;
        if (_crashLabel is not null) _crashLabel.Visible = false;

        // Dispose timeline texture
        _timeline?.Dispose();
        _timeline = null;

        // Hide all narrative/watch panels
        _charProfile?.Hide();
        _civHistory?.Hide();
        _focusLens?.Clear();
        _charWatch?.Hide();

        // Reset UI: hide main panels, show gen screen, clear inspector & log
        if (_desktop?.Root is Panel root)
        {
            foreach (var w in root.Widgets)
                if (w.Id == "MainUI") w.Visible = false;
        }
        _genScreen!.Root.Visible = true;
        _commandQueue.Enqueue(new SetInspectedTile(null));
        _commandQueue.Enqueue(new WatchCharacter(new EntityId(0)));  // clear watch target

        // Re-kick world gen
        var worldCfg = new WorldConfig { Seed = Environment.TickCount, WidthKm = 2000, HeightKm = 1600, TileWidthKm = 10 };
        var simCfg   = SimConfigLoader.LoadOrCreateDefault();
        var progress = new Progress<(string, float)>(p => _genProgress.Enqueue(p));
        _genTask = Task.Run(() => GenerateWorld(worldCfg, simCfg, progress));
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        var snapshot = _stateCache.Read();
        if (_simStarted && snapshot is not null && _tileRenderer is not null && _spriteBatch is not null)
        {
            var vp = GraphicsDevice.Viewport;

            // Scissor-clip tile rendering to the map area (leave sidebar + timeline bar uncovered)
            GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, vp.Width - SidebarWidth, vp.Height - TimelineHeight);
            _spriteBatch.Begin(rasterizerState: new RasterizerState { ScissorTestEnable = true });
            _tileRenderer.Draw(_spriteBatch, snapshot);
            _spriteBatch.End();

            // Timeline bar — drawn below the map, no scissor
            if (_timeline is not null)
            {
                var timelineRect = new Rectangle(0, vp.Height - TimelineHeight, vp.Width - SidebarWidth, TimelineHeight);
                _spriteBatch.Begin();
                _timeline.Draw(_spriteBatch, timelineRect, snapshot.CurrentYear);
                _spriteBatch.End();
            }
        }

        _desktop?.Render();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _simLoop?.Stop();
        _eventStore?.Dispose();
        _tileRenderer?.Dispose();
        _timeline?.Dispose();
    }
}
