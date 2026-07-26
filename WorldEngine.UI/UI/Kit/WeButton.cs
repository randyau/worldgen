using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

/// <summary>Visual weight of a <see cref="WeButton"/> (framework §4.1).</summary>
public enum WeButtonVariant { Primary, Ghost, Link, Toggle, Danger }

// MAP: Layer 1 — tokenized button wrapping the TextButton compat shim.
/// <summary>Text button with a fixed set of tokenized visual variants; wraps <see cref="TextButton"/>.</summary>
public sealed class WeButton : IWeWidget
{
    private readonly TextButton _button = new();
    private readonly WeButtonVariant _variant;

    public Widget Root => _button;

    public WeButton(string text, Action onClick, WeButtonVariant variant = WeButtonVariant.Primary)
    {
        _variant = variant;
        _button.Text = text;
        _button.Click += (_, _) => onClick();
        ApplyVariant();
    }

    /// <summary>For <see cref="WeButtonVariant.Toggle"/>: whether the toggle is currently on.</summary>
    public bool Active
    {
        get => _button.TextColor == UiTheme.AccentInteractive;
        set => _button.TextColor = value ? UiTheme.AccentInteractive : UiTheme.TextPrimary;
    }

    /// <summary>Disabled buttons render dimmed and stop firing Click (framework §4.1 affordance rule).</summary>
    public bool Enabled
    {
        get => _button.Enabled;
        set => _button.Enabled = value;
    }

    public bool Visible
    {
        get => _button.Visible;
        set => _button.Visible = value;
    }

    public int? Width
    {
        get => _button.Width;
        set => _button.Width = value;
    }

    public int? Height
    {
        get => _button.Height;
        set => _button.Height = value;
    }

    public Thickness Padding
    {
        get => _button.Padding;
        set => _button.Padding = value;
    }

    public string Text
    {
        get => _button.Text;
        set => _button.Text = value;
    }

    private void ApplyVariant()
    {
        _button.TextColor = _variant switch
        {
            WeButtonVariant.Ghost  => UiTheme.TextSecondary,
            WeButtonVariant.Link   => UiTheme.AccentInteractive,
            WeButtonVariant.Danger => UiTheme.StateNegative,
            _                      => UiTheme.TextPrimary
        };
    }
}
