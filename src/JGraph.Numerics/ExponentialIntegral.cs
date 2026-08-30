using System.Numerics;

namespace JGraph.Numerics;

/// <summary>
/// The exponential integral E₁(z), over the whole complex plane.
/// </summary>
/// <remarks>
/// <para>
/// Which of the two expansions an argument is given to is decided by a <em>curve</em>, not by a
/// magnitude: an eighth-degree polynomial in the real part is compared against the size of the
/// imaginary part, and the argument takes the power series when it lies under that curve and the
/// continued fraction when it lies above. The curve dips below zero at around 2.6 on the real axis,
/// which is what sends every real argument past that point to the continued fraction while the
/// whole negative real axis stays with the series.
/// </para>
/// <para>
/// Two consequences fall out of that, and both are MATLAB's answers rather than accidents here.
/// A negative real argument is served by the series, whose logarithm leaves the reals, so
/// <c>expint(-1)</c> is complex; and NaN satisfies neither comparison, so it falls between the two
/// branches and comes back as the zero the answer was initialised to rather than as NaN.
/// </para>
/// <para>
/// Both expansions run over the whole array with one stopping test, for the reason
/// <see cref="EllipticFunctions"/> gives: a settled element keeps receiving terms while its
/// neighbours are still moving.
/// </para>
/// </remarks>
public static class ExponentialIntegral
{
    /// <summary>Euler's constant, to the digits MATLAB's own routine writes out.</summary>
    private const double EulerGamma = 0.57721566490153286061;

    /// <summary>
    /// The coefficients of the curve dividing the two expansions, highest power first.
    /// </summary>
    private static readonly double[] Divide =
    [
        -3.602693626336023e-09, -4.819538452140960e-07, -2.569498322115933e-05,
        -6.973790859534190e-04, -1.019573529845792e-02, -7.811863559248197e-02,
        -3.012432892762715e-01, -7.773807325735529e-01, 8.267661952366478e+00,
    ];

    /// <summary>Evaluates E₁ at every point of <paramref name="x"/>.</summary>
    /// <param name="x">The arguments. Not modified.</param>
    /// <returns>The values, element for element.</returns>
    public static Complex[] E1(Complex[] x)
    {
        var answer = new Complex[x.Length];
        var series = new List<int>();
        var fraction = new List<int>();
        for (int i = 0; i < x.Length; i++)
        {
            double boundary = Curve(x[i].Real);
            double height = Math.Abs(x[i].Imaginary);

            // Neither branch: NaN fails both comparisons and keeps the zero it was born with.
            if (height <= boundary)
            {
                series.Add(i);
            }
            else if (height > boundary)
            {
                fraction.Add(i);
            }
        }

        if (series.Count > 0)
        {
            Series(x, series, answer);
        }

        if (fraction.Count > 0)
        {
            ContinuedFraction(x, fraction, answer);
        }

        return answer;
    }

    /// <summary>
    /// The dividing curve, evaluated in the real part. Seeded with the leading coefficient rather
    /// than with nought, because at an infinite argument the extra multiply that a nought seed adds
    /// is <c>0 · ∞</c> — which would make the curve NaN there, put an infinity in neither branch,
    /// and answer nought for <c>expint(Inf)</c> where MATLAB answers NaN.
    /// </summary>
    private static double Curve(double real)
    {
        double at = Divide[0];
        for (int i = 1; i < Divide.Length; i++)
        {
            at = (at * real) + Divide[i];
        }

        return at;
    }

    /// <summary>
    /// The power series −γ − log z + Σ (−1)ⁿ⁺¹ zⁿ / (n · n!), which converges for every z but only
    /// usefully where the curve says so.
    /// </summary>
    private static void Series(Complex[] x, List<int> at, Complex[] answer)
    {
        int count = at.Count;
        var value = new Complex[count];
        var running = new Complex[count];
        var term = new Complex[count];
        for (int k = 0; k < count; k++)
        {
            Complex z = x[at[k]];
            value[k] = -EulerGamma - Complex.Log(z);
            running[k] = z;
            term[k] = z;
        }

        int j = 1;
        while (AnyAbove(term, value))
        {
            for (int k = 0; k < count; k++)
            {
                value[k] += term[k];
            }

            j++;
            for (int k = 0; k < count; k++)
            {
                running[k] = -x[at[k]] * running[k] / j;
                term[k] = running[k] / j;
            }
        }

        for (int k = 0; k < count; k++)
        {
            answer[at[k]] = value[k];
        }
    }

