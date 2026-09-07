using System;
using System.Collections.Generic;

namespace JGraph.Numerics;

/// <summary>
/// The second face of the same Gauss–Kronrod pair: the <em>mesh</em> adaptation Shampine's "quadva"
/// uses, where every panel that has not met its share of the tolerance is halved in one sweep and
/// the panels that have met it are retired.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Quadrature.Integrate"/> answers <c>integral</c> and <c>quadgk</c> and splits one panel
/// at a time, steered by a QUADPACK-scaled error estimate. This one exists because the nested verbs
/// — <c>integral2</c>'s <c>'iterated'</c> method and <c>integral3</c>'s outer integral — are decided
/// by <em>where the outer nodes land</em> rather than by how accurate any one of them is: each outer
/// node costs a whole inner integration, whose own answer carries its own tolerance, so an outer
/// mesh half an ulp away from MATLAB's samples a different function and the two answers part in the
/// sixth figure rather than the sixteenth. Sharing a mesh with MATLAB is the only way a triple
/// integral agrees with it to the tolerance it asked for.
/// </para>
/// <para>
/// The three differences from the panel-at-a-time engine are the ones that decide the mesh: the
/// error estimate is the raw Kronrod-minus-Gauss difference and is never rescaled; a panel is
/// retired the moment its error is under its <em>share</em> of the tolerance, <c>2·tol·|h|/L</c> for
/// a path of length <c>L</c>; and the endpoint transform is the same cubic written in the form that
/// keeps its accuracy near the ends, which is what puts the outermost nodes on MATLAB's abscissae to
/// the last bit rather than to within a rounding of the cancellation <c>3t - t³</c> carries.
/// </para>
/// </remarks>
public static partial class Quadrature
{
    /// <summary>What stopped an integration short of its tolerance, if anything did.</summary>
    public enum Trouble
    {
        /// <summary>Nothing: the error bound met the tolerance.</summary>
        None,

        /// <summary>Two abscissae came within rounding of each other, so refining further means nothing.</summary>
        MinimumStepSize,

        /// <summary>The mesh reached its ceiling with panels still unconverged.</summary>
        MaximumIntervalCount,

        /// <summary>The running value or its error bound stopped being a number.</summary>
        NonFiniteValue,
    }

    /// <summary>
    /// What a mesh integration answered: its value, the bound on its error, what stopped it, and the
    /// abscissa the trouble was found at (meaningful only for <see cref="Trouble.MinimumStepSize"/>).
    /// </summary>
    public readonly record struct MeshResult(double Value, double ErrorBound, Trouble Trouble, double Where);

    /// <summary>MATLAB's ceiling on the number of panels one mesh integration may hold.</summary>
    public const int DefaultMaximumIntervalCount = 16384;

