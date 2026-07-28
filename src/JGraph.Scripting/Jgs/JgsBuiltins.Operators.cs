namespace JGraph.Scripting.Jgs;

/// <summary>
/// The function forms of the operators — MATLAB's <c>plus(a, b)</c> for <c>a + b</c>, through to
/// <c>mldivide</c> for <c>a \ b</c>. Each one hands straight back to the interpreter's own operator
/// evaluation, so there is exactly one definition of what <c>+</c> or <c>\</c> means: a second
/// implementation here would fork the semantics the day the first one changed.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Every name <see cref="RegisterOperatorFunctions"/> declares. The interpreter owns these (only
    /// it can apply an operator), so they are not seeded by <c>CreateGlobals</c> and the editor's
    /// name list has to be told about them the same way it is told about <c>run</c> and <c>clear</c>.
    /// </summary>
    internal static IReadOnlyList<string> OperatorFunctionNames { get; } =
    [
        "plus", "minus", "times", "mtimes", "rdivide", "ldivide", "mrdivide", "mldivide",
        "power", "mpower", "uminus", "uplus",
        "eq", "ne", "lt", "le", "gt", "ge", "xor", "colon",
    ];

    /// <summary>
    /// Declares the operator function forms into <paramref name="env"/>, evaluated by
    /// <paramref name="interpreter"/>.
    /// </summary>
    internal static void RegisterOperatorFunctions(JgsEnvironment env, Interpreter interpreter)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void Binary(string name, TokenType op) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 2, line, col);
                return interpreter.ApplyOperator(op, args[0], args[1], line, col);
            });

        Binary("plus", TokenType.Plus);
        Binary("minus", TokenType.Minus);
        Binary("times", TokenType.DotStar);
        Binary("mtimes", TokenType.Star);
        Binary("rdivide", TokenType.DotSlash);
        Binary("ldivide", TokenType.DotBackslash);
        Binary("mrdivide", TokenType.Slash);
        Binary("mldivide", TokenType.Backslash);
        Binary("power", TokenType.DotCaret);
        Binary("mpower", TokenType.Caret);
        Binary("eq", TokenType.EqualEqual);
        Binary("ne", TokenType.BangEqual);
        Binary("lt", TokenType.Less);
        Binary("le", TokenType.LessEqual);
        Binary("gt", TokenType.Greater);
        Binary("ge", TokenType.GreaterEqual);

        // Negation and unary plus are the binary operators against zero, which keeps array
        // broadcasting and the logical-to-double promotion (uplus(true) is 1) in one place.
        Define("uminus", (args, line, col) =>
        {
            Arity("uminus", args, 1, line, col);
            return interpreter.ApplyOperator(TokenType.Minus, JgsValue.Number(0), args[0], line, col);
        });

        Define("uplus", (args, line, col) =>
        {
            Arity("uplus", args, 1, line, col);
            return interpreter.ApplyOperator(TokenType.Plus, JgsValue.Number(0), args[0], line, col);
        });

        // xor has no operator to defer to; it is the one member of the family defined here.
        Define("xor", (args, line, col) => Logical2("xor", args, line, col, static (a, b) => a ^ b));

        Define("colon", (args, line, col) =>
        {
            ArityRange("colon", args, 2, 3, line, col);
            return args.Count == 2
                ? interpreter.BuildRange(args[0], null, args[1], line, col)
                : interpreter.BuildRange(args[0], args[1], args[2], line, col);
        });
    }
}
