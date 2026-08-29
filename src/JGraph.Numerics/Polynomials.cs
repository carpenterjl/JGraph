using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// Polynomials held the way MATLAB holds them: a vector of coefficients, highest power first, so
/// that <c>[1 -3 2]</c> is x² − 3x + 2. Every routine here takes and returns that convention.
/// </summary>
/// <remarks>
/// <para>
/// The one routine that is not simple arithmetic is <see cref="Roots"/>, which is an eigenvalue
/// problem wearing a disguise. The roots of a monic polynomial are exactly the eigenvalues of its
/// companion matrix, so the whole question is handed to the same LAPACK path <c>eig</c> uses, and
/// inherits its accuracy. Doing it any other way — Newton on the polynomial, say, or deflation —
/// would need its own convergence story and would not agree with <c>eig</c> on the hard cases,
/// which are the ones anybody checks.
/// </para>
/// <para>
/// Degenerate leading coefficients are stripped before the matrix is built, and that stripping is
/// not merely tidiness. A polynomial whose leading coefficient is tiny beside the next one has
/// roots out near infinity; dividing through by it overflows, and the companion matrix arrives full
/// of infinities that the eigensolver can only answer with NaN. Discarding those terms answers the
/// polynomial that was really meant, with the roots at infinity simply absent — which is what
/// MATLAB documents and what a caller counting roots must be ready for.
/// </para>
/// </remarks>
public static class Polynomials
{
    /// <summary>
    /// The roots of the polynomial with the given coefficients, highest power first. Leading zeros
    /// are discarded, trailing zeros become roots at the origin, and leading terms so small that
    /// dividing by them overflows are discarded too.
    /// </summary>
    /// <param name="coefficients">The coefficients, highest power first.</param>
    /// <returns>
    /// The roots, the ones at the origin first. Empty when every coefficient is zero, and empty for
    /// a constant, which has no roots.
    /// </returns>
    public static Complex[] Roots(ReadOnlySpan<Complex> coefficients)
    {
        int first = -1;
        int last = -1;
        for (int i = 0; i < coefficients.Length; i++)
        {
            if (coefficients[i] == Complex.Zero)
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            last = i;
        }

        if (first < 0)
        {
            return [];
        }

        // Trailing zeros are roots at the origin: x^k divides the polynomial exactly k times.
        int atOrigin = coefficients.Length - 1 - last;

        var stripped = new Complex[last - first + 1];
        coefficients[first..(last + 1)].CopyTo(stripped);

        // A leading coefficient small enough that the division overflows would fill the companion
        // matrix with infinities. Drop such terms — the roots they carry are at infinity.
        int start = 0;
        while (stripped.Length - start > 1 && OverflowsWhenScaled(stripped, start))
        {
            start++;
        }

        int order = stripped.Length - start - 1;
        if (order < 1)
        {
            return Zeros(atOrigin);
        }

        Complex[] roots = Zeros(atOrigin + order);
        Complex lead = stripped[start];

        bool real = true;
        foreach (Complex c in stripped.AsSpan(start))
        {
            if (c.Imaginary != 0)
            {
                real = false;
                break;
            }
        }

        Complex[] spectrum = real
            ? Eigen.Spectrum(RealCompanion(stripped, start, order, lead.Real), order)
            : ComplexEigen.Values(ComplexCompanion(stripped, start, order, lead));

        spectrum.CopyTo(roots, atOrigin);
        return roots;
    }

    /// <summary>
    /// The coefficients of the polynomial whose roots are the given values, highest power first and
    /// monic. A set closed under conjugation gives a real answer, with the imaginary dust the
    /// expansion leaves discarded rather than reported.
    /// </summary>
    /// <param name="roots">The roots. Infinite ones are ignored; a NaN root poisons the answer.</param>
    /// <returns>One more coefficient than there were finite roots.</returns>
    public static Complex[] FromRoots(ReadOnlySpan<Complex> roots)
    {
        var finite = new List<Complex>(roots.Length);
        foreach (Complex root in roots)
        {
            if (Complex.IsFinite(root))
            {
                finite.Add(root);
            }
        }

        var c = new Complex[finite.Count + 1];
        c[0] = Complex.One;

        // The recursion is (x - r) applied one root at a time, written in place from the top down so
        // that each coefficient is read before it is overwritten.
        for (int j = 0; j < finite.Count; j++)
        {
            Complex root = finite[j];
            for (int k = j + 1; k >= 1; k--)
            {
                c[k] -= root * c[k - 1];
            }
        }

        return ClosedUnderConjugation(finite) ? RealPartsOf(c) : c;
    }

