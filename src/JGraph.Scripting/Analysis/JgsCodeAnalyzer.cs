using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Analysis;

/// <summary>
/// JGraph's Code Analyzer (App Designer window plan W10, decision WO5): the first set of R2025b's checks over
/// a MATLAB file's parse tree, each with R2025b's id, severity, text, span and fix as measured headless
/// (<c>w10_analyzer</c>, <c>w10_rules</c>), the <c>%#ok</c> comments that suppress them (with MSNU and MSNE
/// for the ones that suppress nothing), and the McCabe complexity <c>checkcode -cyc</c> prints. A file the
/// parser refuses is one SYNER message. The analyzer is static: it reads text, never the path.
/// </summary>
public static class JgsCodeAnalyzer
{
    /// <summary>
    /// Analyzes <paramref name="text"/>, a MATLAB file's content; <paramref name="fileName"/> names the file for a script's
    /// complexity line, for the checks of a function's or class's name against it (FNDEF, MCFIL) and, when it is a path, for
    /// a class file's against its <c>@</c> folder (MCDIR; stage P5, ADR 0311).
    /// </summary>
    public static CodeAnalysis Analyze(string text, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> lines = SplitLines(text);
        string stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var pragmas = new List<SourcePragma>();
        IReadOnlyList<Token> tokens;
        IReadOnlyList<Stmt> program;
        try
        {
            tokens = Lexer.Tokenize(text, tolerant: false, JgsDialect.Matlab, pragmas);
        }
        catch (JgsSyntaxException e)
        {
            string at = e.Line >= 1 && e.Line <= lines.Count && e.Column >= 1 && e.Column <= lines[e.Line - 1].Length
                ? "'" + lines[e.Line - 1][e.Column - 1] + "'"
                : "<EOL>";
            return SyntaxError(e, at, lines.Count == 0 || !LooksLikeFunctionFile(lines));
        }

        try
        {
            program = Parser.Parse(tokens, fileName ?? string.Empty, JgsDialect.Matlab);
        }
        catch (JgsSyntaxException e)
        {
            var at = new SourceSpans(tokens, lines);
            int index = at.IndexAt(e.Line, e.Column);
            string what = index < 0 ? "<EOL>" : tokens[index].Type switch
            {
                TokenType.Newline or TokenType.Eof => "<EOL>",
                TokenType.String => "'" + at.Text(at.TokenSpan(index)) + "'",
                _ => "'" + tokens[index].Text + "'",
            };
            return SyntaxError(e, what, !LooksLikeFunctionFile(lines));
        }

        var spans = new SourceSpans(tokens, lines);
        var sink = new List<CodeMessage>();
        var complexity = new List<CodeComplexity>();
        bool isScript = program.Count == 0 || program[0] is not (FnStmt or ClassdefStmt);
        new ScopeAnalysis(spans, sink, complexity, stem, fileName).Run(program, isScript);
        Separators(spans, sink);
        (List<CodeMessage> active, List<CodeMessage> suppressed) = Suppressions.Apply(sink, pragmas, lines);
        return new CodeAnalysis(CodeAnalysis.Sort(active), CodeAnalysis.Sort(suppressed), complexity, isScript);
    }

    private static CodeAnalysis SyntaxError(JgsSyntaxException e, string at, bool isScript)
    {
        int line = Math.Max(1, e.Line);
        int column = Math.Max(1, e.Column);
        string text = string.Format(CodeChecks.SYNER.Text, at);
        var message = new CodeMessage(CodeChecks.SYNER.Id, CodeChecks.SYNER.Severity, line, column, column + Math.Max(0, at.Length - 3), text);
        return new CodeAnalysis([message], [], [], isScript);
    }

    private static bool LooksLikeFunctionFile(IReadOnlyList<string> lines)
    {
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('%'))
            {
                continue;
            }

