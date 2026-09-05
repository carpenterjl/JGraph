using System.Globalization;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Turns a Data Viewer cell edit into the statement that performs it, in the dialect the workspace
/// speaks — the one place that knows how each grid the viewer shows maps back onto a value: an
/// index/value grid is a vector, a matrix grid is two subscripts, a cell grid braces in, a struct
/// grid is a field per row, and a table grid is a variable per column. What the user typed is an
/// expression, as in MATLAB's variable editor, so <c>pi</c> and <c>x + 1</c> mean what they say and
/// a string is written with its quotes.
/// </summary>
internal static class JgsCellAssignment
{
    /// <summary>The statement for the edit, or null when that cell has no write in this dialect.</summary>
    public static string? Compose(ScriptVariable variable, int row, int column, string text, JgsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialect);
        string value = text.Trim();
        if (value.Length == 0 || row < 0 || column < 0 || !IsIdentifier(variable.Name))
        {
            return null;
        }

        string name = variable.Name;
        int b = dialect.IndexBase;
        string r = Index(row + b);
        string c = Index(column + b);
        switch (variable.RawValue)
        {
            // The index/value grid of a vector: only the value column is a cell of the vector.
            case double[] vector when column == 1 && row < vector.Length:
                return $"{name}({r}) = {value};";

            // A table variable per column, one row per row. Dot syntax is MATLAB's; JGS has none.
            case Table table when dialect.IsMatlab && column < table.ColumnCount && row < table.RowCount
                && IsIdentifier(table.ColumnNames[column]):
                return $"{name}.{table.ColumnNames[column]}({r}) = {value};";

            case ScriptValueGrid { Kind: "matrix" } grid when InGrid(grid, row, column):
                return $"{name}({r}, {c}) = {value};";

            case ScriptValueGrid { Kind: "cell" } grid when dialect.IsMatlab && InGrid(grid, row, column):
                return $"{name}{{{r}, {c}}} = {value};";

            // A struct grid is Field / Type / Value; only the value can be written.
            case ScriptValueGrid { Kind: "struct" } grid when dialect.IsMatlab && column == 2 && row < grid.Rows.Count
                && IsIdentifier(grid.Rows[row][0]):
                return $"{name}.{grid.Rows[row][0]} = {value};";

            default:
                return null;
        }
    }

    private static bool InGrid(ScriptValueGrid grid, int row, int column) =>
        row < grid.Rows.Count && column < grid.ColumnNames.Count;

    private static string Index(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }

        foreach (char ch in text)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }
}
