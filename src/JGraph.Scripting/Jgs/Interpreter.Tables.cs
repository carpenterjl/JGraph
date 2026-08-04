using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Subscripting a table. MATLAB gives a table two subscript forms that answer different questions:
/// <c>T(rows, vars)</c> asks for a smaller table, and <c>T{rows, vars}</c> asks for the contents of the
/// variables it selected, laid side by side. Both take the same subscript vocabulary as an array — a
/// number, a range, <c>:</c>, <c>end</c>, a logical mask — and the variable subscript also accepts a
/// name or a cell of names.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>Reads <c>T{rows, vars}</c> — the selected variables' contents, side by side.</summary>
    private JgsValue IndexTableBrace(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        Table table = target.AsTable;
        (int[] rows, int[] columns) = ResolveTableSubscripts(table, subscripts, at, env, "{}");

        bool anyText = false;
        bool anyOther = false;
        foreach (int c in columns)
        {
            if (table[c].Type == ColumnType.Text)
            {
                anyText = true;
            }
            else
            {
                anyOther = true;
            }
        }

        if (anyText && anyOther)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Braces put the selected variables side by side, so they cannot mix text with numbers. Select them separately.");
        }

        int count = rows.Length * columns.Length;
        if (count == 0)
        {
            return anyText ? JgsValue.Cell([]) : JgsValue.Array([]);
        }

        if (anyText)
        {
            var cells = new JgsValue[count];
            for (int c = 0; c < columns.Length; c++)
            {
                TableColumn column = table[columns[c]];
                for (int r = 0; r < rows.Length; r++)
                {
                    cells[r + (c * rows.Length)] = JgsValue.Str(column.GetText(rows[r]));
                }
            }

            JgsValue cell = JgsValue.Cell(cells);
            cell.Reshape(rows.Length, columns.Length);
            return cell;
        }

        var values = new double[count];
        for (int c = 0; c < columns.Length; c++)
        {
            TableColumn column = table[columns[c]];
            for (int r = 0; r < rows.Length; r++)
            {
                values[r + (c * rows.Length)] = column.GetNumber(rows[r]);
            }
        }

        // One number is a number, the same way indexing an array with one subscript is.
        return count == 1
            ? JgsValue.Number(values[0])
            : JgsMatrix.FromColumnMajorDims(values, [rows.Length, columns.Length]);
    }

    /// <summary>Reads <c>T(rows, vars)</c> — a smaller table of the same shape of thing.</summary>
    private JgsValue IndexTableParen(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        Table table = target.AsTable;
        (int[] rows, int[] columns) = ResolveTableSubscripts(table, subscripts, at, env, "()");
        return JgsValue.Table(table.Select(rows, columns));
    }

    /// <summary>
    /// Resolves a table's two subscripts to row and column positions. Rows read like any array
    /// subscript; variables do too, and additionally answer to their own names.
    /// </summary>
    private (int[] Rows, int[] Columns) ResolveTableSubscripts(
        Table table, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env, string form)
    {
        if (subscripts.Count != 2)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Indexing a table with {form} takes two subscripts, the rows and the variables, but got {subscripts.Count}.");
        }

        int[] extents = [table.RowCount, table.ColumnCount];
        JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
        JgsValue? columnIndex = EvaluateIndexArgument(subscripts[1], extents, 1, env);

        int[] rows = rowIndex is null
            ? AllPositions(table.RowCount)
            : Positions(rowIndex, table.RowCount, "table row", at);

        int[] columns = columnIndex is null
            ? AllPositions(table.ColumnCount)
            : TableColumnPositions(table, columnIndex, at);

        return (rows, columns);
    }

    private static int[] AllPositions(int count)
    {
        var all = new int[count];
        for (int i = 0; i < count; i++)
        {
            all[i] = i;
        }

        return all;
    }

    private int[] Positions(JgsValue index, int length, string what, Node at) =>
        index.Type == JgsType.Array
            ? ComputePicks(index, length, what, at.Line, at.Column)
            : [ToIndex(index, length, at.Line, at.Column)];

    /// <summary>A variable subscript: positions, a mask, one name, or a cell of names.</summary>
    private int[] TableColumnPositions(Table table, JgsValue index, Node at)
    {
        if (index.Type == JgsType.String)
        {
            return [TableColumnByName(table, index.AsString, at)];
        }

        if (index.Type == JgsType.Cell)
        {
            JgsValue[] names = index.AsCell;
            var picks = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].Type != JgsType.String)
                {
                    throw new JgsRuntimeException(at.Line, at.Column,
                        "A cell of table variable subscripts must hold names.");
                }

                picks[i] = TableColumnByName(table, names[i].AsString, at);
            }

            return picks;
        }

        return Positions(index, table.ColumnCount, "table variable", at);
    }

    private static int TableColumnByName(Table table, string name, Node at)
    {
        for (int i = 0; i < table.ColumnCount; i++)
        {
            if (string.Equals(table[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new JgsRuntimeException(at.Line, at.Column,
            $"The table has no variable '{name}'. Its variables are: {string.Join(", ", table.ColumnNames)}.");
    }
}
