using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S2: lexing MATLAB. The apostrophe carries the risk here — the same character quotes a char literal
/// and transposes a matrix — so the rule is pinned exhaustively: it is transpose only when it follows
/// something transposable with no space between. Everything else in this file is the smaller MATLAB/JGS
/// spelling differences: comments, escapes, continuations, and the elementwise logical operators.
/// </summary>
public class MatlabLexerTests
{
    private static IReadOnlyList<Token> Lex(string source) =>
        Lexer.Tokenize(source, tolerant: false, JgsDialect.Matlab);

    private static TokenType[] Types(string source) =>
        Lex(source).Select(static t => t.Type).ToArray();

    // --- The apostrophe -------------------------------------------------------------------------

    [Theory]
    [InlineData("a'")]              // after an identifier
    [InlineData("2'")]              // after a number
    [InlineData("(a + b)'")]        // after a closing paren
    [InlineData("[1, 2]'")]         // after a closing bracket
    [InlineData("{1, 2}'")]         // after a closing brace
    [InlineData("x(end)'")]         // after 'end'
    public void Apostrophe_AfterSomethingTransposable_IsTranspose(string source) =>
        Assert.Equal(TokenType.Transpose, Lex(source)[^2].Type);

    [Fact]
    public void Apostrophe_TransposesTwice()
    {
        // a'' is a transpose of a transpose, not the start of a literal.
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Transpose, TokenType.Transpose, TokenType.Eof },
            Types("a''"));
    }

    [Theory]
    [InlineData("x = 'hi'")]        // after '='
    [InlineData("f('hi')")]         // after '('
    [InlineData("f(a, 'hi')")]      // after ','
    [InlineData("'hi'")]            // at the start of the input
    [InlineData("[1, 2] + 'hi'")]   // after an operator
    public void Apostrophe_ElsewhereOpensACharLiteral(string source)
    {
        Token literal = Assert.Single(Lex(source), static t => t.Type == TokenType.String);
        Assert.Equal("hi", literal.Text);
    }

    [Fact]
    public void SpaceBeforeApostrophe_OpensALiteral_SoCommandSyntaxReads()
    {
        // 'disp hello' style command syntax: 'disp 'hi'' must be a string, not a transpose of disp.
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.String, TokenType.Eof },
            Types("disp 'hi'"));
    }

    [Fact]
    public void InsideAMatrix_ASpacedApostropheIsALiteral_AnAttachedOneIsTranspose()
    {
        Assert.Equal(
            new[] { TokenType.LBracket, TokenType.Identifier, TokenType.String, TokenType.RBracket, TokenType.Eof },
            Types("[a 'b']"));

        Assert.Equal(
            new[] { TokenType.LBracket, TokenType.Identifier, TokenType.Transpose, TokenType.RBracket, TokenType.Eof },
            Types("[a']"));
    }

    [Fact]
    public void DoubledQuote_EscapesAQuoteInsideALiteral()
    {
        Assert.Equal("it's", Lex("x = 'it''s'")[^2].Text);
        Assert.Equal("say \"hi\"", Lex("x = \"say \"\"hi\"\"\"")[^2].Text);
    }

    [Fact]
    public void Backslash_IsLiteral_SoWindowsPathsSurvive()
    {
        // MATLAB has no backslash escapes: this is the whole reason a path can be written plainly.
        Assert.Equal(@"C:\temp\new.txt", Lex(@"p = 'C:\temp\new.txt'")[^2].Text);
    }

    [Fact]
    public void DotApostrophe_IsPlainTranspose()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.DotTranspose, TokenType.Eof },
            Types("a.'"));
    }

    [Fact]
    public void UnterminatedLiteral_IsAnError()
    {
        JgsSyntaxException error = Assert.Throws<JgsSyntaxException>(static () => Lex("x = 'oops"));
        Assert.Contains("Unterminated string", error.Message, StringComparison.Ordinal);
    }

    // --- Comments and continuations -------------------------------------------------------------

    [Fact]
    public void Percent_StartsAComment()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Assign, TokenType.Number, TokenType.Eof },
            Types("x = 1 % the rest of this line is a comment"));
    }

    [Fact]
    public void PercentBrace_OpensABlockComment()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Assign, TokenType.Number, TokenType.Eof },
            Types("""
                %{
                everything in here is ignored
                x = 999
                %}
                x = 1
                """));
    }

    [Fact]
    public void PercentBrace_MidLine_IsJustALineComment()
    {
        // MATLAB only treats '%{' as a block opener when it stands alone on its line.
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Assign, TokenType.Number, TokenType.Newline, TokenType.Identifier, TokenType.Eof },
            Types("x = 1 %{ not a block\ny"));
    }

    [Fact]
    public void Ellipsis_ContinuesTheStatementOnTheNextLine()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Assign, TokenType.Number, TokenType.Plus, TokenType.Number, TokenType.Eof },
            Types("x = 1 + ... a trailing note\n    2"));
    }

    // --- Rows, spacing, and operators -----------------------------------------------------------

    [Fact]
    public void NewlineInsideAMatrix_SeparatesRows()
    {
        Assert.Equal(
            new[]
            {
                TokenType.LBracket, TokenType.Number, TokenType.Number, TokenType.Newline,
                TokenType.Number, TokenType.Number, TokenType.RBracket, TokenType.Eof,
            },
            Types("[1 2\n3 4]"));
    }

    [Fact]
    public void NewlineAfterARowSeparator_IsNotASecondRowBreak()
    {
        Assert.Equal(
            new[]
            {
                TokenType.LBracket, TokenType.Number, TokenType.Semicolon,
                TokenType.Number, TokenType.RBracket, TokenType.Eof,
            },
            Types("[1;\n2]"));
    }

    [Fact]
    public void NewlineInsideParens_IsStillInsignificant()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.LParen, TokenType.Number, TokenType.Comma, TokenType.Number, TokenType.RParen, TokenType.Eof },
            Types("f(1,\n2)"));
    }

    [Fact]
    public void Whitespace_IsRecorded_SoTheParserCanSplitMatrixElements()
    {
        // '[1 -2]' is two elements and '[1 - 2]' is one; only the spacing distinguishes them, so the
        // lexer has to carry it.
        IReadOnlyList<Token> twoElements = Lex("[1 -2]");
        Assert.True(twoElements[2].PrecededByWhitespace);   // the '-'
        Assert.False(twoElements[3].PrecededByWhitespace);  // the '2' is glued to it

        IReadOnlyList<Token> subtraction = Lex("[1 - 2]");
        Assert.True(subtraction[2].PrecededByWhitespace);
        Assert.True(subtraction[3].PrecededByWhitespace);
    }

    [Fact]
    public void ElementwiseLogicalOperators_AreTheirOwnTokens()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Amp, TokenType.Identifier, TokenType.Pipe, TokenType.Identifier, TokenType.Eof },
            Types("a & b | c"));

        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.AmpAmp, TokenType.Identifier, TokenType.Eof },
            Types("a && b"));
    }

    [Fact]
    public void FieldAccess_AndFunctionHandles_Lex()
    {
        Assert.Equal(
            new[] { TokenType.Identifier, TokenType.Dot, TokenType.Identifier, TokenType.Eof },
            Types("s.field"));

        Assert.Equal(
            new[] { TokenType.At, TokenType.LParen, TokenType.Identifier, TokenType.RParen, TokenType.Identifier, TokenType.Eof },
            Types("@(x) x"));
    }

    [Fact]
    public void MatlabKeywords_AreRecognized_AndJgsOnlyWordsAreNot()
    {
        Assert.Equal(TokenType.Function, Lex("function y = f(x)")[0].Type);
        Assert.Equal(TokenType.Switch, Lex("switch x")[0].Type);
        Assert.Equal(TokenType.Try, Lex("try")[0].Type);
        Assert.Equal(TokenType.Global, Lex("global g")[0].Type);

        // 'let' and 'fn' are not MATLAB words, so a MATLAB script may use them as variable names.
        Assert.Equal(TokenType.Identifier, Lex("let = 1")[0].Type);
        Assert.Equal(TokenType.Identifier, Lex("fn = 1")[0].Type);
    }

    [Fact]
    public void ShellEscape_IsRejectedWithAnExplanation()
    {
        JgsSyntaxException error = Assert.Throws<JgsSyntaxException>(static () => Lex("!dir"));
        Assert.Contains("shell escape", error.Message, StringComparison.Ordinal);
    }

    // --- JGS is untouched -----------------------------------------------------------------------

    [Fact]
    public void JgsLexing_IsUnchanged()
    {
        // The same characters mean the JGS things they always did.
        Assert.Equal(TokenType.Percent, Lexer.Tokenize("7 % 3")[1].Type);
        Assert.Equal("a\nb", Lexer.Tokenize(@"'a\nb'")[0].Text);   // backslash escapes still decode
        Assert.Equal(
            new[] { TokenType.LBracket, TokenType.Number, TokenType.Comma, TokenType.Number, TokenType.RBracket, TokenType.Eof },
            Lexer.Tokenize("[1,\n2]").Select(static t => t.Type).ToArray()); // newline stays insignificant

        // '.', '@', '&' and '|' remain unknown characters in JGS, not new tokens.
        Assert.Throws<JgsSyntaxException>(static () => Lexer.Tokenize("s.field"));
        Assert.Throws<JgsSyntaxException>(static () => Lexer.Tokenize("@(x) x"));
        Assert.Throws<JgsSyntaxException>(static () => Lexer.Tokenize("a & b"));
    }
}
