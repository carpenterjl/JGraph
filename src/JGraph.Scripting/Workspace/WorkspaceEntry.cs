namespace JGraph.Scripting.Workspace;

/// <summary>
/// One file or directory inside a script workspace, as enumerated by
/// <see cref="ScriptWorkspace.EnumerateAll"/>. Directories carry their children so the tree can be
/// bound directly to a hierarchical view.
/// </summary>
/// <param name="FullPath">The absolute path of the entry.</param>
/// <param name="RelativePath">The path relative to the workspace root (the root itself is "").</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
/// <param name="Children">The entry's children (directories first, then files); empty for files.</param>
public sealed record WorkspaceEntry(
    string FullPath,
    string RelativePath,
    bool IsDirectory,
    IReadOnlyList<WorkspaceEntry> Children)
{
    /// <summary>The file or directory name (the last path segment).</summary>
    public string Name => System.IO.Path.GetFileName(FullPath);
}
