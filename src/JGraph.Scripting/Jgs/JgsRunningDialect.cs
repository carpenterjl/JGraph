namespace JGraph.Scripting.Jgs;

/// <summary>
/// The dialect of the code that is running, as one slot the interpreter swaps at every entry into a
/// body of code and every built-in reads at the call (V11, ADR 0172). Code carries its dialect: a
/// function, an anonymous function, a script and a class method record the dialect of the file they
/// were parsed from, and every call enters that dialect for its body, restoring the caller's on
/// exit, an error included. What a built-in does with an index, a format string, a bracket or a
/// binding is a property of the code that called it, not of the session that registered it, so
/// the registrars capture this slot rather than the dialect the session was built with — which
/// stays readable as <see cref="Session"/> for the few things that are the session's own: the
/// prompt's parser, the completion engine, and the shadowing warning a JGS session never raises.
/// </summary>
/// <remarks>
/// The members mirror <see cref="JgsDialect"/>'s so a registrar's <c>dialect.IndexBase</c> reads
/// the running value unchanged, and the implicit conversion hands the running dialect to any helper
/// typed <see cref="JgsDialect"/> at the moment of the call.
/// </remarks>
internal sealed class JgsRunningDialect
{
    /// <summary>Creates the slot over the dialect the session was built with; the running dialect starts as it.</summary>
    public JgsRunningDialect(JgsDialect session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        Current = session;
    }

    /// <summary>The dialect the session was built with — the host's, never the running code's.</summary>
    public JgsDialect Session { get; }

    /// <summary>The dialect of the code running now; the interpreter swaps it at every body entry.</summary>
    public JgsDialect Current { get; set; }

    /// <inheritdoc cref="JgsDialect.Name"/>
    public string Name => Current.Name;

    /// <inheritdoc cref="JgsDialect.IndexBase"/>
    public int IndexBase => Current.IndexBase;

    /// <inheritdoc cref="JgsDialect.RequireLet"/>
    public bool RequireLet => Current.RequireLet;

    /// <inheritdoc cref="JgsDialect.PercentComment"/>
    public bool PercentComment => Current.PercentComment;

    /// <inheritdoc cref="JgsDialect.QuoteTranspose"/>
    public bool QuoteTranspose => Current.QuoteTranspose;

    /// <inheritdoc cref="JgsDialect.CopyOnAssign"/>
    public bool CopyOnAssign => Current.CopyOnAssign;

    /// <inheritdoc cref="JgsDialect.MatlabFunctions"/>
    public bool MatlabFunctions => Current.MatlabFunctions;

    /// <inheritdoc cref="JgsDialect.MatlabBlocks"/>
    public bool MatlabBlocks => Current.MatlabBlocks;

    /// <inheritdoc cref="JgsDialect.CellBraceSyntax"/>
    public bool CellBraceSyntax => Current.CellBraceSyntax;

    /// <inheritdoc cref="JgsDialect.FunctionScope"/>
    public bool FunctionScope => Current.FunctionScope;

    /// <inheritdoc cref="JgsDialect.ConcatenatesBrackets"/>
    public bool ConcatenatesBrackets => Current.ConcatenatesBrackets;

    /// <inheritdoc cref="JgsDialect.IsMatlab"/>
    public bool IsMatlab => Current.IsMatlab;

    /// <inheritdoc cref="JgsDialect.HasStringArrays"/>
    public bool HasStringArrays => Current.HasStringArrays;

    /// <summary>The running dialect, for a helper typed <see cref="JgsDialect"/> — read at the call, never earlier.</summary>
    public static implicit operator JgsDialect(JgsRunningDialect running) => running.Current;
}
