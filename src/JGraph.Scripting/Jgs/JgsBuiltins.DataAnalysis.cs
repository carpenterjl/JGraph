using JGraph.Maths;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The data-analysis names the base language was missing (M52 wave E): differentiation and
/// integration of sampled data (<c>gradient</c>, <c>trapz</c>, <c>cumtrapz</c>), resampling
/// (<c>interp1</c>), polynomial fitting (<c>polyfit</c>, <c>polyval</c>'s error estimate),
/// ordering and binning (<c>sortrows</c>, <c>histcounts</c>), the second-moment statistics
/// (<c>corrcoef</c>, <c>cov</c>, <c>rms</c>, <c>bounds</c>), and the two names that had a
/// signature rather than a hole — <c>linspace</c> without a count and <c>round</c> with one.
/// </summary>
/// <remarks>
/// These are registered late, after every other builtin file, because three of them replace a
/// name that already exists: <c>linspace</c> gains its optional count, <c>round</c> graduates from
/// the one-argument element-wise table, and <c>polyval</c> gains the second output that turns a
/// fit into an error bar. They are registered before the MATLAB reductions, so <c>rms</c> is
/// wrapped for a dimension exactly the way <c>mean</c> is rather than needing its own copy of that
/// machinery.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the data-analysis builtins, replacing three earlier registrations.</summary>
    private static void RegisterDataAnalysisBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        Define("linspace", EvenlySpaced);
        Define("round", RoundValues);
        Define("rms", RootMeanSquare);

        DefineBoth("gradient", SlopeFields);
        Define("trapz", (args, line, col) => Integrate("trapz", args, cumulative: false, line, col));
        Define("cumtrapz", (args, line, col) => Integrate("cumtrapz", args, cumulative: true, line, col));
        Define("interp1", (args, line, col) => Interpolate(args, dialect, line, col));

        DefineBoth("polyfit", PolynomialFit);
        DefineBoth("polyval", PolynomialValue);

        DefineBoth("sortrows", (args, wanted, line, col) => SortedRows(args, wanted, dialect, line, col));
        DefineBoth("histcounts", (args, wanted, line, col) => BinCounts(args, wanted, dialect, line, col));

        DefineBoth("corrcoef", Correlations);
        Define("cov", Covariance);
        DefineBoth("bounds", (args, wanted, line, col) => Extremes(env, args, wanted, line, col));
    }

    // --- Generation and rounding ------------------------------------------------------------------

    /// <summary>
    /// <c>linspace(a, b)</c>, <c>linspace(a, b, n)</c>: evenly spaced values from a to b. The count
    /// defaults to 100, and the last value is b exactly rather than whatever the arithmetic left.
    /// </summary>
    private static JgsValue EvenlySpaced(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("linspace", args, 2, 3, line, col);
        double start = Num("linspace", args, 0, line, col);
        double stop = Num("linspace", args, 1, line, col);
        int count = args.Count == 3 ? Count("linspace", args, 2, line, col) : 100;

        if (count < 1)
        {
            return Numbers([]);
        }

        // One point is the far end, not the near one — asking for a single sample of a range means
        // asking where it finishes.
        if (count == 1)
        {
            return Numbers([stop]);
        }

        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = start + ((stop - start) * i / (count - 1));
        }

        result[^1] = stop;
        return Numbers(result);
    }

    /// <summary>
    /// <c>round(x)</c>, <c>round(x, n)</c>, <c>round(x, n, 'significant')</c>: halves go away from
    /// zero, and n says how many places — after the point by default, or in total.
    /// </summary>
    private static JgsValue RoundValues(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("round", args, 1, 3, line, col);

        // This declaration wins over the one-argument round registered beside floor and ceil, so the
        // complex arm has to be here too or a complex number reaches only the version that refuses it
        // (M81). Both places round both parts, which is MATLAB's answer.
        if (args.Count == 1)
        {
            return MapComplexProducing("round", args[0], RoundAwayFromZero, Always,
                static z => JgsValue.ComplexNum(Componentwise(z, RoundAwayFromZero)), line, col);
        }

        int digits = Count("round", args, 1, line, col);
        bool significant = args.Count == 3
            && OneWord("round", args, 2, line, col, "decimals", "significant") == "significant";
        if (significant && digits < 1)
        {
            throw new JgsRuntimeException(line, col, "round: 'significant' needs at least one digit.");
        }

        return MapComplexProducing("round", args[0], x => RoundTo(x, digits, significant), Always,
            z => JgsValue.ComplexNum(Componentwise(z, x => RoundTo(x, digits, significant))), line, col);
    }

    /// <summary>One value rounded to a number of decimal places, or of significant digits.</summary>
    private static double RoundTo(double value, int digits, bool significant)
    {
        if (value == 0 || !double.IsFinite(value))
        {
            return value;
        }

        // Significant digits are decimal places measured from the leading digit rather than from the
        // point, so the two readings differ only in where the shift starts.
        int shift = significant
            ? digits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value)))
            : digits;

        if (shift > 15)
        {
            return value; // finer than a double distinguishes, so there is nothing to drop
        }

        if (shift >= 0)
        {
            return Math.Round(value, shift, MidpointRounding.AwayFromZero);
        }

        double scale = Math.Pow(10, -shift);
        return Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
    }

    /// <summary>The root mean square of every value, which the MATLAB reductions wrap for a dimension.</summary>
    private static JgsValue RootMeanSquare(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("rms", args, 1, line, col);
        double[] values = FlattenColumnMajor("rms", args[0], line, col);
        if (values.Length == 0)
        {
            return JgsValue.Number(double.NaN);
        }

        double total = 0;
        foreach (double value in values)
        {
            total += value * value;
        }

        return JgsValue.Number(Math.Sqrt(total / values.Length));
    }

    /// <summary>
    /// <c>[S, L] = bounds(X, …)</c>: the smallest and largest, which is <c>min</c> and <c>max</c>
    /// asked the same question. The empty slot is where the extremes keep the other array they might
    /// have been comparing against, so a dimension or <c>'all'</c> lands where they expect it.
    /// </summary>
    private static JgsValue[] Extremes(
        JgsEnvironment env, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("bounds", args, 1, 3, line, col);
        var forwarded = new List<JgsValue> { args[0], JgsValue.Array([]) };
        for (int i = 1; i < args.Count; i++)
        {
            forwarded.Add(args[i]);
        }

        JgsValue[] passed = [.. forwarded];
        return Outputs(
            wanted,
            CallBuiltin(env, "bounds", "min", passed, line, col),
            CallBuiltin(env, "bounds", "max", passed, line, col));
    }

    // --- Differentiation and integration of sampled data ------------------------------------------

    /// <summary>
    /// <c>[FX, FY, FZ, …] = gradient(F, hx, hy, hz, …)</c>: central differences inside, one-sided at
    /// the ends. A vector has one gradient, taken along itself whichever way round it is written; a
    /// matrix has one across its columns and one down its rows; and an array of more dimensions has
    /// one along each, in MATLAB's order — across, down, then page by page.
    /// </summary>
    private static JgsValue[] SlopeFields(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        int[] dims = SizeDims(args[0]);
        ArityRange("gradient", args, 1, System.Math.Max(3, dims.Length + 1), line, col);
        if (dims.Length > 2)
        {
            return VolumeSlopeFields(args, dims, wanted, line, col);
        }

        double[] flat = FlattenColumnMajor("gradient", args[0], line, col);
        int rows = dims[0];
        int columns = dims.Length > 1 ? dims[1] : 1;

        if (rows == 1 || columns == 1)
        {
            double[] at = args.Count > 1 ? Positions("gradient", args[1], flat.Length, line, col) : Steps(flat.Length);
            JgsValue along = JgsMatrix.FromColumnMajorDims(Slopes(flat, at), dims);

            // The second gradient of a vector is the change across its one row or column, which is
            // none — MATLAB reports zeros rather than refusing.
            return wanted <= 1
                ? [along]
                : [along, JgsMatrix.FromColumnMajorDims(new double[flat.Length], dims)];
        }

        double[] x = args.Count > 1 ? Positions("gradient", args[1], columns, line, col) : Steps(columns);
        double[] y = args.Count > 2 ? Positions("gradient", args[2], rows, line, col) : Steps(rows);

        var across = new double[flat.Length];
        for (int r = 0; r < rows; r++)
        {
            var row = new double[columns];
            for (int c = 0; c < columns; c++)
            {
                row[c] = flat[r + (c * rows)];
            }

            double[] slope = Slopes(row, x);
            for (int c = 0; c < columns; c++)
            {
                across[r + (c * rows)] = slope[c];
            }
        }

        JgsValue horizontal = JgsMatrix.FromColumnMajorDims(across, dims);
        if (wanted <= 1)
        {
            return [horizontal];
        }

        var down = new double[flat.Length];
        for (int c = 0; c < columns; c++)
        {
            var column = new double[rows];
            Array.Copy(flat, c * rows, column, 0, rows);
            double[] slope = Slopes(column, y);
            Array.Copy(slope, 0, down, c * rows, rows);
        }

        return [horizontal, JgsMatrix.FromColumnMajorDims(down, dims)];
    }

    /// <summary>
    /// The gradient of an array of three or more dimensions: one field per dimension, each the same
    /// central-difference walk the matrix case does, taken along one direction at a time.
    /// </summary>
    /// <remarks>
    /// The outputs come in MATLAB's order, which is not the order of the dimensions: the first is
    /// along the columns (dimension 2), the second down the rows (dimension 1), and the rest follow
    /// the dimensions from the third onward. That swap is why the walk is written against a dimension
    /// number rather than against rows and columns — <c>curl</c> and <c>divergence</c> ask for the
    /// slope along a named direction, and getting the first two the wrong way round would turn a
    /// field inside out with nothing to show for it.
    /// </remarks>
    private static JgsValue[] VolumeSlopeFields(
        IReadOnlyList<JgsValue> args, int[] dims, int wanted, int line, int col)
    {
        double[] flat = FlattenColumnMajor("gradient", args[0], line, col);
        int[] order = MatlabDimensionOrder(dims.Length);
        int answers = System.Math.Clamp(wanted, 1, dims.Length);

        var fields = new JgsValue[answers];
        for (int i = 0; i < answers; i++)
        {
            int dimension = order[i];
            double[] at = args.Count > i + 1
                ? Positions("gradient", args[i + 1], dims[dimension], line, col)
                : Steps(dims[dimension]);
            fields[i] = JgsMatrix.FromColumnMajorDims(SlopesAlong(flat, dims, dimension, at), dims);
        }

        return fields;
    }

    /// <summary>
    /// The order MATLAB reports gradients in: columns first, then rows, then every later dimension in
    /// its own order.
    /// </summary>
    private static int[] MatlabDimensionOrder(int count)
    {
        var order = new int[count];
        order[0] = 1;
        order[1] = 0;
        for (int i = 2; i < count; i++)
        {
            order[i] = i;
        }

        return order;
    }

    /// <summary>
    /// The slope along one dimension of a column-major array, by pulling out each line that runs
    /// along that dimension, differencing it, and putting it back.
    /// </summary>
    private static double[] SlopesAlong(double[] flat, int[] dims, int dimension, double[] at)
    {
        int length = dims[dimension];
        var slopes = new double[flat.Length];

        // How far apart two neighbours along this dimension sit in the flat array, and how many such
        // lines there are.
        int stride = 1;
        for (int i = 0; i < dimension; i++)
        {
            stride *= dims[i];
        }

        int lines = flat.Length / System.Math.Max(1, length);
        int inner = stride;
        int outer = lines / System.Math.Max(1, inner);

        var line1 = new double[length];
        for (int o = 0; o < outer; o++)
        {
            for (int i = 0; i < inner; i++)
            {
                int start = (o * stride * length) + i;
                for (int k = 0; k < length; k++)
                {
                    line1[k] = flat[start + (k * stride)];
                }

                double[] slope = Slopes(line1, at);
                for (int k = 0; k < length; k++)
                {
                    slopes[start + (k * stride)] = slope[k];
                }
            }
        }

        return slopes;
    }

    /// <summary>Central differences over the two-step span, one-sided at each end.</summary>
    private static double[] Slopes(double[] values, double[] at)
    {
        int n = values.Length;
        var slope = new double[n];
        if (n < 2)
        {
            return slope; // nothing to difference against
        }

        slope[0] = (values[1] - values[0]) / (at[1] - at[0]);
        slope[n - 1] = (values[n - 1] - values[n - 2]) / (at[n - 1] - at[n - 2]);
        for (int i = 1; i < n - 1; i++)
        {
            slope[i] = (values[i + 1] - values[i - 1]) / (at[i + 1] - at[i - 1]);
        }

        return slope;
    }

    /// <summary>Where n samples sit: a spacing given as one number, or the positions themselves.</summary>
    private static double[] Positions(string name, JgsValue given, int n, int line, int col)
    {
        if (given.Type is JgsType.Number or JgsType.Bool)
        {
            double step = given.AsNumber;
            var uniform = new double[n];
            for (int i = 0; i < n; i++)
            {
                uniform[i] = i * step;
            }

            return uniform;
        }

        double[] at = ToDoubles(name, given, line, col);
        if (at.Length != n)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the sample positions must be one spacing or {n} coordinates, but got {at.Length}.");
        }

        return at;
    }

    private static double[] Steps(int n)
    {
        var at = new double[n];
        for (int i = 0; i < n; i++)
        {
            at[i] = i;
        }

        return at;
    }

    /// <summary>
    /// <c>trapz</c> and <c>cumtrapz</c> in every documented shape: <c>(Y)</c>, <c>(Y, dim)</c>,
    /// <c>(X, Y)</c>, <c>(X, Y, dim)</c>. Two arguments are ambiguous on their face, and MATLAB's
    /// rule is the one used here — a single number in the second slot is a dimension, because
    /// sample positions never arrive as one.
    /// </summary>
    private static JgsValue Integrate(
        string name, IReadOnlyList<JgsValue> args, bool cumulative, int line, int col)
    {
        ArityRange(name, args, 1, 3, line, col);

        JgsValue subject;
        JgsValue? sample = null;
        int? named = null;
        if (args.Count == 1)
        {
            subject = args[0];
        }
        else if (args.Count == 2)
        {
            if (args[1].Type is JgsType.Number or JgsType.Bool)
            {
                subject = args[0];
                named = Count(name, args, 1, line, col);
            }
            else
            {
                sample = args[0];
                subject = args[1];
            }
        }
        else
        {
            sample = args[0];
            subject = args[1];
            named = Count(name, args, 2, line, col);
        }

        (double[][] slices, int[] dims, int dim) = Cut(name, subject, named, line, col);
        int length = slices.Length == 0 ? 0 : slices[0].Length;
        double[][] coordinates = SampleGrid(name, sample, subject, length, slices.Length, dim, line, col);

        var results = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            results[s] = cumulative
                ? RunningArea(slices[s], coordinates[s])
                : [TotalArea(slices[s], coordinates[s])];
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(results, dims, dim);
        return ShapedNumbers(joined, shape);
    }

    /// <summary>
    /// A numeric result in the shape it was computed in — as a plain number when a single one is
    /// left, which is what integrating or reducing a vector answers.
    /// </summary>
    private static JgsValue ShapedNumbers(double[] flat, IReadOnlyList<int> dims) =>
        flat.Length == 1 ? JgsValue.Number(flat[0]) : JgsMatrix.FromColumnMajorDims(flat, dims);

    /// <summary>
    /// The sample positions each slice is integrated against: none (unit spacing), one spacing, one
    /// coordinate vector shared by every slice, or a whole array cut the same way as the data.
    /// </summary>
    private static double[][] SampleGrid(
        string name, JgsValue? sample, JgsValue subject, int length, int slices, int dim, int line, int col)
    {
        double[] shared;
        if (sample is null)
        {
            shared = Steps(length);
        }
        else if (sample.Type is JgsType.Number or JgsType.Bool)
        {
            shared = Positions(name, sample, length, line, col);
        }
        else if (SizeDims(sample).AsSpan().SequenceEqual(SizeDims(subject)))
        {
            // X the same size as Y means one set of positions per slice, which is the same cut.
            (double[][] cut, _, _) = Cut(name, sample, dim, line, col);
            return cut;
        }
        else
        {
            shared = Positions(name, sample, length, line, col);
        }

        var every = new double[slices][];
        for (int s = 0; s < slices; s++)
        {
            every[s] = shared;
        }

        return every;
    }

    private static double TotalArea(double[] values, double[] at)
    {
        double total = 0;
        for (int i = 0; i < values.Length - 1; i++)
        {
            total += (at[i + 1] - at[i]) * (values[i] + values[i + 1]) / 2;
        }

        return total;
    }

    private static double[] RunningArea(double[] values, double[] at)
    {
        var running = new double[values.Length];
        for (int i = 1; i < values.Length; i++)
        {
            running[i] = running[i - 1] + ((at[i] - at[i - 1]) * (values[i - 1] + values[i]) / 2);
        }

        return running;
    }

    // --- Resampling -------------------------------------------------------------------------------

    private static readonly string[] Interp1Methods =
        ["linear", "nearest", "next", "previous", "pchip", "cubic", "spline", "makima", "v5cubic"];

    /// <summary>
    /// <c>interp1(x, v, xq, method, extrapolation)</c> in all of its documented shapes. The sample
    /// positions may be left out (the values are then taken as evenly spaced), the method defaults
    /// to <c>'linear'</c>, and the tail is either <c>'extrap'</c> or the value to fill outside with.
    /// </summary>
    /// <remarks>
    /// <c>'makima'</c> and <c>'v5cubic'</c> are refused by name rather than silently substituted:
    /// each is a different curve, and answering with a different one would be wrong quietly. The
    /// default fill outside the samples is NaN for the piecewise-linear family and extrapolation for
    /// the cubics, which is what MATLAB does and the one place the method changes more than shape.
    /// </remarks>
    private static JgsValue Interpolate(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        ArityRange("interp1", args, 2, 5, line, col);

        int numeric = 0;
        while (numeric < args.Count && args[numeric].Type != JgsType.String)
        {
            numeric++;
        }

        if (numeric is not (2 or 3))
        {
            throw new JgsRuntimeException(line, col,
                "interp1 takes interp1(x, v, xq) or interp1(v, xq), then an optional method and extrapolation.");
        }

        JgsValue values = args[numeric - 2];
        JgsValue queries = args[numeric - 1];
        double[] samples;
        if (numeric == 3)
        {
            samples = FlattenColumnMajor("interp1", args[0], line, col);
        }
        else
        {
            // Left out, the sample positions are the value's own places, counted the way this
            // dialect counts: 1..n under MATLAB, 0..n-1 under JGS.
            int count = SizeDims(values).Max();
            samples = new double[count];
            for (int i = 0; i < count; i++)
            {
                samples[i] = i + dialect.IndexBase;
            }
        }

        string method = numeric < args.Count
            ? OneWord("interp1", args, numeric, line, col, Interp1Methods)
            : "linear";
        if (method is "makima" or "v5cubic")
        {
            throw new JgsRuntimeException(line, col,
                $"interp1: '{method}' is not available here; 'spline' and 'pchip' are the cubics that are.");
        }

        if (method == "cubic")
        {
            method = "pchip"; // MATLAB's own alias since R2020b
        }

        bool extrapolate = method is "spline" or "pchip";
        double outside = double.NaN;
        if (numeric + 1 < args.Count)
        {
            JgsValue tail = args[numeric + 1];
            if (tail.Type == JgsType.String)
            {
                if (!string.Equals(tail.AsString, "extrap", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"interp1: '{tail.AsString}' is not an extrapolation; say 'extrap' or a fill value.");
                }

                extrapolate = true;
            }
            else
            {
                outside = Num("interp1", args, numeric + 1, line, col);
                extrapolate = false;
            }
        }

        if (args.Count > numeric + 2)
        {
            throw new JgsRuntimeException(line, col,
                $"interp1 expects at most {numeric + 2} argument(s) in this form, but got {args.Count}.");
        }

        return Resample(samples, values, queries, method, extrapolate, outside, line, col);
    }

    /// <summary>Interpolates one data set, or each column of a matrix of them.</summary>
    private static JgsValue Resample(
        double[] samples, JgsValue values, JgsValue queries, string method,
        bool extrapolate, double outside, int line, int col)
    {
        int n = samples.Length;
        if (n < 2)
        {
            throw new JgsRuntimeException(line, col, "interp1 needs at least two samples.");
        }

        int[] shape = SizeDims(values);
        double[] flat = FlattenColumnMajor("interp1", values, line, col);
        bool vector = shape.Length <= 2 && shape.Any(static d => d == 1);
        int sets;
        if (vector && flat.Length == n)
        {
            sets = 1;
        }
        else if (shape.Length == 2 && shape[0] == n)
        {
            sets = shape[1];
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                $"interp1: the values must be a vector of {n} or a matrix with {n} rows.");
        }

        double[] at = FlattenColumnMajor("interp1", queries, line, col);
        int[] order = SortedOrder(samples, line, col);
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = samples[order[i]];
        }

        var answer = new double[at.Length * sets];
        for (int set = 0; set < sets; set++)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                y[i] = flat[(set * n) + order[i]];
            }

            double[] slopes = method switch
            {
                "spline" => Interpolation.SplineSlopes(x, y),
                "pchip" => Interpolation.PchipSlopes(x, y),
                _ => [],
            };

            for (int q = 0; q < at.Length; q++)
            {
                answer[(set * at.Length) + q] =
                    ValueAt(x, y, slopes, method, at[q], extrapolate, outside);
            }
        }

        return sets == 1
            ? ShapedNumbers(answer, SizeDims(queries))
            : JgsMatrix.FromColumnMajor(answer, at.Length, sets);
    }

    /// <summary>One query point, by whichever rule the method names.</summary>
    private static double ValueAt(
        double[] x, double[] y, double[] slopes, string method, double at, bool extrapolate, double outside)
    {
        int n = x.Length;
        if (double.IsNaN(at))
        {
            return double.NaN;
        }

        if ((at < x[0] || at > x[n - 1]) && !extrapolate)
        {
            return outside;
        }

        // The interval the point falls in, clamped so an extrapolated point continues the end piece.
        int i = Bracket(x, at);

        return method switch
        {
            // Exactly halfway between two samples takes the later one, because MATLAB's nearest
            // rounds the fractional index away from zero rather than down.
            "nearest" => at - x[i] < x[i + 1] - at ? y[i] : y[i + 1],
            "previous" => at >= x[i + 1] ? y[i + 1] : at < x[i] ? double.NaN : y[i],
            "next" => at <= x[i] ? y[i] : at > x[i + 1] ? double.NaN : y[i + 1],
            "spline" or "pchip" => Interpolation.Hermite(x[i], x[i + 1], y[i], y[i + 1], slopes[i], slopes[i + 1], at),
            _ => y[i] + ((y[i + 1] - y[i]) * (at - x[i]) / (x[i + 1] - x[i])),
        };
    }

    /// <summary>The index of the interval containing a point, clamped to the ends.</summary>
    private static int Bracket(double[] x, double at)
    {
        int low = 0;
        int high = x.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (at < x[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    /// <summary>The order that sorts the samples, refusing a repeated position.</summary>
    private static int[] SortedOrder(double[] samples, int line, int col)
    {
        var order = new int[samples.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => samples[a].CompareTo(samples[b]));
        for (int i = 1; i < order.Length; i++)
        {
            if (samples[order[i]] == samples[order[i - 1]])
            {
                throw new JgsRuntimeException(line, col,
                    "interp1: the sample positions must all be different.");
            }
        }

        return order;
    }

    // --- Polynomial fitting -----------------------------------------------------------------------

    /// <summary>
    /// <c>[p, S, mu] = polyfit(x, y, n)</c>: the least-squares polynomial, the factorization that
    /// sizes its error bars, and — when asked for — the centring and scaling that keeps a
    /// high-degree fit conditioned.
    /// </summary>
    private static JgsValue[] PolynomialFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("polyfit", args, 3, line, col);
        double[] x = FlattenColumnMajor("polyfit", args[0], line, col);
        double[] y = FlattenColumnMajor("polyfit", args[1], line, col);
        int order = Count("polyfit", args, 2, line, col);

        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"polyfit: x and y must be the same length, but got {x.Length} and {y.Length}.");
        }

        if (order < 0)
        {
            throw new JgsRuntimeException(line, col, "polyfit: the degree cannot be negative.");
        }

        int terms = order + 1;
        if (x.Length < terms)
        {
            throw new JgsRuntimeException(line, col,
                $"polyfit needs at least {terms} points for a degree-{order} fit, but got {x.Length}.");
        }

        double centre = 0;
        double scale = 1;
        if (wanted >= 3)
        {
            centre = Average(x);
            scale = Math.Sqrt(JgsStdlib.Variance(x.Length > 1 ? x : [x[0], x[0]]));
            if (scale == 0 || !double.IsFinite(scale))
            {
                scale = 1; // every point in the same place: there is nothing to scale by
            }

            x = Rescaled(x, centre, scale);
        }

        double[,] design = Vandermonde(x, terms);
        QrDecomposition qr = QrDecomposition.Factor(design);

        var target = new double[y.Length, 1];
        for (int r = 0; r < y.Length; r++)
        {
            target[r, 0] = y[r];
        }

        double[,] solved;
        try
        {
            solved = qr.SolveColumns(target);
        }
        catch (InvalidOperationException)
        {
            throw new JgsRuntimeException(line, col,
                "polyfit: the fit is rank deficient — the points do not pin down a polynomial of that degree.");
        }

        var coefficients = new double[terms];
        for (int i = 0; i < terms; i++)
        {
            coefficients[i] = solved[i, 0];
        }

        if (wanted <= 1)
        {
            return [Numbers(coefficients)];
        }

        double residual = 0;
        for (int r = 0; r < y.Length; r++)
        {
            double fitted = 0;
            for (int c = 0; c < terms; c++)
            {
                fitted += design[r, c] * coefficients[c];
            }

            residual += (y[r] - fitted) * (y[r] - fitted);
        }

        double[,] triangular = qr.R;
        var statistics = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["R"] = JgsMatrix.Build(terms, terms, (r, c) => triangular[r, c]),
            ["df"] = JgsValue.Number(y.Length - terms),
            ["normr"] = JgsValue.Number(Math.Sqrt(residual)),
        });

        return wanted >= 3
            ? [Numbers(coefficients), statistics, Numbers([centre, scale])]
            : [Numbers(coefficients), statistics];
    }

    /// <summary>
    /// <c>polyval(p, x)</c>, and with the fit's own record <c>[y, delta] = polyval(p, x, S)</c> —
    /// where delta is the half-width of a roughly 68% prediction interval — plus the <c>mu</c> that
    /// undoes <c>polyfit</c>'s centring.
    /// </summary>
    private static JgsValue[] PolynomialValue(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("polyval", args, 2, 4, line, col);
        double[] coefficients = ToDoubles("polyval", args[0], line, col);

        double centre = 0;
        double scale = 1;
        if (args.Count == 4 && !IsEmpty(args[3]))
        {
            double[] mu = ToDoubles("polyval", args[3], line, col);
            if (mu.Length != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "polyval: mu is polyfit's [centre, scale] pair, so it holds two numbers.");
            }

            centre = mu[0];
            scale = mu[1];
        }

        JgsValue evaluated = MapNumeric(
            "polyval", args[1], v => Horner(coefficients, (v - centre) / scale), line, col);

        bool given = args.Count >= 3 && !IsEmpty(args[2]);
        if (wanted <= 1 && !given)
        {
            return [evaluated];
        }

        if (!given)
        {
            throw new JgsRuntimeException(line, col,
                "polyval: the error estimate needs polyfit's record — [y, delta] = polyval(p, x, S).");
        }

        if (args[2].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                $"polyval expects argument 3 to be polyfit's record, but got a {args[2].TypeName}.");
        }

        return wanted <= 1
            ? [evaluated]
            : [evaluated, PredictionError(args[2].AsStruct, args[1], coefficients.Length, centre, scale, line, col)];
    }

    /// <summary>
    /// The spread of the fit at each point: how far the design row reaches through the triangular
    /// factor, scaled by the residual per degree of freedom. A fit with nothing left over — as many
    /// coefficients as points — has no spread to report, and answers infinity as MATLAB does.
    /// </summary>
    private static JgsValue PredictionError(
        Dictionary<string, JgsValue> record, JgsValue at, int terms, double centre, double scale, int line, int col)
    {
        foreach (string field in new[] { "R", "df", "normr" })
        {
            if (!record.ContainsKey(field))
            {
                throw new JgsRuntimeException(line, col,
                    $"polyval: the record from polyfit is missing '{field}'.");
            }
        }

        double[][] triangular = JgsMatrix.ToRows("polyval", record["R"], line, col);
        if (triangular.Length != terms || triangular[0].Length != terms)
        {
            throw new JgsRuntimeException(line, col,
                "polyval: the record's R does not match the number of coefficients.");
        }

        double df = record["df"].AsNumber;
        double normr = record["normr"].AsNumber;
        double[] x = Rescaled(FlattenColumnMajor("polyval", at, line, col), centre, scale);
        double[,] design = Vandermonde(x, terms);

        var spread = new double[x.Length];
        for (int r = 0; r < x.Length; r++)
        {
            // Solve eᵀ·R = vᵀ by forward substitution: R is upper triangular, so its transpose is
            // lower and each unknown falls out of the ones already found.
            var e = new double[terms];
            double sum = 1;
            for (int i = 0; i < terms; i++)
            {
                double known = 0;
                for (int j = 0; j < i; j++)
                {
                    known += triangular[j][i] * e[j];
                }

                e[i] = (design[r, i] - known) / triangular[i][i];
                sum += e[i] * e[i];
            }

            spread[r] = df > 0 ? normr / Math.Sqrt(df) * Math.Sqrt(sum) : double.PositiveInfinity;
        }

        return ShapedNumbers(spread, SizeDims(at));
    }

    /// <summary>The design matrix of a polynomial fit: highest power first, matching p's order.</summary>
    private static double[,] Vandermonde(double[] x, int terms)
    {
        var design = new double[x.Length, terms];
        for (int r = 0; r < x.Length; r++)
        {
            double power = 1;
            for (int c = terms - 1; c >= 0; c--)
            {
                design[r, c] = power;
                power *= x[r];
            }
        }

        return design;
    }

    private static double Horner(double[] coefficients, double x)
    {
        double y = 0;
        foreach (double p in coefficients)
        {
            y = (y * x) + p;
        }

        return y;
    }

    private static double[] Rescaled(double[] values, double centre, double scale)
    {
        var moved = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            moved[i] = (values[i] - centre) / scale;
        }

        return moved;
    }

    // --- Ordering ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>[B, i] = sortrows(A, columns, direction)</c>: rows ordered by whole columns, later columns
    /// breaking ties in earlier ones, and equal rows keeping the order they arrived in.
    /// </summary>
    /// <remarks>
    /// A negative column means "descending" in MATLAB, which works because its columns are numbered
    /// from 1. Under JGS they are numbered from 0, where there is no negative zero to write, so the
    /// shorthand is refused by name and <c>'descend'</c> says the same thing.
    /// </remarks>
    private static JgsValue[] SortedRows(
        IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        ArityRange("sortrows", args, 1, 3, line, col);
        int[] dims = SizeDims(args[0]);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                "sortrows takes a matrix, not an array with more than two dimensions.");
        }

        int rows = dims[0];
        int columns = dims.Length > 1 ? dims[1] : 1;
        double[] flat = FlattenColumnMajor("sortrows", args[0], line, col);

        var keys = new List<(int Column, bool Descending)>();
        if (args.Count >= 2 && !IsEmpty(args[1]))
        {
            foreach (double raw in ToDoubles("sortrows", args[1], line, col))
            {
                bool descending = raw < 0;
                if (descending && !dialect.IsMatlab)
                {
                    throw new JgsRuntimeException(line, col,
                        "sortrows: a negative column reads as descending only where columns start at 1; say 'descend'.");
                }

                int column = (int)Math.Abs(raw) - dialect.IndexBase;
                if (column < 0 || column >= columns || Math.Abs(raw) != Math.Floor(Math.Abs(raw)))
                {
                    throw new JgsRuntimeException(line, col,
                        $"sortrows: {raw} is not a column of a matrix with {columns} of them.");
                }

                keys.Add((column, descending));
            }
        }
        else
        {
            for (int c = 0; c < columns; c++)
            {
                keys.Add((c, false));
            }
        }

        if (args.Count == 3)
        {
            string[] words = DirectionWords(args[2], keys.Count, line, col);
            for (int i = 0; i < keys.Count; i++)
            {
                keys[i] = (keys[i].Column, words[i] == "descend");
            }
        }

        var order = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            order[r] = r;
        }

        Array.Sort(order, (a, b) =>
        {
            foreach ((int column, bool descending) in keys)
            {
                int rank = RankNumbers(flat[a + (column * rows)], flat[b + (column * rows)]);
                if (rank != 0)
                {
                    return descending ? -rank : rank;
                }
            }

            return a.CompareTo(b); // equal rows keep the order they came in
        });

        var sorted = new double[flat.Length];
        var places = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            places[r] = order[r] + dialect.IndexBase;
            for (int c = 0; c < columns; c++)
            {
                sorted[r + (c * rows)] = flat[order[r] + (c * rows)];
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(sorted, dims),
            JgsMatrix.FromColumnMajor(places, rows, 1));
    }

    /// <summary>A missing reading sorts to the back, which is where MATLAB's own comparisons put it.</summary>
    private static int RankNumbers(double a, double b) =>
        double.IsNaN(a) ? (double.IsNaN(b) ? 0 : 1)
        : double.IsNaN(b) ? -1
        : a.CompareTo(b);

    /// <summary>One direction word for every sort key: a single word applies to all of them.</summary>
    private static string[] DirectionWords(JgsValue given, int keys, int line, int col)
    {
        var words = new List<string>();
        if (given.Type == JgsType.String)
        {
            words.Add(given.AsString);
        }
        else if (given.Type == JgsType.Cell)
        {
            foreach (JgsValue item in given.AsCell)
            {
                if (item.Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, "sortrows: the directions must all be words.");
                }

                words.Add(item.AsString);
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                $"sortrows expects a direction word or a cell of them, but got a {given.TypeName}.");
        }

        var chosen = new string[keys];
        for (int i = 0; i < keys; i++)
        {
            string word = words.Count == 1 ? words[0] : i < words.Count ? words[i] : string.Empty;
            chosen[i] = word.ToLowerInvariant() switch
            {
                "ascend" => "ascend",
                "descend" => "descend",
                _ => throw new JgsRuntimeException(line, col,
                    words.Count is not 1 && words.Count != keys
                        ? $"sortrows: {keys} column(s) need one direction each, or one for all, but got {words.Count}."
                        : $"sortrows: '{word}' is not a direction (expected 'ascend', 'descend')."),
            };
        }

        return chosen;
    }

    // --- Binning ----------------------------------------------------------------------------------

    private static readonly OptionSpec HistCountsOptions = new(
        "histcounts",
        Flags: [],
        Names: ["BinWidth", "BinLimits", "Normalization", "BinMethod"]);

    /// <summary>
    /// <c>[N, edges, bin] = histcounts(X, …)</c>: how many values fall in each bin, the bin edges,
    /// and which bin each value landed in. Every bin takes its left edge and not its right, except
    /// the last, which takes both — that is what makes the counts add up to the sample size.
    /// </summary>
    private static JgsValue[] BinCounts(
        IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "histcounts needs some data.");
        }

        ParsedArgs parsed = HistCountsOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "histcounts needs the data before any option.");
        }

        double[] data = FlattenColumnMajor("histcounts", parsed.Positional[0], line, col);
        double[]? limits = parsed.Vector("BinLimits");
        double? width = parsed.Named("BinWidth") is null ? null : parsed.Scalar("BinWidth", 0);
        if (width is { } step && (!(step > 0) || !double.IsFinite(step)))
        {
            throw new JgsRuntimeException(line, col, "histcounts: 'BinWidth' must be a positive number.");
        }

        string normalization = parsed.Word(
            "Normalization", "count", "count", "countdensity", "cumcount", "probability", "pdf", "cdf");
        string rule = parsed.Word("BinMethod", "auto", "auto", "scott", "sturges", "sqrt", "fd", "integers");

        double[]? given = null;
        int? requested = null;
        if (parsed.Positional.Count == 2)
        {
            JgsValue second = parsed.Positional[1];
            if (second.Type is JgsType.Number or JgsType.Bool)
            {
                requested = Count("histcounts", parsed.Positional, 1, line, col);
                if (requested < 1)
                {
                    throw new JgsRuntimeException(line, col, "histcounts needs at least one bin.");
                }
            }
            else
            {
                given = ToDoubles("histcounts", second, line, col);
                if (given.Length < 2)
                {
                    throw new JgsRuntimeException(line, col, "histcounts: bin edges come in twos or more.");
                }
            }
        }

        if (given is not null && (requested is not null || width is not null || limits is not null))
        {
            throw new JgsRuntimeException(line, col,
                "histcounts: bin edges already say where every bin is, so a count, width or limits cannot be given too.");
        }

        if (limits is not null && limits.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "histcounts: 'BinLimits' takes a [low high] pair.");
        }

        double[] edges = given ?? Binning.EdgesFor(data, requested, width, limits, rule);
        var counts = new double[edges.Length - 1];
        var which = new double[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            int bin = BinOf(data[i], edges);
            which[i] = bin < 0 ? dialect.IndexBase - 1 : bin + dialect.IndexBase;
            if (bin >= 0)
            {
                counts[bin]++;
            }
        }

        return Outputs(
            wanted,
            Numbers(Normalized(counts, edges, normalization, data.Length)),
            Numbers(edges),
            JgsMatrix.FromColumnMajorDims(which, SizeDims(parsed.Positional[0])));
    }

    /// <summary>Which bin a value falls in, or −1 for one outside every bin.</summary>
    private static int BinOf(double value, double[] edges)
    {
        if (double.IsNaN(value) || value < edges[0] || value > edges[^1])
        {
            return -1;
        }

        if (value == edges[^1])
        {
            return edges.Length - 2; // the last bin is closed at both ends
        }

        int low = 0;
        int high = edges.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (value < edges[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    /// <summary>Bins filling exactly the span from low to high, which is what named limits mean.</summary>
    private static double[] Spanning(double low, double high, int bins)
    {
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = low + ((high - low) * i / bins);
        }

        edges[^1] = high;
        return edges;
    }

    /// <summary>The counts as whatever the call asked to be counted in.</summary>
    private static double[] Normalized(double[] counts, double[] edges, string normalization, int total)
    {
        var scaled = new double[counts.Length];
        double running = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            double width = edges[i + 1] - edges[i];
            running += counts[i];
            scaled[i] = normalization switch
            {
                "countdensity" => counts[i] / width,
                "cumcount" => running,
                "probability" => total == 0 ? 0 : counts[i] / total,
                "pdf" => total == 0 ? 0 : counts[i] / (total * width),
                "cdf" => total == 0 ? 0 : running / total,
                _ => counts[i],
            };
        }

        return scaled;
    }

    // --- Second moments ---------------------------------------------------------------------------

    private static readonly OptionSpec CorrelationOptions = new(
        "corrcoef",
        Flags: [],
        Names: ["Alpha", "Rows"]);

    /// <summary>
    /// <c>[R, P, RL, RU] = corrcoef(…)</c>: the correlation between every pair of variables, the
    /// chance of seeing one that large from uncorrelated data, and the ends of a confidence interval
    /// around it.
    /// </summary>
    private static JgsValue[] Correlations(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "corrcoef needs some data.");
        }

        ParsedArgs parsed = CorrelationOptions.Parse(args, 2, line, col);
        double alpha = parsed.Scalar("Alpha", 0.05);
        if (!(alpha > 0 && alpha < 1))
        {
            throw new JgsRuntimeException(line, col, "corrcoef: 'Alpha' sits strictly between 0 and 1.");
        }

        string rows = parsed.Word("Rows", "all", "all", "complete", "pairwise");
        double[][] variables = ObservationColumns("corrcoef", parsed.Positional, line, col);
        if (rows == "complete")
        {
            variables = WithoutMissingRows(variables);
        }

        int n = variables.Length;
        var r = new double[n * n];
        var p = new double[n * n];
        var lower = new double[n * n];
        var upper = new double[n * n];
        double halfWidth = Math.Sqrt(2) * SpecialFunctions.ErfInverse(1 - alpha);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                (double[] a, double[] b) = rows == "pairwise"
                    ? BothPresent(variables[i], variables[j])
                    : (variables[i], variables[j]);

                // A variable correlates perfectly with itself, except that a constant one has no
                // spread for the question to be about — which is the NaN MATLAB reports there too.
                double correlation = i == j
                    ? (a.Length < 2 || CoMoment(a, a, 0) == 0 ? double.NaN : 1)
                    : PearsonOf(a, b);
                r[i + (j * n)] = correlation;
                p[i + (j * n)] = i == j ? 1 : Significance(correlation, a.Length);
                (lower[i + (j * n)], upper[i + (j * n)]) = i == j
                    ? (1, 1)
                    : FisherInterval(correlation, a.Length, halfWidth);
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(r, n, n),
            JgsMatrix.FromColumnMajor(p, n, n),
            JgsMatrix.FromColumnMajor(lower, n, n),
            JgsMatrix.FromColumnMajor(upper, n, n));
    }

    /// <summary>
    /// <c>cov(A)</c>, <c>cov(x, y)</c>, <c>cov(…, w)</c>, <c>cov(…, nanflag)</c>: the covariance
    /// between every pair of variables, normalized by n−1 unless a weight of 1 asks for n.
    /// </summary>
    private static JgsValue Covariance(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("cov", args, 1, 4, line, col);

        int numeric = 0;
        while (numeric < args.Count && args[numeric].Type != JgsType.String)
        {
            numeric++;
        }

        string missing = "includenan";
        if (numeric < args.Count)
        {
            missing = OneWord("cov", args, numeric, line, col, "includenan", "omitrows", "partialrows");
            if (args.Count > numeric + 1)
            {
                throw new JgsRuntimeException(line, col, "cov: the missing-data word comes last.");
            }
        }

        double weight = 0;
        var data = new List<JgsValue>();
        for (int i = 0; i < numeric; i++)
        {
            data.Add(args[i]);
        }

        // cov(A, w) and cov(A, B) are told apart the way MATLAB tells them apart: a lone number in
        // the second slot is the normalization, because a second variable never arrives as one.
        if (data.Count >= 2 && data[^1].Type is JgsType.Number or JgsType.Bool)
        {
            weight = data[^1].AsNumber;
            data.RemoveAt(data.Count - 1);
            if (weight is not (0 or 1))
            {
                throw new JgsRuntimeException(line, col, "cov: the normalization is 0 (n-1) or 1 (n).");
            }
        }

        if (data.Count is 0 or > 2)
        {
            throw new JgsRuntimeException(line, col, "cov takes cov(A) or cov(A, B), then an optional weight.");
        }

        double[][] variables = ObservationColumns("cov", data, line, col);
        if (missing == "omitrows")
        {
            variables = WithoutMissingRows(variables);
        }

        int n = variables.Length;
        var result = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                (double[] a, double[] b) = missing == "partialrows"
                    ? BothPresent(variables[i], variables[j])
                    : (variables[i], variables[j]);
                result[i + (j * n)] = CoMoment(a, b, weight);
            }
        }

        return n == 1 ? JgsValue.Number(result[0]) : JgsMatrix.FromColumnMajor(result, n, n);
    }

    /// <summary>
    /// The variables a second-moment statistic compares: a matrix's columns, or the two arrays a
    /// two-argument call names — which are read whole, however they were shaped.
    /// </summary>
    private static double[][] ObservationColumns(
        string name, IReadOnlyList<JgsValue> positional, int line, int col)
    {
        if (positional.Count == 2)
        {
            double[] first = FlattenColumnMajor(name, positional[0], line, col);
            double[] second = FlattenColumnMajor(name, positional[1], line, col);
            if (first.Length != second.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the two sets must be the same size, but got {first.Length} and {second.Length}.");
            }

            return [first, second];
        }

        int[] dims = SizeDims(positional[0]);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a vector or a matrix, not an array with more than two dimensions.");
        }

        double[] flat = FlattenColumnMajor(name, positional[0], line, col);
        int rows = dims[0];
        int columns = dims.Length > 1 ? dims[1] : 1;
        if (rows == 1 || columns == 1)
        {
            return [flat]; // a vector is one variable, however it is written
        }

        var variables = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            variables[c] = new double[rows];
            Array.Copy(flat, c * rows, variables[c], 0, rows);
        }

        return variables;
    }

    /// <summary>Every variable with the observations dropped where any of them is missing.</summary>
    private static double[][] WithoutMissingRows(double[][] variables)
    {
        int rows = variables.Length == 0 ? 0 : variables[0].Length;
        var keep = new List<int>();
        for (int r = 0; r < rows; r++)
        {
            if (variables.All(v => !double.IsNaN(v[r])))
            {
                keep.Add(r);
            }
        }

        var kept = new double[variables.Length][];
        for (int v = 0; v < variables.Length; v++)
        {
            kept[v] = [.. keep.Select(r => variables[v][r])];
        }

        return kept;
    }

    /// <summary>The observations where both of a pair were recorded.</summary>
    private static (double[] Left, double[] Right) BothPresent(double[] a, double[] b)
    {
        var left = new List<double>();
        var right = new List<double>();
        for (int i = 0; i < a.Length; i++)
        {
            if (!double.IsNaN(a[i]) && !double.IsNaN(b[i]))
            {
                left.Add(a[i]);
                right.Add(b[i]);
            }
        }

        return ([.. left], [.. right]);
    }

    private static double CoMoment(double[] a, double[] b, double weight)
    {
        int n = a.Length;
        if (n == 0)
        {
            return double.NaN;
        }

        // A single observation has no spread under either normalization, which MATLAB reports as
        // zero rather than as the division by n-1 that the formula would otherwise ask for.
        if (n == 1)
        {
            return 0;
        }

        double divisor = weight == 1 ? n : n - 1;

        double meanA = Average(a);
        double meanB = Average(b);
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            total += (a[i] - meanA) * (b[i] - meanB);
        }

        return total / divisor;
    }

    private static double PearsonOf(double[] a, double[] b)
    {
        double spreadA = CoMoment(a, a, 0);
        double spreadB = CoMoment(b, b, 0);
        double together = CoMoment(a, b, 0);
        double scale = Math.Sqrt(spreadA * spreadB);
        return scale == 0 ? double.NaN : together / scale;
    }

    /// <summary>
    /// How often uncorrelated data would produce a correlation at least this large, both ways. The
    /// test statistic is Student's t on n−2 degrees of freedom, and the two-sided tail is exactly the
    /// regularized incomplete beta that defines it.
    /// </summary>
    private static double Significance(double r, int n)
    {
        if (n < 3 || double.IsNaN(r))
        {
            return double.NaN;
        }

        if (Math.Abs(r) >= 1)
        {
            return 0;
        }

        double df = n - 2;
        double t = r * Math.Sqrt(df / (1 - (r * r)));
        return SpecialFunctions.BetaRegularized(df / (df + (t * t)), df / 2, 0.5);
    }

    /// <summary>
    /// A confidence interval on a correlation, through Fisher's transformation: atanh makes the
    /// sampling distribution near enough normal to put a symmetric interval on, and tanh brings the
    /// ends back. Fewer than four observations leave nothing to say, so the interval is the whole
    /// range.
    /// </summary>
    private static (double Low, double High) FisherInterval(double r, int n, double halfWidth)
    {
        if (n < 4 || double.IsNaN(r))
        {
            return (-1, 1);
        }

        double z = Math.Atanh(Math.Clamp(r, -1, 1));
        double spread = halfWidth / Math.Sqrt(n - 3);
        return (Math.Tanh(z - spread), Math.Tanh(z + spread));
    }

    private static double Average(double[] values)
    {
        if (values.Length == 0)
        {
            return double.NaN;
        }

        double total = 0;
        foreach (double value in values)
        {
            total += value;
        }

        return total / values.Length;
    }

    // --- Shared argument reading -------------------------------------------------------------------

    /// <summary>
    /// A positional argument that has to be one of a fixed set of words. The diagnostic lists them,
    /// for the same reason <see cref="ParsedArgs.Word"/> does: a word that is merely ignored is how a
    /// script silently does something other than what it says.
    /// </summary>
    private static string OneWord(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col, params string[] allowed)
    {
        string word = Str(name, args, index, line, col);
        foreach (string candidate in allowed)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: '{word}' is not one of {string.Join(", ", allowed.Select(static a => $"'{a}'"))}.");
    }
}
