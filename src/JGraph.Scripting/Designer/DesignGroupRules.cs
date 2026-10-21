using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// One thing selected on the canvas (App Designer's selection holds peer nodes, a group's among
/// them): a component in no selected group, or a group selected whole.
/// </summary>
public readonly record struct DesignPick
{
    private DesignPick(DesignComponent? component, DesignGroup? group)
    {
        Component = component;
        Group = group;
    }

    /// <summary>The component; null for a group.</summary>
    public DesignComponent? Component { get; }

    /// <summary>The group; null for a component.</summary>
    public DesignGroup? Group { get; }

    /// <summary>A component selected on its own.</summary>
    public static DesignPick Of(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new(component, null);
    }

    /// <summary>A group selected whole.</summary>
    public static DesignPick Of(DesignGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new(null, group);
    }

    /// <summary>The components the pick stands for: the component, or every component in the group.</summary>
    public IEnumerable<DesignComponent> Leaves() => Component is { } c ? [c] : Group?.AllComponents() ?? [];

    /// <summary>The container the pick is in: the component's parent, or the group's members' container (<c>ContentParentId</c>).</summary>
    public DesignComponent? Container => Component?.Parent ?? Group?.Container;

    /// <inheritdoc/>
    public override string ToString() => Component?.CodeName ?? (Group is { } g ? $"group {g.Id}" : "?");
}

/// <summary>Where a labelled component's label stands across its field (<c>LabeledComponentResizeCalculator</c>'s <c>HorzLabelPosition</c>).</summary>
public enum LabelAcross
{
    /// <summary>Centred on the field, within 2 pixels.</summary>
    Center,

    /// <summary>Wholly left of the field.</summary>
    Before,

    /// <summary>Wholly right of the field.</summary>
    After,

    /// <summary>Over the field, towards its left.</summary>
    LeftHalf,

    /// <summary>Over the field, towards its right.</summary>
    RightHalf,
}

/// <summary>Where a labelled component's label stands up and down its field (<c>VertLabelPosition</c>).</summary>
public enum LabelAlong
{
    /// <summary>Centred on the field, within 2 pixels.</summary>
    Middle,

    /// <summary>Wholly above the field.</summary>
    Above,

    /// <summary>Wholly below the field.</summary>
    Below,

    /// <summary>Over the field, towards its top.</summary>
    UpperHalf,

    /// <summary>Over the field, towards its bottom.</summary>
    LowerHalf,
}

/// <summary>How a labelled component resizes (<c>ComponentAdapter.LabeledComponent.ResizeType</c>).</summary>
public enum LabeledResize
{
    /// <summary>Any side: four handles.</summary>
    All,

    /// <summary>Across only: the left and right handles (text and number fields, spinners, drop-downs, date and colour pickers).</summary>
    Horizontal,

    /// <summary>One axis, the one the field's shape does not fix (a slider).</summary>
    Fixed,
}

/// <summary>
/// App Designer's rules for component groups on the canvas (open item 150), with no window:
/// which pick a press makes (<c>GroupSelectionRules</c>, <c>WidgetSelectionRules</c>), when the
/// grouping actions apply (<c>GroupUtil.canCreateGroup</c> and the rest), and how a labelled
/// component resizes with its label following (<c>LabeledComponentResizeCalculator</c>).
/// </summary>
public static class DesignGroupRules
{
    /// <summary>The field classes whose labelled component resizes across only.</summary>
    private static readonly HashSet<string> HorizontalFields = new(StringComparer.Ordinal)
    {
        "matlab.ui.control.EditField",
        "matlab.ui.control.NumericEditField",
        "matlab.ui.control.Spinner",
        "matlab.ui.control.DropDown",
        "matlab.ui.control.DatePicker",
        "matlab.ui.control.ColorPicker",
    };

    /// <summary>How a labelled component whose field is of <paramref name="fieldType"/> resizes.</summary>
    public static LabeledResize ResizeOf(string fieldType) =>
        HorizontalFields.Contains(fieldType) ? LabeledResize.Horizontal
        : fieldType == "matlab.ui.control.Slider" ? LabeledResize.Fixed
        : LabeledResize.All;

