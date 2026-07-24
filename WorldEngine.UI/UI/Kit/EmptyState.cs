using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Kit;

/// <summary>Which standard empty-state treatment applies (framework §7.5).</summary>
public enum EmptyStateKind { PreSim, NotBuiltYet, FilteredEmpty }

// MAP: Layer 2 — standard "no data" treatment so no panel ever renders an ambiguous blank area.
/// <summary>Standard empty-state treatment: icon-free message + optional hint (framework §7.5).</summary>
public static class EmptyState
{
    public static Widget Build(EmptyStateKind kind, string message, string? hint = null)
    {
        var stack = new VerticalStackPanel { Spacing = UiTheme.Space.Xs };
        stack.Widgets.Add(new Label { Text = message, TextColor = UiTheme.TextMuted });
        if (hint is not null)
            stack.Widgets.Add(new Label { Text = hint, TextColor = UiTheme.TextDisabled });
        return stack;
    }
}
