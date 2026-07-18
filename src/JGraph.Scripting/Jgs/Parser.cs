namespace JGraph.Scripting.Jgs;

/// <summary>
/// A recursive-descent parser that turns the <see cref="Lexer"/>'s tokens into a list of statement nodes.
/// Statements are separated by significant newlines or semicolons (a trailing ';' additionally marks the
/// statement <see cref="Stmt.Suppressed"/>). Blocks are delimited by braces, or MATLAB-style: an
/// <c>if</c>/<c>for</c>/<c>while</c>/<c>fn</c> header not followed by '{' collects statements up to a
/// closing <c>end</c> (with <c>elseif</c>/<c>else</c> arms sharing the one <c>end</c>). Every node records
/// the source position of the token it started at, for runtime error reporting.
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

    private TokenType NextType => _pos + 1 < _tokens.Count ? _tokens[_pos + 1].Type : TokenType.Eof;

    private bool IsAtEnd => Current.Type == TokenType.Eof;

    private IReadOnlyList<Stmt> ParseProgram()
    {
        var statements = new List<Stmt>();
        SkipSeparators();
        while (!IsAtEnd)
        {
            statements.Add(ParseTerminatedStatement());
            SkipSeparators();
        }

        return statements;
    }

    private IReadOnlyList<Stmt> ParseBlock()
    {
        SkipSeparators(); // allow the '{' on its own line after fn/if/for/while headers
        Expect(TokenType.LBrace, "'{'");
        var statements = new List<Stmt>();
        SkipSeparators();
        while (!Check(TokenType.RBrace) && !IsAtEnd)
        {
            statements.Add(ParseTerminatedStatement());
            SkipSeparators();
        }

        Expect(TokenType.RBrace, "'}'");
        return statements;
    }

    /// <summary>A MATLAB-style body: statements up to (not consuming) <c>end</c>/<c>else</c>/<c>elseif</c>.</summary>
    private IReadOnlyList<Stmt> ParseMatlabBody()
    {
        var statements = new List<Stmt>();
        SkipSeparators();
        while (Current.Type is not (TokenType.End or TokenType.Else or TokenType.ElseIf) && !IsAtEnd)
        {
            statements.Add(ParseTerminatedStatement());
            SkipSeparators();
        }

        return statements;
    }

    /// <summary>
    /// A loop or function body in either style: a braced block, or a MATLAB body whose closing
    /// <c>end</c> the caller must consume (signalled by <paramref name="braced"/> = false).
    /// </summary>
    private IReadOnlyList<Stmt> ParseBody(out bool braced)
    {
        int mark = _pos;
        SkipSeparators();
        braced = Check(TokenType.LBrace);
        _pos = mark;
        return braced ? ParseBlock() : ParseMatlabBody();
    }

    /// <summary>Parses one statement and marks it <see cref="Stmt.Suppressed"/> when a ';' follows.</summary>
    private Stmt ParseTerminatedStatement()
    {
        Stmt statement = ParseStatement();
        if (Check(TokenType.Semicolon))
        {
            statement.Suppressed = true; // the separator itself is consumed by the caller's SkipSeparators
        }

        return statement;
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
            TokenType.End => throw Error(start, "Unexpected 'end': there is no MATLAB-style if/for/while block to close here."),
            TokenType.ElseIf => throw Error(start, "Unexpected 'elseif': it only follows a MATLAB-style 'if' body."),
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

        // Destructuring form: let [a, b] = expr
        if (Match(TokenType.LBracket))
        {
            var names = new List<string>();
            do
            {
                names.Add(Expect(TokenType.Identifier, "a variable name").Text);
            }
            while (Match(TokenType.Comma));

            Expect(TokenType.RBracket, "']'");
            Expect(TokenType.Assign, "'='");
            Expr tuple = ParseExpression();
            return new DestructuringLetStmt(names, tuple) { Line = start.Line, Column = start.Column };
        }

        Token name = Expect(TokenType.Identifier, "a variable name");
        Expect(TokenType.Assign, "'='");
        Expr value = ParseExpression();
        return new LetStmt(name.Text, value) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseFn(Token start)
    {
        Advance(); // 'fn'
        Token name = Expect(TokenType.Identifier, "a function name");
        SkipSeparators(); // allow the parameter list on its own line after the name
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
        IReadOnlyList<Stmt> body = ParseBody(out bool braced);
        if (!braced)
        {
            Expect(TokenType.End, "'end'");
        }

        return new FnStmt(name.Text, parameters, body) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseIf(Token start)
    {
        Advance(); // 'if'
        Expr condition = ParseExpression();

        int mark = _pos;
        SkipSeparators();
        bool braced = Check(TokenType.LBrace);
        _pos = mark;
        if (!braced)
        {
            return ParseMatlabIfChain(start, condition);
        }

        IReadOnlyList<Stmt> then = ParseBlock();

        IReadOnlyList<Stmt>? elseBranch = null;
        mark = _pos;
        SkipSeparators();
        if (Match(TokenType.Else))
        {
            SkipSeparators(); // allow 'else' and its 'if'/'{' on separate lines
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

    /// <summary>
    /// The MATLAB-style if: statements up to <c>elseif</c>/<c>else</c>/<c>end</c>. An <c>elseif</c> arm
    /// recurses as a nested <see cref="IfStmt"/>; the whole chain shares the single closing <c>end</c>,
    /// consumed at the innermost arm.
    /// </summary>
    private Stmt ParseMatlabIfChain(Token start, Expr condition)
    {
        IReadOnlyList<Stmt> then = ParseMatlabBody();

        IReadOnlyList<Stmt>? elseBranch = null;
        if (Check(TokenType.ElseIf))
        {
            Token arm = Advance();
            Expr armCondition = ParseExpression();
            Stmt nested = ParseMatlabIfChain(arm, armCondition);
            nested.SourceId = _sourceId; // built directly, not via ParseStatement
            elseBranch = new[] { nested };
        }
        else if (Match(TokenType.Else))
        {
            elseBranch = ParseMatlabBody();
            Expect(TokenType.End, "'end'");
        }
        else
        {
            Expect(TokenType.End, "'end'");
        }

        return new IfStmt(condition, then, elseBranch) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseFor(Token start)
    {
        Advance(); // 'for'
        Token variable = Expect(TokenType.Identifier, "a loop variable name");
        if (!Match(TokenType.In))
        {
            Expect(TokenType.Assign, "'in' or '='"); // MATLAB header: for k = 2:n
        }

        Expr iterable = ParseExpression();
        IReadOnlyList<Stmt> body = ParseBody(out bool braced);
        if (!braced)
        {
            Expect(TokenType.End, "'end'");
        }

        return new ForStmt(variable.Text, iterable, body) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseWhile(Token start)
    {
        Advance(); // 'while'
        Expr condition = ParseExpression();
        IReadOnlyList<Stmt> body = ParseBody(out bool braced);
        if (!braced)
        {
            Expect(TokenType.End, "'end'");
        }

        return new WhileStmt(condition, body) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseReturn(Token start)
    {
        Advance(); // 'return'
        Expr? value = IsStatementEnd() ? null : ParseExpression();
        return new ReturnStmt(value) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseAssignmentOrExpression(Token start) =>
        new ExprStmt(ParseExpression()) { Line = start.Line, Column = start.Column };

    // --- Expressions --------------------------------------------------------------------------

    private static readonly TokenType[] AssignmentOperators =
    [
        TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign,
        TokenType.StarAssign, TokenType.SlashAssign, TokenType.PercentAssign,
    ];

    private Expr ParseExpression() => ParseAssignment();

    private Expr ParseAssignment()
    {
        Expr left = ParseOr();
        if (AssignmentOperators.Contains(Current.Type))
        {
            Token op = Advance();
            Expr value = ParseAssignment(); // right-associative: a = b = 0
            RequireAssignable(left, op);
            return new AssignExpr(left, op.Type, value) { Line = op.Line, Column = op.Column };
        }

        return left;
    }

    /// <summary>
    /// Rejects assignment/increment targets that are not a variable or an array element. A
    /// <see cref="CallExpr"/> is allowed for MATLAB paren-index writes (<c>x(k) = v</c>); the
    /// interpreter validates that the callee is actually an array.
    /// </summary>
    private static void RequireAssignable(Expr target, Token op)
    {
        if (target is not (VariableExpr or IndexExpr or CallExpr))
        {
            throw Error(op, $"The left-hand side of '{op.Text}' must be a variable or an array element.");
        }
    }

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
        ParseBinary(ParseRange, TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual);

    /// <summary>MATLAB colon ranges: <c>a:b</c> and <c>a:step:b</c> — looser than arithmetic, tighter
    /// than comparisons (so <c>0:1/fs:3</c> parses each part as an arithmetic expression).</summary>
    private Expr ParseRange()
    {
        Expr first = ParseAdditive();
        if (!Check(TokenType.Colon))
        {
            return first;
        }

        Token colon = Advance();
        Expr second = ParseAdditive();
        if (Match(TokenType.Colon))
        {
            Expr stop = ParseAdditive();
            return new RangeExpr(first, second, stop) { Line = colon.Line, Column = colon.Column };
        }

        return new RangeExpr(first, null, second) { Line = colon.Line, Column = colon.Column };
    }

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

        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
        {
            Token op = Advance();
            Expr target = ParseUnary();
            RequireAssignable(target, op);
            return new IncDecExpr(target, op.Type == TokenType.PlusPlus, prefix: true)
            {
                Line = op.Line,
                Column = op.Column,
            };
        }

        return ParsePower();
    }

    /// <summary>
    /// MATLAB power: <c>^</c> binds tighter than unary minus (<c>-2^2 = -4</c>), associates left
    /// (<c>2^3^2 = 64</c>), and allows a unary sign on its right operand (<c>2^-3</c>).
    /// </summary>
    private Expr ParsePower()
    {
        Expr left = ParsePostfix();
        while (Check(TokenType.Caret))
        {
            Token op = Advance();
            Expr right = ParsePowerOperand();
            left = new BinaryExpr(TokenType.Caret, left, right) { Line = op.Line, Column = op.Column };
        }

        return left;
    }

    private Expr ParsePowerOperand()
    {
        if (Check(TokenType.Minus) || Check(TokenType.Bang))
        {
            Token op = Advance();
            return new UnaryExpr(op.Type, ParsePowerOperand()) { Line = op.Line, Column = op.Column };
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
                        // A lone ':' filling a whole argument is MATLAB "all elements" (x(:)).
                        if (Check(TokenType.Colon) && NextType is TokenType.Comma or TokenType.RParen)
                        {
                            Token colon = Advance();
                            arguments.Add(new AllExpr { Line = colon.Line, Column = colon.Column });
                        }
                        else
                        {
                            arguments.Add(ParseExpression());
                        }
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
            else if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            {
                Token op = Advance();
                RequireAssignable(expr, op);
                expr = new IncDecExpr(expr, op.Type == TokenType.PlusPlus, prefix: false)
                {
                    Line = op.Line,
                    Column = op.Column,
                };
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
            case TokenType.ImaginaryNumber:
                Advance();
                return new ComplexLiteral(token.Number) { Line = token.Line, Column = token.Column };
            case TokenType.End:
                Advance();
                return new EndExpr { Line = token.Line, Column = token.Column };
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
        var rows = new List<IReadOnlyList<Expr>>();
        var current = new List<Expr>();
        if (!Check(TokenType.RBracket))
        {
            while (true)
            {
                current.Add(ParseExpression());
                if (Match(TokenType.Comma))
                {
                    continue;
                }

                // ';' starts a new MATLAB row ([1, 2; 3, 4] / vertical concat [a; b]).
                if (Match(TokenType.Semicolon))
                {
                    rows.Add(current);
                    current = new List<Expr>();
                    if (Check(TokenType.RBracket))
                    {
                        break; // tolerate a trailing ';' before ']'
                    }

                    continue;
                }

                break;
            }
        }

        Expect(TokenType.RBracket, "']'");
        if (rows.Count == 0)
        {
            return new ArrayLiteral(current) { Line = start.Line, Column = start.Column };
        }

        if (current.Count > 0)
        {
            rows.Add(current);
        }

        return new MatrixLiteral(rows) { Line = start.Line, Column = start.Column };
    }

    // --- Token helpers ------------------------------------------------------------------------

    private bool IsStatementEnd() =>
        Current.Type is TokenType.Newline or TokenType.Semicolon or TokenType.RBrace or TokenType.Eof
            or TokenType.End or TokenType.Else or TokenType.ElseIf;

    private void SkipSeparators()
    {
        while (Check(TokenType.Newline) || Check(TokenType.Semicolon))
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
