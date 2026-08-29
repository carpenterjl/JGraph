using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The option surfaces of the text, cell and formatting builtins (M52 wave D): <c>strsplit</c> and
/// <c>strjoin</c>, the option words every regular-expression builtin reads, <c>cellfun</c> in all
/// the shapes MATLAB documents, and <c>num2str</c> on something bigger than one number.
/// </summary>
/// <remarks>
/// These five had the same shortfall as the set operations wave C fixed, one layer up: each answered
/// the first sentence of its documentation and quietly ignored — or refused — the rest.
/// <c>strsplit</c> took one delimiter and no options; <c>strjoin</c> could not be given a different
/// separator per gap; <c>regexprep</c> understood exactly one option word out of twelve and let the
/// other eleven fall through unnoticed; <c>cellfun</c> took one cell, one output and no error
/// handler; <c>num2str</c> read only the first element of an array.
///
/// Three of the changes here alter an answer rather than adding one, because the old answer was
/// wrong rather than merely absent: a regular expression's dot now spans a newline (MATLAB's
/// default is <c>'dotall'</c>, .NET's is not), a zero-length match is no longer replaced (MATLAB's
/// default is <c>'noemptymatch'</c>), and splitting on whitespace now keeps the empty pieces a
/// leading or trailing delimiter produces, which is what makes <c>strsplit</c> the same function as
/// <c>regexp(…, 'split')</c>.
/// </remarks>
internal static partial class JgsBuiltins
{
    // --- Splitting and joining --------------------------------------------------------------------

    /// <summary>MATLAB's default delimiter set: every whitespace character <c>sprintf</c> can name.</summary>
    private static readonly string[] WhitespaceDelimiters = [" ", "\f", "\n", "\r", "\t", "\v"];

    private static readonly OptionSpec SplitOptions = new(
        "strsplit", Flags: [], Names: ["CollapseDelimiters", "DelimiterType"], StringPositionals: 2);

