namespace JGraph.Scripting.Jgs;

/// <summary>Something a JGS script can call: a built-in or a user-defined <c>fn</c>.</summary>
internal interface IJgsCallable
{
    /// <summary>The name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Invokes the callable with already-evaluated <paramref name="arguments"/>. The
    /// <paramref name="line"/>/<paramref name="column"/> locate the call site for error reporting.
    /// </summary>
    JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column);
}

/// <summary>
/// Something that can produce several values at once, for MATLAB's <c>[a, b] = f(x)</c>. A callable
/// that does not implement this simply produces its single value.
/// </summary>
internal interface IJgsMultiCallable
{
    /// <summary>
    /// Invokes the callable asking for <paramref name="wanted"/> outputs. It may return fewer (the
    /// caller reports the shortfall) but never more than it can produce.
    /// </summary>
    JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column);
}

/// <summary>A built-in function implemented in C#, exposed to scripts by name.</summary>
internal sealed class BuiltinFunction : IJgsCallable, IJgsMultiCallable
{
    private readonly Func<IReadOnlyList<JgsValue>, int, int, JgsValue> _implementation;

    /// <summary>Creates a built-in named <paramref name="name"/> backed by <paramref name="implementation"/>.</summary>
    public BuiltinFunction(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> implementation)
    {
        Name = name;
        _implementation = implementation;

        // Decided from the name at mint time rather than stamped onto the environment afterwards
        // (M63). Several builtins are declared twice — an inner one that does the work and an outer
        // wrapper that adds a second output — and only the wrapper is reachable from the environment.
        // Marking by name reaches both; marking the environment reached the wrapper alone, which is
        // how size("abc") came back 1-by-3 while numel("abc") correctly came back 1.
        KeepsStringArguments = JgsBuiltins.StringAwareBuiltins.Contains(name);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Whether a bare call statement binds this built-in's result to <c>ans</c> and echoes it.
    /// MATLAB's <c>figure(1)</c> as a statement prints nothing, while <c>h = figure(1)</c> still hands
    /// back the handle; with no nargout plumbing in the interpreter, this flag is how the two differ.
    /// </summary>
    public bool BindsAnsAsStatement { get; init; } = true;

    /// <summary>
    /// Whether a bare mention of this name means its value rather than the function itself, so
    /// <c>x = eps</c> stores 2.2e-16 and not a handle. MATLAB implements its constants as
    /// zero-argument functions and calls them on sight; the flag opts a builtin into that without
    /// disturbing ordinary names, which stay callable values (<c>f = @sin</c>, <c>sin(x)</c>).
    /// </summary>
    public bool AutoCallsBare { get; init; }

    /// <summary>
    /// Optional multi-output implementation, for MATLAB's <c>[X, Y] = meshgrid(x, y)</c>: given
    /// (arguments, wanted, line, column), produces up to <c>wanted</c> outputs. Null for the many
    /// builtins that only ever produce one value — asking those for more reports a shortfall.
    /// </summary>
    public Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? MultiOutput { get; init; }

    /// <summary>
    /// Whether this builtin is told when its answer is being thrown away — a call written as a
    /// statement reaches <see cref="MultiOutput"/> with <c>wanted</c> zero rather than one.
    /// </summary>
    /// <remarks>
    /// Almost nothing needs this: a function that answers the same thing either way cannot tell the
    /// difference and should not ask. What needs it is the handful of names that <em>draw</em> when
    /// nobody wanted the numbers — <c>ecdf(x)</c> on its own is a plot, and <c>F = ecdf(x)</c> is a
    /// vector — which is a distinction that cannot be made after the fact.
    /// </remarks>
    public bool KnowsWhenDiscarded { get; init; }

    /// <summary>
    /// Whether this builtin wants its string arguments as they were written (M63). False for nearly
    /// everything, and that is the point: a string scalar arriving at an ordinary builtin is demoted
    /// to the char row it stands for, so <c>title("Speed")</c> and <c>plot(x, y, "LineWidth", 2)</c>
    /// went on working the day <c>"..."</c> stopped being a char, without any of the ~2,500 builtins
    /// being touched.
    /// </summary>
    /// <remarks>
    /// Set it on the names for which the difference between a string and a char <em>is</em> the
    /// answer — <c>class</c>, <c>isstring</c>, <c>ischar</c>, <c>strlength</c>, <c>string</c>,
    /// <c>char</c>, <c>numel</c> and the rest of the size family — and nowhere else. Only a string
    /// <em>scalar</em> is ever demoted; a string array of any other size is not a char row and is
    /// handed over as it is.
    /// </remarks>
    public bool KeepsStringArguments { get; set; }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        Guarded(() => _implementation(DemoteStringScalars(arguments), line, column), line, column);

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
        MultiOutput is { } multi && wanted > 1
            ? Guarded(() => multi(DemoteStringScalars(arguments), wanted, line, column), line, column)
            : [Call(arguments, line, column)];

    /// <summary>
    /// Runs a builtin's body and turns the argument-shaped exceptions the layers underneath throw
    /// into script errors a script can catch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numeric routines report a bad shape by throwing <see cref="ArgumentException"/> — "QR
    /// factorization here needs at least as many rows as columns" is a sentence written for a person
    /// to read. Nothing between here and <c>Main</c> caught it: the interpreter's <c>try</c> catches
    /// <see cref="JgsRuntimeException"/> alone and the runner catches <see cref="JgsException"/>
    /// alone, so <c>qr</c> of a wide matrix ended the process. A script must not be able to do that,
    /// whatever it passes.
    /// </para>
    /// <para>
    /// Only the four kinds that mean "these arguments are wrong" are translated. A
    /// <see cref="JgsException"/> is already a script error and passes through with the line it was
    /// raised at; a cancellation and a script's own <c>exit</c> are control flow and must reach the
    /// runner that is listening for them; anything else is a defect in this build rather than in the
    /// script, and is left to the runner's own last resort so that it is reported as one.
    /// </para>
    /// </remarks>
    private T Guarded<T>(Func<T> body, int line, int column)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or ArithmeticException or IndexOutOfRangeException or KeyNotFoundException
            or FormatException)
        {
            throw new JgsRuntimeException(line, column, $"{Name}: {MessageOf(ex)}");
        }
    }

    /// <summary>
    /// An exception's message without the parameter name .NET appends to it — "needs a square
    /// matrix. (Parameter 'matrix')" reads as an internal detail to someone writing a script.
    /// </summary>
    private static string MessageOf(Exception ex) =>
        ex is ArgumentException { ParamName: { Length: > 0 } name }
            ? ex.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal)
            : ex.Message;

    /// <summary>
    /// Replaces every string scalar in <paramref name="arguments"/> with its char row, allocating a
    /// new list only when there is one to replace — which is almost never, so the check costs a walk
    /// over a handful of already-hot values and nothing else.
    /// </summary>
    private IReadOnlyList<JgsValue> DemoteStringScalars(IReadOnlyList<JgsValue> arguments)
    {
        if (KeepsStringArguments)
        {
            return arguments;
        }

        int first = -1;
        for (int i = 0; i < arguments.Count; i++)
        {
            if (IsStringScalar(arguments[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return arguments;
        }

        var demoted = new JgsValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            demoted[i] = IsStringScalar(arguments[i]) ? arguments[i].ElementAt(0) : arguments[i];
        }

        return demoted;
    }

    /// <summary>Whether <paramref name="value"/> is a 1-by-1 string array — the string scalar.</summary>
    internal static bool IsStringScalar(JgsValue value) => value.IsStringArray && value.ArrayLength == 1;
}

/// <summary>A user-defined function: its parameters, body, and captured defining environment (a closure).</summary>
internal sealed class UserFunction : IJgsCallable, IJgsMultiCallable
{
    private readonly FnStmt _declaration;
    private readonly JgsEnvironment _closure;
    private readonly Interpreter _interpreter;

    /// <summary>Creates the function over its <paramref name="declaration"/> and captured <paramref name="closure"/>.</summary>
    public UserFunction(FnStmt declaration, JgsEnvironment closure, Interpreter interpreter)
    {
        _declaration = declaration;
        _closure = closure;
        _interpreter = interpreter;
    }

    /// <inheritdoc />
    public string Name => _declaration.Name;

    /// <summary>The <c>fn</c> declaration behind this function — the debugger uses it to refresh a
    /// function's body when its source file is live-edited.</summary>
    public FnStmt Declaration => _declaration;

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        JgsValue[] outputs = CallMultiple(arguments, wanted: 1, line, column);
        return outputs.Length > 0 ? outputs[0] : JgsValue.Null;
    }

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        IReadOnlyList<string> parameters = _declaration.Parameters;
        bool variadic = parameters.Count > 0 && parameters[^1] == "varargin";
        int fixedCount = variadic ? parameters.Count - 1 : parameters.Count;

        if (arguments.Count > fixedCount && !variadic)
        {
            throw new JgsRuntimeException(line, column,
                $"Function '{Name}' expects {parameters.Count} argument(s) but got {arguments.Count}.");
        }

        // MATLAB lets a caller pass fewer arguments than the header names and reports how many arrived
        // through nargin; JGS requires an exact match.
        if (arguments.Count < fixedCount && !_interpreter.Dialect.MatlabFunctions)
        {
            throw new JgsRuntimeException(line, column,
                $"Function '{Name}' expects {parameters.Count} argument(s) but got {arguments.Count}.");
        }

        // A frame opened outside every other frame is a MATLAB function's own workspace, and an
        // assignment inside it may not write out to whatever called it; a frame opened inside one
        // belongs to a nested function, which shares its parent's variables by design. JGS is a
        // lexically-scoped language whose closures deliberately write to what they captured, and its
        // surface is frozen — so the boundary is MATLAB's alone.
        var local = new JgsEnvironment(_closure)
        {
            IsCallBoundary = _interpreter.Dialect.MatlabFunctions && !_closure.IsCallBoundary,
        };
        for (int i = 0; i < fixedCount && i < arguments.Count; i++)
        {
            // Arguments are values in MATLAB: writing to a parameter must not reach the caller's array.
            local.Declare(parameters[i], _interpreter.CopyForBinding(arguments[i]));
        }

        if (variadic)
        {
            var rest = new JgsValue[Math.Max(0, arguments.Count - fixedCount)];
            for (int i = 0; i < rest.Length; i++)
            {
                rest[i] = _interpreter.CopyForBinding(arguments[fixedCount + i]);
            }

            local.Declare("varargin", JgsValue.Cell(rest));
        }

        if (_interpreter.Dialect.MatlabFunctions)
        {
            local.Declare("nargin", JgsValue.Number(arguments.Count));
            local.Declare("nargout", JgsValue.Number(wanted));
        }

        JgsValue returned = _interpreter.ExecuteFunctionBody(_declaration, local, line);

        // A JGS 'fn' hands back what it returned; a MATLAB 'function' hands back the values its named
        // outputs hold when it ends.
        if (_declaration.Outputs.Count == 0)
        {
            return returned.Type == JgsType.Null ? System.Array.Empty<JgsValue>() : [returned];
        }

        // A trailing 'varargout' is a cell holding as many outputs as the function chose to make, so
        // the count it can answer is not known from the header the way the named outputs are.
        IReadOnlyList<string> outputs = _declaration.Outputs;
        bool variadicOut = outputs[^1] == "varargout";
        int namedCount = variadicOut ? outputs.Count - 1 : outputs.Count;
        int produced = Math.Max(wanted, 1);
        if (!variadicOut)
        {
            produced = Math.Min(produced, outputs.Count);
        }

        var results = new List<JgsValue>(produced);
        for (int i = 0; i < namedCount && results.Count < produced; i++)
        {
            string output = outputs[i];
            if (!local.TryGet(output, out JgsValue value))
            {
                throw new JgsRuntimeException(line, column,
                    $"Function '{Name}' finished without assigning its output '{output}'.");
            }

            results.Add(value);
        }

        if (variadicOut && results.Count < produced)
        {
            // An unassigned varargout is an empty list, not a fault: a function that was asked for
            // only its named outputs never had reason to fill one.
            JgsValue[] rest = local.TryGet("varargout", out JgsValue packed) && packed.Type == JgsType.Cell
                ? packed.AsCell
                : System.Array.Empty<JgsValue>();

            for (int i = 0; i < rest.Length && results.Count < produced; i++)
            {
                results.Add(rest[i]);
            }
        }

        return [.. results];
    }
}

