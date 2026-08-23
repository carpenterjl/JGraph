using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Numerics;

using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A tree-walking evaluator for the JGS language. It runs a parsed program against a global environment
/// (seeded with the built-ins), supporting variables, arrays, closures, control flow, and element-wise
/// numeric operators. Runaway scripts are bounded three ways: a per-statement step budget, a call-depth
/// limit, and a cooperative cancellation check — so even a tight <c>while true {}</c> loop is interruptible.
/// </summary>
internal sealed partial class Interpreter
{
    private const long MaxSteps = 50_000_000;

    // MATLAB's default RecursionLimit is 500; 512 gives ported code that depth with a little
    // headroom. Every script entry point runs on a ScriptThread 16 MB stack, so this limit trips
    // as a catchable script error long before the native stack is in danger.
    private const int MaxCallDepth = 512;

    /// <summary>The declaration of the function body currently executing (null at script level) —
    /// what <c>persistent</c> keys its storage by.</summary>
    private FnStmt? _currentFunction;

    /// <summary>Each function's persistent variables, surviving across calls for the session's life.</summary>
    private readonly Dictionary<FnStmt, Dictionary<string, JgsValue>> _persistents = [];

    /// <summary>
    /// Names of functions user code has defined at the global scope. MATLAB's plain <c>clear</c>
    /// drops variables but not functions, and this set is how <c>clear</c> tells them apart from a
    /// variable that merely holds a handle.
    /// </summary>
    internal HashSet<string> ScriptFunctionNames { get; } = new(StringComparer.Ordinal);

    private readonly JgsEnvironment _globals;

    /// <summary>
    /// Where variables a <c>global</c> statement declared actually live.
    /// </summary>
    /// <remarks>
    /// MATLAB's global workspace is not its base workspace. A script's own <c>counter</c> and a
    /// <c>global counter</c> are two different variables, and storing globals in the base environment
    /// made them one — so a helper's <c>global counter</c> overwrote the script's local of that name.
    /// This scope has no parent, which is what keeps it unreachable except through a declaration.
    /// </remarks>
    private readonly JgsEnvironment _globalWorkspace = new();
    private CancellationToken _cancellationToken;
    private readonly IJgsDebugHook? _hook;
    private readonly Action<string>? _echo;
    // 'end' resolves against the top entry: the extents of the value being subscripted, and which
    // subscript slot is being evaluated. Two entries make A(end, end) mean the last row and the last
    // column rather than the last element twice.
    private readonly List<(int[] Extents, int Slot)> _indexContext = new();

    private readonly Action _cancelCheck; // per-chunk poll inside packed operations
    private long _steps;
    private int _callDepth;

    /// <summary>Creates an interpreter over a prepared <paramref name="globals"/> environment.</summary>
    /// <param name="globals">The global environment, seeded with the built-ins.</param>
    /// <param name="cancellationToken">Checked cooperatively before every statement.</param>
    /// <param name="hook">The debug hook, or null for a plain full-speed run.</param>
    /// <param name="echo">Sink for MATLAB-style console echo of unsuppressed statement results, or
    /// null to disable echo entirely.</param>
    /// <param name="dialect">The language variant being run, or null for <see cref="JgsDialect.Jgs"/>.</param>
    public Interpreter(
        JgsEnvironment globals,
        CancellationToken cancellationToken,
        IJgsDebugHook? hook = null,
        Action<string>? echo = null,
        JgsDialect? dialect = null)
    {
        _globals = globals;
        CurrentFrame = globals;
        _cancellationToken = cancellationToken;
        _hook = hook;
        _echo = echo;
        Dialect = dialect ?? JgsDialect.Jgs;

        // Packed operations run in ~4M-element chunks and poll this between chunks, so Stop
        // interrupts a 100M-element elementwise statement mid-flight instead of after it.
        _cancelCheck = () => _cancellationToken.ThrowIfCancellationRequested();

        // plus/minus/mldivide/… are the operators under function names, so only something that can
        // apply an operator may declare them. Declaring here also puts them in the globals before a
        // workspace owner snapshots it, which keeps them out of whos and save.
        JgsBuiltins.RegisterOperatorFunctions(globals, this);
    }

    /// <summary>The language variant this run speaks; every JGS/MATLAB difference reads from it.</summary>
    public JgsDialect Dialect { get; private set; }

    /// <summary>
    /// Runs <paramref name="action"/> with <paramref name="dialect"/> active, restoring the caller's
    /// dialect after — how <c>run('file.m')</c> from a JGS script executes with MATLAB semantics
    /// (index base, auto-declaration, bracket concatenation) and not just MATLAB parsing. Known
    /// limit: a function the include defines runs its body under whatever dialect is active when it
    /// is called later, not the dialect it was written in.
    /// </summary>
    internal void RunInDialect(JgsDialect dialect, Action action)
    {
        JgsDialect previous = Dialect;
        Dialect = dialect;
        try
        {
            action();
        }
        finally
        {
            Dialect = previous;
        }
    }

    /// <summary>
    /// The MATLAB search path, or null when this run has none (JGS, and hosts that never built one).
    /// It is consulted only after the workspace, the script's own functions, and the built-ins have
    /// all failed a name — see <see cref="JgsFunctionPath"/> for why that order is deliberate.
    /// </summary>
    internal JgsFunctionPath? FunctionPath { get; set; }

    /// <summary>The message of the last error a <c>try</c> caught — what <c>lasterr</c> reports.</summary>
    internal string LastError { get; set; } = string.Empty;

    /// <summary>The identifier of the last error a <c>try</c> caught, for <c>lasterror</c>.</summary>
    internal string LastErrorIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// The frames an error unwound through, as the struct array <c>ME.stack</c> is (a real one since
    /// M65). An error raised at the top level unwound through nothing and gets an empty stack, which
    /// is MATLAB's answer too.
    /// </summary>
    private static JgsValue StackOf(JgsRuntimeException error)
    {
        var frames = new Dictionary<string, JgsValue>[error.Frames.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            (string name, string file, int line) = error.Frames[i];
            frames[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["file"] = JgsValue.Str(file),
                ["name"] = JgsValue.Str(name),
                ["line"] = JgsValue.Number(line),
            };
        }

        // A column, the shape MATLAB's is.
        return JgsValue.StructArray(
            new JgsStructArray(frames, ["file", "name", "line"]),
            frames.Length, frames.Length == 0 ? 0 : 1);
    }

    /// <summary>The message of the last warning raised — what <c>lastwarn</c> reports.</summary>
    internal string LastWarning { get; set; } = string.Empty;

    /// <summary>
    /// The environment of the innermost running function, or the globals when the script itself is
    /// running. It is the scope <c>eval</c> evaluates in and <c>exist</c>/<c>who</c> answer about,
    /// which is why it tracks function frames rather than every block.
    /// </summary>
    internal JgsEnvironment CurrentFrame { get; private set; } = null!;

    /// <summary>
    /// The frame that called the innermost running function, or null when nothing has been called and
    /// the script itself is running. This is the workspace <c>evalin('caller', …)</c> and
    /// <c>assignin('caller', …)</c> mean, and it is one frame of history rather than a full stack
    /// because one frame is the whole of what MATLAB's workspace words can name.
    /// </summary>
    internal JgsEnvironment? CallerFrame { get; private set; }

    /// <summary>
    /// The call expression that created the innermost function frame, or null at the top level.
    /// <c>inputname</c> reads its argument list to name the caller's variables.
    /// </summary>
    internal CallExpr? CurrentCall { get; private set; }

    /// <summary>The call being made right now, handed to the frame it is about to create.</summary>
    private CallExpr? _pendingCall;

    /// <summary>The global environment — <c>evalin('base', …)</c>'s workspace.</summary>
    internal JgsEnvironment Globals => _globals;

    /// <summary>
    /// Parses and runs <paramref name="code"/> in <paramref name="env"/>, returning the value of a
    /// trailing bare expression so <c>x = eval('1+1')</c> is 2. A parse failure becomes a runtime
    /// error at the call site, because from the script's point of view that is where it happened.
    /// </summary>
    internal JgsValue EvaluateSource(string code, JgsEnvironment env, int line, int column)
    {
        IReadOnlyList<Stmt> program;
        try
        {
            program = Parser.Parse(code, sourceId: "eval", Dialect);
        }
        catch (JgsException ex)
        {
            throw new JgsRuntimeException(line, column, ex.Message);
        }

        JgsValue result = JgsValue.Null;
        for (int i = 0; i < program.Count; i++)
        {
            if (i == program.Count - 1 && program[i] is ExprStmt tail && tail.Expression is not AssignExpr)
            {
                result = Evaluate(tail.Expression, env);
                continue;
            }

            Execute(program[i], env);
        }

        return result;
    }

    /// <summary>
    /// Applies a binary operator to two already-evaluated values, for the builtins that are the
    /// function forms of the operators (<c>plus</c>, <c>mldivide</c>, …). The line and column stand in
    /// for the syntax node the operator normally reports errors against.
    /// </summary>
    internal JgsValue ApplyOperator(TokenType op, JgsValue left, JgsValue right, int line, int column) =>
        ApplyBinary(op, left, right, new PreEvaluated(JgsValue.Null) { Line = line, Column = column });

    /// <summary>Builds a range from already-evaluated bounds, for <c>colon(a, b)</c>.</summary>
    internal JgsValue BuildRange(JgsValue start, JgsValue? step, JgsValue stop, int line, int column) =>
        EvaluateRange(
            new RangeExpr(
                new PreEvaluated(start),
                step is { } increment ? new PreEvaluated(increment) : null,
                new PreEvaluated(stop))
            {
                Line = line,
                Column = column,
            },
            _globals);

    /// <summary>
    /// Rebinds this interpreter for the next statement of an interactive session: a fresh cancellation
    /// token (each prompt gets its own Stop) and a fresh step budget (the limit exists to catch one
    /// runaway statement, so it must not accumulate across a session that stays alive for hours).
    /// A one-shot run never calls this — its token and budget come from the constructor.
    /// </summary>
    /// <remarks>
    /// Safe because a session executes statements one at a time: <c>_cancelCheck</c> reads the field
    /// rather than capturing the token, so the new token takes effect for the whole next statement.
    /// </remarks>
    public void BeginStatement(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _steps = 0;
    }

    private enum CompletionKind
    {
        Normal,
        Break,
        Continue,
        Return,
    }

    /// <summary>Runs a whole program. Top-level function declarations are hoisted so order does not matter.</summary>
    public void Run(IReadOnlyList<Stmt> program)
    {
        foreach (Stmt statement in program)
        {
            if (statement is FnStmt fn)
            {
                _globals.Declare(fn.Name, JgsValue.Function(new UserFunction(fn, _globals, this)));
                ScriptFunctionNames.Add(fn.Name);
            }
        }

        // The top level runs through the same block executor as everything else, so a debug hook sees
        // top-level statements too. (Reaching a FnStmt just re-declares it — hoisting made it callable
        // earlier; re-declaration is the same binding again.)
        Completion completion = ExecuteBlock(program, _globals);
        if (completion.Kind is CompletionKind.Break or CompletionKind.Continue)
        {
            throw new JgsRuntimeException(completion.Line, completion.Column,
                $"'{(completion.Kind == CompletionKind.Break ? "break" : "continue")}' can only appear inside a loop.");
        }

        // A top-level 'return' simply ends the script.
    }

    /// <summary>
    /// Runs a script file's statements in <paramref name="scope"/> — how a script named on the search
    /// path runs. MATLAB's rule is that a script shares the workspace of whatever called it, so this
    /// takes the caller's frame rather than making one, and the file's own functions are hoisted into
    /// that frame the way the top level's are.
    /// </summary>
    internal void RunScriptFile(IReadOnlyList<Stmt> program, JgsEnvironment scope)
    {
        foreach (Stmt statement in program)
        {
            if (statement is FnStmt fn)
            {
                scope.Declare(fn.Name, JgsValue.Function(new UserFunction(fn, scope, this)));
            }
        }

        Completion completion = ExecuteBlock(program, scope);
        if (completion.Kind is CompletionKind.Break or CompletionKind.Continue)
        {
            throw new JgsRuntimeException(completion.Line, completion.Column,
                $"'{(completion.Kind == CompletionKind.Break ? "break" : "continue")}' can only appear inside a loop.");
        }
    }

    /// <summary>Runs a user function's body in <paramref name="local"/> and returns its result (or null).</summary>
    /// <param name="declaration">The function being invoked (carries its name, body, and source).</param>
    /// <param name="local">The call's local environment with parameters already bound.</param>
    /// <param name="callLine">The 1-based line of the call site, for the debugger's call stack.</param>
    public JgsValue ExecuteFunctionBody(FnStmt declaration, JgsEnvironment local, int callLine)
    {
        if (++_callDepth > MaxCallDepth)
        {
            _callDepth--;
            throw new JgsRuntimeException(callLine, 0,
                $"Maximum recursion limit of {MaxCallDepth} reached.");
        }

        // eval and friends work in the scope that called them, which is this frame while the body
        // runs and the caller's again the moment it returns. inputname wants the call site the same
        // way, so the pending node moves into the frame here and is cleared so a later frame with no
        // call expression behind it cannot inherit this one's.
        JgsEnvironment callerFrame = CurrentFrame;
        JgsEnvironment? callersCaller = CallerFrame;
        CallExpr? callerCall = CurrentCall;
        FnStmt? callerFunction = _currentFunction;
        CurrentFrame = local;
        CallerFrame = callerFrame;
        CurrentCall = _pendingCall;
        _pendingCall = null;
        _currentFunction = declaration;

        // Nested functions hoist like top-level ones do in Run(): a handle taken before the nested
        // declaration line (increment = @doInc) must already resolve. Each closes over this call's
        // frame, which is what shares the parent's workspace with it.
        foreach (Stmt statement in declaration.Body)
        {
            if (statement is FnStmt nested)
            {
                local.Declare(nested.Name, JgsValue.Function(new UserFunction(nested, local, this)));
            }
        }

        _hook?.EnterFunction(declaration, callLine, local);
        try
        {
            Completion completion = ExecuteBlock(declaration.Body, local);
            return completion.Kind switch
            {
                CompletionKind.Return => completion.Value,
                CompletionKind.Break => throw new JgsRuntimeException(completion.Line, completion.Column,
                    "'break' can only appear inside a loop."),
                CompletionKind.Continue => throw new JgsRuntimeException(completion.Line, completion.Column,
                    "'continue' can only appear inside a loop."),
                _ => JgsValue.Null,
            };
        }
        catch (JgsRuntimeException error)
        {
            // The stack ME reports is built as the error unwinds, because by the time a catch sees it
            // every frame it passed through has already been torn down by the finally below. Each
            // frame records the line that was running in it: this one's own failing line for the
            // innermost, and the call it was waiting on for everything outside.
            error.PushFrame(declaration.Name, declaration.SourceId, callLine);
            throw;
        }
        finally
        {
            SavePersistents(declaration, local);
            CurrentFrame = callerFrame;
            CallerFrame = callersCaller;
            CurrentCall = callerCall;
            _currentFunction = callerFunction;
            _callDepth--;
            _hook?.ExitFunction();
        }
    }

    /// <summary>
    /// Applies an <c>arguments</c> block to the frame the call has already bound: fills in defaults
    /// for what the caller left out, checks the declared size and class, and runs each validator.
    /// </summary>
    /// <remarks>
    /// It runs as an ordinary statement rather than as part of the call, which is what makes a
    /// default expression able to mention an earlier argument (<c>b = a * 2</c>) — by the time this
    /// line reaches b, a is bound. MATLAB defines it the same way and for the same reason.
    /// </remarks>
    private void ExecuteArguments(ArgumentsStmt statement, JgsEnvironment env)
    {
        foreach (ArgumentSpec spec in statement.Arguments)
        {
            // The frame's own bindings, not the whole chain: a parameter named after a builtin was
            // otherwise found in the global scope and took the builtin's function value instead of
            // its declared default.
            if (!env.DeclaresLocally(spec.Name) || !env.TryGet(spec.Name, out JgsValue value))
            {
                value = JgsValue.Null;
                if (spec.Default is null)
                {
                    throw new JgsRuntimeException(statement.Line, statement.Column,
                        $"Not enough input arguments: '{spec.Name}' has no default and none was passed.");
                }

                value = Evaluate(spec.Default, env);
                env.Declare(spec.Name, value);
            }

            JgsValue checked_ = JgsBuiltins.CheckArgument(
                spec, value, statement.Line, statement.Column, _globals);
            if (!ReferenceEquals(checked_, value))
            {
                env.Declare(spec.Name, checked_); // a declared class the value did not have yet
            }

            RunValidators(spec.Validators, checked_, env);
        }
    }

    /// <summary>
    /// Runs one declaration's <c>mustBe…</c> validators over <paramref name="value"/> in
    /// <paramref name="env"/>. Shared by the <c>arguments</c> block and by class properties (M68),
    /// because MATLAB writes the two declarations with the same grammar and means the same by them.
    /// </summary>
    internal void RunValidators(IReadOnlyList<Expr> validators, JgsValue value, JgsEnvironment env)
    {
        foreach (Expr validator in validators)
        {
            // A bare name is the call MATLAB writes it as shorthand for; anything else is already
            // a call and is evaluated exactly as written, so mustBeMember(x, {'a','b'}) reads its
            // own argument out of the frame.
            _ = validator is VariableExpr bare
                ? EvaluateCall(
                    new CallExpr(bare, [new PreEvaluated(value)])
                        { Line = validator.Line, Column = validator.Column },
                    env)
                : Evaluate(validator, env);
        }
    }

    /// <summary>
    /// Binds a function's <c>persistent</c> names to their kept values (initially <c>[]</c>).
    /// Storage is keyed by the function declaration, so every call — and every closure instance of a
    /// nested function — shares the same slots, the way MATLAB persists per function, not per call.
    /// </summary>
    private void ExecutePersistent(PersistentStmt statement, JgsEnvironment env)
    {
        if (_currentFunction is not FnStmt owner)
        {
            throw new JgsRuntimeException(statement.Line, statement.Column,
                "'persistent' is only valid inside a function.");
        }

        if (!_persistents.TryGetValue(owner, out Dictionary<string, JgsValue>? slots))
        {
            _persistents[owner] = slots = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        }

        foreach (string name in statement.Names)
        {
            if (!slots.TryGetValue(name, out JgsValue? kept))
            {
                slots[name] = kept = JgsValue.Array(System.Array.Empty<JgsValue>());
            }

            env.Declare(name, kept);
        }
    }

    /// <summary>
    /// Writes a frame's persistent variables back to their function's slots when the call ends —
    /// assignment rebinds the frame's name to a new value, so the slot has to be refreshed from the
    /// binding rather than sharing it. Runs from the call's finally: a value assigned before a later
    /// error still persists, as in MATLAB.
    /// </summary>
    private void SavePersistents(FnStmt declaration, JgsEnvironment local)
    {
        if (!_persistents.TryGetValue(declaration, out Dictionary<string, JgsValue>? slots))
        {
            return;
        }

        foreach (string name in slots.Keys.ToArray())
        {
            // The frame's own binding only — a chain walk could find an unrelated outer variable.
            if (local.Locals.TryGetValue(name, out JgsValue? latest))
            {
                slots[name] = latest;
            }
        }
    }

    // --- Statements ---------------------------------------------------------------------------

    private Completion ExecuteBlock(IReadOnlyList<Stmt> statements, JgsEnvironment env)
    {
        if (_hook is not null)
        {
            return ExecuteBlockHooked(statements, env);
        }

        // The plain path stays allocation-free and hook-free — full speed for normal runs.
        foreach (Stmt statement in statements)
        {
            Tick();
            Completion completion = Execute(statement, env);
            if (completion.Kind != CompletionKind.Normal)
            {
                return completion;
            }
        }

        return Completion.Normal;
    }

    private Completion ExecuteBlockHooked(IReadOnlyList<Stmt> statements, JgsEnvironment env)
    {
        var block = new BlockExecution(statements);
        _hook!.EnterBlock(block);
        try
        {
            for (int i = 0; i < block.Statements.Count; i++)
            {
                Tick();

                // The hook may block (pause), edit the block's statement list in place (live edit),
                // or redirect execution (set next statement) by returning a jump index.
                if (_hook.BeforeStatement(block, i, env, _callDepth) is int jump)
                {
                    i = jump - 1; // loop increment re-enters BeforeStatement at the jump target
                    continue;
                }

                Completion completion = Execute(block.Statements[i], env);
                if (completion.Kind != CompletionKind.Normal)
                {
                    return completion;
                }
            }

            return Completion.Normal;
        }
        finally
        {
            _hook.ExitBlock();
        }
    }

    private Completion Execute(Stmt statement, JgsEnvironment env)
    {
        switch (statement)
        {
            case LetStmt let:
                JgsValue letValue = Evaluate(let.Value, env);
                env.Declare(let.Name, letValue);
                EchoBinding(let, let.Name, letValue);
                return Completion.Normal;

            case DestructuringLetStmt destructure:
                JgsValue tuple = Evaluate(destructure.Value, env);
                if (tuple.Type != JgsType.Array)
                {
                    throw new JgsRuntimeException(destructure.Line, destructure.Column,
                        $"Destructuring 'let' needs an array on the right-hand side, but got a {tuple.TypeName}.");
                }

                if (tuple.ArrayLength != destructure.Names.Count)
                {
                    throw new JgsRuntimeException(destructure.Line, destructure.Column,
                        $"Destructuring 'let' names {destructure.Names.Count} variables, but the array has {tuple.ArrayLength} elements.");
                }

                for (int n = 0; n < destructure.Names.Count; n++)
                {
                    JgsValue part = tuple.ElementAt(n);
                    env.Declare(destructure.Names[n], part);
                    EchoBinding(destructure, destructure.Names[n], part);
                }

                return Completion.Normal;

            case ExprStmt expr:
                ExecuteExpressionStatement(expr, env);
                return Completion.Normal;

            case FnStmt fn:
                env.Declare(fn.Name, JgsValue.Function(new UserFunction(fn, env, this)));
                if (ReferenceEquals(env, _globals))
                {
                    ScriptFunctionNames.Add(fn.Name);
                }

                return Completion.Normal;

            case IfStmt ifStmt:
                if (Evaluate(ifStmt.Condition, env).IsTruthy)
                {
                    return ExecuteBlock(ifStmt.Then, BlockScope(env));
                }

                return ifStmt.Else is not null
                    ? ExecuteBlock(ifStmt.Else, BlockScope(env))
                    : Completion.Normal;

            case WhileStmt whileStmt:
                return ExecuteWhile(whileStmt, env);

            case ForStmt forStmt:
                return ExecuteFor(forStmt, env);

            case ReturnStmt ret:
                return Completion.MakeReturn(ret.Value is null ? JgsValue.Null : Evaluate(ret.Value, env));

            case BreakStmt br:
                return Completion.MakeBreak(br.Line, br.Column);

            case ContinueStmt cont:
                return Completion.MakeContinue(cont.Line, cont.Column);

            case SwitchStmt switchStmt:
                return ExecuteSwitch(switchStmt, env);

            case TryStmt tryStmt:
                return ExecuteTry(tryStmt, env);

            case GlobalStmt globalStmt:
            {
                // The declaration belongs to the workspace that made it, which in MATLAB is the
                // function's own frame — 'global x' in one function leaves every other function's x
                // alone. JGS has no call boundary at all (see JgsEnvironment.IsCallBoundary), so
                // recording there would make the declaration die with the block that wrote it;
                // recording on the globals keeps the run-wide meaning JGS has always had.
                JgsEnvironment declaring = Dialect.MatlabFunctions ? env : _globals;
                foreach (string name in globalStmt.Names)
                {
                    declaring.DeclareGlobal(name);
                    if (!_globalWorkspace.Contains(name))
                    {
                        // MATLAB's answer for a global nobody has assigned yet is [], not an error.
                        _globalWorkspace.Declare(name, JgsValue.Array(System.Array.Empty<JgsValue>()));
                    }
                }

                return Completion.Normal;
            }

            case PersistentStmt persistentStmt:
                ExecutePersistent(persistentStmt, env);
                return Completion.Normal;

            case ArgumentsStmt arguments:
                ExecuteArguments(arguments, env);
                return Completion.Normal;

            // A classdef defines the class and binds its name to the constructor (M68). Running the
            // class file itself is therefore how a class is defined, and a classdef written inside an
            // ordinary script works for the same reason — one statement, one meaning, no second path.
            case ClassdefStmt classdef:
                env.Declare(
                    classdef.Name,
                    DefineClass(classdef, new JgsEnvironment(_globals)).ConstructorValue);
                return Completion.Normal;

            case MultiAssignStmt multi:
                ExecuteMultiAssign(multi, env);
                return Completion.Normal;

            default:
                throw new JgsRuntimeException(statement.Line, statement.Column, "Unsupported statement.");
        }
    }

