using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.UI.Layout;

// MAP: Layer 4 — one modal surface: scrim + centered content, captures all input via the Modal band.
/// <summary>
/// The single modal surface (framework §5.5): dims the app with <see cref="UiTheme.SurfaceModalScrim"/>,
/// centers content, and captures all input while open (the <see cref="InputRouter"/> treats a
/// Modal region with content as an unconditional catch). Closes on <see cref="Close"/> or Esc.
/// </summary>
public sealed class ModalHost
{
    private readonly Panel _scrim;
    private readonly Panel _contentSlot;
    private readonly Region? _modalRegion;
    private Action? _onClose;

    /// <summary>Root widget for this host; add once to the Modal region's content.</summary>
    public Widget Root => _scrim;

    public bool IsOpen { get; private set; }

    /// <param name="modalRegion">
    /// The LayoutHost's Modal-band <see cref="Region"/>, if wired. <see cref="InputRouter"/>
    /// checks this region's <see cref="Region.Content"/> to decide whether an open modal should
    /// unconditionally capture the pointer, so Show/Close must keep it in sync.
    /// </param>
    public ModalHost(Region? modalRegion = null)
    {
        _modalRegion = modalRegion;
        _contentSlot = new Panel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _scrim = new Panel
        {
            Background = new SolidBrush(UiTheme.SurfaceModalScrim),
            Visible    = false
        };
        _scrim.Widgets.Add(_contentSlot);
    }

    public void Show(Widget content, Action? onClose = null)
    {
        _contentSlot.Widgets.Clear();
        _contentSlot.Widgets.Add(content);
        _onClose = onClose;
        _scrim.Visible = true;
        IsOpen = true;
        if (_modalRegion is not null) _modalRegion.Content = content;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _scrim.Visible = false;
        _contentSlot.Widgets.Clear();
        _onClose?.Invoke();
        if (_modalRegion is not null) _modalRegion.Content = null;
    }
}
