using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The option surfaces of the set, selection and sliding-window builtins (M52 wave C): <c>unique</c>
/// and its index outputs, the tolerance-aware pair, <c>maxk</c>/<c>mink</c>, <c>histc</c>, the nine
/// <c>mov*</c> statistics, and the ordering rules <c>sort</c> reads.
/// </summary>
/// <remarks>
/// Every one of these answered only the first sentence of what MATLAB documents: <c>unique</c> had no
/// <c>'stable'</c>, no <c>'rows'</c> and no index outputs; <c>maxk</c> and <c>histc</c> had no
/// dimension; a sliding window could not be told what to do at the ends. None of it was refused on
/// purpose — the arity check simply stopped one argument short — so a script that wrote the documented
/// call got a complaint about the argument count rather than an answer. Everything added here is that:
/// a form that used to error now works.
/// </remarks>
internal static partial class JgsBuiltins
{
    // --- What counts as the same value ------------------------------------------------------------

    /// <summary>
    /// The distinct groups in a list of keys: which input each output value is taken from, and where
    /// each input landed in the output. Every set operation that reports indices — <c>unique</c>,
    /// <c>uniquetol</c> — is this function plus a rule for what counts as the same key.
    /// </summary>
    /// <remarks>
    /// <paramref name="stable"/> keeps the groups in order of first appearance rather than in sorted
    /// order, and <paramref name="last"/> represents each group by its last member rather than its
    /// first.
    /// A key holding NaN is its own group every time, which is what MATLAB reports and what keeps
    /// <c>C(ic)</c> rebuilding the input exactly: a missing reading is not evidence that two rows
    /// agree, so <c>unique([NaN NaN])</c> is two values rather than one.
    /// </remarks>
    private static (int[] Order, int[] Positions) GroupDistinct(
        string name, IReadOnlyList<JgsValue[]> keys, bool stable, bool last, int line, int col)
    {
        int count = keys.Count;
        var sorted = new int[count];
        for (int i = 0; i < count; i++)
        {
            sorted[i] = i;
        }

        // The index tiebreak makes the order total, so 'first' and 'last' name real members rather
        // than whichever one the sort happened to leave in front.
        Array.Sort(sorted, (a, b) =>
        {
            int order = CompareKeys(name, keys[a], keys[b], line, col);
            return order != 0 ? order : a.CompareTo(b);
        });

        var group = new int[count];
        var firstOf = new List<int>();
        var lastOf = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int at = sorted[i];
            bool joins = i > 0
                && !HasMissing(keys[at])
                && CompareKeys(name, keys[sorted[i - 1]], keys[at], line, col) == 0;
            if (joins)
            {
                firstOf[^1] = Math.Min(firstOf[^1], at);
                lastOf[^1] = Math.Max(lastOf[^1], at);
            }
            else
            {
                firstOf.Add(at);
                lastOf.Add(at);
            }

            group[at] = firstOf.Count - 1;
        }

        int groups = firstOf.Count;
        var slot = new int[groups];
        var order = new int[groups];
        if (stable)
        {
            // 'stable' reports the groups in the order they first turn up in the input, so the sorted
            // grouping above is only how membership was decided — not how the answer is laid out.
            var appearance = new int[groups];
            for (int g = 0; g < groups; g++)
            {
                appearance[g] = g;
            }

            Array.Sort(appearance, (a, b) => firstOf[a].CompareTo(firstOf[b]));
            for (int place = 0; place < groups; place++)
            {
                slot[appearance[place]] = place;
                order[place] = firstOf[appearance[place]];
            }
        }
        else
        {
            for (int g = 0; g < groups; g++)
            {
                slot[g] = g;
                order[g] = last ? lastOf[g] : firstOf[g];
            }
        }

        var positions = new int[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = slot[group[i]];
        }

