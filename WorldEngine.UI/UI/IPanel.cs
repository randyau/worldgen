using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI;

// MAP: A panel that can be shown or hidden; implemented by the pre-M8 Summoned panels.
/// <summary>A panel that can be shown or hidden under a uniform toggle model.</summary>
public interface IPanel
{
    Widget Root { get; }
    bool IsVisible { get; }
    void Show();
    void Hide();
}
