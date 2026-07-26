namespace JGraph.Scripting;

/// <summary>
/// An engine that can host an interactive session — a console prompt where statements execute one at a
/// time against state that survives between them. This is a capability, not part of
/// <see cref="IScriptEngine"/>: hosts feature-detect with <c>is IScriptRepl</c>, exactly as they already
/// do for <c>IJgsDebuggable</c>, so an engine that only knows how to run a whole file stays valid.
/// </summary>
public interface IScriptRepl
{
    /// <summary>
    /// Creates a session bound to <paramref name="context"/>. The context is captured for the session's
    /// whole lifetime, so its output sink and figure callback must outlive a single statement.
    /// </summary>
    IScriptSession CreateSession(ScriptContext context);
}

/// <summary>
/// One live interactive session: a workspace of variables, an interpreter (or child process) holding it,
/// and whatever figures the session has created. Statements run one at a time — a session is not
/// re-entrant, and a host must not call <see cref="ExecuteAsync"/> again until the previous call has
/// completed.
/// </summary>
/// <remarks>
/// A session deliberately does <em>not</em> reset between statements, which is the whole point: figures
/// stay open, packed buffers stay alive because live variables still point at them, and the built-in
/// scope is created once. The cost is that buffers are only released by <see cref="Clear"/> or by
/// disposing the session — see ADR 0035.
/// </remarks>
public interface IScriptSession : IAsyncDisposable
{
    /// <summary>The language this session runs, matching its engine's <see cref="IScriptEngine.Language"/>.</summary>
    string Language { get; }

    /// <summary>
    /// Executes <paramref name="code"/> against the session's workspace. Reuses
    /// <see cref="ScriptRunResult"/>, so a failure comes back as a result rather than an exception, and
    /// <see cref="ScriptRunResult.Variables"/> carries the workspace as it stands after the statement.
    /// </summary>
    /// <param name="code">One or more statements in this session's language.</param>
    /// <param name="sourceId">The identity of <paramref name="code"/> — a file path when the host is
    /// running a document through the session, or "" for something typed at the prompt. Engines that
    /// have no notion of source identity ignore it.</param>
    /// <param name="cancellationToken">Interrupts the statement; how promptly is engine-specific.</param>
    Task<ScriptRunResult> ExecuteAsync(string code, string sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Executes <paramref name="code"/> as a whole file rather than prompt input. Identical to
    /// <see cref="ExecuteAsync"/> except that an engine may apply whole-file semantics — for JGS and
    /// MATLAB, a <em>function file</em> (a file that is nothing but function definitions) has its main
    /// function invoked after the definitions load, exactly as running the file does in MATLAB. A
    /// function defined at the prompt must only be defined, so hosts route typed input through
    /// <see cref="ExecuteAsync"/> and document runs through this method.
    /// </summary>
    /// <param name="code">The complete text of the file being run.</param>
    /// <param name="sourceId">The file's path, or "" for an unsaved document.</param>
    /// <param name="cancellationToken">Interrupts the run; how promptly is engine-specific.</param>
    Task<ScriptRunResult> ExecuteFileAsync(string code, string sourceId, CancellationToken cancellationToken) =>
        ExecuteAsync(code, sourceId, cancellationToken);

    /// <summary>The session's current workspace, for a variables panel. Cheap enough to call after
    /// every statement.</summary>
    IReadOnlyList<ScriptVariable> GetVariables();

    /// <summary>
    /// Empties the workspace back to a pristine state: variables go, the buffers they held are released,
    /// and the figure registry is reset. This is what <c>clear</c> and <c>Clear Workspace</c> mean, and
    /// it is the only point besides disposal at which a long session gives memory back.
    /// </summary>
    void Clear();
}
