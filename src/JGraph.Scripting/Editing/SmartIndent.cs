using System.Text.RegularExpressions;

namespace JGraph.Scripting.Editing;

/// <summary>How a function's contents are indented (<c>matlab.editor.language.matlab.FunctionIndentingFormat</c>).</summary>
public enum IndentMode
{
    /// <summary><c>MixedFunctionIndent</c>: a nested function's and a method's contents are indented, a top-level function's are not.</summary>
    Mixed,

    /// <summary><c>AllFunctionIndent</c>: every function's contents are indented.</summary>
    All,

    /// <summary><c>ClassicFunctionIndent</c>: no function's contents are indented.</summary>
    Classic,
}

/// <summary>
/// Smart Indent, free of a window (App Designer window plan W9b): what R2025b's formatter makes of MATLAB code (<c>indentcode</c> over
/// <c>matlab.codeanalyzer.internal.formatCode</c>, measured by <c>ui-probes/w9/w9_indent.m</c>), carried over from the rich text component's
/// <c>rtc/plugins/indentutilities/IndentUtilities</c>: each line's indent from the statement before it - one more indent after a line that
/// opens a block or stands as <c>else</c>, <c>case</c> or <c>catch</c>, one less for each block a line closes - a closing <c>end</c> or a
/// middle keyword at its block's own indent, a continued line one indent in, a nested function one indent past the function it is in, a
/// function's contents by the mode. Lines inside a block comment are left as they are, and an empty line is padded or emptied as asked.
/// </summary>
public static class SmartIndent
{
    private static readonly Regex LeadingWhitespace = new(@"^\s*", RegexOptions.CultureInvariant);
    private static readonly Regex TrailingWhitespace = new(@"[ \t]+$", RegexOptions.CultureInvariant);

    private enum Kind
    {
        Other,
        Comment,
        BlockStart,
        BlockMid,
        BlockEnd,
        Start,
        Mid,
        End,
    }

    private sealed class Token
    {
        public Kind Kind;
        public string Text = string.Empty;
        public int Line;
        public bool FirstOnLine;

        /// <summary>For an <see cref="Kind.End"/> or <see cref="Kind.Mid"/> token, the block's opener; for a <see cref="Kind.Start"/>, the opener enclosing it.</summary>
        public Token? Opener;

        /// <summary>For a function, the lines its signature spans (a continued signature runs on).</summary>
        public int SignatureLine = -1;
        public int SignatureEnd = -1;

        /// <summary>For a <see cref="Kind.Start"/> token, the <c>end</c> that closes it, or null when none does.</summary>
        public Token? Closer;

        public bool IsFunction => Kind == Kind.Start && Text == "function";
        public bool IsSwitch => Kind == Kind.Start && Text == "switch";
    }

    private sealed class Line
    {
        public int Index;
        public bool HasContinuation;
        public bool InBlockComment;
        public Token? First;
        public int FirstTokenIndex = -1;
        public int LastTokenIndex = -1;

        /// <summary>Whether the line is part of <paramref name="function"/>'s signature.</summary>
        public bool IsSignatureOf(Token function) => Index >= function.SignatureLine && Index <= function.SignatureEnd;
    }

    private static readonly HashSet<string> Openers = ["if", "for", "parfor", "while", "switch", "try", "function", "classdef", "spmd"];
    private static readonly HashSet<string> ClassOpeners = ["properties", "methods", "events", "enumeration"];
    private static readonly HashSet<string> Mids = ["else", "elseif", "case", "otherwise", "catch"];

    /// <summary>
    /// Formats lines <paramref name="first"/> to <paramref name="last"/> (zero-based) of <paramref name="lines"/>, each by the lines above it as they
    /// now stand, and gives back the lines; those outside the range are left as they are.
    /// </summary>
    public static List<string> Format(IReadOnlyList<string> lines, int first, int last, IndentMode mode, bool padEmptyLines, bool trimWhitespace = true)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var text = new List<string>(lines);
        if (text.Count == 0)
        {
            return text;
        }

