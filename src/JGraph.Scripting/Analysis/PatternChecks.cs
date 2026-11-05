using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Analysis;

/// <summary>
/// The checks on the shape of a call or a comparison (App Designer window plan W10), run from the walk at each
/// call of a function (a name that is no variable of the scope): <c>char(10)</c>, <c>feval</c>, <c>exist</c>,
/// <c>eval</c>, <c>string('...')</c>, <c>logical(1)</c>, <c>repmat(1, ...)</c>, <c>strcmp(upper(a), b)</c>,
/// <c>getfield</c>, <c>setfield</c>, <c>size(a + b)</c>, <c>axes(h)</c> in a loop, <c>clear</c>, <c>load</c>,
/// <c>subsasgn</c>, <c>disp(sprintf(...))</c>, the comparisons <c>length(x) == 1</c>, <c>length(x) == 0</c>,
/// <c>ndims(x) == 2</c>, and single-element brackets. Each message's span and fix follow R2025b's as measured.
/// </summary>
internal sealed partial class ScopeAnalysis
{
    private static class PatternChecks
    {
        private static readonly HashSet<TokenType> ElementwiseOperators =
        [
            TokenType.Plus, TokenType.Minus, TokenType.DotStar, TokenType.DotSlash, TokenType.DotBackslash, TokenType.DotCaret,
            TokenType.EqualEqual, TokenType.BangEqual, TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual,
        ];

        /// <summary>A call of the function <paramref name="name"/> anywhere in an expression.</summary>
        public static void Call(ScopeAnalysis analysis, Scope scope, CallExpr call, VariableExpr name)
        {
            SourceSpans spans = analysis._spans;
            IReadOnlyList<Expr> args = call.Arguments;
            Span nameSpan = new(name.Line, name.Column, name.Column + name.Name.Length - 1);
            switch (name.Name)
            {
                case "char" when args.Count == 1 && args[0] is NumberLiteral { Value: 10 }:
                {
                    Span whole = spans.SpanOf(call);
                    analysis.Emit(CodeChecks.CHARTEN, whole, CodeFix.Of(whole.Line, whole.Start, whole.End, "newline"));
                    break;
                }

                case "feval" when args.Count >= 1:
                    Feval(analysis, spans, scope, call, args, nameSpan);
                    break;
                case "exist" when args.Count == 1:
                    analysis.Emit(CodeChecks.EXIST, nameSpan);
                    break;
                case "eval":
                    Eval(analysis, args, nameSpan);
                    break;
                case "string" when args.Count == 1 && args[0] is StringLiteral literal:
                {
                    Span whole = spans.SpanOf(call);
                    analysis.Emit(CodeChecks.STRQUOT, whole, CodeFix.Of(whole.Line, whole.Start, whole.End, "\"" + literal.Value.Replace("\"", "\"\"") + "\""));
                    break;
                }

                case "logical" when args.Count == 1 && args[0] is NumberLiteral { Value: 0 or 1 } flag:
                {
                    Span whole = spans.SpanOf(call);
                    analysis.Emit(CodeChecks.LOGL, nameSpan, CodeFix.Of(whole.Line, whole.Start, whole.End, flag.Value == 1 ? "true" : "false"));
                    break;
                }

                case "repmat" when args.Count >= 2:
                    Repmat(analysis, spans, scope, args, nameSpan);
                    break;
                case "strcmp" or "strncmp" or "strcmpi" or "strncmpi" when args.Count >= 2:
                    StringCompare(analysis, spans, scope, call, name.Name, args, nameSpan);
                    break;
                case "getfield" when args.Count == 2:
                    analysis.Emit(CodeChecks.GFLD, nameSpan);
                    break;
                case "setfield" when args.Count == 3:
                    analysis.Emit(CodeChecks.SFLD, nameSpan);
                    break;
                case "size" or "length" or "ndims" or "numel" when args.Count == 1 && args[0] is BinaryExpr { } operand && ElementwiseOperators.Contains(operand.Op):
                {
                    string op = SourceSpans.OperatorText(operand.Op);
                    analysis.Emit(CodeChecks.ELARLOG, spans.SpanOf(operand), text: string.Format(CodeChecks.ELARLOG.Text, op, name.Name));
                    break;
                }

                case "axes" when args.Count >= 1 && scope.InLoop:
                    analysis.Emit(CodeChecks.LAXES, nameSpan);
                    break;
            }
        }

