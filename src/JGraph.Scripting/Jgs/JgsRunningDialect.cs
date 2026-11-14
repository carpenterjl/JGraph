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
    public JgsDialect Current
    {
        get => _current;
        set
        {
            _current = value;
            t_matlab = value.IsMatlab;
        }
    }

    private JgsDialect _current = null!;

    [ThreadStatic]
    private static bool t_matlab;

    /// <summary>
    /// Whether the code this thread last entered is MATLAB's, for a static helper with no slot in hand
    /// (the arity checks, ADR 0214): set with <see cref="Current"/>, so it follows every body entry.
    /// </summary>
    internal static bool ThreadIsMatlab => t_matlab;

    /// <summary>How many bodies of code this thread is inside now: the interpreter's entries count it.</summary>
    [ThreadStatic]
    private static int t_entered;

    /// <summary>
    /// Whether MATLAB code is running on this thread now - <see cref="ThreadIsMatlab"/> inside a body
    /// the interpreter entered, and false outside every run (open items 155 and 164, ADR 0362).
    /// <see cref="ThreadIsMatlab"/> outlives its session: building a slot sets it and nothing clears
    /// it, so a thread that once built a MATLAB session reads MATLAB for host code run on it later.
    /// A reader reachable from code no script is running - the hook every new axes in the process
    /// passes through, the MAT-file writer's static API - asks this instead.
    /// </summary>
    internal static bool ThreadRunsMatlab => t_entered > 0 && t_matlab;

    /// <summary>Counts an entry into a body of code on this thread; <see cref="Leave"/> undoes it.</summary>
    internal static void Enter() => t_entered++;

    /// <summary>Counts a body of code left on this thread.</summary>
    internal static void Leave() => t_entered--;

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
