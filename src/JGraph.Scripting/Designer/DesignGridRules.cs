using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>Which of a grid's two track lists an edit touches (designer plan DS4).</summary>
public enum GridAxis
{
    /// <summary>The rows: the grid's <c>RowHeight</c> and a child's <c>Layout.Row</c>.</summary>
    Row,

    /// <summary>The columns: the grid's <c>ColumnWidth</c> and a child's <c>Layout.Column</c>.</summary>
    Column,
}

/// <summary>
/// The cells a grid child takes (designer plan DS4), as MATLAB's <c>Layout.Row</c> and
/// <c>Layout.Column</c> hold them: 1-based and inclusive, one track where a start and its end are
/// equal. App Designer's client keeps the same pair as <c>LayoutConstraints.Row</c> and
/// <c>.Column</c>, each a number or <c>[a b]</c>.
/// </summary>
/// <param name="RowStart">The first row.</param>
/// <param name="RowEnd">The last row.</param>
/// <param name="ColumnStart">The first column.</param>
/// <param name="ColumnEnd">The last column.</param>
public readonly record struct GridSpan(int RowStart, int RowEnd, int ColumnStart, int ColumnEnd)
{
    /// <summary>The one cell at <paramref name="row"/> and <paramref name="column"/>.</summary>
    public static GridSpan Cell(int row, int column) => new(row, row, column, column);

    /// <summary>
    /// The value <c>Layout.Row</c> is written as: the row when the span has one, else
    /// <c>[start end]</c>. Never <c>[a a]</c>, which MATLAB refuses (a span must increase,
    /// <c>validateScalarOrIncreasingArrayOf2</c>) and App Designer collapses wherever it writes one
    /// (<c>GridUtil</c>'s span helpers, <c>GridChildResizeHelpers</c>, <c>GridLayoutCommandCreator</c>).
    /// </summary>
    public DesignValue RowValue => Value(RowStart, RowEnd);

    /// <summary>The value <c>Layout.Column</c> is written as; see <see cref="RowValue"/>.</summary>
    public DesignValue ColumnValue => Value(ColumnStart, ColumnEnd);

    /// <summary>
    /// The span a child's <c>Layout.Row</c> and <c>Layout.Column</c> values give: each a positive
    /// whole number or a 1-by-2 row of them that does not decrease (App Designer's client takes
    /// <c>[a a]</c> as <c>a</c>); null when either is missing or anything else.
    /// </summary>
    public static GridSpan? FromValues(DesignValue? row, DesignValue? column)
    {
        if (Track(row) is not { } r || Track(column) is not { } c)
        {
            return null;
        }

        return new GridSpan(r.Start, r.End, c.Start, c.End);
    }

    /// <summary>Whether the span takes the cell at <paramref name="row"/> and <paramref name="column"/>.</summary>
    public bool Covers(int row, int column) =>
        row >= RowStart && row <= RowEnd && column >= ColumnStart && column <= ColumnEnd;

    private static DesignValue Value(int start, int end) =>
        start == end ? NumericValue.Scalar(start) : NumericValue.Row(start, end);

    private static (int Start, int End)? Track(DesignValue? value)
    {
        if (value is not NumericValue { Rows: 1 } numeric || numeric.Cols is not (1 or 2))
        {
            return null;
        }

        double start = numeric.Data[0];
        double end = numeric.Data[^1];
        if (!Whole(start) || !Whole(end) || start > end)
        {
            return null;
        }

        return ((int)start, (int)end);

        static bool Whole(double v) => v >= 1 && v <= int.MaxValue && v == System.Math.Floor(v);
    }
}

/// <summary>
/// A grid's cells as the canvas draws them (designer plan DS4), on the surface with y growing down:
/// each row's top and height and each column's left and width, padding and spacing outside them -
/// what App Designer's rules read from the design-time grid's cell overlays
/// (<c>DesignTimeGridCellVisualizationMixin</c>: one div per cell, on the CSS track of the cell
/// alone), with the grid's own spacing and padding properties beside them.
/// </summary>
/// <remarks>
/// App Designer reads the cells in screen pixels, zoom included, but the spacing and padding as the
/// properties hold them, so away from a zoom of 1 its half-spacing zones and padding bands do not
/// match what is drawn (placement spec 5, commands spec 3.1). The rules here take all of them in the
/// one frame the caller gives: at a zoom of 1 that is App Designer's frame exactly.
/// </remarks>
public sealed class GridCells
{
    private readonly (double Start, double Size)[] rowTracks;
    private readonly (double Start, double Size)[] columnTracks;

    /// <summary>
    /// The cells of a grid whose box is <paramref name="box"/>: <paramref name="rows"/> top to bottom
    /// (each row's top and height) and <paramref name="columns"/> left to right (each column's left
    /// and width), in surface coordinates.
    /// </summary>
    /// <param name="box">The grid's own box.</param>
    /// <param name="padding">The grid's <c>Padding</c>, MATLAB's order <c>[left bottom right top]</c>.</param>
    /// <param name="rowSpacing">The grid's <c>RowSpacing</c>.</param>
    /// <param name="columnSpacing">The grid's <c>ColumnSpacing</c>.</param>
    /// <param name="rows">Each row's top and height.</param>
    /// <param name="columns">Each column's left and width.</param>
    public GridCells(
        Rect2D box,
        IReadOnlyList<double> padding,
        double rowSpacing,
        double columnSpacing,
        IReadOnlyList<(double Start, double Size)> rows,
        IReadOnlyList<(double Start, double Size)> columns)
    {
        ArgumentNullException.ThrowIfNull(padding);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        if (padding.Count != 4)
        {
            throw new ArgumentException("A grid's padding is four numbers, [left bottom right top].", nameof(padding));
        }

        Box = box;
        Padding = padding.ToArray();
        RowSpacing = rowSpacing;
        ColumnSpacing = columnSpacing;
        rowTracks = rows.ToArray();
        columnTracks = columns.ToArray();
    }

    /// <summary>The grid's own box.</summary>
    public Rect2D Box { get; }

    /// <summary>How many rows the grid has.</summary>
    public int Rows => rowTracks.Length;

    /// <summary>How many columns the grid has.</summary>
    public int Columns => columnTracks.Length;

    /// <summary>The grid's <c>RowSpacing</c>.</summary>
    public double RowSpacing { get; }

    /// <summary>The grid's <c>ColumnSpacing</c>.</summary>
    public double ColumnSpacing { get; }

    /// <summary>The grid's <c>Padding</c>, <c>[left bottom right top]</c>.</summary>
    public IReadOnlyList<double> Padding { get; }

    /// <summary>The cell at <paramref name="row"/> and <paramref name="column"/>, 1-based.</summary>
    public Rect2D Cell(int row, int column)
    {
        CheckRow(row);
        CheckColumn(column);
        (double top, double height) = rowTracks[row - 1];
        (double left, double width) = columnTracks[column - 1];
        return new Rect2D(left, top, width, height);
    }

