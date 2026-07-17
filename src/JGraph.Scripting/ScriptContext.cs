using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>
/// The host services a script may use while it runs: where to write output, how to display a finished
/// figure, and the directory that relative file paths (e.g. <c>readcsv("data.csv")</c>) resolve against.
/// The engine passes this to the script's globals. The two callbacks are invoked from the engine's
/// background thread, so a UI host must marshal them onto its dispatcher.
/// </summary>
public sealed class ScriptContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="output">Where script output is written.</param>
    /// <param name="showFigure">Invoked when the script calls <c>show()</c>, with the figure's number
    /// (1-based, MATLAB-style) and the figure to display.</param>
    /// <param name="workingDirectory">Base directory for relative file paths, or null to use the process directory.</param>
    public ScriptContext(IScriptOutput output, Action<int, FigureModel> showFigure, string? workingDirectory = null)
        : this(output, showFigure, workingDirectory, resolvePath: null)
    {
    }

    /// <summary>Creates a context whose relative file paths resolve through a workspace resolver.</summary>
    /// <param name="output">Where script output is written.</param>
    /// <param name="showFigure">Invoked when the script calls <c>show()</c>, with the figure's number
    /// (1-based, MATLAB-style) and the figure to display.</param>
    /// <param name="workingDirectory">Fallback base directory when <paramref name="resolvePath"/> is null.</param>
    /// <param name="resolvePath">Maps a script-supplied path to the path to open (e.g. probing the
    /// script's directory then the workspace root), or null to use <paramref name="workingDirectory"/>.</param>
    /// <param name="figureFiles">Figure save/load/export services, or null when the host offers none
    /// (the corresponding builtins then fail with a clear message).</param>
    public ScriptContext(
        IScriptOutput output,
        Action<int, FigureModel> showFigure,
        string? workingDirectory,
        Func<string, string>? resolvePath,
        IScriptFigureFiles? figureFiles = null)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        ShowFigure = showFigure ?? throw new ArgumentNullException(nameof(showFigure));
        WorkingDirectory = workingDirectory;
        ResolvePath = resolvePath;
        FigureFiles = figureFiles;
    }

    /// <summary>The sink for script output.</summary>
    public IScriptOutput Output { get; }

    /// <summary>The callback that displays a figure produced by the script, keyed by its 1-based number
    /// so the host can reuse one window per figure across runs.</summary>
    public Action<int, FigureModel> ShowFigure { get; }

    /// <summary>Figure save/load/export services, or null when the host offers none.</summary>
    public IScriptFigureFiles? FigureFiles { get; }

    /// <summary>The directory relative file paths resolve against, or null for the process directory.</summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// The workspace path resolver script file access flows through, or null to resolve against
    /// <see cref="WorkingDirectory"/>. Invoked on the engine's background thread.
    /// </summary>
    public Func<string, string>? ResolvePath { get; }
}
