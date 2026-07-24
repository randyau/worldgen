using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Settings;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Settings panel: Display + Controls tabs, backed by UiPrefs (M8.5.2-8.5.4).
/// <summary>
/// Settings shell: a left tab list (Display / Controls) + right content, both inside a
/// <see cref="PanelFrame"/>. The simulation-config tab is explicitly out of scope for M8 (lands
/// in M10) — tabs are registered by id here so that tab is a drop-in later.
/// </summary>
// MOD SEAM: settings tab registry — a `simconfig` tab (M10) or a mod tab can be appended here
// without editing the shell, once there's a second real consumer to justify generalizing this
// from the two hardcoded tabs below into an actual registry.
public sealed class SettingsPanel : IToggleablePanel
{
    private readonly KeybindEditor _editor;
    private readonly Action<UiPrefs> _onChanged;
    private readonly WeVStack _body = new(UiTheme.Space.Sm);
    private UiPrefs _prefs;
    private string _activeTab = "display";

    public string Id => "settings";
    public string Title => "Settings";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public SettingsPanel(UiPrefs initialPrefs, CommandRegistry commands, KeybindRegistry keybinds, Action<UiPrefs> onChanged)
    {
        _prefs     = initialPrefs;
        _onChanged = onChanged;
        _editor    = new KeybindEditor(commands, keybinds, onChanged: PersistKeybindOverrides);
    }

    public Widget Build()
    {
        var tabRow = new WeHStack(UiTheme.Space.Sm);
        var displayBtn  = new TextButton { Text = "[Display]" };
        var controlsBtn = new TextButton { Text = "[Controls]" };
        displayBtn.Click  += (_, _) => { _activeTab = "display";  RebuildBody(); };
        controlsBtn.Click += (_, _) => { _activeTab = "controls"; RebuildBody(); };
        tabRow.Add(displayBtn);
        tabRow.Add(controlsBtn);

        var root = new WeVStack(UiTheme.Space.Sm);
        root.Add(tabRow);
        root.Add(_body);
        RebuildBody();
        return PanelFrame.Build(Title, root.Root, new PanelFrameOptions { OnClose = Hide });
    }

    public void Bind(PanelContext ctx) { }
    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;

    public void Refresh() { /* rebuilt reactively by tab clicks and field/editor callbacks */ }

    /// <summary>Forwards to the hosted <see cref="KeybindEditor"/>; see its doc for capture semantics.</summary>
    public bool TryCaptureKey(Microsoft.Xna.Framework.Input.Keys key, bool ctrl) => _editor.TryCaptureKey(key, ctrl);

    private void RebuildBody()
    {
        _body.Clear();
        if (_activeTab == "display") BuildDisplayTab();
        else BuildControlsTab();
    }

    private void BuildDisplayTab()
    {
        _body.Add(SectionHeader.Build("Display"));

        var dockWidthField = new WeField("Dock width (px):", _prefs.DockWidth.ToString());
        dockWidthField.Value = _prefs.DockWidth.ToString();
        var applyBtn = new TextButton { Text = "[Apply]" };
        applyBtn.Click += (_, _) =>
        {
            if (int.TryParse(dockWidthField.Value, out int width))
                Persist(_prefs with { DockWidth = Math.Clamp(width, LayoutHost.MinDockWidth, LayoutHost.MaxDockWidth) });
        };
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

    private void Persist(UiPrefs updated)
    {
        _prefs = updated;
        _onChanged(_prefs);
    }

    private void PersistKeybindOverrides() => _onChanged(_prefs);
}
