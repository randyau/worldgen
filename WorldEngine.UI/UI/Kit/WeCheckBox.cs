using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI.Kit;

using CheckBox = WorldEngine.UI.UI.CheckBox;

// MAP: Layer 1 — labeled toggle wrapping the CheckBox compat shim.
/// <summary>Labeled checkbox. Wraps <see cref="CheckBox"/>.</summary>
public sealed class WeCheckBox : IWeWidget
{
    private readonly CheckBox _box;

    public Widget Root => _box;

    public event Action? Changed;

    public WeCheckBox(string label, bool isChecked = false)
    {
        _box = new CheckBox { Text = label, IsChecked = isChecked };
        _box.IsCheckedChanged += (_, _) => Changed?.Invoke();
    }

    public bool IsChecked
    {
        get => _box.IsChecked;
        set => _box.IsChecked = value;
    }
}
