using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A live interactive session for the JGS interpreter, in either dialect. Everything
/// <see cref="JgsRunner.Run"/> builds per run — the host globals, the built-in scope, the interpreter,
/// the <c>run()</c> include builtin — is built <em>once</em> here and reused for every statement, which
/// is what makes variables, functions and figures survive from one prompt to the next.
/// </summary>
/// <remarks>
/// Three things a one-shot run does are deliberately absent between statements, and only happen in
/// <see cref="Clear"/> or <see cref="DisposeAsync"/>: <c>JG.Reset()</c> (figures must stay open, and
/// <c>figure(1)</c> at the prompt must keep meaning the window it already opened), releasing packed
/// buffers (live variables still point at them), and rebuilding the built-in scope. See ADR 0035.
/// </remarks>
internal sealed class JgsReplSession : IScriptSession, IGraphicsEventSession
{
    private readonly ScriptContext _context;
    private readonly JgsDialect _dialect;

    private JGraphScriptGlobals _globals = null!;
    private JgsEnvironment _environment = null!;
    private Interpreter _interpreter = null!;
    private JgsCallbackDispatcher _dispatcher = null!;
    private Dictionary<string, JgsValue> _pristine = null!;
    private bool _disposed;

    /// <summary>1 while a statement or an event-pump run owns the interpreter (the two must not
    /// overlap — callbacks themselves run nested inside whichever of the two holds this).</summary>
    private int _busy;

