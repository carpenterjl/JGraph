using System;
using System.Collections.Generic;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The last of the text family to be told that its subject may be a container (M121):
/// <c>split</c>, <c>regexp</c>, <c>regexpi</c>, <c>regexprep</c>, <c>extractBetween</c> and
/// <c>strfind</c>, each of which was written for one piece of text and refused a string array by
/// name.
/// </summary>
/// <remarks>
/// <para>
/// M104 gave this repository two rules and most of a family that obeys them: a verb answers in the
/// container it was handed, and a verb handed several pieces of text answers once per piece. Six
/// names were left outside — not because either rule is different for them, but because each was
/// written before the string array existed and nothing since had asked it the question. Three of
/// them are the regular-expression verbs, whose answers are not text and so had nowhere to go in
/// the existing retrofit; the other three simply were not on the list.
/// </para>
/// <para>
/// The retrofit here is the same shape as <c>MapOverText</c>'s and differs from it in exactly two
/// places. It maps over the <em>first</em> argument by name rather than over the first container it
/// finds, because for these names a container in any later position is a set of patterns rather
/// than a partner to pair with (<c>replace(s, ["a";"c"], "z")</c> applies both patterns to <c>s</c>;
/// it does not answer twice). And it puts the per-element answers back three different ways,
/// because these verbs answer three different things: one piece of text, several, or something that
/// is not text at all.
/// </para>
/// <para>
/// A char matrix is not a container here. MATLAB refuses one to every name in this file — measured,
/// not assumed: <c>regexprep(['ab';'cd'], 'a', 'z')</c> raises
/// <c>MATLAB:string:MustBeCharCellArrayOrString</c> — so mapping over its rows would have invented
/// an answer rather than matched one.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>How a verb's per-element answers are put back together.</summary>
    private enum TextAnswer
    {
        /// <summary>One piece of text per element, in the container the subject arrived in.</summary>
        Text,

        /// <summary>Several pieces per element, spread along a new trailing dimension.</summary>
        Pieces,

        /// <summary>Anything at all per element, gathered into a cell shaped like the subject.</summary>
        Boxed,
    }

    /// <summary>A name whose first argument may be a container of text, and what it answers with.</summary>
    private readonly record struct SubjectMap(string Name, TextAnswer Answer);

    /// <summary>
    /// The names retrofitted here. Each maps over its first argument only; everything after it is
    /// handed to the body unchanged, once per element.
    /// </summary>
    private static readonly SubjectMap[] SubjectMappedBuiltins =
    [
        // split and strfind read their own containers since the section-5 rebuild; regexprep is
        // the one name still mapped here.
        new("regexprep", TextAnswer.Text),
    ];

    /// <summary>
    /// Wraps <see cref="SubjectMappedBuiltins"/> so a string array or a cell of char in the first
    /// argument is answered once per element, in a container shaped like the one that arrived.
    /// </summary>
    private static void MapTextSubjects(JgsEnvironment env)
    {
        foreach (SubjectMap map in SubjectMappedBuiltins)
        {
            MapTextSubject(env, map);
        }
    }

    /// <summary>
    /// Wraps one name so a string array or a cell of char in its first argument is answered once per
    /// element. Also called by <see cref="RegisterEvalBuiltins"/>, which declares <c>regexprep</c> a
    /// second time once there is an interpreter to hand it and has to wrap it again.
    /// </summary>
    private static void MapTextSubject(JgsEnvironment env, SubjectMap map)
    {
        if (!env.TryGet(map.Name, out JgsValue declared)
            || declared.Type != JgsType.Function
            || declared.AsCallable is not BuiltinFunction inner)
        {
            return;
        }

        SubjectMap captured = map;
        env.Declare(map.Name, JgsValue.Function(new BuiltinFunction(
            map.Name,
            (args, line, col) => OverSubject(captured, inner, args, wanted: 1, line, col)[0])
        {
            // The wrapper has to see the string array to map over it, so it opts out of the
            // demotion — and then hands each element down as the char row the body expects.
            KeepsStringArguments = true,
            BindsAnsAsStatement = inner.BindsAnsAsStatement,
            AutoCallsBare = inner.AutoCallsBare,
            KnowsWhenDiscarded = inner.KnowsWhenDiscarded,

            // Carried only where there was one to carry: handing a name a multi-output form it
            // never had changes how CallMultiple treats it, which is a change to the calling
            // convention rather than to the answer.
            MultiOutput = inner.MultiOutput is null
                ? null
                : (args, wanted, line, col) => OverSubject(captured, inner, args, wanted, line, col),
        }));
    }

    /// <summary>
    /// Runs <paramref name="inner"/> once per element of its first argument, or once in total when
    /// that argument is one piece of text — which is every call these names took before M121, and
    /// which reaches the body by the same road it always did.
    /// </summary>
    private static JgsValue[] OverSubject(
        SubjectMap map, BuiltinFunction inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        // Only a string array or a cell of char is a container to map over. A char row is one piece
        // of text and a char matrix is not text these names accept at all, so both go straight
        // through to the body and are refused or answered exactly as they were.
        //
        // A one-element container still comes through here, and for the two text-answering kinds it
        // has to: `split("a,b", ",")` is a string array of two where `split('a,b', ',')` is a cell of
        // two, and the only thing that tells them apart is the container the subject arrived in. The
        // exception is a verb whose answer is not text — boxing a scalar's answer would wrap it in a
        // cell it never had, so those pass straight through as well.
        if (args.Count == 0
            || !TryReadText(args[0], out TextBundle subject)
            || subject.Kind == TextKind.Char
            || (subject.Scalar && map.Answer == TextAnswer.Boxed))
        {
            return Call(inner, args, wanted, line, col);
        }

        // An empty container answers an empty one of the same kind and shape, which is what MATLAB
        // does and what the walk below cannot say: with no element to ask, there is no count of
        // pieces to spread along and no answer to box.
        if (subject.Texts.Length == 0)
        {
            var empty = new JgsValue[Math.Max(1, Math.Max(wanted, 1))];
            for (int o = 0; o < empty.Length; o++)
            {
                empty[o] = map.Answer == TextAnswer.Boxed
                    ? EmptyCell(subject)
                    : RebuildLike(subject, []);
            }

            return empty;
        }

        // The arguments beside the subject do not change as the map walks, so they are demoted once
        // here rather than once per element — the same economy MapOverText makes, for the same
        // reason: at two hundred thousand elements the walk is most of what the call costs.
        var one = new JgsValue[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            one[i] = inner.Demote(args[i]);
        }

        return inner.Protect(
            () =>
            {
                int produced = Math.Max(1, wanted);
                var perOutput = new JgsValue[produced][];
                for (int o = 0; o < produced; o++)
                {
                    perOutput[o] = new JgsValue[subject.Texts.Length];
                }

                for (int i = 0; i < subject.Texts.Length; i++)
                {
                    one[0] = JgsValue.Str(subject.Texts[i]);
                    JgsValue[] answers = inner.MultiOutput is { } multi && produced > 1
                        ? multi(one, produced, line, col)
                        : [inner.Invoke(one, line, col)];

                    for (int o = 0; o < produced; o++)
                    {
                        // A body that answered fewer outputs than were asked for has already said so
                        // for the first element; the shortfall is reported by the caller that wanted
                        // them, not invented here.
                        perOutput[o][i] = o < answers.Length ? answers[o] : JgsValue.Array([]);
                    }
                }

                var gathered = new JgsValue[produced];
                for (int o = 0; o < produced; o++)
                {
                    gathered[o] = Assemble(map, subject, perOutput[o], line, col);
                }

                return gathered;
            },
            line,
            col);
    }

    /// <summary>One call of a builtin, wanting <paramref name="wanted"/> outputs.</summary>
    private static JgsValue[] Call(
        BuiltinFunction inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
        wanted > 1 ? inner.CallMultiple(args, wanted, line, col) : [inner.Call(args, line, col)];

    /// <summary>Puts a map's per-element answers back into one value.</summary>
    private static JgsValue Assemble(
        SubjectMap map, TextBundle subject, JgsValue[] answers, int line, int col)
    {
        switch (map.Answer)
        {
            case TextAnswer.Text:
                return RebuildLike(subject, Array.ConvertAll(answers, TextOfAnswer));

            case TextAnswer.Pieces:
                var pieces = new string[answers.Length][];
                for (int i = 0; i < answers.Length; i++)
                {
                    pieces[i] = PiecesOfAnswer(answers[i]);
                }

                return SpreadPieces(map.Name, subject, pieces, "delimiters", line, col);

            default:
                // A cell of the subject's shape, whatever each element answered. This is MATLAB's
                // rule for regexp and strfind over an array, with one exception measured rather
                // than assumed: where every element answers exactly one piece of text — 'match'
                // with 'once' — a string subject answers a string array instead of a cell.
                if (subject.Kind == TextKind.String && Array.TrueForAll(answers, IsOnePieceOfText))
                {
                    return RebuildLike(subject, Array.ConvertAll(answers, TextOfAnswer));
                }

                JgsValue cell = JgsValue.Cell(answers);
                cell.Reshape(subject.Rows, subject.Cols);
                return cell;
        }
    }

    /// <summary>A cell shaped like the container that arrived, with nothing in it.</summary>
    private static JgsValue EmptyCell(TextBundle subject)
    {
        JgsValue cell = JgsValue.Cell([]);
        cell.Reshape(subject.Rows, subject.Cols);
        return cell;
    }

    /// <summary>Whether an answer is exactly one piece of text, and so can go into a string array.</summary>
    private static bool IsOnePieceOfText(JgsValue answer) =>
        answer.Type == JgsType.String || IsStringScalar(answer);

    /// <summary>The text of an answer that is one piece of text.</summary>
    private static string TextOfAnswer(JgsValue answer) =>
        answer.Type == JgsType.String ? answer.AsString
        : IsStringScalar(answer) ? TextOf(answer)
        : answer.Display();

    /// <summary>The pieces of an answer that is several of them.</summary>
    private static string[] PiecesOfAnswer(JgsValue answer) =>
        TextElementsOf(answer) ?? [TextOfAnswer(answer)];

    // --- concatenating containers of text -----------------------------------------------------------

    // Lifted out of the interpreter's bracket literal in M121, because `horzcat`, `vertcat` and
    // `cat` are the function spellings of `[a b]` and `[a; b]` and had no way to reach it: every one
    // of them refused a string array outright. Two spellings of one operation want one
    // implementation, and this is the one that was already right.

    /// <summary>
    /// A multi-row bracket of string arrays, joined the way the numeric block machinery joins
    /// numbers: each row's blocks stand side by side and must agree on height, then the rows stack
    /// and must agree on width. An empty block contributes nothing, exactly as it does elsewhere.
    /// </summary>
    internal static JgsValue ConcatenateStringArrays(List<JgsValue[]> rows, int line, int col)
    {
        var bands = new List<(int Rows, int Cols, string[] Texts)>(rows.Count);
        foreach (JgsValue[] row in rows)
        {
            var blocks = new List<(int Rows, int Cols, string[] Texts)>(row.Length);
            int height = -1;
            foreach (JgsValue piece in row)
            {
                (int Rows, int Cols, string[] Texts) block = StringBlock(piece);
                if (block.Rows == 0 || block.Cols == 0)
                {
                    continue;
                }

                if (height < 0)
                {
                    height = block.Rows;
                }
                else if (height != block.Rows)
                {
                    throw new JgsRuntimeException(line, col,
                        "Dimensions of arrays being concatenated are not consistent.");
                }

                blocks.Add(block);
            }

            if (blocks.Count == 0)
            {
                continue;
            }

            int width = 0;
            foreach ((int Rows, int Cols, string[] Texts) block in blocks)
            {
                width += block.Cols;
            }

            var band = new string[height * width];
            int placed = 0;
            foreach ((int Rows, int Cols, string[] Texts) block in blocks)
            {
                for (int c = 0; c < block.Cols; c++)
                {
                    for (int r = 0; r < height; r++)
                    {
                        band[r + ((placed + c) * height)] = block.Texts[r + (c * block.Rows)];
                    }
                }

                placed += block.Cols;
            }

            bands.Add((height, width, band));
        }

        if (bands.Count == 0)
        {
            return JgsValue.StringArray([], 0, 0);
        }

        int cols = bands[0].Cols;
        int total = 0;
        foreach ((int Rows, int Cols, string[] Texts) band in bands)
        {
            if (band.Cols != cols)
            {
                throw new JgsRuntimeException(line, col,
                    "Dimensions of arrays being concatenated are not consistent.");
            }

            total += band.Rows;
        }

        var texts = new JgsValue[total * cols];
        int top = 0;
        foreach ((int Rows, int Cols, string[] Texts) band in bands)
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < band.Rows; r++)
                {
                    texts[top + r + (c * total)] = JgsValue.Str(band.Texts[r + (c * band.Rows)]);
                }
            }

            top += band.Rows;
        }

        return JgsValue.StringArray(texts, total, cols);
    }

    /// <summary>One block of a string-array bracket: its shape, and its elements in column order.</summary>
    private static (int Rows, int Cols, string[] Texts) StringBlock(JgsValue piece)
    {
        if (piece.IsStringArray)
        {
            return (piece.Rows, piece.Cols,
                Array.ConvertAll(piece.BoxedElements(), static e => e.AsString));
        }

        // A char row is one element of the answer, not a row of characters — the same rule the
        // single-row join follows, and the reason ["a" 'b'] is 1-by-2 rather than 1-by-1.
        if (piece.Type == JgsType.String)
        {
            return (1, 1, [piece.AsString]);
        }

        if (piece.Type == JgsType.Array)
        {
            return piece.ArrayLength == 0
                ? (0, 0, [])
                : (piece.Rows, piece.Cols,
                    Array.ConvertAll(piece.BoxedElements(), static e => e.Display()));
        }

        return (1, 1, [piece.Display()]);
    }

    // --- join, along a dimension the caller may name ------------------------------------------------

    /// <summary>
    /// <c>join(str)</c>, <c>join(str, delimiter)</c>, <c>join(str, dim)</c> and
    /// <c>join(str, delimiter, dim)</c>: the text of one dimension run together, in the container it
    /// arrived in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until M121 this joined an N-by-M array along its rows and everything else into one string,
    /// which meant a column of text collapsed to a single string where MATLAB leaves it a column —
    /// the head-to-head text script carries a comment saying so and works around it.
    /// </para>
    /// <para>
    /// The dimension is the last one that is not a singleton unless the caller names it, and the
    /// delimiter expands over the gaps the way any other operand expands: a column of delimiters
    /// gives each row its own, a row of them gives each gap its own, and a full grid gives every gap
    /// its own. That is implicit expansion against <c>size(str)</c> with the joined dimension one
    /// shorter, and it is measured against MATLAB rather than inferred from the documentation.
    /// </para>
    /// </remarks>
    private static JgsValue Joined(IReadOnlyList<JgsValue> args, bool matlab, int line, int col)
    {
        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        ArityRange("join", args, 1, 3, line, col);

        if (args[0].IsCharMatrix || !TryReadText(args[0], out TextBundle subject))
        {
            if (matlab)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                    "First argument must be text.");
            }

            // JGS's join([1 2 3], "-") has run everything together into one string since the
            // dialect landed, and a name that already answers something is not the place to start
            // refusing.
            string only = args.Count >= 2 && TryReadText(args[1], out TextBundle written)
                ? written.Texts[0]
                : " ";
            JgsValue[] loose = Arr("join", args, 0, line, col);
            return JgsValue.Str(string.Join(only, loose.Select(static p => p.Display())));
        }

        // Which of the two second arguments this is, asked the way MATLAB asks it: text is the
        // delimiter and a number is the dimension. The bare `missing` value is neither.
        bool delimiterGiven = args.Count >= 2 && TryReadText(args[1], out _) && !args[1].IsCharMatrix
            && !(args[1].Type == JgsType.String && IsMissingText(args[1].AsString));
        int dimensionAt = args.Count == 3 ? 2 : args.Count == 2 && !delimiterGiven ? 1 : -1;
        if (args.Count == 3 && !delimiterGiven)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                "Delimiter must be a string array, character vector, or cell array of character vectors.");
        }

        TextBundle delimiter = delimiterGiven
            ? ReadText(args[1], "The delimiter must be text.", line, col)
            : new(TextKind.Char, [" "], 1, 1);

        int rows = subject.Rows;
        int cols = subject.Cols;
        int dim;
        if (dimensionAt >= 0)
        {
            if (!IsOneNumber(args[dimensionAt]) || OneNumber(args[dimensionAt]) < 1
                || OneNumber(args[dimensionAt]) != Math.Floor(OneNumber(args[dimensionAt])))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBePositiveIntegerScalar",
                    "Dimension argument must be a positive integer scalar within indexing range.");
            }

            dim = (int)OneNumber(args[dimensionAt]);
        }
        else
        {
            // The last dimension that is not 1: a 0 counts, which is what makes join(strings(1, 0))
            // join along its empty second dimension and answer one missing string (measured).
            dim = cols != 1 ? 2 : 1;
        }

        // Joining along a dimension the array does not have, or has only one of, joins nothing,
        // and MATLAB answers the array back rather than refusing it.
        if (dim > 2 || (dim == 1 ? rows : cols) == 1)
        {
            return RebuildLike(subject, subject.Texts);
        }

        int gapRows = dim == 1 ? rows - 1 : rows;
        int gapCols = dim == 1 ? cols : cols - 1;
        if ((dim == 1 ? rows : cols) == 0)
        {
            // Nothing to join: each answer is the missing string, or '' in a cell (measured).
            int emptyRows = dim == 1 ? 1 : rows;
            int emptyCols = dim == 1 ? cols : 1;
            var none = new string[emptyRows * emptyCols];
            Array.Fill(none, subject.Kind == TextKind.String ? MissingSentinel : string.Empty);
            return RebuildText(subject.Kind == TextKind.Char ? TextKind.Cell : subject.Kind, none, emptyRows, emptyCols);
        }

        // The delimiter expands over the gaps: each of its dimensions is 1 or the gap count, exactly.
        if ((delimiter.Rows != 1 && delimiter.Rows != gapRows) || (delimiter.Cols != 1 && delimiter.Cols != gapCols))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:InvalidDelimiterDimensions",
                "Invalid delimiter dimensions.");
        }

        int outRows = dim == 1 ? 1 : rows;
        int outCols = dim == 1 ? cols : 1;
        var joined = new string[outRows * outCols];
        var built = new System.Text.StringBuilder();
        for (int slot = 0; slot < joined.Length; slot++)
        {
            int fixedRow = dim == 1 ? -1 : slot;
            int fixedCol = dim == 1 ? slot : -1;
            int along = dim == 1 ? rows : cols;

            built.Clear();
            bool missing = false;
            for (int k = 0; k < along; k++)
            {
                int r = dim == 1 ? k : fixedRow;
                int c = dim == 1 ? fixedCol : k;
                if (k > 0)
                {
                    // The gap before element k is gap k-1 along the joined dimension.
                    int gr = dim == 1 ? k - 1 : r;
                    int gc = dim == 1 ? c : k - 1;
                    string between = delimiter.Texts[ElementIndex(delimiter, gr, gc)];
                    missing |= delimiter.Kind == TextKind.String && IsMissingText(between);
                    built.Append(between);
                }

                string piece = subject.Texts[r + (c * rows)];
                missing |= subject.Kind == TextKind.String && IsMissingText(piece);
                built.Append(piece);
            }

            // A missing string anywhere in the run, or as its delimiter, makes the answer missing.
            joined[slot] = missing ? MissingSentinel : built.ToString();
        }

        return RebuildText(subject.Kind == TextKind.Char ? TextKind.Cell : subject.Kind,
            joined, outRows, outCols);
    }

    // --- Patterns as a set -------------------------------------------------------------------------

    /// <summary>
    /// The one pattern an argument names, without building a list to hold it.
    /// </summary>
    /// <remarks>
    /// Every caller of <see cref="PatternsOf"/> below sits inside a body the elementwise wrapper
    /// runs once per element, so an allocation there is an allocation per element: two hundred
    /// thousand of them for one <c>contains(keys, "7")</c>. This asks the question the common case
    /// actually has -- one pattern -- and answers it without touching the heap, which is what keeps
    /// the list support from costing anything to the calls that never use it. Measured: without it,
    /// the text script's three predicates over 200,000 keys went from 41 ms to 68 ms.
    /// </remarks>
    private static bool IsOnePattern(JgsValue value, out string only)
    {
        if (value.Type == JgsType.String)
        {
            only = value.AsString;
            return true;
        }

        if (IsStringScalar(value))
        {
            only = TextOf(value);
            return true;
        }

        only = string.Empty;
        return false;
    }

    /// <summary>
    /// The patterns one argument names: the single one a piece of text is, or the several a
    /// container holds. MATLAB lets every search-and-edit verb take a list here, and what it does
    /// with the list is the verb's own business — <c>contains</c> asks whether <em>any</em> matched,
    /// <c>count</c> adds them up, and <c>replace</c> applies each in turn.
    /// </summary>
    private static string[] PatternsOf(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (index >= args.Count)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an argument in position {index + 1}.");
        }

        // A char matrix is not a list of patterns to MATLAB — replace('abc', ['a';'b'], 'X') is
        // refused by name — so its rows are not quietly read as one.
        if (args[index].IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: text to search for must be a char row vector, a cell array of char row vectors, or a string array.");
        }

        return TextElementsOf(args[index])
            ?? throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be text, but got a {args[index].TypeName}.");
    }

    /// <summary>
    /// Every pattern replaced in one left-to-right pass, so no replacement can be found again by a
    /// later pattern. A null entry in <paramref name="with"/> erases rather than replaces.
    /// </summary>
    /// <remarks>
    /// One pass rather than one pass per pattern, because that is what MATLAB does and the two
    /// disagree: <c>replace("ab", ["a";"b"], ["b";"a"])</c> is <c>"ba"</c>, where replacing in turn
    /// would put an <c>a</c> where the first replacement had just written a <c>b</c> and answer
    /// <c>"aa"</c>. Where two patterns can match at the same place the earlier one wins, which is
    /// the only reading that also gives <c>replace("a", ["a";"b"], ["b";"c"])</c> its measured
    /// <c>"b"</c>.
    /// </remarks>
    private static string ReplacedAtOnce(string text, string[] patterns, string?[] with)
    {
        if (patterns.Length == 1 && patterns[0].Length > 0)
        {
            // The null a caller passes for "erase this" is an empty replacement, not a reason to
            // walk the string a character at a time. Getting this wrong made erase over 200,000
            // keys 2.7 times slower than it had been.
            return text.Replace(patterns[0], with[0] ?? string.Empty, StringComparison.Ordinal);
        }

        // An empty pattern matches at every position, the end included, which is how
        // replace('abc', '', 'X') comes to be 'XaXbXcX' (measured).
        var built = new System.Text.StringBuilder(text.Length);
        int at = 0;
        while (at <= text.Length)
        {
            int matched = -1;
            for (int p = 0; p < patterns.Length; p++)
            {
                int length = patterns[p].Length;
                if (length == 0 || (at + length <= text.Length && string.CompareOrdinal(text, at, patterns[p], 0, length) == 0))
                {
                    matched = p;
                    break;
                }
            }

            if (matched < 0)
            {
                if (at < text.Length)
                {
                    built.Append(text[at]);
                }

                at++;
                continue;
            }

            built.Append(with[matched] ?? string.Empty);
            if (patterns[matched].Length == 0)
            {
                if (at < text.Length)
                {
                    built.Append(text[at]);
                }

                at++;
            }
            else
            {
                at += patterns[matched].Length;
            }
        }

        return built.ToString();
    }

    /// <summary>
    /// The replacements to go with <paramref name="patterns"/>: one for all of them, or one each.
    /// </summary>
    private static string[] ReplacementsFor(
        string name, IReadOnlyList<JgsValue> args, int index, string[] patterns, int line, int col)
    {
        string[] written = PatternsOf(name, args, index, line, col);
        if (written.Length == patterns.Length)
        {
            return written;
        }

        if (written.Length == 1)
        {
            var spread = new string[patterns.Length];
            Array.Fill(spread, written[0]);
            return spread;
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: {patterns.Length} pattern(s) and {written.Length} replacement(s) — there must be "
            + "one replacement for each pattern, or one for all of them.");
    }
}
