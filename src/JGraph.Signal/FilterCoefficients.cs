using System.Numerics;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>
/// The four ways of writing the same filter down — coefficients, roots, state space and a partial
/// fraction — and the conversions between them (M133).
/// </summary>
/// <remarks>
/// <para>
/// A digital filter has one behaviour and half a dozen spellings. The transfer function is a ratio
/// of polynomials; the zero-pole-gain form is those polynomials' roots; the state-space form is a
/// first-order recurrence in a vector; the partial fraction is a sum of one-pole terms. Every
/// conversion here goes through polynomial roots or polynomial products, and every one of them is
/// numerically worse than the form it started from — which is why MATLAB's own documentation warns
/// against the transfer function for anything above about order ten, and why the second-order
/// section machinery in <see cref="SecondOrderSections"/> exists at all.
/// </para>
/// <para>
/// The care here is in the edges rather than the algebra. Which trailing zero counts, whether a
/// missing zero is padded with infinity or dropped, whether a gain is taken from the first non-zero
/// coefficient or from the leading one — those are the choices that make two implementations of the
/// same textbook formula disagree, and they are copied from the sources rather than reasoned out.
/// </para>
/// </remarks>
public static class FilterCoefficients
{
    /// <summary>How close two poles must be, relatively, to count as one repeated pole.</summary>
    private const double PoleTolerance = 0.001;

    /// <summary>A transfer function's roots: its zeros, its poles and the gain that scales them.</summary>
    /// <param name="Zeros">The numerator's roots, one column per numerator row.</param>
    /// <param name="Poles">The denominator's roots.</param>
    /// <param name="Gains">One gain per numerator row.</param>
    /// <param name="ZeroRows">How many rows the zero matrix has.</param>
    public readonly record struct Zpk(Complex[] Zeros, Complex[] Poles, Complex[] Gains, int ZeroRows);

    /// <summary>A state-space quadruple, each matrix stored row by row.</summary>
    public readonly record struct StateSpace(
        double[,] A, double[,] B, double[,] C, double[,] D);

    /// <summary>A partial fraction expansion of a rational function of <c>z</c>.</summary>
    public readonly record struct Expansion(Complex[] Residues, Complex[] Poles, Complex[] Direct);

    // --- Transfer function to roots -----------------------------------------------------------

    /// <summary>
    /// <c>tf2zp</c>: the roots of a transfer function whose numerator may have several rows.
    /// </summary>
    /// <remarks>
    /// The numerator's leading all-zero columns are dropped before its roots are taken, which is
    /// what makes a numerator shorter than its denominator answer the same zeros as the padded one.
    /// A row whose zeros do not fill the matrix is padded with infinity rather than with nothing,
    /// so that the zero matrix stays rectangular and a missing zero is visible as one at infinity.
    /// </remarks>
    public static Zpk TfToZp(Complex[] numerator, int numeratorRows, int numeratorColumns, ReadOnlySpan<Complex> denominator)
    {
        if (denominator.Length > 0 && denominator[0] == Complex.Zero)
        {
            throw new ArgumentException("The denominator must have a non-zero leading coefficient.", nameof(denominator));
        }

        Complex lead = denominator.Length > 0 ? denominator[0] : Complex.One;
        Complex[] poles = denominator.Length > 1 ? Polynomials.Roots(denominator) : [];

        int first = 0;
        while (first < numeratorColumns && ColumnIsZero(numerator, numeratorRows, first))
        {
            first++;
        }

        int width = numeratorColumns - first;
        int height = System.Math.Max(0, width - 1);
        var zeros = new Complex[height * numeratorRows];
        for (int i = 0; i < zeros.Length; i++)
        {
            zeros[i] = new Complex(double.PositiveInfinity, 0);
        }

        var gains = new Complex[numeratorRows];
        for (int r = 0; r < numeratorRows; r++)
        {
            var row = new Complex[width];
            for (int c = 0; c < width; c++)
            {
                row[c] = numerator[((first + c) * numeratorRows) + r];
            }

            Complex[] found = row.Length > 1 ? Polynomials.Roots(row) : [];
            for (int i = 0; i < found.Length && i < height; i++)
            {
                zeros[(r * height) + i] = found[i];
            }

            for (int c = 0; c < width; c++)
            {
                if (row[c] != Complex.Zero)
                {
                    gains[r] = row[c] / lead;
                    break;
                }
            }
        }

        return new Zpk(zeros, poles, gains, height);
    }

