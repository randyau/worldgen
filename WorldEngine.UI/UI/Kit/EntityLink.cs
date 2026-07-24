using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Selection;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 2 — clickable entity reference; every nameable thing should render through this.
/// <summary>Clickable reference to a character/civ/settlement/tile; selects on click (framework §7.2).</summary>
public static class EntityLink
{
    public static Widget Build(EntityRef target, string text, ISelectionSink sink)
    {
        var btn = new TextButton { Text = text, TextColor = UiTheme.AccentInteractive };
        btn.Click += (_, _) => sink.Select(target);
        return btn;
    }
}
