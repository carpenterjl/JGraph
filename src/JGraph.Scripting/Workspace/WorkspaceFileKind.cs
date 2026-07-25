using System.IO;

namespace JGraph.Scripting.Workspace;

/// <summary>What opening a workspace file should do.</summary>
public enum WorkspaceFileKind
{
    /// <summary>Nothing here can open it.</summary>
    Unsupported,

    /// <summary>A table to load into the data viewer (<c>.csv</c>, <c>.tsv</c>, <c>.xlsx</c>).</summary>
    Data,

    /// <summary>A saved figure document (<c>.graph</c>) to open as a live numbered figure.</summary>
    Figure,

    /// <summary>Text to open in an editor tab — a script, or a plain note the editor can show.</summary>
    Document,
}

/// <summary>
/// Which pane a workspace file belongs to, by extension. This is product policy, not a detail of the
/// tree control, so it lives here where it can be tested and reused (the file tree, drag-and-drop,
/// and the console's file-opening path all ask the same question).
/// </summary>
public static class WorkspaceFiles
{
    /// <summary>Classifies <paramref name="path"/> by its extension; the file need not exist.</summary>
    public static WorkspaceFileKind Classify(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" or ".tsv" or ".xlsx" => WorkspaceFileKind.Data,
            ".graph" => WorkspaceFileKind.Figure,
            ".jgs" or ".m" or ".csx" or ".cs" or ".py" or ".txt" or ".md" or ".json" => WorkspaceFileKind.Document,
            _ => WorkspaceFileKind.Unsupported,
        };
    }
}