        private static void Feval(ScopeAnalysis analysis, SourceSpans spans, Scope scope, CallExpr call, IReadOnlyList<Expr> args, Span nameSpan)
        {
            string? target = args[0] switch
            {
                StringLiteral literal when Regex.IsMatch(literal.Value, "^[A-Za-z]\\w*(\\.[A-Za-z]\\w*)*$") => literal.Value,
                FunctionHandleExpr handle => handle.Name,
                _ => null,
            };
            if (target is null)
            {
                // An anonymous function, or a variable holding a name throughout: no fix is offered.
                if (args[0] is AnonymousFnExpr || (args[0] is VariableExpr v && scope.IsVariable(v.Name) && scope.KnownLiteral(v.Name) is not null))
                {
                    analysis.Emit(CodeChecks.FVAL, nameSpan);
                }

                return;
            }

            Span whole = spans.SpanOf(call);
            CodeFix? fix = null;
            if (args.Count == 1)
            {
                fix = CodeFix.Of(whole.Line, whole.Start, whole.End, target + "()");
            }
            else
            {
                Span first = spans.SpanOf(args[1]);
                Span last = spans.SpanOf(args[^1]);
                if (first.Line == whole.Line && last.Line == whole.Line)
                {
                    string rest = spans.Text(new Span(whole.Line, first.Start, last.End));
                    fix = CodeFix.Of(whole.Line, whole.Start, whole.End, target + "(" + rest + ")");
                }
            }

            analysis.Emit(CodeChecks.FVAL, nameSpan, fix);
        }

        private static void Eval(ScopeAnalysis analysis, IReadOnlyList<Expr> args, Span nameSpan)
        {
            if (args.Count == 2)
            {
                analysis.Emit(CodeChecks.EV2IN, nameSpan);
                return;
            }

            if (args.Count != 1)
            {
                return;
            }

            IReadOnlyList<Expr>? elements = args[0] switch
            {
                ArrayLiteral a => a.Elements,
                MatrixLiteral { Rows.Count: 1 } m => m.Rows[0],
                _ => null,
            };
            if (elements is { Count: > 0 } && elements[0] is StringLiteral first)
            {
                if (first.Value.Contains('='))
                {
                    analysis.Emit(CodeChecks.EVLEQ, nameSpan);
                }
                else if (Regex.IsMatch(first.Value, "^\\s*[A-Za-z]\\w*\\s+$"))
                {
                    analysis.Emit(CodeChecks.EVLDUAL, nameSpan);
                }

                return;
            }

            if (elements is not null)
            {
                return;
            }

            analysis.Emit(CodeChecks.EVLCS, nameSpan);
        }

        private static void Repmat(ScopeAnalysis analysis, SourceSpans spans, Scope scope, IReadOnlyList<Expr> args, Span nameSpan)
        {
            (CodeChecks.Check Check, string Maker)? form = args[0] switch
            {
                NumberLiteral { Value: 1 } => (CodeChecks.RPMT1, "ones"),
                NumberLiteral { Value: 0 } => (CodeChecks.RPMT0, "zeros"),
                VariableExpr { Name: "true" } v when !scope.IsVariable(v.Name) => (CodeChecks.RPMTT, "true"),
                VariableExpr { Name: "false" } v when !scope.IsVariable(v.Name) => (CodeChecks.RPMTF, "false"),
                BoolLiteral { Value: true } => (CodeChecks.RPMTT, "true"),
                BoolLiteral { Value: false } => (CodeChecks.RPMTF, "false"),
                _ => null,
            };
            if (form is null)
            {
                return;
            }

            Span second = spans.SpanOf(args[1]);
            CodeFix? fix = second.Line == nameSpan.Line ? CodeFix.Of(nameSpan.Line, nameSpan.Start, second.Start - 1, form.Value.Maker + "(") : null;
            analysis.Emit(form.Value.Check, nameSpan, fix);
        }

