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
public sealed record ScriptDiagnostic(int Line, int Column, string Message, bool IsError)
{
    /// <summary>Formats the diagnostic as <c>(line,col): message</c>, omitting the location when unknown.</summary>
    public override string ToString() =>
        Line > 0 ? $"({Line},{Column}): {Message}" : Message;
}