    /// <summary>
    /// The derivative of the polynomial <c>p</c>, or of the product <c>a·b</c> when a second one is
    /// given. Leading zeros are stripped from the answer.
    /// </summary>
    /// <param name="a">The polynomial, or the first factor.</param>
    /// <param name="b">The second factor, or a single 1 for the plain derivative.</param>
    /// <returns>The derivative's coefficients, highest power first.</returns>
    public static double[] Derivative(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        (double[] sum, _) = DerivativeParts(a, b, quotient: false);
        return sum;
    }

    /// <summary>
    /// The derivative of the ratio <c>a / b</c>, as a ratio of its own: the quotient rule's
    /// numerator <c>a'b − ab'</c> over <c>b²</c>, each with leading zeros stripped.
    /// </summary>
    /// <param name="a">The numerator.</param>
    /// <param name="b">The denominator.</param>
    /// <returns>The derivative's numerator and denominator.</returns>
    public static (double[] Numerator, double[] Denominator) QuotientDerivative(
        ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        (double[] numerator, int span) = DerivativeParts(a, b, quotient: true);
        double[] denominator = Trimmed(Convolution.Convolve(b, b));

        // A NaN coefficient makes the subtraction leave a term that cancels for every finite input,
        // so the vector can come back one longer than the rule allows. Trim to the rule's length.
        if (numerator.Length > span)
        {
            numerator = numerator[1..];
        }

        return (numerator, denominator);
    }

    /// <summary>
    /// The antiderivative of the polynomial, with <paramref name="constant"/> as the constant of
    /// integration — one coefficient longer than what came in.
    /// </summary>
    /// <param name="p">The polynomial's coefficients, highest power first.</param>
    /// <param name="constant">The constant of integration.</param>
    /// <returns>The antiderivative's coefficients.</returns>
    public static double[] Antiderivative(ReadOnlySpan<double> p, double constant)
    {
        var q = new double[p.Length + 1];
        for (int i = 0; i < p.Length; i++)
        {
            q[i] = p[i] / (p.Length - i);
        }

        q[^1] = constant;
        return q;
    }

