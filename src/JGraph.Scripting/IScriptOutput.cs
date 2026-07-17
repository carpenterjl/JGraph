namespace JGraph.Scripting;

/// <summary>
/// The sink a running script writes to. A host implementation typically appends to a console panel;
/// engines call it from a background thread, so implementations must marshal to the UI thread themselves.
/// </summary>
public interface IScriptOutput
{
    /// <summary>Writes text without a trailing newline (used for streamed stdout).</summary>
    void Write(string text);

    /// <summary>Writes a line of normal output.</summary>
    void WriteLine(string text);

    /// <summary>Writes a line of error output (stderr, diagnostics, exceptions).</summary>
    void WriteError(string text);
}
