namespace JGraph.Scripting.Jgs;

/// <summary>
/// The array-level statistics and rearrangements (M38): the function-application family
/// (<c>arrayfun</c>, <c>bsxfun</c>, <c>structfun</c>), running and partial extremes
/// (<c>cummax</c>, <c>maxk</c>), the tolerance-aware set operations, and the nine <c>mov*</c>
/// windowed statistics.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the array and moving-statistics builtins (M38).</summary>
    private static void RegisterArrayBuiltins(JgsEnvironment env, Random random, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        RegisterApplication(Define);
        RegisterRunningStatistics(Define);
        RegisterSelectionAndSets(Define, dialect);
        RegisterRandomDraws(Define, random);
        RegisterRearrangements(Define);
        RegisterMovingStatistics(Define);
    }

    // --- Applying a function ----------------------------------------------------------------------

    private static void RegisterApplication(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("arrayfun", (args, line, col) =>
        {
            if (args.Count < 2 || args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "arrayfun(f, a) applies a function handle to each element.");
            }

            bool uniform = UniformOutputWanted(args, 2);
            var inputs = new List<JgsValue[]>();
            for (int i = 1; i < args.Count && args[i].Type != JgsType.String; i++)
            {
                inputs.Add(ElementsOf("arrayfun", args[i]));
            }

            int length = inputs[0].Length;
            foreach (JgsValue[] input in inputs)
            {
                if (input.Length != length)
                {
                    throw new JgsRuntimeException(line, col, "arrayfun needs every array to be the same length.");
                }
            }

            var results = new JgsValue[length];
            var call = new JgsValue[inputs.Count];
            for (int i = 0; i < length; i++)
            {
                for (int k = 0; k < inputs.Count; k++)
                {
                    call[k] = inputs[k][i];
                }

                results[i] = args[0].AsCallable.Call(call, line, col);
                if (uniform && results[i].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col,
                        "arrayfun: the function returned something that is not a number — pass 'UniformOutput', false.");
                }
            }

            if (!uniform)
            {
                return JgsValue.Cell(results);
            }

            // The uniform result takes the first array's shape — 2-D or N-D — the way MATLAB's does.
            return JgsMatrix.Like(args[1], JgsMatrix.FromElements(results, 1, results.Length));
        });

        Define("bsxfun", (args, line, col) =>
        {
            if (args.Count != 3 || args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "bsxfun(f, a, b) applies a function handle pairwise.");
            }

            // Singleton expansion is the one shape rule bsxfun exists to spell out; the same engine
            // now backs the elementwise operators, so bsxfun and '+' cannot disagree about a shape.
            IJgsCallable f = args[0].AsCallable;
            if (args[1].Type != JgsType.Array || args[2].Type != JgsType.Array)
            {
                JgsValue direct = f.Call([args[1], args[2]], line, col);
                return direct;
            }

            return JgsBroadcast.Map(args[1], args[2], "bsxfun", line, col,
                (a, b) => f.Call([a, b], line, col));
        });

        Define("structfun", (args, line, col) =>
        {
            if (args.Count < 2 || args[0].Type != JgsType.Function || args[1].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "structfun(f, s) applies a function handle to each field.");
            }

            bool uniform = UniformOutputWanted(args, 2);
            Dictionary<string, JgsValue> fields = args[1].AsStruct;
            var results = new List<JgsValue>();
            var names = new List<string>();
            foreach ((string field, JgsValue value) in fields)
            {
                names.Add(field);
                results.Add(args[0].AsCallable.Call([value], line, col));
            }

            if (uniform)
            {
                return JgsValue.Array(results.ToArray());
            }

            // Non-uniform structfun hands back a struct with the same field names, not a cell —
            // the one place the family's shape rule differs from cellfun's.
            var mapped = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                mapped[names[i]] = results[i];
            }

            return JgsValue.Struct(mapped);
        });

        Define("struct2cell", (args, line, col) =>
        {
            Arity("struct2cell", args, 1, line, col);
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, $"struct2cell expects a struct, but got a {args[0].TypeName}.");
            }

            return JgsValue.Cell(args[0].AsStruct.Values.ToArray());
        });

        Define("cell2struct", (args, line, col) =>
        {
            ArityRange("cell2struct", args, 2, 3, line, col);
            if (args[0].Type != JgsType.Cell || args[1].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "cell2struct(values, names) takes two cell arrays.");
            }

            JgsValue[] values = args[0].AsCell;
            JgsValue[] names = args[1].AsCell;
            if (values.Length != names.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"cell2struct got {values.Length} values for {names.Length} field names.");
            }

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, $"cell2struct: field name {i + 1} is a {names[i].TypeName}, not a string.");
                }

                fields[names[i].AsString] = values[i];
            }

            return JgsValue.Struct(fields);
        });
    }

    /// <summary>Reads the trailing 'UniformOutput' switch the apply family shares.</summary>
    private static bool UniformOutputWanted(IReadOnlyList<JgsValue> args, int from)
    {
        for (int i = from; i + 1 < args.Count; i++)
        {
            if (args[i].Type == JgsType.String
                && string.Equals(args[i].AsString, "UniformOutput", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].IsTruthy;
            }
        }

        return true;
    }

    /// <summary>A value's elements as a boxed list; a scalar is a one-element list.</summary>
    private static JgsValue[] ElementsOf(string name, JgsValue value) => value.Type switch
    {
        JgsType.Array => value.BoxedElements(),
        JgsType.Cell => value.AsCell,
        _ => [value],
    };

    // --- Running statistics -----------------------------------------------------------------------

    private static void RegisterRunningStatistics(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        void Running(string name, Func<double, double, double> pick) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                double[] values = ToDoubles(name, args[0], line, col);
                var running = new double[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    running[i] = i == 0 ? values[0] : pick(running[i - 1], values[i]);
                }

                return Numbers(running);
            });

        Running("cummax", Math.Max);
        Running("cummin", Math.Min);
    }

    // --- Selection and set membership -------------------------------------------------------------

    private static void RegisterSelectionAndSets(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define, JgsDialect dialect)
    {
        void Extremes(string name, bool largest) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 2, line, col);
                double[] values = ToDoubles(name, args[0], line, col);
                int k = Math.Min(Math.Max(0, Count(name, args, 1, line, col)), values.Length);
                var sorted = (double[])values.Clone();
                Array.Sort(sorted);
                if (largest)
                {
                    Array.Reverse(sorted);
                }

                return Numbers(sorted[..k]);
            });

        Extremes("maxk", largest: true);
        Extremes("mink", largest: false);

        Define("histc", (args, line, col) =>
        {
            Arity("histc", args, 2, line, col);
            double[] values = ToDoubles("histc", args[0], line, col);
            double[] edges = ToDoubles("histc", args[1], line, col);
            var counts = new double[edges.Length];
            foreach (double value in values)
            {
                for (int b = edges.Length - 1; b >= 0; b--)
                {
                    // The last bin counts only exact hits on the final edge; every other bin is
                    // half open. That asymmetry is histc's documented behaviour.
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

            return Numbers(counts);
        });

        Define("uniquetol", (args, line, col) =>
        {
            ArityRange("uniquetol", args, 1, 2, line, col);
            double[] values = ToDoubles("uniquetol", args[0], line, col);
            double tolerance = ToleranceFor(values, args, 1, line, col);
            var kept = new List<double>();
            foreach (double value in values.OrderBy(static v => v))
            {
                if (kept.Count == 0 || Math.Abs(value - kept[^1]) > tolerance)
                {
                    kept.Add(value);
                }
            }

            return Numbers(kept.ToArray());
        });

        Define("ismembertol", (args, line, col) =>
        {
            ArityRange("ismembertol", args, 2, 3, line, col);
            double[] probes = ToDoubles("ismembertol", args[0], line, col);
            double[] set = ToDoubles("ismembertol", args[1], line, col);
            double tolerance = ToleranceFor(set.Concat(probes).ToArray(), args, 2, line, col);
            var mask = new JgsValue[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                mask[i] = JgsValue.Bool(Array.Exists(set, s => Math.Abs(s - probes[i]) <= tolerance));
            }

            return JgsValue.Array(mask);
        });

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

        _ = dialect;
    }

    /// <summary>The absolute tolerance a tol-suffixed set operation uses, defaulting as MATLAB does.</summary>
    private static double ToleranceFor(double[] values, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double relative = index < args.Count ? Num("tolerance", args, index, line, col) : 1e-6;

        // MATLAB scales the tolerance by the largest magnitude in the data, so it means "this many
        // significant figures" rather than a fixed distance.
        double scale = 0;
        foreach (double value in values)
        {
            scale = Math.Max(scale, Math.Abs(value));
        }

        return relative * Math.Max(scale, 1.0);
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

    // --- Random draws -----------------------------------------------------------------------------

    private static void RegisterRandomDraws(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define, Random random)
    {
        Define("randi", (args, line, col) =>
        {
            ArityRange("randi", args, 1, int.MaxValue, line, col);

            // randi(imax) draws from 1..imax; randi([lo hi]) from the range it names.
            int low = 1;
            int high;
            if (args[0].Type == JgsType.Array)
            {
                double[] bounds = ToDoubles("randi", args[0], line, col);
                if (bounds.Length != 2)
                {
                    throw new JgsRuntimeException(line, col, "randi: a range is [low high].");
                }

                low = (int)bounds[0];
                high = (int)bounds[1];
            }
            else
            {
                high = Count("randi", args, 0, line, col);
            }

            if (high < low)
            {
                throw new JgsRuntimeException(line, col, "randi: the range is empty.");
            }

            if (args.Count == 1)
            {
                return JgsValue.Number(random.Next(low, high + 1));
            }

            // Everything after the range is the requested shape: (n), (r, c, …), or a size vector
            // (randi([0 1], size(A))), any number of dimensions.
            var sizeArgs = new JgsValue[args.Count - 1];
            for (int i = 1; i < args.Count; i++)
            {
                sizeArgs[i - 1] = args[i];
            }

            int[] dims = SquareDims("randi", sizeArgs, line, col);
            long total = 1;
            foreach (int dim in dims)
            {
                total *= dim;
            }

            if (Array.TrueForAll(dims, static d => d == 1))
            {
                return JgsValue.Number(random.Next(low, high + 1));
            }

            var flat = new double[total];
            for (int i = 0; i < flat.Length; i++)
            {
                flat[i] = random.Next(low, high + 1);
            }

            return JgsMatrix.FromColumnMajorDims(flat, dims);
        });

        Define("randperm", (args, line, col) =>
        {
            ArityRange("randperm", args, 1, 2, line, col);
            int n = Count("randperm", args, 0, line, col);
            int wanted = args.Count == 2 ? Count("randperm", args, 1, line, col) : n;
            if (wanted < 0 || wanted > n)
            {
                throw new JgsRuntimeException(line, col, $"randperm cannot take {wanted} values out of {n}.");
            }

            var order = new double[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i + 1; // randperm is 1-based in both dialects: it permutes 1..n by definition
            }

            for (int i = n - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return Numbers(order[..wanted]);
        });
    }

    // --- Rearrangement ----------------------------------------------------------------------------

    private static void RegisterRearrangements(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("circshift", (args, line, col) =>
        {
            Arity("circshift", args, 2, line, col);
            int by = Count("circshift", args, 1, line, col);

            if (IsMatrixValue(args[0]))
            {
                // A matrix shifts down its rows, matching MATLAB's default of the first dimension.
                double[,] a = RectOf("circshift", args[0], line, col);
                int rows = a.GetLength(0);
                int cols = a.GetLength(1);
                var shifted = new double[rows, cols];
                for (int r = 0; r < rows; r++)
                {
                    int source = ((r - by) % rows + rows) % rows;
                    for (int c = 0; c < cols; c++)
                    {
                        shifted[r, c] = a[source, c];
                    }
                }

                return FromRect(shifted);
            }

            double[] values = ToDoubles("circshift", args[0], line, col);
            if (values.Length == 0)
            {
                return args[0];
            }

            var result = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = values[((i - by) % values.Length + values.Length) % values.Length];
            }

            return Numbers(result);
        });

        Define("rot90", (args, line, col) =>
        {
            ArityRange("rot90", args, 1, 2, line, col);
            double[,] a = RectOf("rot90", args[0], line, col);
            int quarters = ((args.Count == 2 ? Count("rot90", args, 1, line, col) : 1) % 4 + 4) % 4;
            for (int turn = 0; turn < quarters; turn++)
            {
                int rows = a.GetLength(0);
                int cols = a.GetLength(1);
                var turned = new double[cols, rows];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        // One counter-clockwise quarter turn: the last column becomes the first row.
                        turned[cols - 1 - c, r] = a[r, c];
                    }
                }

                a = turned;
            }

            return FromRect(a);
        });

        Define("accumarray", (args, line, col) =>
        {
            ArityRange("accumarray", args, 2, 5, line, col);
            double[] subscripts = ToDoubles("accumarray", args[0], line, col);
            double[] values = args[1].Type is JgsType.Number or JgsType.Bool
                ? Enumerable.Repeat(args[1].AsNumber, subscripts.Length).ToArray()
                : ToDoubles("accumarray", args[1], line, col);
            if (values.Length != subscripts.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"accumarray got {subscripts.Length} subscripts for {values.Length} values.");
            }

            int size = 0;
            foreach (double subscript in subscripts)
            {
                if (subscript < 1 || subscript != Math.Floor(subscript))
                {
                    throw new JgsRuntimeException(line, col, "accumarray subscripts are whole numbers from 1 up.");
                }

                size = Math.Max(size, (int)subscript);
            }

            if (args.Count >= 3 && args[2].Type is JgsType.Number or JgsType.Bool)
            {
                size = Math.Max(size, (int)args[2].AsNumber);
            }
            else if (args.Count >= 3 && args[2].Type == JgsType.Array)
            {
                double[] shape = ToDoubles("accumarray", args[2], line, col);
                size = Math.Max(size, (int)shape[0]);
            }

            // With a function handle each bin's values are collected and handed over together;
            // without one the bins simply sum, which is what accumarray is nearly always used for.
            IJgsCallable? reducer = args.Count >= 4 && args[3].Type == JgsType.Function ? args[3].AsCallable : null;
            double fill = args.Count >= 5 ? Num("accumarray", args, 4, line, col) : 0;

            if (reducer is null)
            {
                var sums = new double[size];
                Array.Fill(sums, fill);
                var touched = new bool[size];
                for (int i = 0; i < subscripts.Length; i++)
                {
                    int bin = (int)subscripts[i] - 1;
                    sums[bin] = touched[bin] ? sums[bin] + values[i] : values[i];
                    touched[bin] = true;
                }

                return Numbers(sums);
            }

            var buckets = new List<double>[size];
            for (int i = 0; i < subscripts.Length; i++)
            {
                int bin = (int)subscripts[i] - 1;
                (buckets[bin] ??= new List<double>()).Add(values[i]);
            }

            var reduced = new double[size];
            for (int bin = 0; bin < size; bin++)
            {
                if (buckets[bin] is null)
                {
                    reduced[bin] = fill;
                    continue;
                }

                JgsValue outcome = reducer.Call([Numbers(buckets[bin]!.ToArray())], line, col);
                reduced[bin] = outcome.Type is JgsType.Number or JgsType.Bool
                    ? outcome.AsNumber
                    : throw new JgsRuntimeException(line, col, "accumarray: the function must return one number per bin.");
            }

            return Numbers(reduced);
        });
    }

    // --- Moving statistics ------------------------------------------------------------------------

    private static void RegisterMovingStatistics(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        void Moving(string name, Func<double[], double> statistic) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 2, line, col);
                double[] values = ToDoubles(name, args[0], line, col);
                int window = Count(name, args, 1, line, col);
                if (window < 1)
                {
                    throw new JgsRuntimeException(line, col, $"{name}: the window must be at least 1 wide.");
                }

                // MATLAB centres an odd window on the point; an even one covers the current element
                // and the k/2 before it, so the extra element is behind. The window shrinks at the
                // ends rather than padding with zeros.
                int behind = window / 2;
                int ahead = (window - 1) / 2;
                var result = new double[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    int from = Math.Max(0, i - behind);
                    int to = Math.Min(values.Length - 1, i + ahead);
                    result[i] = statistic(values[from..(to + 1)]);
                }

                return Numbers(result);
            });

        Moving("movmean", static w => w.Average());
        Moving("movsum", static w => w.Sum());
        Moving("movmax", static w => w.Max());
        Moving("movmin", static w => w.Min());
        Moving("movprod", static w => w.Aggregate(1.0, static (product, x) => product * x));
        Moving("movmedian", MedianOf);
        Moving("movvar", SampleVarianceOf);
        Moving("movstd", static w => Math.Sqrt(SampleVarianceOf(w)));

        // The mean absolute deviation about the window's own mean.
        Moving("movmad", static w =>
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