    /// <summary>
    /// Runs an expression statement with the MATLAB console conventions: a bare function name calls it
    /// with no arguments (<c>figure;</c>); a bare variable displays it; an unsuppressed assignment
    /// echoes the assigned variable; any other unsuppressed non-null result is bound to <c>ans</c> and
    /// echoed as <c>ans = …</c>.
    /// </summary>
    private void ExecuteExpressionStatement(ExprStmt statement, JgsEnvironment env)
    {
        Expr expression = statement.Expression;

        if (expression is VariableExpr name && env.TryGet(name.Name, out JgsValue existing))
        {
            if (existing.Type == JgsType.Function)
            {
                JgsValue called = existing.AsCallable.Call(System.Array.Empty<JgsValue>(), statement.Line, statement.Column);
                if (BindsAns(existing))
                {
                    BindAns(statement, called, env);
                }

                return;
            }

            EchoBinding(statement, name.Name, existing);
            return;
        }

        // A few builtins draw when nothing was asked for and answer numbers when something was, and
        // that is a distinction only the statement itself can make: by the time the call has been
        // evaluated, "nobody wanted this" looks exactly like "somebody wanted one of these".
        if (expression is CallExpr discarded
            && CalleeValue(discarded, env).Type == JgsType.Function
            && CalleeValue(discarded, env).AsCallable is BuiltinFunction { KnowsWhenDiscarded: true } knowing
            && knowing.MultiOutput is { } none)
        {
            var given = new JgsValue[discarded.Arguments.Count];
            for (int i = 0; i < given.Length; i++)
            {
                given[i] = Evaluate(discarded.Arguments[i], env);
            }

            none(given, 0, discarded.Line, discarded.Column);
            return;
        }

        JgsValue value = Evaluate(expression, env);
        switch (expression)
        {
            case AssignExpr assign when RootName(assign.Target) is string assigned:
                EchoVariable(statement, assigned, env);
                break;
            case IncDecExpr incDec when RootName(incDec.Target) is string bumped:
                EchoVariable(statement, bumped, env);
                break;
            case CallExpr call when !BindsAns(CalleeValue(call, env)):
                break;
            default:
                BindAns(statement, value, env);
                break;
        }
    }

    /// <summary>
    /// Resolves what is being called. Identical to <see cref="Evaluate"/> except that a plain name is
    /// taken as-is: an auto-calling constant such as <c>eps</c> must stay a function here, so
    /// <c>eps(x)</c> reaches the builtin instead of trying to subscript the number it evaluates to.
    /// </summary>
    private JgsValue EvaluateCallee(Expr callee, JgsEnvironment env)
    {
        if (callee is VariableExpr name)
        {
            if (LookUp(name.Name, env, out JgsValue resolved))
            {
                return resolved;
            }

            // The path is consulted here as well as at the bare-name site, and it has to be: falling
            // through to Evaluate would find the same file and then *call* it, so f(3) would run f
            // with no arguments and subscript the answer.
            if (TryResolveOnPath(name.Name, out JgsValue fromFile))
            {
                return fromFile;
            }
        }

        // A dotted name in callee position is resolved without the auto-call a bare mention gets, for
        // the same reason the name branch above skips the path: `containers.Map(k, v)` must hand the
        // arguments to the constructor, not build an empty collection and then subscript it (M64).
        if (callee is MemberExpr dotted)
        {
            return EvaluateMember(dotted, env, autoCall: false);
        }

        return Evaluate(callee, env);
    }

    /// <summary>
    /// Looks <paramref name="name"/> up on the MATLAB search path — the last thing tried before a name
    /// is declared undefined.
    /// </summary>
    private bool TryResolveOnPath(string name, out JgsValue value)
    {
        if (Dialect.IsMatlab && FunctionPath is { } path)
        {
            return path.TryResolve(name, out value);
        }

        value = JgsValue.Null;
        return false;
    }

    /// <summary>The callable a call expression resolved to, when it is a plain name that is in scope.</summary>
    private static JgsValue CalleeValue(CallExpr call, JgsEnvironment env) =>
        call.Callee is VariableExpr name && env.TryGet(name.Name, out JgsValue value)
            ? value
            : JgsValue.Null;

    /// <summary>Whether a bare call of this value should bind and echo <c>ans</c>.</summary>
    private static bool BindsAns(JgsValue callee) =>
        callee.Type != JgsType.Function
        || callee.AsCallable is not BuiltinFunction builtin
        || builtin.BindsAnsAsStatement;

    /// <summary>The variable name at the root of an assignment target (<c>x</c>, <c>x[i]</c>, <c>x(i)</c>).</summary>
    private static string? RootName(Expr target) => target switch
    {
        VariableExpr variable => variable.Name,
        IndexExpr index => RootName(index.Target),
        CallExpr call => RootName(call.Callee),
        BraceIndexExpr brace => RootName(brace.Target),
        MemberExpr member => RootName(member.Target),
        _ => null,
    };

    /// <summary>
    /// Binds a bare expression's non-null result to <c>ans</c> and echoes it when unsuppressed.
    /// <c>ans</c> lands in the scope the statement ran in: at the prompt that is the base workspace,
    /// but inside a function body it is the call frame, which dies with the call — as in MATLAB,
    /// where running a function file leaves the base workspace untouched.
    /// </summary>
    private void BindAns(Stmt statement, JgsValue value, JgsEnvironment env)
    {
        if (value.Type == JgsType.Null)
        {
            return; // verbs like title(...) return nothing — no ans, no echo
        }

        env.Declare("ans", value);
        EchoBinding(statement, "ans", value);
    }

    private void EchoVariable(Stmt statement, string name, JgsEnvironment env)
    {
        if (_echo is not null && !statement.Suppressed && env.TryGet(name, out JgsValue value))
        {
            EchoBinding(statement, name, value);
        }
    }

    private void EchoBinding(Stmt statement, string name, JgsValue value)
    {
        if (_echo is null || statement.Suppressed)
        {
            return;
        }

        // A class that defines disp says how its instances look, and the echo of a bare name has to
        // ask it too — otherwise `obj` and `disp(obj)` show different things (M68).
        if (AnyClasses && value.Type == JgsType.Object && value.AsObject.Class.TryMethod("disp", out _))
        {
            _echo($"{name} =");
            TryObjectDisplay(value, statement.Line, statement.Column);
            return;
        }

        _echo($"{name} = {EchoDisplay(value)}");
    }

    /// <summary>
    /// A budgeted one-line display for console echo: arrays stop emitting elements once the line is
    /// long enough and note the total count, so echoing a million-sample signal stays O(line length).
    /// </summary>
    private static string EchoDisplay(JgsValue value)
    {
        if (value.Type != JgsType.Array)
        {
            return value.Display();
        }

        // A matrix is echoed by the same formatter disp uses, because its rows are the whole point of
        // it: the budgeted run below walks elements in column-major order and would show
        // [3 2 1 0; 4 5 6 7] as [3, 4, 2, 5, 1, 6, 0, 7], which is not the thing that was typed.
        // That formatter caps itself at a thousand elements and answers '[RxC matrix]' past it, so
        // echoing a large one stays bounded the same way this does.
        if (value.IsShaped)
        {
            return value.Display();
        }

        const int Budget = 100;
        int count = value.ArrayLength;
        var sb = new StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            if (sb.Length > Budget)
            {
                sb.Append("… (").Append(count).Append(" elements)");
                break;
            }

            JgsValue item = value.ElementAt(i);
            sb.Append(item.Type == JgsType.Array ? EchoDisplay(item) : item.Display());
        }

