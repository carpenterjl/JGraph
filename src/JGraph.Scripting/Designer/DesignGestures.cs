using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>A mouse button, as the canvas's gestures know it.</summary>
public enum DesignButton
{
    Left,
    Right,
    Middle,
}

/// <summary>The keys held with a press or a key.</summary>
[Flags]
public enum DesignModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>A key the canvas acts on.</summary>
public enum DesignKey
{
    Left,
    Right,
    Up,
    Down,
    Escape,

    /// <summary>Ctrl+A.</summary>
    SelectAll,
}

/// <summary>The pointer the canvas shows where the mouse is.</summary>
public enum DesignCursor
{
    Arrow,
    Move,
    SizeNorthSouth,
    SizeWestEast,
    SizeNorthWestSouthEast,
    SizeNorthEastSouthWest,
}

/// <summary>A resize handle of a selected component: its edges, and its square on the surface.</summary>
public readonly record struct DesignHandle(DesignComponent Component, ResizeEdges Edges, Rect2D Box);

/// <summary>
/// The canvas's gestures (designer plan DS2), as R2025b's App Designer makes them, apart from any
/// window: what a press, a drag, a release and a key do to an app's <see cref="DesignDocument"/>.
/// The canvas turns its mouse and keys into surface points and calls these, and draws what they
/// leave - the guides, the rubber band, the container a drop goes into.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A press selects: alone, or with Ctrl or Shift added to a selection in the same container
/// (and a selected one taken out). A press on one of several selected keeps them for a drag, and
/// selects it alone if no drag follows.</item>
/// <item>A drag moves the selection, every component placed by its position: its corner to the grid
/// when the grid is shown and snapped to, else its edges and centres to every other shown box's and
/// the figure's within 7 pixels. Released inside a container that can hold it, it goes in.</item>
/// <item>A handle resizes the selection by the same travel, each within its shape; Ctrl grows it
/// about its centre and Shift keeps its proportions.</item>
/// <item>A press on the figure, or on a container's body away from its edge and title, draws a
/// rubber band when dragged, and selects what it holds whole; without a drag it selects what was
/// pressed.</item>
/// <item>The arrow keys move the selection a pixel, ten with Shift; a run of them is one edit,
/// ended when no arrow is held. Ctrl+A selects everything in the selection's container. Escape
/// cancels a gesture under way.</item>
/// </list>
/// </remarks>
public sealed class DesignGestures
{
    /// <summary>The side of a resize handle.</summary>
    public const double HandleSize = 7;

    /// <summary>How deep a container's edge is where a press moves it rather than drawing a band.</summary>
    public const double ContainerEdge = 6;

    private PressState _press;
    private Intent _intent;
    private Point2D _down;
    private DesignComponent? _anchor;
    private ResizeEdges _edges;
    private IReadOnlyList<DesignComponent>? _clickSelects;
    private bool _keyMoving;
    private double _keyDX;
    private double _keyDY;

    /// <summary>The gestures on <paramref name="document"/>.</summary>
    public DesignGestures(DesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
    }

    /// <summary>The app the gestures edit.</summary>
    public DesignDocument Document { get; }

    /// <summary>The tab whose heading is at a point of the surface, which the canvas's component layer knows; null when none.</summary>
    public Func<Point2D, DesignComponent?>? TabHeadingAt { get; set; }

    /// <summary>Whether the grid is shown; a move and a resize snap to it then, when <see cref="SnapToGrid"/>.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Whether a shown grid is snapped to (App Designer's default).</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>The grid's interval.</summary>
    public double GridInterval { get; set; } = DesignGeometry.GridInterval;

    /// <summary>Whether a move or a resize shows alignment guides and snaps to them.</summary>
    public bool ShowAlignmentHints { get; set; } = true;

    /// <summary>How far a press must travel before it is a drag.</summary>
    public double DragThreshold { get; set; } = 4;

    /// <summary>Whether a press is held: the canvas keeps the mouse until it is released.</summary>
    public bool IsPressed => _press != PressState.None;

    /// <summary>The rubber band being drawn, on the surface; null when none.</summary>
    public Rect2D? Band { get; private set; }

