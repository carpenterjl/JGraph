namespace JGraph.Scripting.Jgs;

/// <summary>
/// A comma-separated list where one value is wanted, in the MATLAB dialect (open item 98). M61 gave
/// the list its places to spread — an argument list, a bracket, a cell literal, a multiple
/// assignment; this is the rest of R2025b's rule, for <c>s.f</c> on a struct array and <c>c{...}</c>
/// naming other than one slot. What happens depends on where the list stands, measured in
/// probe_98, probe_98b and probe_98c:
/// <list type="bullet">
/// <item>an assignment, a parenthesis or an anonymous function's answer takes the first value, and
/// refuses a list of none;</item>
/// <item>an operator is a call of plus, uminus or transpose with the list as its arguments, so too
/// many or too few is MATLAB:maxrhs or MATLAB:minrhs;</item>
/// <item>a condition (if, while, switch, for, &amp;&amp;, ||) takes exactly one;</item>
/// <item>a subscript, a dot or a brace after the list takes exactly one;</item>
/// <item>a subscript list and a colon spread it, as an argument list does.</item>
/// </list>
/// The JGS dialect keeps its own reading: a struct array's field is the collected row (frozen since
/// M41), and a brace naming several is refused in its words.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>Where a comma-separated list stands when one value is wanted.</summary>
    private enum ListUse
    {
        /// <summary>The right side of an assignment, a parenthesis: the first value.</summary>
        Value,

        /// <summary>An anonymous function's body asked for its answer: the first value.</summary>
        Returned,

        /// <summary>An operand of an operator: a call with the list as its arguments.</summary>
        Operand,

        /// <summary>The head of an if, while, switch or for, or a side of &amp;&amp; or ||.</summary>
        Condition,

        /// <summary>A target that a subscript, a dot or a brace goes on to index.</summary>
        Intermediate,
    }

    /// <summary>
    /// Evaluates an expression where one value is wanted, saying where it stands so that a dot or a
    /// brace naming a list of other than one answers as R2025b's does there.
    /// </summary>
    private JgsValue EvaluateOne(Expr expr, JgsEnvironment env, ListUse use) => !Dialect.IsMatlab
        ? Evaluate(expr, env)
        : expr switch
        {
            MemberExpr member => EvaluateMember(member, env, use: use),
            BraceIndexExpr brace => EvaluateBraceIndex(brace, env, use),
            _ => Evaluate(expr, env),
        };

    /// <summary>
    /// The one value a list of <paramref name="count"/> gives where it stands, or R2025b's refusal
    /// there. <paramref name="brace"/> says which kind of indexing made the list, which is in the
    /// refusals' words.
    /// </summary>
    private static JgsValue OneOfList(int count, JgsValue? first, Node at, bool brace, ListUse use)
    {
        string kind = brace ? "brace '{}'" : "dot '.'";
        switch (use)
        {
            case ListUse.Value or ListUse.Returned when count > 0:
                return first!;

            case ListUse.Value when brace:
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:TooManyOutputsDueToMissingBraces",
                    "Unable to perform assignment with 0 elements on the right-hand side.");

            case ListUse.Value or ListUse.Returned:
                throw NeedMoreOutputs(at);

            case ListUse.Operand:
                throw count == 0
                    ? new JgsRuntimeException(at.Line, at.Column, "MATLAB:minrhs", "Not enough input arguments.")
                    : new JgsRuntimeException(at.Line, at.Column, "MATLAB:maxrhs", "Too many input arguments.");

            case ListUse.Condition:
                if (count == 1)
                {
                    return first!;
                }

                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:index:expected_one_output_from_reference",
                    $"A {kind} indexing expression produced a comma-separated list with {count} values where only a single value is allowed.");

            default:
                if (count == 1)
                {
                    return first!;
                }

                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:index:expected_one_output_from_intermediate_indexing",
                    $"Intermediate {kind} indexing produced a comma-separated list with {count} values, but it must produce a single value when followed by subsequent indexing operations.");
        }
    }

    private static JgsRuntimeException NeedMoreOutputs(Node at) => new(at.Line, at.Column, "MATLAB:needMoreRhsOutputs",
        "Insufficient number of outputs from right hand side of equal sign to satisfy assignment.");

    /// <summary>
    /// A subscript list with every comma-separated list in it spread into its place, as R2025b reads
    /// <c>A(idx{:})</c> and <c>m(s.f)</c> on a 1-by-2 struct array (<c>m(1, 2)</c>): the MATLAB
    /// dialect's only, and the same list back when nothing in it is one. A list item is evaluated
    /// here, the rest are left to the index read, so an <c>end</c> among them still counts its slot
    /// after the spreading.
    /// </summary>
    private IReadOnlyList<Expr> SpreadSubscripts(IReadOnlyList<Expr> subscripts, JgsEnvironment env)
    {
        if (!Dialect.IsMatlab)
        {
            return subscripts;
        }

        int first = -1;
        for (int i = 0; i < subscripts.Count; i++)
        {
            if (IsListSubscript(subscripts[i], env))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return subscripts;
        }

        var spread = new List<Expr>(subscripts.Count + 2);
        for (int i = 0; i < subscripts.Count; i++)
        {
            Expr subscript = subscripts[i];
            if (i < first || !IsListSubscript(subscript, env))
            {
                spread.Add(subscript);
                continue;
            }

            foreach (JgsValue value in EvaluateSpread(subscript, env))
            {
                spread.Add(new PreEvaluated(value) { Line = subscript.Line, Column = subscript.Column });
            }
        }

        return spread;
    }

    /// <summary>
    /// Whether a subscript is a list of other than one, told cheaply: a brace or a dot on a variable
    /// is peeked at (<see cref="ListLength"/>), and a brace on anything else is a list worth spreading.
    /// </summary>
    private bool IsListSubscript(Expr subscript, JgsEnvironment env) => subscript switch
    {
        BraceIndexExpr { Target: VariableExpr } or MemberExpr => ListLength(subscript, env) != 1,
        BraceIndexExpr => true,
        _ => false,
    };

    /// <summary>
    /// <c>a:b</c> or <c>a:s:b</c> whose parts hold a list, MATLAB dialect: the parts are colon's
    /// arguments, so <c>1:s.f</c> on a 1-by-2 struct array is <c>colon(1, 1, 2)</c> and on an empty
    /// one is a call of colon with one argument (probe_98b, probe_98c). Null when no part is a list.
    /// </summary>
    private JgsValue? RangeOfList(RangeExpr range, JgsEnvironment env)
    {
        if (!Dialect.IsMatlab
            || !(IsListSubscript(range.Start, env) || IsListSubscript(range.Stop, env)
                || (range.Step is { } step && IsListSubscript(step, env))))
        {
            return null;
        }

        List<Expr> parts = range.Step is null ? [range.Start, range.Stop] : [range.Start, range.Step, range.Stop];
        JgsValue[] given = EvaluateAll(parts, env);
        return given.Length switch
        {
            < 2 => throw new JgsRuntimeException(range.Line, range.Column, "MATLAB:minrhs", "Not enough input arguments."),
            2 => RangeFromValues(range, given[0], JgsValue.Number(1), given[1]),
            3 => RangeFromValues(range, given[0], given[1], given[2]),
            _ => throw new JgsRuntimeException(range.Line, range.Column, "MATLAB:maxrhs", "Too many input arguments."),
        };
    }
}
