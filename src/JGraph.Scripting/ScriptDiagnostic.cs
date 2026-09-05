using JGraph.Scripting.Jgs;

namespace JGraph.Scripting;

/// <summary>
/// A single compile- or run-time message from a script engine, located at a 1-based line and column in
/// the script source. Both engines map their native diagnostics (Roslyn diagnostics, Python tracebacks)
/// onto this shape so a host can present them uniformly.
/// </summary>
/// <param name="Line">The 1-based line the message refers to, or 0 when unknown.</param>
/// <param name="Column">The 1-based column the message refers to, or 0 when unknown.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="IsError">True for errors; false for warnings or informational notes.</param>
/// <param name="File">
/// The file the location counts in when it is not the source being run — a script or function
/// reached through the search path — or "" when it is, or is unknown.
/// </param>
public sealed record ScriptDiagnostic(int Line, int Column, string Message, bool IsError, string File = "")
{
    /// <summary>
    /// Formats the diagnostic as <c>(line,col): message</c>, prefixed by the file when the location
    /// is in another file (<c>C:/work/sub.m(4,12): message</c>), and omitting the location when unknown.
    /// </summary>
    public override string ToString() =>
        Line > 0 ? $"{File}({Line},{Column}): {Message}"
        : File.Length > 0 ? $"{File}: {Message}"
        : Message;

    /// <summary>
    /// The diagnostic for a script error, naming the file only when it is not the one being run:
    /// the run's own source is what the reader is looking at, and its errors read as they always have.
    /// </summary>
    public static ScriptDiagnostic For(JgsException error, string runSourceId) =>
        new(error.Line, error.Column, error.Message, IsError: true,
            string.Equals(error.SourceId, runSourceId, StringComparison.OrdinalIgnoreCase) ? "" : error.SourceId);
}
