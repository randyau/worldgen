using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — typed dropdown wrapping the ComboBox compat shim.
/// <summary>Typed combo box. Wraps <see cref="ComboBox"/> with a strongly-typed item list.</summary>
public sealed class WeDropdown<T> : IWeWidget
{
    private readonly ComboBox _combo = new();
    private List<T> _items = new();
    private Func<T, string> _render = x => x?.ToString() ?? string.Empty;

    public Widget Root => _combo;

    public event Action<T>? OnChanged;

    public IReadOnlyList<T> Items => _items;

    public void Render(Func<T, string> render) => _render = render;

    public void SetItems(IEnumerable<T> items)
    {
        _items = items.ToList();
        _combo.Items.Clear();
        foreach (var item in _items)
            _combo.Items.Add(new ListItem(_render(item)));
    }

    public T? Selected
    {
        get => _combo.SelectedIndex is { } i && i >= 0 && i < _items.Count ? _items[i] : default;
        set
        {
            int idx = value is null ? -1 : _items.IndexOf(value);
            _combo.SelectedIndex = idx >= 0 ? idx : null;
        }
    }

    public WeDropdown()
    {
        _combo.SelectedIndexChanged += (_, _) =>
        {
            if (Selected is { } sel) OnChanged?.Invoke(sel);
        };
    }
}
