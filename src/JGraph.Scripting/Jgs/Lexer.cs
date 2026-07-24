using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Turns JGS source text into a flat list of <see cref="Token"/>s. Newlines are significant statement
/// separators, except inside round or square brackets (so calls and array literals may span lines); runs of
/// blank lines collapse to a single separator. A ';' is its own <see cref="TokenType.Semicolon"/> token — a
/// separator that additionally suppresses console echo, and a row separator inside array literals. Line
/// comments start with <c>#</c> or <c>//</c>. Strings are double- or single-quoted (interchangeably,
/// MATLAB-style) with the usual <c>\n \t \r \\ \" \'</c> escapes. A number with a trailing <c>i</c> or
/// <c>j</c> (<c>2i</c>, <c>1.5j</c>) is an imaginary literal.
/// <para>
/// The same lexer serves MATLAB source: a <see cref="JgsDialect"/> selects the details the two languages
/// spell differently, starting with <c>%</c> — a comment in MATLAB, modulo in JGS.
/// </para>
/// </summary>
internal static class Lexer
{
    private static readonly IReadOnlyDictionary<string, TokenType> JgsKeywords = new Dictionary<string, TokenType>(StringComparer.Ordinal)
    {
        ["let"] = TokenType.Let,
        ["fn"] = TokenType.Fn,
        ["return"] = TokenType.Return,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["for"] = TokenType.For,
        ["while"] = TokenType.While,
        ["in"] = TokenType.In,
        ["break"] = TokenType.Break,
        ["continue"] = TokenType.Continue,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["end"] = TokenType.End,
        ["elseif"] = TokenType.ElseIf,
    };

    /// <summary>
    /// MATLAB's keywords. <c>let</c>, <c>fn</c> and <c>in</c> are absent — they are not MATLAB words, so a
    /// script may use them as ordinary variable names — and the block words MATLAB adds are present.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, TokenType> MatlabKeywords = new Dictionary<string, TokenType>(StringComparer.Ordinal)
    {
        ["return"] = TokenType.Return,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["elseif"] = TokenType.ElseIf,
        ["for"] = TokenType.For,
        ["while"] = TokenType.While,
        ["break"] = TokenType.Break,
        ["continue"] = TokenType.Continue,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["end"] = TokenType.End,
        ["function"] = TokenType.Function,
        ["switch"] = TokenType.Switch,
        ["case"] = TokenType.Case,
        ["otherwise"] = TokenType.Otherwise,
        ["try"] = TokenType.Try,
        ["catch"] = TokenType.Catch,
        ["global"] = TokenType.Global,
    };

    /// <summary>The JGS keyword spellings, for the builtin catalog (and through it, editors).</summary>
    public static IEnumerable<string> KeywordNames => JgsKeywords.Keys;

    /// <summary>The MATLAB keyword spellings, for the MATLAB editor's highlighting.</summary>
    public static IEnumerable<string> MatlabKeywordNames => MatlabKeywords.Keys;

