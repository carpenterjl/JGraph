using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Turns JGS source text into a flat list of <see cref="Token"/>s. Newlines are significant statement
/// separators, except inside round or square brackets (so calls and array literals may span lines); runs of
/// blank lines collapse to a single separator. Line comments start with <c>#</c> or <c>//</c>. Strings are
/// double- or single-quoted (interchangeably, MATLAB-style) with the usual <c>\n \t \r \\ \" \'</c> escapes.
/// </summary>
internal static class Lexer
{
    private static readonly IReadOnlyDictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>(StringComparer.Ordinal)
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
    };

    /// <summary>The keyword spellings, for the builtin catalog (and through it, editors).</summary>
    public static IEnumerable<string> KeywordNames => Keywords.Keys;

    /// <summary>Lexes <paramref name="source"/> into tokens, terminated by a single <see cref="TokenType.Eof"/>.</summary>
    /// <param name="source">The JGS source text.</param>
    /// <param name="tolerant">When true, never throws: an unterminated string becomes a string token to the
    /// end of the line and an unexpected character is skipped. Used by the completion engine, whose input is
    /// a buffer mid-keystroke and therefore routinely broken.</param>
    /// <exception cref="JgsSyntaxException">On an unterminated string or an unexpected character (only when
    /// <paramref name="tolerant"/> is false).</exception>
    public static IReadOnlyList<Token> Tokenize(string source, bool tolerant = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new List<Token>();
        int i = 0;
        int line = 1;
        int lineStart = 0;      // index where the current line begins, for column computation
        int bracketDepth = 0;   // '(' and '[' nesting; newlines inside brackets are insignificant

        int Column(int index) => index - lineStart + 1;

        void Add(TokenType type, string text, int startIndex, double number = 0) =>
            tokens.Add(new Token(type, text, number, line, Column(startIndex)));

        while (i < source.Length)
        {
            char c = source[i];

            // Newlines: significant at bracket depth 0, otherwise whitespace.
            if (c == '\n')
            {
                if (bracketDepth == 0 && tokens.Count > 0 && tokens[^1].Type != TokenType.Newline)
                {
                    Add(TokenType.Newline, "\\n", i);
                }

                i++;
                line++;
                lineStart = i;
                continue;
            }

            if (c is ' ' or '\t' or '\r')
            {
                i++;
                continue;
            }

            // Line comments: '#...' or '//...' to end of line.
            if (c == '#' || (c == '/' && i + 1 < source.Length && source[i + 1] == '/'))
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            int start = i;

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
                Add(Keywords.TryGetValue(word, out TokenType keyword) ? keyword : TokenType.Identifier, word, start);
                continue;
            }

            // Operators and punctuation (two-character operators first).
            TokenType type;
            string lexeme;
            switch (c)
            {
                case '(': type = TokenType.LParen; lexeme = "("; bracketDepth++; break;
                case ')': type = TokenType.RParen; lexeme = ")"; if (bracketDepth > 0) bracketDepth--; break;
                case '[': type = TokenType.LBracket; lexeme = "["; bracketDepth++; break;
                case ']': type = TokenType.RBracket; lexeme = "]"; if (bracketDepth > 0) bracketDepth--; break;
                case '{': type = TokenType.LBrace; lexeme = "{"; break;
                case '}': type = TokenType.RBrace; lexeme = "}"; break;
                case ',': type = TokenType.Comma; lexeme = ","; break;
                case ';': type = TokenType.Newline; lexeme = ";"; break;
                case '+': type = TokenType.Plus; lexeme = "+"; break;
                case '-': type = TokenType.Minus; lexeme = "-"; break;
                case '*': type = TokenType.Star; lexeme = "*"; break;
                case '/': type = TokenType.Slash; lexeme = "/"; break;
                case '%': type = TokenType.Percent; lexeme = "%"; break;
                case '=' when Peek(source, i) == '=': type = TokenType.EqualEqual; lexeme = "=="; break;
                case '=': type = TokenType.Assign; lexeme = "="; break;
                case '!' when Peek(source, i) == '=': type = TokenType.BangEqual; lexeme = "!="; break;
                case '!': type = TokenType.Bang; lexeme = "!"; break;
                case '<' when Peek(source, i) == '=': type = TokenType.LessEqual; lexeme = "<="; break;
                case '<': type = TokenType.Less; lexeme = "<"; break;
                case '>' when Peek(source, i) == '=': type = TokenType.GreaterEqual; lexeme = ">="; break;
                case '>': type = TokenType.Greater; lexeme = ">"; break;
                case '&' when Peek(source, i) == '&': type = TokenType.AmpAmp; lexeme = "&&"; break;
                case '|' when Peek(source, i) == '|': type = TokenType.PipePipe; lexeme = "||"; break;
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

        // A trailing newline separator carries no statement; drop it for a cleaner token stream.
        if (tokens.Count > 0 && tokens[^1].Type == TokenType.Newline)
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        tokens.Add(new Token(TokenType.Eof, string.Empty, 0, line, Column(i)));
        return tokens;
    }

    private static char Peek(string source, int index) =>
        index + 1 < source.Length ? source[index + 1] : '\0';

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
