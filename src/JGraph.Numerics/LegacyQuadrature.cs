using System;

namespace JGraph.Numerics;

/// <summary>
/// The quadrature MATLAB shipped before <c>integral</c>: recursive adaptive Simpson (<c>quad</c>),
/// recursive adaptive Lobatto–Kronrod (<c>quadl</c>), and the array-valued Simpson (<c>quadv</c>).
/// </summary>
/// <remarks>
/// <para>
/// These are kept because scripts still call them and because their answers are not the same as
/// <c>integral</c>'s: each asks for an <em>absolute</em> tolerance on every subinterval separately,
/// which is a local test rather than a global one, and each counts its own evaluations and hands the
/// count back. A caller who asks for <c>fcnt</c> is asking about the method, so the method has to be
/// the one MATLAB implements — Gander and Gautschi's <c>adaptsim</c> and <c>adaptlob</c>, down to
/// the 0.13579 that puts the three starting subintervals off centre.
/// </para>
/// <para>
/// That constant is not decoration. Starting from equal subintervals makes the abscissae of a
/// symmetric integrand coincide with its own symmetry, so a rule can be exact on a function it has
/// barely looked at; an ugly fraction breaks the coincidence, and the error estimate goes back to
/// measuring the integrand instead of the arithmetic.
/// </para>
/// </remarks>
public static class LegacyQuadrature
{
    /// <summary>What a recursive integration ran into, in MATLAB's own order of severity.</summary>
    public enum Trouble
    {
        /// <summary>Nothing: every subinterval met the tolerance.</summary>
        None = 0,

        /// <summary>A subinterval shrank to the smallest step there is with the tolerance unmet.</summary>
        MinimumStepSize = 1,

        /// <summary>Ten thousand evaluations were spent, which says a singularity is likely.</summary>
        MaximumFunctionCount = 2,

        /// <summary>The estimate over some subinterval stopped being a number.</summary>
        ImproperFunctionValue = 3,
    }

    /// <summary>What one of these answered: the integral, the evaluation count, and any trouble.</summary>
    public readonly record struct Result(double Value, int FunctionCount, Trouble Trouble);

    /// <summary>The same for an array-valued integrand.</summary>
    public readonly record struct ArrayResult(double[] Value, int FunctionCount, Trouble Trouble);

    /// <summary>The default absolute tolerance the three of them share.</summary>
    public const double DefaultTolerance = 1e-6;

    /// <summary>The ceiling on evaluations, past which a singularity is assumed.</summary>
    private const int MaximumFunctionCount = 10000;

    /// <summary>The spacing of doubles near one, which is what MATLAB's endpoint nudge is measured in.</summary>
    private const double Ulp = 2.220446049250313e-16;

    /// <summary>The smallest normal double, added to a denominator so a ratio of zeros is not one.</summary>
    private const double SmallestNormal = 2.2250738585072014e-308;

    /// <summary>What a trace prints per recursion: the count so far, the left end, the width, the value.</summary>
    public delegate void Trace(int count, double from, double width, double value);

    /// <summary>The spacing of doubles at <paramref name="x"/>, which is MATLAB's <c>eps(x)</c>.</summary>
    public static double Eps(double x)
    {
        double magnitude = Math.Abs(x);
        if (double.IsNaN(magnitude) || double.IsInfinity(magnitude))
        {
            return double.NaN;
        }

        return magnitude == 0.0 ? double.Epsilon : Math.BitIncrement(magnitude) - magnitude;
    }

