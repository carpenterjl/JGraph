using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>What sizes one row or column of a grid: MATLAB's <c>'fit'</c>, a number of pixels, or <c>'Nx'</c>.</summary>
public enum UiGridTrackKind
{
    /// <summary>As large as the largest thing in it asks to be.</summary>
    Fit,

    /// <summary>A fixed number of pixels.</summary>
    Fixed,

    /// <summary>A share of what the other tracks leave, by weight.</summary>
    Weight,
}

/// <summary>
/// One row or column of a grid. <c>Value</c> is the pixels of a fixed track and the weight of a
/// weighted one; <c>NumberClass</c> is the numeric class a fixed track was written in, which R2025b
/// hands back (<c>{int8(5)}</c> reads back as <c>int8</c>).
/// </summary>
public readonly record struct UiGridTrack(UiGridTrackKind Kind, double Value, string NumberClass = "double")
{
    /// <summary>MATLAB's <c>'1x'</c>, what a grid grows by.</summary>
    public static readonly UiGridTrack One = new(UiGridTrackKind.Weight, 1);

    public static readonly UiGridTrack Fit = new(UiGridTrackKind.Fit, 0);
}

/// <summary>
/// Where a child sits in a grid: MATLAB's <c>Layout.Row</c> and <c>Layout.Column</c>, each one track
/// or a run of them, counted from 1.
/// </summary>
public readonly record struct UiGridCell(int Row, int RowEnd, int Column, int ColumnEnd)
{
    public UiGridCell(int row, int column)
        : this(row, row, column, column)
    {
    }
}

/// <summary>One child of a grid, as the arithmetic needs it: its cell and the size it asks for.</summary>
public readonly record struct UiGridItem(UiGridCell Cell, Size2D Fit);

/// <summary>
/// A grid laid out at one size: each track's size, and each child's rectangle as MATLAB's pixel
/// rectangle in the grid's own area (left and bottom from 1, Y upward). <c>Content</c> is the size
/// the tracks, the spacing and the padding come to, which is larger than the grid when its fixed
/// tracks do not fit.
/// </summary>
public sealed record UiGridArrangement(
    IReadOnlyList<double> RowHeights,
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<Rect2D> Cells,
    Size2D Content)
{
    /// <summary>
    /// Where each row begins, down from the top of the area inside the padding; a track that takes
    /// no room sits where the next would. The designer draws a grid's cells from these.
    /// </summary>
    public IReadOnlyList<double> RowStarts { get; init; } = [];

    /// <summary>Where each column begins, from the left of the area inside the padding.</summary>
    public IReadOnlyList<double> ColumnStarts { get; init; } = [];
}

/// <summary>
/// A scrollable grid laid out (<see cref="UiGridMath.ArrangeScrolling"/>): the arrangement in its
/// viewport, the viewport's size, and which scroll bars show. The arrangement's <c>Content</c> is
/// what there is to scroll: the grid's tracks as far as its last occupied row and column.
/// </summary>
public sealed record UiGridScrolled(UiGridArrangement Arrangement, Size2D Viewport, bool Vertical, bool Horizontal);

/// <summary>
/// The arithmetic of <c>uigridlayout</c> (app-building plan U5, ADR 0202), transcribed from R2025b's own
/// grid code in its web bundle (open item 153): the runtime controller
/// (<c>gbtcomponents/controller/GridController</c>: <c>_convertFitToPixels</c>,
/// <c>computeFitValuesWithNoSpanChildren</c>, <c>computeFitValuesWithSpanningChildren</c>,
/// <c>_computeFitLength</c>, <c>_getScrollableContentBox</c>, <c>_getScrollableContentMinimumSize</c>),
/// the spacing rule (<c>gbtshared/util/GridLayoutUtils.getSizesWithSpacing</c>), the view
/// (<c>gbtcomponents/view/Grid</c>: a weighted track's pixels, where a child's cell starts and ends),
/// the child strategy (<c>gbtshared/strategies/GridLayoutStrategy</c>), the scroll decision
/// (<c>gbtshared/mixins/controller/ScrollableContentBoxControllerMixin.calculateScrollableState</c>),
/// the design-time controller's empty <c>'fit'</c> track, and the MATLAB side's <c>'0x'</c>
/// (<c>GridLayoutController.convert0xTo0px</c>):
/// <list type="bullet">
/// <item>a <c>'0x'</c> track is a fixed track of 0 pixels;</item>
/// <item>a fixed track is its pixels; a <c>'fit'</c> track is the largest size asked for by a child
/// that sits in it alone; then each child that spans, largest first, shares what it still needs among
/// the <c>'fit'</c> tracks it crosses that no child sits in alone (all of them when there are none),
/// each empty one taking a pixel first; a child spanning a weighted track asks nothing;</item>
/// <item>a track of 0 pixels that is not weighted gives up the spacing after it, and the last one
/// the nearest spacing before it;</item>
/// <item>the weighted tracks share what the rest leave by weight, never below nothing;</item>
/// <item>a child has the whole of its cell, from its first track to its last that is not a 0-pixel
/// unweighted one;</item>
/// <item>a grid asked for its own size gives each weighted track its content times its weight over
/// the smallest weight;</item>
/// <item>a scrollable grid scrolls to its last occupied row and column.</item>
/// </list>
/// </summary>
public static class UiGridMath
{
    /// <summary>The thickness of the scroll bar a scrollable grid shows when its tracks do not fit.</summary>
    public const double ScrollBarSize = 17;

