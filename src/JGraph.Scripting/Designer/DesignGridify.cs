using System.Globalization;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>A component "Apply Grid Layout" puts into the new grid.</summary>
/// <param name="Id">The name the result knows the component by; unique among the components gridified together.</param>
/// <param name="Box">
/// The component's box relative to its container's content area: top-left origin, y down, in design
/// pixels at zoom 1. For a component placed by its <c>Position</c> [x y w h] in a container whose
/// content is H pixels high, that is (x - 1, H - (y - 1) - h, w, h).
/// </param>
/// <param name="GrowsHorizontally">Whether the component grows across (<see cref="DesignGridify.Growth"/>).</param>
/// <param name="GrowsVertically">Whether the component grows up and down.</param>
public sealed record GridifyComponent(string Id, Rect2D Box, bool GrowsHorizontally, bool GrowsVertically);

/// <summary>
/// One row's height or one column's width as App Designer gives it to the new grid: a weight
/// (<c>'1.18x'</c>) or a size in pixels (<c>22</c>).
/// </summary>
/// <param name="Value">The weight (1.18 for <c>'1.18x'</c>) or the pixels, each rounded to two decimals as App Designer rounds them.</param>
/// <param name="IsWeight">Whether the track is a weight rather than pixels.</param>
public readonly record struct GridifyTrack(double Value, bool IsWeight)
{
    /// <summary>
    /// The weight as App Designer writes it, <c>1.18x</c>: the number as JavaScript prints it, then
    /// <c>x</c>. Empty for a track in pixels.
    /// </summary>
    public string WeightText => IsWeight ? DesignGridify.NumberText(Value) + "x" : string.Empty;

    /// <summary>The track as a grid's <c>RowHeight</c> or <c>ColumnWidth</c> holds it: the weight's char row, or the pixels.</summary>
    public DesignValue ToDesignValue() => IsWeight ? new CharValue(WeightText) : NumericValue.Scalar(Value);

    /// <inheritdoc/>
    public override string ToString() => IsWeight ? WeightText : DesignGridify.NumberText(Value);
}

/// <summary>Where a component goes in the new grid: its first and last row and column, from 1.</summary>
/// <param name="RowStart">The first row.</param>
/// <param name="RowEnd">The last row.</param>
/// <param name="ColumnStart">The first column.</param>
/// <param name="ColumnEnd">The last column.</param>
public sealed record GridifyCell(int RowStart, int RowEnd, int ColumnStart, int ColumnEnd)
{
    /// <summary>
    /// The component's <c>Layout.Row</c>: the row, or [first last] when it spans several
    /// (<c>Converters.componentGridLocationToGridLayout</c>).
    /// </summary>
    public NumericValue ToRowValue() => RowStart == RowEnd ? NumericValue.Scalar(RowStart) : NumericValue.Row(RowStart, RowEnd);

    /// <summary>The component's <c>Layout.Column</c>, as <see cref="ToRowValue"/>.</summary>
    public NumericValue ToColumnValue() =>
        ColumnStart == ColumnEnd ? NumericValue.Scalar(ColumnStart) : NumericValue.Row(ColumnStart, ColumnEnd);
}

/// <summary>What "Apply Grid Layout" sets on the new grid and its children (<c>AppDesignerGridifyResult</c>).</summary>
/// <param name="RowHeight">The grid's rows, top to bottom.</param>
/// <param name="ColumnWidth">The grid's columns, left to right.</param>
/// <param name="RowSpacing">The space between rows; not clamped, as App Designer does not clamp it.</param>
/// <param name="ColumnSpacing">The space between columns; not clamped.</param>
/// <param name="Padding">The grid's padding in MATLAB's order, [left bottom right top], none below 0.</param>
/// <param name="Cells">Each component's place in the grid, by its id; empty for an empty container.</param>
public sealed record GridifyResult(
    IReadOnlyList<GridifyTrack> RowHeight,
    IReadOnlyList<GridifyTrack> ColumnWidth,
    double RowSpacing,
    double ColumnSpacing,
    IReadOnlyList<double> Padding,
    IReadOnlyDictionary<string, GridifyCell> Cells);

/// <summary>
/// App Designer's "Apply Grid Layout" (designer plan DS4), transcribed from R2025b's
/// <c>appdesigner/gridify</c> modules: <c>AppDesignerGridifier</c> and the <c>Gridifier</c> under it,
/// which snap nearly aligned edges together (<c>ComponentDimensionAdjuster</c>), draw a line at every
/// edge (<c>GridLineFactory</c>), merge lines closer than a tolerance (<c>GridLineAdjuster</c>), make
/// a track between each two lines (<c>GridTrackFactory</c>), fit the tracks, the spacing and the
/// padding into the container (<c>GridTrackAdjuster</c>), place each component
/// (<c>ComponentGridLocationFactory</c>), and give each track a weight or pixels by whether a
/// component in it grows (<c>Converters</c>); and <c>ComponentAutoLayoutInfo.shouldComponentGrow</c>,
/// which says which components grow.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is App Designer's in its own order, so the result is its result to the bit: its
/// stable sorts, its ordered sets of component ids, the set objects its lines and tracks share, its
/// rounding (<c>Math.round</c>, ties upward), and its quirks - a loop that never runs, a shift that
/// counts twice, spacing left unclamped. Where a component hangs far over the container's edge App
/// Designer can give a row or column a size of 0 or less; that is returned as it is, for the caller
/// to decide on.
/// </para>
/// <para>
/// Ids are taken in the order given, the container's child order. App Designer keeps its components
/// in JavaScript objects keyed by id, which keep that order because its ids are never array indices.
/// </para>
/// </remarks>
public static class DesignGridify
{
    /// <summary>The nominal column width the empty-container rule fits columns of.</summary>
    public const double NominalColumnWidth = 200;

    /// <summary>The nominal row height the empty-container rule fits rows of.</summary>
    public const double NominalRowHeight = 50;

    /// <summary>
    /// The greatest tolerance for rows (<c>MAX_VERTICAL_SNAP_OFFSET</c>): how far apart two horizontal
    /// edges or lines may be and still snap or merge. The tolerance used is the shortest component's
    /// height less 1 when that is smaller, and never below 1.
    /// </summary>
    public const double MaxVerticalSnapOffset = 15;

    /// <summary>The greatest tolerance for columns (<c>MAX_HORIZONTAL_SNAP_OFFSET</c>), as <see cref="MaxVerticalSnapOffset"/> with widths.</summary>
    public const double MaxHorizontalSnapOffset = 25;

    /// <summary>The spacing a zero spacing is taken as (<c>GridifyParametersFactory</c>: <c>RowSpacing || 10</c>).</summary>
    public const double FallbackSpacing = 10;

    /// <summary>Whether the padding and spacing give way when the tracks overflow (<c>DYNAMIC_PADDING_SPACING</c>, always on).</summary>
    private const bool DynamicPaddingSpacing = true;

