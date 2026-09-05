using System.Linq;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The comparing, searching, trimming, padding and joining verbs, rebuilt against MATLAB R2025b:
/// <c>strcmp</c> and its three siblings, <c>contains</c>/<c>startsWith</c>/<c>endsWith</c>/
/// <c>matches</c>/<c>count</c> with <c>'IgnoreCase'</c>, <c>strfind</c> with
/// <c>'ForceCellOutput'</c>, <c>strlength</c>, <c>ismissing</c>, the three <c>convert…</c>
/// helpers, <c>cellstr</c>, <c>strcat</c>, <c>pad</c>, <c>strip</c>, <c>deblank</c>,
/// <c>strtrim</c> and <c>split</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these used to reach MATLAB's answer only for one piece of char text and fall over
/// — or, worse, answer something else — for the containers a MATLAB script actually hands them: a
/// cell of char, a string array, a char matrix, a number where a character was meant. The rules they
/// now share were measured in MATLAB rather than read from its documentation, and the reference
/// scripts that measured them sit beside the string-function audit.
/// </para>
/// <para>
/// Three rules recur. <b>A missing string is never equal to anything and answers a missing string
/// from every text verb</b>: <c>strcmp(string(missing), string(missing))</c> is false,
/// <c>upper(string(missing))</c> is missing, <c>strlength</c> of it is NaN. <b>Two containers pair
/// element by element, and a one-element container expands</b>: <c>strcmp({'a','b'}, {'a'})</c> is
/// <c>[1 0]</c> and <c>strcmp({'a','b'}, {'a','b','c'})</c> is refused. <b>A verb answers in the
/// container it was handed</b>: char in, char out; string in, string out; cell in, cell out.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The characters <c>strtrim</c>, <c>strip</c> and <c>pad</c> read as whitespace: ASCII 9–13 and the space.</summary>
    private static readonly char[] TextWhitespace = ['\t', '\n', '\v', '\f', '\r', ' '];

    /// <summary>What <c>deblank</c> removes: the whitespace above and the NUL character (measured).</summary>
    private static readonly char[] DeblankWhitespace = ['\t', '\n', '\v', '\f', '\r', ' ', '\0'];

    /// <summary>Whether a string-array element is the missing string.</summary>
    private static bool IsMissingText(string text) => text == MissingSentinel;

    /// <summary>Registers the family. Runs after every other text define so it holds each name.</summary>
    internal static void RegisterTextFamilyBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        BuiltinFunction? Inner(string name) =>
            env.TryGet(name, out JgsValue declared) && declared.Type == JgsType.Function
                && declared.AsCallable is BuiltinFunction inner
                ? inner
                : null;

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                MultiOutput = multi,

                // Every name here reads the container it was handed, so none may be demoted on the way in.
                KeepsStringArguments = true,
            }));

        // --- comparing -----------------------------------------------------------------------------
        Define("strcmp", (args, line, col) => TextCompared("strcmp", args, StringComparison.Ordinal, false, line, col));
        Define("strcmpi", (args, line, col) => TextCompared("strcmpi", args, StringComparison.OrdinalIgnoreCase, false, line, col));
        Define("strncmp", (args, line, col) => TextCompared("strncmp", args, StringComparison.Ordinal, true, line, col));
        Define("strncmpi", (args, line, col) => TextCompared("strncmpi", args, StringComparison.OrdinalIgnoreCase, true, line, col));

        // --- searching -----------------------------------------------------------------------------
        BuiltinFunction? legacyContains = Inner("contains");
        Define("contains", (args, line, col) => TextSearched("contains", args, dialect, legacyContains,
            static (text, patterns, cmp) => JgsValue.Bool(Array.Exists(patterns, p => text.Contains(p, cmp))),
            JgsValue.Bool(false), line, col));
        Define("startsWith", (args, line, col) => TextSearched("startsWith", args, dialect, null,
            static (text, patterns, cmp) => JgsValue.Bool(Array.Exists(patterns, p => text.StartsWith(p, cmp))),
            JgsValue.Bool(false), line, col));
        Define("endsWith", (args, line, col) => TextSearched("endsWith", args, dialect, null,
            static (text, patterns, cmp) => JgsValue.Bool(Array.Exists(patterns, p => text.EndsWith(p, cmp))),
            JgsValue.Bool(false), line, col));
        Define("matches", (args, line, col) => TextSearched("matches", args, dialect, null,
            static (text, patterns, cmp) => JgsValue.Bool(Array.Exists(patterns, p => string.Equals(text, p, cmp))),
            JgsValue.Bool(false), line, col));
        Define("count", (args, line, col) => TextSearched("count", args, dialect, null,
            static (text, patterns, cmp) => JgsValue.Number(cmp == StringComparison.Ordinal
                ? CountAtOnce(text, patterns)
                : CountAtOnce(text.ToUpperInvariant(), Array.ConvertAll(patterns, static p => p.ToUpperInvariant()))),
            JgsValue.Number(0), line, col));

        Define("strfind", StrFind);
        Define("strlength", StrLength);

        BuiltinFunction? legacyMissing = Inner("ismissing");
        Define("ismissing", (args, line, col) => IsMissingOf(args, legacyMissing, line, col));
        Define("anymissing", (args, line, col) =>
        {
            Arity("anymissing", args, 1, line, col);
            JgsValue flags = IsMissingOf(args, legacyMissing, line, col);
            return JgsValue.Bool(flags.Type == JgsType.Array
                ? Array.Exists(flags.BoxedElements(), static f => f.IsTruthy)
                : flags.IsTruthy);
        });

        // --- converting ----------------------------------------------------------------------------
        Define("convertCharsToStrings",
            (args, line, col) => ConvertEach("convertCharsToStrings", args, CharsToStrings, 1, line, col)[0],
            (args, wanted, line, col) => ConvertEach("convertCharsToStrings", args, CharsToStrings, wanted, line, col));
        Define("convertStringsToChars",
            (args, line, col) => ConvertEach("convertStringsToChars", args, StringsToChars, 1, line, col)[0],
            (args, wanted, line, col) => ConvertEach("convertStringsToChars", args, StringsToChars, wanted, line, col));
        Define("convertContainedStringsToChars",
            (args, line, col) => ConvertEach("convertContainedStringsToChars", args, ContainedStringsToChars, 1, line, col)[0],
            (args, wanted, line, col) => ConvertEach("convertContainedStringsToChars", args, ContainedStringsToChars, wanted, line, col));
        Define("cellstr", CellStr);

        // --- editing -------------------------------------------------------------------------------
        Define("strcat", StrCat);
        Define("pad", Padded);
        Define("strip", Stripped);
        Define("deblank", (args, line, col) =>
        {
            Arity("deblank", args, 1, line, col);
            return Trimmed("deblank", args[0], DeblankWhitespace, leading: false, passNumbers: true, line, col);
        });
        Define("strtrim", (args, line, col) =>
        {
            Arity("strtrim", args, 1, line, col);
            return Trimmed("strtrim", args[0], TextWhitespace, leading: true, passNumbers: false, line, col);
        });

        // reverse is a text verb in MATLAB: reverse(5) and reverse(['ab';'cd']) are refused
        // (measured), where JGS keeps its array flip.
        BuiltinFunction? legacyReverse = Inner("reverse");
        if (legacyReverse is not null && dialect.IsMatlab)
        {
            env.Declare("reverse", JgsValue.Function(new BuiltinFunction("reverse", (args, line, col) =>
            {
                if (args.Count == 1 && (args[0].IsCharMatrix || !TryReadText(args[0], out _)))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                        "First argument must be text.");
                }

                return legacyReverse.Call(args, line, col);
            })
            { KeepsStringArguments = true }));
        }

        // --- splitting -----------------------------------------------------------------------------
        BuiltinFunction? legacySplit = Inner("split");
        Define("split",
            (args, line, col) => SplitText2(args, dialect, legacySplit, 1, line, col)[0],
            (args, wanted, line, col) => SplitText2(args, dialect, legacySplit, wanted, line, col));
    }

    // --- shared readers -----------------------------------------------------------------------------

    /// <summary>Whether a value is one number: a bare number or logical, or a 1-by-1 numeric array.</summary>
    private static bool IsOneNumber(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool
        || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix && value.ArrayLength == 1
            && value.ElementAt(0).Type is JgsType.Number or JgsType.Bool);

    /// <summary>The one number a value is; ask <see cref="IsOneNumber"/> first.</summary>
    private static double OneNumber(JgsValue value) =>
        value.Type == JgsType.Array ? value.ElementAt(0).AsNumber : value.AsNumber;

    /// <summary>An empty array that class reports as logical, in the shape asked for.</summary>
    private static JgsValue EmptyLogical(int rows, int cols) => JgsPacking.Enabled
        ? JgsValue.Shaped(JgsPacking.Allocate(0), rows, cols, JgsPackedKind.Bool)
        : JgsEmpty.Shaped(rows, cols);

    /// <summary>A logical array in the shape asked for; one element in a 1-by-1 is the bare logical.</summary>
    private static JgsValue LogicalMask(bool[] flags, int rows, int cols)
    {
        if (flags.Length == 1 && rows == 1 && cols == 1)
        {
            return JgsValue.Bool(flags[0]);
        }

        if (flags.Length == 0)
        {
            return EmptyLogical(rows, cols);
        }

        JgsValue mask = JgsValue.Array(Array.ConvertAll(flags, JgsValue.Bool));
        mask.Reshape(rows, cols);
        return mask;
    }

    /// <summary>Values in the shape asked for; one in a 1-by-1 is the bare value.</summary>
    private static JgsValue ShapedValues(JgsValue[] values, int rows, int cols)
    {
        if (values.Length == 1 && rows == 1 && cols == 1)
        {
            return values[0];
        }

        JgsValue shaped = JgsValue.Array(values);
        shaped.Reshape(rows, cols);
        return shaped;
    }

    /// <summary>
    /// Whether the word names an option, spelled whole or as any leading part of it: MATLAB's
    /// name/value parsing accepts <c>'Ignore'</c> for <c>'IgnoreCase'</c>, in any case.
    /// </summary>
    private static bool NamesOption(JgsValue value, string option) =>
        IsTextScalar(value) && TextOf(value).Length > 0
        && option.StartsWith(TextOf(value), StringComparison.OrdinalIgnoreCase);

    /// <summary>The logical a name/value option was given, or the refusal of anything else.</summary>
    private static bool OptionFlag(string name, string option, JgsValue value, int line, int col)
    {
        if (!IsOneNumber(value))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the value of '{option}' must be a logical scalar.");
        }

        return OneNumber(value) != 0;
    }

    // --- strcmp and friends -------------------------------------------------------------------------

    /// <summary>One side of a text comparison: its pieces (null where a piece is not text) and its shape.</summary>
    private readonly record struct CompareSide(string?[] Texts, int Rows, int Cols)
    {
        public bool Scalar => Texts.Length == 1 && Rows == 1 && Cols == 1;
    }

    /// <summary>
    /// Reads a comparison operand. A missing string and anything that is not text compare false
    /// rather than refusing; a char matrix is one piece compared whole, so
    /// <c>strcmp(['ab';'cd'], ['ab';'cd'])</c> is true and <c>strcmp(['ab';'cd'], 'ab')</c> false.
    /// </summary>
    private static CompareSide ReadCompareSide(JgsValue value)
    {
        if (value.IsCharMatrix)
        {
            return new(["" + string.Join("", value.CharMatrixRows())], 1, 1);
        }

        if (value.Type == JgsType.String)
        {
            return new([IsMissingText(value.AsString) ? null : value.AsString], 1, 1);
        }

        if (value.IsStringArray)
        {
            return new(
                Array.ConvertAll(value.BoxedElements(), static e => IsMissingText(e.AsString) ? null : e.AsString),
                value.Rows, value.Cols);
        }

        if (value.Type == JgsType.Cell)
        {
            return new(
                Array.ConvertAll(value.AsCell, static e => e.Type == JgsType.String ? e.AsString : null),
                value.Rows, value.Cols);
        }

        return new([null], 1, 1);
    }

    /// <summary>The shape two paired operands answer in: either may be one element, else they agree.</summary>
    private static (int Rows, int Cols) PairedShape(CompareSide a, CompareSide b, int line, int col)
    {
        if (a.Scalar)
        {
            return (b.Rows, b.Cols);
        }

        if (b.Scalar || (a.Rows == b.Rows && a.Cols == b.Cols))
        {
            return (a.Rows, a.Cols);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:strcmp:InputsSizeMismatch",
            "Inputs must be the same size or either one can be a scalar.");
    }

    /// <summary>
    /// <c>strcmp</c>, <c>strcmpi</c>, <c>strncmp</c> and <c>strncmpi</c>: a logical per paired
    /// element. <c>strncmp</c> compares the first n characters where both have that many, and the
    /// whole strings where one does not (measured: <c>strncmp('abc', 'abc', 5)</c> is true,
    /// <c>strncmp('abc', 'abcd', 5)</c> false); n of zero or less is always true.
    /// </summary>
    private static JgsValue TextCompared(
        string name, IReadOnlyList<JgsValue> args, StringComparison comparison, bool prefix, int line, int col)
    {
        Arity(name, args, prefix ? 3 : 2, line, col);
        CompareSide a = ReadCompareSide(args[0]);
        CompareSide b = ReadCompareSide(args[1]);
        int n = int.MaxValue;
        if (prefix)
        {
            if (!IsOneNumber(args[2]))
            {
                throw new JgsRuntimeException(line, col, $"{name}: the length must be a numeric scalar.");
            }

            double given = OneNumber(args[2]);
            n = double.IsNaN(given) ? 0 : (int)Math.Floor(Math.Clamp(given, int.MinValue, int.MaxValue));
        }

        (int rows, int cols) = PairedShape(a, b, line, col);
        int count = rows * cols;
        var flags = new bool[count];
        for (int i = 0; i < count; i++)
        {
            string? x = a.Texts[a.Scalar ? 0 : i];
            string? y = b.Texts[b.Scalar ? 0 : i];
            flags[i] = x is not null && y is not null && AgreeOn(x, y, n, comparison);
        }

        return LogicalMask(flags, rows, cols);
    }

    private static bool AgreeOn(string x, string y, int n, StringComparison comparison)
    {
        if (n <= 0)
        {
            return true;
        }

        if (n <= x.Length && n <= y.Length)
        {
            return string.Compare(x, 0, y, 0, n, comparison) == 0;
        }

        return string.Equals(x, y, comparison);
    }

    // --- contains, startsWith, endsWith, matches, count ---------------------------------------------

    /// <summary>
    /// The shape every search verb shares: <c>verb(str, pattern)</c> or
    /// <c>verb(str, pattern, 'IgnoreCase', tf)</c>, answered once per element of <c>str</c>, with a
    /// list of patterns asked as one question. A missing string answers <paramref name="forMissing"/>.
    /// </summary>
    private static JgsValue TextSearched(
        string name, IReadOnlyList<JgsValue> args, JgsDialect dialect, BuiltinFunction? legacy,
        Func<string, string[], StringComparison, JgsValue> answer, JgsValue forMissing, int line, int col)
    {
        if (args.Count is not (2 or 4))
        {
            throw new JgsRuntimeException(line, col, args.Count < 2
                ? $"{name} expects at least 2 argument(s), but got {args.Count}."
                : $"{name}: '{(args.Count == 3 ? TextOfAnswer(args[2]) : "IgnoreCase")}' needs a value after it.");
        }

        bool ignoreCase = false;
        if (args.Count == 4)
        {
            if (!NamesOption(args[2], "IgnoreCase"))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: unknown option '{TextOfAnswer(args[2])}'. The only option is 'IgnoreCase'.");
            }

            ignoreCase = OptionFlag(name, "IgnoreCase", args[3], line, col);
        }

        if (args[0].IsCharMatrix || !TryReadText(args[0], out TextBundle subject))
        {
            // JGS's contains is also a membership test over an array, and that reading is kept for it.
            if (legacy is not null && !dialect.IsMatlab && args.Count == 2)
            {
                return legacy.Call(args, line, col);
            }

            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                "First argument must be text.");
        }

        string[] patterns = PatternsOf(name, args, 1, line, col);
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var answers = new JgsValue[subject.Texts.Length];
        for (int i = 0; i < answers.Length; i++)
        {
            answers[i] = subject.Kind == TextKind.String && IsMissingText(subject.Texts[i])
                ? forMissing
                : answer(subject.Texts[i], patterns, comparison);
        }

        if (answers.Length == 0)
        {
            return forMissing.Type == JgsType.Bool
                ? EmptyLogical(subject.Rows, subject.Cols)
                : JgsEmpty.Shaped(subject.Rows, subject.Cols);
        }

        return ShapedValues(answers, subject.Rows, subject.Cols);
    }

    // --- strfind ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>strfind(str, pattern)</c> and <c>strfind(str, pattern, 'ForceCellOutput', tf)</c>: every
    /// start position of the pattern, overlapping, as a row — a 0-by-0 when there are none — and one
    /// such row per element in a cell when <c>str</c> is a container or the cell is forced. A numeric
    /// array is searched as a sequence of values, and a pattern of numbers is read as character codes.
    /// </summary>
    private static JgsValue StrFind(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2 || args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, args.Count < 2
                ? $"strfind expects at least 2 argument(s), but got {args.Count}."
                : "strfind: 'ForceCellOutput' needs a value after it.");
        }

        bool forceCell = false;
        for (int at = 2; at < args.Count; at += 2)
        {
            if (!NamesOption(args[at], "ForceCellOutput"))
            {
                throw new JgsRuntimeException(line, col,
                    $"strfind: unknown option '{TextOfAnswer(args[at])}'. The only option is 'ForceCellOutput'.");
            }

            forceCell = OptionFlag("strfind", "ForceCellOutput", args[at + 1], line, col);
        }

        if (args[0].IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:strfind:InvalidInput",
                "First argument must be a character vector, a cell array of character vectors, a string array, or a numeric array.");
        }

        JgsValue subjectValue = args[0];
        JgsValue patternValue = args[1];

        // A numeric subject is searched as numbers; a numeric pattern against text is character codes.
        if (subjectValue.Type is JgsType.Number or JgsType.Bool
            || (subjectValue.Type == JgsType.Array && !subjectValue.IsStringArray))
        {
            double[] haystack = ToDoubles("strfind", subjectValue, line, col);
            double[] needle = PatternValues(patternValue, line, col);
            JgsValue positions = Found(SequenceOccurrences(haystack, needle));
            return forceCell ? JgsValue.Cell([positions]) : positions;
        }

        if (!TryReadText(subjectValue, out TextBundle subject))
        {
            if (subjectValue.Type == JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:strfind:InvalidInput",
                    "Cell must be a cell array of character vectors.");
            }

            throw new JgsRuntimeException(line, col, "MATLAB:strfind:InvalidInput",
                "First argument must be a character vector, a cell array of character vectors, a string array, or a numeric array.");
        }

        string pattern = PatternText(patternValue, line, col);
        var perElement = new JgsValue[subject.Texts.Length];
        for (int i = 0; i < perElement.Length; i++)
        {
            perElement[i] = subject.Kind == TextKind.String && IsMissingText(subject.Texts[i])
                ? JgsEmpty.Zero()
                : Found(Occurrences(subject.Texts[i], pattern, 1));
        }

        bool oneRow = subject.Kind == TextKind.Char || (subject.Kind == TextKind.String && subject.Scalar);
        if (oneRow && !forceCell)
        {
            return perElement[0];
        }

        JgsValue cell = JgsValue.Cell(perElement);
        cell.Reshape(subject.Rows, subject.Cols);
        return cell;
    }

    /// <summary>The one pattern <c>strfind</c> looks for: text, or a cell or string array holding one piece.</summary>
    private static string PatternText(JgsValue pattern, int line, int col)
    {
        if (pattern.Type is JgsType.Number or JgsType.Bool
            || (pattern.Type == JgsType.Array && !pattern.IsStringArray && !pattern.IsCharMatrix))
        {
            var codes = new StringBuilder();
            foreach (double code in ToDoubles("strfind", pattern, line, col))
            {
                codes.Append((char)(int)code);
            }

            return codes.ToString();
        }

        string[]? pieces = pattern.IsCharMatrix ? null : TextElementsOf(pattern);
        if (pieces is { Length: 1 })
        {
            return pieces[0];
        }

        throw new JgsRuntimeException(line, col, "MATLAB:strfind:InvalidInput",
            "Pattern must be a string scalar, a character vector, or a cell array containing one character vector.");
    }

    /// <summary>The values a numeric-subject <c>strfind</c> looks for: numbers, or the codes of text.</summary>
    private static double[] PatternValues(JgsValue pattern, int line, int col)
    {
        if (pattern.Type is JgsType.Number or JgsType.Bool
            || (pattern.Type == JgsType.Array && !pattern.IsStringArray && !pattern.IsCharMatrix))
        {
            return ToDoubles("strfind", pattern, line, col);
        }

        string text = PatternText(pattern, line, col);
        var codes = new double[text.Length];
        for (int i = 0; i < codes.Length; i++)
        {
            codes[i] = text[i];
        }

        return codes;
    }

    /// <summary>Every start of <paramref name="needle"/> in <paramref name="haystack"/>, overlapping, 1-based.</summary>
    private static double[] SequenceOccurrences(double[] haystack, double[] needle)
    {
        if (needle.Length == 0)
        {
            return [];
        }

        var found = new List<double>();
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            bool same = true;
            for (int k = 0; k < needle.Length && same; k++)
            {
                same = haystack[at + k].Equals(needle[k]);
            }

            if (same)
            {
                found.Add(at + 1);
            }
        }

        return found.ToArray();
    }

    // --- strlength ----------------------------------------------------------------------------------

    /// <summary>Characters per element, NaN for a missing string, in the subject's shape.</summary>
    private static JgsValue StrLength(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("strlength", args, 1, line, col);
        if (args[0].IsCharMatrix || !TryReadText(args[0], out TextBundle subject))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                "First argument must be text.");
        }

        if (subject.Texts.Length == 0)
        {
            return JgsEmpty.Shaped(subject.Rows, subject.Cols);
        }

        var lengths = new double[subject.Texts.Length];
        for (int i = 0; i < lengths.Length; i++)
        {
            lengths[i] = subject.Kind == TextKind.String && IsMissingText(subject.Texts[i])
                ? double.NaN
                : subject.Texts[i].Length;
        }

        if (lengths.Length == 1 && subject.Rows == 1 && subject.Cols == 1)
        {
            return JgsValue.Number(lengths[0]);
        }

        JgsValue answer = Numbers(lengths);
        answer.Reshape(subject.Rows, subject.Cols);
        return answer;
    }

    // --- ismissing ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>ismissing(A)</c> and <c>ismissing(A, indicators)</c>. A string array is missing where it
    /// holds the missing string; a cell of char is missing where a cell is empty; a char row is asked
    /// character by character and is only ever missing where an indicator says so; numbers are
    /// missing where NaN. Anything else keeps the answer it had.
    /// </summary>
    private static JgsValue IsMissingOf(IReadOnlyList<JgsValue> args, BuiltinFunction? legacy, int line, int col)
    {
        ArityRange("ismissing", args, 1, 2, line, col);
        JgsValue value = args[0];
        JgsValue? indicators = args.Count == 2 ? args[1] : null;
        string[] indicatorTexts = indicators is null ? [] : TextElementsOf(indicators) ?? [];
        double[] indicatorNumbers = indicators is { } given && indicatorTexts.Length == 0
            && (given.Type is JgsType.Number or JgsType.Bool || (given.Type == JgsType.Array && !given.IsStringArray))
            ? ToDoubles("ismissing", given, line, col)
            : [];

        // With indicators, only the indicators count: ismissing(["a" missing], "a") is [1 0] and
        // ismissing([1 2 NaN], 2) is [0 1 0] (measured).
        bool byDefault = indicators is null;
        bool Named(string text) => Array.IndexOf(indicatorTexts, text) >= 0;

        if (value.IsStringArray)
        {
            string[] texts = Array.ConvertAll(value.BoxedElements(), static e => e.AsString);
            return LogicalMask(Array.ConvertAll(texts, t => byDefault ? IsMissingText(t) : Named(t)), value.Rows, value.Cols);
        }

        if (value.Type == JgsType.String)
        {
            string text = value.AsString;
            if (IsMissingText(text))
            {
                return JgsValue.Bool(true); // the bare `missing` value
            }

            if (text.Length == 0)
            {
                return EmptyLogical(0, 0);
            }

            var flags = new bool[text.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] = Array.Exists(indicatorTexts, t => t.Contains(text[i]));
            }

            return LogicalMask(flags, 1, text.Length);
        }

        if (value.Type == JgsType.Cell)
        {
            if (indicators is { IsStringArray: true })
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ismissing:IndicatorTypeMismatch",
                    "ismissing: the indicators for a cell array of character vectors must be character vectors.");
            }

            JgsValue[] cells = value.AsCell;
            var flags = new bool[cells.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] = cells[i].Type == JgsType.String
                    && (byDefault ? cells[i].AsString.Length == 0 : Named(cells[i].AsString));
            }

            return LogicalMask(flags, value.Rows, value.Cols);
        }

        if (value.Type is JgsType.Number or JgsType.Bool
            || (value.Type == JgsType.Array && !value.IsCharMatrix && !value.IsNd))
        {
            if (value.Type == JgsType.Array && value.ArrayLength == 0)
            {
                return EmptyLogical(value.Rows, value.Cols);
            }

            double[] numbers = ToDoubles("ismissing", value, line, col);
            var flags = new bool[numbers.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] = byDefault ? double.IsNaN(numbers[i]) : Array.IndexOf(indicatorNumbers, numbers[i]) >= 0;
            }

            return value.Type == JgsType.Array
                ? LogicalMask(flags, value.Rows, value.Cols)
                : JgsValue.Bool(flags[0]);
        }

        if (legacy is not null && indicators is null)
        {
            return legacy.Call(args, line, col);
        }

        throw new JgsRuntimeException(line, col, $"ismissing does not know how to read a {value.TypeName}.");
    }

    // --- the convert… helpers -----------------------------------------------------------------------

    /// <summary>Each argument converted in turn: one answer per argument, as MATLAB's do.</summary>
    private static JgsValue[] ConvertEach(
        string name, IReadOnlyList<JgsValue> args, Func<JgsValue, JgsValue> convert, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        // One answer per argument, and every argument must have somewhere to go (measured:
        // convertCharsToStrings({'a'}, 'b') with one output is an error).
        if (args.Count > Math.Max(1, wanted))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:ConvertTooManyInputs",
                $"{name}: the number of output arguments must equal the number of input arguments.");
        }

        var converted = new JgsValue[args.Count];
        for (int i = 0; i < converted.Length; i++)
        {
            converted[i] = convert(args[i]);
        }

        return converted;
    }

    /// <summary>A char row or a cell of char becomes strings; everything else is left alone.</summary>
    private static JgsValue CharsToStrings(JgsValue value)
    {
        if (value.Type == JgsType.String)
        {
            return JgsValue.StringScalar(value.AsString);
        }

        if (value.IsCharMatrix)
        {
            // One string of the matrix's storage order, which is how MATLAB reads a char matrix that
            // is not a row: convertCharsToStrings(['ab';'cd']) is "acbd" (measured).
            return JgsValue.StringScalar(value.CharMatrixText());
        }

        if (value.Type == JgsType.Cell && Array.TrueForAll(value.AsCell, static e => e.Type == JgsType.String))
        {
            return JgsValue.StringArray(Array.ConvertAll(value.AsCell, static e => JgsValue.Str(e.AsString)), value.Rows, value.Cols);
        }

        return value;
    }

    /// <summary>A string becomes char, a string array a cell of char, a missing string ''; else unchanged.</summary>
    private static JgsValue StringsToChars(JgsValue value)
    {
        if (!value.IsStringArray)
        {
            return value;
        }

        string[] texts = Array.ConvertAll(value.BoxedElements(), static e => IsMissingText(e.AsString) ? string.Empty : e.AsString);
        if (texts.Length == 1 && value.Rows == 1 && value.Cols == 1)
        {
            return JgsValue.Str(texts[0]);
        }

        JgsValue cell = JgsValue.Cell(Array.ConvertAll(texts, JgsValue.Str));
        cell.Reshape(value.Rows, value.Cols);
        return cell;
    }

    /// <summary>Strings anywhere inside a cell or a struct become char; the containers keep their shape.</summary>
    private static JgsValue ContainedStringsToChars(JgsValue value)
    {
        if (value.IsStringArray)
        {
            return StringsToChars(value);
        }

        if (value.Type == JgsType.Cell)
        {
            JgsValue cell = JgsValue.Cell(Array.ConvertAll(value.AsCell, ContainedStringsToChars));
            cell.Reshape(value.Rows, value.Cols);
            return cell;
        }

        if (value.Type == JgsType.Struct && IsStructValue(value))
        {
            JgsStructArray elements = value.AsStructArray;
            var converted = new Dictionary<string, JgsValue>[elements.Length];
            for (int i = 0; i < converted.Length; i++)
            {
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JgsValue> field in elements.Elements[i])
                {
                    fields[field.Key] = ContainedStringsToChars(field.Value);
                }

                converted[i] = fields;
            }

            return JgsValue.StructArray(new JgsStructArray(converted, elements.EmptyFields), value.Rows, value.Cols);
        }

        return value;
    }

    // --- cellstr ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>cellstr(A)</c>: a cell of char rows in the shape of the string array or char matrix given,
    /// trailing blanks removed from char, the missing string read as ''. A cell that already holds
    /// char rows is itself; one that holds anything else is refused, as MATLAB refuses it.
    /// </summary>
    private static JgsValue CellStr(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("cellstr", args, 1, line, col);
        JgsValue input = args[0];

        if (input.IsCharMatrix)
        {
            string[] rows = input.CharMatrixRows();
            JgsValue stacked = JgsValue.Cell(Array.ConvertAll(rows, static r => JgsValue.Str(r.TrimEnd(' '))));
            stacked.Reshape(rows.Length, 1);
            return stacked;
        }

        if (input.Type == JgsType.String)
        {
            return JgsValue.Cell([JgsValue.Str(input.AsString.TrimEnd(' '))]);
        }

        if (input.IsStringArray)
        {
            JgsValue[] texts = Array.ConvertAll(input.BoxedElements(),
                static e => JgsValue.Str(IsMissingText(e.AsString) ? string.Empty : e.AsString));
            JgsValue cell = JgsValue.Cell(texts);
            cell.Reshape(input.Rows, input.Cols);
            return cell;
        }

        if (input.Type == JgsType.Cell && Array.TrueForAll(input.AsCell, static e => e.Type == JgsType.String))
        {
            return input;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:cellstr:MustContainText",
            "Input must be a string array, character array, or cell array of character vectors.");
    }

    // --- strcat -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>strcat(s1, …, sN)</c>: text joined end to end. Char arguments lose their trailing
    /// whitespace and cell and string arguments keep it; numbers are character codes; containers pair
    /// element by element with a one-element container expanding; and the answer is a string array if
    /// any argument was one, else a cell if any was, else char — a char matrix when there are rows.
    /// </summary>
    private static JgsValue StrCat(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        bool anyString = false;
        bool anyCell = false;
        var sides = new CompareSide[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            JgsValue arg = args[i];
            if (arg.IsStringArray)
            {
                anyString = true;
                sides[i] = new(
                    Array.ConvertAll(arg.BoxedElements(), static e => IsMissingText(e.AsString) ? null : e.AsString),
                    arg.Rows, arg.Cols);
            }
            else if (arg.Type == JgsType.Cell)
            {
                anyCell = true;

                // A number in a cell is the character it codes for: strcat({'a', 1}, 'b') is
                // {['a' char(1)], 'b'} (measured).
                sides[i] = new(
                    Array.ConvertAll(arg.AsCell, static e =>
                        e.Type == JgsType.String ? e.AsString
                        : IsStringScalar(e) ? TextOf(e)
                        : e.Type is JgsType.Number ? ((char)(int)e.AsNumber).ToString()
                        : string.Empty),
                    arg.Rows, arg.Cols);
            }
            else if (arg.IsCharMatrix)
            {
                string[] lines = arg.CharMatrixRows();
                sides[i] = new(Array.ConvertAll(lines, r => (string?)r.TrimEnd(TextWhitespace)), lines.Length, 1);
            }
            else if (arg.Type == JgsType.String)
            {
                sides[i] = new([arg.AsString.TrimEnd(TextWhitespace)], 1, 1);
            }
            else if (arg.Type == JgsType.Number
                     || (arg.Type == JgsType.Array && !IsLogicalValue(arg) && !arg.IsNd
                         && Array.TrueForAll(arg.BoxedElements(), static e => e.Type == JgsType.Number)))
            {
                sides[i] = CodesAsText(arg);
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:strcat:InvalidInputType",
                    "Inputs must be character arrays, cell arrays of character vectors, string arrays, or numeric arrays.");
            }
        }

        // Numbers stand for characters only beside char: strcat("a", 98) and strcat({'a'}, 98) are
        // refused (measured).
        if ((anyString || anyCell) && args.Any(static a => a.Type == JgsType.Number
                || (a.Type == JgsType.Array && !a.IsStringArray && !a.IsCharMatrix)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:strcat:NumericInput",
                "strcat: a numeric input can only be combined with character arrays.");
        }

        // The pairing shape: every container with more than one element must agree.
        int rows = 1;
        int cols = 1;
        bool shaped = false;
        foreach (CompareSide side in sides)
        {
            if (side.Scalar)
            {
                continue;
            }

            if (!shaped)
            {
                (rows, cols, shaped) = (side.Rows, side.Cols, true);
            }
            else if (side.Rows != rows || side.Cols != cols)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:strcat:InvalidInputSize",
                    "All the inputs must be the same size or scalars.");
            }
        }

        int count = rows * cols;
        var texts = new string[count];
        var built = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            built.Clear();
            bool missing = false;
            foreach (CompareSide side in sides)
            {
                string? piece = side.Texts[side.Scalar ? 0 : i];
                if (piece is null)
                {
                    missing = true;
                    break;
                }

                built.Append(piece);
            }

            texts[i] = missing ? MissingSentinel : built.ToString();
        }

        if (anyString)
        {
            return JgsValue.StringArray(Array.ConvertAll(texts, JgsValue.Str), rows, cols);
        }

        if (anyCell)
        {
            JgsValue cell = JgsValue.Cell(Array.ConvertAll(texts, JgsValue.Str));
            cell.Reshape(rows, cols);
            return cell;
        }

        return count == 1 ? JgsValue.Str(texts[0]) : PadIntoCharMatrix(texts);
    }

    /// <summary>Numbers read as the characters they code for: a row is one piece, a matrix one per row.</summary>
    private static CompareSide CodesAsText(JgsValue numbers)
    {
        if (numbers.Type == JgsType.Number)
        {
            return new([((char)(int)numbers.AsNumber).ToString().TrimEnd(TextWhitespace)], 1, 1);
        }

        JgsValue[] elements = numbers.BoxedElements();
        if (elements.Length == 0)
        {
            return new([string.Empty], 1, 1);
        }

        int height = numbers.Rows > 1 && numbers.Cols > 1 ? numbers.Rows : 1;
        int width = elements.Length / height;
        var rows = new string?[height];
        for (int r = 0; r < height; r++)
        {
            var row = new StringBuilder(width);
            for (int c = 0; c < width; c++)
            {
                row.Append((char)(int)elements[r + (c * height)].AsNumber);
            }

            rows[r] = row.ToString().TrimEnd(TextWhitespace);
        }

        return new(rows, height, 1);
    }

    // --- pad and strip ------------------------------------------------------------------------------

    /// <summary>The side a word names for <c>pad</c> and <c>strip</c>, in any case, or null.</summary>
    private static string? SideWord(JgsValue value)
    {
        if (!IsTextScalar(value))
        {
            return null;
        }

        string word = TextOf(value).ToLowerInvariant();
        return word is "left" or "right" or "both" ? word : null;
    }

    /// <summary>The subject of an editing verb, or MATLAB's refusal of what is not text.</summary>
    private static TextBundle EditingSubject(string name, JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix || !TryReadText(value, out TextBundle subject))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                $"{name}: first argument must be a string array, character vector, or cell array of character vectors.");
        }

        return subject;
    }

    /// <summary>
    /// <c>pad(str)</c>, <c>pad(str, n)</c>, <c>pad(str, side)</c>, <c>pad(str, n, side)</c>,
    /// <c>pad(str, n, padChar)</c>, <c>pad(str, side, padChar)</c> and
    /// <c>pad(str, n, side, padChar)</c>. Without n every element is padded to the longest; 'both'
    /// puts the odd character on the right; a missing string stays missing.
    /// </summary>
    private static JgsValue Padded(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("pad", args, 1, 4, line, col);
        TextBundle subject = EditingSubject("pad", args[0], line, col);

        int at = 1;
        int? width = null;
        if (at < args.Count && !IsTextScalar(args[at]) && args[at].Type != JgsType.Cell)
        {
            if (!IsOneNumber(args[at]) || OneNumber(args[at]) < 0 || OneNumber(args[at]) != Math.Floor(OneNumber(args[at])))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeNonNegativeInteger",
                    "pad: the length must be a non-negative integer scalar.");
            }

            width = (int)OneNumber(args[at]);
            at++;
        }

        string side = "right";
        if (at < args.Count && SideWord(args[at]) is { } named)
        {
            side = named;
            at++;
        }

        char padChar = ' ';
        if (at < args.Count)
        {
            if (!IsTextScalar(args[at]) || TextOf(args[at]).Length != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeSingleCharacter",
                    "pad: the pad character must be a single character.");
            }

            padChar = TextOf(args[at])[0];
            at++;
        }

        if (at < args.Count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        bool strings = subject.Kind == TextKind.String;
        int target = width ?? 0;
        if (width is null)
        {
            foreach (string text in subject.Texts)
            {
                if (!(strings && IsMissingText(text)))
                {
                    target = Math.Max(target, text.Length);
                }
            }
        }

        var padded = new string[subject.Texts.Length];
        for (int i = 0; i < padded.Length; i++)
        {
            string text = subject.Texts[i];
            if (strings && IsMissingText(text))
            {
                padded[i] = text;
                continue;
            }

            int extra = target - text.Length;
            padded[i] = extra <= 0 ? text : side switch
            {
                "left" => new string(padChar, extra) + text,
                "both" => new string(padChar, extra / 2) + text + new string(padChar, extra - (extra / 2)),
                _ => text + new string(padChar, extra),
            };
        }

        return RebuildLike(subject, padded);
    }

    /// <summary>
    /// <c>strip(str)</c>, <c>strip(str, side)</c>, <c>strip(str, stripChar)</c> and
    /// <c>strip(str, side, stripChar)</c>: whitespace, or the one character named, taken off the
    /// side asked for. A missing string stays missing.
    /// </summary>
    private static JgsValue Stripped(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("strip", args, 1, 3, line, col);
        TextBundle subject = EditingSubject("strip", args[0], line, col);

        int at = 1;
        string side = "both";
        if (at < args.Count && SideWord(args[at]) is { } named)
        {
            side = named;
            at++;
        }

        char[] gone = TextWhitespace;
        if (at < args.Count)
        {
            if (!IsTextScalar(args[at]) || TextOf(args[at]).Length != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeSingleCharacter",
                    "strip: the character to strip must be a single character.");
            }

            gone = [TextOf(args[at])[0]];
            at++;
        }

        if (at < args.Count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        var stripped = new string[subject.Texts.Length];
        for (int i = 0; i < stripped.Length; i++)
        {
            string text = subject.Texts[i];
            stripped[i] = subject.Kind == TextKind.String && IsMissingText(text) ? text : side switch
            {
                "left" => text.TrimStart(gone),
                "right" => text.TrimEnd(gone),
                _ => text.Trim(gone),
            };
        }

        return RebuildLike(subject, stripped);
    }

    // --- deblank and strtrim ------------------------------------------------------------------------

    /// <summary>
    /// The body of <c>deblank</c> (trailing only, NUL included, numbers passed through) and
    /// <c>strtrim</c> (both ends). A cell is answered element by element and may hold strings beside
    /// char; a char matrix loses whole blank columns; a missing string stays missing.
    /// </summary>
    private static JgsValue Trimmed(
        string name, JgsValue value, char[] blanks, bool leading, bool passNumbers, int line, int col)
    {
        string Trim(string text) => leading ? text.Trim(blanks) : text.TrimEnd(blanks);

        if (value.IsStringArray)
        {
            JgsValue[] texts = Array.ConvertAll(value.BoxedElements(),
                e => JgsValue.Str(IsMissingText(e.AsString) ? e.AsString : Trim(e.AsString)));
            return JgsValue.StringArray(texts, value.Rows, value.Cols);
        }

        if (value.Type == JgsType.String)
        {
            return JgsValue.Str(Trim(value.AsString));
        }

        if (value.IsCharMatrix)
        {
            string[] rows = value.CharMatrixRows();
            int width = rows.Length == 0 ? 0 : rows[0].Length;
            bool BlankColumn(int c) => Array.TrueForAll(rows, r => Array.IndexOf(blanks, r[c]) >= 0);

            int end = width;
            while (end > 0 && BlankColumn(end - 1))
            {
                end--;
            }

            int start = 0;
            while (leading && start < end && BlankColumn(start))
            {
                start++;
            }

            return JgsValue.CharMatrix(Array.ConvertAll(rows, r => r[start..end]));
        }

        if (value.Type == JgsType.Cell)
        {
            JgsValue[] cells = value.AsCell;
            var trimmed = new JgsValue[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                JgsValue element = cells[i];
                trimmed[i] = element.Type == JgsType.String ? JgsValue.Str(Trim(element.AsString))
                    : IsStringScalar(element) ? Trimmed(name, element, blanks, leading, passNumbers, line, col)
                    : passNumbers && (element.Type is JgsType.Number or JgsType.Bool || (element.Type == JgsType.Array && !element.IsStringArray)) ? element
                    : throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                        $"{name}: a cell must hold character vectors or strings.");
            }

            JgsValue cell = JgsValue.Cell(trimmed);
            cell.Reshape(value.Rows, value.Cols);
            return cell;
        }

        if (passNumbers && (value.Type is JgsType.Number or JgsType.Bool || value.Type == JgsType.Array))
        {
            return value;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
            $"{name}: the argument must be a string array, character vector, or cell array of character vectors.");
    }

    // --- split --------------------------------------------------------------------------------------

    /// <summary>
    /// <c>split(str)</c>, <c>split(str, delimiter)</c>, <c>split(str, delimiter, dim)</c> and the
    /// two-output form that also answers the delimiters cut on. Several delimiters may be given and
    /// the longest match at a position wins; an empty delimiter cuts between every character. The
    /// pieces are laid along <c>dim</c>, which defaults to the first dimension after the last one
    /// the subject has more than one of; an element that yields a different count is refused by
    /// MATLAB's own sentence.
    /// </summary>
    private static JgsValue[] SplitText2(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, BuiltinFunction? legacy, int wanted, int line, int col)
    {
        ArityRange("split", args, 1, 3, line, col);

        // The calendar-duration split and the JGS flat list both keep the definition they had.
        if (!dialect.IsMatlab || (args.Count == 2 && IsCalendarDuration(args[0])))
        {
            return legacy is null
                ? throw new JgsRuntimeException(line, col, "split is not available.")
                : wanted > 1 ? legacy.CallMultiple(args, wanted, line, col) : [legacy.Call(args, line, col)];
        }

        if (args[0].IsCharMatrix || !TryReadText(args[0], out TextBundle subject))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                "First argument must be text.");
        }

        string[]? delimiters = null;
        if (args.Count >= 2)
        {
            if (args[1].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                    "Delimiter must be a text or pattern array.");
            }

            delimiters = TextElementsOf(args[1]) ?? throw new JgsRuntimeException(line, col,
                args[1].Type == JgsType.Cell
                    ? "Cell must be a cell array of character vectors."
                    : "Delimiter must be a text or pattern array.");
        }

        int? dim = null;
        if (args.Count == 3)
        {
            if (!IsOneNumber(args[2]) || OneNumber(args[2]) < 1 || OneNumber(args[2]) != Math.Floor(OneNumber(args[2])))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBePositiveIntegerScalar",
                    "Dimension argument must be a positive integer scalar within indexing range.");
            }

            dim = (int)OneNumber(args[2]);
        }

        var pieces = new string[subject.Texts.Length][];
        var cuts = new string[subject.Texts.Length][];
        for (int i = 0; i < pieces.Length; i++)
        {
            (pieces[i], cuts[i]) = delimiters is null
                ? SplitOnWhitespace(subject.Texts[i])
                : SplitOnDelimiters(subject.Texts[i], delimiters);
        }

        TextKind kind = subject.Kind == TextKind.Char ? TextKind.Cell : subject.Kind;
        JgsValue first = ArrangePieces("split", kind, subject, pieces, "delimiters", 1, dim, line, col);
        return wanted <= 1
            ? [first]
            : [first, ArrangePieces("split", kind, subject, cuts, "matches", 0, dim, line, col)];
    }

    /// <summary>MATLAB's default split: runs of whitespace, with the empty pieces they leave dropped.</summary>
    private static (string[] Pieces, string[] Cuts) SplitOnWhitespace(string text)
    {
        var pieces = new List<string>();
        var cuts = new List<string>();
        int at = 0;
        while (at < text.Length)
        {
            int start = at;
            while (at < text.Length && Array.IndexOf(TextWhitespace, text[at]) < 0)
            {
                at++;
            }

            if (at > start)
            {
                pieces.Add(text[start..at]);
            }

            int gap = at;
            while (at < text.Length && Array.IndexOf(TextWhitespace, text[at]) >= 0)
            {
                at++;
            }

            // A run of whitespace is a cut only between two pieces.
            if (at > gap && at < text.Length && pieces.Count > 0)
            {
                cuts.Add(text[gap..at]);
            }
        }

        if (pieces.Count == 0)
        {
            pieces.Add(string.Empty);
        }

        return (pieces.ToArray(), cuts.ToArray());
    }

    /// <summary>Splits on the longest delimiter matching at each position; an empty one cuts everywhere.</summary>
    private static (string[] Pieces, string[] Cuts) SplitOnDelimiters(string text, string[] delimiters)
    {
        if (Array.Exists(delimiters, static d => d.Length == 0))
        {
            if (text.Length == 0)
            {
                return ([string.Empty], []);
            }

            var every = new string[text.Length + 2];
            every[0] = string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                every[i + 1] = text[i].ToString();
            }

            every[^1] = string.Empty;
            var nothing = new string[text.Length + 1];
            Array.Fill(nothing, string.Empty);
            return (every, nothing);
        }

        var pieces = new List<string>();
        var cuts = new List<string>();
        int start = 0;
        int at = 0;
        while (at < text.Length)
        {
            string? matched = null;
            foreach (string delimiter in delimiters)
            {
                if (at + delimiter.Length <= text.Length
                    && string.CompareOrdinal(text, at, delimiter, 0, delimiter.Length) == 0
                    && (matched is null || delimiter.Length > matched.Length))
                {
                    matched = delimiter;
                }
            }

            if (matched is null)
            {
                at++;
                continue;
            }

            pieces.Add(text[start..at]);
            cuts.Add(matched);
            at += matched.Length;
            start = at;
        }

        pieces.Add(text[start..]);
        return (pieces.ToArray(), cuts.ToArray());
    }

    /// <summary>
    /// Lays each element's pieces along a dimension. The default is the first dimension after the last
    /// one the subject has more than one of; a named dimension the subject has more than one of moves
    /// the subject's elements to that first free dimension instead. Anything three-dimensional is
    /// refused, since nothing here holds one.
    /// </summary>
    private static JgsValue ArrangePieces(
        string name, TextKind kind, TextBundle subject, string[][] pieces, string noun, int less, int? dim, int line, int col)
    {
        int count = pieces.Length == 0 ? 0 : pieces[0].Length;
        for (int i = 1; i < pieces.Length; i++)
        {
            if (pieces[i].Length != count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustHaveSameNumberOf",
                    $"Element {i + 1} of the text contains {pieces[i].Length - less} {noun} while "
                    + $"the previous elements have {count - less}. All elements must contain the "
                    + $"same number of {noun}.");
            }
        }

        if (pieces.Length == 0)
        {
            return RebuildText(kind, [], subject.Rows, subject.Cols);
        }

        int rows = subject.Rows;
        int cols = subject.Cols;
        int free = rows != 1 && cols != 1 ? 3 : cols != 1 ? 3 : rows != 1 ? 2 : 1;
        int along = dim ?? free;
        int elements = pieces.Length;

        // Two layouts a 2-D answer can have: pieces down with the elements across, or elements down
        // with the pieces across. Everything else is three-dimensional.
        bool piecesDown;
        if (along >= 3)
        {
            if (count != 1)
            {
                throw ThreeDimensional(name, rows, cols, count, noun, line, col);
            }

            return RebuildText(kind, Array.ConvertAll(pieces, static p => p[0]), rows, cols);
        }

        if (rows != 1 && cols != 1)
        {
            throw ThreeDimensional(name, rows, cols, count, noun, line, col);
        }

        if (along == 1)
        {
            // A row of text with its pieces laid down would be count-by-1-by-C.
            if (cols != 1 && elements != 1)
            {
                throw ThreeDimensional(name, rows, cols, count, noun, line, col);
            }

            piecesDown = true;
        }
        else
        {
            // A row of text with its pieces laid across would be 1-by-C-by-count.
            if (cols != 1 && elements != 1)
            {
                throw ThreeDimensional(name, rows, cols, count, noun, line, col);
            }

            piecesDown = false;
        }

        int outRows = piecesDown ? count : elements;
        int outCols = piecesDown ? elements : count;
        var flat = new string[count * elements];
        for (int e = 0; e < elements; e++)
        {
            for (int p = 0; p < count; p++)
            {
                flat[piecesDown ? p + (e * count) : e + (p * elements)] = pieces[e][p];
            }
        }

        return RebuildText(kind, flat, outRows, outCols);
    }

    private static JgsRuntimeException ThreeDimensional(string name, int rows, int cols, int count, string noun, int line, int col) =>
        new(line, col,
            $"{name}: {rows}-by-{cols} text with {count} {noun} each would answer a "
            + "three-dimensional array, which is not supported — pass a column instead.");
}