    /// <summary>
    /// What an empty <c>'fit'</c> track comes to at design time, where App Designer draws it so that
    /// it can be seen and dropped into (<c>uicomponents_appdesigner_plugin/controller/GridController
    /// ._convertFitToPixels</c>: a <c>'fit'</c> track worked out to 0 is 10 pixels). A script's grid
    /// gives such a track no room.
    /// </summary>
    public const double DesignTimeFit = 10;

    /// <summary>
    /// Lays out a grid that scrolls: at its size when everything fits, and otherwise in what a
    /// scroll bar along the right, along the bottom or both leave of it. The cells are MATLAB's
    /// pixel rectangles in that viewport, which sits in the grid's top-left corner. R2025b's
    /// <c>calculateScrollableState</c>: the content reaches to the last occupied row and column;
    /// a direction that overflows takes a bar, and the other then takes one only when its minimum
    /// size and the bar do not fit; laid out again with the bars until they settle.
    /// </summary>
    public static UiGridScrolled ArrangeScrolling(
        IReadOnlyList<UiGridTrack> rows,
        IReadOnlyList<UiGridTrack> columns,
        IReadOnlyList<double> padding,
        double rowSpacing,
        double columnSpacing,
        Size2D size,
        IReadOnlyList<UiGridItem> items)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(padding);
        ArgumentNullException.ThrowIfNull(items);
        (double left, double bottom, double right, double top) = Pad(padding);

        // _getLastOccupiedColumnAndRow: 1 and 1 for an empty grid.
        int lastRow = 1;
        int lastColumn = 1;
        foreach (UiGridItem item in items)
        {
            lastRow = System.Math.Max(lastRow, System.Math.Max(item.Cell.Row, item.Cell.RowEnd));
            lastColumn = System.Math.Max(lastColumn, System.Math.Max(item.Cell.Column, item.Cell.ColumnEnd));
        }

        // _getScrollableContentMinimumSize: the weighted tracks as nothing, every spacing counted.
        double minimumWidth = 0;
        double minimumHeight = 0;
        if (items.Count > 0)
        {
            minimumWidth = MinimumLength(columns, items, ColumnSpan, Width, columnSpacing, lastColumn) + left + right + (columnSpacing * (columns.Count - 1));
            minimumHeight = MinimumLength(rows, items, RowSpan, Height, rowSpacing, lastRow) + top + bottom + (rowSpacing * (rows.Count - 1));
        }

