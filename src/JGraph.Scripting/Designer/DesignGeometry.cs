using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>Which edges of a component a resize moves: a handle's compass point.</summary>
[Flags]
public enum ResizeEdges
{
    None = 0,
    North = 1,
    South = 2,
    East = 4,
    West = 8,
    NorthEast = North | East,
    NorthWest = North | West,
    SouthEast = South | East,
    SouthWest = South | West,
}

/// <summary>What a component's shape allows (probe <c>u9_extra</c>, recorded again by <c>ds2_sizes</c>).</summary>
/// <param name="Ratio">The width over the height it keeps whatever it is given, or null for any shape.</param>
/// <param name="FixedWidth">Whether its width cannot be changed (a vertical slider).</param>
/// <param name="FixedHeight">Whether its height cannot be changed (a horizontal slider).</param>
/// <param name="Minimum">The smallest size the canvas resizes it to.</param>
public readonly record struct DesignShape(double? Ratio, bool FixedWidth, bool FixedHeight, Size2D Minimum)
{
    /// <summary>Any shape, down to App Designer's least size for a component, 10 by 10.</summary>
    public static DesignShape Free { get; } = new(null, false, false, new Size2D(10, 10));
}

/// <summary>An alignment guide the canvas draws while a component moves or resizes: a line on the surface.</summary>
/// <param name="Vertical">Whether the line runs up and down (it aligns left edges, centres or right edges).</param>
/// <param name="Position">Where the line stands across its direction: an x for a vertical line, a y for a horizontal one.</param>
/// <param name="From">Where it starts along its direction.</param>
/// <param name="To">Where it ends along its direction.</param>
/// <param name="Fixed">Whether it marks an edge the resize leaves where it is, aligned already.</param>
public readonly record struct DesignGuide(bool Vertical, double Position, double From, double To, bool Fixed = false);

/// <summary>Where the guides would put a moving box: how far to shift it, and the lines to draw.</summary>
/// <param name="DX">How far to the right the box snaps.</param>
/// <param name="DY">How far down the box snaps.</param>
/// <param name="Guides">The lines that show what it aligned with.</param>
public readonly record struct DesignSnap(double DX, double DY, IReadOnlyList<DesignGuide> Guides);

/// <summary>
/// The canvas's arithmetic (designer plan DS2), as R2025b's App Designer does it - read from its
/// canvas code (<c>AlignmentHintManager</c>, <c>ResizeCalculator</c>, the component move and resize
/// gestures, <c>RubberBandManager</c>, the keyboard mover): alignment guides that snap a moving
/// box to another's edges and centres within 7 pixels, the grid that snaps to a 10-pixel interval
/// when shown, the least size a resize leaves, and the keyboard's 1- and 10-pixel steps. Every box
/// is on the figure's surface, top-left origin, in whole pixels, as App Designer's are.
/// </summary>
public static class DesignGeometry
{
    /// <summary>
    /// The least side of a box drawn in click-and-draw mode (DS3); a smaller drag is a click, which
    /// places the component at its own size, as App Designer's does below two pixels.
    /// </summary>
    public const double MinimumDrawn = 3;

    /// <summary>How near an edge or centre must come to another's to snap to it.</summary>
    public const double SnapDistance = 7;

    /// <summary>How far a guide runs beyond the two boxes it joins.</summary>
    public const double GuideExtension = 20;

    /// <summary>A box this thin or less aligns by its centre only, across that thinness.</summary>
    public const double CentreOnlySize = 10;

    /// <summary>Boxes both this small or less, differing in size by <see cref="SmallDifference"/> or less, align only by their centres.</summary>
    public const double SmallSize = 4 * SnapDistance;

    /// <summary>See <see cref="SmallSize"/>.</summary>
    public const double SmallDifference = 5;

    /// <summary>The grid's interval when first shown, and its least, greatest and step.</summary>
    public const double GridInterval = 10;

    /// <inheritdoc cref="GridInterval"/>
    public const double GridMinimum = 10;

    /// <inheritdoc cref="GridInterval"/>
    public const double GridMaximum = 50;

    /// <inheritdoc cref="GridInterval"/>
    public const double GridStep = 5;

    /// <summary>How far an arrow key moves the selection, and with Shift.</summary>
    public const double Nudge = 1;

    /// <inheritdoc cref="Nudge"/>
    public const double ShiftNudge = 10;

