using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A tree-walking evaluator for the JGS language. It runs a parsed program against a global environment
/// (seeded with the built-ins), supporting variables, arrays, closures, control flow, and element-wise
/// numeric operators. Runaway scripts are bounded three ways: a per-statement step budget, a call-depth
/// limit, and a cooperative cancellation check — so even a tight <c>while true {}</c> loop is interruptible.
/// </summary>
internal sealed class Interpreter
{
    private const long MaxSteps = 50_000_000;
    private const int MaxCallDepth = 250;

    private readonly JgsEnvironment _globals;
    private readonly CancellationToken _cancellationToken;
    private readonly IJgsDebugHook? _hook;
    private readonly Action<string>? _echo;
    private readonly List<int> _indexTargetLengths = new(); // 'end' resolves to the top entry

    /// <summary>
    /// Names a MATLAB <c>global</c> declaration has bound to the global scope. Reads and writes of
    /// these names go straight to the globals wherever they appear, which is how a function and the
    /// script that calls it share one variable. (MATLAB scopes the declaration per function; treating
    /// it as run-wide differs only for a script that also uses the name as an ordinary local.)
    /// </summary>
    private readonly HashSet<string> _globalNames = new(StringComparer.Ordinal);

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
        _cancellationToken = cancellationToken;
        _hook = hook;
        _echo = echo;
        Dialect = dialect ?? JgsDialect.Jgs;

        // Packed operations run in ~4M-element chunks and poll this between chunks, so Stop
        // interrupts a 100M-element elementwise statement mid-flight instead of after it.
        _cancelCheck = () => _cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>The language variant this run speaks; every JGS/MATLAB difference reads from it.</summary>
    public JgsDialect Dialect { get; }

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

    /// <summary>Runs a user function's body in <paramref name="local"/> and returns its result (or null).</summary>
    /// <param name="declaration">The function being invoked (carries its name, body, and source).</param>
    /// <param name="local">The call's local environment with parameters already bound.</param>
    /// <param name="callLine">The 1-based line of the call site, for the debugger's call stack.</param>
    public JgsValue ExecuteFunctionBody(FnStmt declaration, JgsEnvironment local, int callLine)
    {
        if (++_callDepth > MaxCallDepth)
        {
            _callDepth--;
            throw new JgsRuntimeException(0, 0, "Maximum call depth exceeded (possible infinite recursion).");
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
        finally
        {
            _callDepth--;
            _hook?.ExitFunction();
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
                foreach (string name in globalStmt.Names)
                {
                    _globalNames.Add(name);
                    if (!_globals.Contains(name))
                    {
                        _globals.Declare(name, JgsValue.Array(System.Array.Empty<JgsValue>()));
                    }
                }

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
                BindAns(statement, called);
                return;
            }

            EchoBinding(statement, name.Name, existing);
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
            default:
                BindAns(statement, value);
                break;
        }
    }

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