            return trimmed.StartsWith("function", StringComparison.Ordinal) || trimmed.StartsWith("classdef", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>The text's lines without their breaks (a trailing break adds no line).</summary>
    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>(text.Split('\n'));
        for (int i = 0; i < lines.Count; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>NOCOMMA and NOSEMI: a comma or semicolon right after another separator or a block's <c>end</c>.</summary>
    private static void Separators(SourceSpans spans, List<CodeMessage> sink)
    {
        IReadOnlyList<Token> tokens = spans.Tokens;
        int depth = 0;
        for (int i = 1; i < tokens.Count; i++)
        {
            Token t = tokens[i];
            switch (t.Type)
            {
                case TokenType.LParen or TokenType.LBracket or TokenType.LBrace:
                    depth++;
                    continue;
                case TokenType.RParen or TokenType.RBracket or TokenType.RBrace:
                    depth = Math.Max(0, depth - 1);
                    continue;
            }

            if (depth > 0 || t.Type is not (TokenType.Comma or TokenType.Semicolon))
            {
                continue;
            }

            Token previous = tokens[i - 1];
            if (previous.Line != t.Line)
            {
                continue;
            }

            bool after = previous.Type is TokenType.Comma or TokenType.Semicolon or TokenType.End;
            if (!after)
            {
                continue;
            }

            CodeChecks.Check check = t.Type == TokenType.Comma ? CodeChecks.NOCOMMA : CodeChecks.NOSEMI;
            sink.Add(new CodeMessage(check.Id, check.Severity, t.Line, t.Column, t.Column, check.Text, CodeFix.Of(t.Line, t.Column, t.Column, string.Empty)));
        }
    }

    /// <summary>
    /// The <c>%#ok</c> comments: <c>%#ok</c> alone suppresses every message on its line, <c>%#ok&lt;A,B&gt;</c> those
    /// with the ids named, <c>%#ok&lt;*A&gt;</c> an id in the whole file; the file-wide form wins. A line comment
    /// that suppressed nothing is MSNU, an id no check has is MSNE; the first <c>%#ok</c> of a line is the one read.
    /// </summary>
    private static class Suppressions
    {
        private sealed record Tag(string Id, int Start, int End);

        private sealed record Pragma(int Line, int Start, int End, bool Bare, List<Tag> Tags);

        public static (List<CodeMessage> Active, List<CodeMessage> Suppressed) Apply(List<CodeMessage> messages, List<SourcePragma> pragmas, IReadOnlyList<string> lines)
        {
            var byLine = new Dictionary<int, Pragma>();
            var fileWide = new HashSet<string>(StringComparer.Ordinal);
            foreach (SourcePragma pragma in pragmas)
            {
                if (pragma.Word != "ok" || byLine.ContainsKey(pragma.Line) || pragma.Line < 1 || pragma.Line > lines.Count)
                {
                    continue;
                }

                string line = lines[pragma.Line - 1];
                int at = line.IndexOf("%#ok", StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }

                int start = at + 1;
                var tags = new List<Tag>();
                bool bare = true;
                int end = start + 3;
                if (at + 4 < line.Length && line[at + 4] == '<')
                {
                    int close = line.IndexOf('>', at + 5);
                    if (close > 0)
                    {
                        bare = false;
                        end = close + 1;
                        int cursor = at + 5;
                        foreach (string part in line[(at + 5)..close].Split(','))
                        {
                            string id = part.Trim();
                            int offset = part.IndexOf(id, StringComparison.Ordinal);
                            int tagStart = cursor + offset + 1;
                            if (id.StartsWith('*'))
                            {
                                fileWide.Add(id[1..]);
                            }
                            else if (id.Length > 0)
                            {
                                tags.Add(new Tag(id, tagStart, tagStart + id.Length - 1));
                            }

                            cursor += part.Length + 1;
                        }
                    }
                }

                byLine[pragma.Line] = new Pragma(pragma.Line, start, end, bare, tags);
            }

            var active = new List<CodeMessage>();
            var suppressed = new List<CodeMessage>();
            var usedTags = new HashSet<(int, string)>();
            var usedBare = new HashSet<int>();
            foreach (CodeMessage message in messages)
            {
                if (fileWide.Contains(message.Id))
                {
                    suppressed.Add(message with { Suppression = CodeSuppression.File });
                    continue;
                }

                if (byLine.TryGetValue(message.Line, out Pragma? pragma))
                {
                    if (pragma.Bare)
                    {
                        usedBare.Add(message.Line);
                        suppressed.Add(message with { Suppression = CodeSuppression.Line });
                        continue;
                    }

                    if (pragma.Tags.Any(t => t.Id == message.Id))
                    {
                        usedTags.Add((message.Line, message.Id));
                        suppressed.Add(message with { Suppression = CodeSuppression.Line });
                        continue;
                    }
                }

                active.Add(message);
            }

            foreach (Pragma pragma in byLine.Values)
            {
                if (pragma.Bare)
                {
                    if (!usedBare.Contains(pragma.Line))
                    {
                        active.Add(Note(CodeChecks.MSNU, pragma.Line, pragma.Start, pragma.End, pragma.Start, pragma.End));
                    }

                    continue;
                }

                var idle = new List<(Tag Tag, CodeChecks.Check Check)>();
                foreach (Tag tag in pragma.Tags)
                {
                    if (usedTags.Contains((pragma.Line, tag.Id)))
                    {
                        continue;
                    }

                    if (CodeChecks.ById(tag.Id) is not null)
                    {
                        idle.Add((tag, CodeChecks.MSNU));
                    }
                    else if (!CodeChecks.KnownElsewhere.Contains(tag.Id))
                    {
                        idle.Add((tag, CodeChecks.MSNE));
                    }
                }

                if (idle.Count == 0)
                {
                    continue;
                }

                if (idle.Count == pragma.Tags.Count && idle.All(i => i.Check == idle[0].Check))
                {
                    // Nothing in the comment was needed: the whole comment is marked, and the fix takes it out.
                    active.Add(Note(idle[0].Check, pragma.Line, pragma.Start, pragma.End, pragma.Start, pragma.End));
                    continue;
                }

                foreach ((Tag tag, CodeChecks.Check check) in idle)
                {
                    // The id and its comma go; the first id takes the comma after it.
                    string line = lines[pragma.Line - 1];
                    int removeStart = tag.Start;
                    int removeEnd = tag.End;
                    if (removeEnd < line.Length && line[removeEnd] == ',')
                    {
                        removeEnd++;
                    }
                    else if (removeStart >= 2 && line[removeStart - 2] == ',')
                    {
                        removeStart--;
                    }

                    active.Add(Note(check, pragma.Line, tag.Start, tag.End, removeStart, removeEnd));
                }
            }

            return (active, suppressed);
        }

        private static CodeMessage Note(CodeChecks.Check check, int line, int start, int end, int removeStart, int removeEnd) =>
            new(check.Id, check.Severity, line, start, end, check.Text, CodeFix.Of(line, removeStart, removeEnd, string.Empty));
    }

    /// <summary>Whether <paramref name="name"/> is a check id's shape: capitals and digits.</summary>
    internal static bool LooksLikeId(string name) => Regex.IsMatch(name, "^[A-Z][A-Z0-9]*$");
}