    /// <summary>
    /// <c>quad</c>: adaptive Simpson with one step of Romberg extrapolation, over three unequal
    /// starting subintervals.
    /// </summary>
    public static Result AdaptiveSimpson(
        Func<double[], double[]> f, double a, double b, double tolerance, Trace? trace = null)
    {
        double h = 0.13579 * (b - a);
        double[] x = [a, a + h, a + (2.0 * h), (a + b) / 2.0, b - (2.0 * h), b - h, b];
        double[] y = f(x);
        int count = 7;
        if (y.Length != 7)
        {
            throw new ArgumentException("notVectorized");
        }

        // An infinite value at an endpoint is nudged inwards by one ulp of the interval rather than
        // being allowed to poison the whole Simpson sum: an integrable singularity on a limit is a
        // thing scripts do on purpose.
        if (!double.IsFinite(y[0]))
        {
            y[0] = f([a + (Ulp * (b - a))])[0];
            count++;
        }

        if (!double.IsFinite(y[6]))
        {
            y[6] = f([b - (Ulp * (b - a))])[0];
            count++;
        }

        double smallest = Eps(b - a) / 1024.0;
        var run = new SimpsonRun(f, tolerance, trace, smallest, count);
        double q = run.Step(x[0], x[2], y[0], y[1], y[2])
            + run.Step(x[2], x[4], y[2], y[3], y[4])
            + run.Step(x[4], x[6], y[4], y[5], y[6]);
        return new(q, run.Count, run.Worst);
    }

    /// <summary>
    /// <c>quadv</c>: the same Simpson recursion over an integrand that answers an array for one
    /// scalar abscissa, with the one tolerance applied to the largest component.
    /// </summary>
    public static ArrayResult AdaptiveSimpsonOfAnArray(
        Func<double, double[]> f, double a, double b, double tolerance, Trace? trace = null)
    {
        double h = 0.13579 * (b - a);
        double[] x = [a, a + h, a + (2.0 * h), (a + b) / 2.0, b - (2.0 * h), b - h, b];
        var y = new double[7][];
        for (int j = 0; j < 7; j++)
        {
            y[j] = f(x[j]);
        }

        int count = 7;
        if (!AllFinite(y[0]))
        {
            y[0] = f(a + (Ulp * (b - a)));
            count++;
        }

        if (!AllFinite(y[6]))
        {
            y[6] = f(b - (Ulp * (b - a)));
            count++;
        }

        double smallest = Eps(b - a) / 1024.0;
        var run = new ArraySimpsonRun(f, tolerance, trace, smallest, count);
        double[] q = Add(
            Add(run.Step(x[0], x[2], y[0], y[1], y[2]), run.Step(x[2], x[4], y[2], y[3], y[4])),
            run.Step(x[4], x[6], y[4], y[5], y[6]));
        return new(q, run.Count, run.Worst);
    }

