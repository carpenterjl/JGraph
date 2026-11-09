namespace JGraph.Scripting.Jgs;

/// <summary>
/// The every-row plan's I1 (ADR 0324): an ODE solver's right-hand side compiled once per solve. An
/// anonymous <c>@(t, y) [e1; e2; ...]</c> whose elements are scalar arithmetic over <c>t</c>,
/// <c>y(k)</c> with a constant <c>k</c>, real double scalars the handle captured, the hot loop's
/// constants and its scalar builtins becomes a <see cref="CompiledBody"/>: a postfix program the
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

/// <summary>
/// The every-row plan's I2 (ADR 0325): the same compiler for the scalar callbacks - an objective
/// handed to <c>fzero</c>, <c>fminbnd</c> or <c>fminsearch</c>, the function <c>arrayfun</c> applies
/// to each element, and the integrand <c>integral</c> and <c>quadgk</c> ask for a panel of points at
/// a time. Its own switch, so the two stages are measured apart on one binary.
/// </summary>
internal static class JgsCallbackCompile
{
    /// <summary>Whether callbacks compile; <c>JGRAPH_FN_COMPILE=1|0</c> forces it.</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_FN_COMPILE") switch
    {
        "0" or "false" => false,
        _ => true,
    };

    /// <summary><c>JGRAPH_FN_TRACE=1</c>: every refusal is written to standard error with what refused it.</summary>
    public static bool Trace { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_FN_TRACE") is "1" or "true";

    /// <summary>How many callback handles were compiled on this thread (the tests' witness).</summary>
    [ThreadStatic]
    internal static long Compiled;

    /// <summary>How many evaluations (one point, one element or one panel) a compiled callback handed back to the walk.</summary>
    [ThreadStatic]
    internal static long Walked;
}

/// <summary>The op a <see cref="CompiledBody"/> is made of; a postfix machine over a small stack.</summary>
internal enum BodyOp : byte
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
internal readonly record struct BodyInstruction(BodyOp Code, int Arg);

/// <summary>
/// A body compiled by <see cref="Interpreter.TryCompileOdeBody"/>, <see cref="Interpreter.TryCompilePointBody"/>
/// or <see cref="Interpreter.TryCompileBatchBody"/>. The scalar the program reads with
/// <see cref="BodyOp.Time"/> is an ODE's time or a batch's element; <see cref="BodyOp.State"/> reads
/// an ODE's state or a solver's point.
/// </summary>
internal sealed class CompiledBody
{
    private const int StackSize = 32;
    private readonly BodyInstruction[] _ops;
    private readonly double[] _constants;
    private readonly Func<double, double>[] _unary;
    private readonly Func<double, bool>?[] _guards;
    private readonly int _depth;

    internal CompiledBody(BodyInstruction[] ops, double[] constants, Func<double, double>[] unary,
        Func<double, bool>?[] guards, int depth, int outputs)
    {
        _ops = ops;
        _constants = constants;
        _unary = unary;
        _guards = guards;
        _depth = depth;
        Outputs = outputs;
    }

    /// <summary>The length of the answer: an ODE's state, or one for a callback.</summary>
    public int Outputs { get; }

    /// <summary>A copy of the constant table, for a template to fill with a new handle's captured values.</summary>
    internal double[] CopyConstants() => (double[])_constants.Clone();

    /// <summary>The same program over another constant table.</summary>
    internal CompiledBody WithConstants(double[] constants) =>
        new(_ops, constants, _unary, _guards, _depth, Outputs);

    /// <summary>
    /// The slopes at (<paramref name="t"/>, <paramref name="y"/>) into <paramref name="dy"/>; false
    /// when an op's answer leaves the reals, and the caller walks that call instead.
    /// </summary>
    public bool TryEvaluate(double t, ReadOnlySpan<double> y, Span<double> dy)
    {
        Span<double> stack = _depth <= StackSize ? stackalloc double[StackSize] : new double[_depth];
        return Run(t, y, dy, stack);
    }

