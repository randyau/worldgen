using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI;

// Myra 1.6 removed the convenience `TextButton`/`CheckBox` widgets in favor of the generic
// content-based `Button`/`CheckButton` (you now set a child `Label` as Content). These thin
// shims re-introduce the old `Text`/`TextColor` surface so our panels keep their concise
// `new TextButton { Text = "…" }` style against the new API. Because they live in this
// namespace, unqualified `TextButton`/`CheckBox` in WorldEngine.UI resolve here.

/// <summary>A button whose content is a single text <see cref="Label"/> (Myra 1.6 compat).</summary>
public sealed class TextButton : Button
{
    private readonly Label _label = new();

    public TextButton() => Content = _label;

    public string Text      { get => _label.Text;      set => _label.Text = value; }
    public Color  TextColor { get => _label.TextColor; set => _label.TextColor = value; }
}

/// <summary>A check button whose label is a single text <see cref="Label"/> (Myra 1.6 compat).</summary>
public sealed class CheckBox : CheckButton
{
    private readonly Label _label = new();

    public CheckBox() => Content = _label;

    public string Text { get => _label.Text; set => _label.Text = value; }
}
