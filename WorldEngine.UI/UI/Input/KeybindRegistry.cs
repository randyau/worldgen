using Microsoft.Xna.Framework.Input;

namespace WorldEngine.UI.UI.Input;

/// <summary>When a binding fires: on the frame the key goes down, or every frame it is held.</summary>
public enum KeybindTrigger { Edge, Hold }

/// <summary>
/// A single keyboard shortcut. The <see cref="Action"/> is the one place the behavior lives —
/// both the key handler and any UI button wire to the same delegate, so keys and visible
/// controls can never diverge (M6 Epic 6.1.3, "UI-primary" interaction).
/// </summary>
public sealed record Keybind(
    Keys Key,
    bool Ctrl,
    string Label,
    string Category,
    Action Action,
    KeybindTrigger Trigger = KeybindTrigger.Edge);

/// <summary>
/// The single source of truth for keyboard shortcuts. <c>Game1</c> drives it from input each
/// frame; the help overlay renders directly from <see cref="Bindings"/> so the two cannot drift.
/// </summary>
// MAP: Central keybind table — single source of truth for shortcuts, driven by Game1 and shown by HelpOverlayPanel.
public sealed class KeybindRegistry
{
    private readonly List<Keybind> _binds = new();

    /// <summary>All registered bindings, in registration order.</summary>
    public IReadOnlyList<Keybind> Bindings => _binds;

    public Keybind Register(Keybind bind)
    {
        _binds.Add(bind);
        return bind;
    }

    public Keybind Register(Keys key, string label, string category, Action action,
        KeybindTrigger trigger = KeybindTrigger.Edge, bool ctrl = false)
        => Register(new Keybind(key, ctrl, label, category, action, trigger));

    /// <summary>
    /// Evaluates every binding against the current and previous keyboard state, invoking the
    /// action of each that matches (edge = key just pressed; hold = key down this frame).
    /// </summary>
    public void Process(KeyboardState kb, KeyboardState prev)
    {
        bool ctrlDown = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
        foreach (var b in _binds)
        {
            if (b.Ctrl && !ctrlDown) continue;
            bool fire = b.Trigger == KeybindTrigger.Hold
                ? kb.IsKeyDown(b.Key)
                : kb.IsKeyDown(b.Key) && !prev.IsKeyDown(b.Key);
            if (fire) b.Action();
        }
    }

    /// <summary>Human-readable accelerator label for a binding (e.g. "Ctrl+S", "?", "Esc").</summary>
    public static string KeyLabel(Keybind b)
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