        bool vertical = false;
        bool horizontal = false;
        for (int pass = 0; ; pass++)
        {
            // The view is the grid less its bars (the view's _getScrollInsets).
            double viewWidth = size.Width - (vertical ? ScrollBarSize : 0);
            double viewHeight = size.Height - (horizontal ? ScrollBarSize : 0);
            var view = new Size2D(System.Math.Max(0, viewWidth), System.Math.Max(0, viewHeight));
            Laid across = LayDirection(columns, items, ColumnSpan, Width, columnSpacing, viewWidth, left + right, designTime: false);
            Laid down = LayDirection(rows, items, RowSpan, Height, rowSpacing, viewHeight, top + bottom, designTime: false);

            // _getScrollableContentBox: the tracks as far as the last occupied one, and the padding,
            // as a box from the grid's bottom-left corner; R2025b compares its sides exactly, so the
            // sums are made in its order (a grid its weighted tracks fill can come out a hair over).
            double contentWidth = Leading(across.View, (2 * lastColumn) - 1) + left + right;
            double contentHeight = Leading(down.View, (2 * lastRow) - 1) + top + bottom;
            double boxWidth = System.Math.Max(size.Width, contentWidth);
            double boxTop = size.Height;
            double boxBottom = System.Math.Min(size.Height - contentHeight, 0);
            bool wide = boxWidth > size.Width;
            bool tall = boxTop - boxBottom > size.Height;
            if (wide && !tall)
            {
                if (System.Math.Floor(minimumHeight + ScrollBarSize) <= size.Height)
                {
                    boxTop -= ScrollBarSize;
                }
                else
                {
                    tall = true;
                }
            }

            if (tall && !wide)
            {
                if (System.Math.Floor(minimumWidth + ScrollBarSize) <= size.Width)
                {
                    boxWidth -= ScrollBarSize;
                }
                else
                {
                    wide = true;
                }
            }

            if ((tall == vertical && wide == horizontal) || pass == 4)
            {
                UiGridArrangement arrangement = Assemble(across, down, padding, view, items) with
                {
                    Content = new Size2D(boxWidth, boxTop - boxBottom),
                };
                return new UiGridScrolled(arrangement, view, vertical, horizontal);
            }

            vertical = tall;
            horizontal = wide;
        }
    }

    /// <summary>
    /// Lays a grid out. <paramref name="padding"/> is MATLAB's <c>[left bottom right top]</c>. With
    /// <paramref name="size"/> null it gives the size the grid itself asks for when it sits in a
    /// <c>'fit'</c> track of another (<c>_computeFitSize</c>), with each weighted track as large as
    /// its content times its weight over the smallest weight. At <paramref name="designTime"/> an
    /// empty <c>'fit'</c> track is <see cref="DesignTimeFit"/>.
    /// </summary>
    public static UiGridArrangement Arrange(
        IReadOnlyList<UiGridTrack> rows,
        IReadOnlyList<UiGridTrack> columns,
        IReadOnlyList<double> padding,
        double rowSpacing,
        double columnSpacing,
        Size2D? size,
        IReadOnlyList<UiGridItem> items,
        bool designTime = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(padding);
        ArgumentNullException.ThrowIfNull(items);
        (double left, double bottom, double right, double top) = Pad(padding);
        Laid across = LayDirection(columns, items, ColumnSpan, Width, columnSpacing, size?.Width, left + right, designTime);
        Laid down = LayDirection(rows, items, RowSpan, Height, rowSpacing, size?.Height, top + bottom, designTime);
        return Assemble(across, down, padding, size, items);
    }

    /// <summary>One direction laid out: each track's pixels, which are weighted, and R2025b's view of them - the tracks with the spacing between.</summary>
    private sealed record Laid(double[] Sizes, bool[] Weighted, double[] View);

    private static (int From, int To) RowSpan(UiGridCell cell) => (cell.Row, cell.RowEnd);

    private static (int From, int To) ColumnSpan(UiGridCell cell) => (cell.Column, cell.ColumnEnd);

    private static double Width(Size2D fit) => fit.Width;

    private static double Height(Size2D fit) => fit.Height;

    private static (double Left, double Bottom, double Right, double Top) Pad(IReadOnlyList<double> padding) => (
        padding.Count > 0 ? padding[0] : 0,
        padding.Count > 1 ? padding[1] : 0,
        padding.Count > 2 ? padding[2] : 0,
        padding.Count > 3 ? padding[3] : 0);

    /// <summary>The arrangement from both directions laid out: track sizes and starts, each child's rectangle, the content.</summary>
    private static UiGridArrangement Assemble(Laid across, Laid down, IReadOnlyList<double> padding, Size2D? size, IReadOnlyList<UiGridItem> items)
    {
        (double left, double bottom, double right, double top) = Pad(padding);

        // _computeLengthWithoutInsets: the view's entries, then the padding.
        var content = new Size2D(
            Leading(across.View, across.View.Length) + (left + right),
            Leading(down.View, down.View.Length) + (bottom + top));
        double height = size?.Height ?? content.Height;

        var cells = new Rect2D[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            UiGridCell cell = items[i].Cell;
            (double x, double w) = Span(across, cell.Column, cell.ColumnEnd, left);
            (double y, double h) = Span(down, cell.Row, cell.RowEnd, top);

            // Rows count down from the top; MATLAB's rectangle counts up from the bottom, from 1
            // (GridLayoutStrategy: the grid's height, less the row's top, less the cell's height).
            cells[i] = new Rect2D(x + 1, height - y - h + 1, w, h);
        }

        return new UiGridArrangement(down.Sizes, across.Sizes, cells, content)
        {
            RowStarts = Starts(down),
            ColumnStarts = Starts(across),
        };
    }

    /// <summary>A track of the grid as R2025b's layout reads it: <c>'0x'</c> is 0 pixels (<c>convert0xTo0px</c>).</summary>
    private static UiGridTrack[] Specs(IReadOnlyList<UiGridTrack> tracks)
    {
        var specs = new UiGridTrack[tracks.Count];
        for (int i = 0; i < specs.Length; i++)
        {
            UiGridTrack track = tracks[i];
            specs[i] = track.Kind == UiGridTrackKind.Weight && track.Value == 0 ? new UiGridTrack(UiGridTrackKind.Fixed, 0) : track;
        }

        return specs;
    }

    /// <summary>
    /// One direction's tracks. With <paramref name="available"/> (the grid's length less its bar)
    /// the runtime's layout: <c>_convertSizesForWidget</c>, then the view's spacing and weighted
    /// shares of what is left inside <paramref name="padding"/> (the two sides' sum). Without, the
    /// grid's own fit length (<c>_computeFitLength</c>): weighted tracks as <c>'fit'</c>, times their
    /// weight over the smallest, keeping the spacing round them.
    /// </summary>
    private static Laid LayDirection(
        IReadOnlyList<UiGridTrack> tracks,
        IReadOnlyList<UiGridItem> items,
        Func<UiGridCell, (int From, int To)> span,
        Func<Size2D, double> along,
        double spacing,
        double? available,
        double padding,
        bool designTime)
    {
        UiGridTrack[] specs = Specs(tracks);
        int count = specs.Length;
        var weighted = new bool[count];
        var fits = new bool[count];
        double least = double.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            weighted[i] = specs[i].Kind == UiGridTrackKind.Weight;
            fits[i] = specs[i].Kind == UiGridTrackKind.Fit || (weighted[i] && available is null);
            if (weighted[i])
            {
                least = System.Math.Min(least, specs[i].Value);
            }
        }

        double[] sizes = FitToPixels(specs, fits, items, span, along, spacing, designTime);
        if (available is null)
        {
            // normalizeSizesTo1x, then each weighted track's content times its weight.
            for (int i = 0; i < count; i++)
            {
                if (weighted[i])
                {
                    sizes[i] *= specs[i].Value / least;
                }
            }

            return new Laid(sizes, weighted, WithSpacing(sizes, weighted, spacing));
        }

        double[] view = WithSpacing(sizes, weighted, spacing);

        // The view's _getColumnWidthInPixels: a weighted track's share of what the view's other
        // tracks and the spacing leave, never below nothing.
        double others = 0;
        double weights = 0;
        for (int v = 0; v < view.Length; v++)
        {
            if (v % 2 == 0 && weighted[v / 2])
            {
                weights += specs[v / 2].Value;
            }
            else
            {
                others += view[v];
            }
        }

        double room = available.Value - others - padding;
        for (int i = 0; i < count; i++)
        {
            if (weighted[i])
            {
                sizes[i] = System.Math.Max(0, specs[i].Value / weights * room);
                view[2 * i] = sizes[i];
            }
        }

        return new Laid(sizes, weighted, view);
    }

    /// <summary>
    /// R2025b's <c>_convertFitToPixels</c>: each <c>'fit'</c> track (<paramref name="fits"/>) from the
    /// children in it alone (<c>computeFitValuesWithNoSpanChildren</c>), then from the children that
    /// span (<c>computeFitValuesWithSpanningChildren</c>). Fixed tracks are their pixels; a weighted
    /// track that is not counted as <c>'fit'</c> is left at 0 for the view to share out.
    /// </summary>
    private static double[] FitToPixels(
        UiGridTrack[] specs,
        bool[] fits,
        IReadOnlyList<UiGridItem> items,
        Func<UiGridCell, (int From, int To)> span,
        Func<Size2D, double> along,
        double spacing,
        bool designTime)
    {
        int count = specs.Length;
        var sizes = new double[count];
        var alone = new bool[count];
        for (int i = 0; i < count; i++)
        {
            if (!fits[i] && specs[i].Kind == UiGridTrackKind.Fixed)
            {
                sizes[i] = System.Math.Max(0, specs[i].Value);
            }
        }

        if (count == 0)
        {
            return sizes;
        }

        foreach (UiGridItem item in items)
        {
            (int from, int to) = Clamp(span(item.Cell), count);
            if (from == to && fits[from])
            {
                sizes[from] = System.Math.Max(sizes[from], along(item.Fit));
                alone[from] = true;
            }
        }

        // _sortChildrenByDescendingFitSize: a stable sort, so equal children keep their order.
        foreach (UiGridItem item in items.OrderByDescending(item => along(item.Fit)))
        {
            (int from, int to) = Clamp(span(item.Cell), count);
            if (from == to)
            {
                continue;
            }

            double need = along(item.Fit);
            int nonzero = 0;
            var crossed = new List<int>();
            for (int t = from; t <= to; t++)
            {
                if (!fits[t] && specs[t].Kind == UiGridTrackKind.Weight)
                {
                    need = 0;
                    nonzero++;
                    break;
                }

                double has = sizes[t];
                need -= has;
                if (fits[t])
                {
                    crossed.Add(t);
                }

                if (has != 0)
                {
                    nonzero++;
                }
            }

            need -= System.Math.Max(0, nonzero - 1) * spacing;
            if (need <= 0 || crossed.Count == 0)
            {
                continue;
            }

            // The 'fit' tracks no child sits in alone take it, or all of them when there are none;
            // each empty one first takes a pixel, and the spacing it opens.
            List<int> takers = crossed.FindAll(t => !alone[t]);
            if (takers.Count == 0)
            {
                takers = crossed;
            }

            List<int> empty = takers.FindAll(t => sizes[t] == 0);
            if (empty.Count > 0)
            {
                foreach (int t in empty)
                {
                    sizes[t] += 1;
                }

                int spanned = to - from + 1;
                int gaps = empty.Count == spanned ? empty.Count - 1 : empty.Count;
                need = need - empty.Count - (gaps * spacing);
            }

            if (need > 0)
            {
                foreach (int t in takers)
                {
                    sizes[t] += need / takers.Count;
                }
            }
        }

        // At design time an empty 'fit' track is drawn, so that it can be seen and dropped into.
        if (designTime)
        {
            for (int i = 0; i < count; i++)
            {
                if (fits[i] && sizes[i] == 0)
                {
                    sizes[i] = DesignTimeFit;
                }
            }
        }

        return sizes;
    }

    /// <summary>
    /// R2025b's <c>getSizesWithSpacing</c>: the tracks with the spacing between them, where a track of
    /// 0 pixels that is not weighted gives up the spacing after it and a last one the nearest spacing
    /// before it.
    /// </summary>
    private static double[] WithSpacing(double[] sizes, bool[] weighted, double spacing)
    {
        if (sizes.Length == 0)
        {
            return [];
        }

        var view = new double[(2 * sizes.Length) - 1];
        for (int i = 0; i < sizes.Length; i++)
        {
            view[2 * i] = sizes[i];
            if (i < sizes.Length - 1)
            {
                view[(2 * i) + 1] = spacing;
            }
        }

        for (int o = 0; o < view.Length - 2; o += 2)
        {
            if (view[o] == 0 && !weighted[o / 2])
            {
                view[o + 1] = 0;
            }
        }

        int last = view.Length - 1;
        if (view[last] == 0 && !weighted[last / 2])
        {
            for (int o = view.Length - 2; o >= 0; o -= 2)
            {
                if (view[o] > 0)
                {
                    view[o] = 0;
                    break;
                }
            }
        }

        return view;
    }

    /// <summary>
    /// The minimum length R2025b's scroll decision measures one direction by
    /// (<c>_getScrollableContentMinimumSize</c>): the tracks up to the last occupied one, weighted
    /// ones as nothing; the caller adds the padding and every spacing.
    /// </summary>
    private static double MinimumLength(
        IReadOnlyList<UiGridTrack> tracks,
        IReadOnlyList<UiGridItem> items,
        Func<UiGridCell, (int From, int To)> span,
        Func<Size2D, double> along,
        double spacing,
        int last)
    {
        UiGridTrack[] specs = Specs(tracks);
        var fits = new bool[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].Kind == UiGridTrackKind.Weight)
            {
                specs[i] = new UiGridTrack(UiGridTrackKind.Fixed, 0);
            }

            fits[i] = specs[i].Kind == UiGridTrackKind.Fit;
        }

        double[] sizes = FitToPixels(specs, fits, items, span, along, spacing, designTime: false);
        double total = 0;
        for (int i = 0; i < System.Math.Min(last, sizes.Length); i++)
        {
            total += sizes[i];
        }

        return total;
    }

    /// <summary>The sum of a view's first <paramref name="count"/> entries (JavaScript's <c>slice(0, count)</c>).</summary>
    private static double Leading(double[] view, int count)
    {
        double total = 0;
        for (int v = 0; v < System.Math.Min(count, view.Length); v++)
        {
            total += view[v];
        }

        return total;
    }

    private static (int From, int To) Clamp((int From, int To) span, int count)
    {
        int from = System.Math.Clamp(span.From, 1, System.Math.Max(1, count)) - 1;
        int to = System.Math.Clamp(span.To, from + 1, System.Math.Max(1, count)) - 1;
        return (from, to);
    }

    /// <summary>Where each track begins, from the first one's edge: the view's entries before it.</summary>
    private static double[] Starts(Laid laid)
    {
        var starts = new double[laid.Sizes.Length];
        for (int i = 0; i < starts.Length; i++)
        {
            starts[i] = Leading(laid.View, 2 * i);
        }

        return starts;
    }

    /// <summary>
    /// Where a child's run of tracks begins and how long it is (the view's
    /// <c>getColumnLeftPositionInPixelsWrtGrid</c> and <c>getCellSize</c>): its end is pulled back
    /// over tracks of 0 pixels that are not weighted (<c>_getEndIndexForView</c>). The start is
    /// counted on from <paramref name="padding"/>, the side's padding, as the view counts it.
    /// </summary>
    private static (double Start, double Length) Span(Laid laid, int from, int to, double padding)
    {
        if (laid.Sizes.Length == 0)
        {
            return (padding, 0);
        }

        (int first, int last) = Clamp((from, to), laid.Sizes.Length);
        while (last > first && laid.Sizes[last] == 0 && !laid.Weighted[last])
        {
            last--;
        }

        double start = padding;
        for (int v = 0; v < 2 * first; v++)
        {
            start += laid.View[v];
        }

        double length = laid.View[2 * first];
        for (int v = (2 * first) + 1; v <= 2 * last; v++)
        {
            length += laid.View[v];
        }

        return (start, length);
    }
}

