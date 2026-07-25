using System.Collections.Generic;
using AvalonDock.Layout;

namespace JGraph.Application.Scripting;

/// <summary>One tool pane: what it is called, where it belongs, and the control it hosts.</summary>
/// <param name="ContentId">
/// The pane's identity in a saved dock layout. <b>Never rename one.</b> A layout stores panes by this
/// string, so a rename silently orphans that pane in every session anyone has already saved.
/// </param>
/// <param name="Title">The pane's caption.</param>
/// <param name="DefaultSide">Where the pane is recreated when a layout has no place for it.</param>
/// <param name="Content">The control the pane hosts.</param>
public sealed record PaneDescriptor(
    string ContentId,
    string Title,
    AnchorableShowStrategy DefaultSide,
    object Content);

/// <summary>
/// The tool panes the workspace offers, in menu order. One registry serves three jobs that must
/// agree: rebinding controls during a layout restore, building the View menu, and recreating a pane
/// that a saved layout does not mention (an upgrade that adds a pane, or a layout written before it
/// existed).
/// </summary>
public static class PaneCatalog
{
    /// <summary>Builds the catalog over the window's named controls.</summary>
    /// <remarks>A method, not a constant: the controls only exist after InitializeComponent.</remarks>
    public static IReadOnlyList<PaneDescriptor> For(ScriptWorkspaceWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return
        [
            new("files", "Files", AnchorableShowStrategy.Left, window.FilesPanel),
            new("console", "Console", AnchorableShowStrategy.Bottom, window.ConsolePanel),
            new("dataviewer", "Data Viewer", AnchorableShowStrategy.Bottom, window.DataViewer),
            new("variables", "Workspace", AnchorableShowStrategy.Right, window.VariablesList),
            new("callstack", "Call Stack", AnchorableShowStrategy.Right, window.CallStackList),
        ];
    }
}