    /// <summary>Creates a session and its first workspace. Resets the figure registry: a new session
    /// is a new workspace, and its figure numbers start from a known state.</summary>
    /// <param name="context">The host services for the session's whole lifetime.</param>
    /// <param name="dialect">The language variant every statement is parsed in.</param>
    /// <param name="language">The engine language name this session reports.</param>
    public JgsReplSession(ScriptContext context, JgsDialect dialect, string language)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        Language = language ?? throw new ArgumentNullException(nameof(language));
        JG.Reset();
        JgsHandleRegistry.Clear();
        Build();
    }

    /// <inheritdoc />
    public string Language { get; }

    /// <inheritdoc />
    public Task<ScriptRunResult> ExecuteAsync(string code, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(sourceId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The token is deliberately not passed to ScriptThread.Run: Execute maps cancellation to a
        // failed result, and a token already cancelled at call time would otherwise cancel the task
        // instead.
        return ScriptThread.Run(() => Execute(code, sourceId, cancellationToken));
    }

    /// <inheritdoc />
    public Task<ScriptRunResult> ExecuteFileAsync(string code, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(sourceId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ScriptThread.Run(() => Execute(code, sourceId, cancellationToken, asFile: true));
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptVariable> GetVariables() =>
        _disposed ? Array.Empty<ScriptVariable>() : JgsRunner.SnapshotGlobals(_environment, _pristine);

    /// <inheritdoc />
    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseWorkspace();
        JG.Reset();
        JgsHandleRegistry.Clear();
        Build();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            JgsCallbackDispatcher.Install(null);
            ReleaseWorkspace();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public Task DrainGraphicsEventsAsync(Func<bool>? yieldRequested, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ScriptThread.Run(() => DrainGraphicsEvents(yieldRequested, cancellationToken));
    }

    /// <summary>
    /// An idle pump run: the same ceremony as a statement, wrapped around delivering queued events
    /// instead of parsing code. Losing the race to a real statement is fine — the events stay
    /// queued, and the host tries again when that statement finishes.
    /// </summary>
    private ScriptRunResult DrainGraphicsEvents(Func<bool>? yieldRequested, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return ScriptRunResult.Ok(0, Array.Empty<ScriptVariable>());
        }

        _dispatcher.StatementThreadId = Environment.CurrentManagedThreadId;
        try
        {
            _globals.BeginRun(null, null);
            _interpreter.BeginStatement(cancellationToken);
            _dispatcher.StatementToken = cancellationToken;
            try
            {
                _dispatcher.Drain(yieldRequested);
            }
            catch (OperationCanceledException)
            {
                return ScriptRunResult.Failed("Statement was cancelled.");
            }
            finally
            {
                _globals.ShowTouchedFigures();
            }

            return ScriptRunResult.Ok(_globals.FiguresShown, GetVariables());
        }
        finally
        {
            _dispatcher.StatementThreadId = null;
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private ScriptRunResult Execute(string code, string sourceId, CancellationToken cancellationToken, bool asFile = false)
    {
        Interlocked.Exchange(ref _busy, 1);
        _dispatcher.StatementThreadId = Environment.CurrentManagedThreadId;
        try
        {
            return ExecuteCore(code, sourceId, cancellationToken, asFile);
        }
        finally
        {
            _dispatcher.StatementThreadId = null;
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private ScriptRunResult ExecuteCore(string code, string sourceId, CancellationToken cancellationToken, bool asFile)
    {
        _globals.BeginRun(asFile ? ScriptDirectoryOf(sourceId) : null, asFile ? sourceId : null);
        _interpreter.BeginStatement(cancellationToken);
        _dispatcher.StatementToken = cancellationToken;
        try
        {
            IReadOnlyList<Stmt> program = Parser.Parse(code, sourceId, _dialect);
            _interpreter.Run(program);
            if (asFile)
            {
                // A file that is nothing but function definitions is a MATLAB function file: running
                // it means calling its main function. Prompt input never takes this branch.
                JgsRunner.InvokeMainIfFunctionFile(program, _environment);
            }

            _globals.ShowTouchedFigures();
            return ScriptRunResult.Ok(_globals.FiguresShown, GetVariables());
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is { } exit)
        {
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Exited(exit.ExitCode, _globals.FiguresShown, GetVariables());
        }
        catch (JgsException ex)
        {
            // A failed statement leaves the workspace as it stands — that is the point of a prompt.
            // Whatever ran before the error keeps its effect, exactly as MATLAB does.
            var diagnostic = new ScriptDiagnostic(ex.Line, ex.Column, ex.Message, IsError: true);
            _context.Output.WriteError(diagnostic.ToString());
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Failed(ex.Message, new[] { diagnostic });
        }
        catch (OperationCanceledException)
        {
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Failed("Statement was cancelled.");
        }
        catch (Exception ex)
        {
            // A defect in this build, not in the statement (M76). A prompt above all must survive
            // one: the workspace stands, the session goes on, and the statement is reported failed.
            var diagnostic = new ScriptDiagnostic(0, 0,
                $"Internal error: {ex.GetType().Name}: {ex.Message}", IsError: true);
            _context.Output.WriteError(diagnostic.ToString());
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Failed(diagnostic.Message, new[] { diagnostic });
        }
    }

    /// <summary>
    /// The folder a file run's relative paths are tried against. The source id is the document's path
    /// when it has one; an unsaved document passes an empty id and simply has no folder of its own.
    /// </summary>
    private static string? ScriptDirectoryOf(string sourceId) =>
        sourceId.Length > 0 && System.IO.Path.IsPathRooted(sourceId)
            ? System.IO.Path.GetDirectoryName(sourceId)
            : null;

    private void Build()
    {
        _globals = new JGraphScriptGlobals(_context);
        _environment = JgsBuiltins.CreateGlobals(_globals, CancellationToken.None, _dialect);

        // The interpreter's token is replaced per statement by BeginStatement; the one it is
        // constructed with only covers the (impossible) window before the first statement.
        _interpreter = new Interpreter(_environment, CancellationToken.None, hook: null,
            echo: line => _context.Output.WriteLine(line), _dialect);
        JgsRunner.DefineRunBuiltin(_environment, _interpreter, _globals, _dialect);
        JgsBuiltins.RegisterEvalBuiltins(_environment, _interpreter, _globals, _dialect);
        JgsBuiltins.RegisterSessionBuiltins(_environment, _globals);
        JgsWorkspaceIo.DefineSaveLoad(_environment, _globals, () => UserVariables());
        JgsRunner.DefineWorkspaceBuiltins(_environment, _interpreter, _context.Output, () => _pristine);

        _pristine = _environment.Locals.ToDictionary(
            static p => p.Key, static p => p.Value, StringComparer.Ordinal);

        // A figure window outlives the run that drew it, so its clicks come back to this session —
        // queued, and delivered on the script thread. Nothing runs the interpreter on a window's
        // thread: a click during a running statement waits at the next drain point instead of being
        // dropped, and the callback gets the 16 MB script stack it was written for.
        _dispatcher = new JgsCallbackDispatcher(_globals, _context);
        JgsCallbackDispatcher.Install(_dispatcher);
    }

    /// <summary>The user-created (or rebound) names and their values, the set <c>whos</c> lists.</summary>
    private IEnumerable<(string Name, JgsValue Value)> UserVariables()
    {
        foreach ((string name, JgsValue value) in _environment.Locals)
        {
            if (!_pristine.TryGetValue(name, out JgsValue? original) || !ReferenceEquals(original, value))
            {
                yield return (name, value);
            }
        }
    }

    private void ReleaseWorkspace()
    {
        _globals.CloseAllFiles();
        JgsRunner.DisposeBuffers(UserVariables().Select(static pair => pair.Value));
    }

}