/// <summary>
/// MATLAB's <c>uigridlayout</c> (<c>matlab.ui.container.GridLayout</c>): an invisible container that
/// fills its parent and places its children in rows and columns (app-building plan, U5). It draws
/// only its background. Children are placed as they arrive, row by row after the last one placed,
/// and the grid grows by <c>'1x'</c> tracks to hold wherever a child is put.
/// </summary>
public sealed class UiGridLayoutModel : UiContainerModel
{
    private static readonly IReadOnlyList<UiGridTrack> TwoOnes = [UiGridTrack.One, UiGridTrack.One];
    private static readonly IReadOnlyList<double> TenAllRound = [10, 10, 10, 10];

    private IReadOnlyList<UiGridTrack> _rows = TwoOnes;
    private IReadOnlyList<UiGridTrack> _columns = TwoOnes;
    private IReadOnlyList<double> _padding = TenAllRound;
    private double _rowSpacing = 10;
    private double _columnSpacing = 10;
    private string _paddingClass = "double";
    private string _rowSpacingClass = "double";
    private string _columnSpacingClass = "double";
    private UiColor _background = UiPanelModel.DefaultBackground;

    public UiGridLayoutModel()
    {
        Name = "GridLayout";
        Units = UiUnits.Pixels;
        Position = new Rect2D(1, 1, 100, 100);
    }