    /// <summary><c>tf2zpk</c>: the same, after both sides are padded to a common length.</summary>
    public static Zpk TfToZpk(ReadOnlySpan<double> b, ReadOnlySpan<double> a)
    {
        (double[] num, double[] den) = EqualLength(b, a);
        var numerator = new Complex[num.Length];
        for (int i = 0; i < num.Length; i++)
        {
            numerator[i] = num[i];
        }

        var denominator = new Complex[den.Length];
        for (int i = 0; i < den.Length; i++)
        {
            denominator[i] = den[i];
        }

        return TfToZp(numerator, 1, numerator.Length, denominator);
    }

    /// <summary>Whether one column of a row-major-indexed numerator is entirely zero.</summary>
    private static bool ColumnIsZero(Complex[] numerator, int rows, int column)
    {
        for (int r = 0; r < rows; r++)
        {
            if (numerator[(column * rows) + r] != Complex.Zero)
            {
                return false;
            }
        }

        return true;
    }

    // --- Roots to transfer function -----------------------------------------------------------

    /// <summary>
    /// <c>zp2tf</c>: the polynomials whose roots these are, each numerator row scaled by its gain
    /// and left-padded to the denominator's length.
    /// </summary>
    public static (double[] Numerator, int Rows, int Columns, double[] Denominator) ZpToTf(
        Complex[] zeros, int zeroRows, int zeroColumns, ReadOnlySpan<Complex> poles, ReadOnlySpan<double> gains)
    {
        double[] den = RealPolynomial(poles);
        int width = den.Length;

        if (zeroRows == 0 || zeroColumns == 0)
        {
            var flat = new double[gains.Length * width];
            for (int r = 0; r < gains.Length; r++)
            {
                flat[((width - 1) * gains.Length) + r] = gains[r];
            }

            return (flat, gains.Length, width, den);
        }

        var num = new double[zeroColumns * width];
        for (int c = 0; c < zeroColumns; c++)
        {
            var column = new Complex[zeroRows];
            for (int r = 0; r < zeroRows; r++)
            {
                column[r] = zeros[(c * zeroRows) + r];
            }

            double[] row = RealPolynomial(column);
            for (int i = 0; i < row.Length; i++)
            {
                row[i] *= gains[c];
            }

            for (int i = 0; i < row.Length; i++)
            {
                num[((width - row.Length + i) * zeroColumns) + c] = row[i];
            }
        }

        return (num, zeroColumns, width, den);
    }

    /// <summary>MATLAB's <c>real(poly(v))</c>: the monic polynomial with these roots.</summary>
    public static double[] RealPolynomial(ReadOnlySpan<Complex> roots)
    {
        Complex[] coefficients = Polynomials.FromRoots(roots);
        var real = new double[coefficients.Length];
        for (int i = 0; i < coefficients.Length; i++)
        {
            real[i] = coefficients[i].Real;
        }

        return real;
    }

    // --- Padding, stabilising and scaling -----------------------------------------------------

    /// <summary>
    /// <c>eqtflength</c>: both polynomials trimmed to the longer of their two significant lengths.
    /// </summary>
    /// <remarks>
    /// The trim is to the last non-zero coefficient of each side, not to the arrays' lengths, so a
    /// numerator written with trailing zeros loses them unless the denominator needs the room. The
    /// two orders it reports back are those significant lengths less one.
    /// </remarks>
    public static (double[] B, double[] A, int NumeratorOrder, int DenominatorOrder) EqualLengths(
        ReadOnlySpan<double> num, ReadOnlySpan<double> den)
    {
        if (den.Length == 0)
        {
            throw new ArgumentException("A transfer function's denominator cannot be empty.", nameof(den));
        }

        bool anyDen = false;
        foreach (double v in den)
        {
            if (v != 0)
            {
                anyDen = true;
                break;
            }
        }

        if (!anyDen)
        {
            throw new ArgumentException("A transfer function's denominator cannot be all zeros.", nameof(den));
        }

        int lastDen = -1;
        for (int i = den.Length - 1; i >= 0; i--)
        {
            if (den[i] != 0)
            {
                lastDen = i;
                break;
            }
        }

        int lastNum = -1;
        for (int i = num.Length - 1; i >= 0; i--)
        {
            if (num[i] != 0)
            {
                lastNum = i;
                break;
            }
        }

        int m = lastDen;
        int n = lastNum < 0 ? 0 : lastNum;
        int range = System.Math.Max(m + 1, n + 1);

        var a = new double[range];
        var b = new double[range];
        for (int i = 0; i < range; i++)
        {
            a[i] = i < den.Length ? den[i] : 0;
            b[i] = i < num.Length ? num[i] : 0;
        }

        return (b, a, n, m);
    }

    /// <summary>The two padded polynomials alone, which is what most callers want.</summary>
    public static (double[] B, double[] A) EqualLength(ReadOnlySpan<double> num, ReadOnlySpan<double> den)
    {
        (double[] b, double[] a, _, _) = EqualLengths(num, den);
        return (b, a);
    }

