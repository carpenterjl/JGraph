using System.Numerics;

namespace JGraph.Numerics;

/// <summary>
/// The partial-fraction expansion of one polynomial over another, and the way back (MATLAB
/// <c>residue</c>).
/// </summary>
/// <remarks>
/// <para>
/// The expansion is worked out one pole at a time rather than by solving for every residue at once.
/// A pole of multiplicity <c>M</c> contributes the first <c>M</c> terms of a power series about
/// itself, and those terms are a local question — shift both polynomials to that pole and divide
/// their series in ascending powers. Building the n-by-n system whose columns are
/// <c>a(s)/(s-p)^i</c> and solving it would give the same answer where it is well conditioned, and
/// a much worse one where it is not: a triple root makes that matrix nearly singular, and the whole
/// answer degrades rather than the one group that is genuinely delicate.
/// </para>
/// <para>
/// Poles that are close together are treated as one repeated pole and moved to their mean, which is
/// MATLAB's rule and not a rounding convenience. The roots of a polynomial with a double root come
/// back from any eigenvalue solver as a conjugate pair a whisker off the real axis — the square root
/// of the working precision away, so about 1e-8 — and reading those as two distinct simple poles
/// gives two enormous residues that cancel, instead of the two modest ones the expansion has.
/// </para>
/// </remarks>
public static class PartialFractions
{
    /// <summary>
    /// How close two poles must be to count as one repeated pole: MATLAB's <c>mpoles</c> default,
    /// measured relative to the pole's own size so a pole at 1e6 is not held to an absolute 1e-3.
    /// </summary>
    public const double DefaultPoleTolerance = 1e-3;

    /// <summary>
    /// A partial-fraction expansion. <paramref name="Poles"/> repeats a pole once per multiplicity
    /// and <paramref name="Residues"/> runs alongside it in ascending power, so the pair at index
    /// <c>j</c> always means <c>Residues[j] / (s - Poles[j])^i</c> with <c>i</c> counting from one
    /// again at each new pole — the layout MATLAB documents and every caller reads positionally.
    /// </summary>
    /// <param name="Residues">One residue per pole entry.</param>
    /// <param name="Poles">The poles, repeated by multiplicity.</param>
    /// <param name="Direct">The polynomial part, highest power first; empty when there is none.</param>
    public readonly record struct Expansion(Complex[] Residues, Complex[] Poles, Complex[] Direct);

    /// <summary>
    /// Expands <c>b(s) / a(s)</c>, both given highest power first.
    /// </summary>
    /// <param name="numerator">The numerator's coefficients, highest power first.</param>
    /// <param name="denominator">The denominator's coefficients, highest power first.</param>
    /// <param name="poleTolerance">How close two poles must be to count as one; see
    /// <see cref="DefaultPoleTolerance"/>.</param>
    /// <returns>The residues, poles and polynomial part.</returns>
    /// <exception cref="ArgumentException">The denominator is zero.</exception>
    public static Expansion Expand(
        ReadOnlySpan<Complex> numerator,
        ReadOnlySpan<Complex> denominator,
        double poleTolerance = DefaultPoleTolerance)
    {
        Complex[] b = WithoutLeadingZeros(numerator);
        Complex[] a = WithoutLeadingZeros(denominator);
        if (a.Length == 0)
        {
            throw new ArgumentException("The denominator polynomial is zero.", nameof(denominator));
        }

        // An empty numerator is the zero polynomial, which has a residue of zero at every pole
        // rather than no residue at all — MATLAB answers r = 0 for residue([], [1 1]).
        if (b.Length == 0)
        {
            b = [Complex.Zero];
        }

        Complex[] direct = [];
        Complex[] rest = b;
        if (b.Length >= a.Length)
        {
            (Complex[] quotient, Complex[] remainder) = Polynomials.Divide(b, a);
            direct = quotient;
            rest = remainder;
        }

        int order = a.Length - 1;
        if (order == 0)
        {
            return new Expansion([], [], direct);
        }

        Complex[] grouped = Grouped(Polynomials.Roots(a), poleTolerance, out int[] multiplicities);

        var residues = new Complex[order];
        var poles = new Complex[order];
        int at = 0;
        int group = 0;
        while (at < order)
        {
            Complex pole = grouped[at];
            int multiplicity = multiplicities[group++];

            // a(s) with this pole's whole factor divided out. Deflating the real denominator rather
            // than multiplying the other groups back together keeps the leading coefficient exactly
            // where it was, so a(s) = (s - pole)^m * rest holds to the last bit.
            Complex[] others = a;
            for (int i = 0; i < multiplicity; i++)
            {
                others = Deflated(others, pole);
            }

            Complex[] top = AboutPoint(rest, pole);
            Complex[] bottom = AboutPoint(others, pole);
            Complex[] series = SeriesQuotient(top, bottom, multiplicity);

            for (int i = 0; i < multiplicity; i++)
            {
                poles[at + i] = pole;

                // The coefficient of 1/(s - p)^(i+1) is the series term of order m - (i + 1): the
                // lowest term of the series is the highest-power fraction, which is why the residues
                // of one pole read backwards from the series that produced them.
                residues[at + i] = series[multiplicity - 1 - i];
            }

            at += multiplicity;
        }

        return new Expansion(residues, poles, direct);
    }

