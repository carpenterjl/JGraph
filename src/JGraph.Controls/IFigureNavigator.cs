using JGraph.Core.Primitives;

namespace JGraph.Controls;

/// <summary>
/// The navigation surface a view model drives: undo/redo of view changes, reset-to-fit, and the
/// current viewport size (for what-you-see exports). The <see cref="FigureControl"/> implements this
/// so an MVVM shell can bind toolbar commands without reaching into rendering or interaction
/// internals.
/// </summary>
public interface IFigureNavigator
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    /// <summary>The figure viewport's current size in device-independent units.</summary>
    Size2D ViewportSize { get; }

    /// <summary>Raised when undo/redo availability changes.</summary>
    event EventHandler? NavigationStateChanged;

    void Undo();

    void Redo();

    void ResetView();
}
