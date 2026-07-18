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
    private readonly Action _cancelCheck; // per-chunk poll inside packed operations
    private long _steps;
    private int _callDepth;

    /// <summary>Creates an interpreter over a prepared <paramref name="globals"/> environment.</summary>
    /// <param name="globals">The global environment, seeded with the built-ins.</param>
    /// <param name="cancellationToken">Checked cooperatively before every statement.</param>
    /// <param name="hook">The debug hook, or null for a plain full-speed run.</param>
    /// <param name="echo">Sink for MATLAB-style console echo of unsuppressed statement results, or
    /// null to disable echo entirely.</param>
    public Interpreter(JgsEnvironment globals, CancellationToken cancellationToken, IJgsDebugHook? hook = null, Action<string>? echo = null)
    {
        _globals = globals;
        _cancellationToken = cancellationToken;
        _hook = hook;
        _echo = echo;

        // Packed operations run in ~4M-element chunks and poll this between chunks, so Stop
        // interrupts a 100M-element elementwise statement mid-flight instead of after it.
        _cancelCheck = () => _cancellationToken.ThrowIfCancellationRequested();
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
                    return ExecuteBlock(ifStmt.Then, new JgsEnvironment(env));
                }

                return ifStmt.Else is not null
                    ? ExecuteBlock(ifStmt.Else, new JgsEnvironment(env))
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

    private Completion ExecuteWhile(WhileStmt statement, JgsEnvironment env)
    {
        while (Evaluate(statement.Condition, env).IsTruthy)
        {
            Tick();
            Completion completion = ExecuteBlock(statement.Body, new JgsEnvironment(env));
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
            JgsValue element = iterable.ElementAt(index);
            Tick();
            var local = new JgsEnvironment(env);
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

                return JgsValue.Number(_indexTargetLengths[^1]);

            case AllExpr:
                throw new JgsRuntimeException(expression.Line, expression.Column,
                    "':' by itself is only valid as an index argument, like x(:).");

            case VariableExpr variable:
                if (env.TryGet(variable.Name, out JgsValue value))
                {
                    return value;
                }

                throw new JgsRuntimeException(variable.Line, variable.Column, $"'{variable.Name}' is not defined.");

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
            JgsValue stored = rhs;
            if (assign.Op != TokenType.Assign)
            {
                if (!env.TryGet(variable.Name, out JgsValue current))
                {
                    throw NotDefined(variable.Name, assign);
                }

                stored = ApplyBinary(UnderlyingOp(assign.Op), current, rhs, assign);
            }

            if (!env.TryAssign(variable.Name, stored))
            {
                throw NotDefined(variable.Name, assign);
            }

            return stored;
        }

        // MATLAB paren-index write: x(k) = v, x(1:n) = 0, x(mask) = v, x(:) = v.
        if (assign.Target is CallExpr paren)
        {
            return AssignThroughParen(paren, assign.Op, rhs, assign, env);
        }

        // The parser guarantees the only other target shape is an array element.
        var element = (IndexExpr)assign.Target;
        (JgsValue container, int index) = ResolveElement(element, env);
        JgsValue value = assign.Op == TokenType.Assign
            ? rhs
            : ApplyBinary(UnderlyingOp(assign.Op), container.ElementAt(index), rhs, assign);
        WriteElement(container, index, value);
        return value;
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
    /// A 1-based paren-index write. The callee and the single index argument evaluate exactly once;
    /// a scalar right-hand side broadcasts over the selection, an array right-hand side must match
    /// its length. Compound operators apply per element.
    /// </summary>
    private JgsValue AssignThroughParen(CallExpr paren, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsValue callee = Evaluate(paren.Callee, env);
        if (callee.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Cannot assign into a {callee.TypeName} with paren indexing; the target must be an array.");
        }

        if (paren.Arguments.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Paren-index assignment takes exactly one index argument (an index, a range, a mask, or ':').");
        }

        JgsValue? index = EvaluateIndexArgument(paren.Arguments[0], callee.ArrayLength, env);

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
            int single = ToIndex(index, array.Length, at.Line, at.Column, oneBased: true);
            JgsValue stored = op == TokenType.Assign
                ? rhs
                : ApplyBinary(UnderlyingOp(op), array[single], rhs, at);
            array[single] = stored;
            return stored;
        }

        int[] picks = index is null
            ? AllPicks(array.Length)
            : ComputePicks(index, array.Length, oneBased: true, "array", at.Line, at.Column);

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

            int single = ToIndex(index, buffer.Length, at.Line, at.Column, oneBased: true);
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
            : ComputePicks(index, buffer.Length, oneBased: true, "array", at.Line, at.Column);

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

            int single = ToIndex(index, planes.Length, at.Line, at.Column, oneBased: true);
            System.Numerics.Complex written = rhs.AsComplex;
            planes.Re.AsSpan()[single] = written.Real;
            planes.Im.AsSpan()[single] = written.Imaginary;
            return true;
        }

        int[] picks = index is null
            ? AllPicks(planes.Length)
            : ComputePicks(index, planes.Length, oneBased: true, "array", at.Line, at.Column);

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

        if (incDec.Target is CallExpr paren)
        {
            JgsValue callee = Evaluate(paren.Callee, env);
            if (callee.Type != JgsType.Array || paren.Arguments.Count != 1)
            {
                throw new JgsRuntimeException(incDec.Line, incDec.Column,
                    $"'{symbol}' with paren indexing needs an array and a single index, like x(k){symbol}.");
            }

            JgsValue? parenIndex = EvaluateIndexArgument(paren.Arguments[0], callee.ArrayLength, env);
            if (parenIndex is null || parenIndex.Type == JgsType.Array)
            {
                throw new JgsRuntimeException(incDec.Line, incDec.Column,
                    $"'{symbol}' needs a single element, not a slice.");
            }

            int single = ToIndex(parenIndex, callee.ArrayLength, incDec.Line, incDec.Column, oneBased: true);
            JgsValue previous = callee.ElementAt(single);
            JgsValue bumped = JgsValue.Number(RequireIncDecNumber(previous, symbol, incDec) + delta);
            WriteElement(callee, single, bumped);
            return incDec.Prefix ? bumped : previous;
        }

        var element = (IndexExpr)incDec.Target;
        (JgsValue container, int index) = ResolveElement(element, env);
        JgsValue old = container.ElementAt(index);
        JgsValue result = JgsValue.Number(RequireIncDecNumber(old, symbol, incDec) + delta);
        WriteElement(container, index, result);
        return incDec.Prefix ? result : old;
    }

    /// <summary>
    /// Evaluates an element-assignment target exactly once: the container expression and the index
    /// expression each evaluate a single time, so <c>a[f(i)] += 1</c> calls <c>f</c> once.
    /// </summary>
    private (JgsValue Container, int Index) ResolveElement(IndexExpr element, JgsEnvironment env)
    {
        JgsValue target = Evaluate(element.Target, env);
        if (target.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(element.Line, element.Column,
                $"Cannot assign by index into a {target.TypeName}; only arrays support element assignment.");
        }

        int index = ToIndex(Evaluate(element.Index, env), target.ArrayLength, element.Line, element.Column);
        return (target, index);
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

    private JgsValue EvaluateIndex(IndexExpr indexExpr, JgsEnvironment env)
    {
        JgsValue target = Evaluate(indexExpr.Target, env);
        JgsValue index = Evaluate(indexExpr.Index, env);

        if (target.Type is not (JgsType.Array or JgsType.String))
        {
            throw new JgsRuntimeException(indexExpr.Line, indexExpr.Column,
                $"Cannot index a {target.TypeName}.");
        }

        return GatherOrIndex(target, index, indexExpr.Line, indexExpr.Column);
    }

    private JgsValue EvaluateCall(CallExpr call, JgsEnvironment env)
    {
        JgsValue callee = Evaluate(call.Callee, env);

        // MATLAB-style: "calling" an array (or string) with one argument is 1-based indexing —
        // a scalar element lookup, a bool-mask filter, an index-array/range gather, 'end', or ':'.
        if (callee.Type is JgsType.Array or JgsType.String)
        {
            if (call.Arguments.Count != 1)
            {
                throw new JgsRuntimeException(call.Line, call.Column,
                    $"Indexing a {callee.TypeName} takes exactly one argument (an index, an index array, or a mask).");
            }

            int length = callee.Type == JgsType.String ? callee.AsString.Length : callee.ArrayLength;
            JgsValue? index = EvaluateIndexArgument(call.Arguments[0], length, env);
            if (index is null)
            {
                // x(:) — everything, as a fresh array (or the string itself).
                return callee.Type == JgsType.String ? callee
                    : callee.IsPacked ? PackedOps.Clone(callee, _cancelCheck)
                    : callee.IsPackedComplex ? PackedOps.CloneComplex(callee, _cancelCheck)
                    : JgsValue.Array((JgsValue[])callee.AsArray.Clone());
            }

            return GatherOrIndex(callee, index, call.Line, call.Column, oneBased: true);
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
    /// all-number array gathers by index — 0-based for brackets, 1-based for MATLAB parens
    /// (<paramref name="oneBased"/>). Gathering a string yields a string.
    /// </summary>
    private static JgsValue GatherOrIndex(JgsValue target, JgsValue index, int line, int column, bool oneBased = false)
    {
        bool isString = target.Type == JgsType.String;
        int length = isString ? target.AsString.Length : target.ArrayLength;

        if (index.Type != JgsType.Array)
        {
            int single = ToIndex(index, length, line, column, oneBased);
            return isString ? JgsValue.Str(target.AsString[single].ToString()) : target.ElementAt(single);
        }

        int[] picks = ComputePicks(index, length, oneBased, target.TypeName, line, column);
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
    private static int[] ComputePicks(JgsValue index, int length, bool oneBased, string targetName, int line, int column)
    {
        if (index.IsPacked)
        {
            return PackedOps.PicksFromPacked(index, length, oneBased, targetName, line, column);
        }

        if (index.IsPackedComplex)
        {
            return PackedOps.PicksFromPackedComplex(index, length, oneBased, line, column);
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
                picks.Add(ToIndex(position, length, line, column, oneBased));
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

    private static int ToIndex(JgsValue index, int length, int line, int column, bool oneBased = false)
    {
        if (index.Type != JgsType.Number)
        {
            throw new JgsRuntimeException(line, column, $"An index must be a number, but got a {index.TypeName}.");
        }

        double raw = index.AsNumber;
        if (raw != Math.Floor(raw) || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new JgsRuntimeException(line, column, $"An index must be a whole number, but got {raw.ToString("R", CultureInfo.InvariantCulture)}.");
        }

        int i = (int)raw;
        if (oneBased)
        {
            if (i < 1 || i > length)
            {
                throw new JgsRuntimeException(line, column,
                    $"Index {i} is out of range for length {length} (paren indexing is 1-based).");
            }

            return i - 1;
        }

        if (i < 0 || i >= length)
        {
            throw new JgsRuntimeException(line, column, $"Index {i} is out of range for length {length}.");
        }

        return i;
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