    /// <summary>How many edits the canvas can undo.</summary>
    public const int UndoDepth = 200;

    /// <summary>The least size of a tab group and a button group on the canvas.</summary>
    public static Size2D GroupMinimum { get; } = new(100, 30);

    // --- guides ----------------------------------------------------------------------------------

    private enum Edge
    {
        Left,
        Centre,
        Right,
        Top,
        Middle,
        Bottom,
    }

    private readonly record struct Hint(double Position, double MinEdge, double MaxEdge, double Size, Edge Kind);

    /// <summary>
    /// Where the guides put <paramref name="moving"/>, a box being moved or resized, among
    /// <paramref name="others"/> - every other box the canvas shows, the figure's own among them:
    /// its left edge, centre and right edge each snap to the nearest left or right edge or centre
    /// within <see cref="SnapDistance"/>, and its top, middle and bottom likewise.
    /// </summary>
    /// <param name="moving">The box, where the pointer put it.</param>
    /// <param name="others">The boxes it may align with.</param>
    /// <param name="resizing">The edges a resize moves; <see cref="ResizeEdges.None"/> for a move.
    /// A resize aligns the edges it moves and shows a guide where an edge it keeps is aligned already.</param>
    public static DesignSnap Snap(Rect2D moving, IEnumerable<Rect2D> others, ResizeEdges resizing = ResizeEdges.None)
    {
        ArgumentNullException.ThrowIfNull(others);
        var x = new List<Hint>();
        var centres = new List<Hint>();
        var y = new List<Hint>();
        var middles = new List<Hint>();
        var lefts = new List<Hint>();
        var rights = new List<Hint>();
        var tops = new List<Hint>();
        var bottoms = new List<Hint>();
        foreach (Rect2D other in others)
        {
            Rect2D r = Whole(other);
            double right = r.X + r.Width - 1;
            double bottom = r.Y + r.Height - 1;
            var left = new Hint(r.X, r.Y, bottom, r.Width, Edge.Left);
            var rightEdge = new Hint(right, r.Y, bottom, r.Width, Edge.Right);
            var top = new Hint(r.Y, r.X, right, r.Height, Edge.Top);
            var bottomEdge = new Hint(bottom, r.X, right, r.Height, Edge.Bottom);
            x.Add(left);
            x.Add(rightEdge);
            y.Add(top);
            y.Add(bottomEdge);
            lefts.Add(left);
            rights.Add(rightEdge);
            tops.Add(top);
            bottoms.Add(bottomEdge);
            centres.Add(new Hint(System.Math.Round(r.X + (r.Width / 2) - 1, MidpointRounding.AwayFromZero), r.Y, bottom, r.Width, Edge.Centre));
            middles.Add(new Hint(System.Math.Round(r.Y + (r.Height / 2) - 1, MidpointRounding.AwayFromZero), r.X, right, r.Height, Edge.Middle));
        }

        Rect2D m = Whole(moving);
        bool resize = resizing != ResizeEdges.None;

        // Which of its own lines the box aligns: all six for a move, but not across a thinness of
        // 10 or less; a resize only the edges it moves, its centres never.
        bool narrow = m.Width <= CentreOnlySize;
        bool flat = m.Height <= CentreOnlySize;
        bool useLeft = !narrow && (!resize || resizing.HasFlag(ResizeEdges.West));
        bool useRight = !narrow && (!resize || resizing.HasFlag(ResizeEdges.East));
        bool useTop = !flat && (!resize || resizing.HasFlag(ResizeEdges.North));
        bool useBottom = !flat && (!resize || resizing.HasFlag(ResizeEdges.South));

        var guides = new List<DesignGuide>();
        double dx = Closest(m, vertical: true,
            [
                (useLeft, m.X, Merge(x), Edge.Left),
                (!resize, System.Math.Round(m.X + (m.Width / 2) - 1, MidpointRounding.AwayFromZero), Merge(centres), Edge.Centre),
                (useRight, m.X + m.Width - 1, Merge(x), Edge.Right),
            ], guides);
        double dy = Closest(m, vertical: false,
            [
                (useTop, m.Y, Merge(y), Edge.Top),
                (!resize, System.Math.Round(m.Y + (m.Height / 2) - 1, MidpointRounding.AwayFromZero), Merge(middles), Edge.Middle),
                (useBottom, m.Y + m.Height - 1, Merge(y), Edge.Bottom),
            ], guides);

        // A resize's kept edges, where they line up exactly with another's like edge already.
        if (resize)
        {
            if (resizing.HasFlag(ResizeEdges.East) && Fixed(m.X, lefts) is { } keptLeft)
            {
                guides.Add(Guide(keptLeft, m, vertical: true, isFixed: true));
            }
            else if (resizing.HasFlag(ResizeEdges.West) && Fixed(m.X + m.Width - 1, rights) is { } keptRight)
            {
                guides.Add(Guide(keptRight, m, vertical: true, isFixed: true));
            }

            if (resizing.HasFlag(ResizeEdges.North) && Fixed(m.Y + m.Height - 1, bottoms) is { } keptBottom)
            {
                guides.Add(Guide(keptBottom, m, vertical: false, isFixed: true));
            }
            else if (resizing.HasFlag(ResizeEdges.South) && Fixed(m.Y, tops) is { } keptTop)
            {
                guides.Add(Guide(keptTop, m, vertical: false, isFixed: true));
            }
        }

        return new DesignSnap(dx, dy, guides);
    }

