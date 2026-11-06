using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Analysis;

/// <summary>A span of one line: inclusive 1-based columns.</summary>
internal readonly record struct Span(int Line, int Start, int End);

/// <summary>
/// Where the parse tree's nodes sit in the text (App Designer window plan W10). A node carries the line and
/// column of its first token (or of its operator); the Code Analyzer marks ranges - <c>char(10)</c> whole,
/// the argument of <c>size(a + b)</c>, a statement to its semicolon - so this finds the rest from the token
/// stream the parser read: the token at a position, the bracket that closes one, a statement's last token.
/// </summary>
internal sealed class SourceSpans
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly Dictionary<long, int> _at = [];

    public SourceSpans(IReadOnlyList<Token> tokens, IReadOnlyList<string> lines)
    {
        _tokens = tokens;
        Lines = lines;
        for (int i = 0; i < tokens.Count; i++)
        {
            _at.TryAdd(Key(tokens[i].Line, tokens[i].Column), i);
        }
    }

    /// <summary>The text's lines, without their line breaks.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The tokens the parser read.</summary>
    public IReadOnlyList<Token> Tokens => _tokens;

    private static long Key(int line, int column) => ((long)line << 20) | (uint)column;

    /// <summary>The index of the token at a node's position, or -1.</summary>
    public int IndexAt(int line, int column) => _at.TryGetValue(Key(line, column), out int i) ? i : -1;

    /// <summary>The index of the token at a node's position.</summary>
    public int IndexOf(Node node) => IndexAt(node.Line, node.Column);

    /// <summary>The line's text, or empty beyond the text.</summary>
    public string LineText(int line) => line >= 1 && line <= Lines.Count ? Lines[line - 1] : string.Empty;

    /// <summary>The last column of the token at <paramref name="index"/>, from its text as written.</summary>
    public int EndColumn(int index)
    {
        Token token = _tokens[index];
        int length = token.Type switch
        {
            TokenType.String => StringLength(token),
            TokenType.Newline or TokenType.Eof => 1,
            _ => Math.Max(1, token.Text.Length),
        };
        return token.Column + length - 1;
    }

    /// <summary>The written length of a string literal: its quotes and its doubled quotes, read from the line.</summary>
    private int StringLength(Token token)
    {
        string line = LineText(token.Line);
        int start = token.Column - 1;
        if (start < 0 || start >= line.Length || (line[start] != '\'' && line[start] != '"'))
        {
            return Math.Max(1, token.Text.Length);
        }

        char quote = line[start];
        int i = start + 1;
        while (i < line.Length)
        {
            if (line[i] == quote)
            {
                if (i + 1 < line.Length && line[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i - start + 1;
            }

            i++;
        }

        return line.Length - start;
    }

    /// <summary>The span of the token at <paramref name="index"/>.</summary>
    public Span TokenSpan(int index) => new(_tokens[index].Line, _tokens[index].Column, EndColumn(index));

    /// <summary>The index of the bracket closing the one at <paramref name="open"/>, or -1.</summary>
    public int MatchingClose(int open)
    {
        int depth = 0;
        for (int i = open; i < _tokens.Count; i++)
        {
            switch (_tokens[i].Type)
            {
                case TokenType.LParen or TokenType.LBracket or TokenType.LBrace:
                    depth++;
                    break;
                case TokenType.RParen or TokenType.RBracket or TokenType.RBrace:
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
                case TokenType.Eof:
                    return -1;
            }
        }

        return -1;
    }

    /// <summary>The first token of an expression: the leftmost of its operands.</summary>
    public int StartIndex(Expr expr) => expr switch
    {
        CallExpr call when IndexOf(call) is int i && i >= 0 && _tokens[i].Type == TokenType.LParen => StartIndex(call.Callee),
        CallExpr call => IndexOf(call), // command syntax: the name
        IndexExpr index => StartIndex(index.Target),
        BraceIndexExpr brace => StartIndex(brace.Target),
        MemberExpr member => StartIndex(member.Target),
        BinaryExpr binary => StartIndex(binary.Left),
        LogicalExpr logical => StartIndex(logical.Left),
        TransposeExpr transpose => StartIndex(transpose.Operand),
        RangeExpr range => StartIndex(range.Start),
        AssignExpr assign => StartIndex(assign.Target),
        _ => IndexOf(expr),
    };

    /// <summary>The last token of an expression: the bracket closing it, or the rightmost of its operands.</summary>
    public int EndIndex(Expr expr)
    {
        switch (expr)
        {
            case CallExpr call:
            {
                int i = IndexOf(call);
                if (i >= 0 && _tokens[i].Type == TokenType.LParen)
                {
                    return MatchingClose(i);
                }

                // Command syntax: the name, or the last word.
                return call.Arguments.Count > 0 ? EndIndex(call.Arguments[^1]) : i;
            }

            case IndexExpr index:
                return MatchingClose(IndexOf(index));
            case BraceIndexExpr brace:
                return MatchingClose(IndexOf(brace));
            case MemberExpr member:
            {
                int dot = IndexOf(member);
                if (dot < 0)
                {
                    return -1;
                }

                return member.FieldName is null ? dot + 1 : MatchingClose(dot + 1);
            }

            case BinaryExpr binary:
                return EndIndex(binary.Right);
            case LogicalExpr logical:
                return EndIndex(logical.Right);
            case UnaryExpr unary:
                return EndIndex(unary.Operand);
            case TransposeExpr transpose:
                return IndexOf(transpose);
            case RangeExpr range:
                return EndIndex(range.Stop);
            case AssignExpr assign:
                return EndIndex(assign.Value);
            case ArrayLiteral or MatrixLiteral or CellLiteral:
                return MatchingClose(IndexOf(expr));
            case AnonymousFnExpr anonymous:
                return EndIndex(anonymous.Body);
            case FunctionHandleExpr handle:
            {
                int at = IndexOf(handle);
                return at < 0 ? -1 : at + 1 + 2 * handle.Name.Count(static c => c == '.');
            }

            case StringLiteral literal when IndexOf(literal) is int i && i >= 0 && _tokens[i].Type != TokenType.String:
                // A command-syntax word glued from several tokens: its text is the word.
                return i;
            default:
                return IndexOf(expr);
        }
    }

    /// <summary>The span of a whole expression on its first line (an expression continued over lines ends on its first line's last token).</summary>
    public Span SpanOf(Expr expr)
    {
        int start = StartIndex(expr);
        int end = EndIndex(expr);
        if (start < 0)
        {
            return new Span(expr.Line, expr.Column, expr.Column);
        }

        Token first = _tokens[start];
        if (end < 0 || _tokens[end].Line != first.Line)
        {
            // A command-syntax word's text is the word itself.
            if (expr is StringLiteral word && _tokens[start].Type != TokenType.String)
            {
                return new Span(first.Line, first.Column, first.Column + Math.Max(1, word.Value.Length) - 1);
            }

            return new Span(first.Line, first.Column, EndColumn(start));
        }

        if (expr is StringLiteral glued && _tokens[start].Type != TokenType.String && end == start)
        {
            return new Span(first.Line, first.Column, first.Column + Math.Max(1, glued.Value.Length) - 1);
        }

        return new Span(first.Line, first.Column, EndColumn(end));
    }

    /// <summary>The span of the name of a call's callee, or of a bare name.</summary>
    public Span NameSpan(Expr callee) => callee is VariableExpr name
        ? new Span(name.Line, name.Column, name.Column + name.Name.Length - 1)
        : SpanOf(callee);

    /// <summary>The index of the last token of the statement starting at <paramref name="start"/>: before its terminator at bracket depth zero.</summary>
    public int StatementEnd(int start)
    {
        int depth = 0;
        int last = start;
        for (int i = start; i < _tokens.Count; i++)
        {
            TokenType type = _tokens[i].Type;
            if (depth == 0 && type is TokenType.Newline or TokenType.Semicolon or TokenType.Comma or TokenType.Eof)
            {
                return last;
            }

            if (type is TokenType.LParen or TokenType.LBracket or TokenType.LBrace)
            {
                depth++;
            }
            else if (type is TokenType.RParen or TokenType.RBracket or TokenType.RBrace)
            {
                depth--;
            }

            last = i;
        }

        return last;
    }

    /// <summary>The token right after the statement starting at <paramref name="start"/>: its terminator, or the end of input.</summary>
    public int Terminator(int start)
    {
        int end = StatementEnd(start);
        return Math.Min(end + 1, _tokens.Count - 1);
    }

    /// <summary>The span of a simple statement, from its first token to its last (on one line).</summary>
    public Span StatementSpan(Stmt statement)
    {
        int start = IndexOf(statement);
        if (start < 0)
        {
            return new Span(statement.Line, statement.Column, statement.Column);
        }

        int end = StatementEnd(start);
        if (_tokens[end].Line != _tokens[start].Line)
        {
            end = LastOnLine(start);
        }

        return new Span(_tokens[start].Line, _tokens[start].Column, EndColumn(end));
    }

    private int LastOnLine(int start)
    {
        int last = start;
        for (int i = start; i < _tokens.Count && _tokens[i].Line == _tokens[start].Line && _tokens[i].Type != TokenType.Newline && _tokens[i].Type != TokenType.Eof; i++)
        {
            last = i;
        }

        return last;
    }

    /// <summary>The operator's text for a message.</summary>
    public static string OperatorText(TokenType type) => type switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.Slash => "/",
        TokenType.Backslash => "\\",
        TokenType.Caret => "^",
        TokenType.DotStar => ".*",
        TokenType.DotSlash => "./",
        TokenType.DotBackslash => ".\\",
        TokenType.DotCaret => ".^",
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
        TokenType.Colon => ":",
        TokenType.Transpose => "'",
        TokenType.DotTranspose => ".'",
        _ => type.ToString(),
    };

    /// <summary>The source text between two columns of a line, inclusive.</summary>
    public string Text(Span span)
    {
        string line = LineText(span.Line);
        int start = Math.Clamp(span.Start - 1, 0, line.Length);
        int end = Math.Clamp(span.End, start, line.Length);
        return line[start..end];
    }

    /// <summary>
    /// The identifiers of a <c>function</c> line: the outputs, the name and the parameters with their spans, read from
    /// the tokens after the keyword (a <c>~</c> parameter is the word <c>~</c>).
    /// </summary>
    /// <summary>
    /// The span of a method's name whether it is written with a body (<c>function r = name(o)</c>) or as a
    /// signature line of a <c>methods</c> block (<c>r = name(o)</c>, <c>[a, b] = name</c>, <c>name</c>; stage P5).
    /// </summary>
    public Span SignatureName(FnStmt function)
    {
        int i = IndexOf(function);
        if (i < 0)
        {
            return new Span(function.Line, function.Column, function.Column);
        }

        if (_tokens[i].Type == TokenType.Function)
        {
            return Signature(function).Name;
        }

        // A signature line: '[a, b] = name', 'r = name' or 'name', the name the identifier after any '='.
        if (_tokens[i].Type == TokenType.LBracket)
        {
            int close = MatchingClose(i);
            i = close < 0 ? i : close + 1;
        }
        else if (i + 1 < _tokens.Count && _tokens[i].Type == TokenType.Identifier && _tokens[i + 1].Type == TokenType.Assign)
        {
            i++;
        }

        if (i < _tokens.Count && _tokens[i].Type == TokenType.Assign)
        {
            i++;
        }

        return i < _tokens.Count && _tokens[i].Type == TokenType.Identifier
            ? TokenSpan(i)
            : new Span(function.Line, function.Column, function.Column);
    }

    public (List<Span> Outputs, Span Name, List<(string Text, Span Span)> Parameters) Signature(FnStmt function)
    {
        var outputs = new List<Span>();
        var parameters = new List<(string, Span)>();
        Span name = new(function.Line, function.Column, function.Column);
        int i = IndexOf(function);
        if (i < 0 || _tokens[i].Type != TokenType.Function)
        {
            return (outputs, name, parameters);
        }

        i++;
        bool End(int at) => at >= _tokens.Count || _tokens[at].Type is TokenType.Newline or TokenType.Semicolon or TokenType.Eof;

        // The outputs: '[a, b] =' or 'a ='.
        if (!End(i) && _tokens[i].Type == TokenType.LBracket)
        {
            int close = MatchingClose(i);
            if (close < 0)
            {
                return (outputs, name, parameters);
            }

            for (int j = i + 1; j < close; j++)
            {
                if (_tokens[j].Type == TokenType.Identifier)
                {
                    outputs.Add(TokenSpan(j));
                }
            }

            i = close + 1;
            if (!End(i) && _tokens[i].Type == TokenType.Assign)
            {
                i++;
            }
        }
        else if (!End(i) && _tokens[i].Type == TokenType.Identifier && i + 1 < _tokens.Count && _tokens[i + 1].Type == TokenType.Assign)
        {
            outputs.Add(TokenSpan(i));
            i += 2;
        }

        // The name: 'f', or 'set.P' (three tokens); its span runs from the first to the last.
        if (End(i) || _tokens[i].Type != TokenType.Identifier)
        {
            return (outputs, name, parameters);
        }

        int first = i;
        int last = i;
        i++;
        while (i + 1 < _tokens.Count && _tokens[i].Type == TokenType.Dot && _tokens[i + 1].Type == TokenType.Identifier && !_tokens[i].PrecededByWhitespace)
        {
            last = i + 1;
            i += 2;
        }

        name = new Span(_tokens[first].Line, _tokens[first].Column, EndColumn(last));

        // The parameters.
        if (!End(i) && _tokens[i].Type == TokenType.LParen)
        {
            int close = MatchingClose(i);
            for (int j = i + 1; j < close; j++)
            {
                if (_tokens[j].Type == TokenType.Identifier)
                {
                    parameters.Add((_tokens[j].Text, TokenSpan(j)));
                }
                else if (_tokens[j].Type == TokenType.Bang)
                {
                    parameters.Add(("~", TokenSpan(j)));
                }
            }
        }

        return (outputs, name, parameters);
    }

    /// <summary>The spans of the identifiers after a keyword statement (<c>global a b</c>, <c>persistent p</c>).</summary>
    public List<Span> NamesAfter(Stmt statement)
    {
        var names = new List<Span>();
        int i = IndexOf(statement);
        if (i < 0)
        {
            return names;
        }

        for (i++; i < _tokens.Count; i++)
        {
            TokenType type = _tokens[i].Type;
            if (type is TokenType.Newline or TokenType.Semicolon or TokenType.Eof)
            {
                break;
            }

            if (type == TokenType.Identifier)
            {
                names.Add(TokenSpan(i));
            }
        }

        return names;
    }
}
