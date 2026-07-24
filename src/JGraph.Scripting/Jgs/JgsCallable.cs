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
internal sealed class BuiltinFunction : IJgsCallable
{
    private readonly Func<IReadOnlyList<JgsValue>, int, int, JgsValue> _implementation;

    /// <summary>Creates a built-in named <paramref name="name"/> backed by <paramref name="implementation"/>.</summary>
    public BuiltinFunction(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> implementation)
    {
        Name = name;
        _implementation = implementation;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        _implementation(arguments, line, column);
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

        var local = new JgsEnvironment(_closure);
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

        int produced = Math.Min(Math.Max(wanted, 1), _declaration.Outputs.Count);
        var results = new JgsValue[produced];
        for (int i = 0; i < produced; i++)
        {
            string output = _declaration.Outputs[i];
            if (!local.TryGet(output, out JgsValue value))
            {
                throw new JgsRuntimeException(line, column,
                    $"Function '{Name}' finished without assigning its output '{output}'.");
            }

            results[i] = value;
        }

        return results;
    }
}

/// <summary>
/// A MATLAB anonymous function, <c>@(x) expr</c>. MATLAB captures the values of the free variables when
/// the handle is created, not when it is called, so the environment here is a snapshot: changing a
/// captured variable afterwards does not change what the handle computes.
/// </summary>
internal sealed class AnonymousFunction : IJgsCallable
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

        return _interpreter.EvaluateIn(_declaration.Body, local);
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
