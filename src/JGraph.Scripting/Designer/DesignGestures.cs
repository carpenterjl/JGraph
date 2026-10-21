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

    /// <summary>Delete or Backspace: deletes the selection (DS3).</summary>
    Delete,

    /// <summary>Ctrl+G: groups the selection (open item 150).</summary>
    Group,

    /// <summary>Ctrl+Shift+G: takes the selected groups apart.</summary>
    Ungroup,
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

    /// <summary>Drawing a component from the library (DS3).</summary>
    Cross,
}

/// <summary>A resize handle of a selected component: its edges, and its square on the surface.</summary>
public readonly record struct DesignHandle(DesignComponent Component, ResizeEdges Edges, Rect2D Box, DesignGroup? Group = null);

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
/// cancels a gesture under way. Delete and Backspace delete the selection (DS3).</item>
/// <item>With a library entry to draw (<see cref="DrawEntry"/>, App Designer's click-and-draw mode,
/// DS3), a press adds the entry where it is: a click at its own size with its top-left corner there,
/// a drag at the size drawn; then the mode ends. A library entry dropped on the canvas
/// (<see cref="Drop"/>) is added where it lands. On a shown grid the corner snaps to it.</item>
/// </list>
/// </remarks>
public sealed class DesignGestures
{
    /// <summary>The side of a resize handle.</summary>
    public const double HandleSize = 7;

    /// <summary>How deep a container's edge is where a press moves it rather than drawing a band.</summary>
    public const double ContainerEdge = 6;

    private PressState _press;
    private DesignPaletteEntry? _drawEntry;
    private Intent _intent;
    private Point2D _down;
    private Point2D _drawFrom;
    private DesignComponent? _anchor;
    private ResizeEdges _edges;
    private IReadOnlyList<DesignPick>? _clickSelects;
    private DesignGroup? _resizeGroup;
    private bool _keyMoving;
    private double _keyDX;
    private double _keyDY;
    private DesignPlacement? _drawIn;
    private GridSpanResize? _spanResize;
    private DesignGridConfigure? _configure;
    private Point2D _lastDrag;
    private Point2D _pressAt;
    private DesignSplitter? _splitter;
    private DesignRegionResize? _regionResize;
    private double _splitAnchor;

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

