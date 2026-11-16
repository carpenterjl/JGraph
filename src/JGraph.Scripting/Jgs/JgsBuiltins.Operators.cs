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

    /// <summary>The verb each MATLAB-dialect <c>and</c>/<c>or</c>/<c>not</c> wrapper stands over (ADR 0379).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BuiltinFunction, BuiltinFunction> McLogicalVerbOriginals = new();

    /// <summary>
    /// Declares the operator function forms into <paramref name="env"/>, evaluated by
    /// <paramref name="interpreter"/>.
    /// </summary>
    internal static void RegisterOperatorFunctions(JgsEnvironment env, Interpreter interpreter)
    {
        // The operators tell a string from a char themselves (plus("abc", 5) is the string "abc5",
        // measured), so a string scalar must reach them as the string it is.
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

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
            if (interpreter.Dialect.IsMatlab && IsCalendarDuration(args[0]))
            {
                return CalendarSign(args[0], negate: true); // open item 347, ADR 0384
            }

            // -S of a sparse matrix is sparse, as the unary operator answers it (0 - S is not; ADR 0374).
            if (args[0].Type == JgsType.Sparse)
            {
                return SparseNegated(args[0]);
            }

            if (interpreter.Dialect.IsMatlab && Interpreter.RefusesUnarySign(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical",
                    "Invalid data type. Argument must be numeric, char, or logical."); // open item 341, ADR 0377
            }

            return interpreter.ApplyOperator(TokenType.Minus, JgsValue.Number(0), args[0], line, col);
        });

        Define("uplus", (args, line, col) =>
        {
            Arity("uplus", args, 1, line, col);
            if (interpreter.Dialect.IsMatlab && IsCalendarDuration(args[0]))
            {
                return args[0]; // open item 347, ADR 0384
            }

            // +S is S; +L of a logical one is double (c_uplus_l; ADR 0374).
            if (args[0].Type == JgsType.Sparse)
            {
                return args[0].IsLogicalSparse ? JgsValue.Sparse(args[0].AsSparse) : args[0];
            }

            // MATLAB's uplus is the + operator's, which refuses a string rather than reading its
            // codes (open item 341, ADR 0377); JGS keeps 0 + x.
            return interpreter.Dialect.IsMatlab
                ? interpreter.ApplyUplus(args[0], line, col)
                : interpreter.ApplyOperator(TokenType.Plus, JgsValue.Number(0), args[0], line, col);
        });

        // xor has no operator to defer to; it is the one member of the family defined here.
        Define("xor", (args, line, col) =>
        {
            // MATLAB code: the operators' own refusals, expansion and logical empties (open items 340, 341; ADR 0379).
            if (JgsRunningDialect.ThreadIsMatlab && args.Count == 2 && args[0].Type != JgsType.Sparse && args[1].Type != JgsType.Sparse
                && args[0].Type != JgsType.External && args[1].Type != JgsType.External)
            {
                return interpreter.ApplyMatlabXor(args[0], args[1], line, col);
            }

            // Of sparse matrices, a logical sparse matrix (open item 331, R2025b's l_xor); a sparse
            // matrix beside a dense value reads full and answers dense (b_xor_f, ADR 0374).
            if (args.Count == 2 && (args[0].Type == JgsType.Sparse || args[1].Type == JgsType.Sparse))
            {
                JgsValue[] read = [.. args.Select(static a => a.Type == JgsType.Sparse ? SparseDense(a) : a)];
                JgsValue full = LogicalVerbShaped(Logical2("xor", read, line, col, static (a, b) => a ^ b), read);
                return args[0].Type == JgsType.Sparse && args[1].Type == JgsType.Sparse ? DenseAsSparse(full, "xor", line, col) : full;
            }

            return Logical2("xor", args, line, col, static (a, b) => a ^ b);
        });

        // and, or and not are the operators & | ~ in MATLAB code, refusals and logical empties included
        // (open items 340, 341; ADR 0379); JGS code keeps the verbs CreateGlobals declared.
        foreach ((string name, TokenType op) in new[] { ("and", TokenType.Amp), ("or", TokenType.Pipe), ("not", TokenType.Bang) })
        {
            if (!env.Builtins.TryGet(name, out JgsValue declared) || declared.AsCallable is not BuiltinFunction found)
            {
                continue;
            }

            // A second interpreter over the same globals wraps the verb CreateGlobals declared, not this wrapper.
            BuiltinFunction jgs = McLogicalVerbOriginals.TryGetValue(found, out BuiltinFunction? original) ? original : found;
            var wrapper = new BuiltinFunction(name, (args, line, col) =>
                !JgsRunningDialect.ThreadIsMatlab || args.Any(static a => a.Type == JgsType.External) ? jgs.Call(args, line, col)
                : op == TokenType.Bang && args.Count == 1 ? interpreter.ApplyMatlabNot(args[0], line, col)
                : op != TokenType.Bang && args.Count == 2 ? interpreter.ApplyOperator(op, args[0], args[1], line, col)
                : jgs.Call(args, line, col))
            {
                KeepsStringArguments = jgs.KeepsStringArguments,
                BindsAnsAsStatement = jgs.BindsAnsAsStatement,
                AutoCallsBare = jgs.AutoCallsBare,
            };
            McLogicalVerbOriginals.AddOrUpdate(wrapper, jgs);
            env.Builtins.Register(name, JgsValue.Function(wrapper));
        }

        Define("colon", (args, line, col) =>
        {
            ArityRange("colon", args, 2, 3, line, col);
            return args.Count == 2
                ? interpreter.BuildRange(args[0], null, args[1], line, col)
                : interpreter.BuildRange(args[0], args[1], args[2], line, col);
        });
    }
}
