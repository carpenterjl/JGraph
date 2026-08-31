using System;
using System.Collections.Generic;
using System.Linq;
using JGraph.Data;
using JGraph.Maths;
using JGraph.Numerics;
using JGraph.Statistics.Distributions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The data-cleaning family (M103): <c>isoutlier</c>, <c>rmoutliers</c> and <c>filloutliers</c>,
/// which share one scan; <c>ischange</c>, which is <see cref="ChangePoints"/> read along slices;
/// <c>standardizeMissing</c>; and the small verdict verbs <c>clip</c>, <c>isuniform</c>,
/// <c>rmse</c> and <c>mape</c>.
/// </summary>
/// <remarks>
/// <para>
/// The outlier trio is one question asked three ways: where do the fences stand, and which readings
/// are outside them. <see cref="OutlierFences"/> answers that question once per slice —
/// <c>isoutlier</c> reports it, <c>rmoutliers</c> deletes along the slice direction, and
/// <c>filloutliers</c> writes replacements — so the three cannot disagree about which reading is
/// out. The fences follow MATLAB R2024a exactly, including the two that are statistical tests:
/// Grubbs reports the fences of its final survivors, while the generalized ESD reports the fences
/// of the round that flagged its last outlier, with the previous rounds' outliers already removed.
/// Both were measured, not read, because the documentation says neither.
/// </para>
/// <para>
/// A center for the quartile and percentile fences is the midpoint of the fences' anchors, not the
/// median — <c>isoutlier([1 2 100 3 4], 'quartiles')</c> centers on 14.875 in MATLAB, which no
/// median of that data is.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// MATLAB's scaled-MAD factor, <c>−1/(√2·erfcinv(3/2))</c> as a double: the constant that makes
    /// the median absolute deviation estimate a normal standard deviation.
    /// </summary>
    private const double ScaledMadFactor = 1.4826022185056018;

    private static readonly string[] OutlierCenteredMethods =
        ["median", "mean", "quartiles", "grubbs", "gesd"];

    private static readonly string[] OutlierMovingMethods = ["movmedian", "movmean"];

    private static readonly string[] OutlierFillWords =
        ["center", "clip", "previous", "next", "nearest", "linear", "spline", "pchip", "makima"];

    /// <summary>Registers the cleaning family into <paramref name="env"/>.</summary>
    internal static void RegisterCleaningBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        DefineBoth("isoutlier", IsOutlier);
        DefineBoth("rmoutliers", RemovedOutliers);
        DefineBoth("filloutliers", FilledOutliers);
        DefineBoth("ischange", IsChange);
        Define("standardizeMissing", StandardizedMissing);
        Define("clip", Clipped);
        DefineBoth("isuniform", IsUniform);
        Define("rmse", (args, line, col) => ErrorMetric("rmse", args, line, col));
        Define("mape", (args, line, col) => ErrorMetric("mape", args, line, col));
    }

    // --- the outlier scan ---------------------------------------------------------------------

    /// <summary>Everything the three outlier verbs agree to ask: which fences, along what.</summary>
    private sealed class OutlierPlan
    {
        public string Method = "median";

        public double Threshold = 3;

        public JgsValue? Window;

        public double[]? Percentiles;

        public double[]? SamplePoints;

        public int? MaxNumOutliers;

        public int? Dim;

        public int MinNumOutliers = 1;

        public double[]? Locations;

        public bool Moving => Window is not null;
    }

    private static readonly OptionSpec OutlierOptions = new(
        "isoutlier",
        Flags: [],
        Names: ["ThresholdFactor", "SamplePoints", "MaxNumOutliers", "MinNumOutliers", "OutlierLocations", "DataVariables"]);

    /// <summary>
    /// Reads the shared tail — method word, window or percentile pair, dimension, options — starting
    /// at <paramref name="start"/>. <paramref name="badWordId"/> is the identifier each verb's
    /// documentation gives an unrecognised method.
    /// </summary>
    private static OutlierPlan OutlierPlanOf(
        string name, IReadOnlyList<JgsValue> args, int start, string badWordId, int line, int col)
    {
        var plan = new OutlierPlan();
        int i = start;
        if (args.Count > i && IsTextScalar(args[i]) && !OutlierOptions.Knows(TextOf(args[i])))
        {
            string word = TextOf(args[i]).ToLowerInvariant();
            if (OutlierCenteredMethods.Contains(word))
            {
                plan.Method = word;
                i++;
            }
            else if (OutlierMovingMethods.Contains(word))
            {
                plan.Method = word;
                i++;
                if (args.Count <= i || IsTextScalar(args[i]))
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{name}:MissingWindowLength",
                        $"Specify a window length after '{word}'.");
                }

                plan.Window = args[i];
                i++;
            }
            else if (word == "percentiles")
            {
                plan.Method = word;
                i++;
                double[] pair = args.Count > i ? NumericVector(name, args[i], line, col) : [];
                if (pair.Length != 2 || pair[0] < 0 || pair[1] > 100 || pair[0] > pair[1])
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: 'percentiles' takes a [lower upper] pair between 0 and 100.");
                }

                plan.Percentiles = pair;
                i++;
            }
            else if (name == "rmoutliers")
            {
                throw new JgsRuntimeException(line, col, badWordId,
                    "The second input must be 'median', 'mean', 'movmedian', 'movmean', "
                    + "'quartiles', 'grubbs', 'gesd', 'DataVariables', 'MinNumOutliers', "
                    + "'OutlierLocations', 'SamplePoints', or 'ThresholdFactor'.");
            }
            else
            {
                throw new JgsRuntimeException(line, col, badWordId,
                    $"Expected input number {i + 1} to match one of these values:\n\n'median', "
                    + "'mean', 'quartiles', 'grubbs', 'gesd', 'movmedian', 'movmean', "
                    + "'percentiles', 'SamplePoints', 'DataVariables', 'ThresholdFactor', "
                    + "'MaxNumOutliers', 'OutputFormat'\n\nThe input, '"
                    + TextOf(args[i]) + "', did not match any of the valid values.");
            }
        }

        if (args.Count > i && !IsTextScalar(args[i]))
        {
            plan.Dim = Count(name, args, i, line, col);
            i++;
        }

        ParsedArgs parsed = OutlierOptions.Parse([.. args.Skip(i)], 0, line, col);
        if (parsed.Named("DataVariables") is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'DataVariables' picks variables out of a table, which {name} here does not take.");
        }

        if (parsed.Named("ThresholdFactor") is not null)
        {
            plan.Threshold = parsed.Scalar("ThresholdFactor", 0);
            if (!(plan.Threshold >= 0))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:expectedNonnegative",
                    "Expected ThresholdFactor to be nonnegative.");
            }
        }
        else
        {
            plan.Threshold = plan.Method switch
            {
                "quartiles" => 1.5,
                "grubbs" or "gesd" => 0.05,
                _ => 3,
            };
        }

        if ((plan.Method is "grubbs" or "gesd") && plan.Threshold > 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the '{plan.Method}' threshold is a detection level between 0 and 1.");
        }

        plan.SamplePoints = parsed.Vector("SamplePoints");
        if (parsed.Named("MaxNumOutliers") is not null)
        {
            plan.MaxNumOutliers = (int)parsed.Scalar("MaxNumOutliers", 0);
            if (plan.MaxNumOutliers < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'MaxNumOutliers' is a positive whole count.");
            }
        }

        if (parsed.Named("MinNumOutliers") is not null)
        {
            if (name != "rmoutliers")
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'MinNumOutliers' belongs to rmoutliers.");
            }

            plan.MinNumOutliers = (int)parsed.Scalar("MinNumOutliers", 0);
        }

        if (parsed.Named("OutlierLocations") is { } where)
        {
            if (where.Type != JgsType.Bool && !(where.Type == JgsType.Array
                && where.BoxedElements().All(static e => e.Type == JgsType.Bool)))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidType",
                    "Expected OutlierLocations to be of type logical.");
            }

            plan.Locations = FlattenColumnMajor(name, where, line, col);
        }

        return plan;
    }

    /// <summary>The fences of one slice: lower, upper and center, each length 1 or per-element.</summary>
    private readonly record struct OutlierFencesOf(double[] Lower, double[] Upper, double[] Center);

    private static OutlierFencesOf OutlierFences(string name, double[] slice, OutlierPlan plan, int line, int col)
    {
        if (plan.Moving)
        {
            return MovingFences(name, slice, plan, line, col);
        }

        double[] present = PrepPresent(slice);
        switch (plan.Method)
        {
            case "median":
            {
                double center = present.Length == 0 ? double.NaN : MedianOf(present);
                double spread = ScaledMadFactor * MedianOf(Array.ConvertAll(present, v => Math.Abs(v - center)));
                return new([center - (plan.Threshold * spread)], [center + (plan.Threshold * spread)], [center]);
            }

            case "mean":
            {
                double center = present.Length == 0 ? double.NaN : present.Average();
                double spread = Math.Sqrt(SampleVarianceOf(present));
                return new([center - (plan.Threshold * spread)], [center + (plan.Threshold * spread)], [center]);
            }

            case "quartiles":
            {
                double first = Quartiles.Percentile(present, 25);
                double third = Quartiles.Percentile(present, 75);
                double reach = plan.Threshold * (third - first);
                return new([first - reach], [third + reach], [(first + third) / 2]);
            }

            case "percentiles":
            {
                double lower = Quartiles.Percentile(present, plan.Percentiles![0]);
                double upper = Quartiles.Percentile(present, plan.Percentiles[1]);
                return new([lower], [upper], [(lower + upper) / 2]);
            }

            case "grubbs":
                return GrubbsFences(present, plan.Threshold);

            default:
                return EsdFences(present, plan.Threshold, plan.MaxNumOutliers);
        }
    }

    /// <summary>
    /// Grubbs' test, applied one worst reading at a time until nothing left is significant. The
    /// fences reported are those of the final survivors — measured against R2024a, whose center for
    /// <c>[57 … 300 …]</c> is the mean with both outliers already gone.
    /// </summary>
    private static OutlierFencesOf GrubbsFences(double[] present, double alpha)
    {
        var kept = new List<double>(present);
        double mean = double.NaN;
        double critical = double.NaN;
        double deviation = double.NaN;
        while (true)
        {
            int n = kept.Count;
            mean = n == 0 ? double.NaN : kept.Average();
            deviation = Math.Sqrt(SampleVarianceOf([.. kept]));
            if (n < 3)
            {
                critical = double.NaN;
                break;
            }

            double t = Math.Abs(ContinuousDistributions.TInv(alpha / (2 * n), n - 2));
            critical = (n - 1) / Math.Sqrt(n) * Math.Sqrt(t * t / (n - 2 + (t * t)));
            int worst = 0;
            double reach = -1;
            for (int i = 0; i < n; i++)
            {
                double away = Math.Abs(kept[i] - mean);
                if (away > reach)
                {
                    reach = away;
                    worst = i;
                }
            }

            if (deviation > 0 && reach / deviation > critical)
            {
                kept.RemoveAt(worst);
                continue;
            }

            break;
        }

        double fence = critical * deviation;
        return new([mean - fence], [mean + fence], [mean]);
    }

    /// <summary>
    /// The generalized extreme studentized deviate test. Up to <paramref name="most"/> rounds each
    /// remove the current worst reading; the outlier count is the last significant round, and the
    /// fences reported are that round's — computed with the earlier rounds' outliers removed.
    /// </summary>
    private static OutlierFencesOf EsdFences(double[] present, double alpha, int? most)
    {
        int total = present.Length;
        int rounds = Math.Min(most ?? (int)Math.Ceiling(0.1 * total), Math.Max(0, total - 2));
        var kept = new List<double>(present);
        double lower = double.NaN;
        double upper = double.NaN;
        double center = total == 0 ? double.NaN : present.Average();
        int significant = 0;
        var fences = new List<(double Lower, double Upper, double Center)>();
        for (int round = 1; round <= rounds; round++)
        {
            int n = kept.Count;
            double mean = kept.Average();
            double deviation = Math.Sqrt(SampleVarianceOf([.. kept]));
            int worst = 0;
            double reach = -1;
            for (int i = 0; i < n; i++)
            {
                double away = Math.Abs(kept[i] - mean);
                if (away > reach)
                {
                    reach = away;
                    worst = i;
                }
            }

            int remaining = total - round + 1;
            double t = Math.Abs(ContinuousDistributions.TInv(alpha / (2 * remaining), remaining - 2));
            double critical = (remaining - 1) * t
                / Math.Sqrt((remaining - 2 + (t * t)) * remaining);
            fences.Add((mean - (critical * deviation), mean + (critical * deviation), mean));
            if (deviation > 0 && reach / deviation > critical)
            {
                significant = round;
            }

            kept.RemoveAt(worst);
        }

        int report = Math.Max(1, significant);
        if (fences.Count > 0)
        {
            (lower, upper, center) = fences[Math.Min(report, fences.Count) - 1];
        }

        return new([lower], [upper], [center]);
    }

    /// <summary>
    /// A window measured against its own median: the scaled median of how far each reading sits
    /// from the middle one. It is the one spread here that cannot be carried, because the centre it
    /// is measured from moves with the window.
    /// </summary>
    private static double ScaledMedianDeviationOf(ReadOnlySpan<double> window)
    {
        double middle = MedianOf(window);
        var apart = new double[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            apart[i] = Math.Abs(window[i] - middle);
        }

        return ScaledMadFactor * MedianOf(apart);
    }

    /// <summary>The moving fences: a sliding center and a sliding spread, one value per element.</summary>
    private static OutlierFencesOf MovingFences(string name, double[] slice, OutlierPlan plan, int line, int col)
    {
        bool byMedian = plan.Method == "movmedian";
        WindowSummary center = byMedian ? MedianOf : MeanOf;
        WindowSummary spread = byMedian ? ScaledMedianDeviationOf : StandardDeviationOf;

        // The centre is a sliding summary either way; only the median's own spread — a deviation
        // measured from a centre that moves with the window — has to be walked window by window.
        WindowStat centerKind = byMedian ? WindowStat.Median : WindowStat.Mean;
        WindowStat spreadKind = byMedian ? WindowStat.Other : WindowStat.StandardDeviation;

        double[] centers;
        double[] spreads;
        if (plan.SamplePoints is { } points)
        {
            if (points.Length != slice.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'SamplePoints' has {points.Length} places for {slice.Length} values.");
            }

            (double behind, double ahead) = SpanOf(name, plan.Window!, line, col);
            centers = SlideOverPoints(
                slice, points, behind, ahead, "shrink", true, double.NaN, centerKind, center);
            spreads = SlideOverPoints(
                slice, points, behind, ahead, "shrink", true, double.NaN, spreadKind, spread);
        }
        else
        {
            (int behind, int ahead) = ReachOf(name, plan.Window!, line, col);
            centers = Slide(slice, behind, ahead, "shrink", 0, true, double.NaN, centerKind, center);
            spreads = Slide(slice, behind, ahead, "shrink", 0, true, double.NaN, spreadKind, spread);
        }

        var lower = new double[slice.Length];
        var upper = new double[slice.Length];
        for (int i = 0; i < slice.Length; i++)
        {
            lower[i] = centers[i] - (plan.Threshold * spreads[i]);
            upper[i] = centers[i] + (plan.Threshold * spreads[i]);
        }

        return new(lower, upper, centers);
    }

    private static bool[] OutlierMask(double[] slice, in OutlierFencesOf fences)
    {
        var mask = new bool[slice.Length];
        for (int i = 0; i < slice.Length; i++)
        {
            double low = fences.Lower[fences.Lower.Length == 1 ? 0 : i];
            double high = fences.Upper[fences.Upper.Length == 1 ? 0 : i];
            mask[i] = slice[i] < low || slice[i] > high;
        }

        return mask;
    }

    /// <summary>
    /// <c>[TF, L, U, C] = isoutlier(A, …)</c>: which readings are outside the fences, and where the
    /// fences stand. The fences are one value per slice for the centered methods and one per reading
    /// for the moving ones.
    /// </summary>
    private static JgsValue[] IsOutlier(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "isoutlier needs some data.");
        }

        OutlierPlan plan = OutlierPlanOf(
            "isoutlier", args, 1, "MATLAB:unrecognizedStringChoice", line, col);
        (double[][] slices, int[] dims, int dim) = Cut("isoutlier", args[0], plan.Dim, line, col);

        var masks = new double[slices.Length][];
        var lows = new double[slices.Length][];
        var highs = new double[slices.Length][];
        var centers = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            OutlierFencesOf fences = OutlierFences("isoutlier", slices[s], plan, line, col);
            masks[s] = Array.ConvertAll(OutlierMask(slices[s], fences), static b => b ? 1.0 : 0.0);
            (lows[s], highs[s], centers[s]) = (fences.Lower, fences.Upper, fences.Center);
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(masks, dims, dim);
        JgsValue Fence(double[][] values)
        {
            if (plan.Moving)
            {
                (double[] all, int[] full) = JgsMatrix.JoinAlong(values, dims, dim);
                return JgsMatrix.FromColumnMajorDims(all, full);
            }

            var flat = new int[dims.Length];
            dims.CopyTo(flat, 0);
            flat[dim - 1] = 1;
            (double[] one, int[] fenced) = JgsMatrix.JoinAlong(values, flat, dim);
            return JgsMatrix.FromColumnMajorDims(one, fenced);
        }

        return Outputs(
            wanted,
            PrepMask(Array.ConvertAll(joined, static v => v != 0), shape),
            Fence(lows),
            Fence(highs),
            Fence(centers));
    }

    /// <summary>
    /// <c>[B, TF] = rmoutliers(A, …)</c>: the data with its outliers removed — elements of a vector,
    /// whole slices of a matrix. A slice goes when it holds at least <c>MinNumOutliers</c> of them,
    /// and <c>TF</c> marks the removed positions along the operating dimension.
    /// </summary>
    private static JgsValue[] RemovedOutliers(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "rmoutliers needs some data.");
        }

        OutlierPlan plan = OutlierPlanOf(
            "rmoutliers", args, 1, "MATLAB:rmoutliers:SecondInputString", line, col);
        int[] dims = SizeDims(args[0]);
        bool vector = dims.Count(static d => d > 1) <= 1;

        // A vector loses elements; anything wider loses whole slices across the scan direction. The
        // scan runs along the operating dimension either way, so the mask below is per position
        // along that dimension.
        int dim = plan.Dim ?? JgsMatrix.DefaultDim(dims);
        (double[][] slices, _, _) = Cut("rmoutliers", args[0], dim, line, col);

        int positions = dim <= dims.Length ? dims[dim - 1] : 1;
        var remove = new bool[positions];
        if (plan.Locations is { } located)
        {
            if (located.Length != slices.Sum(static s => s.Length))
            {
                throw new JgsRuntimeException(line, col,
                    "rmoutliers: 'OutlierLocations' must be the size of the data.");
            }

            int at = 0;
            var counts = new int[positions];
            foreach (double[] slice in slices)
            {
                for (int i = 0; i < slice.Length; i++)
                {
                    counts[i] += located[at++] != 0 ? 1 : 0;
                }
            }

            for (int i = 0; i < positions; i++)
            {
                remove[i] = counts[i] >= plan.MinNumOutliers;
            }
        }
        else
        {
            var counts = new int[positions];
            foreach (double[] slice in slices)
            {
                OutlierFencesOf fences = OutlierFences("rmoutliers", slice, plan, line, col);
                bool[] mask = OutlierMask(slice, fences);
                for (int i = 0; i < mask.Length; i++)
                {
                    counts[i] += mask[i] ? 1 : 0;
                }
            }

            for (int i = 0; i < positions; i++)
            {
                remove[i] = counts[i] >= plan.MinNumOutliers;
            }
        }

        var keepIndex = new List<int>();
        for (int i = 0; i < positions; i++)
        {
            if (!remove[i])
            {
                keepIndex.Add(i);
            }
        }

        var trimmed = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            trimmed[s] = [.. keepIndex.Select(i => slices[s][i])];
        }

        var shape = new int[dims.Length];
        dims.CopyTo(shape, 0);
        shape[dim - 1] = keepIndex.Count;
        (double[] joined, int[] outDims) = JgsMatrix.JoinAlong(trimmed, shape, dim);

        int[] flagDims = vector ? dims : (dim == 2 ? [1, positions] : [positions, 1]);
        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(joined, outDims),
            PrepMask(remove, flagDims));
    }

    /// <summary>
    /// <c>[B, TF, L, U, C] = filloutliers(A, fill, …)</c>: the outliers replaced — by the center, by
    /// the nearer fence, by a neighbour, by an interpolant through the readings that stayed in, or
    /// by a constant.
    /// </summary>
    private static JgsValue[] FilledOutliers(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "filloutliers(A, fill) needs the data and a fill.");
        }

        string fill;
        double constant = 0;
        if (IsTextScalar(args[1]))
        {
            fill = TextOf(args[1]).ToLowerInvariant();
            if (!OutlierFillWords.Contains(fill))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:filloutliers:unrecognizedStringChoice",
                    "Expected input number 2, Fill, to match one of these values:\n\n'center', "
                    + "'clip', 'previous', 'next', 'nearest', 'linear', 'spline', 'pchip', "
                    + "'makima'\n\nThe input, '" + TextOf(args[1])
                    + "', did not match any of the valid values.");
            }
        }
        else
        {
            fill = "constant";
            constant = Num("filloutliers", args, 1, line, col);
        }

        OutlierPlan plan = OutlierPlanOf(
            "filloutliers", args, 2, "MATLAB:filloutliers:unrecognizedStringChoice", line, col);
        if (plan.Locations is not null && fill is "center" or "clip")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:filloutliers:UnsupportedFill",
                "'center' and 'clip' options not supported when 'OutlierLocations' parameter is specified.");
        }

        (double[][] slices, int[] dims, int dim) = Cut("filloutliers", args[0], plan.Dim, line, col);

        var filled = new double[slices.Length][];
        var masks = new double[slices.Length][];
        var lows = new double[slices.Length][];
        var highs = new double[slices.Length][];
        var centers = new double[slices.Length][];
        int seen = 0;
        for (int s = 0; s < slices.Length; s++)
        {
            double[] slice = slices[s];
            bool[] mask;
            OutlierFencesOf fences;
            if (plan.Locations is { } located)
            {
                mask = new bool[slice.Length];
                for (int i = 0; i < slice.Length; i++)
                {
                    mask[i] = located[seen + i] != 0;
                }

                fences = new([double.NaN], [double.NaN], [double.NaN]);
            }
            else
            {
                fences = OutlierFences("filloutliers", slice, plan, line, col);
                mask = OutlierMask(slice, fences);
            }

            seen += slice.Length;
            filled[s] = FillSlice(slice, mask, fences, fill, constant, plan.SamplePoints);
            masks[s] = Array.ConvertAll(mask, static b => b ? 1.0 : 0.0);
            (lows[s], highs[s], centers[s]) = (fences.Lower, fences.Upper, fences.Center);
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(filled, dims, dim);
        (double[] flat, _) = JgsMatrix.JoinAlong(masks, dims, dim);
        JgsValue Fence(double[][] values)
        {
            if (plan.Moving)
            {
                (double[] all, int[] full) = JgsMatrix.JoinAlong(values, dims, dim);
                return JgsMatrix.FromColumnMajorDims(all, full);
            }

            var one = new int[dims.Length];
            dims.CopyTo(one, 0);
            one[dim - 1] = 1;
            (double[] fencesFlat, int[] fenced) = JgsMatrix.JoinAlong(values, one, dim);
            return JgsMatrix.FromColumnMajorDims(fencesFlat, fenced);
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(joined, shape),
            PrepMask(Array.ConvertAll(flat, static v => v != 0), shape),
            Fence(lows),
            Fence(highs),
            Fence(centers));
    }

    /// <summary>One slice with its flagged readings replaced by the chosen fill.</summary>
    private static double[] FillSlice(
        double[] slice, bool[] mask, in OutlierFencesOf fences, string fill, double constant,
        double[]? samplePoints)
    {
        var result = (double[])slice.Clone();
        if (fill == "constant")
        {
            for (int i = 0; i < result.Length; i++)
            {
                if (mask[i])
                {
                    result[i] = constant;
                }
            }

            return result;
        }

        if (fill is "center" or "clip")
        {
            for (int i = 0; i < result.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                double low = fences.Lower[fences.Lower.Length == 1 ? 0 : i];
                double high = fences.Upper[fences.Upper.Length == 1 ? 0 : i];
                double middle = fences.Center[fences.Center.Length == 1 ? 0 : i];
                result[i] = fill == "center" ? middle : (result[i] < low ? low : high);
            }

            return result;
        }

        // The neighbour and interpolant fills read only the readings that stayed in, at the places
        // they were sampled. An outlier with no reading on the side its fill needs becomes NaN —
        // measured: MATLAB writes NaN, where the interpolant fills extrapolate instead.
        var goodAt = new List<double>();
        var goodValue = new List<double>();
        for (int i = 0; i < slice.Length; i++)
        {
            if (!mask[i])
            {
                goodAt.Add(samplePoints is { } t ? t[i] : i + 1);
                goodValue.Add(slice[i]);
            }
        }

        if (goodAt.Count == 0)
        {
            return result;
        }

        double[] sites = [.. goodAt];
        double[] values = [.. goodValue];
        double[] cubic = fill is "spline" or "pchip" or "makima" && sites.Length > 1
            ? CubicCoefficients(sites, values, fill)
            : [];
        for (int i = 0; i < slice.Length; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            double at = samplePoints is { } t ? t[i] : i + 1;
            int after = Array.BinarySearch(sites, at);
            after = after >= 0 ? after : ~after;
            switch (fill)
            {
                case "previous":
                    result[i] = after > 0 ? values[after - 1] : double.NaN;
                    break;
                case "next":
                    result[i] = after < sites.Length ? values[after] : double.NaN;
                    break;
                case "nearest":
                {
                    if (after == 0)
                    {
                        result[i] = values[0];
                    }
                    else if (after >= sites.Length)
                    {
                        result[i] = values[^1];
                    }
                    else
                    {
                        // MATLAB's nearest rounds up on a tie, and these distances are exact.
                        result[i] = at - sites[after - 1] < sites[after] - at
                            ? values[after - 1]
                            : values[after];
                    }

                    break;
                }

                case "linear":
                {
                    if (sites.Length == 1)
                    {
                        result[i] = values[0];
                    }
                    else
                    {
                        int piece = Math.Clamp(after - 1, 0, sites.Length - 2);
                        double slope = (values[piece + 1] - values[piece]) / (sites[piece + 1] - sites[piece]);
                        result[i] = values[piece] + (slope * (at - sites[piece]));
                    }

                    break;
                }

                default:
                {
                    if (cubic.Length == 0)
                    {
                        result[i] = values[0];
                    }
                    else
                    {
                        int piece = Math.Clamp(after - 1, 0, sites.Length - 2);
                        result[i] = CubicAt(cubic, piece, at - sites[piece]);
                    }

                    break;
                }
            }
        }

        return result;
    }

    // --- ischange -----------------------------------------------------------------------------

    private static readonly OptionSpec IsChangeOptions = new(
        "ischange",
        Flags: [],
        Names: ["Threshold", "MaxNumChanges", "SamplePoints", "DataVariables"]);

    /// <summary>
    /// <c>[TF, S1, S2] = ischange(A, method, dim, …)</c>: where the signal stops being one thing.
    /// <c>TF</c> marks the first sample of every new segment; <c>S1</c> and <c>S2</c> carry each
    /// segment's statistics over the samples it covers — mean and variance for the first two
    /// methods, slope and intercept for the linear one.
    /// </summary>
    private static JgsValue[] IsChange(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "ischange needs some data.");
        }

        string method = "mean";
        int i = 1;
        if (args.Count > i && IsTextScalar(args[i]) && !IsChangeOptions.Knows(TextOf(args[i])))
        {
            method = TextOf(args[i]).ToLowerInvariant();
            if (method is not ("mean" or "variance" or "linear"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ischange:MethodInvalid",
                    "Change detection method must be 'mean', 'variance', or 'linear'.");
            }

            i++;
        }

        int? dim = null;
        if (args.Count > i && !IsTextScalar(args[i]))
        {
            dim = Count("ischange", args, i, line, col);
            i++;
        }

        ParsedArgs parsed = IsChangeOptions.Parse([.. args.Skip(i)], 0, line, col);
        if (parsed.Named("DataVariables") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "ischange: 'DataVariables' picks variables out of a table, which ischange here does not take.");
        }

        double threshold = 1;
        if (parsed.Named("Threshold") is not null)
        {
            threshold = parsed.Scalar("Threshold", 0);
            if (!(threshold >= 0))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ischange:Threshold",
                    "'Threshold' value must be a non-negative real scalar.");
            }
        }

        int? budget = null;
        if (parsed.Named("MaxNumChanges") is not null)
        {
            if (parsed.Named("Threshold") is not null)
            {
                throw new JgsRuntimeException(line, col,
                    "ischange: give 'Threshold' or 'MaxNumChanges', not both.");
            }

            budget = (int)parsed.Scalar("MaxNumChanges", 0);
            if (budget < 0)
            {
                throw new JgsRuntimeException(line, col,
                    "ischange: 'MaxNumChanges' is a whole count of changes.");
            }
        }

        double[]? points = parsed.Vector("SamplePoints");
        (double[][] slices, int[] dims, int dim2) = Cut("ischange", args[0], dim, line, col);

        ChangePoints.Statistic statistic = method switch
        {
            "mean" => ChangePoints.Statistic.Mean,
            "variance" => ChangePoints.Statistic.Variance,
            _ => ChangePoints.Statistic.Linear,
        };

        var flags = new double[slices.Length][];
        var first = new double[slices.Length][];
        var second = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            double[] slice = slices[s];
            double[] abscissae = points ?? [.. Enumerable.Range(1, slice.Length).Select(static v => (double)v)];
            if (abscissae.Length != slice.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"ischange: 'SamplePoints' has {abscissae.Length} places for {slice.Length} values.");
            }

            int[] changes = ChangePoints.Find(slice, abscissae, statistic, threshold, budget);
            flags[s] = new double[slice.Length];
            foreach (int change in changes)
            {
                flags[s][change] = 1;
            }

            if (method == "linear")
            {
                (first[s], second[s]) = ChangePoints.SegmentLines(slice, abscissae, changes);
            }
            else
            {
                first[s] = ChangePoints.SegmentMeans(slice, changes);
                second[s] = ChangePoints.SegmentVariances(slice, changes);
            }
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(flags, dims, dim2);
        (double[] ones, _) = JgsMatrix.JoinAlong(first, dims, dim2);
        (double[] twos, _) = JgsMatrix.JoinAlong(second, dims, dim2);
        return Outputs(
            wanted,
            PrepMask(Array.ConvertAll(joined, static v => v != 0), shape),
            JgsMatrix.FromColumnMajorDims(ones, shape),
            JgsMatrix.FromColumnMajorDims(twos, shape));
    }

    // --- standardizeMissing -------------------------------------------------------------------

    /// <summary>
    /// <c>B = standardizeMissing(A, indicator)</c>: the named stand-ins replaced by the missing
    /// value of the data's own kind — NaN among numbers, the empty char in a cell of chars, the
    /// missing string among strings, and per-variable across a table.
    /// </summary>
    private static JgsValue StandardizedMissing(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("standardizeMissing", args, 2, 4, line, col);
        JgsValue data = args[0];
        JgsValue indicator = args[1];
        string[]? dataVariables = null;
        if (args.Count > 2)
        {
            if (args.Count != 4 || !IsTextScalar(args[2])
                || !string.Equals(TextOf(args[2]), "DataVariables", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    "standardizeMissing: past the indicator comes 'DataVariables' and the variables it names.");
            }

            if (data.Type != JgsType.Table)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:standardizeMissing:DataVariablesArray",
                    "'DataVariables' and 'ReplaceValues' parameters are only supported for table inputs.");
            }

            dataVariables = TableVariableNames("standardizeMissing", data.AsTable, args[3], line, col);
        }

        if (data.Type == JgsType.Table)
        {
            Table table = data.AsTable;
            double[] numbers = indicator.Type is JgsType.Number or JgsType.Array or JgsType.Bool
                ? FlattenColumnMajor("standardizeMissing", indicator, line, col)
                : [];
            string[] words = TextElementsOf(indicator) ?? [];
            string[] chosen = dataVariables ?? [.. table.ColumnNames];
            var columns = new List<TableColumn>();
            foreach (TableColumn column in table.Columns)
            {
                bool wanted = chosen.Contains(column.Name, StringComparer.Ordinal);
                if (!wanted)
                {
                    columns.Add(column);
                }
                else if (column.Type == ColumnType.Text)
                {
                    var texts = new string[table.RowCount];
                    for (int r = 0; r < table.RowCount; r++)
                    {
                        string text = column.GetText(r);
                        texts[r] = words.Contains(text, StringComparer.Ordinal) ? string.Empty : text;
                    }

                    columns.Add(new TextColumn(column.Name, texts));
                }
                else
                {
                    var values = new double[table.RowCount];
                    for (int r = 0; r < table.RowCount; r++)
                    {
                        double value = column.GetNumber(r);
                        values[r] = numbers.Contains(value) ? double.NaN : value;
                    }

                    columns.Add(new NumberColumn(column.Name, values));
                }
            }

            return JgsValue.Table(new Table(columns));
        }

        if (TextElementsOf(data) is { } elements)
        {
            string[] standins = TextElementsOf(indicator)
                ?? throw new JgsRuntimeException(line, col,
                    "standardizeMissing: a text array takes text indicators.");
            bool strings = data.IsStringArray || data.Type == JgsType.String;
            string missing = strings ? MissingSentinel : string.Empty;
            JgsValue[] replaced = [.. elements.Select(
                text => JgsValue.Str(standins.Contains(text, StringComparer.Ordinal) ? missing : text))];
            if (data.Type == JgsType.String)
            {
                return replaced[0];
            }

            JgsValue answer = data.Type == JgsType.Cell ? JgsValue.Cell(replaced) : JgsValue.Array(replaced);
            answer.TakeShapeOf(data);
            if (data.IsStringArray)
            {
                answer.MarkStringArray();
            }

            return answer;
        }

        if (TextElementsOf(indicator) is not null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ismissing:IndicatorsDouble",
                "Second argument must be numeric or logical.");
        }

        double[] marks = NumericVector("standardizeMissing", indicator, line, col);
        double[] flat = FlattenColumnMajor("standardizeMissing", data, line, col);
        var cleaned = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            cleaned[i] = marks.Contains(flat[i]) ? double.NaN : flat[i];
        }

        return JgsMatrix.FromColumnMajorDims(cleaned, SizeDims(data));
    }

    // --- clip, isuniform, rmse, mape ----------------------------------------------------------

    /// <summary>
    /// <c>clip(x, lower, upper)</c>: every reading pulled inside the bounds. An empty bound leaves
    /// that side open, and NaN passes through untouched.
    /// </summary>
    private static JgsValue Clipped(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("clip", args, 3, line, col);
        double[] flat = FlattenColumnMajor("clip", args[0], line, col);
        double[] lower = FlattenColumnMajor("clip", args[1], line, col);
        double[] upper = FlattenColumnMajor("clip", args[2], line, col);
        if ((lower.Length > 1 && lower.Length != flat.Length)
            || (upper.Length > 1 && upper.Length != flat.Length))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch",
                "Arrays have incompatible sizes for this operation.");
        }

        var result = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            double low = lower.Length == 0 ? double.NegativeInfinity : lower[lower.Length == 1 ? 0 : i];
            double high = upper.Length == 0 ? double.PositiveInfinity : upper[upper.Length == 1 ? 0 : i];
            if (low > high)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:clip:InvalidLowerBound",
                    "Lower bound must be less than or equal to upper bound.");
            }

            double value = flat[i];
            result[i] = double.IsNaN(value) ? value : Math.Min(Math.Max(value, low), high);
        }

        return JgsMatrix.FromColumnMajorDims(result, SizeDims(args[0]));
    }

    /// <summary>
    /// <c>[TF, step] = isuniform(v)</c>: whether the readings are evenly spaced, within four ulps of
    /// the largest one. A scalar and an empty answer no; the step of a non-uniform vector is NaN.
    /// </summary>
    private static JgsValue[] IsUniform(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("isuniform", args, 1, line, col);
        if (args[0].Type == JgsType.Cell || TextElementsOf(args[0]) is not null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:isuniform:MustBeReal",
                "Input data must be numeric and real.");
        }

        int[] dims = SizeDims(args[0]);
        double[] flat = FlattenColumnMajor("isuniform", args[0], line, col);
        bool vector = dims.Count(static d => d > 1) <= 1 && dims.All(static d => d > 0);
        bool uniform = false;
        double step = double.NaN;
        if (vector && flat.Length >= 2)
        {
            double candidate = (flat[^1] - flat[0]) / (flat.Length - 1);
            double scale = flat.Max(Math.Abs);
            double tolerance = 4 * (Math.BitIncrement(scale) - scale);
            uniform = true;
            for (int i = 1; i < flat.Length && uniform; i++)
            {
                uniform = Math.Abs(flat[i] - flat[i - 1] - candidate) <= tolerance
                    && double.IsFinite(flat[i]);
            }

            uniform = uniform && double.IsFinite(flat[0]);
            step = uniform ? candidate : double.NaN;
        }

        return Outputs(wanted, JgsValue.Bool(uniform), JgsValue.Number(step));
    }

    private static readonly OptionSpec ErrorMetricOptions = new(
        "rmse",
        Flags: ["omitnan", "includenan", "omitmissing", "includemissing"],
        Names: ["Weights"]);

    /// <summary>
    /// <c>rmse(F, A, dim)</c> and <c>mape(F, A, dim)</c>: how far the forecast sits from the actual,
    /// as a root-mean-square or as a mean absolute percentage, optionally weighted, along one
    /// dimension or over everything.
    /// </summary>
    private static JgsValue ErrorMetric(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name}(F, A) needs a forecast and an actual.");
        }

        int? dim = null;
        bool everything = false;
        int i = 2;
        if (args.Count > i && !IsTextScalar(args[i]))
        {
            dim = Count(name, args, i, line, col);
            i++;
        }
        else if (args.Count > i && IsTextScalar(args[i])
            && string.Equals(TextOf(args[i]), "all", StringComparison.OrdinalIgnoreCase))
        {
            everything = true;
            i++;
        }

        ParsedArgs parsed = ErrorMetricOptions.Parse([.. args.Skip(i)], 0, line, col);
        bool omit = parsed.Has("omitnan") || parsed.Has("omitmissing");

        double[] forecast = FlattenColumnMajor(name, args[0], line, col);
        double[] actual = FlattenColumnMajor(name, args[1], line, col);
        int[] dims = SizeDims(forecast.Length >= actual.Length ? args[0] : args[1]);
        if (forecast.Length == 1 && actual.Length > 1)
        {
            forecast = [.. Enumerable.Repeat(forecast[0], actual.Length)];
        }

        if (actual.Length == 1 && forecast.Length > 1)
        {
            actual = [.. Enumerable.Repeat(actual[0], forecast.Length)];
        }

        if (forecast.Length != actual.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch",
                "Arrays have incompatible sizes for this operation.");
        }

        double[]? weights = null;
        if (parsed.Named("Weights") is { } given)
        {
            weights = FlattenColumnMajor(name, given, line, col);
            if (weights.Any(static w => w < 0 || double.IsNaN(w)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:errormetrics:InvalidWeight",
                    "Weighting scheme must be a double or single array of real, nonnegative values.");
            }
        }

        var errors = new double[forecast.Length];
        for (int e = 0; e < errors.Length; e++)
        {
            errors[e] = name == "rmse"
                ? (forecast[e] - actual[e]) * (forecast[e] - actual[e])
                : Math.Abs((forecast[e] - actual[e]) / actual[e]) * 100;
        }

        double Reduce(IReadOnlyList<double> values, IReadOnlyList<double>? weight)
        {
            double top = 0;
            double bottom = 0;
            for (int v = 0; v < values.Count; v++)
            {
                double value = values[v];
                if (omit && double.IsNaN(value))
                {
                    continue;
                }

                double w = weight is null ? 1 : weight[v];
                top += w * value;
                bottom += w;
            }

            double mean = top / bottom;
            return name == "rmse" ? Math.Sqrt(mean) : mean;
        }

        if (everything || dims.Count(static d => d > 1) <= 1)
        {
            if (everything || dim is null || dim == JgsMatrix.DefaultDim(dims))
            {
                if (weights is not null && weights.Length != errors.Length && weights.Length != 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:errormetrics:InvalidWeight",
                        "Weighting scheme must match the size of the data.");
                }

                double[]? spread = weights?.Length == 1
                    ? [.. Enumerable.Repeat(weights[0], errors.Length)]
                    : weights;
                return JgsValue.Number(Reduce(errors, spread));
            }
        }

        (double[][] slices, _) = JgsMatrix.SlicesAlong(errors, dims, dim ?? JgsMatrix.DefaultDim(dims));
        int along = dim ?? JgsMatrix.DefaultDim(dims);
        int width = along <= dims.Length ? dims[along - 1] : 1;
        if (weights is not null && weights.Length != width)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:errormetrics:InvalidWeight",
                "Weighting scheme must match the size of the operating dimension.");
        }

        var reduced = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            reduced[s] = [Reduce(slices[s], weights)];
        }

        var shape = new int[dims.Length];
        dims.CopyTo(shape, 0);
        shape[along - 1] = 1;
        (double[] joined, int[] outDims) = JgsMatrix.JoinAlong(reduced, shape, along);
        return JgsMatrix.FromColumnMajorDims(joined, outDims);
    }
}