    /// <summary>A solver's objective at <paramref name="point"/>; false when the walk must answer.</summary>
    public bool TryEvaluatePoint(ReadOnlySpan<double> point, out double value)
    {
        Span<double> stack = _depth <= StackSize ? stackalloc double[StackSize] : new double[_depth];
        Span<double> answer = stackalloc double[1];
        bool real = Run(0, point, answer, stack);
        value = answer[0];
        return real;
    }

    /// <summary>
    /// The body at every element of <paramref name="x"/>, into <paramref name="values"/>; false as
    /// soon as one element's answer leaves the reals, and the caller walks the whole batch, since
    /// the walk's answer to the batch is then complex.
    /// </summary>
    public bool TryEvaluateBatch(ReadOnlySpan<double> x, Span<double> values)
    {
        Span<double> stack = _depth <= StackSize ? stackalloc double[StackSize] : new double[_depth];
        for (int i = 0; i < x.Length; i++)
        {
            if (!Run(x[i], default, values.Slice(i, 1), stack))
            {
                return false;
            }
        }

        return true;
    }

    private bool Run(double t, ReadOnlySpan<double> y, Span<double> dy, Span<double> stack)
    {
        BodyInstruction[] ops = _ops;
        int sp = 0;
        for (int ip = 0; ip < ops.Length; ip++)
        {
            BodyInstruction op = ops[ip];
            switch (op.Code)
            {
                case BodyOp.Const:
                    stack[sp++] = _constants[op.Arg];
                    break;
                case BodyOp.Time:
                    stack[sp++] = t;
                    break;
                case BodyOp.State:
                    stack[sp++] = y[op.Arg];
                    break;
                case BodyOp.Add:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] + stack[sp];
                    break;
                case BodyOp.Sub:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] - stack[sp];
                    break;
                case BodyOp.Mul:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] * stack[sp];
                    break;
                case BodyOp.Div:
                    sp--;
                    stack[sp - 1] = stack[sp - 1] / stack[sp];
                    break;
                case BodyOp.Neg:
                    stack[sp - 1] = -stack[sp - 1];
                    break;
                case BodyOp.Pow:
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

                case BodyOp.Call1:
                    stack[sp - 1] = _unary[op.Arg](stack[sp - 1]);
                    break;
                case BodyOp.Call1G:
                {
                    double x = stack[sp - 1];
                    if (!_guards[op.Arg]!(x))
                    {
                        return false;
                    }

                    stack[sp - 1] = _unary[op.Arg](x);
                    break;
                }

                case BodyOp.Mod:
                    sp--;
                    stack[sp - 1] = JgsBuiltins.ScalarMod(stack[sp - 1], stack[sp]);
                    break;
                case BodyOp.Rem:
                    sp--;
                    stack[sp - 1] = JgsBuiltins.ScalarRem(stack[sp - 1], stack[sp]);
                    break;
                case BodyOp.Atan2:
                    sp--;
                    stack[sp - 1] = Math.Atan2(stack[sp - 1], stack[sp]);
                    break;
                case BodyOp.Store:
                    dy[op.Arg] = stack[--sp];
                    break;
            }
        }

        return true;
    }
}

internal sealed partial class Interpreter
{
    /// <summary>What a body is compiled for, which decides what its parameters mean.</summary>
    private enum BodyShape
    {
        /// <summary><c>@(t, y)</c>: a scalar time and a state read as <c>y(k)</c>; one answer per state.</summary>
        Ode,

        /// <summary><c>@(x)</c> over a solver's point, read as <c>x(k)</c> (bare when it has one element); one answer.</summary>
        Point,

        /// <summary><c>@(x)</c> over a batch of points, element by element; one answer per point.</summary>
        Batch,
    }