    /// <summary>Binds a bare expression's non-null result to <c>ans</c> and echoes it when unsuppressed.</summary>
    private void BindAns(Stmt statement, JgsValue value)
    {
        if (value.Type == JgsType.Null)
        {
            return; // verbs like title(...) return nothing — no ans, no echo
        }

        _globals.Declare("ans", value);
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
        if (_echo is not null && !statement.Suppressed)
        {
            _echo($"{name} = {EchoDisplay(value)}");
        }
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
        if (_globalNames.Contains(name) && _globals.TryGet(name, out value))
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
            JgsEnvironment handler = BlockScope(env);
            if (statement.ErrorVariable is { } name)
            {
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["message"] = JgsValue.Str(error.Message),
                    ["identifier"] = JgsValue.Str(string.Empty),
                };
                handler.Declare(name, JgsValue.Struct(fields));
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
        JgsValue[] outputs = EvaluateForOutputs(statement.Call, statement.Targets.Count, env);
        for (int i = 0; i < statement.Targets.Count; i++)
        {
            if (statement.Targets[i] is not { } target)
            {
                continue; // '~': the output was computed, and is deliberately dropped
            }

            if (i >= outputs.Length)
            {
                throw new JgsRuntimeException(statement.Line, statement.Column,
                    $"This call returns {outputs.Length} value(s), but {statement.Targets.Count} were asked for.");
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
    /// Evaluates a call that is expected to produce <paramref name="wanted"/> outputs. User functions
    /// hand back their named outputs; a builtin that knows how to produce several does so; anything
    /// else produces its single value.
    /// </summary>
    private JgsValue[] EvaluateForOutputs(Expr call, int wanted, JgsEnvironment env)
    {
        if (call is CallExpr invocation && Evaluate(invocation.Callee, env) is { Type: JgsType.Function } callee)
        {
            var arguments = new JgsValue[invocation.Arguments.Count];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = Evaluate(invocation.Arguments[i], env);
            }

            if (callee.AsCallable is IJgsMultiCallable multi)
            {
                return multi.CallMultiple(arguments, wanted, invocation.Line, invocation.Column);
            }

            return [callee.AsCallable.Call(arguments, invocation.Line, invocation.Column)];
        }

        return [Evaluate(call, env)];
    }

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
        if (iterable.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(statement.Line, statement.Column,
                $"'for' can only iterate over an array, but got a {iterable.TypeName}.");
        }

        // Indexed iteration serves boxed and packed alike (a packed iterable materializes one
        // element per pass, exactly what the boxed foreach allocated anyway).
        int iterationCount = iterable.ArrayLength;
        for (int index = 0; index < iterationCount; index++)
        {
            JgsValue element = CopyForBinding(iterable.ElementAt(index));
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

            case StringLiteral str:
                return JgsValue.Str(str.Value);

            case BoolLiteral boolean:
                return JgsValue.Bool(boolean.Value);

            case ArrayLiteral array:
                var elements = new JgsValue[array.Elements.Count];
                for (int i = 0; i < elements.Length; i++)
                {
                    elements[i] = Evaluate(array.Elements[i], env);
                }

                if (JgsPacking.Enabled && PackedOps.TryPackElements(elements, out JgsValue packedLiteral))
                {
                    return packedLiteral;
                }

                return JgsValue.Array(elements);

            case MatrixLiteral matrix:
                return EvaluateMatrix(matrix, env);

            case RangeExpr range:
                return EvaluateRange(range, env);

            case EndExpr:
                if (_indexTargetLengths.Count == 0)
                {
                    throw new JgsRuntimeException(expression.Line, expression.Column,
                        "'end' is only valid inside an index expression, like x(end).");
                }

                // The stack holds target *lengths*; 'end' is the last valid *index*, which in a
                // 0-based dialect is one less and in a 1-based one is the length itself.
                return JgsValue.Number(_indexTargetLengths[^1] - 1 + Dialect.IndexBase);

            case AllExpr:
                throw new JgsRuntimeException(expression.Line, expression.Column,
                    "':' by itself is only valid as an index argument, like x(:).");

            case VariableExpr variable:
                if (LookUp(variable.Name, env, out JgsValue value))
                {
                    return value;
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
    /// Evaluates a semicolon-rowed literal: all-scalar rows build a matrix (nested row arrays, equal
    /// lengths required); rows containing arrays vertically concatenate into one flat array
    /// (<c>[a; zeros(k, 1)]</c>).
    /// </summary>
    private JgsValue EvaluateMatrix(MatrixLiteral matrix, JgsEnvironment env)
    {
        var rows = new List<JgsValue[]>(matrix.Rows.Count);
        bool concatenate = false;
        foreach (IReadOnlyList<Expr> row in matrix.Rows)
        {
            var evaluated = new JgsValue[row.Count];
            for (int i = 0; i < evaluated.Length; i++)
            {
                evaluated[i] = Evaluate(row[i], env);
                concatenate |= evaluated[i].Type == JgsType.Array;
            }

            rows.Add(evaluated);
        }

        if (concatenate)
        {
            // All-number concatenations ([x_pad; zeros(k, 1)]) build one packed buffer with bulk
            // span copies; anything mixed falls back to the boxed flatten.
            if (JgsPacking.Enabled && PackedOps.TryFlattenNumeric(rows, _cancelCheck, out JgsValue flattened))
            {
                return flattened;
            }

            var flat = new List<JgsValue>();
            foreach (JgsValue[] row in rows)
            {
                foreach (JgsValue value in row)
                {
                    FlattenInto(value, flat);
                }
            }

            return JgsValue.Array(flat.ToArray());
        }

        int width = rows[0].Length;
        for (int r = 1; r < rows.Count; r++)
        {
            if (rows[r].Length != width)
            {
                throw new JgsRuntimeException(matrix.Line, matrix.Column,
                    $"Matrix rows must have equal lengths (row 1 has {width}, row {r + 1} has {rows[r].Length}).");
            }
        }

        var result = new JgsValue[rows.Count];
        for (int r = 0; r < result.Length; r++)
        {
            // All-number rows pack individually; the outer array of rows stays boxed.
            result[r] = JgsPacking.Enabled && PackedOps.TryPackElements(rows[r], out JgsValue packedRow)
                && packedRow.PackedKind == JgsPackedKind.Number
                ? packedRow
                : JgsValue.Array(rows[r]);
        }

        return JgsValue.Array(result);
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
        if (unary.Op == TokenType.Bang)
        {
            return JgsValue.Bool(!operand.IsTruthy);
        }

        // Minus: numeric negation, element-wise over arrays (complex included).
        return MapNumeric(operand, v => -v, "-", unary.Line, unary.Column, static c => -c);
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
    private JgsValue ApplyBinary(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        // MATLAB's '*', '/' and '^' are matrix operations; only the dotted spellings are elementwise.
        // Everything below this point is elementwise, so the matrix forms are resolved first.
        if (Dialect.IsMatlab)
        {
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
            && !(op == TokenType.Plus && (left.Type == JgsType.String || right.Type == JgsType.String)))
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
                return NumericBinary(left, right, (a, b) => a * b, "*", at.Line, at.Column, static (a, b) => a * b);
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
        if (!Dialect.CopyOnAssign || value.Type is not (JgsType.Array or JgsType.Cell or JgsType.Struct))
        {
            return value; // scalars, strings and functions are immutable — nothing to copy
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

            return JgsValue.Cell(copied);
        }

        if (value.Type == JgsType.Struct)
        {
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach ((string name, JgsValue field) in value.AsStruct)
            {
                fields[name] = CopyForBinding(field);
            }

            return JgsValue.Struct(fields);
        }

        if (value.IsPacked)
        {
            // A fresh wrapper over a fresh buffer: the single-wrapper invariant holds, and the
            // previous-run disposal walk (which compares by reference) sees two distinct arrays.
            NumericBuffer source = value.AsBuffer;
            NumericBuffer copy = JgsPacking.Allocate(source.Length);
            source.AsSpan().CopyTo(copy.AsSpan());
            GC.KeepAlive(source);
            return JgsValue.Packed(copy, value.PackedKind);
        }

        if (value.IsPackedComplex)
        {
            JgsPackedComplex planes = value.AsPackedComplex;
            NumericBuffer re = JgsPacking.Allocate(planes.Length);
            NumericBuffer im = JgsPacking.Allocate(planes.Length);
            planes.Re.AsSpan().CopyTo(re.AsSpan());
            planes.Im.AsSpan().CopyTo(im.AsSpan());
            GC.KeepAlive(planes);
            return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
        }

        JgsValue[] source2 = value.AsArray;
        var elements = new JgsValue[source2.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            // Rows of a matrix are arrays too, and MATLAB copies the whole thing.
            elements[i] = source2[i].Type == JgsType.Array ? CopyContainer(source2[i]) : source2[i];
        }

        return JgsValue.Array(elements);
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

    private JgsValue EvaluateAssign(AssignExpr assign, JgsEnvironment env)
    {
        JgsValue rhs = Evaluate(assign.Value, env);

        if (assign.Target is VariableExpr variable)
        {
            // A name declared 'global' is written where every scope can see it.
            JgsEnvironment scope = _globalNames.Contains(variable.Name) ? _globals : env;
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

        return AssignThroughIndex(container, subscripts, assign.Op, rhs, assign, env);
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
            if (container.PackedKind == JgsPackedKind.Number && value.Type == JgsType.Number)
            {
                container.AsBuffer.AsSpan()[index] = value.AsNumber;
                return;
            }

            if (container.PackedKind == JgsPackedKind.Bool && value.Type == JgsType.Bool)
            {
                container.AsBuffer.AsSpan()[index] = value.AsBool ? 1 : 0;
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
        if (callee.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign by index into a {callee.TypeName}; only arrays support element assignment.");
        }

        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Index assignment takes exactly one subscript (an index, a range, a mask, or ':').");
        }

        JgsValue? index = EvaluateIndexArgument(subscripts[0], callee.ArrayLength, env);

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

        NumericBuffer buffer = target.AsBuffer;

        // Scalar index: a single-element write (an array right-hand side would nest, boxed-style).
        if (index is { Type: not JgsType.Array })
        {
            if (!rhsScalar)
            {
                return false;
            }

            int single = ToIndex(index, buffer.Length, at.Line, at.Column);
            Span<double> span = buffer.AsSpan();
            double stored = simple
                ? rhs.AsNumber
                : ApplyBinary(UnderlyingOp(op), JgsValue.Number(span[single]), rhs, at).AsNumber;
            span[single] = stored;
            result = simple ? rhs : JgsValue.Number(stored);
            return true;
        }

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
        if (container.Type != JgsType.Array || subscripts.Count != 1)
        {
            throw new JgsRuntimeException(incDec.Line, incDec.Column,
                $"'{symbol}' by index needs an array and a single index, like x(k){symbol}.");
        }

        JgsValue? index = EvaluateIndexArgument(subscripts[0], container.ArrayLength, env);
        if (index is null || index.Type == JgsType.Array)
        {
            throw new JgsRuntimeException(incDec.Line, incDec.Column,
                $"'{symbol}' needs a single element, not a slice.");
        }

        int single = ToIndex(index, container.ArrayLength, incDec.Line, incDec.Column);
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
        if (target.Type is not (JgsType.Array or JgsType.String or JgsType.Image))
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
        JgsValue callee = Evaluate(call.Callee, env);

        // "Calling" an array, string, or image with subscripts is indexing, identical to the bracket
        // form — a scalar lookup, a bool-mask filter, an index-array/range gather, 'end', or ':'.
        if (callee.Type is JgsType.Array or JgsType.String or JgsType.Image)
        {
            return IndexInto(callee, call.Arguments, call, env);
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

        if (callee.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(call.Line, call.Column, $"Cannot call a {callee.TypeName}; it is not a function.");
        }

        var arguments = new JgsValue[call.Arguments.Count];
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Evaluate(call.Arguments[i], env);
        }

        return callee.AsCallable.Call(arguments, call.Line, call.Column);
    }

    /// <summary>
    /// The one index read shared by <c>a[…]</c> and <c>a(…)</c>: an array or string takes a single
    /// subscript (scalar, range, mask, ':' or 'end'), an image takes two or three.
    /// </summary>
    private JgsValue IndexInto(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        if (target.Type == JgsType.Image)
        {
            return IndexImage(target, subscripts, at, env);
        }

        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Indexing a {target.TypeName} takes exactly one subscript (an index, an index array, or a mask).");
        }

        int length = target.Type == JgsType.String ? target.AsString.Length : target.ArrayLength;
        JgsValue? index = EvaluateIndexArgument(subscripts[0], length, env);
        if (index is null)
        {
            // x(:) — everything, as a fresh array (or the string itself).
            return target.Type == JgsType.String ? target
                : target.IsPacked ? PackedOps.Clone(target, _cancelCheck)
                : target.IsPackedComplex ? PackedOps.CloneComplex(target, _cancelCheck)
                : JgsValue.Array((JgsValue[])target.AsArray.Clone());
        }

        return GatherOrIndex(target, index, at.Line, at.Column);
    }

    /// <summary>Reads one sample from an image value via 0-based <c>img(r, c)</c> / <c>img(r, c, ch)</c>.</summary>
    private JgsValue IndexImage(JgsValue callee, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        JGraph.Imaging.ImageBuffer image = callee.AsImage;
        if (subscripts.Count is not (2 or 3))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Index an image with img(row, col) for grayscale or img(row, col, channel) for colour.");
        }

        int row = ImageSubscript(subscripts[0], "row", at, env);
        int col = ImageSubscript(subscripts[1], "column", at, env);
        int channel;
        if (subscripts.Count == 3)
        {
            channel = ImageSubscript(subscripts[2], "channel", at, env);
        }
        else if (image.Channels == 1)
        {
            channel = 0;
        }
        else
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"This image has {image.Channels} channels; read it with img(row, col, channel).");
        }

        if ((uint)row >= (uint)image.Height ||
            (uint)col >= (uint)image.Width ||
            (uint)channel >= (uint)image.Channels)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Image subscript ({row}, {col}, {channel}) is out of range for a " +
                $"{image.Height}x{image.Width}x{image.Channels} image (subscripts are 0-based).");
        }

        double sample = image[row, col, channel];
        GC.KeepAlive(image);
        return JgsValue.Number(sample);
    }

    private int ImageSubscript(Expr expr, string name, Node at, JgsEnvironment env)
    {
        JgsValue value = Evaluate(expr, env);
        if (value.Type != JgsType.Number)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Image {name} subscript must be a number, not a {value.TypeName}.");
        }

        double raw = value.AsNumber;
        int rounded = (int)Math.Round(raw);
        if (rounded != raw)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Image {name} subscript must be a whole number, not {raw.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        return rounded;
    }

    /// <summary>
    /// Evaluates a paren-index argument with <c>end</c> bound to <paramref name="targetLength"/>.
    /// Returns null for a lone ':' (select everything).
    /// </summary>
    private JgsValue? EvaluateIndexArgument(Expr argument, int targetLength, JgsEnvironment env)
    {
        if (argument is AllExpr)
        {
            return null;
        }

        _indexTargetLengths.Add(targetLength);
        try
        {
            return Evaluate(argument, env);
        }
        finally
        {
            _indexTargetLengths.RemoveAt(_indexTargetLengths.Count - 1);
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
            return PackedOps.Gather(target, picks); // a new packed array of the same kind
        }

        if (target.IsPackedComplex)
        {
            return PackedOps.GatherComplex(target, picks);
        }

        var gathered = new JgsValue[picks.Length];
        for (int i = 0; i < gathered.Length; i++)
        {
            gathered[i] = target.AsArray[picks[i]];
        }

        return JgsValue.Array(gathered);
    }

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

        string symbol = and ? "&" : "|";
        return JgsValue.Array(Broadcast(left, right,
            (a, b) => JgsValue.Bool(and ? a != 0 && b != 0 : a != 0 || b != 0), symbol, at.Line, at.Column));
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
        string symbol = op == TokenType.Star ? "*" : op == TokenType.Slash ? "/" : "^";
        if (op != TokenType.Star)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"'{symbol}' between two arrays is a matrix operation, which JGraph does not implement. "
                + $"Use '.{symbol}' for the elementwise form.");
        }

        double[][] a = AsRows(left);
        double[][] b = AsRows(right);
        bool leftIsVector = !IsMatrix(left);
        bool rightIsVector = !IsMatrix(right);

        if (leftIsVector && rightIsVector)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "'*' between two vectors is ambiguous here: JGraph's arrays have no row/column "
                + "orientation. Use '.*' for the elementwise product, or dot(a, b) for the inner product.");
        }

        // A vector meeting a matrix is whichever orientation makes the product work: a row on the left,
        // a column on the right.
        if (leftIsVector)
        {
            a = [a[0]];
        }

        if (rightIsVector)
        {
            b = b[0].Select(static v => new[] { v }).ToArray();
        }

        int inner = a[0].Length;
        if (inner != b.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Matrix dimensions do not agree for '*': the left has {inner} columns and the right has {b.Length} rows.");
        }

        int columns = b[0].Length;
        var product = new JgsValue[a.Length];
        for (int r = 0; r < a.Length; r++)
        {
            var row = new double[columns];
            for (int c = 0; c < columns; c++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++)
                {
                    if (b[k].Length != columns)
                    {
                        throw new JgsRuntimeException(at.Line, at.Column, "Matrix rows must have equal lengths.");
                    }

                    sum += a[r][k] * b[k][c];
                }

                row[c] = sum;
            }

            product[r] = NumbersOf(row);
        }

        // A single row (or a single column) is a plain vector again, as MATLAB would show it.
        if (product.Length == 1)
        {
            return product[0];
        }

        if (columns == 1)
        {
            return NumbersOf(product.Select(static p => p.ElementAt(0).AsNumber).ToArray());
        }

        return JgsValue.Array(product);
    }

    // --- Cells and structs ----------------------------------------------------------------------

    /// <summary>
    /// Builds a cell array. Rows are flattened: JGraph's containers are one-dimensional, so a
    /// <c>{1, 2; 3, 4}</c> literal holds four elements in reading order.
    /// </summary>
    private JgsValue EvaluateCellLiteral(CellLiteral literal, JgsEnvironment env)
    {
        var elements = new List<JgsValue>();
        foreach (IReadOnlyList<Expr> row in literal.Rows)
        {
            foreach (Expr element in row)
            {
                elements.Add(Evaluate(element, env));
            }
        }

        return JgsValue.Cell(elements.ToArray());
    }

    /// <summary>Reads <c>c{i}</c> — the contents of a cell, where <c>c(i)</c> would give a cell back.</summary>
    private JgsValue EvaluateBraceIndex(BraceIndexExpr brace, JgsEnvironment env)
    {
        JgsValue target = Evaluate(brace.Target, env);
        if (target.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"Braces index a cell array, but this is a {target.TypeName}. Use parentheses to index it.");
        }

        JgsValue[] elements = target.AsCell;
        JgsValue? index = EvaluateIndexArgument(Single(brace.Indices, brace, "A cell index"), elements.Length, env);
        return elements[ToIndex(index!, elements.Length, brace.Line, brace.Column)];
    }

    /// <summary>Reads <c>s.field</c> (or the dynamic <c>s.('field')</c>).</summary>
    private JgsValue EvaluateMember(MemberExpr member, JgsEnvironment env)
    {
        JgsValue target = Evaluate(member.Target, env);
        string field = FieldName(member, env);
        if (target.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(member.Line, member.Column,
                $"'.{field}' needs a struct, but this is a {target.TypeName}.");
        }

        if (target.AsStruct.TryGetValue(field, out JgsValue? value))
        {
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
        JgsValue container = ResolveStructForWrite(member.Target, env);
        container.AsStruct[FieldName(member, env)] = value;
        return value;
    }

    private JgsValue ResolveStructForWrite(Expr expr, JgsEnvironment env)
    {
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

                    return existing;
                }

                JgsValue created = JgsValue.EmptyStruct();
                env.Declare(variable.Name, created);
                return created;

            case MemberExpr nested:
                JgsValue parent = ResolveStructForWrite(nested.Target, env);
                string field = FieldName(nested, env);
                if (!parent.AsStruct.TryGetValue(field, out JgsValue? child) || child.Type != JgsType.Struct)
                {
                    child = JgsValue.EmptyStruct();
                    parent.AsStruct[field] = child;
                }

                return child;

            default:
                JgsValue evaluated = Evaluate(expr, env);
                if (evaluated.Type != JgsType.Struct)
                {
                    throw new JgsRuntimeException(expr.Line, expr.Column,
                        $"Cannot set a field on a {evaluated.TypeName}.");
                }

                return evaluated;
        }
    }

    /// <summary>
    /// Writes <c>c{i} = v</c>. Assigning past the end grows the cell (filling the gap with empty
    /// arrays), which is what makes MATLAB's <c>c{end+1} = x</c> accumulation idiom work.
    /// </summary>
    private JgsValue AssignToBraceIndex(BraceIndexExpr brace, JgsValue value, JgsEnvironment env)
    {
        if (brace.Target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                "Only a named cell array can be assigned into with braces.");
        }

        if (!env.TryGet(variable.Name, out JgsValue target))
        {
            target = JgsValue.Cell(System.Array.Empty<JgsValue>());
            env.Declare(variable.Name, target);
        }

        if (target.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                $"Braces assign into a cell array, but '{variable.Name}' is a {target.TypeName}.");
        }

        JgsValue[] elements = target.AsCell;
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
    /// MATLAB's transpose. A matrix — an array of row arrays — is genuinely transposed. A vector has no
    /// row/column orientation in this model, so <c>v'</c> hands back its values unchanged (conjugated
    /// for <c>'</c> when they are complex), which is what makes the ubiquitous <c>(0:0.1:1)'</c> idiom
    /// work. A scalar is its own transpose.
    /// </summary>
    private JgsValue EvaluateTranspose(TransposeExpr transpose, JgsEnvironment env)
    {
        JgsValue value = Evaluate(transpose.Operand, env);
        if (value.Type == JgsType.Complex)
        {
            return transpose.Conjugate ? JgsValue.ComplexNum(Complex.Conjugate(value.AsComplex)) : value;
        }

        if (value.Type != JgsType.Array)
        {
            return value;
        }

        if (!IsMatrix(value))
        {
            return transpose.Conjugate ? Conjugated(value) : CopyContainer(value);
        }

        int rows = value.ArrayLength;
        int columns = value.ElementAt(0).ArrayLength;
        var transposed = new JgsValue[columns];
        for (int c = 0; c < columns; c++)
        {
            var column = new JgsValue[rows];
            for (int r = 0; r < rows; r++)
            {
                JgsValue row = value.ElementAt(r);
                if (row.ArrayLength != columns)
                {
                    throw new JgsRuntimeException(transpose.Line, transpose.Column,
                        "Only a rectangular matrix can be transposed (its rows have different lengths).");
                }

                JgsValue element = row.ElementAt(c);
                column[r] = transpose.Conjugate && element.Type == JgsType.Complex
                    ? JgsValue.ComplexNum(Complex.Conjugate(element.AsComplex))
                    : element;
            }

            transposed[c] = JgsPacking.Enabled && PackedOps.TryPackElements(column, out JgsValue packed)
                ? packed
                : JgsValue.Array(column);
        }

        return JgsValue.Array(transposed);
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
            return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
        }

        JgsValue[] source2 = value.AsArray;
        var conjugated = new JgsValue[source2.Length];
        for (int i = 0; i < conjugated.Length; i++)
        {
            conjugated[i] = source2[i].Type == JgsType.Complex
                ? JgsValue.ComplexNum(Complex.Conjugate(source2[i].AsComplex))
                : source2[i];
        }

        return JgsValue.Array(conjugated);
    }

    /// <summary>Whether a value is a matrix in this model: an array whose elements are themselves arrays.</summary>
    private static bool IsMatrix(JgsValue value) =>
        value.Type == JgsType.Array && !value.IsPacked && !value.IsPackedComplex
        && value.ArrayLength > 0 && value.ElementAt(0).Type == JgsType.Array;

    /// <summary>A numeric array or matrix as rows of doubles; a vector becomes a single row.</summary>
    private double[][] AsRows(JgsValue value)
    {
        if (!IsMatrix(value))
        {
            return [RowOf(value)];
        }

        var rows = new double[value.ArrayLength][];
        for (int r = 0; r < rows.Length; r++)
        {
            rows[r] = RowOf(value.ElementAt(r));
        }

        return rows;
    }

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
        if (left.Type == JgsType.Complex || right.Type == JgsType.Complex)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Operator '{TokenText(opToken)}' is not defined for complex numbers — compare abs(), real(), or imag() instead.");
        }

        if (IsNumericScalar(left) && IsNumericScalar(right))
        {
            return JgsValue.Bool(op(left.AsNumber, right.AsNumber));
        }

        // Element-wise over arrays with scalar broadcasting, producing an array of bools (a mask).
        if (left.Type == JgsType.Array || right.Type == JgsType.Array)
        {
            string symbol = TokenText(opToken);
            return JgsValue.Array(Broadcast(left, right,
                (a, b) => JgsValue.Bool(op(a, b)), symbol, at.Line, at.Column));
        }

        throw new JgsRuntimeException(at.Line, at.Column,
            $"Operator '{TokenText(opToken)}' needs two numbers, but got {left.TypeName} and {right.TypeName}.");
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

            return JgsValue.Array(pairwise);
        }

        // One side is a scalar; broadcast it across the array.
        JgsValue[] array = (left.Type == JgsType.Array ? left : right).BoxedElements();
        JgsValue scalar = left.Type == JgsType.Array ? right : left;
        var result = new JgsValue[array.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = JgsValue.Bool(AreEqual(array[i], scalar) != negate);
        }

        return JgsValue.Array(result);
    }

    private static bool AreEqual(JgsValue left, JgsValue right) => JgsValue.AreEqual(left, right);

    private JgsValue NumericBinary(JgsValue left, JgsValue right, Func<double, double, double> op, string symbol, int line, int column, Func<Complex, Complex, Complex>? complexOp = null)
    {
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
            return JgsValue.Array(Broadcast(left, right,
                (a, b) => JgsValue.Number(op(a, b)), symbol, line, column, complexOp));
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
    private static JgsValue[] Broadcast(JgsValue left, JgsValue right, Func<double, double, JgsValue> combine, string symbol, int line, int column, Func<Complex, Complex, Complex>? complexOp = null)
    {
        // Nested arrays recurse, so matrices (arrays of row arrays) broadcast elementwise too:
        // M + M pairs rows, M + scalar spreads the scalar across every row.
        JgsValue Element(JgsValue a, JgsValue b) =>
            a.Type == JgsType.Array || b.Type == JgsType.Array
                ? JgsValue.Array(Broadcast(a, b, combine, symbol, line, column, complexOp))
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
                return JgsValue.Packed(dest);
            }

            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < result.Length; i++)
            {
                // Recurse so matrices (nested arrays) map elementwise as well.
                result[i] = MapNumeric(source[i], op, symbol, line, column, complexOp);
            }

            return JgsValue.Array(result);
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
