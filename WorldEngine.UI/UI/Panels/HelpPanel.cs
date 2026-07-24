using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Help panel: hosts KeybindEditor + static workflow cards (M8.4.3/8.4.4).
/// <summary>
/// "?"-toggled panel listing every command via <see cref="KeybindEditor"/>, plus the God-Mode/
/// Spotlight workflow cards. Rendered directly from <see cref="CommandRegistry"/>/
/// <see cref="KeybindRegistry"/> so it can never drift from actual input handling.
/// </summary>
public sealed class HelpPanel : IToggleablePanel
{
    private readonly KeybindEditor _editor;
    private readonly WeVStack _content = new(UiTheme.Space.Xs);

    public string Id => "help";
    public string Title => "Help (?)";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public HelpPanel(CommandRegistry commands, KeybindRegistry keybinds, Action? onKeybindsChanged = null)
    {
        _editor = new KeybindEditor(commands, keybinds, onKeybindsChanged);
    }

    public Widget Build()
    {
        _content.Add(_editor);
        _content.Add(SectionHeader.Build("GOD MODE (F2)"));
        _content.Add(new WeText("  1. Click map tile to select target"));
        _content.Add(new WeText("  2. Pause (Space), then choose action"));
        _content.Add(new WeText("  Nudge: open Watch (W) first to select character"));
        _content.Add(SectionHeader.Build("SPOTLIGHT (W panel)"));
        _content.Add(new WeText("  Open Watch (W) → [Enter Spotlight]"));
        _content.Add(new WeText("  Click map tile → move intent"));
        _content.Add(new WeText("  Goal buttons → bias character behavior"));
        _content.Add(new WeText("  Character remains autonomous; intent biases decisions"));
        return PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });
    }

    public void Bind(PanelContext ctx) { }
    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;

    public void Refresh() { /* rebuilt reactively by the editor's own rebind/reset handlers */ }

    /// <summary>Forwards to the hosted <see cref="KeybindEditor"/>; see its doc for capture semantics.</summary>
    public bool TryCaptureKey(Microsoft.Xna.Framework.Input.Keys key, bool ctrl) => _editor.TryCaptureKey(key, ctrl);
}
