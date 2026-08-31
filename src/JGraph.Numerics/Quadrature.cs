using System;
using System.Collections.Generic;

namespace JGraph.Numerics;

/// <summary>
/// Adaptive numerical integration by the Gauss–Kronrod (7, 15) pair: the engine behind both
/// <c>integral</c> and <c>quadgk</c>, as it is in MATLAB, where the two names are one method with
/// two interfaces.
/// </summary>
/// <remarks>
/// <para>
/// Three things make this answer integrals a plain rule cannot. The <em>pair</em> is two rules over
/// the same fifteen points — a fifteen-point Kronrod estimate and the seven-point Gauss estimate
/// nested inside it — so an error estimate costs no extra evaluations. The <em>adaptation</em>
/// always splits whichever panel currently carries the most of that error, so effort follows the
/// difficulty rather than being spread evenly. And the <em>transform</em> below bends the panel so
/// its ends are approached and never reached, which is what lets an integrable singularity sit on
/// a limit: <c>1/√x</c> over [0, 1] is answered as 2 without <c>1/0</c> ever being formed.
/// </para>
/// <para>
/// The nodes and weights were derived rather than transcribed — Laurie's algorithm on the Legendre
/// recurrence, then the symmetric eigenproblem — and then checked the way a quadrature rule can
/// check itself: the Kronrod rule integrates every polynomial up to degree 22 exactly and fails at
/// 24, and the Gauss rule inside it is exact to degree 13. A mistyped node fails that at low degree
/// and loudly, which a table copied out of a book does not.
/// </para>
/// </remarks>
public static class Quadrature
{
    /// <summary>What an integration answered: its value, the bound on its error, and whether it met the tolerance.</summary>
    public readonly record struct Result(double Value, double ErrorBound, bool Converged);

    /// <summary>MATLAB's default relative tolerance.</summary>
    public const double DefaultRelativeTolerance = 1e-6;

    /// <summary>MATLAB's default absolute tolerance.</summary>
    public const double DefaultAbsoluteTolerance = 1e-10;

    /// <summary>MATLAB's default ceiling on how many panels a single integration may use.</summary>
    public const int DefaultMaximumIntervals = 650;

    /// <summary>How many panels the mesh starts with before any adaptation, as MATLAB's does.</summary>
    private const int InitialPanels = 10;

    /// <summary>The spacing of doubles near one, which is the finest an error estimate can mean.</summary>
    private const double Ulp = 2.220446049250313e-16;

    private static readonly double[] Nodes =
    [
        -9.91455371120812501e-01, -9.49107912342758486e-01, -8.64864423359769097e-01,
        -7.41531185599394460e-01, -5.86087235467691148e-01, -4.05845151377397184e-01,
        -2.07784955007898398e-01, 0.00000000000000000e+00, 2.07784955007898398e-01,
        4.05845151377397184e-01, 5.86087235467691148e-01, 7.41531185599394460e-01,
        8.64864423359769097e-01, 9.49107912342758486e-01, 9.91455371120812501e-01,
    ];

    private static readonly double[] KronrodWeights =
    [
        2.29353220105290578e-02, 6.30920926299787937e-02, 1.04790010322250049e-01,
        1.40653259715525725e-01, 1.69004726639267910e-01, 1.90350578064785753e-01,
        2.04432940075298386e-01, 2.09482141084727208e-01, 2.04432940075298386e-01,
        1.90350578064785753e-01, 1.69004726639267910e-01, 1.40653259715525725e-01,
        1.04790010322250049e-01, 6.30920926299787937e-02, 2.29353220105290578e-02,
    ];

    /// <summary>
    /// The seven-point Gauss weights, laid out over all fifteen nodes so the two estimates are one
    /// pass over one array of values. The eight zeros are the Kronrod-only nodes.
    /// </summary>
    private static readonly double[] GaussWeights =
    [
        0.00000000000000000e+00, 1.29484966168869731e-01, 0.00000000000000000e+00,
        2.79705391489276867e-01, 0.00000000000000000e+00, 3.81830050505118701e-01,
        0.00000000000000000e+00, 4.17959183673469292e-01, 0.00000000000000000e+00,
        3.81830050505118701e-01, 0.00000000000000000e+00, 2.79705391489276867e-01,
        0.00000000000000000e+00, 1.29484966168869731e-01, 0.00000000000000000e+00,
    ];

