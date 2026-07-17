namespace JGraph.Scripting.Jgs;

/// <summary>
/// The base type for every error the JGS language raises — lexing, parsing, and interpretation. Each error
/// records the 1-based <see cref="Line"/> and <see cref="Column"/> in the script it refers to (0 when the
/// location is unknown) so the engine can map it onto a <see cref="ScriptDiagnostic"/>.
/// </summary>
public abstract class JgsException : Exception
{
    /// <summary>Creates the exception at a 1-based <paramref name="line"/>/<paramref name="column"/>.</summary>
    protected JgsException(int line, int column, string message)
        : base(message)
    {
        Line = line;
        Column = column;
    }

    /// <summary>The 1-based line the error refers to, or 0 when unknown.</summary>
    public int Line { get; }

    /// <summary>The 1-based column the error refers to, or 0 when unknown.</summary>
    public int Column { get; }
}

/// <summary>An error raised while lexing or parsing a script (a malformed program).</summary>
public sealed class JgsSyntaxException : JgsException
{
    /// <summary>Creates a syntax error at a 1-based <paramref name="line"/>/<paramref name="column"/>.</summary>
    public JgsSyntaxException(int line, int column, string message)
        : base(line, column, message)
    {
    }
}

/// <summary>An error raised while interpreting a script (a well-formed program that failed at run time).</summary>
public sealed class JgsRuntimeException : JgsException
{
    /// <summary>Creates a runtime error at a 1-based <paramref name="line"/>/<paramref name="column"/>.</summary>
    public JgsRuntimeException(int line, int column, string message)
        : base(line, column, message)
    {
    }
}
