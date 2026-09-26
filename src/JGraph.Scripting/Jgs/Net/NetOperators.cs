using System.Reflection;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// MATLAB's operators with a .NET value on either side (interop plan, stage 1, ADR 0174). An operator
/// the type overloads (<c>op_Equality</c>, <c>op_Addition</c>, …) is called with the other side
/// converted as any argument is, which is how <c>System.String('Hello') == 'Hello'</c> answers true.
/// Without an overload, <c>==</c> and <c>~=</c> compare identity for a reference type and
/// <c>Equals</c> for a value type (R2025b: <c>m == m</c> true, two equal-valued objects false), and
/// every other operator is refused.
/// </summary>
internal static class NetOperators
{
    public static JgsValue Apply(TokenType op, JgsValue left, JgsValue right, int line, int col)
    {
        string? special = op switch
        {
            TokenType.EqualEqual => "op_Equality",
            TokenType.BangEqual => "op_Inequality",
            TokenType.Plus => "op_Addition",
            TokenType.Minus => "op_Subtraction",
            TokenType.Star or TokenType.DotStar => "op_Multiply",
            TokenType.Slash or TokenType.DotSlash => "op_Division",
            TokenType.Less => "op_LessThan",
            TokenType.LessEqual => "op_LessThanOrEqual",
            TokenType.Greater => "op_GreaterThan",
            TokenType.GreaterEqual => "op_GreaterThanOrEqual",
            _ => null,
        };

        if (special is not null && Overload(special, left, right) is { } overload)
        {
            return overload.Call(line, col);
        }

        if (op is TokenType.EqualEqual or TokenType.BangEqual)
        {
            bool same = left.AsExternalOrNull() is NetObject a && right.AsExternalOrNull() is NetObject b
                && (a.IsHandle ? ReferenceEquals(a.Target, b.Target) : Equals(a.Target, b.Target));
            if (left.AsExternalOrNull() is { } x && right.AsExternalOrNull() is { } y && x is not NetObject)
            {
                same = ReferenceEquals(x, y);
            }

            return JgsValue.Bool(op == TokenType.EqualEqual ? same : !same);
        }

        string named = (left.Type == JgsType.External ? left : right).AsExternal.ClassName;
        throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
            $"Operator '{Symbol(op)}' is not supported for operands of type '{named}'.");
    }

    /// <summary>The overload of <paramref name="name"/> the operands' types declare that both reach, or null.</summary>
    private static Bound? Overload(string name, JgsValue left, JgsValue right)
    {
        var candidates = new List<MethodInfo>();
        foreach (JgsValue side in new[] { left, right })
        {
            if (side.AsExternalOrNull() is NetObject net)
            {
                foreach (MethodInfo method in net.Type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == name && method.GetParameters().Length == 2 && !candidates.Contains(method))
                    {
                        candidates.Add(method);
                    }
                }
            }
        }

        MethodInfo? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (MethodInfo candidate in candidates)
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (NetConvert.Rank(left, parameters[0].ParameterType) is { } a
                && NetConvert.Rank(right, parameters[1].ParameterType) is { } b && a + b < bestScore)
            {
                best = candidate;
                bestScore = a + b;
            }
        }

        return best is null ? null : new Bound(best, left, right);
    }

    private sealed record Bound(MethodInfo Method, JgsValue Left, JgsValue Right)
    {
        public JgsValue Call(int line, int col) =>
            NetInvoke.InvokeChosen(Method, null, [Left, Right], "MethodInvoke", line, col);
    }

    private static string Symbol(TokenType op) => op switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.DotStar => ".*",
        TokenType.Slash => "/",
        TokenType.DotSlash => "./",
        TokenType.Caret => "^",
        TokenType.DotCaret => ".^",
        TokenType.Less => "<",
        TokenType.LessEqual => "<=",
        TokenType.Greater => ">",
        TokenType.GreaterEqual => ">=",
        TokenType.Amp => "&",
        TokenType.Pipe => "|",
        TokenType.Backslash => "\\",
        TokenType.DotBackslash => ".\\",
        _ => op.ToString(),
    };
}
