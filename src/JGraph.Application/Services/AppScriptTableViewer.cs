using JGraph.Application.Scripting;
using JGraph.Scripting;

namespace JGraph.Application.Services;

/// <summary>
/// The app's <see cref="IScriptTableViewer"/>: <c>methodsview</c> and <c>libfunctionsview</c> open a
/// <see cref="MethodsTableWindow"/> (ADR 0183). Called on the script thread, so the window is made on
/// the UI thread and the script goes on without waiting for it, as R2025b's does.
/// </summary>
public sealed class AppScriptTableViewer : IScriptTableViewer
{
    /// <inheritdoc />
    public void ShowTable(string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        string[] columns = [.. headers];
        string[][] copied = [.. rows.Select(static r => (string[])r.Clone())];
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => new MethodsTableWindow(title, columns, copied).Show());
    }
}