    /// <summary>Lexes <paramref name="source"/> into tokens, terminated by a single <see cref="TokenType.Eof"/>.</summary>
    /// <param name="source">The source text.</param>
    /// <param name="tolerant">When true, never throws: an unterminated string becomes a string token to the
    /// end of the line and an unexpected character is skipped. Used by the completion engine, whose input is
    /// a buffer mid-keystroke and therefore routinely broken.</param>
    /// <param name="dialect">The language variant to lex, or null for <see cref="JgsDialect.Jgs"/>.</param>
    /// <exception cref="JgsSyntaxException">On an unterminated string or an unexpected character (only when
    /// <paramref name="tolerant"/> is false).</exception>
    public static IReadOnlyList<Token> Tokenize(string source, bool tolerant = false, JgsDialect? dialect = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        dialect ??= JgsDialect.Jgs;
        bool matlab = dialect.IsMatlab;
        IReadOnlyDictionary<string, TokenType> keywords = matlab ? MatlabKeywords : JgsKeywords;

        var tokens = new List<Token>();
        int i = 0;
        int line = 1;
        int lineStart = 0;      // index where the current line begins, for column computation
        int parenDepth = 0;     // '(' nesting; newlines inside a call or grouping are insignificant
        int matrixDepth = 0;    // '[' nesting (and '{' where braces build cells)
        bool spaceBefore = false;

        int Column(int index) => index - lineStart + 1;

        void Add(TokenType type, string text, int startIndex, double number = 0)
        {
            tokens.Add(new Token(type, text, number, line, Column(startIndex), spaceBefore));
            spaceBefore = false;
        }

        // Inside a MATLAB matrix or cell a newline separates rows, so it must reach the parser — but only
        // where a separator can stand, otherwise '[1 2;\n3 4]' would report two row breaks instead of one.
        bool NewlineIsSignificant() => matlab
            ? parenDepth == 0 && (matrixDepth == 0 || tokens.Count == 0 || tokens[^1].Type is not (
                TokenType.Newline or TokenType.Semicolon or TokenType.Comma
                or TokenType.LBracket or TokenType.LBrace))
            : parenDepth == 0 && matrixDepth == 0;

        while (i < source.Length)
        {
            char c = source[i];

            // Newlines: significant at bracket depth 0 (and between MATLAB matrix rows), else whitespace.
            if (c == '\n')
            {
                if (NewlineIsSignificant() && tokens.Count > 0 && tokens[^1].Type != TokenType.Newline)
                {
                    Add(TokenType.Newline, "\\n", i);
                }

                i++;
                line++;
                lineStart = i;
                spaceBefore = true; // a row break separates elements just as a space does
                continue;
            }

            if (c is ' ' or '\t' or '\r')
            {
                i++;
                spaceBefore = true;
                continue;
            }

            // MATLAB's '...' continuation: the rest of the line, and the line break itself, are whitespace.
            if (matlab && c == '.' && i + 2 < source.Length && source[i + 1] == '.' && source[i + 2] == '.')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                if (i < source.Length)
                {
                    i++; // the line break the continuation swallows
                    line++;
                    lineStart = i;
                }

                spaceBefore = true;
                continue;
            }

            // MATLAB's block comment: '%{' and '%}' each alone on a line, everything between ignored.
            if (matlab && c == '%' && i + 1 < source.Length && source[i + 1] == '{'
                && IsAloneOnLine(source, i, lineStart))
            {
                SkipBlockComment(source, ref i, ref line, ref lineStart);
                continue;
            }

            // Line comments: '#...' or '//...' to end of line, plus '%...' in MATLAB (where '%' is a
            // comment, not modulo — mod() and rem() are the operators there).
            if (c == '#' || (c == '%' && dialect.PercentComment)
                || (c == '/' && i + 1 < source.Length && source[i + 1] == '/'))
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            int start = i;

            // MATLAB spells transpose with the same character it uses to quote a char literal. It is
            // transpose when it follows something transposable and nothing separates them; a space before
            // it always starts a literal, which is what makes command syntax ("disp 'hi'") read correctly.
            if (matlab && c == '\'' && !spaceBefore && tokens.Count > 0 && tokens[^1].Type is
                TokenType.Identifier or TokenType.Number or TokenType.ImaginaryNumber or TokenType.End
                or TokenType.RParen or TokenType.RBracket or TokenType.RBrace
                or TokenType.Transpose or TokenType.DotTranspose)
            {
                Add(TokenType.Transpose, "'", start);
                i++;
                continue;
            }

            if (matlab && c is '"' or '\'')
            {
                Add(TokenType.String, ReadMatlabString(source, ref i, line, Column(start), c, tolerant), start);
                continue;
            }

            if (c is '"' or '\'')
            {
                if (tolerant)
                {
                    // Mid-keystroke buffers routinely hold an unterminated string; take it to end of line.
                    int end = i + 1;
                    while (end < source.Length && source[end] != c && source[end] != '\n')
                    {
                        end += source[end] == '\\' && end + 1 < source.Length && source[end + 1] != '\n' ? 2 : 1;
                    }

                    bool closed = end < source.Length && source[end] == c;
                    Add(TokenType.String, source[(i + 1)..System.Math.Min(end, source.Length)], start);
                    i = closed ? end + 1 : end;
                    continue;
                }

                Add(TokenType.String, ReadString(source, ref i, line, Column(start), c), start);
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                string text = ReadNumber(source, ref i);
                double value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

                // A trailing 'i'/'j' not glued to a longer identifier makes an imaginary literal (2i, 1.5j).
                if (i < source.Length && (source[i] == 'i' || source[i] == 'j')
                    && (i + 1 >= source.Length || (!char.IsLetterOrDigit(source[i + 1]) && source[i + 1] != '_')))
                {
                    i++;
                    Add(TokenType.ImaginaryNumber, source[start..i], start, value);
                    continue;
                }

                Add(TokenType.Number, text, start, value);
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                string word = source[start..i];
                Add(keywords.TryGetValue(word, out TokenType keyword) ? keyword : TokenType.Identifier, word, start);
                continue;
            }

            // Operators and punctuation (two-character operators first).
            TokenType type;
            string lexeme;
            switch (c)
            {
                case '(': type = TokenType.LParen; lexeme = "("; parenDepth++; break;
                case ')': type = TokenType.RParen; lexeme = ")"; if (parenDepth > 0) parenDepth--; break;
                case '[': type = TokenType.LBracket; lexeme = "["; matrixDepth++; break;
                case ']': type = TokenType.RBracket; lexeme = "]"; if (matrixDepth > 0) matrixDepth--; break;

                // Braces build cell arrays in MATLAB (so they nest like '[') but delimit block bodies in
                // JGS, where a newline inside them is a statement separator and must stay significant.
                case '{': type = TokenType.LBrace; lexeme = "{"; if (matlab) { matrixDepth++; } break;
                case '}': type = TokenType.RBrace; lexeme = "}"; if (matlab && matrixDepth > 0) { matrixDepth--; } break;
                case ',': type = TokenType.Comma; lexeme = ","; break;
                case ';': type = TokenType.Semicolon; lexeme = ";"; break;
                case ':': type = TokenType.Colon; lexeme = ":"; break;
                case '^': type = TokenType.Caret; lexeme = "^"; break;
                case '~' when Peek(source, i) == '=': type = TokenType.BangEqual; lexeme = "~="; break;
                case '~': type = TokenType.Bang; lexeme = "~"; break;
                // JGS's '*' is already elementwise, so '.*' is just an alias there. MATLAB's is matrix
                // multiplication, so the two spellings must stay distinguishable.
                case '.' when Peek(source, i) == '*': type = matlab ? TokenType.DotStar : TokenType.Star; lexeme = ".*"; break;
                case '.' when Peek(source, i) == '/': type = matlab ? TokenType.DotSlash : TokenType.Slash; lexeme = "./"; break;
                case '.' when Peek(source, i) == '^': type = matlab ? TokenType.DotCaret : TokenType.Caret; lexeme = ".^"; break;
                case '.' when matlab && Peek(source, i) == '\'': type = TokenType.DotTranspose; lexeme = ".'"; break;
                case '.' when matlab: type = TokenType.Dot; lexeme = "."; break;
                case '@' when matlab: type = TokenType.At; lexeme = "@"; break;
                case '+' when Peek(source, i) == '+': type = TokenType.PlusPlus; lexeme = "++"; break;
                case '+' when Peek(source, i) == '=': type = TokenType.PlusAssign; lexeme = "+="; break;
                case '+': type = TokenType.Plus; lexeme = "+"; break;
                case '-' when Peek(source, i) == '-': type = TokenType.MinusMinus; lexeme = "--"; break;
                case '-' when Peek(source, i) == '=': type = TokenType.MinusAssign; lexeme = "-="; break;
                case '-': type = TokenType.Minus; lexeme = "-"; break;
                case '*' when Peek(source, i) == '=': type = TokenType.StarAssign; lexeme = "*="; break;
                case '*': type = TokenType.Star; lexeme = "*"; break;
                case '/' when Peek(source, i) == '=': type = TokenType.SlashAssign; lexeme = "/="; break;
                case '/': type = TokenType.Slash; lexeme = "/"; break;
                case '%' when Peek(source, i) == '=': type = TokenType.PercentAssign; lexeme = "%="; break;
                case '%': type = TokenType.Percent; lexeme = "%"; break;
                case '=' when Peek(source, i) == '=': type = TokenType.EqualEqual; lexeme = "=="; break;
                case '=': type = TokenType.Assign; lexeme = "="; break;
                case '!' when matlab:
                    // '!' runs a shell command in MATLAB. JGraph's scripting sandbox has no shell, and
                    // silently reading it as 'not' would change what a script means.
                    throw new JgsSyntaxException(line, Column(start),
                        "The shell escape '!' is not supported in JGraph. Use '~' for logical not.");
                case '!' when Peek(source, i) == '=': type = TokenType.BangEqual; lexeme = "!="; break;
                case '!': type = TokenType.Bang; lexeme = "!"; break;
                case '<' when Peek(source, i) == '=': type = TokenType.LessEqual; lexeme = "<="; break;
                case '<': type = TokenType.Less; lexeme = "<"; break;
                case '>' when Peek(source, i) == '=': type = TokenType.GreaterEqual; lexeme = ">="; break;
                case '>': type = TokenType.Greater; lexeme = ">"; break;
                case '&' when Peek(source, i) == '&': type = TokenType.AmpAmp; lexeme = "&&"; break;
                case '|' when Peek(source, i) == '|': type = TokenType.PipePipe; lexeme = "||"; break;

                // MATLAB's elementwise logical operators: like && and || but without short-circuiting,
                // and defined over whole arrays.
                case '&' when matlab: type = TokenType.Amp; lexeme = "&"; break;
                case '|' when matlab: type = TokenType.Pipe; lexeme = "|"; break;
                default:
                    if (tolerant)
                    {
                        i++;
                        continue;
                    }

                    throw new JgsSyntaxException(line, Column(start), $"Unexpected character '{c}'.");
            }

            Add(type, lexeme, start);
            i += lexeme.Length;
        }

