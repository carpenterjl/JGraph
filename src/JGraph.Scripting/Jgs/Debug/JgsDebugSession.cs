using System.IO;
using System.Linq;
using System.Threading;

namespace JGraph.Scripting.Jgs.Debug;

/// <summary>
/// An interactive debug run of a JGS script: breakpoints, pause/continue, step in/over/out, and — while
/// paused — the call stack, variables, set-next-statement, and live code edits. One session runs one
/// script, once.
///
/// Threading model: the interpreter runs on a background thread and calls into the session before every
/// statement. Pausing blocks that thread on a gate; <see cref="Paused"/>/<see cref="Resumed"/> are
/// raised on the interpreter thread, so a UI host must marshal. While <see cref="IsPaused"/> is true the
/// interpreter thread is blocked, which is what makes <see cref="GetCallStack"/>/<see cref="GetVariables"/>
/// race-free to call from another thread — and what makes <see cref="TryApplyEdit"/> free to mutate the
/// program and <see cref="EvaluateAsync"/> free to borrow the interpreter for a typed statement.
/// Cancelling the run token aborts the run even while paused — the gate wait observes it and
/// unwinds exactly like the interpreter's cooperative cancellation.
///
/// A run either owns a fresh workspace (<see cref="RunAsync(string, string, ScriptContext, CancellationToken)"/>)
/// or runs inside a live console session's (<see cref="RunAsync(IScriptSession, string, string, CancellationToken)"/>),
/// which is what MATLAB does: a script paused at a breakpoint is in the base workspace, and what it
/// leaves behind is there at the prompt afterwards.
/// </summary>
public sealed class JgsDebugSession
{
    private readonly object _stateLock = new();
    private readonly ManualResetEventSlim _gate = new(initialState: false);
    private readonly List<FrameEntry> _frames = new();
    private readonly List<BlockEntry> _blocks = new();
    private readonly Hook _hook;

    private volatile Dictionary<string, HashSet<int>> _breakpoints = new(SourceIdComparer);
    private DebugMode _mode = DebugMode.Run;
    private int _stepDepth;
    private int _started;
    private CancellationToken _runToken;
    private Interpreter? _interpreter;
    private FnStmt? _pendingFunction;

    private volatile bool _isPaused;
    private volatile bool _evaluating;
    private readonly ManualResetEventSlim _evaluationDone = new(initialState: true);
    private IScriptOutput? _output;
    private JgsDebugLocation? _pausedLocation;
    private IReadOnlyList<JgsStackFrame> _pausedCallStack = Array.Empty<JgsStackFrame>();
    private JgsEnvironment? _pausedEnvironment;
    private int _pausedDepth;
    private int? _pendingJump;
    private bool _jumpArrival;

    private readonly JgsDialect _dialect;

    // The dialect of the code the run is paused in (V11, ADR 0172): the running dialect at the
    // pause, which a .m function reached from JGS makes MATLAB. The prompt parses in it.
    private JgsDialect _pausedDialect;

    /// <summary>Creates a session. Obtain one from an engine's <c>CreateDebugSession</c>.</summary>
    /// <param name="dialect">The language variant to debug, or null for <see cref="JgsDialect.Jgs"/>.</param>
    internal JgsDebugSession(JgsDialect? dialect = null)
    {
        _dialect = dialect ?? JgsDialect.Jgs;
        _pausedDialect = _dialect;
        _hook = new Hook(this);
    }

    // --- The attached form (App Designer window plan W11) -------------------------------------------
    //
    // A session's own debugger: it lives as long as the session, reads the session's breakpoint
    // store before every statement - a typed statement's, a run's, an app callback's - and pauses
    // wherever one of them is hit, so dbstop at the prompt and a breakpoint set in App Designer's
    // code view both pause a callback that runs later. It is active, and so hooked into the
    // interpreter, only while the store has something in it or a pause is wanted: the hook costs
    // the interpreter its fast path.

    private JgsBreakpointStore? _store;
    private volatile IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> _storeLines = new Dictionary<string, IReadOnlyDictionary<int, string>>();
    private readonly Dictionary<string, JgsSourceMap?> _maps = new(SourceIdComparer);
    private CancellationTokenSource? _quit;
    private bool _pauseWanted;

    // Where the interpreter is between the hook's calls, for a pause raised inside a statement (a
    // warning, output): read on the interpreter thread only.
    private BlockExecution? _currentBlock;
    private int _currentIndex;
    private JgsEnvironment? _currentEnv;
    private int _currentDepth;

    /// <summary>
    /// Makes the debugger a <see cref="JgsReplSession"/> keeps for its whole life, over the session's
    /// <paramref name="store"/> of breakpoints. The session binds it to each interpreter it builds
    /// (<see cref="Bind"/>) and installs its <see cref="Hook"/> while <see cref="IsActive"/>.
    /// </summary>
    internal static JgsDebugSession Attached(JgsBreakpointStore store, JgsDialect dialect, string language)
    {
        ArgumentNullException.ThrowIfNull(store);
        var session = new JgsDebugSession(dialect) { _store = store, Language = language, _started = 1 };
        session._storeLines = store.EnabledLines();
        store.Changed += session.OnStoreChanged;
        return session;
    }

    /// <summary>Whether this is a session's own debugger rather than one run's.</summary>
    public bool IsAttached => _store is not null;

    /// <summary>The language of the session an attached debugger belongs to; null for one run's.</summary>
    public string? Language { get; private init; }

    /// <summary>The breakpoints an attached debugger reads; null for one run's, which takes them through <see cref="SetBreakpoints"/>.</summary>
    public JgsBreakpointStore? Breakpoints => _store;

    /// <summary>Whether an attached debugger wants its hook in the interpreter: something is set, or a pause is wanted, or it is paused.</summary>
    public bool IsActive => _store is { IsEmpty: false } || _pauseWanted || _isPaused;

    /// <summary>Raised when <see cref="IsActive"/> may have changed, on the thread that changed it; the session re-installs or removes the hook.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>The interpreter-side hook, for the session to install.</summary>
    internal IJgsDebugHook InterpreterHook => _hook;

    /// <summary>
    /// Points an attached debugger at the interpreter a session built (again after <c>clear</c> rebuilds it) and
    /// listens for its warnings and output, for <c>dbstop if warning</c> and Pause on Output.
    /// </summary>
    internal void Bind(Interpreter interpreter, JgsEnvironment globals)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        _interpreter = interpreter;
        if (interpreter.Host is { } host)
        {
            host.Warnings.Raised = OnWarningRaised;
            host.OutputHook = OnOutputWritten;
        }

