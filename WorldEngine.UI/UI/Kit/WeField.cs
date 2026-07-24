using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

/// <summary>Validation state of a <see cref="WeField"/> (drives border color).</summary>
public enum WeValidationState { Normal, Invalid }

// MAP: Layer 1 — labeled text input wrapping Label + TextBox.
/// <summary>Labeled text input. Wraps a <see cref="Label"/> + <see cref="TextBox"/> pair.</summary>
public sealed class WeField : IWeWidget
{
    private readonly HorizontalStackPanel _root;
    private readonly TextBox _box = new();

    public Widget Root => _root;

    public event Action? Changed;

    public WeField(string label, string? placeholder = null)
    {
        _root = new HorizontalStackPanel { Spacing = UiTheme.Space.Sm };
        _root.Widgets.Add(new Label { Text = label, TextColor = UiTheme.TextSecondary });
        _box.HintText = placeholder ?? string.Empty;
        _box.TextChangedByUser += (_, _) => Changed?.Invoke();
        _root.Widgets.Add(_box);
    }

    public string Value
    {
        get => _box.Text ?? string.Empty;
        set => _box.Text = value;
    }

    public string Placeholder
    {
        get => _box.HintText ?? string.Empty;
        set => _box.HintText = value;
    }

    public WeValidationState ValidationState
    {
        set => _box.TextColor = value == WeValidationState.Invalid ? UiTheme.StateNegative : UiTheme.TextPrimary;
    }
}