    /// <summary>
    /// The polynomial evaluated at a square matrix, where every power is a matrix power and the
    /// constant term is that multiple of the identity — not the elementwise evaluation.
    /// </summary>
    /// <param name="p">The polynomial's coefficients, highest power first.</param>
    /// <param name="x">The matrix, square, in column-major order.</param>
    /// <param name="n">Its order.</param>
    /// <returns>The answer in column-major order.</returns>
    public static double[] MatrixValue(ReadOnlySpan<double> p, ReadOnlySpan<double> x, int n)
    {
        var y = new double[n * n];
        if (p.Length == 1)
        {
            for (int i = 0; i < n; i++)
            {
                y[(i * n) + i] = p[0];
            }

            return y;
        }

        // Horner's rule, with the matrix product taken in full each pass: Y <- X·Y + p(i)·I.
        var next = new double[n * n];
        foreach (double coefficient in p)
        {
            Array.Clear(next);
            for (int col = 0; col < n; col++)
            {
                for (int k = 0; k < n; k++)
                {
                    double scale = y[(col * n) + k];
                    if (scale == 0)
                    {
                        continue;
                    }

                    for (int row = 0; row < n; row++)
                    {
                        next[(col * n) + row] += x[(k * n) + row] * scale;
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                next[(i * n) + i] += coefficient;
            }

            (y, next) = (next, y);
        }

        return y;
    }

    /// <summary>
    /// Long division of one polynomial by another: the quotient, and a remainder laid out in the
    /// dividend's own coefficient slots so that <c>b</c> is <c>conv(a, q) + r</c>.
    /// </summary>
    /// <param name="b">The dividend.</param>
    /// <param name="a">The divisor. Its leading coefficient must not be zero.</param>
    /// <returns>
    /// The quotient and the remainder. A divisor longer than the dividend divides in zero times, so
    /// the quotient is a single 0 and the remainder is the dividend unchanged.
    /// </returns>
    public static (double[] Quotient, double[] Remainder) Divide(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a)
    {
        if (a.Length > b.Length)
        {
            return ([0.0], b.ToArray());
        }

        int lq = b.Length - a.Length + 1;
        var q = new double[lq];
        var r = new double[b.Length];

        // Synthetic division: each quotient coefficient, once known, is subtracted out of the terms
        // below it. What is left when the quotient runs out is the remainder, and it lands in the
        // low-order slots because its degree is below the divisor's by construction.
        Span<double> working = b.Length <= 256 ? stackalloc double[b.Length] : new double[b.Length];
        b.CopyTo(working);

        double lead = a[0];
        for (int i = 0; i < lq; i++)
        {
            double factor = working[i] / lead;
            q[i] = factor;
            if (factor == 0)
            {
                continue;
            }

            for (int j = 1; j < a.Length; j++)
            {
                working[i + j] -= factor * a[j];
            }
        }

        for (int i = lq; i < b.Length; i++)
        {
            r[i] = working[i];
        }

        return (q, r);
    }

    /// <summary>
    /// Long division of one complex polynomial by another, laid out exactly as the real form.
    /// </summary>
    /// <param name="b">The dividend.</param>
    /// <param name="a">The divisor. Its leading coefficient must not be zero.</param>
    /// <returns>The quotient and the remainder.</returns>
    public static (Complex[] Quotient, Complex[] Remainder) Divide(
        ReadOnlySpan<Complex> b, ReadOnlySpan<Complex> a)
    {
        if (a.Length > b.Length)
        {
            return ([Complex.Zero], b.ToArray());
        }

        int lq = b.Length - a.Length + 1;
        var q = new Complex[lq];
        var r = new Complex[b.Length];
        var working = new Complex[b.Length];
        b.CopyTo(working);

        Complex lead = a[0];
        for (int i = 0; i < lq; i++)
        {
            Complex factor = working[i] / lead;
            q[i] = factor;
            if (factor == Complex.Zero)
            {
                continue;
            }

            for (int j = 1; j < a.Length; j++)
            {
                working[i + j] -= factor * a[j];
            }
        }

        for (int i = lq; i < b.Length; i++)
        {
            r[i] = working[i];
        }

        return (q, r);
    }

    /// <summary>
    /// The next power of two at or above <c>|x|</c>, as its exponent: <c>ceil(log2(|x|))</c>, worked
    /// out from the exponent field rather than from a logarithm so that an exact power of two
    /// answers itself.
    /// </summary>
    /// <param name="x">The value.</param>
    /// <returns>
    /// The exponent. Zero for 0 and for ±1, and the input itself for an infinity or a NaN.
    /// </returns>
    public static double NextPowerOfTwo(double x)
    {
        double magnitude = Math.Abs(x);
        if (double.IsNaN(magnitude))
        {
            return double.NaN;
        }

        if (double.IsInfinity(magnitude))
        {
            return double.PositiveInfinity;
        }

        if (magnitude == 0)
        {
            return 0;
        }

        // ILogB gives e with |x| = m·2^e and m in [1, 2). MATLAB's frexp splits it as f·2^p with f in
        // [0.5, 1), so p is e + 1, and the exact-power case (f = 0.5) takes one back off.
        int exponent = Math.ILogB(magnitude);
        return IsPowerOfTwo(magnitude) ? exponent : exponent + 1;
    }

    private static bool IsPowerOfTwo(double magnitude)
    {
        long bits = BitConverter.DoubleToInt64Bits(magnitude);
        long mantissa = bits & 0x000F_FFFF_FFFF_FFFFL;
        if ((bits & 0x7FF0_0000_0000_0000L) != 0)
        {
            return mantissa == 0;
        }

        // Subnormal: the leading bit is not implied, so it is a power of two when one bit is set.
        return (mantissa & (mantissa - 1)) == 0;
    }

    private static (double[] Sum, int Span) DerivativeParts(
        ReadOnlySpan<double> u, ReadOnlySpan<double> v, bool quotient)
    {
        double[] up = ScaledByPower(u);
        double[] vp = ScaledByPower(v);

        double[] a1 = Convolution.Convolve(up, v);
        double[] a2 = Convolution.Convolve(u, vp);

        int length = Math.Max(a1.Length, a2.Length);
        var combined = new double[length];
        for (int i = 0; i < length; i++)
        {
            // The shorter one is the lower-order polynomial, so it lines up at the tail.
            double left = i >= length - a1.Length ? a1[i - (length - a1.Length)] : 0;
            double right = i >= length - a2.Length ? a2[i - (length - a2.Length)] : 0;
            combined[i] = quotient ? left - right : left + right;
        }

        int span = Math.Max(u.Length + v.Length - 2, 1);
        return (Trimmed(combined), span);
    }

    /// <summary>Each coefficient times the power of x it sits on, dropping the constant term.</summary>
    private static double[] ScaledByPower(ReadOnlySpan<double> p)
    {
        if (p.Length < 2)
        {
            return [0.0];
        }

        var scaled = new double[p.Length - 1];
        for (int i = 0; i < scaled.Length; i++)
        {
            scaled[i] = p[i] * (p.Length - 1 - i);
        }

        return scaled;
    }

    /// <summary>The coefficients from the first nonzero one on, or a single 0 if there is none.</summary>
    private static double[] Trimmed(double[] p)
    {
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] != 0)
            {
                return i == 0 ? p : p[i..];
            }
        }

