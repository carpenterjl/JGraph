using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Where the expression a frame was running when an error left it begins (open item 336, ADR 0378): the
/// start of a call or index chain (<c>P7C.staterr()</c>, <c>s.b.c(3)</c>), or, when <see cref="Chain"/> is
/// false, the place an error was raised inside an argument list (a name, an operator).
/// </summary>
internal readonly record struct ReportAnchor(int Line, int Column, bool Chain)
{
    /// <summary>The line and column a call or index chain starts at: its leftmost name, on the call's own line.</summary>
    internal static (int Line, int Column) StartOf(Expr expression)
    {
        Expr at = expression;
        while (true)
        {
            switch (at)
            {
                case CallExpr call:
                    at = call.Callee;
                    continue;
                case IndexExpr index:
                    at = index.Target;
                    continue;
                case BraceIndexExpr brace:
                    at = brace.Target;
                    continue;
                case MemberExpr member:
                    at = member.Target;
                    continue;
            }

            break;
        }

        return at.Line == expression.Line && at.Column > 0 ? (at.Line, at.Column) : (expression.Line, expression.Column);
    }
}

/// <summary>
/// What a caught MException's report needs that its stack does not hold (open item 336, ADR 0378): for each
/// frame, the name the stack gives it and the line (so a stack a script changed is not misread), the name
/// R2025b's report writes for it when the stack's name does not say it, and the anchor of what it ran.
/// </summary>
internal sealed record ReportInfo(
    (string Name, int Line, string? Shown, ReportAnchor? Anchor)[] Frames,
    bool ShowsRaisingFrame, int ErrorLine, int ErrorColumn);

/// <summary>
/// R2025b's <c>getReport</c> in its default ('extended') form (open item 336, ADR 0378, measured): the header
/// and message, then one "Error in" block per frame the error unwound through - its name and line, the line of
/// code as the file holds it, and a line of carets under the expression it was running - then its causes.
/// </summary>
internal static class JgsErrorReport
{
    /// <summary>
    /// <paramref name="text"/> wrapped as R2025b wraps a report's message at a console <paramref name="width"/>
    /// characters wide (open item 350, ADR 0385, measured at 180 under -batch): a line holds at most width - 1
    /// characters and breaks at the last space or tab that keeps it so, the run of blanks there dropped; a word
    /// longer than that stays whole and breaks at the first blank after it; a continuation line starts with
    /// the leading spaces of the line it continues (a cause's four included); a carriage return starts the
    /// count again. Each line of the text is wrapped on its own.
    /// </summary>
    internal static string WrapToConsole(string text, int width)
    {
        int most = width - 1;
        if (most < 1 || text.Length <= most)
        {
            return text;
        }

        string[] lines = text.Split('\n');
        for (int k = 0; k < lines.Length; k++)
        {
            if (lines[k].Length > most)
            {
                lines[k] = string.Join('\r', lines[k].Split('\r').Select(segment => WrapLine(segment, most)));
            }
        }

        return string.Join('\n', lines);
    }

    private static string WrapLine(string line, int most)
    {
        static bool Blank(char c) => c is ' ' or '\t';
        int indentLength = 0;
        while (indentLength < line.Length && line[indentLength] == ' ')
        {
            indentLength++;
        }

        string indent = line[..indentLength];
        var wrapped = new StringBuilder();
        string rest = line;
        while (rest.Length > most)
        {
            // The last run of blanks starting within the line's room, then the first one after it.
            int cut = -1;
            for (int i = most; i > indentLength; i--)
            {
                if (Blank(rest[i]) && !Blank(rest[i - 1]))
                {
                    cut = i;
                    break;
                }
            }

            if (cut < 0)
            {
                for (int i = most + 1; i < rest.Length; i++)
                {
                    if (Blank(rest[i]))
                    {
                        cut = i;
                        break;
                    }
                }
            }

            if (cut < 0)
            {
                break; // one word longer than the line: it stays whole
            }

            string head = rest[..cut].TrimEnd(' ', '\t');
            string tail = rest[cut..].TrimStart(' ', '\t');
            wrapped.Append(head);
            if (tail.Length == 0)
            {
                return wrapped.ToString();
            }

            wrapped.Append('\n');
            rest = indent + tail;
        }

        return wrapped.Append(rest).ToString();
    }

    /// <summary>The source lines of the files read for reports, by path, with the time each was read at.</summary>
    private static readonly Dictionary<string, (DateTime Written, string[] Lines)> Sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The "Error in" blocks for <paramref name="frames"/>, joined as R2025b joins them: a frame with a line
    /// is its header, its code and its carets; a handle made from text has no line and is its header over a
    /// blank line. Empty when there is no frame.
    /// </summary>
    internal static string ErrorInBlocks(IReadOnlyList<(string Shown, string Name, string File, int Line, ReportAnchor? Anchor, string? Inner, (int Line, int Column)? Raised)> frames)
    {
        var blocks = new List<string>(frames.Count);
        foreach ((string shown, string name, string file, int line, ReportAnchor? anchor, string? inner, (int Line, int Column)? raised) in frames)
        {
            bool lineless = line == 0 && name.StartsWith('@');
            var block = new StringBuilder("Error in ").Append(shown);
            if (lineless)
            {
                blocks.Add(block.Append('\n').ToString());
                continue;
            }

            block.Append(" (line ").Append(line).Append(')');
            if (SourceLine(file, line) is { } code)
            {
                block.Append('\n').Append(code);
                if (Underline(code, line, anchor, inner, raised) is { } span && span.Length > 0)
                {
                    block.Append('\n').Append(' ', span.Start).Append('^', span.Length);
                }
            }

            blocks.Add(block.ToString());
        }

        return string.Join('\n', blocks);
    }

