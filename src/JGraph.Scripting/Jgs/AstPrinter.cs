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
        Write(text, expression);
        return text.ToString();
    }

    /// <summary>The <c>@(args) body</c> text of an anonymous function.</summary>
    public static string Print(AnonymousFnExpr function) =>
        $"@({string.Join(", ", function.Parameters)}) {Print(function.Body)}";

    private static void Write(StringBuilder text, Expr expression)
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
                text.Append(variable.Name);
                break;

            case EndExpr:
                text.Append("end");
                break;

            case AllExpr:
                text.Append(':');
                break;

            case ArrayLiteral array:
                text.Append('[');
                WriteList(text, array.Elements);
                text.Append(']');
                break;

            case MatrixLiteral matrix:
                text.Append('[');
                WriteRows(text, matrix.Rows);
                text.Append(']');
                break;

            case CellLiteral cell:
                text.Append('{');
                WriteRows(text, cell.Rows);
                text.Append('}');
                break;

            case RangeExpr range:
                Write(text, range.Start);
                text.Append(':');
                if (range.Step is not null)
                {
                    Write(text, range.Step);
                    text.Append(':');
                }

                Write(text, range.Stop);
                break;

            case UnaryExpr unary:
                text.Append(Operator(unary.Op));
                WriteNested(text, unary.Operand);
                break;

            case BinaryExpr binary:
                WriteNested(text, binary.Left);
                text.Append(' ').Append(Operator(binary.Op)).Append(' ');
                WriteNested(text, binary.Right);
                break;

            case LogicalExpr logical:
                WriteNested(text, logical.Left);
                text.Append(' ').Append(Operator(logical.Op)).Append(' ');
                WriteNested(text, logical.Right);
                break;

            case AssignExpr assign:
                Write(text, assign.Target);
                text.Append(' ').Append(Operator(assign.Op)).Append(' ');
                Write(text, assign.Value);
                break;

            case CallExpr call:
                Write(text, call.Callee);
                text.Append('(');
                WriteList(text, call.Arguments);
                text.Append(')');
                break;

            case IndexExpr index:
                Write(text, index.Target);
                text.Append('[');
                WriteList(text, index.Indices);
                text.Append(']');
                break;

            case BraceIndexExpr brace:
                Write(text, brace.Target);
                text.Append('{');
                WriteList(text, brace.Indices);
                text.Append('}');
                break;

            case MemberExpr member:
                Write(text, member.Target);
                text.Append('.');
                if (member.Field is not null)
                {
                    text.Append(member.Field);
                }
                else if (member.FieldName is not null)
                {
                    text.Append('(');
                    Write(text, member.FieldName);
                    text.Append(')');
                }

                break;

            case TransposeExpr transpose:
                WriteNested(text, transpose.Operand);
                text.Append(transpose.Conjugate ? "'" : ".'");
                break;

            case IncDecExpr incDec:
                if (incDec.Prefix)
                {
                    text.Append(incDec.Increment ? "++" : "--");
                    Write(text, incDec.Target);
                }
                else
                {
                    Write(text, incDec.Target);
                    text.Append(incDec.Increment ? "++" : "--");
                }

                break;

            case AnonymousFnExpr anonymous:
                text.Append(Print(anonymous));
                break;

            case FunctionHandleExpr handle:
                text.Append('@').Append(handle.Name);
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
    private static void WriteNested(StringBuilder text, Expr expression)
    {
        bool needsBrackets = expression is BinaryExpr or LogicalExpr or RangeExpr or AssignExpr or UnaryExpr;
        if (needsBrackets)
        {
            text.Append('(');
        }

        Write(text, expression);

        if (needsBrackets)
        {
            text.Append(')');
        }
    }

    private static void WriteList(StringBuilder text, IReadOnlyList<Expr> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            Write(text, items[i]);
        }
    }

    private static void WriteRows(StringBuilder text, IReadOnlyList<IReadOnlyList<Expr>> rows)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            if (r > 0)
            {
                text.Append("; ");
            }

            WriteList(text, rows[r]);
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