    /// <summary>
    /// MATLAB's <c>RowHeight</c>: the rows a script declared, with <c>'1x'</c> rows after them for
    /// as far down as a child reaches. The extra rows are there only while a child needs them
    /// (R2025b, probe <c>u5_grid</c>).
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<UiGridTrack> Rows
    {
        get => Grown(_rows, NeededRows());
        set => SetProperty(ref _rows, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>ColumnWidth</c>, grown as <see cref="Rows"/> is for as far across as a child reaches.</summary>
    [Browsable(false)]
    public IReadOnlyList<UiGridTrack> Columns
    {
        get => Grown(_columns, NeededColumns());
        set => SetProperty(ref _columns, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Padding</c>: left, bottom, right, top.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Padding
    {
        get => _padding;
        set => SetProperty(ref _padding, value ?? TenAllRound, InvalidationKind.Ui);
    }

    /// <summary>The numeric class <see cref="Padding"/> was written in, which reads back.</summary>
    [Browsable(false)]
    public string PaddingClass
    {
        get => _paddingClass;
        set => _paddingClass = value ?? "double";
    }

    [Browsable(false)]
    public double RowSpacing
    {
        get => _rowSpacing;
        set => SetProperty(ref _rowSpacing, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string RowSpacingClass
    {
        get => _rowSpacingClass;
        set => _rowSpacingClass = value ?? "double";
    }

    [Browsable(false)]
    public double ColumnSpacing
    {
        get => _columnSpacing;
        set => SetProperty(ref _columnSpacing, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string ColumnSpacingClass
    {
        get => _columnSpacingClass;
        set => _columnSpacingClass = value ?? "double";
    }

    [Browsable(false)]
    public UiColor BackgroundColor
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Ui);
    }

    /// <summary>A grid has no border and no title: its children are placed in the whole of it.</summary>
    public override Thickness Insets() => new(0);

    /// <summary>
    /// This grid laid out to scroll in its own rectangle (open item 47): its tracks, the viewport
    /// its bars leave, and which bars it shows — what <c>scroll</c> and
    /// <c>ScrollableViewportLocation</c> measure against on the script's side.
    /// </summary>
    public UiGridScrolled Scrolled()
    {
        IReadOnlyList<GraphObject> placed = Placed();
        var items = new UiGridItem[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            items[i] = new UiGridItem(CellOf(placed[i]), UiFit.Of(placed[i]));
        }

        return UiGridMath.ArrangeScrolling(Rows, Columns, _padding, _rowSpacing, _columnSpacing, OwnSize(), items);
    }

    /// <summary>Everything placed in the grid, components first and then axes, each in its own order.</summary>
    public IReadOnlyList<GraphObject> Placed()
    {
        var placed = new List<GraphObject>(Components.Count);
        placed.AddRange(Components);
        placed.AddRange(ContainedAxes());
        return placed;
    }

    /// <summary>The cell a child of this grid sits in; (1, 1) for one that was never placed.</summary>
    public static UiGridCell CellOf(GraphObject child) => child switch
    {
        UiObject component => component.GridCell ?? new UiGridCell(1, 1),
        AxesModel axes => axes.GridCell ?? new UiGridCell(1, 1),
        _ => new UiGridCell(1, 1),
    };

    /// <summary>
    /// Gives a newcomer its cell, R2025b's way (probe <c>u5_grid</c>): the cell after the furthest
    /// one any child reaches, reading row by row; the first cell when the grid is empty. A new row is
    /// added when the last one is full.
    /// </summary>
    public UiGridCell NextCell(GraphObject newcomer)
    {
        int row = 0;
        int column = 0;
        foreach (GraphObject child in Placed())
        {
            if (ReferenceEquals(child, newcomer))
            {
                continue;
            }

            UiGridCell cell = CellOf(child);
            if (cell.RowEnd > row || (cell.RowEnd == row && cell.ColumnEnd > column))
            {
                row = cell.RowEnd;
                column = cell.ColumnEnd;
            }
        }

        if (row == 0)
        {
            return new UiGridCell(1, 1);
        }

        return column < System.Math.Max(1, Columns.Count) ? new UiGridCell(row, column + 1) : new UiGridCell(row + 1, 1);
    }

    /// <summary>A child has been given a cell: the grid's tracks may have changed with it.</summary>
    public void Hold(UiGridCell cell)
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Columns));
        Invalidate(InvalidationKind.Ui);
    }

    private int NeededRows()
    {
        int needed = 0;
        foreach (GraphObject child in Placed())
        {
            needed = System.Math.Max(needed, CellOf(child).RowEnd);
        }

        return needed;
    }

    private int NeededColumns()
    {
        int needed = 0;
        foreach (GraphObject child in Placed())
        {
            needed = System.Math.Max(needed, CellOf(child).ColumnEnd);
        }

        return needed;
    }

    private static IReadOnlyList<UiGridTrack> Grown(IReadOnlyList<UiGridTrack> tracks, int count)
    {
        if (tracks.Count >= count)
        {
            return tracks;
        }

        var grown = new List<UiGridTrack>(tracks);
        while (grown.Count < count)
        {
            grown.Add(UiGridTrack.One);
        }

        return grown;
    }

    /// <summary>
    /// The grid's own rectangle: the whole of the area its parent gives its children, or its cell
    /// when it sits in another grid.
    /// </summary>
    public Rect2D OwnPixelRect()
    {
        if (Parent is UiGridLayoutModel outer)
        {
            return outer.RectOf(this);
        }

        Size2D area = Parent switch
        {
            UiPanelModel panel => panel.GridArea(),
            IUiContainer holder => holder.InnerPixelSize,
            _ => new Size2D(100, 100),
        };
        return new Rect2D(1, 1, area.Width, area.Height);
    }

    [ThreadStatic]
    private static Dictionary<UiGridLayoutModel, UiGridArrangement>? _remembered;

    /// <summary>
    /// For as long as the answer is held, a grid on this thread is laid out once however many of its
    /// children ask where they are — what taking a frame of a figure full of grids needs. Nothing may
    /// change the grids meanwhile.
    /// </summary>
    public static IDisposable Remembering()
    {
        if (_remembered is not null)
        {
            return new Forgetting(restore: false);
        }

        _remembered = new Dictionary<UiGridLayoutModel, UiGridArrangement>(ReferenceEqualityComparer.Instance);
        return new Forgetting(restore: true);
    }

    private sealed class Forgetting(bool restore) : IDisposable
    {
        public void Dispose()
        {
            if (restore)
            {
                _remembered = null;
            }
        }
    }

    /// <summary>This grid laid out at the size it has now, with its children in <see cref="Placed"/> order.</summary>
    public UiGridArrangement Arrange()
    {
        if (_remembered is { } remembered && remembered.TryGetValue(this, out UiGridArrangement? known))
        {
            return known;
        }

        Rect2D own = OwnPixelRect();
        UiGridArrangement arrangement = Arrange(new Size2D(own.Width, own.Height));
        _remembered?.TryAdd(this, arrangement);
        return arrangement;
    }

    /// <summary>
    /// Pins each axes placed in this grid to its cell, in pixels, so that everything which places an
    /// axes — the renderer, a script's <c>Position</c>, a click — finds it where the grid put it.
    /// Script thread only.
    /// </summary>
    public void PinAxes()
    {
        IReadOnlyList<GraphObject> placed = Placed();
        UiGridArrangement? arrangement = null;
        for (int i = 0; i < placed.Count; i++)
        {
            if (placed[i] is not AxesModel axes)
            {
                continue;
            }

            arrangement ??= Arrange();
            Rect2D cell = arrangement.Cells[i];
            if (axes.Units != UiUnits.Pixels)
            {
                axes.Units = UiUnits.Pixels;
            }

            if (axes.InnerTarget is not null)
            {
                axes.InnerTarget = null;
            }

            if (axes.PixelBounds != cell)
            {
                axes.PixelBounds = cell;
            }
        }
    }

    /// <summary>This grid laid out at a size, or — with none — at the size it asks for.</summary>
    public UiGridArrangement Arrange(Size2D? size)
    {
        IReadOnlyList<GraphObject> placed = Placed();
        var items = new UiGridItem[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            items[i] = new UiGridItem(CellOf(placed[i]), UiFit.Of(placed[i]));
        }

        // At design time a grid draws its empty 'fit' tracks and never scrolls, as App Designer's does.
        bool designTime = Figure?.IsDesignTime == true;
        if (size is not { } given || !Scrollable || designTime)
        {
            return UiGridMath.Arrange(Rows, Columns, _padding, _rowSpacing, _columnSpacing, size, items, designTime);
        }

        // A grid that scrolls lays out in its viewport, which is the top-left of its rectangle: a
        // bar along the bottom lifts every cell by its height.
        UiGridScrolled scrolled = UiGridMath.ArrangeScrolling(Rows, Columns, _padding, _rowSpacing, _columnSpacing, given, items);
        if (!scrolled.Horizontal)
        {
            return scrolled.Arrangement;
        }

        return scrolled.Arrangement with
        {
            Cells = [.. scrolled.Arrangement.Cells.Select(static cell => new Rect2D(cell.X, cell.Y + UiGridMath.ScrollBarSize, cell.Width, cell.Height))],
        };
    }

    /// <summary>A child's cell as MATLAB's pixel rectangle in the grid's area.</summary>
    public Rect2D RectOf(GraphObject child)
    {
        IReadOnlyList<GraphObject> placed = Placed();
        for (int i = 0; i < placed.Count; i++)
        {
            if (ReferenceEquals(placed[i], child))
            {
                return Arrange().Cells[i];
            }
        }

        return new Rect2D(1, 1, 0, 0);
    }

    /// <inheritdoc cref="UiContainerModel.InnerPixelSize" />
    public Size2D OwnSize()
    {
        Rect2D own = OwnPixelRect();
        return new Size2D(own.Width, own.Height);
    }
}
