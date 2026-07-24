using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Help panel listing all shortcuts, generated from KeybindRegistry (M8.3.6).
/// <summary>
/// "?"-toggled panel listing every keyboard shortcut, grouped by category. Rendered directly
/// from the <see cref="KeybindRegistry"/> so it can never drift from actual input handling
/// (M6 Epic 6.1.3).
/// </summary>
// DECISION: rendering from CommandRegistry (framework §8.3.6) is deferred to land alongside
// 8.4, which introduces that registry; this migration only moves the panel onto the kit.
public sealed class HelpOverlayPanel : IToggleablePanel
{
    private readonly WeVStack _content = new(UiTheme.Space.Xs);

    public string Id => "help";
    public string Title => "Help (?)";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) { }
    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;
    public void Refresh() { /* content is static once Populate() runs */ }

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;

    /// <summary>Rebuilds the shortcut list from the registry. Call once after the registry is built.</summary>
    public void Populate(KeybindRegistry registry)
    {
        _content.Clear();
        foreach (var group in registry.Bindings.GroupBy(b => b.Category))
        {
            _content.Add(SectionHeader.Build(group.Key.ToUpperInvariant()));
            foreach (var b in group)
                _content.Add(new WeText($"  {KeybindRegistry.KeyLabel(b),-10}  {b.Label}"));
        }

        // Static section: button-based flows that have no keyboard shortcut
        _content.Add(SectionHeader.Build("GOD MODE (F2)"));
        _content.Add(new WeText("  1. Click map tile to select target"));
        _content.Add(new WeText("  2. Pause (Space), then choose action"));
        _content.Add(new WeText("  Nudge: open Watch (W) first to select character"));
        _content.Add(SectionHeader.Build("SPOTLIGHT (W panel)"));
        _content.Add(new WeText("  Open Watch (W) → [Enter Spotlight]"));
        _content.Add(new WeText("  Click map tile → move intent"));
        _content.Add(new WeText("  Goal buttons → bias character behavior"));
        _content.Add(new WeText("  Character remains autonomous; intent biases decisions"));
    }
}