/// <summary>
/// A MATLAB anonymous function, <c>@(x) expr</c>. MATLAB captures the values of the free variables when
/// the handle is created, not when it is called, so the environment here is a snapshot: changing a
/// captured variable afterwards does not change what the handle computes.
/// </summary>
internal sealed class AnonymousFunction : IJgsCallable, IJgsMultiCallable
{
    private readonly AnonymousFnExpr _declaration;
    private readonly JgsEnvironment _captured;
    private readonly Interpreter _interpreter;

    private AnonymousFunction(AnonymousFnExpr declaration, JgsEnvironment captured, Interpreter interpreter)
    {
        _declaration = declaration;
        _captured = captured;
        _interpreter = interpreter;
    }

    /// <inheritdoc />
    public string Name => "@anonymous";

    /// <summary>The expression the handle was written as, which <c>func2str</c> prints back.</summary>
    public AnonymousFnExpr Declaration => _declaration;

    /// <summary>Creates the handle, snapshotting every name its body refers to that is not a parameter.</summary>
    public static AnonymousFunction Create(AnonymousFnExpr declaration, JgsEnvironment defining, Interpreter interpreter)
    {
        var snapshot = new JgsEnvironment(defining);
        foreach (string name in FreeNames(declaration))
        {
            if (defining.TryGet(name, out JgsValue value))
            {
                snapshot.Declare(name, value);
            }
        }

        return new AnonymousFunction(declaration, snapshot, interpreter);
    }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        JgsValue[] outputs = CallMultiple(arguments, wanted: 1, line, column);
        return outputs.Length > 0 ? outputs[0] : JgsValue.Null;
    }

    /// <summary>
    /// Invokes the handle asking for several outputs. A handle is a wrapper, not a function with
    /// outputs of its own, so the count passes through to whatever the body calls: that is what makes
    /// <c>[a, b] = f(x)</c> behave the same for <c>@minmax</c> and for <c>@(x) minmax(x)</c>, and it
    /// is what lets <c>cellfun</c> ask each element for two answers.
    /// </summary>
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        if (arguments.Count != _declaration.Parameters.Count)
        {
            throw new JgsRuntimeException(line, column,
                $"This anonymous function expects {_declaration.Parameters.Count} argument(s) but got {arguments.Count}.");
        }

        var local = new JgsEnvironment(_captured);
        for (int i = 0; i < arguments.Count; i++)
        {
            local.Declare(_declaration.Parameters[i], _interpreter.CopyForBinding(arguments[i]));
        }

        return _interpreter.EvaluateForOutputsIn(_declaration.Body, wanted, local);
    }

    /// <summary>Every identifier the body mentions apart from the parameters.</summary>
    private static IEnumerable<string> FreeNames(AnonymousFnExpr declaration)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Walk(declaration.Body, names);
        names.ExceptWith(declaration.Parameters);
        return names;
    }

    private static void Walk(Expr? expression, HashSet<string> names)
    {
        switch (expression)
        {
            case null:
                return;
            case VariableExpr variable:
                names.Add(variable.Name);
                return;
            case FunctionHandleExpr handle:
                names.Add(handle.Name);
                return;
            case UnaryExpr unary:
                Walk(unary.Operand, names);
                return;
            case TransposeExpr transpose:
                Walk(transpose.Operand, names);
                return;
            case BinaryExpr binary:
                Walk(binary.Left, names);
                Walk(binary.Right, names);
                return;
            case LogicalExpr logical:
                Walk(logical.Left, names);
                Walk(logical.Right, names);
                return;
            case RangeExpr range:
                Walk(range.Start, names);
                Walk(range.Step, names);
                Walk(range.Stop, names);
                return;
            case CallExpr call:
                Walk(call.Callee, names);
                WalkAll(call.Arguments, names);
                return;
            case IndexExpr index:
                Walk(index.Target, names);
                WalkAll(index.Indices, names);
                return;
            case BraceIndexExpr brace:
                Walk(brace.Target, names);
                WalkAll(brace.Indices, names);
                return;
            case MemberExpr member:
                Walk(member.Target, names);
                Walk(member.FieldName, names);
                return;
            case ArrayLiteral array:
                WalkAll(array.Elements, names);
                return;
            case MatrixLiteral matrix:
                foreach (IReadOnlyList<Expr> row in matrix.Rows)
                {
                    WalkAll(row, names);
                }

                return;
            case CellLiteral cell:
                foreach (IReadOnlyList<Expr> row in cell.Rows)
                {
                    WalkAll(row, names);
                }

                return;
            case AssignExpr assign:
                Walk(assign.Target, names);
                Walk(assign.Value, names);
                return;
            case AnonymousFnExpr nested:
                Walk(nested.Body, names);
                names.ExceptWith(nested.Parameters);
                return;
            default:
                return; // literals and 'end'/':' refer to nothing
        }
    }

    private static void WalkAll(IReadOnlyList<Expr> expressions, HashSet<string> names)
    {
        foreach (Expr expression in expressions)
        {
            Walk(expression, names);
        }
    }
}
