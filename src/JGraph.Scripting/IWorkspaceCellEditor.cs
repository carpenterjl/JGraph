namespace JGraph.Scripting;

/// <summary>
/// A workspace whose variables the Data Viewer may edit in place. A capability, not part of
/// <see cref="IScriptSession"/> — hosts feature-detect with <c>is IWorkspaceCellEditor</c>, as they do
/// for <see cref="IScriptRepl"/> — because an engine that cannot say how one cell of a value is
/// written (C#, a Python child process) simply shows its values read-only.
/// </summary>
/// <remarks>
/// The edit is expressed as a statement rather than performed here, so that it runs exactly the way
/// a typed one does — same thread, same interrupt, same echo and error reporting, same Workspace
/// pane refresh — and so that the paused debugger can run it in the frame the value belongs to.
/// </remarks>
public interface IWorkspaceCellEditor
{
    /// <summary>
    /// The statement, in this workspace's language, that writes <paramref name="text"/> into one cell
    /// of the Data Viewer grid of <paramref name="variable"/>; null when that cell cannot be edited
    /// (an index column, a value with no element syntax in this language, an oversize value).
    /// </summary>
    /// <param name="variable">The variable as the Workspace pane shows it, with its raw value.</param>
    /// <param name="row">The 0-based grid row.</param>
    /// <param name="column">The 0-based grid column.</param>
    /// <param name="text">What the user typed — an expression, as in MATLAB's own variable editor.</param>
    string? ComposeCellAssignment(ScriptVariable variable, int row, int column, string text);
}
