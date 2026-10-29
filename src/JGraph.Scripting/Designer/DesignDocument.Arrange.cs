using System.Globalization;
using System.Text.RegularExpressions;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>Where Reorder sends the selection among its siblings (CANVAS > ARRANGE > Reorder).</summary>
public enum DesignStack
{
    /// <summary>In front of every sibling of its kind (Bring to Front).</summary>
    Front,

    /// <summary>One place forward (Bring Forward).</summary>
    Forward,

    /// <summary>One place back (Send Backward).</summary>
    Backward,

    /// <summary>Behind every sibling of its kind (Send to Back).</summary>
    Back,
}

/// <summary>What a font key does to the selection (Ctrl+B, Ctrl+I, Ctrl+], Ctrl+[).</summary>
public enum DesignFontChange
{
    /// <summary>Bold on or off.</summary>
    Bold,

    /// <summary>Italic on or off.</summary>
    Italic,

    /// <summary>The next larger of App Designer's font sizes.</summary>
    Larger,

    /// <summary>The next smaller of App Designer's font sizes.</summary>
    Smaller,
}

/// <content>
/// Arranging the selection (App Designer window plan, stage W4): the anchor, Align, Same Size,
/// Space, Reorder, Tab Order, Add Label, the font keys and the figure resized by its edges, each one
/// edit, with R2025b's rules for what each applies to (the models' <c>DesignGestures</c> and
/// <c>ComponentTabBehavior</c>, and each action's own checks).
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>App Designer's font sizes, which the font keys step through (<c>SUPPORTED_FONT_SIZES</c>).</summary>
    public static readonly IReadOnlyList<double> FontSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>The least size the figure's edges resize it to (<c>MIN_FIGURE_WIDTH</c>, <c>MIN_FIGURE_HEIGHT</c>).</summary>
    public const double MinimumFigureSize = 10;

    /// <summary>The words of the refusal Add Label gives a grid's child with no cell for its label.</summary>
    public const string AddLabelFailedTitle = "Unable to Add Label";

    /// <inheritdoc cref="AddLabelFailedTitle"/>
    public const string AddLabelFailedMessage = "Unable to add label because there is no space in the grid layout.";

    private const string FigureType = "matlab.ui.Figure";
    private const string TabType = "matlab.ui.container.Tab";
    private const string MenuType = "matlab.ui.container.Menu";
    private const string ContextMenuType = "matlab.ui.container.ContextMenu";
    private const string ToolbarType = "matlab.ui.container.Toolbar";
    private const string AxesType = "matlab.ui.control.UIAxes";

    // The models whose DesignGestures refuse CanAlign, CanSpace, CanSameSize and CanChangeFont.
    private static readonly HashSet<string> NoArrange = new(StringComparer.Ordinal)
    {
        TabType, MenuType, ContextMenuType, ToolbarType, "matlab.ui.container.TreeNode",
        "matlab.ui.container.toolbar.PushTool", "matlab.ui.container.toolbar.ToggleTool", GridType,
    };

    // And those that refuse CanChangeFont only.
    private static readonly HashSet<string> NoFont = new(StringComparer.Ordinal)
    {
        "matlab.ui.control.Image", "matlab.ui.control.Lamp", "matlab.ui.container.TabGroup",
    };

    // The models whose DesignGestures refuse CanStack.
    private static readonly HashSet<string> NoStack = new(StringComparer.Ordinal)
    {
        FigureType, TabType, MenuType, ContextMenuType, ToolbarType,
        "matlab.ui.container.toolbar.PushTool", "matlab.ui.container.toolbar.ToggleTool",
    };

    // ComponentTabBehavior.doesParticipateInAutoTabOrder false: these keep their places when the tab order is applied.
    private static readonly HashSet<string> NoAutoTabOrder = new(StringComparer.Ordinal)
    {
        FigureType, MenuType, ContextMenuType, ToolbarType, AxesType, "matlab.ui.container.TreeNode",
        "matlab.ui.container.toolbar.PushTool", "matlab.ui.container.toolbar.ToggleTool",
    };

    // ComponentTabBehavior.isTabFocusableAtRuntime false: these take no focus as the app runs.
    private static readonly HashSet<string> NotFocusable = new(StringComparer.Ordinal)
    {
        FigureType, "matlab.ui.container.Panel", "matlab.ui.container.ButtonGroup", GridType, TabType, "matlab.ui.container.TabGroup",
        "matlab.ui.control.Label", "matlab.ui.control.Lamp", "matlab.ui.control.Gauge", "matlab.ui.control.LinearGauge",
        "matlab.ui.control.NinetyDegreeGauge", "matlab.ui.control.SemicircularGauge", "matlab.ui.control.Knob",
        "matlab.ui.control.DiscreteKnob", "matlab.ui.control.Switch", "matlab.ui.control.RockerSwitch", "matlab.ui.control.ToggleSwitch",
        ToolbarType, ContextMenuType, "matlab.ui.container.TreeNode", AxesType,
    };

    private DesignPick? _anchor;

    /// <summary>
    /// The canvas's zoom (W6), which App Designer's gridifier reads its boxes at: Apply Grid Layout's
    /// and the auto tab order's results depend on it, as App Designer measures the page (DS4).
    /// </summary>
    public double CanvasZoom { get; set; } = 1;

    private enum Arrange
    {
        Align,
        Space,
        SameSize,
        Font,
        Stack,
    }

    // --- the anchor --------------------------------------------------------------------------------

    /// <summary>
    /// The pick the selection is aligned and sized to (<c>SelectionModelManager</c>'s anchor): the last
    /// selected of two or more, or the one a right click on the selection pointed at; null for one or none.
    /// </summary>
    public DesignPick? Anchor =>
        _picks.Count < 2 ? null
        : _anchor is { } anchor && DesignGroupRules.IsSelected(_picks, anchor) ? _picks.First(p => Same(p, anchor))
        : _picks[^1];

    /// <summary>Makes <paramref name="pick"/>, one of two or more selected, the anchor - as a right click on it does.</summary>
    public void SetAnchor(DesignPick pick)
    {
        if (_picks.Count > 1 && DesignGroupRules.IsSelected(_picks, pick) && !(Anchor is { } now && Same(now, pick)))
        {
            _anchor = pick;
            OnChanged();
        }
    }

    private static bool Same(DesignPick a, DesignPick b) =>
        (a.Component is not null && ReferenceEquals(a.Component, b.Component)) || (a.Group is not null && ReferenceEquals(a.Group, b.Group));

    // --- what applies ------------------------------------------------------------------------------

    private bool Supports(DesignPick pick, Arrange what)
    {
        if (pick.Group is { } group)
        {
            // GroupModel: aligned, spaced and given a font as one; never sized or restacked.
            return what is Arrange.Align or Arrange.Space or Arrange.Font && group.AllComponents().All(c => what == Arrange.Font || IsMovable(c));
        }

        DesignComponent c = pick.Component!;
        return what switch
        {
            Arrange.Align or Arrange.Space or Arrange.SameSize => !NoArrange.Contains(c.Type) && IsMovable(c),
            Arrange.Font => !NoArrange.Contains(c.Type) && !NoFont.Contains(c.Type),
            _ => !NoStack.Contains(c.Type) && !IsRegion(c) && c.Parent is not null,
        };
    }

    /// <summary>Whether two or more are selected, all of which <paramref name="what"/> applies to, and the first not in a grid (each action's <c>_updateAction</c>).</summary>
    private bool CanArrange(Arrange what) =>
        _picks.Count >= 2 && !_picks.Any(static p => p.Component is { Parent: null })
        && _picks.All(p => Supports(p, what))
        && !(_picks[0].Component ?? _picks[0].Group!.AllComponents().First()).IsInGrid;

    /// <summary>Whether CANVAS > ALIGN applies to the selection.</summary>
    public bool CanAlign => CanArrange(Arrange.Align);

    /// <summary>Whether Same Size applies to the selection.</summary>
    public bool CanSameSize => CanArrange(Arrange.SameSize);

    /// <summary>Whether CANVAS > SPACE applies to the selection.</summary>
    public bool CanSpace => CanArrange(Arrange.Space);

    // --- align, same size, space -------------------------------------------------------------------

    /// <summary>Aligns the selection on its anchor, as one edit (<c>AlignCommand</c>); false when it does not apply or nothing moved.</summary>
    public bool Align(DesignAlign how) =>
        CanAlign && ApplyArrange(items => DesignArrange.Align(how, items.Select(static i => i.Item).ToArray(), AnchorItem(items)));

    /// <summary>Gives the selection its anchor's size, as one edit (<c>SameSizeCommand</c>); false when it does not apply or nothing changed.</summary>
    public bool SameSize(DesignSameSize how) =>
        CanSameSize && ApplyArrange(items => DesignArrange.SameSize(how, items.Select(static i => i.Item).ToArray(), AnchorItem(items)));

    /// <summary>
    /// Spaces the selection across (<paramref name="horizontal"/>) or down, as one edit
    /// (<c>SpacingCommand</c>): evenly for <see cref="DesignArrange.Evenly"/>, else the pixels
    /// <paramref name="spacing"/> starts with, as JavaScript's <c>parseFloat</c> reads them.
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    public bool Space(bool horizontal, string spacing)
    {
        ArgumentNullException.ThrowIfNull(spacing);
        double? gap = null;
        if (spacing != DesignArrange.Evenly)
        {
            if (ParseFloat(spacing) is not { } pixels)
            {
                return false;
            }

            gap = pixels;
        }

        return CanSpace && ApplyArrange(items => DesignArrange.Space(horizontal, gap, items.Select(static i => i.Item).ToArray()));
    }

    /// <summary>JavaScript's <c>parseFloat</c>: the number a text starts with, past leading space; null for none.</summary>
    internal static double? ParseFloat(string text)
    {
        Match m = Regex.Match(text, @"^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)");
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    private DesignArrangeItem? AnchorItem(List<(DesignPick Pick, DesignArrangeItem Item, Rect2D Start)> items) =>
        Anchor is { } anchor ? items.FirstOrDefault(i => Same(i.Pick, anchor)).Item : null;

    /// <summary>
    /// The selection as App Designer's arrange commands see it: each component by its <c>Position</c>,
    /// each group by the box round its components' outer positions, edges rounded
    /// (<c>calculateGroupSizeProperties</c>), in its container's pixels.
    /// </summary>
    private List<(DesignPick Pick, DesignArrangeItem Item, Rect2D Start)> ArrangeItems()
    {
        var items = new List<(DesignPick, DesignArrangeItem, Rect2D)>();
        foreach (DesignPick pick in _picks)
        {
            if (pick.Component is { } c)
            {
                if (Runtime.PositionOf(c) is not { } p)
                {
                    continue;
                }

                DesignShape shape = ShapeOf(c);
                items.Add((pick, new DesignArrangeItem(p.X, p.Y, p.Width, p.Height)
                {
                    FixedWidth = shape.FixedWidth,
                    FixedHeight = shape.FixedHeight,
                    KeepsRatio = shape.Ratio is not null,
                    Minimum = (shape.Minimum.Width, shape.Minimum.Height),
                    Z = IndexIn(c.Parent!, c),
                }, p));
                continue;
            }

            double left = double.PositiveInfinity, bottom = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity;
            foreach (DesignComponent member in pick.Group!.AllComponents())
            {
                if (OuterPositionOf(member) is not { } o)
                {
                    continue;
                }

                double x = DesignArrange.JsRound(o.X);
                double y = DesignArrange.JsRound(o.Y);
                left = System.Math.Min(left, x);
                bottom = System.Math.Min(bottom, y);
                right = System.Math.Max(right, x + DesignArrange.JsRound(o.Width));
                top = System.Math.Max(top, y + DesignArrange.JsRound(o.Height));
            }

            if (!double.IsInfinity(left))
            {
                var box = new Rect2D(left, bottom, right - left, top - bottom);
                items.Add((pick, new DesignArrangeItem(box.X, box.Y, box.Width, box.Height) { Z = -1 }, box));
            }
        }

        return items;
    }

    /// <summary>A component's <c>OuterPosition</c> as the runtime holds it, else its <c>Position</c>.</summary>
    private Rect2D? OuterPositionOf(DesignComponent component) =>
        Runtime.ReadNumbers(component, "OuterPosition") is [var x, var y, var w, var h] ? new Rect2D(x, y, w, h) : Runtime.PositionOf(component);

    /// <summary>Runs <paramref name="arrange"/> over the selection and makes what it changed one edit, the selection and its anchor kept.</summary>
    private bool ApplyArrange(Func<List<(DesignPick Pick, DesignArrangeItem Item, Rect2D Start)>, bool> arrange)
    {
        EndGesture(commit: false);
        List<(DesignPick Pick, DesignArrangeItem Item, Rect2D Start)> items = ArrangeItems();
        if (items.Count < 2 || !arrange(items))
        {
            return false;
        }

        var edits = new List<Edit>();
        foreach ((DesignPick pick, DesignArrangeItem item, Rect2D start) in items)
        {
            var now = new Rect2D(item.X, item.Y, item.Width, item.Height);
            if (now == start)
            {
                continue;
            }

            if (pick.Component is { } c)
            {
                edits.Add(new PositionEdit(c, c.Get("Position"), Row(now), Row(start)) { Selects = false });
                continue;
            }

            // A group moves its every component (GroupWidgetData.move).
            double dx = item.X - start.X;
            double dy = item.Y - start.Y;
            foreach (DesignComponent member in pick.Group!.AllComponents())
            {
                if (Runtime.PositionOf(member) is { } p)
                {
                    edits.Add(new PositionEdit(member, member.Get("Position"), Row(new Rect2D(p.X + dx, p.Y + dy, p.Width, p.Height)), Row(p)) { Selects = false });
                }
            }
        }

        if (edits.Count == 0)
        {
            return false;
        }

        edits.Add(new PicksEdit(edits[0].Component, [.. _picks], Anchor));
        Do([.. edits]);
        return true;
    }

    // --- reorder -----------------------------------------------------------------------------------

    /// <summary>
    /// The selected components Reorder moves, in their siblings' order, and the siblings of their kind
    /// they move among (<c>ReorderAction._getReorderableComponents</c>): menus among menus, axes among
    /// axes, toolbars among toolbars, anything else among the rest but context menus.
    /// </summary>
    private (List<DesignComponent> Moving, List<DesignComponent> Among)? StackSet()
    {
        if (_picks.Count == 0 || _picks.Any(static p => p.Component is { Parent: null }))
        {
            return null;
        }

        List<DesignComponent> chosen = _picks.Where(p => p.Component is not null && Supports(p, Arrange.Stack)).Select(static p => p.Component!).ToList();
        if (chosen.Count == 0)
        {
            return null;
        }

        DesignComponent parent = chosen[0].Parent!;
        Func<DesignComponent, bool> kind = chosen[0].Type switch
        {
            MenuType => static c => c.Type == MenuType,
            AxesType => static c => c.Type == AxesType,
            ToolbarType => static c => c.Type == ToolbarType,
            _ => static c => c.Type is not (MenuType or ContextMenuType or AxesType or ToolbarType),
        };
        List<DesignComponent> among = parent.Children.Where(kind).ToList();
        List<DesignComponent> moving = among.Where(c => chosen.Contains(c, ReferenceEqualityComparer.Instance)).ToList();
        return moving.Count == 0 || among.Count < 2 ? null : (moving, among);
    }

    /// <summary>Whether Reorder can move the selection <paramref name="how"/> (<c>_checkAbilityToReorder</c>).</summary>
    public bool CanStack(DesignStack how)
    {
        if (StackSet() is not { } set)
        {
            return false;
        }

        int first = set.Among.IndexOf(set.Moving[0]);
        int last = set.Among.IndexOf(set.Moving[^1]);
        return how is DesignStack.Back or DesignStack.Backward ? first > 0 : last < set.Among.Count - 1;
    }

    /// <summary>
    /// Moves the selection <paramref name="how"/> among its siblings of its kind, as one edit
    /// (<c>ReorderAction._performReorder</c>, <c>ReorderCommand</c>): a group of them keeps the gaps
    /// between them going to the front or the back. The order is the order the code makes them in,
    /// the later in front; the selection stays.
    /// </summary>
    /// <returns>Whether the order changed.</returns>
    public bool Stack(DesignStack how)
    {
        if (!CanStack(how) || StackSet() is not { } set)
        {
            return false;
        }

        EndGesture(commit: false);
        List<DesignComponent> among = set.Among;
        int t = among.IndexOf(set.Moving[0]);
        int i = among.IndexOf(set.Moving[^1]);
        int s = among.Count - 1;
        var moves = set.Moving.Select(c =>
        {
            int n = among.IndexOf(c);
            int f = how switch
            {
                DesignStack.Back => n - t,
                DesignStack.Front => n + s - i,
                DesignStack.Backward => n - 1,
                _ => n + 1,
            };
            return (Component: c, Old: n, New: f);
        }).ToList();
        List<DesignComponent> after = Reordered(among, moves);
        return ReorderEdits(set.Moving[0].Parent!, among, after) is { } edit && DoOrder([edit]);
    }

    /// <summary>
    /// <paramref name="order"/> after <c>ReorderCommand._moveComponents</c>: the moves taken from the
    /// greatest new index, or from the least when all of them go back, each one out of the list and in
    /// again at its new index.
    /// </summary>
    private static List<DesignComponent> Reordered(List<DesignComponent> order, List<(DesignComponent Component, int Old, int New)> moves)
    {
        var list = new List<DesignComponent>(order);
        var sorted = moves.OrderByDescending(static m => m.New).ToList();
        if (sorted.All(static m => m.New < m.Old))
        {
            sorted.Reverse();
        }

        foreach ((DesignComponent component, _, int to) in sorted)
        {
            list.Remove(component);
            list.Insert(System.Math.Clamp(to, 0, list.Count), component);
        }

        return list;
    }

    /// <summary>The edit that puts <paramref name="among"/>, children of <paramref name="parent"/>, in the order <paramref name="after"/>, in the places they held; null when it is the same.</summary>
    private static Edit? ReorderEdits(DesignComponent parent, List<DesignComponent> among, List<DesignComponent> after)
    {
        if (among.SequenceEqual(after, ReferenceEqualityComparer.Instance))
        {
            return null;
        }

        DesignComponent[] before = [.. parent.Children];
        var slots = new Queue<DesignComponent>(after);
        var set = new HashSet<DesignComponent>(among, ReferenceEqualityComparer.Instance);
        DesignComponent[] whole = before.Select(c => set.Contains(c) ? slots.Dequeue() : c).ToArray();
        return new OrderEdit(parent, before, whole);
    }

    private bool DoOrder(List<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return false;
        }

        Do([.. edits]);
        return true;
    }

    // --- tab order ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether Apply Auto Tab Order and Visualize Tab Order apply to <paramref name="container"/> in
    /// the canvas's menu (<c>canApplyAutoTabOrderToComponent</c>): the figure, a panel, a button group,
    /// a grid, a tab or a responsive app's region.
    /// </summary>
    public bool CanApplyAutoTabOrder(DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.Type is FigureType or "matlab.ui.container.Panel" or "matlab.ui.container.ButtonGroup" or TabType
            || IsGrid(container) || IsRegion(container);
    }

    private bool ParticipatesInAutoTabOrder(DesignComponent c) =>
        !NoAutoTabOrder.Contains(c.Type) && !IsRegion(c) && !IsResponsiveGrid(c);

    /// <summary>
    /// App Designer's Apply Auto Tab Order, as one edit (<c>AutoTabOrderCommand</c>): in each container,
    /// the components that take part laid out by its gridifier, with no padding - the grid's cells
    /// read row by row, left to right - and put in that order after the children that do not take
    /// part (menus, toolbars, axes), before the context menus. With no <paramref name="container"/>,
    /// the whole app, every container in it (CANVAS > Tab Order), and the selection stays; else that
    /// container's children only (the canvas's menu), which is then selected. A container whose
    /// children App Designer's gridifier cannot lay out (one of no size, in a tab not shown) is left
    /// as it is.
    /// </summary>
    /// <returns>Whether the order changed.</returns>
    public bool ApplyAutoTabOrder(DesignComponent? container = null)
    {
        EndGesture(commit: false);
        var edits = new List<Edit>();
        AutoTabOrder(container ?? Design.Figure, recursive: container is null, edits);
        if (container is not null)
        {
            edits.Add(new SelectEdit(container));
            Do([.. edits]);
            return edits.Count > 1;
        }

        return DoOrder(edits);
    }

    private void AutoTabOrder(DesignComponent container, bool recursive, List<Edit> edits)
    {
        List<DesignComponent> taking = container.Children.Where(ParticipatesInAutoTabOrder).ToList();
        if (taking.Count >= 2 && GridOrder(container, taking) is { } sorted)
        {
            int offset = container.Children.Count - taking.Count - container.Children.Count(static c => c.Type == ContextMenuType);
            var children = container.Children.ToList();
            var moves = sorted.Select((c, k) => (Component: c, Old: children.IndexOf(c), New: k + offset)).ToList();
            List<DesignComponent> after = Reordered(children, moves);
            if (ReorderEdits(container, children, after) is { } edit)
            {
                edits.Add(edit);
            }
        }

        if (recursive)
        {
            foreach (DesignComponent child in container.Children)
            {
                AutoTabOrder(child, recursive, edits);
            }
        }
    }

    /// <summary>The components laid out by the gridifier and read row by row (<c>_convertGridifyResultToAutoTabOrderData</c>); null when it cannot lay them out.</summary>
    private List<DesignComponent>? GridOrder(DesignComponent container, List<DesignComponent> taking)
    {
        Rect2D area = ReferenceEquals(container, Design.Figure)
            ? new Rect2D(0, 0, Runtime.Figure.Size.Width, Runtime.Figure.Size.Height)
            : BoxOf(container) is { Shown: true } shown ? shown.Box : default;
        var given = new List<GridifyComponent>();
        foreach (DesignComponent c in taking)
        {
            Rect2D box = BoxOf(c) is { Shown: true } b ? (IsGridChild(c) ? CellBoxOf(c) ?? b.Box : b.Box) : default;
            (bool horizontal, bool vertical) = DesignGridify.Growth(c);
            given.Add(new GridifyComponent(c.CodeName, new Rect2D(box.X - area.X, box.Y - area.Y, box.Width, box.Height), horizontal, vertical));
        }

        GridifyResult result;
        try
        {
            result = DesignGridify.Gridify(new Size2D(area.Width, area.Height), given, [0, 0, 0, 0], GridSpacing, GridSpacing, CanvasZoom);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }

        // A stable sort by row, then column, of the components in their children's order.
        return taking.Select((c, k) => (c, k, cell: result.Cells[c.CodeName]))
            .OrderBy(static p => p.cell.RowStart).ThenBy(static p => p.cell.ColumnStart).ThenBy(static p => p.k)
            .Select(static p => p.c).ToList();
    }

    /// <summary>
    /// The components focus goes to with Tab as the app runs, in order (<c>VisualizeTabOrderCommand</c>):
    /// <paramref name="container"/>'s children that take focus, or with none, the app's - every
    /// container's in turn, a group's tabs but the one shown, and the menus but the top ones, left out.
    /// </summary>
    public IReadOnlyList<DesignComponent> TabOrder(DesignComponent? container = null)
    {
        var order = new List<DesignComponent>();
        Visit(container ?? Design.Figure, container is null);
        return order;

        void Visit(DesignComponent at, bool recursive)
        {
            foreach (DesignComponent child in at.Children)
            {
                if ((child.Type == TabType && BoxOf(child) is not { Shown: true }) || (child.Type == MenuType && child.Parent?.Type != FigureType))
                {
                    continue;
                }

                if (!NotFocusable.Contains(child.Type) && !IsRegion(child) && !IsResponsiveGrid(child))
                {
                    order.Add(child);
                }

                if (recursive && child.Class?.IsUserComponent != true)
                {
                    Visit(child, recursive);
                }
            }
        }
    }

    // --- add label -----------------------------------------------------------------------------------

    /// <summary>
    /// Whether Add Label (Ctrl+L) applies (<c>AddLabelAction</c>): the first selected is a component -
    /// no group - whose library entry comes with a label, and it is no labelled component's already.
    /// </summary>
    public bool CanAddLabel =>
        _picks is [{ Component: { Parent: not null } c }, ..] && Palette.LabelPlaceOf(c.Type) != DesignLabelPlace.None && Design.LabeledGroupOf(c) is null;

    /// <summary>
    /// Adds a label to the first selected component, as one edit (<c>AddLabelToComponentCommand</c>):
    /// named and placed as the label of a new labelled component of its kind beside a component where
    /// it stands - left of it, or under it, the component left where it is - put just before the
    /// sibling made before it, and grouped with it as a labelled component (the group it was in
    /// holding the new one in its place). In a grid, the label goes in the cell left of it or under
    /// it, whatever is there already.
    /// </summary>
    /// <returns>Null when the label was added; else App Designer's refusal, a grid child in its grid's first column or last row.</returns>
    public string? AddLabel()
    {
        if (!CanAddLabel)
        {
            return null;
        }

        EndGesture(commit: false);
        DesignComponent component = _picks[0].Component!;
        DesignComponent parent = component.Parent!;
        DesignLabelPlace place = Palette.LabelPlaceOf(component.Type);
        DesignComponent label = Palette.MakeLabelFor(component.Type, Design);
        Size2D size = DesignPalette.LabelSize(((CharValue)label.Get("Text")!).Text);
        if (IsGridChild(component))
        {
            if (SpanOf(component) is not { } span || CellsOf(parent) is not { } cells)
            {
                return null;
            }

            bool west = place != DesignLabelPlace.SouthCenter;
            if (west ? span.ColumnStart == 1 : span.RowEnd == cells.Rows)
            {
                return AddLabelFailedMessage;
            }

            label.Set("Layout.Row", west ? span.RowValue : NumericValue.Scalar(span.RowEnd + 1));
            label.Set("Layout.Column", west ? NumericValue.Scalar(span.ColumnStart - 1) : span.ColumnValue);
        }
        else if (OuterPositionOf(component) is { } outer && Runtime.PositionOf(component) is { } inner)
        {
            (double x, double y) = LabelCorner(place, component.Type, outer, inner, size);
            label.Set("Position", NumericValue.Row(x, y, size.Width, size.Height));
        }
        else
        {
            return null;
        }

        int at = IndexIn(parent, component);
        var edits = new List<Edit>();
        DesignGroup? around = Design.GroupOf(component);
        int aroundIndex = around is null ? -1 : around.Members.ToList().FindIndex(m => ReferenceEquals(m.Component, component));
        if (around is not null)
        {
            edits.Add(new LeaveGroupEdit(component, around, aroundIndex) { Selects = false });
        }

        edits.Add(new AddEdit(label, parent, at > 0 ? at - 1 : 0) { Selects = false });
        edits.Add(new GroupEdit(component, new DesignGroup(), [DesignGroupMember.Of(label), DesignGroupMember.Of(component)], around, around is null ? null : aroundIndex));
        Do([.. edits]);
        return null;
    }

    /// <summary>
    /// Where the new label's corner goes (<c>CalculateLabeledCompDefaultsFromUnlabeled</c> and the
    /// entry's calculator): the box a new labelled component would take is put where the component's
    /// outer box is, less the label's side as a new one has it, and the label placed in it.
    /// </summary>
    private (double X, double Y) LabelCorner(DesignLabelPlace place, string type, Rect2D outer, Rect2D inner, Size2D label)
    {
        double lw = label.Width;
        double lh = label.Height;
        if (place == DesignLabelPlace.SouthCenter)
        {
            // The label's offset as a new one has it, which the box is moved by only when the
            // component is narrower than its label and the new label stood left of it.
            double fresh = (lw / 2) - (Palette.DefaultOuterWidth(type) / 2);
            double offset = fresh > 0 ? -DesignArrange.JsRound(fresh) : DesignArrange.JsRound(-fresh);
            double cornerX = outer.X + (outer.Width < lw && offset < 0 ? offset : 0);
            double over = (lw / 2) - (outer.Width / 2);
            return (cornerX + (over > 0 ? 0 : DesignArrange.JsRound(-over)), outer.Y - lh - DesignPalette.LabelOffset);
        }

        double x = outer.X - lw - DesignPalette.LabelOffset;
        double above = inner.Y - outer.Y;
        if (place == DesignLabelPlace.WestTop)
        {
            double top = inner.Height + above;
            return (x, outer.Y + (lh > top ? 0 : top - lh - 2));
        }

        double centre = (inner.Height / 2) + above;
        double half = lh / 2;
        return (x, outer.Y + (half > centre ? 0 : DesignArrange.JsRound(centre - half)));
    }

    // --- the font keys -------------------------------------------------------------------------------

    /// <summary>
    /// The component the font keys read the font from (<c>FontAction._updateAction</c>): the anchor -
    /// a group's last component that has a font - else the first selected that has one; null when the
    /// figure or nothing is selected.
    /// </summary>
    private DesignComponent? FontTarget()
    {
        if (_picks.Count == 0 || _picks.Any(static p => p.Component is { Parent: null }))
        {
            return null;
        }

        if (Anchor is { } anchor && Leaves(anchor).LastOrDefault(HasFont) is { } fromAnchor)
        {
            return fromAnchor;
        }

        return _picks.SelectMany(Leaves).FirstOrDefault(HasFont);
    }

    private static IEnumerable<DesignComponent> Leaves(DesignPick pick) => pick.Group?.AllComponents() ?? [pick.Component!];

    private static bool HasFont(DesignComponent c) => !NoArrange.Contains(c.Type) && !NoFont.Contains(c.Type);

    /// <summary>Whether the font key <paramref name="change"/> applies to the selection.</summary>
    public bool CanChangeFont(DesignFontChange change)
    {
        if (FontTarget() is not { } target)
        {
            return false;
        }

        return change switch
        {
            DesignFontChange.Larger => FontSizeOf(target) is { } size && FontSizes.Any(s => s > size),
            DesignFontChange.Smaller => FontSizeOf(target) is { } size && FontSizes.Any(s => s < size),
            _ => true,
        };
    }

    private double? FontSizeOf(DesignComponent c) =>
        c.Class?.Property("FontSize") is not null && ValueOf(c, "FontSize") is NumericValue { Data: [var size] } ? size : null;

    /// <summary>
    /// The font key <paramref name="change"/> on the selection, as one edit (<c>FontCommand</c>): bold
    /// or italic turned the other way from the font of the component <see cref="FontTarget"/> gives,
    /// or its size stepped to the next of <see cref="FontSizes"/>; set on every selected component -
    /// a group's every one - and every one inside them, that has the font. The selection stays.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool ChangeFont(DesignFontChange change)
    {
        if (!CanChangeFont(change) || FontTarget() is not { } target)
        {
            return false;
        }

        EndGesture(commit: false);
        (string name, DesignValue value) = change switch
        {
            DesignFontChange.Bold => ("FontWeight", (DesignValue)new CharValue(WordOf(ValueOf(target, "FontWeight")) == "normal" ? "bold" : "normal")),
            DesignFontChange.Italic => ("FontAngle", new CharValue(WordOf(ValueOf(target, "FontAngle")) == "normal" ? "italic" : "normal")),
            DesignFontChange.Larger => ("FontSize", NumericValue.Scalar(FontSizes.First(s => s > FontSizeOf(target)!.Value))),
            _ => ("FontSize", NumericValue.Scalar(FontSizes.Last(s => s < FontSizeOf(target)!.Value))),
        };

        var edits = new List<Edit>();
        foreach (DesignComponent c in _picks.SelectMany(Leaves).SelectMany(Walk).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>())
        {
            if (!HasFont(c) || c.Class?.Property(name) is null)
            {
                continue;
            }

            DesignValue? now = ValueOf(c, name);
            if (SameValue(now, value))
            {
                continue;
            }

            edits.Add(new PropertyEdit(c, name, c.Get(name), value, RuntimeBefore: now, RuntimeAfter: value) { Selects = false });
        }

        if (edits.Count == 0)
        {
            return false;
        }

        edits.Add(new PicksEdit(edits[0].Component, [.. _picks], Anchor));
        Do([.. edits]);
        return true;
    }

    private static string? WordOf(DesignValue? value) => value switch
    {
        CharValue c => c.Text,
        StringValue s => s.Text,
        _ => null,
    };

    // --- the figure's edges --------------------------------------------------------------------------

    /// <summary>
    /// Whether the figure's edges resize it on the canvas. A responsive app's figure reflows its
    /// regions as it is resized (<c>LayoutTemplatedFigureResize</c>), which waits (open item 224).
    /// </summary>
    public bool CanResizeFigure => !HasRegions;

    /// <summary>Shows the figure at <paramref name="size"/> on the runtime only, while its edge is dragged; null puts back the size the design holds.</summary>
    public void PreviewFigureSize(Size2D? size)
    {
        if (Runtime.PositionOf(Design.Figure) is not { } p)
        {
            return;
        }

        Rect2D held = Design.Figure.Get("Position") is NumericValue { Data: [var x, var y, var w, var h] } ? new Rect2D(x, y, w, h) : p;
        Runtime.Set(Design.Figure, "Position", Row(size is { } s ? new Rect2D(held.X, held.Y, s.Width, s.Height) : held));
        _boxes = null;
        OnChanged();
    }

    /// <summary>
    /// Resizes the figure to <paramref name="width"/> by <paramref name="height"/>, no smaller than
    /// <see cref="MinimumFigureSize"/>, as one edit (<c>FigureResizeCommand</c>): its <c>Position</c>'s
    /// size; what it holds keeps its positions, from the figure's bottom-left corner. The figure is
    /// selected.
    /// </summary>
    /// <returns>Whether the size changed.</returns>
    public bool ResizeFigure(double width, double height)
    {
        if (!CanResizeFigure)
        {
            return false;
        }

        PreviewFigureSize(null);
        EndGesture(commit: false);
        if (Runtime.PositionOf(Design.Figure) is not { } p)
        {
            return false;
        }

        var after = new Rect2D(p.X, p.Y, System.Math.Max(width, MinimumFigureSize), System.Math.Max(height, MinimumFigureSize));
        if (after == p)
        {
            return false;
        }

        Do([new PositionEdit(Design.Figure, Design.Figure.Get("Position"), Row(after), Row(p))]);
        return true;
    }

    // --- the edits -----------------------------------------------------------------------------------

    /// <summary>
    /// A container's children put in another order (<c>ReorderCommand</c>): the order the code makes
    /// them in, and so their stack and their tab order; the runtime's stack follows. The selection stays.
    /// </summary>
    private sealed record OrderEdit(DesignComponent Component, IReadOnlyList<DesignComponent> Before, IReadOnlyList<DesignComponent> After) : Edit(Component)
    {
        public override bool KeepsSelection => true;

        public override void Do(DesignDocument document) => Put(document, After);

        public override void Undo(DesignDocument document) => Put(document, Before);

        private void Put(DesignDocument document, IReadOnlyList<DesignComponent> order)
        {
            foreach (DesignComponent child in order)
            {
                Component.Remove(child);
            }

            foreach (DesignComponent child in order)
            {
                Component.Add(child);
            }

            document.Runtime.Restack(Component);
        }
    }

    /// <summary>Nothing changed, but the picks and the anchor as they were, once the step is done or undone (the arrange commands' <c>setComponentSelection</c> and <c>setAnchorComponent</c>).</summary>
    private sealed record PicksEdit(DesignComponent Component, IReadOnlyList<DesignPick> Picks, DesignPick? AnchorPick) : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
        }

        public override void Undo(DesignDocument document)
        {
        }

        public override IReadOnlyList<DesignPick>? PicksAfter(bool done) => Picks;
    }
}
