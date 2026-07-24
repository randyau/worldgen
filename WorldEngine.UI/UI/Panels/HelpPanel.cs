using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Input;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Layout;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Panels;

// MAP: Layer 3 — Summoned Help panel: renders from CommandRegistry/KeybindRegistry + rebind capture (M8.4.3-8.4.4).
/// <summary>
/// "?"-toggled panel listing every command, grouped by category, with its current key and a
/// [Rebind] affordance. Rendered directly from <see cref="CommandRegistry"/>/<see cref="KeybindRegistry"/>
/// so it can never drift from actual input handling (M6 Epic 6.1.3, continued by M8.4).
/// </summary>
public sealed class HelpPanel : IToggleablePanel
{
    private readonly CommandRegistry _commands;
    private readonly KeybindRegistry _keybinds;
    private readonly WeVStack _content = new(UiTheme.Space.Xs);

    /// <summary>Non-null while waiting for the next keypress to rebind this command. Game1 feeds
    /// the next key into <see cref="TryCaptureKey"/> before normal keybind processing.</summary>
    private string? _awaitingRebindCommandId;

    public string Id => "help";
    public string Title => "Help (?)";
    public PanelPlacement Placement => new(PanelPlacementKind.Summoned);
    public bool IsVisible { get; private set; }

    public HelpPanel(CommandRegistry commands, KeybindRegistry keybinds)
    {
        _commands = commands;
        _keybinds = keybinds;
    }

    public Widget Build() => PanelFrame.Build(Title, _content.Root, new PanelFrameOptions { OnClose = Hide });

    public void Bind(PanelContext ctx) { }
    public EmptyStateSpec? EmptyFor(PanelContext ctx) => null;

    public void Show() { IsVisible = true; Rebuild(); }
    public void Hide() { IsVisible = false; _awaitingRebindCommandId = null; }

    public void Refresh() { /* rebuilt on Show()/rebind, not per-frame */ }

    /// <summary>
    /// If a rebind is pending, binds the awaited command to <paramref name="key"/> and consumes
    /// it (returns true so Game1 skips normal keybind dispatch for this key this frame).
    /// </summary>
    public bool TryCaptureKey(Microsoft.Xna.Framework.Input.Keys key, bool ctrl)
    {
        if (_awaitingRebindCommandId is null) return false;
        _keybinds.Bind(_awaitingRebindCommandId, key, ctrl);
        _awaitingRebindCommandId = null;
        Rebuild();
        return true;
    }

    private void Rebuild()
    {
        _content.Clear();

        if (_awaitingRebindCommandId is { } pendingId)
        {
            var pendingCmd = _commands.ById(pendingId);
            _content.Add(new WeText($"Press a key to bind \"{pendingCmd?.Label}\"…", color: UiTheme.ColorRole.AccentInteractive));
        }

        foreach (var group in _commands.Commands.GroupBy(c => c.Category))
        {
            _content.Add(SectionHeader.Build(group.Key.ToUpperInvariant()));
            foreach (var cmd in group)
            {
                var binding = _keybinds.BindingFor(cmd.Id);
                string keyLabel = binding is not null ? KeybindRegistry.KeyLabel(binding) : "(unbound)";

                var row = new WeHStack(UiTheme.Space.Sm);
                row.Add(new WeText($"{keyLabel,-10}", color: UiTheme.ColorRole.TextPrimary));
                row.Add(new WeText(cmd.Label, color: UiTheme.ColorRole.TextSecondary));

                string capturedId = cmd.Id;
                var rebindBtn = new TextButton { Text = "[Rebind]" };
                rebindBtn.Click += (_, _) => { _awaitingRebindCommandId = capturedId; Rebuild(); };
                row.Add(rebindBtn);

                _content.Add(row);
            }
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