    /// <summary>What the rubber band holds now.</summary>
    public IReadOnlyList<DesignComponent> Banded { get; private set; } = [];

    /// <summary>The guides a move or a resize aligned to.</summary>
    public IReadOnlyList<DesignGuide> Guides { get; private set; } = [];

    /// <summary>The container a drag released here would move the selection into; null when it stays where it is.</summary>
    public DesignComponent? DropTarget { get; private set; }

    /// <summary>Raised when what the canvas draws over the components changed: guides, band, drop target.</summary>
    public event EventHandler? Changed;

    private double? Grid => ShowGrid && SnapToGrid ? DesignGeometry.GridIntervalWithin(GridInterval) : null;

    private enum PressState
    {
        None,
        Pending,
        Moving,
        Resizing,
        Band,
    }

    private enum Intent
    {
        Nothing,
        Move,
        Resize,
        Band,
    }

    // --- the mouse -------------------------------------------------------------------------------

    /// <summary>A press at <paramref name="at"/> on the surface.</summary>
    /// <returns>Whether the canvas should hold the mouse until the release.</returns>
    public bool Press(Point2D at, DesignButton button, DesignModifiers modifiers)
    {
        if (_press != PressState.None)
        {
            return true;
        }

        EndKeyMove();
        bool control = modifiers.HasFlag(DesignModifiers.Control);
        bool shift = modifiers.HasFlag(DesignModifiers.Shift);
        _down = at;
        _clickSelects = null;
        DesignDocument document = Document;

        // A handle of a selected component resizes the selection.
        if (button == DesignButton.Left && HandleAt(at) is { } handle)
        {
            return Begin(Intent.Resize, handle.Component, handle.Edges);
        }

        // A tab's heading shows the tab and selects it.
        if (TabHeadingAt?.Invoke(at) is { } tab)
        {
            document.Runtime.ShowTab(tab);
            document.Select([tab]);
            return false;
        }

        DesignComponent hit = document.ComponentAt(at);
        IReadOnlyList<DesignComponent> selection = document.Selection;
        bool selected = selection.Contains(hit);
        if (button != DesignButton.Left)
        {
            if (!selected)
            {
                document.Select([hit]);
            }

            return false;
        }

        if (IsBody(hit, at))
        {
            _clickSelects = DesignGeometry.Click(selection, hit, control, shift);
            return Begin(Intent.Band, hit, ResizeEdges.None);
        }

        if (control || shift)
        {
            IReadOnlyList<DesignComponent> next = DesignGeometry.Click(selection, hit, control, shift);
            document.Select(next);
            return Begin(next.Contains(hit) ? Intent.Move : Intent.Nothing, hit, ResizeEdges.None);
        }

        if (selected && selection.Count > 1)
        {
            _clickSelects = [hit];
        }
        else if (!selected)
        {
            document.Select([hit]);
        }

        return Begin(Intent.Move, hit, ResizeEdges.None);
    }

    private bool Begin(Intent intent, DesignComponent anchor, ResizeEdges edges)
    {
        _press = PressState.Pending;
        _intent = intent;
        _anchor = anchor;
        _edges = edges;
        return true;
    }

