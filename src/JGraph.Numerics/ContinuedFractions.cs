namespace JGraph.Numerics;

/// <summary>
/// The rational approximation of a real number by its continued fraction.
/// </summary>
/// <remarks>
/// The expansion is taken one term at a time and stopped as soon as the convergent it has built so
/// far is within the tolerance of the number itself — not when the remainder looks small, which is
/// a different and weaker test. Each term is the <em>nearest</em> integer rather than the floor, so
/// a term can be negative in the middle of a positive expansion: 2.5 is 3 + 1/(−2), not
/// 2 + 1/(2).
/// </remarks>
public static class ContinuedFractions
{
    /// <summary>One number's expansion: the terms, and the convergent they build.</summary>
    /// <param name="Terms">The partial quotients, outermost first.</param>
    /// <param name="Numerator">The convergent's numerator, carrying the sign.</param>
    /// <param name="Denominator">The convergent's denominator, never negative.</param>
    public readonly record struct Expansion(double[] Terms, double Numerator, double Denominator);

    /// <summary>
    /// Expands <paramref name="value"/> until the convergent is within <paramref name="tol"/> of it,
    /// or within the spacing of the number itself, whichever is the looser.
    /// </summary>
    /// <param name="value">The number to approximate. Must be finite.</param>
    /// <param name="tol">How far the convergent may sit from it.</param>
    /// <returns>The expansion.</returns>
    public static Expansion Expand(double value, double tol)
    {
        double x = value;
        double n0 = 1.0;
        double n1 = 0.0;
        double d0 = 0.0;
        double d1 = 1.0;
        var terms = new List<double>();
        double allowed = Math.Max(tol, Spacing(value));

        while (true)
        {
            bool negative = x < 0;
            double term = RoundHalfAway(x);
            if (!double.IsInfinity(x))
            {
                x -= term;
                double nextN = (n0 * term) + n1;
                double nextD = (d0 * term) + d1;
                n1 = n0;
                d1 = d0;
                n0 = nextN;
                d0 = nextD;
            }
            else
            {
                n1 = n0;
                d1 = d0;
                n0 = x;
                d0 = 0.0;
            }

            terms.Add(negative && term == 0.0 ? -0.0 : term);
            if (x == 0.0 || Math.Abs((n0 / d0) - value) <= allowed)
            {
                break;
            }

            x = 1.0 / x;
        }

        double sign = Math.Sign(d0);
        return new Expansion([.. terms], n0 / sign, Math.Abs(d0));
    }

    /// <summary>
    /// The expansion written the way MATLAB writes it: the outermost term, then a reciprocal
    /// bracket for every term after it.
    /// </summary>
    /// <param name="terms">The partial quotients.</param>
    /// <returns>The nested spelling.</returns>
    public static string Spell(double[] terms)
    {
        var built = new System.Text.StringBuilder();
        for (int i = 0; i < terms.Length; i++)
        {
            if (i > 0)
            {
                built.Append(" + 1/(");
            }

            // The sign is the term's own, and a term that rounded to nothing from below still
            // carries the minus that says which side of nought it came from.
            if (double.IsNegative(terms[i]))
            {
                built.Append('-');
            }

            built.Append(Whole(Math.Abs(terms[i])));
        }

        built.Append(')', terms.Length - 1);
        return built.ToString();
    }

    /// <summary>An integer-valued double written without an exponent or a point.</summary>
    public static string Whole(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Inf" : "-Inf";
        }

        return value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>MATLAB's <c>round</c>: halves go away from zero.</summary>
    private static double RoundHalfAway(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

    /// <summary>MATLAB's <c>eps(x)</c>: the distance from |x| to the next double above it.</summary>
    private static double Spacing(double x)
    {
        double size = Math.Abs(x);
        if (double.IsNaN(size) || double.IsInfinity(size))
        {
            return double.NaN;
        }

        return Math.BitIncrement(size) - size;
    }
}
