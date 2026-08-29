using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The string family MATLAB writes in MATLAB (M104): <c>append</c>, the between-verbs
/// <c>eraseBetween</c> and <c>replaceBetween</c>, <c>extract</c>, <c>splitlines</c>,
/// <c>strtok</c>, <c>strjust</c>, the char-matrix builders <c>strvcat</c> and <c>str2mat</c>,
/// <c>strmatch</c>, <c>isStringScalar</c>, and the bit-pattern pair <c>hex2num</c>/<c>num2hex</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every name here answers text of the kind it was handed, and that rule is one type — a
/// <see cref="TextBundle"/> holds what kind of container arrived, its shape, and the pieces inside
/// it, so no verb has to decide the question a second time. The three kinds are the three MATLAB
/// has: a char row, a string array, and a cell of char rows.
/// </para>
/// <para>
/// The one-to-many verbs (<c>splitlines</c>, <c>extract</c>) share a second rule, measured rather
/// than read: every element must yield the same number of pieces, and the pieces go along a new
/// trailing dimension — so one piece of text answers a column, a column of text answers a matrix,
/// and MATLAB's three-dimensional answer for anything wider is refused by name here.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>What kind of container a piece of text arrived in, and must leave in.</summary>
    private enum TextKind
    {
        /// <summary>A char row, or the char matrix a stack of them makes.</summary>
        Char,

        /// <summary>A string array, including the 1-by-1 a double-quoted literal means.</summary>
        String,

        /// <summary>A cell array of char rows.</summary>
        Cell,
    }

    /// <summary>The pieces of text a call was handed, the kind they came in, and their shape.</summary>
    private readonly record struct TextBundle(TextKind Kind, string[] Texts, int Rows, int Cols)
    {
        /// <summary>Whether this is one piece of text — a char row, or a one-element container.</summary>
        public bool Scalar => Texts.Length == 1;
    }

    /// <summary>The words MATLAB will not let a variable be called, as <c>iskeyword</c> lists them.</summary>
    private static readonly string[] MatlabKeywordList =
    [
        "break", "case", "catch", "classdef", "continue", "else", "elseif", "end", "for", "function",
        "global", "if", "otherwise", "parfor", "persistent", "return", "spmd", "switch", "try", "while",
    ];

    /// <summary>The characters <c>strtok</c> breaks on when no delimiters are named: 9-13 and space.</summary>
    private static readonly char[] DefaultTokenDelimiters =
        ['\t', '\n', '\v', '\f', '\r', ' '];

    /// <summary>Registers the M104 string family into <paramref name="env"/>.</summary>
    internal static void RegisterTextPartBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                MultiOutput = multi,

                // Every one of these reads the container it was handed rather than the char row the
                // older text builtins expect, so none of them may be demoted on the way in.
                KeepsStringArguments = true,
            }));

        Define("append", Appended);
        Define("eraseBetween", (args, line, col) => Between("eraseBetween", args, line, col));
        Define("replaceBetween", (args, line, col) => Between("replaceBetween", args, line, col));
        Define("extract", Extracted);
        Define("splitlines", SplitLines);
        Define("strtok", (args, line, col) => Tokenized(args, 1, line, col)[0], Tokenized);
        Define("strjust", Justified);
        Define("strvcat", (args, line, col) => Stacked("strvcat", args, line, col));
        Define("str2mat", (args, line, col) => Stacked("str2mat", args, line, col));
        Define("strmatch", Matched);
        Define("isStringScalar", (args, line, col) =>
        {
            Arity("isStringScalar", args, 1, line, col);
            return JgsValue.Bool(IsStringScalar(args[0]));
        });
        Define("hex2num", HexToNum);
        Define("num2hex", NumToHex);
        Define("isvarname", (args, line, col) =>
        {
            ArityRange("isvarname", args, 0, 1, line, col);
            return JgsValue.Bool(args.Count == 1
                && IsTextScalar(args[0])
                && IsValidVariableName(TextOf(args[0])));
        });
    }

    // --- reading and rebuilding a container of text -------------------------------------------

    /// <summary>
    /// Reads a value as text, or answers false. A char row is one piece, a char matrix is its rows,
    /// a string array is its elements, and a cell of char rows is its cells.
    /// </summary>
    private static bool TryReadText(JgsValue value, out TextBundle bundle)
    {
        bundle = default;

        if (value.Type == JgsType.String)
        {
            bundle = new(TextKind.Char, [value.AsString], 1, 1);
            return true;
        }

        if (value.IsStringArray)
        {
            bundle = new(
                TextKind.String,
                Array.ConvertAll(value.BoxedElements(), static e => e.AsString),
                value.Rows,
                value.Cols);
            return true;
        }

        if (value.Type == JgsType.Cell && Array.TrueForAll(value.AsCell, static e => e.Type == JgsType.String))
        {
            bundle = new(
                TextKind.Cell,
                Array.ConvertAll(value.AsCell, static e => e.AsString),
                value.Rows,
                value.Cols);
            return true;
        }

        // A char matrix is an array of equal-length char rows, which is how a stack of strings is
        // held here; it reads as its rows, exactly as MATLAB's does.
        if (value.Type == JgsType.Array && value.ArrayLength > 0
            && Array.TrueForAll(value.BoxedElements(), static e => e.Type == JgsType.String))
        {
            bundle = new(
                TextKind.Char,
                Array.ConvertAll(value.BoxedElements(), static e => e.AsString),
                value.Rows,
                value.Cols);
            return true;
        }

        return false;
    }

    /// <summary>Reads a value as text, or raises MATLAB's own refusal with <paramref name="message"/>.</summary>
    private static TextBundle ReadText(JgsValue value, string message, int line, int col) =>
        TryReadText(value, out TextBundle bundle)
            ? bundle
            : throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString", message);

    /// <summary>
    /// Puts answers back into the container they came from: one string per element, the kind and the
    /// shape unchanged. A single char row stays a char row; several become a char matrix.
    /// </summary>
    private static JgsValue RebuildText(TextKind kind, string[] texts, int rows, int cols)
    {
        if (kind == TextKind.Char)
        {
            return texts.Length == 1 ? JgsValue.Str(texts[0]) : PadIntoCharMatrix(texts);
        }

        var boxed = Array.ConvertAll(texts, JgsValue.Str);
        if (kind == TextKind.String)
        {
            return JgsValue.StringArray(boxed, rows, cols);
        }

        JgsValue cell = JgsValue.Cell(boxed);
        cell.Reshape(rows, cols);
        return cell;
    }

    /// <summary>Rebuilds an answer shaped exactly like the bundle it was computed from.</summary>
    private static JgsValue RebuildLike(TextBundle bundle, string[] texts) =>
        RebuildText(bundle.Kind, texts, bundle.Rows, bundle.Cols);

    /// <summary>
    /// The shape a one-to-many verb answers with. Every element must yield the same number of
    /// pieces — MATLAB says so by name — and the pieces go along a new trailing dimension: one piece
    /// of text answers a column, a column of text answers rows-by-pieces.
    /// </summary>
    private static JgsValue SpreadPieces(
        string name, TextBundle bundle, string[][] pieces, string noun, int line, int col)
    {
        // A split is counted by its delimiters and an extraction by its matches, which is the same
        // scan reported one apart — MATLAB's sentence says 'contains 0 delimiters' where the
        // element yielded one piece.
        int less = noun == "delimiters" ? 1 : 0;
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

        // The kind a one-to-many verb answers with is the container's, except that a char row has no
        // container of its own and so answers a cell, exactly as MATLAB's does.
        TextKind kind = bundle.Kind == TextKind.Char ? TextKind.Cell : bundle.Kind;

        if (bundle.Scalar)
        {
            return RebuildText(kind, pieces.Length == 0 ? [] : pieces[0], count, 1);
        }

        if (bundle.Cols != 1 && count != 1)
        {
            // MATLAB answers a rows-by-cols-by-pieces array here; nothing in this build holds a
            // three-dimensional container of text, so the shape is refused rather than flattened.
            throw new JgsRuntimeException(line, col,
                $"{name}: {bundle.Rows}-by-{bundle.Cols} text with {count} {noun} each would answer a "
                + "three-dimensional array, which is not supported — pass a column instead.");
        }

        // A column spreads its pieces across the columns of the answer, which is column-major
        // element (r, p) at r + (p * rows).
        var flat = new string[bundle.Texts.Length * count];
        for (int r = 0; r < pieces.Length; r++)
        {
            for (int p = 0; p < count; p++)
            {
                flat[r + (p * pieces.Length)] = pieces[r][p];
            }
        }

        return count == 1
            ? RebuildText(kind, flat, bundle.Rows, bundle.Cols)
            : RebuildText(kind, flat, bundle.Rows, count);
    }

    // --- append -------------------------------------------------------------------------------

    /// <summary>
    /// <c>append(s1, …, sN)</c>: text joined with nothing between and nothing trimmed, which is the
    /// whole of the difference from <c>strcat</c>. The answer is a string if any argument was one, a
    /// cell if any was a cell, and a char row otherwise; the arguments expand against each other.
    /// </summary>
    private static JgsValue Appended(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        var bundles = new TextBundle[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            // A char matrix is not text to append: MATLAB refuses it by the same message a number
            // gets, because there is no one string in it to join to.
            if (!TryReadText(args[i], out bundles[i])
                || (bundles[i].Kind == TextKind.Char && bundles[i].Texts.Length > 1))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                    "Input must be text.");
            }
        }

        // A string wins over a cell and a cell over a char, which is the same precedence the
        // concatenation operator uses.
        TextKind kind = bundles.Any(static b => b.Kind == TextKind.String) ? TextKind.String
            : bundles.Any(static b => b.Kind == TextKind.Cell) ? TextKind.Cell
            : TextKind.Char;

        int rows = 1;
        int cols = 1;
        foreach (TextBundle bundle in bundles)
        {
            if (bundle.Scalar)
            {
                continue;
            }

            rows = Expand("append", rows, bundle.Rows, line, col);
            cols = Expand("append", cols, bundle.Cols, line, col);
        }

        var texts = new string[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var piece = new System.Text.StringBuilder();
                foreach (TextBundle bundle in bundles)
                {
                    piece.Append(bundle.Texts[bundle.Scalar ? 0 : ElementIndex(bundle, r, c)]);
                }

                texts[r + (c * rows)] = piece.ToString();
            }
        }

        return RebuildText(kind, texts, rows, cols);
    }

    /// <summary>The linear index of element (r, c) of a bundle, with a singleton dimension expanded.</summary>
    private static int ElementIndex(TextBundle bundle, int r, int c) =>
        (bundle.Rows == 1 ? 0 : r) + ((bundle.Cols == 1 ? 0 : c) * bundle.Rows);

    /// <summary>One dimension of an implicit expansion, or the refusal MATLAB makes of a mismatch.</summary>
    private static int Expand(string name, int have, int want, int line, int col) => have switch
    {
        _ when have == want => have,
        1 => want,
        _ when want == 1 => have,
        _ => throw new JgsRuntimeException(line, col, "MATLAB:string:InvalidArgumentSize",
            $"{name}: arrays have incompatible sizes."),
    };

    // --- eraseBetween / replaceBetween --------------------------------------------------------

    /// <summary>
    /// The two between-verbs, which differ only in what they write into the span they found.
    /// A pair of markers bounds the span exclusively by default; a pair of positions bounds it
    /// inclusively. <c>'Boundaries'</c> names the other reading in each case.
    /// </summary>
    private static JgsValue Between(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool replacing = name == "replaceBetween";
        int least = replacing ? 4 : 3;
        if (args.Count < least)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        TextBundle subject = ReadText(args[0], "First argument must be text.", line, col);
        bool markers = IsTextArgument(args[1]);
        bool inclusive = !markers;

        for (int i = least; i < args.Count; i += 2)
        {
            string word = IsTextArgument(args[i]) ? TextOf2(args[i]) : string.Empty;
            if (word.Length == 0
                || !"Boundaries".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:UnrecognizedParameterName",
                    $"Unrecognized parameter name '{word}'. Parameter name must be 'Boundaries'.");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:UnrecognizedParameterValue",
                    "'Boundaries' option must be 'Exclusive' or 'Inclusive'.");
            }

            string value = IsTextArgument(args[i + 1]) ? TextOf2(args[i + 1]) : string.Empty;
            inclusive = value switch
            {
                _ when "inclusive".StartsWith(value, StringComparison.OrdinalIgnoreCase) && value.Length > 0 => true,
                _ when "exclusive".StartsWith(value, StringComparison.OrdinalIgnoreCase) && value.Length > 0 => false,
                _ => throw new JgsRuntimeException(line, col, "MATLAB:string:UnrecognizedParameterValue",
                    "'Boundaries' option must be 'Exclusive' or 'Inclusive'."),
            };
        }

        string[] answers = new string[subject.Texts.Length];
        if (markers)
        {
            TextBundle open = ReadText(args[1], "Second argument must be text.", line, col);
            TextBundle close = ReadText(args[2], "Third argument must be text.", line, col);
            TextBundle? written = replacing
                ? ReadText(args[3], "Fourth argument must be text.", line, col)
                : null;

            for (int i = 0; i < answers.Length; i++)
            {
                answers[i] = BetweenMarkers(
                    subject.Texts[i],
                    Pick(open, i),
                    Pick(close, i),
                    written is { } w ? Pick(w, i) : null,
                    inclusive);
            }
        }
        else
        {
            double[] from = PositionsFor(name, args[1], subject, line, col);
            double[] to = PositionsFor(name, args[2], subject, line, col);
            TextBundle? written = replacing
                ? ReadText(args[3], "Fourth argument must be text.", line, col)
                : null;

            for (int i = 0; i < answers.Length; i++)
            {
                answers[i] = BetweenPositions(
                    name,
                    subject.Texts[i],
                    from[i],
                    to[i],
                    written is { } w ? Pick(w, i) : null,
                    inclusive,
                    i,
                    line,
                    col);
            }
        }

        return RebuildLike(subject, answers);
    }

    /// <summary>Whether an argument is text rather than the numeric positions the other overload takes.</summary>
    private static bool IsTextArgument(JgsValue value) =>
        value.Type == JgsType.String || value.IsStringArray
        || (value.Type == JgsType.Cell && Array.TrueForAll(value.AsCell, static e => e.Type == JgsType.String));

    /// <summary>The text of a one-piece argument, whatever container it arrived in.</summary>
    private static string TextOf2(JgsValue value) =>
        value.Type == JgsType.String ? value.AsString
        : value.Type == JgsType.Cell ? value.AsCell[0].AsString
        : value.ElementAt(0).AsString;

    /// <summary>Element <paramref name="i"/> of a bundle, with one piece of text standing for all.</summary>
    private static string Pick(TextBundle bundle, int i) => bundle.Texts[bundle.Scalar ? 0 : i];

    /// <summary>
    /// A numeric bound read per element: one number stands for all, otherwise there must be exactly
    /// as many as there are pieces of text.
    /// </summary>
    private static double[] PositionsFor(
        string name, JgsValue value, TextBundle subject, int line, int col)
    {
        double[] numbers = value.Type == JgsType.Number || value.Type == JgsType.Bool
            ? [value.AsNumber]
            : value.Type == JgsType.Array
                ? Array.ConvertAll(value.BoxedElements(), static e => e.AsNumber)
                : throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                    "Positions must be numeric.");

        if (numbers.Length == 1)
        {
            var spread = new double[subject.Texts.Length];
            Array.Fill(spread, numbers[0]);
            return spread;
        }

        return numbers.Length == subject.Texts.Length
            ? numbers
            : throw new JgsRuntimeException(line, col, "MATLAB:string:InvalidArgumentSize",
                "Dimensions of position argument must match the dimensions of the text argument or be 1.");
    }

    /// <summary>
    /// One piece of text with every span between a start marker and the end marker after it either
    /// erased or replaced. The scan resumes after the end marker, so markers never nest.
    /// </summary>
    private static string BetweenMarkers(
        string text, string open, string close, string? written, bool inclusive)
    {
        if (open.Length == 0 || close.Length == 0)
        {
            return text;
        }

        var built = new System.Text.StringBuilder();
        int at = 0;
        while (at <= text.Length)
        {
            int start = text.IndexOf(open, at, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            int inner = start + open.Length;
            int stop = inner > text.Length ? -1 : text.IndexOf(close, inner, StringComparison.Ordinal);
            if (stop < 0)
            {
                break;
            }

            built.Append(text, at, (inclusive ? start : inner) - at);
            built.Append(written ?? string.Empty);

            // The scan always resumes past the end marker, whichever side of the span it fell on:
            // MATLAB's spans never nest, so eraseBetween('aXbXcXd', 'X', 'X') leaves 'aXXcXd' and
            // not 'aXXXd' — the closing X cannot open the next span.
            if (!inclusive)
            {
                built.Append(text, stop, close.Length);
            }

            at = stop + close.Length;
        }

        built.Append(text, at, text.Length - at);
        return built.ToString();
    }

    /// <summary>
    /// One piece of text with the span between two positions erased or replaced. Positions bound the
    /// span inclusively unless <c>'Boundaries', 'exclusive'</c> said otherwise.
    /// </summary>
    private static string BetweenPositions(
        string name, string text, double from, double to, string? written, bool inclusive,
        int element, int line, int col)
    {
        if (from != Math.Floor(from) || to != Math.Floor(to) || from < 1 || to < 0
            || double.IsNaN(from) || double.IsNaN(to))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:PositionMustBePositiveInteger",
                "Numeric position must be a positive integer.");
        }

        int start = (int)from;
        int stop = (int)to;
        if (start > text.Length + 1 || stop > text.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:PositionOutOfRange",
                $"Numeric value exceeds the number of characters in element {element + 1}.");
        }

        int cutFrom = inclusive ? start - 1 : start;
        int cutTo = inclusive ? stop : stop - 1;
        if (cutTo < cutFrom)
        {
            cutTo = cutFrom;
        }

        cutFrom = Math.Clamp(cutFrom, 0, text.Length);
        cutTo = Math.Clamp(cutTo, cutFrom, text.Length);
        return string.Concat(text.AsSpan(0, cutFrom), written ?? string.Empty, text.AsSpan(cutTo));
    }

    // --- extract ------------------------------------------------------------------------------

    /// <summary>
    /// <c>extract(str, pat)</c> answers every occurrence of a piece of text, and
    /// <c>extract(str, pos)</c> the one character at a position. Both spread their answers the way
    /// <see cref="SpreadPieces"/> spreads them.
    /// </summary>
    private static JgsValue Extracted(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        Arity("extract", args, 2, line, col);
        TextBundle subject = ReadText(args[0], "First argument must be text.", line, col);

        if (!IsTextArgument(args[1]))
        {
            double[] at = PositionsFor("extract", args[1], subject, line, col);
            var single = new string[subject.Texts.Length][];
            for (int i = 0; i < single.Length; i++)
            {
                if (at[i] != Math.Floor(at[i]) || at[i] < 1 || double.IsNaN(at[i]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:string:PositionMustBePositiveInteger",
                        "Numeric position must be a positive integer.");
                }

                if (at[i] > subject.Texts[i].Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:string:PositionOutOfRange",
                        $"Numeric value exceeds the number of characters in element {i + 1}.");
                }

                single[i] = [subject.Texts[i][(int)at[i] - 1].ToString()];
            }

            return SpreadPieces("extract", subject, single, "matches", line, col);
        }

        TextBundle pattern = ReadText(args[1], "Second argument must be text.", line, col);
        var found = new string[subject.Texts.Length][];
        for (int i = 0; i < found.Length; i++)
        {
            found[i] = [.. Occurrences(subject.Texts[i], Pick(pattern, i))];
        }

        return SpreadPieces("extract", subject, found, "matches", line, col);
    }

    /// <summary>
    /// Every non-overlapping occurrence of a piece of text. An empty pattern occurs between every
    /// pair of characters and at both ends, which is one more time than there are characters.
    /// </summary>
    private static IEnumerable<string> Occurrences(string text, string pattern)
    {
        if (pattern.Length == 0)
        {
            for (int i = 0; i <= text.Length; i++)
            {
                yield return string.Empty;
            }

            yield break;
        }

        int at = 0;
        while (at <= text.Length - pattern.Length)
        {
            int found = text.IndexOf(pattern, at, StringComparison.Ordinal);
            if (found < 0)
            {
                yield break;
            }

            yield return pattern;
            at = found + pattern.Length;
        }
    }

    // --- splitlines ---------------------------------------------------------------------------

    /// <summary>
    /// <c>splitlines</c>: the pieces between line breaks. The breaks are the carriage-return family
    /// and nothing else — measured, against a documentation page that also lists the vertical tab,
    /// the form feed and the Unicode line separators, none of which R2024a splits on.
    /// </summary>
    private static JgsValue SplitLines(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("splitlines", args, 1, line, col);
        TextBundle subject = ReadText(args[0], "First argument must be text.", line, col);

        // A char matrix is not one piece of text and MATLAB will not split it, so it is refused with
        // the same sentence a number gets rather than split row by row.
        if (subject.Kind == TextKind.Char && subject.Texts.Length > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                "First argument must be text.");
        }

        var pieces = new string[subject.Texts.Length][];
        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i] = [.. Lines(subject.Texts[i])];
        }

        return SpreadPieces("splitlines", subject, pieces, "delimiters", line, col);
    }

    /// <summary>The pieces of one string between CRLF, LF and CR, keeping every empty one.</summary>
    private static List<string> Lines(string text)
    {
        var pieces = new List<string>();
        int at = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r'))
            {
                continue;
            }

            pieces.Add(text[at..i]);
            at = i + 1;
            if (text[i] == '\r' && at < text.Length && text[at] == '\n')
            {
                at++;
                i++;
            }
        }

        pieces.Add(text[at..]);
        return pieces;
    }

    // --- strtok -------------------------------------------------------------------------------

    /// <summary>
    /// <c>strtok</c>: the first run of characters that are not delimiters, and everything from the
    /// delimiter that ended it. Leading delimiters are skipped, so the token is never empty unless
    /// there is nothing left to take.
    /// </summary>
    private static JgsValue[] Tokenized(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("strtok", args, 1, 2, line, col);
        TextBundle subject = ReadText(args[0], "First argument must be text.", line, col);
        char[] delimiters = args.Count > 1
            ? ReadText(args[1], "Second argument must be text.", line, col).Texts[0].ToCharArray()
            : DefaultTokenDelimiters;

        // MATLAB's own strtok measures its argument with `length`, not `numel`, so a char matrix is
        // read as its column-major characters cut off at its longest side. Reproduced because a
        // script that hands strtok a char matrix gets that answer and no other.
        string[] sources = subject.Texts;
        if (subject.Kind == TextKind.Char && subject.Texts.Length > 1)
        {
            sources = [FlattenCharMatrix(subject)];
        }

        var tokens = new string[sources.Length];
        var rests = new string[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            string text = sources[i];
            int start = 0;
            while (start < text.Length && Array.IndexOf(delimiters, text[start]) >= 0)
            {
                start++;
            }

            int stop = start;
            while (stop < text.Length && Array.IndexOf(delimiters, text[stop]) < 0)
            {
                stop++;
            }

            tokens[i] = text[start..stop];
            rests[i] = text[stop..];
        }

        // A char matrix collapsed to one run of characters answers one token, not a stack of them.
        return sources.Length == subject.Texts.Length
            ? Outputs(wanted, RebuildLike(subject, tokens), RebuildLike(subject, rests))
            : Outputs(wanted, JgsValue.Str(tokens[0]), JgsValue.Str(rests[0]));
    }

    /// <summary>
    /// A char matrix as the characters MATLAB's own <c>strtok</c> sees: column-major order, cut off
    /// at <c>length</c> — the longer of the two sides — rather than at the element count.
    /// </summary>
    private static string FlattenCharMatrix(TextBundle bundle)
    {
        int width = bundle.Texts.Max(static t => t.Length);
        var all = new System.Text.StringBuilder(bundle.Texts.Length * width);
        for (int c = 0; c < width; c++)
        {
            foreach (string row in bundle.Texts)
            {
                all.Append(c < row.Length ? row[c] : ' ');
            }
        }

        int keep = Math.Min(all.Length, Math.Max(bundle.Texts.Length, width));
        return all.ToString(0, keep);
    }

    // --- strjust ------------------------------------------------------------------------------

    /// <summary>
    /// <c>strjust</c>: each row's characters moved to one side of the blanks that pad it. The width
    /// never changes — a null character counts as a blank and comes back as one.
    /// </summary>
    private static JgsValue Justified(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("strjust", args, 1, 2, line, col);
        TextBundle subject = ReadText(args[0], "First argument must be text.", line, col);
        string side = args.Count > 1 && IsTextArgument(args[1]) ? TextOf2(args[1]) : "right";
        if (side is not ("left" or "right" or "center"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:strjust:UnknownParameter",
                "Second argument to strjust must be one of: left, right, center.");
        }

        var answers = new string[subject.Texts.Length];
        for (int i = 0; i < answers.Length; i++)
        {
            string text = subject.Texts[i].Replace('\0', ' ');
            string core = text.Trim(' ');
            int slack = text.Length - core.Length;
            answers[i] = side switch
            {
                "left" => core.PadRight(text.Length),
                "right" => core.PadLeft(text.Length),
                _ => core.PadLeft(core.Length + (slack / 2)).PadRight(text.Length),
            };
        }

        return RebuildLike(subject, answers);
    }

    // --- strvcat / str2mat --------------------------------------------------------------------

    /// <summary>
    /// The two char-matrix builders. They differ in one rule and only one: <c>strvcat</c> leaves out
    /// an argument with no characters in it, and <c>str2mat</c> keeps it as a row of blanks.
    /// </summary>
    private static JgsValue Stacked(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool dropsEmpty = name == "strvcat";
        var rows = new List<string>();
        foreach (JgsValue argument in args)
        {
            if (TryReadText(argument, out TextBundle bundle))
            {
                foreach (string text in bundle.Texts)
                {
                    if (text.Length > 0 || !dropsEmpty)
                    {
                        rows.Add(text);
                    }
                }

                continue;
            }

            // A number is its code point, the same reading `char` gives it.
            if (argument.Type is JgsType.Number or JgsType.Bool)
            {
                rows.Add(((char)(int)argument.AsNumber).ToString());
                continue;
            }

            if (argument.Type == JgsType.Array)
            {
                var codes = new System.Text.StringBuilder();
                foreach (JgsValue element in argument.BoxedElements())
                {
                    codes.Append((char)(int)element.AsNumber);
                }

                if (codes.Length > 0 || !dropsEmpty)
                {
                    rows.Add(codes.ToString());
                }

                continue;
            }

            throw new JgsRuntimeException(line, col, $"{name} takes text or code points.");
        }

        if (rows.Count == 0)
        {
            return JgsValue.Str(string.Empty);
        }

        return rows.Count == 1 ? JgsValue.Str(rows[0]) : PadIntoCharMatrix([.. rows]);
    }

    // --- strmatch -----------------------------------------------------------------------------

    /// <summary>
    /// <c>strmatch</c>: which rows of a list of text start with the given text, or — with
    /// <c>'exact'</c> — equal it once both have been padded to the list's width. The list is padded
    /// into a char matrix first, which is why a candidate shorter than the sought text never matches.
    /// </summary>
    private static JgsValue Matched(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        ArityRange("strmatch", args, 2, 3, line, col);
        string sought = ReadText(args[0], "First argument must be text.", line, col).Texts[0];
        string[] candidates = TryReadText(args[1], out TextBundle list) ? list.Texts : [];
        bool exact = args.Count > 2
            && string.Equals(TextOf2(args[2]), "exact", StringComparison.OrdinalIgnoreCase);

        var hits = new List<double>();
        if (candidates.Length > 0)
        {
            int width = candidates.Max(static c => c.Length);
            if (sought.Length <= width)
            {
                string padded = sought.PadRight(width);
                for (int i = 0; i < candidates.Length; i++)
                {
                    string candidate = candidates[i].PadRight(width);
                    bool hit = exact
                        ? string.Equals(candidate, padded, StringComparison.Ordinal)
                        : candidate.AsSpan(0, sought.Length).SequenceEqual(sought);
                    if (hit)
                    {
                        hits.Add(i + 1);
                    }
                }
            }
        }

        if (hits.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        JgsValue answer = JgsValue.Array([.. hits.Select(JgsValue.Number)]);
        answer.Reshape(hits.Count, 1);
        return answer;
    }

    // --- hex2num / num2hex --------------------------------------------------------------------

    /// <summary>
    /// <c>hex2num</c>: the double whose bits a run of hexadecimal digits spells. Fewer than sixteen
    /// digits are padded on the right with zeros — so <c>hex2num('4')</c> is 2, not 4 — and more
    /// than sixteen are cut off there.
    /// </summary>
    private static JgsValue HexToNum(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("hex2num", args, 1, line, col);
        if (!TryReadText(args[0], out TextBundle digits))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:hex2num:InputMustBeString",
                "Input to hex2num must be a character vector, string, or cell array of character vectors.");
        }

        var numbers = new List<JgsValue>();
        foreach (string text in digits.Texts)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int digit = i < trimmed.Length ? HexDigit(trimmed[i], line, col) : 0;
                bits = (bits << 4) | (uint)digit;
            }

            numbers.Add(JgsValue.Number(BitConverter.Int64BitsToDouble(unchecked((long)bits))));
        }

        if (numbers.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        if (numbers.Count == 1)
        {
            return numbers[0];
        }

        JgsValue column = JgsValue.Array([.. numbers]);
        column.Reshape(numbers.Count, 1);
        return column;
    }

    /// <summary>One hexadecimal digit's value, or MATLAB's refusal of anything else.</summary>
    private static int HexDigit(char c, int line, int col) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new JgsRuntimeException(line, col, "MATLAB:hex2num:OutOfRange",
            "Input to hex2num should have just 0-9, a-f, or A-F."),
    };

    /// <summary>
    /// <c>num2hex</c>: the hexadecimal spelling of a number's own bits — sixteen digits for a double
    /// and eight for a single. An array answers one row per element, in column-major order.
    /// </summary>
    private static JgsValue NumToHex(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("num2hex", args, 1, line, col);
        JgsValue value = args[0];

        if (value.Type == JgsType.Complex
            || (value.Type == JgsType.Array
                && Array.Exists(value.BoxedElements(), static e => e.Type == JgsType.Complex)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:num2hex:realInput", "Input must be real.");
        }

        if (value.Type is not (JgsType.Number or JgsType.Array)
            || value.NumericClass is not (JgsNumericClass.Double or JgsNumericClass.Single))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:num2hex:floatpointInput",
                $"Inputs must be floating point, and may not be of class {ClassOf(value, JgsDialect.Matlab)}.");
        }

        bool single = value.NumericClass == JgsNumericClass.Single;
        double[] numbers = value.Type == JgsType.Number
            ? [value.AsNumber]
            : Array.ConvertAll(value.BoxedElements(), static e => e.AsNumber);

        var spellings = new string[numbers.Length];
        for (int i = 0; i < numbers.Length; i++)
        {
            spellings[i] = single
                ? ((uint)BitConverter.SingleToInt32Bits((float)numbers[i])).ToString("x8", CultureInfo.InvariantCulture)
                : ((ulong)BitConverter.DoubleToInt64Bits(numbers[i])).ToString("x16", CultureInfo.InvariantCulture);
        }

        return spellings.Length switch
        {
            0 => JgsValue.Str(string.Empty),
            1 => JgsValue.Str(spellings[0]),
            _ => PadIntoCharMatrix(spellings),
        };
    }

    // --- variable names -----------------------------------------------------------------------

    /// <summary>
    /// Whether a piece of text is a name MATLAB would let a variable have: a letter, then letters,
    /// digits and underscores, no longer than <c>namelengthmax</c>, and not one of the keywords.
    /// </summary>
    private static bool IsValidVariableName(string name)
    {
        if (name.Length == 0 || name.Length > 63 || !char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return Array.IndexOf(MatlabKeywordList, name) < 0;
    }
}
