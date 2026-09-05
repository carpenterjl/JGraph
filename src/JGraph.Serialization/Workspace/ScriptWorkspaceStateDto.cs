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

    /// <summary>
    /// The language the console prompt was set to ("MATLAB", "JGS", …), or null when never chosen.
    /// Part of the session rather than the settings: it is picked in the window, beside the layout,
    /// and comes back with it.
    /// </summary>
    public string? ConsoleLanguage { get; set; }

    /// <summary>
    /// The arrangement generation <see cref="DockLayoutXml"/> was written by (see
    /// <see cref="ScriptWorkspaceStateFormat.CurrentLayoutSchema"/>). A release that rearranges the
    /// default panes incompatibly bumps it, and a layout from an older generation is discarded rather
    /// than half-restored. Separate from <see cref="FormatVersion"/> so the two can move independently.
    /// </summary>
    public int LayoutSchema { get; set; }

    /// <summary>The shell window's last position, or null when it was never recorded.</summary>
    public double? WindowLeft { get; set; }

    /// <summary>The shell window's last position, or null when it was never recorded.</summary>
    public double? WindowTop { get; set; }

    /// <summary>The shell window's last size, or null when it was never recorded.</summary>
    public double? WindowWidth { get; set; }

    /// <summary>The shell window's last size, or null when it was never recorded.</summary>
    public double? WindowHeight { get; set; }

    /// <summary>The shell window's last state ("Normal" or "Maximized"), or null. A minimized window
    /// is recorded as normal — reopening minimized would look like a failure to launch.</summary>
    public string? WindowState { get; set; }
}