    /// <summary>
    /// The box a span covers: from its first cell's top-left corner to its last cell's bottom-right,
    /// the spacing between them inside.
    /// </summary>
    public Rect2D SpanRect(GridSpan span)
    {
        CheckRow(span.RowStart);
        CheckRow(span.RowEnd);
        CheckColumn(span.ColumnStart);
        CheckColumn(span.ColumnEnd);
        double left = columnTracks[span.ColumnStart - 1].Start;
        double top = rowTracks[span.RowStart - 1].Start;
        double right = columnTracks[span.ColumnEnd - 1].Start + columnTracks[span.ColumnEnd - 1].Size;
        double bottom = rowTracks[span.RowEnd - 1].Start + rowTracks[span.RowEnd - 1].Size;
        return new Rect2D(left, top, right - left, bottom - top);
    }

    /// <summary>A row's top, 0-based.</summary>
    internal double Top(int index) => rowTracks[index].Start;

    /// <summary>A row's height, 0-based.</summary>
    internal double Height(int index) => rowTracks[index].Size;

    /// <summary>A column's left, 0-based.</summary>
    internal double Left(int index) => columnTracks[index].Start;

    /// <summary>A column's width, 0-based.</summary>
    internal double Width(int index) => columnTracks[index].Size;

    private void CheckRow(int row)
    {
        if (row < 1 || row > Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, $"The grid has {Rows} rows.");
        }
    }

    private void CheckColumn(int column)
    {
        if (column < 1 || column > Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, $"The grid has {Columns} columns.");
        }
    }
}

/// <summary>
/// What lies under a point of a grid (<c>GridUtil.getColumnRowUnderPoint</c>): the row and column
/// whose band holds it, the gap it is in when it is in no row or column (the two tracks on either
/// side), and the padding band it is in (1 before the first track, one past the last after it).
/// </summary>
/// <param name="Row">The row whose band holds the point, edges included.</param>
/// <param name="Column">The column whose band holds the point, edges included.</param>
/// <param name="RowSpacing">The rows on either side of the gap holding the point, when no row does.</param>
/// <param name="ColumnSpacing">The columns on either side of the gap holding the point, when no column does.</param>
/// <param name="RowPadding">1 in the top padding, rows + 1 in the bottom padding, when in no row gap.</param>
/// <param name="ColumnPadding">1 in the left padding, columns + 1 in the right padding, when in no column gap.</param>
public readonly record struct GridPointInfo(
    int? Row, int? Column, (int, int)? RowSpacing, (int, int)? ColumnSpacing, int? RowPadding, int? ColumnPadding);

/// <summary>
/// App Designer's grid arithmetic (designer plan DS4), transcribed from R2025b's own client code:
/// which cells a component's box takes when it is dropped or moved into a grid
/// (<c>GridUtil.calculateGridConstraintsListByBoundsListAndCells</c> under
/// <c>CellBoundsValidator</c>), the cells a labelled entry's label and field take, the cell a click
/// lands in (<c>MousePositionValidator</c>, <c>CellMousePositionValidator</c>), what the grid's
/// context menu reads under the pointer (<c>getColumnRowUnderPoint</c>, <c>parseAddingInfo</c>,
/// <c>parseDeletingInfo</c>), how a child's span shifts when a row or column is added or deleted
/// (<c>updateRowByAddingNewRow</c> and its kin), and the span a click-and-draw gives
/// (<c>beginComponentDraw</c>, <c>_HandleDoingComponentDraw</c>, through the grid-child resize's
/// helpers, <see cref="GridSpanResize"/>). Every box and point is on the canvas surface, the same
/// frame as the <see cref="GridCells"/>.
/// </summary>
/// <remarks>
/// Divergence: three of App Designer's tests are corrected, by the house rule that correctness
/// outranks parity; each is marked where it stands (<see cref="SpanOfBounds"/>,
/// <see cref="LabeledClick"/>, <see cref="PointInfo"/>).
/// </remarks>
public static class DesignGridRules
{
    // --- a box to its cells ----------------------------------------------------------------------

    /// <summary>
    /// The cells a component's box takes in a grid (<c>CellBoundsValidator</c>, through
    /// <c>GridUtil.calculateGridConstraintsListByBoundsListAndCells</c> and its local <c>P</c>);
    /// null when the box touches none of the cells (touching counts). On each axis an edge belongs to
    /// the cell whose box, widened by half the spacing either side, holds it, an edge before the first
    /// cell's centre to the first and one past the last cell's centre to the last; a cell between the
    /// edges' cells counts only when the box covers its centre, but a box whose edges lie past the
    /// centre of one cell and short of the next one's keeps both.
    /// </summary>
    /// <remarks>
    /// Divergence: App Designer's two edge searches leave out an edge exactly on a gap's midline
    /// (each zone is open at both ends), so it falls back to the first cell, giving a span too large
    /// (<c>[1 2]</c> where the box is in column 2) or one that runs backwards, <c>Column [2 1]</c>,
    /// which App Designer then writes and MATLAB refuses (placement spec 17.1). Here the start edge's
    /// zone is <c>[x - sp/2, x + w + sp/2)</c> and the end edge's <c>(x - sp/2, x + w + sp/2]</c>, so a
    /// midline start edge belongs to the later cell and a midline end edge to the earlier. Only a box
    /// of no width standing exactly on a midline still has its edges cross; it is taken as lying in the
    /// gap, both cells, as App Designer takes any narrow box in a gap. Every other box gets App
    /// Designer's cells - in a frame whose spacing matches the gaps drawn between the cells. Given
    /// spacing narrower than the drawn gaps (App Designer's own frame at a zoom above 1), an end edge in
    /// the part of a gap no zone covers falls back to the first cell as in App Designer, and the edges
    /// cross there too; the swap then gives the cells the box covers, where App Designer writes a
    /// backwards span such as <c>Column [2 1]</c>.
    /// </remarks>
    public static GridSpan? SpanOfBounds(Rect2D bounds, GridCells cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        int rows = cells.Rows;
        int columns = cells.Columns;
        if (rows == 0 || columns == 0)
        {
            return null;
        }

        if (bounds.Right < cells.Left(0) || bounds.Left > cells.Left(columns - 1) + cells.Width(columns - 1)
            || bounds.Bottom < cells.Top(0) || bounds.Top > cells.Top(rows - 1) + cells.Height(rows - 1))
        {
            return null;
        }

        (int firstColumn, int lastColumn) = EdgeCells(bounds.Left, bounds.Right, columns, cells.Left, cells.Width, cells.ColumnSpacing);
        (int firstRow, int lastRow) = EdgeCells(bounds.Top, bounds.Bottom, rows, cells.Top, cells.Height, cells.RowSpacing);
        return new GridSpan(firstRow + 1, lastRow + 1, firstColumn + 1, lastColumn + 1);
    }

    /// <summary>
    /// The cells of each box of a moved selection, each by <see cref="SpanOfBounds"/>
    /// (<c>calculateGridConstraintsListByBoundsListAndCells</c>); null when any box touches no cell.
    /// </summary>
    public static IReadOnlyList<GridSpan>? SpansOfBounds(IReadOnlyList<Rect2D> bounds, GridCells cells)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(cells);
        var spans = new GridSpan[bounds.Count];
        for (int k = 0; k < bounds.Count; k++)
        {
            if (SpanOfBounds(bounds[k], cells) is not { } span)
            {
                return null;
            }

            spans[k] = span;
        }