    /// <summary>
    /// <paramref name="f"/>'s body compiled for a state of <paramref name="states"/> elements, or
    /// null when anything in it is outside the whitelist, the debugger is attached, or the switch
    /// is off. The captured values are read here: the snapshot cannot change while the solve runs,
    /// since nothing the whitelist admits can reach it.
    /// </summary>
    internal CompiledBody? TryCompileOdeBody(AnonymousFunction f, int states) =>
        JgsOdeCompile.Enabled ? TryCompileBody(f, BodyShape.Ode, states) : null;

    /// <summary>
    /// I2: <paramref name="f"/> as a solver's objective over a point of <paramref name="length"/>
    /// elements (one for <c>fzero</c>, <c>fminbnd</c> and an <c>arrayfun</c> element), answering one
    /// number; null when it is outside the whitelist.
    /// </summary>
    internal CompiledBody? TryCompilePointBody(AnonymousFunction f, int length) =>
        JgsCallbackCompile.Enabled ? TryCompileBody(f, BodyShape.Point, length) : null;

    /// <summary>
    /// I2: <paramref name="f"/> as an integrand asked for a batch of points at once and answering
    /// one value per point, run element by element. Only a body whose every operator is elementwise
    /// over the batch compiles: <c>x*x</c>, <c>x^2</c>, <c>1/x</c> refuse, since the walk answers
    /// them as matrix operations (or refuses them), and so does a body that does not read the batch,
    /// which the walk answers with one value for the whole panel.
    /// </summary>
    internal CompiledBody? TryCompileBatchBody(AnonymousFunction f) =>
        JgsCallbackCompile.Enabled ? TryCompileBody(f, BodyShape.Batch, 1) : null;

    private CompiledBody? TryCompileBody(AnonymousFunction f, BodyShape shape, int states)
    {
        AnonymousFnExpr declaration = f.Declaration;
        int parameters = shape == BodyShape.Ode ? 2 : 1;
        if (_hook is not null || states < 1
            || !declaration.Dialect.IsMatlab || !declaration.Dialect.MatlabFunctions
            || declaration.Parameters.Count != parameters
            || (parameters == 2 && declaration.Parameters[0] == declaration.Parameters[1])
            || declaration.Parameters[parameters - 1] is "varargin" or "nargin")
        {
            return null;
        }

        // A handle made in a loop is one declaration made many times: its program is learned once
        // and each new handle only reads its own captured values into it (I2, ADR 0325). Only a
        // program whose every name is a parameter or a captured variable is kept, so nothing about
        // the path or the layers can have changed under it; a refusal is kept when it is about the
        // body's shape alone.
        long key = ((long)shape << 32) | (uint)states;
        var templates = declaration.CompiledTemplates as System.Collections.Concurrent.ConcurrentDictionary<long, BodyTemplate>;
        if (templates is not null && templates.TryGetValue(key, out BodyTemplate? template))
        {
            if (template.Body is null)
            {
                return null;
            }

            if (template.Instantiate(f) is { } instance)
            {
                return instance;
            }
        }

        var builder = new BodyBuilder(this, f, shape, states);
        CompiledBody? built;
        using (EnterContext(f.Captured, f.File, f.Text, 0))
        using (EnterDialect(declaration.Dialect))
        {
            built = builder.Build();
        }

        if (built is not null ? builder.Cacheable : builder.StructuralRefusal)
        {
            templates ??= (System.Collections.Concurrent.ConcurrentDictionary<long, BodyTemplate>)(declaration.CompiledTemplates
                ??= new System.Collections.Concurrent.ConcurrentDictionary<long, BodyTemplate>());
            templates[key] = new BodyTemplate(built, builder.CapturedSlots);
        }

        return built;
    }

    /// <summary>
    /// A declaration's program for one shape, with the constant slots its captured names fill; or,
    /// with no body, the record that the declaration's shape refuses.
    /// </summary>
    private sealed class BodyTemplate(CompiledBody? body, (int Slot, string Name)[] captured)
    {
        public CompiledBody? Body { get; } = body;