        return (order, positions);
    }

    /// <summary>Lexicographic order over a key: the first element that differs decides.</summary>
    private static int CompareKeys(string name, JgsValue[] left, JgsValue[] right, int line, int col)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            int order = CompareValues(name, left[i], right[i], line, col);
            if (order != 0)
            {
                return order;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// Ascending order over one value, with NaN last. <see cref="double.CompareTo(double)"/> puts NaN
    /// first, which is where <c>sort([1 NaN 2])</c> used to leave it — the opposite end from MATLAB.
    /// </summary>
    private static int CompareValues(string name, JgsValue left, JgsValue right, int line, int col)
    {
        if (left.Type is JgsType.Number or JgsType.Bool && right.Type is JgsType.Number or JgsType.Bool)
        {
            double a = left.AsNumber;
            double b = right.AsNumber;
            if (double.IsNaN(a))
            {
                return double.IsNaN(b) ? 0 : 1;
            }

            return double.IsNaN(b) ? -1 : a.CompareTo(b);
        }

        if (left.Type == JgsType.String && right.Type == JgsType.String)
        {
            return string.CompareOrdinal(left.AsString, right.AsString);
        }

        throw new JgsRuntimeException(line, col,
            $"{name} needs an array of all numbers or all strings.");
    }

    /// <summary>Whether a key holds a missing reading, which stops it joining any group.</summary>
    private static bool HasMissing(JgsValue[] key)
    {
        foreach (JgsValue element in key)
        {
            if (element.Type is JgsType.Number or JgsType.Bool && double.IsNaN(element.AsNumber))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An index output: storage positions in the dialect's own numbering, down a column.</summary>
    /// <remarks>
    /// MATLAB reports <c>ia</c> and <c>ic</c> as columns whatever shape the input had, because they
    /// are lists of positions rather than a reshaped copy of the data.
    /// </remarks>
    private static JgsValue IndexColumn(int[] indices, JgsDialect dialect)
    {
        var flat = new double[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            flat[i] = indices[i] + dialect.IndexBase;
        }

        JgsValue value = Numbers(flat);
        if (flat.Length > 1)
        {
            value.Reshape(flat.Length, 1);
        }

        return value;
    }

    /// <summary>As many of a builtin's outputs as the call asked for.</summary>
    private static JgsValue[] Outputs(int wanted, params JgsValue[] all) =>
        all[..Math.Clamp(wanted, 1, all.Length)];

    // --- unique -----------------------------------------------------------------------------------

    private static readonly OptionSpec UniqueOptions = new(
        "unique",
        Flags: ["rows", "sorted", "stable", "first", "last"],
        Names: [],
        StringPositionals: 1);

    /// <summary>
    /// <c>[C, ia, ic] = unique(A, …)</c>: the distinct values, where each came from, and where each
    /// input went. <c>C = A(ia)</c> and <c>A = C(ic)</c> both hold, which is the property the index
    /// outputs exist for — a script groups by <c>ic</c> and reads the group's name out of <c>C</c>.
    /// </summary>
    private static JgsValue[] UniqueParts(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "unique needs an array.");
        }

        ParsedArgs parsed = UniqueOptions.Parse(args, 1, line, col);
        JgsValue subject = parsed.Positional[0];
        bool stable = parsed.OneOf("sorted", "sorted", "stable") == "stable";
        bool last = parsed.OneOf("first", "first", "last") == "last";
        if (stable && last)
        {
            throw new JgsRuntimeException(line, col,
                "unique: 'stable' keeps each value where it first appeared, so there is no 'last' occurrence for it to take.");
        }

        if (parsed.Has("rows"))
        {
            return UniqueRows(subject, stable, last, dialect, outputs, line, col);
        }

        // A cell of char is how a table hands over a text variable, and it is the shape MATLAB code
        // reaches for when it asks which serial numbers appear in a log.
        bool cells = subject.Type == JgsType.Cell;
        JgsValue[] elements = cells ? subject.AsCell : Arr("unique", [subject], 0, line, col);
        var keys = new JgsValue[elements.Length][];
        for (int i = 0; i < elements.Length; i++)
        {
            keys[i] = [elements[i]];
        }

        (int[] order, int[] positions) = GroupDistinct("unique", keys, stable, last, line, col);
        var picked = new JgsValue[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            picked[i] = elements[order[i]];
        }

        JgsValue values = cells ? JgsValue.Cell(picked) : JgsValue.Array(picked);

        // MATLAB answers a column unless the input was a row — and a cell of text always answers a
        // column, which is the shape a script then walks with parts{i,1}.
        if (picked.Length > 1 && (cells || JgsMatrix.RowCount(subject) > 1))
        {
            values.Reshape(picked.Length, 1);
        }

        return Outputs(outputs, values, IndexColumn(order, dialect), IndexColumn(positions, dialect));
    }

    /// <summary>The <c>'rows'</c> form: whole rows are the values, compared left to right.</summary>
    private static JgsValue[] UniqueRows(
        JgsValue subject, bool stable, bool last, JgsDialect dialect, int outputs, int line, int col)
    {
        if (subject.Type != JgsType.Array || subject.IsNd)
        {
            throw new JgsRuntimeException(line, col,
                "unique: 'rows' needs a matrix, which is the only shape that has rows to compare.");
        }

        int rows = JgsMatrix.RowCount(subject);
        int cols = JgsMatrix.ColCount(subject);
        var keys = new JgsValue[rows][];
        for (int r = 0; r < rows; r++)
        {
            var key = new JgsValue[cols];
            for (int c = 0; c < cols; c++)
            {
                key[c] = JgsMatrix.At(subject, r, c);
            }

            keys[r] = key;
        }

        (int[] order, int[] positions) = GroupDistinct("unique", keys, stable, last, line, col);

        var flat = new double[order.Length * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int i = 0; i < order.Length; i++)
            {
                JgsValue element = keys[order[i]][c];
                flat[(c * order.Length) + i] = element.Type is JgsType.Number or JgsType.Bool
                    ? element.AsNumber
                    : throw new JgsRuntimeException(line, col, "unique: 'rows' needs a numeric matrix.");
            }
        }

        return Outputs(
            outputs,
            JgsMatrix.FromColumnMajorDims(flat, [order.Length, cols]),
            IndexColumn(order, dialect),
            IndexColumn(positions, dialect));
    }

    // --- sort -------------------------------------------------------------------------------------

    private static readonly OptionSpec SortOptions = new(
        "sort",
        Flags: ["ascend", "descend", "asc", "desc"],
        Names: ["MissingPlacement", "ComparisonMethod"],
        StringPositionals: 1);

    /// <summary>
    /// A sorted copy under MATLAB's rules: which way round, where missing readings land, and what
    /// "in order" means for a complex number. Null when the array mixes kinds, which the caller reports.
    /// </summary>
    /// <remarks>
    /// <paramref name="missing"/> is <c>'auto'</c> (last when ascending, first when descending — last
    /// in reading order either way), <c>'first'</c> or <c>'last'</c>. <paramref name="comparison"/> is
    /// <c>'auto'</c>/<c>'real'</c>, which order by value and then by imaginary part, or <c>'abs'</c>,
    /// which orders by magnitude and settles ties by angle — the only ordering a complex array has
    /// naturally.
    /// </remarks>
    private static JgsValue[]? SortElements(
        JgsValue[] elements, bool descending, string missing, string comparison, int line, int col)
    {
        // MATLAB orders a complex array by magnitude unless told otherwise: for it, the default
        // 'auto' means 'abs' the moment there is a complex element to order, and only 'real'
        // asks for the real-then-imaginary reading. An array of plain numbers orders the same way
        // under either, so the test is only ever about what is in the array.
        bool magnitude = string.Equals(comparison, "abs", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(comparison, "auto", StringComparison.OrdinalIgnoreCase)
                && Array.Exists(elements, static v => v.Type == JgsType.Complex));

        if (Array.TrueForAll(elements, static v => v.Type == JgsType.String))
        {
            if (magnitude)
            {
                throw new JgsRuntimeException(line, col,
                    "sort: 'ComparisonMethod' says how to order numbers, so 'abs' does not apply to text.");
            }

            var text = (JgsValue[])elements.Clone();
            Array.Sort(text, (a, b) =>
            {
                int order = string.CompareOrdinal(a.AsString, b.AsString);
                return descending ? -order : order;
            });

            return text;
        }

        if (!Array.TrueForAll(elements, static v => v.Type is JgsType.Number or JgsType.Bool or JgsType.Complex))
        {
            return null;
        }

        var present = new List<int>();
        var absent = new List<int>();
        for (int i = 0; i < elements.Length; i++)
        {
            (IsMissingNumber(elements[i]) ? absent : present).Add(i);
        }

        int[] byValue = present.ToArray();
        Array.Sort(byValue, (a, b) =>
        {
            int order = OrderOf(elements[a], elements[b], magnitude);
            if (descending)
            {
                order = -order;
            }

            // Equal values keep the order they arrived in, which is what makes the second output of
            // sort a permutation a script can rely on rather than whatever the sort settled on.
            return order != 0 ? order : a.CompareTo(b);
        });

        bool upFront = string.Equals(missing, "first", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(missing, "auto", StringComparison.OrdinalIgnoreCase) && descending);

        var result = new JgsValue[elements.Length];
        int at = 0;
        if (upFront)
        {
            foreach (int i in absent)
            {
                result[at++] = elements[i];
            }
        }

        foreach (int i in byValue)
        {
            result[at++] = elements[i];
        }

        if (!upFront)
        {
            foreach (int i in absent)
            {
                result[at++] = elements[i];
            }
        }

        return result;
    }

    /// <summary>Whether a number is missing: NaN, or a complex number with NaN in either part.</summary>
    private static bool IsMissingNumber(JgsValue value) => value.Type == JgsType.Complex
        ? double.IsNaN(value.AsComplex.Real) || double.IsNaN(value.AsComplex.Imaginary)
        : double.IsNaN(value.AsNumber);

    private static int OrderOf(JgsValue left, JgsValue right, bool magnitude)
    {
        if (!magnitude && left.Type != JgsType.Complex && right.Type != JgsType.Complex)
        {
            return left.AsNumber.CompareTo(right.AsNumber);
        }

        Complex a = left.AsComplex;
        Complex b = right.AsComplex;
        if (!magnitude)
        {
            int real = a.Real.CompareTo(b.Real);
            return real != 0 ? real : a.Imaginary.CompareTo(b.Imaginary);
        }

        int size = Complex.Abs(a).CompareTo(Complex.Abs(b));
        return size != 0 ? size : a.Phase.CompareTo(b.Phase);
    }

    // --- Selection and set membership -------------------------------------------------------------

    private static void RegisterSelectionAndSets(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        foreach (string name in new[] { "maxk", "mink" })
        {
            string self = name;
            bool largest = name == "maxk";
            DefineBoth(name, (args, outputs, line, col) =>
                LargestFew(self, largest, args, dialect, outputs, line, col));
        }

        Define("histc", (args, line, col) => BinCountsOf(args, line, col));

        DefineBoth("uniquetol", (args, outputs, line, col) => UniqueWithinTolerance(args, dialect, outputs, line, col));
        DefineBoth("ismembertol", (args, outputs, line, col) => MemberWithinTolerance(args, dialect, outputs, line, col));

        Define("issortedrows", (args, line, col) =>
        {
            Arity("issortedrows", args, 1, line, col);
            if (!IsMatrixValue(args[0]))
            {
                double[] flat = ToDoubles("issortedrows", args[0], line, col);
                return JgsValue.Bool(IsAscending(flat));
            }

            // Rows compare lexicographically: the first column that differs decides, which is what
            // sortrows produces and therefore what issortedrows has to check.
            double[,] a = RectOf("issortedrows", args[0], line, col);
            for (int r = 1; r < a.GetLength(0); r++)
            {
                for (int c = 0; c < a.GetLength(1); c++)
                {
                    if (a[r, c] > a[r - 1, c])
                    {
                        break;
                    }

                    if (a[r, c] < a[r - 1, c])
                    {
                        return JgsValue.Bool(false);
                    }
                }
            }

            return JgsValue.Bool(true);
        });
    }

    private static bool IsAscending(double[] values)
    {
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < values[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>[B, I] = maxk(A, k, dim)</c> and its <c>mink</c> twin: the k largest (or smallest) values of
    /// every slice, in order, with where each came from inside its slice.
    /// </summary>
    private static JgsValue[] LargestFew(
        string name, bool largest, IReadOnlyList<JgsValue> args, JgsDialect dialect,
        int outputs, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        int wanted = Count(name, args, 1, line, col);
        if (wanted < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} cannot take {wanted} values.");
        }

        (double[][] slices, int[] dims, int dim) = Cut(name, args[0], args.Count == 3 ? Count(name, args, 2, line, col) : null, line, col);

        var values = new double[slices.Length][];
        var places = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            double[] slice = slices[s];
            var order = new int[slice.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            // A missing reading is never among the k largest, so NaN sinks to the back whichever end
            // is being asked for — the same reading of NaN the reductions settled on in wave B.
            Array.Sort(order, (a, b) =>
            {
                double x = slice[a];
                double y = slice[b];
                int rank = double.IsNaN(x) ? (double.IsNaN(y) ? 0 : 1)
                    : double.IsNaN(y) ? -1
                    : largest ? y.CompareTo(x) : x.CompareTo(y);
                return rank != 0 ? rank : a.CompareTo(b);
            });

            int keep = Math.Min(wanted, slice.Length);
            values[s] = new double[keep];
            places[s] = new double[keep];
            for (int i = 0; i < keep; i++)
            {
                values[s][i] = slice[order[i]];
                places[s][i] = order[i] + dialect.IndexBase;
            }
        }

        (double[] picked, int[] shape) = JgsMatrix.JoinAlong(values, dims, dim);
        (double[] from, _) = JgsMatrix.JoinAlong(places, dims, dim);
        return Outputs(
            outputs,
            JgsMatrix.FromColumnMajorDims(picked, shape),
            JgsMatrix.FromColumnMajorDims(from, shape));
    }

    /// <summary>
    /// <c>histc(x, edges)</c> and <c>histc(x, edges, dim)</c>: how many values fall in each bin, per
    /// slice. Every bin is half open except the last, which counts only exact hits on the final edge.
    /// </summary>
    private static JgsValue BinCountsOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("histc", args, 2, 3, line, col);
        double[] edges = ToDoubles("histc", args[1], line, col);
        int[] dims = JgsMatrix.DimsOf(args[0]);
        int? named = args.Count == 3 ? Count("histc", args, 2, line, col) : null;

        // A vector has one slice whichever dimension is walked, and MATLAB reports its counts in the
        // edges' own orientation rather than the data's — so there is nothing to slice for.
        if (named is null && Array.FindAll(dims, static d => d != 1).Length <= 1)
        {
            double[] counts = InBins(FlattenColumnMajor("histc", args[0], line, col), edges);
            JgsValue answer = Numbers(counts);
            if (counts.Length > 1 && JgsMatrix.RowCount(args[1]) > 1)
            {
                answer.Reshape(counts.Length, 1);
            }

            return answer;
        }

        (double[][] slices, int[] shape, int dim) = Cut("histc", args[0], named, line, col);
        var counted = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            counted[s] = InBins(slices[s], edges);
        }

        (double[] joined, int[] result) = JgsMatrix.JoinAlong(counted, shape, dim);
        return JgsMatrix.FromColumnMajorDims(joined, result);
    }

    private static double[] InBins(double[] values, double[] edges)
    {
        var counts = new double[edges.Length];
        foreach (double value in values)
        {
            for (int b = edges.Length - 1; b >= 0; b--)
            {
                bool inside = b == edges.Length - 1
                    ? value == edges[b]
                    : value >= edges[b] && value < edges[b + 1];
                if (inside)
                {
                    counts[b]++;
                    break;
                }
            }
        }

        return counts;
    }

    /// <summary>The slices of a value along one dimension, defaulting to MATLAB's first non-singleton.</summary>
    private static (double[][] Slices, int[] Dims, int Dim) Cut(
        string name, JgsValue subject, int? named, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(subject);
        int dim = named ?? JgsMatrix.DefaultDim(dims);
        if (dim < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the dimension must be a positive whole number, but was {dim}.");
        }

        (double[][] slices, _) = JgsMatrix.SlicesAlong(FlattenColumnMajor(name, subject, line, col), dims, dim);
        return (slices, dims, dim);
    }

    // --- Tolerance-aware set operations -----------------------------------------------------------

    private static readonly OptionSpec UniqueTolOptions = new(
        "uniquetol",
        Flags: [],
        Names: ["ByRows", "DataScale", "OutputAllIndices"]);

    private static readonly OptionSpec IsMemberTolOptions = new(
        "ismembertol",
        Flags: [],
        Names: ["ByRows", "DataScale", "OutputAllIndices"]);

    /// <summary>
    /// The absolute distance two values may differ by and still count as one. MATLAB scales the
    /// tolerance by the largest magnitude in the data, so <c>1e-6</c> means "six significant figures"
    /// rather than a fixed distance; <c>'DataScale'</c> replaces that scale, per column when the
    /// comparison is by rows.
    /// </summary>
    private static double[] ToleranceScale(
        ParsedArgs parsed, double relative, double[] magnitudes, int columns, int line, int col)
    {
        var scale = new double[columns];
        double[]? given = parsed.Vector("DataScale");
        if (given is null)
        {
            double largest = 0;
            foreach (double value in magnitudes)
            {
                if (!double.IsNaN(value))
                {
                    largest = Math.Max(largest, Math.Abs(value));
                }
            }

            Array.Fill(scale, largest);
        }
        else if (given.Length == 1)
        {
            Array.Fill(scale, given[0]);
        }
        else if (given.Length == columns)
        {
            given.CopyTo(scale, 0);
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                $"'DataScale' takes one number or one per column, but got {given.Length} for {columns} columns.");
        }

        for (int c = 0; c < columns; c++)
        {
            scale[c] *= relative;
        }

        return scale;
    }

    /// <summary>The rows a tolerance-aware set operation compares: one row per item, one column each.</summary>
    private static (double[][] Rows, int Columns) ItemsOf(
        string name, JgsValue subject, bool byRows, int line, int col)
    {
        if (!byRows)
        {
            double[] flat = FlattenColumnMajor(name, subject, line, col);
            var singles = new double[flat.Length][];
            for (int i = 0; i < flat.Length; i++)
            {
                singles[i] = [flat[i]];
            }

            return (singles, 1);
        }

        if (subject.Type != JgsType.Array || subject.IsNd)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'ByRows' needs a matrix, which is the only shape that has rows to compare.");
        }

        int rows = JgsMatrix.RowCount(subject);
        int cols = JgsMatrix.ColCount(subject);
        var items = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            var row = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                JgsValue element = JgsMatrix.At(subject, r, c);
                row[c] = element.Type is JgsType.Number or JgsType.Bool
                    ? element.AsNumber
                    : throw new JgsRuntimeException(line, col, $"{name}: 'ByRows' needs a numeric matrix.");
            }

            items[r] = row;
        }

        return (items, cols);
    }

    /// <summary>Whether two items agree to within the per-column tolerance.</summary>
    private static bool WithinTolerance(double[] left, double[] right, double[] tolerance)
    {
        for (int c = 0; c < left.Length; c++)
        {
            if (!(Math.Abs(left[c] - right[c]) <= tolerance[c]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>[C, ia, ic] = uniquetol(A, tol, …)</c>: the distinct values once anything within the
    /// tolerance counts as the same reading. Values are visited in sorted order and each one either
    /// joins the group it is close enough to or starts a new one, so a run of gradually drifting
    /// values does not collapse into a single group.
    /// </summary>
    private static JgsValue[] UniqueWithinTolerance(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        ParsedArgs parsed = UniqueTolOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "uniquetol needs an array.");
        }

        bool byRows = parsed.Flag("ByRows", false);
        bool allIndices = parsed.Flag("OutputAllIndices", false);
        double relative = parsed.Positional.Count > 1
            ? Num("uniquetol", parsed.Positional, 1, line, col)
            : 1e-6;

        (double[][] items, int columns) = ItemsOf("uniquetol", parsed.Positional[0], byRows, line, col);
        double[] tolerance = ToleranceScale(parsed, relative, Flat(items), columns, line, col);

        var order = new int[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            for (int c = 0; c < columns; c++)
            {
                int compare = items[a][c].CompareTo(items[b][c]);
                if (compare != 0)
                {
                    return compare;
                }
            }

            return a.CompareTo(b);
        });

        var kept = new List<int>();
        var members = new List<List<int>>();
        var group = new int[items.Length];
        foreach (int at in order)
        {
            if (kept.Count > 0 && WithinTolerance(items[kept[^1]], items[at], tolerance))
            {
                members[^1].Add(at);
            }
            else
            {
                kept.Add(at);
                members.Add([at]);
            }

            group[at] = kept.Count - 1;
        }

        // The value reported for a group is its lowest-numbered member, which is what makes
        // uniquetol answer with values that are actually in A rather than a computed midpoint.
        var representative = new int[kept.Count];
        for (int g = 0; g < kept.Count; g++)
        {
            members[g].Sort();
            representative[g] = members[g][0];
        }

        JgsValue values = byRows
            ? RowsAsMatrix(items, representative, columns)
            : Vector(representative, i => items[i][0], JgsMatrix.RowCount(parsed.Positional[0]) > 1);

        JgsValue first = allIndices
            ? JgsValue.Cell([.. members.Select(m => IndexColumn([.. m], dialect))])
            : IndexColumn(representative, dialect);

        return Outputs(outputs, values, first, IndexColumn(group, dialect));
    }

    /// <summary>
    /// <c>[LIA, LOCB] = ismembertol(A, B, tol, …)</c>: whether each value of A is within the tolerance
    /// of something in B, and which member of B it matched.
    /// </summary>
    private static JgsValue[] MemberWithinTolerance(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        ParsedArgs parsed = IsMemberTolOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "ismembertol(a, b) needs both an array and a set.");
        }

        bool byRows = parsed.Flag("ByRows", false);
        bool allIndices = parsed.Flag("OutputAllIndices", false);
        double relative = parsed.Positional.Count > 2
            ? Num("ismembertol", parsed.Positional, 2, line, col)
            : 1e-6;

        (double[][] probes, int columns) = ItemsOf("ismembertol", parsed.Positional[0], byRows, line, col);
        (double[][] set, int setColumns) = ItemsOf("ismembertol", parsed.Positional[1], byRows, line, col);
        if (columns != setColumns)
        {
            throw new JgsRuntimeException(line, col,
                $"ismembertol: 'ByRows' compares rows of the same width, but got {columns} and {setColumns} columns.");
        }

        // The scale spans both arrays, because a tolerance that meant one thing on the left and
        // another on the right would make membership depend on which side a value was written.
        double[] tolerance = ToleranceScale(parsed, relative, [.. Flat(probes), .. Flat(set)], columns, line, col);

        var mask = new JgsValue[probes.Length];
        var found = new int[probes.Length];
        var all = new JgsValue[probes.Length];
        for (int i = 0; i < probes.Length; i++)
        {
            var matches = new List<int>();
            for (int j = 0; j < set.Length; j++)
            {
                if (WithinTolerance(probes[i], set[j], tolerance))
                {
                    matches.Add(j);
                }
            }

            mask[i] = JgsValue.Bool(matches.Count > 0);

            // MATLAB reports the lowest-numbered match, and 0 — never the dialect's first index —
            // for a value that matched nothing, because 0 is "no row" rather than a position.
            found[i] = matches.Count > 0 ? matches[0] + dialect.IndexBase : 0;
            all[i] = IndexColumn([.. matches], dialect);
        }

        JgsValue where = allIndices ? JgsValue.Cell(all) : Numbers([.. found.Select(static f => (double)f)]);
        return Outputs(outputs, JgsValue.Array(mask), where);
    }

    private static double[] Flat(double[][] items)
    {
        var all = new List<double>();
        foreach (double[] item in items)
        {
            all.AddRange(item);
        }

        return all.ToArray();
    }

    private static JgsValue Vector(int[] pick, Func<int, double> read, bool asColumn)
    {
        var flat = new double[pick.Length];
        for (int i = 0; i < pick.Length; i++)
        {
            flat[i] = read(pick[i]);
        }

        JgsValue value = Numbers(flat);
        if (asColumn && flat.Length > 1)
        {
            value.Reshape(flat.Length, 1);
        }

        return value;
    }

    private static JgsValue RowsAsMatrix(double[][] items, int[] pick, int columns)
    {
        var flat = new double[pick.Length * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < pick.Length; i++)
            {
                flat[(c * pick.Length) + i] = items[pick[i]][c];
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [pick.Length, columns]);
    }

    // --- Moving statistics ------------------------------------------------------------------------

    /// <summary>
    /// The nine sliding-window statistics, each the same walk over the same windows with a different
    /// summary at the end of it.
    /// </summary>
    /// <remarks>
    /// The window is <c>k</c> wide and centred, with the extra element behind the current one when
    /// <c>k</c> is even; <c>[nb nf]</c> says how far back and forward directly. <c>'Endpoints'</c>
    /// decides what an incomplete window at either end means — shrink it (the default), drop those
    /// points, fill them with NaN, or pad the missing places with a value. NaN inside the window is
    /// included by default, which is what <c>sum</c> and <c>mean</c> do with it and what these
    /// functions did before they could be asked.
    /// </remarks>
    private static void RegisterMovingStatistics(JgsEnvironment env)
    {
        void Moving(string name, double identity, Func<double[], double> statistic) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                OptionSpec spec = new(name, Flags: ["omitnan", "includenan"], Names: ["Endpoints", "SamplePoints"]);
                ParsedArgs parsed = spec.Parse(args, 3, line, col);
                if (parsed.Positional.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, $"{name}(x, k) needs an array and a window width.");
                }

                double[]? points = parsed.Vector("SamplePoints");
                if (points is not null && parsed.Named("Endpoints") is { Type: JgsType.Number or JgsType.Bool })
                {
                    // Padding means putting values at places outside the data, and sample points say
                    // that those places do not exist. The other three endpoint rules still mean what
                    // they meant, because each of them only ever asks whether a window was complete.
                    throw new JgsRuntimeException(line, col,
                        $"{name}: 'SamplePoints' places the values where they were sampled, so there is nowhere " +
                        "to pad; use 'shrink', 'discard' or 'fill'.");
                }

                (int behind, int ahead) = points is null
                    ? ReachOf(name, parsed.Positional[1], line, col)
                    : (0, 0);
                (double reachBehind, double reachAhead) = points is null
                    ? (behind, ahead)
                    : SpanOf(name, parsed.Positional[1], line, col);

                bool omitNan = parsed.OneOf("includenan", "includenan", "omitnan") == "omitnan";
                (string endpoints, double pad) = EndpointsOf(name, parsed, line, col);

                int? named = parsed.Positional.Count > 2 ? Count(name, parsed.Positional, 2, line, col) : null;
                (double[][] slices, int[] dims, int dim) = Cut(name, parsed.Positional[0], named, line, col);

                var windowed = new double[slices.Length][];
                for (int s = 0; s < slices.Length; s++)
                {
                    if (points is not null && points.Length != slices[s].Length)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{name}: 'SamplePoints' has {points.Length} places for {slices[s].Length} values.");
                    }

                    windowed[s] = points is null
                        ? Slide(slices[s], behind, ahead, endpoints, pad, omitNan, identity, statistic)
                        : SlideOverPoints(
                            slices[s], points, reachBehind, reachAhead, endpoints, omitNan, identity, statistic);
                }

                (double[] joined, int[] shape) = JgsMatrix.JoinAlong(windowed, dims, dim);
                return JgsMatrix.FromColumnMajorDims(joined, shape);
            })));

        Moving("movmean", double.NaN, static w => w.Average());
        Moving("movsum", 0, static w => w.Sum());
        Moving("movmax", double.NaN, static w => w.Max());
        Moving("movmin", double.NaN, static w => w.Min());
        Moving("movprod", 1, static w => w.Aggregate(1.0, static (product, x) => product * x));
        Moving("movmedian", double.NaN, MedianOf);
        Moving("movvar", double.NaN, SampleVarianceOf);
        Moving("movstd", double.NaN, static w => Math.Sqrt(SampleVarianceOf(w)));

        // The mean absolute deviation about the window's own mean.
        Moving("movmad", double.NaN, static w =>
        {
            double mean = w.Average();
            double total = 0;
            foreach (double x in w)
            {
                total += Math.Abs(x - mean);
            }

            return total / w.Length;
        });
    }

    /// <summary>How far the window reaches behind and ahead of the point it is centred on.</summary>
    private static (int Behind, int Ahead) ReachOf(string name, JgsValue width, int line, int col)
    {
        double[] size = NumericVector(name, width, line, col);
        if (size.Length == 2)
        {
            int behind = WholeCount(name, size[0], "window", line, col);
            int ahead = WholeCount(name, size[1], "window", line, col);
            if (behind < 0 || ahead < 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a [before after] window reaches zero or more places each way.");
            }

            return (behind, ahead);
        }

        if (size.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the window is one width or a [before after] pair, but got {size.Length} numbers.");
        }

        int window = WholeCount(name, size[0], "window", line, col);
        if (window < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the window must be at least 1 wide.");
        }

        // MATLAB centres an odd window on the point; an even one covers the current element and the
        // k/2 before it, so the extra element is behind.
        return (window / 2, (window - 1) / 2);
    }

    /// <summary>
    /// How far the window reaches when the values sit at named places rather than at 1, 2, 3. The
    /// width is then a distance along the sample points, not a count of elements: <c>movmean(x, 3,
    /// 'SamplePoints', t)</c> averages everything within 1.5 of each reading's own time, whether that
    /// is two readings or twenty.
    /// </summary>
    private static (double Behind, double Ahead) SpanOf(string name, JgsValue width, int line, int col)
    {
        double[] size = NumericVector(name, width, line, col);
        if (size.Length == 2)
        {
            if (size[0] < 0 || size[1] < 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a [before after] window reaches zero or more each way.");
            }

            return (size[0], size[1]);
        }

        if (size.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the window is one width or a [before after] pair, but got {size.Length} numbers.");
        }

        if (!(size[0] > 0))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the window must span more than nothing.");
        }

        return (size[0] / 2, size[0] / 2);
    }

    /// <summary>
    /// One slice window by window, where the window is a distance rather than a count. The endpoint
    /// rules mean what they always meant — a window is incomplete when it would reach past the first
    /// or last sample point — which is why 'shrink', 'discard' and 'fill' need no special case here.
    /// </summary>
    private static double[] SlideOverPoints(
        double[] values, double[] points, double behind, double ahead, string endpoints,
        bool omitNan, double identity, Func<double[], double> statistic)
    {
        var answers = new List<double>(values.Length);

        for (int i = 0; i < values.Length; i++)
        {
            bool complete = points[i] - behind >= points[0] && points[i] + ahead <= points[^1];
            if (!complete && endpoints == "discard")
            {
                continue;
            }

            if (!complete && endpoints == "fill")
            {
                answers.Add(double.NaN);
                continue;
            }

            var window = new List<double>();
            for (int j = 0; j < values.Length; j++)
            {
                if (points[j] < points[i] - behind || points[j] > points[i] + ahead)
                {
                    continue;
                }

                if (omitNan && double.IsNaN(values[j]))
                {
                    continue;
                }

                window.Add(values[j]);
            }

            answers.Add(window.Count == 0 ? identity : statistic([.. window]));
        }

        return [.. answers];
    }

    private static int WholeCount(string name, double value, string what, int line, int col) =>
        value == Math.Floor(value) && double.IsFinite(value)
            ? (int)value
            : throw new JgsRuntimeException(line, col, $"{name}: the {what} must be a whole number, but got {value}.");

    private static (string Endpoints, double Pad) EndpointsOf(
        string name, ParsedArgs parsed, int line, int col)
    {
        if (parsed.Named("Endpoints") is not { } given)
        {
            return ("shrink", double.NaN);
        }

        if (given.Type is JgsType.Number or JgsType.Bool)
        {
            return ("pad", given.AsNumber);
        }

        return (parsed.Word("Endpoints", "shrink", "shrink", "discard", "fill"), double.NaN);
    }

    /// <summary>One slice, window by window, through whichever summary the name asked for.</summary>
    private static double[] Slide(
        double[] values, int behind, int ahead, string endpoints, double pad,
        bool omitNan, double identity, Func<double[], double> statistic)
    {
        // 'discard' keeps only the points whose window fits inside the data, so the answer is shorter
        // than its input — the one endpoint rule that changes the length rather than the values.
        int from = endpoints == "discard" ? behind : 0;
        int to = endpoints == "discard" ? values.Length - 1 - ahead : values.Length - 1;
        var result = new double[Math.Max(0, to - from + 1)];

        for (int i = from; i <= to; i++)
        {
            int start = i - behind;
            int stop = i + ahead;
            bool complete = start >= 0 && stop < values.Length;
            if (!complete && endpoints == "fill")
            {
                result[i - from] = double.NaN;
                continue;
            }

            var window = new List<double>(stop - start + 1);
            for (int j = start; j <= stop; j++)
            {
                bool inside = j >= 0 && j < values.Length;
                if (inside)
                {
                    window.Add(values[j]);
                }
                else if (endpoints == "pad")
                {
                    window.Add(pad);
                }
            }

            if (omitNan)
            {
                window.RemoveAll(double.IsNaN);
            }

            // A window with nothing left in it is the statistic of nothing: 0 for a sum, 1 for a
            // product, NaN for anything that has to divide by how many values it saw.
            result[i - from] = window.Count == 0 ? identity : statistic(window.ToArray());
        }

        return result;
    }

    private static double MedianOf(double[] window)
    {
        var sorted = (double[])window.Clone();
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>The sample variance (dividing by n-1), which is what MATLAB's var and movvar report.</summary>
    private static double SampleVarianceOf(double[] window)
    {
        if (window.Length < 2)
        {
            return 0;
        }

        double mean = window.Average();
        double total = 0;
        foreach (double x in window)
        {
            total += (x - mean) * (x - mean);
        }

        return total / (window.Length - 1);
    }
}
