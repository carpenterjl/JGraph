using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;

namespace JGraph.Interaction;

/// <summary>
/// A snapshot of an axes' view: the range and auto-scale flag of every X and Y axis. Capturing before
/// and after a navigation gesture lets the whole change be undone/redone atomically.
/// </summary>
public sealed class AxesViewState
{
    private readonly (DataRange Range, bool AutoScale)[] _x;
    private readonly (DataRange Range, bool AutoScale)[] _y;

    private AxesViewState((DataRange, bool)[] x, (DataRange, bool)[] y)
    {
        _x = x;
        _y = y;
    }

    /// <summary>Captures the current view state of an axes.</summary>
    public static AxesViewState Capture(AxesModel axes)
    {
        var x = new (DataRange, bool)[axes.XAxes.Count];
        for (int i = 0; i < axes.XAxes.Count; i++)
        {
            x[i] = (axes.XAxes[i].Range, axes.XAxes[i].AutoScale);
        }

        var y = new (DataRange, bool)[axes.YAxes.Count];
        for (int i = 0; i < axes.YAxes.Count; i++)
        {
            y[i] = (axes.YAxes[i].Range, axes.YAxes[i].AutoScale);
        }

        return new AxesViewState(x, y);
    }

    /// <summary>Restores this captured state onto the axes.</summary>
    public void ApplyTo(AxesModel axes)
    {
        for (int i = 0; i < _x.Length && i < axes.XAxes.Count; i++)
        {
            axes.XAxes[i].AutoScale = _x[i].AutoScale;
            axes.XAxes[i].Range = _x[i].Range;
        }

        for (int i = 0; i < _y.Length && i < axes.YAxes.Count; i++)
        {
            axes.YAxes[i].AutoScale = _y[i].AutoScale;
            axes.YAxes[i].Range = _y[i].Range;
        }
    }

    /// <summary>True when this state differs from another (used to skip no-op undo entries).</summary>
    public bool DiffersFrom(AxesViewState other)
    {
        if (_x.Length != other._x.Length || _y.Length != other._y.Length)
        {
            return true;
        }

        for (int i = 0; i < _x.Length; i++)
        {
            if (_x[i] != other._x[i])
            {
                return true;
            }
        }

        for (int i = 0; i < _y.Length; i++)
        {
            if (_y[i] != other._y[i])
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>An undoable change to an axes' view (zoom, pan, or reset).</summary>
public sealed class AxesViewChangeAction : IUndoableAction
{
    private readonly AxesModel _axes;
    private readonly AxesViewState _before;
    private readonly AxesViewState _after;

    public AxesViewChangeAction(AxesModel axes, AxesViewState before, AxesViewState after, string description)
    {
        _axes = axes;
        _before = before;
        _after = after;
        Description = description;
    }

    public string Description { get; }

    public void Redo() => _after.ApplyTo(_axes);

    public void Undo() => _before.ApplyTo(_axes);
}