    // --- selection ---------------------------------------------------------------------------------

    /// <summary>What a press decides: the picks selected now, and those selected on a release with no drag between.</summary>
    /// <param name="Now">The selection after the press, or null when the press leaves it.</param>
    /// <param name="OnRelease">The selection a click (no drag) makes on release, or null for none.</param>
    public readonly record struct PressResult(IReadOnlyList<DesignPick>? Now, IReadOnlyList<DesignPick>? OnRelease)
    {
        /// <summary>Nothing changes.</summary>
        public static PressResult None => new(null, null);
    }

    /// <summary>
    /// What a press on <paramref name="hit"/> selects, given <paramref name="selection"/> (the picks),
    /// as App Designer's selection rules decide it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A member of a group (<c>GroupSelectionRules.selectGroupMember</c>): with no keys, a press
    /// selects its group - unless the group or another member is selected already, when it selects
    /// the member alone; one on a selected pick that is one of several selects it alone on release;
    /// a selection whose ancestor group is selected waits for the release too, so that a drag moves
    /// what is selected. Alt drills up: the group, or past the outermost selected group of its chain.
    /// Ctrl or Shift adds: nothing or the figure selected takes the group; a selected member comes
    /// out; one of another container is selected alone; one whose chain holds nothing selected is
    /// added; else it takes the place of its selected ancestor group.
    /// </para>
    /// <para>
    /// A group pressed on its own area (<c>selectGroup</c>) follows the same rules with itself as
    /// the pick. A component in no group (<c>WidgetSelectionRules.selectWidget</c>): Ctrl or Shift
    /// selects it alone when the figure is selected, takes it out when it is selected, adds it when
    /// it is in the first pick's container (a group's being its members'), else selects it alone;
    /// no keys selects it, or on release when it is one of several selected.
    /// </para>
    /// </remarks>
    public static PressResult Press(AppDesign design, IReadOnlyList<DesignPick> selection, DesignPick hit, DesignModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(selection);
        bool control = modifiers.HasFlag(DesignModifiers.Control);
        bool shift = modifiers.HasFlag(DesignModifiers.Shift);
        bool alt = modifiers.HasFlag(DesignModifiers.Alt);
        // App Designer's EventUtils: Ctrl alone or Shift alone adds, Alt alone drills up, no keys
        // selects; any other combination does nothing.
        bool multiple = (control ^ shift) && !alt;
        bool drillUp = alt && !control && !shift;
        if (!multiple && !drillUp && (control || shift || alt))
        {
            return PressResult.None;
        }

        bool figureSelected = selection is [{ Component: { Parent: null } }];
        bool isSelected = IsSelected(selection, hit);
        DesignGroup? parentGroup = hit.Component is { } hc ? design.GroupOf(hc) : hit.Group?.Parent;

        if (hit.Component is { Parent: null })
        {
            return new PressResult([hit], null); // the figure
        }

        if (parentGroup is null && hit.Group is null)
        {
            // A component in no group (WidgetSelectionRules, which has no Alt branch).
            if (drillUp)
            {
                return PressResult.None;
            }

            if (multiple)
            {
                if (figureSelected)
                {
                    return new PressResult([hit], null);
                }

                if (isSelected)
                {
                    return new PressResult(Without(selection, hit), null);
                }

                return selection.Count == 0 || ReferenceEquals(selection[0].Container, hit.Container)
                    ? new PressResult([.. selection, hit], null)
                    : new PressResult([hit], null);
            }

            if (isSelected && selection.Count > 1)
            {
                return new PressResult(null, [hit]);
            }

            return isSelected ? PressResult.None : new PressResult([hit], null);
        }

        // A member, or a group (GroupSelectionRules).
        if (drillUp)
        {
            DesignPick target;
            if (hit.Group is { } group)
            {
                target = selection.Count > 1 || figureSelected ? hit : DesignPick.Of(NextOutermostUnselected(selection, group));
            }
            else
            {
                target = selection.Count > 1 || figureSelected ? DesignPick.Of(parentGroup!) : DesignPick.Of(NextOutermostUnselected(selection, parentGroup!));
            }

            return new PressResult([target], null);
        }

        if (multiple)
        {
            if (hit.Component is not null)
            {
                DesignGroup group = parentGroup!;
                if (selection.Count == 0 || figureSelected)
                {
                    return new PressResult([DesignPick.Of(group)], null);
                }

                if (isSelected)
                {
                    return new PressResult(Without(selection, hit), null);
                }

                if (!CanBeAdded(selection, hit))
                {
                    return new PressResult([hit], null);
                }

                if (!HasSelectionInChain(selection, group))
                {
                    return new PressResult([.. selection, hit], null);
                }

                DesignGroup? selectedAncestor = SelectedAncestor(selection, group);
                return selectedAncestor is not null
                    ? new PressResult([.. Without(selection, DesignPick.Of(selectedAncestor)), hit], null)
                    : new PressResult([.. selection, hit], null);
            }
            else
            {
                DesignGroup group = hit.Group!;
                if (selection.Count == 0 || figureSelected)
                {
                    return new PressResult([hit], null);
                }

                if (isSelected)
                {
                    return new PressResult(Without(selection, hit), null);
                }

                if (!CanBeAdded(selection, hit))
                {
                    return new PressResult([hit], null);
                }

                if (!HasSelectionInChain(selection, group))
                {
                    return new PressResult([.. selection, hit], null);
                }

                if (group.Parent is { } up && SelectedAncestor(selection, up) is { } ancestor)
                {
                    return new PressResult([.. Without(selection, DesignPick.Of(ancestor)), hit], null);
                }

                // Selected descendants give way to the group.
                DesignPick[] descendants = selection.Where(p => IsInside(p, group)).ToArray();
                return descendants.Length > 0
                    ? new PressResult([.. selection.Where(p => !IsInside(p, group)), hit], null)
                    : new PressResult([.. selection, hit], null);
            }
        }

        // No keys (_processSingleSelection).
        if (isSelected)
        {
            return selection.Count > 1 ? new PressResult(null, [hit]) : PressResult.None;
        }

        DesignPick pick = hit;
        if (hit.Component is not null)
        {
            DesignGroup group = parentGroup!;
            bool groupSelected = IsSelected(selection, DesignPick.Of(group));
            bool anotherMemberSelected = group.Members.Any(m => IsSelected(selection, m.Component is { } c ? DesignPick.Of(c) : DesignPick.Of(m.Group!)));
            if (!groupSelected && !anotherMemberSelected)
            {
                pick = DesignPick.Of(group);
            }
        }

        DesignGroup? chain = pick.Component is { } pc ? design.GroupOf(pc) : pick.Group?.Parent;
        return HasSelectedAncestor(selection, chain)
            ? new PressResult(null, [pick])
            : new PressResult([pick], null);
    }