    /// <summary>
    /// <c>quadl</c>: a four-point Lobatto rule refined by a seven-point Kronrod rule over the same
    /// abscissae, recursing into six subintervals at a time rather than two.
    /// </summary>
    /// <remarks>
    /// The tolerance is loosened before the recursion starts, by a factor read off the integrand
    /// itself: if the thirteen-point rule and the seven-point rule agree much better than the
    /// seven-point and four-point rules did, refinement is working, and asking for the stated
    /// tolerance on every subinterval would then buy far more accuracy than was asked for.
    /// </remarks>
    public static Result AdaptiveLobatto(
        Func<double[], double[]> f, double a, double b, double tolerance, Trace? trace = null)
    {
        double c = (a + b) / 2.0;
        double h = (b - a) / 2.0;
        double[] s = [0.942882415695480, Math.Sqrt(2.0 / 3.0), 0.641853342345781,
            1.0 / Math.Sqrt(5.0), 0.236383199662150];
        double[] x =
        [
            a,
            c - (h * s[0]), c - (h * s[1]), c - (h * s[2]), c - (h * s[3]), c - (h * s[4]),
            c,
            c + (h * s[4]), c + (h * s[3]), c + (h * s[2]), c + (h * s[1]), c + (h * s[0]),
            b,
        ];
        double[] y = f(x);
        int count = 13;
        if (y.Length != 13)
        {
            throw new ArgumentException("notVectorized");
        }

        if (!double.IsFinite(y[0]))
        {
            y[0] = f([a + (Ulp * (b - a))])[0];
            count++;
        }

        if (!double.IsFinite(y[12]))
        {
            y[12] = f([b - (Ulp * (b - a))])[0];
            count++;
        }

        double q1 = h / 6.0 * ((1.0 * y[0]) + (5.0 * y[4]) + (5.0 * y[8]) + (1.0 * y[12]));
        double q2 = h / 1470.0 * ((77.0 * y[0]) + (432.0 * y[2]) + (625.0 * y[4]) + (672.0 * y[6])
            + (625.0 * y[8]) + (432.0 * y[10]) + (77.0 * y[12]));
        double[] inner = [0.0158271919734802, 0.094273840218850, 0.155071987336585,
            0.188821573960182, 0.199773405226859, 0.224926465333340];
        double[] w =
        [
            inner[0], inner[1], inner[2], inner[3], inner[4], inner[5],
            0.242611071901408,
            inner[5], inner[4], inner[3], inner[2], inner[1], inner[0],
        ];
        double q0 = 0.0;
        for (int j = 0; j < 13; j++)
        {
            q0 += w[j] * y[j];
        }

        q0 *= h;
        double ratio = Math.Abs(q2 - q0) / Math.Abs(q1 - q0 + SmallestNormal);
        double loosened = ratio > 0.0 && ratio < 1.0 ? tolerance / ratio : tolerance;
        double smallest = Eps(b - a) / 1024.0;
        var run = new LobattoRun(f, loosened, trace, smallest, count);
        double q = run.Step(a, b, y[0], y[12]);
        return new(q, run.Count, run.Worst);
    }

