namespace JGraph.Scripting.Jgs;

/// <summary>
/// Cell concatenation in a bracket literal: <c>[a, b]</c> and <c>[a; b]</c> where the pieces are cells.
/// </summary>
/// <remarks>
/// <para>
/// Before this, a cell in a bracket was measured as one element whatever its size, because the block
/// measurement asked only whether a piece was a numeric array. Two 1-by-1 cells joined into a 1-by-2
/// cell by accident, which is the right answer; every other case was wrong, and the one that mattered
/// was the empty cell. <c>[{}, {x}]</c> came back 1-by-<b>2</b> — a phantom leading element — which
/// made <c>acc = [acc, {value}]</c>, the ordinary way a MATLAB script grows a cell from nothing,
/// quietly build a list one longer than it should be. Found in M68, whose stress script grows a log
/// that way; it is not M68's own defect, and ADR 0068 records it as such.
/// </para>
/// <para>
/// The shape follows the struct-array rules M65 established, for the same reason: both are containers
/// whose elements are values rather than numbers, so neither can go through the numeric block
/// machinery. A cell joined with anything that is not a cell is refused, as MATLAB refuses it.
/// </para>
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>Whether any piece of a bracket literal is a cell, so the bracket joins cells.</summary>
    private static bool AnyCell(List<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.Type == JgsType.Cell)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Joins a bracket literal's rows of cells into one cell array.</summary>
    private JgsValue ConcatenateCells(IReadOnlyList<IReadOnlyList<JgsValue>> rows, Node at)
    {
        var joined = new List<JgsValue>(rows.Count);
        foreach (IReadOnlyList<JgsValue> row in rows)
        {
            joined.Add(JoinCellsAcross(row, at));
        }

        return StackCellRows(joined, at);
    }

    /// <summary>
    /// One row of a bracket: cells side by side. Storage is column-major, so appending one piece's
    /// elements after another's is exactly the join; an empty cell contributes nothing at all, which
    /// is what makes growing from <c>{}</c> work.
    /// </summary>
    private JgsValue JoinCellsAcross(IReadOnlyList<JgsValue> pieces, Node at)
    {
        var elements = new List<JgsValue>();
        int rows = -1;
        int cols = 0;
        foreach (JgsValue piece in pieces)
        {
            if (piece.Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"A cell can only be concatenated with another cell, not with a {piece.TypeName}; "
                    + "wrap the value in braces to make it one.");
            }

            JgsValue[] held = piece.AsCell;
            if (held.Length == 0)
            {
                continue;
            }

            if (rows < 0)
            {
                rows = piece.Rows;
            }
            else if (piece.Rows != rows)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Cells joined side by side must have the same number of rows, not {rows} and {piece.Rows}.");
            }

            elements.AddRange(held);
            cols += piece.Cols;
        }

        return ShapedCell(elements, rows < 0 ? 0 : rows, cols);
    }

    /// <summary>
    /// Stacks the rows of a bracket one above another. Column-major storage makes this an interleave
    /// rather than an append: the result's first column holds every row block's first column in turn.
    /// </summary>
    private JgsValue StackCellRows(IReadOnlyList<JgsValue> rows, Node at)
    {
        var kept = new List<JgsValue>(rows.Count);
        int cols = -1;
        int height = 0;
        foreach (JgsValue row in rows)
        {
            if (row.AsCell.Length == 0)
            {
                continue;
            }

            if (cols < 0)
            {
                cols = row.Cols;
            }
            else if (row.Cols != cols)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Cells stacked one above another must have the same number of columns, not {cols} and {row.Cols}.");
            }

            kept.Add(row);
            height += row.Rows;
        }

        if (kept.Count == 1)
        {
            return kept[0];
        }

        if (kept.Count == 0 || cols <= 0)
        {
            return ShapedCell([], 0, 0);
        }

        var stacked = new JgsValue[height * cols];
        for (int column = 0; column < cols; column++)
        {
            int at_ = 0;
            foreach (JgsValue block in kept)
            {
                JgsValue[] held = block.AsCell;
                for (int r = 0; r < block.Rows; r++)
                {
                    stacked[at_ + r + (column * height)] = held[r + (column * block.Rows)];
                }

                at_ += block.Rows;
            }
        }

        return ShapedCell([.. stacked], height, cols);
    }

    /// <summary>A cell of the given elements with the given shape; an empty one is 0-by-0.</summary>
    private static JgsValue ShapedCell(IReadOnlyList<JgsValue> elements, int rows, int cols)
    {
        JgsValue cell = JgsValue.Cell([.. elements]);
        cell.Reshape(elements.Count == 0 ? 0 : rows, elements.Count == 0 ? 0 : cols);
        return cell;
    }
}
