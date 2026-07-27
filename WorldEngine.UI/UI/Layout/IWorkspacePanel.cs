using Myra.Graphics2D.UI;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Kit;
using WorldEngine.UI.UI.Present;
using WorldEngine.UI.UI.Selection;

namespace WorldEngine.UI.UI.Layout;

// MAP: Layer 3 contract — everything a panel needs, and nothing it's allowed to reach past.
/// <summary>
/// Per-frame context handed to every panel (framework §6): the current snapshot, the selection
/// sink, the formatting service, and the command gateway. A panel needs nothing else.
/// </summary>
public readonly record struct PanelContext(WorldSnapshot Snapshot, ISelectionSink Selection, Presenter Present, CommandGateway Commands);

/// <summary>Where a panel lives in the dock (framework §5.2).</summary>
public enum PanelPlacementKind { PinnedDefault, Contextual, Summoned }

/// <summary>A panel's placement; <paramref name="For"/> is required (and only meaningful) for <see cref="PanelPlacementKind.Contextual"/>.</summary>
public readonly record struct PanelPlacement(PanelPlacementKind Kind, SelectionKind? For = null);

/// <summary>Data for a standard empty-state render (see <see cref="Kit.EmptyState"/>, which takes this as a spec).</summary>
public readonly record struct EmptyStateSpec(EmptyStateKind Kind, string Message, string? Hint = null);

/// <summary>
/// The Layer 3 panel contract (framework §6). A panel builds its content once, is bound a fresh
/// <see cref="PanelContext"/> each frame it's visible, and refreshes only then — never touching
/// Myra layout geometry or reaching past the snapshot surface.
/// </summary>
public interface IWorkspacePanel
{
    string Id { get; }
    string Title { get; }
    PanelPlacement Placement { get; }

    /// <summary>Builds (once) and returns this panel's root widget — typically a <see cref="PanelFrame"/>.</summary>
    Widget Build();

    /// <summary>Called each frame this panel is visible, before <see cref="Refresh"/>.</summary>
    void Bind(PanelContext ctx);

    /// <summary>Rebuilds this panel's displayed content from the bound context. Called only while visible.</summary>
    void Refresh();
}

/// <summary>Implemented by panels that can be shown/hidden independently of dock placement (Summoned panels).</summary>
public interface IToggleablePanel : IWorkspacePanel
{
    void Show();
    void Hide();
    bool IsVisible { get; }
}