    /// <summary>The 1-based <paramref name="line"/> of <paramref name="file"/> as the file holds it, or null when it cannot be read.</summary>
    private static string? SourceLine(string file, int line)
    {
        if (line <= 0 || file.Length == 0 || !Path.IsPathRooted(file)
            || !Path.GetExtension(file).Equals(".m", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            DateTime written = File.GetLastWriteTimeUtc(file);
            string[] lines;
            lock (Sources)
            {
                if (!Sources.TryGetValue(file, out (DateTime Written, string[] Lines) held) || held.Written != written)
                {
                    string text = JgsSourceText.Read(file);
                    held = (written, text.Split('\n'));
                    Sources[file] = held;
                }

                lines = held.Lines;
            }

            return line <= lines.Length ? lines[line - 1].TrimEnd('\r') : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The characters the carets stand under: the anchored expression when the anchor is on this line, else
    /// the place the error was raised (the frame it was raised in), else the call of the frame inside this
    /// one found by its name (a frame still running when the error was caught, whose expression no filter
    /// saw), else an <c>eval</c> call on the line.
    /// </summary>
    private static (int Start, int Length)? Underline(string code, int line, ReportAnchor? anchor, string? inner, (int Line, int Column)? raised)
    {
        if (anchor is { } at && at.Line == line && at.Column >= 1 && at.Column <= code.Length)
        {
            return at.Chain ? ChainAt(code, at.Column - 1) : TokenAt(code, at.Column - 1);
        }

        if (raised is { } place && place.Line == line && place.Column >= 1 && place.Column <= code.Length)
        {
            return TokenAt(code, place.Column - 1);
        }

        bool[] mask = CodeMask(code);
        if (inner is not null && FindCallOf(code, mask, inner) is int found)
        {
            return ChainAt(code, found);
        }

        foreach (string evaluating in (string[])["evalc", "evalin", "eval"])
        {
            if (FindName(code, mask, evaluating, 0) is int call && NextSignificant(code, call + evaluating.Length) == '(')
            {
                return ChainAt(code, call);
            }
        }

        return null;
    }

    /// <summary>
    /// The start of the call of <paramref name="inner"/> (a frame's stack name) on the line: its last name
    /// (<c>staterr</c> of <c>P7C.staterr</c>, <c>inner</c> of <c>f/inner</c>) with the names dotted before it;
    /// for an anonymous frame, the call whose argument list holds the first handle written on the line.
    /// </summary>
    private static int? FindCallOf(string code, bool[] mask, string inner)
    {
        if (inner.StartsWith('@'))
        {
            int handle = IndexInCode(code, mask, '@');
            if (handle < 0)
            {
                return null;
            }

            int depth = 0;
            for (int i = handle - 1; i >= 0; i--)
            {
                if (!mask[i])
                {
                    continue;
                }

                if (code[i] is ')' or ']' or '}')
                {
                    depth++;
                }
                else if (code[i] is '[' or '{')
                {
                    depth = System.Math.Max(0, depth - 1);
                }
                else if (code[i] == '(')
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    int end = i - 1;
                    while (end >= 0 && code[end] == ' ')
                    {
                        end--;
                    }

                    int start = end;
                    while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] is '_' or '.'))
                    {
                        start--;
                    }

                    return start < end ? start + 1 : null;
                }
            }

            return null;
        }

        int cut = inner.LastIndexOfAny(['>', '/', '.']);
        string simple = cut >= 0 ? inner[(cut + 1)..] : inner;
        if (simple.Length == 0)
        {
            return null;
        }

        if (FindName(code, mask, simple, 0) is int at)
        {
            int start = at;
            while (start >= 2 && code[start - 1] == '.' && (char.IsLetterOrDigit(code[start - 2]) || code[start - 2] == '_'))
            {
                int previous = start - 2;
                while (previous > 0 && (char.IsLetterOrDigit(code[previous - 1]) || code[previous - 1] == '_'))
                {
                    previous--;
                }

                start = previous;
            }

            return start;
        }

