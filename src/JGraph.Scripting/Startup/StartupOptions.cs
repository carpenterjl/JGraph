namespace JGraph.Scripting.Startup;

/// <summary>How the executable was asked to start.</summary>
public enum StartupMode
{
    /// <summary>No script options — open the normal interactive application.</summary>
    Interactive,

    /// <summary>Run a statement non-interactively, log to standard output, and exit (<c>-batch</c>).</summary>
    Batch,

    /// <summary>Run a statement, then keep the interactive session open until <c>exit</c> (<c>-r</c>).</summary>
    Run,

    /// <summary>Show the startup-option documentation (<c>-h</c>/<c>-help</c>).</summary>
    Help,
}

/// <summary>
/// The parsed command line, shared by both executables: the headless launcher (<c>jgraph.exe</c>) and
/// the WPF application, which must agree exactly on what a given argument list means because the
/// launcher forwards some of them verbatim to the application.
/// </summary>
/// <param name="Mode">What to do.</param>
/// <param name="Statement">The <c>-batch</c>/<c>-r</c> argument: JGS source, or the path of a script
/// file (see <see cref="StartupStatement"/>). Null in the other modes.</param>
/// <param name="LogFile">The <c>-logfile</c> path, or null.</param>
/// <param name="StartDirectory">The <c>-sd</c> directory, or null to use the process's current
/// directory — which, for a run launched from a shell, is the shell's current directory.</param>
/// <param name="ShowFigures">Whether <c>-showfigures</c> was given: a batch run displays its figures
/// in standalone windows instead of suppressing them.</param>
/// <param name="UsageError">The reason the command line was rejected, or null when it parsed.</param>
public sealed record StartupOptions(
    StartupMode Mode,
    string? Statement = null,
    string? LogFile = null,
    string? StartDirectory = null,
    bool ShowFigures = false,
    string? UsageError = null)
{
    /// <summary>The default: launch the interactive application.</summary>
    public static StartupOptions Interactive { get; } = new(StartupMode.Interactive);

    /// <summary>A rejected command line, carrying the reason to show the user.</summary>
    public static StartupOptions Invalid(string reason) =>
        new(StartupMode.Interactive, UsageError: reason);

    /// <summary>Whether the command line was rejected (the caller should print usage and exit 2).</summary>
    public bool HasUsageError => UsageError is not null;
}