    /// <summary>
    /// Puts an expansion back together: the inverse of <see cref="Expand"/>, and MATLAB's
    /// three-argument two-output form.
    /// </summary>
    /// <param name="residues">One residue per pole entry.</param>
    /// <param name="poles">The poles; consecutive equal entries are one repeated pole, in ascending
    /// power, which is the layout <see cref="Expand"/> produces.</param>
    /// <param name="direct">The polynomial part, highest power first; may be empty.</param>
    /// <param name="poleTolerance">How close two poles must be to count as one.</param>
    /// <returns>The numerator and denominator, both highest power first.</returns>
    public static (Complex[] Numerator, Complex[] Denominator) Combine(
        ReadOnlySpan<Complex> residues,
        ReadOnlySpan<Complex> poles,
        ReadOnlySpan<Complex> direct,
        double poleTolerance = DefaultPoleTolerance)
    {
        if (residues.Length != poles.Length)
        {
            throw new ArgumentException(
                $"There are {residues.Length} residues and {poles.Length} poles, which do not pair up.",
                nameof(poles));
        }

        Complex[] denominator = poles.Length == 0 ? [Complex.One] : Polynomials.FromRoots(poles);
        Complex[] shortened = WithoutLeadingZeros(direct);

        // The numerator is as long as the denominator's degree plus whatever the polynomial part
        // adds, and it is not trimmed: MATLAB leaves the leading rounding dust in place rather than
        // deciding on the caller's behalf that a 5e-17 leading coefficient was meant to be nothing.
        int length = Math.Max(poles.Length, shortened.Length + poles.Length);
        var numerator = new Complex[Math.Max(length, 1)];

        if (shortened.Length > 0)
        {
            Complex[] whole = Multiplied(shortened, denominator);
            Add(numerator, whole);
        }

        int at = 0;
        while (at < poles.Length)
        {
            Complex pole = poles[at];
            int multiplicity = 1;
            while (at + multiplicity < poles.Length
                   && Near(poles[at + multiplicity], pole, poleTolerance))
            {
                multiplicity++;
            }

            Complex[] factor = denominator;
            for (int i = 0; i < multiplicity; i++)
            {
                factor = Deflated(factor, pole);
                Add(numerator, Scaled(factor, residues[at + i]));
            }

            at += multiplicity;
        }

        return (numerator, denominator);
    }

    /// <summary>
    /// The roots with near-equal ones brought together and replaced by their mean, and the size of
    /// each such group in the order the groups appear.
    /// </summary>
    /// <remarks>
    /// The order the groups appear in is the order the roots came in, which is what makes the poles
    /// of a polynomial with no repeats identical to <c>roots</c>'s own answer. Only a repeat moves
    /// anything, and then only far enough to sit beside the pole it repeats.
    /// </remarks>
    private static Complex[] Grouped(Complex[] roots, double tolerance, out int[] multiplicities)
    {
        var order = new List<List<int>>();
        foreach (int index in Enumerable.Range(0, roots.Length))
        {
            List<int>? found = null;
            foreach (List<int> candidate in order)
            {
                if (Near(roots[index], roots[candidate[0]], tolerance))
                {
                    found = candidate;
                    break;
                }
            }

            if (found is null)
            {
                order.Add([index]);
            }
            else
            {
                found.Add(index);
            }
        }

        var grouped = new Complex[roots.Length];
        multiplicities = new int[order.Count];
        int at = 0;
        for (int g = 0; g < order.Count; g++)
        {
            List<int> members = order[g];
            multiplicities[g] = members.Count;

            Complex sum = Complex.Zero;
            foreach (int index in members)
            {
                sum += roots[index];
            }

            Complex centre = sum / members.Count;
            for (int i = 0; i < members.Count; i++)
            {
                grouped[at++] = centre;
            }
        }

        return grouped;
    }

