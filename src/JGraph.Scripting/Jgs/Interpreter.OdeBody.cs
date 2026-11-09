namespace JGraph.Scripting.Jgs;

/// <summary>
/// The every-row plan's I1 (ADR 0324): an ODE solver's right-hand side compiled once per solve. An
/// anonymous <c>@(t, y) [e1; e2; ...]</c> whose elements are scalar arithmetic over <c>t</c>,
/// <c>y(k)</c> with a constant <c>k</c>, real double scalars the handle captured, the hot loop's
/// constants and its scalar builtins becomes a <see cref="CompiledOdeBody"/>: a postfix program the
/// solver's stages run over its own state array, with no value minted, no workspace made and no
/// tree walked per stage. Anything else is refused and the handle is called as before.
/// </summary>
/// <remarks>
/// Every op binds the arithmetic the walk applies to two real double scalars, the same table the
/// compiled loop binds (<see cref="JgsBuiltins.TryHotLoopUnary"/>, <see cref="JgsBuiltins.PowerStaysReal"/>,
/// <see cref="JgsBuiltins.ScalarMod"/>), so the two roads give the same doubles. The cases a double
/// cannot hold - a power or a builtin that leaves the reals - fail the evaluation and that one call
/// walks, which also raises whatever the walk raises.
/// </remarks>
internal static class JgsOdeCompile
{
    /// <summary>Whether ODE right-hand sides compile; <c>JGRAPH_ODE_COMPILE=1|0</c> forces it.</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_ODE_COMPILE") switch
    {
        "0" or "false" => false,
        _ => true,
    };

    /// <summary><c>JGRAPH_ODE_TRACE=1</c>: every refusal is written to standard error with what refused it.</summary>
    public static bool Trace { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_ODE_TRACE") is "1" or "true";

    /// <summary>How many solves ran a compiled right-hand side on this thread (the tests' witness).</summary>
    [ThreadStatic]
    internal static long CompiledSolves;

    /// <summary>How many single evaluations a compiled body handed back to the walk on this thread.</summary>
    [ThreadStatic]
    internal static long Walked;
}

/// <summary>The op a <see cref="CompiledOdeBody"/> is made of; a postfix machine over a small stack.</summary>
internal enum OdeOp : byte
{
    Const,
    Time,
    State,
    Add,
    Sub,
    Mul,
    Div,
    Neg,
    Pow,
    Call1,
    Call1G,
    Mod,
    Rem,
    Atan2,
    Store,
}

/// <summary>One op and its argument: a constant's index, a state's index, a kernel's index or an output's.</summary>
internal readonly record struct OdeBodyOp(OdeOp Code, int Arg);

/// <summary>A right-hand side compiled by <see cref="Interpreter.TryCompileOdeBody"/>.</summary>
internal sealed class CompiledOdeBody
{
    private readonly OdeBodyOp[] _ops;
    private readonly double[] _constants;
    private readonly Func<double, double>[] _unary;
    private readonly Func<double, bool>?[] _guards;
    private readonly int _depth;

    internal CompiledOdeBody(OdeBodyOp[] ops, double[] constants, Func<double, double>[] unary,
        Func<double, bool>?[] guards, int depth, int outputs)
    {
        _ops = ops;
        _constants = constants;
        _unary = unary;
        _guards = guards;
        _depth = depth;
        Outputs = outputs;
    }

    /// <summary>The length of the answer, which is the state's.</summary>
    public int Outputs { get; }

    /// <summary>
    /// The slopes at (<paramref name="t"/>, <paramref name="y"/>) into <paramref name="dy"/>; false
    /// when an op's answer leaves the reals, and the caller walks that call instead.
    /// </summary>
    public bool TryEvaluate(double t, double[] y, double[] dy)
    {
        Span<double> stack = _depth <= 32 ? stackalloc double[32] : new double[_depth];
        OdeBodyOp[] ops = _ops;
        int sp = 0;
        for (int ip = 0; ip < ops.Length; ip++)
        {
            OdeBodyOp op = ops[ip];
            switch (op.Code)
            {
                case OdeOp.Const:
                    stack[sp++] = _constants[op.Arg];
                    break;
                case OdeOp.Time:
                    stack[sp++] = t;
                    break;
                case OdeOp.State:
                    stack[sp++] = y[op.Arg];
                    break;
                case OdeOp.Add:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] + stack[sp];
                    break;
                case OdeOp.Sub:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] - stack[sp];
                    break;
                case OdeOp.Mul:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] * stack[sp];
                    break;
                case OdeOp.Div:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] / stack[sp];
                    break;
                case OdeOp.Neg:
                    stack[sp - 1] = -stack[sp - 1];
                    break;
                case OdeOp.Pow:
                {
                    sp--;
                    double a = stack[sp - 1];
                    double b = stack[sp];
                    if (!JgsBuiltins.PowerStaysReal(a, b))
                    {
                        return false;
                    }

                    stack[sp - 1] = Math.Pow(a, b);
                    break;
                }