    /// <summary>
    /// The grid "Apply Grid Layout" makes of <paramref name="components"/> in a container of
    /// <paramref name="container"/> (<c>AppDesignerGridifier.gridify</c>): its rows and columns, the
    /// spacing and padding, and each component's cells.
    /// </summary>
    /// <param name="container">The container's content area in design pixels at zoom 1.</param>
    /// <param name="components">The components in view, in the container's child order. With none, the grid is the empty-container rule's: as many 200-by-50 tracks as fit, at least 2 by 2, all <c>'1x'</c>.</param>
    /// <param name="padding">The new grid's default padding, [left bottom right top] (a GridLayout's is [10 10 10 10]).</param>
    /// <param name="rowSpacing">The new grid's default row spacing (10).</param>
    /// <param name="columnSpacing">The new grid's default column spacing (10).</param>
    /// <param name="zoom">
    /// The canvas zoom. App Designer reads the boxes off the page, zoomed, but neither the padding nor
    /// the spacing nor its tolerances scale, so its result depends on the zoom; only the pixel sizes are
    /// divided by it again.
    /// </param>
    /// <exception cref="ArgumentException">An id is missing or repeated, or a size is not finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scene is one App Designer's own code fails on (a component ends up in no row or column, or
    /// no track is left); among 136,000 random scenes checked against App Designer's code, only those
    /// with a component of zero size or less than 2 pixels across did.
    /// </exception>
    public static GridifyResult Gridify(
        Size2D container, IReadOnlyList<GridifyComponent> components, IReadOnlyList<double> padding, double rowSpacing, double columnSpacing, double zoom = 1)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(padding);
        if (padding.Count != 4)
        {
            throw new ArgumentException("The padding is four numbers, [left bottom right top].", nameof(padding));
        }

