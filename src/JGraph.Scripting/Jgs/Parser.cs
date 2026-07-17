namespace JGraph.Scripting.Jgs;

/// <summary>
/// A recursive-descent parser that turns the <see cref="Lexer"/>'s tokens into a list of statement nodes.
/// Statements are separated by significant newlines or semicolons; blocks are delimited by braces. Every
/// node records the source position of the token it started at, for runtime error reporting.
/// </summary>
internal sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _sourceId;
    private int _pos;

    private Parser(IReadOnlyList<Token> tokens, string sourceId)
    {
        _tokens = tokens;
        _sourceId = sourceId;
    }

    /// <summary>
    /// Parses <paramref name="source"/> into a program (a flat list of top-level statements). Every
    /// statement is stamped with <paramref name="sourceId"/> (a file path, or "" for unsaved code) so
    /// the debugger can map execution back to the right document.
    /// </summary>
    /// <exception cref="JgsSyntaxException">On any lexing or parsing error.</exception>
    public static IReadOnlyList<Stmt> Parse(string source, string sourceId = "")
    {
        var parser = new Parser(Lexer.Tokenize(source), sourceId);
        return parser.ParseProgram();
    }

    private Token Current => _tokens[_pos];

    private bool IsAtEnd => Current.Type == TokenType.Eof;

    private IReadOnlyList<Stmt> ParseProgram()
    {
        var statements = new List<Stmt>();
        SkipSeparators();
        while (!IsAtEnd)
        {
            statements.Add(ParseStatement());
            SkipSeparators();
        }

        return statements;
    }

    private IReadOnlyList<Stmt> ParseBlock()
    {
        Expect(TokenType.LBrace, "'{'");
        var statements = new List<Stmt>();
        SkipSeparators();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            statements.Add(ParseStatement());
            SkipSeparators();
        }

        Expect(TokenType.RBrace, "'}'");
        return statements;
    }

    private Stmt ParseStatement()
    {
        Token start = Current;
        Stmt statement = Current.Type switch
        {
            TokenType.Let => ParseLet(start),
            TokenType.Fn => ParseFn(start),
            TokenType.If => ParseIf(start),
            TokenType.For => ParseFor(start),
            TokenType.While => ParseWhile(start),
            TokenType.Return => ParseReturn(start),
            TokenType.Break => ParseBreak(start),
            TokenType.Continue => ParseContinue(start),
            _ => ParseAssignmentOrExpression(start),
        };
        statement.SourceId = _sourceId; // every statement (nested ones included) flows through here
        return statement;
    }

    private Stmt ParseBreak(Token start)
    {
        Advance(); // 'break'
        return new BreakStmt { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseContinue(Token start)
    {
        Advance(); // 'continue'
        return new ContinueStmt { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseLet(Token start)
    {
        Advance(); // 'let'
        Token name = Expect(TokenType.Identifier, "a variable name");
        Expect(TokenType.Assign, "'='");
        Expr value = ParseExpression();
        return new LetStmt(name.Text, value) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseFn(Token start)
    {
        Advance(); // 'fn'
        Token name = Expect(TokenType.Identifier, "a function name");
        Expect(TokenType.LParen, "'('");
        var parameters = new List<string>();
        if (!Check(TokenType.RParen))
        {
            do
            {
                parameters.Add(Expect(TokenType.Identifier, "a parameter name").Text);
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RParen, "')'");
        IReadOnlyList<Stmt> body = ParseBlock();
        return new FnStmt(name.Text, parameters, body) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseIf(Token start)
    {
        Advance(); // 'if'
        Expr condition = ParseExpression();
        IReadOnlyList<Stmt> then = ParseBlock();

        IReadOnlyList<Stmt>? elseBranch = null;
        int mark = _pos;
        SkipSeparators();
        if (Match(TokenType.Else))
        {
            elseBranch = Check(TokenType.If)
                ? new[] { ParseStatement() }   // 'else if' chains as a single nested statement
                : ParseBlock();
        }
        else
        {
            _pos = mark; // no else: leave the separators for the caller's statement loop
        }

        return new IfStmt(condition, then, elseBranch) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseFor(Token start)
    {
        Advance(); // 'for'
        Token variable = Expect(TokenType.Identifier, "a loop variable name");
        Expect(TokenType.In, "'in'");
        Expr iterable = ParseExpression();
        IReadOnlyList<Stmt> body = ParseBlock();
        return new ForStmt(variable.Text, iterable, body) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseWhile(Token start)
    {
        Advance(); // 'while'
        Expr condition = ParseExpression();
        IReadOnlyList<Stmt> body = ParseBlock();
        return new WhileStmt(condition, body) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseReturn(Token start)
    {
        Advance(); // 'return'
        Expr? value = IsStatementEnd() ? null : ParseExpression();
        return new ReturnStmt(value) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseAssignmentOrExpression(Token start)
    {
        Expr target = ParseExpression();
        if (Match(TokenType.Assign))
        {
            Expr value = ParseExpression();
            return target switch
            {
                VariableExpr variable => new AssignStmt(variable.Name, value) { Line = start.Line, Column = start.Column },
                IndexExpr index => new IndexAssignStmt(index.Target, index.Index, value) { Line = start.Line, Column = start.Column },
                _ => throw Error(start, "The left-hand side of '=' must be a variable or an array element."),
            };
        }

        return new ExprStmt(target) { Line = start.Line, Column = start.Column };
    }

    // --- Expressions --------------------------------------------------------------------------

    private Expr ParseExpression() => ParseOr();

    private Expr ParseOr()
    {
        Expr left = ParseAnd();
        while (Check(TokenType.PipePipe))
        {
            Token op = Advance();
            Expr right = ParseAnd();
            left = new LogicalExpr(op.Type, left, right) { Line = op.Line, Column = op.Column };
        }

        return left;
    }

    private Expr ParseAnd()
    {
        Expr left = ParseEquality();
        while (Check(TokenType.AmpAmp))
        {
            Token op = Advance();
            Expr right = ParseEquality();
            left = new LogicalExpr(op.Type, left, right) { Line = op.Line, Column = op.Column };
        }

        return left;
    }

    private Expr ParseEquality() =>
        ParseBinary(ParseComparison, TokenType.EqualEqual, TokenType.BangEqual);

    private Expr ParseComparison() =>
        ParseBinary(ParseAdditive, TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual);

    private Expr ParseAdditive() =>
        ParseBinary(ParseMultiplicative, TokenType.Plus, TokenType.Minus);

    private Expr ParseMultiplicative() =>
        ParseBinary(ParseUnary, TokenType.Star, TokenType.Slash, TokenType.Percent);

    private Expr ParseBinary(Func<Expr> operand, params TokenType[] operators)
    {
        Expr left = operand();
        while (operators.Contains(Current.Type))
        {
            Token op = Advance();
            Expr right = operand();
            left = new BinaryExpr(op.Type, left, right) { Line = op.Line, Column = op.Column };
        }

        return left;
    }

    private Expr ParseUnary()
    {
        if (Check(TokenType.Minus) || Check(TokenType.Bang))
        {
            Token op = Advance();
            Expr operand = ParseUnary();
            return new UnaryExpr(op.Type, operand) { Line = op.Line, Column = op.Column };
        }

        return ParsePostfix();
    }

    private Expr ParsePostfix()
    {
        Expr expr = ParsePrimary();
        while (true)
        {
            if (Check(TokenType.LParen))
            {
                Token paren = Advance();
                var arguments = new List<Expr>();
                if (!Check(TokenType.RParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    }
                    while (Match(TokenType.Comma));
                }

                Expect(TokenType.RParen, "')'");
                expr = new CallExpr(expr, arguments) { Line = paren.Line, Column = paren.Column };
            }
            else if (Check(TokenType.LBracket))
            {
                Token bracket = Advance();
                Expr index = ParseExpression();
                Expect(TokenType.RBracket, "']'");
                expr = new IndexExpr(expr, index) { Line = bracket.Line, Column = bracket.Column };
            }
            else
            {
                return expr;
            }
        }
    }

    private Expr ParsePrimary()
    {
        Token token = Current;
        switch (token.Type)
        {
            case TokenType.Number:
                Advance();
                return new NumberLiteral(token.Number) { Line = token.Line, Column = token.Column };
            case TokenType.String:
                Advance();
                return new StringLiteral(token.Text) { Line = token.Line, Column = token.Column };
            case TokenType.True:
            case TokenType.False:
                Advance();
                return new BoolLiteral(token.Type == TokenType.True) { Line = token.Line, Column = token.Column };
            case TokenType.Identifier:
                Advance();
                return new VariableExpr(token.Text) { Line = token.Line, Column = token.Column };
            case TokenType.LParen:
                Advance();
                Expr grouped = ParseExpression();
                Expect(TokenType.RParen, "')'");
                return grouped;
            case TokenType.LBracket:
                return ParseArrayLiteral(token);
            default:
                throw Error(token, $"Expected an expression, but found {token.Describe()}.");
        }
    }

    private Expr ParseArrayLiteral(Token start)
    {
        Advance(); // '['
        var elements = new List<Expr>();
        if (!Check(TokenType.RBracket))
        {
            do
            {
                elements.Add(ParseExpression());
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RBracket, "']'");
        return new ArrayLiteral(elements) { Line = start.Line, Column = start.Column };
    }

    // --- Token helpers ------------------------------------------------------------------------

    private bool IsStatementEnd() =>
        Current.Type is TokenType.Newline or TokenType.RBrace or TokenType.Eof;

    private void SkipSeparators()
    {
        while (Check(TokenType.Newline))
        {
            Advance();
        }
    }

    private bool Check(TokenType type) => Current.Type == type;

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }

        return false;
    }

    private Token Advance()
    {
        Token token = Current;
        if (!IsAtEnd)
        {
            _pos++;
        }

        return token;
    }

    private Token Expect(TokenType type, string what)
    {
        if (Check(type))
        {
            return Advance();
        }

        throw Error(Current, $"Expected {what}, but found {Current.Describe()}.");
    }

    private static JgsSyntaxException Error(Token token, string message) =>
        new(token.Line, token.Column, message);
}