        return null;
    }

    /// <summary>The first <paramref name="what"/> in the code part of the line, or -1.</summary>
    private static int IndexInCode(string code, bool[] mask, char what)
    {
        for (int i = 0; i < code.Length; i++)
        {
            if (mask[i] && code[i] == what)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Where the name <paramref name="name"/> stands whole in the code part of the line from <paramref name="from"/>, or null.</summary>
    private static int? FindName(string code, bool[] mask, string name, int from)
    {
        for (int at = code.IndexOf(name, from, StringComparison.Ordinal); at >= 0; at = code.IndexOf(name, at + 1, StringComparison.Ordinal))
        {
            bool whole = (at == 0 || !(char.IsLetterOrDigit(code[at - 1]) || code[at - 1] == '_'))
                && (at + name.Length >= code.Length || !(char.IsLetterOrDigit(code[at + name.Length]) || code[at + name.Length] == '_'));
            if (whole && mask[at])
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>The first character after <paramref name="from"/> that is not a blank, or '\0'.</summary>
    private static char NextSignificant(string code, int from)
    {
        for (int i = from; i < code.Length; i++)
        {
            if (code[i] is not (' ' or '\t'))
            {
                return code[i];
            }
        }

        return '\0';
    }

    /// <summary>
    /// Whether the character at <paramref name="i"/> opens a quoted text: a double quote, or a single quote
    /// where no operand ends just before it (<paramref name="last"/> is the last character that was not a blank).
    /// </summary>
    private static bool OpensText(string code, int i, char last) =>
        code[i] == '"'
        || (code[i] == '\'' && (i > 0 && code[i - 1] is ' ' or '\t'
            || !(char.IsLetterOrDigit(last) || last is '_' or ')' or ']' or '}' or '\'' or '.')));

    /// <summary>
    /// Which characters of the line are code, not a quoted text or a comment: a quote opens text where an
    /// operand cannot end (a transpose follows a name, a number, a closing bracket or another transpose).
    /// </summary>
    private static bool[] CodeMask(string code)
    {
        // (an operand followed by a blank and then a quote is a text in a list, as in [x(3) '%>'])
        var mask = new bool[code.Length];
        char last = '\0';
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '%')
            {
                break;
            }

            if (OpensText(code, i, last))
            {
                int close = i + 1;
                while (close < code.Length)
                {
                    if (code[close] == c)
                    {
                        if (close + 1 < code.Length && code[close + 1] == c)
                        {
                            close += 2;
                            continue;
                        }

                        break;
                    }

                    close++;
                }

                i = close;
                last = 'a'; // a text is an operand
                continue;
            }

            mask[i] = true;
            if (c is not (' ' or '\t'))
            {
                last = c;
            }
        }

        return mask;
    }

    /// <summary>
    /// The chain that starts at <paramref name="start"/>: a name, then any run of <c>.name</c>, <c>.(expr)</c>,
    /// <c>(...)</c> and <c>{...}</c>; a token alone when no name starts there.
    /// </summary>
    private static (int Start, int Length) ChainAt(string code, int start)
    {
        if (!(char.IsLetter(code[start]) || code[start] == '_'))
        {
            return TokenAt(code, start);
        }

        int i = start;
        while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
        {
            i++;
        }

        while (i < code.Length)
        {
            if (code[i] == '.' && i + 1 < code.Length && (char.IsLetter(code[i + 1]) || code[i + 1] == '_'))
            {
                i += 2;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                {
                    i++;
                }

                continue;
            }

            if (code[i] == '.' && i + 1 < code.Length && code[i + 1] == '(')
            {
                i++;
            }

            if (code[i] is '(' or '{')
            {
                int close = Closing(code, i);
                if (close < 0)
                {
                    i = code.Length;
                    break;
                }

                i = close + 1;
                continue;
            }

            break;
        }

        return (start, i - start);
    }

    /// <summary>The bracket closing the one at <paramref name="open"/>, quoted text skipped, or -1 when the line ends first.</summary>
    private static int Closing(string code, int open)
    {
        int depth = 0;
        char last = '(';
        for (int i = open; i < code.Length; i++)
        {
            char c = code[i];
            if (OpensText(code, i, last))
            {
                int close = i + 1;
                while (close < code.Length && !(code[close] == c && (close + 1 >= code.Length || code[close + 1] != c)))
                {
                    close += code[close] == c ? 2 : 1;
                }

                i = close;
                last = 'a';
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' && --depth == 0)
            {
                return i;
            }

            if (c is not (' ' or '\t'))
            {
                last = c;
            }
        }

        return -1;
    }

    /// <summary>The one token at <paramref name="start"/>: a name, a number, or an operator of one or two characters.</summary>
    private static (int Start, int Length) TokenAt(string code, int start)
    {
        char c = code[start];
        if (char.IsLetter(c) || c == '_')
        {
            int end = start;
            while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
            {
                end++;
            }

            // A name with a list after it is the call or index it starts (zeros('a'), x(5)).
            return NextSignificant(code, end) is '(' or '{' ? ChainAt(code, start) : (start, end - start);
        }

        if (char.IsDigit(c))
        {
            int end = start;
            while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '.'))
            {
                end++;
            }

            return (start, end - start);
        }

        if (start + 1 < code.Length)
        {
            string pair = code.Substring(start, 2);
            if (pair is ".*" or "./" or ".\\" or ".^" or ".'" or "==" or "~=" or "<=" or ">=" or "&&" or "||")
            {
                return (start, 2);
            }
        }

        return (start, 1);
    }
}