    /// <summary>
    /// The library entry a press on the canvas adds (App Designer's click-and-draw mode, DS3); null
    /// when none. Set by a click on the entry in the library, and ended by a placement or Escape.
    /// </summary>
    public DesignPaletteEntry? DrawEntry
    {
        get => _drawEntry;
        set
        {
            _drawEntry = value;
            Drawn = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The box being drawn for <see cref="DrawEntry"/>, on the surface; null when none.</summary>
    public Rect2D? Drawn { get; private set; }

    /// <summary>What the last add from the library refused (an entry the designer does not make yet); null when it went.</summary>
    public string? Refusal { get; private set; }

    /// <summary>
    /// Where a drag or a library drag would drop what it carries (DS4): in a grid, the cells each
    /// component takes, which the canvas fills; null when nothing is dragged.
    /// </summary>
    public DesignPlacement? Placement { get; private set; }

    /// <summary>The cells a component being drawn in a grid takes so far (DS4); null when none.</summary>
    public (DesignComponent Grid, IReadOnlyList<GridSpan> Spans)? DrawnCells { get; private set; }

    /// <summary>
    /// A grid child's span being resized by a handle (DS4): the box App Designer previews - its
    /// dragged edge following the pointer, unsnapped - and the cells it would take; null when none.
    /// </summary>
    public (DesignComponent Child, Rect2D Box, GridSpan Span)? SpanPreview { get; private set; }

    /// <summary>A labelled component's label where a span resize of the pair has it (open item 150), beside <see cref="SpanPreview"/>'s field; null otherwise.</summary>
    public (DesignComponent Child, Rect2D Box, GridSpan Span)? LabelSpanPreview { get; private set; }

    /// <summary>
    /// The grid being configured (App Designer's grid edit mode, DS4); null when none. Configuring
    /// ends when it is ended, when the grid is gone, or when the selection it began with changes - App
    /// Designer's deselection ends it (<c>SelectableController._visualize</c>) - and then it is over:
    /// selecting the grid again does not bring it back.
    /// </summary>
    public DesignGridConfigure? Configuring
    {
        get
        {
            if (_configure is { } configure
                && (Document.BoxOf(configure.Grid) is not { Shown: true }
                    || !Document.Selection.SequenceEqual(_configureSelection, ReferenceEqualityComparer.Instance)))
            {
                _configure = null;
                Hint = null;
            }

            return _configure;
        }
    }

    private IReadOnlyList<DesignComponent> _configureSelection = [];

    /// <summary>The row or column a context menu item or a popup button would add or delete, which the canvas shows; null when none.</summary>
    public DesignGridHint? Hint { get; private set; }

    /// <summary>Whether a drag, a library drag or a drawing is under way: every grid then shows its cells, as App Designer's drop hints.</summary>
    public bool ShowsDropHints => _press is PressState.Moving or PressState.Drawing || _libraryDrag;

    /// <summary>Raised when what the canvas draws over the components changed: guides, band, drop target.</summary>
    public event EventHandler? Changed;

    private bool _libraryDrag;

    private double? Grid => ShowGrid && SnapToGrid ? DesignGeometry.GridIntervalWithin(GridInterval) : null;

    private enum PressState
    {
        None,
        Pending,
        Moving,
        Resizing,
        Band,
        Drawing,
        Spanning,
        Splitting,
    }

    private enum Intent
    {
        Nothing,
        Move,
        Resize,
        Band,
        Span,
        Split,
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
        Refusal = null;
        _down = at;
        _lastDrag = at;
        _pressAt = at;
        _clickSelects = null;
        DesignDocument document = Document;

        // A grid being configured takes every press on its controls; a press elsewhere ends it (DS4).
        if (Configuring is { } configure)
        {
            if (button == DesignButton.Left && ConfigureButton(configure.Grid) is { } exit && exit.Contains(at))
            {
                EndConfigure();
                return false;
            }

            // Only the primary button picks a band (a click); another on the controls leaves them as they are.
            if (configure.Takes(at))
            {
                if (button == DesignButton.Left)
                {
                    configure.Click(at);
                    Changed?.Invoke(this, EventArgs.Empty);
                }

                return false;
            }

            EndConfigure();
        }

        // The button at a grid's top-left corner configures it.
        if (button == DesignButton.Left && _drawEntry is null && ConfigureButtonAt(at) is { } toConfigure)
        {
            Configure(toConfigure);
            return false;
        }

        // Click and draw: the press begins the component's box - or, in a grid, its cells.
        if (_drawEntry is not null)
        {
            if (button != DesignButton.Left)
            {
                DrawEntry = null;
                return false;
            }

            DesignPlacement drawIn = document.DrawPlacement(_drawEntry, at);
            if (!drawIn.CanDrop || drawIn.Container is not { } container)
            {
                Refusal = $"{_drawEntry.Name} cannot be drawn there: in a grid, press in one of its cells.";
                Changed?.Invoke(this, EventArgs.Empty);
                return false;
            }

            _drawIn = drawIn;
            if (document.IsGrid(container) && drawIn.Spans is { } spans)
            {
                _down = at;
                _drawFrom = at;
                DrawnCells = (container, spans);
            }
            else
            {
                _down = SnapCorner(container, at);
                _drawFrom = _down;
            }

            _press = PressState.Drawing;
            return true;
        }

        // A responsive app's splitter resizes its regions (DS4b).
        if (button == DesignButton.Left && SplitterAt(at) is { } splitter)
        {
            _splitter = splitter;
            _regionResize = null;
            _splitAnchor = System.Math.Round(at.X, MidpointRounding.AwayFromZero);
            return Begin(Intent.Split, document.Design.Figure, ResizeEdges.None);
        }

        // A handle of a selected component resizes the selection; a grid child's spans it over cells;
        // a labelled component's resizes its field with the label following (open item 150).
        if (button == DesignButton.Left && HandleAt(at) is { } handle)
        {
            _resizeGroup = handle.Group;
            return Begin(document.IsGridChild(handle.Component) ? Intent.Span : Intent.Resize, handle.Component, handle.Edges);
        }

        // A tab's heading shows the tab and selects it.
        if (TabHeadingAt?.Invoke(at) is { } tab)
        {
            document.ShowTab(tab);
            document.Select([tab]);
            return false;
        }

        // What is pressed: the component there, or a group's own area (open item 150); App
        // Designer's selection rules decide what is selected, now or on a click's release.
        DesignComponent hit = document.ComponentAt(at);
        DesignPick pick = document.PickAt(at);
        IReadOnlyList<DesignPick> picks = document.Picks;
        bool selected = pick.Component is { } pc ? document.Selection.Contains(pc) : DesignGroupRules.IsSelected(picks, pick);
        if (button != DesignButton.Left)
        {
            // A right press selects what is under it: a member's group, unless a group around it is
            // selected already (_processMemberRightClick, _processGroupRightClick).
            if (!selected && DesignGroupRules.Press(document.Design, picks, pick, DesignModifiers.None) is { Now: { } chosen })
            {
                document.SelectPicks(chosen);
            }

            return false;
        }

        DesignGroupRules.PressResult result = DesignGroupRules.Press(document.Design, picks, pick, modifiers);
        if (IsBody(hit, at) && pick.Group is null)
        {
            _clickSelects = result.Now ?? result.OnRelease;
            return Begin(Intent.Band, hit, ResizeEdges.None);
        }

        if (result.Now is { } now)
        {
            document.SelectPicks(now);
        }

        _clickSelects = result.OnRelease;
        bool inSelection = pick.Component is { } c ? document.Selection.Contains(c) : DesignGroupRules.IsSelected(document.Picks, pick);
        DesignComponent anchor = pick.Component ?? pick.Group!.AllComponents().First();
        return Begin(inSelection ? Intent.Move : Intent.Nothing, anchor, ResizeEdges.None);
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

        _lastDrag = at;
        DesignDocument document = Document;
        if (_press == PressState.Drawing)
        {
            DragDraw(at);
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_press == PressState.Pending)
        {
            if (System.Math.Abs(at.X - _down.X) < DragThreshold && System.Math.Abs(at.Y - _down.Y) < DragThreshold)
            {
                return;
            }

            IReadOnlyList<DesignComponent> movable = document.Selection.Where(document.IsMovable).ToArray();
            IReadOnlyList<DesignComponent> draggable = document.Selection.Where(document.CanDrag).ToArray();
            switch (_intent)
            {
                case Intent.Move when draggable.Count == document.Selection.Count && draggable.Count > 0:
                    _clickSelects = null;
                    document.BeginGesture(draggable);
                    _press = PressState.Moving;
                    break;
                case Intent.Resize when movable.Count > 0:
                    document.BeginGesture(movable);
                    _press = PressState.Resizing;
                    break;
                case Intent.Span when _anchor is { Parent: { } grid } child && document.SpanOf(child) is { } span
                    && document.CellsOf(grid) is { } cells && document.CellBoxOf(child) is { } box:
                    // Only the component whose handle was grabbed spans (GridChildResizeServiceModel) -
                    // or a labelled component's pair, its label following its side (open item 150).
                    _clickSelects = null;
                    if (_resizeGroup is { IsLabeled: true, Label: { } label, Field: { } field } && ReferenceEquals(field, child)
                        && document.SpanOf(label) is { } labelSpan && document.CellBoxOf(label) is { } labelBox && document.BoxOfGroup(_resizeGroup) is { } pairBox)
                    {
                        _spanResize = new GridSpanResize(cells, _edges, GridSpanResize.LabeledEntries(labelSpan, labelBox, span, box, pairBox));
                    }
                    else
                    {
                        _resizeGroup = null;
                        _spanResize = new GridSpanResize(cells, _edges, [new GridResizeEntry(span, box, CanResize: true, IsLabel: false)]);
                    }

                    _press = PressState.Spanning;
                    break;
                case Intent.Band:
                    _clickSelects = null;
                    _press = PressState.Band;
                    break;
                case Intent.Split when _splitter is not null:
                    _press = PressState.Splitting;
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
                DragMove(dx, dy, at);
                break;
            case PressState.Resizing:
                DragResize(dx, dy, modifiers);
                break;
            case PressState.Splitting when _splitter is { } dragged:
                // App Designer's steps: each from where the last left the regions, by what the pointer
                // moved past the part of the drag the regions took (_HandleDoingLayoutTemplateResize).
                if (document.ResizeRegions(dragged, System.Math.Round(at.X, MidpointRounding.AwayFromZero) - _splitAnchor) is { } step)
                {
                    _splitAnchor += step.Used;
                    _regionResize = step.After(_regionResize);
                    document.PreviewRegions(_regionResize);
                }

                break;
            case PressState.Spanning when _spanResize is { } resize && _anchor is { } spanned:
                IReadOnlyList<Rect2D> live = resize.LiveBoxes(dx, dy);
                IReadOnlyList<GridSpan> spans = resize.Spans(dx, dy);
                if (_resizeGroup is { IsLabeled: true, Label: { } label } && live.Count == 3)
                {
                    // The entries are the pair, its label and its field.
                    LabelSpanPreview = (label, live[1], spans[1]);
                    SpanPreview = (spanned, live[2], spans[2]);
                }
                else
                {
                    SpanPreview = (spanned, live[0], spans[0]);
                }

                break;
            case PressState.Band:
                Rect2D band = new(System.Math.Min(at.X, _down.X), System.Math.Min(at.Y, _down.Y), System.Math.Abs(dx), System.Math.Abs(dy));
                Band = band;
                // A responsive app's grid is never selected (SelectableController): its regions are the top.
                Banded = DesignGeometry.RubberBand(band, _down, document.Boxes.Where(b => !document.IsResponsiveGrid(b.Component)));
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
                document.SelectPicks(selects);
                break;
            case PressState.Moving:
                DesignPlacement? placement = Placement;
                Guides = [];
                DropTarget = null;
                Placement = null;
                document.EndGesture(commit: true, placement);
                break;
            case PressState.Resizing:
                Guides = [];
                _resizeGroup = null;
                document.EndGesture(commit: true);
                break;
            case PressState.Splitting:
                _splitter = null;
                if (_regionResize is { } regions)
                {
                    _regionResize = null;
                    document.CommitRegions(regions);
                }

                break;
            case PressState.Spanning:
                (DesignComponent Child, Rect2D Box, GridSpan Span)? spanned = SpanPreview;
                (DesignComponent Child, Rect2D Box, GridSpan Span)? labelSpanned = LabelSpanPreview;
                SpanPreview = null;
                LabelSpanPreview = null;
                _spanResize = null;
                _resizeGroup = null;
                if (spanned is { } s)
                {
                    if (labelSpanned is { } l)
                    {
                        document.SetGridSpans([(l.Child, l.Span), (s.Child, s.Span)]);
                    }
                    else
                    {
                        document.SetGridSpan(s.Child, s.Span);
                    }
                }

                break;
            case PressState.Band:
                IReadOnlyList<DesignComponent> banded = Banded;
                Band = null;
                Banded = [];
                document.Select(banded.Count > 0 ? banded : _anchor is { } pressed ? [pressed] : []);
                break;
            case PressState.Drawing when _drawEntry is { } entry:
                ReleaseDraw(entry);
                break;
        }

        _clickSelects = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels a gesture under way: its components go back to where they were.</summary>
    public void Cancel()
    {
        _press = PressState.None;
        Drawn = null;
        DrawnCells = null;
        _drawIn = null;
        _keyMoving = false;
        Guides = [];
        Band = null;
        Banded = [];
        DropTarget = null;
        Placement = null;
        SpanPreview = null;
        LabelSpanPreview = null;
        _spanResize = null;
        _resizeGroup = null;
        _clickSelects = null;
        if (_regionResize is not null)
        {
            _regionResize = null;
            Document.PreviewRegions(null);
        }

        _splitter = null;
        Document.EndGesture(commit: false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves the gesture's components by the drag, snapped to the grid or to the guides - none in or
    /// over a grid, as App Designer clears its alignment hints there - and finds where they would drop.
    /// </summary>
    private void DragMove(double dx, double dy, Point2D at)
    {
        DesignDocument document = Document;
        IReadOnlyDictionary<DesignComponent, Rect2D> start = document.GestureStart;
        DesignComponent anchor = _anchor is not null && start.ContainsKey(_anchor) ? _anchor : start.Keys.First();
        Rect2D from = start[anchor];
        Dictionary<DesignComponent, Rect2D> Moved(double byX, double byY)
        {
            var moved = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
            foreach ((DesignComponent component, Rect2D box) in start)
            {
                moved[component] = Shifted(box, byX, byY);
            }

            return moved;
        }

        Guides = [];
        // Over a grid there are no alignment guides; the canvas grid still snaps (ComponentMoveHelpers.calculateDragOffsets).
        bool overGrid = Placement?.Container is { } over && document.IsGrid(over);
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
            if (ShowAlignmentHints && !overGrid)
            {
                DesignSnap snap = DesignGeometry.Snap(Shifted(from, dx, dy), Others(start.Keys));
                dx += snap.DX;
                dy += snap.DY;
                Guides = snap.Guides;
            }
        }

        DesignComponent[] moving = [.. start.Keys];
        Dictionary<DesignComponent, Rect2D> boxes = Moved(dx, dy);
        DesignPlacement placement = document.PlacementFor(moving, moving.Select(m => boxes[m]).ToArray(), at);
        if (placement.OverRegionEdge)
        {
            // Over a responsive app's region edge App Designer hides its alignment hints, keeping the
            // place they snapped the selection to, and refuses the move there (TemplateComponentMove).
            Guides = [];
        }

        document.Preview(boxes);
        Placement = placement;
        DropTarget = placement is { CanDrop: true, Spans: null, Container: { } target } && !ReferenceEquals(target, anchor.Parent) ? target : null;
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

        // A labelled component: its field by the dragged side, the label following where it stands
        // (LabeledComponentResizeCalculator, open item 150); no alignment hints.
        if (_resizeGroup is { IsLabeled: true, Label: { } label, Field: { } field }
            && start.TryGetValue(field, out Rect2D fieldStart) && start.TryGetValue(label, out Rect2D labelStart) && field.Parent is { } holder)
        {
            Rect2D inner = document.Runtime.InnerOnSurface(holder);
            DesignGroupRules.LabeledBoxes sized = DesignGroupRules.ResizeLabeled(
                fieldStart, labelStart, _edges, dx, dy, document.ShapeOf(field), fromCentre, Grid, new Point2D(inner.X, inner.Y));
            var pair = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance)
            {
                [field] = sized.Field,
                [label] = sized.Label,
            };
            document.Preview(pair);
            return;
        }

        if (Grid is null && ShowAlignmentHints)
        {
            Rect2D sized = DesignGeometry.Resize(start[anchor], _edges, dx, dy, document.ShapeOf(anchor), fromCentre, keepAspect);
            DesignSnap snap = DesignGeometry.Snap(sized, Others(start.Keys), _edges);
            dx += _edges.HasFlag(ResizeEdges.West) || _edges.HasFlag(ResizeEdges.East) ? snap.DX : 0;
            dy += _edges.HasFlag(ResizeEdges.North) || _edges.HasFlag(ResizeEdges.South) ? snap.DY : 0;
            Guides = snap.Guides;
        }

        // In a responsive app's region each box is then held 6 pixels inside it, snapped or not
        // (BeginTemplateComponentResize's offsets, applied by ResizeCalculator).
        RegionLimits? limits = RegionLimitsOf(start);
        var boxes = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach ((DesignComponent component, Rect2D box) in start)
        {
            DesignShape shape = document.ShapeOf(component);
            Rect2D sized = DesignGeometry.Resize(box, _edges, dx, dy, shape, fromCentre, keepAspect, Grid);
            double? ratio = shape.Ratio ?? (keepAspect && box.Height > 0 ? box.Width / box.Height : null);
            boxes[component] = limits is { } l ? InRegion(box, sized, l, fromCentre, ratio) : sized;
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

    // --- the library (DS3) -----------------------------------------------------------------------

    /// <summary>
    /// A library entry dropped at <paramref name="at"/> on the surface: added into the container there
    /// that can hold it, its box's top-left corner where it landed (snapped to a shown grid).
    /// </summary>
    /// <returns>What was added; empty when the entry was refused (<see cref="Refusal"/> says why).</returns>
    public IReadOnlyList<DesignComponent> Drop(DesignPaletteEntry entry, Point2D at)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Cancel();
        _drawEntry = null;
        _libraryDrag = false;
        DesignPlacement placement = Document.PlacementForNew(entry, at);
        if (!placement.CanDrop || placement.Container is not { } container)
        {
            Refusal = $"{entry.Name} cannot be dropped there: in a grid it goes into the cells it covers.";
            Changed?.Invoke(this, EventArgs.Empty);
            return [];
        }

        return Document.IsGrid(container) && placement.Spans is { } spans
            ? AddFrom(() => Document.AddToGrid(entry, container, spans))
            : entry.Type == DesignDocument.GridType && !entry.FigureOnly
                ? AddFrom(() => [Document.Gridify(container)])
                : AddFrom(() => Document.Add(entry, container, SnapCorner(container, at), null));
    }

    /// <summary>
    /// A library entry dragged over the canvas with its corner at <paramref name="at"/>: where it
    /// would drop (<see cref="Placement"/>, <see cref="DropTarget"/>), and every grid's cells shown.
    /// </summary>
    public void DragOver(DesignPaletteEntry entry, Point2D at)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _libraryDrag = true;
        DesignPlacement placement = Document.PlacementForNew(entry, at);
        Placement = placement;
        DropTarget = placement is { CanDrop: true, Spans: null, Container: { } target } && !ReferenceEquals(target, Document.Design.Figure)
            ? target
            : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A library drag left the canvas.</summary>
    public void DragLeave()
    {
        _libraryDrag = false;
        Placement = null;
        DropTarget = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<DesignComponent> AddFrom(Func<IReadOnlyList<DesignComponent>> add)
    {
        Refusal = null;
        try
        {
            return add();
        }
        catch (InvalidOperationException ex)
        {
            Refusal = ex.Message;
            return [];
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The pointer moved while drawing: the box from the press - or, in a grid, the cells from the
    /// pressed one to the pointer (App Designer reuses the span resize's snapping). A Grid Layout is
    /// not drawn (its model's <c>CanDraw</c> is false): a drag shows nothing and adds nothing.
    /// </summary>
    private void DragDraw(Point2D at)
    {
        if (_drawEntry?.Type == DesignDocument.GridType)
        {
            Drawn = null;
            return;
        }

        if (_drawIn is { Container: { } grid, Spans: { } spans } && Document.IsGrid(grid) && Document.CellsOf(grid) is { } cells)
        {
            if (spans is [var start])
            {
                DrawnCells = (grid, [DesignGridRules.DrawnSpan(cells, start, _down, at)]);
            }
            else if (spans is [var label, var field] && _drawEntry is { } entry)
            {
                (int dColumn, int dRow) = DesignDocument.LabelOffsetOf(entry.Label);
                (GridSpan drawnLabel, GridSpan drawnField) = DesignGridRules.DrawnLabeledSpans(cells, label, field, dColumn, dRow, _down, at);
                DrawnCells = (grid, [drawnLabel, drawnField]);
            }

            return;
        }

        if (_drawIn?.Spans is null)
        {
            Drawn = DrawnBox(at);
        }
    }

    /// <summary>The press let go while drawing: what was drawn is added, and the mode ends.</summary>
    private void ReleaseDraw(DesignPaletteEntry entry)
    {
        DesignPlacement? drawIn = _drawIn;
        (DesignComponent Grid, IReadOnlyList<GridSpan> Spans)? cells = DrawnCells;
        Rect2D? drawn = Drawn is { Width: >= DesignGeometry.MinimumDrawn, Height: >= DesignGeometry.MinimumDrawn } box ? box : null;
        bool dragged = System.Math.Abs(_lastDrag.X - _pressAt.X) >= DragThreshold || System.Math.Abs(_lastDrag.Y - _pressAt.Y) >= DragThreshold;
        Drawn = null;
        DrawnCells = null;
        _drawIn = null;
        _drawEntry = null;
        if (drawIn?.Container is not { } container)
        {
            return;
        }

        if (entry.Type == DesignDocument.GridType && dragged)
        {
            Refusal = "A Grid Layout is not drawn: click where it goes.";
            return;
        }

        if (cells is { } c)
        {
            AddFrom(() => Document.AddToGrid(entry, c.Grid, c.Spans));
        }
        else if (entry.Type == DesignDocument.GridType && !entry.FigureOnly)
        {
            AddFrom(() => [Document.Gridify(container)]);
        }
        else
        {
            AddFrom(() => Document.Add(entry, container, drawn is { } d ? new Point2D(d.X, d.Y) : _drawFrom, drawn is { } s ? new Size2D(s.Width, s.Height) : null));
        }
    }

    /// <summary>A corner on the surface, snapped to the grid of <paramref name="container"/> when the grid is shown and snapped to.</summary>
    private Point2D SnapCorner(DesignComponent container, Point2D at)
    {
        if (Grid is not { } grid)
        {
            return at;
        }

        Rect2D inner = Document.Runtime.InnerOnSurface(container);
        return new Point2D(
            inner.X + DesignGeometry.OnGrid(at.X - inner.X, grid),
            inner.Y + DesignGeometry.OnGrid(at.Y - inner.Y, grid));
    }

    /// <summary>The box drawn from the press to <paramref name="at"/>, in whole pixels; its far corner on a shown grid as its near one is.</summary>
    private Rect2D DrawnBox(Point2D at)
    {
        double x = at.X;
        double y = at.Y;
        if (Grid is { } grid)
        {
            x = _drawFrom.X + DesignGeometry.OnGrid(x - _drawFrom.X, grid);
            y = _drawFrom.Y + DesignGeometry.OnGrid(y - _drawFrom.Y, grid);
        }

        double left = System.Math.Round(System.Math.Min(x, _drawFrom.X), MidpointRounding.AwayFromZero);
        double top = System.Math.Round(System.Math.Min(y, _drawFrom.Y), MidpointRounding.AwayFromZero);
        double right = System.Math.Round(System.Math.Max(x, _drawFrom.X), MidpointRounding.AwayFromZero);
        double bottom = System.Math.Round(System.Math.Max(y, _drawFrom.Y), MidpointRounding.AwayFromZero);
        return new Rect2D(left, top, right - left, bottom - top);
    }

    // --- handles and the pointer -----------------------------------------------------------------

    /// <summary>
    /// The resize handles of the selection: eight on each component placed by its position, none along
    /// a side its shape fixes; a grid's child has the same, on its cells' edges (DS4), and a grid has
    /// eight only in a grid's cells - one in any other container fills it. None while a grid is configured.
    /// </summary>
    public IEnumerable<DesignHandle> Handles()
    {
        DesignDocument document = Document;
        if (Configuring is not null)
        {
            yield break;
        }

        // A labelled component selected whole has side handles on its box, by how its field resizes
        // (GroupController._updateLabeledComponentResizeHandles, open item 150); any other group has none.
        foreach (DesignGroup group in document.SelectedGroups)
        {
            if (group is not { IsLabeled: true, Field: { } field } || document.BoxOfGroup(group) is not { } gb)
            {
                continue;
            }

            DesignShape fieldShape = document.ShapeOf(field);
            (bool across, bool along) = DesignGroupRules.ResizeOf(field.Type) switch
            {
                LabeledResize.Horizontal => (true, false),
                LabeledResize.Fixed => fieldShape.FixedHeight && !fieldShape.FixedWidth ? (true, false) : fieldShape.FixedWidth && !fieldShape.FixedHeight ? (false, true) : (false, false),
                _ => (true, true),
            };
            double hh = HandleSize / 2;
            if (across)
            {
                yield return new DesignHandle(field, ResizeEdges.West, new Rect2D(gb.X - hh, gb.Y + (gb.Height / 2) - hh, HandleSize, HandleSize), group);
                yield return new DesignHandle(field, ResizeEdges.East, new Rect2D(gb.X + gb.Width - hh, gb.Y + (gb.Height / 2) - hh, HandleSize, HandleSize), group);
            }

            if (along)
            {
                yield return new DesignHandle(field, ResizeEdges.North, new Rect2D(gb.X + (gb.Width / 2) - hh, gb.Y - hh, HandleSize, HandleSize), group);
                yield return new DesignHandle(field, ResizeEdges.South, new Rect2D(gb.X + (gb.Width / 2) - hh, gb.Y + gb.Height - hh, HandleSize, HandleSize), group);
            }
        }

        foreach (DesignPick pick in document.Picks)
        {
            if (pick.Component is not { } component)
            {
                continue;
            }

            bool inGrid = document.IsGridChild(component);
            if ((!document.IsMovable(component) && !inGrid) || document.BoxOf(component) is not { Shown: true } box)
            {
                continue;
            }

            DesignShape shape = component.Type == DesignDocument.GridType ? DesignShape.Free : document.ShapeOf(component);
            Rect2D b = inGrid && document.CellBoxOf(component) is { } cells ? cells : box.Box;
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

                // A shape that keeps its ratio has its corners only (AbstractDesignTimeController._attachResizeHandles, DS4).
                if (shape.Ratio is not null && !(vertical && horizontal))
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
        if (_drawEntry is not null)
        {
            return DesignCursor.Cross;
        }

        if (SplitterAt(at) is not null)
        {
            return DesignCursor.SizeWestEast;
        }

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
        DesignPick pick = Document.PickAt(at);
        if (pick.Group is { } group)
        {
            return DesignGroupRules.IsSelected(Document.Picks, pick) && group.AllComponents().All(Document.CanDrag) ? DesignCursor.Move : DesignCursor.Arrow;
        }

        return Document.Selection.Contains(hit) && Document.CanDrag(hit) && !IsBody(hit, at) ? DesignCursor.Move : DesignCursor.Arrow;
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
            case DesignKey.Escape when _drawEntry is not null:
                DrawEntry = null;
                return true;
            case DesignKey.Escape when Configuring is not null:
                EndConfigure();
                return true;
            case DesignKey.Delete when _press == PressState.None && document.Selection.Any(document.CanDelete):
                EndKeyMove();
                document.Delete(document.Selection);
                return true;
            case DesignKey.Group when _press == PressState.None:
                EndKeyMove();
                return document.Group() is not null;
            case DesignKey.Ungroup when _press == PressState.None:
                EndKeyMove();
                return document.Ungroup();
            case DesignKey.Left or DesignKey.Right or DesignKey.Up or DesignKey.Down
                when (modifiers & ~DesignModifiers.Shift) == DesignModifiers.None && _press == PressState.None:
                double step = modifiers.HasFlag(DesignModifiers.Shift) ? DesignGeometry.ShiftNudge : DesignGeometry.Nudge;

                // App Designer's arrows move no grid's child, and no grid, but take the key (ArrowKeyMoveReorderPlugin).
                if (document.Selection is [var first, ..] && (document.IsGridChild(first) || first.Type == DesignDocument.GridType))
                {
                    return true;
                }

                // Nor a responsive app's region (ArrowKeyMoveReorderPlugin, DS4b).
                if (document.Selection.Any(document.IsRegion))
                {
                    return true;
                }

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
                (_keyDX, _keyDY) = InRegion(document.GestureStart, _keyDX, _keyDY);
                var boxes = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
                foreach ((DesignComponent component, Rect2D box) in document.GestureStart)
                {
                    boxes[component] = Shifted(box, _keyDX, _keyDY);
                }

                document.Preview(boxes);
                return true;
            case DesignKey.SelectAll:
                DesignComponent container = document.Selection.Count > 0 && document.Selection[0].Parent is { } parent ? parent : document.Design.Figure;
                document.Select(document.Boxes.Where(b => b.Shown && ReferenceEquals(b.Component.Parent, container) && !document.IsResponsiveGrid(b.Component))
                    .Select(static b => b.Component).ToArray());
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

    // --- responsive apps (DS4b) -----------------------------------------------------------------

    /// <summary>
    /// The splitter being dragged and where the drag has the regions - with whether they stopped short
    /// of the pointer, which App Designer shows on the splitter (<c>ad-template-resize-handle-alert</c>);
    /// null when no splitter is dragged.
    /// </summary>
    public (DesignSplitter Splitter, DesignRegionResize Resize)? Splitting =>
        _press == PressState.Splitting && _splitter is { } splitter && _regionResize is { } resize ? (splitter, resize) : null;

    /// <summary>The splitter between a responsive app's regions at <paramref name="at"/>, or null.</summary>
    public DesignSplitter? SplitterAt(Point2D at)
    {
        foreach (DesignSplitter splitter in Document.Splitters())
        {
            if (splitter.Box.Contains(at))
            {
                return splitter;
            }
        }

        return null;
    }

    /// <summary>
    /// How far a selection's edges may move in its responsive app's region (App Designer's
    /// <c>LayoutTemplateUtils.getMaxNegativeOffsets</c> and <c>getMaxPositiveOffsets</c>, from the
    /// region's box and the selection's): its left and top no further than 6 pixels inside the region's,
    /// its right and bottom likewise. Null when the selection is in no region.
    /// </summary>
    private RegionLimits? RegionLimitsOf(IReadOnlyDictionary<DesignComponent, Rect2D> start)
    {
        DesignDocument document = Document;
        if (start.Count == 0 || document.RegionOf(start.Keys.First()) is not { } region || document.BoxOf(region) is not { } box)
        {
            return null;
        }

        Rect2D r = box.Box;
        double inset = DesignDocument.RegionInset;
        return new RegionLimits(
            r.Left - start.Values.Min(static b => b.Left) + inset,
            r.Top - start.Values.Min(static b => b.Top) + inset,
            r.Right - start.Values.Max(static b => b.Right) - inset,
            r.Bottom - start.Values.Max(static b => b.Bottom) - inset);
    }

    /// <summary>The offsets a selection's left and top edges may go down to, and its right and bottom up to.</summary>
    private readonly record struct RegionLimits(double Left, double Top, double Right, double Bottom);

    /// <summary>
    /// A move by the arrow keys of components in a responsive app's region, kept 6 pixels inside it
    /// (<c>ArrowKeyMoveReorderPlugin._buildArrowData</c>); a selection already nearer its region's edge
    /// is not pushed in.
    /// </summary>
    private (double DX, double DY) InRegion(IReadOnlyDictionary<DesignComponent, Rect2D> start, double dx, double dy)
    {
        if (RegionLimitsOf(start) is not { } limits)
        {
            return (dx, dy);
        }

        dx = System.Math.Max(System.Math.Min(dx, System.Math.Max(0, limits.Right)), System.Math.Min(0, limits.Left));
        dy = System.Math.Max(System.Math.Min(dy, System.Math.Max(0, limits.Bottom)), System.Math.Min(0, limits.Top));
        return (dx, dy);
    }

    /// <summary>
    /// A box resized from <paramref name="start"/> to <paramref name="sized"/> - snapped to the guides
    /// or the grid, or not - held inside its responsive app's region (App Designer's
    /// <c>ResizeCalculator</c> with the offsets of <see cref="RegionLimitsOf"/>): the most any moved edge
    /// passes its limit across, and the most down, is taken back from every edge that moved - from both
    /// sides of a resize from the centre, and for a shape that keeps its ratio across and down together
    /// through the ratio. An edge that did not move is not counted, where App Designer counts it too, so
    /// that a box already nearer its region's edge than 6 pixels shrinks when dragged the other way.
    /// </summary>
    private static Rect2D InRegion(Rect2D start, Rect2D sized, RegionLimits limits, bool fromCentre, double? ratio)
    {
        double left = sized.Left - start.Left;
        double top = sized.Top - start.Top;
        double right = sized.Right - start.Right;
        double bottom = sized.Bottom - start.Bottom;
        double across = System.Math.Max(left != 0 ? System.Math.Max(0, limits.Left - left) : 0, right != 0 ? System.Math.Max(0, right - limits.Right) : 0);
        double down = System.Math.Max(top != 0 ? System.Math.Max(0, limits.Top - top) : 0, bottom != 0 ? System.Math.Max(0, bottom - limits.Bottom) : 0);
        if (across == 0 && down == 0)
        {
            return sized;
        }

        if (ratio is { } o && o > 0)
        {
            double back = System.Math.Max(down * o, across);
            double backDown = System.Math.Round(back / o, MidpointRounding.AwayFromZero);
            left += left != 0 ? back : 0;
            top += top != 0 ? backDown : 0;
            right -= right != 0 ? back : 0;
            bottom -= bottom != 0 ? backDown : 0;
        }
        else if (fromCentre)
        {
            left += across;
            right -= across;
            top += down;
            bottom -= down;
        }
        else
        {
            left += left != 0 ? across : 0;
            top += top != 0 ? down : 0;
            right -= right != 0 ? across : 0;
            bottom -= bottom != 0 ? down : 0;
        }

        double width = start.Width + right - left;
        double height = start.Height + bottom - top;
        return width < 1 || height < 1 ? start : new Rect2D(start.Left + left, start.Top + top, width, height);
    }

    // --- grids (DS4) -----------------------------------------------------------------------------

    /// <summary>The side of a grid's configure button, its border in.</summary>
    public const double ConfigureButtonSize = 22;

    /// <summary>How far up and left of a grid its configure button stands (<c>left: -25px; top: -25px</c>).</summary>
    public const double ConfigureButtonOffset = 25;

    /// <summary>The button at a grid's top-left corner that configures it, or ends its configuring: 25 pixels up and left of the grid.</summary>
    public Rect2D? ConfigureButton(DesignComponent grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return Document.BoxOf(grid) is { Shown: true } box
            ? new Rect2D(box.Box.X - ConfigureButtonOffset, box.Box.Y - ConfigureButtonOffset, ConfigureButtonSize, ConfigureButtonSize)
            : null;
    }

    /// <summary>The grid whose configure button is at <paramref name="at"/>: one showing it (<see cref="DesignGridMode.Simple"/>); null when none.</summary>
    public DesignComponent? ConfigureButtonAt(Point2D at)
    {
        foreach ((DesignComponent grid, DesignGridMode mode) in GridModes())
        {
            if (mode == DesignGridMode.Simple && ConfigureButton(grid) is { } button && button.Contains(at))
            {
                return grid;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts configuring <paramref name="grid"/> (App Designer's grid edit mode): its rows and
    /// columns become bands to pick, and the pick opens the popup that sets it.
    /// </summary>
    public void Configure(DesignComponent grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!Document.IsGrid(grid))
        {
            return;
        }

        Cancel();
        _configure = new DesignGridConfigure(Document, grid);
        _configureSelection = [.. Document.Selection];
        _configure.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends configuring a grid.</summary>
    public void EndConfigure()
    {
        if (_configure is not null)
        {
            _configure = null;
            Hint = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// A double click at <paramref name="at"/>: with one grid selected, configures it
    /// (<c>GridRichEditPlugin.handleDoubleClick</c>).
    /// </summary>
    /// <returns>Whether it did.</returns>
    public bool DoubleClick(Point2D at)
    {
        if (Document.Selection is [var grid] && Document.IsGrid(grid) && Configuring?.Grid != grid)
        {
            Configure(grid);
            return true;
        }

        return false;
    }

    /// <summary>
    /// How each grid shows (<c>SelectableController._visualize</c>): a selected grid, and the nearest
    /// grid around a selected component, show their cells and the button that configures them; the
    /// grids further out, and the grids inside one showing its button, show their cells; with the
    /// figure selected, every grid not in a grid shows its button; the grid being configured is in
    /// edit mode. Grids not listed show nothing.
    /// </summary>
    public IReadOnlyDictionary<DesignComponent, DesignGridMode> GridModes()
    {
        DesignDocument document = Document;
        var modes = new Dictionary<DesignComponent, DesignGridMode>(ReferenceEqualityComparer.Instance);
        foreach (DesignComponent selected in document.Selection)
        {
            bool first = true;
            for (DesignComponent? up = selected; up?.Parent is not null; up = up.Parent)
            {
                if (document.IsGrid(up))
                {
                    modes[up] = first ? DesignGridMode.Simple : DesignGridMode.Layout;
                    first = false;
                }
            }

            Down(selected);
        }

        if (Configuring is { } configure)
        {
            modes[configure.Grid] = DesignGridMode.Edit;
        }

        return modes;

        void Down(DesignComponent node)
        {
            if (document.IsGrid(node))
            {
                bool simpleAbove = false;
                for (DesignComponent? up = node.Parent; up is not null; up = up.Parent)
                {
                    simpleAbove |= modes.TryGetValue(up, out DesignGridMode mode) && mode == DesignGridMode.Simple;
                }

                modes[node] = simpleAbove ? DesignGridMode.Layout : DesignGridMode.Simple;
            }

            foreach (DesignComponent child in node.Children)
            {
                Down(child);
            }
        }
    }

    /// <summary>Shows <paramref name="hint"/> - a row or column a menu item or popup button would add or delete - or none.</summary>
    public void ShowHint(DesignGridHint? hint)
    {
        if (Hint != hint)
        {
            Hint = hint;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The canvas's context menu for a right click at <paramref name="at"/>, with the selection the
    /// press left (App Designer's <c>GridMenuItemProvider</c>, the delete item, and
    /// <c>LayoutConversionMenuItemProvider</c>): for one grid selected, its configuring and the rows and
    /// columns the point is in or between - in a cell, beside it; in a gap, into it; in the padding,
    /// first or last - and removing its layout; Delete; and Apply Grid Layout for a figure, panel or tab.
    /// </summary>
    public IReadOnlyList<DesignMenuItem> MenuAt(Point2D at)
    {
        DesignDocument document = Document;
        var items = new List<DesignMenuItem>();
        if (document.Selection is [var grid] && document.IsGrid(grid) && document.CellsOf(grid) is { } cells)
        {
            int rows = document.TracksOf(grid, GridAxis.Row).Count;
            int columns = document.TracksOf(grid, GridAxis.Column).Count;
            int? above, below, before, after, deleteRow, deleteColumn;
            if (rows == 0 || columns == 0)
            {
                // An empty list takes one track; nothing can be deleted.
                above = below = rows == 0 ? 1 : null;
                before = after = columns == 0 ? 1 : null;
                deleteRow = deleteColumn = null;
            }
            else
            {
                GridPointInfo info = DesignGridRules.PointInfo(at, cells);
                (above, below) = DesignGridRules.AddingRows(info);
                (before, after) = DesignGridRules.AddingColumns(info);
                deleteRow = info.Row;
                deleteColumn = info.Column;
            }

            items.Add(new DesignMenuItem("Configure Grid Layout", Configuring?.Grid != grid, () => Configure(grid)));
            items.Add(new DesignMenuItem("Add Row", above is not null || below is not null, Items:
            [
                Insert("Above", GridAxis.Row, above),
                Insert("Below", GridAxis.Row, below),
            ]));
            items.Add(new DesignMenuItem("Add Column", before is not null || after is not null, Items:
            [
                Insert("Left", GridAxis.Column, before),
                Insert("Right", GridAxis.Column, after),
            ]));
            items.Add(Remove("Delete Row", GridAxis.Row, deleteRow));
            items.Add(Remove("Delete Column", GridAxis.Column, deleteColumn));
            items.Add(new DesignMenuItem("Remove Grid Layout", true, () =>
            {
                EndConfigure();
                document.Ungridify(grid);
            }));

            DesignMenuItem Insert(string label, GridAxis axis, int? index) =>
                new(label, index is not null, index is { } i ? () => document.InsertTrack(grid, axis, i) : null,
                    Hint: index is { } h ? new DesignGridHint(grid, axis, h, Insert: true) : null);

            DesignMenuItem Remove(string label, GridAxis axis, int? index) =>
                new(label, index is not null, index is { } i ? () => document.DeleteTrack(grid, axis, i) : null,
                    Hint: index is { } h ? new DesignGridHint(grid, axis, h, Insert: false) : null);
        }

        if (document.Selection.Any(document.CanDelete))
        {
            if (items.Count > 0)
            {
                items.Add(DesignMenuItem.Separator);
            }

            items.Add(new DesignMenuItem("Delete", true, () => document.Delete(document.Selection)));
        }

        // The grouping actions (GroupingAction, open item 150), each when it applies.
        if (document.Selection.Any(static c => c.Parent is not null))
        {
            if (items.Count > 0)
            {
                items.Add(DesignMenuItem.Separator);
            }

            items.Add(new DesignMenuItem("Group", document.CanGroup, () => document.Group()));
            items.Add(new DesignMenuItem("Add to Group", document.CanAddToGroup, () => document.AddToGroup()));
            items.Add(new DesignMenuItem("Remove from Group", document.CanRemoveFromGroup, () => document.RemoveFromGroup()));
            items.Add(new DesignMenuItem("Ungroup", document.CanUngroup, () => document.Ungroup()));
        }

        if (document.Selection is [var container, ..] && document.CanGridify(container))
        {
            if (items.Count > 0)
            {
                items.Add(DesignMenuItem.Separator);
            }

            items.Add(new DesignMenuItem("Apply Grid Layout", true, () => document.Gridify(container)));
        }

        return items;
    }
}

/// <summary>
/// An item of the canvas's context menu: its label, whether it is enabled, what it does, the items
/// under it, and the row or column it would add or delete, which the canvas shows while it is pointed at.
/// </summary>
public sealed record DesignMenuItem(string Label, bool Enabled = true, Action? Run = null, IReadOnlyList<DesignMenuItem>? Items = null, DesignGridHint? Hint = null)
{
    /// <summary>A line between groups of items.</summary>
    public static DesignMenuItem Separator { get; } = new("-", false);

    /// <summary>Whether this is the line between groups.</summary>
    public bool IsSeparator => ReferenceEquals(this, Separator);
}