        private static void StringCompare(ScopeAnalysis analysis, SourceSpans spans, Scope scope, CallExpr call, string name, IReadOnlyList<Expr> args, Span nameSpan)
        {
            var wrapped = new List<(int Index, CallExpr Wrapper, string Case)>();
            for (int i = 0; i < 2; i++)
            {
                if (args[i] is CallExpr { Callee: VariableExpr { Name: "upper" or "lower" } w, Arguments.Count: 1 } wrapper && !scope.IsVariable(w.Name))
                {
                    wrapped.Add((i, wrapper, w.Name));
                }
            }

            if (wrapped.Count == 0)
            {
                return;
            }

            bool insensitive = name.EndsWith('i');

            // strcmp(lower(s), 'abc') with s a constant text throughout the scope and a bare text on the other side:
            // R2025b reads the constant and says nothing (measured; its reasoning is its own).
            if (!insensitive)
            {
                for (int i = 0; i < 2; i++)
                {
                    (int Index, CallExpr Wrapper, string Case) w = wrapped.FirstOrDefault(w => w.Index == i);
                    if (w.Wrapper is not null && w.Wrapper.Arguments[0] is VariableExpr sv && scope.IsVariable(sv.Name) && scope.KnownLiteral(sv.Name) is not null && args[1 - i] is StringLiteral)
                    {
                        return;
                    }
                }
            }

            if (!insensitive)
            {
                // A literal on the other side whose case the wrapper can never match: the comparison fails.
                for (int i = 0; i < 2; i++)
                {
                    if (args[i] is StringLiteral literal && wrapped.Any(w => w.Index != i))
                    {
                        string wrapperCase = wrapped.First(w => w.Index != i).Case;
                        bool mismatch = wrapperCase == "upper" ? literal.Value.Any(char.IsLower) : literal.Value.Any(char.IsUpper);
                        if (mismatch)
                        {
                            analysis.Emit(CodeChecks.STCUL, nameSpan);
                            return;
                        }
                    }
                }
            }

            var edits = new List<CodeEdit>();
            if (!insensitive)
            {
                edits.Add(new CodeEdit(nameSpan.Line, nameSpan.Start, nameSpan.End, name + "i"));
            }

            bool sameLine = true;
            foreach ((_, CallExpr wrapper, _) in wrapped)
            {
                Span whole = spans.SpanOf(wrapper);
                Span inner = spans.SpanOf(wrapper.Arguments[0]);
                if (whole.Line != nameSpan.Line || inner.Line != whole.Line)
                {
                    sameLine = false;
                    continue;
                }

                edits.Add(new CodeEdit(whole.Line, whole.Start, whole.End, spans.Text(inner)));
            }

            CodeFix? fix = sameLine ? new CodeFix(edits) : null;
            CodeChecks.Check check = insensitive ? CodeChecks.STLOW : name == "strcmp" ? CodeChecks.STCI : CodeChecks.STNCI;
            analysis.Emit(check, nameSpan, fix);
            _ = call;
        }

        /// <summary>A call of a function as a whole statement: <c>clear</c>, <c>load</c>, <c>subsasgn</c>, <c>disp(sprintf(...))</c>.</summary>
        public static void StatementCall(ScopeAnalysis analysis, Scope scope, CallExpr call, VariableExpr name)
        {
            SourceSpans spans = analysis._spans;
            IReadOnlyList<Expr> args = call.Arguments;
            Span nameSpan = new(name.Line, name.Column, name.Column + name.Name.Length - 1);
            switch (name.Name)
            {
                case "clear":
                    if (args.Count == 0 && !scope.IsScript)
                    {
                        analysis.Emit(CodeChecks.CLEAR0ARGS, nameSpan);
                    }

                    foreach (Expr argument in args)
                    {
                        if (argument is StringLiteral { Value: "all" or "functions" } option)
                        {
                            analysis.Emit(option.Value == "all" ? CodeChecks.CLALL : CodeChecks.CLFUNC, spans.SpanOf(option));
                        }
                    }

                    break;
                case "load" when !scope.IsScript:
                    if (args.Count == 1)
                    {
                        analysis.Emit(CodeChecks.LOAD, nameSpan);
                    }
                    else if (args.Count > 1)
                    {
                        analysis.RecordLoadNames(scope, call);
                    }

                    break;
                case "subsasgn":
                    analysis.Emit(CodeChecks.SUBSASGN, nameSpan);
                    break;
                case "disp" when args.Count == 1 && args[0] is CallExpr { Callee: VariableExpr { Name: "sprintf" } s, Arguments.Count: >= 1 } inner && !scope.IsVariable(s.Name):
                {
                    Span whole = spans.SpanOf(call);
                    Span innerSpan = spans.SpanOf(inner);
                    CodeFix? fix = null;
                    if (whole.Line == innerSpan.Line && inner.Arguments[0] is StringLiteral format)
                    {
                        Span formatSpan = spans.SpanOf(format);
                        var edits = new List<CodeEdit> { new(whole.Line, whole.Start, innerSpan.Start + "sprintf(".Length - 1, "fprintf(") };
                        if (!format.Value.EndsWith("\\n", StringComparison.Ordinal))
                        {
                            edits.Add(new CodeEdit(whole.Line, formatSpan.End, formatSpan.End - 1, "\\n"));
                        }

                        edits.Add(new CodeEdit(whole.Line, whole.End, whole.End, string.Empty));
                        fix = new CodeFix(edits);
                    }

                    analysis.Emit(CodeChecks.DSPSP, whole, fix);
                    break;
                }
            }
        }