    /// <summary>
    /// The nearest hint within reach of any line the box aligns, and the guides at that distance;
    /// the shift that brings the box onto it, or 0.
    /// </summary>
    private static double Closest(
        Rect2D box, bool vertical, (bool Use, double At, List<Hint> Hints, Edge Kind)[] lines, List<DesignGuide> guides)
    {
        double best = double.PositiveInfinity;
        double shift = 0;
        var found = new List<(Hint Hint, Edge Kind)>();
        double size = vertical ? box.Width : box.Height;
        foreach ((bool use, double at, List<Hint> hints, Edge kind) in lines)
        {
            if (!use)
            {
                continue;
            }

            bool centre = kind is Edge.Centre or Edge.Middle;
            foreach (Hint hint in hints)
            {
                // Two small boxes of nearly one size align by their centres only: their edges
                // would snap to each other a pixel or two apart.
                if (!centre && size <= SmallSize && hint.Size <= SmallSize
                    && System.Math.Abs(size - hint.Size) is var difference && difference != 0 && difference <= SmallDifference)
                {
                    continue;
                }

                double distance = System.Math.Abs(hint.Position - at);
                if (distance > SnapDistance || distance > best)
                {
                    continue;
                }

                if (distance < best)
                {
                    found.Clear();
                    best = distance;
                    shift = hint.Position - at;
                }

                found.Add((hint, kind));
            }
        }

        // A centre and an edge of one box both at the nearest distance: the edge's guide is drawn.
        foreach ((Hint hint, Edge kind) in found)
        {
            if (kind is Edge.Centre or Edge.Middle && found.Count == 2
                && found.Any(f => f.Kind is not (Edge.Centre or Edge.Middle) && f.Hint.MinEdge == hint.MinEdge && f.Hint.MaxEdge == hint.MaxEdge))
            {
                continue;
            }

            Rect2D snapped = vertical ? new Rect2D(box.X + shift, box.Y, box.Width, box.Height) : new Rect2D(box.X, box.Y + shift, box.Width, box.Height);
            guides.Add(Guide(hint, snapped, vertical, isFixed: false));
        }

        return shift;
    }

    private static DesignGuide Guide(Hint hint, Rect2D box, bool vertical, bool isFixed)
    {
        double from = vertical ? System.Math.Min(hint.MinEdge, box.Y) : System.Math.Min(hint.MinEdge, box.X);
        double to = vertical ? System.Math.Max(hint.MaxEdge, box.Y + box.Height) : System.Math.Max(hint.MaxEdge, box.X + box.Width);
        return new DesignGuide(vertical, hint.Position, from - GuideExtension, to + GuideExtension, isFixed);
    }

    /// <summary>The hints at exactly <paramref name="at"/>, as one spanning them all; null when none.</summary>
    private static Hint? Fixed(double at, List<Hint> hints)
    {
        Hint[] there = hints.Where(h => h.Position == at).ToArray();
        return there.Length == 0
            ? null
            : there[0] with { MinEdge = there.Min(static h => h.MinEdge), MaxEdge = there.Max(static h => h.MaxEdge) };
    }

