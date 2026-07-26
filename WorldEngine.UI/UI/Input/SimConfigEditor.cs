using Myra.Graphics2D.UI;
using WorldEngine.Sim.Config;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Input;

// MAP: Reusable sim-config editor over ConfigRegistry (M10 10.2) — hosted by SettingsPanel's
// Simulation tab. Group picker + per-group field list, mirroring KeybindEditor's structure.
/// <summary>
/// Renders one <see cref="ConfigRegistry"/> group at a time (picked via dropdown) as
/// <see cref="WeField"/>/<see cref="WeCheckBox"/> rows bound directly to the live
/// <see cref="SimConfig"/> the running sim reads — edits apply immediately, same as the M10 10.1
/// worldgen-preview sea-level field. Values are not written back to sim_config.toml; the file
/// stays the tuned baseline (see the M10 index doc DECISION on "default").
/// </summary>
public sealed class SimConfigEditor : IWeWidget
{
    private readonly SimConfig _live;
    private readonly SimConfig _defaults;
    private readonly WeVStack _root = new(UiTheme.Space.Sm);
    private readonly WeDropdown<string> _groupDropdown = new();
    private readonly WeVStack _fieldList = new(UiTheme.Space.Xs);
    private IReadOnlyList<ConfigRegistry.Entry> _entries = [];

    public Widget Root => _root.Root;

    public SimConfigEditor(SimConfig live, SimConfig defaults)
    {
        _live = live;
        _defaults = defaults;
        _entries = ConfigRegistry.Build(_live, _defaults);

        var groups = _entries.Select(e => e.Group).Distinct().OrderBy(g => g).ToList();
        _groupDropdown.SetItems(groups);
        _groupDropdown.OnChanged += _ => RebuildFieldList();

        var pickerRow = new WeHStack(UiTheme.Space.Sm);
        pickerRow.Add(new WeText("Section:", color: UiTheme.ColorRole.TextSecondary));
        pickerRow.Add(_groupDropdown);
        var resetGroupBtn = new WeButton("[Reset Section]", ResetActiveGroup, WeButtonVariant.Danger);
        pickerRow.Add(resetGroupBtn);

        _root.Add(pickerRow);
        _root.Add(_fieldList);

        if (groups.Count > 0)
        {
            _groupDropdown.Selected = groups[0];
            RebuildFieldList();
        }
    }

    private void RebuildFieldList()
    {
        _fieldList.Clear();
        string? group = _groupDropdown.Selected;
        if (group is null) return;

        foreach (var entry in _entries.Where(e => e.Group == group).OrderBy(e => e.Path))
            _fieldList.Add(BuildRow(entry));
    }

    private Widget BuildRow(ConfigRegistry.Entry entry)
    {
        var row = new WeHStack(UiTheme.Space.Sm);
        var modifiedTag = new WeText("", color: UiTheme.ColorRole.AccentInteractive);

        void UpdateTag() => modifiedTag.Text = entry.IsModified ? "(modified)" : "";

        if (entry.Kind == ConfigValueKind.Bool)
        {
            var box = new WeCheckBox(entry.Path, (bool)entry.Get());
            box.Changed += () => { entry.Set(box.IsChecked); UpdateTag(); };
            row.Add(box);
        }
        else
        {
            var field = new WeField(entry.Path) { Value = entry.Get().ToString() ?? string.Empty };
            field.Changed += () =>
            {
                if (TryParse(entry.Kind, field.Value, out var parsed))
                {
                    entry.Set(parsed);
                    field.ValidationState = WeValidationState.Normal;
                }
                else
                {
                    field.ValidationState = WeValidationState.Invalid;
                }
                UpdateTag();
            };
            row.Add(field);
        }

        var resetBtn = new WeButton("[Reset]", () =>
        {
            entry.Set(entry.Default);
            RebuildFieldList();
        }, WeButtonVariant.Ghost);
        row.Add(resetBtn);
        row.Add(modifiedTag);

        UpdateTag();
        return row.Root;
    }

    private void ResetActiveGroup()
    {
        string? group = _groupDropdown.Selected;
        if (group is null) return;
        foreach (var entry in _entries.Where(e => e.Group == group))
            entry.Set(entry.Default);
        RebuildFieldList();
    }

    private static bool TryParse(ConfigValueKind kind, string text, out object value)
    {
        switch (kind)
        {
            case ConfigValueKind.Int:
                if (int.TryParse(text, out int i)) { value = i; return true; }
                break;
            case ConfigValueKind.Float:
                if (float.TryParse(text, out float f)) { value = f; return true; }
                break;
            case ConfigValueKind.Byte:
                if (byte.TryParse(text, out byte b)) { value = b; return true; }
                break;
            case ConfigValueKind.String:
                value = text;
                return true;
        }
        value = 0;
        return false;
    }
}
