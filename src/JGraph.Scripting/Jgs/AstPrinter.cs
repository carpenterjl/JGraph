using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Renders an expression back to source text. This is what <c>func2str</c> needs, and by extension
/// what <c>functions</c> reports.
/// </summary>
/// <remarks>
/// The alternative — keeping a slice of the original source on every anonymous function — would
/// echo the caller's spacing back, but it means carrying byte offsets through the lexer, the parser
/// and every node, for one builtin. Printing from the tree costs nothing at parse time and produces
/// a normalized form: <c>@(x)x.^2</c> and <c>@(x) x .^ 2</c> come back the same, which is the more
/// useful answer for comparing two handles.
/// </remarks>
internal static class AstPrinter
{
    /// <summary>The source text of an expression, parenthesized enough to parse back the same way.</summary>
    public static string Print(Expr expression)
    {
        var text = new StringBuilder();
        Write(text, expression, null);
        return text.ToString();
    }

    /// <summary>The <c>@(args) body</c> text of an anonymous function.</summary>
    public static string Print(AnonymousFnExpr function) =>
        $"@({string.Join(", ", function.Parameters)}) {Print(function.Body)}";

    /// <summary>
    /// The <c>@(args) body</c> text of an anonymous function with each name in
    /// <paramref name="literals"/> written as the text it maps to - a captured variable's value as
    /// a literal, which is how a <c>.fig</c> carries a capturing callback to MATLAB (open item 139).
    /// Null when such a name is called, indexed, assigned or reached into in the body, where a
    /// literal cannot stand; a parameter of the function or of a nested one shadows the name.
    /// </summary>
    public static string? Print(AnonymousFnExpr function, IReadOnlyDictionary<string, string> literals)
    {
        Dictionary<string, string>? scope = Shadow(new Dictionary<string, string>(literals, StringComparer.Ordinal), function.Parameters);
        if (!CanStandIn(function.Body, scope))
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append("@(").Append(string.Join(", ", function.Parameters)).Append(") ");
        Write(text, function.Body, scope);
        return text.ToString();
    }

    /// <summary>The substitutions less the names <paramref name="parameters"/> shadow; null once none is left.</summary>
    private static Dictionary<string, string>? Shadow(Dictionary<string, string>? scope, IReadOnlyList<string> parameters)
    {
        if (scope is null)
        {
            return null;
        }

        Dictionary<string, string> inner = scope;
        foreach (string parameter in parameters)
        {
            if (inner.ContainsKey(parameter))
            {
                if (ReferenceEquals(inner, scope))
                {
                    inner = new Dictionary<string, string>(scope, StringComparer.Ordinal);
                }

                inner.Remove(parameter);
            }
        }

        return inner.Count == 0 ? null : inner;
    }

    private static bool Substituted(Expr expression, Dictionary<string, string>? scope) =>
        scope is not null && expression is VariableExpr variable && scope.ContainsKey(variable.Name);