        _ = globals;
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        _storeLines = _store!.EnabledLines();
        lock (_maps)
        {
            _maps.Clear(); // the files' lines may have changed since they were read
        }

        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends the pause by unwinding the paused statement or callback (<c>dbquit</c>, Quit Debugging): a
    /// <see cref="JgsDebugQuitException"/> flies out of the interpreter, which the session swallows
    /// quietly. Nothing happens when not paused.
    /// </summary>
    public void Quit()
    {
        CancellationTokenSource? quit;
        lock (_stateLock)
        {
            if (!_isPaused)
            {
                return;
            }

            quit = _quit;
        }

        quit?.Cancel();
    }

    /// <summary>The source map of a file whose lines the debugger needs (a function's end line), read once and dropped when the store changes; null when the file does not parse.</summary>
    private JgsSourceMap? MapOf(string sourceId)
    {
        lock (_maps)
        {
            if (!_maps.TryGetValue(sourceId, out JgsSourceMap? map))
            {
                try
                {
                    map = sourceId.Length > 0 && File.Exists(sourceId) ? JgsSourceMap.Load(sourceId) : null;
                }
                catch (Exception ex) when (ex is JgsException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    map = null;
                }

                _maps[sourceId] = map;
            }

            return map;
        }
    }

    private static StringComparer SourceIdComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private enum DebugMode
    {
        Run,
        StepIn,
        StepOver,
        StepOut,
        PausePending,
    }

    /// <summary>How a block on the execution stack relates to its surroundings.</summary>
    private enum BlockKind
    {
        /// <summary>A program root: the main script or a <c>run()</c>-included file.</summary>
        Root,

        /// <summary>The body of a user function that was just entered.</summary>
        FunctionBody,

        /// <summary>A block nested in the statement its parent block is currently executing.</summary>
        Nested,
    }

    /// <summary>Raised (on the interpreter thread) when execution pauses. The UI marshals.</summary>
    public event EventHandler<JgsPausedEventArgs>? Paused;

    /// <summary>Raised (on the interpreter thread) when execution resumes or the paused run is cancelled.</summary>
    public event EventHandler? Resumed;

    /// <summary>Whether execution is currently paused at a statement.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Runs the script under the debugger. Same contract as <see cref="IScriptEngine.RunAsync"/> —
    /// script failures come back as a failed result, and cancelling <paramref name="cancellationToken"/>
    /// stops the run even while paused. A session can run only once.
    /// </summary>
    /// <param name="sourceId">The identity of <paramref name="code"/> (its file path, or "" when unsaved);
    /// breakpoints and pause locations are reported against it.</param>
    /// <param name="code">The JGS source.</param>
    /// <param name="context">The host services for the run.</param>
    /// <param name="cancellationToken">Stops the run; also aborts a pause.</param>
    public Task<ScriptRunResult> RunAsync(
        string sourceId, string code, ScriptContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A debug session runs one script once; create a new session.");
        }

