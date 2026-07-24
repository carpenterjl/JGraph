namespace JGraph.Scripting;

/// <summary>
/// The outcome of running a script: whether it succeeded, an optional summary message, any diagnostics
/// (compile errors, warnings, or a mapped runtime error location), and how many figures it displayed.
/// Engines never throw for ordinary script failures — they report them here.
/// </summary>
public sealed class ScriptRunResult
{
    private static readonly IReadOnlyList<ScriptDiagnostic> NoDiagnostics = Array.Empty<ScriptDiagnostic>();
    private static readonly IReadOnlyList<ScriptVariable> NoVariables = Array.Empty<ScriptVariable>();

    private ScriptRunResult(
        bool success,
        string? message,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        int figuresShown,
        IReadOnlyList<ScriptVariable> variables,
        int? exitCode = null)
    {
        Success = success;
        Message = message;
        Diagnostics = diagnostics;
        FiguresShown = figuresShown;
        Variables = variables;
        ExitCode = exitCode;
    }

    /// <summary>Whether the script compiled and ran to completion without an unhandled error.</summary>
    public bool Success { get; }

    /// <summary>A short human-readable summary (e.g. an error headline), or null.</summary>
    public string? Message { get; }

    /// <summary>Compile/run diagnostics, most-relevant first; empty when there were none.</summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }

    /// <summary>The number of figures the script displayed via <c>show()</c>.</summary>
    public int FiguresShown { get; }

    /// <summary>
    /// A snapshot of the variables the script left defined when it finished (for a workspace variables
    /// panel); empty when the engine does not capture them or the run failed.
    /// </summary>
    public IReadOnlyList<ScriptVariable> Variables { get; }

    /// <summary>
    /// The exit code the script asked for with <c>exit(code)</c>, or null when it never called it.
    /// A batch host returns this from the process; an interactive host shuts down with it.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>A successful run that displayed <paramref name="figuresShown"/> figures.</summary>
    public static ScriptRunResult Ok(int figuresShown, IReadOnlyList<ScriptVariable>? variables = null) =>
        new(success: true, message: null, NoDiagnostics, figuresShown, variables ?? NoVariables);

    /// <summary>
    /// A run the script ended itself with <c>exit</c>/<c>quit</c>. It counts as a success — the script
    /// did what it meant to — but carries the code it asked the process to exit with.
    /// </summary>
    public static ScriptRunResult Exited(
        int exitCode, int figuresShown, IReadOnlyList<ScriptVariable>? variables = null) =>
        new(success: true, message: null, NoDiagnostics, figuresShown, variables ?? NoVariables, exitCode);

    /// <summary>A failed run with a summary <paramref name="message"/> and optional <paramref name="diagnostics"/>.</summary>
    public static ScriptRunResult Failed(string message, IReadOnlyList<ScriptDiagnostic>? diagnostics = null) =>
        new(success: false, message, diagnostics ?? NoDiagnostics, figuresShown: 0, NoVariables);
}