    /// <summary>Whether every substituted name in <paramref name="expression"/> stands where a literal can.</summary>
    private static bool CanStandIn(Expr? expression, Dictionary<string, string>? scope)
    {
        if (scope is null || expression is null)
        {
            return true;
        }

        switch (expression)
        {
            case CallExpr call:
                return !Substituted(call.Callee, scope) && CanStandIn(call.Callee, scope) && call.Arguments.All(a => CanStandIn(a, scope));
            case IndexExpr index:
                return !Substituted(index.Target, scope) && CanStandIn(index.Target, scope) && index.Indices.All(i => CanStandIn(i, scope));
            case BraceIndexExpr brace:
                return !Substituted(brace.Target, scope) && CanStandIn(brace.Target, scope) && brace.Indices.All(i => CanStandIn(i, scope));
            case MemberExpr member:
                return !Substituted(member.Target, scope) && CanStandIn(member.Target, scope) && CanStandIn(member.FieldName, scope);
            case AssignExpr assign:
                return !Substituted(assign.Target, scope) && CanStandIn(assign.Target, scope) && CanStandIn(assign.Value, scope);
            case IncDecExpr incDec:
                return !Substituted(incDec.Target, scope) && CanStandIn(incDec.Target, scope);
            case UnaryExpr unary:
                return CanStandIn(unary.Operand, scope);
            case TransposeExpr transpose:
                return CanStandIn(transpose.Operand, scope);
            case BinaryExpr binary:
                return CanStandIn(binary.Left, scope) && CanStandIn(binary.Right, scope);
            case LogicalExpr logical:
                return CanStandIn(logical.Left, scope) && CanStandIn(logical.Right, scope);
            case RangeExpr range:
                return CanStandIn(range.Start, scope) && CanStandIn(range.Step, scope) && CanStandIn(range.Stop, scope);
            case ArrayLiteral array:
                return array.Elements.All(e => CanStandIn(e, scope));
            case MatrixLiteral matrix:
                return matrix.Rows.All(row => row.All(e => CanStandIn(e, scope)));
            case CellLiteral cell:
                return cell.Rows.All(row => row.All(e => CanStandIn(e, scope)));
            case AnonymousFnExpr nested:
                return CanStandIn(nested.Body, Shadow(scope, nested.Parameters));
            default:
                return true;
        }
    }

    private static void Write(StringBuilder text, Expr expression, Dictionary<string, string>? scope)
    {
        switch (expression)
        {
            case NumberLiteral number:
                text.Append(Number(number.Value));
                break;

            case StringLiteral literal:
                // Single quotes, MATLAB's char-literal spelling, with an embedded quote doubled.
                text.Append('\'').Append(literal.Value.Replace("'", "''", StringComparison.Ordinal)).Append('\'');
                break;

            case BoolLiteral boolean:
                text.Append(boolean.Value ? "true" : "false");
                break;

            case ComplexLiteral imaginary:
                text.Append(Number(imaginary.Imaginary)).Append('i');
                break;

            case VariableExpr variable:
                text.Append(scope is not null && scope.TryGetValue(variable.Name, out string? standing) ? standing : variable.Name);
                break;

            case EndExpr:
                text.Append("end");
                break;

            case AllExpr:
                text.Append(':');
                break;

            case ArrayLiteral array:
                text.Append('[');
                WriteList(text, array.Elements, scope);
                text.Append(']');
                break;

            case MatrixLiteral matrix:
                text.Append('[');
                WriteRows(text, matrix.Rows, scope);
                text.Append(']');
                break;

            case CellLiteral cell:
                text.Append('{');
                WriteRows(text, cell.Rows, scope);
                text.Append('}');
                break;

            case RangeExpr range:
                Write(text, range.Start, scope);
                text.Append(':');
                if (range.Step is not null)
                {
                    Write(text, range.Step, scope);
                    text.Append(':');
                }

                Write(text, range.Stop, scope);
                break;

            case UnaryExpr unary:
                text.Append(Operator(unary.Op));
                WriteNested(text, unary.Operand, scope);
                break;

            case BinaryExpr binary:
                WriteNested(text, binary.Left, scope);
                text.Append(' ').Append(Operator(binary.Op)).Append(' ');
                WriteNested(text, binary.Right, scope);
                break;

            case LogicalExpr logical:
                WriteNested(text, logical.Left, scope);
                text.Append(' ').Append(Operator(logical.Op)).Append(' ');
                WriteNested(text, logical.Right, scope);
                break;

            case AssignExpr assign:
                Write(text, assign.Target, scope);
                text.Append(' ').Append(Operator(assign.Op)).Append(' ');
                Write(text, assign.Value, scope);
                break;

            case CallExpr call:
                Write(text, call.Callee, scope);
                text.Append('(');
                WriteList(text, call.Arguments, scope);
                text.Append(')');
                break;

            case IndexExpr index:
                Write(text, index.Target, scope);
                text.Append('[');
                WriteList(text, index.Indices, scope);
                text.Append(']');
                break;

            case BraceIndexExpr brace:
                Write(text, brace.Target, scope);
                text.Append('{');
                WriteList(text, brace.Indices, scope);
                text.Append('}');
                break;

            case MemberExpr member:
                Write(text, member.Target, scope);
                text.Append('.');
                if (member.Field is not null)
                {
                    text.Append(member.Field);
                }
                else if (member.FieldName is not null)
                {
                    text.Append('(');
                    Write(text, member.FieldName, scope);
                    text.Append(')');
                }

                break;

            case TransposeExpr transpose:
                WriteNested(text, transpose.Operand, scope);
                text.Append(transpose.Conjugate ? "'" : ".'");
                break;

            case IncDecExpr incDec:
                if (incDec.Prefix)
                {
                    text.Append(incDec.Increment ? "++" : "--");
                    Write(text, incDec.Target, scope);
                }
                else
                {
                    Write(text, incDec.Target, scope);
                    text.Append(incDec.Increment ? "++" : "--");
                }

                break;

            case AnonymousFnExpr anonymous:
                text.Append("@(").Append(string.Join(", ", anonymous.Parameters)).Append(") ");
                Write(text, anonymous.Body, Shadow(scope, anonymous.Parameters));
                break;

            case FunctionHandleExpr handle:
                text.Append('@').Append(handle.Name);
                break;

            case MetaClassExpr meta:
                text.Append('?').Append(meta.Name);
                break;

            case SuperRefExpr reaching:
                text.Append(reaching.Name).Append('@').Append(reaching.Superclass);
                break;

            case PreEvaluated evaluated:
                // Only the interpreter makes these, and only around a value it has already computed.
                text.Append(evaluated.Value.Display());
                break;

            default:
                text.Append("<expression>");
                break;
        }
    }

