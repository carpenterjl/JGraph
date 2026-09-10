using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Signal Processing Toolbox's rate changers and the two names that repair a signal rather than
/// resample it: <c>upsample</c>, <c>downsample</c>, <c>upfirdn</c>, <c>interp</c>, <c>decimate</c>,
/// <c>resample</c>, <c>fillgaps</c> and <c>envelope</c> (M133).
/// </summary>
/// <remarks>
/// <para>
/// The first three are mechanical: insert zeros, drop samples, or do both around a filter. The next
/// three each design a filter first, and that design is where their parity lives — the answer is the
/// filter's coefficients convolved with the signal, so a filter that is right to twelve figures
/// gives a signal that is right to twelve figures and no more.
/// </para>
/// <para>
/// <c>fillgaps</c> and <c>envelope</c> are here because they are the two names in this milestone
/// that are about a signal's shape rather than its rate, and because both of them borrow machinery
/// from a later milestone: an autoregressive fit from M137 and a peak finder from M136. Both are
/// written out in <see cref="SmoothingFilters"/> rather than approximated.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the eight multirate and repair names.</summary>
    internal static void RegisterMultirateBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("upsample", (args, line, col) => Restride("upsample", args, line, col));
        Define("downsample", (args, line, col) => Restride("downsample", args, line, col));
        Define("upfirdn", RateChange);
        Define("interp", (args, line, col) => Interpolated(args, 1, line, col)[0],
            (args, wanted, line, col) => Interpolated(args, wanted, line, col));
        Define("decimate", Decimated);
        Define("resample", (args, line, col) => Resampled(args, 1, line, col)[0],
            (args, wanted, line, col) => Resampled(args, wanted, line, col));
        Define("fillgaps", FilledGaps);
        Define("envelope", (args, line, col) => SignalEnvelope(args, 1, line, col)[0],
            (args, wanted, line, col) => SignalEnvelope(args, wanted, line, col));
    }

    /// <summary>
    /// Slices written back along the dimension they came from, in the shape that makes — the inverse
    /// of the walk every one of these names does over an array wider than a vector.
    /// </summary>
    private static JgsValue JoinedSlices(double[][] slices, IReadOnlyList<int> dims, int dim)
    {
        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(slices, dims, dim);
        return JgsMatrix.FromColumnMajorDims(joined, shape);
    }

    /// <summary>
    /// <c>upsample(x, n, phase)</c> and <c>downsample(x, n, phase)</c>, which share every rule but
    /// the direction: they work along the first dimension that is not a singleton, and they leave
    /// every other dimension alone.
    /// </summary>
    private static JgsValue Restride(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        if (ElementCount(args[0]) == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} reads a signal with something in it.");
        }

        int factor = Count(name, args, 1, line, col);
        if (factor < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}'s factor is a whole number above zero.");
        }

        int phase = args.Count >= 3 ? Count(name, args, 2, line, col) : 0;
        if (phase < 0 || phase > factor - 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}'s phase is between zero and one less than its factor.");
        }

        int[] dims = SizeDims(args[0]);
        int dim = JgsMatrix.DefaultDim(dims);
        double[] x = ToDoubles(name, args[0], line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(x, dims, dim);

        var changed = new double[slices.Length][];
        for (int i = 0; i < slices.Length; i++)
        {
            changed[i] = name == "upsample"
                ? Multirate.Upsample(slices[i], factor, phase)
                : Multirate.Downsample(slices[i], factor, phase);
        }

        int[] shape = JgsMatrix.ShapeAlong(dims, dim, changed.Length == 0 ? 0 : changed[0].Length);
        return JoinedSlices(changed, shape, dim);
    }

    /// <summary><c>upfirdn(x, h, p, q)</c>.</summary>
    private static JgsValue RateChange(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("upfirdn", args, 2, 4, line, col);
        double[] h = FilterVector("upfirdn", args[1], line, col);
        int p = args.Count >= 3 ? Count("upfirdn", args, 2, line, col) : 1;
        int q = args.Count >= 4 ? Count("upfirdn", args, 3, line, col) : 1;

        if (p < 1 || q < 1)
        {
            throw new JgsRuntimeException(line, col, "upfirdn's rate factors are whole numbers above zero.");
        }

        (double[] x, int rows, int columns, bool wasRow, _) =
            FilterSignal("upfirdn", args[0], line, col);

        if (rows == 0 || h.Length == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        var column = new double[rows];
        double[][] answers = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            answers[c] = Multirate.UpFirDown(column, h, p, q);
        }

        int count = answers[0].Length;
        var flat = new double[count * columns];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(answers[c], 0, flat, c * count, count);
        }

        return wasRow && columns == 1
            ? JgsMatrix.FromColumnMajor(flat, 1, count)
            : JgsMatrix.FromColumnMajor(flat, count, columns);
    }

    /// <summary><c>[y, b] = interp(x, r)</c> and its filter-length and cutoff forms.</summary>
    private static JgsValue[] Interpolated(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("interp", args, 2, 4, line, col);
        int r = Count("interp", args, 1, line, col);
        int n = args.Count >= 3 && !IsEmptyValue(args[2]) ? Count("interp", args, 2, line, col) : 4;
        double cutoff = args.Count >= 4 && !IsEmptyValue(args[3]) ? Num("interp", args, 3, line, col) : 0.5;

        if (r < 1 || n < 1)
        {
            throw new JgsRuntimeException(line, col, "interp's rate and filter length are whole numbers above zero.");
        }

        if (cutoff <= 0 || cutoff > 1)
        {
            throw new JgsRuntimeException(line, col, "interp's cutoff is above zero and at most one.");
        }

        (double[] x, int rows, int columns, bool wasRow, _) = FilterSignal("interp", args[0], line, col);
        if (columns > 1)
        {
            throw new JgsRuntimeException(line, col, "interp reads one signal at a time.");
        }

        double[] y;
        double[] b;
        try
        {
            (y, b) = Multirate.Interpolate(x.AsSpan(0, rows), r, n, cutoff);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"interp: {ex.Message}");
        }

        JgsValue signal = wasRow
            ? JgsMatrix.FromColumnMajor(y, 1, y.Length)
            : JgsMatrix.FromColumnMajor(y, y.Length, 1);

        return wanted <= 1
            ? [signal]
            : [signal, JgsMatrix.FromColumnMajor(b, b.Length, 1)];
    }

    /// <summary><c>decimate(x, r)</c>, with the Chebyshev default and the FIR alternative.</summary>
    private static JgsValue Decimated(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("decimate", args, 2, 4, line, col);
        int r = Count("decimate", args, 1, line, col);
        if (r < 1)
        {
            throw new JgsRuntimeException(line, col, "decimate's factor is a whole number above zero.");
        }

        if (r == 1)
        {
            return args[0];
        }

        bool useCheby = true;
        int order = 8;

        if (args.Count == 3)
        {
            if (IsTextScalar(args[2]))
            {
                useCheby = ReadDecimateKind(args[2], line, col);
                order = useCheby ? 8 : 30;
            }
            else
            {
                order = Count("decimate", args, 2, line, col);
            }
        }
        else if (args.Count == 4)
        {
            if (IsTextScalar(args[2]))
            {
                useCheby = ReadDecimateKind(args[2], line, col);
                order = Count("decimate", args, 3, line, col);
            }
            else
            {
                order = Count("decimate", args, 2, line, col);
                useCheby = ReadDecimateKind(args[3], line, col);
            }
        }

        if (order < 1)
        {
            throw new JgsRuntimeException(line, col, "decimate's filter order is a whole number above zero.");
        }

        (double[] x, int rows, int columns, bool wasRow, _) = FilterSignal("decimate", args[0], line, col);
        if (columns > 1)
        {
            throw new JgsRuntimeException(line, col, "decimate reads one signal at a time.");
        }

        double[] y;
        try
        {
            y = useCheby
                ? Multirate.DecimateIir(x.AsSpan(0, rows), r, order)
                : Multirate.DecimateFir(x.AsSpan(0, rows), r, order);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"decimate: {ex.Message}");
        }

        return wasRow
            ? JgsMatrix.FromColumnMajor(y, 1, y.Length)
            : JgsMatrix.FromColumnMajor(y, y.Length, 1);
    }

    /// <summary>Whether the word chooses the recursive filter or the feed-forward one.</summary>
    private static bool ReadDecimateKind(JgsValue value, int line, int col) =>
        FilterWord("decimate", value, line, col, "fir", "iir") == "iir";

    /// <summary>
    /// <c>resample(x, p, q)</c> and its filter-length, Kaiser-shape and given-filter forms.
    /// </summary>
    private static JgsValue[] Resampled(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("resample", args, 2, 6, line, col);

        if (args.Count >= 2 && ElementCount(args[1]) > 1)
        {
            throw new JgsRuntimeException(line, col,
                "resample's non-uniform form, which takes a time vector, is not implemented.");
        }

        int p = Count("resample", args, 1, line, col);
        int q = args.Count >= 3 && !IsEmptyValue(args[2]) ? Count("resample", args, 2, line, col) : 1;
        if (p < 1 || q < 1)
        {
            throw new JgsRuntimeException(line, col, "resample's rate factors are whole numbers above zero.");
        }

        int divisor = GreatestCommonDivisor(p, q);
        p /= divisor;
        q /= divisor;

        double[] filter;
        int length;
        if (args.Count >= 4 && ElementCount(args[3]) > 1)
        {
            filter = FilterVector("resample", args[3], line, col);
            length = filter.Length;
        }
        else
        {
            int n = args.Count >= 4 && !IsEmptyValue(args[3]) ? Count("resample", args, 3, line, col) : 10;
            double beta = args.Count >= 5 && !IsEmptyValue(args[4]) ? Num("resample", args, 4, line, col) : 5;
            filter = Multirate.ResampleFilter(p, q, n, beta);
            length = filter.Length;
        }

        (double[] x, int rows, int columns, bool wasRow, _) = FilterSignal("resample", args[0], line, col);

        var column = new double[rows];
        double[][] answers = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            answers[c] = Multirate.Resample(column, p, q, filter, length);
        }

        int count = columns == 0 ? 0 : answers[0].Length;
        var flat = new double[count * columns];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(answers[c], 0, flat, c * count, count);
        }

        JgsValue signal = wasRow && columns == 1
            ? JgsMatrix.FromColumnMajor(flat, 1, count)
            : JgsMatrix.FromColumnMajor(flat, count, columns);

        if (wanted <= 1)
        {
            return [signal];
        }

        JgsValue taps = p == 1 && q == 1
            ? JgsValue.Number(1)
            : JgsMatrix.FromColumnMajor(filter, 1, filter.Length);
        return [signal, taps];
    }

    /// <summary>The largest whole number that divides both, which is how a ratio is reduced.</summary>
    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a == 0 ? 1 : a;
    }

    /// <summary><c>fillgaps(x)</c>, <c>fillgaps(x, maxlen)</c> and <c>fillgaps(x, maxlen, order)</c>.</summary>
    private static JgsValue FilledGaps(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("fillgaps", args, 1, 3, line, col);

        double maxLength = double.PositiveInfinity;
        if (args.Count >= 2 && !IsEmptyValue(args[1]))
        {
            maxLength = Num("fillgaps", args, 1, line, col);
            if (!double.IsInfinity(maxLength) && (maxLength <= 2 || maxLength != System.Math.Floor(maxLength)))
            {
                throw new JgsRuntimeException(line, col,
                    "fillgaps' longest segment is a whole number above two, or Inf.");
            }
        }

        int? order = null;
        if (args.Count >= 3 && !IsEmptyValue(args[2]))
        {
            if (IsTextScalar(args[2]))
            {
                _ = FilterWord("fillgaps", args[2], line, col, "aic");
            }
            else
            {
                double given = Num("fillgaps", args, 2, line, col);
                if (given <= 0 || given != System.Math.Floor(given))
                {
                    throw new JgsRuntimeException(line, col,
                        "fillgaps' model order is a whole number above zero.");
                }

                if (given > maxLength)
                {
                    throw new JgsRuntimeException(line, col,
                        "fillgaps' model order cannot exceed its longest segment.");
                }

                order = (int)given;
            }
        }

        (double[] x, int rows, int columns, bool wasRow, int[] dims) =
            FilterSignal("fillgaps", args[0], line, col);

        double[] y = SmoothingFilters.FillGaps(x, rows, columns, maxLength, order);
        return FilterShaped(y, wasRow, dims);
    }

    /// <summary><c>[upper, lower] = envelope(x)</c> and its length and method forms.</summary>
    private static JgsValue[] SignalEnvelope(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("envelope", args, 1, 3, line, col);

        int? n = null;
        if (args.Count >= 2 && !IsEmptyValue(args[1]))
        {
            n = Count("envelope", args, 1, line, col);
            if (n <= 0)
            {
                throw new JgsRuntimeException(line, col, "envelope's length is a whole number above zero.");
            }
        }

        string method = args.Count >= 3
            ? FilterWord("envelope", args[2], line, col, "analytic", "rms", "peaks")
            : "analytic";

        (double[] x, int rows, int columns, bool wasRow, int[] dims) =
            FilterSignal("envelope", args[0], line, col);

        (double[] upper, double[] lower) = SmoothingFilters.Envelope(x, rows, columns, n, method);
        JgsValue top = FilterShaped(upper, wasRow, dims);
        return wanted <= 1 ? [top] : [top, FilterShaped(lower, wasRow, dims)];
    }
}
