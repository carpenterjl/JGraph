using JGraph.Maths;
using JGraph.Data;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The preprocessing family (M66 wave A): the nine names a real script reaches for between reading a
/// measurement file and plotting it — <c>normalize</c>, <c>rescale</c>, <c>discretize</c>,
/// <c>fillmissing</c>, <c>rmmissing</c>, <c>islocalmax</c>, <c>islocalmin</c>, <c>smoothdata</c> and
/// <c>groupsummary</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these works on numbers, and every one of them is also asked about times: a gappy
/// temperature log is a datetime column beside a double column, and <c>fillmissing</c> has to answer
/// for both. Rather than teach nine functions what a datetime is, this file has one seam —
/// <see cref="PrepStrip"/> and <see cref="PrepDress"/> — that hands the family plain milliseconds and
/// puts the tag back on the way out. That is M64's reduction trick applied to a new family, and it is
/// why the time-awareness the plan promised cost a helper rather than nine rewrites.
/// </para>
/// <para>
/// The binning names share <c>histcounts</c>' edge chooser rather than growing a second one:
/// <c>discretize(x, 5)</c> and <c>histcounts(x, 5)</c> must agree about where the five bins are, and
/// two implementations of one rule is how they would stop agreeing.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>normalize</c>'s methods. They cannot be declared as <see cref="OptionSpec"/> names because
    /// a name there always consumes the argument after it, and <c>normalize(x, 'zscore')</c> is a
    /// complete call — the method's parameter is optional in a way no option is.
    /// </summary>
    private static readonly string[] NormalizeMethods =
        ["zscore", "norm", "range", "center", "scale", "medianiqr"];

    /// <summary>
    /// What joins several grouping variables into one key. A unit separator rather than a comma,
    /// because a grouping variable is allowed to contain a comma and is not allowed to contain this.
    /// </summary>
    private const char GroupKeySeparator = '\u001f';

    private static readonly OptionSpec RescaleOptions = new(
        "rescale",
        Flags: [],
        Names: ["InputMin", "InputMax"]);

    private static readonly OptionSpec DiscretizeOptions = new(
        "discretize",
        Flags: [],
        Names: ["IncludedEdge"]);

    private static readonly OptionSpec FillMissingOptions = new(
        "fillmissing",
        Flags: [],
        Names: ["EndValues", "MissingLocations", "DataVariables"],
        StringPositionals: 0);

    private static readonly OptionSpec RmMissingOptions = new(
        "rmmissing",
        Flags: [],
        Names: ["MinNumMissing", "DataVariables"]);

    private static readonly OptionSpec LocalExtremaOptions = new(
        "islocalmax",
        Flags: [],
        Names: ["MinProminence", "MinSeparation", "FlatSelection", "MaxNumExtrema", "SamplePoints"]);

    private static readonly OptionSpec SmoothOptions = new(
        "smoothdata",
        Flags: ["omitnan", "includenan"],
        Names: ["SamplePoints", "SmoothingFactor", "degree"]);

    /// <summary>Registers the preprocessing family.</summary>
    private static void RegisterPreprocessingBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        Define("normalize", PreprocessNormalize);
        Define("rescale", PreprocessRescale);
        DefineBoth("discretize", (args, wanted, line, col) => Discretized(args, wanted, dialect, line, col));
        DefineBoth("fillmissing", FilledMissing);
        DefineBoth("rmmissing", RemovedMissing);
        DefineBoth("islocalmax", (args, wanted, line, col) => LocalExtrema("islocalmax", args, wanted, true, line, col));
        DefineBoth("islocalmin", (args, wanted, line, col) => LocalExtrema("islocalmin", args, wanted, false, line, col));
        DefineBoth("smoothdata", Smoothed);
        DefineBoth("groupsummary", (args, wanted, line, col) => GroupSummary(args, wanted, line, col));
    }

    // --- The seam -----------------------------------------------------------------------------

    /// <summary>
    /// A value taken apart into the numbers the family works on, the shape it had, and the tag that
    /// says what those numbers meant. A datetime arrives here as milliseconds since the epoch and
    /// leaves <see cref="PrepDress"/> a datetime again.
    /// </summary>
    private readonly record struct BareValues(double[] Values, int[] Dims, JgsTimeTag? Tag);

    private static BareValues PrepStrip(string name, JgsValue value, int line, int col) =>
        value.IsTime
            ? new BareValues(TimeMs(value), JgsMatrix.DimsOf(value), value.TimeTag)
            : new BareValues(FlattenColumnMajor(name, value, line, col), SizeDims(value), null);

    private static JgsValue PrepDress(BareValues source, double[] values, IReadOnlyList<int> dims)
    {
        JgsValue plain = JgsMatrix.FromColumnMajorDims(values, dims);
        return source.Tag is null ? plain : WrapTime(plain, source.Tag);
    }

    /// <summary>The dimension a family member walks along, and the slices along it.</summary>
    private static (double[][] Slices, int Dim) PrepSlices(BareValues bare, int? named)
    {
        int dim = named ?? JgsMatrix.DefaultDim(bare.Dims);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(bare.Values, bare.Dims, dim);
        return (slices, dim);
    }

    /// <summary>Slices put back where they came from, with the original tag restored.</summary>
    private static JgsValue PrepJoin(BareValues bare, double[][] slices, int dim)
    {
        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(slices, bare.Dims, dim);
        return PrepDress(bare, joined, shape);
    }

    /// <summary>A logical array of the same shape, which is what the mask-returning names answer.</summary>
    private static JgsValue PrepMask(bool[] mask, IReadOnlyList<int> dims)
    {
        var boxed = new JgsValue[mask.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            boxed[i] = JgsValue.Bool(mask[i]);
        }

        return JgsMatrix.FromElementsDims(boxed, dims);
    }

    /// <summary>The dimension given as a bare number in a positional slot, if one was.</summary>
    private static int? PrepDim(string name, IReadOnlyList<JgsValue> positional, int slot, int line, int col)
    {
        if (positional.Count <= slot || positional[slot].Type is not (JgsType.Number or JgsType.Bool))
        {
            return null;
        }

        int dim = Count(name, positional, slot, line, col);
        return dim >= 1
            ? dim
            : throw new JgsRuntimeException(line, col,
                $"{name}: the dimension must be a positive whole number, but was {dim}.");
    }

    // --- Statistics the family shares ---------------------------------------------------------

    /// <summary>The quantile MATLAB's prctile computes: the (i−0.5)/n rule, interpolated.</summary>
    private static double PrepQuantile(double[] sorted, double fraction)
    {
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        double position = (fraction * sorted.Length) - 0.5;
        if (position <= 0)
        {
            return sorted[0];
        }

        if (position >= sorted.Length - 1)
        {
            return sorted[^1];
        }

        int low = (int)Math.Floor(position);
        double weight = position - low;
        return sorted[low] + (weight * (sorted[low + 1] - sorted[low]));
    }

    private static double PrepIqr(double[] values)
    {
        double[] finite = PrepPresent(values);
        if (finite.Length == 0)
        {
            return double.NaN;
        }

        Array.Sort(finite);
        return PrepQuantile(finite, 0.75) - PrepQuantile(finite, 0.25);
    }

    /// <summary>The median absolute deviation about the median, scaled the way MATLAB's mad(x, 1) is not.</summary>
    private static double PrepMad(double[] values)
    {
        double[] finite = PrepPresent(values);
        if (finite.Length == 0)
        {
            return double.NaN;
        }

        double centre = MedianOf(finite);
        var spread = new double[finite.Length];
        for (int i = 0; i < finite.Length; i++)
        {
            spread[i] = Math.Abs(finite[i] - centre);
        }

        return MedianOf(spread);
    }

    private static double PrepMean(double[] values)
    {
        double[] finite = PrepPresent(values);
        return finite.Length == 0 ? double.NaN : finite.Average();
    }

    private static double PrepStd(double[] values)
    {
        double[] finite = PrepPresent(values);
        return finite.Length < 2 ? 0 : Math.Sqrt(SampleVarianceOf(finite));
    }

    private static double[] PrepPresent(double[] values)
    {
        var kept = new List<double>(values.Length);
        foreach (double v in values)
        {
            if (!double.IsNaN(v))
            {
                kept.Add(v);
            }
        }

        return [.. kept];
    }

    // --- normalize ----------------------------------------------------------------------------

    /// <summary>
    /// <c>normalize(A)</c> and its methods: <c>'zscore'</c> (the default), <c>'norm'</c>,
    /// <c>'range'</c>, <c>'center'</c>, <c>'scale'</c> and <c>'medianiqr'</c>, each along one
    /// dimension. The method arrives as a name-value pair whose value is the method's own parameter,
    /// which is why <c>normalize(x, 'range', [0 1])</c> and <c>normalize(x, 'center', 'median')</c>
    /// read the same way to the option parser.
    /// </summary>
    private static JgsValue PreprocessNormalize(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "normalize needs some data.");
        }

        BareValues bare = PrepStrip("normalize", args[0], line, col);
        if (bare.Tag is not null)
        {
            throw new JgsRuntimeException(line, col,
                "normalize: a point in time has no scale to normalize against; take the differences first.");
        }

        // normalize(A), normalize(A, dim), normalize(A, method), normalize(A, method, methodtype) and
        // normalize(A, dim, method[, methodtype]) — read in that order, one slot at a time.
        int next = 1;
        int? dim = PrepDim("normalize", args, next, line, col);
        if (dim is not null)
        {
            next++;
        }

        string method = "zscore";
        if (next < args.Count && IsTextScalar(args[next]))
        {
            method = NamedMethod("normalize", TextOf(args[next]), NormalizeMethods, line, col);
            next++;
        }

        JgsValue? parameter = next < args.Count ? args[next] : null;
        if (parameter is not null && next + 1 < args.Count)
        {
            throw new JgsRuntimeException(line, col,
                "normalize takes the data, an optional dimension, a method, and the method's own setting — nothing more.");
        }

        (double[][] slices, int chosen) = PrepSlices(bare, dim);
        var done = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            done[s] = NormalizeSlice(slices[s], method, parameter, line, col);
        }

        return PrepJoin(bare, done, chosen);
    }

    /// <summary>A method word matched against the ones a name knows, or a diagnostic listing them.</summary>
    private static string NamedMethod(string name, string word, string[] known, int line, int col)
    {
        foreach (string candidate in known)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: no method called '{word}' (expected one of '{string.Join("', '", known)}').");
    }

    private static double[] NormalizeSlice(
        double[] slice, string method, JgsValue? parameter, int line, int col)
    {
        double centre;
        double scale;

        switch (method)
        {
            case "zscore":
            {
                string kind = MethodWord("normalize", "zscore", parameter, "std", line, col, "std", "robust");
                centre = kind == "robust" ? MedianOf(PrepPresent(slice)) : PrepMean(slice);
                scale = kind == "robust" ? PrepMad(slice) : PrepStd(slice);
                break;
            }

            case "medianiqr":
                centre = MedianOf(PrepPresent(slice));
                scale = PrepIqr(slice);
                break;

            case "center":
                scale = 1;
                centre = parameter is { Type: JgsType.Number or JgsType.Bool } number
                    ? number.AsNumber
                    : MethodWord("normalize", "center", parameter, "mean", line, col, "mean", "median") == "median"
                        ? MedianOf(PrepPresent(slice))
                        : PrepMean(slice);
                break;

            case "scale":
                centre = 0;
                scale = parameter is { Type: JgsType.Number or JgsType.Bool } factor
                    ? factor.AsNumber
                    : MethodWord("normalize", "scale", parameter, "std", line, col, "std", "mad", "first", "iqr") switch
                    {
                        "mad" => PrepMad(slice),
                        "first" => slice.Length == 0 ? double.NaN : slice[0],
                        "iqr" => PrepIqr(slice),
                        _ => PrepStd(slice),
                    };
                break;

            case "norm":
            {
                double p = parameter is { Type: JgsType.Number or JgsType.Bool } order ? order.AsNumber : 2;
                centre = 0;
                scale = VectorNorm(slice, p, line, col);
                break;
            }

            case "range":
            {
                double[] bounds = parameter is null ? [0, 1] : NumericVector("normalize", parameter, line, col);
                if (bounds.Length != 2)
                {
                    throw new JgsRuntimeException(line, col, "normalize: 'range' takes a [low high] pair.");
                }

                return RescaleInto(slice, bounds[0], bounds[1], null, null);
            }

            default:
                throw new JgsRuntimeException(line, col, $"normalize: no method called '{method}'.");
        }

        // A slice with no spread would divide by zero; MATLAB leaves such a slice centred rather than
        // filling it with infinities, and so does this.
        if (scale == 0 || double.IsNaN(scale))
        {
            scale = 1;
        }

        var result = new double[slice.Length];
        for (int i = 0; i < slice.Length; i++)
        {
            result[i] = (slice[i] - centre) / scale;
        }

        return result;
    }

    private static string MethodWord(
        string name, string method, JgsValue? parameter, string fallback, int line, int col, params string[] allowed)
    {
        if (parameter is null)
        {
            return fallback;
        }

        if (!IsTextScalar(parameter))
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{method}' takes a word.");
        }

        string word = TextOf(parameter);
        foreach (string candidate in allowed)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: '{method}' does not take '{word}' (expected one of '{string.Join("', '", allowed)}').");
    }

    private static double VectorNorm(double[] values, double p, int line, int col)
    {
        if (double.IsPositiveInfinity(p))
        {
            double largest = 0;
            foreach (double v in values)
            {
                largest = Math.Max(largest, Math.Abs(v));
            }

            return largest;
        }

        if (!(p > 0))
        {
            throw new JgsRuntimeException(line, col, "normalize: 'norm' takes a positive order or Inf.");
        }

        double total = 0;
        foreach (double v in values)
        {
            total += Math.Pow(Math.Abs(v), p);
        }

        return Math.Pow(total, 1 / p);
    }

    // --- rescale ------------------------------------------------------------------------------

    /// <summary>
    /// <c>rescale(A)</c>, <c>rescale(A, l, u)</c>: the whole array stretched onto an interval, not
    /// each column separately — which is the difference between <c>rescale</c> and
    /// <c>normalize(A, 'range')</c> and the reason both exist.
    /// </summary>
    private static JgsValue PreprocessRescale(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "rescale needs some data.");
        }

        ParsedArgs parsed = RescaleOptions.Parse(args, 3, line, col);
        BareValues bare = PrepStrip("rescale", parsed.Positional[0], line, col);
        double low = parsed.Positional.Count > 1 ? Num("rescale", parsed.Positional, 1, line, col) : 0;
        double high = parsed.Positional.Count > 2 ? Num("rescale", parsed.Positional, 2, line, col) : 1;

        double? inputMin = parsed.Named("InputMin") is null ? null : parsed.Scalar("InputMin", 0);
        double? inputMax = parsed.Named("InputMax") is null ? null : parsed.Scalar("InputMax", 1);

        return PrepDress(bare, RescaleInto(bare.Values, low, high, inputMin, inputMax), bare.Dims);
    }

    /// <summary>
    /// The stretch itself. When every value is the same there is no interval to stretch, and rather
    /// than divide by zero the answer is the low end for every element — the one place this family
    /// picks an answer instead of propagating a NaN, and it is picked because a constant signal
    /// rescaled to [0 1] is a constant signal, not a signal of NaNs.
    /// </summary>
    private static double[] RescaleInto(double[] values, double low, double high, double? givenMin, double? givenMax)
    {
        double[] finite = PrepPresent(values);
        double from = givenMin ?? (finite.Length == 0 ? 0 : finite.Min());
        double to = givenMax ?? (finite.Length == 0 ? 1 : finite.Max());
        double span = to - from;

        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = double.IsNaN(values[i]) ? double.NaN
                : span == 0 ? low
                : low + ((values[i] - from) * (high - low) / span);
        }

        return result;
    }

    // --- discretize ---------------------------------------------------------------------------

    /// <summary>
    /// <c>[bin, edges] = discretize(x, edges | n)</c>: which bin each value belongs to. Bins take
    /// their left edge and not their right — except the last, which takes both — exactly as
    /// <c>histcounts</c> counts them, because the two share the chooser that decides where the edges
    /// are. <c>'IncludedEdge', 'right'</c> flips which end each bin owns.
    /// </summary>
    private static JgsValue[] Discretized(
        IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "discretize(x, edges) needs data and either edges or a bin count.");
        }

        ParsedArgs parsed = DiscretizeOptions.Parse(args, 3, line, col);
        BareValues bare = PrepStrip("discretize", parsed.Positional[0], line, col);
        bool rightEdge = parsed.Word("IncludedEdge", "left", "left", "right") == "right";

        JgsValue second = parsed.Positional[1];
        double[] edges;
        if (second.Type is JgsType.Number or JgsType.Bool)
        {
            int count = Count("discretize", parsed.Positional, 1, line, col);
            if (count < 1)
            {
                throw new JgsRuntimeException(line, col, "discretize needs at least one bin.");
            }

            edges = Binning.EdgesFor(bare.Values, count, null, null, "auto");
        }
        else
        {
            edges = ToDoubles("discretize", second, line, col);
            if (edges.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "discretize: bin edges come in twos or more.");
            }
        }

        // A third positional is the value each bin stands for, so discretize can label as well as
        // number. Text labels are a categorical in MATLAB, which this does not have.
        double[]? binValues = null;
        if (parsed.Positional.Count > 2)
        {
            if (TextElementsOf(parsed.Positional[2]) is not null)
            {
                throw new JgsRuntimeException(line, col,
                    "discretize: named bins answer a categorical, which JGraph does not have — " +
                    "take the bin numbers and index your own list of names.");
            }

            binValues = ToDoubles("discretize", parsed.Positional[2], line, col);
            if (binValues.Length != edges.Length - 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"discretize: {edges.Length - 1} bins need {edges.Length - 1} values, but got {binValues.Length}.");
            }
        }

        var result = new double[bare.Values.Length];
        Binning.BinFinder finder = Binning.BinFinder.For(edges);
        for (int i = 0; i < result.Length; i++)
        {
            int bin = rightEdge ? finder.OfRightClosed(bare.Values[i]) : finder.Of(bare.Values[i]);
            result[i] = bin < 0 ? double.NaN
                : binValues is not null ? binValues[bin]
                : bin + dialect.IndexBase;
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(result, bare.Dims),
            Numbers(edges));
    }


    // --- fillmissing --------------------------------------------------------------------------

    /// <summary>
    /// <c>[F, TF] = fillmissing(A, method, …)</c>. Missing is NaN for numbers, NaT for times, and the
    /// empty string for text; the method says what goes in its place. Text takes the methods that
    /// only ever copy an existing value — a string cannot be interpolated — and says so for the ones
    /// that would have to invent one.
    /// </summary>
    private static JgsValue[] FilledMissing(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "fillmissing needs some data.");
        }

        if (TextElementsOf(args[0]) is { } texts)
        {
            return FilledMissingText(args, wanted, texts, line, col);
        }

        // The method word is positional, so it must not be read as an option name: the tail starts
        // after the data, the method, and the method's own parameter.
        (string method, int consumed) = FillMethod("fillmissing", args, line, col);
        ParsedArgs parsed = FillMissingOptions.Parse(OptionTail(args, consumed), positionalMax: 1, line, col);

        if (parsed.Named("DataVariables") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "fillmissing: 'DataVariables' picks variables out of a table, which fillmissing here does not take.");
        }

        BareValues bare = PrepStrip("fillmissing", args[0], line, col);
        double constant = 0;
        int window = 0;
        if (method == "constant")
        {
            if (args.Count < 3)
            {
                throw new JgsRuntimeException(line, col, "fillmissing(A, 'constant', v) needs the value to fill with.");
            }

            constant = Num("fillmissing", args, 2, line, col);
        }
        else if (method is "movmean" or "movmedian")
        {
            if (args.Count < 3)
            {
                throw new JgsRuntimeException(line, col, $"fillmissing(A, '{method}', k) needs a window width.");
            }

            window = Count("fillmissing", args, 2, line, col);
            if (window < 1)
            {
                throw new JgsRuntimeException(line, col, $"fillmissing: the '{method}' window must be at least 1 wide.");
            }
        }

        string endValues = "extrap";
        double endConstant = double.NaN;
        if (parsed.Named("EndValues") is { } given)
        {
            if (given.Type is JgsType.Number or JgsType.Bool)
            {
                (endValues, endConstant) = ("constant", given.AsNumber);
            }
            else
            {
                endValues = parsed.Word("EndValues", "extrap", "extrap", "none", "nearest", "previous", "next");
            }
        }

        int? dim = PrepDim("fillmissing", parsed.Positional, 0, line, col);
        (double[][] slices, int chosen) = PrepSlices(bare, dim);

        var mask = new bool[bare.Values.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = double.IsNaN(bare.Values[i]);
        }

        var done = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            done[s] = FillSlice(slices[s], method, constant, window, endValues, endConstant);
        }

        return Outputs(wanted, PrepJoin(bare, done, chosen), PrepMask(mask, bare.Dims));
    }

    /// <summary>The method word and how many leading arguments it and its parameter used up.</summary>
    private static (string Method, int Consumed) FillMethod(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2 || !IsTextScalar(args[1]))
        {
            // MATLAB's default is linear interpolation over the interior with the ends left alone.
            return ("linear", 1);
        }

        string word = TextOf(args[1]);
        string[] known =
        [
            "constant", "previous", "next", "nearest", "linear", "movmean", "movmedian",
        ];

        foreach (string candidate in known)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
            {
                bool takesParameter = candidate is "constant" or "movmean" or "movmedian";
                return (candidate, takesParameter ? 3 : 2);
            }
        }

        if (word is "spline" or "pchip" or "makima")
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: '{word}' fills a gap by fitting a curve through the neighbours, which JGraph does not do — " +
                "'linear', 'nearest', 'previous' or 'next' fill from the values that are there.");
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: no method called '{word}' (expected one of '{string.Join("', '", known)}').");
    }

    private static double[] FillSlice(
        double[] slice, string method, double constant, int window, string endValues, double endConstant)
    {
        var result = (double[])slice.Clone();
        if (method == "constant")
        {
            for (int i = 0; i < result.Length; i++)
            {
                if (double.IsNaN(result[i]))
                {
                    result[i] = constant;
                }
            }

            return result;
        }

        if (method is "movmean" or "movmedian")
        {
            int behind = window / 2;
            int ahead = (window - 1) / 2;
            for (int i = 0; i < result.Length; i++)
            {
                if (!double.IsNaN(slice[i]))
                {
                    continue;
                }

                var seen = new List<double>(window);
                for (int j = Math.Max(0, i - behind); j <= Math.Min(slice.Length - 1, i + ahead); j++)
                {
                    if (!double.IsNaN(slice[j]))
                    {
                        seen.Add(slice[j]);
                    }
                }

                result[i] = seen.Count == 0 ? double.NaN
                    : method == "movmean" ? seen.Average()
                    : MedianOf([.. seen]);
            }

            return result;
        }

        // Every remaining method fills from the nearest values that exist, so they all need to know
        // which those are before deciding what to do with them.
        for (int i = 0; i < result.Length; i++)
        {
            if (!double.IsNaN(slice[i]))
            {
                continue;
            }

            int before = PreviousPresent(slice, i);
            int after = NextPresent(slice, i);
            bool interior = before >= 0 && after >= 0;

            if (!interior)
            {
                result[i] = EndFill(slice, i, before, after, method, endValues, endConstant);
                continue;
            }

            result[i] = method switch
            {
                "previous" => slice[before],
                "next" => slice[after],
                "nearest" => i - before <= after - i ? slice[before] : slice[after],
                _ => slice[before] + ((slice[after] - slice[before]) * (i - before) / (double)(after - before)),
            };
        }

        return result;
    }

    /// <summary>What goes in a gap that runs off one end of the data, where there is nothing to interpolate between.</summary>
    private static double EndFill(
        double[] slice, int at, int before, int after, string method, string endValues, double endConstant)
    {
        if (before < 0 && after < 0)
        {
            return double.NaN; // nothing present anywhere: there is no value to copy or reach for
        }

        return endValues switch
        {
            "none" => double.NaN,
            "constant" => endConstant,
            "previous" => before >= 0 ? slice[before] : double.NaN,
            "next" => after >= 0 ? slice[after] : double.NaN,
            _ => method switch
            {
                "previous" => before >= 0 ? slice[before] : double.NaN,
                "next" => after >= 0 ? slice[after] : double.NaN,
                _ => before >= 0 ? slice[before] : slice[after],
            },
        };

        // 'extrap' for linear would mean continuing the last slope past the data. MATLAB's default
        // for the ends is to hold the nearest value, and holding it is the answer a measurement log
        // wants; the sloped continuation is left out rather than guessed at.
    }

    private static int PreviousPresent(double[] slice, int from)
    {
        for (int i = from - 1; i >= 0; i--)
        {
            if (!double.IsNaN(slice[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int NextPresent(double[] slice, int from)
    {
        for (int i = from + 1; i < slice.Length; i++)
        {
            if (!double.IsNaN(slice[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Text filling: missing is the empty string, and only the copying methods apply.</summary>
    private static JgsValue[] FilledMissingText(
        IReadOnlyList<JgsValue> args, int wanted, string[] texts, int line, int col)
    {
        (string method, _) = FillMethod("fillmissing", args, line, col);
        string constant = string.Empty;
        if (method == "constant")
        {
            if (args.Count < 3 || !IsTextScalar(args[2]))
            {
                throw new JgsRuntimeException(line, col,
                    "fillmissing: text filled with a constant needs a string to fill with.");
            }

            constant = TextOf(args[2]);
        }
        else if (method is not ("previous" or "next" or "nearest"))
        {
            throw new JgsRuntimeException(line, col,
                $"fillmissing: '{method}' would have to invent a string; text takes 'constant', 'previous', " +
                "'next' or 'nearest'.");
        }

        var filled = (string[])texts.Clone();
        var mask = new bool[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            mask[i] = texts[i].Length == 0;
        }

        for (int i = 0; i < filled.Length; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            int before = PreviousText(mask, i);
            int after = NextText(mask, i);
            filled[i] = method switch
            {
                "constant" => constant,
                "previous" => before >= 0 ? texts[before] : string.Empty,
                "next" => after >= 0 ? texts[after] : string.Empty,
                _ => before >= 0 && (after < 0 || i - before <= after - i) ? texts[before]
                    : after >= 0 ? texts[after]
                    : string.Empty,
            };
        }

        JgsValue answer = args[0].Type == JgsType.Cell
            ? JgsValue.Cell(Array.ConvertAll(filled, JgsValue.Str))
            : JgsValue.StringArray(Array.ConvertAll(filled, JgsValue.Str));
        answer.TakeShapeOf(args[0]);

        return Outputs(wanted, answer, PrepMask(mask, SizeDims(args[0])));
    }

    private static int PreviousText(bool[] missing, int from)
    {
        for (int i = from - 1; i >= 0; i--)
        {
            if (!missing[i])
            {
                return i;
            }
        }

        return -1;
    }

    private static int NextText(bool[] missing, int from)
    {
        for (int i = from + 1; i < missing.Length; i++)
        {
            if (!missing[i])
            {
                return i;
            }
        }

        return -1;
    }

    // --- rmmissing ----------------------------------------------------------------------------

    /// <summary>
    /// <c>[R, TF] = rmmissing(A)</c>: a vector loses its missing entries, and a matrix loses whole
    /// rows — because a matrix with one element removed is no longer a matrix, and MATLAB chooses to
    /// keep the shape rectangular rather than the data complete.
    /// </summary>
    private static JgsValue[] RemovedMissing(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "rmmissing needs some data.");
        }

        if (TextElementsOf(args[0]) is { } texts)
        {
            var keptText = new List<string>(texts.Length);
            var textMask = new bool[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                textMask[i] = texts[i].Length == 0;
                if (!textMask[i])
                {
                    keptText.Add(texts[i]);
                }
            }

            JgsValue trimmed = args[0].Type == JgsType.Cell
                ? JgsValue.Cell([.. keptText.Select(JgsValue.Str)])
                : JgsValue.StringArray([.. keptText.Select(JgsValue.Str)]);
            if (args[0].Cols == 1 && args[0].Rows > 1)
            {
                trimmed.Reshape(keptText.Count, keptText.Count == 0 ? 0 : 1);
            }

            return Outputs(wanted, trimmed, PrepMask(textMask, SizeDims(args[0])));
        }

        ParsedArgs parsed = RmMissingOptions.Parse(args, 2, line, col);
        if (parsed.Named("DataVariables") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "rmmissing: 'DataVariables' picks variables out of a table, which rmmissing here does not take.");
        }

        BareValues bare = PrepStrip("rmmissing", parsed.Positional[0], line, col);
        int minMissing = Math.Max(1, parsed.Whole("MinNumMissing", 1));
        int rows = bare.Dims.Length > 0 ? bare.Dims[0] : 0;
        int cols = bare.Values.Length == 0 ? 0 : bare.Values.Length / Math.Max(1, rows);

        if (bare.Dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                "rmmissing: removing entries from an N-D array has no shape to answer with; work one plane at a time.");
        }

        // A vector — either orientation — drops elements; anything else drops rows.
        bool asVector = rows <= 1 || cols <= 1;
        int? dim = PrepDim("rmmissing", parsed.Positional, 1, line, col);
        bool alongRows = dim is null or 1;

        if (asVector)
        {
            var kept = new List<double>(bare.Values.Length);
            var mask = new bool[bare.Values.Length];
            for (int i = 0; i < bare.Values.Length; i++)
            {
                mask[i] = double.IsNaN(bare.Values[i]);
                if (!mask[i])
                {
                    kept.Add(bare.Values[i]);
                }
            }

            int[] shape = rows > 1 ? [kept.Count, kept.Count == 0 ? 0 : 1] : [kept.Count == 0 ? 0 : 1, kept.Count];
            return Outputs(wanted, PrepDress(bare, [.. kept], shape), PrepMask(mask, bare.Dims));
        }

        int along = alongRows ? rows : cols;
        int across = alongRows ? cols : rows;
        var drop = new bool[along];
        for (int i = 0; i < along; i++)
        {
            int missing = 0;
            for (int j = 0; j < across; j++)
            {
                int index = alongRows ? i + (j * rows) : (i * rows) + j;
                if (double.IsNaN(bare.Values[index]))
                {
                    missing++;
                }
            }

            drop[i] = missing >= minMissing;
        }

        var survivors = new List<int>(along);
        for (int i = 0; i < along; i++)
        {
            if (!drop[i])
            {
                survivors.Add(i);
            }
        }

        var packed = new double[survivors.Count * across];
        for (int j = 0; j < across; j++)
        {
            for (int k = 0; k < survivors.Count; k++)
            {
                int index = alongRows ? survivors[k] + (j * rows) : (survivors[k] * rows) + j;
                int target = alongRows ? k + (j * survivors.Count) : (k * across) + j;
                packed[target] = bare.Values[index];
            }
        }

        int[] dims = alongRows ? [survivors.Count, across] : [across, survivors.Count];
        return Outputs(
            wanted,
            PrepDress(bare, packed, dims),
            PrepMask(drop, alongRows ? [along, 1] : [1, along]));
    }

    // --- islocalmax / islocalmin --------------------------------------------------------------

    /// <summary>
    /// <c>[TF, P] = islocalmax(A, …)</c>: the local extrema and how far each one stands above its
    /// surroundings. Prominence is the whole point — without it every ripple in a noisy trace is a
    /// maximum, and <c>'MinProminence'</c> is what turns the mask into an answer.
    /// </summary>
    private static JgsValue[] LocalExtrema(
        string name, IReadOnlyList<JgsValue> args, int wanted, bool maxima, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs some data.");
        }

        OptionSpec spec = LocalExtremaOptions with { Builtin = name };
        ParsedArgs parsed = spec.Parse(args, 2, line, col);
        BareValues bare = PrepStrip(name, parsed.Positional[0], line, col);

        double[]? points = parsed.Vector("SamplePoints");
        string flat = parsed.Word("FlatSelection", "center", "center", "first", "last", "all");
        double minProminence = parsed.Scalar("MinProminence", 0);
        double minSeparation = parsed.Scalar("MinSeparation", 0);
        int maxCount = parsed.Whole("MaxNumExtrema", int.MaxValue);

        int? dim = PrepDim(name, parsed.Positional, 1, line, col);
        (double[][] slices, int chosen) = PrepSlices(bare, dim);

        var masks = new double[slices.Length][];
        var proms = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            if (points is not null && points.Length != slices[s].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'SamplePoints' has {points.Length} places for {slices[s].Length} values.");
            }

            (bool[] found, double[] prominence) = ExtremaOfSlice(
                slices[s], maxima, flat, points, minProminence, minSeparation, maxCount);

            masks[s] = Array.ConvertAll(found, static f => f ? 1.0 : 0.0);
            proms[s] = prominence;
        }

        (double[] flags, int[] shape) = JgsMatrix.JoinAlong(masks, bare.Dims, chosen);
        (double[] prominences, _) = JgsMatrix.JoinAlong(proms, bare.Dims, chosen);

        return Outputs(
            wanted,
            PrepMask(Array.ConvertAll(flags, static f => f != 0), shape),
            JgsMatrix.FromColumnMajorDims(prominences, shape));
    }

    /// <summary>One slice's extrema, their prominences, and the thinning the options asked for.</summary>
    private static (bool[] Found, double[] Prominence) ExtremaOfSlice(
        double[] slice, bool maxima, string flat, double[]? points,
        double minProminence, double minSeparation, int maxCount)
    {
        int n = slice.Length;
        var found = new bool[n];
        var prominence = new double[n];
        if (n < 3)
        {
            return (found, prominence); // the ends are never local extrema, so two points have none
        }

        // Work on the signal the caller means: a minimum of x is a maximum of −x, and one search
        // written once beats two searches that have to agree.
        var signal = new double[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = maxima ? slice[i] : -slice[i];
        }

        for (int i = 1; i < n - 1; i++)
        {
            if (double.IsNaN(signal[i]))
            {
                continue;
            }

            int left = i - 1;
            while (left >= 0 && (double.IsNaN(signal[left]) || signal[left] == signal[i]))
            {
                left--;
            }

            int right = i + 1;
            while (right < n && (double.IsNaN(signal[right]) || signal[right] == signal[i]))
            {
                right++;
            }

            if (left < 0 || right >= n || signal[left] >= signal[i] || signal[right] >= signal[i])
            {
                continue;
            }

            // A flat top is one extremum spread over several places; which of them is marked is what
            // 'FlatSelection' decides.
            int runStart = left + 1;
            int runStop = right - 1;
            switch (flat)
            {
                case "first":
                    found[runStart] = true;
                    break;
                case "last":
                    found[runStop] = true;
                    break;
                case "all":
                    for (int k = runStart; k <= runStop; k++)
                    {
                        found[k] = true;
                    }

                    break;
                default:
                    found[(runStart + runStop) / 2] = true;
                    break;
            }

            double height = ProminenceAt(signal, i);
            for (int k = runStart; k <= runStop; k++)
            {
                prominence[k] = found[k] ? height : prominence[k];
            }

            i = runStop;
        }

        if (minProminence > 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (found[i] && prominence[i] < minProminence)
                {
                    found[i] = false;
                }
            }
        }

        if (minSeparation > 0)
        {
            ThinBySeparation(found, prominence, points, minSeparation);
        }

        if (maxCount < n)
        {
            KeepStrongest(found, prominence, maxCount);
        }

        return (found, prominence);
    }

    /// <summary>
    /// How far a peak stands above the higher of the two valleys that flank it — walking outwards
    /// until the signal rises above the peak, which is what makes prominence a measure of the peak's
    /// own standing rather than of its height above zero.
    /// </summary>
    private static double ProminenceAt(double[] signal, int peak)
    {
        double height = signal[peak];

        double leftFloor = double.PositiveInfinity;
        for (int i = peak - 1; i >= 0; i--)
        {
            if (double.IsNaN(signal[i]))
            {
                continue;
            }

            if (signal[i] > height)
            {
                break;
            }

            leftFloor = Math.Min(leftFloor, signal[i]);
        }

        double rightFloor = double.PositiveInfinity;
        for (int i = peak + 1; i < signal.Length; i++)
        {
            if (double.IsNaN(signal[i]))
            {
                continue;
            }

            if (signal[i] > height)
            {
                break;
            }

            rightFloor = Math.Min(rightFloor, signal[i]);
        }

        // A peak with nothing higher on one side is measured against the side that does enclose it.
        double floor = Math.Max(
            double.IsPositiveInfinity(leftFloor) ? double.NegativeInfinity : leftFloor,
            double.IsPositiveInfinity(rightFloor) ? double.NegativeInfinity : rightFloor);

        return double.IsNegativeInfinity(floor) ? height : height - floor;
    }

    /// <summary>Keeps the more prominent of any two extrema closer together than the separation asked for.</summary>
    private static void ThinBySeparation(bool[] found, double[] prominence, double[]? points, double minSeparation)
    {
        var order = new List<int>();
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i])
            {
                order.Add(i);
            }
        }

        order.Sort((a, b) => prominence[b].CompareTo(prominence[a]));
        var kept = new List<int>(order.Count);
        foreach (int candidate in order)
        {
            bool crowded = false;
            foreach (int already in kept)
            {
                double gap = points is null
                    ? Math.Abs(candidate - already)
                    : Math.Abs(points[candidate] - points[already]);
                if (gap < minSeparation)
                {
                    crowded = true;
                    break;
                }
            }

            if (crowded)
            {
                found[candidate] = false;
            }
            else
            {
                kept.Add(candidate);
            }
        }
    }

    private static void KeepStrongest(bool[] found, double[] prominence, int maxCount)
    {
        var order = new List<int>();
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i])
            {
                order.Add(i);
            }
        }

        if (order.Count <= maxCount)
        {
            return;
        }

        order.Sort((a, b) => prominence[b].CompareTo(prominence[a]));
        for (int i = maxCount; i < order.Count; i++)
        {
            found[order[i]] = false;
        }
    }

    // --- smoothdata ---------------------------------------------------------------------------

    /// <summary>
    /// <c>[B, window] = smoothdata(A, …)</c>: the moving and local-regression smoothers behind one
    /// name. The second output is the window it chose, which is the only way a script can find out
    /// what the automatic width actually was.
    /// </summary>
    private static JgsValue[] Smoothed(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "smoothdata needs some data.");
        }

        (string method, int consumed) = SmoothMethod(args, line, col);
        ParsedArgs parsed = SmoothOptions.Parse(OptionTail(args, consumed), positionalMax: 2, line, col);

        BareValues bare = PrepStrip("smoothdata", args[0], line, col);
        bool omitNan = parsed.OneOf("omitnan", "includenan", "omitnan") == "omitnan";
        double[]? points = parsed.Vector("SamplePoints");

        // The dimension may come before the method or after it, so both leading positionals are
        // examined: whichever numbers are there are the dimension and the window.
        int? dim = PrepDim("smoothdata", args, 1, line, col);
        int? given = null;
        for (int i = 0; i < parsed.Positional.Count; i++)
        {
            if (parsed.Positional[i].Type is JgsType.Number or JgsType.Bool)
            {
                given = Count("smoothdata", parsed.Positional, i, line, col);
            }
        }

        (double[][] slices, int chosen) = PrepSlices(bare, dim);
        int length = slices.Length == 0 ? 0 : slices[0].Length;
        int window = given ?? AutomaticWindow(length, parsed.Named("SmoothingFactor") is null
            ? null
            : parsed.Scalar("SmoothingFactor", 0.25), line, col);
        int degree = parsed.Whole("degree", method == "sgolay" ? 2 : 1);

        var done = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            if (points is not null && points.Length != slices[s].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"smoothdata: 'SamplePoints' has {points.Length} places for {slices[s].Length} values.");
            }

            done[s] = SmoothSlice(slices[s], method, window, degree, omitNan, points);
        }

        return Outputs(wanted, PrepJoin(bare, done, chosen), JgsValue.Number(window));
    }

    private static (string Method, int Consumed) SmoothMethod(IReadOnlyList<JgsValue> args, int line, int col)
    {
        string[] known =
        [
            "movmean", "movmedian", "gaussian", "lowess", "loess", "rlowess", "rloess", "sgolay",
        ];

        // The method word may sit in slot 1 or, when a dimension came first, in slot 2.
        for (int slot = 1; slot <= 2 && slot < args.Count; slot++)
        {
            if (!IsTextScalar(args[slot]))
            {
                continue;
            }

            string word = TextOf(args[slot]);
            foreach (string candidate in known)
            {
                if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
                {
                    return (candidate, slot + 1);
                }
            }

            // A string in a method slot that names no method is a misspelling, not an option: the
            // option names are all name-value pairs and none of them is a bare word here.
            if (!string.Equals(word, "omitnan", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(word, "includenan", StringComparison.OrdinalIgnoreCase)
                && SmoothOptions.Names.All(n => !string.Equals(n, word, StringComparison.OrdinalIgnoreCase)))
            {
                throw new JgsRuntimeException(line, col,
                    $"smoothdata: no method called '{word}' (expected one of '{string.Join("', '", known)}').");
            }

            break;
        }

        return ("movmean", 1);
    }

    /// <summary>
    /// The window when the call did not name one. MATLAB derives it from the data; this uses a tenth
    /// of the length, and <c>'SmoothingFactor'</c> replaces the tenth with a fraction of the caller's
    /// choosing. The exact rule is recorded in ADR 0066 rather than claimed to match.
    /// </summary>
    private static int AutomaticWindow(int length, double? factor, int line, int col)
    {
        if (factor is { } f)
        {
            if (f is < 0 or > 1)
            {
                throw new JgsRuntimeException(line, col, "smoothdata: 'SmoothingFactor' runs from 0 to 1.");
            }

            return Math.Max(2, (int)Math.Ceiling(f * length));
        }

        return Math.Max(2, (int)Math.Ceiling(length / 10.0));
    }

    private static double[] SmoothSlice(
        double[] slice, string method, int window, int degree, bool omitNan, double[]? points)
    {
        int n = slice.Length;
        int behind = window / 2;
        int ahead = (window - 1) / 2;

        // The two moving-average methods are the same shrinking window the mov* family slides, and
        // the default window here is a tenth of the data (ADR 0066) — so rebuilding it at every
        // point made the default call quadratic in the length of the series.
        WindowStat kind = method switch
        {
            "movmedian" => WindowStat.Median,
            "gaussian" or "lowess" or "loess" or "rlowess" or "rloess" or "sgolay" => WindowStat.Other,
            _ => WindowStat.Mean,
        };
        if (points is null && WindowKernels.Handles(kind))
        {
            return WindowKernels.Slide(
                kind, slice, behind, ahead, WindowEnds.Shrink, 0, omitNan, double.NaN);
        }

        // Evenly spaced readings give every interior window the same offsets from its own centre,
        // and a window whose shape does not change from one point to the next weighs its neighbours
        // by one row of numbers, worked out once. The robust fits are not in this list because
        // their weights are read off the data, so their shape does change — and a series carrying
        // places of its own has no fixed offsets to begin with.
        double[]? shaped = null;
        bool[]? unanswered = null;
        if (points is null)
        {
            // Stepping over a missing reading changes the shape of every window that holds it, so
            // those windows are walked afterwards and all the others are answered by the kernel.
            // That trade is worth making while there are fewer windows to walk than the series has
            // points; past that it is cheaper to walk the lot, which is what the walk below does.
            int missing = omitNan ? SmoothKernels.Missing(slice) : 0;
            if ((long)missing * (behind + ahead + 1) < n)
            {
                shaped = method switch
                {
                    "gaussian" => SmoothKernels.Gaussian(slice, behind, ahead, window),
                    "lowess" => SmoothKernels.LocalPolynomial(slice, behind, ahead, 1, weighted: true),
                    "loess" => SmoothKernels.LocalPolynomial(slice, behind, ahead, 2, weighted: true),
                    "sgolay" => SmoothKernels.LocalPolynomial(
                        slice, behind, ahead, Math.Max(1, degree), weighted: false),
                    _ => null,
                };
            }

            if (shaped is not null)
            {
                if (missing == 0)
                {
                    return shaped;
                }

                // A window holds the reading at i when it starts no later than i and ends no
                // earlier, which is every point from i - ahead to i + behind.
                unanswered = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (!double.IsNaN(slice[i]))
                    {
                        continue;
                    }

                    for (int j = Math.Max(0, i - ahead); j <= Math.Min(n - 1, i + behind); j++)
                    {
                        unanswered[j] = true;
                    }
                }
            }
        }

        // Everything else reads its window one at a time. The buffers below are filled and refilled
        // rather than built per sample: this walk used to allocate four arrays for every answer it
        // gave, and a normal system's worth of scratch for every reading inside every one of them.
        int order = method switch
        {
            "loess" or "rloess" => 2,
            "sgolay" => Math.Max(1, degree),
            _ => 1,
        };
        double[] result = shaped ?? new double[n];
        var xs = new double[n];
        var ys = new double[n];
        FitBuffers buffers = FitBuffers.For(n, order);
        for (int i = 0; i < n; i++)
        {
            if (unanswered is not null && !unanswered[i])
            {
                continue;
            }

            (int from, int to) = points is null
                ? (Math.Max(0, i - behind), Math.Min(n - 1, i + ahead))
                : SpanAround(points, i, window);

            int held = 0;
            for (int j = from; j <= to; j++)
            {
                if (omitNan && double.IsNaN(slice[j]))
                {
                    continue;
                }

                xs[held] = points is null ? j : points[j];
                ys[held] = slice[j];
                held++;
            }

            if (held == 0)
            {
                result[i] = double.NaN;
                continue;
            }

            ReadOnlySpan<double> alongX = xs.AsSpan(0, held);
            ReadOnlySpan<double> alongY = ys.AsSpan(0, held);
            double at = points is null ? i : points[i];
            result[i] = method switch
            {
                "movmedian" => MedianOf(alongY, buffers.Sorted),
                "gaussian" => GaussianAt(alongX, alongY, at, window),
                "lowess" or "loess" => LocalFit(alongX, alongY, at, order, false, buffers),
                "rlowess" or "rloess" => LocalFit(alongX, alongY, at, order, true, buffers),
                "sgolay" => LocalFit(alongX, alongY, at, order, false, buffers, weighted: false),
                _ => SmoothKernels.Mean(alongY),
            };
        }

        return result;
    }

    /// <summary>The window around a point when the samples are not evenly spaced.</summary>
    private static (int From, int To) SpanAround(double[] points, int at, double window)
    {
        double half = window / 2.0;
        int from = at;
        while (from > 0 && points[at] - points[from - 1] <= half)
        {
            from--;
        }

        int to = at;
        while (to < points.Length - 1 && points[to + 1] - points[at] <= half)
        {
            to++;
        }

        return (from, to);
    }

    /// <summary>A Gaussian-weighted average whose standard deviation is a quarter of the window.</summary>
    private static double GaussianAt(
        ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, double at, double window)
    {
        double sigma = Math.Max(window / 4.0, 1e-12);
        double total = 0;
        double weight = 0;
        for (int i = 0; i < xs.Length; i++)
        {
            double z = (xs[i] - at) / sigma;
            double w = Math.Exp(-0.5 * z * z);
            total += w * ys[i];
            weight += w;
        }

        return weight == 0 ? double.NaN : total / weight;
    }

    /// <summary>The middle of a window, sorted into a buffer the caller keeps rather than a fresh copy.</summary>
    private static double MedianOf(ReadOnlySpan<double> window, double[] scratch)
    {
        window.CopyTo(scratch);
        Array.Sort(scratch, 0, window.Length);
        int middle = window.Length / 2;
        return window.Length % 2 == 1
            ? scratch[middle]
            : (scratch[middle - 1] + scratch[middle]) / 2.0;
    }

    /// <summary>
    /// A polynomial fitted through the window and evaluated at its centre — the local regression
    /// behind lowess (degree 1), loess (degree 2) and sgolay. The robust variants refit a few times,
    /// shrinking the weight of whatever the previous fit missed by the most, which is what stops one
    /// outlier from dragging the whole window.
    /// </summary>
    /// <remarks>
    /// One fit answers everywhere. Each pass of the robust loop used to solve the normal system
    /// again for every residual it measured — as many systems as the window has readings, where one
    /// does, since a residual asks the same polynomial about a different place rather than asking a
    /// different polynomial about the same one.
    /// </remarks>
    private static double LocalFit(
        ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, double at, int degree, bool robust,
        in FitBuffers buffers, bool weighted = true)
    {
        int n = xs.Length;
        if (n == 0)
        {
            return double.NaN;
        }

        if (n <= degree)
        {
            return SmoothKernels.Mean(ys);
        }

        double furthest = 0;
        foreach (double x in xs)
        {
            furthest = Math.Max(furthest, Math.Abs(x - at));
        }

        Span<double> weights = buffers.Weights.AsSpan(0, n);
        for (int i = 0; i < n; i++)
        {
            if (!weighted || furthest == 0)
            {
                weights[i] = 1;
                continue;
            }

            double u = Math.Abs(xs[i] - at) / furthest;
            double tri = 1 - (u * u * u);
            weights[i] = Math.Max(0, tri * tri * tri);
        }

        Span<double> coefficients = buffers.Coefficients.AsSpan(0, degree + 1);
        SmoothKernels.Fit(xs, ys, weights, degree, at, buffers.Normal, buffers.Powers, coefficients);
        double fitted = coefficients[0];
        if (!robust)
        {
            return fitted;
        }

        Span<double> residuals = buffers.Residuals.AsSpan(0, n);
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                residuals[i] = Math.Abs(ys[i] - SmoothKernels.At(coefficients, xs[i] - at));
            }

            double median = MedianOf(residuals, buffers.Sorted);
            if (median == 0)
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                double u = residuals[i] / (6 * median);
                double bisquare = u >= 1 ? 0 : (1 - (u * u)) * (1 - (u * u));
                weights[i] *= bisquare;
            }

            SmoothKernels.Fit(xs, ys, weights, degree, at, buffers.Normal, buffers.Powers, coefficients);
            fitted = coefficients[0];
        }

        return fitted;
    }

    /// <summary>Everything a windowed fit writes in, held for the whole slice rather than per sample.</summary>
    private readonly record struct FitBuffers(
        double[] Weights, double[] Residuals, double[] Sorted,
        double[] Normal, double[] Powers, double[] Coefficients)
    {
        public static FitBuffers For(int length, int degree)
        {
            int terms = degree + 1;
            return new FitBuffers(
                new double[length], new double[length], new double[length],
                new double[terms * (terms + 1)], new double[terms], new double[terms]);
        }
    }

    // --- groupsummary -------------------------------------------------------------------------

    /// <summary>
    /// <c>groupsummary</c> over an array — <c>[B, groups] = groupsummary(A, G, method)</c> — and over
    /// a table, where the answer is a table with one row per group. The two forms share the grouping
    /// itself: what differs is only what the summary is wrapped in.
    /// </summary>
    private static JgsValue[] GroupSummary(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "groupsummary(A, groups, method) needs at least the data and the groups.");
        }

        if (args[0].Type == JgsType.Table)
        {
            return [GroupSummaryTable(args, line, col)];
        }

        BareValues bare = PrepStrip("groupsummary", args[0], line, col);
        (string[] keys, string[] order) = GroupKeys("groupsummary", args[1], bare.Values.Length, line, col);
        string method = args.Count > 2
            ? SummaryMethod("groupsummary", args[2], line, col)
            : "count";

        var summarised = new double[order.Length];
        for (int g = 0; g < order.Length; g++)
        {
            var members = new List<double>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == order[g])
                {
                    members.Add(bare.Values[i]);
                }
            }

            summarised[g] = Summarise(method, [.. members]);
        }

        // The summary of a datetime is a datetime when the summary is one of the values (a minimum,
        // a median); a count of them is a count. That distinction is the tag, and it is decided here
        // rather than inside Summarise so the arithmetic stays about numbers.
        JgsValue answer = MethodKeepsUnits(method)
            ? PrepDress(bare, summarised, [order.Length, order.Length == 0 ? 0 : 1])
            : JgsMatrix.FromColumnMajorDims(summarised, [order.Length, order.Length == 0 ? 0 : 1]);

        return Outputs(wanted, answer, GroupLabels(args[1], order));
    }

    private static bool MethodKeepsUnits(string method) =>
        method is "min" or "max" or "median" or "mean" or "mode";

    /// <summary>The group each element belongs to, as text, plus the groups in first-seen order.</summary>
    private static (string[] Keys, string[] Order) GroupKeys(
        string name, JgsValue groups, int expected, int line, int col)
    {
        string[] keys;
        if (TextElementsOf(groups) is { } texts)
        {
            keys = texts;
        }
        else
        {
            double[] numbers = FlattenColumnMajor(name, groups, line, col);
            keys = Array.ConvertAll(numbers, static v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (keys.Length != expected)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: {keys.Length} group labels for {expected} values.");
        }

        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (seen.Add(key))
            {
                order.Add(key);
            }
        }

        // Sorted, because MATLAB's group order is the sorted order of the grouping variable and a
        // script that indexes the second output expects the same order twice running.
        order.Sort(StringComparer.Ordinal);
        return (keys, [.. order]);
    }

    /// <summary>The group identifiers, given back in whatever kind they arrived as.</summary>
    private static JgsValue GroupLabels(JgsValue groups, string[] order)
    {
        if (TextElementsOf(groups) is not null)
        {
            JgsValue text = groups.Type == JgsType.Cell
                ? JgsValue.Cell(Array.ConvertAll(order, JgsValue.Str))
                : JgsValue.StringArray(Array.ConvertAll(order, JgsValue.Str));
            text.Reshape(order.Length, order.Length == 0 ? 0 : 1);
            return text;
        }

        var numbers = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            numbers[i] = double.Parse(order[i], System.Globalization.CultureInfo.InvariantCulture);
        }

        return JgsMatrix.FromColumnMajorDims(numbers, [order.Length, order.Length == 0 ? 0 : 1]);
    }

    private static string SummaryMethod(string name, JgsValue given, int line, int col)
    {
        string[] known =
        [
            "sum", "mean", "median", "mode", "min", "max", "range", "std", "var",
            "count", "nnz", "nummissing", "numunique", "all", "any",
        ];

        if (!IsTextScalar(given))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the method is a word.");
        }

        string word = TextOf(given);
        foreach (string candidate in known)
        {
            if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: no method called '{word}' (expected one of '{string.Join("', '", known)}').");
    }

    private static double Summarise(string method, double[] values)
    {
        double[] present = PrepPresent(values);
        switch (method)
        {
            case "count":
                return values.Length;
            case "nnz":
                return present.Count(static v => v != 0);
            case "nummissing":
                return values.Length - present.Length;
            case "numunique":
                return present.Distinct().Count();
            case "all":
                return present.All(static v => v != 0) ? 1 : 0;
            case "any":
                return present.Any(static v => v != 0) ? 1 : 0;
        }

        if (present.Length == 0)
        {
            return method is "sum" ? 0 : double.NaN;
        }

        return method switch
        {
            "sum" => present.Sum(),
            "mean" => present.Average(),
            "median" => MedianOf(present),
            "mode" => present.GroupBy(static v => v).OrderByDescending(static g => g.Count())
                .ThenBy(static g => g.Key).First().Key,
            "min" => present.Min(),
            "max" => present.Max(),
            "range" => present.Max() - present.Min(),
            "std" => PrepStd(present),
            "var" => present.Length < 2 ? 0 : SampleVarianceOf(present),
            _ => double.NaN,
        };
    }

    /// <summary>
    /// The table form: one row per group, a <c>GroupCount</c> variable, and one summary variable per
    /// data variable, named the way MATLAB names them — <c>mean_pressure</c>, not <c>pressure</c>.
    /// </summary>
    private static JgsValue GroupSummaryTable(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Table table = args[0].AsTable;
        string[] groupNames = TableVariableNames("groupsummary", table, args[1], line, col);
        if (groupNames.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "groupsummary: name at least one grouping variable.");
        }

        string? method = args.Count > 2 ? SummaryMethod("groupsummary", args[2], line, col) : null;
        string[] dataNames = args.Count > 3
            ? TableVariableNames("groupsummary", table, args[3], line, col)
            : [.. table.ColumnNames.Where(n => !groupNames.Contains(n, StringComparer.Ordinal))];

        // One key per row, made from every grouping variable at once, so two variables group the way
        // one pair does rather than the way two separate passes would.
        var keys = new string[table.RowCount];
        for (int r = 0; r < table.RowCount; r++)
        {
            keys[r] = string.Join(GroupKeySeparator, groupNames.Select(n => TableCellText(table, n, r)));
        }

        var order = keys.Distinct(StringComparer.Ordinal).ToList();
        order.Sort(StringComparer.Ordinal);

        var columns = new List<TableColumn>();
        for (int g = 0; g < groupNames.Length; g++)
        {
            TableColumn source = TableVariable("groupsummary", table, groupNames[g], line, col);
            if (source.Type == ColumnType.Text)
            {
                columns.Add(new TextColumn(groupNames[g], [.. order.Select(k => k.Split(GroupKeySeparator)[g])]));
            }
            else
            {
                columns.Add(new NumberColumn(groupNames[g], [.. order.Select(k =>
                    double.Parse(k.Split(GroupKeySeparator)[g], System.Globalization.CultureInfo.InvariantCulture))]));
            }
        }

        var counts = new double[order.Count];
        for (int g = 0; g < order.Count; g++)
        {
            counts[g] = keys.Count(k => string.Equals(k, order[g], StringComparison.Ordinal));
        }

        columns.Add(new NumberColumn("GroupCount", counts));

        if (method is not null and not "count")
        {
            foreach (string data in dataNames)
            {
                TableColumn source = TableVariable("groupsummary", table, data, line, col);
                if (source.Type == ColumnType.Text)
                {
                    continue; // a mean of words has no answer, and skipping is what MATLAB does
                }

                var summarised = new double[order.Count];
                for (int g = 0; g < order.Count; g++)
                {
                    var members = new List<double>();
                    for (int r = 0; r < table.RowCount; r++)
                    {
                        if (string.Equals(keys[r], order[g], StringComparison.Ordinal))
                        {
                            members.Add(source.GetNumber(r));
                        }
                    }

                    summarised[g] = Summarise(method, [.. members]);
                }

                columns.Add(new NumberColumn($"{method}_{data}", summarised));
            }
        }

        return JgsValue.Table(new Table(columns));
    }

    private static string TableCellText(Table table, string name, int row)
    {
        TableColumn column = table.Columns.First(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        return column.Type == ColumnType.Text
            ? column.GetText(row)
            : column.GetNumber(row).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Variable names given as one word, a cell of words, a string array, or column numbers.</summary>
    private static string[] TableVariableNames(string name, Table table, JgsValue given, int line, int col)
    {
        if (TextElementsOf(given) is { } texts)
        {
            foreach (string text in texts)
            {
                _ = TableVariable(name, table, text, line, col);
            }

            return texts;
        }

        double[] indices = FlattenColumnMajor(name, given, line, col);
        var picked = new string[indices.Length];
        string[] all = [.. table.ColumnNames];
        for (int i = 0; i < indices.Length; i++)
        {
            int which = (int)indices[i] - 1;
            if (which < 0 || which >= all.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the table has {all.Length} variables, so {indices[i]} is not one of them.");
            }

            picked[i] = all[which];
        }

        return picked;
    }
}