    /// <summary>The hints in order of position, those at one position merged into one spanning them all.</summary>
    private static List<Hint> Merge(List<Hint> hints)
    {
        var merged = new List<Hint>();
        foreach (Hint hint in hints.OrderBy(static h => h.Position))
        {
            if (merged.Count > 0 && merged[^1].Position == hint.Position)
            {
                Hint last = merged[^1];
                merged[^1] = last with { MinEdge = System.Math.Min(last.MinEdge, hint.MinEdge), MaxEdge = System.Math.Max(last.MaxEdge, hint.MaxEdge) };
            }
            else
            {
                merged.Add(hint);
            }
        }

        return merged;
    }

    private static Rect2D Whole(Rect2D r) => new(
        System.Math.Round(r.X, MidpointRounding.AwayFromZero),
        System.Math.Round(r.Y, MidpointRounding.AwayFromZero),
        System.Math.Round(r.Width, MidpointRounding.AwayFromZero),
        System.Math.Round(r.Height, MidpointRounding.AwayFromZero));

    // --- the grid --------------------------------------------------------------------------------

    /// <summary>A coordinate on the grid of <paramref name="interval"/>: the nearest multiple.</summary>
    public static double OnGrid(double value, double interval) =>
        System.Math.Round(value / interval, MidpointRounding.AwayFromZero) * interval;

    /// <summary>A grid interval within App Designer's limits.</summary>
    public static double GridIntervalWithin(double interval) => System.Math.Clamp(interval, GridMinimum, GridMaximum);

    // --- resizing --------------------------------------------------------------------------------