        /// <summary>A comparison: <c>length(x) == 1</c>, <c>numel(x) == 1</c>, <c>length(x) == 0</c>, <c>ndims(x)</c> against 2.</summary>
        public static void Comparison(ScopeAnalysis analysis, Scope scope, BinaryExpr binary)
        {
            if (binary.Op is not (TokenType.EqualEqual or TokenType.BangEqual or TokenType.Greater))
            {
                return;
            }

            SourceSpans spans = analysis._spans;
            (CallExpr Call, double Number)? sides = Sides(scope, binary.Left, binary.Right) ?? Sides(scope, binary.Right, binary.Left);
            if (sides is null)
            {
                return;
            }

            (CallExpr call, double number) = sides.Value;
            string function = ((VariableExpr)call.Callee).Name;
            Span whole = spans.SpanOf(binary);
            Span argument = spans.SpanOf(call.Arguments[0]);
            bool oneLine = whole.Line == argument.Line;
            string arg = oneLine ? spans.Text(argument) : string.Empty;
            if (function is "length" or "numel" && binary.Op == TokenType.EqualEqual)
            {
                if (number == 1)
                {
                    analysis.Emit(CodeChecks.ISCL, whole, oneLine ? CodeFix.Of(whole.Line, whole.Start, whole.End, $"isscalar({arg})") : null);
                }
                else if (number == 0)
                {
                    analysis.Emit(CodeChecks.ISMT, spans.NameSpan(call.Callee), oneLine ? CodeFix.Of(whole.Line, whole.Start, whole.End, $"isempty({arg})") : null);
                }
            }
            else if (function == "ndims" && number == 2)
            {
                string replacement = binary.Op == TokenType.EqualEqual ? $"ismatrix({arg})" : $"~ismatrix({arg})";
                analysis.Emit(CodeChecks.ISMAT, spans.NameSpan(call.Callee), oneLine ? CodeFix.Of(whole.Line, whole.Start, whole.End, replacement) : null);
            }
        }

        private static (CallExpr, double)? Sides(Scope scope, Expr a, Expr b) =>
            a is CallExpr { Callee: VariableExpr { Name: "length" or "numel" or "ndims" } f, Arguments.Count: 1 } call && !scope.IsVariable(f.Name) && b is NumberLiteral n
                ? (call, n.Value)
                : null;

        /// <summary>NBRAK2: brackets around one plain element.</summary>
        public static void Brackets(ScopeAnalysis analysis, Scope scope, Expr literal, IReadOnlyList<Expr> elements)
        {
            if (elements.Count != 1)
            {
                return;
            }

            // A number, a text or a variable alone; a call may answer several values, so its brackets stay.
            Expr only = elements[0];
            bool plain = only is NumberLiteral or StringLiteral or BoolLiteral || (only is VariableExpr v && scope.IsVariable(v.Name));
            if (!plain)
            {
                return;
            }

            SourceSpans spans = analysis._spans;
            int open = spans.IndexOf(literal);
            int close = open >= 0 ? spans.MatchingClose(open) : -1;
            CodeFix? fix = null;
            if (open >= 0 && close >= 0 && spans.Tokens[open].Line == spans.Tokens[close].Line)
            {
                var edits = new List<CodeEdit> { new(literal.Line, spans.Tokens[close].Column, spans.Tokens[close].Column, string.Empty) };
                if (close - 1 > open && spans.Tokens[close - 1].Type == TokenType.Comma)
                {
                    edits.Add(new CodeEdit(literal.Line, spans.Tokens[close - 1].Column, spans.Tokens[close - 1].Column, string.Empty));
                }

                edits.Add(new CodeEdit(literal.Line, literal.Column, literal.Column, string.Empty));
                fix = new CodeFix(edits);
            }

            analysis.Emit(CodeChecks.NBRAK2, new Span(literal.Line, literal.Column, literal.Column), fix);
        }
    }
}
