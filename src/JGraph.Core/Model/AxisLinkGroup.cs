using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>Which axis dimensions an <see cref="AxisLinkGroup"/> keeps in sync.</summary>
public enum AxisLinkMode
{
    /// <summary>Link the primary X axes only.</summary>
    X,

    /// <summary>Link the primary Y axes only.</summary>
    Y,

    /// <summary>Link both the primary X and Y axes.</summary>
    Both,
}

/// <summary>
/// Keeps the visible <see cref="AxisModel.Range"/> of the primary axes of several
/// <see cref="AxesModel"/> synchronized (MATLAB <c>linkaxes</c>): panning or zooming one member moves
/// the others in lock-step. On creation the linked ranges are unified to their union and auto-scaling
/// is turned off (matching MATLAB), so linked axes share fixed limits. The group listens on each
/// member's bubbling invalidation and mirrors range changes with a re-entrancy guard, so no feedback
/// loop can form. Dispose to break the links.
/// </summary>
public sealed class AxisLinkGroup : IDisposable
{
    private readonly List<AxesModel> _members = new();
    private readonly Dictionary<AxisModel, DataRange> _last = new();
    private bool _syncing;
    private bool _disposed;

    /// <summary>Creates a link group over the given axes and unifies their linked ranges immediately.</summary>
    public AxisLinkGroup(AxisLinkMode mode, params AxesModel[] axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        Mode = mode;
        foreach (AxesModel a in axes)
        {
            AddInternal(a);
        }

        UnifyAndFix();
    }

    /// <summary>The dimensions this group keeps in sync.</summary>
    public AxisLinkMode Mode { get; }

    /// <summary>The linked axes.</summary>
    public IReadOnlyList<AxesModel> Members => _members;

    /// <summary>Creates a link group over the given axes (convenience factory).</summary>
    public static AxisLinkGroup Link(AxisLinkMode mode, params AxesModel[] axes) => new(mode, axes);

    /// <summary>Adds another axes to the group and re-unifies the linked ranges.</summary>
    public void Add(AxesModel axes)
    {
        ThrowIfDisposed();
        AddInternal(axes);
        UnifyAndFix();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (AxesModel m in _members)
        {
            m.Invalidated -= OnInvalidated;
        }

        _members.Clear();
        _last.Clear();
    }

    private bool LinksX => Mode is AxisLinkMode.X or AxisLinkMode.Both;

    private bool LinksY => Mode is AxisLinkMode.Y or AxisLinkMode.Both;

    private void AddInternal(AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (_members.Contains(axes))
        {
            return;
        }

        _members.Add(axes);
        axes.Invalidated += OnInvalidated;
    }

    private void UnifyAndFix()
    {
        _syncing = true;
        try
        {
            if (LinksX)
            {
                UnifyDimension(isX: true);
            }

            if (LinksY)
            {
                UnifyDimension(isX: false);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UnifyDimension(bool isX)
    {
        DataRange union = DataRange.Empty;
        foreach (AxesModel m in _members)
        {
            DataRange r = LinkedAxis(m, isX).Range;
            if (r.IsValid)
            {
                union = union.Union(r);
            }
        }

        foreach (AxesModel m in _members)
        {
            AxisModel axis = LinkedAxis(m, isX);
            if (union.IsValid)
            {
                axis.AutoScale = false;
                axis.Range = union;
            }

            _last[axis] = axis.Range;
        }
    }

    private void OnInvalidated(object? sender, InvalidatedEventArgs e)
    {
        if (_syncing || _disposed)
        {
            return;
        }

        if (LinksX)
        {
            Propagate(isX: true);
        }

        if (LinksY)
        {
            Propagate(isX: false);
        }
    }

    private void Propagate(bool isX)
    {
        AxisModel? source = null;
        foreach (AxesModel m in _members)
        {
            AxisModel axis = LinkedAxis(m, isX);
            if (!_last.TryGetValue(axis, out DataRange cached) || axis.Range != cached)
            {
                source = axis;
                break;
            }
        }

        if (source is null)
        {
            return;
        }

        DataRange newRange = source.Range;
        _syncing = true;
        try
        {
            foreach (AxesModel m in _members)
            {
                AxisModel axis = LinkedAxis(m, isX);
                if (!ReferenceEquals(axis, source))
                {
                    axis.AutoScale = false;
                    axis.Range = newRange;
                }

                _last[axis] = newRange;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static AxisModel LinkedAxis(AxesModel axes, bool isX) => isX ? axes.PrimaryXAxis : axes.PrimaryYAxis;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
