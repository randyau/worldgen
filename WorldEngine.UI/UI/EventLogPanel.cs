using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

public sealed class EventLogPanel
{
    public readonly Panel Root;
    private readonly VerticalStackPanel _rows;
    private readonly ScrollViewer _scroll;

    private bool _showHeadline = true;
    private bool _showRegional = true;
    private bool _showBackground;

    public EventLogPanel()
    {
        _rows  = new VerticalStackPanel { Spacing = 2 };
        _scroll = new ScrollViewer { Content = _rows, Width = 380, Height = 250 };

        var headlineBox  = new CheckBox { Text = "Headline",   IsChecked = _showHeadline };
        var regionalBox  = new CheckBox { Text = "Regional",   IsChecked = _showRegional };
        var backgroundBox = new CheckBox { Text = "Background", IsChecked = _showBackground };

        headlineBox.PressedChanged   += (_, _) => _showHeadline   = headlineBox.IsChecked;
        regionalBox.PressedChanged   += (_, _) => _showRegional   = regionalBox.IsChecked;
        backgroundBox.PressedChanged += (_, _) => _showBackground = backgroundBox.IsChecked;

        var filterBar = new HorizontalStackPanel { Spacing = 8 };
        filterBar.Widgets.Add(headlineBox);
        filterBar.Widgets.Add(regionalBox);
        filterBar.Widgets.Add(backgroundBox);

        var stack = new VerticalStackPanel { Spacing = 4 };
        stack.Widgets.Add(filterBar);
        stack.Widgets.Add(_scroll);

        Root = new Panel();
        Root.Widgets.Add(stack);
    }

    public void Update(WorldSnapshot snapshot)
    {
        _rows.Widgets.Clear();
        foreach (var ev in snapshot.RecentEvents.Reverse())
        {
            if (!ShouldShow(ev.TierInvolvement)) continue;
            var label = new Label
            {
                Text       = $"[{ev.Year}] {ev.Season} {ev.Type}{(ev.Location.HasValue ? $" @({ev.Location.Value.X},{ev.Location.Value.Y})" : "")}",
                TextColor  = TierColor(ev.TierInvolvement)
            };
            _rows.Widgets.Add(label);
        }
    }

    private bool ShouldShow(EventTier tier) => tier switch
    {
        EventTier.Headline   => _showHeadline,
        EventTier.Regional   => _showRegional,
        EventTier.Character  => _showRegional, // show with regional
        EventTier.Background => _showBackground,
        _ => false
    };

    private static Color TierColor(EventTier tier) => tier switch
    {
        EventTier.Headline  => Color.Gold,
        EventTier.Regional  => Color.White,
        EventTier.Character => Color.LightGray,
        _                   => Color.DarkGray
    };
}
