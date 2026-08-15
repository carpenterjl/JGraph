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

    /// <summary>Creates a runtime error carrying MATLAB's <c>component:mnemonic</c> identifier.</summary>
    public JgsRuntimeException(int line, int column, string identifier, string message)
        : base(line, column, message)
    {
        Identifier = identifier;
    }

    /// <summary>
    /// The <c>component:mnemonic</c> identifier a script can branch on in a <c>catch</c>, or the empty
    /// string for the errors the runtime raises itself.
    /// </summary>
    /// <remarks>
    /// Empty is the honest answer for a runtime error, and a deliberate one: inventing identifiers for
    /// the interpreter's own messages would mean promising spellings that MATLAB's do not match, and a
    /// script that switched on one would take the wrong branch on real MATLAB. Only what a script
    /// itself raised carries an identifier — see ADR 0062.
    /// </remarks>
    public string Identifier { get; } = string.Empty;

    private readonly List<(string Name, string File, int Line)> _frames = [];
    private int _lineForNextFrame = -1;

    /// <summary>The functions this error unwound through, innermost first — what <c>ME.stack</c> reports.</summary>
    public IReadOnlyList<(string Name, string File, int Line)> Frames => _frames;

    /// <summary>
    /// Records that the error escaped <paramref name="name"/>, which was entered from
    /// <paramref name="callLine"/>. Called as each frame unwinds, so the frames arrive innermost-first
    /// and each carries the line that was executing <em>in it</em>: the innermost failed where the
    /// error is, and every outer one failed at the call it was waiting on.
    /// </summary>
    internal void PushFrame(string name, string file, int callLine)
    {
        _frames.Add((name, file, _lineForNextFrame < 0 ? Line : _lineForNextFrame));
        _lineForNextFrame = callLine;
    }
}
