using System.Globalization;
using System.Text;

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
    private long _steps;
    private int _callDepth;

    /// <summary>Creates an interpreter over a prepared <paramref name="globals"/> environment.</summary>
    /// <param name="globals">The global environment, seeded with the built-ins.</param>
    /// <param name="cancellationToken">Checked cooperatively before every statement.</param>
    /// <param name="hook">The debug hook, or null for a plain full-speed run.</param>
    public Interpreter(JgsEnvironment globals, CancellationToken cancellationToken, IJgsDebugHook? hook = null)
    {
        _globals = globals;
        _cancellationToken = cancellationToken;
        _hook = hook;
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
                env.Declare(let.Name, Evaluate(let.Value, env));
                return Completion.Normal;

            case DestructuringLetStmt destructure:
                JgsValue tuple = Evaluate(destructure.Value, env);
                if (tuple.Type != JgsType.Array)
                {
                    throw new JgsRuntimeException(destructure.Line, destructure.Column,
                        $"Destructuring 'let' needs an array on the right-hand side, but got a {tuple.TypeName}.");
                }

                if (tuple.AsArray.Length != destructure.Names.Count)
                {
                    throw new JgsRuntimeException(destructure.Line, destructure.Column,
                        $"Destructuring 'let' names {destructure.Names.Count} variables, but the array has {tuple.AsArray.Length} elements.");
                }

                for (int n = 0; n < destructure.Names.Count; n++)
                {
                    env.Declare(destructure.Names[n], tuple.AsArray[n]);
                }

                return Completion.Normal;

            case ExprStmt expr:
                Evaluate(expr.Expression, env);
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

        foreach (JgsValue element in iterable.AsArray)
        {
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

                return JgsValue.Array(elements);

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

    private JgsValue EvaluateUnary(UnaryExpr unary, JgsEnvironment env)
    {
        JgsValue operand = Evaluate(unary.Operand, env);
        if (unary.Op == TokenType.Bang)
        {
            return JgsValue.Bool(!operand.IsTruthy);
        }

        // Minus: numeric negation, element-wise over arrays.
        return MapNumeric(operand, v => -v, "-", unary.Line, unary.Column);
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
        switch (op)
        {
            case TokenType.EqualEqual:
                return Equality(left, right, negate: false, at);
            case TokenType.BangEqual:
                return Equality(left, right, negate: true, at);
            case TokenType.Plus when left.Type == JgsType.String || right.Type == JgsType.String:
                return JgsValue.Str(left.Display() + right.Display());
            case TokenType.Plus:
                return NumericBinary(left, right, (a, b) => a + b, "+", at.Line, at.Column);
            case TokenType.Minus:
                return NumericBinary(left, right, (a, b) => a - b, "-", at.Line, at.Column);
            case TokenType.Star:
                return NumericBinary(left, right, (a, b) => a * b, "*", at.Line, at.Column);
            case TokenType.Slash:
                return NumericBinary(left, right, (a, b) => a / b, "/", at.Line, at.Column);
            case TokenType.Percent:
                return NumericBinary(left, right, (a, b) => a % b, "%", at.Line, at.Column);
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

        // The parser guarantees the only other target shape is an array element.
        var element = (IndexExpr)assign.Target;
        (JgsValue[] array, int index) = ResolveElement(element, env);
        JgsValue value = assign.Op == TokenType.Assign
            ? rhs
            : ApplyBinary(UnderlyingOp(assign.Op), array[index], rhs, assign);
        array[index] = value;
        return value;
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

        var element = (IndexExpr)incDec.Target;
        (JgsValue[] array, int index) = ResolveElement(element, env);
        JgsValue old = array[index];
        JgsValue result = JgsValue.Number(RequireIncDecNumber(old, symbol, incDec) + delta);
        array[index] = result;
        return incDec.Prefix ? result : old;
    }

    /// <summary>
    /// Evaluates an element-assignment target exactly once: the container expression and the index
    /// expression each evaluate a single time, so <c>a[f(i)] += 1</c> calls <c>f</c> once.
    /// </summary>
    private (JgsValue[] Array, int Index) ResolveElement(IndexExpr element, JgsEnvironment env)
    {
        JgsValue target = Evaluate(element.Target, env);
        if (target.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(element.Line, element.Column,
                $"Cannot assign by index into a {target.TypeName}; only arrays support element assignment.");
        }

        int index = ToIndex(Evaluate(element.Index, env), target.AsArray.Length, element.Line, element.Column);
        return (target.AsArray, index);
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

        // MATLAB-style: "calling" an array (or string) with one argument is indexing —
        // a scalar element lookup, a bool-mask filter, or an index-array gather.
        if (callee.Type is JgsType.Array or JgsType.String)
        {
            if (call.Arguments.Count != 1)
            {
                throw new JgsRuntimeException(call.Line, call.Column,
                    $"Indexing a {callee.TypeName} takes exactly one argument (an index, an index array, or a mask).");
            }

            return GatherOrIndex(callee, Evaluate(call.Arguments[0], env), call.Line, call.Column);
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
    /// Resolves <c>target[index]</c> / <c>target(index)</c> for an array or string target:
    /// a scalar number selects one element; an all-bool array is a mask (must match the target's
    /// length); an all-number array gathers by 0-based indices. Gathering a string yields a string.
    /// </summary>
    private static JgsValue GatherOrIndex(JgsValue target, JgsValue index, int line, int column)
    {
        bool isString = target.Type == JgsType.String;
        int length = isString ? target.AsString.Length : target.AsArray.Length;

        if (index.Type != JgsType.Array)
        {
            int single = ToIndex(index, length, line, column);
            return isString ? JgsValue.Str(target.AsString[single].ToString()) : target.AsArray[single];
        }

        JgsValue[] selector = index.AsArray;
        var picks = new List<int>(selector.Length);
        if (selector.Length > 0 && Array.TrueForAll(selector, v => v.Type == JgsType.Bool))
        {
            if (selector.Length != length)
            {
                throw new JgsRuntimeException(line, column,
                    $"A mask must match the {target.TypeName} length (mask {selector.Length}, {target.TypeName} {length}).");
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

        if (isString)
        {
            var sb = new StringBuilder(picks.Count);
            foreach (int i in picks)
            {
                sb.Append(target.AsString[i]);
            }

            return JgsValue.Str(sb.ToString());
        }

        var gathered = new JgsValue[picks.Count];
        for (int i = 0; i < gathered.Length; i++)
        {
            gathered[i] = target.AsArray[picks[i]];
        }

        return JgsValue.Array(gathered);
    }

    // --- Numeric helpers ----------------------------------------------------------------------

    private JgsValue Compare(JgsValue left, JgsValue right, TokenType opToken, Node at, Func<double, double, bool> op)
    {
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
            JgsValue[] a = left.AsArray;
            JgsValue[] b = right.AsArray;
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
        JgsValue[] array = (left.Type == JgsType.Array ? left : right).AsArray;
        JgsValue scalar = left.Type == JgsType.Array ? right : left;
        var result = new JgsValue[array.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = JgsValue.Bool(AreEqual(array[i], scalar) != negate);
        }

        return JgsValue.Array(result);
    }

    private static bool AreEqual(JgsValue left, JgsValue right) => JgsValue.AreEqual(left, right);

    private JgsValue NumericBinary(JgsValue left, JgsValue right, Func<double, double, double> op, string symbol, int line, int column)
    {
        if (IsNumericScalar(left) && IsNumericScalar(right))
        {
            return JgsValue.Number(op(left.AsNumber, right.AsNumber));
        }

        if (left.Type == JgsType.Array || right.Type == JgsType.Array)
        {
            return JgsValue.Array(Broadcast(left, right,
                (a, b) => JgsValue.Number(op(a, b)), symbol, line, column));
        }

        throw new JgsRuntimeException(line, column,
            $"Operator '{symbol}' needs numbers or numeric arrays, but got {left.TypeName} and {right.TypeName}.");
    }

    /// <summary>
    /// Applies <paramref name="combine"/> pairwise over two arrays (equal lengths required) or an
    /// array and a scalar (broadcast). Elements must be numbers or bools (which read as 0/1).
    /// </summary>
    private static JgsValue[] Broadcast(JgsValue left, JgsValue right, Func<double, double, JgsValue> combine, string symbol, int line, int column)
    {
        // Nested arrays recurse, so matrices (arrays of row arrays) broadcast elementwise too:
        // M + M pairs rows, M + scalar spreads the scalar across every row.
        JgsValue Element(JgsValue a, JgsValue b) =>
            a.Type == JgsType.Array || b.Type == JgsType.Array
                ? JgsValue.Array(Broadcast(a, b, combine, symbol, line, column))
                : combine(RequireNumber(a, symbol, line, column), RequireNumber(b, symbol, line, column));

        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            JgsValue[] a = left.AsArray;
            JgsValue[] b = right.AsArray;
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
        JgsValue[] array = (arrayOnLeft ? left : right).AsArray;
        JgsValue scalar = arrayOnLeft ? right : left;
        var broadcast = new JgsValue[array.Length];
        for (int i = 0; i < broadcast.Length; i++)
        {
            broadcast[i] = arrayOnLeft ? Element(array[i], scalar) : Element(scalar, array[i]);
        }

        return broadcast;
    }

    private JgsValue MapNumeric(JgsValue value, Func<double, double> op, string symbol, int line, int column)
    {
        if (IsNumericScalar(value))
        {
            return JgsValue.Number(op(value.AsNumber));
        }

        if (value.Type == JgsType.Array)
        {
            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < result.Length; i++)
            {
                // Recurse so matrices (nested arrays) map elementwise as well.
                result[i] = MapNumeric(source[i], op, symbol, line, column);
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

    private static int ToIndex(JgsValue index, int length, int line, int column)
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