        /// <summary>The program over <paramref name="f"/>'s own captured values, or null when one is no longer a real double scalar.</summary>
        public CompiledBody? Instantiate(AnonymousFunction f)
        {
            if (captured.Length == 0)
            {
                return Body;
            }

            double[] constants = Body!.CopyConstants();
            JgsEnvironment snapshot = f.Captured;
            foreach ((int slot, string name) in captured)
            {
                if (!snapshot.Locals.ContainsKey(name) || snapshot.IsFunctionBinding(name) || snapshot.IsGlobal(name)
                    || !snapshot.TryGet(name, out JgsValue value)
                    || value.Type != JgsType.Number || value.NumericClass != JgsNumericClass.Double)
                {
                    return null;
                }

                constants[slot] = value.AsNumber;
            }

            return Body.WithConstants(constants);
        }
    }

    /// <summary>The compiler behind the three entry points; one per handle and solve.</summary>
    private sealed class BodyBuilder
    {
        private readonly Interpreter _interpreter;
        private readonly AnonymousFunction _f;
        private readonly BodyShape _shape;
        private readonly int _states;
        private readonly List<BodyInstruction> _ops = [];
        private readonly List<double> _constants = [];
        private readonly List<Func<double, double>> _unary = [];
        private readonly List<Func<double, bool>?> _guards = [];
        private readonly Dictionary<string, int> _unaryOf = new(StringComparer.Ordinal);
        private readonly string? _time;
        private readonly string? _state;
        private int _depth;
        private int _maxDepth;

        public BodyBuilder(Interpreter interpreter, AnonymousFunction f, BodyShape shape, int states)
        {
            _interpreter = interpreter;
            _f = f;
            _shape = shape;
            _states = states;
            IReadOnlyList<string> parameters = f.Declaration.Parameters;
            (_time, _state) = shape switch
            {
                BodyShape.Ode => (parameters[0], parameters[1]),
                BodyShape.Point => ((string?)null, parameters[0]),
                _ => (parameters[0], null),
            };
        }

        private readonly List<(int Slot, string Name)> _captured = [];

        private sealed class Refused(string what) : Exception(what);

        /// <summary>Whether the program reads nothing but its parameters and captured variables, so a template may keep it.</summary>
        public bool Cacheable { get; private set; } = true;

        /// <summary>Whether the refusal is about the body's shape alone, whatever the handle captured and wherever it runs.</summary>
        public bool StructuralRefusal { get; private set; }

        /// <summary>The constant slots captured names filled, for a template.</summary>
        public (int Slot, string Name)[] CapturedSlots => [.. _captured];

        /// <summary>The refusal, written to standard error under the shape's trace switch, as the loop compiler's trace is.</summary>
        private Refused Refuse(string what, bool structural = true)
        {
            StructuralRefusal = structural;
            if (_shape == BodyShape.Ode ? JgsOdeCompile.Trace : JgsCallbackCompile.Trace)
            {
                Console.Error.WriteLine($"[{(_shape == BodyShape.Ode ? "ode" : "fn")} compile] refused {_f.Text}: {what}");
            }

            return new Refused(what);
        }

