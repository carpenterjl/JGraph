using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction.Modes;

namespace JGraph.Interaction;

/// <summary>
/// Routes UI-independent input events to the active <see cref="IInteractionMode"/> and owns behavior
/// common to all modes (mouse-wheel zoom about the cursor). It records navigation changes on the
/// shared undo stack and exposes transient overlay state (the rubber-band rectangle and the data
/// cursor) that the host draws on top of the figure.
/// </summary>
public sealed class InteractionController
{
    private const double WheelZoomStep = 1.15;

    private readonly Dictionary<InteractionModeKind, IInteractionMode> _modes;
    private IInteractionMode _current;

    public InteractionController(IInteractionSurface surface)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _modes = new Dictionary<InteractionModeKind, IInteractionMode>
        {
            [InteractionModeKind.Pointer] = new PointerMode(),
            [InteractionModeKind.Pan] = new PanMode(),
            [InteractionModeKind.RectangleZoom] = new RectangleZoomMode(),
            [InteractionModeKind.DataTips] = new DataTipsMode(),
            [InteractionModeKind.Edit] = new EditMode(),
        };
        _current = _modes[InteractionModeKind.Pointer];
    }

    /// <summary>Raised when the active mode, overlay, or cursor changes so the host can refresh chrome.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The host surface providing geometry, the undo stack, and repaint requests.</summary>
    public IInteractionSurface Surface { get; }

    /// <summary>The shared selection, driven by the edit mode and the plot browser.</summary>
    public SelectionManager Selection { get; } = new();

    /// <summary>The active interaction mode.</summary>
    public IInteractionMode CurrentMode => _current;

    /// <summary>The rectangle-zoom mode instance, exposing its <see cref="RectangleZoomMode.Constraint"/>.</summary>
    public RectangleZoomMode RectangleZoom => (RectangleZoomMode)_modes[InteractionModeKind.RectangleZoom];

    /// <summary>
    /// Builds the plot surface's right-click menu for a click at <paramref name="pixel"/>: the
    /// active mode's items (zoom constraints, data-tip deletion), then "Restore View" for the axes
    /// under the pixel. The host renders the items with its own menu control.
    /// </summary>
    public IReadOnlyList<ContextMenuItem> BuildContextMenu(Point2D pixel)
    {
        var items = new List<ContextMenuItem>();
        (_current as IContextMenuSource)?.AddContextMenuItems(this, pixel, items);
        DataTipMenu.AddItems(this, pixel, items);

        if (items.Count > 0)
        {
            items.Add(ContextMenuItem.Separator);
        }

        items.Add(new ContextMenuItem("Restore View", () => ResetView(pixel)));
        return items;
    }

    /// <summary>The cursor hint for the current mode.</summary>
    public InteractionCursor Cursor => _current.Cursor;

    /// <summary>The current rubber-band selection rectangle in device space, or null when inactive.</summary>
    public Rect2D? RubberBand { get; private set; }

    /// <summary>Switches the active interaction mode, cancelling any in-progress gesture.</summary>
    public void SetMode(InteractionModeKind kind)
    {
        if (_current.Kind == kind)
        {
            return;
        }

        _current.Cancel(this);
        _current = _modes[kind];
        RubberBand = null;
        RaiseStateChanged();
    }

    /// <summary>
    /// Lets a mode with a dynamic cursor (the pointer's hover crosshair) tell the host to re-read
    /// <see cref="Cursor"/>.
    /// </summary>
    internal void NotifyCursorChanged() => RaiseStateChanged();

    public void PointerDown(PointerEventArgs e) => _current.OnPointerDown(this, e);

    public void PointerMove(PointerEventArgs e) => _current.OnPointerMove(this, e);

    public void PointerUp(PointerEventArgs e) => _current.OnPointerUp(this, e);

    public void KeyDown(KeyEventArgs e)
    {
        if (e.Key == InteractionKey.Escape)
        {
            _current.Cancel(this);
        }

        _current.OnKey(this, e);
    }

    /// <summary>Handles mouse-wheel zoom about the cursor for the axes under it (all modes).</summary>
    public void Wheel(WheelEventArgs e)
    {
        if (!Surface.TryGetAxesAt(e.Position, out AxesModel axes, out ICoordinateMapper mapper, out _))
        {
            return;
        }

        double factor = e.Delta > 0 ? 1.0 / WheelZoomStep : WheelZoomStep;
        AxesViewState before = AxesViewState.Capture(axes);

        if (axes.Is3D)
        {
            // Dolly: scale all three ranges symmetrically about their centers.
            ScaleAboutCenter(axes.PrimaryXAxis, factor);
            ScaleAboutCenter(axes.PrimaryYAxis, factor);
            ScaleAboutCenter(axes.ZAxis, factor);
            CommitViewChange(axes, before, "Zoom");
            return;
        }

        Navigation.ZoomAboutPixel(axes, mapper, e.Position, factor);
        CommitViewChange(axes, before, "Zoom");
    }

    /// <summary>Scales an axis' visible range about its center and pins auto-scale off.</summary>
    private static void ScaleAboutCenter(AxisModel axis, double factor)
    {
        DataRange range = axis.Range;
        double center = (range.Min + range.Max) / 2;
        double half = (range.Max - range.Min) / 2 * factor;
        axis.AutoScale = false;
        axis.Range = new DataRange(center - half, center + half);
    }

    /// <summary>Undoes the last navigation change.</summary>
    public void Undo() => Surface.UndoStack.Undo();

    /// <summary>Redoes the last undone navigation change.</summary>
    public void Redo() => Surface.UndoStack.Redo();

    /// <summary>Resets the axes under the pointer (or the default axes) to auto-fit.</summary>
    public void ResetView(Point2D? atPixel = null)
    {
        AxesModel? axes = null;
        if (atPixel is { } p && Surface.TryGetAxesAt(p, out AxesModel hit, out _, out _))
        {
            axes = hit;
        }

        axes ??= Surface.DefaultAxes;
        if (axes is null)
        {
            return;
        }

        AxesViewState before = AxesViewState.Capture(axes);
        Navigation.ResetView(axes);
        CommitViewChange(axes, before, "Reset view");
    }

    /// <summary>Sets the rubber-band overlay and requests a repaint.</summary>
    public void SetRubberBand(Rect2D? rect)
    {
        RubberBand = rect;
        Surface.RequestRender();
        RaiseStateChanged();
    }

    /// <summary>Records a navigation change on the undo stack if the view actually moved.</summary>
    public void CommitViewChange(AxesModel axes, AxesViewState before, string description)
    {
        AxesViewState after = AxesViewState.Capture(axes);
        if (after.DiffersFrom(before))
        {
            Surface.UndoStack.Push(new AxesViewChangeAction(axes, before, after, description));
        }

        Surface.RequestRender();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
