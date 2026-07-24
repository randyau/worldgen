using Myra.Graphics2D.UI;

namespace WorldEngine.UI.UI.Kit;

// MAP: Layer 1 — common surface so Layer 2 composites can nest any We* widget.
/// <summary>Implemented by every Layer 1 (<c>We*</c>) widget wrapper; exposes the raw Myra root.</summary>
public interface IWeWidget
{
    /// <summary>The wrapped Myra widget. Only Layer 1/2 code should ever read this.</summary>
    Widget Root { get; }
}