    private static bool AllFinite(double[] values)
    {
        foreach (double value in values)
        {
            if (!double.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    private static double[] Add(double[] left, double[] right)
    {
        var sum = new double[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            sum[i] = left[i] + right[i];
        }

        return sum;
    }

    /// <summary>The state one <c>quad</c> recursion carries: the count so far and the worst news.</summary>
    private sealed class SimpsonRun(
        Func<double[], double[]> f, double tolerance, Trace? trace, double smallest, int count)
    {
        internal int Count { get; private set; } = count;

        internal Trouble Worst { get; private set; } = Trouble.None;

        internal double Step(double a, double b, double fa, double fc, double fb)
        {
            double h = b - a;
            double c = (a + b) / 2.0;
            double[] y = f([(a + c) / 2.0, (c + b) / 2.0]);
            Count += 2;
            double fd = y[0];
            double fe = y[1];
            double q1 = h / 6.0 * (fa + (4.0 * fc) + fb);
            double q2 = h / 12.0 * (fa + (4.0 * fd) + (2.0 * fc) + (4.0 * fe) + fb);
            double q = q2 + ((q2 - q1) / 15.0);
            trace?.Invoke(Count, a, h, q);
            if (!double.IsFinite(q))
            {
                Note(Trouble.ImproperFunctionValue);
                return q;
            }

            if (Count > MaximumFunctionCount)
            {
                Note(Trouble.MaximumFunctionCount);
                return q;
            }

            if (Math.Abs(q2 - q) <= tolerance)
            {
                return q;
            }

            if (Math.Abs(h) < smallest || c == a || c == b)
            {
                Note(Trouble.MinimumStepSize);
                return q;
            }

            return Step(a, c, fa, fd, fc) + Step(c, b, fc, fe, fb);
        }

        private void Note(Trouble trouble)
        {
            if (trouble > Worst)
            {
                Worst = trouble;
            }
        }
    }

    /// <summary>The same recursion over an array-valued integrand.</summary>
    private sealed class ArraySimpsonRun(
        Func<double, double[]> f, double tolerance, Trace? trace, double smallest, int count)
    {
        internal int Count { get; private set; } = count;

        internal Trouble Worst { get; private set; } = Trouble.None;

        internal double[] Step(double a, double b, double[] fa, double[] fc, double[] fb)
        {
            double h = b - a;
            double c = (a + b) / 2.0;
            double[] fd = f((a + c) / 2.0);
            double[] fe = f((c + b) / 2.0);
            Count += 2;
            var q = new double[fa.Length];
            double worstDifference = 0.0;
            for (int i = 0; i < fa.Length; i++)
            {
                double q1 = h / 6.0 * (fa[i] + (4.0 * fc[i]) + fb[i]);
                double q2 = h / 12.0 * (fa[i] + (4.0 * fd[i]) + (2.0 * fc[i]) + (4.0 * fe[i]) + fb[i]);
                q[i] = q2 + ((q2 - q1) / 15.0);
                worstDifference = Math.Max(worstDifference, Math.Abs(q2 - q[i]));
            }

            trace?.Invoke(Count, a, h, q.Length > 0 ? q[0] : double.NaN);
            if (!AllFinite(q))
            {
                Note(Trouble.ImproperFunctionValue);
                return q;
            }

            if (Count > MaximumFunctionCount)
            {
                Note(Trouble.MaximumFunctionCount);
                return q;
            }

            if (worstDifference <= tolerance)
            {
                return q;
            }

            if (Math.Abs(h) < smallest || c == a || c == b)
            {
                Note(Trouble.MinimumStepSize);
                return q;
            }

            return Add(Step(a, c, fa, fd, fc), Step(c, b, fc, fe, fb));
        }

        private void Note(Trouble trouble)
        {
            if (trouble > Worst)
            {
                Worst = trouble;
            }
        }
    }

    /// <summary>The state one <c>quadl</c> recursion carries.</summary>
    private sealed class LobattoRun(
        Func<double[], double[]> f, double tolerance, Trace? trace, double smallest, int count)
    {
        private static readonly double Alpha = Math.Sqrt(2.0 / 3.0);
        private static readonly double Beta = 1.0 / Math.Sqrt(5.0);

        internal int Count { get; private set; } = count;

        internal Trouble Worst { get; private set; } = Trouble.None;

        internal double Step(double a, double b, double fa, double fb)
        {
            double c = (a + b) / 2.0;
            double h = (b - a) / 2.0;
            if (Math.Abs(h) < smallest || c == a || c == b)
            {
                Note(Trouble.MinimumStepSize);
                return h * (fa + fb);
            }

            double[] interior = f([
                c - (Alpha * h), c - (Beta * h), c, c + (Beta * h), c + (Alpha * h)]);
            Count += 5;
            if (Count > MaximumFunctionCount)
            {
                Note(Trouble.MaximumFunctionCount);
                return h * (fa + fb);
            }

            double[] x = [a, c - (Alpha * h), c - (Beta * h), c, c + (Beta * h), c + (Alpha * h), b];
            double[] y = [fa, interior[0], interior[1], interior[2], interior[3], interior[4], fb];
            double q1 = h / 6.0 * (y[0] + (5.0 * y[2]) + (5.0 * y[4]) + y[6]);
            double q2 = h / 1470.0 * ((77.0 * y[0]) + (432.0 * y[1]) + (625.0 * y[2]) + (672.0 * y[3])
                + (625.0 * y[4]) + (432.0 * y[5]) + (77.0 * y[6]));
            if (!double.IsFinite(q2))
            {
                Note(Trouble.ImproperFunctionValue);
                return q2;
            }

            trace?.Invoke(Count, a, h, q2);
            if (Math.Abs(q1 - q2) <= tolerance)
            {
                return q2;
            }

            double q = 0.0;
            for (int k = 0; k < 6; k++)
            {
                q += Step(x[k], x[k + 1], y[k], y[k + 1]);
            }

            return q;
        }

        private void Note(Trouble trouble)
        {
            if (trouble > Worst)
            {
                Worst = trouble;
            }
        }
    }
}
