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
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Panels;
using WorldEngine.UI.UI.Present;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Settings;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI;

// MAP: MonoGame entry: update/draw loop, StateCache reads, input routing; H=civ history, W=watch panel, T=territory overlay.
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
    private IHistoryQuery? _historyQuery;
    private Task<WorldState>? _genTask;

    // Rendering
    private Camera2D? _camera;
    private TileMapRenderer? _tileRenderer;

    // UI panels (created in LoadContent)
    private WorldGenScreen? _genScreen;
    private TimeControlsPanel? _timeControls;
    private EventLogPanel? _eventLog;
    private OverlayBar? _overlayBar;
    private PanelMenuBar? _panelMenuBar;
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

    // M7 — God Mode panel + spotlight state
    private GodModePanel? _godModePanel;
    private EntityId?     _spotlightCharacterId;

    // M6.1.3 — keybind registry (single source of truth) + help overlay
    private KeybindRegistry?  _keybinds;
    private HelpPanel? _helpPanel;
    private SettingsPanel? _settingsPanel;
    private CommandRegistry? _commands;
    private UiPrefs _uiPrefs = new();

    // M6.1.4 / M8.2.1 — unified selection model driving which contextual panel shows
    private SelectionBus? _selectionBus;

    // M6.3.1 — first-class filter panel above the event log
    private FilterPanel? _filterPanel;

    // M8 8.1 — layout host: owns every screen rectangle, z-band, and hit-test; SimWorkspace is
    // the tabbed right dock; ModalHost is the one modal surface. Replaces PanelManager/ToggleBar
    // and the ad-hoc absolute Top/Left placement below (framework §3, §5).
    private LayoutHost?    _layoutHost;
    private InputRouter?   _inputRouter;
    private SimWorkspace?  _workspace;
    private ModalHost?     _modalHost;
    private Presenter?     _presenter;
    private CommandGateway? _commandGateway;
    private WeVStack?      _topBarStack;
    private Panel?         _topBarBoundWidget;
    private WeScroll?      _dockScroll;
    private Rectangle      _lastViewport;

    // Phase 3.6 — save / resume
    private const string SaveDir = "worldsave";
    private Label? _savingLabel;          // "Saving..." overlay
    private bool _resumePromptShown;      // true once we've checked for a save on startup
    private Task<WorldState>? _loadTask;  // background load task for resume

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
        _filterPanel  = new FilterPanel();
        _overlayBar   = new OverlayBar(_commandQueue);
        _tileInspector = new TileInspectorPanel();

        // M8.5.1: UI prefs are global (not per-world) — loaded once here, applied to the layout
        // host below, and reused across New World resets (unlike per-world sim state).
        _uiPrefs = UiPrefsStore.Load();

        _layoutHost      = new LayoutHost { DockWidth = _uiPrefs.DockWidth };
        _inputRouter     = new InputRouter();
        _workspace       = new SimWorkspace();
        _modalHost       = new ModalHost();
        _presenter       = new Presenter();
        _commandGateway  = new CommandGateway(_commandQueue);
        _selectionBus    = new SelectionBus();
        _panelMenuBar    = new PanelMenuBar(_workspace, _commandQueue);

        var rootPanel = BuildRootPanel();
        _desktop = new Desktop { Root = rootPanel };

        // Phase 3.6 — check for a saved world on startup before starting gen
        if (!_resumePromptShown)
        {
            _resumePromptShown = true;
            var meta = WorldStateSaver.ReadMeta(SaveDir);
            if (meta is not null)
            {
                ShowResumePrompt(meta);
                // Don't start world gen yet — wait for player choice
                return;
            }
        }

        // No save exists — start world gen immediately
        StartNewWorldGen();
    }

    // M8 8.1: composes the fixed region grid (framework §3.2, §5.1). Panel *content* never sets
    // its own absolute geometry any more — the handful of Top/Left/Width/Height assignments here
    // are the single, centralized place that turns LayoutHost region rectangles into on-screen
    // widget bounds (see ApplyLayout). This is orchestration wiring, not panel geometry.
    private Panel BuildRootPanel()
    {
        var root = new Panel();

        // Gen screen (shown during gen)
        if (_genScreen is not null)
            root.Widgets.Add(_genScreen.Root);

        // Main UI — root Panel so children can overlap the map freely
        var mainUI = new Panel { Visible = false, Id = "MainUI" };
        _mainUI = mainUI;

        // TopBar region content: time controls + overlay bar stacked, one backed strip.
        _topBarStack = new WeVStack(UiTheme.Space.Xs);
        if (_timeControls is not null) _topBarStack.Add(_timeControls.Root);
        if (_overlayBar   is not null) _topBarStack.Add(_overlayBar.Root);
        if (_panelMenuBar is not null) _topBarStack.Add(_panelMenuBar.Root);
        var topBarPanel = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Top,
            Background = new Myra.Graphics2D.Brushes.SolidBrush(UiTheme.SurfacePanel)
        };
        topBarPanel.Widgets.Add(_topBarStack.Root);
        mainUI.Widgets.Add(topBarPanel);
        _topBarBoundWidget = topBarPanel;

        // RightDock region content: SimWorkspace's tabbed dock, scrolled (WeScroll reserves
        // scrollbar width so dock content is never hidden behind it — framework §3.2).
        _dockScroll = new WeScroll();
        _dockScroll.Root.HorizontalAlignment = HorizontalAlignment.Left;
        _dockScroll.Root.VerticalAlignment   = VerticalAlignment.Top;
        mainUI.Widgets.Add(_dockScroll.Root);

        // Float region content: summoned panels (God Mode, Help, Watch, Civ History).
        if (_workspace is not null)
        {
            _workspace.FloatRoot.HorizontalAlignment = HorizontalAlignment.Left;
            _workspace.FloatRoot.VerticalAlignment   = VerticalAlignment.Top;
            mainUI.Widgets.Add(_workspace.FloatRoot);
        }

        root.Widgets.Add(mainUI);

        // Modal region — one scrim + centered content surface, fills the whole root.
        if (_modalHost is not null)
        {
            _modalHost.Root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _modalHost.Root.VerticalAlignment   = VerticalAlignment.Stretch;
            root.Widgets.Add(_modalHost.Root);
        }

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

        // Saving overlay — shown briefly while a save is in progress (Phase 3.6)
        _savingLabel = new Label
        {
            Text = "Saving...",
            TextColor = Color.Yellow,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Top,
            Top     = 48,
            Visible = false
        };
        root.Widgets.Add(_savingLabel);

        return root;
    }

    /// <summary>Recomputes region rectangles on resize and applies them to the region-hosted widgets.</summary>
    private void ApplyLayout()
    {
        if (_layoutHost is null) return;
        var vp = new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        if (vp == _lastViewport) return;
        _lastViewport = vp;
        _layoutHost.SetViewport(vp);

        var topBar = _layoutHost.Slot(RegionSlot.TopBar).Bounds;
        if (_topBarBoundWidget is not null)
        {
            _topBarBoundWidget.Left = topBar.X; _topBarBoundWidget.Top = topBar.Y;
            _topBarBoundWidget.Width = topBar.Width; _topBarBoundWidget.Height = topBar.Height;
        }

        var dock = _layoutHost.Slot(RegionSlot.RightDock).Bounds;
        if (_dockScroll is not null && _workspace is not null)
        {
            _dockScroll.Root.Left = dock.X; _dockScroll.Root.Top = dock.Y;
            _dockScroll.SetContent(_workspace.DockRoot, dock.Width, dock.Height);
        }

        var floatBounds = _layoutHost.Slot(RegionSlot.Float).Bounds;
        if (_workspace is not null)
        {
            _workspace.FloatRoot.Left = 4;
            _workspace.FloatRoot.Top  = topBar.Bottom + 4;
        }
        _ = floatBounds; // Float region is viewport-sized; content self-sizes, only anchored top-left.
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
        DrainGenProgress();

        // Resume path: load task completed
        if (!_simStarted && _loadTask?.IsCompletedSuccessfully == true)
            StartSimFromLoad(_loadTask.Result);

        // Gen path: show completion screen when ready, then start sim on button click
        if (!_simStarted && _genTask?.IsCompletedSuccessfully == true)
            _genScreen?.ShowComplete();
        if (!_simStarted && _genTask?.IsCompletedSuccessfully == true && _genScreen?.ConsumePendingStart() == true)
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

        ApplyLayout();

        var snapshot = _stateCache.Read();
        if (snapshot is not null && _simStarted)
        {
            HandleInput(snapshot);

            // Every frame, regardless of whether a new sim snapshot arrived — UI interaction
            // state must respond immediately, including while paused (bug: was gated behind the
            // snapshot-changed check below, so panels stayed stuck open/closed and the top-bar
            // highlight lagged until the next tick). Only the *content* a panel displays is sim
            // data and stays tick-gated below.
            _workspace?.SyncVisibility();
            _panelMenuBar?.RefreshHighlights();

            // Only rebuild Myra widgets when the sim has committed a new snapshot
            if (!ReferenceEquals(snapshot, _lastSnapshot))
            {
                _lastSnapshot = snapshot;
                _timeControls?.Update(snapshot);
                _overlayBar?.Update(snapshot.ActiveOverlay);
                _panelMenuBar?.UpdateSpotlightStatus(snapshot.SpotlightCharacterId.HasValue ? snapshot.WatchedCharacter?.Name : null);

                _charWatch?.SetContext(snapshot.SpotlightCharacterId, snapshot.InspectedTile?.Coord);
                _eventLog?.SetContext(_focusLens, _filterPanel?.CurrentFilter);

                if (_workspace is not null && _selectionBus is not null && _presenter is not null && _commandGateway is not null)
                {
                    _workspace.Bind(new PanelContext(snapshot, _selectionBus, _presenter, _commandGateway));
                    _workspace.RefreshVisible();
                }

                // Phase 3.6: show/hide saving overlay
                if (_savingLabel is not null)
                    _savingLabel.Visible = snapshot.IsSaving;

                _spotlightCharacterId = snapshot.SpotlightCharacterId;

                // Refresh timeline event buckets every 50 sim years
                if (_timeline is not null && _historyQuery is not null
                    && snapshot.CurrentYear - _lastBucketLoadYear >= 50)
                {
                    _timeline.LoadEventBuckets(_historyQuery, snapshot.CurrentYear);
                    _timeline.LoadHeadlineEvents(_historyQuery, snapshot.CurrentYear);
                    _lastBucketLoadYear = snapshot.CurrentYear;
                }
            }

            // Camera follow in spotlight mode (M7 Phase 7.4.3)
            if (_spotlightCharacterId.HasValue && snapshot.WatchedCharacter?.Location is { } charLoc && _camera is not null && _layoutHost is not null)
            {
                var mapBounds = _layoutHost.Slot(RegionSlot.MapCanvas).Bounds;
                _camera.CenterOn(charLoc, mapBounds.Width, mapBounds.Height);
            }

            // Apply any selection change to the contextual panels (M6.1.4 / M8.2.1).
            _selectionBus?.Apply();
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

    /// <param name="world">World to simulate.</param>
    /// <param name="spawnInitialEntities">
    /// True for a freshly generated world (runs spawners).
    /// False for a loaded world (entities are already present in WorldState).
    /// </param>
    private void StartSim(WorldState world, bool spawnInitialEntities = true)
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

        // Reuse the existing connection when the EventStore was already Reset() by
        // ResetToNewWorld(); create fresh on first startup or after a full dispose.
        if (_eventStore is null)
        {
            _eventStore = new EventStore("world.db");
            _eventStore.InitializeSchema();
        }
        _historyQuery = _eventStore.GetHistoryQuery();

        var eventCache = new EventCache(simCfg.Events.RecentEventCacheSize);
        var gate = new EventGate(simCfg);
        var phaseRunner = new PhaseRunner(simCfg, _eventStore, eventCache, gate,
            beastCatalog: beastCatalog);

        if (spawnInitialEntities)
        {
            // Fresh world — populate initial entities
            var spawnEvents      = BeastSpawner.SpawnAll(world, beastCatalog);
            var charSpawnEvents  = CharacterSpawner.SpawnAll(world, simCfg);
            var tier2SpawnEvents = Tier2Spawner.SpawnAll(world, simCfg);
            foreach (var pe in spawnEvents)      phaseRunner.InjectPendingEvent(pe);
            foreach (var pe in charSpawnEvents)  phaseRunner.InjectPendingEvent(pe);
            foreach (var pe in tier2SpawnEvents) phaseRunner.InjectPendingEvent(pe);
        }

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

        // ── God Mode panel (M7 epics 7.2.1–7.2.3) ───────────────────────────
        _godModePanel = new GodModePanel(_modalHost!);

        // Build the command/keybind registries now that the workspace/panels exist to target,
        // then Help/Settings from them (M8.4: named actions replace inline keybind lambdas;
        // M8.5: Settings hosts the same KeybindEditor on its Controls tab). A rebind from either
        // panel persists through the same ApplyAndPersistUiPrefs callback.
        BuildKeybinds();
        _helpPanel     = new HelpPanel(_commands!, _keybinds!, onKeybindsChanged: () => ApplyAndPersistUiPrefs(_uiPrefs));
        _settingsPanel = new SettingsPanel(_uiPrefs, _commands!, _keybinds!, ApplyAndPersistUiPrefs);

        // M8 8.1.6: register every panel with the SimWorkspace dock instead of the retired
        // PanelManager/absolute-Top-Left placement. Placement here preserves each panel's
        // pre-M8 keybind/click behavior exactly (Civ History and Watch stay keybind-toggled
        // Summoned panels rather than the framework's illustrative Contextual mapping) —
        // // DECISION: prioritizes zero behavior change during this structural-only phase;
        // every panel is now migrated onto the kit (8.3.1-8.3.6, LegacyPanelAdapter retired).
        if (_workspace is not null)
        {
            _workspace.Register(_eventLog!);
            _workspace.Register(_filterPanel!);
            _workspace.Register(_tileInspector!);
            _workspace.Register(_charProfile!);
            _workspace.Register(_charWatch!);
            _workspace.Register(_civHistory!);
            _workspace.Register(_godModePanel!);
            _workspace.Register(_helpPanel!);
            _workspace.Register(_settingsPanel!);
        }

        if (_desktop?.Root is Panel rootPanelForTimeline)
            rootPanelForTimeline.Widgets.Add(_timeline.ScrubLabel);

        // 6.4.2 — show first-run orientation once after the sim starts, via the ModalHost (8.1.5 proof case)
        if (_modalHost is not null)
            FirstRunOverlay.Show(_modalHost);

        // Unified selection model (M6.1.4 / M8.2.1): one "selected thing" drives which contextual
        // tab shows. One Changed handler replaces the old SelectionRouter callback set.
        if (_selectionBus is not null)
        {
            _selectionBus.Changed += snapshot =>
            {
                switch (snapshot.Kind)
                {
                    // Tile inspection stays a sim command — the snapshot must carry tile detail.
                    case SelectionKind.Tile:
                        _commandQueue.Enqueue(new SetInspectedTile(snapshot.Coord));
                        _workspace?.SetSelection(SelectionKind.Tile);
                        break;
                    case SelectionKind.Character:
                        // Summoned (Float region), not Contextual — clicking a name used to
                        // replace the Tile Inspector tab, which was confusing (playtest feedback).
                        _charProfile?.ShowCharacter(snapshot.Id);
                        if (_historyQuery is not null) _focusLens?.FocusCharacter(snapshot.Id, _historyQuery);
                        break;
                    case SelectionKind.Civ:
                        _civHistory?.ShowCiv(snapshot.Id);
                        if (_historyQuery is not null) _focusLens?.FocusCiv(snapshot.Id, _historyQuery);
                        _workspace?.ShowSummoned("civ");
                        break;
                    case SelectionKind.None:
                        _commandQueue.Enqueue(new SetInspectedTile(null));
                        _focusLens?.Clear();
                        _workspace?.SetSelection(SelectionKind.None);
                        break;
                }
            };
        }

        // M8.2.2: navigation clicks call the bus directly instead of Game1 polling a pending
        // field each frame. Tile Inspector [Watch] and Civ History/Character Watch keep their
        // pre-M8 Summoned-panel behavior exactly (see the DECISION above panel registration) —
        // // DECISION: only the polling mechanism changes here, not panel placement.
        _tileInspector!.OnWatch = id =>
        {
            _commandQueue.Enqueue(new WatchCharacter(new EntityId(id)));
            _workspace?.ShowSummoned("watch");
        };

        _eventLog!.OnCharacterProfile = id => _selectionBus?.Select(new EntityRef(SelectionKind.Character, id, default));
        _eventLog.OnCiv               = id => _selectionBus?.Select(new EntityRef(SelectionKind.Civ, id, default));
        _eventLog.OnCauseChain        = evId => ShowCauseChainDialog(evId);

        // M8.2.3: spotlight/goal intents change the world, so they enqueue commands directly
        // rather than flowing through the selection bus. Entering spotlight also selects the
        // character so the Character contextual tab follows — that part is a selection.
        _charWatch!.OnEnterSpotlight = id =>
        {
            _commandQueue.Enqueue(new EnterSpotlight(id));
            _selectionBus?.Select(new EntityRef(SelectionKind.Character, id.Value, default));
        };
        _charWatch.OnExitSpotlight = () => _commandQueue.Enqueue(new ExitSpotlight());
        _charWatch.OnMoveIntent = () =>
        {
            if (_lastSnapshot?.InspectedTile?.Coord is { } moveTarget)
                _commandQueue.Enqueue(new SetSpotlightMoveIntent(moveTarget));
        };
        _charWatch.OnWanderGoal = () =>
        {
            if (_spotlightCharacterId.HasValue)
                _commandQueue.Enqueue(new AuthorNudgeCharacter(_spotlightCharacterId.Value, CharacterNudge.SetWander));
        };
        _charWatch.OnSettleGoal = () =>
        {
            if (_spotlightCharacterId.HasValue)
                _commandQueue.Enqueue(new AuthorNudgeCharacter(_spotlightCharacterId.Value, CharacterNudge.SetSettle));
        };
        _charWatch.OnProfile = id => _selectionBus?.Select(new EntityRef(SelectionKind.Character, id, default));

        // Default camera: fit the whole world into the map viewport area. (Regression fix: this
        // was dropped during the M8 8.1 StartSim rewrite — restored using LayoutHost's constants
        // since ApplyLayout may not have run yet this frame.)
        if (_camera is not null)
        {
            int dockWidth = _layoutHost?.DockWidth ?? UiTheme.SidebarWidth;
            int mapW = GraphicsDevice.Viewport.Width  - dockWidth;
            int mapH = GraphicsDevice.Viewport.Height - LayoutHost.TopChromeHeight - LayoutHost.TimelineHeight;
            _camera.FitToWorld(world.Config.TileWidth, world.Config.TileHeight, mapW, mapH);
        }
    }

    /// <summary>
    /// Registers every named user action in one place (M8.4.1-8.4.2). Each command's handler is
    /// the single source of behavior — UI buttons invoke the same command id as its keybind, so
    /// keys and visible controls can never diverge (continues M6 Epic 6.1.3's "UI-primary").
    /// </summary>
    private void BuildKeybinds()
    {
        var cmds = new CommandRegistry();

        // Overlays — edge-triggered so holding a key doesn't flood the command queue.
        cmds.Register(new UiCommand("overlay.biome",     "Biome overlay",     "Overlays", () => _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Biome)),          Keys.B));
        cmds.Register(new UiCommand("overlay.elevation", "Elevation overlay", "Overlays", () => _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Elevation)),      Keys.E));
        cmds.Register(new UiCommand("overlay.territory", "Territory overlay", "Overlays", () => _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Territory)),      Keys.T));
        cmds.Register(new UiCommand("overlay.moisture",  "Moisture overlay",  "Overlays", () => _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Moisture)),       Keys.M));
        cmds.Register(new UiCommand("overlay.resources", "Resources overlay", "Overlays", () => _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.Resources)),      Keys.R));
        cmds.Register(new UiCommand("overlay.magic",     "Magic overlay",     "Overlays", () => _commandQueue.Enqueue(new SetActiveOverlay(OverlayType.MagicIntensity)), Keys.G));

        // Panels — route through the SimWorkspace dock so keys stay in lock-step with clicks.
        cmds.Register(new UiCommand("panel.civ",     "Civ history panel",     "Panels", () => _workspace?.ToggleSummoned("civ"),     Keys.H));
        cmds.Register(new UiCommand("panel.watch",   "Character watch panel", "Panels", () => _workspace?.ToggleSummoned("watch"),   Keys.W));
        cmds.Register(new UiCommand("panel.help",    "This help",             "Panels", () => _workspace?.ToggleSummoned("help"),    Keys.OemQuestion));
        cmds.Register(new UiCommand("panel.godmode", "God Mode panel",        "Panels", () => _workspace?.ToggleSummoned("godmode"), Keys.F2));
        cmds.Register(new UiCommand("panel.settings", "Settings",             "Panels", () => _workspace?.ToggleSummoned("settings"), Keys.OemComma, DefaultCtrl: true));

        // World
        cmds.Register(new UiCommand("world.pause", "Pause / resume", "World", () => _commandQueue.Enqueue(new SetSimSpeed(
            _lastSnapshot?.IsPaused == true ? SimSpeed.Normal : SimSpeed.Paused)), Keys.Space));
        cmds.Register(new UiCommand("world.newworld",   "New world",      "World", ResetToNewWorld, Keys.N));
        cmds.Register(new UiCommand("world.save",       "Save world",     "World", () => _commandQueue.Enqueue(new SaveWorld(SaveDir)), Keys.S, DefaultCtrl: true));
        cmds.Register(new UiCommand("select.clear",     "Deselect tile",  "World", () => _commandQueue.Enqueue(new SetInspectedTile(null)), Keys.Escape));

        _commands = cmds;
        _keybinds = new KeybindRegistry(cmds);
        _keybinds.LoadDefaults();
        _keybinds.ApplyOverrides(_uiPrefs.KeybindOverrides);
    }

    /// <summary>
    /// Applies a changed <see cref="UiPrefs"/> live where feasible (currently: dock width) and
    /// persists it, always refreshing <see cref="UiPrefs.KeybindOverrides"/> from the live
    /// registry first — a rebind from either Help or Settings' Controls tab calls this the same
    /// way, so the two panels can never drift on what's actually saved (M8.5.1/8.5.4).
    /// </summary>
    private void ApplyAndPersistUiPrefs(UiPrefs updated)
    {
        _uiPrefs = updated with
        {
            KeybindOverrides = _keybinds is not null
                ? new Dictionary<string, string>(_keybinds.ExportOverrides())
                : updated.KeybindOverrides
        };
        if (_layoutHost is not null)
        {
            _layoutHost.DockWidth = _uiPrefs.DockWidth;
            _lastViewport = default; // force ApplyLayout to recompute regions next frame
        }
        UiPrefsStore.Save(_uiPrefs);
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

        // Scroll wheel zoom — only when the pointer isn't over a panel/scrollbar, otherwise the
        // wheel should scroll that panel's content instead (bug: previously fired unconditionally
        // and zoomed the map even while scrolling the dock).
        int scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scrollDelta != 0 && _desktop?.IsMouseOverGUI != true)
        {
            float factor = scrollDelta > 0 ? 1.15f : 1f / 1.15f;
            _camera.ZoomAt(new Vector2(mouse.X, mouse.Y), factor);
        }

        // Left-click → inspect tile. The InputRouter is the single click-leak fix (framework
        // §5.1/§3.2): a null route means no opaque Chrome/Modal region claimed the point, so
        // it falls through to the map. MapCanvas itself is non-opaque and never claims input.
        if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed
            && _layoutHost is not null && _inputRouter is not null)
        {
            var routed = _inputRouter.Route(new Point(mouse.X, mouse.Y), _layoutHost);
            bool overGui = _desktop?.IsMouseOverGUI == true;
            if (routed is null && !overGui)
            {
                var coord = _camera.ScreenToTile(new Vector2(mouse.X, mouse.Y));
                // Discard clicks that land outside the valid tile grid (zoomed-out empty space)
                if (coord.X < 0 || coord.X >= snapshot.WorldTileWidth
                 || coord.Y < 0 || coord.Y >= snapshot.WorldTileHeight)
                    return;
                _selectionBus?.Select(new EntityRef(SelectionKind.Tile, 0, coord));
                // In spotlight mode: map click also sets move intent
                if (_spotlightCharacterId.HasValue)
                    _commandQueue.Enqueue(new SetSpotlightMoveIntent(coord));
            }
        }

        // M8.4.4/8.5.4: a pending Help or Settings rebind captures the next keypress instead of
        // dispatching it as a normal shortcut — otherwise rebinding a key would also fire
        // whatever it used to do. At most one of the two is ever awaiting at a time in practice
        // (only one panel is visible/interactive), but both are checked defensively.
        bool capturedForRebind = false;
        if (_helpPanel is not null || _settingsPanel is not null)
        {
            bool ctrlDown = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
            foreach (var key in kb.GetPressedKeys())
            {
                if (_prevKb.IsKeyDown(key)) continue;
                if (key is Keys.LeftControl or Keys.RightControl) continue;
                if (_helpPanel?.TryCaptureKey(key, ctrlDown) == true) { capturedForRebind = true; break; }
                if (_settingsPanel?.TryCaptureKey(key, ctrlDown) == true) { capturedForRebind = true; break; }
            }
        }

        // All keyboard shortcuts flow through the single registry (M6.1.3), so keys and the
        // visible UI controls added in later stories share one code path.
        // DECISION: T is Territory overlay (M3 Phase 3.4). Temperature moved off keyboard.
        if (!capturedForRebind)
            _keybinds?.Process(kb, _prevKb);

        // Timeline scrubber
        if (_timeline is not null && _layoutHost is not null)
        {
            var timelineRect = _layoutHost.Slot(RegionSlot.Timeline).Bounds;
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
        // Stop the sim thread before touching shared state.
        _simLoop?.Stop();
        _simLoop = null;

        // Reset the event store in-place (drops and recreates tables) rather than
        // closing, deleting, and reopening world.db. On Windows the WAL lock is held
        // by SQLite's finalizer even after Dispose(), so file deletion races and
        // crashes the game if all retries are exhausted. Reusing the open connection
        // avoids the lock entirely.
        if (_eventStore is not null)
        {
            _eventStore.Reset();
            _historyQuery = _eventStore.GetHistoryQuery();
        }

        // Phase 3.6: delete save directory so the next startup shows no resume prompt
        WorldStateSaver.DeleteSave(SaveDir);

        // Reset state flags
        _simStarted         = false;
        _simCrashReported   = false;
        _lastBucketLoadYear = -1;
        if (_crashLabel is not null) _crashLabel.Visible = false;

        // Dispose timeline texture
        _timeline?.Dispose();
        _timeline = null;

        // Drop all dock/float registrations so the next StartSim rebuilds them from scratch
        // without stale roots accumulating (M8 8.1: replaces PanelManager.ResetRegistrations).
        _workspace?.Reset();
        _keybinds = null;   // rebuilt by StartSim against the fresh panels

        _commandQueue.Enqueue(new ExitSpotlight());
        _spotlightCharacterId = null;
        _godModePanel = null;
        if (_desktop?.Root is Panel dp && _timeline is not null)
            dp.Widgets.Remove(_timeline.ScrubLabel);
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
        StartNewWorldGen();
    }

    // ── Phase 3.6 helpers ─────────────────────────────────────────────────────

    private void StartNewWorldGen()
    {
        var worldCfg = new WorldConfig { Seed = Environment.TickCount, WidthKm = 2000, HeightKm = 1600, TileWidthKm = 10 };
        var simCfg   = SimConfigLoader.LoadOrCreateDefault();
        var progress = new Progress<(string, float)>(p => _genProgress.Enqueue(p));
        _genTask = Task.Run(() => GenerateWorld(worldCfg, simCfg, progress));
    }

    /// <summary>Shows a modal "Resume saved world?" prompt when a save exists at startup.</summary>
    private void ShowResumePrompt(MetaDto meta)
    {
        if (_desktop is null) return;

        var content = new VerticalStackPanel { Spacing = 8 };
        content.Widgets.Add(new Label
        {
            Text      = $"A saved world was found.\nYear {meta.SavedYear} — Seed {meta.Seed}",
            TextColor = Color.White
        });

        var btnRow = new HorizontalStackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

        var resumeBtn = new TextButton { Text = "Resume World" };
        resumeBtn.Click += (_, _) =>
        {
            // Dismiss dialog, start background load
            (_desktop.Root as Panel)?.Widgets
                .OfType<Window>().FirstOrDefault()?.Close();
            var simCfg = SimConfigLoader.LoadOrCreateDefault();
            _genScreen?.Update("Loading save...", 0.5f);
            _genScreen!.Root.Visible = true;
            _loadTask = Task.Run(() => WorldStateSaver.Load(SaveDir, simCfg));
        };

        var newBtn = new TextButton { Text = "New World" };
        newBtn.Click += (_, _) =>
        {
            (_desktop.Root as Panel)?.Widgets
                .OfType<Window>().FirstOrDefault()?.Close();
            WorldStateSaver.DeleteSave(SaveDir);
            StartNewWorldGen();
        };

        btnRow.Widgets.Add(resumeBtn);
        btnRow.Widgets.Add(newBtn);
        content.Widgets.Add(btnRow);

        var window = new Window
        {
            Title   = "Resume Saved World?",
            Content = content,
            Width   = 320,
            Height  = 160
        };
        window.ShowModal(_desktop);
    }

    /// <summary>Called when the background load task completes. Wires up the sim without running world gen or spawning.</summary>
    private void StartSimFromLoad(WorldState world)
    {
        _genScreen!.Root.Visible = false;
        _loadTask = null;
        // spawnInitialEntities: false — entities were restored from the save
        StartSim(world, spawnInitialEntities: false);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        var snapshot = _stateCache.Read();
        if (_simStarted && snapshot is not null && _tileRenderer is not null && _spriteBatch is not null && _layoutHost is not null)
        {
            // Scissor-clip tile rendering to the MapCanvas region (framework §3.2: the region
            // that owns the rectangle also owns what draws inside it — chrome above it is opaque
            // anyway, so this is a clip, not a coordinate shift; nothing moves on screen).
            GraphicsDevice.ScissorRectangle = _layoutHost.Slot(RegionSlot.MapCanvas).Bounds;
            _spriteBatch.Begin(rasterizerState: new RasterizerState { ScissorTestEnable = true });
            _tileRenderer.Draw(_spriteBatch, snapshot);
            _spriteBatch.End();

            // Timeline bar — drawn below the map, no scissor
            if (_timeline is not null)
            {
                var timelineRect = _layoutHost.Slot(RegionSlot.Timeline).Bounds;
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
