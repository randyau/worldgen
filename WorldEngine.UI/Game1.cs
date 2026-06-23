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
    private GraphicsDeviceManager _graphics;
    private SpriteBatch? _spriteBatch;
    private Desktop? _desktop;

    // Sim wiring
    private readonly CommandQueue _commandQueue = new();
    private readonly StateCache _stateCache = new();
    private readonly ConcurrentQueue<(string Layer, float Fraction)> _genProgress = new();
    private SimLoop? _simLoop;
    private EventStore? _eventStore;
    private Task<WorldState>? _genTask;

    // Rendering
    private Camera2D? _camera;
    private TileMapRenderer? _tileRenderer;

    // UI panels
    private WorldGenScreen? _genScreen;
    private TimeControlsPanel? _timeControls;
    private EventLogPanel? _eventLog;
    private TileInspectorPanel? _tileInspector;

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

        // Main UI layout (hidden until sim starts)
        var mainStack = new VerticalStackPanel { Visible = false, Id = "MainUI" };

        if (_timeControls is not null)
            mainStack.Widgets.Add(_timeControls.Root);

        var mapAndSidebar = new HorizontalStackPanel();
        // Map area: fills remaining space
        var mapPlaceholder = new Panel { Id = "MapArea", HorizontalAlignment = HorizontalAlignment.Stretch };
        mapAndSidebar.Widgets.Add(mapPlaceholder);

        var sidebar = new VerticalStackPanel { Width = 360, Spacing = 8 };
        if (_tileInspector is not null) sidebar.Widgets.Add(_tileInspector.Root);
        if (_eventLog is not null) sidebar.Widgets.Add(_eventLog.Root);
        mapAndSidebar.Widgets.Add(sidebar);

        mainStack.Widgets.Add(mapAndSidebar);
        root.Widgets.Add(mainStack);

        // Crash overlay — hidden until sim thread dies
        _crashLabel = new Label
        {
            Text = "",
            TextColor = Microsoft.Xna.Framework.Color.Red,
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
                _eventLog?.Update(snapshot);
                _tileInspector?.Update(snapshot.InspectedTile, snapshot);
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
    }

    private void HandleInput(WorldSnapshot snapshot)
    {
        if (_camera is null) return;
        var mouse = Mouse.GetState();

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

        // Left-click → inspect tile
        if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
        {
            var coord = _camera.ScreenToTile(new Vector2(mouse.X, mouse.Y));
            _commandQueue.Enqueue(new SetInspectedTile(coord));
        }

        // Overlay shortcuts
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.B)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Biome));
        if (kb.IsKeyDown(Keys.E)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Elevation));
        if (kb.IsKeyDown(Keys.T)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Temperature));
        if (kb.IsKeyDown(Keys.M)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Moisture));
        if (kb.IsKeyDown(Keys.R)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Resources));
        if (kb.IsKeyDown(Keys.G)) _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.MagicIntensity));

        // N = new world (press-edge only, not hold)
        if (kb.IsKeyDown(Keys.N) && !_prevKb.IsKeyDown(Keys.N) && _simStarted) ResetToNewWorld();
    }

    private void ResetToNewWorld()
    {
        // Stop sim thread first, then truncate DB while connection is still open
        // (avoids SQLite WAL file-lock on Windows that File.Delete would hit)
        _simLoop?.Stop();
        _simLoop = null;
        _eventStore?.Truncate();
        _eventStore?.Dispose();
        _eventStore = null;

        // Reset state flags
        _simStarted         = false;
        _simCrashReported   = false;
        if (_crashLabel is not null) _crashLabel.Visible = false;

        // Reset UI: hide main panels, show gen screen, clear inspector & log
        if (_desktop?.Root is Panel root)
        {
            foreach (var w in root.Widgets)
                if (w.Id == "MainUI") w.Visible = false;
        }
        _genScreen!.Root.Visible = true;
        _commandQueue.Enqueue(new SetInspectedTile(null));

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
            _spriteBatch.Begin();
            _tileRenderer.Draw(_spriteBatch, snapshot);
            _spriteBatch.End();
        }

        _desktop?.Render();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _simLoop?.Stop();
        _eventStore?.Dispose();
        _tileRenderer?.Dispose();
    }
}