        public CompiledBody? Build()
        {
            try
            {
                Expr body = _f.Declaration.Body;
                IReadOnlyList<Expr> elements = body switch
                {
                    MatrixLiteral or ArrayLiteral or CellLiteral when _shape == BodyShape.Batch => throw Refuse("a bracket around a batch"),
                    MatrixLiteral { Rows.Count: 1 } row => row.Rows[0],
                    MatrixLiteral column when column.Rows.All(r => r.Count == 1) => column.Rows.Select(r => r[0]).ToArray(),
                    ArrayLiteral row => row.Elements,
                    MatrixLiteral or ArrayLiteral or CellLiteral => throw Refuse("a nested bracket"),
                    _ => [body],
                };

                int outputs = _shape == BodyShape.Ode ? _states : 1;
                if (elements.Count != outputs)
                {
                    StructuralRefusal = true;
                    return null; // the walk raises the size refusal in the caller's words
                }

                for (int k = 0; k < elements.Count; k++)
                {
                    bool varies = Emit(elements[k]);
                    if (_shape == BodyShape.Batch && !varies)
                    {
                        // One value for a whole panel: the walk's answer is a scalar, which the
                        // integrand's caller refuses in its own words.
                        throw Refuse("a body that does not read the batch");
                    }

                    _ops.Add(new BodyInstruction(BodyOp.Store, k));
                    _depth--;
                }

                return new CompiledBody([.. _ops], [.. _constants], [.. _unary], [.. _guards], _maxDepth, outputs);
            }
            catch (Refused)
            {
                return null;
            }
        }

        private void Push(BodyOp code, int arg = 0)
        {
            _ops.Add(new BodyInstruction(code, arg));
            _maxDepth = Math.Max(_maxDepth, ++_depth);
        }

        private void Apply(BodyOp code)
        {
            _ops.Add(new BodyInstruction(code, 0));
            _depth--;
        }

        private void Constant(double value)
        {
            _constants.Add(value);
            Push(BodyOp.Const, _constants.Count - 1);
        }

        /// <summary>
        /// Emits <paramref name="expression"/>; true when its value depends on the batch's element
        /// (only a batch body asks: every other shape's operands are scalars).
        /// </summary>
        private bool Emit(Expr expression)
        {
            switch (expression)
            {
                case NumberLiteral { IntegerClass: null } number:
                    Constant(number.Value);
                    return false;

                case VariableExpr variable:
                    return EmitName(variable.Name);

                case UnaryExpr { Op: TokenType.Minus } minus:
                {
                    bool varies = Emit(minus.Operand);
                    _ops.Add(new BodyInstruction(BodyOp.Neg, 0));
                    return varies;
                }

                case UnaryExpr { Op: TokenType.Plus } plus:
                    return Emit(plus.Operand);

                case BinaryExpr binary:
                    return EmitBinary(binary);

                case CallExpr { Callee: VariableExpr callee } call:
                    return EmitCall(callee.Name, call.Arguments);

                default:
                    throw Refuse("an expression outside the whitelist");
            }
        }

        private bool EmitBinary(BinaryExpr binary)
        {
            BodyOp code = binary.Op switch
            {
                TokenType.Plus => BodyOp.Add,
                TokenType.Minus => BodyOp.Sub,
                TokenType.Star or TokenType.DotStar => BodyOp.Mul,
                TokenType.Slash or TokenType.DotSlash or TokenType.Backslash or TokenType.DotBackslash => BodyOp.Div,
                TokenType.Caret or TokenType.DotCaret => BodyOp.Pow,
                _ => throw Refuse("an operator outside the whitelist"),
            };

            // a \ b over scalars is b / a: the swap the walk itself makes.
            bool swap = binary.Op is TokenType.Backslash or TokenType.DotBackslash;
            bool left = Emit(swap ? binary.Right : binary.Left);
            bool right = Emit(swap ? binary.Left : binary.Right);
            if (_shape == BodyShape.Batch)
            {
                // Over a batch the undotted operators are matrix operations: elementwise only with a
                // scalar on the side that makes them so (k*x, x/k, k\x), and ^ never.
                bool elementwise = binary.Op switch
                {
                    TokenType.Star => !(left && right),
                    TokenType.Slash or TokenType.Backslash => !right, // after the swap, the divisor is on the right
                    TokenType.Caret => !left && !right,
                    _ => true,
                };

                if (!elementwise)
                {
                    throw Refuse("a matrix operator over the batch");
                }
            }

            Apply(code);
            return left || right;
        }

