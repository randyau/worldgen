using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 2 — tokenized section divider, replaces the AddLine("--- X ---") idiom.
/// <summary>Section divider label (framework §4.2). Replaces <c>AddLine("--- X ---")</c>.</summary>
public static class SectionHeader
{
    public static Widget Build(string text) =>
        new Label { Text = text, TextColor = UiTheme.TextHeader };
}