    /// <summary>
    /// The box a resize gives: <paramref name="start"/> with the <paramref name="edges"/> moved by
    /// the pointer's travel (<paramref name="dx"/>, <paramref name="dy"/>), within what
    /// <paramref name="shape"/> allows. From the centre, the opposite edges move the other way; with
    /// the aspect kept, the box keeps <paramref name="start"/>'s proportions. A box whose shape keeps
    /// a ratio keeps it always, its opposite corner where it was.
    /// </summary>
    /// <param name="start">The box when the resize began.</param>
    /// <param name="edges">The handle's edges.</param>
    /// <param name="dx">How far the pointer went right.</param>
    /// <param name="dy">How far the pointer went down.</param>
    /// <param name="shape">What the component's shape allows.</param>
    /// <param name="fromCentre">Whether the box grows about its centre (Ctrl).</param>
    /// <param name="keepAspect">Whether the box keeps its proportions (Shift).</param>
    /// <param name="grid">The grid interval its moving edges snap to, or null.</param>
    public static Rect2D Resize(
        Rect2D start, ResizeEdges edges, double dx, double dy, DesignShape shape, bool fromCentre = false, bool keepAspect = false, double? grid = null)
    {
        double left = start.X;
        double top = start.Y;
        double right = start.X + start.Width;
        double bottom = start.Y + start.Height;

        // The edges the pointer moves, each to the grid if there is one.
        if (edges.HasFlag(ResizeEdges.East) && !shape.FixedWidth)
        {
            right = Snapped(right + dx);
        }
        else if (edges.HasFlag(ResizeEdges.West) && !shape.FixedWidth)
        {
            left = Snapped(left + dx);
        }

        if (edges.HasFlag(ResizeEdges.South) && !shape.FixedHeight)
        {
            bottom = Snapped(bottom + dy);
        }
        else if (edges.HasFlag(ResizeEdges.North) && !shape.FixedHeight)
        {
            top = Snapped(top + dy);
        }

        double width = right - left;
        double height = bottom - top;
        if (fromCentre)
        {
            width = start.Width + (2 * (width - start.Width));
            height = start.Height + (2 * (height - start.Height));
        }

        // Proportions: the shape's own ratio, or the start's when Shift asks. The side the handle
        // drags decides; a corner takes the larger of the two.
        double? ratio = shape.Ratio ?? (keepAspect && start.Height > 0 ? start.Width / start.Height : null);
        if (ratio is { } r && r > 0)
        {
            bool horizontal = edges.HasFlag(ResizeEdges.East) || edges.HasFlag(ResizeEdges.West);
            bool vertical = edges.HasFlag(ResizeEdges.North) || edges.HasFlag(ResizeEdges.South);
            if (horizontal && vertical)
            {
                if (width * start.Height >= height * start.Width)
                {
                    height = width / r;
                }
                else
                {
                    width = height * r;
                }
            }
            else if (horizontal)
            {
                height = width / r;
            }
            else
            {
                width = height * r;
            }
        }

        // The least size, kept in proportion where the shape has one.
        double minW = shape.Minimum.Width;
        double minH = shape.Minimum.Height;
        if (ratio is { } k && k > 0)
        {
            if (width < minW || height < minH)
            {
                double scale = System.Math.Max(minW / System.Math.Max(width, 1e-9), minH / System.Math.Max(height, 1e-9));
                width *= scale;
                height *= scale;
            }
        }
        else
        {
            width = shape.FixedWidth ? start.Width : System.Math.Max(width, minW);
            height = shape.FixedHeight ? start.Height : System.Math.Max(height, minH);
        }

        width = System.Math.Round(width, MidpointRounding.AwayFromZero);
        height = System.Math.Round(height, MidpointRounding.AwayFromZero);

        // Where the box stands: about its centre, or with the edges the handle does not move kept.
        double x;
        double y;
        if (fromCentre)
        {
            x = start.X + System.Math.Round((start.Width - width) / 2, MidpointRounding.AwayFromZero);
            y = start.Y + System.Math.Round((start.Height - height) / 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            x = edges.HasFlag(ResizeEdges.West) ? start.X + start.Width - width : start.X;
            y = edges.HasFlag(ResizeEdges.North) ? start.Y + start.Height - height : start.Y;
        }

        return new Rect2D(x, y, width, height);

        double Snapped(double value) => grid is { } g ? OnGrid(value, g) : value;
    }

    // --- selection -------------------------------------------------------------------------------

    /// <summary>
    /// What a rubber band selects: the components wholly inside <paramref name="band"/>, all in one
    /// container - the container of the one nearest where the band began - and only those of them
    /// not inside another that is selected.
    /// </summary>
    /// <param name="band">The band, on the surface.</param>
    /// <param name="start">Where the band began.</param>
    /// <param name="boxes">The boxes the canvas shows.</param>
    public static IReadOnlyList<DesignComponent> RubberBand(Rect2D band, Point2D start, IEnumerable<DesignBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        DesignBox[] inside = boxes
            .Where(b => b.Shown && b.Box.X >= band.X && b.Box.Y >= band.Y
                && b.Box.X + b.Box.Width <= band.X + band.Width && b.Box.Y + b.Box.Height <= band.Y + band.Height)
            .ToArray();
        var included = new HashSet<DesignComponent>(inside.Select(static b => b.Component), ReferenceEqualityComparer.Instance);
        DesignBox[] tops = inside.Where(b => !Within(b.Component, included)).ToArray();
        if (tops.Length == 0)
        {
            return [];
        }

        DesignBox nearest = tops.MinBy(b => Distance(b.Box, start));
        return tops.Where(b => ReferenceEquals(b.Component.Parent, nearest.Component.Parent)).Select(static b => b.Component).ToArray();

        static double Distance(Rect2D box, Point2D at)
        {
            double cx = box.X + (box.Width / 2) - at.X;
            double cy = box.Y + (box.Height / 2) - at.Y;
            return (cx * cx) + (cy * cy);
        }

        static bool Within(DesignComponent component, HashSet<DesignComponent> set)
        {
            for (DesignComponent? up = component.Parent; up is not null; up = up.Parent)
            {
                if (set.Contains(up))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The selection after a click on <paramref name="clicked"/>: it alone; or, with Ctrl or Shift, it
    /// added to <paramref name="selection"/> when it shares its container, and alone when it does not;
    /// a selected one is taken out again. A figure is always selected alone.
    /// </summary>
    public static IReadOnlyList<DesignComponent> Click(IReadOnlyList<DesignComponent> selection, DesignComponent clicked, bool control, bool shift)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(clicked);
        bool selected = selection.Contains(clicked, ReferenceEqualityComparer.Instance);
        if (!(control || shift) || clicked.Parent is null || selection.Any(static s => s.Parent is null))
        {
            return [clicked];
        }

        if (selected)
        {
            return selection.Where(s => !ReferenceEquals(s, clicked)).ToArray();
        }

        return selection.Count == 0 || ReferenceEquals(selection[0].Parent, clicked.Parent)
            ? [.. selection, clicked]
            : [clicked];
    }
}