    /// <summary>
    /// Integrates <paramref name="f"/> from <paramref name="a"/> to <paramref name="b"/> over a mesh
    /// that is refined everywhere it is not yet accurate enough, which is the rule the nested verbs
    /// share with MATLAB.
    /// </summary>
    /// <param name="f">The integrand, given a whole mesh of abscissae at once.</param>
    /// <param name="a">The lower limit; may be negative infinity.</param>
    /// <param name="b">The upper limit; may be infinity, and may lie below <paramref name="a"/>.</param>
    /// <param name="absoluteTolerance">The floor under the tolerance, for an answer near zero.</param>
    /// <param name="relativeTolerance">How much of the answer's own size the error may be.</param>
    /// <param name="initialIntervalCount">
    /// How finely the interval is cut before any adaptation: ten for a plain <c>integral</c>, three
    /// for the outer integral of a nested one, where each panel is expensive.
    /// </param>
    /// <param name="waypoints">Places the integrand is not smooth, made panel boundaries.</param>
    /// <param name="maximumIntervalCount">The ceiling on panels.</param>
    public static MeshResult IntegrateOverAMesh(
        Func<double[], double[]> f,
        double a,
        double b,
        double absoluteTolerance = DefaultAbsoluteTolerance,
        double relativeTolerance = DefaultRelativeTolerance,
        int initialIntervalCount = 10,
        IReadOnlyList<double>? waypoints = null,
        int maximumIntervalCount = DefaultMaximumIntervalCount)
    {
        double atol = absoluteTolerance;
        double rtol = relativeTolerance;

        // Pure absolute error control is a legal thing to ask for; anything else is held to at least
        // a hundred ulps, because no mesh can resolve a relative tolerance finer than that.
        if (!(atol > 0.0 && rtol == 0.0) && rtol < 100.0 * Ulp)
        {
            rtol = 100.0 * Ulp;
        }

        bool reversed = b < a;
        double lower = reversed ? b : a;
        double upper = reversed ? a : b;

        var inside = new List<double>();
        if (waypoints is not null)
        {
            foreach (double point in waypoints)
            {
                if (point > lower && point < upper)
                {
                    inside.Add(point);
                }
            }

            inside.Sort();
        }

        MeshResult answer;
        if (double.IsNaN(lower) || double.IsNaN(upper) || lower == upper)
        {
            double middle = (lower + upper) / 2.0;
            double[] value = f([middle]);
            double only = value.Length > 0 ? value[0] : double.NaN;
            double q = (upper - lower) * only;
            answer = new(q, q, double.IsFinite(only) ? Trouble.None : Trouble.NonFiniteValue, middle);
        }
        else if (double.IsFinite(lower) && double.IsFinite(upper))
        {
            var mesh = new List<double> { -1.0 };
            foreach (double point in inside)
            {
                mesh.Add(2.0 * Math.Sin(Math.Asin((lower + upper - (2.0 * point)) / (lower - upper)) / 3.0));
            }

            mesh.Add(1.0);
            answer = OverAMesh(f, BothEndsBent(lower, upper), mesh, atol, rtol,
                initialIntervalCount, maximumIntervalCount);
        }
        else if (double.IsFinite(lower))
        {
            var mesh = new List<double> { 0.0 };
            foreach (double point in inside)
            {
                double alpha = Math.Sqrt(point - lower);
                mesh.Add(alpha / (1.0 + alpha));
            }

            mesh.Add(1.0);
            answer = OverAMesh(f, UpwardsToInfinity(lower), mesh, atol, rtol,
                initialIntervalCount, maximumIntervalCount);
        }
        else if (double.IsFinite(upper))
        {
            var mesh = new List<double> { -1.0 };
            foreach (double point in inside)
            {
                double alpha = Math.Sqrt(upper - point);
                mesh.Add(-alpha / (1.0 + alpha));
            }

            mesh.Add(0.0);
            answer = OverAMesh(f, DownwardsToInfinity(upper), mesh, atol, rtol,
                initialIntervalCount, maximumIntervalCount);
        }
        else
        {
            var mesh = new List<double> { -1.0 };
            foreach (double point in inside)
            {
                mesh.Add(Math.Tanh(Math.Asinh(2.0 * point) / 2.0));
            }

            mesh.Add(1.0);
            answer = OverAMesh(f, TheWholeLine, mesh, atol, rtol,
                initialIntervalCount, maximumIntervalCount);
        }

        return reversed ? answer with { Value = -answer.Value } : answer;
    }

    /// <summary>A change of variable: where a mesh point lands, and the rate the map moves at there.</summary>
    private delegate (double[] At, double[] Rate) MeshTransform(double[] points);