        return sb.Append(']').ToString();
    }

    /// <summary>Reads a name, honouring any <c>global</c> declaration that redirects it.</summary>
    private bool LookUp(string name, JgsEnvironment env, out JgsValue value)
    {
        if (env.IsGlobal(name) && _globalWorkspace.TryGet(name, out value))
        {
            return true;
        }

        return env.TryGet(name, out value);
    }

    /// <summary>
    /// The message for a name that resolves to nothing, in the dialect's own vocabulary. A MATLAB
    /// function JGraph knows about but does not implement says so by name, which is a far better
    /// answer than "not recognized" when a script reaches for a toolbox that is not here.
    /// </summary>
    private string Undefined(string name)
    {
        if (!Dialect.IsMatlab)
        {
            return $"'{name}' is not defined.";
        }

        return JgsBuiltins.IsUnsupportedMatlabFunction(name, out string what)
            ? $"'{name}' is not supported in JGraph ({what})."
            : $"'{name}' is not recognized as a variable or a function.";
    }

    /// <summary>
    /// A MATLAB <c>switch</c>: the first arm whose value matches runs, arms never fall through, and
    /// <c>case {a, b}</c> matches any member of the cell.
    /// </summary>
    private Completion ExecuteSwitch(SwitchStmt statement, JgsEnvironment env)
    {
        JgsValue subject = Evaluate(statement.Subject, env);
        foreach (SwitchCase arm in statement.Cases)
        {
            JgsValue candidate = Evaluate(arm.Value, env);
            bool matched = candidate.Type == JgsType.Cell
                ? System.Array.Exists(candidate.AsCell, alternative => JgsValue.AreEqual(subject, alternative))
                : JgsValue.AreEqual(subject, candidate);

            if (matched)
            {
                return ExecuteBlock(arm.Body, BlockScope(env));
            }
        }

        return statement.Otherwise is not null
            ? ExecuteBlock(statement.Otherwise, BlockScope(env))
            : Completion.Normal;
    }

    /// <summary>
    /// A MATLAB <c>try</c>/<c>catch</c>. It catches the script's own runtime errors only: cancellation,
    /// the step limit, and <c>exit</c> must still unwind, or a script could trap the user's Stop button.
    /// </summary>
    private Completion ExecuteTry(TryStmt statement, JgsEnvironment env)
    {
        try
        {
            return ExecuteBlock(statement.Body, BlockScope(env));
        }
        catch (JgsRuntimeException error)
        {
            // MATLAB remembers the last caught error for lasterr/lasterror, and this is where the
            // catching happens — recording it anywhere else would miss the runtime's own errors.
            LastError = error.Message;

            LastErrorIdentifier = error.Identifier;

            JgsEnvironment handler = BlockScope(env);
            if (statement.ErrorVariable is { } name)
            {
                handler.Declare(name, JgsBuiltins.MakeException(
                    error.Identifier, error.Message, StackOf(error)));
            }

            return ExecuteBlock(statement.Handler, handler);
        }
    }

    /// <summary>
    /// A MATLAB multiple-output call: <c>[a, b] = size(x)</c>. Each target takes the output in its
    /// position; a <c>~</c> target discards one.
    /// </summary>
    private void ExecuteMultiAssign(MultiAssignStmt statement, JgsEnvironment env)
    {
        // One target can stand for several outputs: [varargout{1:nargout}] = f(...) asks for as many
        // as the range names, which is how a relay hands on exactly what it was asked for. Resolving
        // that has to happen before the call, because it is what the call's output count is.
        List<Expr?> targets = ExpandAssignmentTargets(statement, env);

        JgsValue[] outputs = EvaluateForOutputs(statement.Call, targets.Count, env);
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] is not { } target)
            {
                continue; // '~': the output was computed, and is deliberately dropped
            }

            if (i >= outputs.Length)
            {
                throw new JgsRuntimeException(statement.Line, statement.Column,
                    $"This call returns {outputs.Length} value(s), but {targets.Count} were asked for.");
            }

            var assignment = new AssignExpr(target, TokenType.Assign, new PreEvaluated(outputs[i]))
            {
                Line = statement.Line,
                Column = statement.Column,
            };
            EvaluateAssign(assignment, env);
        }
    }

    /// <summary>
    /// The assignment targets one for one with the outputs they take. Every target stands for itself
    /// except a brace index naming several slots, which becomes one single-slot target per slot.
    /// </summary>
    private List<Expr?> ExpandAssignmentTargets(MultiAssignStmt statement, JgsEnvironment env)
    {
        var expanded = new List<Expr?>(statement.Targets.Count);
        foreach (Expr? target in statement.Targets)
        {
            if (target is not BraceIndexExpr { Indices.Count: 1 } brace)
            {
                expanded.Add(target);
                continue;
            }

            // The cell need not exist yet — varargout rarely does when the relay line runs — so the
            // subscript is measured against what is there, and writing past the end grows it.
            int length = brace.Target is VariableExpr named && LookUp(named.Name, env, out JgsValue held)
                && held.Type == JgsType.Cell ? held.AsCell.Length : 0;

            JgsValue? index = EvaluateIndexArgument(brace.Indices[0], length, env);
            if (index is null || index.Type != JgsType.Array)
            {
                expanded.Add(target); // one slot, or a ':' that the assignment itself will judge
                continue;
            }

            foreach (int slot in ComputePicks(index, Math.Max(length, index.ArrayLength), "cell", brace.Line, brace.Column))
            {
                expanded.Add(new BraceIndexExpr(
                    brace.Target,
                    [new PreEvaluated(JgsValue.Number(slot + Dialect.IndexBase))])
                {
                    Line = brace.Line,
                    Column = brace.Column,
                });
            }
        }

        return expanded;
    }

    /// <summary>
    /// Evaluates a call that is expected to produce <paramref name="wanted"/> outputs. User functions
    /// hand back their named outputs; a builtin that knows how to produce several does so; anything
    /// else produces its single value.
    /// </summary>
    private JgsValue[] EvaluateForOutputs(Expr call, int wanted, JgsEnvironment env)
    {
        // [a, b] = f(obj, …) dispatches on the object exactly as the single-output form does (M68);
        // asking here as well is what keeps a method with two outputs reachable.
        if (call is CallExpr dispatched && CouldDispatchOnClass(dispatched, env))
        {
            JgsValue[] given = EvaluateAll(dispatched.Arguments, env);
            if (TryMethodDispatch(((VariableExpr)dispatched.Callee).Name, given, out IJgsCallable? method))
            {
                return method is IJgsMultiCallable several
                    ? several.CallMultiple(given, wanted, dispatched.Line, dispatched.Column)
                    : [method.Call(given, dispatched.Line, dispatched.Column)];
            }

            return InvokeWithArguments(dispatched, given, wanted, env);
        }

        if (call is CallExpr invocation && EvaluateCallee(invocation.Callee, env) is { Type: JgsType.Function } callee)
        {
            JgsValue[] arguments = EvaluateAll(invocation.Arguments, env);

            if (callee.AsCallable is IJgsMultiCallable multi)
            {
                return multi.CallMultiple(arguments, wanted, invocation.Line, invocation.Column);
            }

            return [callee.AsCallable.Call(arguments, invocation.Line, invocation.Column)];
        }

        // A bare name that auto-calls is still a call when several outputs are asked for, so
        // `[x, y, z] = sphere` means what `[x, y, z] = sphere()` does. Reading it as a value instead
        // would evaluate the name through the zero-argument path — drawing a sphere on the way — and
        // then report a shortfall, which is the wrong answer twice over.
        if (call is VariableExpr bare
            && LookUp(bare.Name, env, out JgsValue named)
            && named.Type == JgsType.Function
            && named.AsCallable is BuiltinFunction { AutoCallsBare: true } and IJgsMultiCallable zeroArgument)
        {
            return zeroArgument.CallMultiple(System.Array.Empty<JgsValue>(), wanted, bare.Line, bare.Column);
        }

        // [a, b] = c{1:2} distributes a comma-separated list across the targets. It is not a call at
        // all, which is why it reaches this far: the list is the several values, already in order.
        if (call is BraceIndexExpr or MemberExpr)
        {
            return EvaluateSpread(call, env);
        }

        return [Evaluate(call, env)];
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> asking for <paramref name="wanted"/> outputs. This is
    /// how an anonymous handle passes an output count through to whatever it wraps, so that
    /// <c>[a, b] = f(x)</c> means the same thing whether <c>f</c> is <c>@minmax</c> or
    /// <c>@(x) minmax(x)</c>.
    /// </summary>
    internal JgsValue[] EvaluateForOutputsIn(Expr expression, int wanted, JgsEnvironment env) =>
        EvaluateForOutputs(expression, wanted, env);

    /// <summary>
    /// The environment an if/loop body runs in. JGS gives each block a scope of its own, so a variable
    /// declared inside one does not leak out; MATLAB has only function scope, where 'if c; x = 1; end'
    /// must leave x visible afterwards.
    /// </summary>
    private JgsEnvironment BlockScope(JgsEnvironment env) =>
        Dialect.FunctionScope ? env : new JgsEnvironment(env);

    private Completion ExecuteWhile(WhileStmt statement, JgsEnvironment env)
    {
        while (Evaluate(statement.Condition, env).IsTruthy)
        {
            Tick();
            Completion completion = ExecuteBlock(statement.Body, BlockScope(env));
            if (completion.Kind == CompletionKind.Break)
            {
                break;
            }

            if (completion.Kind == CompletionKind.Return)
            {
                return completion;
            }
        }

        return Completion.Normal;
    }

    private Completion ExecuteFor(ForStmt statement, JgsEnvironment env)
    {
        JgsValue iterable = Evaluate(statement.Iterable, env);
        if (iterable.Type == JgsType.Cell)
        {
            return ExecuteForOverCell(statement, iterable, env);
        }

        // A struct array iterates element by element, each pass binding a 1-by-1 struct (M65) — so
        // `for s = stats` reads s.Area in the body, the way a MATLAB script writes it.
        if (iterable.Type == JgsType.Struct)
        {
            return ExecuteForOverStructs(statement, iterable, env);
        }

        if (iterable.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(statement.Line, statement.Column,
                $"'for' can only iterate over an array or a cell, but got a {iterable.TypeName}.");
        }

        // MATLAB iterates the COLUMNS of the loop expression: over a matrix the variable is each
        // n-by-1 column in turn, and over a vector (one row, or one column — a single column is a
        // single pass) that degenerates to the elementwise walk below.
        bool byColumns = JgsMatrix.IsMatrix(iterable) && JgsMatrix.RowCount(iterable) > 1;
        int iterationCount = byColumns ? JgsMatrix.ColCount(iterable) : iterable.ArrayLength;
        int columnRows = byColumns ? JgsMatrix.RowCount(iterable) : 0;
        for (int index = 0; index < iterationCount; index++)
        {
            int column = index; // for the lambda below; 'index' is also the elementwise position
            JgsValue element = byColumns
                ? JgsMatrix.BuildValues(columnRows, 1, (r, _) => JgsMatrix.At(iterable, r, column))
                : CopyForBinding(iterable.ElementAt(index));
            Tick();
            JgsEnvironment local = BlockScope(env);
            local.Declare(statement.Variable, element);
            Completion completion = ExecuteBlock(statement.Body, local);
            if (completion.Kind == CompletionKind.Break)
            {
                break;
            }

            if (completion.Kind == CompletionKind.Return)
            {
                return completion;
            }
        }

        return Completion.Normal;
    }

    /// <summary>Runs a <c>for</c> whose loop expression is a cell array, column by column.</summary>
    /// <remarks>
    /// A cell iterates exactly the way a matrix does — one column per pass — but the bound value
    /// stays a cell, so a one-row cell binds a 1-by-1 cell each time and the body reads it with
    /// <c>x{1}</c> rather than <c>x</c>. That is what makes <c>for name = {'line', 'diamond'}</c> the
    /// ordinary way to walk a list of words; until M47 it was an error, and a script had to index a
    /// cell by hand to get the same loop.
    /// </remarks>
    private Completion ExecuteForOverCell(ForStmt statement, JgsValue iterable, JgsEnvironment env)
    {
        JgsValue[] elements = iterable.AsCell;
        int rows = System.Math.Max(iterable.Rows, 0);
        int cols = rows == 0 ? 0 : elements.Length / rows;
        for (int c = 0; c < cols; c++)
        {
            var column = new JgsValue[rows];
            for (int r = 0; r < rows; r++)
            {
                column[r] = CopyForBinding(elements[r + (c * rows)]);
            }

            JgsValue element = JgsValue.Cell(column);
            element.Reshape(rows, 1);
            Tick();
            JgsEnvironment local = BlockScope(env);
            local.Declare(statement.Variable, element);
            Completion completion = ExecuteBlock(statement.Body, local);
            if (completion.Kind == CompletionKind.Break)
            {
                break;
            }

            if (completion.Kind == CompletionKind.Return)
            {
                return completion;
            }
        }

        return Completion.Normal;
    }

    // --- Expressions --------------------------------------------------------------------------

    private JgsValue Evaluate(Expr expression, JgsEnvironment env)
    {
        switch (expression)
        {
            case NumberLiteral number:
                return JgsValue.Number(number.Value);

            case ComplexLiteral imaginary:
                return JgsValue.ComplexNum(new Complex(0, imaginary.Imaginary));

            // A double-quoted literal is a string scalar in MATLAB and a plain string in JGS, whose
            // surface is frozen and which has never had a string type to mean anything else (M63).
            case StringLiteral str:
                return str.IsChar || !Dialect.HasStringArrays
                    ? JgsValue.Str(str.Value)
                    : JgsValue.StringScalar(str.Value);

            case BoolLiteral boolean:
                return JgsValue.Bool(boolean.Value);

            case ArrayLiteral array:
                return EvaluateArrayLiteral(array, env);

            case MatrixLiteral matrix:
                return EvaluateMatrix(matrix, env);

            case RangeExpr range:
                return EvaluateRange(range, env);

            case EndExpr:
                if (_indexContext.Count == 0)
                {
                    throw new JgsRuntimeException(expression.Line, expression.Column,
                        "'end' is only valid inside an index expression, like x(end).");
                }

                // The stack holds the target's *extents* and which subscript is being evaluated;
                // 'end' is the last valid *index* along that dimension, which in a 0-based dialect
                // is one less than the extent and in a 1-based one is the extent itself.
                (int[] extents, int slot) = _indexContext[^1];
                return JgsValue.Number(extents[slot] - 1 + Dialect.IndexBase);

            case AllExpr:
                throw new JgsRuntimeException(expression.Line, expression.Column,
                    "':' by itself is only valid as an index argument, like x(:).");

            case VariableExpr variable:
                if (LookUp(variable.Name, env, out JgsValue value))
                {
                    // MATLAB writes its constants as zero-argument functions, and a bare mention of
                    // one means its value: x = eps is 2.2e-16. Only builtins that opt in behave this
                    // way, and callee position resolves through EvaluateCallee, so eps(x) still calls.
                    return value.Type == JgsType.Function
                        && value.AsCallable is BuiltinFunction { AutoCallsBare: true } constant
                            ? constant.Call(System.Array.Empty<JgsValue>(), variable.Line, variable.Column)
                            : value;
                }

                // A file on the path answers a bare name by running, which is MATLAB's rule for any
                // name that is not a variable: 'setup' runs setup.m, and @setup is how you ask for
                // the handle instead. Only path files behave this way — a built-in mentioned bare is
                // still its own value unless it opted into AutoCallsBare above.
                if (TryResolveOnPath(variable.Name, out JgsValue onPath))
                {
                    return onPath.AsCallable.Call(System.Array.Empty<JgsValue>(), variable.Line, variable.Column);
                }

                throw new JgsRuntimeException(variable.Line, variable.Column, Undefined(variable.Name));

            case PreEvaluated ready:
                return ready.Value;

            case UnaryExpr unary:
                return EvaluateUnary(unary, env);

            case LogicalExpr logical:
                return EvaluateLogical(logical, env);

            case BinaryExpr binary:
                return EvaluateBinary(binary, env);

            case IndexExpr index:
                return EvaluateIndex(index, env);

            case CallExpr call:
                return EvaluateCall(call, env);

            case AssignExpr assign:
                return EvaluateAssign(assign, env);

            case IncDecExpr incDec:
                return EvaluateIncDec(incDec, env);

            case TransposeExpr transpose:
                return EvaluateTranspose(transpose, env);

            case CellLiteral cell:
                return EvaluateCellLiteral(cell, env);

            case BraceIndexExpr brace:
                return EvaluateBraceIndex(brace, env);

            case MemberExpr member:
                return EvaluateMember(member, env);

            case AnonymousFnExpr anonymous:
                return JgsValue.Function(AnonymousFunction.Create(anonymous, env, this));

            case FunctionHandleExpr handle:
                if (env.TryGet(handle.Name, out JgsValue referenced) && referenced.Type == JgsType.Function)
                {
                    return referenced;
                }

                // @helper has to reach a file the same way helper(x) does, or a path function could
                // be called but never passed to cellfun.
                if (TryResolveOnPath(handle.Name, out JgsValue handleFromFile))
                {
                    return handleFromFile;
                }

                throw new JgsRuntimeException(handle.Line, handle.Column,
                    $"'@{handle.Name}': there is no function called '{handle.Name}'.");

            default:
                throw new JgsRuntimeException(expression.Line, expression.Column, "Unsupported expression.");
        }
    }

    /// <summary>
    /// Evaluates a MATLAB colon range to an inclusive arithmetic sequence. The endpoint uses a small
    /// floating tolerance so <c>0:0.001:3</c> yields exactly 3001 points despite binary rounding.
    /// </summary>
    private JgsValue EvaluateRange(RangeExpr range, JgsEnvironment env)
    {
        double start = RangeBound(range.Start, "start", env);
        double step = range.Step is null ? 1 : RangeBound(range.Step, "step", env);
        double stop = RangeBound(range.Stop, "stop", env);

        if (step == 0)
        {
            throw new JgsRuntimeException(range.Line, range.Column, "A range step must not be zero.");
        }

        double ratio = (stop - start) / step;
        if (double.IsNaN(ratio) || ratio < 0)
        {
            return JgsValue.Array(System.Array.Empty<JgsValue>());
        }

        const double MachineEpsilon = 2.220446049250313e-16;
        long count = (long)Math.Floor(ratio * (1 + (4 * MachineEpsilon))) + 1;

        // Packed ranges are 8 bytes/element and may spill to disk, so they get a far higher
        // ceiling (2 GB) than boxed ranges (whose ~48 bytes/element would exhaust the heap first).
        long limit = JgsPacking.Enabled ? 250_000_000 : 50_000_000;
        if (count > limit)
        {
            throw new JgsRuntimeException(range.Line, range.Column,
                $"This range would produce {count} elements — too many.");
        }

        if (JgsPacking.Enabled)
        {
            return PackedOps.CreateRange(start, step, count, _cancelCheck);
        }

        var values = new JgsValue[count];
        for (long i = 0; i < count; i++)
        {
            values[i] = JgsValue.Number(start + (i * step));
        }

        return JgsValue.Array(values);
    }

    private double RangeBound(Expr bound, string what, JgsEnvironment env)
    {
        JgsValue value = Evaluate(bound, env);
        if (!IsNumericScalar(value))
        {
            throw new JgsRuntimeException(bound.Line, bound.Column,
                $"The {what} of a range must be a number, but got a {value.TypeName}.");
        }

        return value.AsNumber;
    }

    /// <summary>
    /// Evaluates a bracketed literal as MATLAB concatenation: each element is a block, blocks in a
    /// row join left to right and must agree on height, and the rows stack and must agree on width.
    /// The result carries the shape that implies (ADR 0043).
    /// </summary>
    /// <remarks>
    /// One deliberate leniency, because a JGS vector carries no orientation: in a <em>stacked</em>
    /// literal whose blocks are all vectors, if any of them is a column then the rows are read as
    /// columns too. That is what keeps <c>[audio; zeros(k, 1)]</c> meaning "pad this signal" when
    /// the signal came from a reader that had no orientation to give it. Two genuine row vectors
    /// still stack into a 2-by-n matrix, because neither of them is a column.
    /// </remarks>
    private JgsValue EvaluateMatrix(MatrixLiteral matrix, JgsEnvironment env)
    {
        var rows = new List<JgsValue[]>(matrix.Rows.Count);
        foreach (IReadOnlyList<Expr> row in matrix.Rows)
        {
            rows.Add(EvaluateAll(row, env));
        }

        // Structs concatenate into a struct array (M65) rather than through the numeric block
        // machinery, which read them as one element apiece and answered with a double.
        if (AnyStruct(rows))
        {
            return ConcatenateStructs(rows, matrix);
        }

        // A cell joins as a container too, and for the same reason (M68): the block measurement below
        // reads a cell as one element whatever its size, so [{}, {x}] came back with a phantom
        // element. Asked after structs because a bracket cannot sensibly hold both.
        if (Dialect.ConcatenatesBrackets && AnyCell(rows))
        {
            return ConcatenateCells(rows, matrix);
        }

        var shapes = new List<(int Height, int Width)[]>(rows.Count);
        foreach (JgsValue[] row in rows)
        {
            var rowShapes = new (int, int)[row.Length];
            for (int i = 0; i < row.Length; i++)
            {
                rowShapes[i] = BlockShape(row[i]);
            }

            shapes.Add(rowShapes);
        }

        if (rows.Count > 1)
        {
            ReadRowsAsColumns(shapes);
        }

        (int height, int width) = MeasureLiteral(rows, shapes, matrix);
        return height == 0 || width == 0
            ? JgsValue.Array([])
            : StampLiteral(AssembleLiteral(rows, shapes, height, width), rows, matrix);
    }

    /// <summary>Gives an assembled literal the numeric class its pieces agree on (M47).</summary>
    /// <remarks>
    /// An integer class wins over the doubles beside it, so <c>[int8(1) 300]</c> is an int8 row whose
    /// second element saturates to 127 — the same rule concatenation follows in MATLAB.
    /// </remarks>
    private JgsValue StampLiteral(JgsValue assembled, List<JgsValue[]> rows, Node at)
    {
        JgsNumericClass numericClass = JgsNumericClass.Double;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                numericClass = JgsNumericClasses.CombineForConcat(
                    numericClass, piece.NumericClass, "A bracket literal", at.Line, at.Column);
            }
        }

        return JgsNumericClasses.Stamp(assembled, numericClass);
    }

    /// <summary>
    /// A single-row bracket literal. All-scalar elements — the overwhelmingly common case — build a
    /// plain row directly; anything containing an array is a horizontal concatenation and goes
    /// through the same block machinery a semicolon-rowed literal uses, so <c>[A, B]</c> joins two
    /// matrices side by side rather than nesting them.
    /// </summary>
    private JgsValue EvaluateArrayLiteral(ArrayLiteral array, JgsEnvironment env)
    {
        JgsValue[] elements = EvaluateAll(array.Elements, env);
        bool concatenating = false;
        foreach (JgsValue element in elements)
        {
            concatenating |= element.Type == JgsType.Array;
        }

        // In JGS a bracket literal is a list, so [[1, 2], [3, 4]] is a matrix by nesting — the
        // spelling its own scripts and guide have always used. Only MATLAB concatenates here.
        concatenating &= Dialect.ConcatenatesBrackets;

        // A bracket holding any struct concatenates into a struct array (M65). Asked before the
        // string and numeric joins for the same reason they are asked before each other: the type of
        // the pieces decides what the bracket means.
        if (Dialect.ConcatenatesBrackets && elements.Length > 0
            && Array.Exists(elements, static e => e.Type == JgsType.Struct))
        {
            return ConcatenateStructs([elements], array);
        }

        // A bracket holding any cell joins the cells (M68), which the block machinery below cannot do:
        // it measures a cell as a single element, so an empty one left a phantom behind.
        if (Dialect.ConcatenatesBrackets && elements.Length > 0
            && Array.Exists(elements, static e => e.Type == JgsType.Cell))
        {
            return ConcatenateCells([elements], array);
        }

        // A bracket holding any string array is a string array (M63): ["a" "b"] is 1-by-2, and
        // ["a" 'b'] is too, because a char row joining a string becomes one of its elements rather
        // than being spliced character by character. This is MATLAB's rule and the reason a script
        // can build a list of labels without a cell. It is asked first, because the char-row join
        // below would otherwise swallow the mixed case.
        if (Dialect.ConcatenatesBrackets && elements.Length > 0
            && Array.Exists(elements, static e => e.IsStringArray))
        {
            return JoinStringArrays(elements);
        }

        // Char rows join into one longer char row: ['SN:' id] is how a MATLAB script builds a label.
        // The test is on the values, not on how they were written: it used to require a single-quoted
        // literal among them, which was the only way to tell a char row from a double-quoted one
        // before strings had a type of their own — and which meant [a b], with both chars held in
        // variables, never joined at all.
        if (Dialect.ConcatenatesBrackets && elements.Length > 0
            && Array.TrueForAll(elements, static e => e.Type == JgsType.String))
        {
            var text = new StringBuilder();
            foreach (JgsValue piece in elements)
            {
                text.Append(piece.AsString);
            }

            return JgsValue.Str(text.ToString());
        }

        var rows = new List<JgsValue[]> { elements };
        if (!concatenating)
        {
            JgsValue list = JgsPacking.Enabled && PackedOps.TryPackElements(elements, out JgsValue packed)
                ? packed
                : JgsValue.Array(elements);
            return StampLiteral(list, rows, array);
        }

        var shapes = new List<(int Height, int Width)[]> { new (int, int)[elements.Length] };
        for (int i = 0; i < elements.Length; i++)
        {
            shapes[0][i] = BlockShape(elements[i]);
        }

        (int height, int width) = MeasureLiteral(rows, shapes, array.Line, array.Column);
        return height == 0 || width == 0
            ? JgsValue.Array([])
            : StampLiteral(AssembleLiteral(rows, shapes, height, width), rows, array);
    }

    /// <summary>
    /// Joins the pieces of a bracket literal that holds at least one string array into a single row
    /// of strings (M63). A char row or a number joining them contributes one element, not one per
    /// character or one per number: <c>["a" 'bc' 3]</c> is 1-by-3, and the last two are the strings
    /// <c>"bc"</c> and <c>"3"</c>, which is MATLAB's rule and the reason this cannot reuse the
    /// numeric concatenation machinery below.
    /// </summary>
    private static JgsValue JoinStringArrays(JgsValue[] elements)
    {
        var joined = new List<JgsValue>();
        foreach (JgsValue piece in elements)
        {
            if (piece.IsStringArray)
            {
                joined.AddRange(piece.BoxedElements());
            }
            else if (piece.Type == JgsType.String)
            {
                joined.Add(piece);
            }
            else if (piece.Type == JgsType.Array)
            {
                // A numeric array spreads, one string per number, the way string(x) would make it.
                for (int i = 0; i < piece.ArrayLength; i++)
                {
                    joined.Add(JgsValue.Str(piece.ElementAt(i).Display()));
                }
            }
            else
            {
                joined.Add(JgsValue.Str(piece.Display()));
            }
        }

        return JgsValue.StringArray([.. joined]);
    }

    /// <summary>The block a value contributes to a literal; an empty array contributes nothing.</summary>
    private static (int Height, int Width) BlockShape(JgsValue value) => value.Type != JgsType.Array
        ? (1, 1)
        : value.ArrayLength == 0 ? (0, 0) : (JgsMatrix.RowCount(value), JgsMatrix.ColCount(value));

    /// <summary>
    /// Applies the orientation-free-vector leniency described on <see cref="EvaluateMatrix"/>: turns
    /// every 1-by-n block into n-by-1 when the literal stacks vectors and at least one is a column.
    /// </summary>
    private static void ReadRowsAsColumns(List<(int Height, int Width)[]> shapes)
    {
        bool allVectors = true;
        bool anyColumn = false;
        foreach ((int Height, int Width)[] row in shapes)
        {
            foreach ((int height, int width) in row)
            {
                if (height == 0)
                {
                    continue;
                }

                allVectors &= height == 1 || width == 1;
                anyColumn |= width == 1 && height > 1;
            }
        }

        if (!allVectors || !anyColumn)
        {
            return;
        }

        foreach ((int Height, int Width)[] row in shapes)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (row[i].Height == 1 && row[i].Width > 1)
                {
                    row[i] = (row[i].Width, 1);
                }
            }
        }
    }

    /// <summary>Checks that the blocks tile a rectangle and returns its size.</summary>
    private static (int Height, int Width) MeasureLiteral(
        List<JgsValue[]> rows, List<(int Height, int Width)[]> shapes, Node at) =>
        MeasureLiteral(rows, shapes, at.Line, at.Column);

    private static (int Height, int Width) MeasureLiteral(
        List<JgsValue[]> rows, List<(int Height, int Width)[]> shapes, int atLine, int atColumn)
    {
        int height = 0;
        int width = -1;
        for (int r = 0; r < rows.Count; r++)
        {
            int rowHeight = -1;
            int rowWidth = 0;
            for (int i = 0; i < shapes[r].Length; i++)
            {
                (int blockHeight, int blockWidth) = shapes[r][i];
                if (blockHeight == 0)
                {
                    continue; // [] contributes nothing, exactly as in MATLAB
                }

                if (rowHeight < 0)
                {
                    rowHeight = blockHeight;
                }
                else if (blockHeight != rowHeight)
                {
                    throw new JgsRuntimeException(atLine, atColumn,
                        $"Cannot join these side by side: row {r + 1} starts {rowHeight} rows tall but element {i + 1} is {blockHeight}x{blockWidth}.");
                }

                rowWidth += blockWidth;
            }

            if (rowHeight < 0)
            {
                continue; // a row of nothing but empties
            }

            if (width < 0)
            {
                width = rowWidth;
            }
            else if (rowWidth != width)
            {
                throw new JgsRuntimeException(atLine, atColumn,
                    $"Cannot stack these: the literal is {width} columns wide but row {r + 1} is {rowWidth}.");
            }

            height += rowHeight;
        }

        return (height, width < 0 ? 0 : width);
    }

    /// <summary>
    /// Writes the blocks into one column-major result. Packed numeric blocks — the case a padded
    /// signal hits — go straight into a double buffer; anything else boxes and then repacks.
    /// </summary>
    private JgsValue AssembleLiteral(
        List<JgsValue[]> rows, List<(int Height, int Width)[]> shapes, int height, int width)
    {
        bool numeric = true;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue value in row)
            {
                numeric &= value.Type == JgsType.Number
                    || (value.Type == JgsType.Array && value.IsPacked && value.PackedKind == JgsPackedKind.Number);
            }
        }

        if (numeric && JgsPacking.Enabled)
        {
            var flat = new double[height * width];
            FillLiteral(rows, shapes, height, (destination, element) => flat[destination] = element.AsNumber);
            return JgsMatrix.FromColumnMajor(flat, height, width);
        }

        var boxed = new JgsValue[height * width];
        FillLiteral(rows, shapes, height, (destination, element) => boxed[destination] = element);
        if (JgsPacking.Enabled && PackedOps.TryPackElements(boxed, out JgsValue packed))
        {
            packed.Reshape(height, width);
            return packed;
        }

        return JgsValue.Shaped(boxed, height, width);
    }

    private void FillLiteral(
        List<JgsValue[]> rows, List<(int Height, int Width)[]> shapes, int height, Action<int, JgsValue> write)
    {
        int rowOrigin = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            int colOrigin = 0;
            int rowHeight = 0;
            for (int i = 0; i < rows[r].Length; i++)
            {
                (int blockHeight, int blockWidth) = shapes[r][i];
                if (blockHeight == 0)
                {
                    continue;
                }

                JgsValue block = rows[r][i];
                for (int c = 0; c < blockWidth; c++)
                {
                    int destination = ((colOrigin + c) * height) + rowOrigin;
                    for (int row = 0; row < blockHeight; row++)
                    {
                        write(destination + row, ReadBlock(block, blockHeight, row, c));
                    }
                }

                colOrigin += blockWidth;
                rowHeight = blockHeight;
            }

            rowOrigin += rowHeight;
            _cancelCheck?.Invoke();
        }
    }

    /// <summary>
    /// Element <c>(row, col)</c> of a literal block, read against the height the literal settled on
    /// rather than the block's own — which is what makes an adapted row vector read as a column.
    /// </summary>
    private static JgsValue ReadBlock(JgsValue block, int blockHeight, int row, int col)
    {
        if (block.Type != JgsType.Array)
        {
            return block;
        }

        return JgsMatrix.IsNested(block)
            ? block.ElementAt(row).ElementAt(col)
            : block.ElementAt((col * blockHeight) + row);
    }

    private static void FlattenInto(JgsValue value, List<JgsValue> into)
    {
        if (value.Type == JgsType.Array)
        {
            int count = value.ArrayLength;
            for (int i = 0; i < count; i++)
            {
                FlattenInto(value.ElementAt(i), into);
            }
        }
        else
        {
            into.Add(value);
        }
    }

    private JgsValue EvaluateUnary(UnaryExpr unary, JgsEnvironment env)
    {
        JgsValue operand = Evaluate(unary.Operand, env);
        if (AnyClasses && TryUnaryOverload(unary.Op, operand, unary, out JgsValue overloaded))
        {
            return overloaded;
        }

        if (unary.Op == TokenType.Bang)
        {
            // MATLAB's ~ is element-wise over arrays (~mask negates the mask, M43); JGS keeps
            // its scalar truthiness reading of !arr.
            if (Dialect.IsMatlab && operand.Type == JgsType.Array)
            {
                return JgsBuiltins.MapToBool("~", operand, static x => x == 0, unary.Line, unary.Column);
            }

            return JgsValue.Bool(!operand.IsTruthy);
        }

        // Minus: numeric negation, element-wise over arrays (complex included). Negation stays inside
        // the operand's class, so -uint8(5) saturates to 0 rather than escaping to a negative double.
        return JgsNumericClasses.Stamp(
            MapNumeric(operand, v => -v, "-", unary.Line, unary.Column, static c => -c),
            operand.NumericClass);
    }

    private JgsValue EvaluateLogical(LogicalExpr logical, JgsEnvironment env)
    {
        JgsValue left = Evaluate(logical.Left, env);
        if (logical.Op == TokenType.AmpAmp)
        {
            return left.IsTruthy ? JgsValue.Bool(Evaluate(logical.Right, env).IsTruthy) : JgsValue.False;
        }

        // ||
        return left.IsTruthy ? JgsValue.True : JgsValue.Bool(Evaluate(logical.Right, env).IsTruthy);
    }

    private JgsValue EvaluateBinary(BinaryExpr binary, JgsEnvironment env)
    {
        JgsValue left = Evaluate(binary.Left, env);
        JgsValue right = Evaluate(binary.Right, env);
        return ApplyBinary(binary.Op, left, right, binary);
    }

    /// <summary>Applies a binary operator to already-evaluated operands (shared with compound assignment).</summary>
    /// <summary>
    /// Applies a binary operator, then puts the answer back into the numeric class its operands
    /// agree on (M47).
    /// </summary>
    /// <remarks>
    /// Only arithmetic takes a class: a comparison answers a logical whatever it compared, and
    /// <c>&amp;</c>/<c>|</c> likewise. The rule itself lives in
    /// <see cref="JgsNumericClasses.Combine"/>, and it is what makes <c>uint8(200) + uint8(100)</c>
    /// saturate at 255 instead of quietly becoming 300.
    /// </remarks>
    private JgsValue ApplyBinary(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        if (left.NumericClass == JgsNumericClass.Double && right.NumericClass == JgsNumericClass.Double)
        {
            return ApplyBinaryCore(op, left, right, at);
        }

        if (!IsArithmetic(op))
        {
            return ApplyBinaryCore(op, left, right, at);
        }

        JgsNumericClass numericClass =
            JgsNumericClasses.Combine(left, right, OperatorSymbol(op), at.Line, at.Column);
        return JgsNumericClasses.Stamp(ApplyBinaryCore(op, left, right, at), numericClass);
    }

    /// <summary>The operators whose result carries a numeric class; everything else answers logical.</summary>
    private static bool IsArithmetic(TokenType op) => op is TokenType.Plus or TokenType.Minus
        or TokenType.Star or TokenType.Slash or TokenType.Backslash or TokenType.Caret
        or TokenType.DotStar or TokenType.DotSlash or TokenType.DotBackslash or TokenType.DotCaret;

    private JgsValue ApplyBinaryCore(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        // An operand that is an instance of a user class decides what the operator means (M68), and
        // has to decide before any numeric reading of the operands below — the same lesson M63 and
        // M64 each learnt one branch lower down.
        if (AnyClasses && TryOperatorOverload(op, left, right, at, out JgsValue overloaded))
        {
            return overloaded;
        }

        // Sparse operands route through their own kernels (M42) before any dense machinery sees them.
        if (left.Type == JgsType.Sparse || right.Type == JgsType.Sparse)
        {
            return JgsBuiltins.SparseBinary(op, left, right, at);
        }

        // Time arithmetic resolves before every numeric reading of the operands (M64), which has to
        // include the matrix forms below and not only the implicit expansion further down. A scalar
        // datetime is a 1-by-1 array of milliseconds, so `duration * duration` went to matrix
        // multiplication and answered with a number instead of being refused — the same shape of
        // mistake M63 made by putting string concatenation below expansion, one branch higher up.
        if (JgsBuiltins.IsTimeArithmetic(op, left, right))
        {
            return JgsBuiltins.TimeBinary(op, left, right, at.Line, at.Column,
                (o, a, b) => ApplyBinaryCore(o, a, b, at));
        }

        // MATLAB's '*', '/', '\' and '^' are matrix operations; only the dotted spellings are
        // elementwise. Everything below this point is elementwise, so the matrix forms resolve first.
        if (Dialect.IsMatlab)
        {
            if (op == TokenType.DotBackslash)
            {
                // a .\ b is b ./ a — elementwise division read the other way around. Mapping onto
                // './' (not '/') keeps the swapped operands off the matrix-division branch below.
                (left, right) = (right, left);
                op = TokenType.DotSlash;
            }

            if (op == TokenType.Backslash)
            {
                if (left.Type == JgsType.Array && right.Type == JgsType.Array)
                {
                    return MatrixOperation(op, left, right, at);
                }

                if (left.Type == JgsType.Array)
                {
                    throw new JgsRuntimeException(at.Line, at.Column,
                        "'\\' expects a right-hand side with as many rows as the left matrix.");
                }

                // scalar \ x is x / scalar.
                (left, right) = (right, left);
                op = TokenType.Slash;
            }

            if (op is TokenType.DotStar or TokenType.DotSlash or TokenType.DotCaret)
            {
                op = op switch
                {
                    TokenType.DotStar => TokenType.Star,
                    TokenType.DotSlash => TokenType.Slash,
                    _ => TokenType.Caret,
                };
            }
            else if (op is TokenType.Star or TokenType.Slash or TokenType.Caret
                     && left.Type == JgsType.Array && right.Type == JgsType.Array)
            {
                return MatrixOperation(op, left, right, at);
            }
            else if (op == TokenType.Caret && IsMatrix(left)
                     && right.Type is JgsType.Number or JgsType.Bool)
            {
                return MatrixPower(left, right.AsNumber, at);
            }
        }

        // String concatenation resolves before implicit expansion, and has to (M63): a string array
        // is an array underneath, so "p" + ["1" "2"] would otherwise be expanded pair by pair, each
        // pair joined as char, and the answer reassembled as a plain array — the right text with the
        // wrong type. ConcatenateStrings does its own spreading, which is the same rule applied once.
        if (op == TokenType.Plus && JgsBuiltins.ConcatenatesWithPlus(left, right))
        {
            return JgsBuiltins.ConcatenateStrings(left, right, at.Line, at.Column);
        }


        // MATLAB implicit expansion: two arrays of different shapes combine by expanding singleton
        // dimensions (a column plus a row is their outer sum; a 1x1 array behaves as a scalar).
        // The matrix operators resolved above, so everything reaching here is elementwise; nested
        // arrays keep their legacy recursion. Incompatible shapes throw inside Map with both named.
        if (left.Type == JgsType.Array && right.Type == JgsType.Array
            && !JgsMatrix.IsNested(left) && !JgsMatrix.IsNested(right)
            && !JgsBroadcast.SameShape(left, right))
        {
            return JgsBroadcast.Map(left, right, OperatorSymbol(op), at.Line, at.Column,
                (a, b) => ApplyBinary(op, a, b, at));
        }

        if (op is TokenType.Amp or TokenType.Pipe)
        {
            return ElementwiseLogical(op, left, right, at);
        }

        // Packed fast paths: SIMD kernels over flat buffers when an operand is packed and the
        // shapes fit; anything else falls through to the boxed code below unchanged. Ordering
        // comparisons check complex operands first so the boxed error still fires.
        if ((left.IsPacked || right.IsPacked)
            && left.Type != JgsType.Complex && right.Type != JgsType.Complex
            && !(op == TokenType.Plus
                 && (left.Type == JgsType.String || right.Type == JgsType.String
                     || JgsBuiltins.ConcatenatesWithPlus(left, right))))
        {
            if (PackedOps.MapArithmetic(op) is PackedMath.BinaryOp arithmetic
                && PackedOps.TryArithmetic(arithmetic, OperatorSymbol(op), left, right, _cancelCheck, at.Line, at.Column, out JgsValue fast))
            {
                return fast;
            }

            if (PackedOps.MapComparison(op) is PackedMath.CompareOp comparison
                && PackedOps.TryCompare(comparison, OperatorSymbol(op), left, right, _cancelCheck, at.Line, at.Column, out fast))
            {
                return fast;
            }

            if (op is TokenType.EqualEqual or TokenType.BangEqual
                && PackedOps.TryEquality(left, right, op == TokenType.BangEqual, _cancelCheck, at.Line, at.Column, out fast))
            {
                return fast;
            }
        }

        switch (op)
        {
            case TokenType.EqualEqual:
                return Equality(left, right, negate: false, at);
            case TokenType.BangEqual:
                return Equality(left, right, negate: true, at);
            case TokenType.Plus when left.Type == JgsType.String || right.Type == JgsType.String:
                return JgsValue.Str(left.Display() + right.Display());
            case TokenType.Plus:
                return NumericBinary(left, right, (a, b) => a + b, "+", at.Line, at.Column, static (a, b) => a + b);
            case TokenType.Minus:
                return NumericBinary(left, right, (a, b) => a - b, "-", at.Line, at.Column, static (a, b) => a - b);
            case TokenType.Star:
                return NumericBinary(left, right, (a, b) => a * b, "*", at.Line, at.Column, JgsBuiltins.MultiplyC99);
            case TokenType.Slash:
                return NumericBinary(left, right, (a, b) => a / b, "/", at.Line, at.Column, static (a, b) => a / b);
            case TokenType.Percent:
                return NumericBinary(left, right, (a, b) => a % b, "%", at.Line, at.Column);
            case TokenType.Caret:
                return NumericBinary(left, right, Math.Pow, "^", at.Line, at.Column, Complex.Pow);
            case TokenType.Less:
                return Compare(left, right, op, at, (a, b) => a < b);
            case TokenType.LessEqual:
                return Compare(left, right, op, at, (a, b) => a <= b);
            case TokenType.Greater:
                return Compare(left, right, op, at, (a, b) => a > b);
            case TokenType.GreaterEqual:
                return Compare(left, right, op, at, (a, b) => a >= b);
            default:
                throw new JgsRuntimeException(at.Line, at.Column, "Unsupported operator.");
        }
    }

    // --- Assignment expressions ---------------------------------------------------------------

    /// <summary>
    /// The value to store when binding a name, under the dialect's assignment semantics. MATLAB copies
    /// containers, so <c>b = a; b(1) = 0</c> leaves <c>a</c> alone; JGS shares the reference, which is
    /// cheaper and is what its own scripts already rely on. Applied at the three places a name is
    /// bound: assignment, a loop variable, and a function's arguments.
    /// </summary>
    public JgsValue CopyForBinding(JgsValue value)
    {
        if (!Dialect.CopyOnAssign
            || value.Type is not (JgsType.Array or JgsType.Cell or JgsType.Struct or JgsType.Object))
        {
            return value; // scalars, strings and functions are immutable — nothing to copy
        }

        // An instance of a class written `classdef Name < handle` is a reference, so a second name for
        // it is the same object; an instance of an ordinary class is a value and is copied. That one
        // line is the whole of the difference, which is why the object model below knows nothing about
        // it (M68).
        if (value.Type == JgsType.Object)
        {
            return value.AsObject.Class.IsHandle ? value : JgsValue.Object(value.AsObject.Copy(this));
        }

        // A handle-class value is a reference, so binding a second name to it must not clone it:
        // two names for one containers.Map are one collection, which is the whole difference between
        // it and a dictionary (M64). This is also the rule M68's `classdef … < handle` needs, which
        // is why it is a named rule rather than a check for one class.
        if (JgsBuiltins.IsHandleClass(value))
        {
            return value;
        }

        return CopyContainer(value);
    }

    /// <summary>Evaluates one expression in <paramref name="env"/> — the entry point a callable body needs.</summary>
    public JgsValue EvaluateIn(Expr expression, JgsEnvironment env) => Evaluate(expression, env);

    private JgsValue CopyContainer(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            JgsValue[] cell = value.AsCell;
            var copied = new JgsValue[cell.Length];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = CopyForBinding(cell[i]);
            }

            JgsValue copy = JgsValue.Cell(copied);
            copy.Reshape(value.Rows, value.Cols); // cells carry a shape too (M41)
            return copy;
        }

        if (value.Type == JgsType.Struct)
        {
            JgsStructArray source = value.AsStructArray;
            var copiedElements = new Dictionary<string, JgsValue>[source.Length];
            for (int i = 0; i < copiedElements.Length; i++)
            {
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach ((string name, JgsValue field) in source.Elements[i])
                {
                    fields[name] = CopyForBinding(field);
                }

                copiedElements[i] = fields;
            }

            JgsValue copiedStruct = JgsValue.StructArray(
                new JgsStructArray(copiedElements, source.EmptyFields), value.Rows, value.Cols);
            copiedStruct.SetClassName(value.ClassName); // an MException stays one when it is passed on
            return copiedStruct;
        }

        if (value.IsPacked)
        {
            // A fresh wrapper over a fresh buffer: the single-wrapper invariant holds, and the
            // previous-run disposal walk (which compares by reference) sees two distinct arrays.
            NumericBuffer source = value.AsBuffer;
            NumericBuffer copy = JgsPacking.Allocate(source.Length);
            source.AsSpan().CopyTo(copy.AsSpan());
            GC.KeepAlive(source);
            return KeepShape(value, JgsValue.Packed(copy, value.PackedKind));
        }

        if (value.IsPackedComplex)
        {
            JgsPackedComplex planes = value.AsPackedComplex;
            NumericBuffer re = JgsPacking.Allocate(planes.Length);
            NumericBuffer im = JgsPacking.Allocate(planes.Length);
            planes.Re.AsSpan().CopyTo(re.AsSpan());
            planes.Im.AsSpan().CopyTo(im.AsSpan());
            GC.KeepAlive(planes);
            return KeepShape(value, JgsValue.PackedComplexArray(new JgsPackedComplex(re, im)));
        }

        JgsValue[] source2 = value.AsArray;
        var elements = new JgsValue[source2.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            // Nested arrays are copied too, and MATLAB copies the whole thing.
            elements[i] = source2[i].Type == JgsType.Array ? CopyContainer(source2[i]) : source2[i];
        }

        return KeepShape(value, JgsValue.Array(elements));
    }

    /// <summary>
    /// Gives a freshly built copy the shape and the numeric class of what it was copied from. Both
    /// live on the wrapper, so every path that mints a new wrapper has to carry them across or the
    /// copy silently becomes a flat row of doubles — which is exactly what a MATLAB-dialect binding
    /// does to every matrix it touches.
    /// </summary>
    private static JgsValue KeepShape(JgsValue source, JgsValue copy)
    {
        if (source.IsShaped || source.IsNd)
        {
            copy.TakeShapeOf(source);
        }

        return CarryValueTags(source, copy);
    }

    /// <summary>
    /// Gives a freshly built value the three tags that say what it <em>is</em> — its numeric class,
    /// whether it is a string array, and what kind of time it holds — without touching its shape.
    /// </summary>
    /// <remarks>
    /// This is the same trap M62's MException class name fell into and M63's string array fell into
    /// again: a tag that lives on the wrapper is lost by every path that mints a new one, and MATLAB's
    /// value semantics mint a new one constantly. Keeping the three together in one helper is what
    /// stops the next path from carrying two of them. Transpose is separate from
    /// <see cref="KeepShape"/> precisely because its shape is deliberately <em>not</em> the source's.
    /// </remarks>
    private static JgsValue CarryValueTags(JgsValue source, JgsValue copy)
    {
        copy.SetNumericClass(source.NumericClass);

        if (source.IsStringArray)
        {
            copy.MarkStringArray();
        }

        if (source.TimeTag is JgsTimeTag time)
        {
            copy.MarkTime(time);
        }

        return copy;
    }

    /// <summary>Maps a compound-assignment token to the underlying binary operator.</summary>
    private static TokenType UnderlyingOp(TokenType op) => op switch
    {
        TokenType.PlusAssign => TokenType.Plus,
        TokenType.MinusAssign => TokenType.Minus,
        TokenType.StarAssign => TokenType.Star,
        TokenType.SlashAssign => TokenType.Slash,
        TokenType.PercentAssign => TokenType.Percent,
        _ => TokenType.Assign,
    };

    /// <summary>An empty pristine table, for a <see cref="JgsEnvironment.Forget"/> that reverts to nothing.</summary>
    private static readonly Dictionary<string, JgsValue> EmptyPristine = new();

    private JgsValue EvaluateAssign(AssignExpr assign, JgsEnvironment env)
    {
        JgsValue rhs = Evaluate(assign.Value, env);

        if (assign.Target is VariableExpr variable)
        {
            // A name this workspace declared 'global' is written where every scope that declared it
            // can see it.
            JgsEnvironment scope = env.IsGlobal(variable.Name) ? _globalWorkspace : env;
            JgsValue stored = rhs;
            if (assign.Op != TokenType.Assign)
            {
                if (!scope.TryGet(variable.Name, out JgsValue current))
                {
                    throw NotDefined(variable.Name, assign);
                }

                stored = ApplyBinary(UnderlyingOp(assign.Op), current, rhs, assign);
            }
            else
            {
                stored = CopyForBinding(stored);
            }

            if (!scope.TryAssign(variable.Name, stored))
            {
                // A first plain assignment declares the variable where 'let' is optional; where it is
                // required, not having one is the typo the requirement exists to catch.
                if (Dialect.RequireLet || assign.Op != TokenType.Assign)
                {
                    throw NotDefined(variable.Name, assign);
                }

                scope.Declare(variable.Name, stored);
            }

            return stored;
        }

        if (assign.Target is MemberExpr member)
        {
            if (assign.Op != TokenType.Assign)
            {
                JgsValue current = EvaluateMember(member, env);
                return AssignToMember(member, ApplyBinary(UnderlyingOp(assign.Op), current, rhs, assign), env);
            }

            return AssignToMember(member, CopyForBinding(rhs), env);
        }

        if (assign.Target is BraceIndexExpr brace)
        {
            if (assign.Op != TokenType.Assign)
            {
                JgsValue current = EvaluateBraceIndex(brace, env);
                return AssignToBraceIndex(brace, ApplyBinary(UnderlyingOp(assign.Op), current, rhs, assign), env);
            }

            return AssignToBraceIndex(brace, CopyForBinding(rhs), env);
        }

        // An index write in either spelling: x(k) = v, x[0:n] = 0, x(mask) = v, x[:] = v. The parser
        // guarantees the only remaining target shapes are these two.
        (Expr container, IReadOnlyList<Expr> subscripts) = assign.Target switch
        {
            CallExpr paren => (paren.Callee, paren.Arguments),
            _ => (((IndexExpr)assign.Target).Target, ((IndexExpr)assign.Target).Indices),
        };

        // MATLAB conjures the variable an index write names: x(5) = 1 with no x makes [0 0 0 0 1],
        // the same grow-and-zero-fill an existing array gets. The write starts from [] and the growth
        // below does the rest; a write that then fails takes the conjured variable with it, so a bad
        // subscript does not leave an empty x behind. Where a first assignment must say 'let', it
        // still must — the typo net a bare plain assignment respects is not defeated by adding a
        // subscript — and a compound op reads before it writes, so x(5) += 1 on no x stays an error.
        JgsEnvironment? conjuredScope = null;
        string? conjuredName = null;
        if (assign.Op == TokenType.Assign
            && !Dialect.RequireLet
            && container is VariableExpr fresh
            && !LookUp(fresh.Name, env, out _))
        {
            conjuredScope = env.IsGlobal(fresh.Name) ? _globalWorkspace : env;
            conjuredName = fresh.Name;
            conjuredScope.Declare(conjuredName, JgsValue.Array(System.Array.Empty<JgsValue>()));
        }

        try
        {
            return subscripts.Count switch
            {
                2 => AssignTwoSubscripts(container, subscripts, assign.Op, rhs, assign, env),
                > 2 => AssignNSubscripts(container, subscripts, assign.Op, rhs, assign, env),
                _ => AssignThroughIndex(container, subscripts, assign.Op, rhs, assign, env),
            };
        }
        catch when (conjuredScope is not null)
        {
            conjuredScope.Forget(conjuredName!, EmptyPristine);
            throw;
        }
    }

    /// <summary>
    /// <c>A(i, j) = v</c>: a scalar right-hand side fills the selection, an array must match its
    /// shape. Writing past an edge grows the matrix and zero-fills, and <c>A(i, :) = []</c> deletes —
    /// both of which reallocate, so they need a plain variable to rebind.
    /// </summary>
    private JgsValue AssignTwoSubscripts(
        Expr target, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsValue callee = Evaluate(target, env);
        if (callee.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign by index into a {callee.TypeName}; only arrays support element assignment.");
        }

        int rows = JgsMatrix.RowCount(callee);
        int cols = JgsMatrix.ColCount(callee);
        int[] extents = [rows, cols];
        JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
        JgsValue? colIndex = EvaluateIndexArgument(subscripts[1], extents, 1, env);

        if (op == TokenType.Assign && rhs.Type == JgsType.Array && rhs.ArrayLength == 0)
        {
            return DeleteSlice(target, callee, rowIndex, colIndex, rows, cols, at, env);
        }

        int[] rowPicks = WritePicks(rowIndex, rows, at);
        int[] colPicks = WritePicks(colIndex, cols, at);

        int neededRows = Math.Max(rows, Highest(rowPicks) + 1);
        int neededCols = Math.Max(cols, Highest(colPicks) + 1);
        if (neededRows > rows || neededCols > cols)
        {
            callee = Grow(target, callee, rows, cols, neededRows, neededCols, at, env);
            rows = neededRows;
        }

        bool scalarRhs = rhs.Type is not JgsType.Array;
        if (!scalarRhs)
        {
            int wanted = rowPicks.Length * colPicks.Length;
            if (rhs.ArrayLength != wanted)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Cannot assign {rhs.ArrayLength} values into a {rowPicks.Length}x{colPicks.Length} selection.");
            }
        }

        // A nested matrix stores one array per row rather than one column-major run, so the flat slot
        // below would index the list of rows instead of the elements. The read path has always gone
        // through JgsMatrix.At, which knows both forms; the write path did not, and walked off the end
        // of the row list for any selection reaching past the row count — `A(1:2, 1:2) = 5` on a JGS
        // 4×4 crashed the interpreter outright rather than raising a script error.
        bool nested = JgsMatrix.IsNested(callee);

        for (int c = 0; c < colPicks.Length; c++)
        {
            for (int r = 0; r < rowPicks.Length; r++)
            {
                JgsValue source = scalarRhs ? rhs : rhs.ElementAt((c * rowPicks.Length) + r);
                if (nested)
                {
                    JgsValue row = callee.ElementAt(rowPicks[r]);
                    JgsValue nestedStored = op == TokenType.Assign
                        ? source
                        : ApplyBinary(UnderlyingOp(op), row.ElementAt(colPicks[c]), source, at);
                    WriteElement(row, colPicks[c], nestedStored);
                    continue;
                }

                int slot = rowPicks[r] + (colPicks[c] * rows);
                JgsValue stored = op == TokenType.Assign
                    ? source
                    : ApplyBinary(UnderlyingOp(op), callee.ElementAt(slot), source, at);
                WriteElement(callee, slot, stored);
            }
        }

        return rhs;
    }

    private static int Highest(int[] picks)
    {
        int highest = -1;
        foreach (int pick in picks)
        {
            highest = Math.Max(highest, pick);
        }

        return highest;
    }

    /// <summary>
    /// Subscript positions for a write. Unlike a read, an index past the end is not an error — it is
    /// how a matrix grows — so only the lower bound is checked here. A mask still has to fit, because
    /// a mask that does not match the dimension is a mistake rather than a request to grow.
    /// </summary>
    private int[] WritePicks(JgsValue? index, int extent, Node at)
    {
        if (index is null)
        {
            return AllPicks(extent);
        }

        if (index.Type != JgsType.Array)
        {
            return [WriteIndex(index, at)];
        }

        if (IsLogicalIndex(index))
        {
            return ComputePicks(index, extent, "array", at.Line, at.Column);
        }

        int count = index.ArrayLength;
        var picks = new int[count];
        for (int i = 0; i < count; i++)
        {
            picks[i] = WriteIndex(index.ElementAt(i), at);
        }

        return picks;
    }

    private int WriteIndex(JgsValue position, Node at)
    {
        if (position.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"An index must be a number, but got a {position.TypeName}.");
        }

        double raw = position.AsNumber;
        if (raw != Math.Floor(raw) || double.IsNaN(raw))
        {
            throw new JgsRuntimeException(at.Line, at.Column, $"An index must be a whole number, not {raw}.");
        }

        int slot = (int)raw - Dialect.IndexBase;
        if (slot < 0)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Index {(int)raw} is out of range (indexing is {Dialect.IndexBase}-based).");
        }

        return slot;
    }

    /// <summary>
    /// Reallocates a matrix to a larger shape, zero-filling the new cells and rebinding the name.
    /// Growth has to replace the value, not mutate it, so the target must be a plain variable — the
    /// same restriction cell growth has had since <c>c{end + 1} = x</c>.
    /// </summary>
    private JgsValue Grow(
        Expr target, JgsValue current, int rows, int cols, int newRows, int newCols, Node at, JgsEnvironment env)
    {
        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Assigning outside a {rows}x{cols} matrix would grow it, which needs a plain variable on the left.");
        }

        // MATLAB's copy-on-assign makes the bound wrapper uniquely owned, so a packed matrix can
        // grow in place with amortized capacity — the difference between seconds and hours for a
        // loop that grows one row and column per step. JGS shares wrappers between names, where
        // the rebuild-and-rebind below is the observable behavior scripts rely on.
        if (Dialect.CopyOnAssign && current.TryGrowInPlace(newRows, newCols))
        {
            return current;
        }

        var elements = new JgsValue[newRows * newCols];
        Array.Fill(elements, JgsValue.Number(0));
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                elements[r + (c * newRows)] = JgsMatrix.At(current, r, c);
            }
        }

        JgsValue grown = JgsMatrix.FromElements(elements, newRows, newCols);
        if (!env.TryAssign(variable.Name, grown))
        {
            env.Declare(variable.Name, grown);
        }

        return grown;
    }

    /// <summary>
    /// <c>A(i, :) = []</c> and <c>A(:, j) = []</c>: MATLAB deletes whole rows or columns, and refuses
    /// anything else, because removing a lone element from a rectangle has no answer.
    /// </summary>
    private JgsValue DeleteSlice(
        Expr target, JgsValue current, JgsValue? rowIndex, JgsValue? colIndex, int rows, int cols, Node at, JgsEnvironment env)
    {
        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Deleting rows or columns needs a plain variable on the left.");
        }

        bool deletingRows = colIndex is null;
        if (deletingRows == (rowIndex is null))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Deleting from a matrix takes a whole row or column: A(i, :) = [] or A(:, j) = [].");
        }

        int[] removed = deletingRows
            ? ComputePicks(AsIndexArray(rowIndex!), rows, "row", at.Line, at.Column)
            : ComputePicks(AsIndexArray(colIndex!), cols, "column", at.Line, at.Column);
        var drop = new HashSet<int>(removed);

        int[] keptRows = deletingRows ? Remaining(rows, drop) : AllPicks(rows);
        int[] keptCols = deletingRows ? AllPicks(cols) : Remaining(cols, drop);

        JgsValue trimmed = JgsMatrix.BuildValues(keptRows.Length, keptCols.Length,
            (r, c) => JgsMatrix.At(current, keptRows[r], keptCols[c]));
        if (!env.TryAssign(variable.Name, trimmed))
        {
            env.Declare(variable.Name, trimmed);
        }

        return trimmed;
    }

    /// <summary>
    /// Extends a vector to <paramref name="needed"/> elements, zero-filling, and rebinds the name —
    /// the <c>x(end + 1) = v</c> idiom. The vector keeps whichever orientation it already had.
    /// </summary>
    private JgsValue GrowVector(Expr target, JgsValue current, int needed, Node at, JgsEnvironment env)
    {
        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Assigning past the end of a {current.ArrayLength}-element array would grow it, "
                + "which needs a plain variable on the left.");
        }

        bool growsAsColumn = current.Cols == 1 && current.Rows > 1;
        if (Dialect.CopyOnAssign
            && current.TryGrowInPlace(growsAsColumn ? needed : 1, growsAsColumn ? 1 : needed))
        {
            return current;
        }

        var elements = new JgsValue[needed];
        Array.Fill(elements, JgsValue.Number(0));
        for (int i = 0; i < current.ArrayLength; i++)
        {
            elements[i] = current.ElementAt(i);
        }

        bool wasColumn = current.Cols == 1 && current.Rows > 1;
        JgsValue grown = JgsMatrix.FromElements(elements, wasColumn ? needed : 1, wasColumn ? 1 : needed);
        if (!env.TryAssign(variable.Name, grown))
        {
            env.Declare(variable.Name, grown);
        }

        return grown;
    }

    /// <summary>
    /// <c>x(idx) = []</c>: removes the selected elements and rebinds. A matrix cannot lose a lone
    /// element and stay rectangular, so it takes a whole row or column instead.
    /// </summary>
    private JgsValue DeleteElements(Expr target, JgsValue current, JgsValue index, Node at, JgsEnvironment env)
    {
        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "Deleting elements needs a plain variable on the left.");
        }

        if (current.Rows > 1 && current.Cols > 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Deleting from a matrix takes a whole row or column: A(i, :) = [] or A(:, j) = [].");
        }

        var removed = new HashSet<int>(ComputePicks(AsIndexArray(index), current.ArrayLength, "array", at.Line, at.Column));
        int[] kept = Remaining(current.ArrayLength, removed);
        var elements = new JgsValue[kept.Length];
        for (int i = 0; i < kept.Length; i++)
        {
            elements[i] = current.ElementAt(kept[i]);
        }

        bool wasColumn = current.Cols == 1 && current.Rows > 1;
        JgsValue trimmed = JgsMatrix.FromElements(
            elements, wasColumn ? elements.Length : 1, wasColumn ? 1 : elements.Length);
        if (!env.TryAssign(variable.Name, trimmed))
        {
            env.Declare(variable.Name, trimmed);
        }

        return trimmed;
    }

    /// <summary>A scalar subscript read as a one-element index array, so one code path covers both.</summary>
    private static JgsValue AsIndexArray(JgsValue index) =>
        index.Type == JgsType.Array ? index : JgsValue.Array([index]);

    private static int[] Remaining(int extent, HashSet<int> removed)
    {
        var kept = new List<int>(extent);
        for (int i = 0; i < extent; i++)
        {
            if (!removed.Contains(i))
            {
                kept.Add(i);
            }
        }

        return kept.ToArray();
    }

    /// <summary>
    /// Writes one element of an array value. Packed arrays take the fast path when the value's
    /// type matches the buffer's kind; any other write demotes the array to boxed in place first
    /// (all aliases share the wrapper, so they all see the demotion — semantics identical).
    /// </summary>
    private static void WriteElement(JgsValue container, int index, JgsValue value)
    {
        if (container.IsPacked)
        {
            // SetPackedNumber, not AsBuffer: the growth-capacity write path must stay capacity-aware,
            // and AsBuffer's compaction guard would undo the amortized growth per element.
            if (container.PackedKind == JgsPackedKind.Number && value.Type == JgsType.Number)
            {
                container.SetPackedNumber(index, value.AsNumber);
                return;
            }

            if (container.PackedKind == JgsPackedKind.Bool && value.Type == JgsType.Bool)
            {
                container.SetPackedNumber(index, value.AsBool ? 1 : 0);
                return;
            }

            container.DemoteToBoxed();
        }
        else if (container.IsPackedComplex)
        {
            if (value.Type is JgsType.Number or JgsType.Complex)
            {
                System.Numerics.Complex written = value.AsComplex; // a Number reads as re+0i
                JgsPackedComplex planes = container.AsPackedComplex;
                planes.Re.AsSpan()[index] = written.Real;
                planes.Im.AsSpan()[index] = written.Imaginary;
                return;
            }

            container.DemoteToBoxed();
        }

        container.AsArray[index] = value;
    }

    /// <summary>
    /// An index write (0-based), shared by <c>a[…] = v</c> and <c>a(…) = v</c>. The target and its
    /// single subscript evaluate exactly once, so <c>a[f(i)] += 1</c> calls <c>f</c> once; a scalar
    /// right-hand side broadcasts over the selection, an array right-hand side must match its length.
    /// Compound operators apply per element.
    /// </summary>
    private JgsValue AssignThroughIndex(
        Expr target, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsValue callee = Evaluate(target, env);

        // m(key) = value on a keyed collection writes the entry rather than an element (M64). It
        // writes in place, which is right for both: a Map is shared by every name bound to it, and a
        // dictionary was copied when this name was bound.
        if (JgsBuiltins.IsKeyedCollection(callee))
        {
            if (subscripts.Count != 1 || op != TokenType.Assign)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    "A keyed collection is written one key at a time, as m(key) = value.");
            }

            JgsBuiltins.Put(callee, Evaluate(subscripts[0], env), rhs, at.Line, at.Column);
            return rhs;
        }

        // S(k) = [] deletes elements from a struct array (M65); S(k) = otherStruct replaces them.
        // Both need a plain name to rebind, since the element list is rebuilt either way.
        if (callee.Type == JgsType.Struct && op == TokenType.Assign)
        {
            return AssignIntoStruct(target, callee, subscripts, rhs, at, env);
        }

        if (callee.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign by index into a {callee.TypeName}; only arrays support element assignment.");
        }

        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Index assignment takes one subscript (an index, a range, a mask, or ':') or two (a row and a column).");
        }

        JgsValue? index = EvaluateIndexArgument(subscripts[0], callee.ArrayLength, env);

        // x(idx) = [] removes those elements; x(n) = v past the end grows and zero-fills. Both
        // replace the value rather than writing into it, so both need a plain variable to rebind.
        if (op == TokenType.Assign && index is not null && rhs.Type == JgsType.Array && rhs.ArrayLength == 0)
        {
            return DeleteElements(target, callee, index, at, env);
        }

        if (index is not null)
        {
            int[] wanted = WritePicks(index, callee.ArrayLength, at);
            int needed = Highest(wanted) + 1;
            if (needed > callee.ArrayLength)
            {
                callee = JgsMatrix.IsMatrix(callee)
                    ? throw new JgsRuntimeException(at.Line, at.Column,
                        $"Index {needed - 1 + Dialect.IndexBase} is past the end of a "
                        + $"{JgsMatrix.RowCount(callee)}x{JgsMatrix.ColCount(callee)} matrix; grow it with two subscripts, like A({needed}, 1).")
                    : GrowVector(target, callee, needed, at, env);
            }
        }

        if (callee.IsPacked)
        {
            if (TryPackedParenWrite(callee, index, op, rhs, at, out JgsValue packedResult))
            {
                return packedResult;
            }

            // Outside the fast path (logical target, non-numeric right-hand side, …): demote in
            // place and run the boxed code below — every alias follows, semantics unchanged.
            callee.DemoteToBoxed();
        }
        else if (callee.IsPackedComplex)
        {
            if (TryPackedComplexParenWrite(callee, index, op, rhs, at, out JgsValue complexResult))
            {
                return complexResult;
            }

            callee.DemoteToBoxed();
        }

        JgsValue[] array = callee.AsArray;

        // Scalar index: single-element write, no picks array needed.
        if (index is { Type: not JgsType.Array })
        {
            int single = ToIndex(index, array.Length, at.Line, at.Column);
            JgsValue stored = op == TokenType.Assign
                ? rhs
                : ApplyBinary(UnderlyingOp(op), array[single], rhs, at);
            array[single] = stored;
            return stored;
        }

        int[] picks = index is null
            ? AllPicks(array.Length)
            : ComputePicks(index, array.Length, "array", at.Line, at.Column);

        if (rhs.Type != JgsType.Array)
        {
            foreach (int pick in picks)
            {
                array[pick] = op == TokenType.Assign ? rhs : ApplyBinary(UnderlyingOp(op), array[pick], rhs, at);
            }
        }
        else
        {
            JgsValue[] source = rhs.AsArray;
            if (source.Length != picks.Length)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Cannot assign {source.Length} values into {picks.Length} selected elements.");
            }

            for (int i = 0; i < picks.Length; i++)
            {
                array[picks[i]] = op == TokenType.Assign
                    ? source[i]
                    : ApplyBinary(UnderlyingOp(op), array[picks[i]], source[i], at);
            }
        }

        return rhs;
    }

    /// <summary>
    /// The packed-target paren write: numeric scalars and packed-number right-hand sides write
    /// straight into the buffer (bulk fill/scatter for plain assignment, a sequential
    /// read-modify-write loop for compound operators so aliasing and repeated picks behave exactly
    /// like the boxed loop). Returns false for shapes the boxed path must handle after demotion.
    /// </summary>
    private bool TryPackedParenWrite(JgsValue target, JgsValue? index, TokenType op, JgsValue rhs, Node at, out JgsValue result)
    {
        result = rhs;
        if (target.PackedKind != JgsPackedKind.Number)
        {
            return false; // writes into logical masks are rare — demote and let the boxed path decide
        }

        bool simple = op == TokenType.Assign;
        bool rhsScalar = rhs.Type == JgsType.Number;
        bool rhsPacked = rhs.Type == JgsType.Array && rhs.IsPacked && rhs.PackedKind == JgsPackedKind.Number;
        if (!rhsScalar && !rhsPacked)
        {
            return false;
        }

        // Scalar index: a single-element write (an array right-hand side would nest, boxed-style).
        // Capacity-aware on purpose: touching AsBuffer here would compact a growth-capacity vector
        // on every iteration of the very x(i) = v loop this fast path exists for.
        if (index is { Type: not JgsType.Array })
        {
            if (!rhsScalar)
            {
                return false;
            }

            int single = ToIndex(index, target.ArrayLength, at.Line, at.Column);
            double stored = simple
                ? rhs.AsNumber
                : ApplyBinary(UnderlyingOp(op), target.ElementAt(single), rhs, at).AsNumber;
            target.SetPackedNumber(single, stored);
            result = simple ? rhs : JgsValue.Number(stored);
            return true;
        }

        NumericBuffer buffer = target.AsBuffer; // compacts growth capacity; bulk writes want it flat

        int[] picks = index is null
            ? AllPicks(buffer.Length)
            : ComputePicks(index, buffer.Length, "array", at.Line, at.Column);

        if (rhsPacked && rhs.ArrayLength != picks.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign {rhs.ArrayLength} values into {picks.Length} selected elements.");
        }

        if (simple)
        {
            if (rhsScalar)
            {
                PackedMath.ScatterConstant(buffer, picks, rhs.AsNumber);
            }
            else
            {
                PackedMath.Scatter(buffer, picks, rhs.AsBuffer);
            }

            return true;
        }

        // Compound: sequential read-modify-write, identical in order (and therefore in aliasing
        // and repeated-pick behavior) to the boxed loop. Cancellation polls between stretches.
        Func<double, double, double> combine = UnderlyingOp(op) switch
        {
            TokenType.Plus => static (a, b) => a + b,
            TokenType.Minus => static (a, b) => a - b,
            TokenType.Star => static (a, b) => a * b,
            TokenType.Slash => static (a, b) => a / b,
            _ => static (a, b) => a % b, // Percent — the only remaining compound operator
        };

        NumericBuffer? source = rhsPacked ? rhs.AsBuffer : null;
        double scalarRhs = rhsScalar ? rhs.AsNumber : 0;
        for (int i = 0; i < picks.Length; i++)
        {
            Span<double> span = buffer.AsSpan();
            span[picks[i]] = combine(span[picks[i]], source is null ? scalarRhs : source.AsSpan()[i]);
            if ((i & ((1 << 20) - 1)) == (1 << 20) - 1)
            {
                _cancelCheck();
            }
        }

        GC.KeepAlive(buffer);
        return true;
    }

    /// <summary>
    /// The packed-complex paren write: plain assignment of a number or complex scalar (broadcast
    /// over the selection) or of a matching packed array writes both planes in place — the
    /// <c>X(1:k) = 0</c> spectral-zeroing idiom without demoting a million-bin spectrum. Compound
    /// operators and other right-hand shapes return false for the demote-and-box fallback.
    /// </summary>
    private bool TryPackedComplexParenWrite(JgsValue target, JgsValue? index, TokenType op, JgsValue rhs, Node at, out JgsValue result)
    {
        result = rhs;
        if (op != TokenType.Assign)
        {
            return false;
        }

        JgsPackedComplex planes = target.AsPackedComplex;
        bool rhsScalar = rhs.Type is JgsType.Number or JgsType.Complex;
        bool rhsPackedReal = rhs is { Type: JgsType.Array, IsPacked: true, PackedKind: JgsPackedKind.Number };
        bool rhsPackedComplex = rhs.Type == JgsType.Array && rhs.IsPackedComplex;
        if (!rhsScalar && !rhsPackedReal && !rhsPackedComplex)
        {
            return false;
        }

        // Scalar index: single-element write (array right-hand sides would nest, boxed-style).
        if (index is { Type: not JgsType.Array })
        {
            if (!rhsScalar)
            {
                return false;
            }

            int single = ToIndex(index, planes.Length, at.Line, at.Column);
            System.Numerics.Complex written = rhs.AsComplex;
            planes.Re.AsSpan()[single] = written.Real;
            planes.Im.AsSpan()[single] = written.Imaginary;
            return true;
        }

        int[] picks = index is null
            ? AllPicks(planes.Length)
            : ComputePicks(index, planes.Length, "array", at.Line, at.Column);

        if (!rhsScalar && rhs.ArrayLength != picks.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign {rhs.ArrayLength} values into {picks.Length} selected elements.");
        }

        if (rhsScalar)
        {
            System.Numerics.Complex written = rhs.AsComplex;
            PackedMath.ScatterConstant(planes.Re, picks, written.Real);
            PackedMath.ScatterConstant(planes.Im, picks, written.Imaginary);
        }
        else if (rhsPackedReal)
        {
            PackedMath.Scatter(planes.Re, picks, rhs.AsBuffer);
            PackedMath.ScatterConstant(planes.Im, picks, 0);
        }
        else
        {
            JgsPackedComplex source = rhs.AsPackedComplex;
            PackedMath.Scatter(planes.Re, picks, source.Re);
            PackedMath.Scatter(planes.Im, picks, source.Im);
        }

        return true;
    }

    private static int[] AllPicks(int length)
    {
        var picks = new int[length];
        for (int i = 0; i < picks.Length; i++)
        {
            picks[i] = i;
        }

        return picks;
    }

    private JgsValue EvaluateIncDec(IncDecExpr incDec, JgsEnvironment env)
    {
        string symbol = incDec.Increment ? "++" : "--";
        double delta = incDec.Increment ? 1 : -1;

        if (incDec.Target is VariableExpr variable)
        {
            if (!env.TryGet(variable.Name, out JgsValue current))
            {
                throw NotDefined(variable.Name, incDec);
            }

            JgsValue updated = JgsValue.Number(RequireIncDecNumber(current, symbol, incDec) + delta);
            env.TryAssign(variable.Name, updated); // TryGet succeeded, so the binding exists
            return incDec.Prefix ? updated : current;
        }

        // x(k)++ / x[k]++ — one element of an array, in either spelling.
        (Expr targetExpr, IReadOnlyList<Expr> subscripts) = incDec.Target switch
        {
            CallExpr paren => (paren.Callee, paren.Arguments),
            _ => (((IndexExpr)incDec.Target).Target, ((IndexExpr)incDec.Target).Indices),
        };

        JgsValue container = Evaluate(targetExpr, env);
        if (container.Type != JgsType.Array || subscripts.Count is not (1 or 2))
        {
            throw new JgsRuntimeException(incDec.Line, incDec.Column,
                $"'{symbol}' by index needs an array and an index, like x(k){symbol} or A(i, j){symbol}.");
        }

        int single;
        if (subscripts.Count == 2)
        {
            int rows = JgsMatrix.RowCount(container);
            int[] extents = [rows, JgsMatrix.ColCount(container)];
            JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
            JgsValue? colIndex = EvaluateIndexArgument(subscripts[1], extents, 1, env);
            if (rowIndex is null or { Type: JgsType.Array } || colIndex is null or { Type: JgsType.Array })
            {
                throw new JgsRuntimeException(incDec.Line, incDec.Column,
                    $"'{symbol}' needs a single element, not a slice.");
            }

            single = ToIndex(rowIndex, rows, incDec.Line, incDec.Column)
                + (ToIndex(colIndex, extents[1], incDec.Line, incDec.Column) * rows);
        }
        else
        {
            JgsValue? index = EvaluateIndexArgument(subscripts[0], container.ArrayLength, env);
            if (index is null || index.Type == JgsType.Array)
            {
                throw new JgsRuntimeException(incDec.Line, incDec.Column,
                    $"'{symbol}' needs a single element, not a slice.");
            }

            single = ToIndex(index, container.ArrayLength, incDec.Line, incDec.Column);
        }

        JgsValue previous = container.ElementAt(single);
        JgsValue bumped = JgsValue.Number(RequireIncDecNumber(previous, symbol, incDec) + delta);
        WriteElement(container, single, bumped);
        return incDec.Prefix ? bumped : previous;
    }

    private static double RequireIncDecNumber(JgsValue value, string symbol, Node at)
    {
        if (value.Type == JgsType.Number)
        {
            return value.AsNumber;
        }

        if (value.Type == JgsType.Bool)
        {
            return value.AsBool ? 1 : 0;
        }

        throw new JgsRuntimeException(at.Line, at.Column,
            $"'{symbol}' needs a number, but got a {value.TypeName}.");
    }

    private static JgsRuntimeException NotDefined(string name, Node at) =>
        new(at.Line, at.Column, $"'{name}' is not defined. Declare it first with 'let'.");

    /// <summary>Evaluates <c>target[…]</c>. Brackets never call: <c>f[x]</c> on a function is an error
    /// even though <c>f(x)</c> would invoke it — that distinction is the two forms' only difference.</summary>
    private JgsValue EvaluateIndex(IndexExpr indexExpr, JgsEnvironment env)
    {
        JgsValue target = Evaluate(indexExpr.Target, env);
        if (target.Type == JgsType.Table)
        {
            return IndexTableParen(target, indexExpr.Indices, indexExpr, env);
        }

        if (target.Type is JgsType.Number or JgsType.Bool)
        {
            target = OneElementArray(target);
        }
        else if (target.Type is not (JgsType.Array or JgsType.String or JgsType.Image))
        {
            throw new JgsRuntimeException(indexExpr.Line, indexExpr.Column,
                target.Type == JgsType.Function
                    ? "Cannot index a function; call it with parentheses instead."
                    : $"Cannot index a {target.TypeName}.");
        }

        return IndexInto(target, indexExpr.Indices, indexExpr, env);
    }

    private JgsValue EvaluateCall(CallExpr call, JgsEnvironment env)
    {
        // A call is dispatched on the class of its first argument before the name is looked up at all
        // (M68), because a class method must beat a builtin of the same name. The guard is three cheap
        // checks that a script defining no classes fails on the first of them.
        if (CouldDispatchOnClass(call, env))
        {
            JgsValue[] given = EvaluateAll(call.Arguments, env);
            if (TryMethodDispatch(((VariableExpr)call.Callee).Name, given, out IJgsCallable? method))
            {
                _pendingCall = call;
                return method.Call(given, call.Line, call.Column);
            }

            return InvokeWithArguments(call, given, env);
        }

        JgsValue callee = EvaluateCallee(call.Callee, env);

        // "Calling" an array, string, or image with subscripts is indexing, identical to the bracket
        // form — a scalar lookup, a bool-mask filter, an index-array/range gather, 'end', or ':'.
        if (callee.Type is JgsType.Array or JgsType.String or JgsType.Image)
        {
            return IndexInto(callee, call.Arguments, call, env);
        }

        // T(rows, vars) on a table selects a smaller table; T{rows, vars} takes the contents out.
        if (callee.Type == JgsType.Table)
        {
            return IndexTableParen(callee, call.Arguments, call, env);
        }

        // c(i) on a cell array selects a sub-cell; c{i} (the brace form) takes the contents out.
        if (callee.Type == JgsType.Cell)
        {
            JgsValue[] elements = callee.AsCell;
            JgsValue? index = EvaluateIndexArgument(
                Single(call.Arguments, call, "Indexing a cell"), elements.Length, env);
            if (index is null)
            {
                return JgsValue.Cell((JgsValue[])elements.Clone()); // c(:) is the whole cell
            }

            if (index.Type == JgsType.Array)
            {
                int[] picks = ComputePicks(index, elements.Length, "cell", call.Line, call.Column);
                var selected = new JgsValue[picks.Length];
                for (int i = 0; i < picks.Length; i++)
                {
                    selected[i] = elements[picks[i]];
                }

                return JgsValue.Cell(selected);
            }

            return JgsValue.Cell([elements[ToIndex(index, elements.Length, call.Line, call.Column)]]);
        }

        // A single number is a one-by-one array, so subscripting one is a read out of it: h(1) on a
        // lone handle, x(1) on a scalar reading. Found writing M57's stress script, where a chart verb
        // that drew one thing handed back one handle and h(1) — the spelling that works when it drew
        // several — could not read it back.
        if (callee.Type is JgsType.Number or JgsType.Bool && call.Arguments.Count > 0)
        {
            return IndexInto(OneElementArray(callee), call.Arguments, call, env);
        }

        // m('key') on a keyed collection is a lookup (M64). It arrives here rather than at IndexInto
        // because a name followed by parentheses parses as a call until something says otherwise,
        // and for a Map nothing did: it read as a struct being called.
        if (JgsBuiltins.IsKeyedCollection(callee) && call.Arguments.Count == 1)
        {
            return JgsBuiltins.Lookup(callee, Evaluate(call.Arguments[0], env), call.Line, call.Column);
        }

        // S(k) on a struct is a subscript, not a call (M65) — it arrives here for the same reason a
        // Map lookup does: a name followed by parentheses parses as a call until something says
        // otherwise. Before M65 nothing did, so S(2).a on a struct array reported it was not a
        // function.
        if (callee.Type == JgsType.Struct && call.Arguments.Count > 0)
        {
            return IndexStruct(callee, call.Arguments, call, env);
        }

        // S(i, j) on a sparse matrix is a subscript, arriving here for the same reason a struct array
        // and a keyed collection do: parentheses after a name read as a call until something says
        // otherwise, and until M66 nothing said so for sparse.
        if (JgsBuiltins.IsSparseSubscript(callee) && call.Arguments.Count > 0)
        {
            return JgsBuiltins.SparseSubscript(
                callee, EvaluateAll(call.Arguments, env), Dialect, call.Line, call.Column);
        }

        if (callee.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(call.Line, call.Column, $"Cannot call a {callee.TypeName}; it is not a function.");
        }

        JgsValue[] arguments = EvaluateAll(call.Arguments, env);

        // inputname reports the caller's variable name for an argument, so the call expression has
        // to reach the frame the call creates. Handing over the node itself costs one field write —
        // building a list of names here would cost an allocation on every call in the language.
        _pendingCall = call;
        return callee.AsCallable.Call(arguments, call.Line, call.Column);
    }

    /// <summary>
    /// The one index read shared by <c>a[…]</c> and <c>a(…)</c>: one subscript indexes linearly
    /// (column-major over a matrix, ADR 0043), two name a row and a column. An image takes two or
    /// three. A string takes one.
    /// </summary>
    /// <summary>
    /// The one-by-one array a scalar is, kept in whatever numeric class the scalar was in so that
    /// <c>class(x(1))</c> answers what <c>class(x)</c> does.
    /// </summary>
    private static JgsValue OneElementArray(JgsValue scalar) =>
        JgsNumericClasses.Stamp(JgsValue.Array([scalar]), scalar.NumericClass);

    private JgsValue IndexInto(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        // A selection out of a uint8 array is still uint8 (M47). The samples are already inside the
        // class, so stamping the wrapper the read produced costs nothing but keeps class(x(1)) right.
        if (target.NumericClass != JgsNumericClass.Double)
        {
            return JgsNumericClasses.Stamp(IndexIntoCore(target, subscripts, at, env), target.NumericClass);
        }

        // A selection out of a string array is a string array (M63), for the same reason: s(2) is
        // MATLAB's 1-by-1 string, not the char row inside it. Without this, indexing would quietly
        // demote every element the first time a script reached for one.
        if (target.IsStringArray)
        {
            JgsValue picked = IndexIntoCore(target, subscripts, at, env);
            return picked.Type == JgsType.String ? JgsValue.StringScalar(picked.AsString)
                : picked.Type == JgsType.Array && !picked.IsStringArray ? picked.MarkStringArray()
                : picked;
        }

        // A subscript into a keyed collection is a key, not a position (M64), so it resolves before
        // every positional reading below.
        if (IsKeyedRead(target))
        {
            if (subscripts.Count != 1)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    "A keyed collection takes one subscript: the key.");
            }

            return JgsBuiltins.Lookup(target, Evaluate(subscripts[0], env), at.Line, at.Column);
        }

        // A selection out of a datetime is a datetime (M64). The milliseconds the read picked out are
        // already right; what a plain read loses is only that they are a time, which is the one thing
        // the value was for.
        if (target.TimeTag is JgsTimeTag time)
        {
            return JgsBuiltins.WrapTime(IndexIntoCore(target, subscripts, at, env), time);
        }

        return IndexIntoCore(target, subscripts, at, env);
    }

    /// <summary>
    /// Whether a subscript into <paramref name="target"/> is a keyed lookup rather than a position
    /// (M64). A collection's subscript is its key, which is why the two cannot both be tried.
    /// </summary>
    private static bool IsKeyedRead(JgsValue target) => JgsBuiltins.IsKeyedCollection(target);

    private JgsValue IndexIntoCore(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        if (target.Type == JgsType.Image)
        {
            return IndexImage(target, subscripts, at, env);
        }

        // S(k) picks elements out of a struct array (M65) — and out of a scalar struct, which is the
        // 1-by-1 case, so S(1) works on one the way MATLAB says it does.
        if (target.Type == JgsType.Struct)
        {
            return IndexStruct(target, subscripts, at, env);
        }

        if (subscripts.Count == 2 && target.Type == JgsType.Array)
        {
            return IndexTwoSubscripts(target, subscripts, at, env);
        }

        if (subscripts.Count > 2 && target.Type == JgsType.Array)
        {
            return IndexNSubscripts(target, subscripts, at, env);
        }

        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Indexing a {target.TypeName} takes one subscript (an index, an index array, or a mask) or two (a row and a column).");
        }

        int length = target.Type == JgsType.String ? target.AsString.Length : target.ArrayLength;
        JgsValue? index = EvaluateIndexArgument(subscripts[0], length, env);
        if (index is null)
        {
            // x(:) — every element, in storage order, as a column. MATLAB's one reliable way to
            // flatten, and for a shaped value it is a buffer clone rather than a gather.
            if (target.Type == JgsType.String)
            {
                return target;
            }

            JgsValue all = target.IsPacked ? PackedOps.Clone(target, _cancelCheck)
                : target.IsPackedComplex ? PackedOps.CloneComplex(target, _cancelCheck)
                : JgsValue.Array((JgsValue[])target.AsArray.Clone());
            if (all.ArrayLength > 1)
            {
                all.Reshape(all.ArrayLength, 1);
            }

            return all;
        }

        return GatherOrIndex(target, index, at.Line, at.Column);
    }

    /// <summary>
    /// <c>A(i, j)</c>: two scalars select an element, and any range, vector, mask or ':' in either
    /// slot selects the submatrix they name, with the shape that implies.
    /// </summary>
    private JgsValue IndexTwoSubscripts(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        int rows = JgsMatrix.RowCount(target);
        int cols = JgsMatrix.ColCount(target);
        int[] extents = [rows, cols];

        JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
        JgsValue? colIndex = EvaluateIndexArgument(subscripts[1], extents, 1, env);

        bool rowScalar = rowIndex is { Type: not JgsType.Array };
        bool colScalar = colIndex is { Type: not JgsType.Array };
        int[] rowPicks = SubscriptPicks(rowIndex, rows, "row", at);
        int[] colPicks = SubscriptPicks(colIndex, cols, "column", at);

        if (rowScalar && colScalar)
        {
            return JgsMatrix.At(target, rowPicks[0], colPicks[0]);
        }

        return JgsMatrix.BuildValues(rowPicks.Length, colPicks.Length,
            (r, c) => JgsMatrix.At(target, rowPicks[r], colPicks[c]));
    }

    /// <summary>
    /// <c>A(i, j, k, …)</c>: three or more subscripts over an array. With as many subscripts as
    /// dimensions each addresses its own; with fewer, the trailing dimensions fold into the last
    /// subscript (MATLAB's rule); subscripts past the rank must be 1. The result's shape is the
    /// per-dimension pick counts, trailing singletons trimmed.
    /// </summary>
    private JgsValue IndexNSubscripts(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        int count = subscripts.Count;
        int[] extents = SubscriptExtents(JgsMatrix.DimsOf(target), count);

        var picks = new int[count][];
        bool scalar = true;
        for (int i = 0; i < count; i++)
        {
            JgsValue? index = EvaluateIndexArgument(subscripts[i], extents, i, env);
            scalar &= index is { Type: not JgsType.Array };
            picks[i] = SubscriptPicks(index, extents[i], $"dimension-{i + 1}", at);
        }

        var strides = new int[count];
        int stride = 1;
        for (int i = 0; i < count; i++)
        {
            strides[i] = stride;
            stride *= extents[i];
        }

        if (scalar)
        {
            int slot = 0;
            for (int i = 0; i < count; i++)
            {
                slot += picks[i][0] * strides[i];
            }

            return target.ElementAt(slot);
        }

        var resultDims = new int[count];
        long total = 1;
        for (int i = 0; i < count; i++)
        {
            resultDims[i] = picks[i].Length;
            total *= picks[i].Length;
        }

        var elements = new JgsValue[total];
        var counter = new int[count]; // odometer over the result, column-major
        for (long n = 0; n < total; n++)
        {
            int slot = 0;
            for (int d = 0; d < count; d++)
            {
                slot += picks[d][counter[d]] * strides[d];
            }

            elements[n] = target.ElementAt(slot);
            for (int d = 0; d < count; d++)
            {
                if (++counter[d] < resultDims[d])
                {
                    break;
                }

                counter[d] = 0;
            }
        }

        JgsValue result = JgsMatrix.FromElements(elements, 1, elements.Length);
        result.ReshapeDims(resultDims);
        return result;
    }

    /// <summary>
    /// The extent each of <paramref name="count"/> subscripts indexes over: the matching dimension,
    /// with trailing dimensions folded into the last subscript when there are fewer subscripts than
    /// dimensions, and 1 beyond the array's rank.
    /// </summary>
    private static int[] SubscriptExtents(int[] dims, int count)
    {
        var extents = new int[count];
        for (int i = 0; i < count; i++)
        {
            extents[i] = i < dims.Length ? dims[i] : 1;
        }

        if (count < dims.Length)
        {
            long fold = 1;
            for (int i = count - 1; i < dims.Length; i++)
            {
                fold *= dims[i];
            }

            extents[count - 1] = (int)fold;
        }

        return extents;
    }

    /// <summary>
    /// <c>A(i, j, k, …) = v</c> for three or more subscripts: a scalar right-hand side fills the
    /// selection, an array must match its element count. In-range only — growing an N-D array by
    /// assignment is not supported.
    /// </summary>
    private JgsValue AssignNSubscripts(
        Expr target, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsValue callee = Evaluate(target, env);
        if (callee.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign by index into a {callee.TypeName}; only arrays support element assignment.");
        }

        if (op == TokenType.Assign && rhs.Type == JgsType.Array && rhs.ArrayLength == 0)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Deleting with three or more subscripts is not supported; delete whole rows or columns instead.");
        }

        int count = subscripts.Count;
        int[] extents = SubscriptExtents(JgsMatrix.DimsOf(callee), count);

        var picks = new int[count][];
        for (int i = 0; i < count; i++)
        {
            JgsValue? index = EvaluateIndexArgument(subscripts[i], extents, i, env);
            picks[i] = SubscriptPicks(index, extents[i], $"dimension-{i + 1}", at);
        }

        var strides = new int[count];
        int stride = 1;
        for (int i = 0; i < count; i++)
        {
            strides[i] = stride;
            stride *= extents[i];
        }

        long wanted = 1;
        foreach (int[] pick in picks)
        {
            wanted *= pick.Length;
        }

        bool scalarRhs = rhs.Type is not JgsType.Array;
        if (!scalarRhs && rhs.ArrayLength != wanted)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign {rhs.ArrayLength} values into a selection of {wanted} element(s).");
        }

        var counter = new int[count];
        for (long n = 0; n < wanted; n++)
        {
            int slot = 0;
            for (int d = 0; d < count; d++)
            {
                slot += picks[d][counter[d]] * strides[d];
            }

            JgsValue source = scalarRhs ? rhs : rhs.ElementAt((int)n);
            JgsValue stored = op == TokenType.Assign
                ? source
                : ApplyBinary(UnderlyingOp(op), callee.ElementAt(slot), source, at);
            WriteElement(callee, slot, stored);

            for (int d = 0; d < count; d++)
            {
                if (++counter[d] < picks[d].Length)
                {
                    break;
                }

                counter[d] = 0;
            }
        }

        return rhs;
    }

    /// <summary>
    /// One subscript slot resolved to 0-based positions along its dimension: null is ':', a scalar
    /// is a single position, and an array is a mask or a list of indices.
    /// </summary>
    private int[] SubscriptPicks(JgsValue? index, int extent, string dimension, Node at) => index switch
    {
        null => AllPicks(extent),
        { Type: JgsType.Array } => ComputePicks(index, extent, dimension, at.Line, at.Column),
        _ => [ToIndex(index, extent, at.Line, at.Column)],
    };

    /// <summary>
    /// Reads from an image value: <c>img(r, c)</c>, <c>img(r, c, ch)</c>, or any of those with a range,
    /// a mask or <c>:</c> in a slot — in the dialect's own index base and its own intensity scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index base and the scale were both wrong for MATLAB before M46. Subscripts ignored
    /// <see cref="Dialect"/> entirely, so a <c>.m</c> script's <c>img(1, 1)</c> quietly read the pixel
    /// diagonally in from the corner; and the sample came back in [0, 1] where MATLAB reports a
    /// <c>uint8</c> picture's pixels as 0–255. JGS keeps 0-based subscripts (ADR 0028) and its
    /// documented [0, 1] samples.
    /// </para>
    /// <para>
    /// Slicing (M46 wave L) is the third of those. Every subscript slot was required to be a single
    /// number, so <c>BW(:, 19:22)</c> on a mask that an imaging builtin had just returned was an
    /// error — while the same expression on the matrix that produced it worked. A picture and a
    /// matrix are the same thing under a subscript, so the slots now go through the ordinary
    /// <see cref="SubscriptPicks"/> path and a selection wider than one sample comes back as a
    /// matrix rather than a number.
    /// </para>
    /// </remarks>
    private JgsValue IndexImage(JgsValue callee, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        JGraph.Imaging.ImageBuffer image = callee.AsImage;

        // img(:) — every sample as a column, in MATLAB's column-major order. This is how a script
        // reduces over a whole picture (`sum(BW(:))`, `max(I(:))`), and without it the imaging
        // builtins that return a mask would be unusable in the idiom they exist to serve.
        if (subscripts.Count == 1 && EvaluateIndexArgument(subscripts[0], SampleCountOf(image), env) is null)
        {
            return FlattenImage(image);
        }

        if (subscripts.Count is not (2 or 3))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Index an image with img(row, col) for grayscale, img(row, col, channel) for colour, " +
                "or img(:) for every sample as a column.");
        }

        int[] extents = subscripts.Count == 3
            ? [image.Height, image.Width, image.Channels]
            : [image.Height, image.Width];

        int[] rows = SubscriptPicks(
            EvaluateIndexArgument(subscripts[0], extents, 0, env), image.Height, "image row", at);
        int[] cols = SubscriptPicks(
            EvaluateIndexArgument(subscripts[1], extents, 1, env), image.Width, "image column", at);
        int[] channels;
        if (subscripts.Count == 3)
        {
            channels = SubscriptPicks(
                EvaluateIndexArgument(subscripts[2], extents, 2, env), image.Channels, "image channel", at);
        }
        else if (image.Channels == 1)
        {
            channels = [0];
        }
        else
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"This image has {image.Channels} channels; read it with img(row, col, channel).");
        }

        if (rows.Length == 1 && cols.Length == 1 && channels.Length == 1)
        {
            double sample = image[rows[0], cols[0], channels[0]];
            GC.KeepAlive(image);
            return JgsValue.Number(Dialect.IsMatlab ? image.Class.ToNative(sample) : sample);
        }

        return SliceImage(image, rows, cols, channels);
    }

    /// <summary>
    /// A rectangular selection out of an image, laid out column-major as an ordinary numeric array —
    /// two-dimensional when one channel was picked, three when several were, which is the shape the
    /// rest of the imaging surface already reads.
    /// </summary>
    private JgsValue SliceImage(JGraph.Imaging.ImageBuffer image, int[] rows, int[] cols, int[] channels)
    {
        var flat = new double[(long)rows.Length * cols.Length * channels.Length];
        int k = 0;
        foreach (int ch in channels)
        {
            foreach (int c in cols)
            {
                foreach (int r in rows)
                {
                    double sample = image[r, c, ch];
                    flat[k++] = Dialect.IsMatlab ? image.Class.ToNative(sample) : sample;
                }
            }
        }

        GC.KeepAlive(image);
        int[] dims = channels.Length == 1
            ? [rows.Length, cols.Length]
            : [rows.Length, cols.Length, channels.Length];
        return JgsMatrix.FromColumnMajorDims(flat, dims);
    }

    private static int SampleCountOf(JGraph.Imaging.ImageBuffer image) =>
        (int)Math.Min(int.MaxValue, image.SampleCount);

    /// <summary>
    /// <c>img(:)</c> as a column vector. Storage here is row-major and interleaved, so the walk is
    /// written out rather than copied: MATLAB reads down each column of each colour plane in turn.
    /// </summary>
    private JgsValue FlattenImage(JGraph.Imaging.ImageBuffer image)
    {
        var flat = new double[SampleCountOf(image)];
        int k = 0;
        for (int ch = 0; ch < image.Channels; ch++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int r = 0; r < image.Height; r++)
                {
                    double sample = image[r, c, ch];
                    flat[k++] = Dialect.IsMatlab ? image.Class.ToNative(sample) : sample;
                }
            }
        }

        GC.KeepAlive(image);
        JgsValue column = NumbersOf(flat);
        if (flat.Length > 1)
        {
            column.Reshape(flat.Length, 1);
        }

        return column;
    }

    /// <summary>
    /// Evaluates a paren-index argument with <c>end</c> bound to <paramref name="targetLength"/>.
    /// Returns null for a lone ':' (select everything).
    /// </summary>
    private JgsValue? EvaluateIndexArgument(Expr argument, int targetLength, JgsEnvironment env) =>
        EvaluateIndexArgument(argument, [targetLength], 0, env);

    /// <summary>
    /// Evaluates one subscript with <c>end</c> bound to the extent of the slot it occupies.
    /// Returns null for a lone ':' (select everything along this dimension).
    /// </summary>
    private JgsValue? EvaluateIndexArgument(Expr argument, int[] extents, int slot, JgsEnvironment env)
    {
        if (argument is AllExpr)
        {
            return null;
        }

        _indexContext.Add((extents, slot));
        try
        {
            return Evaluate(argument, env);
        }
        finally
        {
            _indexContext.RemoveAt(_indexContext.Count - 1);
        }
    }

    /// <summary>
    /// Resolves <c>target[index]</c> / <c>target(index)</c> for an array or string target: a scalar
    /// number selects one element; an all-bool array is a mask (must match the target's length); an
    /// all-number array gathers by index. Both spellings are 0-based (ADR 0028). Gathering a string
    /// yields a string.
    /// </summary>
    private JgsValue GatherOrIndex(JgsValue target, JgsValue index, int line, int column)
    {
        bool isString = target.Type == JgsType.String;
        int length = isString ? target.AsString.Length : target.ArrayLength;

        if (index.Type != JgsType.Array)
        {
            int single = ToIndex(index, length, line, column);
            return isString ? JgsValue.Str(target.AsString[single].ToString()) : target.ElementAt(single);
        }

        int[] picks = ComputePicks(index, length, target.TypeName, line, column);
        if (isString)
        {
            var sb = new StringBuilder(picks.Length);
            foreach (int i in picks)
            {
                sb.Append(target.AsString[i]);
            }

            return JgsValue.Str(sb.ToString());
        }

        if (target.IsPacked)
        {
            return OrientGather(PackedOps.Gather(target, picks), target, index); // same packed kind
        }

        if (target.IsPackedComplex)
        {
            return OrientGather(PackedOps.GatherComplex(target, picks), target, index);
        }

        var gathered = new JgsValue[picks.Length];
        for (int i = 0; i < gathered.Length; i++)
        {
            gathered[i] = target.AsArray[picks[i]];
        }

        return OrientGather(JgsValue.Array(gathered), target, index);
    }

    /// <summary>
    /// MATLAB's shape rule for linear indexing. When the target and the index are both vectors the
    /// target's own orientation wins, which is what keeps <c>v(1:3)</c> looking like <c>v</c>.
    /// Otherwise the result takes the index's shape — except for a logical mask, which always
    /// gathers into a column, since the elements it picked out are scattered rather than laid out.
    /// </summary>
    private static JgsValue OrientGather(JgsValue result, JgsValue target, JgsValue index)
    {
        if (result.ArrayLength <= 1)
        {
            return result;
        }

        bool targetIsVector = target.Rows == 1 || target.Cols == 1;
        bool indexIsVector = index.Rows == 1 || index.Cols == 1;

        if (targetIsVector && indexIsVector)
        {
            if (target.Cols == 1 && target.Rows > 1)
            {
                result.Reshape(result.ArrayLength, 1);
            }

            return result;
        }

        if (!indexIsVector && !IsLogicalIndex(index))
        {
            // A numeric index matrix picks in its own column-major order, which is the order the
            // gather already produced, so the shape can simply be applied.
            result.Reshape(index.Rows, index.Cols);
            return result;
        }

        if (index.Cols == 1 || !indexIsVector)
        {
            result.Reshape(result.ArrayLength, 1);
        }

        return result;
    }

    /// <summary>Whether an index value is a logical mask rather than a list of positions.</summary>
    private static bool IsLogicalIndex(JgsValue index) => index.IsPacked
        ? index.PackedKind == JgsPackedKind.Bool
        : index.Type == JgsType.Array && index.ArrayLength > 0
          && Array.TrueForAll(index.AsArray, static v => v.Type == JgsType.Bool);

    /// <summary>Resolves an index array (a mask or a list of indices) to 0-based element positions.</summary>
    private int[] ComputePicks(JgsValue index, int length, string targetName, int line, int column)
    {
        if (index.IsPacked)
        {
            return PackedOps.PicksFromPacked(index, length, targetName, Dialect.IndexBase, line, column);
        }

        if (index.IsPackedComplex)
        {
            return PackedOps.PicksFromPackedComplex(index, length, Dialect.IndexBase, line, column);
        }

        JgsValue[] selector = index.AsArray;
        var picks = new List<int>(selector.Length);
        if (selector.Length > 0 && Array.TrueForAll(selector, v => v.Type == JgsType.Bool))
        {
            if (selector.Length != length)
            {
                throw new JgsRuntimeException(line, column,
                    $"A mask must match the {targetName} length (mask {selector.Length}, {targetName} {length}).");
            }

            for (int i = 0; i < selector.Length; i++)
            {
                if (selector[i].AsBool)
                {
                    picks.Add(i);
                }
            }
        }
        else if (Array.TrueForAll(selector, v => v.Type == JgsType.Number))
        {
            foreach (JgsValue position in selector)
            {
                picks.Add(ToIndex(position, length, line, column));
            }
        }
        else
        {
            throw new JgsRuntimeException(line, column,
                "An index array must be all numbers (indices) or all bools (a mask).");
        }

        return picks.ToArray();
    }

    // --- Numeric helpers ----------------------------------------------------------------------

    /// <summary>
    /// MATLAB's elementwise logical operators. Unlike <c>&amp;&amp;</c>/<c>||</c> they evaluate both
    /// sides and work over whole arrays, producing a mask.
    /// </summary>
    private JgsValue ElementwiseLogical(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        bool and = op == TokenType.Amp;
        if (left.Type != JgsType.Array && right.Type != JgsType.Array)
        {
            return JgsValue.Bool(and ? left.IsTruthy && right.IsTruthy : left.IsTruthy || right.IsTruthy);
        }

        // The mask has to keep the operand's shape, the way every other elementwise operator does:
        // `A > 2 | A < 0` is a 4-by-6 answer about a 4-by-6 picture, not a row of twenty-four bools.
        string symbol = and ? "&" : "|";
        return ShapeLike(
            JgsValue.Array(Broadcast(left, right,
                (a, b) => JgsValue.Bool(and ? a != 0 && b != 0 : a != 0 || b != 0),
                symbol, at.Line, at.Column)),
            left, right);
    }

    /// <summary>
    /// MATLAB's matrix <c>*</c> for two arrays. JGraph's arrays are one-dimensional — a matrix is an
    /// array of row arrays, and a vector has no row/column orientation — so the shapes that can be
    /// resolved unambiguously are matrix×matrix and matrix×vector. Anything else is refused rather than
    /// guessed at: an elementwise answer where MATLAB would give a matrix product is a wrong number, and
    /// a wrong number is worse than an error.
    /// </summary>
    private JgsValue MatrixOperation(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        if (op == TokenType.Caret)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "'^' between two arrays is not defined. Use '.^' for the elementwise power.");
        }

        if (op == TokenType.Backslash)
        {
            // A\B: the solution of A·X = B.
            double[,] coefficients = Rect(AsRows(left), at);
            double[,] rhs = Rect(AsRows(right), at);
            if (!IsMatrix(right) && coefficients.GetLength(0) != 1)
            {
                rhs = JGraph.Numerics.LinearAlgebra.Linear.Transpose(rhs); // a vector rhs is a column
            }

            return MatrixSolve(coefficients, rhs, transposeResult: false, at);
        }

        if (op == TokenType.Slash)
        {
            // A/B: the solution of X·B = A, computed as (Bᵀ \ Aᵀ)ᵀ.
            double[,] coefficients = JGraph.Numerics.LinearAlgebra.Linear.Transpose(Rect(AsRows(right), at));
            double[,] rhs = JGraph.Numerics.LinearAlgebra.Linear.Transpose(Rect(AsRows(left), at));
            return MatrixSolve(coefficients, rhs, transposeResult: true, at);
        }

        // Complex operands take the boxed complex product; the real fast path below is untouched.
        if (JgsBuiltins.HasComplexElements(left) || JgsBuiltins.HasComplexElements(right))
        {
            return JgsBuiltins.ComplexMatrixProduct(left, right, at.Line, at.Column);
        }

        double[][] a = JgsMatrix.ToRows("'*'", left, at.Line, at.Column);
        double[][] b = JgsMatrix.ToRows("'*'", right, at.Line, at.Column);
        bool leftIsVector = IsVector(left);
        bool rightIsVector = IsVector(right);

        // Two bare rows carry no orientation between them, so which product was meant is a guess.
        if (JgsMatrix.RowCount(left) == 1 && JgsMatrix.RowCount(right) == 1
            && left.ArrayLength > 1 && right.ArrayLength > 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "'*' between two row vectors is ambiguous: transpose one of them to say which product "
                + "you mean, or use '.*' for the elementwise product and dot(a, b) for the inner product.");
        }

        // A vector's orientation is often incidental — it came from a reader or a range that had
        // none to give. So the shapes as written are tried first, and only if they do not meet is a
        // vector turned the other way. A matrix is never reinterpreted: its shape is real.
        if (a[0].Length != b.Length)
        {
            if (rightIsVector)
            {
                b = TransposeRows(b);
            }
            else if (leftIsVector)
            {
                a = TransposeRows(a);
            }
        }

        int inner = a[0].Length;
        if (inner != b.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Matrix dimensions do not agree for '*': the left has {inner} columns and the right has {b.Length} rows.");
        }

        int columns = b[0].Length;
        foreach (double[] row in b)
        {
            if (row.Length != columns)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "Matrix rows must have equal lengths.");
            }
        }

        // The product runs on flat column-major buffers through the parallel kernel — the naive
        // per-element loop over jagged rows made a 1000x1000 product a minute-scale operation.
        int m = a.Length;
        var flatA = new double[(long)m * inner];
        for (int r = 0; r < m; r++)
        {
            double[] row = a[r];
            for (int k = 0; k < inner; k++)
            {
                flatA[(k * m) + r] = row[k];
            }
        }

        var flatB = new double[(long)inner * columns];
        for (int k = 0; k < inner; k++)
        {
            double[] row = b[k];
            for (int c = 0; c < columns; c++)
            {
                flatB[(c * inner) + k] = row[c];
            }
        }

        double[] product = JGraph.Numerics.LinearAlgebra.DenseProduct.ColumnMajor(flatA, m, inner, flatB, columns);
        return m == 1 && columns == 1
            ? JgsValue.Number(product[0]) // an inner product is a scalar, exactly as Build returned
            : JgsMatrix.FromColumnMajor(product, m, columns);
    }

    /// <summary>Jagged rows as a rectangular matrix, validating equal row lengths.</summary>
    private static double[,] Rect(double[][] rows, Node at)
    {
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        var rect = new double[rows.Length, width];
        for (int r = 0; r < rows.Length; r++)
        {
            if (rows[r].Length != width)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "Matrix rows must have equal lengths.");
            }

            for (int c = 0; c < width; c++)
            {
                rect[r, c] = rows[r][c];
            }
        }

        return rect;
    }

    /// <summary>Runs the dense solver, translating its shape and rank complaints into script errors.</summary>
    private JgsValue MatrixSolve(double[,] a, double[,] b, bool transposeResult, Node at)
    {
        try
        {
            double[,] x = JGraph.Numerics.LinearAlgebra.Linear.Solve(a, b);
            return MatrixResult(transposeResult ? JGraph.Numerics.LinearAlgebra.Linear.Transpose(x) : x);
        }
        catch (ArgumentException)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Matrix dimensions do not agree for the division: the right-hand side must have as many rows as the matrix.");
        }
        catch (InvalidOperationException ex)
        {
            throw new JgsRuntimeException(at.Line, at.Column, ex.Message);
        }
    }

    /// <summary>MATLAB's <c>A^p</c>: an integer matrix power (negative p inverts first).</summary>
    private JgsValue MatrixPower(JgsValue matrix, double exponent, Node at)
    {
        if (System.Math.Abs(exponent - System.Math.Round(exponent)) > 1e-12)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "'^' on a matrix supports integer exponents only. Use '.^' for the elementwise power.");
        }

        double[,] a = Rect(AsRows(matrix), at);
        if (a.GetLength(0) != a.GetLength(1))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "'^' needs a square matrix.");
        }

        try
        {
            return MatrixResult(JGraph.Numerics.LinearAlgebra.Linear.Power(a, (int)System.Math.Round(exponent)));
        }
        catch (InvalidOperationException ex)
        {
            throw new JgsRuntimeException(at.Line, at.Column, ex.Message);
        }
    }

    /// <summary>A rectangular result as a script value, carrying the shape it was computed at.</summary>
    private static JgsValue MatrixResult(double[,] matrix) =>
        JgsMatrix.Build(matrix.GetLength(0), matrix.GetLength(1), (r, c) => matrix[r, c]);

    // --- Cells and structs ----------------------------------------------------------------------

    /// <summary>
    /// Builds a cell array. Rows are flattened: JGraph's containers are one-dimensional, so a
    /// <c>{1, 2; 3, 4}</c> literal holds four elements in reading order.
    /// </summary>
    /// <summary>Builds a cell literal, rows and all.</summary>
    /// <remarks>
    /// A semicolon-rowed literal used to be flattened row by row into a single 1-by-n cell, so
    /// <c>{1, 'two'; 3, 'four'}</c> reported a size of 1-by-4 and <c>C{2,1}</c> was out of range
    /// (M47). Storage is column-major here as everywhere else, which is what makes <c>C{2}</c> the
    /// second element <em>down</em> and lets a cell iterate a column at a time.
    /// </remarks>
    private JgsValue EvaluateCellLiteral(CellLiteral literal, JgsEnvironment env)
    {
        int rows = literal.Rows.Count;

        // Each row is evaluated before its width is known, because a comma-separated list inside one
        // ({c{:}}) contributes as many entries as it names rather than the one it is written as.
        var built = new List<JgsValue[]>(rows);
        foreach (IReadOnlyList<Expr> row in literal.Rows)
        {
            built.Add(EvaluateAll(row, env));
        }

        int cols = rows == 0 ? 0 : built[0].Length;
        for (int r = 1; r < rows; r++)
        {
            if (built[r].Length != cols)
            {
                throw new JgsRuntimeException(literal.Line, literal.Column,
                    $"Every row of a cell literal needs the same number of entries; row {r + 1} has " +
                    $"{built[r].Length} where the first has {cols}.");
            }
        }

        var elements = new JgsValue[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                elements[r + (c * rows)] = built[r][c];
            }
        }

        JgsValue cell = JgsValue.Cell(elements);
        cell.Reshape(rows, cols);
        return cell;
    }

    /// <summary>Reads <c>c{i}</c> — the contents of a cell, where <c>c(i)</c> would give a cell back.</summary>
    private JgsValue EvaluateBraceIndex(BraceIndexExpr brace, JgsEnvironment env)
    {
        JgsValue target = Evaluate(brace.Target, env);
        if (target.Type == JgsType.Table)
        {
            return IndexTableBrace(target, brace.Indices, brace, env);
        }

        // s{i} on a string array is the char row inside, where s(i) is the 1-by-1 string around it
        // (M63) — the same distinction braces draw on a cell, which is why MATLAB spells it the same.
        if (target.IsStringArray)
        {
            JgsValue inside = IndexInto(target, brace.Indices, brace, env);
            return inside.IsStringArray && inside.ArrayLength == 1 ? inside.ElementAt(0) : inside;
        }

        if (target.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"Braces index a cell array, but this is a {target.TypeName}. Use parentheses to index it.");
        }

        JgsValue[] elements = target.AsCell;
        int[] slots = BraceSlots(target, brace.Indices, brace, env);

        // One value is wanted here and the subscripts must name one. A brace that names several is a
        // comma-separated list, which is a thing only an argument list, a bracket, or a multiple
        // assignment has room for — saying which of those is missing is more use than "bad index".
        if (slots.Length != 1)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"This brace index names {slots.Length} elements where one value is wanted. A list of " +
                "several only fits an argument list, a bracket, or a multiple assignment.");
        }

        return elements[slots[0]];
    }

    // --- Comma-separated lists (M61) --------------------------------------------------------------

    /// <summary>
    /// The values an expression contributes to an argument list, a bracket, or a cell literal.
    /// Almost every expression contributes exactly one; <c>c{:}</c> and a struct array's field
    /// contribute as many as they name, which is what MATLAB calls a comma-separated list.
    /// </summary>
    /// <remarks>
    /// The list is deliberately not a <see cref="JgsValue"/>: it cannot be stored in a variable, and
    /// it lives only as long as it takes the caller to spread it. That is also MATLAB's rule, and
    /// keeping it out of the value model is what stops every one of the builtins having to learn
    /// about a kind of value that is never handed to one.
    /// </remarks>
    private JgsValue[] EvaluateSpread(Expr expr, JgsEnvironment env)
    {
        if (expr is BraceIndexExpr brace)
        {
            JgsValue target = Evaluate(brace.Target, env);
            if (target.Type == JgsType.Cell)
            {
                JgsValue[] elements = target.AsCell;
                int[] slots = BraceSlots(target, brace.Indices, brace, env);
                var spread = new JgsValue[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    spread[i] = elements[slots[i]];
                }

                return spread;
            }
        }

        // A struct array's field is a list of that field across the elements. Reading the target is
        // restricted to a plain name so that asking the question cannot run a call twice; s.field is
        // the form scripts write. MATLAB dialect only: JGS has answered this with the collected row
        // since M41 and that surface is frozen.
        if (Dialect.IsMatlab && expr is MemberExpr { Target: VariableExpr name } member
            && LookUp(name.Name, env, out JgsValue array)
            && array.IsStructArray)
        {
            return StructArrayFieldValues(array, FieldName(member, env), member);
        }

        // S(2:3).field names the same list over a slice. The target is evaluated once and only when
        // the name it indexes already holds a struct, which is what keeps the restriction above —
        // asking the question must not run anything twice — while letting the commoner half of the
        // idiom through.
        // The subscript reaches here as either shape, because MATLAB spells indexing and calling the
        // same way and the parser cannot know which S is until it runs.
        if (Dialect.IsMatlab
            && expr is MemberExpr picked
            && SubscriptedName(picked.Target) is { } indexed
            && LookUp(indexed, env, out JgsValue whole)
            && whole.Type == JgsType.Struct
            && Evaluate(picked.Target, env) is { } chosen
            && chosen.IsStructArray)
        {
            return StructArrayFieldValues(chosen, FieldName(picked, env), picked);
        }

        return [Evaluate(expr, env)];
    }

    /// <summary>
    /// The plain name a subscript expression is over, or null when it is over anything else. Both
    /// shapes are checked because MATLAB spells indexing and calling alike, so which one the parser
    /// built says nothing about which one it turns out to be.
    /// </summary>
    private static string? SubscriptedName(Expr expr) => expr switch
    {
        IndexExpr { Target: VariableExpr name } => name.Name,
        CallExpr { Callee: VariableExpr callee } => callee.Name,
        _ => null,
    };

    /// <summary>
    /// Evaluates a whole argument or element list, spreading any comma-separated list inside it.
    /// </summary>
    private JgsValue[] EvaluateAll(IReadOnlyList<Expr> exprs, JgsEnvironment env)
    {
        // Nothing in the list can spread, which is the overwhelmingly common case: evaluate straight
        // into the array the caller wanted rather than through a list that would be copied out again.
        if (!MightSpread(exprs))
        {
            var plain = new JgsValue[exprs.Count];
            for (int i = 0; i < plain.Length; i++)
            {
                plain[i] = Evaluate(exprs[i], env);
            }

            return plain;
        }

        var spread = new List<JgsValue>(exprs.Count);
        foreach (Expr expr in exprs)
        {
            spread.AddRange(EvaluateSpread(expr, env));
        }

        return [.. spread];
    }

    /// <summary>
    /// Whether any expression in a list is shaped like one that could spread. This is a syntactic
    /// test, so it costs nothing to ask and is allowed to say yes to a brace that turns out to name
    /// exactly one element.
    /// </summary>
    private bool MightSpread(IReadOnlyList<Expr> exprs)
    {
        for (int i = 0; i < exprs.Count; i++)
        {
            if (exprs[i] is BraceIndexExpr
                || (Dialect.IsMatlab && exprs[i] is MemberExpr { Target: VariableExpr }))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The storage slots a brace subscript list names: one linear subscript, or a row and a column
    /// over the cell's shape (column-major, like arrays). A subscript that names several — ':', a
    /// range, an index array, or a mask — names several slots, which is a comma-separated list.
    /// </summary>
    private int[] BraceSlots(JgsValue cell, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        JgsValue[] elements = cell.AsCell;
        if (subscripts.Count == 2)
        {
            int rows = cell.Rows;
            int cols = cell.Cols;
            int[] extents = [rows, cols];
            int[] rowPicks = BracePicks(
                EvaluateIndexArgument(subscripts[0], extents, 0, env), rows, "cell row", at);
            int[] colPicks = BracePicks(
                EvaluateIndexArgument(subscripts[1], extents, 1, env), cols, "cell column", at);

            // Column-major, so a two-subscript list runs down each column in turn — the same order
            // c(:) reads in, which is what makes c{:, 1} and c{:} agree on a single-column cell.
            var grid = new int[rowPicks.Length * colPicks.Length];
            int next = 0;
            foreach (int column in colPicks)
            {
                foreach (int row in rowPicks)
                {
                    grid[next++] = row + (column * rows);
                }
            }

            return grid;
        }

        return BracePicks(
            EvaluateIndexArgument(Single(subscripts, at, "A cell index"), elements.Length, env),
            elements.Length,
            "cell",
            at);
    }

    /// <summary>
    /// The slots one brace subscript names along an extent: null is ':' (all of them), an array
    /// gathers or masks, and anything else is the single element it names.
    /// </summary>
    private int[] BracePicks(JgsValue? index, int extent, string targetName, Node at)
    {
        if (index is null)
        {
            var all = new int[extent];
            for (int i = 0; i < extent; i++)
            {
                all[i] = i;
            }

            return all;
        }

        if (index.Type == JgsType.Array)
        {
            return ComputePicks(index, extent, targetName, at.Line, at.Column);
        }

        return [ToIndex(index, extent, at.Line, at.Column)];
    }

    /// <summary>Reads <c>s.field</c> (or the dynamic <c>s.('field')</c>).</summary>
    /// <summary>Reads <c>target.field</c>.</summary>
    /// <param name="member">The dotted expression.</param>
    /// <param name="env">The scope to read in.</param>
    /// <param name="autoCall">
    /// Whether a field holding a self-calling builtin should be called on mention. True everywhere a
    /// dotted name stands for a value, and false in callee position, where the parentheses that
    /// follow are the call.
    /// </param>
    private JgsValue EvaluateMember(MemberExpr member, JgsEnvironment env, bool autoCall = true)
    {
        // `Circle.unit()` and `Circle.Sides` name the class, not an instance of it — and that has to
        // be settled before the target is evaluated, because evaluating a class name *builds* one
        // (the constructor auto-calls bare, which is what makes `c = Circle;` a default instance).
        // A variable of the same name holding data still wins, exactly as it does for a call.
        if (TryClassInFront(member, env, autoCall, out JgsValue onClass))
        {
            return onClass;
        }

        JgsValue target = Evaluate(member.Target, env);
        string field = FieldName(member, env);

        // A table's dot reads a variable's column (M43): numeric columns come back as column
        // vectors, text columns as cells so T.Code{2} braces in.
        if (target.Type == JgsType.Table)
        {
            return JgsBuiltins.TableColumnValue(target.AsTable, field, member.Line, member.Column);
        }

        // obj.something on an instance of a user class is a property or a method (M68). A method
        // comes back with the object already in its hand, which is what makes obj.area() and
        // area(obj) the same call written two ways — and what lets the bare obj.area run it.
        if (target.Type == JgsType.Object)
        {
            return ObjectMember(target, field, member, autoCall);
        }

        // A handle on a figure object is a number (M51), so a dot on one reads that object's
        // property rather than a struct field.
        if (JgsHandleRegistry.TryGet(target, out JgsHandleEntry? handle))
        {
            return JgsBuiltins.GetHandleProperty(handle, field, member.Line, member.Column);
        }

        // Class-constructor statics (M42): uint8.empty(0, 5) reads a builtin off a builtin —
        // the dot never was a struct access.
        if (target.Type == JgsType.Function && target.AsCallable is BuiltinFunction builtinTarget
            && JgsBuiltins.TryGetBuiltinStatic(builtinTarget.Name, field, out JgsValue staticMember))
        {
            return staticMember;
        }

        // S.field on an array reads that field across every element (M65). A 1-by-1 falls through to
        // the ordinary field read below, which is the same expression meaning the same thing.
        if (target.IsStructArray)
        {
            return StructArrayField(target, field, member);
        }

        if (target.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(member.Line, member.Column,
                $"'.{field}' needs a struct, but this is a {target.TypeName}.");
        }

        if (target.AsStruct.TryGetValue(field, out JgsValue? value))
        {
            // A dotted name that stands for a value rather than a function calls itself on mention,
            // exactly as a bare name does (M37's AutoCallsBare). Without this `m = containers.Map;`
            // bound the constructor, and every later mention of m called it afresh — so m('x') = 10
            // wrote into a collection nobody kept, and the writes vanished without a word.
            if (autoCall && value.Type == JgsType.Function
                && value.AsCallable is BuiltinFunction { AutoCallsBare: true } constructor)
            {
                return constructor.Call([], member.Line, member.Column);
            }

            return value;
        }

        throw new JgsRuntimeException(member.Line, member.Column, $"This struct has no field '{field}'.");
    }

    private string FieldName(MemberExpr member, JgsEnvironment env)
    {
        if (member.Field is { } literal)
        {
            return literal;
        }

        JgsValue name = Evaluate(member.FieldName!, env);
        if (name.Type != JgsType.String)
        {
            throw new JgsRuntimeException(member.Line, member.Column,
                $"A dynamic field name must be a string, but got a {name.TypeName}.");
        }

        return name.AsString;
    }

    private static Expr Single(IReadOnlyList<Expr> subscripts, Node at, string what)
    {
        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column, $"{what} takes exactly one subscript.");
        }

        return subscripts[0];
    }

    /// <summary>
    /// Writes <c>s.field = v</c>, creating the struct — and any struct on the way to it — if it does
    /// not exist yet, which is how MATLAB scripts routinely build one up field by field.
    /// </summary>
    private JgsValue AssignToMember(MemberExpr member, JgsValue value, JgsEnvironment env)
    {
        // A dotted write onto a handle sets a figure object's property (M51). This has to be asked
        // before the struct path, which would otherwise refuse the number or, worse, overwrite the
        // variable with a fresh struct.
        if (TryResolveHandleTarget(member.Target, env) is { } handle)
        {
            JgsBuiltins.SetHandleProperty(handle, FieldName(member, env), value, member.Line, member.Column);
            return value;
        }

        // A dotted write onto an instance of a user class sets a declared property, checked against
        // its declaration (M68). Asked before the struct path for the same reason the handle write is.
        if (TryAssignToObject(member, value, env))
        {
            return value;
        }

        JgsValue container = ResolveStructForWrite(member.Target, env, out JgsStructArray? owner);
        string field = FieldName(member, env);
        container.AsStruct[field] = value;

        // Every element of a struct array has every field (M65), so writing S(2).b gives element one
        // a b as well, holding []. The old cell-of-structs could not hold that invariant, which is
        // why S(5).a = 9 used to leave four elements with no fields at all.
        owner?.EnsureField(field);
        return value;
    }

    /// <summary>
    /// The figure object a dotted write is aimed at, or null when the write is an ordinary struct
    /// field. Only a bound variable and a subscript into one are considered: anywhere else the target
    /// would have to be evaluated on the chance it is a handle, and evaluating twice is not free.
    /// </summary>
    private JgsHandleEntry? TryResolveHandleTarget(Expr expr, JgsEnvironment env)
    {
        switch (expr)
        {
            case VariableExpr variable when env.TryGet(variable.Name, out JgsValue bound):
                return JgsHandleRegistry.TryGet(bound, out JgsHandleEntry? entry) ? entry : null;

            // h(i).Color = c — a handle out of an array of them. A numeric array can hold nothing
            // but handles here, so a miss is an error rather than a fall-through to the struct path.
            case CallExpr { Arguments.Count: 1, Callee: VariableExpr callee } call
                when IsHandleArray(callee, env):
                return JgsHandleRegistry.Require(Evaluate(call, env), call.Line, call.Column);
            case IndexExpr { Indices.Count: 1, Target: VariableExpr target } indexed
                when IsHandleArray(target, env):
                return JgsHandleRegistry.Require(Evaluate(indexed, env), indexed.Line, indexed.Column);

            // ax.XAxis.Color = c, and h.Annotation.LegendInformation.IconDisplayStyle = 'off' — a
            // chain of properties that each answer a handle, which is how MATLAB spells the settings
            // an object keeps on a smaller object of its own. Every step is a property read, so
            // walking the chain cannot have a side effect; a name the owner does not answer to falls
            // through to the struct path and gets its ordinary error there.
            case MemberExpr inner when TryResolveHandleTarget(inner.Target, env) is { } owner
                && JgsGraphicsProperties.TryFind(owner.Target, FieldName(inner, env), out _):
                return JgsHandleRegistry.TryGet(
                    JgsGraphicsProperties.Get(owner, FieldName(inner, env), inner.Line, inner.Column),
                    out JgsHandleEntry? nested)
                    ? nested
                    : null;

            // t.DataTipRows(1).Label = 'x' — one of a row of handles a property answered with.
            case CallExpr { Arguments.Count: 1, Callee: MemberExpr callee } nestedCall
                when TryResolveHandleTarget(callee.Target, env) is not null:
                return JgsHandleRegistry.TryGet(Evaluate(nestedCall, env), out JgsHandleEntry? one)
                    ? one
                    : null;

            default:
                return null;
        }
    }

    private static bool IsHandleArray(VariableExpr variable, JgsEnvironment env) =>
        env.TryGet(variable.Name, out JgsValue value)
        && value.Type == JgsType.Array
        && value.ArrayLength > 0
        && JgsHandleRegistry.TryGet(value.ElementAt(0), out _);

    /// <summary>
    /// The struct a dotted write lands in.
    /// </summary>
    /// <param name="expr">The write's target expression — a name, a nested dot, or a subscript.</param>
    /// <param name="env">The scope to resolve in.</param>
    /// <param name="owner">
    /// The struct array the returned element belongs to, or null when the write is into a struct
    /// that stands alone. The caller needs it to give every sibling the field being written (M65):
    /// a struct array where one element has a field the others lack is not a value the type allows.
    /// </param>
    private JgsValue ResolveStructForWrite(Expr expr, JgsEnvironment env, out JgsStructArray? owner)
    {
        owner = null;
        switch (expr)
        {
            case VariableExpr variable:
                if (env.TryGet(variable.Name, out JgsValue existing))
                {
                    if (existing.Type != JgsType.Struct)
                    {
                        throw new JgsRuntimeException(variable.Line, variable.Column,
                            $"Cannot set a field on '{variable.Name}': it is a {existing.TypeName}, not a struct.");
                    }

                    if (existing.IsStructArray)
                    {
                        throw new JgsRuntimeException(variable.Line, variable.Column,
                            $"'{variable.Name}' is a struct array, so a field write must name an element, like {variable.Name}(1).field = v.");
                    }

                    return existing;
                }

                JgsValue created = JgsValue.EmptyStruct();
                env.Declare(variable.Name, created);
                return created;

            case MemberExpr nested:
                JgsValue parent = ResolveStructForWrite(nested.Target, env, out _);
                string field = FieldName(nested, env);
                if (!parent.AsStruct.TryGetValue(field, out JgsValue? child) || child.Type != JgsType.Struct)
                {
                    child = JgsValue.EmptyStruct();
                    parent.AsStruct[field] = child;
                }

                return child;

            // S(k).field = v: one element of a struct array. The array is created or grown on sight —
            // S(100).A = [] preallocates a 100-element one — which is MATLAB's own idiom for
            // building struct arrays.
            case CallExpr { Arguments.Count: 1 } call when call.Callee is VariableExpr:
                return ResolveStructElementForWrite((VariableExpr)call.Callee, call.Arguments[0], call, env, out owner);
            case IndexExpr { Indices.Count: 1 } indexed when indexed.Target is VariableExpr:
                return ResolveStructElementForWrite((VariableExpr)indexed.Target, indexed.Indices[0], indexed, env, out owner);

            default:
                JgsValue evaluated = Evaluate(expr, env);
                if (evaluated.Type == JgsType.Struct && !evaluated.IsStructArray)
                {
                    return evaluated;
                }

                throw new JgsRuntimeException(expr.Line, expr.Column,
                    evaluated.Type == JgsType.Struct
                        ? "Cannot set one field across a whole struct array — name an element first, like S(1).field = v."
                        : $"Cannot set a field on a {evaluated.TypeName}.");
        }
    }

    /// <summary>
    /// The struct at position <paramref name="subscript"/> of the named struct array, creating the
    /// array (or growing it, or promoting an empty <c>[]</c>) as needed and replacing an empty
    /// placeholder element with a fresh struct. The element is returned by reference, so the caller's
    /// field write lands inside the array.
    /// </summary>
    private JgsValue ResolveStructElementForWrite(
        VariableExpr variable, Expr subscript, Node at, JgsEnvironment env, out JgsStructArray? owner)
    {
        bool defined = env.TryGet(variable.Name, out JgsValue existing);
        bool isStruct = defined && existing.Type == JgsType.Struct;

        // `end` inside the subscript counts the elements already there, so S(end+1).f = v appends —
        // the accumulation idiom that used to be refused because nothing told `end` what it was in.
        JgsValue index = EvaluateIndexArgument(
            subscript, isStruct ? existing.AsStructArray.Length : 0, env)
            ?? throw new JgsRuntimeException(at.Line, at.Column,
                "A struct-array element is named by one whole-number subscript, not ':'.");

        if (index.Type != JgsType.Number || index.AsNumber != System.Math.Floor(index.AsNumber))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "A struct-array element is named by one whole-number subscript.");
        }

        int slot = (int)index.AsNumber - Dialect.IndexBase;
        if (slot < 0)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Index {(int)index.AsNumber} is out of range (indexing is {Dialect.IndexBase}-based).");
        }

        if (!defined || (existing.Type == JgsType.Array && existing.ArrayLength == 0))
        {
            existing = JgsValue.StructArray(new JgsStructArray([]), 0, 0);
            isStruct = true;
        }
        else if (!isStruct)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot index into '{variable.Name}' with a subscript and a field: it is a {existing.TypeName}.");
        }

        JgsStructArray payload = existing.AsStructArray;
        JgsValue array = existing;
        if (slot >= payload.Length)
        {
            // Growing fills the gap with elements carrying the array's fields, each holding [] —
            // MATLAB's rule that every element has every field, applied at the moment the gap appears.
            var grown = new Dictionary<string, JgsValue>[slot + 1];
            System.Array.Copy(payload.Elements, grown, payload.Length);

            // The fields come from what is already there, read before the gap exists: a half-filled
            // array has no element zero to ask.
            string[] fields = payload.FieldNames;
            for (int i = payload.Length; i < grown.Length; i++)
            {
                var filler = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (string field in fields)
                {
                    filler[field] = JgsValue.Array([]);
                }

                grown[i] = filler;
            }

            payload = new JgsStructArray(grown, fields);

            // A column grows down its column; everything else grows along a row.
            bool column = existing.Cols == 1 && existing.Rows > 1;
            array = JgsValue.StructArray(payload,
                column ? grown.Length : 1, column ? 1 : grown.Length);
            array.SetClassName(existing.ClassName);
            if (!env.TryAssign(variable.Name, array))
            {
                env.Declare(variable.Name, array);
            }
        }

        owner = payload;

        // A JgsValue over the element's own dictionary: the caller's field write lands in the array
        // because both hold the same dictionary reference.
        return JgsValue.Struct(payload.Elements[slot]);
    }

    /// <summary>
    /// Writes <c>c{i} = v</c>. Assigning past the end grows the cell (filling the gap with empty
    /// arrays), which is what makes MATLAB's <c>c{end+1} = x</c> accumulation idiom work.
    /// </summary>
    private JgsValue AssignToBraceIndex(BraceIndexExpr brace, JgsValue value, JgsEnvironment env)
    {
        JgsValue target;
        VariableExpr? variable = brace.Target as VariableExpr;
        if (variable is null)
        {
            // A dot-chain target (s.a.b{r, c} = v, M43): the chain's cell is written in place —
            // member reads hand back the stored reference, so the struct sees the write. Growth
            // still needs a rebindable name, so out-of-range writes stay the named form's.
            target = Evaluate(brace.Target, env);
            if (target.Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(brace.Line, brace.Column,
                    $"Braces assign into a cell array, but this is a {target.TypeName}.");
            }
        }
        else if (!env.TryGet(variable.Name, out target))
        {
            target = JgsValue.Cell(System.Array.Empty<JgsValue>());
            env.Declare(variable.Name, target);
        }

        if (target.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"Braces assign into a cell array, but '{variable?.Name ?? "this"}' is a {target.TypeName}.");
        }

        JgsValue[] elements = target.AsCell;

        // C{r, c} writes through the cell's shape, in range only — growth is the linear form's.
        if (brace.Indices.Count == 2)
        {
            int[] slots = BraceSlots(target, brace.Indices, brace, env);
            if (slots.Length != 1)
            {
                throw new JgsRuntimeException(brace.Line, brace.Column,
                    $"This brace index names {slots.Length} elements, and a brace assignment writes one. " +
                    "Assign to a paren index to write several at once.");
            }

            elements[slots[0]] = value;
            return value;
        }

        JgsValue? index = EvaluateIndexArgument(
            Single(brace.Indices, brace, "A cell index"), elements.Length, env);
        if (index is null || index.Type != JgsType.Number)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column, "A cell index must be a single number.");
        }

        int position = (int)index.AsNumber - Dialect.IndexBase;
        if (position < 0)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"Index {(int)index.AsNumber} is out of range (indexing is {Dialect.IndexBase}-based).");
        }

        if (position >= elements.Length)
        {
            if (variable is null)
            {
                throw new JgsRuntimeException(brace.Line, brace.Column,
                    "A cell reached through a field cannot grow by brace assignment; assign the field a larger cell first.");
            }

            var grown = new JgsValue[position + 1];
            System.Array.Copy(elements, grown, elements.Length);
            for (int i = elements.Length; i < grown.Length; i++)
            {
                grown[i] = JgsValue.Array(System.Array.Empty<JgsValue>());
            }

            grown[position] = value;
            env.TryAssign(variable.Name, JgsValue.Cell(grown));
            return value;
        }

        elements[position] = value;
        return value;
    }

    /// <summary>
    /// MATLAB's transpose: an r-by-c value becomes c-by-r, with <c>'</c> also conjugating. Now that
    /// arrays carry a shape (ADR 0043) this is finally true of vectors as well — <c>(0:0.1:1)'</c> is
    /// a real column, where it used to hand the same row back — so a transposed vector can be
    /// concatenated into a matrix and reports its own size. A scalar is its own transpose.
    /// </summary>
    private JgsValue EvaluateTranspose(TransposeExpr transpose, JgsEnvironment env)
    {
        JgsValue value = Evaluate(transpose.Operand, env);
        if (value.Type == JgsType.Complex)
        {
            return transpose.Conjugate ? JgsValue.ComplexNum(Complex.Conjugate(value.AsComplex)) : value;
        }

        // A transposed sparse matrix is a sparse matrix. It used to come back dense, which meant the
        // storage a script chose on purpose was silently discarded by a single quote.
        if (value.Type == JgsType.Sparse)
        {
            return JgsValue.Sparse(value.AsSparse.Transpose());
        }

        if (value.Type != JgsType.Array)
        {
            return value;
        }

        int rows = JgsMatrix.RowCount(value);
        int columns = JgsMatrix.ColCount(value);
        var transposed = new JgsValue[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            // Element (r, c) of the source lands at (c, r) of the result, whose column-major
            // position is c + r*columns — so the source row walks the result's storage in order.
            int origin = r * columns;
            for (int c = 0; c < columns; c++)
            {
                JgsValue element = JgsMatrix.At(value, r, c);
                transposed[origin + c] = transpose.Conjugate && element.Type == JgsType.Complex
                    ? JgsValue.ComplexNum(Complex.Conjugate(element.AsComplex))
                    : element;
            }
        }

        // A transposed value is the same kind of thing stood on its side: uint8 stays uint8, a string
        // array stays a string array, and a duration stays a duration. Transpose rebuilt the wrapper
        // and carried none of that until M64 found it — `timetable(seconds(1:3)', …)` stored raw
        // milliseconds, because the tag came off at the apostrophe rather than anywhere near the call.
        return CarryValueTags(value, ShapedFrom(transposed, columns, rows));
    }

    /// <summary>Packs freshly built elements if they are homogeneous, then applies the shape.</summary>
    private static JgsValue ShapedFrom(JgsValue[] elements, int rows, int cols)
    {
        if (JgsPacking.Enabled && PackedOps.TryPackElements(elements, out JgsValue packed))
        {
            packed.Reshape(rows, cols);
            return packed;
        }

        return JgsValue.Shaped(elements, rows, cols);
    }

    /// <summary>A copy of an array with every complex element conjugated.</summary>
    private JgsValue Conjugated(JgsValue value)
    {
        if (value.IsPacked)
        {
            return CopyContainer(value); // real numbers are their own conjugates
        }

        if (value.IsPackedComplex)
        {
            JgsPackedComplex planes = value.AsPackedComplex;
            NumericBuffer re = JgsPacking.Allocate(planes.Length);
            NumericBuffer im = JgsPacking.Allocate(planes.Length);
            planes.Re.AsSpan().CopyTo(re.AsSpan());
            Span<double> source = planes.Im.AsSpan();
            Span<double> target = im.AsSpan();
            for (int i = 0; i < source.Length; i++)
            {
                target[i] = -source[i];
            }

            GC.KeepAlive(planes);
            return KeepShape(value, JgsValue.PackedComplexArray(new JgsPackedComplex(re, im)));
        }

        JgsValue[] source2 = value.AsArray;
        var conjugated = new JgsValue[source2.Length];
        for (int i = 0; i < conjugated.Length; i++)
        {
            conjugated[i] = source2[i].Type == JgsType.Complex
                ? JgsValue.ComplexNum(Complex.Conjugate(source2[i].AsComplex))
                : source2[i];
        }

        return KeepShape(value, JgsValue.Array(conjugated));
    }

    /// <summary>Whether a value is a matrix — see <see cref="JgsMatrix"/> for what that means now.</summary>
    private static bool IsMatrix(JgsValue value) => JgsMatrix.IsMatrix(value);

    /// <summary>Whether an array has a singleton dimension, so its orientation could be flipped.</summary>
    private static bool IsVector(JgsValue value) =>
        JgsMatrix.RowCount(value) == 1 || JgsMatrix.ColCount(value) == 1;

    private static double[][] TransposeRows(double[][] rows)
    {
        int height = rows.Length;
        int width = height == 0 ? 0 : rows[0].Length;
        var transposed = new double[width][];
        for (int c = 0; c < width; c++)
        {
            transposed[c] = new double[height];
            for (int r = 0; r < height; r++)
            {
                transposed[c][r] = rows[r][c];
            }
        }

        return transposed;
    }

    /// <summary>A numeric array or matrix as rows of doubles; a vector becomes a single row.</summary>
    private double[][] AsRows(JgsValue value) =>
        IsMatrix(value) ? JgsMatrix.ToRows("'*'", value, 0, 0) : [RowOf(value)];

    private double[] RowOf(JgsValue value)
    {
        int length = value.ArrayLength;
        var row = new double[length];
        for (int i = 0; i < length; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type is not (JgsType.Number or JgsType.Bool))
            {
                throw new JgsRuntimeException(0, 0, $"'*' needs numbers, but an element was a {element.TypeName}.");
            }

            row[i] = element.AsNumber;
        }

        return row;
    }

    /// <summary>Wraps a freshly built double[] as a numeric array value (adopted, not copied).</summary>
    private static JgsValue NumbersOf(double[] values)
    {
        if (JgsPacking.Enabled)
        {
            return JgsValue.Packed(ManagedBuffer.Adopt(values));
        }

        var boxed = new JgsValue[values.Length];
        for (int i = 0; i < boxed.Length; i++)
        {
            boxed[i] = JgsValue.Number(values[i]);
        }

        return JgsValue.Array(boxed);
    }

    private JgsValue Compare(JgsValue left, JgsValue right, TokenType opToken, Node at, Func<double, double, bool> op)
    {
        // MATLAB orders complex numbers by their real parts alone, and discards the imaginary ones
        // without comment. That is a strange rule — 1+9i and 1-9i compare equal under it — but it is
        // the rule, and throwing instead meant a ported script stopped at a line MATLAB runs. The
        // orderings that do use the whole number are sort, max and min, which go by magnitude.
        if (left.Type == JgsType.Complex || right.Type == JgsType.Complex)
        {
            (left, right) = (RealPartOf(left), RealPartOf(right));
        }

        if (IsNumericScalar(left) && IsNumericScalar(right))
        {
            return JgsValue.Bool(op(left.AsNumber, right.AsNumber));
        }

        // Element-wise over arrays with scalar broadcasting, producing an array of bools (a mask).
        if (left.Type == JgsType.Array || right.Type == JgsType.Array)
        {
            string symbol = TokenText(opToken);
            return ShapeLike(
                JgsValue.Array(Broadcast(
                    left, right, (a, b) => JgsValue.Bool(op(a, b)), symbol, at.Line, at.Column, byRealPart: true)),
                left, right);
        }

        throw new JgsRuntimeException(at.Line, at.Column,
            $"Operator '{TokenText(opToken)}' needs two numbers, but got {left.TypeName} and {right.TypeName}.");
    }

    /// <summary>
    /// A value with its imaginary parts dropped, for the relational operators. An array is walked so
    /// that a mixed array — real numbers beside complex ones — compares element by element rather
    /// than refusing on the first complex entry it meets.
    /// </summary>
    private static JgsValue RealPartOf(JgsValue value)
    {
        if (value.Type == JgsType.Complex)
        {
            return JgsValue.Number(value.AsComplex.Real);
        }

        if (value.Type != JgsType.Array)
        {
            return value;
        }

        JgsValue[] source = value.BoxedElements();
        var parts = new JgsValue[source.Length];
        bool anyComplex = false;
        for (int i = 0; i < source.Length; i++)
        {
            anyComplex |= source[i].Type is JgsType.Complex or JgsType.Array;
            parts[i] = RealPartOf(source[i]);
        }

        if (!anyComplex)
        {
            return value;
        }

        var stripped = JgsValue.Array(parts);
        stripped.TakeShapeOf(value);
        return stripped;
    }

    /// <summary>
    /// Evaluates <c>==</c>/<c>!=</c>: element-wise (broadcasting a scalar) when either side is an
    /// array — so <c>ids == "ABC"</c> yields a mask — and a single bool otherwise. Mismatched element
    /// types compare unequal rather than throwing. Use <c>isequal</c> for whole-value equality.
    /// </summary>
    private static JgsValue Equality(JgsValue left, JgsValue right, bool negate, Node at)
    {
        if (left.Type != JgsType.Array && right.Type != JgsType.Array)
        {
            return JgsValue.Bool(AreEqual(left, right) != negate);
        }

        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            JgsValue[] a = left.BoxedElements();
            JgsValue[] b = right.BoxedElements();
            if (a.Length != b.Length)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Cannot apply '{(negate ? "!=" : "==")}' to arrays of different lengths ({a.Length} and {b.Length}).");
            }

            var pairwise = new JgsValue[a.Length];
            for (int i = 0; i < pairwise.Length; i++)
            {
                pairwise[i] = JgsValue.Bool(AreEqual(a[i], b[i]) != negate);
            }

            return ShapeLike(JgsValue.Array(pairwise), left, right);
        }

        // One side is a scalar; broadcast it across the array.
        JgsValue[] array = (left.Type == JgsType.Array ? left : right).BoxedElements();
        JgsValue scalar = left.Type == JgsType.Array ? right : left;
        var result = new JgsValue[array.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = JgsValue.Bool(AreEqual(array[i], scalar) != negate);
        }

        return ShapeLike(JgsValue.Array(result), left, right);
    }

    /// <summary>The boxed twin of <c>PackedOps.KeepShape</c>: an elementwise result is the same
    /// shape as the operand it was computed over.</summary>
    private static JgsValue ShapeLike(JgsValue result, JgsValue left, JgsValue right)
    {
        JgsValue model = left.Type == JgsType.Array && left.IsShaped ? left
            : right.Type == JgsType.Array && right.IsShaped ? right
            : JgsValue.Null;
        if (model.Type == JgsType.Array && model.ArrayLength == result.ArrayLength)
        {
            result.Reshape(model.Rows, model.Cols);
        }

        return result;
    }

    private static bool AreEqual(JgsValue left, JgsValue right) => JgsValue.AreEqual(left, right);

    private JgsValue NumericBinary(JgsValue left, JgsValue right, Func<double, double, double> op, string symbol, int line, int column, Func<Complex, Complex, Complex>? complexOp = null)
    {
        // A picture is a matrix of readings, and MATLAB has no other kind: 1 - I after a mat2gray is
        // ordinary arithmetic there and refused here until M72, so chaining an image-processing
        // result into plain maths needed a cast MATLAB never asks for. Reading it as numbers at the
        // operator rather than at every verb is what makes the whole family compose. A picture too
        // large to box declines and the refusal below still names the types, which is the honest
        // answer for an expression that would otherwise allocate a hundred million boxes.
        if (left.Type == JgsType.Image && JgsBuiltins.TryNumbersOf(left, Dialect, out JgsValue leftNumbers))
        {
            left = leftNumbers;
        }

        if (right.Type == JgsType.Image && JgsBuiltins.TryNumbersOf(right, Dialect, out JgsValue rightNumbers))
        {
            right = rightNumbers;
        }

        if (IsNumericScalar(left) && IsNumericScalar(right))
        {
            return JgsValue.Number(op(left.AsNumber, right.AsNumber));
        }

        // Either side complex (and neither an array): promote and apply the complex form.
        if (IsComplexOrNumeric(left) && IsComplexOrNumeric(right))
        {
            return JgsValue.ComplexNum(RequireComplexOp(complexOp, symbol, line, column)(left.AsComplex, right.AsComplex));
        }

        if (left.Type == JgsType.Array || right.Type == JgsType.Array)
        {
            return ShapeLike(
                JgsValue.Array(Broadcast(left, right, (a, b) => JgsValue.Number(op(a, b)), symbol, line, column, complexOp)),
                left, right);
        }

        throw new JgsRuntimeException(line, column,
            $"Operator '{symbol}' needs numbers or numeric arrays, but got {left.TypeName} and {right.TypeName}.");
    }

    private static bool IsComplexOrNumeric(JgsValue value) =>
        value.Type == JgsType.Complex || IsNumericScalar(value);

    private static Func<Complex, Complex, Complex> RequireComplexOp(Func<Complex, Complex, Complex>? complexOp, string symbol, int line, int column) =>
        complexOp ?? throw new JgsRuntimeException(line, column,
            $"Operator '{symbol}' is not defined for complex numbers.");

    /// <summary>
    /// Applies <paramref name="combine"/> pairwise over two arrays (equal lengths required) or an
    /// array and a scalar (broadcast). Elements must be numbers or bools (which read as 0/1) — or
    /// complex, when the operator supplies a <paramref name="complexOp"/>.
    /// </summary>
    private static JgsValue[] Broadcast(JgsValue left, JgsValue right, Func<double, double, JgsValue> combine, string symbol, int line, int column, Func<Complex, Complex, Complex>? complexOp = null, bool byRealPart = false)
    {
        // Nested arrays recurse, so matrices (arrays of row arrays) broadcast elementwise too:
        // M + M pairs rows, M + scalar spreads the scalar across every row.
        JgsValue Element(JgsValue a, JgsValue b) =>
            a.Type == JgsType.Array || b.Type == JgsType.Array
                ? JgsValue.Array(Broadcast(a, b, combine, symbol, line, column, complexOp, byRealPart))

                // The relational operators order complex numbers by their real parts, which is what
                // byRealPart says: a complex element here is not an error but a number with an
                // imaginary part that this particular operator has no use for.
                : byRealPart && (a.Type == JgsType.Complex || b.Type == JgsType.Complex)
                    ? combine(
                        RequireComplex(a, symbol, line, column).Real,
                        RequireComplex(b, symbol, line, column).Real)
                : a.Type == JgsType.Complex || b.Type == JgsType.Complex
                    ? JgsValue.ComplexNum(RequireComplexOp(complexOp, symbol, line, column)(
                        RequireComplex(a, symbol, line, column), RequireComplex(b, symbol, line, column)))
                    : combine(RequireNumber(a, symbol, line, column), RequireNumber(b, symbol, line, column));

        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            JgsValue[] a = left.BoxedElements();
            JgsValue[] b = right.BoxedElements();
            if (a.Length != b.Length)
            {
                throw new JgsRuntimeException(line, column,
                    $"Cannot apply '{symbol}' to arrays of different lengths ({a.Length} and {b.Length}).");
            }

            var result = new JgsValue[a.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Element(a[i], b[i]);
            }

            return result;
        }

        // One side is a scalar; broadcast it across the array.
        bool arrayOnLeft = left.Type == JgsType.Array;
        JgsValue[] array = (arrayOnLeft ? left : right).BoxedElements();
        JgsValue scalar = arrayOnLeft ? right : left;
        var broadcast = new JgsValue[array.Length];
        for (int i = 0; i < broadcast.Length; i++)
        {
            broadcast[i] = arrayOnLeft ? Element(array[i], scalar) : Element(scalar, array[i]);
        }

        return broadcast;
    }

    private JgsValue MapNumeric(JgsValue value, Func<double, double> op, string symbol, int line, int column, Func<Complex, Complex>? complexOp = null)
    {
        if (IsNumericScalar(value))
        {
            return JgsValue.Number(op(value.AsNumber));
        }

        if (value.Type == JgsType.Complex && complexOp is not null)
        {
            return JgsValue.ComplexNum(complexOp(value.AsComplex));
        }

        if (value.Type == JgsType.Array)
        {
            if (value.IsPacked)
            {
                // The same scalar delegate runs over the flat buffer — bit-identical results with
                // no per-element boxing (bools read as 0/1, and the result kind is Number, exactly
                // as the boxed branch produces).
                NumericBuffer dest = JgsPacking.Allocate(value.ArrayLength);
                PackedMath.Map(value.AsBuffer, dest, new Func<double, double>(op), _cancelCheck);
                return KeepShape(value, JgsValue.Packed(dest));
            }

            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < result.Length; i++)
            {
                // Recurse so nested arrays map elementwise as well.
                result[i] = MapNumeric(source[i], op, symbol, line, column, complexOp);
            }

            return KeepShape(value, JgsValue.Array(result));
        }

        throw new JgsRuntimeException(line, column, $"Operator '{symbol}' needs a number or numeric array, but got {value.TypeName}.");
    }

    /// <summary>Whether the value reads as a number in arithmetic: a number, or a bool (0/1).</summary>
    private static bool IsNumericScalar(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool;

    private static double RequireNumber(JgsValue value, string symbol, int line, int column)
    {
        if (!IsNumericScalar(value))
        {
            throw new JgsRuntimeException(line, column, $"Operator '{symbol}' needs numbers, but an array element was a {value.TypeName}.");
        }

        return value.AsNumber;
    }

    private static Complex RequireComplex(JgsValue value, string symbol, int line, int column)
    {
        if (!IsComplexOrNumeric(value))
        {
            throw new JgsRuntimeException(line, column, $"Operator '{symbol}' needs numbers, but an array element was a {value.TypeName}.");
        }

        return value.AsComplex;
    }

    /// <summary>
    /// An index value as a 0-based element position, counted from the dialect's base — 0 in JGS
    /// (ADR 0028), 1 in MATLAB. Both spellings, <c>a[i]</c> and <c>a(i)</c>, share it.
    /// </summary>
    private int ToIndex(JgsValue index, int length, int line, int column)
    {
        if (index.Type != JgsType.Number)
        {
            throw new JgsRuntimeException(line, column, $"An index must be a number, but got a {index.TypeName}.");
        }

        return PackedOps.ToIndex(index.AsNumber, length, Dialect.IndexBase, line, column);
    }

    private void Tick()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (++_steps > MaxSteps)
        {
            throw new JgsRuntimeException(0, 0, "Step limit exceeded (the script ran too long — check for an infinite loop).");
        }
    }

    private static string TokenText(TokenType type) => type switch
    {
        TokenType.Less => "<",
        TokenType.LessEqual => "<=",
        TokenType.Greater => ">",
        TokenType.GreaterEqual => ">=",
        _ => type.ToString(),
    };

    /// <summary>The user-facing symbol for a binary operator (matches the boxed paths' messages).</summary>
    private static string OperatorSymbol(TokenType type) => type switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.Slash => "/",
        TokenType.Percent => "%",
        TokenType.Caret => "^",
        _ => TokenText(type),
    };

    private readonly struct Completion
    {
        public static readonly Completion Normal = new(CompletionKind.Normal, JgsValue.Null, 0, 0);

        private Completion(CompletionKind kind, JgsValue value, int line, int column)
        {
            Kind = kind;
            Value = value;
            Line = line;
            Column = column;
        }

        public CompletionKind Kind { get; }

        public JgsValue Value { get; }

        public int Line { get; }

        public int Column { get; }

        public static Completion MakeReturn(JgsValue value) => new(CompletionKind.Return, value, 0, 0);

        public static Completion MakeBreak(int line, int column) => new(CompletionKind.Break, JgsValue.Null, line, column);

        public static Completion MakeContinue(int line, int column) => new(CompletionKind.Continue, JgsValue.Null, line, column);
    }
}
