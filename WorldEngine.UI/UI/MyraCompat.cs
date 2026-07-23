using System.Collections.Generic;
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

/// <summary>
/// A selectable list item for use in <see cref="ComboBox"/> (Myra 1.6 compat).
/// Wraps a Button with a text Label so it implements ISelectorItem.
/// </summary>
public sealed class ListItem : Button
{
    private readonly Label _label = new();

    public ListItem(string text)
    {
        _label.Text = text;
        Content = _label;
    }

    public string? Text { get => _label.Text; set => _label.Text = value ?? ""; }
}

/// <summary>
/// Dropdown selector widget (Myra 1.6 compat). Wraps <see cref="ComboView"/> with a
/// string-item-friendly API matching the old Myra ComboBox surface.
/// </summary>
public sealed class ComboBox : ComboView
{
    /// <summary>Collection of <see cref="ListItem"/> entries (alias for Widgets).</summary>
    public IList<Widget> Items => Widgets;

    /// <summary>Index of the selected item (0-based), or null if none.</summary>
    public new int? SelectedIndex
    {
        get => base.SelectedIndex;
        set => base.SelectedIndex = value;
    }

    /// <summary>Currently selected <see cref="ListItem"/>, or null.</summary>
    public new ListItem? SelectedItem => base.SelectedItem as ListItem;
}