        _runToken = cancellationToken;
        _output = context.Output;
        return ScriptThread.Run(
            () => JgsRunner.Run(code, context, cancellationToken, sourceId, _hook, _dialect),
            cancellationToken);
    }

    /// <summary>
    /// Runs the script under the debugger <em>inside</em> <paramref name="session"/>'s workspace: the
    /// script sees what the prompt defined, the prompt keeps what the script leaves, and a function
    /// defined at the prompt is stepped into like any other. The session must come from the same
    /// engine family (<see cref="JgsScriptEngine"/> or <see cref="MatlabScriptEngine"/>); the run
    /// completes as a statement of that session does — cancellation comes back as a failed result.
    /// </summary>
    /// <param name="session">The live console session whose workspace the run joins.</param>
    /// <param name="sourceId">The identity of <paramref name="code"/> (its file path, or "" when unsaved).</param>
    /// <param name="code">The JGS or MATLAB source.</param>
    /// <param name="cancellationToken">Stops the run; also aborts a pause.</param>
    /// <exception cref="ArgumentException"><paramref name="session"/> is not one of ours.</exception>
    public Task<ScriptRunResult> RunAsync(
        IScriptSession session, string sourceId, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(code);
        if (session is not JgsReplSession host)
        {
            throw new ArgumentException("Only a JGS or MATLAB console session can host a debug run.", nameof(session));
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A debug session runs one script once; create a new session.");
        }

        _runToken = cancellationToken;
        _output = host.Output;
        return host.ExecuteFileAsync(code, sourceId, _hook, cancellationToken);
    }

    /// <summary>Whether a typed statement is currently borrowing the paused interpreter.</summary>
    public bool IsEvaluating => _evaluating;

    /// <summary>
    /// The statement that writes <paramref name="text"/> into one Data Viewer cell of a paused
    /// frame's <paramref name="variable"/>, for <see cref="EvaluateAsync"/> to run in that frame —
    /// the debugger's twin of <see cref="IWorkspaceCellEditor.ComposeCellAssignment"/>. Null when
    /// that cell cannot be edited. Composed in the dialect of the selected frame's code (V11, ADR
    /// 0172): a cell of a <c>.m</c> function's variable is written 1-based whatever the run started as.
    /// </summary>
    /// <param name="variable">The variable as the Data Viewer shows it.</param>
    /// <param name="row">The edited cell's row in the viewer's grid.</param>
    /// <param name="column">The edited cell's column in the viewer's grid.</param>
    /// <param name="text">What was typed into the cell.</param>
    /// <param name="frameIndex">The frame the variable was read from (0 = innermost), as for <see cref="GetVariables"/>.</param>
    public string? ComposeCellAssignment(ScriptVariable variable, int row, int column, string text, int frameIndex = 0) =>
        JgsCellAssignment.Compose(variable, row, column, text, DialectOf(frameIndex));

    /// <summary>
    /// Runs <paramref name="code"/> in the given paused frame — the <c>K&gt;&gt;</c> prompt. The
    /// statement reads and writes the frame's variables, its output and echo go where the script's
    /// do, and an error is reported like a prompt error: printed, returned as a failed result, and
    /// the script stays paused where it was. Breakpoints inside anything the statement calls do not
    /// fire — the debugger stands aside while it runs. The result's variables are the frame's after
    /// the statement.
    /// </summary>
    /// <param name="code">One or more statements in the dialect of the selected frame's code (V11, ADR
    /// 0172): MATLAB inside a <c>.m</c> function reached from JGS, JGS in the outer frame that called it.</param>
    /// <param name="frameIndex">The frame to evaluate in (0 = innermost), as for <see cref="GetVariables"/>.</param>
    /// <param name="cancellationToken">Interrupts the statement without ending the run.</param>
    /// <exception cref="InvalidOperationException">The session is not paused, or a statement is already running.</exception>
    /// <exception cref="ArgumentOutOfRangeException">No such frame.</exception>
    public Task<ScriptRunResult> EvaluateAsync(string code, int frameIndex, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        JgsEnvironment environment;
        string file;
        JgsDialect dialect;
        lock (_stateLock)
        {
            EnsurePaused();
            if (_evaluating)
            {
                throw new InvalidOperationException("A statement is already being evaluated.");
            }

            environment = EnvironmentOf(frameIndex);
            file = FileOf(frameIndex);
            dialect = DialectOf(frameIndex);
            _evaluating = true;
            _evaluationDone.Reset();
        }

        // The run's own token ends the statement too: Stop while a K>> statement runs must not leave
        // it running in a workspace the run is unwinding out of.
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_runToken, cancellationToken);
        return ScriptThread.Run(() =>
        {
            try
            {
                return Evaluate(code, environment, file, dialect, linked.Token);
            }
            finally
            {
                linked.Dispose();
                _evaluating = false;
                _evaluationDone.Set();
            }
        });
    }

    private ScriptRunResult Evaluate(
        string code, JgsEnvironment environment, string file, JgsDialect dialect, CancellationToken cancellationToken)
    {
        Interpreter interpreter = _interpreter!;
        try
        {
            IReadOnlyList<Stmt> program = Parser.Parse(code, sourceId: "", dialect);
            interpreter.RunWhilePaused(program, environment, file, dialect, cancellationToken);
            return ScriptRunResult.Ok(0, Project(environment));
        }
        catch (JgsException ex)
        {
            ScriptDiagnostic diagnostic = ScriptDiagnostic.For(ex, runSourceId: "");
            _output?.WriteError(diagnostic.ToString());
            return ScriptRunResult.Failed(ex.Message, new[] { diagnostic });
        }
        catch (OperationCanceledException)
        {
            return ScriptRunResult.Failed("Statement was cancelled.");
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is null)
        {
            var diagnostic = new ScriptDiagnostic(0, 0,
                $"Internal error: {ex.GetType().Name}: {ex.Message}", IsError: true);
            _output?.WriteError(diagnostic.ToString());
            return ScriptRunResult.Failed(diagnostic.Message, new[] { diagnostic });
        }
    }

    /// <summary>
    /// Replaces the breakpoints for <paramref name="sourceId"/> with <paramref name="lines"/>
    /// (1-based). Safe to call at any time, including while running.
    /// </summary>
    public void SetBreakpoints(string sourceId, IReadOnlyCollection<int> lines)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(lines);
        lock (_stateLock)
        {
            // Copy-on-write so the interpreter thread reads a consistent snapshot without locking.
            var next = new Dictionary<string, HashSet<int>>(_breakpoints, SourceIdComparer);
            if (lines.Count == 0)
            {
                next.Remove(sourceId);
            }
            else
            {
                next[sourceId] = new HashSet<int>(lines);
            }

            _breakpoints = next;
        }
    }

    /// <summary>Requests a pause at the next statement of the running script - or, for a session's own debugger, of whatever runs next in the session.</summary>
    public void Pause()
    {
        bool activate = false;
        lock (_stateLock)
        {
            if (!_isPaused)
            {
                _mode = DebugMode.PausePending;
                activate = _store is not null && !_pauseWanted;
                _pauseWanted = _store is not null;
            }
        }

        if (activate)
        {
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Resumes a paused script, running until the next breakpoint (or the end).</summary>
    public void Continue() => Resume(DebugMode.Run);

    /// <summary>Executes the paused statement, stopping at the next one — entering functions.</summary>
    public void StepIn() => Resume(DebugMode.StepIn);

    /// <summary>Executes the paused statement, stopping at the next one at the same (or a shallower)
    /// call depth — running any function calls to completion.</summary>
    public void StepOver() => Resume(DebugMode.StepOver);

    /// <summary>Runs until the current function returns to its caller.</summary>
    public void StepOut() => Resume(DebugMode.StepOut);

    /// <summary>The paused call stack, innermost frame first.</summary>
    /// <exception cref="InvalidOperationException">The session is not paused.</exception>
    public IReadOnlyList<JgsStackFrame> GetCallStack()
    {
        EnsurePaused();
        return _pausedCallStack;
    }

    /// <summary>
    /// The variables visible in the given paused frame (0 = innermost), innermost scope winning on
    /// shadowing. Untouched builtins are hidden. Valid only while paused — the blocked interpreter
    /// thread is what makes this read race-free.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session is not paused.</exception>
    /// <exception cref="ArgumentOutOfRangeException">No such frame.</exception>
    public IReadOnlyList<ScriptVariable> GetVariables(int frameIndex = 0)
    {
        EnsurePaused();
        return Project(EnvironmentOf(frameIndex));
    }

    /// <summary>
    /// The environment a paused frame reads and writes. Frame 0 is the exact paused environment
    /// (innermost block scope included). Every other frame is the caller of the record above it,
    /// read from that record — the workspace the interpreter had when it made the call, handed to
    /// the hook at entry — and never reconstructed from depth: the frame below a function reached
    /// through an escaped anonymous handle is the handle's own workspace, not the invoker's, and
    /// the frame below one called from <c>evalin('caller', …)</c> is the named workspace. The
    /// script frame is the base workspace that way too, by identity and not by walking.
    /// </summary>
    private JgsEnvironment EnvironmentOf(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _pausedCallStack.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        return (frameIndex == 0 ? _pausedEnvironment : _frames[_pausedDepth - frameIndex].CallerFrame)!;
    }

    /// <summary>
    /// The file a paused frame's names come from: the interpreter's current file at the pause for
    /// frame 0, and for every other frame the file its record says the call was made from — the
    /// pair with <see cref="EnvironmentOf"/>, installed together for the prompt.
    /// </summary>
    private string FileOf(int frameIndex) =>
        frameIndex == 0 ? _pausedFile : _frames[_pausedDepth - frameIndex].CallerFile;

    /// <summary>
    /// The dialect a paused frame's code was written in (V11, ADR 0172): the running dialect at the
    /// pause for frame 0, and for every other frame the dialect its record says the call was made
    /// under - the third of the triple with <see cref="EnvironmentOf"/> and <see cref="FileOf"/>,
    /// installed together for the prompt, the cell edit and the paused execution. Before the first
    /// pause, and past the stack, the run's own.
    /// </summary>
    private JgsDialect DialectOf(int frameIndex)
    {
        if (frameIndex == 0 || !_isPaused)
        {
            return _pausedDialect;
        }

        int record = _pausedDepth - frameIndex;
        return record >= 0 && record < _frames.Count ? _frames[record].CallerDialect : _dialect;
    }

    /// <summary>
    /// Moves the execution point of the paused script to the statement on <paramref name="line"/> of
    /// the block it is paused in — skipped statements simply never run; re-targeted ones run (again).
    /// On success the session re-pauses at the target (reason <see cref="PauseReason.EntryJump"/>),
    /// raising <see cref="Resumed"/> then <see cref="Paused"/>. The target must be a statement of the
    /// current block: jumping into a nested block or another frame is unsound (its scope was never
    /// established) and is rejected.
    /// </summary>
    /// <param name="sourceId">The file the request came from — must be the file execution is paused in.</param>
    /// <param name="line">The 1-based target line.</param>
    /// <param name="error">Why the target was rejected (null on success).</param>
    public bool TrySetNextStatement(string sourceId, int line, out string? error)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        lock (_stateLock)
        {
            if (!_isPaused)
            {
                error = "The script is not paused.";
                return false;
            }

            if (_evaluating)
            {
                error = "A statement is being evaluated at the prompt; wait for it to finish.";
                return false;
            }

            if (!SourceIdComparer.Equals(sourceId, _pausedLocation!.SourceId))
            {
                error = "The next statement must be set in the file execution is paused in.";
                return false;
            }

            IReadOnlyList<Stmt> statements = _blocks[^1].List;
            int target = -1;
            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i].Line == line)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                error = "The target must be a statement in the block execution is paused in.";
                return false;
            }

            _pendingJump = target;
            _jumpArrival = true;
            error = null;
        }

        // Wake the interpreter: it returns the jump index and immediately re-pauses at the target.
        _gate.Set();
        return true;
    }

    /// <summary>
    /// Applies an edited version of <paramref name="sourceId"/> to the paused program. Compatible when
    /// the code that already ran (and the statements execution is currently inside) is unchanged: the
    /// tails of the active blocks are swapped in place, edited function bodies are refreshed (closures
    /// included), and new top-level functions are hoisted — so the edit takes effect on resume, on later
    /// loop iterations, and on future calls. An incompatible edit changes nothing and reports why; the
    /// host typically offers to restart the run with the new code instead.
    /// Call only while paused (the blocked interpreter thread is what makes the mutation safe).
    /// </summary>
    /// <param name="sourceId">The file the edited code belongs to.</param>
    /// <param name="newCode">The complete new source of that file.</param>
    /// <exception cref="InvalidOperationException">The session is not paused.</exception>
    public LiveEditResult TryApplyEdit(string sourceId, string newCode)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(newCode);
        EnsurePaused();
        EnsureNotEvaluating();

        // The edited file's own dialect (V11, ADR 0172): a .m file is MATLAB whatever the run
        // started as, and any other file speaks the run's dialect - the rule run() parses by.
        IReadOnlyList<Stmt> newProgram;
        try
        {
            newProgram = Parser.Parse(newCode, sourceId, JgsRunner.DialectOfFile(sourceId, _dialect));
        }
        catch (JgsException ex)
        {
            return LiveEditResult.Incompatible($"the new code does not parse — {ex.Message}");
        }

        // --- Validate: resolve every active block of the edited file to its counterpart in the new
        // program, walking the stack outward-in. Nothing is mutated until every check passes. --------
        var resolved = new IReadOnlyList<Stmt>?[_blocks.Count];
        for (int i = 0; i < _blocks.Count; i++)
        {
            BlockEntry entry = _blocks[i];
            Stmt current = entry.List[entry.Index];
            bool inFile = SourceIdComparer.Equals(current.SourceId, sourceId);
            switch (entry.Kind)
            {
                case BlockKind.Root when inFile:
                    resolved[i] = newProgram;
                    break;

                case BlockKind.FunctionBody when inFile:
                    FnStmt declaration = entry.Function!;
                    FnStmt? replacement = TopLevelFunction(newProgram, declaration.Name);
                    if (replacement is null)
                    {
                        return LiveEditResult.Incompatible(
                            $"function '{declaration.Name}' is on the call stack but is no longer declared at the top level");
                    }

                    if (!replacement.Parameters.SequenceEqual(declaration.Parameters, StringComparer.Ordinal))
                    {
                        return LiveEditResult.Incompatible(
                            $"function '{declaration.Name}' is on the call stack; its parameters cannot change");
                    }

                    resolved[i] = replacement.Body;
                    break;

                case BlockKind.Nested when resolved[i - 1] is IReadOnlyList<Stmt> newParentBlock:
                    // The parent block is executing the statement this block belongs to. That statement
                    // is pinned: its header and other branches may not change — only the branch being
                    // executed (checked level-by-level as we descend) may.
                    BlockEntry parentEntry = _blocks[i - 1];
                    Stmt oldParent = parentEntry.List[parentEntry.Index];
                    if (parentEntry.Index >= newParentBlock.Count
                        || !AstEquals.EqualExceptSlot(oldParent, newParentBlock[parentEntry.Index], entry.Slot))
                    {
                        return LiveEditResult.Incompatible(
                            $"the statement at line {oldParent.Line}, which execution is inside, changed" +
                            " (only the block being executed may be edited)");
                    }

                    resolved[i] = AstChildren.Slot(newParentBlock[parentEntry.Index], entry.Slot)!;
                    break;
            }
        }

        for (int i = 0; i < _blocks.Count; i++)
        {
            if (resolved[i] is not IReadOnlyList<Stmt> newBlock)
            {
                continue;
            }

            BlockEntry entry = _blocks[i];
            if (entry.List is not List<Stmt>)
            {
                return LiveEditResult.Incompatible("the active block cannot be edited in place");
            }

            for (int j = i + 1; j < _blocks.Count; j++)
            {
                if (resolved[j] is not null && ReferenceEquals(_blocks[j].List, entry.List))
                {
                    return LiveEditResult.Incompatible(
                        "a function that appears more than once on the call stack (recursion) cannot be live-edited");
                }
            }

            if (newBlock.Count <= entry.Index)
            {
                return LiveEditResult.Incompatible(
                    $"the statement at line {entry.List[entry.Index].Line} that execution is at was removed");
            }

            for (int k = 0; k < entry.Index; k++)
            {
                // A top-level `fn` above the execution point may change freely: its code does not run
                // by re-executing the declaration — hoisting bound it in the globals, and the
                // function-refresh pass below swaps its body (or re-hoists it). Everything else that
                // already ran must be untouched.
                if (entry.Kind == BlockKind.Root && entry.List[k] is FnStmt && newBlock[k] is FnStmt)
                {
                    continue;
                }

                if (!AstEquals.StatementsEqual(entry.List[k], newBlock[k]))
                {
                    return LiveEditResult.Incompatible(
                        $"code before the execution point changed (line {entry.List[k].Line} already ran)");
                }
            }

            // A non-innermost level whose child is a call boundary (a function call or run() include):
            // the calling statement is mid-execution on the interpreter's own stack and cannot change.
            // (When the child is a nested block, the resolution pass pinned the statement already.)
            if (i < _blocks.Count - 1 && _blocks[i + 1].Kind != BlockKind.Nested
                && !AstEquals.StatementsEqual(entry.List[entry.Index], newBlock[entry.Index]))
            {
                return LiveEditResult.Incompatible(
                    $"the statement at line {entry.List[entry.Index].Line} is executing a call and cannot change");
            }
        }

        // --- Plan the whole-file function refresh: top-level fns of the edited file that are not on
        // the call stack get their bodies swapped in place (so closures and aliases see the new code);
        // signature changes and brand-new functions are re-hoisted like a fresh run would. -----------
        var stackedLists = new HashSet<IReadOnlyList<Stmt>>(ReferenceEqualityComparer.Instance);
        foreach (BlockEntry entry in _blocks)
        {
            stackedLists.Add(entry.List);
        }

        var refreshes = new List<(List<Stmt> Target, IReadOnlyList<Stmt> Content)>();
        var hoists = new List<FnStmt>();
        foreach (FnStmt fn in newProgram.OfType<FnStmt>())
        {
            // The file's storage is asked by source id: a function lives with its file (M145).
            UserFunction? existing =
                _interpreter is Interpreter live
                && live.TryGetHoisted(sourceId, fn.Name, fn.Dialect, out JgsValue bound)
                && bound.Type == JgsType.Function
                && bound.AsCallable is UserFunction user
                && SourceIdComparer.Equals(user.Declaration.SourceId, sourceId)
                    ? user
                    : null;
            if (existing is null)
            {
                hoists.Add(fn);
            }
            else if (stackedLists.Contains(existing.Declaration.Body))
            {
                // On the call stack: the level machinery above already handled its body precisely.
            }
            else if (existing.Declaration.Parameters.SequenceEqual(fn.Parameters, StringComparer.Ordinal)
                && existing.Declaration.Body is List<Stmt> body)
            {
                refreshes.Add((body, fn.Body));
            }
            else
            {
                hoists.Add(fn);
            }
        }

        // --- Apply. In-place list mutation is the whole trick: every holder of these lists — the AST
        // nodes, the executing BlockExecution cursors, closures — reads the new statements from now on.
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (resolved[i] is not IReadOnlyList<Stmt> newBlock)
            {
                continue;
            }

            BlockEntry entry = _blocks[i];
            var target = (List<Stmt>)entry.List;

            // The innermost statement (the paused one) has not run and is replaced like the tail; at
            // outer levels the statement at the index is mid-execution and must keep its identity —
            // its nested lists are the ones being mutated at the deeper levels.
            Stmt? executing = i == _blocks.Count - 1 ? null : target[entry.Index];
            target.Clear();
            target.AddRange(newBlock);
            if (executing is not null)
            {
                target[entry.Index] = executing;
            }
        }

        foreach ((List<Stmt> target, IReadOnlyList<Stmt> content) in refreshes)
        {
            target.Clear();
            target.AddRange(content);
        }

        if (_interpreter is Interpreter interpreter)
        {
            foreach (FnStmt fn in hoists)
            {
                interpreter.Hoist(fn, sourceId);
            }
        }

        // Where the paused statement now sits (its line may have shifted with the edit).
        JgsDebugLocation? newLocation = null;
        BlockEntry top = _blocks[^1];
        if (resolved[^1] is not null)
        {
            Stmt paused = top.List[top.Index];
            newLocation = new JgsDebugLocation(paused.SourceId, paused.Line, paused.Column);
            lock (_stateLock)
            {
                _pausedLocation = newLocation;
            }
        }

        return LiveEditResult.Ok(newLocation);
    }

    private static FnStmt? TopLevelFunction(IReadOnlyList<Stmt> program, string name) =>
        program.OfType<FnStmt>().FirstOrDefault(fn => string.Equals(fn.Name, name, StringComparison.Ordinal));

    private void Resume(DebugMode mode)
    {
        lock (_stateLock)
        {
            if (!_isPaused)
            {
                return; // benign: the run may have just completed
            }

            EnsureNotEvaluating();

            _mode = mode;
            _stepDepth = _pausedDepth;

            // Running from here on, as far as any caller can see. The interpreter thread clears
            // this flag too, once it is through the gate — but that is later, and a Pause() that
            // arrives in between used to read the stale value and drop the request, leaving the
            // script running with nobody waiting for it.
            _isPaused = false;
        }

        _gate.Set();
    }

    private void EnsurePaused()
    {
        if (!_isPaused)
        {
            throw new InvalidOperationException("The session is not paused.");
        }
    }

    /// <summary>
    /// A typed statement has borrowed the interpreter; waking the interpreter thread underneath it
    /// would put two threads in one interpreter. The host disables stepping while a statement runs,
    /// so reaching this is a host bug worth hearing about rather than a race to paper over.
    /// </summary>
    private void EnsureNotEvaluating()
    {
        if (_evaluating)
        {
            throw new InvalidOperationException("A statement is being evaluated at the prompt; wait for it to finish.");
        }
    }

    private static IReadOnlyList<ScriptVariable> Project(JgsEnvironment? environment)
    {
        var variables = new List<ScriptVariable>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // The built-in layer is not a workspace: its constants (pi, i, newline) are not variables.
        for (JgsEnvironment? scope = environment; scope is not null && !scope.IsBuiltinLayer; scope = scope.Parent)
        {
            foreach ((string name, JgsValue value) in scope.Variables)
            {
                // Innermost scope wins on shadowing; builtins the script never rebound stay hidden.
                if (!seen.Add(name) || (value.Type == JgsType.Function && value.AsCallable is BuiltinFunction))
                {
                    continue;
                }

                variables.Add(JgsRunner.ToScriptVariable(name, value));
            }
        }

        variables.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return variables;
    }

    // --- Interpreter-thread side (the hook) ------------------------------------------------------

    private void OnRunStarting(Interpreter interpreter, JgsEnvironment globals) => _interpreter = interpreter;

    private void OnEnterBlock(BlockExecution block)
    {
        if (_evaluating)
        {
            return; // a K>> statement's blocks are not part of the paused program
        }

        var entry = new BlockEntry(block.Statements);
        if (_pendingFunction is FnStmt function)
        {
            entry.Kind = BlockKind.FunctionBody;
            entry.Function = function;
            _pendingFunction = null;
        }
        else if (_blocks.Count > 0 && SlotInParent(block.Statements) is int slot)
        {
            entry.Kind = BlockKind.Nested;
            entry.Slot = slot;
        }
        else
        {
            entry.Kind = BlockKind.Root; // the main program, or a run() include
        }

        _blocks.Add(entry);
    }

    private int? SlotInParent(IReadOnlyList<Stmt> statements)
    {
        BlockEntry parent = _blocks[^1];
        return parent.Index < parent.List.Count
            ? AstChildren.SlotOf(parent.List[parent.Index], statements)
            : null;
    }

    private void OnExitBlock()
    {
        if (!_evaluating && _blocks.Count > 0)
        {
            _blocks.RemoveAt(_blocks.Count - 1);
        }
    }

    private int? OnBeforeStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth)
    {
        if (_evaluating)
        {
            return null; // the debugger stands aside for a typed statement: no breakpoints, no steps
        }

        _blocks[^1].Index = index;
        Stmt statement = block.Statements[index];
        _currentSourceId = statement.SourceId;
        _currentBlock = block;
        _currentIndex = index;
        _currentEnv = env;
        _currentDepth = callDepth;
        _statementThread = Environment.CurrentManagedThreadId;

        PauseReason? reason = ShouldPause(statement, env, callDepth);
        if (reason is null)
        {
            return null;
        }

        PauseAt(new JgsDebugLocation(statement.SourceId, statement.Line, statement.Column), env, callDepth, reason.Value, detail: null);

        lock (_stateLock)
        {
            if (_pendingJump is int jump)
            {
                // Set-next-statement: redirect the block cursor; the next BeforeStatement fires at the
                // target and pauses there (the _jumpArrival flag), so the user stays in control.
                _pendingJump = null;
                return jump;
            }
        }

        return null;
    }

    /// <summary>
    /// Blocks the interpreter thread at <paramref name="location"/> until a resume, a quit or the run's cancellation:
    /// every pause - a breakpoint, a step, an error, a warning, a function's end - comes through here.
    /// </summary>
    private void PauseAt(JgsDebugLocation location, JgsEnvironment env, int callDepth, PauseReason reason, string? detail)
    {
        JgsPausedEventArgs args;
        var quit = new CancellationTokenSource();
        lock (_stateLock)
        {
            _mode = DebugMode.Run; // each step command re-arms; a pause request is now satisfied
            _pauseWanted = false;
            _pausedLocation = location;
            _pausedEnvironment = env;
            _pausedFile = _interpreter!.CurrentFile;
            _pausedDialect = _interpreter.Dialect;
            _pausedDepth = callDepth;
            _pausedCallStack = BuildCallStack(location, callDepth);
            args = new JgsPausedEventArgs(location, _pausedCallStack, reason, detail);
            _quit = quit;
            _gate.Reset();
            _isPaused = true;
        }

        // Stop-while-paused cancels this wait and unwinds the run: the run's own token for one run's
        // debugger, the statement's for a session's; Quit Debugging does the same through its own.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_runToken, _interpreter.Cancellation, quit.Token);
        try
        {
            Paused?.Invoke(this, args);
            _gate.Wait(linked.Token);
        }
        catch (OperationCanceledException) when (quit.IsCancellationRequested && !_runToken.IsCancellationRequested && !_interpreter.Cancellation.IsCancellationRequested)
        {
            throw new JgsDebugQuitException();
        }
        finally
        {
            // Stop can arrive while a typed statement holds the interpreter. Its linked token has
            // already told it to finish; the unwinding must not start until it has, or two threads
            // are tearing down one interpreter.
            _evaluationDone.Wait();
            lock (_stateLock)
            {
                _isPaused = false;
                _pausedEnvironment = null;
                _quit = null;
            }

            quit.Dispose();
            Resumed?.Invoke(this, EventArgs.Empty);
            if (_store is not null)
            {
                ActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private PauseReason? ShouldPause(Stmt statement, JgsEnvironment env, int callDepth)
    {
        DebugMode mode;
        int stepDepth;
        lock (_stateLock)
        {
            if (_jumpArrival)
            {
                _jumpArrival = false;
                return PauseReason.EntryJump;
            }

            mode = _mode;
            stepDepth = _stepDepth;
        }

        // A function's declaration is hoisted, not run: no pause on its line (R2025b moves such a
        // breakpoint to the first statement of the body).
        if (statement is FnStmt or ClassdefStmt)
        {
            return null;
        }

        if (mode == DebugMode.PausePending)
        {
            return PauseReason.PauseRequest;
        }

        if (_store is not null)
        {
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> lines = _storeLines;
            if (lines.TryGetValue(statement.SourceId, out IReadOnlyDictionary<int, string>? set) && set.TryGetValue(statement.Line, out string? condition)
                && ConditionHolds(condition, env))
            {
                return PauseReason.Breakpoint;
            }
        }
        else
        {
            Dictionary<string, HashSet<int>> breakpoints = _breakpoints;
            if (breakpoints.TryGetValue(statement.SourceId, out HashSet<int>? set) && set.Contains(statement.Line))
            {
                return PauseReason.Breakpoint;
            }
        }

        // The hook fires exactly once per statement execution, so stepping is pure depth comparison:
        // the very next statement (StepIn), the next at or above the starting depth (StepOver — calls
        // run to completion), or the next above it (StepOut — the current function returns).
        return mode switch
        {
            DebugMode.StepIn => PauseReason.Step,
            DebugMode.StepOver when callDepth <= stepDepth => PauseReason.Step,
            DebugMode.StepOut when callDepth < stepDepth => PauseReason.Step,
            _ => null,
        };
    }

    private IReadOnlyList<JgsStackFrame> BuildCallStack(JgsDebugLocation paused, int callDepth)
    {
        // _frames is mutated only on the interpreter thread (which is executing this very method),
        // so reading it here needs no synchronization.
        var stack = new List<JgsStackFrame>(callDepth + 1)
        {
            new(callDepth > 0 ? _frames[callDepth - 1].Name : "(script)", paused.SourceId, paused.Line, paused.Column),
        };

        // Each caller frame sits at the call site of the function above it.
        for (int depth = callDepth; depth >= 1; depth--)
        {
            FrameEntry callee = _frames[depth - 1];
            string callerName = depth > 1 ? _frames[depth - 2].Name : "(script)";
            stack.Add(new JgsStackFrame(callerName, callee.CallSiteSourceId, callee.CallLine, 0));
        }

        return stack;
    }

    private void OnEnterFunction(
        FnStmt declaration, int callLine, JgsEnvironment local, JgsEnvironment callerFrame, string callerFile)
    {
        if (_evaluating)
        {
            return;
        }

        // The call site lives in the statement the interpreter last announced; the caller's
        // workspace and file are what the interpreter handed over, not a reconstruction, and the
        // caller's dialect is the one still running - the callee enters its own after this (V11).
        // A method's frame is named Class.method, as the error stack names it (W11).
        string name = local.ClassContext is { } running && running.DeclaresMethod(declaration)
            ? $"{running.Name}.{declaration.Name}"
            : declaration.Name;
        _frames.Add(new FrameEntry(
            name, _currentSourceId, callLine, local, callerFrame, callerFile, _interpreter!.Dialect));
        _pendingFunction = declaration; // the next EnterBlock is this function's body
    }

    // --- The conditions (W11): dbstop if error, caught error, warning, naninf; Pause on Output; a function's end ---

    /// <summary>Whether a breakpoint's condition holds in <paramref name="env"/>: "" always; an expression by its truth, an error in it pausing with the error reported, as R2025b's does.</summary>
    private bool ConditionHolds(string condition, JgsEnvironment env)
    {
        if (condition.Length == 0)
        {
            return true;
        }

        _evaluating = true; // the debugger stands aside for its own expression
        try
        {
            return _interpreter!.EvaluateSourceIn(condition, env, 0, 0).IsTruthy;
        }
        catch (JgsException ex)
        {
            _output?.WriteError($"Error evaluating breakpoint condition \"{condition}\": {ex.Message}");
            return true;
        }
        finally
        {
            _evaluating = false;
        }
    }

    private void OnAfterStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth)
    {
        if (_evaluating || _store is null || !_store.IsOn(StopCondition.NanInf))
        {
            return;
        }

        // The conditions pause code in files, as R2025b's do; a statement typed at the prompt has no
        // line to show and is reported as it is.
        Stmt statement = block.Statements[index];
        if (statement.SourceId.Length == 0)
        {
            return;
        }

        foreach (string name in AssignedNames(statement))
        {
            if (env.TryGet(name, out JgsValue value) && HasNanOrInf(value))
            {
                PauseAt(new JgsDebugLocation(statement.SourceId, statement.Line, statement.Column), env, callDepth, PauseReason.NanInf, $"{name} holds NaN or Inf");
                return;
            }
        }
    }

    private void OnStatementFailed(BlockExecution block, int index, JgsEnvironment env, int callDepth, JgsRuntimeException error, bool caught)
    {
        if (_evaluating || _store is null)
        {
            return;
        }

        StopCondition condition = caught ? StopCondition.CaughtError : StopCondition.Error;
        Stmt statement = block.Statements[index];
        if (statement.SourceId.Length == 0 || !_store.Stops(condition, error.Identifier))
        {
            return; // an error at the prompt is reported, not paused at (R2025b stops in files)
        }

        _output?.WriteError(error.Message);
        PauseAt(new JgsDebugLocation(statement.SourceId, statement.Line, statement.Column), env, callDepth, PauseReason.Error, error.Message);
    }

    private void OnFunctionReturning(FnStmt declaration, JgsEnvironment local, int callDepth)
    {
        if (_evaluating || _store is null)
        {
            return;
        }

        IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> lines = _storeLines;
        if (!lines.TryGetValue(declaration.SourceId, out IReadOnlyDictionary<int, string>? set) || MapOf(declaration.SourceId) is not { } map)
        {
            return;
        }

        foreach (JgsFunctionSpan function in map.Functions)
        {
            if (function.HasEnd && function.Signature == declaration.Line && set.TryGetValue(function.End, out string? condition) && ConditionHolds(condition, local))
            {
                PauseAt(new JgsDebugLocation(declaration.SourceId, function.End, 0), local, callDepth, PauseReason.FunctionEnd, detail: null);
                return;
            }
        }
    }

    /// <summary>A warning raised by the running statement: <c>dbstop if warning</c> pauses it inside the call, where R2025b does.</summary>
    private void OnWarningRaised(string identifier, string message)
    {
        if (_evaluating || _isPaused || _store is null || _currentBlock is null || !_store.Stops(StopCondition.Warning, identifier)
            || _interpreter?.Host is not { } host || !host.Warnings.IsOn(identifier) || !OnInterpreterThread())
        {
            return;
        }

        Stmt statement = _currentBlock.Statements[_currentIndex];
        if (statement.SourceId.Length == 0)
        {
            return;
        }

        PauseAt(new JgsDebugLocation(statement.SourceId, statement.Line, statement.Column), _currentEnv!, _currentDepth, PauseReason.Warning, message);
    }

    /// <summary>Something reached the output console from the running statement: Pause on Output pauses it there.</summary>
    private void OnOutputWritten()
    {
        if (_evaluating || _isPaused || _store is not { PauseOnOutput: true } || _currentBlock is null || !OnInterpreterThread())
        {
            return;
        }

        Stmt statement = _currentBlock.Statements[_currentIndex];
        if (statement.SourceId.Length == 0)
        {
            return;
        }

        PauseAt(new JgsDebugLocation(statement.SourceId, statement.Line, statement.Column), _currentEnv!, _currentDepth, PauseReason.Output, detail: null);
    }

    /// <summary>Whether this is the thread the hook last saw a statement begin on: a pause from any other thread (a window's) would block the wrong one.</summary>
    private bool OnInterpreterThread() => _interpreter is { DebugHook: not null } && Environment.CurrentManagedThreadId == _statementThread;

    private int _statementThread = -1;

    /// <summary>The variables a statement writes, by their root names.</summary>
    private static IEnumerable<string> AssignedNames(Stmt statement)
    {
        switch (statement)
        {
            case LetStmt let:
                yield return let.Name;
                break;
            case DestructuringLetStmt destructuring:
                foreach (string name in destructuring.Names)
                {
                    yield return name;
                }

                break;
            case ExprStmt { Expression: AssignExpr assign } when RootOf(assign.Target) is { } root:
                yield return root;
                break;
            case ExprStmt { Expression: AssignExpr }:
                break;
            case ExprStmt expression when expression.Expression is not AssignExpr:
                yield return "ans";
                break;
            case MultiAssignStmt multi:
                foreach (Expr? target in multi.Targets)
                {
                    if (target is not null && RootOf(target) is { } root)
                    {
                        yield return root;
                    }
                }

                break;
        }
    }

    private static string? RootOf(Expr target) => target switch
    {
        VariableExpr variable => variable.Name,
        IndexExpr index => RootOf(index.Target),
        BraceIndexExpr brace => RootOf(brace.Target),
        MemberExpr member => RootOf(member.Target),
        _ => null,
    };

    /// <summary>Whether a real or complex number, or an array of them, holds NaN or Inf anywhere.</summary>
    private static bool HasNanOrInf(JgsValue value)
    {
        switch (value.Type)
        {
            case JgsType.Number:
                return !double.IsFinite(value.AsNumber);
            case JgsType.Complex:
                return !double.IsFinite(value.AsComplex.Real) || !double.IsFinite(value.AsComplex.Imaginary);
            case JgsType.Array when value.IsPacked:
                foreach (double element in value.AsBuffer.AsSpan())
                {
                    if (!double.IsFinite(element))
                    {
                        return true;
                    }
                }

                return false;
            case JgsType.Array when !value.IsStringArray && !value.IsCharMatrix:
                foreach (JgsValue element in value.AsArray)
                {
                    if (element.Type is JgsType.Number or JgsType.Complex && HasNanOrInf(element))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private void OnExitFunction()
    {
        if (!_evaluating && _frames.Count > 0)
        {
            // Back in the statement that made the call: in `x = a() + b()` the record for b must
            // name the caller's file, not the last line of a.m that was announced.
            _currentSourceId = _frames[^1].CallSiteSourceId;
            _frames.RemoveAt(_frames.Count - 1);
        }
    }

    private void OnEnterContext(
        string name, string file, int callLine, JgsEnvironment local, JgsEnvironment callerFrame, string callerFile)
    {
        if (_evaluating)
        {
            return;
        }

        // A context is a frame like a call's, entered from the statement last announced. Its own
        // calls are made from its file: a function it calls records that as its call site, the way
        // one called from a function's statement records the function's file.
        _frames.Add(new FrameEntry(
            name, _currentSourceId, callLine, local, callerFrame, callerFile, _interpreter!.Dialect));
        _currentSourceId = file;
    }

    private void OnExitContext() => OnExitFunction();

    private string _currentSourceId = "";
    private string _pausedFile = "";

    /// <summary>One user-function call on the stack: the frame it runs in, and the (workspace, file, dialect) triple it was called from.</summary>
    private sealed record FrameEntry(
        string Name, string CallSiteSourceId, int CallLine, JgsEnvironment Local,
        JgsEnvironment CallerFrame, string CallerFile, JgsDialect CallerDialect);

    /// <summary>One block on the execution stack: which statement list, where in it execution is, and
    /// how it hangs off its surroundings (the live-edit path back to a program root).</summary>
    private sealed class BlockEntry
    {
        public BlockEntry(IReadOnlyList<Stmt> list) => List = list;

        public IReadOnlyList<Stmt> List { get; }

        public int Index { get; set; }

        public BlockKind Kind { get; set; }

        /// <summary>For <see cref="BlockKind.Nested"/>: which slot of the parent's current statement.</summary>
        public int Slot { get; set; }

        /// <summary>For <see cref="BlockKind.FunctionBody"/>: the declaration being executed.</summary>
        public FnStmt? Function { get; set; }
    }

    /// <summary>The internal hook adapter, so the session's public surface stays clean.</summary>
    private sealed class Hook : IJgsDebugHook
    {
        private readonly JgsDebugSession _session;

        public Hook(JgsDebugSession session) => _session = session;

        public void RunStarting(Interpreter interpreter, JgsEnvironment globals) =>
            _session.OnRunStarting(interpreter, globals);

        public int? BeforeStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth) =>
            _session.OnBeforeStatement(block, index, env, callDepth);

        public void EnterBlock(BlockExecution block) => _session.OnEnterBlock(block);

        public void ExitBlock() => _session.OnExitBlock();

        public void EnterFunction(
            FnStmt declaration, int callLine, JgsEnvironment local, JgsEnvironment callerFrame, string callerFile) =>
            _session.OnEnterFunction(declaration, callLine, local, callerFrame, callerFile);

        public void ExitFunction() => _session.OnExitFunction();

        public void EnterContext(
            string name, string file, int callLine, JgsEnvironment local, JgsEnvironment callerFrame, string callerFile) =>
            _session.OnEnterContext(name, file, callLine, local, callerFrame, callerFile);

        public void ExitContext() => _session.OnExitContext();

        public void AfterStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth) =>
            _session.OnAfterStatement(block, index, env, callDepth);

        public void StatementFailed(BlockExecution block, int index, JgsEnvironment env, int callDepth, JgsRuntimeException error, bool caught) =>
            _session.OnStatementFailed(block, index, env, callDepth, error, caught);

        public void FunctionReturning(FnStmt declaration, JgsEnvironment local, int callDepth) =>
            _session.OnFunctionReturning(declaration, local, callDepth);
    }
}