                case OdeOp.Call1:
                    stack[sp - 1] = _unary[op.Arg](stack[sp - 1]);
                    break;
                case OdeOp.Call1G:
                {
                    double x = stack[sp - 1];
                    if (!_guards[op.Arg]!(x))
                    {
                        return false;
                    }

                    stack[sp - 1] = _unary[op.Arg](x);
                    break;
                }

                case OdeOp.Mod:
                    sp--;
                    stack[sp - 1] = JgsBuiltins.ScalarMod(stack[sp - 1], stack[sp]);
                    break;
                case OdeOp.Rem:
                    sp--;
                    stack[sp - 1] = JgsBuiltins.ScalarRem(stack[sp - 1], stack[sp]);
                    break;
                case OdeOp.Atan2:
                    sp--;
                    stack[sp - 1] = Math.Atan2(stack[sp - 1], stack[sp]);
                    break;
                case OdeOp.Store:
                    dy[op.Arg] = stack[--sp];
                    break;
            }
        }

        return true;
    }
}

internal sealed partial class Interpreter
{
    /// <summary>
    /// <paramref name="f"/>'s body compiled for a state of <paramref name="states"/> elements, or
    /// null when anything in it is outside the whitelist, the debugger is attached, or the switch
    /// is off. The captured values are read here: the snapshot cannot change while the solve runs,
    /// since nothing the whitelist admits can reach it.
    /// </summary>
    internal CompiledOdeBody? TryCompileOdeBody(AnonymousFunction f, int states)
    {
        AnonymousFnExpr declaration = f.Declaration;
        if (!JgsOdeCompile.Enabled || _hook is not null || states < 1
            || !declaration.Dialect.IsMatlab || !declaration.Dialect.MatlabFunctions
            || declaration.Parameters.Count != 2
            || declaration.Parameters[0] == declaration.Parameters[1]
            || declaration.Parameters[1] is "varargin" or "nargin")
        {
            return null;
        }

        var builder = new OdeBodyBuilder(this, f, states);
        using ContextScope context = EnterContext(f.Captured, f.File, f.Text, 0);
        using DialectContext code = EnterDialect(declaration.Dialect);
        return builder.Build();
    }

    /// <summary>The compiler behind <see cref="TryCompileOdeBody"/>; one per handle and solve.</summary>
    private sealed class OdeBodyBuilder(Interpreter interpreter, AnonymousFunction f, int states)
    {
        private readonly List<OdeBodyOp> _ops = [];
        private readonly List<double> _constants = [];
        private readonly List<Func<double, double>> _unary = [];
        private readonly List<Func<double, bool>?> _guards = [];
        private readonly Dictionary<string, int> _unaryOf = new(StringComparer.Ordinal);
        private readonly string _time = f.Declaration.Parameters[0];
        private readonly string _state = f.Declaration.Parameters[1];
        private int _depth;
        private int _maxDepth;

        private sealed class Refused(string what) : Exception(what);

        /// <summary>The refusal, written to standard error under <c>JGRAPH_ODE_TRACE=1</c>, as the loop compiler's trace is.</summary>
        private Refused Refuse(string what)
        {
            if (JgsOdeCompile.Trace)
            {
                Console.Error.WriteLine($"[ode compile] refused {f.Text}: {what}");
            }

            return new Refused(what);
        }

        public CompiledOdeBody? Build()
        {
            try
            {
                Expr body = f.Declaration.Body;
                IReadOnlyList<Expr> elements = body switch
                {
                    MatrixLiteral { Rows.Count: 1 } row => row.Rows[0],
                    MatrixLiteral column when column.Rows.All(r => r.Count == 1) => column.Rows.Select(r => r[0]).ToArray(),
                    ArrayLiteral row => row.Elements,
                    MatrixLiteral or ArrayLiteral or CellLiteral => throw Refuse("a nested bracket"),
                    _ => [body],
                };

                if (elements.Count != states)
                {
                    return null; // the walk raises the size refusal in the solver's words
                }

                for (int k = 0; k < elements.Count; k++)
                {
                    Emit(elements[k]);
                    _ops.Add(new OdeBodyOp(OdeOp.Store, k));
                    _depth--;
                }

                return new CompiledOdeBody([.. _ops], [.. _constants], [.. _unary], [.. _guards], _maxDepth, states);
            }
            catch (Refused)
            {
                return null;
            }
        }

        private void Push(OdeOp code, int arg = 0)
        {
            _ops.Add(new OdeBodyOp(code, arg));
            _maxDepth = Math.Max(_maxDepth, ++_depth);
        }

        private void Apply(OdeOp code)
        {
            _ops.Add(new OdeBodyOp(code, 0));
            _depth--;
        }

        private void Constant(double value)
        {
            _constants.Add(value);
            Push(OdeOp.Const, _constants.Count - 1);
        }

        private void Emit(Expr expression)
        {
            switch (expression)
            {
                case NumberLiteral { IntegerClass: null } number:
                    Constant(number.Value);
                    return;

                case VariableExpr variable:
                    EmitName(variable.Name);
                    return;

                case UnaryExpr { Op: TokenType.Minus } minus:
                    Emit(minus.Operand);
                    _ops.Add(new OdeBodyOp(OdeOp.Neg, 0));
                    return;

                case UnaryExpr { Op: TokenType.Plus } plus:
                    Emit(plus.Operand);
                    return;

                case BinaryExpr binary:
                    EmitBinary(binary);
                    return;

                case CallExpr { Callee: VariableExpr callee } call:
                    EmitCall(callee.Name, call.Arguments);
                    return;

                default:
                    throw Refuse("an expression outside the whitelist");
            }
        }

