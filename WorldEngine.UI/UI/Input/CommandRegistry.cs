using Microsoft.Xna.Framework.Input;

namespace WorldEngine.UI.UI.Input;

// MAP: Named-action layer under keybinds — every user action has exactly one definition (M8.4.1).
/// <summary>
/// A single named user action. The <see cref="Handler"/> is the one place the behavior lives —
/// both a keybind and any UI button invoke the same command by <see cref="Id"/>, so keys and
/// visible controls can never diverge (framework §9.1, continuing M6 Epic 6.1.3's "UI-primary").
/// </summary>
public readonly record struct UiCommand(
    string Id, string Label, string Category, Action Handler, Keys? DefaultKey = null,
    bool DefaultCtrl = false, KeybindTrigger Trigger = KeybindTrigger.Edge);

/// <summary>
/// The set of all named user actions. <see cref="KeybindRegistry"/> binds keys to command ids and
/// invokes them here; UI buttons can invoke the same id directly.
/// </summary>
// MOD SEAM: a mod could register additional UiCommands here.
public sealed class CommandRegistry
{
    private readonly List<UiCommand> _commands = new();
    private readonly Dictionary<string, UiCommand> _byId = new();

    /// <summary>All registered commands, in registration order.</summary>
    public IReadOnlyList<UiCommand> Commands => _commands;

    public UiCommand Register(UiCommand cmd)
    {
        _commands.Add(cmd);
        _byId[cmd.Id] = cmd;
        return cmd;
    }

    public UiCommand? ById(string id) => _byId.TryGetValue(id, out var cmd) ? cmd : null;

    public void Invoke(string id) => ById(id)?.Handler();
}
