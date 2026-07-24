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
/// program. Cancelling the run token aborts the run even while paused — the gate wait observes it and
/// unwinds exactly like the interpreter's cooperative cancellation.
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
    private JgsEnvironment? _globals;
    private FnStmt? _pendingFunction;

    private volatile bool _isPaused;
    private JgsDebugLocation? _pausedLocation;
    private IReadOnlyList<JgsStackFrame> _pausedCallStack = Array.Empty<JgsStackFrame>();
    private JgsEnvironment? _pausedEnvironment;
    private int _pausedDepth;
    private int? _pendingJump;
    private bool _jumpArrival;

    private readonly JgsDialect _dialect;

    /// <summary>Creates a session. Obtain one from an engine's <c>CreateDebugSession</c>.</summary>
    /// <param name="dialect">The language variant to debug, or null for <see cref="JgsDialect.Jgs"/>.</param>
    internal JgsDebugSession(JgsDialect? dialect = null)
    {
        _dialect = dialect ?? JgsDialect.Jgs;
        _hook = new Hook(this);
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
        return Task.Run(
            () => JgsRunner.Run(code, context, cancellationToken, sourceId, _hook, _dialect),
            cancellationToken);
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

    /// <summary>Requests a pause at the next statement of the running script.</summary>
    public void Pause()
    {
        lock (_stateLock)
        {
            if (!_isPaused)
            {
                _mode = DebugMode.PausePending;
            }
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
        if (frameIndex < 0 || frameIndex >= _pausedCallStack.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        // Frame 0 sees the exact paused environment (innermost block scope included); caller frames
        // see their function-local scope chain; the script frame sees the globals.
        int depth = _pausedDepth - frameIndex;
        JgsEnvironment? environment = frameIndex == 0
            ? _pausedEnvironment
            : depth > 0 ? _frames[depth - 1].Local : GlobalsOf(_pausedEnvironment);
        return Project(environment);
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

        IReadOnlyList<Stmt> newProgram;
        try
        {
            newProgram = Parser.Parse(newCode, sourceId, _dialect);
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
            UserFunction? existing =
                _globals is not null
                && _globals.Locals.TryGetValue(fn.Name, out JgsValue? bound)
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

        if (_interpreter is Interpreter interpreter && _globals is JgsEnvironment globals)
        {
            foreach (FnStmt fn in hoists)
            {
                globals.Declare(fn.Name, JgsValue.Function(new UserFunction(fn, globals, interpreter)));
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

            _mode = mode;
            _stepDepth = _pausedDepth;
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

    private static JgsEnvironment? GlobalsOf(JgsEnvironment? environment)
    {
        JgsEnvironment? scope = environment;
        while (scope?.Parent is not null)
        {
            scope = scope.Parent;
        }

        return scope;
    }

    private static IReadOnlyList<ScriptVariable> Project(JgsEnvironment? environment)
    {
        var variables = new List<ScriptVariable>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (JgsEnvironment? scope = environment; scope is not null; scope = scope.Parent)
        {
            foreach ((string name, JgsValue value) in scope.Locals)
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

    private void OnRunStarting(Interpreter interpreter, JgsEnvironment globals)
    {
        _interpreter = interpreter;
        _globals = globals;
    }

    private void OnEnterBlock(BlockExecution block)
    {
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
        if (_blocks.Count > 0)
        {
            _blocks.RemoveAt(_blocks.Count - 1);
        }
    }

    private int? OnBeforeStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth)
    {
        _blocks[^1].Index = index;
        Stmt statement = block.Statements[index];
        _currentSourceId = statement.SourceId;

        PauseReason? reason = ShouldPause(statement, callDepth);
        if (reason is null)
        {
            return null;
        }

        var location = new JgsDebugLocation(statement.SourceId, statement.Line, statement.Column);
        JgsPausedEventArgs args;
        lock (_stateLock)
        {
            _mode = DebugMode.Run; // each step command re-arms; a pause request is now satisfied
            _pausedLocation = location;
            _pausedEnvironment = env;
            _pausedDepth = callDepth;
            _pausedCallStack = BuildCallStack(location, callDepth);
            args = new JgsPausedEventArgs(location, _pausedCallStack, reason.Value);
            _gate.Reset();
            _isPaused = true;
        }

        try
        {
            Paused?.Invoke(this, args);
            _gate.Wait(_runToken); // Stop-while-paused cancels this wait and unwinds the run
        }
        finally
        {
            _isPaused = false;
            _pausedEnvironment = null;
            Resumed?.Invoke(this, EventArgs.Empty);
        }

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

    private PauseReason? ShouldPause(Stmt statement, int callDepth)
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

        if (mode == DebugMode.PausePending)
        {
            return PauseReason.PauseRequest;
        }

        Dictionary<string, HashSet<int>> breakpoints = _breakpoints;
        if (breakpoints.TryGetValue(statement.SourceId, out HashSet<int>? lines) && lines.Contains(statement.Line))
        {
            return PauseReason.Breakpoint;
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

    private void OnEnterFunction(FnStmt declaration, int callLine, JgsEnvironment local)
    {
        // The call site lives in the statement the interpreter last announced.
        _frames.Add(new FrameEntry(declaration.Name, _currentSourceId, callLine, local));
        _pendingFunction = declaration; // the next EnterBlock is this function's body
    }

    private void OnExitFunction()
    {
        if (_frames.Count > 0)
        {
            _frames.RemoveAt(_frames.Count - 1);
        }
    }

    private string _currentSourceId = "";

    private sealed record FrameEntry(string Name, string CallSiteSourceId, int CallLine, JgsEnvironment Local);

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

        public void EnterFunction(FnStmt declaration, int callLine, JgsEnvironment local) =>
            _session.OnEnterFunction(declaration, callLine, local);

        public void ExitFunction() => _session.OnExitFunction();
    }
}