    /// <summary>The mouse at <paramref name="at"/> with the press held.</summary>
    public void Drag(Point2D at, DesignModifiers modifiers)
    {
        if (_press == PressState.None)
        {
            return;
        }

        DesignDocument document = Document;
        if (_press == PressState.Pending)
        {
            if (System.Math.Abs(at.X - _down.X) < DragThreshold && System.Math.Abs(at.Y - _down.Y) < DragThreshold)
            {
                return;
            }

            IReadOnlyList<DesignComponent> movable = document.Selection.Where(document.IsMovable).ToArray();
            switch (_intent)
            {
                case Intent.Move when movable.Count == document.Selection.Count && movable.Count > 0:
                    _clickSelects = null;
                    document.BeginGesture(movable);
                    _press = PressState.Moving;
                    break;
                case Intent.Resize when movable.Count > 0:
                    document.BeginGesture(movable);
                    _press = PressState.Resizing;
                    break;
                case Intent.Band:
                    _clickSelects = null;
                    _press = PressState.Band;
                    break;
                default:
                    _press = PressState.None;
                    return;
            }
        }

        double dx = at.X - _down.X;
        double dy = at.Y - _down.Y;
        switch (_press)
        {
            case PressState.Moving:
                DragMove(dx, dy);
                break;
            case PressState.Resizing:
                DragResize(dx, dy, modifiers);
                break;
            case PressState.Band:
                Rect2D band = new(System.Math.Min(at.X, _down.X), System.Math.Min(at.Y, _down.Y), System.Math.Abs(dx), System.Math.Abs(dy));
                Band = band;
                Banded = DesignGeometry.RubberBand(band, _down, document.Boxes);
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The press released.</summary>
    public void Release()
    {
        DesignDocument document = Document;
        PressState press = _press;
        _press = PressState.None;
        switch (press)
        {
            case PressState.Pending when _clickSelects is { } selects:
                document.Select(selects);
                break;
            case PressState.Moving:
                DesignComponent? into = DropTarget;
                Guides = [];
                DropTarget = null;
                document.EndGesture(commit: true, into);
                break;
            case PressState.Resizing:
                Guides = [];
                document.EndGesture(commit: true);
                break;
            case PressState.Band:
                IReadOnlyList<DesignComponent> banded = Banded;
                Band = null;
                Banded = [];
                document.Select(banded.Count > 0 ? banded : _anchor is { } pressed ? [pressed] : []);
                break;
        }

        _clickSelects = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels a gesture under way: its components go back to where they were.</summary>
    public void Cancel()
    {
        _press = PressState.None;
        _keyMoving = false;
        Guides = [];
        Band = null;
        Banded = [];
        DropTarget = null;
        _clickSelects = null;
        Document.EndGesture(commit: false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the gesture's components by the drag, snapped to the grid or to the guides.</summary>
    private void DragMove(double dx, double dy)
    {
        DesignDocument document = Document;
        IReadOnlyDictionary<DesignComponent, Rect2D> start = document.GestureStart;
        DesignComponent anchor = _anchor is not null && start.ContainsKey(_anchor) ? _anchor : start.Keys.First();
        Rect2D from = start[anchor];
        Guides = [];
        if (Grid is { } grid && anchor.Parent is { } parent)
        {
            // The anchor's corner to the grid of the container it is in.
            Rect2D inner = document.Runtime.InnerOnSurface(parent);
            double left = from.X - inner.X + dx;
            double top = from.Y - inner.Y + dy;
            dx += DesignGeometry.OnGrid(left, grid) - left;
            dy += DesignGeometry.OnGrid(top, grid) - top;
        }
        else
        {
            dx = System.Math.Round(dx, MidpointRounding.AwayFromZero);
            dy = System.Math.Round(dy, MidpointRounding.AwayFromZero);
            if (ShowAlignmentHints)
            {
                DesignSnap snap = DesignGeometry.Snap(Shifted(from, dx, dy), Others(start.Keys));
                dx += snap.DX;
                dy += snap.DY;
                Guides = snap.Guides;
            }
        }

        var boxes = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach ((DesignComponent component, Rect2D box) in start)
        {
            boxes[component] = Shifted(box, dx, dy);
        }

        document.Preview(boxes);
        DesignComponent target = document.DropTarget([.. start.Keys], boxes[anchor]);
        DropTarget = ReferenceEquals(target, anchor.Parent) ? null : target;
    }

    /// <summary>Resizes the gesture's components by the drag on a handle, within their shapes, snapped.</summary>
    private void DragResize(double dx, double dy, DesignModifiers modifiers)
    {
        DesignDocument document = Document;
        IReadOnlyDictionary<DesignComponent, Rect2D> start = document.GestureStart;
        DesignComponent anchor = _anchor is not null && start.ContainsKey(_anchor) ? _anchor : start.Keys.First();
        bool fromCentre = modifiers.HasFlag(DesignModifiers.Control);
        bool keepAspect = modifiers.HasFlag(DesignModifiers.Shift);
        dx = System.Math.Round(dx, MidpointRounding.AwayFromZero);
        dy = System.Math.Round(dy, MidpointRounding.AwayFromZero);
        Guides = [];
        if (Grid is null && ShowAlignmentHints)
        {
            Rect2D sized = DesignGeometry.Resize(start[anchor], _edges, dx, dy, document.ShapeOf(anchor), fromCentre, keepAspect);
            DesignSnap snap = DesignGeometry.Snap(sized, Others(start.Keys), _edges);
            dx += _edges.HasFlag(ResizeEdges.West) || _edges.HasFlag(ResizeEdges.East) ? snap.DX : 0;
            dy += _edges.HasFlag(ResizeEdges.North) || _edges.HasFlag(ResizeEdges.South) ? snap.DY : 0;
            Guides = snap.Guides;
        }

        var boxes = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach ((DesignComponent component, Rect2D box) in start)
        {
            boxes[component] = DesignGeometry.Resize(box, _edges, dx, dy, document.ShapeOf(component), fromCentre, keepAspect, Grid);
        }

        document.Preview(boxes);
    }

    /// <summary>The boxes a moving selection aligns with: every other shown one, and the figure's.</summary>
    private IEnumerable<Rect2D> Others(IEnumerable<DesignComponent> moving)
    {
        DesignComponent[] movers = [.. moving];
        Size2D size = Document.Runtime.Figure.Size;
        yield return new Rect2D(0, 0, size.Width, size.Height);
        foreach (DesignBox box in Document.Boxes)
        {
            if (box.Shown && !movers.Any(m => IsWithin(box.Component, m)))
            {
                yield return box.Box;
            }
        }
    }

    private static bool IsWithin(DesignComponent component, DesignComponent ancestor)
    {
        for (DesignComponent? up = component; up is not null; up = up.Parent)
        {
            if (ReferenceEquals(up, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static Rect2D Shifted(Rect2D box, double dx, double dy) => new(box.X + dx, box.Y + dy, box.Width, box.Height);

    /// <summary>
    /// Whether a press at <paramref name="at"/> on <paramref name="hit"/> is on a body that draws a
    /// band: the figure's, a tab's or a grid's, or a panel's or button group's away from its edge and title.
    /// </summary>
    private bool IsBody(DesignComponent hit, Point2D at)
    {
        if (hit.Parent is null || hit.Type is "matlab.ui.container.Tab" or "matlab.ui.container.GridLayout")
        {
            return true;
        }

        if (hit.Type is not ("matlab.ui.container.Panel" or "matlab.ui.container.ButtonGroup"))
        {
            return false;
        }

        Rect2D inner = Document.Runtime.InnerOnSurface(hit);
        return at.X >= inner.X + ContainerEdge && at.X <= inner.X + inner.Width - ContainerEdge
            && at.Y >= inner.Y + ContainerEdge && at.Y <= inner.Y + inner.Height - ContainerEdge;
    }

    // --- handles and the pointer -----------------------------------------------------------------

    /// <summary>The resize handles of the selection: eight on each component placed by its position, none along a side its shape fixes.</summary>
    public IEnumerable<DesignHandle> Handles()
    {
        DesignDocument document = Document;
        foreach (DesignComponent component in document.Selection)
        {
            if (!document.IsMovable(component) || document.BoxOf(component) is not { Shown: true } box)
            {
                continue;
            }

            DesignShape shape = document.ShapeOf(component);
            Rect2D b = box.Box;
            double h = HandleSize / 2;
            (ResizeEdges Edges, double X, double Y)[] points =
            [
                (ResizeEdges.NorthWest, b.X, b.Y), (ResizeEdges.North, b.X + (b.Width / 2), b.Y), (ResizeEdges.NorthEast, b.X + b.Width, b.Y),
                (ResizeEdges.East, b.X + b.Width, b.Y + (b.Height / 2)), (ResizeEdges.SouthEast, b.X + b.Width, b.Y + b.Height),
                (ResizeEdges.South, b.X + (b.Width / 2), b.Y + b.Height), (ResizeEdges.SouthWest, b.X, b.Y + b.Height),
                (ResizeEdges.West, b.X, b.Y + (b.Height / 2)),
            ];
            foreach ((ResizeEdges edges, double x, double y) in points)
            {
                bool vertical = edges.HasFlag(ResizeEdges.North) || edges.HasFlag(ResizeEdges.South);
                bool horizontal = edges.HasFlag(ResizeEdges.East) || edges.HasFlag(ResizeEdges.West);
                if ((shape.FixedHeight && vertical) || (shape.FixedWidth && horizontal))
                {
                    continue;
                }

                yield return new DesignHandle(component, edges, new Rect2D(x - h, y - h, HandleSize, HandleSize));
            }
        }
    }

    /// <summary>The handle at <paramref name="at"/>, or null.</summary>
    public DesignHandle? HandleAt(Point2D at)
    {
        foreach (DesignHandle handle in Handles())
        {
            if (handle.Box.Contains(at))
            {
                return handle;
            }
        }

        return null;
    }

    /// <summary>The pointer for the mouse at <paramref name="at"/> with no press held.</summary>
    public DesignCursor CursorAt(Point2D at)
    {
        if (HandleAt(at) is { } handle)
        {
            return handle.Edges switch
            {
                ResizeEdges.NorthWest or ResizeEdges.SouthEast => DesignCursor.SizeNorthWestSouthEast,
                ResizeEdges.NorthEast or ResizeEdges.SouthWest => DesignCursor.SizeNorthEastSouthWest,
                ResizeEdges.North or ResizeEdges.South => DesignCursor.SizeNorthSouth,
                _ => DesignCursor.SizeWestEast,
            };
        }

        DesignComponent hit = Document.ComponentAt(at);
        return Document.Selection.Contains(hit) && Document.IsMovable(hit) && !IsBody(hit, at) ? DesignCursor.Move : DesignCursor.Arrow;
    }

    // --- keys ------------------------------------------------------------------------------------

    /// <summary>A key pressed.</summary>
    /// <returns>Whether the canvas acted on it.</returns>
    public bool KeyDown(DesignKey key, DesignModifiers modifiers)
    {
        DesignDocument document = Document;
        switch (key)
        {
            case DesignKey.Escape when _press != PressState.None || _keyMoving:
                Cancel();
                return true;
            case DesignKey.Left or DesignKey.Right or DesignKey.Up or DesignKey.Down
                when (modifiers & ~DesignModifiers.Shift) == DesignModifiers.None && _press == PressState.None:
                double step = modifiers.HasFlag(DesignModifiers.Shift) ? DesignGeometry.ShiftNudge : DesignGeometry.Nudge;
                IReadOnlyList<DesignComponent> movable = document.Selection.Where(document.IsMovable).ToArray();
                if (movable.Count == 0 || movable.Count != document.Selection.Count)
                {
                    return false;
                }

                if (!_keyMoving)
                {
                    document.BeginGesture(movable);
                    _keyMoving = true;
                    _keyDX = 0;
                    _keyDY = 0;
                }

                _keyDX += key == DesignKey.Left ? -step : key == DesignKey.Right ? step : 0;
                _keyDY += key == DesignKey.Up ? -step : key == DesignKey.Down ? step : 0;
                var boxes = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
                foreach ((DesignComponent component, Rect2D box) in document.GestureStart)
                {
                    boxes[component] = Shifted(box, _keyDX, _keyDY);
                }

                document.Preview(boxes);
                return true;
            case DesignKey.SelectAll:
                DesignComponent container = document.Selection.Count > 0 && document.Selection[0].Parent is { } parent ? parent : document.Design.Figure;
                document.Select(document.Boxes.Where(b => b.Shown && ReferenceEquals(b.Component.Parent, container)).Select(static b => b.Component).ToArray());
                return true;
            default:
                return false;
        }
    }

    /// <summary>An arrow key let go; <paramref name="arrowHeld"/> says whether another still is. The run of moves ends as one edit when none is.</summary>
    public void ArrowUp(bool arrowHeld)
    {
        if (!arrowHeld)
        {
            EndKeyMove();
        }
    }

    /// <summary>Ends a run of arrow keys as one edit: when the keys are let go, or anything else begins.</summary>
    public void EndKeyMove()
    {
        if (_keyMoving)
        {
            _keyMoving = false;
            Document.EndGesture(commit: true);
        }
    }
}