        return [0.0];
    }

    private static bool OverflowsWhenScaled(ReadOnlySpan<Complex> c, int start)
    {
        Complex lead = c[start];
        for (int i = start + 1; i < c.Length; i++)
        {
            Complex scaled = c[i] / lead;
            if (double.IsInfinity(scaled.Real) || double.IsInfinity(scaled.Imaginary))
            {
                return true;
            }
        }

        return false;
    }

    private static double[] RealCompanion(
        ReadOnlySpan<Complex> c, int start, int order, double lead)
    {
        // Column-major: the first row holds the scaled coefficients negated, and the subdiagonal
        // holds ones. That is the matrix whose characteristic polynomial is the monic original.
        var a = new double[order * order];
        for (int col = 0; col < order; col++)
        {
            a[col * order] = -c[start + 1 + col].Real / lead;
            if (col + 1 < order)
            {
                a[(col * order) + col + 1] = 1;
            }
        }

        return a;
    }

    private static Complex[,] ComplexCompanion(
        ReadOnlySpan<Complex> c, int start, int order, Complex lead)
    {
        var a = new Complex[order, order];
        for (int col = 0; col < order; col++)
        {
            a[0, col] = -c[start + 1 + col] / lead;
            if (col + 1 < order)
            {
                a[col + 1, col] = Complex.One;
            }
        }

        return a;
    }

    private static Complex[] Zeros(int count) =>
        count == 0 ? [] : new Complex[count];

    private static Complex[] RealPartsOf(Complex[] c)
    {
        var real = new Complex[c.Length];
        for (int i = 0; i < c.Length; i++)
        {
            real[i] = new Complex(c[i].Real, 0);
        }

        return real;
    }

    /// <summary>
    /// Whether the roots above the real axis are exactly the conjugates of the ones below it, which
    /// is when the expanded polynomial is real and its imaginary part is only rounding.
    /// </summary>
    private static bool ClosedUnderConjugation(List<Complex> roots)
    {
        var above = new List<Complex>();
        var below = new List<Complex>();
        foreach (Complex root in roots)
        {
            if (root.Imaginary > 0)
            {
                above.Add(root);
            }
            else if (root.Imaginary < 0)
            {
                below.Add(Complex.Conjugate(root));
            }
        }

        if (above.Count != below.Count)
        {
            return false;
        }

        Comparison<Complex> order = (x, y) =>
        {
            int byReal = x.Real.CompareTo(y.Real);
            return byReal != 0 ? byReal : x.Imaginary.CompareTo(y.Imaginary);
        };

        above.Sort(order);
        below.Sort(order);
        for (int i = 0; i < above.Count; i++)
        {
            if (above[i] != below[i])
            {
                return false;
            }
        }

        return true;
    }
}