    /// <summary>
    /// <c>[C, matches] = strsplit(str, delimiter, …)</c>. The delimiter is positional but optional and
    /// is usually text, so it is claimed before parsing: a second argument counts as the delimiter
    /// unless it spells one of the option names, which is the same rule MATLAB applies.
    /// </summary>
    private static JgsValue[] SplitText(IReadOnlyList<JgsValue> args, int outputs, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "strsplit needs text to split.");
        }

        bool given = args.Count > 1 && !NamesSplitOption(args[1]);
        ParsedArgs parsed = SplitOptions.Parse(args, given ? 2 : 1, line, col);
        string text = Str("strsplit", parsed.Positional, 0, line, col);
        bool collapse = parsed.Flag("CollapseDelimiters", true);
        bool simple = parsed.Word("DelimiterType", "Simple", "Simple", "RegularExpression") == "Simple";
        string[] delimiters = given
            ? DelimitersOf(parsed.Positional[1], line, col)
            : WhitespaceDelimiters;

        var alternatives = new List<string>();
        foreach (string delimiter in delimiters)
        {
            if (delimiter.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "strsplit: an empty delimiter has nothing to split on.");
            }

            // A simple delimiter is literal text, but sprintf's escapes still name the characters a
            // script cannot type — '\t' is a tab here, exactly as it is in MATLAB.
            alternatives.Add(simple ? Regex.Escape(TranslateEscapes(delimiter)) : delimiter);
        }

        var pieces = new List<JgsValue>();
        var matched = new List<JgsValue>();
        string pattern = "(?:" + string.Join("|", alternatives) + ")" + (collapse ? "+" : string.Empty);
        int at = 0;
        foreach (Match match in Compile("strsplit", pattern, RegexOptions.None, line, col).Matches(text))
        {
            if (match.Length == 0)
            {
                continue; // a delimiter that matches nothing would split between every character
            }

            pieces.Add(JgsValue.Str(text[at..match.Index]));
            matched.Add(JgsValue.Str(match.Value));
            at = match.Index + match.Length;
        }

        pieces.Add(JgsValue.Str(text[at..]));
        return Outputs(outputs, JgsValue.Cell(pieces.ToArray()), JgsValue.Cell(matched.ToArray()));
    }

    private static bool NamesSplitOption(JgsValue value) =>
        value.Type == JgsType.String
        && (string.Equals(value.AsString, "CollapseDelimiters", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.AsString, "DelimiterType", StringComparison.OrdinalIgnoreCase));

    /// <summary>One delimiter or a cell of them — MATLAB accepts either wherever a delimiter goes.</summary>
    private static string[] DelimitersOf(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return [value.AsString];
        }

        if (value.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col,
                $"strsplit: the delimiter is text or a cell of text, but got a {value.TypeName}.");
        }

        JgsValue[] elements = value.AsCell;
        var delimiters = new string[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            delimiters[i] = TextIn("strsplit", elements[i], line, col);
        }

        return delimiters;
    }

    /// <summary>
    /// <c>strjoin(C, delimiter)</c>, where the delimiter is one separator or a cell holding a different
    /// one for every gap — the form that writes <c>'a, b and c'</c> in a single call.
    /// </summary>
    private static JgsValue JoinText(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("strjoin", args, 1, 2, line, col);
        var parts = new List<string>();
        foreach (JgsValue element in Elements("strjoin", args[0], line, col))
        {
            parts.Add(element.Type == JgsType.String ? element.AsString : element.Display());
        }

        if (args.Count == 1)
        {
            return JgsValue.Str(string.Join(" ", parts));
        }

        if (args[1].Type != JgsType.Cell)
        {
            return JgsValue.Str(string.Join(TranslateEscapes(Str("strjoin", args, 1, line, col)), parts));
        }

        JgsValue[] gaps = args[1].AsCell;
        int wanted = Math.Max(parts.Count - 1, 0);
        if (gaps.Length != wanted)
        {
            throw new JgsRuntimeException(line, col,
                $"strjoin: joining {parts.Count} piece(s) takes {wanted} delimiter(s), but got {gaps.Length}.");
        }

        var joined = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                joined.Append(TranslateEscapes(TextIn("strjoin", gaps[i - 1], line, col)));
            }

            joined.Append(parts[i]);
        }

        return JgsValue.Str(joined.ToString());
    }

    /// <summary>An element that has to be text, named in the diagnostic when it is not.</summary>
    private static string TextIn(string name, JgsValue value, int line, int col) =>
        value.Type == JgsType.String
            ? value.AsString
            : throw new JgsRuntimeException(line, col, $"{name} expects text, but got a {value.TypeName}.");

    /// <summary>
    /// The escape sequences MATLAB reads inside a simple delimiter. Anything else keeps its backslash,
    /// so a Windows path used as a delimiter survives intact.
    /// </summary>
    private static string TranslateEscapes(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        var built = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                built.Append(text[i]);
                continue;
            }

            char escape = text[i + 1];
            char? plain = escape switch
            {
                '\\' => '\\',
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => null,
            };

            if (plain is { } character)
            {
                built.Append(character);
                i++;
            }
            else
            {
                built.Append(text[i]);
            }
        }

        return built.ToString();
    }

    // --- Regular-expression option words ----------------------------------------------------------

    /// <summary>What MATLAB's regular-expression option words select, once they have all been read.</summary>
    /// <param name="Options">The .NET flags the words add up to.</param>
    /// <param name="Once">Whether only the first match counts.</param>
    /// <param name="EmptyMatch">Whether a zero-length match is a match.</param>
    /// <param name="PreserveCase">Whether a replacement takes the case of the text it replaces.</param>
    private readonly record struct RegexMode(RegexOptions Options, bool Once, bool EmptyMatch, bool PreserveCase);

    /// <summary>The words that name an output of <c>regexp</c>, in MATLAB's own default order.</summary>
    private static readonly string[] RegexOutputWords =
        ["start", "end", "tokenExtents", "match", "tokens", "names", "split"];

    /// <summary>The words that change how the expression is matched, shared by every regex builtin.</summary>
    private static readonly string[] RegexModeWords =
    [
        "once", "matchcase", "ignorecase", "preservecase", "noemptymatch", "emptymatch",
        "dotall", "dotexceptnewline", "stringanchors", "lineanchors", "literalspacing", "freespacing",
    ];

    /// <summary>
    /// Reads the option tail every regular-expression builtin shares. <paramref name="requested"/> is
    /// non-null for the builtins that also let a word name an output; passing null makes an output word
    /// an unknown option, which is the truth for <c>regexprep</c>.
    /// </summary>
    /// <remarks>
    /// The defaults are MATLAB's, not .NET's, and two of them differ: a dot spans a newline unless
    /// <c>'dotexceptnewline'</c> says otherwise, and a zero-length match is ignored unless
    /// <c>'emptymatch'</c> asks for it. Both used to follow .NET by omission.
    /// </remarks>
    private static RegexMode ReadRegexWords(
        string name, IReadOnlyList<JgsValue> args, int from, RegexOptions options,
        List<string>? requested, int line, int col)
    {
        bool once = false;
        bool emptyMatch = false;
        bool preserveCase = false;
        options |= RegexOptions.Singleline;

        for (int i = from; i < args.Count; i++)
        {
            string word = Str(name, args, i, line, col);
            switch (word)
            {
                case "once": once = true; break;
                case "ignorecase": options |= RegexOptions.IgnoreCase; preserveCase = false; break;
                case "matchcase": options &= ~RegexOptions.IgnoreCase; preserveCase = false; break;

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

        return new RegexMode(options, once, emptyMatch, preserveCase);
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

    /// <summary>
    /// <c>regexprep(str, expression, replacement, options…)</c>. The replacement is built match by match
    /// rather than handed to <see cref="Regex.Replace(string, string)"/> so that <c>'once'</c>,
    /// <c>'noemptymatch'</c> and <c>'preservecase'</c> each have somewhere to act.
    /// </summary>
    private static JgsValue ReplaceMatches(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col,
                $"regexprep expects at least 3 argument(s), but got {args.Count}.");
        }

        string text = Str("regexprep", args, 0, line, col);
        string pattern = Str("regexprep", args, 1, line, col);
        string replacement = Str("regexprep", args, 2, line, col);
        RegexMode mode = ReadRegexWords("regexprep", args, 3, RegexOptions.None, requested: null, line, col);

        var built = new StringBuilder();
        int at = 0;
        foreach (Match match in Compile("regexprep", pattern, mode.Options, line, col).Matches(text))
        {
            if (match.Length == 0 && !mode.EmptyMatch)
            {
                continue;
            }

            built.Append(text, at, match.Index - at);

            // MATLAB and .NET spell a capture reference the same way ($1), so the replacement text
            // passes straight through the substitution.
            string produced = match.Result(replacement);
            built.Append(mode.PreserveCase ? InTheCaseOf(match.Value, produced) : produced);
            at = match.Index + match.Length;
            if (mode.Once)
            {
                break;
            }
        }

        built.Append(text, at, text.Length - at);
        return JgsValue.Str(built.ToString());
    }

    /// <summary>
    /// A replacement wearing the case of the text it replaced: SHOUTED text stays shouted, a Capitalized
    /// word stays capitalized, and anything else is left as the replacement was written.
    /// </summary>
    private static string InTheCaseOf(string matched, string replacement)
    {
        bool letters = false;
        bool upper = true;
        bool lower = true;
        foreach (char character in matched)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            letters = true;
            upper &= char.IsUpper(character);
            lower &= char.IsLower(character);
        }

        if (!letters || replacement.Length == 0)
        {
            return replacement;
        }

        if (upper)
        {
            return replacement.ToUpperInvariant();
        }

        if (lower)
        {
            return replacement.ToLowerInvariant();
        }

        return char.IsUpper(matched[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }

    // --- cellfun ----------------------------------------------------------------------------------

    private static readonly OptionSpec CellOptions = new(
        "cellfun", Flags: [], Names: ["UniformOutput", "ErrorHandler"]);

    /// <summary>
    /// The questions <c>cellfun</c> answers when its first argument is a name rather than a handle.
    /// They predate function handles and survive in scripts because they are faster than a closure;
    /// each one maps onto a builtin JGraph already has.
    /// </summary>
    private static readonly Dictionary<string, string> CellQuestions = new(StringComparer.Ordinal)
    {
        ["isempty"] = "isempty",
        ["islogical"] = "islogical",
        ["isreal"] = "isreal",
        ["length"] = "length",
        ["ndims"] = "ndims",
        ["prodofsize"] = "numel",
        ["size"] = "size",
        ["isclass"] = "class",
    };

    /// <summary>
    /// <c>cellfun</c> over any number of cells, producing any number of outputs. The cells are taken
    /// from the front until something that is not a cell appears, which is what lets the option tail,
    /// the legacy <c>'size'</c> dimension and the <c>'isclass'</c> class name all share one slot rule.
    /// </summary>
    private static JgsValue[] ApplyOverCells(
        JgsEnvironment env, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "cellfun(f, c) applies a function handle — or the name of a question — to each cell.");
        }

        string? question = null;
        if (args[0].Type == JgsType.String)
        {
            if (!CellQuestions.ContainsKey(args[0].AsString))
            {
                throw new JgsRuntimeException(line, col,
                    $"cellfun: '{args[0].AsString}' is not one of the names cellfun answers " +
                    $"(names: {string.Join(", ", CellQuestions.Keys.Select(static k => $"'{k}'"))}). " +
                    "Pass a function handle to call something else.");
            }

            question = args[0].AsString;
        }
        else if (args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col,
                $"cellfun expects a function handle or a name, but got a {args[0].TypeName}.");
        }

        var cells = new List<JgsValue>();
        int i = 1;
        while (i < args.Count && args[i].Type == JgsType.Cell)
        {
            cells.Add(args[i]);
            i++;
        }

        if (cells.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"cellfun expects a cell array, but got a {args[1].TypeName}.");
        }

        JgsValue? detail = null;
        if (question is "size" or "isclass")
        {
            if (i >= args.Count)
            {
                throw new JgsRuntimeException(line, col, question == "size"
                    ? "cellfun('size', c, dim) needs the dimension to measure."
                    : "cellfun('isclass', c, 'name') needs the class name to test.");
            }

            detail = args[i];
            i++;
        }

        var tail = new List<JgsValue>();
        for (int t = i; t < args.Count; t++)
        {
            tail.Add(args[t]);
        }

        ParsedArgs parsed = CellOptions.Parse(tail, 0, line, col);
        bool uniform = parsed.Flag("UniformOutput", true);
        JgsValue? handler = parsed.Named("ErrorHandler");
        if (handler is { Type: not JgsType.Function })
        {
            throw new JgsRuntimeException(line, col, "cellfun: 'ErrorHandler' takes a function handle.");
        }

        int count = cells[0].AsCell.Length;
        foreach (JgsValue cell in cells)
        {
            if (cell.AsCell.Length != count)
            {
                throw new JgsRuntimeException(line, col,
                    "cellfun: every cell must hold the same number of elements.");
            }
        }

        int produced = Math.Max(wanted, 1);
        var collected = new JgsValue[produced][];
        for (int o = 0; o < produced; o++)
        {
            collected[o] = new JgsValue[count];
        }

        for (int k = 0; k < count; k++)
        {
            var inputs = new JgsValue[cells.Count];
            for (int c = 0; c < cells.Count; c++)
            {
                inputs[c] = cells[c].AsCell[k];
            }

            JgsValue[] answers;
            if (question is not null)
            {
                answers = [AskOfCell(env, question, inputs[0], detail, line, col)];
            }
            else
            {
                try
                {
                    answers = CallForOutputs(args[0].AsCallable, inputs, produced, line, col);
                }
                catch (JgsRuntimeException failure) when (handler is { } catcher)
                {
                    // MATLAB hands the handler a record of what went wrong followed by the same
                    // inputs, so a handler can answer for the element rather than only report it.
                    var handed = new JgsValue[inputs.Length + 1];
                    handed[0] = FailureRecord(failure, k);
                    inputs.CopyTo(handed, 1);
                    answers = CallForOutputs(catcher.AsCallable, handed, produced, line, col);
                }
            }

            if (answers.Length < produced)
            {
                throw new JgsRuntimeException(line, col,
                    $"cellfun: element {k + 1} produced {answers.Length} output(s), but {produced} were asked for.");
            }

            for (int o = 0; o < produced; o++)
            {
                collected[o][k] = answers[o];
            }
        }

        int rows = JgsMatrix.RowCount(cells[0]);
        int columns = JgsMatrix.ColCount(cells[0]);
        var outputs = new JgsValue[produced];
        for (int o = 0; o < produced; o++)
        {
            outputs[o] = CollectResults("cellfun", collected[o], uniform, rows, columns, line, col);
        }

        return outputs;
    }

    /// <summary>Calls something for several outputs when it can produce them, and one when it cannot.</summary>
    private static JgsValue[] CallForOutputs(
        IJgsCallable callable, IReadOnlyList<JgsValue> inputs, int wanted, int line, int col) =>
        wanted > 1 && callable is IJgsMultiCallable many
            ? many.CallMultiple(inputs, wanted, line, col)
            : [callable.Call(inputs, line, col)];

    /// <summary>
    /// The struct MATLAB hands an <c>'ErrorHandler'</c>. The identifier is empty because JGraph's
    /// errors carry a message and a place, not an identifier — <c>error('id:sub', …)</c> reads the
    /// identifier to tell the two call forms apart and then drops it.
    /// </summary>
    private static JgsValue FailureRecord(JgsRuntimeException failure, int index) =>
        JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["identifier"] = JgsValue.Str(string.Empty),
            ["message"] = JgsValue.Str(failure.Message),
            ["index"] = JgsValue.Number(index + 1),
        });

    /// <summary>One legacy <c>cellfun</c> question, answered by the builtin that already answers it.</summary>
    private static JgsValue AskOfCell(
        JgsEnvironment env, string question, JgsValue item, JgsValue? detail, int line, int col)
    {
        string builtin = CellQuestions[question];
        if (question == "isclass")
        {
            string wanted = TextIn("cellfun", detail!, line, col);
            JgsValue actual = CallBuiltin(env, "cellfun", builtin, [item], line, col);
            return JgsValue.Bool(string.Equals(actual.AsString, wanted, StringComparison.Ordinal));
        }

        return question == "size"
            ? CallBuiltin(env, "cellfun", builtin, [item, detail!], line, col)
            : CallBuiltin(env, "cellfun", builtin, [item], line, col);
    }

    /// <summary>Calls a registered builtin by name, so a question is answered in exactly one place.</summary>
    private static JgsValue CallBuiltin(
        JgsEnvironment env, string caller, string name, JgsValue[] args, int line, int col)
    {
        if (!env.TryGet(name, out JgsValue found) || found.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"{caller}: '{name}' is not available here.");
        }

        return found.AsCallable.Call(args, line, col);
    }

    /// <summary>
    /// Per-element results gathered back into one value, keeping the shape they were drawn from.
    /// A uniform result has to be scalar, except that single characters join into a char row —
    /// <c>cellfun(@(s) s(1), names)</c> is asking for a word, not for an error.
    /// </summary>
    private static JgsValue CollectResults(
        string name, JgsValue[] values, bool uniform, int rows, int columns, int line, int col)
    {
        if (!uniform)
        {
            JgsValue cell = JgsValue.Cell(values);
            if (rows > 1 || values.Length == 0)
            {
                cell.Reshape(rows, columns);
            }

            return cell;
        }

        // Nothing applied to nothing still keeps the shape it was drawn from (M96b), so cellfun over
        // {} is the 0-by-0 empty rather than a bare 1-by-0 row.
        if (values.Length == 0)
        {
            return JgsEmpty.Shaped(rows, columns);
        }

        bool characters = true;
        foreach (JgsValue value in values)
        {
            characters &= value.Type == JgsType.String && value.AsString.Length == 1;
        }

        if (characters)
        {
            var word = new StringBuilder(values.Length);
            foreach (JgsValue value in values)
            {
                word.Append(value.AsString);
            }

            return JgsValue.Str(word.ToString());
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Type is not (JgsType.Number or JgsType.Bool))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: element {i + 1} produced a {values[i].TypeName}. " +
                    "Add 'UniformOutput', false to collect a cell.");
            }
        }

        return JgsMatrix.FromElements(values, rows, columns);
    }

    // --- num2str ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>num2str</c> over a whole array. MATLAB lays the elements out in right-aligned columns and
    /// answers a char matrix, one row per row of the input; JGraph has no char matrix, so one row is a
    /// string and several are a cell of strings — the same rule <c>dec2bin</c> already follows.
    /// </summary>
    private static JgsValue NumberText(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("num2str", args, 1, 2, line, col);
        JgsValue subject = args[0];
        if (subject.Type == JgsType.String)
        {
            return subject; // MATLAB hands char straight back rather than describing it
        }

        if (subject.Type == JgsType.Complex || HasComplexElements(subject))
        {
            // A complex number's two halves have their own widths; printing it the way the console
            // prints it is the honest answer until there is a column rule that covers both.
            return JgsValue.Str(subject.Display());
        }

        // Nothing to describe, so the description is the empty char row — never a cell, which is
        // what a 0-by-0 subject fell into once the row list below came back with no rows in it
        // (M96b). MATLAB answers 0-by-0 char for every empty, whatever shape it wore.
        if (JgsEmpty.IsEmptyArray(subject))
        {
            return JgsValue.Str(string.Empty);
        }

        double[][] rows = subject.Type is JgsType.Number or JgsType.Bool
            ? [[subject.AsNumber]]
            : JgsMatrix.ToRows("num2str", subject, line, col);

        string[] text = args.Count == 2 && args[1].Type == JgsType.String
            ? FormattedRows(args[1].AsString, rows, line, col)
            : AlignedRows(rows, args.Count == 2 ? Count("num2str", args, 1, line, col) : null);

        // Several rows are a char matrix, which is what MATLAB answers and what the aligned columns
        // above were computed for (M105). It used to be a cell, so num2str([1; 22]) came back 1-by-2
        // cell where MATLAB says 2-by-2 char — and a cell is the one container none of the char rules
        // apply to.
        return text.Length == 1
            ? JgsValue.Str(text[0])
            : JgsValue.CharMatrix(text);
    }

    /// <summary><c>num2str(A, '%8.3f')</c>: the format is applied a row at a time, cycling over it.</summary>
    private static string[] FormattedRows(string format, double[][] rows, int line, int col)
    {
        var text = new string[rows.Length];
        for (int r = 0; r < rows.Length; r++)
        {
            var values = new JgsValue[rows[r].Length];
            for (int c = 0; c < values.Length; c++)
            {
                values[c] = JgsValue.Number(rows[r][c]);
            }

            try
            {
                text[r] = JgsSprintf.FormatMatlab(format, values);
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        }

        return text;
    }

    /// <summary>
    /// MATLAB's own column layout: every element is printed into a field of one fixed width, and the
    /// blanks every row shares at the front are then removed. The width comes from the magnitude of
    /// the largest element, which is what makes <c>num2str([1 2 3])</c> read <c>'1  2  3'</c> and
    /// <c>num2str([1 20; 300 4])</c> keep its columns lined up.
    /// </summary>
    private static string[] AlignedRows(double[][] rows, int? digits)
    {
        double biggest = 0;
        bool whole = true;
        bool negative = false;
        foreach (double[] row in rows)
        {
            foreach (double value in row)
            {
                negative |= value < 0;
                if (!double.IsFinite(value))
                {
                    continue; // NaN and Inf have a spelling, not a magnitude
                }

                biggest = Math.Max(biggest, Math.Abs(value));
                whole &= value == Math.Floor(value);
            }
        }

        int magnitude = biggest > 0
            ? Math.Min(15, Math.Max(1, (int)Math.Floor(Math.Log10(biggest)) + 1))
            : 1;

        int width;
        int precision;
        if (digits is { } asked)
        {
            width = asked + 7;
            precision = asked;
        }
        else if (whole)
        {
            width = magnitude + (negative ? 1 : 0) + 2;
            precision = 0;
        }
        else
        {
            width = magnitude + 7;
            precision = magnitude + 4;
        }

        var cells = new string[rows.Length][];
        int longest = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            cells[r] = new string[rows[r].Length];
            for (int c = 0; c < rows[r].Length; c++)
            {
                cells[r][c] = OneNumber(rows[r][c], precision);
                longest = Math.Max(longest, cells[r][c].Length);
            }
        }

        // NaN and Inf are spelled, not sized, so a column of small numbers holding one of them would
        // otherwise be narrower than the word in it and run the elements together.
        width = Math.Max(width, longest + 2);

        var text = new string[rows.Length];
        int shared = int.MaxValue;
        for (int r = 0; r < rows.Length; r++)
        {
            var built = new StringBuilder();
            foreach (string cell in cells[r])
            {
                built.Append(cell.PadLeft(width));
            }

            text[r] = built.ToString();
            int blanks = 0;
            while (blanks < text[r].Length && text[r][blanks] == ' ')
            {
                blanks++;
            }

            shared = Math.Min(shared, blanks);
        }

        for (int r = 0; r < text.Length; r++)
        {
            text[r] = text[r][Math.Min(shared, text[r].Length)..];
        }

        return text;
    }

    /// <summary>One element as text: a whole number keeps every digit, anything else takes %g.</summary>
    private static string OneNumber(double value, int precision)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Inf" : "-Inf";
        }

        return precision == 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : JgsSprintf.FormatMatlab(
                "%." + precision.ToString(CultureInfo.InvariantCulture) + "g", [JgsValue.Number(value)]);
    }

    // --- mat2str, int2str and deal (M52 wave E) ---------------------------------------------------

    /// <summary>
    /// <c>mat2str(A)</c>, <c>mat2str(A, n)</c>: the value written the way the language would read it
    /// back — brackets, spaces between columns, semicolons between rows.
    /// </summary>
    /// <remarks>
    /// This is <c>num2str</c>'s opposite number: <c>num2str</c> lays a matrix out for a person, so it
    /// pads columns and hands back one string per row, while <c>mat2str</c> writes one string that
    /// <c>eval</c> would turn back into the same value. Fifteen significant digits is MATLAB's
    /// default, which is what makes the round trip exact for a double.
    /// </remarks>
    private static JgsValue MatrixText(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mat2str", args, 1, 2, line, col);
        JgsValue subject = args[0];
        int precision = args.Count == 2 ? Count("mat2str", args, 1, line, col) : 15;
        if (precision < 1)
        {
            throw new JgsRuntimeException(line, col, "mat2str: the precision is a count of digits, so it is at least 1.");
        }

        if (subject.Type == JgsType.String)
        {
            // A char row reads back as a char row, which means the quotes are part of the answer.
            return JgsValue.Str("\"" + subject.AsString + "\"");
        }

        // A complex value has to be written as one (M81). Before this, mat2str reached JgsMatrix.ToRows,
        // which reads only reals: a complex scalar came back as the bare text '[]' and a complex array
        // threw — so the one function whose whole contract is "text eval reads back as this value"
        // silently failed to hold it.
        if (subject.Type == JgsType.Complex || HasComplexElements(subject))
        {
            return JgsValue.Str(ComplexMatrixText(subject, precision, line, col));
        }

        bool logical = IsLogicalValue(subject);
        double[][] rows = subject.Type is JgsType.Number or JgsType.Bool
            ? [[subject.AsNumber]]
            : JgsMatrix.ToRows("mat2str", subject, line, col);

        string One(double value) => logical
            ? value != 0 ? "true" : "false"
            : OneNumber(value, precision);

        if (rows.Length == 1 && rows[0].Length == 1)
        {
            return JgsValue.Str(One(rows[0][0]));
        }

        var written = new List<string>(rows.Length);
        foreach (double[] row in rows)
        {
            written.Add(string.Join(" ", row.Select(One)));
        }

        return JgsValue.Str("[" + string.Join(";", written) + "]");
    }

    /// <summary>
    /// <c>mat2str</c> of a value with an imaginary part anywhere in it.
    /// </summary>
    /// <remarks>
    /// Every element is written <c>re+imi</c>, including the ones that happen to be real, because that
    /// is what makes the text read back as the same value: <c>[1+0i 0+2i]</c> is a complex array where
    /// <c>[1 0+2i]</c> would be one too, but only by accident of the second element. MATLAB writes it
    /// the same way and for the same reason.
    /// </remarks>
    private static string ComplexMatrixText(JgsValue subject, int precision, int line, int col)
    {
        string One(System.Numerics.Complex z)
        {
            string real = OneNumber(z.Real, precision);
            string imaginary = OneNumber(Math.Abs(z.Imaginary), precision);
            return real + (z.Imaginary < 0 ? "-" : "+") + imaginary + "i";
        }

        if (subject.Type is JgsType.Complex or JgsType.Number or JgsType.Bool)
        {
            return One(subject.AsComplex);
        }

        int rows = JgsMatrix.RowCount(subject);
        int cols = JgsMatrix.ColCount(subject);
        var written = new List<string>(rows);
        for (int r = 0; r < rows; r++)
        {
            var cells = new List<string>(cols);
            for (int c = 0; c < cols; c++)
            {
                JgsValue element = JgsMatrix.IsNested(subject)
                    ? subject.ElementAt(r).ElementAt(c)
                    : subject.ElementAt((c * rows) + r);
                if (element.Type is not (JgsType.Number or JgsType.Bool or JgsType.Complex))
                {
                    throw new JgsRuntimeException(line, col,
                        $"mat2str needs numbers, but element ({r}, {c}) was a {element.TypeName}.");
                }

                cells.Add(One(element.AsComplex));
            }

            written.Add(string.Join(" ", cells));
        }

        return rows == 1 && cols == 1 ? written[0] : "[" + string.Join(";", written) + "]";
    }

    /// <summary>
    /// <c>int2str(x)</c>: the value rounded to whole numbers and written out. Everything about the
    /// layout is <c>num2str</c>'s, because it is the same question asked of numbers that happen to
    /// have no fractional part.
    /// </summary>
    private static JgsValue WholeNumberText(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("int2str", args, 1, line, col);
        JgsValue rounded = MapNumeric(
            "int2str", args[0], static x => double.IsFinite(x) ? Math.Round(x, MidpointRounding.AwayFromZero) : x,
            line, col);
        return NumberText([rounded], line, col);
    }

    /// <summary>
    /// <c>[a, b, …] = deal(…)</c>: one value handed to every output, or one value each. It exists
    /// because a function's outputs are the only place several values can be assigned at once, and
    /// sometimes the several values are already in hand.
    /// </summary>
    private static JgsValue[] Dealt(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "deal needs a value to hand out.");
        }

        int outputs = Math.Max(wanted, 1);
        if (args.Count == 1)
        {
            var copies = new JgsValue[outputs];
            Array.Fill(copies, args[0]);
            return copies;
        }

        if (args.Count != outputs)
        {
            throw new JgsRuntimeException(line, col,
                $"deal: {args.Count} value(s) cannot fill {outputs} output(s) — pass one value, or one each.");
        }

        return [.. args];
    }
}
