using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;

namespace JGraph.Interaction;

/// <summary>
/// A snapshot of an axes' view: the range and auto-scale flag of every X and Y axis, plus the Z axis,
/// the two polar rulers and the chart's rotation, the camera angles, and whatever the camera has been
/// placed at by hand. Capturing before and after a navigation gesture lets the whole change (pan,
/// zoom, rotate, dolly, or reset) be undone/redone atomically — including the release of a
/// hand-placed camera that rotating performs.
/// </summary>
/// <remarks>
/// The polar three arrived with M83 and are the silent half of that milestone: without them a polar
/// gesture works perfectly and <c>CommitViewChange</c> compares the before and after as equal, so
/// nothing is pushed and nothing can be undone.
/// </remarks>
public sealed class AxesViewState
{
    private readonly (DataRange Range, bool AutoScale)[] _x;
    private readonly (DataRange Range, bool AutoScale)[] _y;
    private readonly (DataRange Range, bool AutoScale) _z;
    private readonly (DataRange Range, bool AutoScale) _r;
    private readonly DataRange _theta;
    private readonly double _thetaZeroOffset;
    private readonly double _azimuth;
    private readonly double _elevation;
    private readonly Vector3D? _cameraPosition;
    private readonly Vector3D? _cameraTarget;
    private readonly Vector3D? _cameraUpVector;
    private readonly double? _cameraViewAngle;

    private AxesViewState(
        (DataRange, bool)[] x,
        (DataRange, bool)[] y,
        (DataRange, bool) z,
        (DataRange, bool) r,
        DataRange theta,
        double thetaZeroOffset,
        double azimuth,
        double elevation,
        Vector3D? cameraPosition,
        Vector3D? cameraTarget,
        Vector3D? cameraUpVector,
        double? cameraViewAngle)
    {
        _x = x;
        _y = y;
        _z = z;
        _r = r;
        _theta = theta;
        _thetaZeroOffset = thetaZeroOffset;
        _azimuth = azimuth;
        _elevation = elevation;
        _cameraPosition = cameraPosition;
        _cameraTarget = cameraTarget;
        _cameraUpVector = cameraUpVector;
        _cameraViewAngle = cameraViewAngle;
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

        return new AxesViewState(
            x,
            y,
            (axes.ZAxis.Range, axes.ZAxis.AutoScale),
            (axes.RAxis.Range, axes.RAxis.AutoScale),
            axes.ThetaAxis.Range,
            axes.ThetaZeroOffset,
            axes.Azimuth,
            axes.Elevation,
            axes.CameraPosition,
            axes.CameraTarget,
            axes.CameraUpVector,
            axes.CameraViewAngle);
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

        axes.ZAxis.AutoScale = _z.AutoScale;
        axes.ZAxis.Range = _z.Range;
        axes.RAxis.AutoScale = _r.AutoScale;
        axes.RAxis.Range = _r.Range;
        axes.ThetaAxis.Range = _theta;
        axes.ThetaZeroOffset = _thetaZeroOffset;
        axes.Azimuth = _azimuth;
        axes.Elevation = _elevation;
        axes.CameraPosition = _cameraPosition;
        axes.CameraTarget = _cameraTarget;
        axes.CameraUpVector = _cameraUpVector;
        axes.CameraViewAngle = _cameraViewAngle;
    }

    /// <summary>True when this state differs from another (used to skip no-op undo entries).</summary>
    public bool DiffersFrom(AxesViewState other)
    {
        if (_x.Length != other._x.Length || _y.Length != other._y.Length)
        {
            return true;
        }

        if (_z != other._z || _azimuth != other._azimuth || _elevation != other._elevation)
        {
            return true;
        }

        if (_r != other._r || _theta != other._theta || _thetaZeroOffset != other._thetaZeroOffset)
        {
            return true;
        }

        if (_cameraPosition != other._cameraPosition || _cameraTarget != other._cameraTarget
            || _cameraUpVector != other._cameraUpVector || _cameraViewAngle != other._cameraViewAngle)
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