        private void EmitBinary(BinaryExpr binary)
        {
            OdeOp code = binary.Op switch
            {
                TokenType.Plus => OdeOp.Add,
                TokenType.Minus => OdeOp.Sub,
                TokenType.Star or TokenType.DotStar => OdeOp.Mul,
                TokenType.Slash or TokenType.DotSlash or TokenType.Backslash or TokenType.DotBackslash => OdeOp.Div,
                TokenType.Caret or TokenType.DotCaret => OdeOp.Pow,
                _ => throw Refuse("an operator outside the whitelist"),
            };

            // a \ b over scalars is b / a: the swap the walk itself makes.
            bool swap = binary.Op is TokenType.Backslash or TokenType.DotBackslash;
            Emit(swap ? binary.Right : binary.Left);
            Emit(swap ? binary.Left : binary.Right);
            Apply(code);
        }

        private void EmitName(string name)
        {
            if (name == _time)
            {
                Push(OdeOp.Time);
                return;
            }

            if (name == _state)
            {
                // The whole state is a scalar only when it has one element.
                if (states != 1)
                {
                    throw Refuse("the whole state where a scalar is wanted");
                }

                Push(OdeOp.State, 0);
                return;
            }

            if (name == "nargin")
            {
                throw Refuse("nargin");
            }

            JgsEnvironment captured = f.Captured;
            if (captured.Locals.ContainsKey(name))
            {
                if (captured.IsFunctionBinding(name) || captured.IsGlobal(name)
                    || !captured.TryGet(name, out JgsValue value)
                    || value.Type != JgsType.Number || value.NumericClass != JgsNumericClass.Double)
                {
                    throw Refuse("a captured value that is not a real double scalar");
                }

                Constant(value.AsNumber);
                return;
            }

            // A constant builtin mentioned bare, folded once as the compiled loop folds it: the layer
            // holds most of them as the number itself, the rest as a function called with nothing.
            if (JgsBuiltins.IsHotLoopBareConstant(name)
                && interpreter._resolver.Lookup(name, captured) is { Layer: ResolutionLayer.Builtin } held
                && held.Value.Type == JgsType.Number && held.Value.NumericClass == JgsNumericClass.Double)
            {
                Constant(held.Value.AsNumber);
                return;
            }

            if (JgsBuiltins.IsHotLoopBareConstant(name)
                && interpreter._resolver.CompiledBuiltin(name, captured, bare: true) is BuiltinFunction { AutoCallsBare: true } constant)
            {
                JgsValue answer = constant.Call(Array.Empty<JgsValue>(), 0, 0);
                if (answer.Type != JgsType.Number || answer.NumericClass != JgsNumericClass.Double)
                {
                    throw Refuse("a bare constant that does not answer a double");
                }

                Constant(answer.AsNumber);
                return;
            }

            throw Refuse("a name that is not a parameter, a captured scalar or a constant");
        }

        private void EmitCall(string name, IReadOnlyList<Expr> arguments)
        {
            if (name == _state)
            {
                // y(k) with a literal k inside the state: the one read the body may make of it.
                if (arguments.Count != 1 || arguments[0] is not NumberLiteral { IntegerClass: null } index
                    || index.Value != Math.Floor(index.Value) || index.Value < 1 || index.Value > states)
                {
                    throw Refuse("a read of the state that is not y(k) with a literal k inside it");
                }

                Push(OdeOp.State, (int)index.Value - 1);
                return;
            }

            if (name == _time || name == "nargin" || f.Captured.Locals.ContainsKey(name)
                || interpreter._resolver.CompiledBuiltin(name, f.Captured, bare: false) is null)
            {
                throw Refuse("an index into a captured value, or a call that does not reach the builtin"); // an index into a captured array, or a call the walk must resolve
            }

            if (arguments.Count == 1
                && JgsBuiltins.TryHotLoopUnary(name, out Func<double, double> kernel, out Func<double, bool>? staysReal))
            {
                if (!_unaryOf.TryGetValue(name, out int slot))
                {
                    slot = _unary.Count;
                    _unaryOf[name] = slot;
                    _unary.Add(kernel);
                    _guards.Add(staysReal);
                }

                Emit(arguments[0]);
                _ops.Add(new OdeBodyOp(staysReal is null ? OdeOp.Call1 : OdeOp.Call1G, slot));
                return;
            }

            OdeOp? binary = arguments.Count == 2
                ? name switch
                {
                    "mod" => OdeOp.Mod,
                    "rem" => OdeOp.Rem,
                    "atan2" => OdeOp.Atan2,
                    _ => null,
                }
                : null;
            if (binary is not { } op)
            {
                throw Refuse("a call outside the whitelist");
            }

            Emit(arguments[0]);
            Emit(arguments[1]);
            Apply(op);
        }
    }
}