        // A trailing newline separator carries no statement; drop it for a cleaner token stream. A trailing
        // ';' stays — the parser needs it to mark the final statement as echo-suppressed.
        if (tokens.Count > 0 && tokens[^1].Type == TokenType.Newline)
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        tokens.Add(new Token(TokenType.Eof, string.Empty, 0, line, Column(i)));
        return tokens;
    }

    private static char Peek(string source, int index) =>
        index + 1 < source.Length ? source[index + 1] : '\0';

    /// <summary>Whether only whitespace precedes <paramref name="index"/> on its line.</summary>
    private static bool IsAloneOnLine(string source, int index, int lineStart)
    {
        for (int j = lineStart; j < index; j++)
        {
            if (source[j] is not (' ' or '\t' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Skips a MATLAB block comment: from the <c>%{</c> line through the matching <c>%}</c> line, or to
    /// the end of the file if the closer is missing (a missing closer comments out the rest of the file in
    /// MATLAB too). Nesting is honoured, as MATLAB does.
    /// </summary>
    private static void SkipBlockComment(string source, ref int i, ref int line, ref int lineStart)
    {
        int depth = 0;
        while (i < source.Length)
        {
            int start = i;
            while (i < source.Length && source[i] != '\n')
            {
                i++;
            }

            ReadOnlySpan<char> text = source.AsSpan(start, i - start).Trim();
            if (text is "%{")
            {
                depth++;
            }
            else if (text is "%}")
            {
                depth--;
            }

            if (i < source.Length)
            {
                i++; // the newline
                line++;
                lineStart = i;
            }

            if (depth == 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reads a MATLAB char literal (<c>'…'</c>) or string (<c>"…"</c>). MATLAB has no backslash escapes —
    /// a lone backslash is just a backslash, which is what makes Windows paths writable — and doubles the
    /// quote character to embed it.
    /// </summary>
    private static string ReadMatlabString(string source, ref int i, int line, int column, char quote, bool tolerant)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (true)
        {
            if (i >= source.Length || source[i] == '\n')
            {
                if (tolerant)
                {
                    return sb.ToString(); // a buffer mid-keystroke: take what is there
                }

                throw new JgsSyntaxException(line, column, "Unterminated string literal.");
            }

            if (source[i] == quote)
            {
                if (i + 1 < source.Length && source[i + 1] == quote)
                {
                    sb.Append(quote); // '' inside a literal is one quote
                    i += 2;
                    continue;
                }

                i++; // closing quote
                return sb.ToString();
            }

            sb.Append(source[i]);
            i++;
        }
    }

    private static string ReadNumber(string source, ref int i)
    {
        int start = i;
        while (i < source.Length && char.IsDigit(source[i]))
        {
            i++;
        }

        if (i < source.Length && source[i] == '.')
        {
            i++;
            while (i < source.Length && char.IsDigit(source[i]))
            {
                i++;
            }
        }

        if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
        {
            int mark = i;
            i++;
            if (i < source.Length && (source[i] == '+' || source[i] == '-'))
            {
                i++;
            }

            if (i < source.Length && char.IsDigit(source[i]))
            {
                while (i < source.Length && char.IsDigit(source[i]))
                {
                    i++;
                }
            }
            else
            {
                i = mark; // Not an exponent after all (e.g. "2e" or "1.eq"); leave the 'e' to the lexer.
            }
        }

        return source[start..i];
    }

    private static string ReadString(string source, ref int i, int line, int column, char quote)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (i < source.Length && source[i] != quote)
        {
            char c = source[i];
            if (c == '\n')
            {
                throw new JgsSyntaxException(line, column, "Unterminated string literal.");
            }

            if (c == '\\' && i + 1 < source.Length)
            {
                char next = source[i + 1];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\'' => '\'',
                    '\\' => '\\',
                    _ => next,
                });
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        if (i >= source.Length)
        {
            throw new JgsSyntaxException(line, column, "Unterminated string literal.");
        }

        i++; // closing quote
        return sb.ToString();
    }
}
