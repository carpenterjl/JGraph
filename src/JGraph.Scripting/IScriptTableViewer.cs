namespace JGraph.Scripting;

/// <summary>
/// The host's window for a read-only table, behind <c>methodsview</c> and <c>libfunctionsview</c>
/// (interop plan, stage 10, ADR 0183) — the app opens one window per call, as R2025b opens one
/// figure. A host without windows (the CLI, a batch run, tests) passes none, and the builtins print
/// the same table as text. Invoked on the engine's background thread, so a UI host marshals it.
/// </summary>
public interface IScriptTableViewer
{
    /// <summary>Shows a table in a window of its own and returns without waiting for it to close.</summary>
    /// <param name="title">The window title: <c>Methods for class System.Math</c>.</param>
    /// <param name="headers">The column headers, one per column.</param>
    /// <param name="rows">The rows, each with one cell per header.</param>
    void ShowTable(string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows);
}
