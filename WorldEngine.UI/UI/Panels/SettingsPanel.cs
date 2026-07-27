using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Settings;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Settings panel: Display + Controls + Simulation tabs, backed by
// UiPrefs (M8.5.2-8.5.4) and ConfigRegistry (M10 10.2).
/// <summary>
/// Settings shell: a left tab list (Display / Controls / Simulation) + right content, both inside
/// a <see cref="PanelFrame"/>. The Simulation tab is optional — omitted (constructor overload)
/// before a world/SimConfig exists, e.g. from the worldgen preview screen.
/// </summary>
// MOD SEAM: settings tab registry — a mod tab can be appended here without editing the shell,
// once there's a need to generalize this from the hardcoded tabs below into an actual registry.
public sealed class SettingsPanel : IToggleablePanel
{
    private readonly KeybindEditor _editor;
    private readonly SimConfigEditor? _simConfigEditor;
    private readonly Action<UiPrefs> _onChanged;
    private readonly WeVStack _body = new(UiTheme.Space.Sm);
    private UiPrefs _prefs;
    private string _activeTab = "display";

    public string Id => "settings";
    public string Title => "Settings";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public SettingsPanel(UiPrefs initialPrefs, CommandRegistry commands, KeybindRegistry keybinds,
        Action<UiPrefs> onChanged, SimConfig? liveSimConfig = null, SimConfig? defaultSimConfig = null)
    {
        _prefs     = initialPrefs;
        _onChanged = onChanged;
        _editor    = new KeybindEditor(commands, keybinds, onChanged: PersistKeybindOverrides);
        if (liveSimConfig is not null && defaultSimConfig is not null)
            _simConfigEditor = new SimConfigEditor(liveSimConfig, defaultSimConfig);
    }

    public Widget Build()
    {
        var tabRow = new WeHStack(UiTheme.Space.Sm);
        var displayBtn  = new WeButton("[Display]",  () => { _activeTab = "display";  RebuildBody(); });
        var controlsBtn = new WeButton("[Controls]", () => { _activeTab = "controls"; RebuildBody(); });
        tabRow.Add(displayBtn);
        tabRow.Add(controlsBtn);
        if (_simConfigEditor is not null)
        {
            var simBtn = new WeButton("[Simulation]", () => { _activeTab = "simulation"; RebuildBody(); });
            tabRow.Add(simBtn);
        }

        var root = new WeVStack(UiTheme.Space.Sm);
        root.Add(tabRow);
        root.Add(_body);
        RebuildBody();
        return PanelFrame.Build(Title, root.Root, new PanelFrameOptions { OnClose = Hide });
    }

    public void Bind(PanelContext ctx) { }

    // Rebuild on open: HelpPanel hosts a separate KeybindEditor instance over the same
    // KeybindRegistry, so a rebind made there wouldn't otherwise reach this editor until its
    // own rebind/reset handlers fire.
    public void Show() { IsVisible = true; _editor.Rebuild(); }
    public void Hide() => IsVisible = false;

    public void Refresh() { /* rebuilt reactively by tab clicks and field/editor callbacks */ }

    /// <summary>Forwards to the hosted <see cref="KeybindEditor"/>; see its doc for capture semantics.</summary>
    public bool TryCaptureKey(Microsoft.Xna.Framework.Input.Keys key, bool ctrl) => _editor.TryCaptureKey(key, ctrl);

    private void RebuildBody()
    {
        _body.Clear();
        if (_activeTab == "display") BuildDisplayTab();
        else if (_activeTab == "simulation" && _simConfigEditor is not null) BuildSimulationTab();
        else BuildControlsTab();
    }

    private void BuildDisplayTab()
    {
        _body.Add(SectionHeader.Build("Display"));

        var dockWidthField = new WeField("Dock width (px):", _prefs.DockWidth.ToString());
        dockWidthField.Value = _prefs.DockWidth.ToString();
        var applyBtn = new WeButton("[Apply]", () =>
        {
            if (int.TryParse(dockWidthField.Value, out int width))
                Persist(_prefs with { DockWidth = Math.Clamp(width, LayoutHost.MinDockWidth, LayoutHost.MaxDockWidth) });
        });
        var dockRow = new WeHStack(UiTheme.Space.Sm);
        dockRow.Add(dockWidthField);
        dockRow.Add(applyBtn);
        _body.Add(dockRow);

        // DECISION: these four are persisted but not yet applied live — see the DECISION comment
        // on UiPrefs itself for why (no theme-variant/animation system to hook into yet).
        var highContrast = new WeCheckBox("High Contrast (reserved — not yet applied)", _prefs.HighContrast);
        highContrast.Changed += () => Persist(_prefs with { HighContrast = highContrast.IsChecked });
        _body.Add(highContrast);

        var reduceMotion = new WeCheckBox("Reduce Motion (reserved — not yet applied)", _prefs.ReduceMotion);
        reduceMotion.Changed += () => Persist(_prefs with { ReduceMotion = reduceMotion.IsChecked });
        _body.Add(reduceMotion);

        var themeField = new WeField("Theme variant:", _prefs.ThemeVariant);
        themeField.Value = _prefs.ThemeVariant;
        themeField.Changed += () => Persist(_prefs with { ThemeVariant = themeField.Value });
        _body.Add(themeField);

        var densityField = new WeField("Density:", _prefs.Density);
        densityField.Value = _prefs.Density;
        densityField.Changed += () => Persist(_prefs with { Density = densityField.Value });
        _body.Add(densityField);
    }

    private void BuildControlsTab()
    {
        _body.Add(SectionHeader.Build("Controls"));
        _body.Add(_editor);
    }

    private void BuildSimulationTab()
    {
        _body.Add(SectionHeader.Build("Simulation"));
        _body.Add(_simConfigEditor!);
    }

    private void Persist(UiPrefs updated)
    {
        _prefs = updated;
        _onChanged(_prefs);
    }

    private void PersistKeybindOverrides() => _onChanged(_prefs);
}