        (List<Token> tokens, Line[] info, bool haveEnds) = Lex(lines);
        first = Math.Max(0, first);
        last = Math.Min(lines.Count - 1, last);
        for (int v = first; v <= last; v++)
        {
            string line = text[v];
            if (!padEmptyLines && line.Length == 0)
            {
                continue;
            }

            if (info[v].InBlockComment)
            {
                // A block comment's lines keep their indent; their trailing white space goes like any other line's.
                if (trimWhitespace)
                {
                    text[v] = TrailingWhitespace.Replace(line, string.Empty);
                }

                continue;
            }

            bool hasText = Regex.IsMatch(line, @"\S");
            if (!hasText && !padEmptyLines)
            {
                text[v] = string.Empty;
                continue;
            }

            int level = IndentLevelFor(v, text, tokens, info, mode, haveEnds);
            string body = line[LeadingWhitespace.Match(line).Length..];
            if (hasText && trimWhitespace)
            {
                body = TrailingWhitespace.Replace(body, string.Empty);
            }

            text[v] = EditorTabs.Spaces(level) + (hasText ? body : string.Empty);
        }

        return text;
    }

    /// <summary>One block of the text: its keyword (or <c>blockComment</c>, <c>case</c>, <c>otherwise</c>) and its first and last lines, zero-based.</summary>
    public readonly record struct Block(string Kind, int First, int Last);

    /// <summary>
    /// The text's blocks, as R2025b's mtree finds them: each opener to its <c>end</c>, a function without one to its last line before the
    /// next function or the end of the text, a <c>case</c> or <c>otherwise</c> to the line before the next of its switch, a block comment
    /// to its <c>%}</c>.
    /// </summary>
    public static IReadOnlyList<Block> Blocks(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        (List<Token> tokens, Line[] info, _) = Lex(lines);
        var blocks = new List<Block>();
        for (int i = 0; i < tokens.Count; i++)
        {
            Token t = tokens[i];
            if (t.Kind == Kind.Start)
            {
                int last = t.Closer?.Line ?? LastLineOfOpenBlock(tokens, i, info);
                blocks.Add(new Block(t.Text, t.Line, last));
            }
            else if (t.Kind == Kind.Mid && t.Text is "case" or "otherwise" && t.Opener is { Text: "switch" } opener)
            {
                int last = LastLineBefore(tokens, i, opener, info);
                blocks.Add(new Block(t.Text, t.Line, last));
            }
            else if (t.Kind == Kind.BlockStart && t.Opener is { } close)
            {
                blocks.Add(new Block("blockComment", t.Line, close.Line));
            }
        }

        return blocks;
    }

    /// <summary>A function with no <c>end</c> runs to the line of its last token - a comment's too - before the next function at its level or the text's end.</summary>
    private static int LastLineOfOpenBlock(List<Token> tokens, int at, Line[] info)
    {
        Token opener = tokens[at];
        int last = opener.Line;
        for (int i = at + 1; i < tokens.Count; i++)
        {
            Token t = tokens[i];
            if (t.Kind == Kind.Start && t.IsFunction && t.Opener is not null && ReferenceEquals(t.Opener, opener))
            {
                break;
            }

            last = t.Line;
        }

        _ = info;
        return last;
    }

    /// <summary>
    /// A <c>case</c> runs to the line of its last token - a comment's too, as <c>mtree</c> with <c>-comments</c> has a case's last node - before
    /// the next <c>case</c>, <c>otherwise</c> or <c>end</c> of its switch.
    /// </summary>
    private static int LastLineBefore(List<Token> tokens, int at, Token opener, Line[] info)
    {
        int last = tokens[at].Line;
        for (int i = at + 1; i < tokens.Count; i++)
        {
            Token t = tokens[i];
            if ((t.Kind == Kind.Mid && ReferenceEquals(t.Opener, opener)) || (t.Kind == Kind.End && ReferenceEquals(t.Opener, opener)))
            {
                break;
            }

            last = t.Line;
        }

        _ = info;
        return last;
    }

    /// <summary>The indent a line would take now (<c>findIndentLevelForLine</c>), in visual columns.</summary>
    public static int IndentLevelOf(IReadOnlyList<string> lines, int line, IndentMode mode)
    {
        ArgumentNullException.ThrowIfNull(lines);
        (List<Token> tokens, Line[] info, bool haveEnds) = Lex(lines);
        return IndentLevelFor(line, lines, tokens, info, mode, haveEnds);
    }

    private static int IndentOf(IReadOnlyList<string> text, int line) => EditorTabs.VisualLength(LeadingWhitespace.Match(text[line]).Value);

    /// <summary><c>findIndentLevelForLine</c> and <c>_findIndentLevel</c>; <paramref name="haveEnds"/> is <c>doFunctionsHaveEnds</c>.</summary>
    private static int IndentLevelFor(int v, IReadOnlyList<string> text, List<Token> tokens, Line[] info, IndentMode mode, bool haveEnds)
    {
        int continuation = 0;
        if (v > 0 && info[v - 1].HasContinuation)
        {
            // processLineContinuations: the first continued line goes one indent in; the ones after it stay level with it.
            continuation = v > 1 && info[v - 2].HasContinuation ? 0 : EditorTabs.IndentSize;
        }

        Token? d = info[v].First;
        int level = -1;
        if (d is not null)
        {
            if (d.Kind == Kind.Mid)
            {
                if (d.Opener is { } start)
                {
                    level = IndentOf(text, start.Line) + (start.IsSwitch ? EditorTabs.IndentSize : 0);
                }
            }
            else if (d.Kind is Kind.End or Kind.BlockEnd)
            {
                level = d.Opener is { } start ? IndentOf(text, start.Line) : -1;
            }
            else if (d.IsFunction)
            {
                // A file whose functions have no ends keeps every function's line at the margin; with ends, a nested one stands one indent past the function it is in.
                if (!haveEnds)
                {
                    level = 0;
                }
                else if (IsNested(d))
                {
                    level = IndentOf(text, d.Opener!.Line) + EditorTabs.IndentSize;
                }
            }
        }

        if (level < 0)
        {
            int from = info[v].FirstTokenIndex >= 0 ? info[v].FirstTokenIndex : IndexBeforeLine(tokens, v);
            Token? y = PreviousReal(tokens, from, null, info);
            if (y is not null)
            {
                Token? t = TokenForIndentCalculation(v, y.Line, info);
                if (y.Kind == Kind.End && ReferenceEquals(y, t))
                {
                    level = IndentOf(text, y.Line);
                    if (ShouldDeIndentAfterBlock(y, mode))
                    {
                        level -= EditorTabs.IndentSize;
                    }
                }
                else if (t is not null)
                {
                    level = SearchBackAndCalculateIndent(y, t, text, tokens, info, mode);
                }
            }
        }

        level += continuation;
        return Math.Max(0, level);
    }

    private static int IndexBeforeLine(List<Token> tokens, int line)
    {
        int i = 0;
        while (i < tokens.Count && tokens[i].Line < line)
        {
            i++;
        }

        return i;
    }

    /// <summary><c>_getTokenForIndentCalculation</c>: the first token of the statement the previous token belongs to.</summary>
    private static Token? TokenForIndentCalculation(int v, int m, Line[] info)
    {
        if (v > 0 && info[v - 1].HasContinuation)
        {
            return info[m].First;
        }

        while (m > 0)
        {
            if (!info[m - 1].HasContinuation)
            {
                return info[m].First;
            }

            m--;
        }

        return info[0].First;
    }

    /// <summary><c>_findNonWhitespaceAndComment</c> going back from before <paramref name="from"/>: the nearest token that is not a comment off the line's start nor a block comment's, stopping at <paramref name="stop"/>.</summary>
    private static Token? PreviousReal(List<Token> tokens, int from, Token? stop, Line[] info)
    {
        for (int i = from - 1; i >= 0; i--)
        {
            Token p = tokens[i];
            if (IsReal(p))
            {
                return p;
            }

            if (ReferenceEquals(p, stop))
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsReal(Token p)
    {
        if (p.Kind is Kind.BlockStart or Kind.BlockMid or Kind.BlockEnd)
        {
            return false;
        }

        if (p.Kind == Kind.Comment && !p.FirstOnLine)
        {
            return false;
        }

        return true;
    }

    private static int IndexOf(List<Token> tokens, Token token) => tokens.IndexOf(token);

    /// <summary><c>searchBackAndCalculateIndent</c>: the statement's first line's indent, plus one indent per block it opens and less one per block it closes.</summary>
    private static int SearchBackAndCalculateIndent(Token y, Token t, IReadOnlyList<string> text, List<Token> tokens, Line[] info, IndentMode mode)
    {
        int s = 0;
        Token current = y;
        Token? v = y;
        while (v is not null)
        {
            current = v;
            if (v.Kind is Kind.Start or Kind.Mid)
            {
                if ((!v.IsFunction || ShouldIndentFunctionContents(v, mode)) && (ReferenceEquals(v, t) || v.Kind == Kind.Start))
                {
                    s++;
                }
            }
            else if (v.Kind == Kind.End && !ReferenceEquals(v, t))
            {
                s--;
                if (mode == IndentMode.Mixed && v.Opener is { IsFunction: true } opener && IsNested(opener))
                {
                    s--;
                }
            }

            v = ReferenceEquals(v, t) ? null : PreviousReal(tokens, IndexOf(tokens, v), t, info);
        }

        return IndentOf(text, current.Line) + s * EditorTabs.IndentSize;
    }

    /// <summary><c>_shouldIndentFunctionContents</c>: always in All, never in Classic, in Mixed for a function inside a function or a class.</summary>
    private static bool ShouldIndentFunctionContents(Token function, IndentMode mode) => mode switch
    {
        IndentMode.All => true,
        IndentMode.Classic => false,
        _ => InFunctionOrClass(function),
    };

    private static bool InFunctionOrClass(Token token)
    {
        for (Token? e = token.Opener; e is not null; e = e.Opener)
        {
            if (e.IsFunction || e.Text == "classdef")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>_isTokenNestedFunction</c>: a function whose enclosing block is a function.</summary>
    private static bool IsNested(Token function) => function.IsFunction && function.Opener is { IsFunction: true };

    /// <summary><c>_shouldDeIndentAfterBlock</c>: after a nested function's <c>end</c>, back to the parent's contents when those are not indented.</summary>
    private static bool ShouldDeIndentAfterBlock(Token end, IndentMode mode)
    {
        if (end.Opener is not { IsFunction: true } function || !IsNested(function))
        {
            return false;
        }

        return mode == IndentMode.Classic || (mode == IndentMode.Mixed && !InFunctionOrClass(function.Opener!));
    }

    // --- the lexer -------------------------------------------------------------------------------------------------------

    private static (List<Token> Tokens, Line[] Lines, bool FunctionsHaveEnds) Lex(IReadOnlyList<string> lines)
    {
        var tokens = new List<Token>();
        var info = new Line[lines.Count];
        var stack = new List<Token>();
        var bodyStarted = new Dictionary<Token, bool>();
        int blockComment = 0;
        int depth = 0;
        bool continued = false;
        for (int v = 0; v < lines.Count; v++)
        {
            var line = new Line { Index = v };
            info[v] = line;
            string text = lines[v];
            string trimmed = text.Trim();
            if (blockComment > 0 || (!continued && trimmed == "%{"))
            {
                Kind kind = trimmed == "%{" ? Kind.BlockStart : trimmed == "%}" ? Kind.BlockEnd : Kind.BlockMid;
                if (kind == Kind.BlockStart)
                {
                    blockComment++;
                }

                var token = new Token { Kind = kind, Text = trimmed, Line = v, FirstOnLine = true };
                if (kind == Kind.BlockEnd)
                {
                    blockComment--;
                    for (int k = tokens.Count - 1; k >= 0; k--)
                    {
                        if (tokens[k].Kind == Kind.BlockStart && tokens[k].Opener is null)
                        {
                            token.Opener = tokens[k];
                            tokens[k].Opener = token;
                            break;
                        }
                    }
                }

                line.InBlockComment = kind == Kind.BlockMid;
                AddToken(tokens, line, token);
                continue;
            }

            if (!continued)
            {
                depth = 0;
            }

            continued = false;
            int i = 0;
            bool transposable = false;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '%')
                {
                    AddToken(tokens, line, new Token { Kind = Kind.Comment, Text = text[i..], Line = v });
                    break;
                }

                if (c == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
                {
                    line.HasContinuation = true;
                    continued = true;
                    AddToken(tokens, line, new Token { Kind = Kind.Comment, Text = text[i..], Line = v });
                    break;
                }

                if (c == '"' || (c == '\'' && !transposable))
                {
                    int j = i + 1;
                    while (j < text.Length)
                    {
                        if (text[j] == c)
                        {
                            if (j + 1 < text.Length && text[j + 1] == c)
                            {
                                j += 2;
                                continue;
                            }

                            break;
                        }

                        j++;
                    }

                    AddToken(tokens, line, new Token { Kind = Kind.Other, Text = text[i..Math.Min(j + 1, text.Length)], Line = v });
                    i = j + 1;
                    transposable = true;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int j = i;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_'))
                    {
                        j++;
                    }

                    string word = text[i..j];
                    bool afterDot = i > 0 && text[i - 1] == '.';
                    var token = new Token { Kind = Kind.Other, Text = word, Line = v };
                    if (depth == 0 && !afterDot)
                    {
                        Classify(token, stack, bodyStarted, line, v);
                    }

                    AddToken(tokens, line, token);
                    if (token.Kind == Kind.Other && stack.Count > 0 && stack[^1].IsFunction && !line.IsSignatureOf(stack[^1]))
                    {
                        bodyStarted[stack[^1]] = true;
                    }

                    i = j;
                    transposable = true;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int j = i;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '.'))
                    {
                        j++;
                    }

                    AddToken(tokens, line, new Token { Kind = Kind.Other, Text = text[i..j], Line = v });
                    MarkBody(stack, bodyStarted, line);
                    i = j;
                    transposable = true;
                    continue;
                }

                if (c is '(' or '[' or '{')
                {
                    depth++;
                    transposable = false;
                }
                else if (c is ')' or ']' or '}')
                {
                    depth = Math.Max(0, depth - 1);
                    transposable = true;
                }
                else
                {
                    transposable = c == '\'' && transposable;
                }

                AddToken(tokens, line, new Token { Kind = Kind.Other, Text = c.ToString(), Line = v });
                if (c is not (';' or ','))
                {
                    MarkBody(stack, bodyStarted, line);
                }

                i++;
            }

            // A function's signature runs on while its line is continued.
            if (line.HasContinuation && stack.Count > 0 && stack[^1].IsFunction && stack[^1].SignatureEnd == v)
            {
                stack[^1].SignatureEnd = v + 1;
            }
        }

        // doFunctionsHaveEnds: a function still open at the end of the text says the file's functions have none.
        bool haveEnds = !stack.Any(static t => t.IsFunction);
        return (tokens, info, haveEnds);
    }

    private static void MarkBody(List<Token> stack, Dictionary<Token, bool> bodyStarted, Line line)
    {
        if (stack.Count > 0 && stack[^1].IsFunction && !line.IsSignatureOf(stack[^1]))
        {
            bodyStarted[stack[^1]] = true;
        }
    }

    /// <summary>A keyword's part in the structure: openers push, <c>end</c> pops, the class blocks count in a class, <c>arguments</c> only at a function's start.</summary>
    private static void Classify(Token token, List<Token> stack, Dictionary<Token, bool> bodyStarted, Line line, int v)
    {
        string word = token.Text;
        Token? top = stack.Count > 0 ? stack[^1] : null;
        if (word == "end")
        {
            token.Kind = Kind.End;
            if (top is not null)
            {
                token.Opener = top;
                top.Closer = token;
                stack.RemoveAt(stack.Count - 1);
            }

            return;
        }

        bool opens = Openers.Contains(word)
            || (ClassOpeners.Contains(word) && top is { Text: "classdef" })
            || (word == "arguments" && top is { IsFunction: true } function && !bodyStarted.GetValueOrDefault(function) && !line.IsSignatureOf(function));
        if (opens)
        {
            token.Kind = Kind.Start;
            token.Opener = top;
            stack.Add(token);
            if (word == "function")
            {
                token.SignatureLine = v;
                token.SignatureEnd = v;
                bodyStarted[token] = false;
            }

            return;
        }

        if (Mids.Contains(word))
        {
            token.Kind = Kind.Mid;
            token.Opener = top;
        }
    }

    private static void AddToken(List<Token> tokens, Line line, Token token)
    {
        token.FirstOnLine = line.First is null;
        if (line.First is null)
        {
            line.First = token;
            line.FirstTokenIndex = tokens.Count;
        }

        line.LastTokenIndex = tokens.Count;
        tokens.Add(token);
    }
}