    /// <summary>
    /// Writes a subexpression, wrapping it in parentheses when it is itself an operation. Tracking
    /// precedence exactly would drop some of these, but a redundant pair changes nothing about what
    /// the text means, where a missing one changes the answer.
    /// </summary>
    private static void WriteNested(StringBuilder text, Expr expression, Dictionary<string, string>? scope)
    {
        bool needsBrackets = expression is BinaryExpr or LogicalExpr or RangeExpr or AssignExpr or UnaryExpr;
        if (needsBrackets)
        {
            text.Append('(');
        }

        Write(text, expression, scope);

        if (needsBrackets)
        {
            text.Append(')');
        }
    }

    private static void WriteList(StringBuilder text, IReadOnlyList<Expr> items, Dictionary<string, string>? scope)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            Write(text, items[i], scope);
        }
    }

    private static void WriteRows(StringBuilder text, IReadOnlyList<IReadOnlyList<Expr>> rows, Dictionary<string, string>? scope)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            if (r > 0)
            {
                text.Append("; ");
            }

            WriteList(text, rows[r], scope);
        }
    }

    /// <summary>A number in the shortest round-tripping form, so 2 prints as 2 rather than 2.0.</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Operator(TokenType op) => op switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.Slash => "/",
        TokenType.Percent => "%",
        TokenType.Caret => "^",
        TokenType.DotStar => ".*",
        TokenType.DotSlash => "./",
        TokenType.DotCaret => ".^",
        TokenType.Backslash => "\\",
        TokenType.EqualEqual => "==",
        TokenType.BangEqual => "~=",
        TokenType.Less => "<",
        TokenType.LessEqual => "<=",
        TokenType.Greater => ">",
        TokenType.GreaterEqual => ">=",
        TokenType.Amp => "&",
        TokenType.Pipe => "|",
        TokenType.AmpAmp => "&&",
        TokenType.PipePipe => "||",
        TokenType.Bang => "~",
        TokenType.Assign => "=",
        TokenType.PlusAssign => "+=",
        TokenType.MinusAssign => "-=",
        TokenType.StarAssign => "*=",
        TokenType.SlashAssign => "/=",
        TokenType.PercentAssign => "%=",
        _ => op.ToString(),
    };
}