    /// <summary>
    /// <c>polystab</c>: every root outside the unit circle reflected to its reciprocal conjugate,
    /// which leaves the magnitude response alone and moves the phase.
    /// </summary>
    public static double[] PolyStabilise(ReadOnlySpan<double> a, out bool wasReal)
    {
        wasReal = true;
        if (a.Length <= 1)
        {
            return a.ToArray();
        }

        var coefficients = new Complex[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            coefficients[i] = a[i];
        }

        Complex[] roots = Polynomials.Roots(coefficients);
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == Complex.Zero)
            {
                continue;
            }

            // MATLAB's own arithmetic: a step at magnitude one, so a root exactly on the circle
            // is halfway between staying put and being reflected.
            double step = 0.5 * (System.Math.Sign(Complex.Abs(roots[i]) - 1.0) + 1.0);
            roots[i] = ((1 - step) * roots[i]) + (step / Complex.Conjugate(roots[i]));
        }

        double lead = 0;
        foreach (double v in a)
        {
            if (v != 0)
            {
                lead = v;
                break;
            }
        }

        Complex[] product = Polynomials.FromRoots(roots);
        var b = new double[product.Length];
        for (int i = 0; i < product.Length; i++)
        {
            b[i] = lead * product[i].Real;
        }

        return b;
    }

    /// <summary>
    /// <c>polyscale</c>: the roots pulled towards the origin by a factor, done on the coefficients
    /// where it costs one multiply each rather than a round trip through the roots.
    /// </summary>
    public static double[] PolyScale(ReadOnlySpan<double> p, double scale)
    {
        var y = new double[p.Length];
        double power = 1;
        for (int i = 0; i < p.Length; i++)
        {
            y[i] = p[i] * power;
            power *= scale;
        }

        return y;
    }

    // --- State space --------------------------------------------------------------------------

    /// <summary>
    /// <c>tf2ss</c>: the controllable canonical form, with the denominator's leading zeros stripped
    /// and the numerator padded or trimmed to match.
    /// </summary>
    public static StateSpace TfToSs(double[] numerator, int rows, int columns, ReadOnlySpan<double> den)
    {
        int start = 0;
        while (start < den.Length && den[start] == 0)
        {
            start++;
        }

        if (start == den.Length)
        {
            throw new ArgumentException("A transfer function's denominator cannot be all zeros.", nameof(den));
        }

        int nden = den.Length - start;
        var stripped = new double[nden];
        for (int i = 0; i < nden; i++)
        {
            stripped[i] = den[start + i];
        }

        double[] num;
        int width;
        if (columns > nden)
        {
            for (int c = 0; c < columns - nden; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (numerator[(c * rows) + r] != 0)
                    {
                        throw new ArgumentException(
                            "A transfer function's numerator cannot have a higher order than its denominator.",
                            nameof(numerator));
                    }
                }
            }

            width = nden;
            num = new double[width * rows];
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    num[(c * rows) + r] = numerator[((columns - nden + c) * rows) + r];
                }
            }
        }
        else
        {
            width = nden;
            num = new double[width * rows];
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    num[((nden - columns + c) * rows) + r] = numerator[(c * rows) + r];
                }
            }
        }

        double head = stripped[0];
        var denNorm = new double[nden];
        for (int i = 0; i < nden; i++)
        {
            denNorm[i] = stripped[i] / head;
        }

        for (int i = 0; i < num.Length; i++)
        {
            num[i] /= head;
        }

        int n = nden - 1;
        var d = new double[rows, 1];
        var c2 = new double[rows, System.Math.Max(0, n)];
        for (int r = 0; r < rows; r++)
        {
            d[r, 0] = num[r];
            for (int j = 0; j < n; j++)
            {
                c2[r, j] = num[((j + 1) * rows) + r] - (num[r] * denNorm[j + 1]);
            }
        }

        if (n == 0)
        {
            return new StateSpace(new double[0, 0], new double[0, 0], new double[rows, 0], d);
        }

        var a = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            a[0, j] = -denNorm[j + 1];
        }

        for (int i = 1; i < n; i++)
        {
            a[i, i - 1] = 1;
        }

        var b = new double[n, 1];
        b[0, 0] = 1;
        return new StateSpace(a, b, c2, d);
    }

    /// <summary>
    /// <c>ss2zp</c>: the poles are the state matrix's eigenvalues and the zeros are the system
    /// pencil's finite generalized eigenvalues.
    /// </summary>
    /// <remarks>
    /// A transmission zero is a frequency the system cannot pass, and it is a generalized rather
    /// than an ordinary eigenvalue: the pencil is the state matrix bordered by the input and output
    /// vectors against an identity bordered by zeros, so an infinite eigenvalue means a zero the
    /// system does not have. MATLAB reaches the same numbers by a deflation its sources do not
    /// publish; this reads them off the pencil.
    /// </remarks>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) SsToZp(
        double[,] a, double[,] b, double[,] c, double[,] d, int input)
    {
        int nx = a.GetLength(0);
        Complex[] poles = nx == 0 ? [] : Eigenvalues(a);

        var pencilA = new double[nx + 1, nx + 1];
        var pencilB = new double[nx + 1, nx + 1];
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nx; j++)
            {
                pencilA[i, j] = a[i, j];
            }

            pencilA[i, nx] = b[i, input];
            pencilA[nx, i] = c[0, i];
            pencilB[i, i] = 1;
        }

        pencilA[nx, nx] = d.GetLength(1) > input ? d[0, input] : 0;

        Complex[] finite = FiniteGeneralizedEigenvalues(pencilA, pencilB);

        double gain = pencilA[nx, nx];
        if (gain == 0)
        {
            // The leading Markov parameter: the first c A^k b that is not zero is the gain, which is
            // what makes zp2tf's numerator come back with the right leading coefficient.
            var state = new double[nx];
            for (int i = 0; i < nx; i++)
            {
                state[i] = b[i, input];
            }

            for (int k = 0; k < nx; k++)
            {
                double value = 0;
                for (int i = 0; i < nx; i++)
                {
                    value += c[0, i] * state[i];
                }

                if (value != 0)
                {
                    gain = value;
                    break;
                }

                var next = new double[nx];
                for (int i = 0; i < nx; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < nx; j++)
                    {
                        sum += a[i, j] * state[j];
                    }

                    next[i] = sum;
                }

                state = next;
            }
        }

        return (finite, poles, gain);
    }

    /// <summary><c>ss2tf</c>: the roots turned straight back into polynomials.</summary>
    public static (double[] Numerator, double[] Denominator) SsToTf(
        double[,] a, double[,] b, double[,] c, double[,] d, int input)
    {
        (Complex[] zeros, Complex[] poles, double gain) = SsToZp(a, b, c, d, input);
        double[] den = RealPolynomial(poles);
        double[] zeroPoly = RealPolynomial(zeros);

        var num = new double[den.Length];
        for (int i = 0; i < zeroPoly.Length; i++)
        {
            num[den.Length - zeroPoly.Length + i] = gain * zeroPoly[i];
        }

        return (num, den);
    }

    /// <summary>
    /// <c>zp2ss</c>: poles and zeros paired off two at a time into balanced second-order blocks and
    /// chained, which keeps a high-order system's state matrix conditioned where its transfer
    /// function would already have lost every figure it had.
    /// </summary>
    public static StateSpace ZpToSs(Complex[] zeros, Complex[] poles, double gain)
    {
        Complex[] pf = Finite(poles);
        Complex[] zf = Finite(zeros);
        int np = pf.Length;
        int nz = zf.Length;

        pf = PairOrPerturb(pf);
        zf = PairOrPerturb(zf);

        var a = new double[np, np];
        var b = new double[np, 1];
        var c = new double[1, np];
        double d = 1;

        bool oddPoles = false;
        bool oddZerosOnly = false;
        int usedPoles = np;
        int usedZeros = nz;

        if (usedPoles % 2 == 1 && usedZeros % 2 == 1)
        {
            a[0, 0] = pf[usedPoles - 1].Real;
            b[0, 0] = 1;
            c[0, 0] = (pf[usedPoles - 1] - zf[usedZeros - 1]).Real;
            d = 1;
            usedPoles--;
            usedZeros--;
            oddPoles = true;
        }
        else if (usedPoles % 2 == 1)
        {
            a[0, 0] = pf[usedPoles - 1].Real;
            b[0, 0] = 1;
            c[0, 0] = 1;
            d = 0;
            usedPoles--;
            oddPoles = true;
        }
        else if (usedZeros % 2 == 1)
        {
            double[] num = RealPolynomial([zf[usedZeros - 1]]);
            double[] den = RealPolynomial(pf.AsSpan(usedPoles - 2, 2));
            double wn = System.Math.Sqrt(Complex.Abs(pf[usedPoles - 2]) * Complex.Abs(pf[usedPoles - 1]));
            if (wn == 0)
            {
                wn = 1;
            }

            a[0, 0] = -den[1];
            a[0, 1] = -den[2] / wn;
            a[1, 0] = wn;
            b[0, 0] = 1;
            c[0, 0] = 1;
            c[0, 1] = num[1] / wn;
            d = 0;
            usedZeros--;
            usedPoles -= 2;
            oddZerosOnly = true;
        }

        int i = 0;
        while (i + 1 < usedZeros)
        {
            double[] num = RealPolynomial(zf.AsSpan(i, 2));
            double[] den = RealPolynomial(pf.AsSpan(i, 2));
            double wn = System.Math.Sqrt(Complex.Abs(pf[i]) * Complex.Abs(pf[i + 1]));
            if (wn == 0)
            {
                wn = 1;
            }

            int j = oddPoles ? i : oddZerosOnly ? i + 1 : i - 1;
            Chain(a, b, c, ref d,
                [-den[1], -den[2] / wn, wn, 0],
                [1, 0],
                [num[1] - den[1], (num[2] - den[2]) / wn],
                1, j, np, chainC: true);
            i += 2;
        }

        while (i + 1 < usedPoles)
        {
            double[] den = RealPolynomial(pf.AsSpan(i, 2));
            double wn = System.Math.Sqrt(Complex.Abs(pf[i]) * Complex.Abs(pf[i + 1]));
            if (wn == 0)
            {
                wn = 1;
            }

            int j = oddPoles ? i : oddZerosOnly ? i + 1 : i - 1;
            Chain(a, b, c, ref d,
                [-den[1], -den[2] / wn, wn, 0],
                [1, 0],
                [0, 1 / wn],
                0, j, np, chainC: false);
            i += 2;
        }

        for (int k = 0; k < np; k++)
        {
            c[0, k] *= gain;
        }

        return new StateSpace(a, b, c, new double[1, 1] { { d * gain } });
    }

    /// <summary>Appends one balanced second-order block to a growing state-space chain.</summary>
    private static void Chain(double[,] a, double[,] b, double[,] c, ref double d,
        double[] block, double[] input, double[] output, double feedthrough, int j, int np, bool chainC)
    {
        if (j == -1)
        {
            a[0, 0] = block[0];
            a[0, 1] = block[1];
            a[1, 0] = block[2];
            a[1, 1] = block[3];
            if (!chainC)
            {
                c[0, 0] = output[0];
                c[0, 1] = output[1];
            }
        }
        else
        {
            for (int r = 0; r < 2; r++)
            {
                for (int col = 0; col <= j; col++)
                {
                    a[j + 1 + r, col] = input[r] * c[0, col];
                }
            }

            a[j + 1, j + 1] = block[0];
            a[j + 1, j + 2] = block[1];
            a[j + 2, j + 1] = block[2];
            a[j + 2, j + 2] = block[3];

            if (!chainC)
            {
                for (int col = 0; col <= j; col++)
                {
                    c[0, col] *= feedthrough;
                }
            }
        }

        int at = j == -1 ? 0 : j + 1;
        b[at, 0] = input[0] * d;
        if (at + 1 < np)
        {
            b[at + 1, 0] = input[1] * d;
        }

        c[0, at] = output[0];
        if (at + 1 < np)
        {
            c[0, at + 1] = output[1];
        }

        d *= feedthrough;
    }

    /// <summary>The finite entries of a root list, in order.</summary>
    private static Complex[] Finite(ReadOnlySpan<Complex> values)
    {
        var kept = new List<Complex>(values.Length);
        foreach (Complex v in values)
        {
            if (double.IsFinite(v.Real) && double.IsFinite(v.Imaginary))
            {
                kept.Add(v);
            }
        }

        return [.. kept];
    }

    /// <summary>
    /// Conjugate pairing at machine tolerance, widened once if the roots came back from a solver
    /// that did not make the two halves of a pair agree exactly.
    /// </summary>
    private static Complex[] PairOrPerturb(Complex[] values)
    {
        if (values.Length == 0)
        {
            return values;
        }

        try
        {
            return PhaseSequences.ConjugatePairs(values, 0);
        }
        catch (ArgumentException)
        {
            double norm = 0;
            foreach (Complex v in values)
            {
                norm += (v.Real * v.Real) + (v.Imaginary * v.Imaginary);
            }

            double tolerance = (1e6 * values.Length * System.Math.Sqrt(norm) * 2.220446049250313e-16)
                + 2.220446049250313e-16;
            return PhaseSequences.ConjugatePairs(values, System.Math.Min(tolerance, 0.9999));
        }
    }

    /// <summary>The eigenvalues of a small dense matrix.</summary>
    private static Complex[] Eigenvalues(double[,] a)
    {
        return Eigen.Factor(a).Values;
    }

    /// <summary>The pencil's eigenvalues that are not at infinity.</summary>
    private static Complex[] FiniteGeneralizedEigenvalues(double[,] a, double[,] b)
    {
        GeneralizedSchur qz = GeneralizedSchur.Factor(a, b);
        int n = a.GetLength(0);
        var kept = new List<Complex>(n);
        for (int i = 0; i < n; i++)
        {
            double beta = qz.Beta[i];
            if (beta == 0)
            {
                continue;
            }

            Complex value = qz.Alpha[i] / beta;
            if (double.IsFinite(value.Real) && double.IsFinite(value.Imaginary))
            {
                kept.Add(value);
            }
        }

        return [.. kept];
    }

    // --- Partial fractions ----------------------------------------------------------------------

    /// <summary>
    /// <c>residuez</c>: the expansion of <c>B(z)/A(z)</c> in powers of <c>z</c> inverse, which is
    /// not the same expansion <c>residue</c> gives — that one is in powers of <c>z</c>.
    /// </summary>
    /// <remarks>
    /// The residue at a simple pole is a ratio of two polynomials evaluated at the pole's
    /// reciprocal, which is exact. A repeated pole has no such formula, so MATLAB builds the
    /// impulse responses of the one-pole filters it has already accounted for, subtracts them from
    /// the signal's own impulse response, and least-squares the rest — one extra sample beyond the
    /// number of unknowns, so the system is over-determined by exactly one.
    /// </remarks>
    public static Expansion Residuez(ReadOnlySpan<double> bIn, ReadOnlySpan<double> aIn)
    {
        if (aIn.Length == 0 || aIn[0] == 0)
        {
            throw new ArgumentException("The first denominator coefficient cannot be zero.", nameof(aIn));
        }

        var b = new double[bIn.Length];
        for (int i = 0; i < bIn.Length; i++)
        {
            b[i] = bIn[i] / aIn[0];
        }

        var a = new double[aIn.Length];
        for (int i = 0; i < aIn.Length; i++)
        {
            a[i] = aIn[i] / aIn[0];
        }

        if (a.Length == 1)
        {
            var direct = new Complex[b.Length];
            for (int i = 0; i < b.Length; i++)
            {
                direct[i] = b[i];
            }

            return new Expansion([], [], direct);
        }

        int residues = a.Length - 1;
        int extra = System.Math.Max(0, b.Length - residues);
        Complex[] k = [];

        if (extra > 0)
        {
            (double[] quotient, double[] remainder) = Polynomials.Divide(Reversed(b), Reversed(a));
            double[] flippedQuotient = Reversed(quotient);
            k = new Complex[flippedQuotient.Length];
            for (int i = 0; i < flippedQuotient.Length; i++)
            {
                k[i] = flippedQuotient[i];
            }

            var rest = new double[System.Math.Max(0, remainder.Length - extra)];
            for (int i = 0; i < rest.Length; i++)
            {
                rest[i] = remainder[extra + i];
            }

            b = Reversed(rest);
        }

        var aComplex = new Complex[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            aComplex[i] = a[i];
        }

        Complex[] poles = Polynomials.Roots(aComplex);
        int n = residues;
        var impulse = new double[n + 1];
        impulse[0] = 1;
        double[] h = DigitalFilter.Filter(b, a, impulse);

        (int[] multiplicities, int[] order) = MultiplePoles(poles, PoleTolerance);
        var ordered = new Complex[poles.Length];
        for (int i = 0; i < poles.Length; i++)
        {
            ordered[i] = poles[order[i]];
        }

        poles = ordered;

        var s = new Complex[n + 1, n];
        for (int j = 0; j < n; j++)
        {
            var den = new Complex[] { 1, -poles[j] };
            Complex[] source = new Complex[n + 1];
            if (multiplicities[j] > 1 && j > 0)
            {
                for (int i = 0; i <= n; i++)
                {
                    source[i] = s[i, j - 1];
                }
            }
            else
            {
                source[0] = 1;
            }

            Complex[] column = ComplexFilter([Complex.One], den, source);
            for (int i = 0; i <= n; i++)
            {
                s[i, j] = column[i];
            }
        }

        var r = new Complex[n];
        var done = new bool[n];
        double[] bFlipped = Reversed(b);

        for (int i = 0; i < n; i++)
        {
            bool last = i == n - 1;
            bool nextIsSimple = !last && multiplicities[i + 1] == 1;
            if (!last && !nextIsSimple)
            {
                continue;
            }

            var others = new List<Complex>(n);
            for (int j = 0; j < i - multiplicities[i] + 1; j++)
            {
                others.Add(poles[j]);
            }

            for (int j = i + 1; j < n; j++)
            {
                others.Add(poles[j]);
            }

            Complex[] temp = Reversed(Polynomials.FromRoots(CollectionsMarshalSpan(others)));
            Complex at = Complex.One / poles[i];
            r[i] = EvaluateComplex(bFlipped, at) / EvaluateComplex(temp, at);
            done[i] = true;
        }

        var residual = new Complex[n + 1];
        for (int i = 0; i <= n; i++)
        {
            residual[i] = h[i];
        }

        for (int j = 0; j < n; j++)
        {
            if (!done[j])
            {
                continue;
            }

            for (int i = 0; i <= n; i++)
            {
                residual[i] -= s[i, j] * r[j];
            }
        }

        var unknown = new List<int>();
        for (int j = 0; j < n; j++)
        {
            if (!done[j])
            {
                unknown.Add(j);
            }
        }

        if (unknown.Count > 0)
        {
            var design = new Complex[n + 1, unknown.Count];
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j < unknown.Count; j++)
                {
                    design[i, j] = s[i, unknown[j]];
                }
            }

            Complex[] solved = ComplexLeastSquares(design, residual);
            for (int j = 0; j < unknown.Count; j++)
            {
                r[unknown[j]] = solved[j];
            }
        }

        return new Expansion(r, poles, k);
    }

    /// <summary>
    /// <c>residuez</c> read backwards: the expansion recombined into a ratio of polynomials.
    /// </summary>
    public static (Complex[] Numerator, Complex[] Denominator) ResiduezInverse(
        Complex[] residues, Complex[] poles, Complex[] direct)
    {
        if (residues.Length != poles.Length)
        {
            throw new ArgumentException("A residue list and a pole list must be the same length.", nameof(residues));
        }

        if (poles.Length == 0)
        {
            return (direct.Length == 0 ? [] : direct, []);
        }

        int total = poles.Length + direct.Length;
        (int[] multiplicities, int[] order) = MultiplePoles(poles, PoleTolerance, keepOrder: true);
        var orderedPoles = new Complex[poles.Length];
        var orderedResidues = new Complex[poles.Length];
        for (int i = 0; i < poles.Length; i++)
        {
            orderedPoles[i] = poles[order[i]];
            orderedResidues[i] = residues[order[i]];
        }

        Complex[] denominator = Polynomials.FromRoots(orderedPoles);

        Complex[] numerator;
        if (direct.Length > 0)
        {
            numerator = Convolve(denominator, direct);
        }
        else
        {
            numerator = new Complex[total];
        }

        for (int i = 0; i < poles.Length; i++)
        {
            var others = new List<Complex>(poles.Length);
            for (int j = 0; j < i - multiplicities[i] + 1; j++)
            {
                others.Add(orderedPoles[j]);
            }

            for (int j = i + 1; j < poles.Length; j++)
            {
                others.Add(orderedPoles[j]);
            }

            Complex[] temp = Polynomials.FromRoots(CollectionsMarshalSpan(others));
            for (int j = 0; j < temp.Length; j++)
            {
                numerator[j] += orderedResidues[i] * temp[j];
            }
        }

        return (numerator, denominator);
    }

    /// <summary>
    /// <c>mpoles</c>: how many times each pole has already been seen, and the order that groups the
    /// repeats together.
    /// </summary>
    /// <param name="poles">The poles to group.</param>
    /// <param name="tolerance">How close, relative to magnitude, two poles must be to count as one.</param>
    /// <param name="keepOrder">Whether to keep the poles in the order given rather than sorting by magnitude.</param>
    public static (int[] Multiplicities, int[] Order) MultiplePoles(
        ReadOnlySpan<Complex> poles, double tolerance, bool keepOrder = false)
    {
        int n = poles.Length;
        var index = new int[n];
        for (int i = 0; i < n; i++)
        {
            index[i] = i;
        }

        if (!keepOrder)
        {
            var magnitudes = new double[n];
            for (int i = 0; i < n; i++)
            {
                magnitudes[i] = Complex.Abs(poles[i]);
            }

            Array.Sort(index, (x, y) =>
            {
                int byMagnitude = magnitudes[y].CompareTo(magnitudes[x]);
                return byMagnitude != 0 ? byMagnitude : x.CompareTo(y);
            });
        }

        var taken = new bool[n];
        var order = new List<int>(n);
        var multiplicities = new List<int>(n);

        for (int a = 0; a < n; a++)
        {
            int i = index[a];
            if (taken[i])
            {
                continue;
            }

            taken[i] = true;
            order.Add(i);
            multiplicities.Add(1);

            double scale = Complex.Abs(poles[i]);
            double reach = scale == 0 ? tolerance : tolerance * scale;
            int count = 1;
            for (int c = a + 1; c < n; c++)
            {
                int j = index[c];
                if (taken[j] || Complex.Abs(poles[j] - poles[i]) > reach)
                {
                    continue;
                }

                taken[j] = true;
                order.Add(j);
                multiplicities.Add(++count);
            }
        }

        return ([.. multiplicities], [.. order]);
    }

    // --- Small numerical helpers ----------------------------------------------------------------

    /// <summary>A one-pole complex recurrence, which is what the residue solve is built out of.</summary>
    private static Complex[] ComplexFilter(Complex[] b, Complex[] a, Complex[] x)
    {
        var y = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            Complex sum = Complex.Zero;
            for (int j = 0; j < b.Length && j <= i; j++)
            {
                sum += b[j] * x[i - j];
            }

            for (int j = 1; j < a.Length && j <= i; j++)
            {
                sum -= a[j] * y[i - j];
            }

            y[i] = sum / a[0];
        }

        return y;
    }

    /// <summary>A complex least-squares solve by Householder QR, for the repeated-pole system.</summary>
    private static Complex[] ComplexLeastSquares(Complex[,] design, Complex[] rhs)
    {
        int m = design.GetLength(0);
        int n = design.GetLength(1);
        var a = (Complex[,])design.Clone();
        var b = (Complex[])rhs.Clone();

        for (int k = 0; k < n; k++)
        {
            double norm = 0;
            for (int i = k; i < m; i++)
            {
                norm += (a[i, k].Real * a[i, k].Real) + (a[i, k].Imaginary * a[i, k].Imaginary);
            }

            norm = System.Math.Sqrt(norm);
            if (norm == 0)
            {
                continue;
            }

            Complex head = a[k, k];
            double headSize = Complex.Abs(head);
            Complex phase = headSize == 0 ? Complex.One : head / headSize;
            Complex alpha = -phase * norm;

            var v = new Complex[m - k];
            for (int i = k; i < m; i++)
            {
                v[i - k] = a[i, k];
            }

            v[0] -= alpha;

            double vNorm = 0;
            foreach (Complex value in v)
            {
                vNorm += (value.Real * value.Real) + (value.Imaginary * value.Imaginary);
            }

            if (vNorm == 0)
            {
                continue;
            }

            for (int j = k; j < n; j++)
            {
                Complex dot = Complex.Zero;
                for (int i = k; i < m; i++)
                {
                    dot += Complex.Conjugate(v[i - k]) * a[i, j];
                }

                Complex factor = 2 * dot / vNorm;
                for (int i = k; i < m; i++)
                {
                    a[i, j] -= factor * v[i - k];
                }
            }

            Complex rhsDot = Complex.Zero;
            for (int i = k; i < m; i++)
            {
                rhsDot += Complex.Conjugate(v[i - k]) * b[i];
            }

            Complex rhsFactor = 2 * rhsDot / vNorm;
            for (int i = k; i < m; i++)
            {
                b[i] -= rhsFactor * v[i - k];
            }
        }

        var x = new Complex[n];
        for (int i = n - 1; i >= 0; i--)
        {
            Complex sum = b[i];
            for (int j = i + 1; j < n; j++)
            {
                sum -= a[i, j] * x[j];
            }

            x[i] = a[i, i] == Complex.Zero ? Complex.Zero : sum / a[i, i];
        }

        return x;
    }

    /// <summary>The product of two polynomials.</summary>
    public static Complex[] Convolve(ReadOnlySpan<Complex> a, ReadOnlySpan<Complex> b)
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

    /// <summary>The product of two real polynomials.</summary>
    public static double[] Convolve(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return [];
        }

        var product = new double[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                product[i + j] += a[i] * b[j];
            }
        }

        return product;
    }

    /// <summary>Horner's evaluation of a real polynomial at a complex point.</summary>
    private static Complex EvaluateComplex(double[] coefficients, Complex at)
    {
        Complex sum = Complex.Zero;
        foreach (double coefficient in coefficients)
        {
            sum = (sum * at) + coefficient;
        }

        return sum;
    }

    /// <summary>Horner's evaluation of a complex polynomial at a complex point.</summary>
    private static Complex EvaluateComplex(Complex[] coefficients, Complex at)
    {
        Complex sum = Complex.Zero;
        foreach (Complex coefficient in coefficients)
        {
            sum = (sum * at) + coefficient;
        }

        return sum;
    }

    /// <summary>A copy read back to front.</summary>
    public static double[] Reversed(ReadOnlySpan<double> values)
    {
        var flipped = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            flipped[i] = values[values.Length - 1 - i];
        }

        return flipped;
    }

    /// <summary>A copy read back to front.</summary>
    public static Complex[] Reversed(ReadOnlySpan<Complex> values)
    {
        var flipped = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            flipped[i] = values[values.Length - 1 - i];
        }

        return flipped;
    }

    /// <summary>A list read as a span, which saves a copy on the hot path of the residue loop.</summary>
    private static ReadOnlySpan<Complex> CollectionsMarshalSpan(List<Complex> values) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
}
