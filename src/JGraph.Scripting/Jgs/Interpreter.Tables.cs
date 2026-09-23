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

        var parts = new JgsValue[variables.Length];
        int offset = 0;
        for (int v = 0; v < variables.Length; v++)
        {
            parts[v] = single || total == widths[v] ? rhs : RightHandColumns(rhs, offset, widths[v], brace, env);
            offset += widths[v];
        }

        Table rebuilt = WriteTableVariables(table, variables, parts, rowExpr, zeroStart: true, brace, env);
        StoreBack(holderExpr, JgsValue.Table(rebuilt), assign, env);
        return rhs;
    }

    /// <summary>
    /// The write half the brace and paren forms share: each selected variable is read out (a
    /// variable the write names for the first time starts from zeros of the table's height, or
    /// from the empty of its part's kind), written at <paramref name="rowExpr"/> by the ordinary
    /// index road — so conversion into the variable's class, scalar expansion, masks, growth and
    /// the count check are an array's — and put back; a write that made a variable longer grows
    /// the table, the row names and the row times with it (<see cref="JgsBuiltins.GrownTo"/>).
    /// Nothing is stored until every variable has taken its part (M14).
    /// </summary>
    private Table WriteTableVariables(
        Table table, (string Name, bool Existing)[] variables, JgsValue[] parts, Expr rowExpr, bool zeroStart, Node at,
        JgsEnvironment env)
    {
        var written = new TableColumn[variables.Length];
        int height = table.RowCount;
        for (int v = 0; v < variables.Length; v++)
        {
            (string name, bool existing) = variables[v];
            JgsValue part = parts[v];

            // A new variable written whole (T(:, 'New') = {7; 8}) is its part; the row count is
            // checked when it joins the table.
            if (!existing && rowExpr is AllExpr)
            {
                written[v] = JgsBuiltins.TableColumnFrom("table", name, part, at.Line, at.Column);
                height = Math.Max(height, written[v].RowCount);
                continue;
            }

            int width = existing && table[name] is NumberMatrixColumn matrix ? matrix.Width : 1;
            JgsValue current = existing
                ? JgsBuiltins.TableColumnValue(table, name, at.Line, at.Column)
                : zeroStart
                    ? JgsMatrix.FromColumnMajorDims(new double[table.RowCount], [table.RowCount, 1])
                    : EmptyOfKind(part);

            // A text variable is a cell, and only a cell goes into it (measured: a char is
            // "Conversion to cell from char", a number "... from double", a string its own words).
            if (current.Type == JgsType.Cell && part.Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(at.Line, at.Column, part.IsStringArray
                    ? "Unable to perform assignment because value of type 'string' is not convertible to 'cell'."
                    : $"Conversion to cell from {JgsBuiltins.ClassOf(part, Dialect)} is not possible.");
            }

            var scratch = new JgsEnvironment(env);
            scratch.Declare(LevelSlot, current);
            var slot = new VariableExpr(LevelSlot) { Line = at.Line, Column = at.Column };
            Expr[] subscripts = width == 1
                ? [rowExpr]
                : [rowExpr, new AllExpr { Line = at.Line, Column = at.Column }];
            IndexWrite(slot, subscripts, TokenType.Assign, part, at, scratch);

            scratch.TryGet(LevelSlot, out JgsValue column);
            written[v] = JgsBuiltins.TableColumnFrom("table", name, column, at.Line, at.Column);
            height = Math.Max(height, written[v].RowCount);
        }

        Table rebuilt = height > table.RowCount ? JgsBuiltins.GrownTo(table, height) : table;
        foreach (TableColumn column in written)
        {
            TableColumn fitted = column.RowCount < height ? JgsBuiltins.GrownTo(new Table([column]), height)[0] : column;
            rebuilt = JgsBuiltins.WithColumn(rebuilt, fitted, grows: false, at.Line, at.Column);
        }

        return rebuilt;
    }

    // ---- T(rows, vars) = rhs, T(rows, :) = [], T(:, vars) = [] (V6, ADR 0167, #117) ---------------

    /// <summary>
    /// Writes <c>T(rows, vars) = rhs</c>: the right-hand side is another table (its variables
    /// taken by position, names ignored; one variable is dealt to every selected one) or a cell
    /// (read as a table first: a column of chars is a text variable, a column of numbers an
    /// array of them, one element expands over the whole selection); each selected variable
    /// then takes its rows through <see cref="WriteTableVariables"/>. A row named for the first
    /// time is added under that name. <c>[]</c> deletes the rows, or the variables, the other
    /// subscript being <c>:</c>; the rebuilt table is stored back where it was read.
    /// </summary>
    private JgsValue WriteTableParen(
        Expr holderExpr, Table table, IReadOnlyList<Expr> subscripts, Node paren, AssignExpr assign, JgsValue rhs,
        JgsEnvironment env)
    {
        if (subscripts.Count != 2)
        {
            throw new JgsRuntimeException(paren.Line, paren.Column, subscripts.Count == 1
                ? "Subscripting into a table using one subscript (as in t(i)) is not supported. Specify a row subscript and a variable subscript, as in t(rows,vars). To select variables, use t(:,i) or for one variable t.(i). To select rows, use t(i,:)."
                : $"Indexing a table with () takes two subscripts, the rows and the variables, but got {subscripts.Count}.");
        }

        if (assign.Op != TokenType.Assign)
        {
            throw new JgsRuntimeException(paren.Line, paren.Column,
                "A table's rows and variables take a plain assignment; an operator assignment into T(rows, vars) is not supported.");
        }

        int[] extents = [table.RowCount, table.ColumnCount];
        JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
        JgsValue? columnIndex = EvaluateIndexArgument(subscripts[1], extents, 1, env);

        if (IsDeletion(rhs))
        {
            StoreBack(holderExpr, JgsValue.Table(WithoutSelection(table, rowIndex, columnIndex, paren)), assign, env);
            return rhs;
        }

        if (rhs.Type is not (JgsType.Table or JgsType.Cell))
        {
            throw new JgsRuntimeException(paren.Line, paren.Column,
                "Right hand side of an assignment into a table must be another table or a cell array.");
        }

        Expr rowExpr;
        int rowCount;
        string[]? addedNames = null;
        if (rowIndex is null)
        {
            rowExpr = new AllExpr { Line = paren.Line, Column = paren.Column };
            rowCount = table.RowCount;
        }
        else
        {
            if (IsNameSubscript(rowIndex))
            {
                int[] named = TableRowPositionsForWrite(table, rowIndex, paren, out addedNames);
                rowIndex = JgsMatrix.FromColumnMajorDims(
                    System.Array.ConvertAll(named, r => (double)(r + Dialect.IndexBase)), [named.Length, 1]);
            }

            RefuseNonPositive(rowIndex, 1, paren);
            rowCount = SelectedCount(rowIndex);
            rowExpr = new PreEvaluated(rowIndex) { Line = paren.Line, Column = paren.Column };
        }

        (string Name, bool Existing)[] variables = TableWriteVariables(table, columnIndex, paren);
        JgsValue[] parts = rhs.Type == JgsType.Table
            ? TableRightHandParts(rhs.AsTable, variables.Length, rowCount, paren)
            : CellRightHandParts(rhs, variables.Length, rowCount, paren);

        Table rebuilt = WriteTableVariables(table, variables, parts, rowExpr, zeroStart: false, paren, env);
        if (addedNames is not null)
        {
            string[] names = [.. rebuilt.RowNames!];
            for (int i = 0; i < addedNames.Length; i++)
            {
                names[table.RowCount + i] = addedNames[i];
            }

            rebuilt = rebuilt.WithRowLabels(names, rebuilt.RowTimes);
        }

        StoreBack(holderExpr, JgsValue.Table(rebuilt), assign, env);
        return rhs;
    }

    /// <summary>How many rows a row subscript selects: a mask's trues, an index list's length, one for a scalar.</summary>
    private static int SelectedCount(JgsValue index)
    {
        if (index.Type != JgsType.Array)
        {
            return 1;
        }

        if (!IsLogicalIndex(index))
        {
            return index.ArrayLength;
        }

        int count = 0;
        foreach (JgsValue flag in index.BoxedElements())
        {
            if (flag.AsBool)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Row positions for a write by name: a name the table has, or — on a table that has row
    /// names — a new row after the last, in the order named (measured: <c>T('c', :) = {3}</c>
    /// adds a row <c>c</c>).
    /// </summary>
    private int[] TableRowPositionsForWrite(Table table, JgsValue index, Node at, out string[]? added)
    {
        added = null;
        if (table.RowNames is not { } rowNames)
        {
            return TableRowPositions(table, index, at);
        }

        JgsValue[] names = index.Type == JgsType.String ? [index]
            : index.Type == JgsType.Cell ? index.AsCell : index.BoxedElements();
        var picks = new int[names.Length];
        var fresh = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i].AsString;
            int found = -1;
            for (int r = 0; r < rowNames.Count; r++)
            {
                if (string.Equals(rowNames[r], name, StringComparison.Ordinal))
                {
                    found = r;
                    break;
                }
            }

            if (found < 0)
            {
                found = fresh.IndexOf(name);
                if (found < 0)
                {
                    fresh.Add(name);
                    found = fresh.Count - 1;
                }

                found += table.RowCount;
            }

            picks[i] = found;
        }

        added = fresh.Count == 0 ? null : [.. fresh];
        return picks;
    }

    /// <summary>The right-hand side of a table paren write when it is a table: its variables by position.</summary>
    private JgsValue[] TableRightHandParts(Table source, int variableCount, int rowCount, Node at)
    {
        if (source.RowCount != rowCount)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "To assign to or create a variable in a table, the number of rows must match the height of the table.");
        }

        if (source.ColumnCount != variableCount && source.ColumnCount != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "The number of table variables in an assignment must match.");
        }

        var parts = new JgsValue[variableCount];
        for (int v = 0; v < variableCount; v++)
        {
            TableColumn column = source[source.ColumnCount == 1 ? 0 : v];
            parts[v] = JgsBuiltins.TableColumnValue(source, column.Name, at.Line, at.Column);
        }

        return parts;
    }

    /// <summary>
    /// The right-hand side of a table paren write when it is a cell: read as a table would be,
    /// one column per variable (<see cref="VariableFromCells"/>); one element expands over the
    /// whole selection, otherwise the width must match the variables and the height the rows.
    /// </summary>
    private JgsValue[] CellRightHandParts(JgsValue cell, int variableCount, int rowCount, Node at)
    {
        JgsValue[] elements = cell.AsCell;
        var parts = new JgsValue[variableCount];
        if (elements.Length == 1)
        {
            for (int v = 0; v < variableCount; v++)
            {
                var column = new JgsValue[rowCount];
                System.Array.Fill(column, elements[0]);
                parts[v] = VariableFromCells(column, at);
            }

            return parts;
        }

        int rows = cell.Rows;
        int cols = elements.Length == 0 ? 0 : elements.Length / Math.Max(rows, 1);
        if (cols != variableCount)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "The number of table variables in an assignment must match.");
        }

        if (rows != rowCount)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "To assign to or create a variable in a table, the number of rows must match the height of the table.");
        }

        for (int v = 0; v < variableCount; v++)
        {
            var column = new JgsValue[rows];
            System.Array.Copy(elements, v * rows, column, 0, rows);
            parts[v] = VariableFromCells(column, at);
        }

        return parts;
    }

    /// <summary>
    /// One column of a cell as the table variable it makes (the rule <c>cell2table</c> applies): a
    /// column of chars is a cell of them, a column of numbers, logicals, strings or times is one
    /// array of them stacked by the bracket's rules, anything else stays a cell.
    /// </summary>
    private JgsValue VariableFromCells(JgsValue[] column, Node at)
    {
        bool allText = true;
        bool stackable = true;
        foreach (JgsValue element in column)
        {
            allText &= element.Type == JgsType.String;
            stackable &= element.Type is JgsType.Number or JgsType.Bool or JgsType.Complex
                || (element.Type == JgsType.Array && !element.IsCharMatrix);
        }

        if (!allText && stackable && column.Length > 0)
        {
            var rows = new List<JgsValue[]>(column.Length);
            foreach (JgsValue element in column)
            {
                rows.Add([element]);
            }

            return Concatenate(rows, at);
        }

        JgsValue held = JgsValue.Cell(System.Array.ConvertAll(column, JgsValue.Share));
        held.Reshape(column.Length, 1);
        return held;
    }

    /// <summary>
    /// The table without the rows a subscript names (the variable subscript being <c>:</c>), or
    /// without the variables named (the row subscript being <c>:</c>); <c>T(:, :) = []</c>
    /// removes every row and keeps the variables (measured).
    /// </summary>
    private Table WithoutSelection(Table table, JgsValue? rowIndex, JgsValue? columnIndex, Node at)
    {
        if (rowIndex is not null && columnIndex is not null)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "At least one subscript must be ':' when you delete rows or variables by assigning [].");
        }

        if (columnIndex is not null)
        {
            var doomed = new HashSet<int>(TableColumnPositions(table, columnIndex, at));
            var kept = new List<TableColumn>();
            for (int i = 0; i < table.ColumnCount; i++)
            {
                if (!doomed.Contains(i))
                {
                    kept.Add(table[i]);
                }
            }

            return table.WithColumns(kept);
        }

        var gone = new bool[table.RowCount];
        if (rowIndex is null)
        {
            System.Array.Fill(gone, true);
        }
        else
        {
            if (rowIndex.Type == JgsType.Number
                && (rowIndex.AsNumber < Dialect.IndexBase || rowIndex.AsNumber != Math.Floor(rowIndex.AsNumber)))
            {
                throw new JgsRuntimeException(at.Line, at.Column, "Array indices must be positive integers or logical values.");
            }

            foreach (int r in TableRowPositions(table, rowIndex, at))
            {
                gone[r] = true;
            }
        }

        var rows = new List<int>();
        for (int r = 0; r < gone.Length; r++)
        {
            if (!gone[r])
            {
                rows.Add(r);
            }
        }

        return table.Select(rows, AllPositions(table.ColumnCount));
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
