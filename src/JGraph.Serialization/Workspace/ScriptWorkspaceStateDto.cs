namespace JGraph.Serialization.Workspace;

/// <summary>
/// The persisted state of the scripting workspace between sessions: which folder was open, which
/// files were open (and active), the breakpoints per file, and the docking layout. A plain DTO —
/// the live workspace types stay free of serialization concerns.
/// </summary>
public sealed class ScriptWorkspaceStateDto
{
    /// <summary>The format tag (see <see cref="ScriptWorkspaceStateFormat.FormatTag"/>).</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>The schema version (see <see cref="ScriptWorkspaceStateFormat.CurrentVersion"/>).</summary>
    public int FormatVersion { get; set; }

    /// <summary>The workspace root folder last opened, or null when none was.</summary>
    public string? RootPath { get; set; }

    /// <summary>The full paths of the files that were open, in tab order.</summary>
    public List<string> OpenFiles { get; set; } = [];

    /// <summary>The full path of the active (focused) file, or null.</summary>
    public string? ActiveFile { get; set; }

    /// <summary>Breakpoints per file: full path → 1-based line numbers.</summary>
    public Dictionary<string, List<int>> Breakpoints { get; set; } = [];

    /// <summary>The docking layout as serialized by the dock manager, or null for the default layout.</summary>
    public string? DockLayoutXml { get; set; }
}
