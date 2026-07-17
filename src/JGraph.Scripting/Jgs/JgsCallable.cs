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
internal sealed class UserFunction : IJgsCallable
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
        if (arguments.Count != _declaration.Parameters.Count)
        {
            throw new JgsRuntimeException(line, column,
                $"Function '{Name}' expects {_declaration.Parameters.Count} argument(s) but got {arguments.Count}.");
        }

        var local = new JgsEnvironment(_closure);
        for (int i = 0; i < arguments.Count; i++)
        {
            local.Declare(_declaration.Parameters[i], arguments[i]);
        }

        return _interpreter.ExecuteFunctionBody(_declaration, local, line);
    }
}
