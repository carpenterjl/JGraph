using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The piecewise-polynomial and interpolation half of MATLAB's <c>polyfun</c> (M101):
/// <c>spline</c>, <c>pchip</c>, <c>makima</c>, <c>ppval</c>, <c>mkpp</c>, <c>unmkpp</c>,
/// <c>interp1q</c>, <c>interpft</c> and <c>interpn</c>, together with the grid reader
/// <c>interp2</c> and <c>interp3</c> now share.
/// </summary>
/// <remarks>
/// <para>
/// The idea this milestone adds is the <em>piecewise polynomial</em> itself: MATLAB's
/// <c>pp</c> structure, which is a curve handed about as data rather than as an answer. Once it
/// exists, <c>spline(x,y)</c> and <c>spline(x,y,xq)</c> stop being two functions and become one
/// construction that is either returned or read; <c>mkpp</c> and <c>unmkpp</c> are its two ends;
/// and <c>interp1(x,v,method,'pp')</c> becomes reachable at last.
/// </para>
/// <para>
/// The other idea is that reading a grid is one operation in any number of dimensions.
/// <see cref="GridSampler"/> holds it, and <c>interp2</c>, <c>interp3</c> and <c>interpn</c> differ
/// here only in how many directions they have and in which order they name them — MATLAB's
/// <c>meshgrid</c> puts x across the columns and y down the rows, while <c>ndgrid</c> numbers the
/// directions as the array numbers them, and that transposition is the whole of the difference.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The three slope rules a cubic through samples can be built by.</summary>
    private enum CubicRule
    {
        /// <summary>Not-a-knot: the second derivative is continuous everywhere.</summary>
        Spline = 0,

        /// <summary>Shape-preserving: the curve never overshoots a sample.</summary>
        Pchip = 1,

        /// <summary>Modified Akima: local enough not to ring, weighted enough not to flatten.</summary>
        Makima = 2,
    }

    /// <summary>A piecewise polynomial, as MATLAB's <c>pp</c> structure holds one.</summary>
    /// <param name="Breaks">Where the pieces meet, increasing; one more than there are pieces.</param>
    /// <param name="Coefficients">
    /// One row of <paramref name="Order"/> per piece per component, highest power first and in the
    /// local variable <c>x − breaks(i)</c>, laid out row after row.
    /// </param>
    /// <param name="Pieces">How many pieces there are.</param>
    /// <param name="Order">How many coefficients each piece has — one more than its degree.</param>
    /// <param name="Dimension">How many numbers the curve answers with at each point.</param>
    private readonly record struct Piecewise(
        double[] Breaks, double[] Coefficients, int Pieces, int Order, int Dimension);

    /// <summary>The methods <c>interp2</c>, <c>interp3</c> and <c>interpn</c> name.</summary>
    private static readonly string[] GridMethods =
        ["linear", "nearest", "cubic", "spline", "makima"];

    /// <summary>Registers the interpolation builtins into <paramref name="env"/>.</summary>
    /// <param name="env">The scope to declare into.</param>
    /// <param name="host">Where a warning about a method that had to be changed is written.</param>
    internal static void RegisterInterpolationBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("spline", (args, line, col) => PiecewiseCubic("spline", CubicRule.Spline, args, line, col));
        Define("pchip", (args, line, col) => PiecewiseCubic("pchip", CubicRule.Pchip, args, line, col));
        Define("makima", (args, line, col) => PiecewiseCubic("makima", CubicRule.Makima, args, line, col));

        Define("mkpp", MakePiecewise);
        Define("unmkpp", (args, line, col) => UnmakePiecewise(args, 1, line, col)[0],
            (args, wanted, line, col) => UnmakePiecewise(args, wanted, line, col));
        Define("ppval", PiecewiseValue);

        Define("interp1q", QuickInterpolate);
        Define("interpft", FourierInterpolate);
        Define("interpn", (args, line, col) => SampleGridded("interpn", args, rank: 0, host, line, col));
    }

    // --- The piecewise polynomial ------------------------------------------------------------------

    /// <summary>The <c>pp</c> structure for a curve, with its fields in the order MATLAB reports them.</summary>
    private static JgsValue PiecewiseStruct(in Piecewise curve)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["form"] = JgsValue.Str("pp"),
            ["breaks"] = Row(curve.Breaks),
            ["coefs"] = RowsOf(curve.Coefficients, curve.Dimension * curve.Pieces, curve.Order),
            ["pieces"] = JgsValue.Number(curve.Pieces),
            ["order"] = JgsValue.Number(curve.Order),
            ["dim"] = JgsValue.Number(curve.Dimension),
        };

        return JgsValue.Struct(fields);
    }

    /// <summary>A matrix from values laid out row after row, which is how a pp's coefficients read.</summary>
    private static JgsValue RowsOf(double[] rowMajor, int rows, int cols)
    {
        var columnMajor = new double[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                columnMajor[r + (c * rows)] = rowMajor[(r * cols) + c];
            }
        }

        return JgsMatrix.FromColumnMajor(columnMajor, rows, cols);
    }

    /// <summary>Reads a <c>pp</c> structure, refusing anything that is not one.</summary>
    private static Piecewise ReadPiecewise(string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Struct || value.IsStructArray)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unmkpp:InputArrayNotPP",
                "The input array does not seem to describe a pp function.");
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        JgsValue Field(string field) => fields.ContainsKey(field)
            ? fields[field]
            : throw new JgsRuntimeException(line, col, "MATLAB:nonExistentField",
                $"Unrecognized field name \"{field}\".");

        JgsValue form = Field("form");
        if (form.Type != JgsType.String || !string.Equals(form.AsString, "pp", StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unmkpp:InputArrayNotPP",
                "The input array does not seem to describe a pp function.");
        }

        double[] breaks = FlattenColumnMajor(name, Field("breaks"), line, col);
        JgsValue coefficients = Field("coefs");
        int pieces = (int)PiecewiseScalar(name, Field("pieces"), line, col);
        int order = (int)PiecewiseScalar(name, Field("order"), line, col);
        int dimension = (int)PiecewiseScalar(name, Field("dim"), line, col);

        int[] shape = SizeDims(coefficients);
        int rows = shape.Length > 0 ? shape[0] : 1;
        double[] flat = FlattenColumnMajor(name, coefficients, line, col);
        var rowMajor = new double[flat.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < order; c++)
            {
                rowMajor[(r * order) + c] = flat[r + (c * rows)];
            }
        }

        return new Piecewise(breaks, rowMajor, pieces, order, dimension);
    }

    private static double PiecewiseScalar(string name, JgsValue value, int line, int col)
    {
        double[] flat = FlattenColumnMajor(name, value, line, col);
        return flat.Length == 1
            ? flat[0]
            : throw new JgsRuntimeException(line, col, "MATLAB:unmkpp:InputArrayNotPP",
                "The input array does not seem to describe a pp function.");
    }

    /// <summary>
    /// The curve read at one point. Outside the breaks the nearest piece is continued rather than
    /// stopped, which is what makes a spline extrapolate and a caller able to rely on it.
    /// </summary>
    private static void EvaluatePiecewise(in Piecewise curve, double at, Span<double> into)
    {
        int piece = 0;
        int last = curve.Pieces - 1;
        int low = 0;
        int high = curve.Breaks.Length - 1;
        if (double.IsNaN(at))
        {
            into.Fill(double.NaN);
            return;
        }

        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (at < curve.Breaks[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        piece = Math.Clamp(low, 0, Math.Max(last, 0));
        double t = at - curve.Breaks[piece];
        for (int component = 0; component < curve.Dimension; component++)
        {
            int row = (piece * curve.Dimension) + component;
            double total = 0;
            for (int k = 0; k < curve.Order; k++)
            {
                total = (total * t) + curve.Coefficients[(row * curve.Order) + k];
            }

            into[component] = total;
        }
    }

    /// <summary>The curve read at every point of a list, laid out one point after another.</summary>
    private static double[] EvaluatePiecewise(in Piecewise curve, double[] at)
    {
        var answer = new double[at.Length * curve.Dimension];
        for (int q = 0; q < at.Length; q++)
        {
            EvaluatePiecewise(curve, at[q], answer.AsSpan(q * curve.Dimension, curve.Dimension));
        }

        return answer;
    }

    /// <summary>
    /// The shape a curve's values take: the shape of the points asked about when the curve answers
    /// with one number, and a column of components per point when it answers with several.
    /// </summary>
    private static JgsValue ShapedPiecewiseValues(double[] values, int dimension, JgsValue queries)
    {
        if (dimension == 1)
        {
            return ShapedNumbers(values, SizeDims(queries));
        }

        return JgsMatrix.FromColumnMajor(values, dimension, values.Length / Math.Max(dimension, 1));
    }

    // --- spline, pchip and makima ------------------------------------------------------------------

    /// <summary>
    /// <c>spline(x,y)</c>, <c>pchip(x,y)</c> and <c>makima(x,y)</c>, which build the curve, and their
    /// three-argument forms, which build it and then read it.
    /// </summary>
    private static JgsValue PiecewiseCubic(
        string name, CubicRule rule, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        double[] sites = FlattenColumnMajor(name, args[0], line, col);
        int n = sites.Length;

        JgsValue given = args[1];
        int[] shape = SizeDims(given);
        int count = ElementCount(given);
        bool vector = IsVector(given) || count <= 1;
        int alongSites = vector ? count : shape[^1];
        int dimension = vector ? 1 : count / Math.Max(alongSites, 1);

        if (n < 2 || count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:chckxy:NotEnoughPts",
                "The first two inputs must have at least two elements.");
        }

        // Two more values than there are sites is how MATLAB asks for a clamped spline: the first
        // and the last are slopes rather than values. It is a spline's end condition and nothing
        // else's, so the shape-preserving rules refuse the same count.
        bool clamped = alongSites == n + 2 && rule == CubicRule.Spline;
        if (alongSites != n && !clamped)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:chckxy:NumSitesMismatchValues",
                $"The number of sample points X, {n}, is incompatible with the number of values Y, {alongSites}.");
        }

        double[] values = FlattenColumnMajor(name, given, line, col);
        if (!vector)
        {
            // A matrix of values runs along its last dimension, so each component's samples are
            // strided rather than contiguous; gathering them once is cheaper than doing it per piece.
            var gathered = new double[values.Length];
            for (int s = 0; s < alongSites; s++)
            {
                for (int c = 0; c < dimension; c++)
                {
                    gathered[(c * alongSites) + s] = values[(s * dimension) + c];
                }
            }

            values = gathered;
        }

        int[] order = SortedSites(sites, line, col);
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = sites[order[i]];
        }

        var coefficients = new double[dimension * (n - 1) * 4];
        var component = new double[n];
        for (int c = 0; c < dimension; c++)
        {
            int at = c * alongSites;
            double leftSlope = clamped ? values[at] : 0;
            double rightSlope = clamped ? values[at + alongSites - 1] : 0;
            int offset = clamped ? 1 : 0;
            for (int i = 0; i < n; i++)
            {
                component[i] = values[at + offset + order[i]];
            }

            double[] slopes = rule switch
            {
                CubicRule.Pchip => Interpolation.PchipSlopes(x, component),
                CubicRule.Makima => Interpolation.MakimaSlopes(x, component),
                _ when clamped => Interpolation.SplineSlopes(x, component, leftSlope, rightSlope),
                _ => Interpolation.SplineSlopes(x, component),
            };

            double[] piece = Interpolation.PieceCoefficients(x, component, slopes);
            for (int p = 0; p < n - 1; p++)
            {
                for (int k = 0; k < 4; k++)
                {
                    coefficients[((((p * dimension) + c) * 4) + k)] = piece[(p * 4) + k];
                }
            }
        }

        var curve = new Piecewise(x, coefficients, n - 1, 4, dimension);
        if (args.Count == 2)
        {
            return PiecewiseStruct(curve);
        }

        double[] at2 = FlattenColumnMajor(name, args[2], line, col);
        return ShapedPiecewiseValues(EvaluatePiecewise(curve, at2), dimension, args[2]);
    }

    /// <summary>
    /// The sample positions in increasing order, refusing a repeat with the identifier MATLAB's own
    /// argument check raises rather than the one <c>interp1</c>'s sorter uses.
    /// </summary>
    private static int[] SortedSites(double[] sites, int line, int col)
    {
        var order = new int[sites.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => sites[a].CompareTo(sites[b]));
        for (int i = 1; i < order.Length; i++)
        {
            if (sites[order[i]] == sites[order[i - 1]])
            {
                throw new JgsRuntimeException(line, col, "MATLAB:chckxy:RepeatedSites",
                    "The first input must contain unique values.");
            }
        }

        return order;
    }

    // --- mkpp, unmkpp and ppval --------------------------------------------------------------------

    /// <summary><c>mkpp(breaks, coefs)</c> and <c>mkpp(breaks, coefs, d)</c>.</summary>
    private static JgsValue MakePiecewise(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mkpp", args, 2, 3, line, col);
        double[] breaks = FlattenColumnMajor("mkpp", args[0], line, col);
        int pieces = breaks.Length - 1;
        int dimension = args.Count > 2 ? (int)Num("mkpp", args, 2, line, col) : 1;

        double[] flat = FlattenColumnMajor("mkpp", args[1], line, col);
        int slots = dimension * pieces;
        if (pieces < 1 || dimension < 1 || slots == 0 || flat.Length % slots != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:mkpp:PPNumberMismatchCoeffs",
                $"The requested number of polynomial pieces, {Math.Max(pieces, 0)}, is incompatible with the "
                + $"proposed size, [{dimension}], of a coefficient and the number, {flat.Length}, of scalar "
                + "coefficients provided.");
        }

        // MATLAB reshapes whatever it was handed into as many rows as there are pieces times
        // components, so a coefficient matrix given the other way round is still read column by
        // column — the reshape is the specification, not a convenience.
        int order = flat.Length / slots;
        var rowMajor = new double[flat.Length];
        for (int r = 0; r < slots; r++)
        {
            for (int c = 0; c < order; c++)
            {
                rowMajor[(r * order) + c] = flat[r + (c * slots)];
            }
        }

        return PiecewiseStruct(new Piecewise(breaks, rowMajor, pieces, order, dimension));
    }

    /// <summary><c>[breaks, coefs, L, order, dim] = unmkpp(pp)</c>.</summary>
    private static JgsValue[] UnmakePiecewise(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("unmkpp", args, 1, line, col);
        Piecewise curve = ReadPiecewise("unmkpp", args[0], line, col);
        return Outputs(
            wanted,
            Row(curve.Breaks),
            RowsOf(curve.Coefficients, curve.Dimension * curve.Pieces, curve.Order),
            JgsValue.Number(curve.Pieces),
            JgsValue.Number(curve.Order),
            JgsValue.Number(curve.Dimension));
    }

    /// <summary><c>ppval(pp, xq)</c>.</summary>
    private static JgsValue PiecewiseValue(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("ppval", args, 2, line, col);
        Piecewise curve = ReadPiecewise("ppval", args[0], line, col);
        double[] at = FlattenColumnMajor("ppval", args[1], line, col);
        return ShapedPiecewiseValues(EvaluatePiecewise(curve, at), curve.Dimension, args[1]);
    }

    // --- what interp1 needs of all this -------------------------------------------------------------

    /// <summary>
    /// The coefficients of every piece for whichever cubic a method names, or nothing at all for a
    /// method that is not one.
    /// </summary>
    /// <remarks>
    /// Cubic convolution is included here even though it has no slopes, because writing it as
    /// coefficients is what makes <c>'extrap'</c> mean the same thing for it as for the others: the
    /// kernel is defined only inside a cell, but the cubic it comes to carries on.
    /// </remarks>
    private static double[] CubicCoefficients(double[] x, double[] y, string method)
    {
        switch (method)
        {
            case "spline":
                return Interpolation.PieceCoefficients(x, y, Interpolation.SplineSlopes(x, y));
            case "pchip":
                return Interpolation.PieceCoefficients(x, y, Interpolation.PchipSlopes(x, y));
            case "makima":
                return Interpolation.PieceCoefficients(x, y, Interpolation.MakimaSlopes(x, y));
            case "cubic":
                return KeysCoefficients(x, y);
            default:
                return [];
        }
    }

    /// <summary>The four coefficients of each piece of the cubic convolution through evenly spaced samples.</summary>
    private static double[] KeysCoefficients(double[] x, double[] y)
    {
        int n = y.Length;

        // The kernel reads one sample beyond each end; those are invented so that the parabola
        // through the three nearest carries on, which is what MATLAB does and what makes the first
        // cell reproduce a quadratic as exactly as the middle ones do.
        var padded = new double[n + 2];
        Array.Copy(y, 0, padded, 1, n);
        padded[0] = Interpolation.KeysEdgeSample(y[0], y[1], y[2]);
        padded[n + 1] = Interpolation.KeysEdgeSample(y[n - 1], y[n - 2], y[n - 3]);

        var coefficients = new double[(n - 1) * 4];
        for (int i = 0; i < n - 1; i++)
        {
            double a = padded[i];
            double b = padded[i + 1];
            double c = padded[i + 2];
            double d = padded[i + 3];
            double h = x[i + 1] - x[i];

            // Keys' four weights are cubics in the fraction of the cell, so the curve through them
            // is one too; these are its coefficients, divided down into the local variable the
            // piecewise form is written in.
            coefficients[i * 4] = ((-0.5 * a) + (1.5 * b) - (1.5 * c) + (0.5 * d)) / (h * h * h);
            coefficients[(i * 4) + 1] = (a - (2.5 * b) + (2 * c) - (0.5 * d)) / (h * h);
            coefficients[(i * 4) + 2] = ((-0.5 * a) + (0.5 * c)) / h;
            coefficients[(i * 4) + 3] = b;
        }

        return coefficients;
    }

    /// <summary>One piece of a cubic, at a point measured from the piece's own left end.</summary>
    private static double CubicAt(double[] coefficients, int piece, double t)
    {
        int at = piece * 4;
        return (((((coefficients[at] * t) + coefficients[at + 1]) * t) + coefficients[at + 2]) * t)
            + coefficients[at + 3];
    }

    /// <summary>
    /// The method <c>interp1</c> can actually answer with. Cubic convolution reads two samples
    /// either side and is written in cell widths, so a short or uneven set of samples cannot carry
    /// it; MATLAB says so and changes the method, and so does this.
    /// </summary>
    private static string SettleInterp1Method(string method, double[] x, JGraphScriptGlobals host)
    {
        if (method != "cubic")
        {
            return method;
        }

        if (x.Length < 3)
        {
            host.WriteErr("Warning: The 'cubic' method requires at least 3 points in each dimension.\n"
                + "Reverting to the default 'linear' method because this condition is not met.\n");
            return "linear";
        }

        if (!IsEvenlySpaced(x))
        {
            host.WriteErr("Warning: The 'cubic' method requires the grid to have a uniform spacing.\n"
                + "Switching the method from 'cubic' to 'spline' because this condition is not met.\n");
            return "spline";
        }

        return method;
    }

    /// <summary>
    /// <c>pp = interp1(x, v, method, 'pp')</c>: the curve <c>interp1</c> would have read, handed
    /// back instead.
    /// </summary>
    /// <remarks>
    /// Three of <c>interp1</c>'s nine methods have no piecewise form and MATLAB refuses each by
    /// name: <c>'previous'</c> and <c>'next'</c> are steps whose breaks would have to be invented,
    /// and <c>'makima'</c> is refused for reasons MathWorks does not give. The refusals are
    /// replicated with their identifiers rather than answered around.
    /// </remarks>
    private static JgsValue Interp1Piecewise(
        double[] sites, JgsValue values, string method, JGraphScriptGlobals host, int line, int col)
    {
        switch (method)
        {
            case "previous":
                throw new JgsRuntimeException(line, col, "MATLAB:interp1:ppGriddedInterpolantPrevious",
                    "INTERP1(...,'previous','PP') is not supported. Use GRIDDEDINTERPOLANT instead.");
            case "next":
                throw new JgsRuntimeException(line, col, "MATLAB:interp1:ppGriddedInterpolantNext",
                    "INTERP1(...,'next','PP') is not supported. Use GRIDDEDINTERPOLANT instead.");
            case "makima":
                throw new JgsRuntimeException(line, col, "MATLAB:interp1:ppAkima",
                    "'pp' option not supported for modified Akima cubic interpolation.");
            case "v5cubic":
                method = "cubic";
                break;
        }

        int n = sites.Length;
        if (n < 2)
        {
            throw new JgsRuntimeException(line, col, "interp1 needs at least two samples.");
        }

        int[] shape = SizeDims(values);
        double[] flat = FlattenColumnMajor("interp1", values, line, col);
        bool vector = shape.Length <= 2 && (shape.Length < 2 || shape[0] == 1 || shape[1] == 1);
        int sets = vector && flat.Length == n
            ? 1
            : shape.Length == 2 && shape[0] == n
                ? shape[1]
                : throw new JgsRuntimeException(line, col,
                    $"interp1: the values must be a vector of {n} or a matrix with {n} rows.");

        int[] order = SortedOrder(sites, line, col);
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = sites[order[i]];
        }

        method = SettleInterp1Method(method, x, host);
        int pieces = method == "nearest" ? n : n - 1;
        int width = method switch
        {
            "nearest" => 1,
            "linear" => 2,
            _ => 4,
        };

        double[] breaks;
        if (method == "nearest")
        {
            // A step's pieces meet halfway between the samples, so its breaks are not the sites:
            // there is one more piece than there are gaps, and the two end ones are half as wide.
            breaks = new double[n + 1];
            breaks[0] = x[0];
            breaks[n] = x[n - 1];
            for (int i = 1; i < n; i++)
            {
                breaks[i] = (x[i - 1] + x[i]) / 2;
            }
        }
        else
        {
            breaks = x;
        }

        var coefficients = new double[sets * pieces * width];
        var component = new double[n];
        for (int set = 0; set < sets; set++)
        {
            for (int i = 0; i < n; i++)
            {
                component[i] = flat[(set * n) + order[i]];
            }

            double[] piece = method switch
            {
                "nearest" => component,
                "linear" => LinearCoefficients(x, component),
                _ => CubicCoefficients(x, component, method),
            };

            for (int p = 0; p < pieces; p++)
            {
                for (int k = 0; k < width; k++)
                {
                    coefficients[((((p * sets) + set) * width) + k)] = piece[(p * width) + k];
                }
            }
        }

        return PiecewiseStruct(new Piecewise(breaks, coefficients, pieces, width, sets));
    }

    /// <summary>Each straight piece as a slope and the value it starts from.</summary>
    private static double[] LinearCoefficients(double[] x, double[] y)
    {
        var coefficients = new double[(x.Length - 1) * 2];
        for (int i = 0; i < x.Length - 1; i++)
        {
            coefficients[i * 2] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
            coefficients[(i * 2) + 1] = y[i];
        }

        return coefficients;
    }

    // --- interp1q ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>interp1q(x, Y, xi)</c>: straight-line interpolation that checks nothing, which is what
    /// makes it quicker and what makes it refuse anything but columns.
    /// </summary>
    /// <remarks>
    /// The column requirement is not a rule MATLAB documents; it is what falls out of its stacking
    /// the sites and the query points on top of each other to sort them, and it is replicated
    /// rather than repaired so that a script that works on the real thing works here — and so that
    /// one which would fail there fails here for the same reason and with the same identifier.
    /// </remarks>
    private static JgsValue QuickInterpolate(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("interp1q", args, 3, line, col);
        int[] sitesShape = SizeDims(args[0]);
        int[] queryShape = SizeDims(args[2]);
        if (!IsColumnShaped(sitesShape) || !IsColumnShaped(queryShape))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:catenate:dimensionMismatch",
                "Dimensions of arrays being concatenated are not consistent.");
        }

        double[] sites = FlattenColumnMajor("interp1q", args[0], line, col);
        double[] at = FlattenColumnMajor("interp1q", args[2], line, col);
        int n = sites.Length;
        int[] shape = SizeDims(args[1]);
        int rows = shape.Length > 0 ? shape[0] : 1;
        int sets = ElementCount(args[1]) / Math.Max(rows, 1);
        double[] values = FlattenColumnMajor("interp1q", args[1], line, col);
        if (rows != n || n < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:badsubscript",
                $"Index in position 1 exceeds array bounds. Index must not exceed {rows}.");
        }

        var answer = new double[at.Length * sets];
        for (int q = 0; q < at.Length; q++)
        {
            double point = at[q];
            if (double.IsNaN(point) || point < sites[0] || point > sites[n - 1])
            {
                for (int s = 0; s < sets; s++)
                {
                    answer[(s * at.Length) + q] = double.NaN;
                }

                continue;
            }

            int low = 0;
            int high = n - 1;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (point < sites[mid])
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            double t = (point - sites[low]) / (sites[low + 1] - sites[low]);
            for (int s = 0; s < sets; s++)
            {
                double left = values[(s * n) + low];
                double right = values[(s * n) + low + 1];
                answer[(s * at.Length) + q] = left + (t * (right - left));
            }
        }

        return sets == 1
            ? JgsMatrix.FromColumnMajor(answer, at.Length, 1)
            : JgsMatrix.FromColumnMajor(answer, at.Length, sets);
    }

    private static bool IsColumnShaped(int[] dims) =>
        dims.Length <= 2 && (dims.Length < 2 || dims[1] == 1);

    // --- interpft ----------------------------------------------------------------------------------

    /// <summary><c>interpft(X, n)</c> and <c>interpft(X, n, dim)</c>.</summary>
    private static JgsValue FourierInterpolate(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("interpft", args, 2, 3, line, col);
        int[] dims = SizeDims(args[0]);
        int wanted = (int)Num("interpft", args, 1, line, col);
        if (wanted < 0)
        {
            throw new JgsRuntimeException(line, col, "interpft: the number of points cannot be negative.");
        }

        int dim = args.Count > 2
            ? DimensionArgument("interpft", args, 2, line, col)
            : DefaultDimensionOf(dims);

        int rank = Math.Max(dims.Length, dim);
        var shape = new int[rank];
        for (int i = 0; i < rank; i++)
        {
            shape[i] = i < dims.Length ? dims[i] : 1;
        }

        Complex[] source = ComplexArrayOf("interpft", args[0], line, col);

        // A real record has a spectrum that is symmetric about zero, so the answer is real too —
        // but only in exact arithmetic, and what actually comes back carries a few 1e-18 of
        // imaginary dust. MATLAB drops it, and a record that was real has to stay real here or
        // every plot of a resampled signal would refuse itself.
        bool real = true;
        foreach (Complex sample in source)
        {
            if (sample.Imaginary != 0)
            {
                real = false;
                break;
            }
        }

        int length = shape[dim - 1];
        int stride = 1;
        for (int i = 0; i < dim - 1; i++)
        {
            stride *= shape[i];
        }

        var answerShape = (int[])shape.Clone();
        answerShape[dim - 1] = wanted;
        int total = 1;
        foreach (int size in answerShape)
        {
            total *= size;
        }

        var answer = new Complex[total];
        if (total == 0 || length == 0)
        {
            return ComplexShaped(answer, answerShape);
        }

        int outer = source.Length / (length * stride);
        var re = new double[length];
        var im = new double[length];
        var outRe = new double[wanted];
        var outIm = new double[wanted];
        for (int o = 0; o < outer; o++)
        {
            for (int i = 0; i < stride; i++)
            {
                int from = (o * length * stride) + i;
                for (int k = 0; k < length; k++)
                {
                    re[k] = source[from + (k * stride)].Real;
                    im[k] = source[from + (k * stride)].Imaginary;
                }

                FourierResampling.Resample(re, im, outRe, outIm);

                int to = (o * wanted * stride) + i;
                for (int k = 0; k < wanted; k++)
                {
                    answer[to + (k * stride)] = new Complex(outRe[k], real ? 0 : outIm[k]);
                }
            }
        }

        return ComplexShaped(answer, answerShape);
    }

    /// <summary>The first dimension a value is longer than one along, which is where a reduction acts.</summary>
    private static int DefaultDimensionOf(int[] dims)
    {
        for (int i = 0; i < dims.Length; i++)
        {
            if (dims[i] != 1)
            {
                return i + 1;
            }
        }

        return 1;
    }
}
