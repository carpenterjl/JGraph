using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;

namespace JGraph.Interaction;

/// <summary>
/// The bridge between the interaction system and whatever hosts the figure. The host (the WPF figure
/// control) exposes the current per-axes geometry from its last paint, the shared undo stack, and a
/// way to request a repaint. The interaction layer depends only on this abstraction, so it never
/// references WPF or the rendering backend.
/// </summary>
public interface IInteractionSurface
{
    /// <summary>The shared navigation/edit undo history.</summary>
    UndoStack UndoStack { get; }

    /// <summary>The axes acted on when no pointer position is available (for example, a toolbar reset).</summary>
    AxesModel? DefaultAxes { get; }

    /// <summary>
    /// Finds the axes whose plot area contains <paramref name="pixel"/>, returning its coordinate
    /// mapper and plot rectangle from the most recent paint.
    /// </summary>
    bool TryGetAxesAt(Point2D pixel, out AxesModel axes, out ICoordinateMapper mapper, out Rect2D plotArea);

    /// <summary>Returns the coordinate mapper for an axes from the most recent paint, if available.</summary>
    ICoordinateMapper? GetMapper(AxesModel axes);

    /// <summary>
    /// The mapper from normalized [0, 1] figure coordinates to device space from the most recent
    /// paint (used by figure-space annotations), or null before the first paint.
    /// </summary>
    ICoordinateMapper? FigureMapper { get; }

    /// <summary>Requests that the host repaint (for example, to update the rubber-band overlay).</summary>
    void RequestRender();
}
