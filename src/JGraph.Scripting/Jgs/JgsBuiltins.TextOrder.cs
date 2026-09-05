using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The ordering and membership verbs taught to read text: <c>sort</c>, <c>issorted</c>,
/// <c>sortrows</c>, <c>unique</c> and <c>ismember</c> over a cell of char or a string array, and
/// <c>max</c>/<c>min</c> over a char row's character codes. Every rule here was measured in MATLAB
/// R2025b; each wrapper hands anything that is not text straight back to the definition it wraps.
/// </summary>
/// <remarks>
/// <para>
/// Text orders by character code, which is why <c>sort({'B', 'a', 'b'})</c> puts the capital first.
/// A missing string is greater than every string, so it sorts last ascending and first descending
/// unless <c>'MissingPlacement'</c> says otherwise, and every missing string is distinct from every
/// other in <c>unique</c>. A cell of char takes exactly one argument in <c>sort</c> and
/// <c>issorted</c>: MATLAB refuses a direction or a dimension for it by name.
/// </para>
/// <para>
/// <c>ismember</c> has two readings of char. Two char arrays compare character by character —
/// <c>ismember('bc', 'abc')</c> is <c>[1 1]</c> — where a char row against a cell or a string array
/// is one piece of text: <c>ismember('b', {'a', 'b'})</c> is true. The <c>'rows'</c> flag turns a
/// char matrix into its rows and is ignored for a cell, exactly as MATLAB ignores it.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the text-aware wrappers over the numeric definitions already declared.</summary>
    internal static void RegisterTextOrderBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Wrap(string name, Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> body)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            JgsValue[] Run(IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                body(inner, args, wanted, line, col) ?? Call(inner, args, wanted, line, col);

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => Run(args, 1, line, col)[0])
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                MultiOutput = (args, wanted, line, col) => Run(args, wanted, line, col),
            }));
        }

        Wrap("sort", (inner, args, wanted, line, col) => SortedText(args, wanted, dialect, line, col));
        Wrap("issorted", (inner, args, wanted, line, col) => IsSortedText(args, line, col));
        Wrap("sortrows", (inner, args, wanted, line, col) => SortedTextRows(args, wanted, dialect, line, col));
        Wrap("unique", (inner, args, wanted, line, col) => UniqueText(args, wanted, dialect, line, col));
        Wrap("ismember", (inner, args, wanted, line, col) => TextMembership(inner, args, wanted, dialect, line, col));

        foreach (string name in new[] { "max", "min" })
        {
            Wrap(name, (inner, args, wanted, line, col) =>
            {
                // A char row is its character codes to max and min (max('abc') is 99, measured), and
                // only MATLAB reads it so; JGS keeps refusing text here. Only the subject is read that
                // way: the words after it ('all', 'omitnan', 'linear') are options, not text.
                if (!dialect.IsMatlab || args.Count == 0 || args[0].Type != JgsType.String)
                {
                    return null;
                }

                var coded = new JgsValue[args.Count];
                for (int i = 0; i < coded.Length; i++)
                {
                    coded[i] = i == 0 ? CharRowOf(args[i]) : args[i];
                }

                return Call(inner, coded, wanted, line, col);
            });
        }
    }

    // --- shared -------------------------------------------------------------------------------------

    /// <summary>
    /// The arguments with every option word after the subject as the char row the option parser
    /// reads: these wrappers keep string scalars, and "descend" is one.
    /// </summary>
    private static JgsValue[] WithOptionWords(IReadOnlyList<JgsValue> args)
    {
        var plain = new JgsValue[args.Count];
        for (int i = 0; i < plain.Length; i++)
        {
            plain[i] = i > 0 && IsStringScalar(args[i]) ? JgsValue.Str(TextOf(args[i])) : args[i];
        }

        return plain;
    }

    /// <summary>Reads a cell of char or a string array as text to order; a char row is not one (its codes are).</summary>
    private static bool TryReadOrderedText(JgsValue value, out TextBundle bundle)
    {
        bundle = default;
        return (value.IsStringArray || value.Type == JgsType.Cell) && TryReadText(value, out bundle);
    }

    /// <summary>Orders two strings by character code, with the missing string greater than any other.</summary>
    private static int CompareTexts(string a, string b)
    {
        bool aMissing = IsMissingText(a);
        bool bMissing = IsMissingText(b);
        if (aMissing || bMissing)
        {
            return aMissing == bMissing ? 0 : aMissing ? 1 : -1;
        }

        return string.CompareOrdinal(a, b);
    }

    /// <summary>
    /// The positions of a slice in sorted order, stable, with missing strings placed as asked:
    /// <c>'auto'</c> puts them last ascending and first descending.
    /// </summary>
    private static int[] SortedOrder(string[] texts, int[] positions, bool descending, string placement)
    {
        var present = new List<int>();
        var absent = new List<int>();
        foreach (int at in positions)
        {
            (IsMissingText(texts[at]) ? absent : present).Add(at);
        }

        int[] ordered = present.ToArray();
        Array.Sort(ordered, (x, y) =>
        {
            int order = string.CompareOrdinal(texts[x], texts[y]);
            if (descending)
            {
                order = -order;
            }

            return order != 0 ? order : x.CompareTo(y);
        });

        bool upFront = string.Equals(placement, "first", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(placement, "auto", StringComparison.OrdinalIgnoreCase) && descending);
        return upFront ? [.. absent, .. ordered] : [.. ordered, .. absent];
    }

    /// <summary>The 1-based positions <paramref name="order"/> names, as a double array shaped like the subject.</summary>
    private static JgsValue IndexArray(int[] order, int rows, int cols, JgsDialect dialect)
    {
        var flat = new double[order.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            flat[i] = order[i] + dialect.IndexBase;
        }

        if (flat.Length == 1 && rows == 1 && cols == 1)
        {
            return JgsValue.Number(flat[0]);
        }

        JgsValue value = Numbers(flat);
        value.Reshape(rows, cols);
        return value;
    }

    /// <summary>The dimension a vector or matrix is worked along when none is named: the first that is not 1.</summary>
    private static int FirstNonSingleton(int rows, int cols) => rows != 1 ? 1 : 2;

    // --- sort ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>sort</c> over text: along a dimension, ascending or descending, with the missing strings
    /// placed as asked and the permutation as the second output. A cell of char takes no options.
    /// </summary>
    private static JgsValue[]? SortedText(IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        if (args.Count == 0 || !TryReadOrderedText(args[0], out TextBundle subject))
        {
            return null;
        }

        bool descending = false;
        string placement = "auto";
        int? dim = null;
        if (subject.Kind == TextKind.Cell)
        {
            if (args.Count != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:sort:cellArrayOneInput",
                    "Only one input argument is supported for cell arrays.");
            }
        }
        else
        {
            ParsedArgs parsed = SortOptions.Parse(WithOptionWords(args), 2, line, col);
            descending = parsed.OneOf("ascend", "ascend", "descend", "asc", "desc") is "descend" or "desc";
            placement = parsed.Word("MissingPlacement", "auto", "auto", "first", "last");
            if (parsed.Positional.Count == 2)
            {
                if (!IsOneNumber(parsed.Positional[1]) || OneNumber(parsed.Positional[1]) < 1
                    || OneNumber(parsed.Positional[1]) != Math.Floor(OneNumber(parsed.Positional[1])))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:sort:dimensionMustBePositiveInteger",
                        "sort: the dimension must be a positive integer scalar.");
                }

                dim = (int)OneNumber(parsed.Positional[1]);
            }
        }

        int rows = subject.Rows;
        int cols = subject.Cols;
        int along = dim ?? FirstNonSingleton(rows, cols);
        var sorted = new string[subject.Texts.Length];
        var index = new int[subject.Texts.Length];
        if (along > 2 || subject.Texts.Length == 0)
        {
            Array.Copy(subject.Texts, sorted, sorted.Length);
            for (int i = 0; i < index.Length; i++)
            {
                index[i] = i;
            }
        }
        else
        {
            int slices = along == 1 ? cols : rows;
            int length = along == 1 ? rows : cols;
            for (int s = 0; s < slices; s++)
            {
                var positions = new int[length];
                for (int k = 0; k < length; k++)
                {
                    positions[k] = along == 1 ? k + (s * rows) : s + (k * rows);
                }

                int[] order = SortedOrder(subject.Texts, positions, descending, placement);
                for (int k = 0; k < length; k++)
                {
                    sorted[positions[k]] = subject.Texts[order[k]];
                    index[positions[k]] = along == 1 ? order[k] - (s * rows) : (order[k] - s) / rows;
                }
            }
        }

        return Outputs(wanted, RebuildLike(subject, sorted), IndexArray(index, rows, cols, dialect));
    }

    // --- issorted -----------------------------------------------------------------------------------

    /// <summary><c>issorted</c> over text, and over a char row's character codes.</summary>
    private static JgsValue[]? IsSortedText(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            return null;
        }

        if (args[0].Type == JgsType.String)
        {
            string text = args[0].AsString;
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i] < text[i - 1])
                {
                    return [JgsValue.Bool(false)];
                }
            }

            return [JgsValue.Bool(true)];
        }

        if (!TryReadOrderedText(args[0], out TextBundle subject))
        {
            return null;
        }

        bool descending = false;
        if (subject.Kind == TextKind.Cell)
        {
            if (args.Count != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:issorted:cellArrayOneInput",
                    "Only one input argument is supported for cell arrays.");
            }
        }
        else
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (IsTextScalar(args[i]))
                {
                    string word = TextOf(args[i]).ToLowerInvariant();
                    if (word is "descend" or "strictdescend")
                    {
                        descending = true;
                    }
                    else if (word is not ("ascend" or "strictascend" or "monotonic" or "strictmonotonic"))
                    {
                        throw new JgsRuntimeException(line, col, $"issorted: unknown option '{TextOf(args[i])}'.");
                    }
                }
            }
        }

        int rows = subject.Rows;
        int cols = subject.Cols;
        int along = FirstNonSingleton(rows, cols);
        int slices = along == 1 ? cols : rows;
        int length = along == 1 ? rows : cols;
        for (int s = 0; s < slices; s++)
        {
            for (int k = 1; k < length; k++)
            {
                string previous = subject.Texts[along == 1 ? (k - 1) + (s * rows) : s + ((k - 1) * rows)];
                string current = subject.Texts[along == 1 ? k + (s * rows) : s + (k * rows)];
                int order = CompareTexts(previous, current);
                if (descending ? order < 0 : order > 0)
                {
                    return [JgsValue.Bool(false)];
                }
            }
        }

        return [JgsValue.Bool(true)];
    }

    // --- sortrows -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>sortrows</c> over a cell or a string array: by every column left to right, or by the
    /// columns named (a negative column descends), then by the direction words. A cell may mix a
    /// column of text with a column of numbers, but not within one column.
    /// </summary>
    private static JgsValue[]? SortedTextRows(IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        if (args.Count == 0 || args.Count > 3)
        {
            return null;
        }

        JgsValue subject = args[0];
        bool strings = subject.IsStringArray;
        if (!strings && subject.Type != JgsType.Cell)
        {
            return null;
        }

        JgsValue[] cells = subject.BoxedElements();
        if (!strings && !Array.TrueForAll(cells, static c => c.Type is JgsType.String or JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sortrows:invalidCell",
                "sortrows: a cell array must hold character vectors or numbers.");
        }

        int rows = subject.Rows;
        int cols = subject.Cols;
        var keys = new List<(int Column, bool Descending)>();
        if (args.Count >= 2 && !IsTextScalar(args[1]) && args[1].Type != JgsType.Cell)
        {
            foreach (double raw in ToDoubles("sortrows", args[1], line, col))
            {
                int column = (int)Math.Abs(raw) - dialect.IndexBase;
                if (column < 0 || column >= cols || Math.Abs(raw) != Math.Floor(Math.Abs(raw)))
                {
                    throw new JgsRuntimeException(line, col,
                        $"sortrows: {raw} is not a column of an array with {cols} of them.");
                }

                keys.Add((column, raw < 0));
            }
        }
        else
        {
            for (int c = 0; c < cols; c++)
            {
                keys.Add((c, false));
            }
        }

        int wordsAt = args.Count == 3 ? 2 : args.Count == 2 && (IsTextScalar(args[1]) || args[1].Type == JgsType.Cell) ? 1 : -1;
        if (wordsAt >= 0)
        {
            string[] words = DirectionWords(args[wordsAt], keys.Count, line, col);
            for (int i = 0; i < keys.Count; i++)
            {
                keys[i] = (keys[i].Column, words[i] == "descend");
            }
        }

        int CompareCells(int r1, int r2, int c)
        {
            JgsValue a = cells[r1 + (c * rows)];
            JgsValue b = cells[r2 + (c * rows)];
            if (a.Type == JgsType.String && b.Type == JgsType.String)
            {
                return CompareTexts(a.AsString, b.AsString);
            }

            if (a.Type != JgsType.String && b.Type != JgsType.String)
            {
                double x = a.AsNumber;
                double y = b.AsNumber;
                return double.IsNaN(x) || double.IsNaN(y) ? (double.IsNaN(x) ? 1 : 0) - (double.IsNaN(y) ? 1 : 0) : x.CompareTo(y);
            }

            throw new JgsRuntimeException(line, col, "MATLAB:sortrows:mixedColumn",
                "sortrows: a column cannot mix text and numbers.");
        }

        var order = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            order[r] = r;
        }

        Array.Sort(order, (r1, r2) =>
        {
            foreach ((int column, bool descending) in keys)
            {
                int result = CompareCells(r1, r2, column);
                if (result != 0)
                {
                    return descending ? -result : result;
                }
            }

            return r1.CompareTo(r2);
        });

        var sorted = new JgsValue[cells.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sorted[r + (c * rows)] = cells[order[r] + (c * rows)];
            }
        }

        JgsValue first;
        if (strings)
        {
            first = JgsValue.StringArray(sorted, rows, cols);
        }
        else
        {
            first = JgsValue.Cell(sorted);
            first.Reshape(rows, cols);
        }

        return Outputs(wanted, first, IndexArray(order, rows, 1, dialect));
    }

    // --- unique -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>unique</c> over text: the distinct strings in character-code order, or in first-appearance
    /// order with <c>'stable'</c>; every missing string is distinct and sorts last. A row of text
    /// answers a row and anything else a column, and <c>'rows'</c> reads a string matrix by its rows.
    /// </summary>
    private static JgsValue[]? UniqueText(IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        if (args.Count == 0 || !TryReadOrderedText(args[0], out TextBundle subject))
        {
            return null;
        }

        ParsedArgs parsed = UniqueOptions.Parse(WithOptionWords(args), 1, line, col);
        bool stable = parsed.OneOf("sorted", "sorted", "stable") == "stable";
        bool last = parsed.OneOf("first", "first", "last") == "last";
        bool byRows = parsed.Has("rows") && subject.Kind == TextKind.String;

        int rows = subject.Rows;
        int cols = subject.Cols;
        int count = byRows ? rows : subject.Texts.Length;
        string[] Key(int i)
        {
            if (!byRows)
            {
                return [subject.Texts[i]];
            }

            var key = new string[cols];
            for (int c = 0; c < cols; c++)
            {
                key[c] = subject.Texts[i + (c * rows)];
            }

            return key;
        }

        int CompareKeys(string[] a, string[] b)
        {
            for (int c = 0; c < a.Length; c++)
            {
                int order = CompareTexts(a[c], b[c]);
                if (order != 0)
                {
                    return order;
                }
            }

            return 0;
        }

        bool AnyMissing(string[] key) => subject.Kind == TextKind.String && Array.Exists(key, IsMissingText);

        var keys = new string[count][];
        for (int i = 0; i < count; i++)
        {
            keys[i] = Key(i);
        }

        // Sorted positions, ties in arrival order; then the groups, each missing its own.
        var sorted = new int[count];
        for (int i = 0; i < count; i++)
        {
            sorted[i] = i;
        }

        Array.Sort(sorted, (x, y) =>
        {
            int order = CompareKeys(keys[x], keys[y]);
            return order != 0 ? order : x.CompareTo(y);
        });

        var groupOf = new int[count];
        var firstOf = new List<int>();
        var lastOf = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int at = sorted[i];
            bool starts = i == 0 || AnyMissing(keys[at]) || CompareKeys(keys[sorted[i - 1]], keys[at]) != 0;
            if (starts)
            {
                firstOf.Add(at);
                lastOf.Add(at);
            }
            else
            {
                lastOf[^1] = Math.Max(lastOf[^1], at);
            }

            groupOf[at] = firstOf.Count - 1;
        }

        int[] groups = Enumerable.Range(0, firstOf.Count).ToArray();
        if (stable)
        {
            Array.Sort(groups, (g, h) => firstOf[g].CompareTo(firstOf[h]));
        }

        var slot = new int[firstOf.Count];
        for (int i = 0; i < groups.Length; i++)
        {
            slot[groups[i]] = i;
        }

        var ia = new int[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            ia[i] = last ? lastOf[groups[i]] : firstOf[groups[i]];
        }

        var ic = new int[count];
        for (int i = 0; i < count; i++)
        {
            ic[i] = slot[groupOf[i]];
        }

        JgsValue values;
        if (byRows)
        {
            var flat = new string[groups.Length * cols];
            for (int i = 0; i < groups.Length; i++)
            {
                for (int c = 0; c < cols; c++)
                {
                    flat[i + (c * groups.Length)] = keys[firstOf[groups[i]]][c];
                }
            }

            values = RebuildText(TextKind.String, flat, groups.Length, cols);
        }
        else
        {
            var picked = new string[groups.Length];
            for (int i = 0; i < picked.Length; i++)
            {
                picked[i] = subject.Texts[firstOf[groups[i]]];
            }

            bool row = rows == 1 && cols != 1;
            values = RebuildText(subject.Kind, picked, row ? 1 : picked.Length, row ? picked.Length : 1);
        }

        return Outputs(wanted, values, IndexArray(ia, ia.Length, 1, dialect), IndexArray(ic, ic.Length, 1, dialect));
    }

    // --- ismember -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>ismember</c> where either side is text. Two char arrays compare character by character
    /// unless <c>'rows'</c> is given; otherwise every element of A is one piece of text looked up in B.
    /// The second output is where in B each was first found, 0 where it was not.
    /// </summary>
    private static JgsValue[]? TextMembership(
        BuiltinFunction inner, IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        if (args.Count is not (2 or 3))
        {
            return null;
        }

        JgsValue a = args[0];
        JgsValue b = args[1];
        bool rows = args.Count == 3 && IsTextScalar(args[2]) && TextOf(args[2]).Equals("rows", StringComparison.OrdinalIgnoreCase);
        if (args.Count == 3 && !rows)
        {
            return null;
        }

        static bool IsChar(JgsValue v) => v.Type == JgsType.String || v.IsCharMatrix;
        static bool IsTextContainer(JgsValue v) =>
            v.IsStringArray || (v.Type == JgsType.Cell && Array.TrueForAll(v.AsCell, static e => e.Type == JgsType.String));
        static bool IsNumeric(JgsValue v) =>
            v.Type is JgsType.Number or JgsType.Bool || (v.Type == JgsType.Array && !v.IsStringArray && !v.IsCharMatrix);

        if (!IsChar(a) && !IsTextContainer(a) && !IsChar(b) && !IsTextContainer(b))
        {
            return null;
        }

        // Char against char, or char against numbers, is character codes.
        if ((IsChar(a) || IsNumeric(a)) && (IsChar(b) || IsNumeric(b)) && !rows)
        {
            JgsValue[] coded = [a.Type == JgsType.String ? CharRowOf(a) : a, b.Type == JgsType.String ? CharRowOf(b) : b];
            if (!dialect.IsMatlab)
            {
                return null;
            }

            return Call(inner, coded, wanted, line, col);
        }

        if (IsNumeric(a) || IsNumeric(b))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ISMEMBER:InputClass",
                $"Input A of class {ClassOf(a, dialect)} and input B of class {ClassOf(b, dialect)} must be cell "
                + "arrays of character vectors, unless one is a character vector.");
        }

        (string[] Texts, int Rows, int Cols, bool Strings) Side(JgsValue v)
        {
            if (v.IsCharMatrix)
            {
                string[] lines = v.CharMatrixRows();
                return (lines, lines.Length, 1, false);
            }

            if (v.Type == JgsType.String)
            {
                return ([v.AsString], 1, 1, false);
            }

            TryReadText(v, out TextBundle bundle);
            return (bundle.Texts, bundle.Rows, bundle.Cols, bundle.Kind == TextKind.String);
        }

        (string[] subject, int subjectRows, int subjectCols, bool subjectStrings) = Side(a);
        (string[] set, _, _, bool setStrings) = Side(b);

        var where = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < set.Length; i++)
        {
            if ((setStrings && IsMissingText(set[i])) || where.ContainsKey(set[i]))
            {
                continue;
            }

            where[set[i]] = i;
        }

        var found = new bool[subject.Length];
        var index = new int[subject.Length];
        for (int i = 0; i < subject.Length; i++)
        {
            int at = -1;
            bool present = !(subjectStrings && IsMissingText(subject[i])) && where.TryGetValue(subject[i], out at);
            found[i] = present;
            index[i] = present ? at : -dialect.IndexBase;
        }

        if (subject.Length == 0)
        {
            return Outputs(wanted, EmptyLogical(subjectRows, subjectCols), JgsEmpty.Shaped(subjectRows, subjectCols));
        }

        return Outputs(wanted, LogicalMask(found, subjectRows, subjectCols), IndexArray(index, subjectRows, subjectCols, dialect));
    }
}