    /// <summary>Whether <paramref name="pick"/> is among <paramref name="selection"/>.</summary>
    public static bool IsSelected(IReadOnlyList<DesignPick> selection, DesignPick pick)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Any(p => Same(p, pick));
    }

    private static bool Same(DesignPick a, DesignPick b) =>
        (a.Component is not null && ReferenceEquals(a.Component, b.Component)) || (a.Group is not null && ReferenceEquals(a.Group, b.Group));

    private static DesignPick[] Without(IReadOnlyList<DesignPick> selection, DesignPick pick) => selection.Where(p => !Same(p, pick)).ToArray();

    /// <summary>Whether <paramref name="hit"/> can join <paramref name="selection"/>: it is in the first pick's container (<c>_canBeAddedToSelection</c>).</summary>
    private static bool CanBeAdded(IReadOnlyList<DesignPick> selection, DesignPick hit) =>
        selection.Count == 0 || ReferenceEquals(selection[0].Container, hit.Container);

    /// <summary>Whether any pick is in the chain of groups <paramref name="group"/> is in, from its root (<c>_hasSelectionsInSameGroupHierarchy</c>).</summary>
    private static bool HasSelectionInChain(IReadOnlyList<DesignPick> selection, DesignGroup group)
    {
        DesignGroup root = group.Root;
        foreach (DesignPick p in selection)
        {
            if (p.Group is { } g && ReferenceEquals(g.Root, root))
            {
                return true;
            }

            if (p.Component is { } c && root.Contains(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The selected group among <paramref name="group"/> and the groups around it, innermost first (<c>_getSelectedAncestorGroupId</c>).</summary>
    private static DesignGroup? SelectedAncestor(IReadOnlyList<DesignPick> selection, DesignGroup group)
    {
        for (DesignGroup? at = group; at is not null; at = at.Parent)
        {
            if (IsSelected(selection, DesignPick.Of(at)))
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>Whether a group around <paramref name="group"/>, itself included, is selected (<c>_hasSelectedAncestorGroup</c>).</summary>
    private static bool HasSelectedAncestor(IReadOnlyList<DesignPick> selection, DesignGroup? group) =>
        group is not null && SelectedAncestor(selection, group) is not null;

    private static bool IsInside(DesignPick pick, DesignGroup group) =>
        pick.Component is { } c ? group.Contains(c) : pick.Group is { } g && !ReferenceEquals(g, group) && group.Contains(g);

    /// <summary>
    /// The group a drill-up selects (<c>_getNextOutermostUnselectedGroup</c>): from the chain
    /// <paramref name="group"/>, its parent and so on outward, the group just outside the outermost
    /// selected one; the outermost itself when it is selected; <paramref name="group"/> when none is.
    /// </summary>
    private static DesignGroup NextOutermostUnselected(IReadOnlyList<DesignPick> selection, DesignGroup group)
    {
        var chain = new List<DesignGroup>();
        for (DesignGroup? at = group; at is not null; at = at.Parent)
        {
            chain.Add(at);
        }

        int r = chain.Count - 1;
        for (int a = r; a >= 0; a--)
        {
            if (IsSelected(selection, DesignPick.Of(chain[a])))
            {
                return a < r ? chain[a + 1] : chain[r];
            }
        }

        return chain[0];
    }

    // --- the grouping actions --------------------------------------------------------------------

    /// <summary>
    /// Whether Group applies (<c>canCreateGroup</c>): two or more picks, none a member of a group,
    /// none the figure. (<c>GroupingAction</c>'s grid exclusion sits behind a test of a property its
    /// selection manager does not have, so it never runs: R2025b groups in a grid, and its shipped
    /// apps hold such groups.)
    /// </summary>
    public static bool CanGroup(AppDesign design, IReadOnlyList<DesignPick> selection)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Count > 1 && selection.All(p => p.Container is not null && !IsMember(design, p));
    }

    /// <summary>Whether Add to Group applies (<c>canAddToGroup</c>): two or more picks, exactly one a group, no member among them.</summary>
    public static bool CanAddToGroup(AppDesign design, IReadOnlyList<DesignPick> selection)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Count > 1 && selection.Count(static p => p.Group is not null) == 1
            && selection.All(p => p.Container is not null && !IsMember(design, p));
    }

    /// <summary>
    /// Whether Remove from Group applies (<c>canRemoveFromGroup</c>): every pick a member of one
    /// group that is itself in no group, and the group keeping two or more members.
    /// </summary>
    public static bool CanRemoveFromGroup(AppDesign design, IReadOnlyList<DesignPick> selection)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Count == 0 || !selection.All(p => IsMember(design, p)))
        {
            return false;
        }

        DesignGroup? group = GroupOfPick(design, selection[0]);
        return group is { Parent: null } && selection.All(p => ReferenceEquals(GroupOfPick(design, p), group))
            && group.Members.Count - 2 >= selection.Count;
    }

    /// <summary>Whether Ungroup applies (<c>canUngroup</c>): one or more picks, every one a group in no group.</summary>
    public static bool CanUngroup(AppDesign design, IReadOnlyList<DesignPick> selection)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Count > 0 && selection.All(static p => p.Group is { Parent: null });
    }

    /// <summary>The group <paramref name="pick"/> is a member of; null when it is in none.</summary>
    public static DesignGroup? GroupOfPick(AppDesign design, DesignPick pick)
    {
        ArgumentNullException.ThrowIfNull(design);
        return pick.Component is { } c ? design.GroupOf(c) : pick.Group?.Parent;
    }

    private static bool IsMember(AppDesign design, DesignPick pick) => GroupOfPick(design, pick) is not null;

    // --- a labelled component's resize --------------------------------------------------------------

    /// <summary>Where a label stands across its field, from their boxes (<c>P</c>).</summary>
    public static LabelAcross AcrossOf(Rect2D label, Rect2D field)
    {
        double lc = label.X + (label.Width * .5);
        double fc = field.X + (field.Width * .5);
        if (System.Math.Abs(lc - fc) <= 2)
        {
            return LabelAcross.Center;
        }

        if (lc > fc)
        {
            return label.Right > field.Right ? (System.Math.Abs(label.X - field.X) < 1 ? LabelAcross.LeftHalf : LabelAcross.After) : LabelAcross.RightHalf;
        }

        return label.X < field.X ? (System.Math.Abs(label.Right - field.Right) < 1 ? LabelAcross.RightHalf : LabelAcross.Before) : LabelAcross.LeftHalf;
    }

    /// <summary>Where a label stands up and down its field, from their boxes (<c>x</c>).</summary>
    public static LabelAlong AlongOf(Rect2D label, Rect2D field)
    {
        double lc = label.Y + (label.Height * .5);
        double fc = field.Y + (field.Height * .5);
        if (System.Math.Abs(lc - fc) <= 2)
        {
            return LabelAlong.Middle;
        }

        if (lc > fc)
        {
            return label.Bottom > field.Bottom ? LabelAlong.Below : LabelAlong.LowerHalf;
        }

        return label.Y < field.Y ? LabelAlong.Above : LabelAlong.UpperHalf;
    }

    /// <summary>The boxes a labelled component's resize leaves: the field's and the label's (on the surface).</summary>
    public readonly record struct LabeledBoxes(Rect2D Field, Rect2D Label);

    /// <summary>
    /// A labelled component resized by one side handle (<c>LabeledComponentResizeCalculator.calculateNewBounds</c>):
    /// the field's edge moves by the drag, snapped to the grid when one is shown (whole steps of
    /// <paramref name="grid"/> from <paramref name="gridOrigin"/>), from the centre when
    /// <paramref name="fromCentre"/>, never under <paramref name="shape"/>'s least size, and a shape that keeps
    /// its ratio keeps it; a side the shape fixes does nothing. The label follows the edge by where
    /// it stands: before the field it moves only from the centre, after it it moves with the edge,
    /// centred it stays centred, over the field it keeps within it.
    /// </summary>
    public static LabeledBoxes ResizeLabeled(
        Rect2D field, Rect2D label, ResizeEdges edge, double dx, double dy, DesignShape shape, bool fromCentre,
        double? grid = null, Point2D gridOrigin = default)
    {
        var b = new Bounds(field, label);
        bool horizontal = edge is ResizeEdges.East or ResizeEdges.West;
        if (edge is not (ResizeEdges.East or ResizeEdges.West or ResizeEdges.North or ResizeEdges.South)
            || (horizontal && shape.FixedWidth) || (!horizontal && shape.FixedHeight))
        {
            return new LabeledBoxes(field, label);
        }

        double minW = shape.Minimum.Width;
        double minH = shape.Minimum.Height;
        if (shape.Ratio is { } ratio && ratio > 0)
        {
            // FIXED aspect: the dragged side moves, the other follows the ratio, the label after both.
            switch (edge)
            {
                case ResizeEdges.East:
                {
                    double moved = b.GrowRight(dx, fromCentre, minW, grid, gridOrigin.X);
                    double was = b.Field.Height;
                    double h = b.Field.Width / ratio;
                    double u = h - was;
                    b.Field = new Rect2D(b.Field.X, fromCentre ? b.Field.Y - (u * .5) : b.Field.Y, b.Field.Width, h);
                    b.Across(fromCentre, moved);
                    b.Along(fromCentre, fromCentre ? u * .5 : u, down: true);
                    break;
                }

                case ResizeEdges.West:
                {
                    double moved = b.ShrinkLeft(dx, fromCentre, minW, grid, gridOrigin.X);
                    double was = b.Field.Height;
                    double h = b.Field.Width / ratio;
                    double u = h - was;
                    b.Field = new Rect2D(b.Field.X, fromCentre ? b.Field.Y - (u * .5) : b.Field.Y, b.Field.Width, h);
                    b.AcrossFromLeft(fromCentre, moved);
                    b.Along(fromCentre, fromCentre ? u * .5 : u, down: true);
                    break;
                }

                case ResizeEdges.South:
                {
                    double moved = b.GrowDown(dy, fromCentre, minH, grid, gridOrigin.Y);
                    double was = b.Field.Width;
                    double w = b.Field.Height * ratio;
                    double u = w - was;
                    b.Field = new Rect2D(fromCentre ? b.Field.X - (u * .5) : b.Field.X, b.Field.Y, w, b.Field.Height);
                    b.Across(fromCentre, fromCentre ? u * .5 : u);
                    b.Along(fromCentre, moved, down: true);
                    break;
                }

                default:
                {
                    double moved = b.ShrinkUp(dy, fromCentre, minH, grid, gridOrigin.Y);
                    double was = b.Field.Width;
                    double w = b.Field.Height * ratio;
                    double u = w - was;
                    b.Field = new Rect2D(fromCentre ? b.Field.X - (u * .5) : b.Field.X, b.Field.Y, w, b.Field.Height);
                    b.Across(fromCentre, fromCentre ? u * .5 : u);
                    b.AlongFromTop(fromCentre, moved);
                    break;
                }
            }
        }
        else
        {
            switch (edge)
            {
                case ResizeEdges.East:
                    b.Across(fromCentre, b.GrowRight(dx, fromCentre, minW, grid, gridOrigin.X));
                    break;
                case ResizeEdges.West:
                    b.AcrossFromLeft(fromCentre, b.ShrinkLeft(dx, fromCentre, minW, grid, gridOrigin.X));
                    break;
                case ResizeEdges.South:
                    b.Along(fromCentre, b.GrowDown(dy, fromCentre, minH, grid, gridOrigin.Y), down: true);
                    break;
                default:
                    b.AlongFromTop(fromCentre, b.ShrinkUp(dy, fromCentre, minH, grid, gridOrigin.Y));
                    break;
            }
        }

        return new LabeledBoxes(Round(b.Field), Round(b.Label));

        static Rect2D Round(Rect2D r) => new(
            System.Math.Round(r.X, MidpointRounding.AwayFromZero), System.Math.Round(r.Y, MidpointRounding.AwayFromZero),
            System.Math.Round(r.Width, MidpointRounding.AwayFromZero), System.Math.Round(r.Height, MidpointRounding.AwayFromZero));
    }

    /// <summary>The field's and the label's boxes as the calculator works on them, with the label's standing (<c>b</c>).</summary>
    private sealed class Bounds(Rect2D field, Rect2D label)
    {
        public Rect2D Field { get; set; } = field;

        public Rect2D Label { get; set; } = label;

        private readonly LabelAcross across = AcrossOf(label, field);
        private readonly LabelAlong along = AlongOf(label, field);

        /// <summary>The right edge dragged by <paramref name="dx"/> (<c>C</c>): what the width grew by (half of it, from the centre).</summary>
        public double GrowRight(double dx, bool centre, double min, double? grid, double origin)
        {
            double was = Field.Width;
            double snap = Snap(Field.Right + dx, grid, origin);
            if (centre)
            {
                double width = System.Math.Max(min, Field.Width + ((dx + snap) * 2));
                double o = (width - was) * .5;
                Field = new Rect2D(Field.X - o, Field.Y, width, Field.Height);
                return o;
            }
            else
            {
                double width = System.Math.Max(min, Field.Width + dx + snap);
                Field = new Rect2D(Field.X, Field.Y, width, Field.Height);
                return width - was;
            }
        }

        /// <summary>The left edge dragged by <paramref name="dx"/> (<c>A</c>): what the width shrank by (half of it, from the centre).</summary>
        public double ShrinkLeft(double dx, bool centre, double min, double? grid, double origin)
        {
            double was = Field.Width;
            double snap = Snap(Field.X + dx, grid, origin);
            if (centre)
            {
                double width = System.Math.Max(min, Field.Width - ((dx + snap) * 2));
                double o = (was - width) * .5;
                Field = new Rect2D(Field.X + o, Field.Y, width, Field.Height);
                return o;
            }
            else
            {
                double width = System.Math.Max(min, Field.Width - dx - snap);
                Field = new Rect2D(Field.Right - width, Field.Y, width, Field.Height);
                return was - width;
            }
        }

        /// <summary>The bottom edge dragged by <paramref name="dy"/> (<c>D</c>).</summary>
        public double GrowDown(double dy, bool centre, double min, double? grid, double origin)
        {
            double was = Field.Height;
            double snap = Snap(Field.Bottom + dy, grid, origin);
            if (centre)
            {
                double height = System.Math.Max(min, Field.Height + ((dy + snap) * 2));
                double o = (height - was) * .5;
                Field = new Rect2D(Field.X, Field.Y - o, Field.Width, height);
                return o;
            }
            else
            {
                double height = System.Math.Max(min, Field.Height + dy + snap);
                Field = new Rect2D(Field.X, Field.Y, Field.Width, height);
                return height - was;
            }
        }

        /// <summary>The top edge dragged by <paramref name="dy"/> (<c>M</c>).</summary>
        public double ShrinkUp(double dy, bool centre, double min, double? grid, double origin)
        {
            double was = Field.Height;
            double snap = Snap(Field.Y + dy, grid, origin);
            if (centre)
            {
                double height = System.Math.Max(min, Field.Height - ((dy + snap) * 2));
                double o = (was - height) * .5;
                Field = new Rect2D(Field.X, Field.Y + o, Field.Width, height);
                return o;
            }
            else
            {
                double height = System.Math.Max(min, Field.Height - dy - snap);
                Field = new Rect2D(Field.X, Field.Bottom - height, Field.Width, height);
                return was - height;
            }
        }

        /// <summary>The label following a right-edge drag by <paramref name="f"/> (<c>S</c>).</summary>
        public void Across(bool centre, double f)
        {
            switch (across)
            {
                case LabelAcross.Before:
                    if (centre)
                    {
                        Label = new Rect2D(Label.X - f, Label.Y, Label.Width, Label.Height);
                    }

                    break;
                case LabelAcross.After:
                    Label = new Rect2D(Label.X + f, Label.Y, Label.Width, Label.Height);
                    break;
                case LabelAcross.Center:
                    Label = new Rect2D(Field.X + (Field.Width * .5) - (Label.Width * .5), Label.Y, Label.Width, Label.Height);
                    break;
                case LabelAcross.LeftHalf:
                {
                    double x = centre ? Label.X - f : Label.X;
                    double right = System.Math.Min(Field.Right, x + Label.Width);
                    x = System.Math.Max(Field.X, right - Label.Width);
                    Label = new Rect2D(x, Label.Y, Label.Width, Label.Height);
                    break;
                }

                default: // RightHalf
                {
                    double x = System.Math.Max(Field.X, Label.X + f);
                    double right = System.Math.Min(Field.Right, x + Label.Width);
                    Label = new Rect2D(right - Label.Width, Label.Y, Label.Width, Label.Height);
                    break;
                }
            }
        }

        /// <summary>The label following a left-edge drag by <paramref name="f"/> (<c>y</c>).</summary>
        public void AcrossFromLeft(bool centre, double f)
        {
            switch (across)
            {
                case LabelAcross.Before:
                    Label = new Rect2D(Label.X + f, Label.Y, Label.Width, Label.Height);
                    break;
                case LabelAcross.After:
                    if (centre)
                    {
                        Label = new Rect2D(Label.X - f, Label.Y, Label.Width, Label.Height);
                    }

                    break;
                case LabelAcross.Center:
                    Label = new Rect2D(Field.X + (Field.Width * .5) - (Label.Width * .5), Label.Y, Label.Width, Label.Height);
                    break;
                case LabelAcross.LeftHalf:
                {
                    double right = System.Math.Min(Field.Right, Label.Right + f);
                    double x = System.Math.Max(Field.X, right - Label.Width);
                    Label = new Rect2D(x, Label.Y, Label.Width, Label.Height);
                    break;
                }

                default: // RightHalf
                {
                    double x = centre ? Label.X - f : Label.X;
                    x = System.Math.Max(Field.X, x);
                    double right = System.Math.Min(Field.Right, x + Label.Width);
                    Label = new Rect2D(right - Label.Width, Label.Y, Label.Width, Label.Height);
                    break;
                }
            }
        }

        /// <summary>The label following a bottom-edge drag by <paramref name="f"/> (<c>T</c>).</summary>
        public void Along(bool centre, double f, bool down)
        {
            switch (along)
            {
                case LabelAlong.Above:
                    if (centre)
                    {
                        Label = new Rect2D(Label.X, Label.Y - f, Label.Width, Label.Height);
                    }

                    break;
                case LabelAlong.Below:
                    Label = new Rect2D(Label.X, Label.Y + f, Label.Width, Label.Height);
                    break;
                case LabelAlong.Middle:
                    Label = new Rect2D(Label.X, Field.Y + (Field.Height * .5) - (Label.Height * .5), Label.Width, Label.Height);
                    break;
                case LabelAlong.UpperHalf:
                {
                    double y = centre ? Label.Y - f : Label.Y;
                    double bottom = System.Math.Min(Field.Bottom, y + Label.Height);
                    y = System.Math.Max(Field.Y, bottom - Label.Height);
                    Label = new Rect2D(Label.X, y, Label.Width, Label.Height);
                    break;
                }

                default: // LowerHalf
                {
                    double y = System.Math.Max(Field.Y, Label.Y + f);
                    double bottom = System.Math.Min(Field.Bottom, y + Label.Height);
                    Label = new Rect2D(Label.X, bottom - Label.Height, Label.Width, Label.Height);
                    break;
                }
            }
        }

        /// <summary>The label following a top-edge drag by <paramref name="f"/> (<c>I</c>).</summary>
        public void AlongFromTop(bool centre, double f)
        {
            switch (along)
            {
                case LabelAlong.Above:
                    Label = new Rect2D(Label.X, Label.Y + f, Label.Width, Label.Height);
                    break;
                case LabelAlong.Below:
                    if (centre)
                    {
                        Label = new Rect2D(Label.X, Label.Y - f, Label.Width, Label.Height);
                    }

                    break;
                case LabelAlong.Middle:
                    Label = new Rect2D(Label.X, Field.Y + (Field.Height * .5) - (Label.Height * .5), Label.Width, Label.Height);
                    break;
                case LabelAlong.UpperHalf:
                {
                    double bottom = System.Math.Min(Field.Bottom, Label.Bottom + f);
                    double y = System.Math.Max(Field.Y, bottom - Label.Height);
                    Label = new Rect2D(Label.X, y, Label.Width, Label.Height);
                    break;
                }

                default: // LowerHalf
                {
                    double y = centre ? Label.Y - f : Label.Y;
                    y = System.Math.Max(Field.Y, y);
                    double bottom = System.Math.Min(Field.Bottom, y + Label.Height);
                    Label = new Rect2D(Label.X, bottom - Label.Height, Label.Width, Label.Height);
                    break;
                }
            }
        }

        /// <summary>The snap a dragged edge at <paramref name="at"/> takes to the grid: to the nearest whole step from <paramref name="origin"/> (<c>V</c>).</summary>
        private static double Snap(double at, double? grid, double origin)
        {
            if (grid is not { } g || g <= 0)
            {
                return 0;
            }

            double u = at - origin;
            return (System.Math.Round(u / g, MidpointRounding.AwayFromZero) * g) - u;
        }
    }
}