    /// <summary>
    /// The adaptation itself. Every panel is measured, every panel under its share of the tolerance
    /// is retired into a running total, and everything left is halved — so the mesh grows where the
    /// integrand is hard and nowhere else, and one call of the integrand covers a whole sweep.
    /// </summary>
    private static MeshResult OverAMesh(
        Func<double[], double[]> f, MeshTransform transform, List<double> mesh,
        double atol, double rtol, int initialIntervalCount, int maximumIntervalCount)
    {
        double pathLength = mesh[^1] - mesh[0];
        double[] edges = SplitEvenly(mesh, initialIntervalCount, pathLength);
        if (pathLength == 0.0)
        {
            return new(0.0, 0.0, Trouble.None, mesh[0]);
        }

        int ceiling = Math.Max(maximumIntervalCount, 2 * (edges.Length - 1));
        int count = edges.Length - 1;
        var left = new double[count];
        var right = new double[count];
        for (int k = 0; k < count; k++)
        {
            left[k] = edges[k];
            right[k] = edges[k + 1];
        }

        int nodes = Nodes.Length;
        double accurateValue = 0.0;
        double accurateError = 0.0;
        double lastValue = 0.0;
        double lastBound = 0.0;
        bool first = true;
        while (true)
        {
            var middle = new double[count];
            var half = new double[count];
            var at = new double[nodes * count];
            for (int k = 0; k < count; k++)
            {
                middle[k] = (left[k] + right[k]) / 2.0;
                half[k] = (right[k] - left[k]) / 2.0;
                for (int j = 0; j < nodes; j++)
                {
                    at[j + (nodes * k)] = (Nodes[j] * half[k]) + middle[k];
                }
            }

            (double[] mapped, double[] rate) = transform(at);
            if (!first && TooCloseTogether(mapped, out double where))
            {
                // What is reported is the sweep before this one, because this one never happened:
                // the mesh was found unusable before the integrand was asked about it.
                return new(lastValue, lastBound, Trouble.MinimumStepSize, where);
            }

            first = false;
            double[] raw = f(mapped);
            if (raw.Length != mapped.Length)
            {
                throw new ArgumentException(
                    $"the integrand answered {raw.Length} value(s) for {mapped.Length} point(s).");
            }

            var scaled = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                scaled[i] = raw[i] * rate[i];
            }

            var panelValue = new double[count];
            var panelError = new double[count];
            double total = accurateValue;
            for (int k = 0; k < count; k++)
            {
                double kronrod = 0.0;
                double difference = 0.0;
                for (int j = 0; j < nodes; j++)
                {
                    double v = scaled[j + (nodes * k)];
                    kronrod += KronrodWeights[j] * v;
                    difference += (KronrodWeights[j] - GaussWeights[j]) * v;
                }

                panelValue[k] = kronrod * half[k];
                panelError[k] = difference * half[k];
                total += panelValue[k];
            }

            double tolerance = Math.Max(atol, rtol * Math.Abs(total));
            double share = 2.0 * tolerance / pathLength;
            double remainingError = 0.0;
            int kept = 0;
            for (int k = 0; k < count; k++)
            {
                if (Math.Abs(panelError[k]) <= share * Math.Abs(half[k]))
                {
                    accurateError += panelError[k];
                    accurateValue += panelValue[k];
                    continue;
                }

                remainingError += Math.Abs(panelError[k]);
                left[kept] = left[k];
                right[kept] = right[k];
                middle[kept] = middle[k];
                kept++;
            }

            // The retired panels' errors are added with their signs and the live ones by magnitude:
            // a bound built the other way round would let two unfinished panels cancel each other
            // and report an accuracy neither of them has.
            double bound = Math.Abs(accurateError) + remainingError;
            lastValue = total;
            lastBound = bound;
            if (!double.IsFinite(total) || !double.IsFinite(bound))
            {
                return new(total, bound, Trouble.NonFiniteValue, mapped.Length > 0 ? mapped[0] : double.NaN);
            }

            if (bound <= tolerance || kept == 0)
            {
                return new(total, bound, Trouble.None, double.NaN);
            }

            if (2 * kept > ceiling)
            {
                return new(total, bound, Trouble.MaximumIntervalCount, double.NaN);
            }

            var nextLeft = new double[2 * kept];
            var nextRight = new double[2 * kept];
            for (int k = 0; k < kept; k++)
            {
                nextLeft[2 * k] = left[k];
                nextRight[2 * k] = middle[k];
                nextLeft[(2 * k) + 1] = middle[k];
                nextRight[(2 * k) + 1] = right[k];
            }

            left = nextLeft;
            right = nextRight;
            count = 2 * kept;
        }
    }

    /// <summary>
    /// Cuts the starting mesh so no panel is longer than its share of the path, and drops any panel
    /// that has collapsed to nothing — which is what a waypoint sitting on a limit leaves behind.
    /// </summary>
    private static double[] SplitEvenly(List<double> mesh, int minimum, double pathLength)
    {
        var cut = new List<double>(mesh);
        if (minimum > 1 && pathLength > 0.0)
        {
            double perUnit = minimum / pathLength;
            for (int k = cut.Count - 2; k >= 0; k--)
            {
                double from = cut[k];
                double to = cut[k + 1];
                int extra = (int)Math.Ceiling(Math.Abs(to - from) * perUnit) - 1;
                for (int j = extra; j >= 1; j--)
                {
                    cut.Insert(k + 1, from + ((double)j / (extra + 1) * (to - from)));
                }
            }
        }

        if (minimum > 1)
        {
            for (int k = cut.Count - 2; k >= 0; k--)
            {
                if (cut[k + 1] - cut[k] == 0.0)
                {
                    cut.RemoveAt(k);
                }
            }

            if (cut.Count == 1)
            {
                cut.Add(cut[0]);
            }
        }

        return cut.ToArray();
    }

    /// <summary>
    /// Whether two neighbouring abscissae have come within a hundred ulps of each other, which says
    /// the mesh has run out of room before the integrand ran out of difficulty.
    /// </summary>
    private static bool TooCloseTogether(double[] points, out double where)
    {
        for (int i = 0; i + 1 < points.Length; i++)
        {
            double biggest = Math.Max(Math.Abs(points[i]), Math.Abs(points[i + 1]));
            if (Math.Abs(points[i + 1] - points[i]) <= 100.0 * Ulp * biggest)
            {
                where = points[i];
                return true;
            }
        }

        where = double.NaN;
        return false;
    }

    /// <summary>
    /// The cubic that maps [-1, 1] onto [a, b] with a vanishing derivative at both ends, written the
    /// way MATLAB writes it: the plain form <c>(b-a)(3t - t³)/4</c> away from the ends, and the
    /// offset form <c>(b-a)(|t|-1)²(|t|+2)/4</c> measured from the near end within a quarter of it.
    /// The two are the same polynomial; the second is the one that keeps its figures where the first
    /// subtracts two nearly equal cubes.
    /// </summary>
    private static MeshTransform BothEndsBent(double a, double b) => points =>
    {
        var at = new double[points.Length];
        var rate = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double t = points[i];
            double shifted = Math.Abs(t) - 1.0;
            double offset = 0.25 * (b - a) * shifted * shifted * (shifted + 3.0);
            at[i] = t < -0.25 ? a + offset
                : t > 0.25 ? b - offset
                : (0.25 * (b - a) * t * (3.0 - (t * t))) + (0.5 * (b + a));
            rate[i] = Math.Abs(t) > 0.25
                ? -0.75 * (b - a) * shifted * (shifted + 2.0)
                : 0.75 * (b - a) * (1.0 - (t * t));
        }

        return (at, rate);
    };

    /// <summary>
    /// [a, ∞) read over [0, 1): squared first, which weakens a singularity at the finite end, and
    /// then folded, which brings the infinite one in.
    /// </summary>
    private static MeshTransform UpwardsToInfinity(double a) => points =>
    {
        var at = new double[points.Length];
        var rate = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double t = points[i];
            double u = t / (1.0 - t);
            at[i] = a + (u * u);
            rate[i] = 2.0 * u / ((1.0 - t) * (1.0 - t));
        }

        return (at, rate);
    };

    /// <summary>(-∞, b] read over (-1, 0], the mirror of <see cref="UpwardsToInfinity"/>.</summary>
    private static MeshTransform DownwardsToInfinity(double b) => points =>
    {
        var at = new double[points.Length];
        var rate = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double t = points[i];
            double u = t / (1.0 + t);
            at[i] = b - (u * u);
            rate[i] = -2.0 * u / ((1.0 + t) * (1.0 + t));
        }

        return (at, rate);
    };

    /// <summary>The whole line read over (-1, 1).</summary>
    private static (double[] At, double[] Rate) TheWholeLine(double[] points)
    {
        var at = new double[points.Length];
        var rate = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double t = points[i];
            double denominator = 1.0 - (t * t);
            at[i] = t / denominator;
            rate[i] = (1.0 + (t * t)) / (denominator * denominator);
        }

        return (at, rate);
    }
}
