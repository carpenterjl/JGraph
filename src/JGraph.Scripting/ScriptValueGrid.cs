namespace JGraph.Scripting;

/// <summary>
/// A value that has a table shape but no simpler host representation — a matrix, a cell array, or a
/// struct. The engine flattens it to formatted text here so the host can put it in a grid without
/// knowing anything about the interpreter's value model, which stays internal.
/// </summary>
/// <param name="Kind">What it came from ("matrix", "cell", "struct"), for the viewer's caption.</param>
/// <param name="ColumnNames">The column headers, left to right.</param>
/// <param name="Rows">The rows, each with one string per column.</param>
public sealed record ScriptValueGrid(
    string Kind,
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<string[]> Rows)
{
    /// <summary>
    /// Cells beyond this many are not projected: a grid is for reading, and a 5000×5000 matrix would
    /// cost 25 million formatted strings to show something nobody can look at.
    /// </summary>
    public const int MaxCells = 200_000;
}