    /// <summary>
    /// The continued fraction for E₁, advanced two levels at a time in the modified Lentz shape
    /// MATLAB's own routine uses: numerator and denominator are rescaled by the denominator at
    /// every half step, so the pair never grows.
    /// </summary>
    private static void ContinuedFraction(Complex[] x, List<int> at, Complex[] answer)
    {
        int count = at.Count;
        var am2 = new Complex[count];
        var bm2 = new Complex[count];
        var am1 = new Complex[count];
        var bm1 = new Complex[count];
        var f = new Complex[count];
        var older = new Complex[count];
        for (int k = 0; k < count; k++)
        {
            Complex z = x[at[k]];
            am2[k] = Complex.Zero;
            bm2[k] = Complex.One;
            am1[k] = Complex.One;
            bm1[k] = z;
            f[k] = am1[k] / bm1[k];
            older[k] = new Complex(double.PositiveInfinity, 0.0);
        }

        int j = 2;
        double settle = 100.0 * 2.220446049250313e-16;
        while (AnyMoving(f, older, settle))
        {
            // The half step whose partial numerator is the level, with unit partial denominator.
            double alpha = j / 2.0;
            for (int k = 0; k < count; k++)
            {
                Complex a = am1[k] + (alpha * am2[k]);
                Complex b = bm1[k] + (alpha * bm2[k]);
                am2[k] = am1[k] / b;
                bm2[k] = bm1[k] / b;
                am1[k] = a / b;
                bm1[k] = Complex.One;
                f[k] = am1[k];
            }

            j++;

            // The half step whose partial denominator is the argument itself.
            double level = (j - 1) / 2.0;
            for (int k = 0; k < count; k++)
            {
                Complex z = x[at[k]];
                Complex a = (z * am1[k]) + (level * am2[k]);
                Complex b = (z * bm1[k]) + (level * bm2[k]);
                am2[k] = am1[k] / b;
                bm2[k] = bm1[k] / b;
                am1[k] = a / b;
                bm1[k] = Complex.One;
                older[k] = f[k];
                f[k] = am1[k];
            }

            j++;
        }

        for (int k = 0; k < count; k++)
        {
            Complex z = x[at[k]];

            Complex value = Complex.Exp(-z) * f[k];

            // A real argument has a real answer, and the half turn below is the only thing that can
            // put an imaginary part in it. Said outright rather than left to the arithmetic, because
            // a complex recurrence over an infinity produces NaN in the part that a real one leaves
            // at nought — which is the whole of the difference between expint(Inf) being NaN and
            // being NaN + NaN i.
            if (z.Imaginary == 0)
            {
                value = new Complex(value.Real, 0.0);
            }

            // The fraction gives the principal value; on the negative real axis, which is the cut,
            // E₁ carries a half turn the fraction knows nothing about.
            Complex cut = z.Real < 0 && z.Imaginary == 0
                ? new Complex(0.0, Math.PI)
                : Complex.Zero;
            answer[at[k]] = value - cut;
        }
    }

    /// <summary>
    /// Whether any term is still larger than the spacing of the value it would be added to —
    /// MATLAB's <c>eps</c> of a complex number is the spacing at its magnitude.
    /// </summary>
    private static bool AnyAbove(Complex[] term, Complex[] value)
    {
        for (int k = 0; k < term.Length; k++)
        {
            if (Complex.Abs(term[k]) > Spacing(Complex.Abs(value[k])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyMoving(Complex[] f, Complex[] older, double settle)
    {
        for (int k = 0; k < f.Length; k++)
        {
            if (Complex.Abs(f[k] - older[k]) > settle * Complex.Abs(f[k]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>MATLAB's <c>eps(x)</c>: the distance from |x| to the next double above it.</summary>
    internal static double Spacing(double x)
    {
        double size = Math.Abs(x);
        if (double.IsNaN(size) || double.IsInfinity(size))
        {
            return double.NaN;
        }

        return Math.BitIncrement(size) - size;
    }
}