    /// <summary>Whether two poles are close enough to be one, relative to their own size.</summary>
    private static bool Near(Complex a, Complex b, double tolerance) =>
        Complex.Abs(a - b) <= tolerance * Math.Max(1.0, Complex.Abs(b));

    /// <summary>
    /// The coefficients of <paramref name="p"/> in ascending powers of <c>s - point</c>: a Taylor
    /// shift, done as repeated synthetic division because each division hands back the next
    /// coefficient as its remainder.
    /// </summary>
    private static Complex[] AboutPoint(ReadOnlySpan<Complex> p, Complex point)
    {
        var work = new Complex[p.Length];
        p.CopyTo(work);

        var ascending = new Complex[p.Length];
        int length = p.Length;
        for (int t = 0; t < ascending.Length; t++)
        {
            // One synthetic division leaves the quotient in work[0 .. length-2] and hands back
            // P(point) as its remainder, which is the next coefficient of the shifted polynomial.
            Complex carry = work[0];
            for (int i = 1; i < length; i++)
            {
                Complex next = work[i] + (carry * point);
                work[i - 1] = carry;
                carry = next;
            }

            ascending[t] = carry;
            length--;
        }

        return ascending;
    }

    /// <summary>
    /// The first <paramref name="count"/> coefficients of <c>top / bottom</c> as a power series,
    /// both given in ascending powers.
    /// </summary>
    private static Complex[] SeriesQuotient(
        ReadOnlySpan<Complex> top, ReadOnlySpan<Complex> bottom, int count)
    {
        var series = new Complex[count];
        for (int t = 0; t < count; t++)
        {
            Complex sum = t < top.Length ? top[t] : Complex.Zero;
            for (int u = 0; u < t; u++)
            {
                if (t - u < bottom.Length)
                {
                    sum -= series[u] * bottom[t - u];
                }
            }

            series[t] = bottom.Length > 0 ? sum / bottom[0] : Complex.Zero;
        }

        return series;
    }

    /// <summary>
    /// <paramref name="p"/> divided by <c>s - root</c>, dropping the remainder. Synthetic division,
    /// which is exact when the root really is one.
    /// </summary>
    private static Complex[] Deflated(ReadOnlySpan<Complex> p, Complex root)
    {
        if (p.Length <= 1)
        {
            return [];
        }

        var quotient = new Complex[p.Length - 1];
        Complex carry = p[0];
        for (int i = 0; i < quotient.Length; i++)
        {
            quotient[i] = carry;
            carry = p[i + 1] + (carry * root);
        }

        return quotient;
    }

    /// <summary>The product of two polynomials, both highest power first.</summary>
    private static Complex[] Multiplied(ReadOnlySpan<Complex> a, ReadOnlySpan<Complex> b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return [];
        }

        var product = new Complex[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                product[i + j] += a[i] * b[j];
            }
        }

        return product;
    }

    /// <summary>A polynomial times a scalar.</summary>
    private static Complex[] Scaled(ReadOnlySpan<Complex> p, Complex by)
    {
        var scaled = new Complex[p.Length];
        for (int i = 0; i < p.Length; i++)
        {
            scaled[i] = p[i] * by;
        }

        return scaled;
    }

    /// <summary>Adds a polynomial into a longer buffer, lining the two up at the constant term.</summary>
    private static void Add(Complex[] into, ReadOnlySpan<Complex> p)
    {
        int offset = into.Length - p.Length;
        for (int i = 0; i < p.Length; i++)
        {
            int at = offset + i;
            if (at >= 0)
            {
                into[at] += p[i];
            }
        }
    }

    /// <summary>The coefficients with any leading zeros dropped; an all-zero polynomial is empty.</summary>
    private static Complex[] WithoutLeadingZeros(ReadOnlySpan<Complex> p)
    {
        int start = 0;
        while (start < p.Length && p[start] == Complex.Zero)
        {
            start++;
        }

        return p[start..].ToArray();
    }
}
