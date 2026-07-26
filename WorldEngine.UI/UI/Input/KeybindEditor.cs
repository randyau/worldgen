using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Input;

// MAP: Reusable rebind-row list over CommandRegistry/KeybindRegistry (M8.4.4/8.5.4) — hosted by both HelpPanel and SettingsPanel's Controls tab.
/// <summary>
/// Renders every <see cref="CommandRegistry"/> command grouped by category, each with its current
/// key and [Rebind]/[Reset] affordances. Capture-next-keypress is driven externally via
/// <see cref="TryCaptureKey"/> (the host feeds keyboard input from its own per-frame poll).
/// </summary>
public sealed class KeybindEditor : IWeWidget
{
    private readonly CommandRegistry _commands;
    private readonly KeybindRegistry _keybinds;
    private readonly Action? _onChanged;
    private readonly WeVStack _root = new(UiTheme.Space.Xs);
    private string? _awaitingCommandId;

    public Widget Root => _root.Root;

    public KeybindEditor(CommandRegistry commands, KeybindRegistry keybinds, Action? onChanged = null)
    {
        _commands  = commands;
        _keybinds  = keybinds;
        _onChanged = onChanged;
        Rebuild();
    }

    /// <summary>
    /// If a rebind is pending, binds the awaited command to <paramref name="key"/> and consumes
    /// it (returns true so the host skips normal keybind dispatch for this key this frame).
    /// </summary>
    public bool TryCaptureKey(Keys key, bool ctrl)
    {
        if (_awaitingCommandId is null) return false;
        _keybinds.Bind(_awaitingCommandId, key, ctrl);
        _awaitingCommandId = null;
        Rebuild();
        _onChanged?.Invoke();
        return true;
    }

    public void Rebuild()
    {
        _root.Clear();

        if (_awaitingCommandId is { } pendingId)
        {
            var pendingCmd = _commands.ById(pendingId);
            _root.Add(new WeText($"Press a key to bind \"{pendingCmd?.Label}\"…", color: UiTheme.ColorRole.AccentInteractive));
        }

        foreach (var group in _commands.Commands.GroupBy(c => c.Category))
        {
            _root.Add(SectionHeader.Build(group.Key.ToUpperInvariant()));
            foreach (var cmd in group)
            {
                var binding = _keybinds.BindingFor(cmd.Id);
                string keyLabel = binding is not null ? KeybindRegistry.KeyLabel(binding) : "(unbound)";

                var row = new WeHStack(UiTheme.Space.Sm);
                row.Add(new WeText($"{keyLabel,-10}", color: UiTheme.ColorRole.TextPrimary));
                row.Add(new WeText(cmd.Label, color: UiTheme.ColorRole.TextSecondary));

                string capturedId = cmd.Id;
                var rebindBtn = new WeButton("[Rebind]", () => { _awaitingCommandId = capturedId; Rebuild(); });
                row.Add(rebindBtn);

                if (cmd.DefaultKey is { } defaultKey)
                {
                    var resetBtn = new WeButton("[Reset]", () =>
                    {
                        _keybinds.Bind(capturedId, defaultKey, cmd.DefaultCtrl, cmd.Trigger);
                        Rebuild();
                        _onChanged?.Invoke();
                    }, WeButtonVariant.Ghost);
                    row.Add(resetBtn);
                }

                _root.Add(row);
            }
        }

        var resetAllBtn = new WeButton("[Reset All to Defaults]",
            () => { _keybinds.LoadDefaults(); Rebuild(); _onChanged?.Invoke(); }, WeButtonVariant.Danger);
        _root.Add(resetAllBtn);
    }
}