        RequireFinite(nameof(container), container.Width, container.Height);
        RequireFinite(nameof(padding), padding[0], padding[1], padding[2], padding[3]);
        RequireFinite(nameof(rowSpacing), rowSpacing);
        RequireFinite(nameof(columnSpacing), columnSpacing);
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "The zoom is a positive ratio.");
        }

        // App Designer reads the container's and the components' boxes off the page, where the
        // canvas zoom has scaled them.
        double width = container.Width * zoom;
        double height = container.Height * zoom;
        if (components.Count == 0)
        {
            return EmptyContainer(width, height, padding, rowSpacing, columnSpacing, zoom);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var dimensions = new List<Dimension>(components.Count);
        foreach (GridifyComponent component in components)
        {
            ArgumentNullException.ThrowIfNull(component, nameof(components));
            if (string.IsNullOrEmpty(component.Id))
            {
                throw new ArgumentException("Every component needs an id.", nameof(components));
            }

            if (!ids.Add(component.Id))
            {
                throw new ArgumentException($"Two components have the id '{component.Id}'.", nameof(components));
            }

            Rect2D box = component.Box;
            RequireFinite(nameof(components), box.X, box.Y, box.Width, box.Height);
            dimensions.Add(Dimension.Of(component.Id, box, zoom));
        }

        // GridifyParametersFactory.createGridifyParametersByDefaultValues.
        double spacingRow = rowSpacing == 0 ? FallbackSpacing : rowSpacing;
        double spacingColumn = columnSpacing == 0 ? FallbackSpacing : columnSpacing;
        double paddingLeft = padding[0];
        double paddingBottom = padding[1];
        double paddingRight = padding[2];
        double paddingTop = padding[3];

        // Gridifier.gridify.
        double minHeight = double.PositiveInfinity;
        double minWidth = double.PositiveInfinity;
        foreach (Dimension d in dimensions)
        {
            minHeight = System.Math.Min(minHeight, d.Height);
            minWidth = System.Math.Min(minWidth, d.Width);
        }

        double vertical = System.Math.Max(1, System.Math.Min(MaxVerticalSnapOffset, minHeight - 1));
        double horizontal = System.Math.Max(1, System.Math.Min(MaxHorizontalSnapOffset, minWidth - 1));
        AdjustComponentDimensions(dimensions, horizontal, vertical);
        List<Line> verticalLines = BuildLines(dimensions, width, across: true);
        List<Line> horizontalLines = BuildLines(dimensions, height, across: false);
        verticalLines = AdjustLines(verticalLines, horizontal);
        horizontalLines = AdjustLines(horizontalLines, vertical);
        List<Track> rowTracks = BuildTracks(horizontalLines, vertical);
        List<Track> columnTracks = BuildTracks(verticalLines, horizontal);
        var heights = new Dictionary<string, double>(StringComparer.Ordinal);
        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Dimension d in dimensions)
        {
            heights[d.Id] = d.Height;
            widths[d.Id] = d.Width;
        }

        FittedTracks rows = AdjustTracks(heights, rowTracks, spacingRow, DynamicPaddingSpacing, paddingTop, paddingBottom, vertical, height);
        FittedTracks columns = AdjustTracks(widths, columnTracks, spacingColumn, DynamicPaddingSpacing, paddingLeft, paddingRight, horizontal, width);
        double[] fittedPadding =
        [
            NotBelowZero(columns.StartPadding),
            NotBelowZero(rows.EndPadding),
            NotBelowZero(columns.EndPadding),
            NotBelowZero(rows.StartPadding),
        ];
        Dictionary<string, Location> locations = BuildLocations(rows.Tracks, columns.Tracks);

        // AppDesignerGridifier.gridify, through Converters.
        GridifyTrack[] rowHeight = TrackSizes(rows.Tracks, components, locations, vertically: true, zoom);
        GridifyTrack[] columnWidth = TrackSizes(columns.Tracks, components, locations, vertically: false, zoom);
        var cells = new Dictionary<string, GridifyCell>(StringComparer.Ordinal);
        foreach (GridifyComponent component in components)
        {
            Location at = Located(locations, component.Id);
            cells[component.Id] = new GridifyCell(at.RowStart, at.RowEnd, at.ColumnStart, at.ColumnEnd);
        }

        return new GridifyResult(rowHeight, columnWidth, rows.Spacing, columns.Spacing, fittedPadding, cells);

        static double NotBelowZero(double value) => value < 0 ? 0 : value;
    }

    /// <summary>
    /// The empty-container rule (<c>AppDesignerGridifier</c>, with no components): as many columns of
    /// 200 pixels and rows of 50 as fit between the padding with the spacing between them, rounded, at
    /// least 2 each way, every one <c>'1x'</c>; the spacing and padding as given.
    /// </summary>
    private static GridifyResult EmptyContainer(double width, double height, IReadOnlyList<double> padding, double rowSpacing, double columnSpacing, double zoom)
    {
        double left = padding[0];
        double right = padding[2];
        double top = padding[3];
        double bottom = padding[1];
        double columns = ((width / zoom) - left - right + columnSpacing) / (NominalColumnWidth + columnSpacing);
        double rows = ((height / zoom) - top - bottom + rowSpacing) / (NominalRowHeight + rowSpacing);
        GridifyTrack[] columnWidth = Enumerable.Repeat(new GridifyTrack(1, true), Count(System.Math.Max(JsRound(columns), 2))).ToArray();
        GridifyTrack[] rowHeight = Enumerable.Repeat(new GridifyTrack(1, true), Count(System.Math.Max(JsRound(rows), 2))).ToArray();
        return new GridifyResult(rowHeight, columnWidth, rowSpacing, columnSpacing, padding.ToArray(), new Dictionary<string, GridifyCell>(StringComparer.Ordinal));

        // App Designer counts down from the number (for (M; M--;)): NaN makes no tracks.
        static int Count(double count) => double.IsNaN(count)
            ? 0
            : count <= 100_000
                ? (int)count
                : throw new ArgumentException("The spacing makes App Designer's empty-container rule count without end.");
    }

    // --- ComponentDimensionAdjuster ------------------------------------------------------------------

    /// <summary>
    /// A component's box as the gridifier works on it (<c>ComponentDimension</c>), relative to the
    /// container's content, in page pixels.
    /// </summary>
    private sealed class Dimension(string id)
    {
        public string Id { get; } = id;

        public double Top { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary><c>Converters.peerNodeToComponentDimension</c>: the box on the page, its far edges the near ones plus the size.</summary>
        public static Dimension Of(string id, Rect2D box, double zoom)
        {
            double left = box.X * zoom;
            double top = box.Y * zoom;
            double width = box.Width * zoom;
            double height = box.Height * zoom;
            return new Dimension(id) { Top = top, Left = left, Right = left + width, Bottom = top + height, Width = width, Height = height };
        }
    }

    /// <summary>One of the adjuster's four passes: the edge it snaps, its sort, and what a snap does.</summary>
    private sealed record Pass(Func<Dimension, double> Edge, Comparison<Dimension> Order, Action<Dimension, double> Adjust);

    private static readonly Pass LeftPass = new(
        static d => d.Left,
        static (a, b) => Sign(a.Left - b.Left),
        static (d, by) =>
        {
            d.Left -= by;
            d.Right -= by;
        });

    private static readonly Pass TopPass = new(
        static d => d.Top,
        static (a, b) => Sign(a.Top - b.Top),
        static (d, by) =>
        {
            d.Top -= by;
            d.Bottom -= by;
        });

    private static readonly Pass RightPass = new(
        static d => d.Right,
        static (a, b) => Sign(b.Right - a.Right),
        static (d, by) =>
        {
            d.Width += by;
            d.Right += by;
        });

    private static readonly Pass BottomPass = new(
        static d => d.Bottom,
        static (a, b) => Sign(b.Bottom - a.Bottom),
        static (d, by) =>
        {
            d.Bottom += by;
            d.Height += by;
        });

    /// <summary>
    /// <c>ComponentDimensionAdjuster.adjustComponentDimensions</c>: snaps nearly aligned left edges
    /// (shifting the later component), then tops, then right edges (growing the nearer one), then
    /// bottoms, each pass sorting <paramref name="dimensions"/> in place. Their last order, by bottom
    /// edge descending, is the order the lines are drawn in.
    /// </summary>
    private static void AdjustComponentDimensions(List<Dimension> dimensions, double horizontal, double vertical)
    {
        Snap(dimensions, horizontal, LeftPass);
        Snap(dimensions, vertical, TopPass);
        Snap(dimensions, horizontal, RightPass);
        Snap(dimensions, vertical, BottomPass);
    }

    /// <summary>
    /// One pass: sorted on the pass's edge, each neighbouring pair is a pair for the whole pass; while
    /// the closest pair whose edges differ is within <paramref name="tolerance"/> (the first such
    /// pair on a tie), the pair's later component is brought onto the earlier.
    /// </summary>
    /// <remarks>
    /// App Designer's code seems meant to move whole clusters of components snapped together, but it
    /// joins them in one union-find map and looks them up in another that is never joined, so every
    /// cluster is the one component and only the pair's later one moves.
    /// </remarks>
    private static void Snap(List<Dimension> dimensions, double tolerance, Pass pass)
    {
        StableSort(dimensions, pass.Order);
        var pairs = new List<(Dimension Start, Dimension End)>();
        double best = double.PositiveInfinity;
        int bestPair = -1;
        for (int y = 1; y < dimensions.Count; y++)
        {
            double gap = System.Math.Abs(pass.Edge(dimensions[y]) - pass.Edge(dimensions[y - 1]));
            pairs.Add((dimensions[y - 1], dimensions[y]));
            if (best > gap && gap != 0)
            {
                best = gap;
                bestPair = pairs.Count - 1;
            }
        }

        while (best <= tolerance)
        {
            pass.Adjust(pairs[bestPair].End, best);
            best = double.PositiveInfinity;
            bestPair = -1;
            for (int i = 0; i < pairs.Count; i++)
            {
                double gap = System.Math.Abs(pass.Edge(pairs[i].End) - pass.Edge(pairs[i].Start));
                if (gap < best && gap != 0)
                {
                    best = gap;
                    bestPair = i;
                }
            }
        }
    }

    // --- GridLine, GridLineFactory, GridLineAdjuster -------------------------------------------------

    /// <summary>A JavaScript <c>Set</c> of component ids: ordered as they were first added.</summary>
    private sealed class Ids
    {
        private readonly List<string> _items = [];
        private readonly HashSet<string> _set = new(StringComparer.Ordinal);

        public int Count => _items.Count;

        public IReadOnlyList<string> Items => _items;

        public void Add(string id)
        {
            if (_set.Add(id))
            {
                _items.Add(id);
            }
        }

        public bool Contains(string id) => _set.Contains(id);
    }

    /// <summary>
    /// A candidate track boundary (<c>GridLine</c>): where it stands, the components whose far edge is
    /// on it (<c>componentsBefore</c>) and those whose near edge is (<c>componentsAfter</c>).
    /// </summary>
    private sealed class Line(double position)
    {
        public double Position { get; set; } = position;

        public Ids Before { get; } = new();

        /// <summary>Settable: a collapsed gap gives the line the next line's set itself.</summary>
        public Ids After { get; set; } = new();

        /// <summary>Whether a component starts on the earlier of the two lines and ends exactly on the later.</summary>
        public bool HasComponentsBetween(Line other) =>
            Position - other.Position > 0 ? other.HasComponentsBetween(this) : After.Items.Any(other.Before.Contains);

        /// <summary>Adds <paramref name="other"/>'s ids to this line's, in their order.</summary>
        public void Merge(Line other)
        {
            foreach (string id in other.Before.Items.ToArray())
            {
                Before.Add(id);
            }

            foreach (string id in other.After.Items.ToArray())
            {
                After.Add(id);
            }
        }
    }

    private static int ByPosition(Line a, Line b) => Sign(a.Position - b.Position);

    /// <summary>
    /// <c>GridLineFactory.buildVerticalLines</c> (<paramref name="across"/>: left and right edges) and
    /// <c>buildHorizontalLines</c> (top and bottom): a line at 0, one at the container's extent, and one
    /// at each edge of each component, those at one position merged into the first made there, sorted.
    /// </summary>
    private static List<Line> BuildLines(List<Dimension> dimensions, double extent, bool across)
    {
        var made = new List<Line> { new(0), new(extent) };
        foreach (Dimension d in dimensions)
        {
            var start = new Line(across ? d.Left : d.Top);
            var end = new Line(across ? d.Right : d.Bottom);
            start.After.Add(d.Id);
            end.Before.Add(d.Id);
            made.Add(start);
            made.Add(end);
        }

        // Keyed by position as a JavaScript object keys it, where -0 and 0 are one key.
        var byPosition = new Dictionary<double, Line>();
        var lines = new List<Line>();
        foreach (Line line in made)
        {
            double key = line.Position == 0 ? 0 : line.Position;
            if (byPosition.TryGetValue(key, out Line? first))
            {
                foreach (string id in line.Before.Items)
                {
                    first.Before.Add(id);
                }

                foreach (string id in line.After.Items)
                {
                    first.After.Add(id);
                }
            }
            else
            {
                byPosition[key] = line;
                lines.Add(line);
            }
        }

        StableSort(lines, ByPosition);
        return lines;
    }

    /// <summary>
    /// <c>GridLineAdjuster.adjustLines</c>: walks the lines in order and merges each that stands within
    /// <paramref name="tolerance"/> of the current one. A gap between components ending and others
    /// starting is cut out and everything after moves back; starts close together snap back onto the
    /// first; ends close together stretch onto the last; a component spanning exactly the two lines
    /// keeps both.
    /// </summary>
    /// <remarks>
    /// Transcribed as written: it edits the list it walks, re-sorts the whole list after a merge, and
    /// leaves merged lines in the list to be sorted with the rest.
    /// </remarks>
    private static List<Line> AdjustLines(List<Line> lines, double tolerance)
    {
        if (lines.Count == 1)
        {
            return lines;
        }

        StableSort(lines, ByPosition);
        var kept = new List<Line>();
        Line current = lines[0];
        for (int e = 1; e < lines.Count;)
        {
            double gap = lines[e].Position - current.Position;
            if (gap <= tolerance)
            {
                if (current.After.Count == 0 && lines[e].Before.Count == 0)
                {
                    // Nothing starts on the current line and nothing ends on the next: the gap goes.
                    current.After = lines[e].After;
                    lines.RemoveAt(e);
                    for (int k = e; k < lines.Count; k++)
                    {
                        lines[k].Position -= gap;
                    }
                }
                else if (current.HasComponentsBetween(lines[e]))
                {
                    kept.Add(current);
                    current = lines[e++];
                }
                else
                {
                    if (lines[e].Before.Count == 0)
                    {
                        // The next line's starters start on the current one, keeping their sizes.
                        current.Merge(lines[e]);
                        ShiftEndLines(lines[e].After, lines, e + 1, -gap);
                        e++;
                    }
                    else
                    {
                        // The current line's enders stretch to the next one, its starters move with it.
                        lines[e].Merge(current);
                        ShiftEndLines(current.After, lines, e + 1, gap);
                        current = lines[e++];
                    }

                    StableSort(lines, ByPosition);
                }
            }
            else
            {
                kept.Add(current);
                current = lines[e++];
            }
        }

        kept.Add(current);
        return kept;
    }

    /// <summary>
    /// Moves, once each, the first line from <paramref name="from"/> on where each of
    /// <paramref name="ids"/> ends, by <paramref name="delta"/>.
    /// </summary>
    private static void ShiftEndLines(Ids ids, List<Line> lines, int from, double delta)
    {
        var hit = new List<Line>();
        foreach (string id in ids.Items)
        {
            for (int n = from; n < lines.Count; n++)
            {
                if (lines[n].Before.Contains(id))
                {
                    if (!hit.Contains(lines[n]))
                    {
                        hit.Add(lines[n]);
                    }

                    break;
                }
            }
        }

        foreach (Line line in hit)
        {
            line.Position += delta;
        }
    }

    // --- GridTrack, GridTrackFactory -----------------------------------------------------------------

    /// <summary>
    /// The space between two lines (<c>GridTrack</c>), holding the lines' own id sets: those ending
    /// and starting at its start and at its end.
    /// </summary>
    private sealed class Track(Line start, Line end)
    {
        public double Start { get; set; } = start.Position;

        public double End { get; set; } = end.Position;

        public double Size { get; set; } = end.Position - start.Position;

        public Ids BeforeStart { get; } = start.Before;

        public Ids AfterStart { get; } = start.After;

        public Ids BeforeEnd { get; } = end.Before;

        public Ids AfterEnd { get; } = end.After;

        public bool IsEmpty { get; set; }

        public bool Resizable { get; set; } = true;

        /// <summary>
        /// <c>getSmallComponent</c>: the first component, in the order its start was recorded, that
        /// fills exactly this track; null when none does.
        /// </summary>
        public string? SmallComponent()
        {
            foreach (string id in AfterStart.Items)
            {
                if (BeforeEnd.Contains(id))
                {
                    return id;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// <c>GridTrackFactory.buildGridTracks</c>: a track between each two lines, empty where no
    /// component is open across it, and the empty ones within <paramref name="tolerance"/> dropped.
    /// </summary>
    /// <remarks>
    /// The drop shifts the tracks after it by the next track's start before that start is shifted,
    /// so after a first drop it counts the shift again; transcribed so.
    /// </remarks>
    private static List<Track> BuildTracks(List<Line> lines, double tolerance)
    {
        var tracks = new List<Track>();
        for (int n = 1; n < lines.Count; n++)
        {
            tracks.Add(new Track(lines[n - 1], lines[n]));
        }

        int open = 0;
        foreach (Track track in tracks)
        {
            open += track.AfterStart.Count;
            if (open == 0)
            {
                track.IsEmpty = true;
                continue;
            }

            open -= track.BeforeEnd.Count;
        }

        double shift = 0;
        bool[] drop = new bool[tracks.Count];
        for (int n = 0; n < tracks.Count; n++)
        {
            tracks[n].Start -= shift;
            tracks[n].End -= shift;
            if (tracks[n].Size <= tolerance && tracks[n].IsEmpty)
            {
                drop[n] = true;
                if (n == tracks.Count - 1)
                {
                    break;
                }

                shift += tracks[n + 1].Start - tracks[n].Start;
            }
        }

        return tracks.Where((_, k) => !drop[k]).ToList();
    }

    // --- GridTrackAdjuster ---------------------------------------------------------------------------

    /// <summary>The tracks of one direction fitted to the container, with the padding and spacing they leave.</summary>
    private sealed record FittedTracks(List<Track> Tracks, double StartPadding, double EndPadding, double Spacing);

    /// <summary>
    /// <c>GridTrackAdjuster.adjustGridTracks</c>: each track that one component fills exactly takes
    /// that component's size, the spacing opens between the tracks and the leading padding before
    /// them; then whatever runs past the container's far padding is taken out of the empty edge tracks,
    /// then the empty tracks, then (the dynamic step) the padding and spacing, then the filled tracks.
    /// A single track is centred instead.
    /// </summary>
    private static FittedTracks AdjustTracks(
        Dictionary<string, double> sizeById, List<Track> tracks, double spacing, bool dynamicPaddingSpacing, double startPadding, double endPadding, double tolerance, double extent)
    {
        if (tracks.Count == 1)
        {
            return SingleTrack(tracks[0], extent, startPadding, endPadding, spacing, tolerance);
        }

        if (tracks.Count == 0)
        {
            throw Fails("the lines leave no track");
        }

        FitToComponents(sizeById, tracks);
        OpenSpacing(tracks, spacing);
        OpenPadding(tracks, startPadding);
        double over = tracks[^1].End - (extent - endPadding);
        if (over > 0)
        {
            (tracks, over) = TrimEmptyTracks(tracks, over, tolerance, spacing);
            (tracks, over) = ShrinkEmptyTracks(tracks, over);
            if (dynamicPaddingSpacing)
            {
                (tracks, over, startPadding, endPadding, spacing) = ShrinkPaddingAndSpacing(tracks, over, startPadding, endPadding, spacing);
            }

            (tracks, _) = ShrinkFilledTracks(tracks, over);
        }

        return new FittedTracks(tracks, startPadding, endPadding, spacing);
    }

    /// <summary>
    /// A lone track: the padding is half of what it leaves of the container, reckoned before an empty
    /// track is resized to the container less the padding, but not below the tolerance.
    /// </summary>
    private static FittedTracks SingleTrack(Track track, double extent, double startPadding, double endPadding, double spacing, double tolerance)
    {
        double padding = (extent - track.Size) / 2;
        if (track.IsEmpty)
        {
            track.Size = System.Math.Min(extent, System.Math.Max(track.Size - startPadding - endPadding, tolerance));
        }

        return new FittedTracks([track], padding, padding, spacing);
    }

    /// <summary>
    /// <c>a()</c>: a track one component fills exactly takes back that component's own size, which the
    /// line merges may have stretched, and the tracks after it move with it.
    /// </summary>
    private static void FitToComponents(Dictionary<string, double> sizeById, List<Track> tracks)
    {
        double offset = 0;
        foreach (Track track in tracks)
        {
            track.Start += offset;
            track.End += offset;
            string? id = track.SmallComponent();
            if (id is not null)
            {
                double delta = sizeById[id] - track.Size;
                track.Size += delta;
                track.End += delta;
                offset += delta;
            }
        }
    }

    /// <summary><c>E()</c>: the spacing between each two tracks, counted up by addition.</summary>
    private static void OpenSpacing(List<Track> tracks, double spacing)
    {
        if (tracks.Count == 1)
        {
            return;
        }

        double by = spacing;
        for (int a = 1; a < tracks.Count; a++)
        {
            tracks[a].Start += by;
            tracks[a].End += by;
            by += spacing;
        }
    }

    /// <summary><c>p()</c>: the leading padding before every track.</summary>
    private static void OpenPadding(List<Track> tracks, double padding)
    {
        foreach (Track track in tracks)
        {
            track.Start += padding;
            track.End += padding;
        }
    }

    /// <summary>The empty tracks and where they stand.</summary>
    private static List<(Track Track, int Index)> EmptyTracks(List<Track> tracks)
    {
        var empty = new List<(Track Track, int Index)>();
        for (int c = 0; c < tracks.Count; c++)
        {
            if (tracks[c].IsEmpty)
            {
                empty.Add((tracks[c], c));
            }
        }

        return empty;
    }

    /// <summary>
    /// <c>i()</c>: takes <paramref name="over"/> out of the empty edge tracks - both at once down to the
    /// tolerance when both are empty, then the last (dropped with its spacing if that is not enough),
    /// then the first (likewise) - and then out of the empty tracks, dropping the smallest first.
    /// What is left over is returned; it can be negative.
    /// </summary>
    /// <remarks>
    /// Between the last and the first edge track App Designer's code has a loop meant to shrink the
    /// empty tracks in the middle, <c>for (F = 1; F &lt; u; F++)</c>, where <c>u</c> is the array of
    /// tracks rather than its length: <c>1 &lt; NaN</c> is false and the loop never runs. Only the
    /// clamp after it does, and so here.
    /// </remarks>
    private static (List<Track> Tracks, double Remaining) TrimEmptyTracks(List<Track> tracks, double over, double tolerance, double spacing)
    {
        Track first = tracks[0];
        Track last = tracks[^1];
        double taken;
        if (over > 0 && last.IsEmpty && first.IsEmpty)
        {
            double both = System.Math.Min(over, (System.Math.Min(last.Size, first.Size) - tolerance) * 2);
            double each = both / 2;
            last.Size -= each;
            last.End -= each;
            first.Size -= each;
            first.End -= each;
            for (int f = 1; f < tracks.Count; f++)
            {
                tracks[f].Start -= each;
                tracks[f].End -= each;
            }

            over -= both;
        }

        if (over > 0 && last.IsEmpty)
        {
            taken = System.Math.Min(last.Size - tolerance, over);
            over -= taken;
            last.Size -= taken;
            last.End -= taken;
            if (over > 0)
            {
                tracks.RemoveAt(tracks.Count - 1);
                over -= tolerance + spacing;
                over = System.Math.Max(0, over);
            }
        }

        if (over > 0)
        {
            // The middle empty tracks' loop, which never runs (see the remarks).
            over = System.Math.Max(0, over);
        }

        if (over > 0)
        {
            if (first.IsEmpty)
            {
                taken = System.Math.Min(first.Size - tolerance, over);
                over -= taken;
                first.Size -= taken;
                first.End -= taken;
                for (int b = 1; b < tracks.Count; b++)
                {
                    tracks[b].Start -= taken;
                    tracks[b].End -= taken;
                }

                if (over > 0)
                {
                    over -= tolerance + spacing;
                    over = System.Math.Max(0, over);
                    if (tracks.Count < 2)
                    {
                        // The last track was dropped above and the first is all that is left: App
                        // Designer reads u[1].start of the one-track list and fails.
                        throw Fails("the first empty track is dropped with no track after it");
                    }

                    taken = tracks[1].Start - spacing;
                    for (int b = 1; b < tracks.Count; b++)
                    {
                        tracks[b].Start -= taken;
                        tracks[b].End -= taken;
                    }

                    tracks.RemoveAt(0);
                }
            }

            over = System.Math.Max(0, over);
        }

        if (over == 0)
        {
            return (tracks, 0);
        }

        List<(Track Track, int Index)> empty = EmptyTracks(tracks);
        var remove = new List<int>();
        double gained = 0;
        double floor = 0;
        double total = 0;
        foreach ((Track Track, int Index) entry in empty)
        {
            total += entry.Track.Size;
            total += spacing;
        }

        if (total < over)
        {
            foreach ((Track Track, int Index) entry in empty)
            {
                remove.Add(entry.Index);
            }

            over -= total;
        }
        else
        {
            StableSort(empty, static (a, b) => Sign(a.Track.Size - b.Track.Size));
            for (int b = 0; b < empty.Count; b++)
            {
                int left = empty.Count - b;
                gained += (empty[b].Track.Size - floor - tolerance) * left;
                floor = empty[b].Track.Size - tolerance;
                if (gained >= over)
                {
                    break;
                }

                remove.Add(empty[b].Index);
                gained += tolerance + spacing;
                if (gained >= over)
                {
                    break;
                }
            }

            foreach (int index in remove)
            {
                over -= tracks[index].Size;
                over -= spacing;
            }
        }

        List<Track> result = tracks;
        double shift = 0;
        if (remove.Count > 0)
        {
            result = [];
            for (int b = 0; b < tracks.Count; b++)
            {
                tracks[b].Start -= shift;
                tracks[b].End -= shift;
                if (remove.Contains(b))
                {
                    if (b == tracks.Count - 1)
                    {
                        break;
                    }

                    shift = tracks[b + 1].Start - tracks[b].Start;
                }
                else
                {
                    result.Add(tracks[b]);
                }
            }
        }

        return (result, over);
    }

    /// <summary>
    /// <c>e()</c>: shares <paramref name="over"/> equally among the empty tracks left, which a
    /// negative <paramref name="over"/> grows; nothing is left over unless there are none.
    /// </summary>
    private static (List<Track> Tracks, double Remaining) ShrinkEmptyTracks(List<Track> tracks, double over)
    {
        List<(Track Track, int Index)> empty = EmptyTracks(tracks);
        if (empty.Count == 0)
        {
            return (tracks, over);
        }

        double each = over / empty.Count;
        int passed = 0;
        if (over != 0)
        {
            foreach (Track track in tracks)
            {
                track.Start -= passed * each;
                track.End -= passed * each;
                if (track.IsEmpty)
                {
                    track.End -= each;
                    track.Size -= each;
                    passed++;
                }
            }
        }

        return (tracks, 0);
    }

    /// <summary>
    /// <c>n()</c>, the dynamic padding and spacing: takes up to all of the two paddings and the
    /// spacings between the filled tracks, in equal parts from each of those m + 1 gaps.
    /// </summary>
    private static (List<Track> Tracks, double Remaining, double StartPadding, double EndPadding, double Spacing) ShrinkPaddingAndSpacing(
        List<Track> tracks, double over, double startPadding, double endPadding, double spacing)
    {
        int filled = tracks.Count - EmptyTracks(tracks).Count;
        double room = ((filled - 1) * spacing) + startPadding + endPadding;
        double taken = System.Math.Min(room, over);
        double each = taken / (filled + 1);
        startPadding -= each;
        endPadding -= each;
        spacing -= each;
        if (each != 0)
        {
            for (int b = 0; b < tracks.Count; b++)
            {
                tracks[b].Start -= each * (b + 1);
                tracks[b].End -= each * (b + 1);
            }
        }

        return (tracks, over - taken, startPadding, endPadding, spacing);
    }

    /// <summary><c>r()</c>: shares what is still over equally among the filled tracks.</summary>
    private static (List<Track> Tracks, double Remaining) ShrinkFilledTracks(List<Track> tracks, double over)
    {
        int filled = tracks.Count - EmptyTracks(tracks).Count;
        if (over == 0 || filled == 0)
        {
            return (tracks, over);
        }

        double each = over / filled;
        int passed = 0;
        if (each != 0)
        {
            foreach (Track track in tracks)
            {
                track.Start -= passed * each;
                track.End -= passed * each;
                if (!track.IsEmpty)
                {
                    track.End -= each;
                    track.Size -= each;
                    passed++;
                }
            }
        }

        return (tracks, 0);
    }

    // --- ComponentGridLocationFactory, Converters ----------------------------------------------------

    /// <summary>A component's rows and columns as the factory finds them, -1 until found.</summary>
    private sealed class Location
    {
        public int RowStart { get; set; } = -1;

        public int RowEnd { get; set; } = -1;

        public int ColumnStart { get; set; } = -1;

        public int ColumnEnd { get; set; } = -1;
    }

    /// <summary>
    /// <c>ComponentGridLocationFactory.buildComponentGridLocationMap</c>: a component's first row is
    /// the track its top starts, its last the track its bottom ends, and so its columns.
    /// </summary>
    private static Dictionary<string, Location> BuildLocations(List<Track> rows, List<Track> columns)
    {
        var locations = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (Track row in rows)
        {
            foreach (string id in row.AfterStart.Items)
            {
                locations[id] = new Location();
            }
        }

        for (int s = 0; s < rows.Count; s++)
        {
            foreach (string id in rows[s].AfterStart.Items)
            {
                Located(locations, id).RowStart = s + 1;
            }

            foreach (string id in rows[s].BeforeEnd.Items)
            {
                Located(locations, id).RowEnd = s + 1;
            }
        }

        for (int s = 0; s < columns.Count; s++)
        {
            foreach (string id in columns[s].AfterStart.Items)
            {
                Located(locations, id).ColumnStart = s + 1;
            }

            foreach (string id in columns[s].BeforeEnd.Items)
            {
                Located(locations, id).ColumnEnd = s + 1;
            }
        }

        return locations;
    }

    private static Location Located(Dictionary<string, Location> locations, string id) =>
        locations.TryGetValue(id, out Location? at) ? at : throw Fails($"'{id}' starts no row");

    /// <summary>
    /// <c>Converters.gridTracksToRowHeight</c> (<paramref name="vertically"/>) and
    /// <c>gridTracksToColumnWidth</c>: a track any component that does not grow that way lies in, even
    /// in part, is in pixels; the rest, every empty one among them, are weights relative to the
    /// smallest of them, which is <c>'1x'</c>. Each is rounded to two decimals; only pixels are divided
    /// by the zoom.
    /// </summary>
    private static GridifyTrack[] TrackSizes(
        List<Track> tracks, IReadOnlyList<GridifyComponent> components, Dictionary<string, Location> locations, bool vertically, double zoom)
    {
        foreach (GridifyComponent component in components)
        {
            if (vertically ? component.GrowsVertically : component.GrowsHorizontally)
            {
                continue;
            }

            Location at = Located(locations, component.Id);
            int start = vertically ? at.RowStart : at.ColumnStart;
            int end = vertically ? at.RowEnd : at.ColumnEnd;
            for (int b = start; b <= end; b++)
            {
                if (b < 1 || b > tracks.Count)
                {
                    throw Fails($"'{component.Id}' is placed in track {b} of {tracks.Count}");
                }

                tracks[b - 1].Resizable = false;
            }
        }

        double smallest = double.PositiveInfinity;
        foreach (Track track in tracks)
        {
            if (track.Resizable)
            {
                smallest = System.Math.Min(smallest, track.Size);
            }
        }

        var sizes = new GridifyTrack[tracks.Count];
        for (int i = 0; i < tracks.Count; i++)
        {
            Track track = tracks[i];
            if (track.Resizable)
            {
                double weight = track.Size / smallest;
                weight = JsRound(weight * 100) / 100;
                sizes[i] = new GridifyTrack(weight, true);
            }
            else
            {
                sizes[i] = new GridifyTrack(JsRound(track.Size * 100 / zoom) / 100, false);
            }
        }

        return sizes;
    }

    // --- ComponentAutoLayoutInfo.shouldComponentGrow -------------------------------------------------

    /// <summary>
    /// The classes <c>ComponentAutoLayoutInfo.isContainer</c> names, lowercased as it compares them.
    /// Its list also has <c>matlab.ui.container.internal.JavaWrapper</c>, which never matches a
    /// lowercased class.
    /// </summary>
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "matlab.ui.container.buttongroup",
        "matlab.ui.container.legacybuttongroup",
        "matlab.ui.container.panel",
        "matlab.ui.container.legacypanel",
        "matlab.ui.container.uicontrolpanel",
        "matlab.ui.container.tabgroup",
        "matlab.ui.container.legacytabgroup",
        "matlab.ui.container.tab",
        "matlab.ui.container.legacytab",
        "matlab.ui.container.internal.uicontainer",
        "matlab.ui.container.internal.legacyuicontainer",
    };

    /// <summary>
    /// Whether <paramref name="component"/> grows across and up and down in a grid
    /// (<c>ComponentAutoLayoutInfo.shouldComponentGrow</c>): a list box, a table, an axes both ways,
    /// an edit field or a drop-down across only, a button neither; a slider or linear gauge along its
    /// orientation (a range slider never up and down, being missing from App Designer's vertical
    /// table); a grid where any of its columns, or rows, is a weight; a panel, tab, tab group or button
    /// group where any of the components inside it does, an empty one not at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container's children count when their place is at all inside it: a <c>Position</c> [x y w h]
    /// with x - 1 + w and y - 1 + h both at least 0. A child with no <c>Position</c> of its own (a tab,
    /// a grid) fills its container and counts; App Designer's controllers for those are composed at run
    /// time, which its JavaScript does not show.
    /// </para>
    /// <para>
    /// Its table also names <c>matlab.ui.scope.TimeScope</c> (growing both ways), which never matches
    /// a lowercased class, so a time scope does not grow.
    /// </para>
    /// </remarks>
    public static (bool Horizontal, bool Vertical) Growth(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return (ShouldGrow(component, horizontally: true), ShouldGrow(component, horizontally: false));
    }

    private static bool ShouldGrow(DesignComponent component, bool horizontally)
    {
        string type = component.Type.ToLowerInvariant();
        if (Containers.Contains(type))
        {
            foreach (DesignComponent child in component.Children)
            {
                if (IsRepositionable(child) && ShouldGrow(child, horizontally))
                {
                    return true;
                }
            }

            return false;
        }

        return horizontally ? GrowsHorizontally(type, component) : GrowsVertically(type, component);
    }

    /// <summary>App Designer's horizontal table, case by case.</summary>
    private static bool GrowsHorizontally(string type, DesignComponent component) => type switch
    {
        "matlab.ui.control.button" => false,
        "matlab.ui.control.checkbox" => false,
        "matlab.ui.control.discreteknob" => true,
        "matlab.ui.control.dropdown" or "matlab.ui.control.datepicker" => true,
        "matlab.ui.control.editfield" => true,
        "matlab.ui.control.gauge" => true,
        "matlab.ui.control.html" => true,
        "matlab.ui.control.image" => false,
        "matlab.ui.control.knob" => true,
        "matlab.ui.control.label" or "matlab.ui.control.hyperlink" => false,
        "matlab.ui.control.lamp" => false,
        "matlab.ui.control.lineargauge" => IsHorizontal(component),
        "matlab.ui.control.listbox" => true,
        "matlab.ui.control.ninetydegreegauge" => true,
        "matlab.ui.control.numericeditfield" => true,
        "matlab.ui.control.radiobutton" => false,
        "matlab.ui.control.rockerswitch" => false,
        "matlab.ui.control.semicirculargauge" => true,
        "matlab.ui.control.slider" or "matlab.ui.control.rangeslider" => IsHorizontal(component),
        "matlab.ui.control.spinner" => true,
        "matlab.ui.control.statebutton" => false,
        "matlab.ui.control.switch" => false,
        "matlab.ui.control.table" or "matlab.ui.control.legacytable" => true,
        "matlab.ui.control.textarea" => true,
        "matlab.ui.control.togglebutton" => false,
        "matlab.ui.control.toggleswitch" => false,
        "matlab.ui.container.tree" or "matlab.ui.container.checkboxtree" => true,
        "matlab.ui.control.uiaxes" => true,
        "aero.ui.control.airspeedindicator" => true,
        "aero.ui.control.altimeter" => true,
        "aero.ui.control.artificialhorizon" => true,
        "aero.ui.control.climbindicator" => true,
        "aero.ui.control.egtindicator" => true,
        "aero.ui.control.headingindicator" => true,
        "aero.ui.control.rpmindicator" => true,
        "aero.ui.control.turncoordinator" => true,
        "matlab.ui.control.internal.progressindicator" => true,
        "matlab.ui.control.internal.circularprogressindicator" => true,
        "matlab.ui.container.gridlayout" => HasWeight(component, "ColumnWidth"),
        "matlab.ui.control.colorpicker" => false,
        "matlab.ui.control.internal.editor" => true,
        "matlab.ui.control.internal.variableeditor" => true,
        "matlab.ui.control.internal.workspacebrowser" => true,
        "matlab.ui.control.internal.inspector" => true,
        "matlab.ui.container.internal.popout" => false,
        _ => false,
    };

    /// <summary>App Designer's vertical table, case by case; it has no range slider and no workspace browser.</summary>
    private static bool GrowsVertically(string type, DesignComponent component) => type switch
    {
        "matlab.ui.control.button" => false,
        "matlab.ui.control.checkbox" => false,
        "matlab.ui.control.discreteknob" => true,
        "matlab.ui.control.dropdown" or "matlab.ui.control.datepicker" => false,
        "matlab.ui.control.editfield" => false,
        "matlab.ui.control.gauge" => true,
        "matlab.ui.control.html" => true,
        "matlab.ui.control.image" => false,
        "matlab.ui.control.knob" => true,
        "matlab.ui.control.label" or "matlab.ui.control.hyperlink" => false,
        "matlab.ui.control.lamp" => false,
        "matlab.ui.control.lineargauge" => !IsHorizontal(component),
        "matlab.ui.control.listbox" => true,
        "matlab.ui.control.ninetydegreegauge" => true,
        "matlab.ui.control.numericeditfield" => false,
        "matlab.ui.control.radiobutton" => false,
        "matlab.ui.control.rockerswitch" => false,
        "matlab.ui.control.semicirculargauge" => true,
        "matlab.ui.control.slider" => !IsHorizontal(component),
        "matlab.ui.control.spinner" => false,
        "matlab.ui.control.statebutton" => false,
        "matlab.ui.control.switch" => false,
        "matlab.ui.control.table" or "matlab.ui.control.legacytable" => true,
        "matlab.ui.control.textarea" => true,
        "matlab.ui.control.togglebutton" => false,
        "matlab.ui.control.toggleswitch" => false,
        "matlab.ui.container.tree" or "matlab.ui.container.checkboxtree" => true,
        "matlab.ui.control.uiaxes" => true,
        "aero.ui.control.airspeedindicator" => true,
        "aero.ui.control.altimeter" => true,
        "aero.ui.control.artificialhorizon" => true,
        "aero.ui.control.climbindicator" => true,
        "aero.ui.control.egtindicator" => true,
        "aero.ui.control.headingindicator" => true,
        "aero.ui.control.rpmindicator" => true,
        "aero.ui.control.turncoordinator" => true,
        "matlab.ui.control.internal.progressindicator" => false,
        "matlab.ui.control.internal.circularprogressindicator" => true,
        "matlab.ui.container.gridlayout" => HasWeight(component, "RowHeight"),
        "matlab.ui.control.colorpicker" => false,
        "matlab.ui.control.internal.editor" => true,
        "matlab.ui.control.internal.variableeditor" => true,
        "matlab.ui.control.internal.inspector" => true,
        "matlab.ui.control.internal.popout" => false,
        _ => false,
    };

    /// <summary>Whether the component's <c>Orientation</c>, held or its class's default, is horizontal, in any case.</summary>
    private static bool IsHorizontal(DesignComponent component)
    {
        string orientation = Text(component.Get("Orientation"))
            ?? Text(ClassOf(component)?.Property("Orientation")?.DefaultFor(DesignTheme.Unthemed))
            ?? "horizontal"; // every class asked about has a recorded default
        return string.Equals(orientation.ToLowerInvariant(), "horizontal", StringComparison.Ordinal);

        static string? Text(DesignValue? value) => value switch
        {
            CharValue c => c.Text,
            StringValue s => s.Text,
            _ => null,
        };
    }

    /// <summary>
    /// The grid widget's <c>containsNxColumns</c> and <c>containsNxRows</c>: whether any entry of the
    /// grid's <paramref name="property"/>, held or the class's default (<c>{'1x', '1x'}</c>), is text
    /// ending in <c>x</c>.
    /// </summary>
    private static bool HasWeight(DesignComponent grid, string property)
    {
        DesignValue? value = grid.Get(property) ?? ClassOf(grid)?.Property(property)?.DefaultFor(DesignTheme.Unthemed);
        return value switch
        {
            CellValue cell => cell.Items.Any(IsWeight),
            _ => value is not null && IsWeight(value),
        };

        static bool IsWeight(DesignValue item) => item switch
        {
            CharValue c => c.Text.EndsWith('x'),
            StringValue s => s.Text.EndsWith('x'),
            _ => false,
        };
    }

    /// <summary>
    /// <c>_isChildRepositionable</c>: whether any of the child's place is inside its container, its
    /// zero-origin outer position [x y w h] having x + w and y + h at least 0.
    /// </summary>
    private static bool IsRepositionable(DesignComponent child)
    {
        DesignValue? position = child.Get("Position") is NumericValue { Data.Count: 4 } held
            ? held
            : ClassOf(child)?.Property("Position")?.DefaultFor(DesignTheme.Unthemed);
        if (position is not NumericValue { Data: [double x, double y, double w, double h] })
        {
            return true; // no place of its own (a tab, a grid): it fills its container
        }

        return (x - 1) + w >= 0 && (y - 1) + h >= 0;
    }

    private static DesignComponentClass? ClassOf(DesignComponent component) => component.Class ?? DesignCatalog.R2025b.ByType(component.Type);

    // --- JavaScript's numbers ------------------------------------------------------------------------

    /// <summary>
    /// JavaScript's <c>Math.round</c> as App Designer's rounding needs it: the nearest integer, a half
    /// going up (2.5 to 3, -2.5 to -2), unlike <see cref="System.Math.Round(double)"/>.
    /// </summary>
    internal static double JsRound(double x)
    {
        double floor = System.Math.Floor(x);
        return x - floor >= 0.5 ? floor + 1 : floor;
    }

    /// <summary>
    /// A number as JavaScript prints it (<c>Number.prototype.toString</c>): the shortest digits that
    /// read back as the number, in plain notation from 1e-6 up to 1e21 and exponent notation outside;
    /// 0 for either zero.
    /// </summary>
    internal static string NumberText(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value == 0)
        {
            return "0";
        }

        // .NET's round-trip form has the same shortest digits; lay them out as JavaScript does.
        string text = System.Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int e = text.IndexOf('E', StringComparison.Ordinal);
        int exponent = e < 0 ? 0 : int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = e < 0 ? text : text[..e];
        int dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        string digits = dot < 0 ? mantissa : string.Concat(mantissa.AsSpan(0, dot), mantissa.AsSpan(dot + 1));
        int point = (dot < 0 ? mantissa.Length : dot) + exponent;
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0')
        {
            lead++;
        }

        digits = digits[lead..].TrimEnd('0');
        point -= lead;
        int k = digits.Length;
        string body;
        if (k <= point && point <= 21)
        {
            body = digits + new string('0', point - k);
        }
        else if (point > 0 && point <= 21)
        {
            body = digits[..point] + "." + digits[point..];
        }
        else if (point > -6 && point <= 0)
        {
            body = "0." + new string('0', -point) + digits;
        }
        else
        {
            int power = point - 1;
            body = (k == 1 ? digits : digits[..1] + "." + digits[1..])
                + "e" + (power < 0 ? "-" : "+") + System.Math.Abs(power).ToString(CultureInfo.InvariantCulture);
        }

        return value < 0 ? "-" + body : body;
    }

    // --- helpers ---------------------------------------------------------------------------------------

    /// <summary>A comparator's result as JavaScript's sort reads it: its sign, NaN as equal.</summary>
    private static int Sign(double difference) => difference < 0 ? -1 : difference > 0 ? 1 : 0;

    /// <summary>Sorts <paramref name="list"/> in place keeping equal items in their order, as JavaScript's sort does.</summary>
    private static void StableSort<T>(List<T> list, Comparison<T> order)
    {
        T[] sorted = list.OrderBy(static item => item, Comparer<T>.Create(order)).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            list[i] = sorted[i];
        }
    }

    private static void RequireFinite(string name, params double[] values)
    {
        if (values.Any(static v => !double.IsFinite(v)))
        {
            throw new ArgumentException("Every size and position must be a finite number.", name);
        }
    }

    private static InvalidOperationException Fails(string what) =>
        new($"App Designer's gridifier fails on this scene too: {what}.");
}
