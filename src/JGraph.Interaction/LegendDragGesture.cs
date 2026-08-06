using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;

namespace JGraph.Interaction;

/// <summary>
/// A drag of the legend box, shared by the pointer and edit modes: the box follows the pointer in
/// plot-area fractions, ending pushes one undoable move, and cancelling puts the legend back where
/// the gesture found it. The gesture tracks whether it has moved at all, because both modes treat a
/// press that never moves as a click on the legend row under it instead.
/// </summary>
internal sealed class LegendDragGesture
{
    private LegendModel? _target;
    private Rect2D _plotArea;
    private Point2D _startPixel;
    private Point2D _startLocation;
    private LegendPosition _startPosition;

    public bool Active => _target is not null;

    /// <summary>True once <see cref="Move"/> has actually displaced the legend.</summary>
    public bool Moved { get; private set; }

    /// <summary>
    /// Starts a drag of <paramref name="axes"/>' legend from <paramref name="pixel"/>. Returns false
    /// (starting nothing) when the plot area is degenerate, since the location is stored as a
    /// fraction of it.
    /// </summary>
    public bool Begin(IInteractionSurface surface, AxesModel axes, Point2D pixel, Rect2D plotArea)
    {
        if (plotArea.Width <= 0 || plotArea.Height <= 0)
        {
            return false;
        }

        LegendModel legend = axes.Legend;
        _target = legend;
        _plotArea = plotArea;
        _startPixel = pixel;
        _startPosition = legend.Position;

        // Start from where the legend is actually drawn, so switching from a preset to a custom
        // placement does not make the box jump on the first pixel of the drag.
        _startLocation = surface.GetLegendBounds(axes) is { } box
            ? new Point2D((box.Left - plotArea.Left) / plotArea.Width, (box.Top - plotArea.Top) / plotArea.Height)
            : legend.Location;

        Moved = false;
        return true;
    }

    public void Move(Point2D pixel)
    {
        if (_target is null)
        {
            return;
        }

        // Re-derive from the gesture start (never accumulate), in plot-area fractions.
        Vector2D delta = pixel - _startPixel;
        _target.Position = LegendPosition.Custom;
        _target.Location = new Point2D(
            _startLocation.X + (delta.X / _plotArea.Width),
            _startLocation.Y + (delta.Y / _plotArea.Height));
        Moved = true;
    }

    /// <summary>Ends the drag, pushing one undoable move if the legend went anywhere.</summary>
    public void End(IInteractionSurface surface)
    {
        if (_target is { } legend && Moved)
        {
            // Placement and the position mode changed together, so they undo together.
            surface.UndoStack.Push(new CompositeAction(
                "Move legend",
                new PropertyChangeAction(
                    legend,
                    nameof(LegendModel.Position),
                    _startPosition,
                    legend.Position),
                new PropertyChangeAction(
                    legend,
                    nameof(LegendModel.Location),
                    _startLocation,
                    legend.Location)));
        }

        Reset();
    }

    /// <summary>Aborts the drag, restoring the legend to where the gesture found it.</summary>
    public void Cancel()
    {
        if (_target is { } legend)
        {
            legend.Location = _startLocation;
            legend.Position = _startPosition;
        }

        Reset();
    }

    public void Reset()
    {
        _target = null;
        Moved = false;
    }
}