        return spans;
    }

    /// <summary>
    /// One axis of <c>GridUtil</c>'s <c>P</c>: the 0-based first and last cells of the edges
    /// <paramref name="start"/> and <paramref name="end"/>, over <paramref name="count"/> tracks.
    /// </summary>
    private static (int First, int Last) EdgeCells(
        double start, double end, int count, Func<int, double> at, Func<int, double> size, double spacing)
    {
        int first = -1;
        int last = -1;
        if (start <= Centre(0))
        {
            first = 0;
        }
        else
        {
            int low = 0;
            int high = count - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;

                // Divergence: App Designer goes left at <=, leaving the midline in no zone.
                if (start < at(middle) - (spacing / 2))
                {
                    high = middle - 1;
                }
                else if (start >= at(middle) + size(middle) + (spacing / 2))
                {
                    low = middle + 1;
                }
                else
                {
                    first = middle;
                    break;
                }
            }
        }

        if (end >= Centre(count - 1))
        {
            last = count - 1;
        }
        else
        {
            int low = 0;
            int high = count - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;

                // Divergence: App Designer goes right at >=, leaving the midline in no zone.
                if (end <= at(middle) - (spacing / 2))
                {
                    high = middle - 1;
                }
                else if (end > at(middle) + size(middle) + (spacing / 2))
                {
                    low = middle + 1;
                }
                else
                {
                    last = middle;
                    break;
                }
            }
        }

        if (first == -1)
        {
            first = 0;
        }

        if (last == -1)
        {
            last = 0;
        }

        // Divergence: a box of no width on a midline - its start edge in the later cell, its end
        // edge in the earlier - lies in the gap, and takes both cells.
        if (first > last)
        {
            (first, last) = (last, first);
        }

        if (first != last && !(start > Centre(first) && end < Centre(last) && last - first == 1))
        {
            if (start > Centre(first))
            {
                first++;
            }

            if (end < Centre(last))
            {
                last--;
            }
        }

        return (first, last);

        double Centre(int index) => at(index) + (size(index) / 2);
    }

    // --- labelled components -----------------------------------------------------------------------

    /// <summary>
    /// The cells a labelled component's label and field take when the pair is dropped or moved into a
    /// grid (<c>CellBoundsValidator</c>, its labelled branch, placement spec 6.4): each box's own cells
    /// by <see cref="SpanOfBounds"/>, then the two put side by side as the label stands to the field -
    /// a label left of the field takes the first of its columns and the field starts one after it, a
    /// label right of it its last column and the field ends one before, a label above or below
    /// likewise by rows - and where the boxes overlap on an axis, both take the cells either takes.
    /// Null when either box touches no cell or the pair would leave the grid.
    /// </summary>
    /// <remarks>
    /// App Designer's code holds a tie-break for a label overlapping its field on both axes (by which
    /// way their centres are further apart), but it tests the two sides by names neither side ever
    /// takes, so it never runs: an overlapping label and field share their cells, as here.
    /// </remarks>
    public static (GridSpan Label, GridSpan Field)? LabeledSpans(Rect2D label, Rect2D field, GridCells cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (SpanOfBounds(label, cells) is not { } l || SpanOfBounds(field, cells) is not { } u)
        {
            return null;
        }

        int lr0 = l.RowStart;
        int lr1 = l.RowEnd;
        int lc0 = l.ColumnStart;
        int lc1 = l.ColumnEnd;
        int ur0 = u.RowStart;
        int ur1 = u.RowEnd;
        int uc0 = u.ColumnStart;
        int uc1 = u.ColumnEnd;

        bool labelBelow = label.Top > field.Bottom;
        bool labelAbove = !labelBelow && label.Bottom < field.Top;
        bool labelRight = label.Left > field.Right;
        bool labelLeft = !labelRight && label.Right < field.Left;

        if (labelLeft)
        {
            if (lc0 + 1 <= cells.Columns)
            {
                lc1 = lc0;
                uc0 = lc0 + 1;
                uc1 = System.Math.Max(uc0, uc1);
            }
            else
            {
                lc0--;
                lc1 = lc0;
            }
        }
        else if (labelRight)
        {
            lc0 = lc1;
            uc1 = lc1 - 1;
            uc0 = System.Math.Min(uc0, uc1);
        }
        else
        {
            lc0 = System.Math.Min(lc0, uc0);
            lc1 = System.Math.Max(lc1, uc1);
            uc0 = lc0;
            uc1 = lc1;
        }

        if (labelAbove)
        {
            lr1 = lr0;
            ur0 = lr0 + 1;
            ur1 = System.Math.Max(ur0, ur1);
        }
        else if (labelBelow)
        {
            if (lr1 - 1 > 0)
            {
                lr0 = lr1;
                ur1 = lr1 - 1;
                ur0 = System.Math.Min(ur0, ur1);
            }
            else
            {
                lr0++;
                lr1 = lr0;
            }
        }
        else
        {
            lr0 = System.Math.Min(lr0, ur0);
            lr1 = System.Math.Max(lr1, ur1);
            ur0 = lr0;
            ur1 = lr1;
        }

        if (!Within(lr0, lr1, lc0, lc1) || !Within(ur0, ur1, uc0, uc1))
        {
            return null;
        }

        return (new GridSpan(lr0, lr1, lc0, lc1), new GridSpan(ur0, ur1, uc0, uc1));

        bool Within(int r0, int r1, int c0, int c1) => r0 >= 1 && r1 <= cells.Rows && c0 >= 1 && c1 <= cells.Columns;
    }

    /// <summary>
    /// The label's cells a labelled library entry gets from its field's when it is dropped into a grid
    /// (<c>LabeledComponentEntryDefinition._adjustLabelLayoutContraints</c>): the field's span moved by
    /// the entry's label offset, <c>[-1 0]</c> (column, row) for a label to the west
    /// (<c>westmiddle</c>, <c>westtop</c>) and <c>[0 1]</c> for one to the south
    /// (<c>southcenter</c>). On an axis the offset moves, the label takes one track, next to the
    /// field's first (a negative offset) or last (a positive one); on the other it keeps the field's
    /// span. Null when that leaves the grid of <paramref name="rows"/> by <paramref name="columns"/>
    /// (App Designer writes it unchecked, its <c>CellBoundsValidator</c> having refused beforehand a
    /// drop whose pair would leave the grid).
    /// </summary>
    public static GridSpan? LabelFromField(GridSpan field, int dColumn, int dRow, int rows, int columns)
    {
        (int r0, int r1) = Adjust(field.RowStart, field.RowEnd, dRow);
        (int c0, int c1) = Adjust(field.ColumnStart, field.ColumnEnd, dColumn);
        if (r0 < 1 || r1 > rows || c0 < 1 || c1 > columns)
        {
            return null;
        }

        return new GridSpan(r0, r1, c0, c1);

        static (int, int) Adjust(int start, int end, int offset)
        {
            if (offset == 0)
            {
                return (start, end);
            }

            int at = (offset > 0 ? end : start) + offset;
            return (at, at);
        }
    }

    // --- points ------------------------------------------------------------------------------------

    /// <summary>
    /// The cell a point is in (<c>MousePositionValidator</c> over the grid's cell areas,
    /// <c>Areas.testContainsPoint</c>): the first, row by row, whose box holds it, edges included;
    /// null in a gap, in the padding and outside.
    /// </summary>
    public static GridSpan? CellUnderPoint(Point2D point, GridCells cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        for (int r = 0; r < cells.Rows; r++)
        {
            for (int c = 0; c < cells.Columns; c++)
            {
                if (point.X >= cells.Left(c) && point.X <= cells.Left(c) + cells.Width(c)
                    && point.Y >= cells.Top(r) && point.Y <= cells.Top(r) + cells.Height(r))
                {
                    return GridSpan.Cell(r + 1, c + 1);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The cells of a labelled entry's label and field when it is placed by the pointer
    /// (<c>CellMousePositionValidator</c>, placement spec 8), the pointer in
    /// <paramref name="pointerCell"/> (its first cell) and the entry's label offset
    /// (<paramref name="dColumn"/>, <paramref name="dRow"/>): in a click-and-draw with a label to the
    /// west or north the pointer's cell is the label's and the field is the next one, both moved back
    /// one when the field would pass the last track; otherwise the pointer's cell is the field's and
    /// the label is beside it, both moved on one when the label would fall before the first track.
    /// Null when either then lies past the grid's last row or column, or before its first.
    /// </summary>
    /// <remarks>
    /// Divergence: App Designer refuses only a cell past the last track. In a click-and-draw with the
    /// label to the west in a grid of one column (or to the north in one of one row), moving both back
    /// one puts the label in column (row) 0, which it accepts, draws and writes, and which MATLAB
    /// refuses (a layout's row and column are positive). Here that is refused like a cell past the
    /// last track, as App Designer's validator refuses the same entry at the same cell outside a draw.
    /// </remarks>
    public static (GridSpan Label, GridSpan Field)? LabeledClick(
        GridSpan pointerCell, int dColumn, int dRow, bool componentDraw, int rows, int columns)
    {
        int labelRow;
        int labelColumn;
        int fieldRow;
        int fieldColumn;
        if (componentDraw && (dColumn < 0 || dRow < 0))
        {
            labelRow = pointerCell.RowStart;
            labelColumn = pointerCell.ColumnStart;
            fieldRow = labelRow - dRow;
            fieldColumn = labelColumn - dColumn;
            if (fieldRow > rows)
            {
                labelRow--;
                fieldRow--;
            }

            if (fieldColumn > columns)
            {
                labelColumn--;
                fieldColumn--;
            }
        }
        else
        {
            fieldRow = pointerCell.RowStart;
            fieldColumn = pointerCell.ColumnStart;
            labelRow = fieldRow + dRow;
            labelColumn = fieldColumn + dColumn;
            if (labelRow == 0)
            {
                fieldRow++;
                labelRow++;
            }

            if (labelColumn == 0)
            {
                fieldColumn++;
                labelColumn++;
            }
        }

        if (labelRow > rows || fieldRow > rows || labelColumn > columns || fieldColumn > columns)
        {
            return null;
        }

        // Divergence: App Designer accepts a cell before the first track (see the remarks).
        if (labelRow < 1 || fieldRow < 1 || labelColumn < 1 || fieldColumn < 1)
        {
            return null;
        }

        return (GridSpan.Cell(labelRow, labelColumn), GridSpan.Cell(fieldRow, fieldColumn));
    }

    /// <summary>
    /// What the grid's context menu reads under the pointer (<c>GridUtil.getColumnRowUnderPoint</c>,
    /// commands spec 3.1): the first row whose band holds the point (rows read in the first column)
    /// and the first such column (read in the first row), edges included; failing a row, the gap
    /// between two rows that holds it (edges excluded), and columns the same; and failing a row gap,
    /// the top padding band (from the padding's outer edge, included, to the first row) or the bottom
    /// one, and columns the same. A grid without rows or columns has nothing under any point (App
    /// Designer's menu does not look there).
    /// </summary>
    /// <remarks>
    /// Divergence: App Designer tests the bottom and right padding bands from the last track's top and
    /// left (<c>W&gt;G[H-1][0].y&amp;&amp;W&lt;=G[H-1][0].y+K[2]</c>), so a point in the real bottom or
    /// right padding finds nothing and Add Row or Add Column is disabled there, while a point in the
    /// last row's top band reports the padding too (commands spec 3.1). Here the bands start at the
    /// last track's bottom and right edges, so the bottom padding adds a row after the last and the
    /// right padding a column.
    /// </remarks>
    public static GridPointInfo PointInfo(Point2D point, GridCells cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        int rows = cells.Rows;
        int columns = cells.Columns;
        if (rows == 0 || columns == 0)
        {
            return default;
        }

        double x = point.X;
        double y = point.Y;
        int? row = null;
        int? column = null;
        (int, int)? rowSpacing = null;
        (int, int)? columnSpacing = null;
        int? rowPadding = null;
        int? columnPadding = null;

        for (int k = 0; k < rows; k++)
        {
            if (y >= cells.Top(k) && y <= cells.Top(k) + cells.Height(k))
            {
                row = k + 1;
                break;
            }
        }

        for (int k = 0; k < columns; k++)
        {
            if (x >= cells.Left(k) && x <= cells.Left(k) + cells.Width(k))
            {
                column = k + 1;
                break;
            }
        }

        if (row is null)
        {
            for (int k = 0; k < rows - 1; k++)
            {
                if (y > cells.Top(k) + cells.Height(k) && y < cells.Top(k + 1))
                {
                    rowSpacing = (k + 1, k + 2);
                    break;
                }
            }
        }

        if (column is null)
        {
            for (int k = 0; k < columns - 1; k++)
            {
                if (x > cells.Left(k) + cells.Width(k) && x < cells.Left(k + 1))
                {
                    columnSpacing = (k + 1, k + 2);
                    break;
                }
            }
        }

        // The design-time widget keeps padding in CSS order, [top right bottom left]
        // (GridController._Padding); MATLAB's is [left bottom right top].
        double padTop = cells.Padding[3];
        double padRight = cells.Padding[2];
        double padBottom = cells.Padding[1];
        double padLeft = cells.Padding[0];
        if (rowSpacing is null)
        {
            double lastBottom = cells.Top(rows - 1) + cells.Height(rows - 1);
            if (y >= cells.Top(0) - padTop && y < cells.Top(0))
            {
                rowPadding = 1;
            }
            else if (y > lastBottom && y <= lastBottom + padBottom)
            {
                rowPadding = rows + 1;
            }
        }

        if (columnSpacing is null)
        {
            double lastRight = cells.Left(columns - 1) + cells.Width(columns - 1);
            if (x >= cells.Left(0) - padLeft && x < cells.Left(0))
            {
                columnPadding = 1;
            }
            else if (x > lastRight && x <= lastRight + padRight)
            {
                columnPadding = columns + 1;
            }
        }

        return new GridPointInfo(row, column, rowSpacing, columnSpacing, rowPadding, columnPadding);
    }

    /// <summary>
    /// Where Add Row's Above and Below insert a row (<c>GridUtil.parseAddingInfo</c>): on a row, at it
    /// and after it; in the gap between two rows, between them for both; in a padding band, there for
    /// both; else nowhere (the items are disabled).
    /// </summary>
    public static (int? Above, int? Below) AddingRows(GridPointInfo info)
    {
        if (info.Row is { } row)
        {
            return (row, row + 1);
        }

        if (info.RowSpacing is { } gap)
        {
            return (gap.Item2, gap.Item2);
        }

        return (info.RowPadding, info.RowPadding);
    }

    /// <summary>Where Add Column's Left and Right insert a column (<c>GridUtil.parseAddingInfo</c>); see <see cref="AddingRows"/>.</summary>
    public static (int? Before, int? After) AddingColumns(GridPointInfo info)
    {
        if (info.Column is { } column)
        {
            return (column, column + 1);
        }

        if (info.ColumnSpacing is { } gap)
        {
            return (gap.Item2, gap.Item2);
        }

        return (info.ColumnPadding, info.ColumnPadding);
    }

    /// <summary>
    /// Which row Delete Row and which column Delete Column take (<c>GridUtil.parseDeletingInfo</c>):
    /// the ones under the pointer, or none (the item is disabled).
    /// </summary>
    public static (int? Row, int? Column) Deleting(GridPointInfo info) => (info.Row, info.Column);

    // --- adding and deleting tracks ----------------------------------------------------------------

    /// <summary>
    /// A child's span once a track is inserted at <paramref name="index"/>, the old track there and
    /// those after it moving on one (<c>GridUtil.updateRowByAddingNewRow</c>,
    /// <c>updateColumnByAddingNewColumn</c>): an end at or after the index moves on, so a span across
    /// the insertion grows by one and one starting there moves.
    /// </summary>
    public static GridSpan WithTrackInserted(GridSpan span, GridAxis axis, int index)
    {
        if (axis == GridAxis.Row)
        {
            return span with { RowStart = Shift(span.RowStart), RowEnd = Shift(span.RowEnd) };
        }

        return span with { ColumnStart = Shift(span.ColumnStart), ColumnEnd = Shift(span.ColumnEnd) };

        int Shift(int at) => at >= index ? at + 1 : at;
    }

    /// <summary>
    /// A child's span once the track at <paramref name="index"/> is deleted
    /// (<c>GridUtil.updateRowByRemovingRow</c>, <c>updateColumnByRemovingColumn</c>): a start after
    /// it and an end at or after it move back one, so a span over it shrinks by one. Null for a child
    /// in that track alone, which App Designer deletes with the track
    /// (<c>getPeerNodesNeedToDeleteRow</c>, <c>DeleteRowCommand</c>).
    /// </summary>
    public static GridSpan? WithTrackRemoved(GridSpan span, GridAxis axis, int index)
    {
        if (axis == GridAxis.Row)
        {
            if (span.RowStart == index && span.RowEnd == index)
            {
                return null;
            }

            return span with { RowStart = span.RowStart > index ? span.RowStart - 1 : span.RowStart, RowEnd = span.RowEnd >= index ? span.RowEnd - 1 : span.RowEnd };
        }

        if (span.ColumnStart == index && span.ColumnEnd == index)
        {
            return null;
        }

        return span with { ColumnStart = span.ColumnStart > index ? span.ColumnStart - 1 : span.ColumnStart, ColumnEnd = span.ColumnEnd >= index ? span.ColumnEnd - 1 : span.ColumnEnd };
    }

    // --- click-and-draw ----------------------------------------------------------------------------

    /// <summary>
    /// The span a click-and-draw inside a grid gives (<c>beginComponentDraw</c>,
    /// <c>_HandleDoingComponentDraw</c>): the grid-child resize's arithmetic (<see cref="GridSpanResize"/>)
    /// run on a box of no size at the press, in <paramref name="startCell"/> (the cell
    /// <see cref="CellUnderPoint"/> gave the press), its handle the corner the pointer is toward -
    /// south when below the press, else north; east when right of it, else west.
    /// </summary>
    public static GridSpan DrawnSpan(GridCells cells, GridSpan startCell, Point2D press, Point2D pointer)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var pressBox = new Rect2D(press.X, press.Y, 0, 0);
        var snapping = new GridSpanSnapping(cells, [new GridResizeEntry(startCell, pressBox, CanResize: true, IsLabel: false)]);
        (IReadOnlyList<GridSpan> spans, _) = snapping.Drag(DrawEdges(press, pointer), pointer.X - press.X, pointer.Y - press.Y);
        return spans[0];
    }

    /// <summary>
    /// The label's and the field's cells a click-and-draw of a labelled entry gives
    /// (<c>beginComponentDraw</c>, <c>_HandleDoingComponentDraw</c>): from the cells
    /// <see cref="LabeledClick"/> gave the press, the pair's span (the label's cell to the field's)
    /// grows as <see cref="DrawnSpan"/>'s does, the field's with it within the same limits, and the
    /// label takes the pair's first cell (a label offset toward the west or north) or its last (one
    /// toward the east or south).
    /// </summary>
    public static (GridSpan Label, GridSpan Field) DrawnLabeledSpans(
        GridCells cells, GridSpan label, GridSpan field, int dColumn, int dRow, Point2D press, Point2D pointer)
    {
        ArgumentNullException.ThrowIfNull(cells);
        bool before = dColumn < 0 || dRow < 0;
        GridSpan pair = before
            ? new GridSpan(label.RowStart, field.RowStart, label.ColumnStart, field.ColumnStart)
            : new GridSpan(field.RowStart, label.RowStart, field.ColumnStart, label.ColumnStart);
        var pressBox = new Rect2D(press.X, press.Y, 0, 0);
        var snapping = new GridSpanSnapping(cells,
        [
            new GridResizeEntry(pair, pressBox, CanResize: true, IsLabel: false),
            new GridResizeEntry(GridSpan.Cell(label.RowStart, label.ColumnStart), pressBox, CanResize: false, IsLabel: false),
            new GridResizeEntry(GridSpan.Cell(field.RowStart, field.ColumnStart), pressBox, CanResize: true, IsLabel: false),
        ]);
        (IReadOnlyList<GridSpan> spans, _) = snapping.Drag(DrawEdges(press, pointer), pointer.X - press.X, pointer.Y - press.Y);
        GridSpan drawn = spans[0];
        GridSpan labelSpan = spans[1];
        if (before)
        {
            labelSpan = GridSpan.Cell(drawn.RowStart, drawn.ColumnStart);
        }
        else if (dColumn > 0 || dRow > 0)
        {
            labelSpan = GridSpan.Cell(drawn.RowEnd, drawn.ColumnEnd);
        }

        return (labelSpan, spans[2]);
    }

    /// <summary>The handle a draw drags (<c>_HandleDoingComponentDraw</c>'s local <c>s</c>): the corner the pointer is toward.</summary>
    private static ResizeEdges DrawEdges(Point2D press, Point2D pointer) =>
        (pointer.Y > press.Y ? ResizeEdges.South : ResizeEdges.North) | (pointer.X > press.X ? ResizeEdges.East : ResizeEdges.West);
}

/// <summary>On which side of its field a labelled component's label stands, on one axis.</summary>
public enum GridLabelSide
{
    /// <summary>Neither side: the two overlap on this axis.</summary>
    None,

    /// <summary>Left of the field.</summary>
    West,

    /// <summary>Right of the field.</summary>
    East,

    /// <summary>Above the field.</summary>
    North,

    /// <summary>Below the field.</summary>
    South,
}

/// <summary>
/// One component a grid-child resize moves (<c>BeginGridChildResize</c>): the one whose handle was
/// grabbed, or a labelled pair's group, label and field.
/// </summary>
/// <param name="Span">Its cells when the resize began.</param>
/// <param name="Box">Its box on the canvas when the resize began.</param>
/// <param name="CanResize">Whether its span grows and shrinks with the dragged edge (the component or pair, and a pair's field).</param>
/// <param name="IsLabel">Whether it is a pair's label, which moves with the dragged edge only on its own side.</param>
/// <param name="Horizontal">A label's side of its field across: <see cref="GridLabelSide.West"/> or <see cref="GridLabelSide.East"/>.</param>
/// <param name="Vertical">A label's side of its field up and down: <see cref="GridLabelSide.North"/> or <see cref="GridLabelSide.South"/>.</param>
public sealed record GridResizeEntry(
    GridSpan Span,
    Rect2D Box,
    bool CanResize,
    bool IsLabel,
    GridLabelSide Horizontal = GridLabelSide.None,
    GridLabelSide Vertical = GridLabelSide.None);

/// <summary>
/// Resizing a grid child's span by a handle (designer plan DS4), as App Designer's gridchildresize
/// gesture does it (<c>SnappingDataBuilder</c>, <c>SnapIndexCalculator</c>,
/// <c>GridChildResizeHelpers</c>, driven by <c>_HandleDoingGridChildResize</c>; childresize spec
/// sections 5 and 6). The dragged edge's travel is clamped so the span stays inside the grid and
/// keeps one track, and the moved edge snaps to the nearest track edge of its own kind - a right
/// edge to columns' right edges, a left edge to their left edges - the switch between two at their
/// midpoint, a pointer exactly there going to the later track. Only the grabbed component (or its
/// labelled pair) changes; other children are not read, so spans may overlap.
/// </summary>
/// <remarks>
/// The limits are App Designer's to the pixel, asymmetric as its code has them: growing outward is
/// measured from the component's own box, shrinking inward from its cells' edges, which differ only
/// for a component not filling its cells. Each drag is from the press (not cumulative).
/// </remarks>
public sealed class GridSpanResize
{
    private readonly GridSpanSnapping snapping;
    private readonly ResizeEdges edges;

    /// <summary>
    /// A resize of <paramref name="entries"/> (the first the component whose handle was grabbed; for a
    /// labelled pair, the pair, its label and its field, <see cref="LabeledEntries"/>) by the handle
    /// <paramref name="edges"/> (none is App Designer's default, south-east).
    /// </summary>
    public GridSpanResize(GridCells cells, ResizeEdges edges, IReadOnlyList<GridResizeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(entries);
        snapping = new GridSpanSnapping(cells, entries);
        this.edges = GridSpanSnapping.Handle(edges);
    }

    /// <summary>
    /// The three entries of a labelled pair's resize as <c>BeginGridChildResize</c> gathers them: the
    /// pair, its span the cells both members take (<c>GroupUtil.calculateGroupSizeProperties</c>) and
    /// its box <paramref name="pairBox"/>; the label, on the side of the field its box stands on
    /// (strictly clear of the field's box, else neither); the field.
    /// </summary>
    public static IReadOnlyList<GridResizeEntry> LabeledEntries(GridSpan label, Rect2D labelBox, GridSpan field, Rect2D fieldBox, Rect2D pairBox)
    {
        var pair = new GridSpan(
            System.Math.Min(label.RowStart, field.RowStart),
            System.Math.Max(label.RowEnd, field.RowEnd),
            System.Math.Min(label.ColumnStart, field.ColumnStart),
            System.Math.Max(label.ColumnEnd, field.ColumnEnd));
        GridLabelSide horizontal = labelBox.Right < fieldBox.X ? GridLabelSide.West
            : labelBox.X > fieldBox.Right ? GridLabelSide.East : GridLabelSide.None;
        GridLabelSide vertical = labelBox.Bottom < fieldBox.Y ? GridLabelSide.North
            : labelBox.Y > fieldBox.Bottom ? GridLabelSide.South : GridLabelSide.None;
        return
        [
            new GridResizeEntry(pair, pairBox, CanResize: true, IsLabel: false),
            new GridResizeEntry(label, labelBox, CanResize: false, IsLabel: true, horizontal, vertical),
            new GridResizeEntry(field, fieldBox, CanResize: true, IsLabel: false),
        ];
    }

    /// <summary>Each entry's span after a drag of (<paramref name="dx"/>, <paramref name="dy"/>) from the press.</summary>
    public IReadOnlyList<GridSpan> Spans(double dx, double dy) => snapping.Drag(edges, dx, dy).Spans;

    /// <summary>
    /// Each entry's box as App Designer previews it during the drag: the dragged edge where the
    /// clamped pointer is, not snapped (a label on that side moved with it).
    /// </summary>
    public IReadOnlyList<Rect2D> LiveBoxes(double dx, double dy) => snapping.Drag(edges, dx, dy).Live;
}

/// <summary>
/// The arithmetic under <see cref="GridSpanResize"/> and <see cref="DesignGridRules.DrawnSpan"/>:
/// App Designer's <c>SnappingDataBuilder.buildSnappingData</c> at the start, and per drag
/// <c>GridChildResizeHelpers.adjustingOffsets</c>, <c>updatePositionAndBounds</c>,
/// <c>SnapIndexCalculator.calculateSnapIndexes</c> and <c>updateByIndexOffset</c>.
/// </summary>
internal sealed class GridSpanSnapping
{
    private readonly GridResizeEntry[] entries;
    private readonly double[] eastEdges;
    private readonly double[] westEdges;
    private readonly double[] northEdges;
    private readonly double[] southEdges;
    private readonly int minEastIndex;
    private readonly int maxWestIndex;
    private readonly int minSouthIndex;
    private readonly int maxNorthIndex;
    private readonly double minWestPixels;
    private readonly double maxWestPixels;
    private readonly double minEastPixels;
    private readonly double maxEastPixels;
    private readonly double minNorthPixels;
    private readonly double maxNorthPixels;
    private readonly double minSouthPixels;
    private readonly double maxSouthPixels;
    private readonly int eastStart;
    private readonly int westEnd;
    private readonly int northEnd;
    private readonly int southStart;
    private readonly int eastOriginal;
    private readonly int westOriginal;
    private readonly int northOriginal;
    private readonly int southOriginal;

    internal GridSpanSnapping(GridCells cells, IReadOnlyList<GridResizeEntry> entries)
    {
        if (entries.Count == 0 || !entries.Any(static e => e.CanResize))
        {
            throw new ArgumentException("A resize needs a component whose span can change.", nameof(entries));
        }

        foreach (GridResizeEntry entry in entries)
        {
            GridSpan s = entry.Span;
            if (s.RowStart < 1 || s.RowStart > s.RowEnd || s.RowEnd > cells.Rows
                || s.ColumnStart < 1 || s.ColumnStart > s.ColumnEnd || s.ColumnEnd > cells.Columns)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), s, $"A span outside the {cells.Rows}-by-{cells.Columns} grid.");
            }
        }

        this.entries = entries.ToArray();

        // The edges a moved edge can snap to: right and left edges of the columns read in the first
        // row, tops and bottoms of the rows read in the first column.
        eastEdges = Enumerable.Range(0, cells.Columns).Select(c => cells.Left(c) + cells.Width(c)).ToArray();
        westEdges = Enumerable.Range(0, cells.Columns).Select(cells.Left).ToArray();
        northEdges = Enumerable.Range(0, cells.Rows).Select(cells.Top).ToArray();
        southEdges = Enumerable.Range(0, cells.Rows).Select(r => cells.Top(r) + cells.Height(r)).ToArray();

        // The index limits, over every entry whose span changes: the span stays in the grid and keeps a track.
        GridResizeEntry[] resizing = this.entries.Where(static e => e.CanResize).ToArray();
        int minWestIndex = int.MinValue;
        int maxEastIndex = int.MaxValue;
        int minNorthIndex = int.MinValue;
        int maxSouthIndex = int.MaxValue;
        minEastIndex = int.MinValue;
        maxWestIndex = int.MaxValue;
        minSouthIndex = int.MinValue;
        maxNorthIndex = int.MaxValue;
        foreach (GridResizeEntry entry in resizing)
        {
            GridSpan s = entry.Span;
            minWestIndex = System.Math.Max(minWestIndex, 1 - s.ColumnStart);
            maxWestIndex = System.Math.Min(maxWestIndex, s.ColumnEnd - s.ColumnStart);
            minEastIndex = System.Math.Max(minEastIndex, s.ColumnStart - s.ColumnEnd);
            maxEastIndex = System.Math.Min(maxEastIndex, cells.Columns - s.ColumnEnd);
            minNorthIndex = System.Math.Max(minNorthIndex, 1 - s.RowStart);
            maxNorthIndex = System.Math.Min(maxNorthIndex, s.RowEnd - s.RowStart);
            minSouthIndex = System.Math.Max(minSouthIndex, s.RowStart - s.RowEnd);
            maxSouthIndex = System.Math.Min(maxSouthIndex, cells.Rows - s.RowEnd);
        }

        // The pixel limits: outward from the component's own box, inward from its cells' edges.
        GridSpan adjusting = this.entries[0].Span;
        int rs = adjusting.RowStart;
        int cs = adjusting.ColumnStart;
        int re = adjusting.RowEnd;
        int ce = adjusting.ColumnEnd;
        Rect2D g0 = resizing[0].Box;
        minWestPixels = At(westEdges, cs + minWestIndex - 1) - At(westEdges, cs - 1);
        minWestPixels += At(westEdges, cs - 1) - g0.X;
        maxWestPixels = At(westEdges, cs + maxWestIndex - 1) - At(westEdges, cs - 1);
        minEastPixels = At(eastEdges, ce + minEastIndex - 1) - At(eastEdges, ce - 1);
        maxEastPixels = At(eastEdges, ce + maxEastIndex - 1) - At(eastEdges, ce - 1);
        maxEastPixels += At(eastEdges, ce - 1) - (g0.X + g0.Width);
        minNorthPixels = At(northEdges, rs + minNorthIndex - 1) - At(northEdges, rs - 1);
        minNorthPixels += At(northEdges, rs - 1) - g0.Y;
        maxNorthPixels = At(northEdges, rs + maxNorthIndex - 1) - At(northEdges, rs - 1);
        minSouthPixels = At(southEdges, re + minSouthIndex - 1) - At(southEdges, re - 1);
        maxSouthPixels = At(southEdges, re + maxSouthIndex - 1) - At(southEdges, re - 1);
        maxSouthPixels += At(southEdges, re - 1) - (g0.Y + g0.Height);

        // Where each snap search starts or ends: the first right edge past every box's left, the last
        // left edge short of every box's right, and the rows likewise.
        Rect2D[] boxes = resizing.Select(static e => e.Box).ToArray();
        eastStart = FirstIndex(eastEdges, v => boxes.All(b => v > b.X));
        westEnd = LastIndex(westEdges, v => boxes.All(b => v < b.X + b.Width));
        northEnd = LastIndex(northEdges, v => boxes.All(b => v < b.Y + b.Height));
        southStart = FirstIndex(southEdges, v => boxes.All(b => v > b.Y));
        eastOriginal = ce - 1;
        westOriginal = cs - 1;
        northOriginal = rs - 1;
        southOriginal = re - 1;
    }

    /// <summary>Each entry's span and live box after a drag of (<paramref name="dx"/>, <paramref name="dy"/>) by <paramref name="edges"/>.</summary>
    internal (IReadOnlyList<GridSpan> Spans, IReadOnlyList<Rect2D> Live) Drag(ResizeEdges edges, double dx, double dy)
    {
        edges = Handle(edges);
        bool north = edges.HasFlag(ResizeEdges.North);
        bool south = edges.HasFlag(ResizeEdges.South);
        bool east = edges.HasFlag(ResizeEdges.East);
        bool west = edges.HasFlag(ResizeEdges.West);

        // adjustingOffsets: the moved edges' travel within the limits (the lower first, so the upper wins).
        if (north)
        {
            dy = System.Math.Max(minNorthPixels, dy);
            dy = System.Math.Min(maxNorthPixels, dy);
        }
        else if (south)
        {
            dy = System.Math.Max(minSouthPixels, dy);
            dy = System.Math.Min(maxSouthPixels, dy);
        }

        if (west)
        {
            dx = System.Math.Max(minWestPixels, dx);
            dx = System.Math.Min(maxWestPixels, dx);
        }
        else if (east)
        {
            dx = System.Math.Max(minEastPixels, dx);
            dx = System.Math.Min(maxEastPixels, dx);
        }

        // updatePositionAndBounds: the grabbed box with its edges moved (its right and bottom kept
        // apart from its width and height, as App Designer's bounds keep them), and the live boxes.
        Rect2D start = entries[0].Box;
        double bx = start.X;
        double by = start.Y;
        double br = start.X + start.Width;
        double bb = start.Y + start.Height;
        Live[] live = entries.Select(static e => new Live(e.Box.X, e.Box.Y, e.Box.Width, e.Box.Height)).ToArray();
        if (north)
        {
            by += dy;
            for (int k = 0; k < live.Length; k++)
            {
                if (entries[k].CanResize)
                {
                    live[k] = live[k] with { Y = live[k].Y + dy, H = live[k].H - dy };
                }
                else if (entries[k].IsLabel && entries[k].Vertical == GridLabelSide.North)
                {
                    live[k] = live[k] with { Y = live[k].Y + dy };
                }
            }
        }
        else if (south)
        {
            bb += dy;
            for (int k = 0; k < live.Length; k++)
            {
                if (entries[k].CanResize)
                {
                    live[k] = live[k] with { H = live[k].H + dy };
                }
                else if (entries[k].IsLabel && entries[k].Vertical == GridLabelSide.South)
                {
                    live[k] = live[k] with { Y = live[k].Y + dy };
                }
            }
        }

        if (west)
        {
            bx += dx;
            for (int k = 0; k < live.Length; k++)
            {
                if (entries[k].CanResize)
                {
                    live[k] = live[k] with { X = live[k].X + dx, W = live[k].W - dx };
                }
                else if (entries[k].IsLabel && entries[k].Horizontal == GridLabelSide.West)
                {
                    live[k] = live[k] with { X = live[k].X + dx };
                }
            }
        }
        else if (east)
        {
            br += dx;
            for (int k = 0; k < live.Length; k++)
            {
                if (entries[k].CanResize)
                {
                    live[k] = live[k] with { W = live[k].W + dx };
                }
                else if (entries[k].IsLabel && entries[k].Horizontal == GridLabelSide.East)
                {
                    live[k] = live[k] with { X = live[k].X + dx };
                }
            }
        }

        // calculateSnapIndexes: how many tracks each moved edge went, 0 for the edges it leaves.
        int northOffset = north ? SnapBefore(by, northEdges, northEnd) - northOriginal : 0;
        int southOffset = south ? SnapAfter(bb, southEdges, southStart) - southOriginal : 0;
        int westOffset = west ? SnapBefore(bx, westEdges, westEnd) - westOriginal : 0;
        int eastOffset = east ? SnapAfter(br, eastEdges, eastStart) - eastOriginal : 0;

        // updateByIndexOffset, every entry, east, west, south then north: a resizing span moves its own
        // end, a label on that side moves whole.
        var spans = new GridSpan[entries.Length];
        for (int k = 0; k < entries.Length; k++)
        {
            GridResizeEntry entry = entries[k];
            GridSpan s = entry.Span;
            int c0 = s.ColumnStart;
            int c1 = s.ColumnEnd;
            int r0 = s.RowStart;
            int r1 = s.RowEnd;

            int i = System.Math.Max(minEastIndex, eastOffset);
            if (entry.CanResize)
            {
                c1 += i;
            }
            else if (entry.IsLabel && entry.Horizontal == GridLabelSide.East)
            {
                c0 += i;
                c1 += i;
            }

            i = System.Math.Min(maxWestIndex, westOffset);
            if (entry.CanResize)
            {
                c0 += i;
            }
            else if (entry.IsLabel && entry.Horizontal == GridLabelSide.West)
            {
                c0 += i;
                c1 += i;
            }

            i = System.Math.Max(minSouthIndex, southOffset);
            if (entry.CanResize)
            {
                r1 += i;
            }
            else if (entry.IsLabel && entry.Vertical == GridLabelSide.South)
            {
                r0 += i;
                r1 += i;
            }

            i = System.Math.Min(northOffset, maxNorthIndex);
            if (entry.CanResize)
            {
                r0 += i;
            }
            else if (entry.IsLabel && entry.Vertical == GridLabelSide.North)
            {
                r0 += i;
                r1 += i;
            }

            spans[k] = new GridSpan(r0, r1, c0, c1);
        }

        return (spans, live.Select(static l => new Rect2D(l.X, l.Y, l.W, l.H)).ToArray());
    }

    /// <summary>
    /// <c>SnapIndexCalculator</c> for a right or bottom edge at <paramref name="value"/>: from
    /// <paramref name="from"/> on, the first pair of edges holding it, the nearer of the two (the later
    /// at the midpoint); else the first edge if it is short of that, the last if past.
    /// </summary>
    private static int SnapAfter(double value, double[] edges, int from)
    {
        int found = -1;
        for (int a = from; a < edges.Length - 1; a++)
        {
            if (value >= At(edges, a) && value <= At(edges, a + 1))
            {
                found = value < (At(edges, a) + At(edges, a + 1)) / 2 ? a : a + 1;
                break;
            }
        }

        if (found == -1)
        {
            found = value < At(edges, from) ? from : edges.Length - 1;
        }

        return found;
    }

    /// <summary>
    /// <c>SnapIndexCalculator</c> for a left or top edge at <paramref name="value"/>: up to
    /// <paramref name="to"/>, the first pair of edges holding it, the nearer of the two (the later at
    /// the midpoint); else the first edge if it is short of the first, <paramref name="to"/> if not.
    /// </summary>
    private static int SnapBefore(double value, double[] edges, int to)
    {
        int found = -1;
        for (int a = 0; a <= to - 1; a++)
        {
            if (value >= At(edges, a) && value <= At(edges, a + 1))
            {
                found = value < (At(edges, a) + At(edges, a + 1)) / 2 ? a : a + 1;
                break;
            }
        }

        if (found == -1)
        {
            found = value < At(edges, 0) ? 0 : to;
        }

        return found;
    }

    /// <summary>
    /// A handle's edges as <c>_HandleInitGridChildResize</c> reads them: none is its default,
    /// south-east; a handle cannot move two opposite edges.
    /// </summary>
    internal static ResizeEdges Handle(ResizeEdges edges)
    {
        if (edges == ResizeEdges.None)
        {
            return ResizeEdges.SouthEast;
        }

        if ((edges.HasFlag(ResizeEdges.North) && edges.HasFlag(ResizeEdges.South))
            || (edges.HasFlag(ResizeEdges.East) && edges.HasFlag(ResizeEdges.West)))
        {
            throw new ArgumentException($"No handle moves opposite edges: {edges}.", nameof(edges));
        }

        return edges;
    }

    /// <summary>An edge by index, NaN past either end - what JavaScript's missing element compares and adds as.</summary>
    private static double At(double[] edges, int index) => index >= 0 && index < edges.Length ? edges[index] : double.NaN;

    private static int FirstIndex(double[] edges, Func<double, bool> test)
    {
        for (int k = 0; k < edges.Length; k++)
        {
            if (test(edges[k]))
            {
                return k;
            }
        }

        return -1;
    }

    private static int LastIndex(double[] edges, Func<double, bool> test)
    {
        for (int k = edges.Length - 1; k >= 0; k--)
        {
            if (test(edges[k]))
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>A live box during the drag.</summary>
    private readonly record struct Live(double X, double Y, double W, double H);
}
