using System.Linq;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs that act at a place in a piece of text — <c>insertAfter</c>, <c>insertBefore</c>,
/// <c>extractAfter</c>, <c>extractBefore</c>, <c>extractBetween</c> — and <c>strrep</c>, all of
/// them container-aware and all of them measured against R2025b.
/// </summary>
/// <remarks>
/// <para>
/// Each reads its subject as a <see cref="TextBundle"/> and answers in the kind it arrived in: a
/// char row answers char, a string answers string, a cell answers a cell. That is why these are not
/// on the elementwise retrofit list any more — the retrofit demoted a string to its char row before
/// the body saw it, so the body could not tell that the missing answer for <c>extractAfter("abc",
/// "x")</c> is <c>&lt;missing&gt;</c> where the char answer is <c>''</c>, and it treated a string
/// marker as the thing to map over, so <c>extractAfter('abc', "a")</c> came back a string.
/// </para>
/// <para>
/// The place a verb acts is either a marker to search for or a 1-based position. A marker is acted
/// on at every occurrence, left to right and without overlap; a position is checked against the text
/// with MATLAB's own sentences, and the two "after" verbs accept 0 where the two "before" verbs
/// accept one past the end, so that both can name the very edge of the text.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Where an insert or extract verb acts: a marker to search for, or a 1-based position.</summary>
    private readonly record struct Place(string? Marker, int Position);

    private static void RegisterTextPositionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

        Define("insertAfter", (args, line, col) => InsertAround("insertAfter", args, after: true, line, col));
        Define("insertBefore", (args, line, col) => InsertAround("insertBefore", args, after: false, line, col));
        Define("extractAfter", (args, line, col) => ExtractSide("extractAfter", args, after: true, line, col));
        Define("extractBefore", (args, line, col) => ExtractSide("extractBefore", args, after: false, line, col));
        Define("extractBetween", ExtractBetween);
        Define("strrep", StrRep);
    }

    // --- reading the arguments ----------------------------------------------------------------------

    /// <summary>The subject of one of these verbs: text in any container, but not a char matrix.</summary>
    private static TextBundle SubjectText(string name, JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix || !TryReadText(value, out TextBundle bundle))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeCharCellArrayOrString",
                $"{name}: first argument must be text.");
        }

        return bundle;
    }

    /// <summary>Whether a value is a number or a numeric array — the position form of an argument.</summary>
    private static bool IsNumericPosition(JgsValue value) =>
        value.Type == JgsType.Number
        || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix
            && (value.ArrayLength == 0 || value.ElementAt(0).Type == JgsType.Number));

    /// <summary>The numbers of a position argument.</summary>
    private static int[] PositionsOf(string name, JgsValue value, int line, int col)
    {
        int count = value.Type == JgsType.Number ? 1 : value.ArrayLength;
        var positions = new int[count];
        for (int i = 0; i < count; i++)
        {
            double raw = value.Type == JgsType.Number ? value.AsNumber : value.ElementAt(i).AsNumber;
            if (!(raw >= 0) || raw != Math.Floor(raw))
            {
                throw new JgsRuntimeException(line, col, $"{name}: numeric position must be a positive integer.");
            }

            positions[i] = (int)raw;
        }

        return positions;
    }

    /// <summary>
    /// One place per element of the subject: the marker or position given, spread to every element
    /// when there is one, or paired off when there is one per element.
    /// </summary>
    private static Place[] PlacesOf(string name, JgsValue value, int count, int line, int col)
    {
        if (IsNumericPosition(value))
        {
            return SpreadTo(name, Array.ConvertAll(PositionsOf(name, value, line, col), static p => new Place(null, p)), count, line, col);
        }

        if (!value.IsCharMatrix && TryReadText(value, out TextBundle markers))
        {
            return SpreadTo(name, Array.ConvertAll(markers.Texts, static m => new Place(m, 0)), count, line, col);
        }

        throw new JgsRuntimeException(line, col, $"{name}: the position must be text to search for or a numeric position.");
    }

    /// <summary>One item for each of <paramref name="count"/> elements: the single one given, or exactly that many.</summary>
    private static T[] SpreadTo<T>(string name, T[] items, int count, int line, int col)
    {
        if (items.Length == count)
        {
            return items;
        }

        if (items.Length == 1)
        {
            var spread = new T[count];
            Array.Fill(spread, items[0]);
            return spread;
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: dimensions of position argument must match the dimensions of the text argument or be 1.");
    }

    /// <summary>A position checked against one element's text, with MATLAB's sentences for the two ways it can be off.</summary>
    private static int CheckedPosition(string name, int position, int least, int most, int element, int line, int col)
    {
        if (position < least)
        {
            throw new JgsRuntimeException(line, col, $"{name}: numeric position must be a positive integer.");
        }

        if (position > most)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: numeric value exceeds the number of characters in element {element}.");
        }

        return position;
    }

    // --- insertAfter / insertBefore -----------------------------------------------------------------

    private static JgsValue InsertAround(string name, IReadOnlyList<JgsValue> args, bool after, int line, int col)
    {
        Arity(name, args, 3, line, col);
        TextBundle subject = SubjectText(name, args[0], line, col);
        int count = subject.Texts.Length;
        Place[] places = PlacesOf(name, args[1], count, line, col);
        if (args[2].IsCharMatrix || !TryReadText(args[2], out TextBundle inserts))
        {
            throw new JgsRuntimeException(line, col, $"{name}: insert value must be text.");
        }

        string[] what = SpreadTo(name, inserts.Texts, count, line, col);
        var texts = new string[count];
        for (int i = 0; i < count; i++)
        {
            string text = subject.Texts[i];
            if (places[i].Marker is { } marker)
            {
                texts[i] = InsertedAtMarkers(text, marker, what[i], after);
                continue;
            }

            int cut = after
                ? CheckedPosition(name, places[i].Position, 0, text.Length, i + 1, line, col)
                : CheckedPosition(name, places[i].Position, 1, text.Length + 1, i + 1, line, col) - 1;
            texts[i] = text[..cut] + what[i] + text[cut..];
        }

        return RebuildLike(subject, texts);
    }

    /// <summary>
    /// <paramref name="insert"/> placed beside every occurrence of <paramref name="marker"/>. An empty
    /// marker occurs between every pair of characters and at both ends, which is what MATLAB does with it.
    /// </summary>
    private static string InsertedAtMarkers(string text, string marker, string insert, bool after)
    {
        var built = new StringBuilder(text.Length + insert.Length);
        if (marker.Length == 0)
        {
            foreach (char c in text)
            {
                built.Append(insert).Append(c);
            }

            return built.Append(insert).ToString();
        }

        int at = 0;
        for (int found = text.IndexOf(marker, StringComparison.Ordinal); found >= 0;
             found = text.IndexOf(marker, at, StringComparison.Ordinal))
        {
            built.Append(text, at, found - at);
            if (after)
            {
                built.Append(marker).Append(insert);
            }
            else
            {
                built.Append(insert).Append(marker);
            }

            at = found + marker.Length;
        }

        return built.Append(text, at, text.Length - at).ToString();
    }

    // --- extractAfter / extractBefore ---------------------------------------------------------------

    private static JgsValue ExtractSide(string name, IReadOnlyList<JgsValue> args, bool after, int line, int col)
    {
        Arity(name, args, 2, line, col);
        TextBundle subject = SubjectText(name, args[0], line, col);
        int count = subject.Texts.Length;
        Place[] places = PlacesOf(name, args[1], count, line, col);

        var texts = new string[count];
        for (int i = 0; i < count; i++)
        {
            string text = subject.Texts[i];
            if (places[i].Marker is { } marker)
            {
                int found = text.IndexOf(marker, StringComparison.Ordinal);
                if (found < 0)
                {
                    // A marker that is not there: no text for a char subject, and the missing string
                    // for a string one — which is the one answer ismissing can pick out afterwards.
                    texts[i] = subject.Kind == TextKind.String ? MissingSentinel : string.Empty;
                    continue;
                }

                texts[i] = after ? text[(found + marker.Length)..] : text[..found];
                continue;
            }

            texts[i] = after
                ? text[CheckedPosition(name, places[i].Position, 0, text.Length, i + 1, line, col)..]
                : text[..(CheckedPosition(name, places[i].Position, 1, text.Length + 1, i + 1, line, col) - 1)];
        }

        return RebuildLike(subject, texts);
    }

    // --- extractBetween -------------------------------------------------------------------------------

    /// <summary>
    /// <c>extractBetween(str, start, end)</c> with either two markers or two positions, and the
    /// <c>'Boundaries'</c> option. Every non-overlapping pair of markers yields a piece, so one char
    /// row answers a column cell of pieces — none of them when the markers are absent — and a container
    /// answers along a new dimension, every element having to yield the same number.
    /// </summary>
    private static JgsValue ExtractBetween(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("extractBetween", args, 3, 5, line, col);
        TextBundle subject = SubjectText("extractBetween", args[0], line, col);
        int count = subject.Texts.Length;

        bool? inclusive = null;
        if (args.Count > 3)
        {
            if (args.Count != 5 || !IsTextScalar(args[3])
                || !string.Equals(TextOf(args[3]), "Boundaries", StringComparison.OrdinalIgnoreCase)
                || !IsTextScalar(args[4]))
            {
                throw new JgsRuntimeException(line, col,
                    "extractBetween: the only option is 'Boundaries', followed by 'inclusive' or 'exclusive'.");
            }

            string side = TextOf(args[4]);
            inclusive = string.Equals(side, "inclusive", StringComparison.OrdinalIgnoreCase) ? true
                : string.Equals(side, "exclusive", StringComparison.OrdinalIgnoreCase) ? false
                : throw new JgsRuntimeException(line, col,
                    $"extractBetween: 'Boundaries' must be 'inclusive' or 'exclusive', not '{side}'.");
        }

        var pieces = new string[count][];
        if (IsNumericPosition(args[1]) || IsNumericPosition(args[2]))
        {
            if (!IsNumericPosition(args[1]) || !IsNumericPosition(args[2]))
            {
                throw new JgsRuntimeException(line, col,
                    "extractBetween: the start and end must both be text or both be numeric positions.");
            }

            // Positions are inclusive unless told otherwise: extractBetween('abcde', 2, 4) is 'bcd'.
            bool keepEnds = inclusive ?? true;
            int[] starts = SpreadTo("extractBetween", PositionsOf("extractBetween", args[1], line, col), count, line, col);
            int[] ends = SpreadTo("extractBetween", PositionsOf("extractBetween", args[2], line, col), count, line, col);
            for (int i = 0; i < count; i++)
            {
                string text = subject.Texts[i];
                int start = CheckedPosition("extractBetween", starts[i], 1, text.Length, i + 1, line, col);
                int end = CheckedPosition("extractBetween", ends[i], 0, text.Length, i + 1, line, col);
                if (end < start - 1)
                {
                    throw new JgsRuntimeException(line, col,
                        "extractBetween: numeric start position must come before numeric end position.");
                }

                pieces[i] = keepEnds
                    ? [text[(start - 1)..end]]
                    : [end - 1 <= start ? string.Empty : text[start..(end - 1)]];
            }
        }
        else
        {
            // Markers are excluded unless told otherwise: extractBetween('a<b>', '<', '>') is 'b'.
            bool keepEnds = inclusive ?? false;
            string[] opens = MarkersOf("extractBetween", args[1], count, line, col);
            string[] closes = MarkersOf("extractBetween", args[2], count, line, col);
            for (int i = 0; i < count; i++)
            {
                pieces[i] = BetweenMarkers(subject.Texts[i], opens[i], closes[i], keepEnds);
            }
        }

        return SpreadPieces("extractBetween", subject, pieces, "substrings", line, col);
    }

    /// <summary>A marker argument spread to one per element.</summary>
    private static string[] MarkersOf(string name, JgsValue value, int count, int line, int col)
    {
        if (value.IsCharMatrix || !TryReadText(value, out TextBundle markers))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the start and end must be text or numeric positions.");
        }

        return SpreadTo(name, markers.Texts, count, line, col);
    }

    /// <summary>Every piece of <paramref name="text"/> between an <paramref name="open"/> and the <paramref name="close"/> after it, without overlap.</summary>
    private static string[] BetweenMarkers(string text, string open, string close, bool keepEnds)
    {
        var pieces = new List<string>();
        int at = 0;
        while (at <= text.Length)
        {
            int start = text.IndexOf(open, at, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            int stop = text.IndexOf(close, start + open.Length, StringComparison.Ordinal);
            if (stop < 0)
            {
                break;
            }

            pieces.Add(keepEnds ? text[start..(stop + close.Length)] : text[(start + open.Length)..stop]);
            int next = stop + close.Length;
            at = next > at ? next : at + 1; // two empty markers must still move along
        }

        return pieces.ToArray();
    }

    // --- strrep -----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>strrep(str, old, new)</c>: every occurrence of <paramref name="args"/>[1] replaced, the
    /// overlapping ones included, which is the documented behaviour and the one <c>replace</c> does
    /// not share — <c>strrep('aaa', 'aa', 'b')</c> is <c>'bb'</c>, <c>replace</c> of the same is
    /// <c>'ba'</c>. Any string argument makes the answer a string, any cell a cell, and the containers
    /// are paired off element by element.
    /// </summary>
    private static JgsValue StrRep(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("strrep", args, 3, line, col);
        var parts = new TextBundle[3];
        for (int i = 0; i < 3; i++)
        {
            parts[i] = StrRepText(args[i], line, col);
        }

        TextKind kind = parts.Any(static p => p.Kind == TextKind.String) ? TextKind.String
            : parts.Any(static p => p.Kind == TextKind.Cell) ? TextKind.Cell
            : TextKind.Char;

        int count = 1;
        TextBundle shape = parts[0];
        foreach (TextBundle part in parts)
        {
            if (part.Texts.Length != 1)
            {
                if (count != 1 && part.Texts.Length != count)
                {
                    throw new JgsRuntimeException(line, col,
                        "strrep: cell array or string array inputs must have the same size.");
                }

                count = part.Texts.Length;
                shape = part;
            }
        }

        var texts = new string[count];
        for (int i = 0; i < count; i++)
        {
            texts[i] = ReplacedOverlapping(Pick(parts[0], i), Pick(parts[1], i), Pick(parts[2], i));
        }

        return RebuildText(kind, texts, shape.Rows, shape.Cols);

        static string Pick(TextBundle part, int i) => part.Texts[part.Texts.Length == 1 ? 0 : i];
    }

    /// <summary>One argument of <c>strrep</c> as text; a number is read as character codes, as MATLAB does with a warning.</summary>
    private static TextBundle StrRepText(JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "strrep: char inputs must be row vectors.");
        }

        if (TryReadText(value, out TextBundle bundle))
        {
            return bundle;
        }

        if (IsNumericPosition(value))
        {
            var codes = new StringBuilder();
            int count = value.Type == JgsType.Number ? 1 : value.ArrayLength;
            for (int i = 0; i < count; i++)
            {
                codes.Append((char)(int)(value.Type == JgsType.Number ? value.AsNumber : value.ElementAt(i).AsNumber));
            }

            return new TextBundle(TextKind.Char, [codes.ToString()], 1, 1);
        }

        throw new JgsRuntimeException(line, col,
            "strrep: inputs must be character vectors, cell arrays of character vectors, or string arrays.");
    }

    /// <summary>
    /// <paramref name="replacement"/> written over every occurrence of <paramref name="old"/> in
    /// <paramref name="text"/>, counting overlapping occurrences: each one is replaced where it starts,
    /// and every character any of them covers is dropped. An empty <paramref name="old"/> changes
    /// nothing — except in empty text, where MATLAB answers the replacement.
    /// </summary>
    private static string ReplacedOverlapping(string text, string old, string replacement)
    {
        if (old.Length == 0)
        {
            return text.Length == 0 ? replacement : text;
        }

        double[] starts = Occurrences(text, old, 0);
        if (starts.Length == 0)
        {
            return text;
        }

        var startsHere = new bool[text.Length];
        var covered = new bool[text.Length];
        foreach (double start in starts)
        {
            startsHere[(int)start] = true;
            for (int k = 0; k < old.Length; k++)
            {
                covered[(int)start + k] = true;
            }
        }

        var built = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (startsHere[i])
            {
                built.Append(replacement);
            }

            if (!covered[i])
            {
                built.Append(text[i]);
            }
        }

        return built.ToString();
    }
}
