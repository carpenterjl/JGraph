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

    /// <summary>
    /// Words MATLAB accepts that JGraph has no equivalent for. Naming them is much kinder than a parse
    /// error pointing at the middle of a class definition.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnsupportedWords = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["classdef"] = "class definitions are not supported",
        ["parfor"] = "parallel loops are not supported — use a plain 'for'",
        ["spmd"] = "parallel blocks are not supported",
        ["persistent"] = "persistent variables are not supported",
    };

    private readonly bool _matlab;

    /// <summary>
    /// How deep the parser is inside a MATLAB <c>[...]</c>/<c>{...}</c> literal, ignoring anything nested
    /// in parentheses. Only there does spacing split elements, so only there may an expression stop at a
    /// <c>+</c>/<c>-</c> that is really the sign of the next element.
    /// </summary>
    private int _literalDepth;

    private Parser(IReadOnlyList<Token> tokens, string sourceId, JgsDialect dialect)
    {
        _tokens = tokens;
        _sourceId = sourceId;
        Dialect = dialect;
        _matlab = dialect.IsMatlab;
    }

    /// <summary>The language variant being parsed.</summary>
    public JgsDialect Dialect { get; }

    /// <summary>
    /// Parses <paramref name="source"/> into a program (a flat list of top-level statements). Every
    /// statement is stamped with <paramref name="sourceId"/> (a file path, or "" for unsaved code) so
    /// the debugger can map execution back to the right document.
    /// </summary>
    /// <param name="source">The source text.</param>
    /// <param name="sourceId">The identity of <paramref name="source"/> — a file path, or "".</param>
    /// <param name="dialect">The language variant to parse, or null for <see cref="JgsDialect.Jgs"/>.</param>
    /// <exception cref="JgsSyntaxException">On any lexing or parsing error.</exception>
    public static IReadOnlyList<Stmt> Parse(string source, string sourceId = "", JgsDialect? dialect = null)
    {
        dialect ??= JgsDialect.Jgs;
        var parser = new Parser(Lexer.Tokenize(source, tolerant: false, dialect), sourceId, dialect);
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

    /// <summary>
    /// A MATLAB-style body: statements up to (not consuming) the word that closes or continues the block —
    /// <c>end</c>, <c>else</c>, <c>elseif</c>, and in MATLAB also <c>case</c>, <c>otherwise</c>,
    /// <c>catch</c> and the <c>function</c> that starts the next subfunction.
    /// </summary>
    private IReadOnlyList<Stmt> ParseMatlabBody()
    {
        var statements = new List<Stmt>();
        SkipSeparators();
        while (!IsBodyTerminator() && !IsAtEnd)
        {
            statements.Add(ParseTerminatedStatement());
            SkipSeparators();
        }

        return statements;
    }

    private bool IsBodyTerminator() => Current.Type switch
    {
        TokenType.End or TokenType.Else or TokenType.ElseIf => true,
        TokenType.Case or TokenType.Otherwise or TokenType.Catch or TokenType.Function => _matlab,
        _ => false,
    };

    /// <summary>
    /// A loop or function body in either style: a braced block, or a MATLAB body whose closing
    /// <c>end</c> the caller must consume (signalled by <paramref name="braced"/> = false).
    /// </summary>
    private IReadOnlyList<Stmt> ParseBody(out bool braced)
    {
        if (_matlab)
        {
            // MATLAB has no braced blocks — a '{' after a header opens a cell literal, not a body.
            braced = false;
            return ParseMatlabBody();
        }

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
            TokenType.Function => ParseMatlabFunction(start),
            TokenType.Switch => ParseSwitch(start),
            TokenType.Try => ParseTry(start),
            TokenType.Global => ParseGlobal(start),
            TokenType.Case => throw Error(start, "Unexpected 'case': it only appears inside a 'switch' block."),
            TokenType.Otherwise => throw Error(start, "Unexpected 'otherwise': it only appears inside a 'switch' block."),
            TokenType.Catch => throw Error(start, "Unexpected 'catch': it only follows a 'try' body."),
            TokenType.Identifier when _matlab && UnsupportedWords.TryGetValue(start.Text, out string? why) =>
                throw Error(start, $"'{start.Text}': {why} in JGraph."),
            TokenType.LBracket when _matlab && LooksLikeMultiAssign() => ParseMultiAssign(start),
            TokenType.Identifier when _matlab && LooksLikeCommandSyntax() => ParseCommandSyntax(start),
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
        bool braced = !_matlab && Check(TokenType.LBrace);
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

    // --- MATLAB statements --------------------------------------------------------------------

    /// <summary>
    /// MATLAB's function declaration: <c>function [a, b] = name(x, y)</c>, <c>function r = name(x)</c>, or
    /// <c>function name(x)</c>. The body runs to a matching <c>end</c>, or — in the classic function-file
    /// style, where no function is closed — to the next <c>function</c> or the end of the file.
    /// </summary>
    private Stmt ParseMatlabFunction(Token start)
    {
        Advance(); // 'function'

        var outputs = new List<string>();
        if (Check(TokenType.LBracket))
        {
            Advance();
            if (!Check(TokenType.RBracket))
            {
                do
                {
                    outputs.Add(Expect(TokenType.Identifier, "an output name").Text);
                }
                while (Match(TokenType.Comma));
            }

            Expect(TokenType.RBracket, "']'");
            Expect(TokenType.Assign, "'='");
        }

        Token name = Expect(TokenType.Identifier, "a function name");

        // 'function r = name(x)': what looked like the name was the single output.
        if (Match(TokenType.Assign))
        {
            outputs.Add(name.Text);
            name = Expect(TokenType.Identifier, "a function name");
        }

        var parameters = new List<string>();
        if (Match(TokenType.LParen))
        {
            if (!Check(TokenType.RParen))
            {
                do
                {
                    // '~' in a parameter list means "this argument is ignored".
                    parameters.Add(Match(TokenType.Bang) ? "~" : Expect(TokenType.Identifier, "a parameter name").Text);
                }
                while (Match(TokenType.Comma));
            }

            Expect(TokenType.RParen, "')'");
        }

        IReadOnlyList<Stmt> body = ParseMatlabBody();
        Match(TokenType.End); // present in the modern style, absent in a classic function file

        return new FnStmt(name.Text, parameters, body, outputs) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseSwitch(Token start)
    {
        Advance(); // 'switch'
        Expr subject = ParseExpression();
        SkipSeparators();

        var cases = new List<SwitchCase>();
        while (Check(TokenType.Case))
        {
            Advance();
            Expr value = ParseExpression();
            cases.Add(new SwitchCase(value, ParseMatlabBody()));
        }

        IReadOnlyList<Stmt>? otherwise = null;
        if (Match(TokenType.Otherwise))
        {
            otherwise = ParseMatlabBody();
        }

        Expect(TokenType.End, "'end'");
        return new SwitchStmt(subject, cases, otherwise) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseTry(Token start)
    {
        Advance(); // 'try'
        Match(TokenType.Comma); // MATLAB allows 'try,'
        IReadOnlyList<Stmt> body = ParseMatlabBody();

        string? errorVariable = null;
        IReadOnlyList<Stmt> handler = Array.Empty<Stmt>();
        if (Match(TokenType.Catch))
        {
            // 'catch err' names the error struct — but only when the name is on the same line, since
            // 'catch\n disp(x)' means a bare catch whose body starts with a call.
            if (Check(TokenType.Identifier) && NextType is not (TokenType.LParen or TokenType.Assign or TokenType.Dot))
            {
                errorVariable = Advance().Text;
            }

            handler = ParseMatlabBody();
        }

        Expect(TokenType.End, "'end'");
        return new TryStmt(body, errorVariable, handler) { Line = start.Line, Column = start.Column };
    }

    private Stmt ParseGlobal(Token start)
    {
        Advance(); // 'global'
        var names = new List<string>();
        do
        {
            names.Add(Expect(TokenType.Identifier, "a variable name").Text);
        }
        while (Match(TokenType.Comma) || Check(TokenType.Identifier)); // 'global a b' or 'global a, b'

        return new GlobalStmt(names) { Line = start.Line, Column = start.Column };
    }

    /// <summary>
    /// Whether a statement-initial '[' opens a multiple-output assignment (<c>[a, b] = f(x)</c>) rather
    /// than a matrix literal. Decided by scanning to the matching ']' and looking for a following '='.
    /// </summary>
    private bool LooksLikeMultiAssign()
    {
        int depth = 0;
        for (int p = _pos; p < _tokens.Count; p++)
        {
            switch (_tokens[p].Type)
            {
                case TokenType.LBracket or TokenType.LBrace or TokenType.LParen:
                    depth++;
                    break;
                case TokenType.RBrace or TokenType.RParen:
                    depth--;
                    break;
                case TokenType.RBracket:
                    if (--depth == 0)
                    {
                        return p + 1 < _tokens.Count && _tokens[p + 1].Type == TokenType.Assign;
                    }

                    break;
                case TokenType.Newline or TokenType.Eof:
                    return false; // a row break inside means it is a literal, not a target list
            }
        }

        return false;
    }

    private Stmt ParseMultiAssign(Token start)
    {
        Advance(); // '['
        var targets = new List<Expr?>();
        if (!Check(TokenType.RBracket))
        {
            do
            {
                // '~' discards one output.
                if (Match(TokenType.Bang))
                {
                    targets.Add(null);
                    continue;
                }

                Expr target = ParsePostfix();
                RequireAssignable(target, start);
                targets.Add(target);
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RBracket, "']'");
        Expect(TokenType.Assign, "'='");
        Expr call = ParseExpression();
        return new MultiAssignStmt(targets, call) { Line = start.Line, Column = start.Column };
    }

    /// <summary>
    /// Whether this statement is MATLAB command syntax — <c>hold on</c>, <c>axis equal</c>,
    /// <c>format long</c> — which is a call whose arguments are the bare words that follow. It applies
    /// only when a space separates the words and every one of them, to the end of the statement, is a
    /// plain word, number, or literal: anything an expression could continue with rules it out.
    /// </summary>
    private bool LooksLikeCommandSyntax()
    {
        if (_pos + 1 >= _tokens.Count || !_tokens[_pos + 1].PrecededByWhitespace)
        {
            return false;
        }

        int p = _pos + 1;
        int words = 0;
        while (p < _tokens.Count)
        {
            switch (_tokens[p].Type)
            {
                case TokenType.Identifier or TokenType.Number or TokenType.String:
                    words++;
                    p++;
                    break;
                case TokenType.Newline or TokenType.Semicolon or TokenType.Comma or TokenType.Eof:
                    return words > 0;
                default:
                    return false;
            }
        }

        return words > 0;
    }

    private Stmt ParseCommandSyntax(Token start)
    {
        Token name = Advance();
        var arguments = new List<Expr>();
        while (Current.Type is TokenType.Identifier or TokenType.Number or TokenType.String)
        {
            Token word = Advance();
            arguments.Add(new StringLiteral(word.Text) { Line = word.Line, Column = word.Column });
        }

        var callee = new VariableExpr(name.Text) { Line = name.Line, Column = name.Column };
        var call = new CallExpr(callee, arguments) { Line = name.Line, Column = name.Column };
        return new ExprStmt(call) { Line = start.Line, Column = start.Column };
    }

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
        if (target is not (VariableExpr or IndexExpr or CallExpr or BraceIndexExpr or MemberExpr))
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
        Expr left = ParseElementwiseOr();
        while (Check(TokenType.AmpAmp))
        {
            Token op = Advance();
            Expr right = ParseElementwiseOr();
            left = new LogicalExpr(op.Type, left, right) { Line = op.Line, Column = op.Column };
        }

        return left;
    }

    /// <summary>
    /// MATLAB's elementwise <c>|</c>: like <c>||</c> but it evaluates both sides and works over whole
    /// arrays. It binds tighter than <c>&amp;&amp;</c> and looser than <c>&amp;</c>, as in MATLAB. These
    /// tokens never occur in JGS, so the two extra levels cost it nothing.
    /// </summary>
    private Expr ParseElementwiseOr() => ParseBinary(ParseElementwiseAnd, TokenType.Pipe);

    private Expr ParseElementwiseAnd() => ParseBinary(ParseEquality, TokenType.Amp);

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
        ParseBinary(ParseUnary, TokenType.Star, TokenType.Slash, TokenType.Percent,
            TokenType.DotStar, TokenType.DotSlash);

    private Expr ParseBinary(Func<Expr> operand, params TokenType[] operators)
    {
        Expr left = operand();
        while (operators.Contains(Current.Type))
        {
            if (AtElementBreak())
            {
                break; // '[1 -2]': the '-' belongs to the next element, not to this expression
            }

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
        while (Check(TokenType.Caret) || Check(TokenType.DotCaret))
        {
            Token op = Advance();
            Expr right = ParsePowerOperand();
            left = new BinaryExpr(op.Type, left, right) { Line = op.Line, Column = op.Column };
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
                List<Expr> arguments = ParseSubscripts(TokenType.RParen, "')'");
                expr = new CallExpr(expr, arguments) { Line = paren.Line, Column = paren.Column };
            }
            else if (Check(TokenType.LBracket) && !_matlab)
            {
                // Brackets take the same subscripts as parens — ':' , 'end', ranges, masks, and the
                // multi-subscript form for images — so the two spellings index alike. Not in MATLAB,
                // where '[' after a value concatenates ('[a [1 2]]') rather than indexing it.
                Token bracket = Advance();
                List<Expr> indices = ParseSubscripts(TokenType.RBracket, "']'");
                expr = new IndexExpr(expr, indices) { Line = bracket.Line, Column = bracket.Column };
            }
            else if (_matlab && Check(TokenType.LBrace))
            {
                Token brace = Advance();
                List<Expr> indices = ParseSubscripts(TokenType.RBrace, "'}'");
                expr = new BraceIndexExpr(expr, indices) { Line = brace.Line, Column = brace.Column };
            }
            else if (_matlab && Check(TokenType.Dot))
            {
                Token dot = Advance();

                // The dynamic form s.('name') picks the field at run time.
                if (Match(TokenType.LParen))
                {
                    Expr fieldName = ParseExpression();
                    Expect(TokenType.RParen, "')'");
                    expr = new MemberExpr(expr, null, fieldName) { Line = dot.Line, Column = dot.Column };
                }
                else
                {
                    Token field = Expect(TokenType.Identifier, "a field name");
                    expr = new MemberExpr(expr, field.Text, null) { Line = dot.Line, Column = dot.Column };
                }
            }
            else if (_matlab && (Check(TokenType.Transpose) || Check(TokenType.DotTranspose)))
            {
                Token op = Advance();
                expr = new TransposeExpr(expr, op.Type == TokenType.Transpose)
                {
                    Line = op.Line,
                    Column = op.Column,
                };
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

    /// <summary>
    /// Parses a comma-separated subscript/argument list up to <paramref name="closer"/>. A lone ':'
    /// filling a whole slot is "all elements" (<c>x(:)</c>).
    /// </summary>
    private List<Expr> ParseSubscripts(TokenType closer, string closerText)
    {
        var arguments = new List<Expr>();
        if (!Check(closer))
        {
            do
            {
                if (Check(TokenType.Colon) && (NextType == TokenType.Comma || NextType == closer))
                {
                    Token colon = Advance();
                    arguments.Add(new AllExpr { Line = colon.Line, Column = colon.Column });
                }
                else
                {
                    // An argument list is punctuated by commas, so spacing carries no meaning here even
                    // when the call itself sits inside a matrix literal.
                    arguments.Add(OutsideLiteral(ParseExpression));
                }
            }
            while (Match(TokenType.Comma));
        }

        Expect(closer, closerText);
        return arguments;
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
                Expr grouped = OutsideLiteral(ParseExpression);
                Expect(TokenType.RParen, "')'");
                return grouped;
            case TokenType.LBracket:
                return ParseArrayLiteral(token);
            case TokenType.LBrace when _matlab:
                return ParseCellLiteral(token);
            case TokenType.At when _matlab:
                return ParseFunctionHandle(token);
            default:
                throw Error(token, $"Expected an expression, but found {token.Describe()}.");
        }
    }

    private Expr ParseArrayLiteral(Token start)
    {
        List<IReadOnlyList<Expr>> rows = ParseLiteralRows(TokenType.RBracket, "']'", out bool rowed);
        return rowed
            ? new MatrixLiteral(rows) { Line = start.Line, Column = start.Column }
            : new ArrayLiteral(rows[0]) { Line = start.Line, Column = start.Column };
    }

    private Expr ParseCellLiteral(Token start)
    {
        List<IReadOnlyList<Expr>> rows = ParseLiteralRows(TokenType.RBrace, "'}'", out _);
        return new CellLiteral(rows) { Line = start.Line, Column = start.Column };
    }

    /// <summary>
    /// The shared body of <c>[...]</c> and <c>{...}</c>: comma-separated elements — or, in MATLAB,
    /// space-separated — with ';' and (in MATLAB) a line break starting a new row. Always returns at
    /// least one row; <paramref name="rowed"/> reports whether any row separator was actually written,
    /// which is what makes <c>[1, 2]</c> a flat array and <c>[1, 2;]</c> a one-row matrix.
    /// </summary>
    private List<IReadOnlyList<Expr>> ParseLiteralRows(TokenType closer, string closerText, out bool rowed)
    {
        Advance(); // '[' or '{'
        var rows = new List<IReadOnlyList<Expr>>();
        var current = new List<Expr>();
        rowed = false;
        _literalDepth++;

        SkipLeadingRowBreaks();
        if (!Check(closer))
        {
            while (true)
            {
                current.Add(ParseExpression());
                if (Match(TokenType.Comma))
                {
                    SkipLeadingRowBreaks(); // a trailing comma may be followed by a continuation
                    continue;
                }

                // ';' (and a line break in MATLAB) starts a new row: [1, 2; 3, 4], or vertical
                // concatenation when the elements are themselves arrays.
                if (IsRowSeparator())
                {
                    rowed = true;
                    rows.Add(current);
                    current = new List<Expr>();
                    while (IsRowSeparator())
                    {
                        Advance(); // consecutive separators are still one row break
                    }

                    if (Check(closer))
                    {
                        break; // tolerate a trailing separator before the closer
                    }

                    continue;
                }

                // MATLAB separates elements by space alone: '[1 2]' is two elements. Only the spacing
                // can say so — '[1 -2]' is two elements while '[1 - 2]' is one subtraction.
                if (_matlab && StartsANewElement())
                {
                    continue;
                }

                break;
            }
        }

        _literalDepth--;
        Expect(closer, closerText);
        if (current.Count > 0 || rows.Count == 0)
        {
            rows.Add(current);
        }

        return rows;
    }

    /// <summary>
    /// Parses <paramref name="inner"/> with the element-splitting rule suspended: inside parentheses,
    /// <c>[f(1 - 2)]</c> is an ordinary subtraction however it is spaced.
    /// </summary>
    private T OutsideLiteral<T>(Func<T> inner)
    {
        int saved = _literalDepth;
        _literalDepth = 0;
        try
        {
            return inner();
        }
        finally
        {
            _literalDepth = saved;
        }
    }

    /// <summary>
    /// Whether the current <c>+</c>/<c>-</c> is the sign of the next element of a space-separated MATLAB
    /// literal rather than a binary operator: <c>[1 -2]</c> has a space before it and none after, while
    /// <c>[1 - 2]</c> and <c>[1-2]</c> are subtractions.
    /// </summary>
    private bool AtElementBreak() =>
        _matlab
        && _literalDepth > 0
        && Current.Type is TokenType.Plus or TokenType.Minus
        && Current.PrecededByWhitespace
        && _pos + 1 < _tokens.Count
        && !_tokens[_pos + 1].PrecededByWhitespace;

    /// <summary>Whether the current token ends a row, consuming it when it does.</summary>
    private bool IsRowSeparator()
    {
        if (Check(TokenType.Semicolon) || (_matlab && Check(TokenType.Newline)))
        {
            Advance();
            return true;
        }

        return false;
    }

    /// <summary>Consumes line breaks between a MATLAB literal's opener (or comma) and the next element.</summary>
    private void SkipLeadingRowBreaks()
    {
        while (_matlab && Check(TokenType.Newline))
        {
            Advance();
        }
    }

    /// <summary>
    /// Whether the current token begins another element of a space-separated MATLAB literal. A leading
    /// sign only starts an element when it is attached to what follows (<c>[1 -2]</c>); with a space on
    /// both sides it is the binary operator (<c>[1 - 2]</c>).
    /// </summary>
    private bool StartsANewElement()
    {
        if (!Current.PrecededByWhitespace)
        {
            return false;
        }

        return Current.Type switch
        {
            TokenType.Number or TokenType.ImaginaryNumber or TokenType.String or TokenType.Identifier
                or TokenType.LParen or TokenType.LBracket or TokenType.LBrace or TokenType.At
                or TokenType.True or TokenType.False or TokenType.End or TokenType.Colon => true,
            TokenType.Plus or TokenType.Minus or TokenType.Bang =>
                _pos + 1 < _tokens.Count && !_tokens[_pos + 1].PrecededByWhitespace,
            _ => false,
        };
    }

    private Expr ParseFunctionHandle(Token start)
    {
        Advance(); // '@'

        // '@name' refers to an existing function; '@(args) expr' defines one inline.
        if (!Match(TokenType.LParen))
        {
            Token name = Expect(TokenType.Identifier, "a function name");
            return new FunctionHandleExpr(name.Text) { Line = start.Line, Column = start.Column };
        }

        var parameters = new List<string>();
        if (!Check(TokenType.RParen))
        {
            do
            {
                parameters.Add(Match(TokenType.Bang) ? "~" : Expect(TokenType.Identifier, "a parameter name").Text);
            }
            while (Match(TokenType.Comma));
        }

        Expect(TokenType.RParen, "')'");
        Expr body = ParseExpression();
        return new AnonymousFnExpr(parameters, body) { Line = start.Line, Column = start.Column };
    }

    // --- Token helpers ------------------------------------------------------------------------

    private bool IsStatementEnd() =>
        Current.Type is TokenType.Newline or TokenType.Semicolon or TokenType.RBrace or TokenType.Eof
            or TokenType.End or TokenType.Else or TokenType.ElseIf
        || IsBodyTerminator();

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
