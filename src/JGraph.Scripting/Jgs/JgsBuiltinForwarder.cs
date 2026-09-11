namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's <c>builtin(name, args…)</c> (M145, step 7): the built-in of that name called past
/// whatever shadows it. Registered under its own name like any other built-in — so a
/// <c>builtin.m</c> or a variable called <c>builtin</c> takes the name, as in R2025b — but not a
/// <see cref="BuiltinFunction"/> wrapping a delegate, because everything it does is decided by the
/// target it forwards to.
/// </summary>
/// <remarks>
/// <para>
/// The name is looked up in the built-in layer and nowhere else, so a script's own <c>size</c>
/// cannot intercept <c>builtin('size', A)</c>. The arguments after the name go to the target as
/// written — the target's own <c>Call</c> applies its own string demotion, so
/// <c>builtin('class', "abc")</c> and <c>class("abc")</c> agree. The output count is forwarded,
/// zero included: <c>[m, n] = builtin('size', A)</c> reaches the target's multi-output body, and
/// <c>builtin('ecdf', x);</c> as a statement tells <c>ecdf</c> nobody wanted the numbers, so it
/// draws. Whether a bare statement binds <c>ans</c> is the target's flag, not this one's.
/// </para>
/// <para>
/// The call site is forwarded too: for the length of the forwarded call the interpreter's pending
/// call is the written call with the selector removed, so the <c>table</c> wrapper reads its
/// variable names from <c>builtin('table', A, B)</c> as it does from <c>table(A, B)</c>. The
/// previous pending call is restored afterwards, so a nested or re-entrant call sees its own.
/// </para>
/// <para>
/// A name the layer does not hold errors with MATLAB's words and identifier. <c>builtin('mean', x)</c>
/// works here and errors in MATLAB, whose <c>mean</c> is a <c>.m</c> file: reaching JGraph's own
/// implementation is the whole point of the call for someone who has shadowed the name, and the
/// divergence is recorded rather than imitated. The target's own exceptions pass through untouched.
/// </para>
/// </remarks>
internal sealed class JgsBuiltinForwarder : IJgsCallable, IJgsMultiCallable
{
    private readonly Interpreter _interpreter;

    /// <summary>Creates the forwarder over <paramref name="interpreter"/>'s built-in layer.</summary>
    public JgsBuiltinForwarder(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    /// <inheritdoc />
    public string Name => "builtin";

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        CallMultiple(arguments, wanted: 1, line, column)[0];

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        (IJgsCallable target, JgsValue[] rest, CallExpr? call) = Prepare(arguments, line, column);
        return _interpreter.WithPendingCall(call, () =>
            target is IJgsMultiCallable several
                ? several.CallMultiple(rest, wanted, line, column)
                : [target.Call(rest, line, column)]);
    }

    /// <summary>
    /// Runs the forwarded call as a bare statement, the way the interpreter runs the target's own
    /// name as one: a target that draws when nobody wants its answer is told so, and otherwise the
    /// answer comes back in <paramref name="answer"/> with true when the target would have bound it
    /// to <c>ans</c>.
    /// </summary>
    public bool CallAsStatement(IReadOnlyList<JgsValue> arguments, int line, int column, out JgsValue answer)
    {
        (IJgsCallable target, JgsValue[] rest, CallExpr? call) = Prepare(arguments, line, column);
        if (target is BuiltinFunction { KnowsWhenDiscarded: true, MultiOutput: not null } drawing)
        {
            _interpreter.WithPendingCall(call, () =>
            {
                drawing.CallDiscarded(rest, line, column);
                return true;
            });
            answer = JgsValue.Null;
            return false;
        }

        answer = _interpreter.WithPendingCall(call, () => target.Call(rest, line, column));
        return target is not BuiltinFunction builtin || builtin.BindsAnsAsStatement;
    }

    private (IJgsCallable Target, JgsValue[] Remaining, CallExpr? Call) Prepare(
        IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        if (arguments.Count == 0)
        {
            throw new JgsRuntimeException(line, column, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (!JgsBuiltins.IsTextScalar(arguments[0]))
        {
            throw new JgsRuntimeException(line, column,
                "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
        }

        string name = JgsBuiltins.TextOf(arguments[0]);
        JgsValue? held = _interpreter.Resolver.BuiltinOf(name);
        if (held is null)
        {
            throw new JgsRuntimeException(line, column,
                "MATLAB:dispatcher:CannotFindBuiltinFunction", $"Cannot find built-in function '{name}'");
        }

        var rest = new JgsValue[arguments.Count - 1];
        for (int i = 1; i < arguments.Count; i++)
        {
            rest[i - 1] = arguments[i];
        }

        // A constant the layer holds as a value — pi, i, newline — is what MATLAB's zero-argument
        // function of that name answers.
        IJgsCallable target = held.Type == JgsType.Function
            ? held.AsCallable
            : new Constant(name, held);

        // The written call, when the interpreter still holds it, minus the selector. A stale call
        // from an earlier statement is not taken: it must be a call of this name with this many
        // arguments, which is what a written builtin(…) reaching here has.
        CallExpr? call = _interpreter.PendingCall is { Callee: VariableExpr { Name: "builtin" } } written
            && written.Arguments.Count == arguments.Count
            ? new CallExpr(
                new VariableExpr(name) { Line = written.Callee.Line, Column = written.Callee.Column },
                written.Arguments.Skip(1).ToArray())
            { Line = written.Line, Column = written.Column }
            : null;

        return (target, rest, call);
    }

    /// <summary>A built-in constant standing in as the zero-argument function MATLAB has under the name.</summary>
    private sealed class Constant(string name, JgsValue value) : IJgsCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
        {
            if (arguments.Count > 0)
            {
                throw new JgsRuntimeException(line, column, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            return value;
        }
    }
}
