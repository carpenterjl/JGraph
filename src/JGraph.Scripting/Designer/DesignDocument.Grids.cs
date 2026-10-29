using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Where what a gesture or a library drop carries would go (designer plan DS4, App Designer's
/// placement): the container, and in a grid the cells each component takes, in the order the
/// components were given; and whether it can be dropped there. A container that refuses a drop still
/// holds the pointer, as a grid does, so that the drop does not fall through to what is behind it.
/// </summary>
public sealed record DesignPlacement(DesignComponent? Container, bool CanDrop, IReadOnlyList<GridSpan>? Spans = null)
{
    /// <summary>
    /// Whether a move is refused because it is over the edge of a responsive app's region, where App
    /// Designer clears its alignment hints (<c>TemplateComponentMove</c>, DS4b).
    /// </summary>
    public bool OverRegionEdge { get; init; }
}

/// <summary>The grids of an app open in the designer (designer plan DS4).</summary>
public sealed partial class DesignDocument
{
    /// <summary>The class of a grid.</summary>
    public const string GridType = "matlab.ui.container.GridLayout";

    /// <summary>
    /// The size of the Grid Layout entry's avatar in the library (<c>GridLayoutEntryDefinition._makeEntryData</c>),
    /// which a drop places by: the cells it covers in a grid hold the new grid.
    /// </summary>
    public static readonly Size2D GridAvatar = new(204, 141);

    /// <summary>
    /// App Designer's own grid layout's padding and spacings, which a new grid starts from and the
    /// gridifier is given (<c>GridLayout.m</c>; the adapter customises nothing).
    /// </summary>
    private static readonly double[] GridPadding = [10, 10, 10, 10];

    private const double GridSpacing = 10;

    /// <summary>
    /// Whether <paramref name="component"/> is the grid a responsive (auto-reflow) app's figure holds,
    /// which App Designer never edits as a grid: it shows no cells, takes no drop and is no target.
    /// An app whose grid is not one of App Designer's templates has none: App Designer opens it as a
    /// standard app (<c>FigureUtil.getTemplateName</c>).
    /// </summary>
    public bool IsResponsiveGrid(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Type == GridType && IsResponsive && ReferenceEquals(component, DesignResponsive.Grid(Design));
    }

    /// <summary>Whether <paramref name="component"/> is a grid the canvas edits as one.</summary>
    public bool IsGrid(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Type == GridType && !IsResponsiveGrid(component);
    }

    /// <summary>Whether <paramref name="component"/> sits in a cell of a grid the canvas edits.</summary>
    public bool IsGridChild(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Parent is { } parent && IsGrid(parent);
    }

    /// <summary>Whether a press and drag on <paramref name="component"/> moves it: one placed by its position, or a grid's child.</summary>
    public bool CanDrag(DesignComponent component) => IsMovable(component) || IsGridChild(component);

    /// <summary>The cells of <paramref name="grid"/> as the canvas draws them; null for one not shown.</summary>
    public GridCells? CellsOf(DesignComponent grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        IReadOnlyList<DesignBox> boxes = Boxes;
        if (!ReferenceEquals(_cellsFor, boxes))
        {
            // Worked out again with the boxes, after every change.
            _cellsFor = boxes;
            _cells.Clear();
        }

        if (!_cells.TryGetValue(grid, out GridCells? cells))
        {
            cells = Runtime.CellsOf(grid);
            _cells[grid] = cells;
        }

        return cells;
    }

    private readonly Dictionary<DesignComponent, GridCells?> _cells = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<DesignBox>? _cellsFor;

    /// <summary>
    /// The cells a grid's child sits in: its <c>Layout.Row</c> and <c>Layout.Column</c> as the design
    /// holds them, else as its component has them (a child the code never placed, which the grid put).
    /// </summary>
    public GridSpan? SpanOf(DesignComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (GridSpan.FromValues(child.Get("Layout.Row"), child.Get("Layout.Column")) is { } held)
        {
            return held;
        }

        return Runtime.ReadNumbers(child, "Layout.Row") is { Length: 1 or 2 } row
            && Runtime.ReadNumbers(child, "Layout.Column") is { Length: 1 or 2 } column
            ? GridSpan.FromValues(NumericValue.Row(row), NumericValue.Row(column))
            : null;
    }

    /// <summary>
    /// The box of a grid child's cells on the surface - where App Designer's design-time wrapper of it
    /// is, which its handles and a drag go by - or null for a component not in a grid. A slider or a
    /// gauge is drawn smaller, inside it.
    /// </summary>
    public Rect2D? CellBoxOf(DesignComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.Parent is { } grid && IsGrid(grid) && SpanOf(child) is { } span && CellsOf(grid) is { } cells
            && span.RowStart >= 1 && span.ColumnStart >= 1 && span.RowEnd <= cells.Rows && span.ColumnEnd <= cells.Columns
            ? cells.SpanRect(span)
            : null;
    }

    /// <summary>
    /// Where a new component of <paramref name="entry"/> is drawn from a press at
    /// <paramref name="at"/> (App Designer's click and draw, <c>ClickAndDrawAlignmentHintsGestureManager</c>):
    /// in a grid, the cell under the pointer - a labelled entry's label there and its component
    /// beside it (<see cref="DesignGridRules.LabeledClick"/>) - and a press in a grid's gap or padding
    /// refused; elsewhere, the front container under the pointer, which refuses it when it cannot
    /// hold it.
    /// </summary>
    public DesignPlacement DrawPlacement(DesignPaletteEntry entry, Point2D at)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.FigureOnly || entry.Type == "matlab.ui.container.Menu")
        {
            return new DesignPlacement(Design.Figure, CanDrop: true);
        }

        bool isGrid = entry.Type == GridType;
        (int dColumn, int dRow) = LabelOffsetOf(entry.Label);
        GridAnswer InGrid(GridCells cells)
        {
            if (DesignGridRules.CellUnderPoint(at, cells) is not { } cell)
            {
                return GridAnswer.None;
            }

            if (!CanBeInGrid(entry.Type))
            {
                return GridAnswer.Refused;
            }

            if (entry.Label == DesignLabelPlace.None)
            {
                return new GridAnswer([cell], Holds: true);
            }

            return DesignGridRules.LabeledClick(cell, dColumn, dRow, componentDraw: true, cells.Rows, cells.Columns) is { } pair
                ? new GridAnswer([pair.Label, pair.Field], Holds: true)
                : GridAnswer.Refused;
        }

        bool Accepts(DesignComponent container) => isGrid ? CanGridify(container) : CanHoldNew(container, entry.Type);
        DesignPlacement? found = Search([], null, at, InGrid, container => PointerIn(container, at), Accepts);
        if (found is not null)
        {
            return found;
        }

        // A responsive app's figure takes no new component outside its regions, which cover it (DS4b).
        return Accepts(Design.Figure) && !IsResponsive ? new DesignPlacement(Design.Figure, CanDrop: true) : new DesignPlacement(Design.Figure, CanDrop: false);
    }

    /// <summary>Whether a component of <paramref name="type"/> can sit in a grid's cell: not a radio or toggle button, which need a button group, nor a tab.</summary>
    public static bool CanBeInGrid(string type) =>
        type is not ("matlab.ui.control.RadioButton" or "matlab.ui.control.ToggleButton" or "matlab.ui.container.Tab"
            or "matlab.ui.container.Menu" or "matlab.ui.container.ContextMenu" or "matlab.ui.container.Toolbar"
            or "matlab.ui.container.TreeNode" or "matlab.ui.container.CheckBoxTreeNode");

    // --- placement -------------------------------------------------------------------------------

    /// <summary>
    /// Where <paramref name="moving"/> - all in one container - would go, dropped with their boxes at
    /// <paramref name="boxes"/> on the surface (in the same order) and the pointer at
    /// <paramref name="pointer"/> (App Designer's placement, read from <c>PlacementUtil</c> and the
    /// movement validators). The containers are tried deepest first, the front one first, a selected
    /// container's own before the rest:
    /// <list type="bullet">
    /// <item>a grid takes the cells each box covers (<see cref="DesignGridRules.SpanOfBounds"/>); it
    /// refuses, and holds the drop, what cannot be in a grid; it does not take several components
    /// coming from a container that places by position (<c>SelectionValidator</c>), nor boxes that
    /// touch none of its cells, but holds the pointer over it;</item>
    /// <item>a panel, tab or button group holds boxes wholly inside it - or, for the container they
    /// come from, touching it (<c>BoundsPositionValidator</c>) - and refuses what it cannot hold; a
    /// button group refuses a grid (<c>GridLayoutModel.canMoveToParent</c>). The canvas never starts a
    /// move by pressing a grid, so App Designer's rules for that (by the pointer, into an empty
    /// container only) do not arise;</item>
    /// <item>a container holding a grid is reached through its grid only, unless the drop comes from it.</item>
    /// </list>
    /// Else the figure takes them, if it can hold them; else they stay where they are.
    /// </summary>
    public DesignPlacement PlacementFor(IReadOnlyList<DesignComponent> moving, IReadOnlyList<Rect2D> boxes, Point2D pointer)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(boxes);
        if (moving.Count == 0 || moving.Count != boxes.Count)
        {
            return new DesignPlacement(null, CanDrop: false);
        }

        DesignComponent from = moving[0].Parent ?? Design.Figure;
        bool fromGrid = IsGrid(from);
        bool movingGrid = moving.Any(static m => m.Type == GridType);
        bool groupMembers = moving.Any(m => Design.GroupOf(m) is not null);
        bool labeled = moving.Any(m => Design.LabeledGroupOf(m) is not null);
        Rect2D union = Union(boxes);
        GridAnswer InGrid(GridCells cells)
        {
            if (!moving.All(m => CanBeInGrid(m.Type)))
            {
                return DesignGridRules.SpansOfBounds(boxes, cells) is null ? GridAnswer.None : GridAnswer.Refused;
            }

            // A selection holding a group member moves as one, its cells together, into a grid
            // (CellLocationValidator, open item 150); a labelled component from elsewhere cannot go in.
            if (groupMembers && fromGrid)
            {
                GridSpan[] own = moving.Select(m => SpanOf(m) ?? GridSpan.Cell(1, 1)).ToArray();
                GridSpan box = DesignGridRules.Union(own);
                DesignGridRules.GroupMoveResult result = DesignGridRules.GroupMove(union, box, cells);
                if (!result.InContainer)
                {
                    return GridAnswer.None;
                }

                if (!result.CanDrop)
                {
                    return GridAnswer.Refused;
                }

                return new GridAnswer(own.Select(s => DesignGridRules.Shifted(s, result.Row - box.RowStart, result.Column - box.ColumnStart)).ToArray(), Holds: true);
            }

            if (groupMembers || labeled)
            {
                return DesignGridRules.SpansOfBounds(boxes, cells) is null ? GridAnswer.None : GridAnswer.Refused;
            }

            IReadOnlyList<GridSpan>? spans = DesignGridRules.SpansOfBounds(boxes, cells);
            return spans is null || (moving.Count > 1 && !fromGrid) ? GridAnswer.None : new GridAnswer(spans, Holds: true);
        }

        bool Inside(DesignComponent container) =>
            BoxOf(container) is { } box && (ReferenceEquals(container, from) ? Touches(box.Box, union) : Holds(box.Box, union));

        bool Accepts(DesignComponent container) =>
            moving.All(m => CanHold(container, m)) && !(movingGrid && container.Type == "matlab.ui.container.ButtonGroup");
        DesignPlacement? found = Search(moving, from, pointer, InGrid, Inside, Accepts);

        // In a responsive app nothing straddles two regions: a move over a region's edge is refused (DS4b).
        if (found is not { Container: { } into } || !IsGrid(into))
        {
            if (!movingGrid && IsOverRegionEdge(union))
            {
                return new DesignPlacement(found?.Container ?? Design.Figure, CanDrop: false) { OverRegionEdge = true };
            }
        }

        if (found is not null)
        {
            return found;
        }

        // A responsive app's figure holds its grid and nothing else: its regions cover it, and App
        // Designer places no grid on it (PlacementUtil.canPlaceGrid) - what no region takes stays (DS4b).
        if (IsResponsive)
        {
            return new DesignPlacement(from, CanDrop: false);
        }

        return moving.All(m => CanHold(Design.Figure, m))
            ? new DesignPlacement(Design.Figure, CanDrop: true)
            : new DesignPlacement(from, CanDrop: true);
    }

    /// <summary>
    /// Where a new component of <paramref name="entry"/> dropped with its box's top-left corner at
    /// <paramref name="at"/> would go: as <see cref="PlacementFor"/>, with the boxes the entry makes
    /// (<see cref="DropBoxes"/>) and the pointer at the corner; a container other than a grid holds a
    /// drop by the pointer (<c>MousePositionValidator</c>). A labelled entry's label and component take
    /// cells beside each other (<see cref="DesignGridRules.LabeledSpans"/>) - with no room for the
    /// label, the grid refuses it; a Grid Layout goes into a figure, a panel or a tab, or the cells
    /// its avatar covers in a grid; the figure tools into the figure.
    /// </summary>
    public DesignPlacement PlacementForNew(DesignPaletteEntry entry, Point2D at)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.FigureOnly || entry.Type == "matlab.ui.container.Menu")
        {
            return new DesignPlacement(Design.Figure, CanDrop: true);
        }

        IReadOnlyList<Rect2D> boxes = DropBoxes(entry, at);
        bool isGrid = entry.Type == GridType;
        GridAnswer InGrid(GridCells cells)
        {
            if (entry.Label != DesignLabelPlace.None && boxes.Count == 2)
            {
                if (LabeledSpans(entry, boxes, cells) is { } spans)
                {
                    return new GridAnswer(spans, Holds: true);
                }

                // Both on the cells and no room for the label: refused there (CellBoundsValidator).
                return DesignGridRules.SpanOfBounds(boxes[0], cells) is not null && DesignGridRules.SpanOfBounds(boxes[1], cells) is not null
                    ? GridAnswer.Refused
                    : GridAnswer.None;
            }

            if (DesignGridRules.SpanOfBounds(Union(boxes), cells) is not { } span)
            {
                return GridAnswer.None;
            }

            return CanBeInGrid(entry.Type) ? new GridAnswer([span], Holds: true) : GridAnswer.Refused;
        }

        bool Accepts(DesignComponent container) => isGrid ? CanGridify(container) : CanHoldNew(container, entry.Type);
        DesignPlacement? found = Search([], null, at, InGrid, container => PointerIn(container, at), Accepts);
        if (found is not null)
        {
            return found;
        }

        // A responsive app's figure takes no new component outside its regions, which cover it (DS4b).
        return Accepts(Design.Figure) && !IsResponsive ? new DesignPlacement(Design.Figure, CanDrop: true) : new DesignPlacement(Design.Figure, CanDrop: false);
    }

    /// <summary>The label's and the component's cells of a labelled entry dropped in a grid, as <c>LabeledComponentEntryDefinition</c> gives them.</summary>
    private static IReadOnlyList<GridSpan>? LabeledSpans(DesignPaletteEntry entry, IReadOnlyList<Rect2D> boxes, GridCells cells)
    {
        if (DesignGridRules.LabeledSpans(boxes[0], boxes[1], cells) is not { } spans)
        {
            return null;
        }

        (int dColumn, int dRow) = LabelOffsetOf(entry.Label);
        return DesignGridRules.LabelFromField(spans.Field, dColumn, dRow, cells.Rows, cells.Columns) is { } label
            ? [label, spans.Field]
            : null;
    }

    /// <summary>A labelled entry's label's place beside its component, in cells: <c>[columns, rows]</c>.</summary>
    internal static (int DColumn, int DRow) LabelOffsetOf(DesignLabelPlace place) =>
        place == DesignLabelPlace.SouthCenter ? (0, 1) : (-1, 0);

    /// <summary>
    /// What a grid answers a drop: the cells it takes; or nothing taken but the drop held there,
    /// refused; or not held at all, so that the search goes on.
    /// </summary>
    private readonly record struct GridAnswer(IReadOnlyList<GridSpan>? Spans, bool Holds)
    {
        public static GridAnswer None => new(null, false);

        public static GridAnswer Refused => new(null, true);
    }

    /// <summary>
    /// The candidate containers in App Designer's order, each tried: a grid by
    /// <paramref name="inGrid"/>, and holding the pointer over it (<c>GridController._postValidation</c>);
    /// anything else held by <paramref name="inside"/> and then taking the drop or refusing it by
    /// <paramref name="accepts"/> (<c>RestrictedParentValidator</c>). Null when none holds the drop.
    /// </summary>
    private DesignPlacement? Search(
        IReadOnlyList<DesignComponent> moving,
        DesignComponent? from,
        Point2D pointer,
        Func<GridCells, GridAnswer> inGrid,
        Func<DesignComponent, bool> inside,
        Func<DesignComponent, bool> accepts)
    {
        foreach (DesignComponent candidate in Candidates())
        {
            if (moving.Any(m => IsWithin(candidate, m)))
            {
                continue;
            }

            if (IsGrid(candidate))
            {
                if (CellsOf(candidate) is { } cells && inGrid(cells) is { Holds: true } answer)
                {
                    return new DesignPlacement(candidate, CanDrop: answer.Spans is not null, answer.Spans);
                }

                // A grid holds the pointer over it: the drop stops there.
                if (PointerIn(candidate, pointer))
                {
                    return new DesignPlacement(candidate, CanDrop: false);
                }

                continue;
            }

            // A container holding a grid is reached through its grid, unless the drop comes from it.
            if (!ReferenceEquals(candidate, from) && candidate.Children.Any(IsGrid))
            {
                continue;
            }

            if (inside(candidate))
            {
                return new DesignPlacement(candidate, CanDrop: accepts(candidate));
            }
        }

        return null;
    }

    /// <summary>Whether the pointer is over <paramref name="container"/> where it is shown.</summary>
    private bool PointerIn(DesignComponent container, Point2D pointer) =>
        BoxOf(container) is { } box && box.Box.Contains(pointer) && box.Clip.Contains(pointer);

    /// <summary>Whether <paramref name="outer"/> holds <paramref name="box"/> whole, edges included.</summary>
    private static bool Holds(Rect2D outer, Rect2D box) =>
        box.Left >= outer.Left && box.Right <= outer.Right && box.Top >= outer.Top && box.Bottom <= outer.Bottom;

    /// <summary>Whether two boxes overlap by more than an edge.</summary>
    private static bool Touches(Rect2D a, Rect2D b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>
    /// The containers a drop is tried in (<c>PlacementUtil.getCandidateContainers</c>): deepest first
    /// and the front one first - the canvas's paint order backwards - with a selected container's own
    /// first. The figure, tried last, is not among them.
    /// </summary>
    private IEnumerable<DesignComponent> Candidates()
    {
        List<DesignComponent> containers = Boxes.Where(static b => b.Shown).Select(static b => b.Component)
            .Where(static c => c.Type is "matlab.ui.container.Panel" or "matlab.ui.container.ButtonGroup"
                or "matlab.ui.container.Tab" or GridType)
            .Reverse()
            .ToList();
        if (Selection is [var selected] && selected.Parent is not null && containers.Contains(selected))
        {
            return containers.Where(c => IsWithin(c, selected)).Concat(containers.Where(c => !IsWithin(c, selected)));
        }

        return containers;
    }

    /// <summary>
    /// The boxes a new component of <paramref name="entry"/> takes, on the surface, with the top-left
    /// corner of them all at <paramref name="topLeft"/>, in the order the entry makes them (a label
    /// before its component): what App Designer's avatar shows. A Grid Layout's is its avatar.
    /// </summary>
    public IReadOnlyList<Rect2D> DropBoxes(DesignPaletteEntry entry, Point2D topLeft)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Type == GridType)
        {
            return [new Rect2D(topLeft.X, topLeft.Y, GridAvatar.Width, GridAvatar.Height)];
        }

        if (entry.UserComponent is { } user)
        {
            // A custom component's avatar is its registration's default size (DS7b); nothing is made for it.
            (double width, double height) = user.DefaultSize;
            return [new Rect2D(topLeft.X, topLeft.Y, width, height)];
        }

        NumericValue[] positions = Palette.Make(entry, Design, Design.Figure, new Point2D(0, 0))
            .Select(static m => m.Component.Get("Position"))
            .OfType<NumericValue>()
            .Where(static p => p.Data.Count == 4)
            .ToArray();
        if (positions.Length == 0)
        {
            return [new Rect2D(topLeft.X, topLeft.Y, 0, 0)];
        }

        double left = positions.Min(static p => p.Data[0]);
        double top = positions.Max(static p => p.Data[1] + p.Data[3]);
        return positions.Select(p => new Rect2D(
            topLeft.X + p.Data[0] - left, topLeft.Y + top - (p.Data[1] + p.Data[3]), p.Data[2], p.Data[3])).ToArray();
    }

    private static Rect2D Union(IReadOnlyList<Rect2D> boxes)
    {
        double left = boxes.Min(static b => b.X);
        double top = boxes.Min(static b => b.Y);
        double right = boxes.Max(static b => b.Right);
        double bottom = boxes.Max(static b => b.Bottom);
        return new Rect2D(left, top, right - left, bottom - top);
    }

    // --- edits in grids --------------------------------------------------------------------------

    /// <summary>
    /// Adds what <paramref name="entry"/> makes into <paramref name="grid"/>'s cells, as one edit:
    /// each component the entry makes takes the span given for it, in order (a label's, then its
    /// component's), its place a row and a column rather than a position. With
    /// <paramref name="withoutLabel"/> a labelled entry's component goes alone into the first span,
    /// where the pointer is (W4).
    /// </summary>
    /// <returns>The components added.</returns>
    public IReadOnlyList<DesignComponent> AddToGrid(DesignPaletteEntry entry, DesignComponent grid, IReadOnlyList<GridSpan> spans, bool withoutLabel = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(spans);
        EndGesture(commit: false);
        if (entry.Type == GridType)
        {
            return [AddGridInGrid(grid, spans[0])];
        }

        IReadOnlyList<(DesignComponent Component, int? Index)> made = MakeFromEntry(entry, grid, withoutLabel: withoutLabel);
        if (withoutLabel && made.Count == 1 && spans.Count == 2)
        {
            spans = [spans[0]];
        }

        if (made.Count != spans.Count)
        {
            throw new ArgumentException($"The entry makes {made.Count} components, and {spans.Count} cells were given.", nameof(spans));
        }

        for (int i = 0; i < made.Count; i++)
        {
            DesignComponent component = made[i].Component;
            component.Unset("Position");
            component.Set("Layout.Row", spans[i].RowValue);
            component.Set("Layout.Column", spans[i].ColumnValue);
        }

        List<Edit> step = made.Select(m => (Edit)new AddEdit(m.Component, grid, null)).ToList();
        AddLabeledGroup(entry, made, step);
        Do([.. step]);
        return made.Select(static m => m.Component).ToArray();
    }

    /// <summary>
    /// Puts <paramref name="child"/> of a grid in <paramref name="span"/>, as one edit: the span
    /// resize's commit. Nothing changes when it is there already.
    /// </summary>
    public void SetGridSpan(DesignComponent child, GridSpan span)
    {
        ArgumentNullException.ThrowIfNull(child);
        EndGesture(commit: false);
        GridSpan? old = SpanOf(child);
        if (old == span || child.Parent is not { } grid || !IsGrid(grid))
        {
            return;
        }

        Do([.. IntoGrid(child, grid, span)]);
    }

    /// <summary>
    /// Puts several children of one grid in their spans as one edit (a labelled component's label and
    /// field spanned together, open item 150). Nothing changes when every one is there already.
    /// </summary>
    public void SetGridSpans(IReadOnlyList<(DesignComponent Child, GridSpan Span)> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        EndGesture(commit: false);
        var edits = new List<Edit>();
        foreach ((DesignComponent child, GridSpan span) in spans)
        {
            if (SpanOf(child) != span && child.Parent is { } grid && IsGrid(grid))
            {
                edits.AddRange(IntoGrid(child, grid, span));
            }
        }

        if (edits.Count > 0)
        {
            Do([.. edits]);
        }
    }

    /// <summary>
    /// The edits that put <paramref name="component"/> in <paramref name="grid"/>'s cells
    /// <paramref name="span"/>: moved in when it is not there (the grid places it, then its cells are
    /// set), its <c>Layout.Row</c> and <c>Layout.Column</c> set, and a position it held unset, which a
    /// grid's child does not have.
    /// </summary>
    private List<Edit> IntoGrid(DesignComponent component, DesignComponent grid, GridSpan span, bool selects = true)
    {
        var edits = new List<Edit>();
        DesignComponent from = component.Parent!;
        GridSpan? old = IsGrid(from) ? SpanOf(component) : null;
        if (component.Get("Position") is { } position)
        {
            // Unset first, so that undo - last to first - puts the runtime's position back once the
            // component is out of the grid again: an axes the grid pinned to its cell keeps it otherwise.
            DesignValue was = Runtime.ReadNumbers(component, "Position") is [var x, var y, var w, var h] ? NumericValue.Row(x, y, w, h) : position;
            edits.Add(new PropertyEdit(component, "Position", position, null, RuntimeBefore: was, RuntimeAfter: null) { Selects = selects });
        }

        if (!ReferenceEquals(from, grid))
        {
            edits.Add(new ParentEdit(component, from, IndexIn(from, component), grid) { Selects = selects });
        }

        edits.Add(new PropertyEdit(component, "Layout.Row", component.Get("Layout.Row"), span.RowValue, old?.RowValue, span.RowValue) { Selects = selects });
        edits.Add(new PropertyEdit(component, "Layout.Column", component.Get("Layout.Column"), span.ColumnValue, old?.ColumnValue, span.ColumnValue) { Selects = selects });
        return edits;
    }

    /// <summary>
    /// The edits that take <paramref name="component"/> out of its grid into <paramref name="into"/>,
    /// which places by position (<c>AbsoluteLayoutCreator</c>): its cells unset, moved, and placed with
    /// its box's top-left corner at <paramref name="box"/>'s, in whole pixels, at the size it had in
    /// the grid. A grid taken out fills its new container and has no position.
    /// </summary>
    private List<Edit> OutOfGrid(DesignComponent component, DesignComponent into, Rect2D box, bool selects = true)
    {
        DesignComponent grid = component.Parent!;
        GridSpan? old = SpanOf(component);
        var edits = new List<Edit>
        {
            new PropertyEdit(component, "Layout.Row", component.Get("Layout.Row"), null, old?.RowValue, RuntimeAfter: null) { Selects = selects },
            new PropertyEdit(component, "Layout.Column", component.Get("Layout.Column"), null, old?.ColumnValue, RuntimeAfter: null) { Selects = selects },
            new ParentEdit(component, grid, IndexIn(grid, component), into) { Selects = selects },
        };
        if (component.Type != GridType)
        {
            Rect2D position = Runtime.ToPosition(into, box);
            NumericValue placed = NumericValue.Row(
                System.Math.Round(position.X, MidpointRounding.AwayFromZero),
                System.Math.Round(position.Y, MidpointRounding.AwayFromZero),
                System.Math.Round(position.Width, MidpointRounding.AwayFromZero),
                System.Math.Round(position.Height, MidpointRounding.AwayFromZero));
            edits.Add(new PositionEdit(component, component.Get("Position"), placed, placed) { Selects = selects });
        }

        return edits;
    }

    // --- rows and columns ------------------------------------------------------------------------

    private static string TrackProperty(GridAxis axis) => axis == GridAxis.Row ? "RowHeight" : "ColumnWidth";

    /// <summary>
    /// <paramref name="grid"/>'s rows or columns as its component holds them: what was set, grown by
    /// <c>'1x'</c> tracks to hold every child, as App Designer reads them from MATLAB's grid.
    /// </summary>
    public IReadOnlyList<DesignValue> TracksOf(DesignComponent grid, GridAxis axis)
    {
        ArgumentNullException.ThrowIfNull(grid);
        string name = TrackProperty(axis);
        DesignValue? like = grid.Get(name) ?? grid.Class?.Property(name)?.DefaultFor(Theme);
        return (Runtime.Read(grid, name, like) ?? like) is CellValue cell ? cell.Items : [];
    }

    /// <summary>
    /// Inserts a <c>'1x'</c> row or column at <paramref name="index"/> (from 1) in
    /// <paramref name="grid"/>, as one edit (<c>AddRowCommand</c>, <c>AddColumnCommand</c>): the
    /// children at it and after it move along, and a child spanning across it grows.
    /// </summary>
    public void InsertTrack(DesignComponent grid, GridAxis axis, int index)
    {
        ArgumentNullException.ThrowIfNull(grid);
        EndGesture(commit: false);
        var tracks = TracksOf(grid, axis).ToList();
        index = System.Math.Clamp(index, 1, tracks.Count + 1);
        tracks.Insert(index - 1, new CharValue("1x"));
        var edits = new List<Edit> { TracksEdit(grid, axis, tracks) };
        foreach (DesignComponent child in grid.Children)
        {
            if (SpanOf(child) is { } span && DesignGridRules.WithTrackInserted(span, axis, index) is var moved && moved != span)
            {
                edits.AddRange(SpanEdits(child, span, moved));
            }
        }

        Do([.. edits]);
    }

    /// <summary>
    /// Deletes the row or column at <paramref name="index"/> (from 1) of <paramref name="grid"/>, as
    /// one edit (<c>DeleteRowCommand</c>, <c>DeleteColumnCommand</c>): the children in it alone are
    /// deleted, a child spanning it shrinks, and the children after it move back.
    /// </summary>
    public void DeleteTrack(DesignComponent grid, GridAxis axis, int index)
    {
        ArgumentNullException.ThrowIfNull(grid);
        EndGesture(commit: false);
        var tracks = TracksOf(grid, axis).ToList();
        if (index < 1 || index > tracks.Count)
        {
            return;
        }

        var victims = new List<DesignComponent>();
        var moves = new List<Edit>();
        foreach (DesignComponent child in grid.Children)
        {
            if (SpanOf(child) is not { } span)
            {
                continue;
            }

            if (DesignGridRules.WithTrackRemoved(span, axis, index) is not { } kept)
            {
                victims.Add(child);
            }
            else if (kept != span)
            {
                moves.AddRange(SpanEdits(child, span, kept));
            }
        }

        tracks.RemoveAt(index - 1);

        // A child its grid placed has its cells written down first, so that undo makes it there again.
        var edits = new List<Edit>();
        foreach (DesignComponent victim in victims)
        {
            if (SpanOf(victim) is { } span && GridSpan.FromValues(victim.Get("Layout.Row"), victim.Get("Layout.Column")) is null)
            {
                edits.Add(new PropertyEdit(victim, "Layout.Row", victim.Get("Layout.Row"), span.RowValue, null, null) { Selects = false });
                edits.Add(new PropertyEdit(victim, "Layout.Column", victim.Get("Layout.Column"), span.ColumnValue, null, null) { Selects = false });
            }
        }

        edits.AddRange(DeleteEdits(victims));
        edits.AddRange(moves);
        edits.Add(TracksEdit(grid, axis, tracks));
        Do([.. edits]);
    }

    /// <summary>
    /// Sets the row or column at <paramref name="index"/> (from 1) of <paramref name="grid"/> to
    /// <paramref name="value"/> - <c>'fit'</c>, <c>'Nx'</c> or pixels - as one edit, the grid's other
    /// tracks as they are (the grid configuration popup's <c>row-change</c> and <c>column-change</c>).
    /// </summary>
    /// <returns>Null when the grid took the value; else what it said.</returns>
    public string? SetTrack(DesignComponent grid, GridAxis axis, int index, DesignValue value)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(value);
        var tracks = TracksOf(grid, axis).ToList();
        if (index < 1 || index > tracks.Count)
        {
            return $"The grid has no {(axis == GridAxis.Row ? "row" : "column")} {index}.";
        }

        tracks[index - 1] = value;
        return SetProperty(grid, TrackProperty(axis), CellValue.Row([.. tracks]));
    }

    /// <summary>A grid's rows or columns set to <paramref name="tracks"/>; unset in the design when they are the default.</summary>
    private PropertyEdit TracksEdit(DesignComponent grid, GridAxis axis, IReadOnlyList<DesignValue> tracks)
    {
        string name = TrackProperty(axis);
        CellValue after = CellValue.Row([.. tracks]);
        DesignValue? held = grid.Get(name);
        DesignValue? runtimeBefore = held ?? grid.Class?.Property(name)?.DefaultFor(Theme);
        return new PropertyEdit(grid, name, held, IsDefaultValue(grid, name, after) ? null : after, runtimeBefore, after);
    }

    /// <summary>A grid child's cells moved from <paramref name="from"/> to <paramref name="to"/> by a row or column edit; the child stays unselected.</summary>
    private static IEnumerable<Edit> SpanEdits(DesignComponent child, GridSpan from, GridSpan to)
    {
        if (from.RowStart != to.RowStart || from.RowEnd != to.RowEnd)
        {
            yield return new PropertyEdit(child, "Layout.Row", child.Get("Layout.Row"), to.RowValue, from.RowValue, to.RowValue) { Selects = false };
        }

        if (from.ColumnStart != to.ColumnStart || from.ColumnEnd != to.ColumnEnd)
        {
            yield return new PropertyEdit(child, "Layout.Column", child.Get("Layout.Column"), to.ColumnValue, from.ColumnValue, to.ColumnValue) { Selects = false };
        }
    }

    // --- the inspector ---------------------------------------------------------------------------

    /// <summary>
    /// Whether the inspector edits <paramref name="name"/> on <paramref name="component"/>: a grid
    /// child's <c>Position</c> is shown and not edited (<c>ApplicationComponentModel.DisabledPropertiesInGrid</c>),
    /// since its grid places it.
    /// </summary>
    public bool IsEditable(DesignComponent component, string name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        // A responsive app's grid is never the user's to edit, nor are what its regions and its figure
        // keep from the template (ResponsiveAppGridLayoutModel, RegionPanelModel, UIFigureModel, DS4b).
        if (IsResponsiveGrid(component))
        {
            return false;
        }

        if (IsRegion(component))
        {
            return name is not ("Position" or "Visible" or "AutoResizeChildren");
        }

        if (ReferenceEquals(component, Design.Figure) && IsResponsive)
        {
            return name is not ("Position" or "AutoResizeChildren");
        }

        return !(name == "Position" && component.IsInGrid);
    }

    /// <summary>
    /// A grid's <c>RowHeight</c> or <c>ColumnWidth</c> typed in the inspector: a cell as MATLAB writes
    /// one, or App Designer's own form (<c>DesignTimeGridLayoutController.handlePropertyEditorEdited</c>):
    /// entries separated by commas, each <c>fit</c>, <c>Nx</c> or numbers - a part of several numbers
    /// (<c>100 200</c>) giving as many, as <c>str2num</c> does. When every part is numbers they make a
    /// cell of numbers, an empty part giving none; an empty part among <c>fit</c> or <c>Nx</c>, or no
    /// tracks at all, is refused, as MATLAB refuses <c>[]</c> there. Refused too when the grid's
    /// components reach past as many tracks as it would have (<c>GridUtil.isNotEnoughSpaceOnGrid</c>).
    /// </summary>
    /// <returns>The value; or null, with why not.</returns>
    public (DesignValue? Value, string? Refusal) ParseTracks(DesignComponent grid, GridAxis axis, string text)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim();
        string name = axis == GridAxis.Row ? "RowHeight" : "ColumnWidth";
        DesignValue value;
        int count;
        if (trimmed.StartsWith('{'))
        {
            value = DesignValueText.Parse(trimmed);
            if (value is not CellValue cell)
            {
                return (value, null);
            }

            count = cell.Items.Count;
        }
        else
        {
            var items = new List<DesignValue>();
            bool empty = false;
            bool allNumbers = true;
            foreach (string part in trimmed.Split(','))
            {
                string token = part.Trim();
                if (token.Length == 0)
                {
                    empty = true;
                }
                else if (Numbers(token) is { } numbers)
                {
                    items.AddRange(numbers.Select(static n => (DesignValue)NumericValue.Scalar(n)));
                }
                else
                {
                    allNumbers = false;
                    items.Add(token.Equals("fit", StringComparison.OrdinalIgnoreCase) || token.EndsWith('x') || token.EndsWith('X')
                        ? new CharValue(token)
                        : DesignValueText.Parse(token));
                }
            }

            if (items.Count == 0 || (empty && !allNumbers))
            {
                return (null, $"'{name}' must be a cell vector containing the keyword 'fit', numbers, or numbers paired with 'x' characters.");
            }

            value = CellValue.Row([.. items]);
            count = items.Count;
        }

        int reach = grid.Children.Select(SpanOf).OfType<GridSpan>()
            .Select(s => axis == GridAxis.Row ? s.RowEnd : s.ColumnEnd)
            .DefaultIfEmpty(0)
            .Max();
        return reach > count
            ? (null, $"The grid's components reach {(axis == GridAxis.Row ? "row" : "column")} {reach}, so it keeps at least {reach}.")
            : (value, null);

        // A part of one or more numbers, written plainly or in brackets; null when it is anything else.
        static double[]? Numbers(string token)
        {
            string inner = token.Trim('[', ']', ' ');
            string[] words = inner.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var numbers = new double[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                if (!double.TryParse(words[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return null;
                }
            }

            return words.Length == 0 ? null : numbers;
        }
    }

    // --- applying and removing a grid layout ------------------------------------------------------

    /// <summary>
    /// Whether "Apply Grid Layout" - and a Grid Layout dropped from the library - can lay out
    /// <paramref name="container"/>'s components in a grid (<c>LayoutConversionMenuItemProvider</c>):
    /// the figure of an app that is not responsive, a panel or a tab.
    /// </summary>
    public bool CanGridify(DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.Type switch
        {
            "matlab.ui.Figure" => !IsResponsive,
            "matlab.ui.container.Panel" or "matlab.ui.container.Tab" => true,
            _ => false,
        };
    }

    /// <summary>
    /// App Designer's "Apply Grid Layout" on <paramref name="container"/>, as one edit
    /// (<c>GridifyCommand</c>): a new grid fills it, its rows, columns, padding and spacing worked out
    /// by App Designer's gridifier (<see cref="DesignGridify"/>) from the components in view - each
    /// one whose centre is inside the container, or that spans it, across and down - which go into
    /// the grid's cells in their order. Components out of view stay where they are. With none in
    /// view, the grid has as many <c>'1x'</c> rows and columns as 50-by-200-pixel cells fill it.
    /// </summary>
    /// <returns>The new grid, which is selected.</returns>
    public DesignComponent Gridify(DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        EndGesture(commit: false);
        if (!CanGridify(container))
        {
            throw new InvalidOperationException($"A grid layout cannot be applied to '{container.CodeName}'.");
        }

        Rect2D area = ReferenceEquals(container, Design.Figure)
            ? new Rect2D(0, 0, Runtime.Figure.Size.Width, Runtime.Figure.Size.Height)
            : Runtime.InnerOnSurface(container);
        var kept = new List<DesignComponent>();
        var given = new List<GridifyComponent>();
        foreach (DesignComponent child in container.Children)
        {
            if (child.Type is "matlab.ui.container.Menu" or "matlab.ui.container.ContextMenu" or "matlab.ui.container.Toolbar"
                || BoxOf(child) is not { } box || !InView(box.Box, area))
            {
                continue;
            }

            (bool horizontal, bool vertical) = DesignGridify.Growth(child);
            kept.Add(child);
            given.Add(new GridifyComponent(
                child.CodeName, new Rect2D(box.Box.X - area.X, box.Box.Y - area.Y, box.Box.Width, box.Box.Height), horizontal, vertical));
        }

        GridifyResult result = DesignGridify.Gridify(new Size2D(area.Width, area.Height), given, GridPadding, GridSpacing, GridSpacing);
        DesignComponent grid = NewGrid(container, result);
        var edits = new List<Edit> { new AddEdit(grid, container, null) };
        foreach (DesignComponent child in kept)
        {
            GridifyCell cell = result.Cells[child.CodeName];
            var span = new GridSpan(cell.RowStart, cell.RowEnd, cell.ColumnStart, cell.ColumnEnd);
            edits.AddRange(IntoGrid(child, grid, span, selects: false));
        }

        Do([.. edits]);
        return grid;
    }

    /// <summary>
    /// A Grid Layout dropped from the library on <paramref name="grid"/>'s cells
    /// <paramref name="span"/> (<c>GridifyCommand._addGridIntoGrid</c>): a new empty grid in them,
    /// with as many <c>'1x'</c> rows and columns as 50-by-200-pixel cells fill them; one edit.
    /// </summary>
    /// <returns>The new grid, which is selected.</returns>
    public DesignComponent AddGridInGrid(DesignComponent grid, GridSpan span)
    {
        ArgumentNullException.ThrowIfNull(grid);
        EndGesture(commit: false);
        Rect2D cells = CellsOf(grid)?.SpanRect(span) ?? new Rect2D(0, 0, GridAvatar.Width, GridAvatar.Height);
        GridifyResult result = DesignGridify.Gridify(new Size2D(cells.Width, cells.Height), [], GridPadding, GridSpacing, GridSpacing);
        DesignComponent made = NewGrid(grid, result);
        made.Set("Layout.Row", span.RowValue);
        made.Set("Layout.Column", span.ColumnValue);
        Do([new AddEdit(made, grid, null)]);
        return made;
    }

    /// <summary>
    /// A new grid named as App Designer names one, holding what the gridifier worked out: its rows,
    /// columns, padding and spacings, each left unset where it is the default. A track worked out
    /// below nothing is nothing, which MATLAB takes.
    /// </summary>
    private DesignComponent NewGrid(DesignComponent parent, GridifyResult result)
    {
        DesignPaletteEntry entry = Palette.Find("Grid Layout")!;
        DesignComponent grid = Palette.Make(entry, Design, parent, new Point2D(0, 0))[0].Component;
        Put("RowHeight", CellValue.Row([.. result.RowHeight.Select(Track)]));
        Put("ColumnWidth", CellValue.Row([.. result.ColumnWidth.Select(Track)]));
        Put("Padding", NumericValue.Row([.. result.Padding]));
        Put("RowSpacing", NumericValue.Scalar(result.RowSpacing));
        Put("ColumnSpacing", NumericValue.Scalar(result.ColumnSpacing));
        return grid;

        static DesignValue Track(GridifyTrack track) =>
            !track.IsWeight && track.Value < 0 ? NumericValue.Scalar(0) : track.ToDesignValue();

        void Put(string name, DesignValue value)
        {
            if (!IsDefaultValue(grid, name, value))
            {
                grid.Set(name, value);
            }
        }
    }

    /// <summary>Whether a component's box is in view in a container's area, as <c>GridifyCommand._getComponentsInView</c> asks, across and down.</summary>
    private static bool InView(Rect2D box, Rect2D area)
    {
        static bool Along(double start, double end, double from, double to) =>
            (start <= from && end >= to) || ((start + end) / 2 >= from && (start + end) / 2 <= to);

        return Along(box.Left, box.Right, area.Left, area.Right) && Along(box.Top, box.Bottom, area.Top, area.Bottom);
    }

    /// <summary>
    /// App Designer's "Remove Grid Layout" on <paramref name="grid"/>, as one edit
    /// (<c>UngridifyCommand</c>): every component in it - and in the grids inside it, which go too -
    /// moves to the grid's container where it is drawn, at its size; into a grid's container, each
    /// takes the cells the grid had. The grids are deleted, the inner ones first.
    /// </summary>
    public void Ungridify(DesignComponent grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        EndGesture(commit: false);
        if (!IsGrid(grid) || grid.Parent is not { } into)
        {
            return;
        }

        bool intoGrid = IsGrid(into);
        GridSpan? gridSpan = intoGrid ? SpanOf(grid) : null;
        var edits = new List<Edit>();
        var dissolved = new List<DesignComponent>();
        Dissolve(grid);
        foreach (DesignComponent gone in dissolved)
        {
            edits.Add(new DeleteEdit(gone, gone.Parent!, IndexIn(gone.Parent!, gone)) { Selects = false });
        }

        // Selected after, as App Designer's delete of the grid leaves it: a grid it was in, else the figure.
        if (intoGrid)
        {
            edits.Add(new SelectEdit(into));
        }

        Do([.. edits]);

        void Dissolve(DesignComponent level)
        {
            foreach (DesignComponent inner in level.Children.Where(IsGrid).ToArray())
            {
                Dissolve(inner);
            }

            foreach (DesignComponent child in level.Children.Where(c => !IsGrid(c)).ToArray())
            {
                if (intoGrid && gridSpan is { } span)
                {
                    edits.AddRange(IntoGrid(child, into, span, selects: false));
                }
                else if (BoxOf(child) is { } box)
                {
                    edits.AddRange(OutOfGrid(child, into, box.Box, selects: false));
                }
            }

            dissolved.Add(level);
        }
    }
}
