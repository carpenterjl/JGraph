using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Concatenating tables (V6, ADR 0167, #121): <c>[T; U]</c> stacks rows — the tables must have
/// the same variables, matched by name and taken in the first table's order, each variable
/// joined by the bracket's own rules so an <c>int8</c> variable saturates and a cell variable
/// stays a cell — and <c>[T U]</c> puts variables side by side, which must not share a name.
/// A cell beside or below a table is read as a table first. Row names are kept from whichever
/// table has them (a stack fills the other's as <c>RowN</c>; a join needs them equal), row
/// times likewise (a stack fills the other's as missing), and the rest of the metadata is the
/// first table's. Every rule and message here is R2025b's, recorded in <c>table_forms</c>.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>Whether any piece of a bracket literal is a table.</summary>
    private static bool AnyTable(List<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.Type == JgsType.Table)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The bracket's join for values that are not in a literal — what <c>vertcat(T, U)</c>,
    /// <c>table2array</c> and the row verbs stack with, so one road settles the class rules.
    /// </summary>
    internal JgsValue Concatenate(List<JgsValue[]> rows, int line, int col) =>
        Concatenate(rows, new AllExpr { Line = line, Column = col });

    private JgsValue Concatenate(List<JgsValue[]> rows, Node at) => AssembleMatrix(at, rows);

    /// <summary><c>[T; U]</c>, <c>[T U]</c> and their mixes, from values in hand.</summary>
    internal JgsValue ConcatenateTables(List<JgsValue[]> rows, int line, int col) =>
        ConcatenateTables(rows, new AllExpr { Line = line, Column = col });

    /// <summary>Each bracket row is joined across, then the rows are stacked — the order the brackets say.</summary>
    private JgsValue ConcatenateTables(List<JgsValue[]> rows, Node at)
    {
        var bands = new List<Table>();
        foreach (JgsValue[] row in rows)
        {
            Table? band = null;
            foreach (JgsValue piece in row)
            {
                if (JgsEmpty.IsEmptyArray(piece))
                {
                    continue; // [T; []] is T, as [] contributes nothing to any bracket
                }

                Table next = piece.Type switch
                {
                    JgsType.Table => piece.AsTable,
                    JgsType.Cell => TableFromCell(piece,
                        band is null && bands.Count > 0 && bands[0].ColumnCount == CellWidth(piece) ? bands[0].ColumnNames : null,
                        band?.ColumnCount ?? 0, at),
                    _ => throw new JgsRuntimeException(at.Line, at.Column, "All input arguments must be tables or cell arrays."),
                };
                band = band is null ? next : JoinTablesAcross(band, next, at);
            }

            if (band is not null)
            {
                bands.Add(band);
            }
        }

        if (bands.Count == 0)
        {
            return EmptyBracket();
        }

        Table result = bands[0];
        for (int i = 1; i < bands.Count; i++)
        {
            result = StackTables(result, bands[i], at);
        }

        return JgsValue.Table(result);
    }

    private static int CellWidth(JgsValue cell) =>
        cell.AsCell.Length == 0 ? 0 : cell.AsCell.Length / Math.Max(cell.Rows, 1);

    /// <summary>
    /// A cell as the table it reads as beside or below one: one variable per column
    /// (<see cref="VariableFromCells"/>), named as given, or <c>VarN</c> by its position in the
    /// result (measured: <c>[T {3; 4}]</c> on a one-variable table adds <c>Var2</c>).
    /// </summary>
    private Table TableFromCell(JgsValue cell, IReadOnlyList<string>? names, int offset, Node at)
    {
        JgsValue[] elements = cell.AsCell;
        int rows = Math.Max(cell.Rows, 1);
        int width = CellWidth(cell);
        var columns = new TableColumn[width];
        for (int c = 0; c < width; c++)
        {
            var column = new JgsValue[rows];
            System.Array.Copy(elements, c * rows, column, 0, rows);
            string name = names is not null ? names[c] : $"Var{offset + c + 1}";
            columns[c] = JgsBuiltins.TableColumnFrom("horzcat", name, VariableFromCells(column, at), at.Line, at.Column);
        }

        return new Table(columns);
    }

    /// <summary><c>[A B]</c>: the variables side by side; heights equal, names distinct, row names equal where both have them.</summary>
    private static Table JoinTablesAcross(Table a, Table b, Node at)
    {
        if (a.RowCount != b.RowCount)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "All tables being horizontally concatenated must have the same number of rows.");
        }

        foreach (TableColumn column in b.Columns)
        {
            if (a.TryGetColumn(column.Name, out _))
            {
                throw new JgsRuntimeException(at.Line, at.Column, $"Duplicate table variable name: '{column.Name}'.");
            }
        }

        if (a.RowNames is { } left && b.RowNames is { } right && !left.SequenceEqual(right, StringComparer.Ordinal))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "All tables being horizontally concatenated must have the same row names.");
        }

        var columns = new List<TableColumn>(a.Columns);
        columns.AddRange(b.Columns);
        return new Table(columns)
        {
            RowNames = a.RowNames ?? b.RowNames,
            RowTimes = a.RowTimes ?? b.RowTimes,
            DimensionNames = a.DimensionNames ?? b.DimensionNames,
            VariableUnits = JoinPerVariable(a.VariableUnits, a.ColumnCount, b.VariableUnits, b.ColumnCount),
            VariableDescriptions = JoinPerVariable(a.VariableDescriptions, a.ColumnCount, b.VariableDescriptions, b.ColumnCount),
            Description = a.Description ?? b.Description,
            UserData = a.UserData ?? b.UserData,
        };
    }

    private static string[]? JoinPerVariable(IReadOnlyList<string>? left, int leftCount, IReadOnlyList<string>? right, int rightCount)
    {
        if (left is null && right is null)
        {
            return null;
        }

        var joined = new string[leftCount + rightCount];
        for (int i = 0; i < leftCount; i++)
        {
            joined[i] = left is not null && i < left.Count ? left[i] : string.Empty;
        }

        for (int i = 0; i < rightCount; i++)
        {
            joined[leftCount + i] = right is not null && i < right.Count ? right[i] : string.Empty;
        }

        return joined;
    }

    /// <summary><c>[A; B]</c>: the rows of both, each variable joined by the bracket's rules, in <paramref name="a"/>'s order.</summary>
    private Table StackTables(Table a, Table b, Node at)
    {
        if (a.ColumnCount != b.ColumnCount)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "All tables being vertically concatenated must have the same number of variables.");
        }

        var columns = new TableColumn[a.ColumnCount];
        for (int i = 0; i < columns.Length; i++)
        {
            string name = a[i].Name;
            if (!b.TryGetColumn(name, out TableColumn other) || !string.Equals(other.Name, name, StringComparison.Ordinal))
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    "All tables being vertically concatenated must have the same variable names.");
            }

            JgsValue above = JgsBuiltins.TableColumnValue(a, name, at.Line, at.Column);
            JgsValue below = JgsBuiltins.TableColumnValue(b, name, at.Line, at.Column);
            if ((above.Type == JgsType.Cell) != (below.Type == JgsType.Cell))
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Unable to concatenate the table variable '{name}' because it is a cell array in one table and not a cell array in another.");
            }

            columns[i] = JgsBuiltins.TableColumnFrom("vertcat", name, StackValues(above, below, at), at.Line, at.Column);
        }

        return a.WithColumns(columns).WithRowLabels(StackedRowNames(a, b, at), StackedRowTimes(a, b, at));
    }

    /// <summary>Two column values one above the other: times by their milliseconds, everything else by the bracket.</summary>
    private JgsValue StackValues(JgsValue above, JgsValue below, Node at)
    {
        if (above.IsTime || below.IsTime)
        {
            JgsTimeTag? tag = above.TimeTag ?? below.TimeTag;
            if (above.TimeTag is { } first && below.TimeTag is { } second && first.Kind != second.Kind)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"A {JgsBuiltins.ClassOf(above, Dialect)} variable cannot be stacked with a {JgsBuiltins.ClassOf(below, Dialect)} one.");
            }

            double[] upper = above.IsTime ? JgsBuiltins.TimeMs(above) : Missing(above.ArrayLength);
            double[] lower = below.IsTime ? JgsBuiltins.TimeMs(below) : Missing(below.ArrayLength);
            var joined = new double[upper.Length + lower.Length];
            upper.CopyTo(joined, 0);
            lower.CopyTo(joined, upper.Length);
            return JgsMatrix.FromColumnMajorDims(joined, [joined.Length, 1]).MarkTime(tag!);
        }

        return Concatenate([[above], [below]], at);
    }

    private static double[] Missing(int count)
    {
        var missing = new double[count];
        System.Array.Fill(missing, double.NaN);
        return missing;
    }

    /// <summary>
    /// The stacked table's row names: both tables' when both have them, or the one's with the
    /// other's rows named <c>RowN</c> by their position in the result; none when neither has.
    /// A name twice over is refused as MATLAB refuses it.
    /// </summary>
    private static string[]? StackedRowNames(Table a, Table b, Node at)
    {
        if (a.RowNames is null && b.RowNames is null)
        {
            return null;
        }

        var names = new string[a.RowCount + b.RowCount];
        for (int r = 0; r < a.RowCount; r++)
        {
            names[r] = a.RowNames is { } left ? left[r] : $"Row{r + 1}";
        }

        for (int r = 0; r < b.RowCount; r++)
        {
            names[a.RowCount + r] = b.RowNames is { } right ? right[r] : $"Row{a.RowCount + r + 1}";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!seen.Add(name))
            {
                throw new JgsRuntimeException(at.Line, at.Column, $"Duplicate table row name: '{name}'.");
            }
        }

        return names;
    }

    /// <summary>The stacked table's row times: both tables', or the one's with the other's rows missing (measured: NaN).</summary>
    private TableColumn? StackedRowTimes(Table a, Table b, Node at)
    {
        if (a.RowTimes is null && b.RowTimes is null)
        {
            return null;
        }

        TableColumn named = (a.RowTimes ?? b.RowTimes)!;
        JgsValue upper = a.RowTimes is { } above
            ? JgsBuiltins.TableColumnValue(new Table([above]), above.Name, at.Line, at.Column)
            : JgsMatrix.FromColumnMajorDims(Missing(a.RowCount), [a.RowCount, 1]);
        JgsValue lower = b.RowTimes is { } below
            ? JgsBuiltins.TableColumnValue(new Table([below]), below.Name, at.Line, at.Column)
            : JgsMatrix.FromColumnMajorDims(Missing(b.RowCount), [b.RowCount, 1]);
        return JgsBuiltins.TableColumnFrom("vertcat", named.Name, StackValues(upper, lower, at), at.Line, at.Column);
    }
}
