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

        // One variable that holds its value as it is (V6) answers with those rows of it, in its
        // own class: an int8 variable reads out int8, a cell variable a cell.
        if (columns.Length == 1 && table[columns[0]] is JgsValueColumn held)
        {
            return JgsValueColumn.RowsOfValue(held.Value, rows);
        }

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
            : TableRowPositions(table, rowIndex, at);

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

    // ---- T{rows, vars} = v (V6, ADR 0167) ---------------------------------------------------------

    /// <summary>
    /// Writes <c>T{rows, vars} = v</c>: the right-hand side's columns are dealt across the selected
    /// variables in order, each variable is read out, written at <c>rows</c> by the ordinary index
    /// road — so conversion into the variable's class, scalar expansion, masks, growth and the
    /// count check are the ones an array gets — and put back; the rebuilt table is stored back
    /// where it was read (<see cref="StoreBack"/>). Nothing is stored until every variable has
    /// taken its part, so a refused write leaves the table as it was (M14).
    /// </summary>
    private JgsValue WriteTableBrace(
        Expr holderExpr, Table table, BraceIndexExpr brace, AssignExpr assign, JgsValue rhs, JgsEnvironment env)
    {
        if (brace.Indices.Count != 2)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column, brace.Indices.Count == 1
                ? "Subscripting into a table using one subscript (as in t(i)) is not supported. Specify a row subscript and a variable subscript, as in t(rows,vars). To select variables, use t(:,i) or for one variable t.(i). To select rows, use t(i,:)."
                : $"Indexing a table with {{}} takes two subscripts, the rows and the variables, but got {brace.Indices.Count}.");
        }

        if (assign.Op != TokenType.Assign)
        {
            rhs = ApplyBinary(UnderlyingOp(assign.Op), IndexTableBrace(JgsValue.Table(table), brace.Indices, brace, env), rhs, assign);
        }

        int[] extents = [table.RowCount, table.ColumnCount];
        JgsValue? rowIndex = EvaluateIndexArgument(brace.Indices[0], extents, 0, env);
        JgsValue? columnIndex = EvaluateIndexArgument(brace.Indices[1], extents, 1, env);

        Expr rowExpr;
        if (rowIndex is null)
        {
            rowExpr = new AllExpr { Line = brace.Line, Column = brace.Column };
        }
        else
        {
            if (IsNameSubscript(rowIndex))
            {
                int[] named = TableRowPositions(table, rowIndex, brace);
                rowIndex = JgsMatrix.FromColumnMajorDims(
                    System.Array.ConvertAll(named, r => (double)(r + Dialect.IndexBase)), [named.Length, 1]);
            }

            RefuseNonPositive(rowIndex, 1, brace);
            rowExpr = new PreEvaluated(rowIndex) { Line = brace.Line, Column = brace.Column };
        }

        (string Name, bool Existing)[] variables = TableWriteVariables(table, columnIndex, brace);
        var widths = new int[variables.Length];
        int total = 0;
        for (int v = 0; v < variables.Length; v++)
        {
            widths[v] = variables[v].Existing && table[variables[v].Name] is NumberMatrixColumn matrix ? matrix.Width : 1;
            total += widths[v];
        }

        bool container = rhs.Type is JgsType.Array or JgsType.Cell;
        bool single = !container || rhs.ArrayLength == 1;
        if (container && rhs.ArrayLength == 0)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"The value on the right-hand side of the assignment must have {total} columns. If you intended to delete rows or variables by assigning [], use () subscripting instead of {{}}.");
        }

        if (!single && rhs.Cols != total)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"The value on the right-hand side of the assignment has the wrong width. The assignment requires a value whose width is {total}.");
        }

        var written = new TableColumn[variables.Length];
        int height = table.RowCount;
        int offset = 0;
        for (int v = 0; v < variables.Length; v++)
        {
            (string name, bool existing) = variables[v];
            JgsValue part = single || total == widths[v] ? rhs : RightHandColumns(rhs, offset, widths[v], brace, env);
            offset += widths[v];

            JgsValue current = existing
                ? JgsBuiltins.TableColumnValue(table, name, brace.Line, brace.Column)
                : JgsMatrix.FromColumnMajorDims(new double[table.RowCount], [table.RowCount, 1]);
            if (current.Type == JgsType.Cell && part.Type == JgsType.String)
            {
                throw new JgsRuntimeException(brace.Line, brace.Column, "Conversion to cell from char is not possible.");
            }

            var scratch = new JgsEnvironment(env);
            scratch.Declare(LevelSlot, current);
            var slot = new VariableExpr(LevelSlot) { Line = brace.Line, Column = brace.Column };
            Expr[] subscripts = widths[v] == 1
                ? [rowExpr]
                : [rowExpr, new AllExpr { Line = brace.Line, Column = brace.Column }];
            IndexWrite(slot, subscripts, TokenType.Assign, part, brace, scratch);

            scratch.TryGet(LevelSlot, out JgsValue column);
            written[v] = JgsBuiltins.TableColumnFrom("table", name, column, brace.Line, brace.Column);
            height = Math.Max(height, written[v].RowCount);
        }

        Table rebuilt = height > table.RowCount ? JgsBuiltins.GrownTo(table, height) : table;
        foreach (TableColumn column in written)
        {
            TableColumn fitted = column.RowCount < height ? JgsBuiltins.GrownTo(new Table([column]), height)[0] : column;
            rebuilt = JgsBuiltins.WithColumn(rebuilt, fitted, grows: false, brace.Line, brace.Column);
        }

        StoreBack(holderExpr, JgsValue.Table(rebuilt), assign, env);
        return rhs;
    }

    /// <summary>Whether a subscript names its picks: a char row, a cell of them, or a string array.</summary>
    private static bool IsNameSubscript(JgsValue index) =>
        index.Type == JgsType.String
        || (index.Type == JgsType.Cell && index.AsCell.Length > 0 && index.AsCell.All(e => e.Type == JgsType.String))
        || (index.Type == JgsType.Array && index.IsStringArray);

    /// <summary>
    /// The variables a brace write names, in the order it names them. A name the table does not
    /// have, or a position just past its last variable, is a variable the write creates (measured:
    /// <c>T{1, 'Nope'} = 5</c> adds <c>Nope</c>, <c>T{1, 3} = 5</c> on two variables adds <c>Var3</c>).
    /// </summary>
    private (string Name, bool Existing)[] TableWriteVariables(Table table, JgsValue? index, Node at)
    {
        if (index is null)
        {
            return table.Columns.Select(c => (c.Name, true)).ToArray();
        }

        if (IsNameSubscript(index))
        {
            JgsValue[] names = index.Type == JgsType.String ? [index]
                : index.Type == JgsType.Cell ? index.AsCell : index.BoxedElements();
            return System.Array.ConvertAll(names, n => table.TryGetColumn(n.AsString, out TableColumn known)
                ? (known.Name, true)
                : (n.AsString, false));
        }

        if (index.Type == JgsType.Array && IsLogicalIndex(index))
        {
            return System.Array.ConvertAll(Positions(index, table.ColumnCount, "table variable", at), p => (table[p].Name, true));
        }

        JgsValue[] picks = index.Type == JgsType.Array ? index.BoxedElements() : [index];
        var variables = new (string, bool)[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            if (picks[i].Type is not (JgsType.Number or JgsType.Bool))
            {
                throw new JgsRuntimeException(at.Line, at.Column, "A table variable subscript must be a position, a name or a mask.");
            }

            double raw = picks[i].AsNumber;
            int position = (int)raw - Dialect.IndexBase;
            if (raw != Math.Floor(raw) || position < 0)
            {
                throw BadWriteIndex((int)raw, 2, at);
            }

            variables[i] = position < table.ColumnCount ? (table[position].Name, true) : ($"Var{position + 1}", false);
        }

        return variables;
    }

    /// <summary><paramref name="count"/> columns of a right-hand side, from <paramref name="first"/> (0-based).</summary>
    private JgsValue RightHandColumns(JgsValue value, int first, int count, Node at, JgsEnvironment env)
    {
        if (value.Type == JgsType.Cell)
        {
            int rows = value.Rows;
            var picked = new JgsValue[rows * count];
            System.Array.Copy(value.AsCell, first * rows, picked, 0, picked.Length);
            JgsValue cell = JgsValue.Cell(System.Array.ConvertAll(picked, JgsValue.Share));
            cell.Reshape(rows, count);
            return cell;
        }

        var positions = new double[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = first + i + Dialect.IndexBase;
        }

        Expr[] subscripts =
        [
            new AllExpr { Line = at.Line, Column = at.Column },
            new PreEvaluated(JgsMatrix.FromColumnMajorDims(positions, [1, count])) { Line = at.Line, Column = at.Column },
        ];
        return IndexInto(value, subscripts, at, env);
    }

    /// <summary>A row subscript: positions, a mask, or — on a table with row names — one name or a cell of them.</summary>
    private int[] TableRowPositions(Table table, JgsValue index, Node at)
    {
        bool named = index.Type == JgsType.String
            || (index.Type == JgsType.Cell && index.AsCell.Length > 0 && index.AsCell.All(e => e.Type == JgsType.String))
            || (index.Type == JgsType.Array && index.IsStringArray);
        if (!named)
        {
            return Positions(index, table.RowCount, "table row", at);
        }

        JgsValue[] names = index.Type == JgsType.String ? [index]
            : index.Type == JgsType.Cell ? index.AsCell : index.BoxedElements();
        var picks = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i].AsString;
            int found = -1;
            for (int r = 0; table.RowNames is { } rowNames && r < rowNames.Count; r++)
            {
                if (string.Equals(rowNames[r], name, StringComparison.Ordinal))
                {
                    found = r;
                    break;
                }
            }

            picks[i] = found >= 0
                ? found
                : throw new JgsRuntimeException(at.Line, at.Column, $"Unrecognized row name '{name}'.");
        }

        return picks;
    }

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
