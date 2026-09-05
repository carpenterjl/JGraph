using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>regexp</c>, <c>regexpi</c> and <c>regexprep</c> the way MATLAB runs them, on top of .NET's
/// engine.
/// </summary>
/// <remarks>
/// <para>
/// The two engines agree on most of the language and disagree on the edges, and every
/// disagreement here was measured against R2025b rather than read from the documentation:
/// </para>
/// <list type="bullet">
/// <item><description>A zero-length match is not a match unless <c>'emptymatch'</c> says so — and
/// that includes a pattern that can only match nothing, so <c>regexprep(s, '^', '>')</c> leaves
/// <c>s</c> alone and <c>regexp('abc', 'b*')</c> answers 2, the one place the pattern matches
/// something. .NET has no such mode; <see cref="MatchesOf"/> gets it from a <c>\G</c> lookbehind
/// that forbids an empty match at the search position and a loop that moves the position up.</description></item>
/// <item><description>MATLAB numbers every capturing group left to right, named or not; .NET numbers
/// the unnamed ones first. <c>$1</c> and the token lists follow MATLAB, so the pattern is read once
/// to learn where its groups are.</description></item>
/// <item><description>A group on the branch of an alternation that was not taken is not a token at
/// all — <c>regexp('ab', '(a)|(b)', 'tokens')</c> is <c>{{'a'}, {'b'}}</c> — where an optional group
/// that simply did not match is an empty token. The same read of the pattern tells the two apart.</description></item>
/// <item><description><c>\&lt;</c> and <c>\&gt;</c> are word anchors, <c>\b</c> is a backspace,
/// <c>\o{101}</c> and <c>\x{41}</c> are code points, and <c>$</c> means the very end of the text
/// unless <c>'lineanchors'</c> is on. <see cref="TranslatePattern"/> rewrites those before .NET sees
/// them.</description></item>
/// <item><description>The replacement text of <c>regexprep</c> decodes <c>\n</c>, <c>\\</c> and
/// <c>\$</c>, reads <c>$0</c>, <c>$N</c> and <c>$&lt;name&gt;</c>, and evaluates <c>${expr}</c> with
/// the tokens spliced in as quoted text.</description></item>
/// </list>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>How long a regular expression may run before it is treated as pathological.</summary>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(5);

    /// <summary>The words that name an output of <c>regexp</c>, in MATLAB's own default order.</summary>
    private static readonly string[] RegexOutputWords =
        ["start", "end", "tokenExtents", "match", "tokens", "names", "split"];

    /// <summary>The words that change how the expression is matched, shared by every regex builtin.</summary>
    private static readonly string[] RegexModeWords =
    [
        "once", "matchcase", "ignorecase", "preservecase", "noemptymatch", "emptymatch",
        "dotall", "dotexceptnewline", "stringanchors", "lineanchors", "literalspacing", "freespacing",
        "forceCellOutput", "warnings",
    ];

    /// <summary>
    /// Pairs of option words MATLAB refuses to see together (measured). The other opposites —
    /// <c>'lineanchors'</c> against <c>'stringanchors'</c>, say — were not measured and are left to
    /// the rule the words always had here: the last one named wins.
    /// </summary>
    private static readonly (string, string)[] RegexConflicts =
    [
        ("ignorecase", "matchcase"), ("ignorecase", "preservecase"), ("emptymatch", "noemptymatch"),
    ];

    /// <summary>What MATLAB's regular-expression option words select, once they have all been read.</summary>
    /// <param name="Options">The .NET flags the words add up to.</param>
    /// <param name="Once">Whether only the first match counts.</param>
    /// <param name="EmptyMatch">Whether a zero-length match is a match.</param>
    /// <param name="PreserveCase">Whether a replacement takes the case of the text it replaces.</param>
    /// <param name="ForceCell">Whether one piece of text still answers in a cell.</param>
    private readonly record struct RegexMode(
        RegexOptions Options, bool Once, bool EmptyMatch, bool PreserveCase, bool ForceCell);

    /// <summary>
    /// A capturing group as MATLAB counts it. <paramref name="Name"/> is null for a plain group, in
    /// which case <paramref name="Ordinal"/> is its number among the unnamed groups — which is also
    /// its .NET number. <paramref name="Path"/> is the chain of alternations it sits inside, as
    /// (node, branch) pairs from the outside in, so two groups can be asked whether they are on
    /// different branches of the same <c>|</c>.
    /// </summary>
    private readonly record struct CaptureGroup(string? Name, int Ordinal, (int Node, int Branch)[] Path);

    /// <summary>A pattern read MATLAB's way and compiled .NET's way.</summary>
    private sealed class MatlabRegex
    {
        public MatlabRegex(string source, Regex compiled, Regex plain, CaptureGroup[] groups)
        {
            Source = source;
            Compiled = compiled;
            Plain = plain;
            Groups = groups;
        }

        /// <summary>The pattern as the script wrote it.</summary>
        public string Source { get; }

        /// <summary>
        /// The translated pattern, wrapped as <c>(?:…)(?&lt;!\G)</c>: it cannot match nothing at the
        /// position the search started from. Passing <c>startat</c> to <see cref="Regex.Match(string, int)"/>
        /// makes that position <c>\G</c>.
        /// </summary>
        public Regex Compiled { get; }

        /// <summary>The translated pattern as it is, for the <c>'emptymatch'</c> mode where an empty match counts.</summary>
        public Regex Plain { get; }

        /// <summary>The capturing groups in source order.</summary>
        public CaptureGroup[] Groups { get; }

        /// <summary>The .NET group behind a MATLAB group, for one match.</summary>
        public static Group GroupOf(Match match, CaptureGroup group) =>
            group.Name is null ? match.Groups[group.Ordinal] : match.Groups[group.Name];
    }

    /// <summary>What became of one capturing group in one match.</summary>
    private enum TokenState
    {
        /// <summary>The group matched text.</summary>
        Matched,

        /// <summary>The group was tried and matched nothing: an optional group that was skipped. It is an empty token.</summary>
        Unmatched,

        /// <summary>The group sits on a branch of an alternation the match did not take. It is no token at all.</summary>
        Dropped,
    }

    // --- registration -----------------------------------------------------------------------------

    private static void RegisterRegexBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Search(string name, RegexOptions options) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(
                name, (args, line, col) => RegexOutputs(name, args, options, dialect, wanted: 1, line, col)[0])
            {
                MultiOutput = (args, wanted, line, col) => RegexOutputs(name, args, options, dialect, wanted, line, col),

                // A string subject answers strings and a char subject answers char, so the builtin
                // has to see which it was handed.
                KeepsStringArguments = true,
            }));

        Search("regexp", RegexOptions.None);
        Search("regexpi", RegexOptions.IgnoreCase);

        // Without the interpreter a ${…} replacement cannot be evaluated; RegisterEvalBuiltins
        // re-declares regexprep with one as soon as there is an interpreter to hand it.
        env.Declare("regexprep", JgsValue.Function(new BuiltinFunction(
            "regexprep", (args, line, col) => ReplaceMatches(args, evaluate: null, line, col))));

        env.Declare("regexptranslate", JgsValue.Function(new BuiltinFunction("regexptranslate", (args, line, col) =>
        {
            Arity("regexptranslate", args, 2, line, col);
            string mode = Str("regexptranslate", args, 0, line, col);
            string text = Str("regexptranslate", args, 1, line, col);
            return JgsValue.Str(mode switch
            {
                "escape" => Regex.Escape(text),

                // A wildcard pattern is a file glob: * and ? mean what they mean in a shell, and
                // everything else is literal.
                "wildcard" => Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".").Replace("\\.", "\\."),
                "flexible" => text,
                _ => throw new JgsRuntimeException(line, col,
                    $"regexptranslate: '{mode}' is not 'escape', 'wildcard', or 'flexible'."),
            });
        })));
    }

    // --- option words -----------------------------------------------------------------------------

    /// <summary>
    /// Reads the option tail every regular-expression builtin shares. <paramref name="requested"/> is
    /// non-null for the builtins that also let a word name an output; passing null makes an output word
    /// an unknown option, which is the truth for <c>regexprep</c>.
    /// </summary>
    /// <remarks>
    /// The defaults are MATLAB's, not .NET's, and two of them differ: a dot spans a newline unless
    /// <c>'dotexceptnewline'</c> says otherwise, and a zero-length match is ignored unless
    /// <c>'emptymatch'</c> asks for it. Words are read in any case, may not repeat, and may not
    /// contradict each other — all three measured.
    /// </remarks>
    private static RegexMode ReadRegexWords(
        string name, IReadOnlyList<JgsValue> args, int from, RegexOptions options,
        List<string>? requested, int line, int col)
    {
        bool once = false;
        bool emptyMatch = false;
        bool preserveCase = false;
        bool forceCell = false;
        options |= RegexOptions.Singleline;
        var seen = new List<string>();

        for (int i = from; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: additional arguments must be char row vectors or scalar strings, but argument {i + 1} is a {args[i].TypeName}.");
            }

            string word = CanonicalRegexWord(TextOf(args[i]));
            if (seen.Contains(word))
            {
                throw new JgsRuntimeException(line, col, $"{name}: the '{word}' option may only be specified once.");
            }

            foreach ((string first, string second) in RegexConflicts)
            {
                string? other = word == first ? second : word == second ? first : null;
                if (other is not null && seen.Contains(other))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: the '{word}' option may not be used in conjunction with the '{other}' option.");
                }
            }

            seen.Add(word);
            switch (word)
            {
                case "once": once = true; break;
                case "ignorecase": options |= RegexOptions.IgnoreCase; break;
                case "matchcase": options &= ~RegexOptions.IgnoreCase; break;

                // 'preservecase' matches without regard to case and then puts the case back, so it
                // implies 'ignorecase' rather than competing with it.
                case "preservecase": options |= RegexOptions.IgnoreCase; preserveCase = true; break;
                case "emptymatch": emptyMatch = true; break;
                case "noemptymatch": emptyMatch = false; break;
                case "dotall": options |= RegexOptions.Singleline; break;
                case "dotexceptnewline": options &= ~RegexOptions.Singleline; break;
                case "stringanchors": options &= ~RegexOptions.Multiline; break;
                case "lineanchors": options |= RegexOptions.Multiline; break;
                case "literalspacing": options &= ~RegexOptions.IgnorePatternWhitespace; break;
                case "freespacing": options |= RegexOptions.IgnorePatternWhitespace; break;
                case "forceCellOutput": forceCell = true; break;
                case "warnings": break;
                default:
                    if (requested is not null && Array.IndexOf(RegexOutputWords, word) >= 0)
                    {
                        requested.Add(word);
                        break;
                    }

                    throw new JgsRuntimeException(line, col,
                        $"{name}: unknown option '{word}' (options: {RegexAlternatives(requested is not null)}).");
            }
        }

        return new RegexMode(options, once, emptyMatch, preserveCase, forceCell);
    }

    /// <summary>An option word in its documented spelling, whatever case it was written in.</summary>
    private static string CanonicalRegexWord(string word)
    {
        foreach (string known in RegexModeWords)
        {
            if (string.Equals(known, word, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        foreach (string known in RegexOutputWords)
        {
            if (string.Equals(known, word, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return word;
    }

    private static string RegexAlternatives(bool withOutputs)
    {
        var all = new List<string>();
        foreach (string word in RegexModeWords)
        {
            all.Add($"'{word}'");
        }

        if (withOutputs)
        {
            foreach (string word in RegexOutputWords)
            {
                all.Add($"'{word}'");
            }
        }

        return string.Join(", ", all);
    }

    // --- reading and compiling a pattern ----------------------------------------------------------

    /// <summary>Compiles a MATLAB pattern, turning a bad one into a script diagnostic rather than a crash.</summary>
    private static MatlabRegex CompileMatlab(string name, string pattern, RegexOptions options, int line, int col)
    {
        CaptureGroup[] groups = CaptureGroupsOf(pattern);
        // An inline (?m) turns line anchors on from inside the pattern, and $ has to follow it.
        bool lineAnchors = options.HasFlag(RegexOptions.Multiline)
            || Regex.IsMatch(pattern, @"\(\?[a-zA-Z]*m[a-zA-Z]*[):]");
        string translated = TranslatePattern(pattern, lineAnchors);
        try
        {
            return new MatlabRegex(
                pattern,
                new Regex("(?:" + translated + @")(?<!\G)", options, RegexBudget),
                new Regex(translated, options, RegexBudget),
                groups);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{pattern}' is not a valid regular expression — {ex.Message}");
        }
    }

    /// <summary>Compiles a .NET pattern as written, turning a bad one into a script diagnostic rather than a crash.</summary>
    private static Regex Compile(string name, string pattern, RegexOptions options, int line, int col)
    {
        try
        {
            return new Regex(pattern, options, RegexBudget);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{pattern}' is not a valid regular expression — {ex.Message}");
        }
    }

    /// <summary>
    /// Rewrites the MATLAB-only spellings in a pattern into .NET's. Everything else passes through
    /// untouched, so a pattern the two engines already agree on is compiled as written.
    /// </summary>
    private static string TranslatePattern(string pattern, bool lineAnchors)
    {
        var built = new StringBuilder(pattern.Length + 16);
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                if (!inClass && next == '<')
                {
                    built.Append(@"\b(?=\w)");
                    i++;
                    continue;
                }

                if (!inClass && next == '>')
                {
                    built.Append(@"\b(?<=\w)");
                    i++;
                    continue;
                }

                // MATLAB's \b is a backspace, as in printf; the word anchors are \< and \>.
                if (!inClass && next == 'b')
                {
                    built.Append(@"\x08");
                    i++;
                    continue;
                }

                if (next is 'o' or 'x')
                {
                    int consumed = CodePointEscape(pattern, i + 2, next == 'o' ? 8 : 16, out int codePoint);
                    if (consumed > 0)
                    {
                        built.Append(codePoint <= 0xFFFF
                            ? @"\u" + codePoint.ToString("X4", CultureInfo.InvariantCulture)
                            : Regex.Escape(char.ConvertFromUtf32(codePoint)));
                        i += 1 + consumed;
                        continue;
                    }
                }

                built.Append(c).Append(next);
                i++;
                continue;
            }

            if (inClass)
            {
                if (c == ']')
                {
                    inClass = false;
                }

                built.Append(c);
                continue;
            }

            if (c == '[')
            {
                inClass = true;
                built.Append(c);

                // A ^ right after the bracket negates, and a ] right after that is a literal.
                if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                {
                    built.Append('^');
                    i++;
                }

                if (i + 1 < pattern.Length && pattern[i + 1] == ']')
                {
                    built.Append(']');
                    i++;
                }

                continue;
            }

            // MATLAB's $ is the end of the text and nothing else; .NET's also matches before a final
            // newline. With 'lineanchors' both mean the end of a line and agree.
            if (c == '$' && !lineAnchors)
            {
                built.Append(@"\z");
                continue;
            }

            built.Append(c);
        }

        return built.ToString();
    }

    /// <summary>
    /// Reads the digits of a <c>\o</c> or <c>\x</c> escape starting at <paramref name="at"/>: either
    /// <c>{digits}</c> or a run of digits, which MATLAB takes greedily (<c>\x41b</c> is U+041B, not
    /// 'A' then 'b'). Answers how many characters were consumed, or 0 when there were no digits.
    /// </summary>
    private static int CodePointEscape(string pattern, int at, int radix, out int codePoint)
    {
        codePoint = 0;
        bool braced = at < pattern.Length && pattern[at] == '{';
        int i = braced ? at + 1 : at;
        int digits = 0;
        while (i < pattern.Length)
        {
            int digit = HexDigit(pattern[i]);
            if (digit < 0 || digit >= radix)
            {
                break;
            }

            codePoint = (codePoint * radix) + digit;
            digits++;
            i++;
            if (codePoint > 0x10FFFF)
            {
                return 0;
            }
        }

        if (digits == 0)
        {
            return 0;
        }

        if (braced)
        {
            if (i >= pattern.Length || pattern[i] != '}')
            {
                return 0;
            }

            i++;
        }

        return i - at;
    }

    /// <summary>
    /// Finds the capturing groups of a pattern in source order, and the alternation each one sits
    /// inside. Every <c>(</c> opens a node whose branches are separated by <c>|</c>; a group's path is
    /// the chain of (node, branch) it was opened under.
    /// </summary>
    private static CaptureGroup[] CaptureGroupsOf(string pattern)
    {
        var groups = new List<CaptureGroup>();
        var stack = new List<(int Node, int Branch)> { (0, 0) };
        int nextNode = 1;
        int unnamed = 0;
        bool inClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (inClass)
            {
                inClass = c != ']';
                continue;
            }

            switch (c)
            {
                case '[':
                    inClass = true;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                    {
                        i++;
                    }

                    if (i + 1 < pattern.Length && pattern[i + 1] == ']')
                    {
                        i++;
                    }

                    break;

                case '(':
                    string? name = null;
                    bool capturing = true;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                    {
                        capturing = false;
                        if (i + 2 < pattern.Length && pattern[i + 2] == '<'
                            && i + 3 < pattern.Length && pattern[i + 3] is not ('=' or '!'))
                        {
                            int close = pattern.IndexOf('>', i + 3);
                            if (close > 0)
                            {
                                name = pattern[(i + 3)..close];
                                capturing = true;
                            }
                        }
                    }

                    if (capturing)
                    {
                        groups.Add(new CaptureGroup(name, name is null ? ++unnamed : -1, stack.ToArray()));
                    }

                    stack.Add((nextNode++, 0));
                    break;

                case '|':
                    (int Node, int Branch) top = stack[^1];
                    stack[^1] = (top.Node, top.Branch + 1);
                    break;

                case ')':
                    if (stack.Count > 1)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    break;
            }
        }

        return groups.ToArray();
    }

    /// <summary>Whether two groups sit on different branches of one alternation.</summary>
    private static bool OnRivalBranches(CaptureGroup a, CaptureGroup b)
    {
        int shared = Math.Min(a.Path.Length, b.Path.Length);
        for (int k = 0; k < shared && a.Path[k].Node == b.Path[k].Node; k++)
        {
            if (a.Path[k].Branch != b.Path[k].Branch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What became of each group in one match, in MATLAB's group order.</summary>
    private static TokenState[] TokenStatesOf(Match match, MatlabRegex regex)
    {
        var states = new TokenState[regex.Groups.Length];
        for (int g = 0; g < states.Length; g++)
        {
            states[g] = MatlabRegex.GroupOf(match, regex.Groups[g]).Success ? TokenState.Matched : TokenState.Unmatched;
        }

        for (int g = 0; g < states.Length; g++)
        {
            if (states[g] != TokenState.Unmatched)
            {
                continue;
            }

            for (int h = 0; h < states.Length; h++)
            {
                if (states[h] == TokenState.Matched && OnRivalBranches(regex.Groups[g], regex.Groups[h]))
                {
                    states[g] = TokenState.Dropped;
                    break;
                }
            }
        }

        return states;
    }

    // --- matching ---------------------------------------------------------------------------------

    /// <summary>
    /// Every match MATLAB would report, in order. Without <paramref name="emptyMatch"/> a zero-length
    /// match is refused and the engine is made to look again for a longer one at the same place,
    /// which is what makes <c>'a*?'</c> find three <c>a</c>s in <c>'aaa'</c> rather than nothing.
    /// </summary>
    private static List<Match> MatchesOf(MatlabRegex regex, string text, bool emptyMatch, bool once)
    {
        var found = new List<Match>();
        if (emptyMatch)
        {
            // With empty matches allowed the two engines agree, so .NET's own walk is the answer.
            foreach (Match match in regex.Plain.Matches(text))
            {
                found.Add(match);
                if (once)
                {
                    break;
                }
            }

            return found;
        }

        for (int at = 0; at <= text.Length;)
        {
            Match match = regex.Compiled.Match(text, at);
            if (!match.Success)
            {
                break;
            }

            if (match.Length == 0)
            {
                // Found ahead of where the search began, where the lookbehind did not apply. Asking
                // again from there makes it apply, so the engine either lengthens the match or moves on.
                at = match.Index;
                continue;
            }

            found.Add(match);
            if (once)
            {
                break;
            }

            at = match.Index + match.Length;
        }

        return found;
    }

    // --- regexp ------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a regular expression and produces the outputs MATLAB's <c>regexp</c> would. Option words
    /// name the outputs and fix their order; with none given the order is MATLAB's default
    /// (start, end, tokenExtents, match, tokens, names, split).
    /// </summary>
    private static JgsValue[] RegexOutputs(
        string name, IReadOnlyList<JgsValue> args, RegexOptions options, JgsDialect dialect,
        int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name}: not enough input arguments.");
        }

        TextBundle subjects = RegexTextArgument(name, args[0], "STRING", line, col);
        TextBundle patterns = RegexTextArgument(name, args[1], "PATTERN", line, col);

        var requested = new List<string>();
        RegexMode mode = ReadRegexWords(name, args, 2, options, requested, line, col);
        int produced = Math.Max(wanted, 1);
        if (requested.Count > produced)
        {
            // An output word with no output to go to is an error in MATLAB, not a silent drop:
            // m = regexp(s, p, 'tokens', 'match') would otherwise hand back the tokens as the match.
            throw new JgsRuntimeException(line, col,
                $"{name}: not enough outputs specified for '{requested[produced]}' option.");
        }

        if (requested.Count == 0)
        {
            requested.AddRange(RegexOutputWords);
        }

        produced = Math.Min(produced, requested.Count);
        bool subjectIsOne = IsOnePiece(subjects);
        bool patternIsOne = IsOnePiece(patterns);
        TextKind kind = subjects.Kind;

        if (subjectIsOne && patternIsOne)
        {
            MatlabRegex regex = CompileMatlab(name, patterns.Texts[0], mode.Options, line, col);
            List<Match> matches = MatchesOf(regex, subjects.Texts[0], mode.EmptyMatch, mode.Once);
            var outputs = new JgsValue[produced];
            for (int i = 0; i < produced; i++)
            {
                outputs[i] = RegexOutput(requested[i], matches, regex, subjects.Texts[0], kind, dialect.IndexBase, mode.Once);
                if (mode.ForceCell)
                {
                    outputs[i] = JgsValue.Cell([outputs[i]]);
                }
            }

            return outputs;
        }

        // An empty container answers an empty cell of its own shape: there is nothing to ask.
        foreach (TextBundle container in new[] { subjects, patterns })
        {
            if (container.Texts.Length == 0)
            {
                var none = new JgsValue[produced];
                for (int o = 0; o < produced; o++)
                {
                    none[o] = JgsValue.Cell([]);
                    none[o].Reshape(container.Rows, container.Cols);
                }

                return none;
            }
        }

        // Several subjects, several patterns, or both: one answer per pair, in a cell shaped like the
        // container that had several. Both may have several only in equal numbers.
        int count = Math.Max(subjects.Texts.Length, patterns.Texts.Length);
        if ((!subjectIsOne && !patternIsOne && subjects.Texts.Length != patterns.Texts.Length
                && subjects.Texts.Length != 1 && patterns.Texts.Length != 1))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: multiple strings and patterns given must have the same quantity.");
        }

        TextBundle shape = !subjectIsOne && subjects.Texts.Length == count ? subjects : patterns;
        var compiled = new MatlabRegex[patterns.Texts.Length];
        for (int p = 0; p < compiled.Length; p++)
        {
            compiled[p] = CompileMatlab(name, patterns.Texts[p], mode.Options, line, col);
        }

        var perOutput = new JgsValue[produced][];
        for (int o = 0; o < produced; o++)
        {
            perOutput[o] = new JgsValue[count];
        }

        for (int i = 0; i < count; i++)
        {
            string text = subjects.Texts[subjects.Texts.Length == 1 ? 0 : i];
            MatlabRegex regex = compiled[compiled.Length == 1 ? 0 : i];
            List<Match> matches = MatchesOf(regex, text, mode.EmptyMatch, mode.Once);
            for (int o = 0; o < produced; o++)
            {
                perOutput[o][i] = RegexOutput(requested[o], matches, regex, text, kind, dialect.IndexBase, mode.Once);
            }
        }

        var gathered = new JgsValue[produced];
        for (int o = 0; o < produced; o++)
        {
            // A string subject asked for one match per element answers a string array of them,
            // measured: regexp(["a1" "b2"], "\d", "match", "once") is ["1" "2"], where every other
            // output — and every output of a cell subject — stays in a cell.
            if (kind == TextKind.String && mode.Once && requested[o] == "match")
            {
                gathered[o] = JgsValue.StringArray(
                    Array.ConvertAll(perOutput[o], static v => v.ElementAt(0)), shape.Rows, shape.Cols);
                continue;
            }

            JgsValue cell = JgsValue.Cell(perOutput[o]);
            cell.Reshape(shape.Rows, shape.Cols);
            gathered[o] = cell;
        }

        return gathered;
    }

    /// <summary>Whether a text argument is one piece of text rather than a container of them.</summary>
    private static bool IsOnePiece(TextBundle bundle) =>
        bundle.Kind == TextKind.Char || (bundle.Kind == TextKind.String && bundle.Texts.Length == 1);

    /// <summary>A text argument of <c>regexp</c>: a char row, a string array or a cell of char rows, and nothing else.</summary>
    private static TextBundle RegexTextArgument(string name, JgsValue value, string role, int line, int col)
    {
        if (value.IsCharMatrix || !TryReadText(value, out TextBundle bundle))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the '{role}' input must be either a char row vector, a cell array of char row vectors, or a string array.");
        }

        return bundle;
    }

    /// <summary>Builds one named <c>regexp</c> output from the match list.</summary>
    private static JgsValue RegexOutput(
        string kindOfOutput, List<Match> matches, MatlabRegex regex, string text, TextKind kind, int origin, bool once)
    {
        switch (kindOfOutput)
        {
            case "start":
                return once
                    ? (matches.Count == 0 ? JgsEmpty.Zero() : JgsValue.Number(matches[0].Index + origin))
                    : Positions(matches, static m => m.Index, origin);

            case "end":
                // The end index is the last character of the match, not one past it.
                return once
                    ? (matches.Count == 0 ? JgsEmpty.Zero() : JgsValue.Number(matches[0].Index + matches[0].Length - 1 + origin))
                    : Positions(matches, static m => m.Index + m.Length - 1, origin);

            case "match":
                if (once)
                {
                    return matches.Count == 0
                        ? (kind == TextKind.String ? JgsValue.StringScalar(MissingSentinel) : JgsValue.Str(string.Empty))
                        : TextPiece(matches[0].Value, kind);
                }

                return TextList(matches.ConvertAll(static m => m.Value), kind, emptyIsZeroByZero: true);

            case "tokens":
                if (once)
                {
                    return matches.Count == 0
                        ? TextList([], kind, emptyIsZeroByZero: true)
                        : TextList(TokensOf(matches[0], regex), kind, emptyIsZeroByZero: false);
                }

                return matches.Count == 0
                    ? TextList([], kind, emptyIsZeroByZero: true)
                    : JgsValue.Cell(matches.ConvertAll(m => TextList(TokensOf(m, regex), kind, emptyIsZeroByZero: false)).ToArray());

            case "tokenExtents":
                if (once)
                {
                    return matches.Count == 0 ? JgsEmpty.Zero() : TokenExtentsOf(matches[0], regex, origin);
                }

                return matches.Count == 0
                    ? EmptyCell()
                    : JgsValue.Cell(matches.ConvertAll(m => TokenExtentsOf(m, regex, origin)).ToArray());

            case "names":
                return NamesOf(matches, regex, kind, once);

            default:
                return TextList(SplitOn(once && matches.Count > 1 ? matches.GetRange(0, 1) : matches, text), kind, emptyIsZeroByZero: false);
        }
    }

    /// <summary>A row of positions, or the 0-by-0 empty when there are none.</summary>
    private static JgsValue Positions(List<Match> matches, Func<Match, int> of, int origin)
    {
        if (matches.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        var values = new double[matches.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = of(matches[i]) + origin;
        }

        return Numbers(values);
    }

    /// <summary>One piece of text in the subject's kind: a char row, or a string scalar.</summary>
    private static JgsValue TextPiece(string text, TextKind kind) =>
        kind == TextKind.String ? JgsValue.StringScalar(text) : JgsValue.Str(text);

    /// <summary>
    /// A list of pieces of text in the subject's kind: a string array for a string subject, a cell of
    /// char rows for anything else. An empty list is 1-by-0 — or 0-by-0 where MATLAB's is, which is
    /// the "no matches at all" answer.
    /// </summary>
    private static JgsValue TextList(IReadOnlyList<string> pieces, TextKind kind, bool emptyIsZeroByZero)
    {
        var boxed = new JgsValue[pieces.Count];
        for (int i = 0; i < boxed.Length; i++)
        {
            boxed[i] = JgsValue.Str(pieces[i]);
        }

        int rows = boxed.Length == 0 && emptyIsZeroByZero ? 0 : 1;
        if (kind == TextKind.String)
        {
            return JgsValue.StringArray(boxed, rows, boxed.Length);
        }

        JgsValue cell = JgsValue.Cell(boxed);
        cell.Reshape(rows, boxed.Length);
        return cell;
    }

    /// <summary>The 0-by-0 cell, which is what <c>regexp</c> answers for a per-match cell output when nothing matched.</summary>
    private static JgsValue EmptyCell()
    {
        JgsValue cell = JgsValue.Cell([]);
        cell.Reshape(0, 0);
        return cell;
    }

    /// <summary>The tokens of one match in MATLAB's order: a group on an untaken branch contributes none.</summary>
    private static List<string> TokensOf(Match match, MatlabRegex regex)
    {
        TokenState[] states = TokenStatesOf(match, regex);
        var tokens = new List<string>(states.Length);
        for (int g = 0; g < states.Length; g++)
        {
            if (states[g] != TokenState.Dropped)
            {
                tokens.Add(states[g] == TokenState.Matched ? MatlabRegex.GroupOf(match, regex.Groups[g]).Value : string.Empty);
            }
        }

        return tokens;
    }

    /// <summary>
    /// The extents of one match's tokens as a rows-by-2 matrix of [start end], or the 0-by-0 empty when
    /// there are no tokens. A token that matched nothing is reported just past the match, one before its
    /// own start — <c>[2 1]</c> for a skipped group after a one-character match at 1.
    /// </summary>
    private static JgsValue TokenExtentsOf(Match match, MatlabRegex regex, int origin)
    {
        TokenState[] states = TokenStatesOf(match, regex);
        var starts = new List<double>();
        var ends = new List<double>();
        for (int g = 0; g < states.Length; g++)
        {
            switch (states[g])
            {
                case TokenState.Matched:
                    Group group = MatlabRegex.GroupOf(match, regex.Groups[g]);
                    starts.Add(group.Index + origin);
                    ends.Add(group.Index + group.Length - 1 + origin);
                    break;

                case TokenState.Unmatched:
                    starts.Add(match.Index + match.Length + origin);
                    ends.Add(match.Index + match.Length + origin - 1);
                    break;
            }
        }

        if (starts.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        JgsValue matrix = Numbers([.. starts, .. ends]);
        matrix.Reshape(starts.Count, 2);
        return matrix;
    }

    /// <summary>
    /// The <c>'names'</c> output: a struct per match with a field per named group. No match at all is
    /// the 0-by-0 struct array that still knows its fields; matches of a pattern with no named group
    /// are one struct with no fields.
    /// </summary>
    private static JgsValue NamesOf(List<Match> matches, MatlabRegex regex, TextKind kind, bool once)
    {
        var names = new List<string>();
        foreach (CaptureGroup group in regex.Groups)
        {
            if (group.Name is not null && !names.Contains(group.Name))
            {
                names.Add(group.Name);
            }
        }

        if (matches.Count == 0)
        {
            return JgsValue.StructArray(new JgsStructArray([], names.ToArray()), 0, 0);
        }

        if (names.Count == 0)
        {
            return JgsValue.EmptyStruct();
        }

        int count = once ? 1 : matches.Count;
        var elements = new Dictionary<string, JgsValue>[count];
        for (int i = 0; i < count; i++)
        {
            TokenState[] states = TokenStatesOf(matches[i], regex);
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int g = 0; g < regex.Groups.Length; g++)
            {
                string? name = regex.Groups[g].Name;
                if (name is null || fields.ContainsKey(name))
                {
                    continue;
                }

                fields[name] = states[g] switch
                {
                    TokenState.Matched => TextPiece(MatlabRegex.GroupOf(matches[i], regex.Groups[g]).Value, kind),
                    TokenState.Unmatched => TextPiece(string.Empty, kind),
                    _ => JgsEmpty.Zero(),
                };
            }

            elements[i] = fields;
        }

        return count == 1 ? JgsValue.Struct(elements[0]) : JgsValue.StructArray(elements);
    }

    /// <summary>The text between the matches, with the text before the first and after the last.</summary>
    private static List<string> SplitOn(List<Match> matches, string text)
    {
        var pieces = new List<string>(matches.Count + 1);
        int at = 0;
        foreach (Match match in matches)
        {
            pieces.Add(text[at..match.Index]);
            at = match.Index + match.Length;
        }

        pieces.Add(text[at..]);
        return pieces;
    }

    // --- regexprep --------------------------------------------------------------------------------

    /// <summary>
    /// <c>regexprep(str, expression, replacement, options…)</c>. The replacement is built match by match
    /// rather than handed to <see cref="Regex.Replace(string, string)"/> so that <c>'once'</c>, a
    /// match number, <c>'preservecase'</c> and MATLAB's own replacement grammar each have somewhere to act.
    /// </summary>
    /// <remarks><paramref name="evaluate"/> runs a <c>${…}</c> expression, or is null when there is no interpreter to run one.</remarks>
    private static JgsValue ReplaceMatches(
        IReadOnlyList<JgsValue> args, Func<string, int, int, JgsValue>? evaluate, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "regexprep: not enough input arguments.");
        }

        string text = Str("regexprep", args, 0, line, col);
        string[] patterns = PatternsOf("regexprep", args, 1, line, col);
        string[] replacements = ReplacementsFor("regexprep", args, 2, patterns, line, col);

        // A number among the options picks one match by its ordinal: regexprep('aaa', 'a', 'b', 2)
        // is 'aba'. It is read out here so that the word reader sees only words.
        int only = 0;
        var words = new List<JgsValue> { args[0], args[1], args[2] };
        for (int i = 3; i < args.Count; i++)
        {
            bool numeric = args[i].Type == JgsType.Number
                || (args[i].Type == JgsType.Array && !args[i].IsStringArray && !args[i].IsCharMatrix
                    && args[i].ArrayLength == 1 && args[i].ElementAt(0).Type == JgsType.Number);
            if (numeric)
            {
                double n = args[i].Type == JgsType.Number ? args[i].AsNumber : args[i].ElementAt(0).AsNumber;
                if (n < 1 || n != Math.Floor(n))
                {
                    throw new JgsRuntimeException(line, col, "regexprep: a match number must be a positive integer.");
                }

                only = (int)n;
                continue;
            }

            words.Add(args[i]);
        }

        RegexMode mode = ReadRegexWords("regexprep", words, 3, RegexOptions.None, requested: null, line, col);

        // Several expressions are applied one after another, each to what the one before it left —
        // which is MATLAB's rule and not `replace`'s. The two genuinely differ, and the difference
        // is visible in one line: regexprep("a", ["a";"b"], ["b";"c"]) is "c", because the b the
        // first expression wrote is found by the second, where replace of the same lists is "b".
        var built = new StringBuilder();
        for (int p = 0; p < patterns.Length; p++)
        {
            MatlabRegex regex = CompileMatlab("regexprep", patterns[p], mode.Options, line, col);
            List<Match> matches = MatchesOf(regex, text, mode.EmptyMatch, mode.Once);
            if (only > 0)
            {
                matches = only <= matches.Count ? [matches[only - 1]] : [];
            }

            built.Clear();
            int at = 0;
            foreach (Match match in matches)
            {
                built.Append(text, at, match.Index - at);
                string produced = ExpandReplacement(match, regex, replacements[p], evaluate, line, col);
                built.Append(mode.PreserveCase ? InTheCaseOf(match.Value, produced) : produced);
                at = match.Index + match.Length;
            }

            built.Append(text, at, text.Length - at);
            text = built.ToString();
        }

        return JgsValue.Str(text);
    }

    /// <summary>
    /// The text one match is replaced by. MATLAB's replacement grammar: <c>$0</c> is the match,
    /// <c>$N</c> (one or two digits, as many as name a token) is a token, <c>$&lt;name&gt;</c> is a
    /// named token, <c>${expr}</c> is MATLAB code run with the tokens spliced in as quoted text, and a
    /// backslash escapes the next character the way <c>sprintf</c> does — <c>\n</c> is a newline,
    /// <c>\\</c> one backslash, <c>\$</c> a dollar, <c>\x41</c> a code point, and any other character
    /// itself. A <c>$</c> that starts none of those is a literal dollar.
    /// </summary>
    private static string ExpandReplacement(
        Match match, MatlabRegex regex, string replacement, Func<string, int, int, JgsValue>? evaluate, int line, int col)
    {
        TokenState[] states = TokenStatesOf(match, regex);
        var built = new StringBuilder(replacement.Length + match.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];
            if (c == '\\')
            {
                i += AppendEscaped(replacement, i + 1, built);
                continue;
            }

            if (c != '$')
            {
                built.Append(c);
                continue;
            }

            if (i + 1 < replacement.Length && replacement[i + 1] == '{')
            {
                int close = MatchingBrace(replacement, i + 1);
                if (close > 0)
                {
                    string code = replacement[(i + 2)..close];
                    built.Append(EvaluateDynamic(code, match, regex, states, evaluate, line, col));
                    i = close;
                    continue;
                }
            }

            int consumed = AppendToken(replacement, i + 1, match, regex, states, built);
            if (consumed == 0)
            {
                built.Append('$');
            }
            else
            {
                i += consumed;
            }
        }

        return built.ToString();
    }

    /// <summary>
    /// Decodes the escape whose backslash sits just before <paramref name="at"/>, appends what it
    /// stands for, and answers how many characters after the backslash it used (0 for a backslash
    /// that ends the text, which is kept as itself).
    /// </summary>
    private static int AppendEscaped(string text, int at, StringBuilder built)
    {
        if (at >= text.Length)
        {
            built.Append('\\');
            return 0;
        }

        char c = text[at];
        switch (c)
        {
            case 'a': built.Append('\a'); return 1;
            case 'b': built.Append('\b'); return 1;
            case 'f': built.Append('\f'); return 1;
            case 'n': built.Append('\n'); return 1;
            case 'r': built.Append('\r'); return 1;
            case 't': built.Append('\t'); return 1;
            case 'v': built.Append('\v'); return 1;
            case 'o':
            case 'x':
                int consumed = CodePointEscape(text, at + 1, c == 'o' ? 8 : 16, out int codePoint);
                if (consumed > 0)
                {
                    built.Append(char.ConvertFromUtf32(codePoint));
                    return 1 + consumed;
                }

                built.Append(c);
                return 1;
            default:
                if (c is >= '0' and <= '7')
                {
                    // Octal digits straight after the backslash, up to three: \101 is 'A'.
                    int value = 0;
                    int digits = 0;
                    while (at + digits < text.Length && digits < 3 && text[at + digits] is >= '0' and <= '7')
                    {
                        value = (value * 8) + (text[at + digits] - '0');
                        digits++;
                    }

                    built.Append((char)value);
                    return digits;
                }

                built.Append(c);
                return 1;
        }
    }

    /// <summary>
    /// Appends the token a <c>$</c> at <paramref name="at"/> - 1 refers to, and answers how many
    /// characters after the dollar it used — 0 when the dollar names no token and is a literal.
    /// </summary>
    private static int AppendToken(
        string text, int at, Match match, MatlabRegex regex, TokenState[] states, StringBuilder built)
    {
        if (at >= text.Length)
        {
            return 0;
        }

        if (text[at] == '<')
        {
            int close = text.IndexOf('>', at + 1);
            if (close > at + 1)
            {
                string name = text[(at + 1)..close];
                for (int g = 0; g < regex.Groups.Length; g++)
                {
                    if (regex.Groups[g].Name == name)
                    {
                        if (states[g] == TokenState.Matched)
                        {
                            built.Append(MatlabRegex.GroupOf(match, regex.Groups[g]).Value);
                        }

                        return close - at + 1;
                    }
                }
            }

            return 0;
        }

        if (!char.IsAsciiDigit(text[at]))
        {
            return 0;
        }

        // Two digits when they name a token that exists, else one: $10 with one group is $1 then '0',
        // and $01 is always the whole match then '1'.
        int digits = 1;
        if (text[at] != '0' && at + 1 < text.Length && char.IsAsciiDigit(text[at + 1]))
        {
            int two = ((text[at] - '0') * 10) + (text[at + 1] - '0');
            if (two >= 1 && two <= TokenCount(states))
            {
                digits = 2;
            }
        }

        int number = int.Parse(text.AsSpan(at, digits), CultureInfo.InvariantCulture);
        if (number == 0)
        {
            built.Append(match.Value);
            return digits;
        }

        if (number > TokenCount(states))
        {
            return 0;
        }

        built.Append(TokenNumbered(number, match, regex, states));
        return digits;
    }

    /// <summary>How many tokens a match has, MATLAB's way: the groups on untaken branches are not counted.</summary>
    private static int TokenCount(TokenState[] states)
    {
        int count = 0;
        foreach (TokenState state in states)
        {
            if (state != TokenState.Dropped)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The text of the <paramref name="number"/>th token (1-based, MATLAB's numbering).</summary>
    private static string TokenNumbered(int number, Match match, MatlabRegex regex, TokenState[] states)
    {
        int seen = 0;
        for (int g = 0; g < states.Length; g++)
        {
            if (states[g] == TokenState.Dropped)
            {
                continue;
            }

            if (++seen == number)
            {
                return states[g] == TokenState.Matched ? MatlabRegex.GroupOf(match, regex.Groups[g]).Value : string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>The index of the <c>}</c> closing the <c>{</c> at <paramref name="open"/>, counting nested braces, or -1.</summary>
    private static int MatchingBrace(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Runs a <c>${…}</c> replacement: every token reference in the code is replaced by the token's
    /// text as a quoted char literal, the code is evaluated, and its answer must be text.
    /// </summary>
    private static string EvaluateDynamic(
        string code, Match match, MatlabRegex regex, TokenState[] states,
        Func<string, int, int, JgsValue>? evaluate, int line, int col)
    {
        if (evaluate is null)
        {
            throw new JgsRuntimeException(line, col,
                "regexprep: a ${…} replacement needs the interpreter, which this environment does not have.");
        }

        var spliced = new StringBuilder(code.Length + 16);
        for (int i = 0; i < code.Length; i++)
        {
            if (code[i] != '$')
            {
                spliced.Append(code[i]);
                continue;
            }

            var token = new StringBuilder();
            int consumed = AppendToken(code, i + 1, match, regex, states, token);
            if (consumed == 0)
            {
                spliced.Append('$');
                continue;
            }

            spliced.Append('\'').Append(token.ToString().Replace("'", "''", StringComparison.Ordinal)).Append('\'');
            i += consumed;
        }

        JgsValue answer;
        try
        {
            answer = evaluate(spliced.ToString(), line, col);
        }
        catch (JgsException ex)
        {
            throw new JgsRuntimeException(line, col, $"regexprep: evaluation of '{code}' failed: {ex.Message}");
        }

        if (answer.Type == JgsType.String)
        {
            return answer.AsString;
        }

        if (IsStringScalar(answer))
        {
            return TextOf(answer);
        }

        throw new JgsRuntimeException(line, col,
            $"regexprep: evaluation of '{code}' did not produce a char vector or scalar string.");
    }

    /// <summary>
    /// A replacement wearing the case of the text it replaced: SHOUTED text keeps the replacement
    /// shouted, a Capitalized word capitalizes it, and anything else leaves the replacement as it was
    /// written — measured: 'ABc' matched by 'abc' is replaced by 'xyz' as written.
    /// </summary>
    private static string InTheCaseOf(string matched, string replacement)
    {
        bool letters = false;
        bool upper = true;
        bool restLower = true;
        bool first = true;
        bool firstUpper = false;
        foreach (char character in matched)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            letters = true;
            upper &= char.IsUpper(character);
            if (first)
            {
                firstUpper = char.IsUpper(character);
                first = false;
            }
            else
            {
                restLower &= char.IsLower(character);
            }
        }

        if (!letters || replacement.Length == 0)
        {
            return replacement;
        }

        if (upper)
        {
            return replacement.ToUpperInvariant();
        }

        return firstUpper && restLower
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }
}
