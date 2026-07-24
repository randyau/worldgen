using Microsoft.Xna.Framework.Input;

namespace WorldEngine.UI.UI.Input;

/// <summary>When a binding fires: on the frame the key goes down, or every frame it is held.</summary>
public enum KeybindTrigger { Edge, Hold }

/// <summary>A key bound to a <see cref="CommandRegistry"/> command id (M8.4.2).</summary>
public sealed record KeyBinding(string CommandId, Keys Key, bool Ctrl, KeybindTrigger Trigger);

/// <summary>
/// The single source of truth for keyboard shortcuts. Binds keys to <see cref="CommandRegistry"/>
/// command ids (not raw delegates) so bindings are rebindable — <see cref="Bind"/> overwrites
/// whatever key a command currently has, and the reverse: binding a key that another command
/// already holds displaces that command (last-wins; framework's own conflict-resolution UI is
/// out of scope here — this is the simplest policy that can't deadlock a rebind).
/// </summary>
// DECISION: last-wins conflict policy on rebind, not reject-duplicate — simpler, and rejecting
// would require a dialog/toast to explain the rejection, which is 8.4.4/8.5 surface we don't
// have a way to visually verify yet. Revisit if playtesting finds accidental clobbers common.
// MAP: Central keybind table — binds keys to CommandRegistry ids; HelpPanel renders from both.
public sealed class KeybindRegistry
{
    private readonly CommandRegistry _commands;
    private readonly Dictionary<string, KeyBinding> _byCommandId = new();

    public KeybindRegistry(CommandRegistry commands) => _commands = commands;

    /// <summary>All current bindings, one per bound command.</summary>
    public IReadOnlyCollection<KeyBinding> Bindings => _byCommandId.Values;

    /// <summary>Binds (or rebinds) a command to a key. Displaces any other command already on that key.</summary>
    public void Bind(string commandId, Keys key, bool ctrl = false, KeybindTrigger? trigger = null)
    {
        var cmd = _commands.ById(commandId);
        var t = trigger ?? cmd?.Trigger ?? KeybindTrigger.Edge;

        foreach (var (id, b) in _byCommandId)
            if (b.Key == key && b.Ctrl == ctrl && id != commandId)
                _byCommandId.Remove(id);

        _byCommandId[commandId] = new KeyBinding(commandId, key, ctrl, t);
    }

    /// <summary>Binds every command that declared a <see cref="UiCommand.DefaultKey"/>.</summary>
    public void LoadDefaults()
    {
        foreach (var cmd in _commands.Commands)
            if (cmd.DefaultKey is { } key)
                Bind(cmd.Id, key, cmd.DefaultCtrl, cmd.Trigger);
    }

    public KeyBinding? BindingFor(string commandId) => _byCommandId.TryGetValue(commandId, out var b) ? b : null;

    /// <summary>Evaluates every binding against the current and previous keyboard state, invoking
    /// the matching command (edge = key just pressed; hold = key down this frame).</summary>
    public void Process(KeyboardState kb, KeyboardState prev)
    {
        bool ctrlDown = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
        foreach (var b in _byCommandId.Values)
        {
            if (b.Ctrl && !ctrlDown) continue;
            bool fire = b.Trigger == KeybindTrigger.Hold
                ? kb.IsKeyDown(b.Key)
                : kb.IsKeyDown(b.Key) && !prev.IsKeyDown(b.Key);
            if (fire) _commands.Invoke(b.CommandId);
        }
    }

    /// <summary>Human-readable accelerator label for a binding (e.g. "Ctrl+S", "?", "Esc").</summary>
    public static string KeyLabel(KeyBinding b)
    {
        string k = b.Key switch
        {
            Keys.OemQuestion => "?",
            Keys.Escape      => "Esc",
            _                => b.Key.ToString()
        };
        return b.Ctrl ? $"Ctrl+{k}" : k;
    }
}