    /// <summary>How many points one panel costs.</summary>
    public static int PointsPerPanel => Nodes.Length;

    /// <summary>
    /// One panel of the integration: where it is, what it came to, how wrong that may be, and
    /// whether there is any point splitting it again.
    /// </summary>
    private readonly record struct Panel(double From, double To, double Value, double Error, bool Splittable);

    /// <summary>
    /// Integrates <paramref name="f"/> from <paramref name="a"/> to <paramref name="b"/>. Either
    /// limit may be infinite, and <paramref name="waypoints"/> names places the integrand is not
    /// smooth so that each is made a panel boundary rather than something the adaptation has to
    /// discover.
    /// </summary>
    /// <param name="f">
    /// The integrand, given every abscissa of a panel at once and answering a value for each — which
    /// is MATLAB's contract for an integrand too, and the reason fifteen points cost one call.
    /// </param>
    /// <param name="a">The lower limit; may be negative infinity.</param>
    /// <param name="b">The upper limit; may be infinity, and may lie below <paramref name="a"/>.</param>
    /// <param name="relativeTolerance">How much of the answer's own size the error may be.</param>
    /// <param name="absoluteTolerance">The floor under that, for an answer near zero.</param>
    /// <param name="waypoints">Places the integrand is not smooth, made panel boundaries.</param>
    /// <param name="maximumIntervals">The ceiling on panels, past which the best estimate is returned unconverged.</param>
    public static Result Integrate(
        Func<double[], double[]> f,
        double a,
        double b,
        double relativeTolerance = DefaultRelativeTolerance,
        double absoluteTolerance = DefaultAbsoluteTolerance,
        IReadOnlyList<double>? waypoints = null,
        int maximumIntervals = DefaultMaximumIntervals)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return new(double.NaN, double.NaN, false);
        }

        if (a == b)
        {
            return new(0.0, 0.0, true);
        }

        // Backwards is forwards negated. Done here rather than inside, so nothing below has to
        // carry a sign, and so an infinite limit is only ever the upper one.
        if (b < a)
        {
            Result flipped = Integrate(f, b, a, relativeTolerance, absoluteTolerance, waypoints, maximumIntervals);
            return flipped with { Value = -flipped.Value };
        }

        Func<double[], double[]> integrand = f;
        double from = a;
        double to = b;
        if (double.IsInfinity(a) || double.IsInfinity(b))
        {
            (integrand, from, to) = OverAnInfiniteRange(f, a, b);
        }

        var edges = new List<double> { from };
        if (waypoints is not null && !double.IsInfinity(a) && !double.IsInfinity(b))
        {
            // A waypoint outside the interval is not a boundary of it; MATLAB ignores those rather
            // than folding the interval back on itself.
            var inside = new List<double>();
            foreach (double point in waypoints)
            {
                if (point > from && point < to)
                {
                    inside.Add(point);
                }
            }

            inside.Sort();
            edges.AddRange(inside);
        }

        edges.Add(to);

        double total = 0.0;
        double bound = 0.0;
        bool converged = true;
        int share = Math.Max(2, maximumIntervals / Math.Max(1, edges.Count - 1));
        for (int i = 0; i + 1 < edges.Count; i++)
        {
            if (edges[i] >= edges[i + 1])
            {
                continue;
            }

            // The endpoint transform is applied once per stretch and the adaptation then works in
            // the transformed variable. Applying it again to every panel would bend an integrand
            // the rule was about to handle exactly, which costs both accuracy and panels.
            Result piece = OverOneSmoothStretch(
                Folded(integrand, Bent(edges[i], edges[i + 1]), Rate(edges[i], edges[i + 1])),
                -1.0, 1.0, relativeTolerance, absoluteTolerance, share);
            total += piece.Value;
            bound += piece.ErrorBound;
            converged &= piece.Converged;
        }

        return new(total, bound, converged);
    }

    /// <summary>
    /// The adaptation: start with one panel, and keep splitting whichever panel holds the most error
    /// until what is left is under the tolerance, or until there are no more panels to spend.
    /// </summary>
    private static Result OverOneSmoothStretch(
        Func<double[], double[]> f, double a, double b, double relative, double absolute, int maximumPanels)
    {
        // Ten panels to begin with, as MATLAB's quadgk does, rather than one. Two reasons, and the
        // second is the one that matters: a single fifteen-point panel over a wide interval can
        // step straight over a narrow feature and answer confidently about an integral it never
        // saw, and its error estimate would agree with it. Ten costs 150 evaluations.
        int seed = Math.Min(InitialPanels, Math.Max(1, maximumPanels));
        var panels = new List<Panel>(seed);
        double total = 0.0;
        double bound = 0.0;
        for (int i = 0; i < seed; i++)
        {
            double lo = a + ((b - a) * i / seed);
            double hi = i + 1 == seed ? b : a + ((b - a) * (i + 1) / seed);
            Panel panel = Measure(f, lo, hi);
            panels.Add(panel);
            total += panel.Value;
            bound += panel.Error;
        }

        while (bound > Math.Max(absolute, relative * Math.Abs(total)))
        {
            if (panels.Count + 1 > maximumPanels || !double.IsFinite(total))
            {
                return new(total, bound, false);
            }

            int worst = -1;
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i].Splittable && (worst < 0 || panels[i].Error > panels[worst].Error))
                {
                    worst = i;
                }
            }

            if (worst < 0)
            {
                // Every panel that could be split has been. The bound is what it is.
                return new(total, bound, false);
            }

            Panel split = panels[worst];
            double middle = split.From + ((split.To - split.From) / 2.0);
            if (!(middle > split.From && middle < split.To))
            {
                // The panel is two adjacent doubles wide: there is nowhere left to split.
                panels[worst] = split with { Splittable = false };
                continue;
            }

            Panel left = Measure(f, split.From, middle);
            Panel right = Measure(f, middle, split.To);
            if (!double.IsFinite(left.Value) || !double.IsFinite(right.Value)
                || !double.IsFinite(left.Error) || !double.IsFinite(right.Error))
            {
                // A split that produced something no longer representable has told us nothing. Keep
                // the parent's estimate and stop trying to improve it — otherwise an overflow at the
                // five-hundredth bisection replaces an answer that thirty bisections had right.
                panels[worst] = split with { Splittable = false };
                continue;
            }

            panels[worst] = left;
            panels.Add(right);

            // Kept as a running correction rather than re-summed: the sum of a few hundred panels
            // re-added every round is most of the work, and the two panels that changed are the
            // only ones that can move it.
            total += left.Value + right.Value - split.Value;
            bound += left.Error + right.Error - split.Error;
        }

        return new(total, bound, true);
    }

    /// <summary>
    /// One panel, measured by both rules of the pair at once. The nodes sit where the rule puts
    /// them and nowhere else: any bending of the interval has already been done, once, around the
    /// whole stretch.
    /// </summary>
    private static Panel Measure(Func<double[], double[]> f, double a, double b)
    {
        double half = (b - a) / 2.0;
        double middle = (b + a) / 2.0;
        var at = new double[Nodes.Length];
        for (int i = 0; i < Nodes.Length; i++)
        {
            at[i] = middle + (half * Nodes[i]);
        }

        double[] values = f(at);
        if (values.Length != Nodes.Length)
        {
            throw new ArgumentException(
                $"the integrand answered {values.Length} value(s) for {Nodes.Length} point(s).");
        }

        double kronrod = 0.0;
        double gauss = 0.0;
        for (int i = 0; i < Nodes.Length; i++)
        {
            kronrod += KronrodWeights[i] * values[i];
            gauss += GaussWeights[i] * values[i];
        }

        double value = half * kronrod;
        double raw = Math.Abs(half * (kronrod - gauss));

        // QUADPACK's scaling, and it earns its keep on a corner. Where the integrand has a kink the
        // two rules are wrong in the same direction, so their difference understates the error and
        // the adaptation stops too early — |sin(x)| over [0, 10] came out 2.5e-6 wrong against a
        // 1e-6 contract on the raw difference and 2.8e-9 with this. Scaling by how much the
        // integrand actually varies over the panel is what tells a kink from a straight line.
        double average = kronrod / 2.0;
        double variation = 0.0;
        double magnitude = 0.0;
        for (int i = 0; i < Nodes.Length; i++)
        {
            variation += KronrodWeights[i] * Math.Abs(values[i] - average);
            magnitude += KronrodWeights[i] * Math.Abs(values[i]);
        }

        variation *= Math.Abs(half);
        magnitude *= Math.Abs(half);

        double error = raw;
        if (variation != 0.0 && error != 0.0)
        {
            error = variation * Math.Min(1.0, Math.Pow(200.0 * error / variation, 1.5));
        }

        // No estimate may claim to be finer than the rounding the sum itself carries.
        double floorForRounding = 50.0 * Ulp * magnitude;
        if (magnitude > double.Epsilon / Math.Max(double.Epsilon, floorForRounding))
        {
            error = Math.Max(floorForRounding, error);
        }

        return new(a, b, value, error, true);
    }

    /// <summary>
    /// MATLAB's endpoint transform, mapping [-1, 1] onto [a, b] with a derivative that vanishes at
    /// both ends — which is what lets an integrable singularity sit on a limit and never be
    /// evaluated. It is a cubic, so it costs the rule nothing on a smooth integrand.
    /// </summary>
    private static Func<double, double> Bent(double a, double b)
    {
        double half = (b - a) / 2.0;
        double middle = (b + a) / 2.0;
        return t => (half * (3.0 - (t * t)) * t / 2.0) + middle;
    }

    /// <summary>The rate <see cref="Bent"/> moves at, which is what the integrand is multiplied by.</summary>
    private static Func<double, double> Rate(double a, double b)
    {
        double half = (b - a) / 2.0;
        return t => 3.0 * half * (1.0 - (t * t)) / 2.0;
    }

    /// <summary>
    /// An infinite range folded onto a finite one, and the integrand multiplied by the derivative
    /// that fold costs. Each of the three cases maps the far end to 1, where the panel transform
    /// above then declines to evaluate it.
    /// </summary>
    private static (Func<double[], double[]> F, double From, double To) OverAnInfiniteRange(
        Func<double[], double[]> f, double a, double b)
    {
        if (double.IsInfinity(a) && double.IsInfinity(b))
        {
            return (Folded(f, static t => t / (1.0 - (t * t)),
                static t => (1.0 + (t * t)) / Squared(1.0 - (t * t))), -1.0, 1.0);
        }

        if (double.IsInfinity(b))
        {
            double start = a;
            return (Folded(f, t => start + ((t * t) / (1.0 - (t * t))),
                static t => 2.0 * t / Squared(1.0 - (t * t))), 0.0, 1.0);
        }

        // Downwards from b to minus infinity as t runs from 0 to 1. The substitution reverses the
        // limits and its derivative is negative; the two sign changes cancel, which is why this
        // arm carries no minus sign of its own.
        double end = b;
        return (Folded(f, t => end - ((t * t) / (1.0 - (t * t))),
            static t => 2.0 * t / Squared(1.0 - (t * t))), 0.0, 1.0);
    }

    private static double Squared(double value) => value * value;

    /// <summary>An integrand read through a change of variable.</summary>
    private static Func<double[], double[]> Folded(
        Func<double[], double[]> f, Func<double, double> where, Func<double, double> rate) =>
        points =>
        {
            var mapped = new double[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                mapped[i] = where(points[i]);
            }

            double[] values = f(mapped);
            var scaled = new double[values.Length];
            for (int i = 0; i < values.Length && i < points.Length; i++)
            {
                scaled[i] = values[i] * rate(points[i]);
            }

            return scaled;
        };
}