        private bool EmitName(string name)
        {
            if (name == _time)
            {
                Push(BodyOp.Time);
                return true;
            }

            if (name == _state)
            {
                // The whole state is a scalar only when it has one element.
                if (_states != 1)
                {
                    throw Refuse("the whole state where a scalar is wanted");
                }

                Push(BodyOp.State, 0);
                return false;
            }

            if (name == "nargin")
            {
                throw Refuse("nargin");
            }

            JgsEnvironment captured = _f.Captured;
            if (captured.Locals.ContainsKey(name))
            {
                if (captured.IsFunctionBinding(name) || captured.IsGlobal(name)
                    || !captured.TryGet(name, out JgsValue value)
                    || value.Type != JgsType.Number || value.NumericClass != JgsNumericClass.Double)
                {
                    throw Refuse("a captured value that is not a real double scalar", structural: false);
                }

                Constant(value.AsNumber);
                _captured.Add((_constants.Count - 1, name));
                return false;
            }

            // A constant builtin mentioned bare, folded once as the compiled loop folds it: the layer
            // holds most of them as the number itself, the rest as a function called with nothing.
            if (JgsBuiltins.IsHotLoopBareConstant(name)
                && _interpreter._resolver.Lookup(name, captured) is { Layer: ResolutionLayer.Builtin } held
                && held.Value.Type == JgsType.Number && held.Value.NumericClass == JgsNumericClass.Double)
            {
                Cacheable = false;
                Constant(held.Value.AsNumber);
                return false;
            }

            if (JgsBuiltins.IsHotLoopBareConstant(name)
                && _interpreter._resolver.CompiledBuiltin(name, captured, bare: true) is BuiltinFunction { AutoCallsBare: true } constant)
            {
                JgsValue answer = constant.Call(Array.Empty<JgsValue>(), 0, 0);
                if (answer.Type != JgsType.Number || answer.NumericClass != JgsNumericClass.Double)
                {
                    throw Refuse("a bare constant that does not answer a double", structural: false);
                }

                Cacheable = false;
                Constant(answer.AsNumber);
                return false;
            }

            throw Refuse("a name that is not a parameter, a captured scalar or a constant", structural: false);
        }

        private bool EmitCall(string name, IReadOnlyList<Expr> arguments)
        {
            if (name == _state)
            {
                // y(k) with a literal k inside the state: the one read the body may make of it.
                if (arguments.Count != 1 || arguments[0] is not NumberLiteral { IntegerClass: null } index
                    || index.Value != Math.Floor(index.Value) || index.Value < 1 || index.Value > _states)
                {
                    throw Refuse("a read of the state that is not y(k) with a literal k inside it");
                }

                Push(BodyOp.State, (int)index.Value - 1);
                return false;
            }

            if (name == _time || name == "nargin" || _f.Captured.Locals.ContainsKey(name)
                || _interpreter._resolver.CompiledBuiltin(name, _f.Captured, bare: false) is null)
            {
                throw Refuse("an index into a captured value, or a call that does not reach the builtin", structural: false);
            }

            Cacheable = false; // a builtin is the path's answer at this moment, asked again for every handle

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

                bool varies = Emit(arguments[0]);
                _ops.Add(new BodyInstruction(staysReal is null ? BodyOp.Call1 : BodyOp.Call1G, slot));
                return varies;
            }

            BodyOp? binary = arguments.Count == 2
                ? name switch
                {
                    "mod" => BodyOp.Mod,
                    "rem" => BodyOp.Rem,
                    "atan2" => BodyOp.Atan2,
                    _ => null,
                }
                : null;
            if (binary is not { } op)
            {
                throw Refuse("a call outside the whitelist");
            }

            bool first = Emit(arguments[0]);
            bool second = Emit(arguments[1]);
            if (_shape == BodyShape.Batch && second && op != BodyOp.Atan2)
            {
                // mod and rem over a batch take one divisor for the whole of it.
                throw Refuse("a divisor that varies over the batch");
            }

            Apply(op);
            return first || second;
        }
    }
}
