using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using JGraph.Imaging;
using JGraph.Maths;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The trend-and-correlation leftovers of <c>datafun</c> (M103): <c>detrend</c>, <c>del2</c>,
/// <c>filter2</c>, <c>histcounts2</c>, <c>xcorr</c>, <c>xcov</c> and <c>subspace</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>detrend</c>'s segmented model was measured, not read: with breakpoints it fits one least
/// squares over a polynomial of the asked degree plus, per breakpoint, hinge powers
/// <c>(t−b)₊¹ … (t−b)₊ⁿ</c> — continuity of value only, nothing about derivatives. Degree zero with
/// breakpoints leaves those hinges empty, and R2024a then subtracts the mean of the <em>first
/// segment</em> from everything, which is what an anchored sequential fit with no free parameters
/// does; that quirk is reproduced deliberately.
/// </para>
/// <para>
/// <c>del2</c>'s boundary is a linear extrapolation, in the coordinates, of the two nearest interior
/// second differences — verified against R2024a on a non-uniform grid, where the uniform-grid
/// reading "cubic extrapolation" stops being a distinguishable description.
/// </para>
/// <para>
/// <c>xcorr</c> goes through the same transform kernels as <c>fft</c>, at MATLAB's own length —
/// two to the power that covers <c>2N−1</c> — because MATLAB's answer visibly carries FFT roundoff
/// (<c>xcorr([1 2 3])</c> ends in <c>3.0000000000000004</c>) and matching it means rounding the
/// same way, not computing the "better" direct sum.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the trend-and-correlation builtins into <paramref name="env"/>.</summary>
    internal static void RegisterDataTrendBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        Define("detrend", Detrended);
        Define("del2", DiscreteLaplacian);
        Define("filter2", Filter2D);
        DefineBoth("histcounts2", BinCounts2D);
        DefineBoth("xcorr", (args, wanted, line, col) => CrossCorrelation("xcorr", args, wanted, false, line, col));
        DefineBoth("xcov", (args, wanted, line, col) => CrossCorrelation("xcov", args, wanted, true, line, col));
        Define("subspace", SubspaceAngle);
    }

    // --- detrend ------------------------------------------------------------------------------

    /// <summary>
    /// <c>y = detrend(x, n, bp, …)</c>: the data with its best-fitting polynomial trend removed —
    /// per column, of degree <c>n</c> (one when unasked), continuously segmented at the breakpoints
    /// unless <c>'Continuous', false</c> asks for independent fits.
    /// </summary>
    private static JgsValue Detrended(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "detrend needs some data.");
        }

        int degree = 1;
        double[] breakpoints = [];
        bool omitNan = false;
        bool continuous = true;
        double[]? samplePoints = null;

        int i = 1;
        if (args.Count > i && IsTextScalar(args[i])
            && TextOf(args[i]).ToLowerInvariant() is "constant" or "linear")
        {
            degree = TextOf(args[i]).ToLowerInvariant() == "constant" ? 0 : 1;
            i++;
        }
        else if (args.Count > i && !IsTextScalar(args[i]))
        {
            degree = Count("detrend", args, i, line, col);
            i++;
            if (args.Count > i && !IsTextScalar(args[i]))
            {
                breakpoints = NumericVector("detrend", args[i], line, col);
                i++;
            }
        }

        while (i < args.Count)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col,
                    "detrend: past the breakpoints come flags and name-value pairs.");
            }

            string word = TextOf(args[i]);
            string lowered = word.ToLowerInvariant();
            if (lowered is "omitnan" or "omitmissing")
            {
                omitNan = true;
                i++;
                continue;
            }

            if (lowered is "includenan" or "includemissing")
            {
                omitNan = false;
                i++;
                continue;
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:detrend:KeyWithoutValue",
                    "Incorrect number of input arguments. Each parameter name must be followed by "
                    + "a corresponding value.");
            }

            switch (lowered)
            {
                case "continuous":
                    continuous = Num("detrend", args, i + 1, line, col) != 0;
                    break;
                case "samplepoints":
                    samplePoints = NumericVector("detrend", args[i + 1], line, col);
                    break;
                case "datavariables" or "replacevalues":
                    throw new JgsRuntimeException(line, col,
                        $"detrend: '{word}' belongs to the table form, which detrend here does not take.");
                default:
                    throw new JgsRuntimeException(line, col, "MATLAB:detrend:ParseFlags",
                        "Parameter name must be 'Continuous', 'SamplePoints', 'DataVariables' or "
                        + "'ReplaceValues'.");
            }

            i += 2;
        }

        (double[][] slices, int[] dims, int dim) = Cut("detrend", args[0], null, line, col);
        var results = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            double[] slice = slices[s];
            double[] t = samplePoints
                ?? [.. Enumerable.Range(1, slice.Length).Select(static v => (double)v)];
            if (t.Length != slice.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"detrend: 'SamplePoints' has {t.Length} places for {slice.Length} values.");
            }

            results[s] = DetrendSlice(slice, t, degree, breakpoints, continuous, omitNan);
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(results, dims, dim);
        return JgsMatrix.FromColumnMajorDims(joined, shape);
    }

    /// <summary>One column detrended: the least-squares trend evaluated everywhere and subtracted.</summary>
    private static double[] DetrendSlice(
        double[] y, double[] t, int degree, double[] breakpoints, bool continuous, bool omitNan)
    {
        double[] cuts = [.. breakpoints
            .Where(b => !double.IsNaN(b))
            .Distinct()
            .Where(b => t.Length > 0 && b > t[0] && b <= t[^1])
            .OrderBy(static b => b)];

        var fitRows = new List<int>();
        for (int i = 0; i < y.Length; i++)
        {
            if (!omitNan || !double.IsNaN(y[i]))
            {
                fitRows.Add(i);
            }
        }

        var result = (double[])y.Clone();
        if (fitRows.Count == 0)
        {
            return result;
        }

        if (!continuous && cuts.Length > 0)
        {
            // Independent fits: each segment gets its own polynomial and never hears of the others.
            var starts = new List<double> { double.NegativeInfinity };
            starts.AddRange(cuts);
            for (int s = 0; s < starts.Count; s++)
            {
                double from = starts[s];
                double to = s + 1 < starts.Count ? starts[s + 1] : double.PositiveInfinity;
                int[] segment = [.. fitRows.Where(r => t[r] >= from && t[r] < to)];
                if (segment.Length == 0)
                {
                    continue;
                }

                double[] trend = PolynomialTrend(
                    [.. segment.Select(r => t[r])], [.. segment.Select(r => y[r])],
                    [.. segment.Select(r => t[r])], degree);
                for (int k = 0; k < segment.Length; k++)
                {
                    result[segment[k]] -= trend[k];
                }
            }

            // The rows a fit never saw (the NaNs) keep their values, which are NaN already.
            return result;
        }

        if (degree == 0 && cuts.Length > 0)
        {
            // R2024a's continuous piecewise constant: the hinge terms of degree zero would break
            // continuity, so none exist, and the fit is then decided entirely by the readings up to
            // and including the first breakpoint — their mean, subtracted from everything. Measured
            // twice: bp 2 over five samples averages the first two, bp 3 the first three.
            double[] first = [.. fitRows.Where(r => t[r] <= cuts[0]).Select(r => y[r])];
            double mean = first.Length == 0 ? 0 : first.Average();
            for (int r = 0; r < result.Length; r++)
            {
                result[r] -= mean;
            }

            return result;
        }

        // The joint model: the polynomial itself, plus per breakpoint the hinge powers 1…n. That is
        // continuity of value and nothing else, which is what MATLAB fits (measured; see the file
        // remarks).
        int hinges = degree < 1 ? 0 : degree;
        int columns = degree + 1 + (cuts.Length * hinges);
        var design = new double[fitRows.Count, columns];
        for (int r = 0; r < fitRows.Count; r++)
        {
            double at = t[fitRows[r]];
            double power = 1;
            for (int p = 0; p <= degree; p++)
            {
                design[r, p] = power;
                power *= at;
            }

            for (int b = 0; b < cuts.Length; b++)
            {
                double past = Math.Max(0, at - cuts[b]);
                double hinge = past;
                for (int p = 1; p <= hinges; p++)
                {
                    design[r, degree + (b * hinges) + p] = hinge;
                    hinge *= past;
                }
            }
        }

        var rhs = new double[fitRows.Count, 1];
        for (int r = 0; r < fitRows.Count; r++)
        {
            rhs[r, 0] = y[fitRows[r]];
        }

        double[] coefficients = SolveLeastSquares(design, rhs);
        for (int r = 0; r < result.Length; r++)
        {
            double at = t[r];
            double trend = 0;
            double power = 1;
            for (int p = 0; p <= degree; p++)
            {
                trend += coefficients[p] * power;
                power *= at;
            }

            for (int b = 0; b < cuts.Length; b++)
            {
                double past = Math.Max(0, at - cuts[b]);
                double hinge = past;
                for (int p = 1; p <= hinges; p++)
                {
                    trend += coefficients[degree + (b * hinges) + p] * hinge;
                    hinge *= past;
                }
            }

            result[r] -= trend;
        }

        return result;
    }

    /// <summary>A plain polynomial trend of one segment, evaluated at the asked places.</summary>
    private static double[] PolynomialTrend(double[] t, double[] y, double[] at, int degree)
    {
        var design = new double[t.Length, degree + 1];
        for (int r = 0; r < t.Length; r++)
        {
            double power = 1;
            for (int p = 0; p <= degree; p++)
            {
                design[r, p] = power;
                power *= t[r];
            }
        }

        var rhs = new double[t.Length, 1];
        for (int r = 0; r < t.Length; r++)
        {
            rhs[r, 0] = y[r];
        }

        double[] coefficients = SolveLeastSquares(design, rhs);
        var trend = new double[at.Length];
        for (int r = 0; r < at.Length; r++)
        {
            double power = 1;
            for (int p = 0; p <= degree; p++)
            {
                trend[r] += coefficients[p] * power;
                power *= at[r];
            }
        }

        return trend;
    }

    /// <summary>
    /// A least-squares solve that survives rank deficiency: LAPACK's solver first, and the
    /// pseudo-inverse when the design is degenerate — a two-point segment asked for a cubic, say.
    /// </summary>
    private static double[] SolveLeastSquares(double[,] design, double[,] rhs)
    {
        try
        {
            double[,] solved = Linear.Solve(design, rhs);
            var coefficients = new double[solved.GetLength(0)];
            for (int r = 0; r < coefficients.Length; r++)
            {
                coefficients[r] = solved[r, 0];
            }

            return coefficients;
        }
        catch (InvalidOperationException)
        {
            int m = design.GetLength(0);
            int n = design.GetLength(1);
            var flat = new double[m * n];
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    flat[(c * m) + r] = design[r, c];
                }
            }

            Svd svd = Svd.Factor(flat, m, n);
            double tolerance = Math.Max(m, n) * (svd.Values.Length == 0 ? 0 : svd.Values[0])
                * (Math.BitIncrement(1.0) - 1.0);
            var coefficients = new double[n];
            for (int k = 0; k < svd.Values.Length; k++)
            {
                if (svd.Values[k] <= tolerance)
                {
                    continue;
                }

                double projected = 0;
                for (int r = 0; r < m; r++)
                {
                    projected += svd.UColumnMajor[(k * m) + r] * rhs[r, 0];
                }

                projected /= svd.Values[k];
                for (int c = 0; c < n; c++)
                {
                    coefficients[c] += svd.VColumnMajor[(k * n) + c] * projected;
                }
            }

            return coefficients;
        }
    }

    // --- del2 ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>L = del2(U, h…)</c>: the discrete Laplacian over <c>2·ndims</c> — each direction's second
    /// difference, on whatever grid its spacing argument describes, with boundaries filled by linear
    /// extrapolation of the two nearest interior differences.
    /// </summary>
    private static JgsValue DiscreteLaplacian(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "del2 needs an array.");
        }

        int[] dims = SizeDims(args[0]);
        int rank = Math.Max(2, dims.Length);
        bool vectorData = dims.Count(static d => d > 1) <= 1;

        // Spacing arguments follow gradient's convention: the first names the column direction, the
        // second the rows, the rest the higher dimensions in order.
        var spacing = new double[rank][];
        if (args.Count == 1)
        {
            for (int d = 0; d < rank; d++)
            {
                spacing[d] = [1];
            }
        }
        else if (args.Count == 2)
        {
            double[] one = NumericVector("del2", args[1], line, col);
            if (one.Length > 1 && !vectorData)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:del2:InvalidInput",
                    "The number of spacing inputs must match the number of array dimensions.");
            }

            for (int d = 0; d < rank; d++)
            {
                spacing[d] = one.Length == 1 ? one : [1];
            }

            if (one.Length > 1)
            {
                int direction = JgsMatrix.DefaultDim(dims) - 1;
                spacing[direction] = one;
            }
        }
        else
        {
            if (args.Count - 1 != rank)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:del2:InvalidInput",
                    "The number of spacing inputs must match the number of array dimensions.");
            }

            for (int a = 1; a < args.Count; a++)
            {
                // hx first: the first spacing belongs to dimension 2, the second to dimension 1.
                int direction = a == 1 ? 1 : a == 2 ? 0 : a - 1;
                spacing[direction] = NumericVector("del2", args[a], line, col);
            }
        }

        double[] flat = FlattenColumnMajor("del2", args[0], line, col);
        var total = new double[flat.Length];
        for (int d = 1; d <= rank; d++)
        {
            int length = d <= dims.Length ? dims[d - 1] : 1;
            if (length < 2)
            {
                continue;
            }

            double[] given = spacing[d - 1];
            double[] places;
            if (given.Length == 1)
            {
                places = new double[length];
                for (int p = 0; p < length; p++)
                {
                    places[p] = p * given[0];
                }
            }
            else
            {
                if (given.Length != length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:del2:InvalidInput",
                        "A spacing vector must have one place per element along its dimension.");
                }

                places = given;
            }

            (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, d);
            var seconds = new double[slices.Length][];
            for (int s = 0; s < slices.Length; s++)
            {
                seconds[s] = SecondDifferences(slices[s], places);
            }

            (double[] joined, _) = JgsMatrix.JoinAlong(seconds, dims, d);
            for (int p = 0; p < total.Length; p++)
            {
                total[p] += joined[p];
            }
        }

        for (int p = 0; p < total.Length; p++)
        {
            total[p] /= 2 * rank;
        }

        return JgsMatrix.FromColumnMajorDims(total, dims);
    }

    /// <summary>
    /// One line's second differences on its own grid: the exact three-point formula inside, the two
    /// nearest interior values extrapolated linearly in the coordinates at the ends.
    /// </summary>
    private static double[] SecondDifferences(double[] u, double[] x)
    {
        int n = u.Length;
        var d = new double[n];
        if (n < 3)
        {
            return d;
        }

        // A uniform grid takes the classic three-point difference, whose bits MATLAB reproduces;
        // the general quotient below rounds differently even when the spacing happens to be even.
        bool uniform = true;
        double step = x[1] - x[0];
        for (int i = 2; i < n && uniform; i++)
        {
            uniform = x[i] - x[i - 1] == step;
        }

        for (int i = 1; i < n - 1; i++)
        {
            if (uniform)
            {
                d[i] = (u[i - 1] - (2 * u[i]) + u[i + 1]) / (step * step);
                continue;
            }

            double left = x[i] - x[i - 1];
            double right = x[i + 1] - x[i];
            double span = x[i + 1] - x[i - 1];
            d[i] = 2 * ((u[i - 1] / (left * span)) - (u[i] / (left * right)) + (u[i + 1] / (span * right)));
        }

        if (n == 3)
        {
            d[0] = d[1];
            d[2] = d[1];
            return d;
        }

        d[0] = d[1] + ((x[0] - x[1]) * (d[2] - d[1]) / (x[2] - x[1]));
        d[n - 1] = d[n - 2] + ((x[n - 1] - x[n - 2]) * (d[n - 2] - d[n - 3]) / (x[n - 2] - x[n - 3]));
        return d;
    }

    // --- filter2 ------------------------------------------------------------------------------

    /// <summary>
    /// <c>Y = filter2(H, X, shape)</c>: two-dimensional correlation, which is convolution with the
    /// kernel turned half a turn — and so rides <c>conv2</c>'s machinery, shapes and all.
    /// </summary>
    private static JgsValue Filter2D(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("filter2", args, 2, 3, line, col);
        double[,] kernel = RectangleOf("filter2", args[0], line, col);
        double[,] data = RectangleOf("filter2", args[1], line, col);
        Conv2Shape shape = Conv2Shape.Same;
        if (args.Count == 3)
        {
            shape = Str("filter2", args, 2, line, col).ToLowerInvariant() switch
            {
                "full" => Conv2Shape.Full,
                "same" => Conv2Shape.Same,
                "valid" => Conv2Shape.Valid,
                _ => throw new JgsRuntimeException(line, col, "MATLAB:conv2:unknownShapeParameter",
                    "SHAPE must be 'full', 'same', or 'valid'."),
            };
        }

        int kh = kernel.GetLength(0);
        int kw = kernel.GetLength(1);
        var turned = new double[kh, kw];
        for (int r = 0; r < kh; r++)
        {
            for (int c = 0; c < kw; c++)
            {
                turned[r, c] = kernel[kh - 1 - r, kw - 1 - c];
            }
        }

        return MatrixToRows(Filters.Convolve2(data, turned, shape));
    }

    // --- histcounts2 --------------------------------------------------------------------------

    private static readonly OptionSpec HistCounts2Options = new(
        "histcounts2",
        Flags: [],
        Names: ["XBinLimits", "YBinLimits", "BinWidth", "BinMethod", "Normalization"]);

    /// <summary>
    /// <c>[N, Xedges, Yedges, binX, binY] = histcounts2(X, Y, …)</c>: pairs counted onto a grid.
    /// The automatic rules are the one-dimensional chooser with the fourth root of the sample count
    /// in Scott's denominator; a pair with either coordinate outside its edges is outside the grid,
    /// and both of its bin numbers say zero.
    /// </summary>
    private static JgsValue[] BinCounts2D(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "histcounts2 needs X and Y.");
        }

        ParsedArgs parsed = HistCounts2Options.Parse(args, 4, line, col);
        double[] xs = FlattenColumnMajor("histcounts2", parsed.Positional[0], line, col);
        double[] ys = parsed.Positional.Count > 1
            ? FlattenColumnMajor("histcounts2", parsed.Positional[1], line, col)
            : throw new JgsRuntimeException(line, col, "histcounts2 needs X and Y.");
        if (xs.Length != ys.Length)
        {
            int[] xdims = SizeDims(parsed.Positional[0]);
            int[] ydims = SizeDims(parsed.Positional[1]);
            throw new JgsRuntimeException(line, col, "MATLAB:histcounts2:incorrectSize",
                $"Expected input number 2, y, to be of size {string.Join("x", xdims)}, but it is "
                + $"of size {string.Join("x", ydims)}.");
        }

        double[]? xLimits = parsed.Vector("XBinLimits");
        double[]? yLimits = parsed.Vector("YBinLimits");
        double[]? widths = parsed.Vector("BinWidth");
        if (widths is not null && (widths.Length != 2 || widths.Any(static w => !(w > 0))))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:histcounts2:expectedPositive",
                "Expected input number 4, BinWidth, to be positive.");
        }

        string rule = parsed.Word("BinMethod", "auto", "auto", "scott", "fd", "integers");
        string normalization = parsed.Word(
            "Normalization", "count", "count", "countdensity", "cumcount", "probability", "pdf", "cdf");

        int? xBins = null;
        int? yBins = null;
        double[]? xEdges = null;
        double[]? yEdges = null;
        if (parsed.Positional.Count == 3)
        {
            double[] asked = NumericVector("histcounts2", parsed.Positional[2], line, col);
            if (asked.Length == 1)
            {
                (xBins, yBins) = ((int)asked[0], (int)asked[0]);
            }
            else if (asked.Length == 2)
            {
                (xBins, yBins) = ((int)asked[0], (int)asked[1]);
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "histcounts2: the bin count is one number or an [nx ny] pair.");
            }
        }
        else if (parsed.Positional.Count == 4)
        {
            xEdges = NumericVector("histcounts2", parsed.Positional[2], line, col);
            yEdges = NumericVector("histcounts2", parsed.Positional[3], line, col);
        }

        xEdges ??= Histogram2Edges(xs, xBins, widths?[0], xLimits, rule);
        yEdges ??= Histogram2Edges(ys, yBins, widths?[1], yLimits, rule);

        int nx = Math.Max(0, xEdges.Length - 1);
        int ny = Math.Max(0, yEdges.Length - 1);
        var counts = new double[nx * ny];
        var binX = new double[xs.Length];
        var binY = new double[ys.Length];
        Binning.BinFinder across = Binning.BinFinder.For(xEdges);
        Binning.BinFinder down = Binning.BinFinder.For(yEdges);
        for (int i = 0; i < xs.Length; i++)
        {
            int bx = across.Of(xs[i]);
            int by = down.Of(ys[i]);
            if (bx >= 0 && by >= 0)
            {
                counts[(by * nx) + bx]++;
                binX[i] = bx + 1;
                binY[i] = by + 1;
            }
        }

        double[] scaled = Normalized2D(counts, nx, ny, xEdges, yEdges, normalization, xs.Length);
        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(scaled, [nx, ny]),
            Numbers(xEdges),
            Numbers(yEdges),
            JgsMatrix.FromColumnMajorDims(binX, SizeDims(parsed.Positional[0])),
            JgsMatrix.FromColumnMajorDims(binY, SizeDims(parsed.Positional[1])));
    }

    /// <summary>
    /// One dimension's edges for <c>histcounts2</c>. The automatic rules defer to the shared
    /// chooser with the fourth root; an asked bin count or width follows MATLAB's own arithmetic —
    /// a left edge snapped down to a round number, a width nudged up to a two-digit one, and edges
    /// accumulated as multiples rather than snapped to the data's end.
    /// </summary>
    private static double[] Histogram2Edges(
        double[] data, int? requested, double? width, double[]? limits, string rule)
    {
        double[] finite = [.. data.Where(double.IsFinite)
            .Where(v => limits is null || (v >= limits[0] && v <= limits[1]))];
        if (requested is null && width is null)
        {
            if (limits is null)
            {
                return Binning.EdgesFor(finite, null, null, null, rule, 4);
            }

            // Named limits are exact, so the automatic rule only chooses how many bins fit between
            // them — with a forgiving ceiling, where the shared chooser rounds (measured: R2024a
            // cuts [0.2, 0.8] into two 0.3-wide bins, not one 0.6-wide one).
            double span = limits[1] - limits[0];
            double rough = RawAutomaticWidth(finite, rule, span);
            double powerOfTen = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double relative = rough / powerOfTen;
            double nice = powerOfTen
                * (relative < 1.5 ? 1 : relative < 2.5 ? 2 : relative < 4 ? 3 : relative < 7.5 ? 5 : 10);
            return Binning.Spanning(limits[0], limits[1], Math.Max(1, CeilWithGrace(span / nice)));
        }

        double low;
        double high;
        if (limits is not null)
        {
            (low, high) = (limits[0], limits[1]);
        }
        else if (finite.Length == 0)
        {
            (low, high) = (0, 1);
        }
        else
        {
            (low, high) = (finite.Min(), finite.Max());
        }

        if (high == low)
        {
            (low, high) = (low - 0.5, low + 0.5);
        }

        if (width is { } step)
        {
            double left = limits is not null ? low : step * Math.Floor(low / step);
            int bins = Math.Max(1, CeilWithGrace((high - left) / step));
            var fromWidth = new double[bins + 1];
            for (int i = 0; i <= bins; i++)
            {
                fromWidth[i] = left + (i * step);
            }

            // The last edge is raised to the data's reach when accumulation left it a hair short,
            // so the largest reading stays inside the histogram it defined.
            fromWidth[^1] = Math.Max(fromWidth[^1], high);
            return fromWidth;
        }

        int count = Math.Max(1, requested!.Value);
        if (limits is not null)
        {
            return Binning.Spanning(low, high, count);
        }

        // An asked bin count picks the nice width the automatic table would (1, 2, 3, 5 or 10
        // times a power of ten), snaps the left edge down onto that width's grid, and only when the
        // asked count of nice bins fails to reach the data's end does it stretch the width — up to
        // the next tenth of the power of ten. Measured against R2024a: three bins over [0.1, 0.9]
        // are 3×0.1 wide starting at 0, and four are 0.23 wide, not 0.225.
        double raw = (high - low) / count;
        double rawPower = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double rawRelative = raw / rawPower;
        double size = rawPower
            * (rawRelative < 1.5 ? 1 : rawRelative < 2.5 ? 2 : rawRelative < 4 ? 3
                : rawRelative < 7.5 ? 5 : 10);
        double left2 = size * Math.Floor(low / size);
        if (left2 + (count * size) < high)
        {
            size = Math.Ceiling((high - left2) / count / (rawPower / 10)) * (rawPower / 10);
        }

        var edges = new double[count + 1];
        for (int i = 0; i <= count; i++)
        {
            edges[i] = left2 + (i * size);
        }

        if (edges[^1] < high)
        {
            edges[^1] = high;
        }

        return edges;
    }

    /// <summary>The unrounded automatic bin width — Scott by default, with the fourth root.</summary>
    private static double RawAutomaticWidth(double[] finite, string rule, double span)
    {
        if (finite.Length == 0)
        {
            return span;
        }

        double quarterRoot = Math.Pow(finite.Length, 0.25);
        double raw = rule switch
        {
            "fd" => 2 * (Quartiles.Percentile(finite, 75) - Quartiles.Percentile(finite, 25))
                / quarterRoot,
            _ => 3.5 * Math.Sqrt(SampleVarianceOf(finite)) / quarterRoot,
        };
        return raw > 0 && double.IsFinite(raw) ? raw : span / 10;
    }

    /// <summary>A ceiling that forgives a few ulps, so <c>0.9/0.3</c> asks for three bins, not four.</summary>
    private static int CeilWithGrace(double value)
    {
        double nearest = Math.Round(value);
        return Math.Abs(value - nearest) <= 8 * Ulp(nearest)
            ? (int)nearest
            : (int)Math.Ceiling(value);
    }

    private static double Ulp(double value) =>
        Math.BitIncrement(Math.Abs(value)) - Math.Abs(value);

    private static double[] Normalized2D(
        double[] counts, int nx, int ny, double[] xEdges, double[] yEdges, string normalization,
        int total)
    {
        if (normalization == "count")
        {
            return counts;
        }

        var scaled = new double[counts.Length];
        if (normalization is "cumcount" or "cdf")
        {
            for (int cy = 0; cy < ny; cy++)
            {
                for (int cx = 0; cx < nx; cx++)
                {
                    double sum = counts[(cy * nx) + cx];
                    if (cx > 0)
                    {
                        sum += scaled[(cy * nx) + cx - 1];
                    }

                    if (cy > 0)
                    {
                        sum += scaled[((cy - 1) * nx) + cx];
                        if (cx > 0)
                        {
                            sum -= scaled[((cy - 1) * nx) + cx - 1];
                        }
                    }

                    scaled[(cy * nx) + cx] = sum;
                }
            }

            if (normalization == "cdf")
            {
                for (int p = 0; p < scaled.Length; p++)
                {
                    scaled[p] /= total;
                }
            }

            return scaled;
        }

        for (int cy = 0; cy < ny; cy++)
        {
            for (int cx = 0; cx < nx; cx++)
            {
                double count = counts[(cy * nx) + cx];
                double area = (xEdges[cx + 1] - xEdges[cx]) * (yEdges[cy + 1] - yEdges[cy]);
                scaled[(cy * nx) + cx] = normalization switch
                {
                    "probability" => count / total,
                    "countdensity" => count / area,
                    _ => count / (total * area), // pdf
                };
            }
        }

        return scaled;
    }

    // --- xcorr and xcov -----------------------------------------------------------------------

    /// <summary>
    /// <c>[r, lags] = xcorr(x, y, maxlag, scaleopt)</c> and <c>xcov</c>, which is the same after
    /// each signal loses its mean. Vectors answer in their own orientation; a matrix answers every
    /// ordered pair of its columns, side by side.
    /// </summary>
    private static JgsValue[] CrossCorrelation(
        string name, IReadOnlyList<JgsValue> args, int wanted, bool removeMeans, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a signal.");
        }

        string scale = "none";
        int? maxlag = null;
        JgsValue? second = null;
        for (int i = 1; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = TextOf(args[i]).ToLowerInvariant();
                if (word is not ("none" or "biased" or "unbiased" or "normalized" or "coeff"))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:xcorr:UnknInput",
                        "Scale option must be 'biased', 'unbiased', 'normalized', or 'none'.");
                }

                scale = word == "coeff" ? "normalized" : word;
                continue;
            }

            // A scalar in a numeric slot is a maxlag, not a one-sample signal — MATLAB's rule.
            int[] argDims = SizeDims(args[i]);
            bool scalar = argDims.All(static d => d == 1);
            if (i == 1 && !scalar)
            {
                second = args[i];
            }
            else if (maxlag is null && scalar)
            {
                maxlag = (int)Math.Abs(Num(name, args, i, line, col));
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name}: too many numeric arguments.");
            }
        }

        Complex[] first = ComplexElements(name, args[0], line, col);
        int[] firstDims = SizeDims(args[0]);
        bool firstVector = firstDims.Count(static d => d > 1) <= 1;
        bool row = firstVector && firstDims.Length >= 2 && firstDims[0] == 1 && firstDims[1] != 1;

        Complex[][] columns;
        Complex[]? other = null;
        if (second is not null)
        {
            if (!firstVector)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:xcorr:MismatchedAB",
                    "First argument must be a vector.");
            }

            other = ComplexElements(name, second, line, col);
            columns = [first];
        }
        else if (firstVector)
        {
            columns = [first];
        }
        else
        {
            int height = firstDims[0];
            int width = first.Length / Math.Max(1, height);
            columns = new Complex[width][];
            for (int c = 0; c < width; c++)
            {
                columns[c] = new Complex[height];
                Array.Copy(first, c * height, columns[c], 0, height);
            }
        }

        if (removeMeans)
        {
            foreach (Complex[] column in columns)
            {
                Complex mean = Complex.Zero;
                foreach (Complex v in column)
                {
                    mean += v;
                }

                mean /= column.Length == 0 ? 1 : column.Length;
                for (int i = 0; i < column.Length; i++)
                {
                    column[i] -= mean;
                }
            }

            if (other is not null)
            {
                Complex mean = Complex.Zero;
                foreach (Complex v in other)
                {
                    mean += v;
                }

                mean /= other.Length == 0 ? 1 : other.Length;
                for (int i = 0; i < other.Length; i++)
                {
                    other[i] -= mean;
                }
            }
        }

        int longest = Math.Max(columns.Max(static c => c.Length), other?.Length ?? 0);
        if (other is not null && other.Length != columns[0].Length && scale != "none")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:xcorr:NoScale",
                "Scale option must be 'none' for input vectors of different lengths.");
        }

        int lag = maxlag ?? (longest - 1);
        int lags = (2 * lag) + 1;

        // MATLAB's own length: two to the power that covers 2N−1, through the same kernels fft
        // uses, so the roundoff in the answer is the roundoff MATLAB shows.
        int nfft = 1;
        while (nfft < (2 * longest) - 1)
        {
            nfft <<= 1;
        }

        Complex[][] spectra = [.. columns.Select(c => PaddedSpectrum(c, nfft))];
        Complex[]? otherSpectrum = other is null ? null : PaddedSpectrum(other, nfft);

        bool allReal = args.All(a => IsTextScalar(a) || !HasImaginary(a));
        var answers = new List<Complex[]>();
        if (other is not null)
        {
            answers.Add(CorrelationOf(spectra[0], otherSpectrum!, nfft, lag, longest));
        }
        else
        {
            foreach (Complex[] left in spectra)
            {
                foreach (Complex[] right in spectra)
                {
                    answers.Add(CorrelationOf(left, right, nfft, lag, longest));
                }
            }
        }

        // The scales: biased divides by the longer length, unbiased by how many products each lag
        // really summed, normalized by the zero-lag energies — computed directly, as MATLAB does.
        for (int a = 0; a < answers.Count; a++)
        {
            Complex[] answer = answers[a];
            if (scale == "biased")
            {
                for (int i = 0; i < answer.Length; i++)
                {
                    answer[i] /= longest;
                }
            }
            else if (scale == "unbiased")
            {
                for (int i = 0; i < answer.Length; i++)
                {
                    int overlap = Math.Max(1, longest - Math.Abs(i - lag));
                    answer[i] /= overlap;
                }
            }
            else if (scale == "normalized")
            {
                Complex[] left = other is not null ? columns[0] : columns[a / columns.Length];
                Complex[] right = other ?? columns[a % columns.Length];
                double energyLeft = left.Sum(static v => v.Magnitude * v.Magnitude);
                double energyRight = right.Sum(static v => v.Magnitude * v.Magnitude);
                double anchor = Math.Sqrt(energyLeft * energyRight);
                for (int i = 0; i < answer.Length; i++)
                {
                    answer[i] /= anchor;
                }
            }
        }

        int columnsOut = answers.Count;
        var flatOut = new Complex[lags * columnsOut];
        for (int c = 0; c < columnsOut; c++)
        {
            Array.Copy(answers[c], 0, flatOut, c * lags, lags);
        }

        int[] shape = columnsOut == 1
            ? (row ? [1, lags] : [lags, 1])
            : [lags, columnsOut];
        JgsValue r = allReal
            ? ShapedFlatReal([.. flatOut.Select(static v => v.Real)], shape)
            : ShapedFlatComplex(flatOut, shape);

        var lagValues = new double[lags];
        for (int i = 0; i < lags; i++)
        {
            lagValues[i] = i - lag;
        }

        return Outputs(wanted, r, JgsMatrix.FromColumnMajorDims(lagValues, [1, lags]));
    }

    /// <summary>Any two-dimensional numeric value as a rectangle — a vector included.</summary>
    private static double[,] RectangleOf(string name, JgsValue value, int line, int col)
    {
        double[] flat = FlattenColumnMajor(name, value, line, col);
        int[] dims = SizeDims(value);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} works on matrices and vectors.");
        }

        int rows = dims.Length > 0 ? dims[0] : 0;
        int cols = rows == 0 ? 0 : flat.Length / rows;
        var rect = new double[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                rect[r, c] = flat[(c * rows) + r];
            }
        }

        return rect;
    }

    private static bool HasImaginary(JgsValue value) =>
        value.Type switch
        {
            JgsType.Complex => true,
            JgsType.Array => value.BoxedElements().Any(static e => e.Type == JgsType.Complex),
            _ => false,
        };

    private static Complex[] PaddedSpectrum(Complex[] signal, int nfft)
    {
        var re = new double[nfft];
        var im = new double[nfft];
        for (int i = 0; i < signal.Length; i++)
        {
            re[i] = signal[i].Real;
            im[i] = signal[i].Imaginary;
        }

        FftKernels.Transform(re, im, nfft, inverse: false);
        var spectrum = new Complex[nfft];
        for (int i = 0; i < nfft; i++)
        {
            spectrum[i] = new Complex(re[i], im[i]);
        }

        return spectrum;
    }

    /// <summary>One pair's correlation, from spectra to the asked lags.</summary>
    private static Complex[] CorrelationOf(Complex[] left, Complex[] right, int nfft, int lag, int longest)
    {
        var re = new double[nfft];
        var im = new double[nfft];
        for (int i = 0; i < nfft; i++)
        {
            Complex product = left[i] * Complex.Conjugate(right[i]);
            re[i] = product.Real;
            im[i] = product.Imaginary;
        }

        FftKernels.Transform(re, im, nfft, inverse: true);
        var answer = new Complex[(2 * lag) + 1];
        for (int k = -lag; k <= lag; k++)
        {
            // A lag no window of the data can reach is zero by definition, not a wrapped copy.
            int at = ((k % nfft) + nfft) % nfft;
            answer[k + lag] = Math.Abs(k) >= longest
                ? Complex.Zero
                : new Complex(re[at], im[at]);
        }

        return answer;
    }

    private static JgsValue ShapedFlatReal(double[] flat, int[] shape) =>
        JgsMatrix.FromColumnMajorDims(flat, shape);

    private static JgsValue ShapedFlatComplex(Complex[] flat, int[] shape)
    {
        var boxed = new JgsValue[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            boxed[i] = flat[i].Imaginary == 0 ? JgsValue.Number(flat[i].Real) : JgsValue.ComplexNum(flat[i]);
        }

        return JgsMatrix.FromElementsDims(boxed, shape);
    }

    // --- subspace -----------------------------------------------------------------------------

    /// <summary>
    /// <c>theta = subspace(A, B)</c>: the largest principal angle between the ranges of the two
    /// matrices — orthonormalize both, project one onto the other, and take the arcsine of what is
    /// left, capped at one so roundoff cannot ask for the arcsine of more.
    /// </summary>
    private static JgsValue SubspaceAngle(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("subspace", args, 2, line, col);
        (double[] a, int aRows, int aCols) = OrthonormalRange(args[0], "subspace", line, col);
        (double[] b, int bRows, int bCols) = OrthonormalRange(args[1], "subspace", line, col);
        if (aRows != bRows)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:innerdim",
                "Incorrect dimensions for matrix multiplication. Check that the number of columns "
                + "in the first matrix matches the number of rows in the second matrix.");
        }

        // The wider basis projects the narrower, so the angle is symmetric in its arguments.
        if (aCols < bCols)
        {
            (a, b) = (b, a);
            (aCols, bCols) = (bCols, aCols);
        }

        int m = aRows;

        // B − A·(AᵀB), then its largest singular value.
        var cross = new double[aCols * bCols];
        for (int i = 0; i < aCols; i++)
        {
            for (int j = 0; j < bCols; j++)
            {
                double sum = 0;
                for (int r = 0; r < m; r++)
                {
                    sum += a[(i * m) + r] * b[(j * m) + r];
                }

                cross[(j * aCols) + i] = sum;
            }
        }

        var residue = new double[m * bCols];
        for (int j = 0; j < bCols; j++)
        {
            for (int r = 0; r < m; r++)
            {
                double sum = b[(j * m) + r];
                for (int i = 0; i < aCols; i++)
                {
                    sum -= a[(i * m) + r] * cross[(j * aCols) + i];
                }

                residue[(j * m) + r] = sum;
            }
        }

        double largest = 0;
        if (bCols > 0)
        {
            double[] values = Svd.Factor(residue, m, bCols).Values;
            largest = values.Length == 0 ? 0 : values[0];
        }

        return JgsValue.Number(Math.Asin(Math.Min(1, largest)));
    }

    /// <summary>An orthonormal basis for a matrix's range, as flat column-major columns.</summary>
    private static (double[] Basis, int Rows, int Columns) OrthonormalRange(
        JgsValue value, string name, int line, int col)
    {
        double[] flat = FlattenColumnMajor(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 0;
        int cols = rows == 0 ? 0 : flat.Length / rows;
        if (rows == 0 || cols == 0)
        {
            return ([], rows, 0);
        }

        Svd svd = Svd.Factor(flat, rows, cols);
        double tolerance = Math.Max(rows, cols) * (svd.Values.Length == 0 ? 0 : svd.Values[0])
            * (Math.BitIncrement(1.0) - 1.0);
        int keep = svd.Values.Count(v => v > tolerance);
        var basis = new double[rows * keep];
        Array.Copy(svd.UColumnMajor, basis, basis.Length);
        return (basis, rows, keep);
    }
}
