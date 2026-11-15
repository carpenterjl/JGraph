using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// MATLAB's <c>A ^ p</c> for a square matrix and a scalar that is not a finite real whole number —
/// R2025b's <c>mpower.m</c> (<c>generalMpower</c> and <c>mpower_schur</c>), transcribed — and the
/// repeated squaring it uses for a whole power of a complex matrix (open item 321, ADR 0365).
/// </summary>
/// <remarks>
/// <para>
/// R2025b's <c>mpower</c> is a MATLAB file, and three roads come out of it. A Hermitian or
/// skew-Hermitian matrix is <c>V·diag(d.^p)·Vᴴ</c> over an orthonormal eigenbasis. Any other matrix
/// is <c>V·diag(d.^p)/V</c> over its eigenvectors, unless they are ill-conditioned (RCOND below
/// 1e-2) and the power is real and finite, when it takes Higham and Lin's Schur–Padé algorithm
/// (SIAM J. Matrix Anal. Appl. 34, 2013): the complex Schur form, square roots until the diagonal
/// is near one, a continued-fraction Padé approximant, and squarings back, with the diagonal and
/// superdiagonal computed directly. A real answer from that road keeps no imaginary roundoff.
/// </para>
/// <para>
/// The answers agree with R2025b's to rounding, not to the bit: the eigenvectors come from this
/// engine's own eigensolver, and two places estimate where MATLAB's code estimates differently —
/// RCOND of the eigenvectors is <see cref="ComplexSquareSystems"/>' <c>zgecon</c> estimate, and the
/// one-norms of powers that choose the Padé degree are exact at every order (MATLAB's <c>normAm</c>
/// is exact below order 50 and an estimate above).
/// </para>
/// </remarks>
public static class FractionalMatrixPower
{
    private const double Eps = 2.220446049250313e-16;

    /// <summary>The largest norm of X for which the degree-m Padé approximant to (I − X)^p is used, m = 1..7.</summary>
    private static readonly double[] Xvals =
    [
        1.512666672122460e-005,
        2.236550782529778e-003,
        1.882832775783885e-002,
        6.036100693089764e-002,
        1.239372725584911e-001,
        1.998030690604271e-001,
        2.787629930862099e-001,
    ];

    /// <summary>
    /// <c>A ^ p</c>, by <c>generalMpower</c>'s roads. <paramref name="real"/> says the matrix holds no
    /// imaginary part, which is what lets a real answer drop the roundoff the Schur road leaves.
    /// </summary>
    public static Complex[,] Evaluate(Complex[,] a, Complex p, bool real)
    {
        ArgumentNullException.ThrowIfNull(a);
        int n = a.GetLength(0);
        if (n == 0)
        {
            return new Complex[0, 0];
        }

        // An infinity or a NaN anywhere is a real NaN throughout in R2025b, Hermitian or not
        // (measured: [Inf 0; 0 1] ^ 0.5, [1 2; NaN 4] ^ 0.5, [NaN 1i; 2 3] ^ 0.5).
        foreach (Complex value in a)
        {
            if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
            {
                var nan = new Complex[n, n];
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        nan[r, c] = new Complex(double.NaN, 0);
                    }
                }

                return nan;
            }
        }

        bool hermitian = IsHermitian(a, skew: false);
        if (hermitian || IsHermitian(a, skew: true))
        {
            Complex[,] basis;
            Complex[] d;
            bool dReal = hermitian;
            if (real && hermitian)
            {
                Eigen eigen = Eigen.Factor(RealPart(a));
                basis = eigen.Vectors;
                d = eigen.Values;
            }
            else
            {
                // A normal matrix's Schur form is diagonal and its basis orthonormal, which is the
                // eigenbasis this road needs even where eigenvalues repeat.
                (basis, Complex[,] t) = MatrixFunction.Triangularize(a, real);
                d = new Complex[n];
                for (int i = 0; i < n; i++)
                {
                    d[i] = hermitian ? new Complex(t[i, i].Real, 0) : new Complex(0, t[i, i].Imaginary);
                }
            }

            Complex[] raised = RaiseAll(d, dReal, p);
            return Product(ScaleColumns(basis, raised), ConjugateTranspose(basis));
        }

        Complex[,] v;
        Complex[] values;
        if (IsDiagonal(a))
        {
            // A diagonal matrix's eigenbasis is the identity, as LAPACK's zgeev returns it; the managed
            // eigensolver leaves roundoff off the diagonal ([4i 0; 0 9] ^ 0.5, ADR 0365).
            v = new Complex[n, n];
            values = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                v[i, i] = Complex.One;
                values[i] = a[i, i];
            }
        }
        else if (real)
        {
            Eigen eigen = Eigen.Factor(RealPart(a));
            v = eigen.Vectors;
            values = eigen.Values;
        }
        else
        {
            (values, v) = ComplexEigen.Factor(a);
        }

        if (p.Imaginary == 0 && double.IsFinite(p.Real) && ComplexSquareSystems.ReciprocalCondition(v) < 1e-2)
        {
            return Schur(a, p.Real, real);
        }

        bool valuesReal = AllReal(values);
        Complex[,] w = ScaleColumns(v, RaiseAll(values, valuesReal, p));

        // solve(Vfact, W', true)': Vᴴ·Zᴴ = Wᴴ, so Z = W/V — in real arithmetic when both are real,
        // as MATLAB's real LU solves it, so an infinity meets no imaginary zero ([1 2; 3 4] ^ Inf).
        if (AllRealColumnStorage(v) && AllRealColumnStorage(w))
        {
            double[] vt = PartOf(ConjugateTranspose(v), imaginary: false);
            double[] wt = PartOf(ConjugateTranspose(w), imaginary: false);
            SquareSystems.Solve(vt, n, wt, n, out _);
            var z = new Complex[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    z[c, r] = new Complex(wt[((long)c * n) + r], 0);
                }
            }

            return z;
        }

        ComplexSquareSystems.Solve(ConjugateTranspose(v), ConjugateTranspose(w), single: false, out Complex[,] zh, out _);
        return ConjugateTranspose(zh);
    }

    /// <summary>
    /// <c>integerMpower</c>: A to a whole power by repeated squaring (A¹³ = A·A⁴·A⁸), the inverse
    /// first for a negative one, with no warning; a zeroth power is the identity, or NaN when A holds one.
    /// </summary>
    public static Complex[,] IntegerPower(Complex[,] a, double k)
    {
        ArgumentNullException.ThrowIfNull(a);
        int n = a.GetLength(0);
        if (k == 0)
        {
            var z0 = new Complex[n, n];
            bool nan = false;
            foreach (Complex value in a)
            {
                nan |= double.IsNaN(value.Real) || double.IsNaN(value.Imaginary);
            }

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    z0[r, c] = nan ? new Complex(double.NaN, 0) : r == c ? Complex.One : Complex.Zero;
                }
            }

            return z0;
        }

        double power = Math.Abs(k);
        Complex[,] d = k < 0 ? InverseOrInfinity(a) : (Complex[,])a.Clone();
        Complex[,]? z = null;
        while (power > 0)
        {
            if (power % 2 == 1)
            {
                z = z is null ? d : Product(d, z);
            }

            power = Math.Floor(power / 2);
            if (power != 0)
            {
                d = Product(d, d);
            }
        }

        return z!;
    }

    /// <summary><c>mpower_schur</c>: A to a real power by the Schur–Padé algorithm.</summary>
    private static Complex[,] Schur(Complex[,] a, double p, bool real)
    {
        const int MaxSqrt = 100;
        int n = a.GetLength(0);

        // Reduce the range to -1 <= p <= 1.
        double pint = 0;
        double pfrac = p;
        if (p > 1)
        {
            pint = Math.Floor(p);
            pfrac = p - pint;
        }
        else if (p < -1)
        {
            pint = Math.Ceiling(p);
            pfrac = p - pint;
        }

        bool hasQ = !IsUpperTriangular(a);
        Complex[,] q = new Complex[0, 0];
        Complex[,] t;
        if (!hasQ)
        {
            t = (Complex[,])a.Clone();
        }
        else
        {
            (q, t) = MatrixFunction.Triangularize(a, real);
        }

        // MATLAB's isreal(T): only the matrix itself, already triangular and real, is real storage.
        bool tReal = !hasQ && real;
        Complex[,] x;
        if (n == 2)
        {
            x = PowerTwoByTwo(t, 0, pfrac, tReal);
        }
        else
        {
            Complex[,] tOld = (Complex[,])t.Clone();
            var diagT = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                diagT[i] = t[i, i];
            }

            int m = 0;
            int more = 0;
            int extra = 0;
            int s0 = MinimumSquareRoots(diagT, MaxSqrt);
            int nsq = s0;
            for (int s = 0; s < nsq; s++)
            {
                t = TriangularRoot(t);
            }

            Complex[,] tmi = MinusIdentity(t);
            double d2 = Math.Pow(OneNormOfPower(tmi, 2), 1.0 / 2);
            double d3 = Math.Pow(OneNormOfPower(tmi, 3), 1.0 / 3);
            double d4 = 0;
            double alpha2 = Math.Max(d2, d3);
            bool foundm = false;
            if (alpha2 <= Xvals[1])
            {
                m = alpha2 <= Xvals[0] ? 1 : 2;
                foundm = true;
            }

            while (!foundm)
            {
                more = 0;
                if (nsq > s0)
                {
                    d3 = Math.Pow(OneNormOfPower(tmi, 3), 1.0 / 3);
                }

                d4 = Math.Pow(OneNormOfPower(tmi, 4), 1.0 / 4);
                double alpha3 = Math.Max(d3, d4);
                if (alpha3 <= Xvals[6])
                {
                    int j = FirstAtMost(alpha3, 2, 6) + 1;
                    if (j <= 6)
                    {
                        m = j;
                        break;
                    }

                    if (alpha3 / 2 <= Xvals[4] && extra < 2)
                    {
                        more = 1;
                        extra++;
                    }
                }

                if (more == 0)
                {
                    double d5 = Math.Pow(OneNormOfPower(tmi, 5), 1.0 / 5);
                    double alpha4 = Math.Max(d4, d5);
                    double eta = Math.Min(alpha3, alpha4);
                    if (eta <= Xvals[6])
                    {
                        m = FirstAtMost(eta, 5, 6) + 1;
                        break;
                    }
                }

                if (nsq == MaxSqrt)
                {
                    m = 13;
                    break;
                }

                t = TriangularRoot(t);
                tmi = MinusIdentity(t);
                nsq++;
            }

            // An accurate superdiagonal of T^(1/2^s), then an accurate diagonal of I - T^(1/2^s).
            for (int i = 0; i < n - 1; i++)
            {
                Complex[,] block = PowerTwoByTwo(tOld, i, 1.0 / Math.Pow(2, nsq), tReal);
                PlaceBlock(t, block, i);
            }

            var r = new Complex[n, n];
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    r[row, col] = (row == col ? Complex.One : Complex.Zero) - t[row, col];
                }
            }

            Complex[] dd = SqrtPowerMinusOne(diagT, nsq);
            for (int i = 0; i < n; i++)
            {
                r[i, i] = -dd[i];
            }

            // The [m/m] Pade approximant of T^p = (I - R)^p, by its continued fraction, bottom up.
            int top = 2 * m;
            Complex[,] sj = Scale(r, Coefficient(pfrac, top));
            for (int j = top - 1; j >= 1; j--)
            {
                double cj = Coefficient(pfrac, j);
                Complex[,] left = sj;
                for (int i = 0; i < n; i++)
                {
                    left[i, i] += Complex.One;
                }

                sj = Scale(UpperTriangularSolve(left, r), cj);
            }

            x = sj;
            for (int i = 0; i < n; i++)
            {
                x[i, i] += Complex.One;
            }

            // The squaring phase, with the diagonal and superdiagonal computed directly.
            for (int s = 0; s <= nsq; s++)
            {
                if (s != 0)
                {
                    x = Product(x, x);
                }

                for (int i = 0; i < n - 1; i++)
                {
                    PlaceBlock(x, PowerTwoByTwo(tOld, i, pfrac / Math.Pow(2, nsq - s), tReal), i);
                }
            }
        }

        if (hasQ)
        {
            x = Product(Product(q, x), ConjugateTranspose(q));
        }

        if (real)
        {
            x = MakeReal(x);
        }

        if (pint != 0)
        {
            x = Product(IntegerPower(a, pint), x);
        }

        return x;
    }

    /// <summary>
    /// <c>powerm2by2</c>: the 2-by-2 upper triangular block of <paramref name="t"/> at (i, i) to the
    /// real power <paramref name="p"/>.
    /// </summary>
    private static Complex[,] PowerTwoByTwo(Complex[,] t, int i, double p, bool real)
    {
        Complex a1 = t[i, i];
        Complex a2 = t[i + 1, i + 1];
        Complex a12 = t[i, i + 1];
        Complex a1p = Raise(a1, real, p);
        Complex a2p = Raise(a2, real, p);
        var x = new Complex[2, 2];
        x[0, 0] = a1p;
        x[1, 1] = a2p;
        if (a1 == a2)
        {
            x[0, 1] = Multiply(Multiply(p, a12, real), Raise(a1, real, p - 1));
            return x;
        }

        Complex z = (a2 - a1) / (a2 + a1);
        if (real || CheckCondition(z))
        {
            x[0, 1] = ComplexSquareSystems.Divide(a12 * (a2p - a1p), a2 - a1);
        }
        else
        {
            // Close eigenvalues.
            Complex loga1 = Complex.Log(a1);
            Complex loga2 = Complex.Log(a2);
            Complex w = Atanh(z) + (Complex.ImaginaryOne * Math.PI * Unwinding(loga2 - loga1));
            Complex dd = 2 * Complex.Exp(p * (loga1 + loga2) / 2) * Sinh(p * w) / (a2 - a1);
            x[0, 1] = a12 * dd;
        }

        return x;
    }

    /// <summary>A real scalar times a number, as MATLAB multiplies a real by a complex: part by part.</summary>
    private static Complex Multiply(double s, Complex z, bool real) =>
        real || z.Imaginary == 0 ? new Complex(s * z.Real, 0) : new Complex(s * z.Real, s * z.Imaginary);

    /// <summary>
    /// A number times another, part by part when either is real, so an infinity meets no zero
    /// imaginary part (0.5 · 0^-0.5 is Inf, not Inf + NaN·i).
    /// </summary>
    private static Complex Multiply(Complex a, Complex b) =>
        a.Imaginary == 0 && b.Imaginary == 0 ? new Complex(a.Real * b.Real, 0)
        : b.Imaginary == 0 ? new Complex(a.Real * b.Real, a.Imaginary * b.Real)
        : a.Imaginary == 0 ? new Complex(a.Real * b.Real, a.Real * b.Imaginary)
        : a * b;

    /// <summary><c>check_condition</c>: whether z is in the range where the plain quotient is safe.</summary>
    private static bool CheckCondition(Complex z)
    {
        double tol = Math.Pow(Eps, 1.0 / 8);
        double rtol = 1 / tol;
        return z.Magnitude > rtol || (z - 1).Magnitude < tol || (z + 1).Magnitude < tol;
    }

    private static double Unwinding(Complex z) => Math.Ceiling((z.Imaginary - Math.PI) / (2 * Math.PI));

    private static Complex Atanh(Complex z) => (Complex.Log(1 + z) - Complex.Log(1 - z)) / 2.0;

    private static Complex Sinh(Complex z) => Complex.Sinh(z);

    /// <summary><c>min_sqrts</c>: how many square roots bring the diagonal within the largest Padé radius of one.</summary>
    private static int MinimumSquareRoots(Complex[] diagonal, int maxSqrt)
    {
        int ss = 0;
        double sum = 0;
        foreach (Complex value in diagonal)
        {
            sum += value.Magnitude;
        }

        if (sum == 0)
        {
            return 0;
        }

        Complex[] d = (Complex[])diagonal.Clone();
        while (LargestDistanceFromOne(d) > Xvals[6] && ss < maxSqrt)
        {
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = ScalarSqrt(d[i]);
            }

            ss++;
        }

        return Math.Min(ss, maxSqrt);
    }

    private static double LargestDistanceFromOne(Complex[] d)
    {
        double largest = 0;
        foreach (Complex value in d)
        {
            double distance = (value - 1).Magnitude;
            if (double.IsNaN(distance))
            {
                return double.NaN;
            }

            largest = Math.Max(largest, distance);
        }

        return largest;
    }

    /// <summary>The first index k in [from, to] (zero-based) with value &lt;= Xvals[k], as MATLAB's find(·, 1) reads it.</summary>
    private static int FirstAtMost(double value, int from, int to)
    {
        for (int k = from; k <= to; k++)
        {
            if (value <= Xvals[k])
            {
                return k;
            }
        }

        return to + 1;
    }

    /// <summary><c>coeff</c>: the i-th coefficient of the continued fraction for the [m/m] Padé approximant to (1 − x)^p.</summary>
    private static double Coefficient(double p, int i)
    {
        if (i == 1)
        {
            return -p;
        }

        double jj = i / 2.0;
        if (jj == Math.Round(jj))
        {
            return (-jj + p) / (2 * ((2 * jj) - 1));
        }

        jj = Math.Floor(jj);
        return (-jj - p) / (2 * ((2 * jj) + 1));
    }

    /// <summary><c>sqrt_power_1</c>: a^(1/2^n) − 1, accurately (Al-Mohy, 2012).</summary>
    private static Complex[] SqrtPowerMinusOne(Complex[] values, int n)
    {
        Complex[] a = (Complex[])values.Clone();
        var r = new Complex[a.Length];
        if (n == 0)
        {
            for (int i = 0; i < a.Length; i++)
            {
                r[i] = a[i] - 1;
            }

            return r;
        }

        int n0 = n;

        // MATLAB's `if angle(a) >= pi/2` over a vector holds only when it holds for every element.
        bool all = true;
        foreach (Complex value in a)
        {
            all &= Math.Atan2(value.Imaginary, value.Real) >= Math.PI / 2;
        }

        if (all)
        {
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = ScalarSqrt(a[i]);
            }

            n0 = n - 1;
        }

        var z0 = new Complex[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            z0[i] = a[i] - 1;
            a[i] = ScalarSqrt(a[i]);
            r[i] = 1 + a[i];
        }

        for (int k = 1; k < n0; k++)
        {
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = ScalarSqrt(a[i]);
                r[i] *= 1 + a[i];
            }
        }

        for (int i = 0; i < a.Length; i++)
        {
            r[i] = z0[i] / r[i];
        }

        return r;
    }

    /// <summary>The principal square root, exact where the number is real.</summary>
    private static Complex ScalarSqrt(Complex z)
    {
        if (z.Imaginary == 0)
        {
            return z.Real >= 0 || double.IsNaN(z.Real)
                ? new Complex(Math.Sqrt(z.Real + 0.0), 0)
                : new Complex(0, Math.Sqrt(-z.Real));
        }

        // t = sqrt((|x| + |z|) / 2) carries the larger part and the other is y / 2t; a pure
        // imaginary number has two equal parts, sqrt(|y| / 2) each. Both are R2025b's sqrt to the
        // bit (measured over fourteen values), where .NET's Complex.Sqrt loses the last bit of
        // sqrt(4i).
        double x = z.Real;
        double y = z.Imaginary;
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return Complex.Sqrt(z);
        }

        if (x == 0)
        {
            double half = Math.Sqrt(Math.Abs(y) / 2);
            return new Complex(half, Math.CopySign(half, y));
        }

        double t = Math.Sqrt((Math.Abs(x) + double.Hypot(x, y)) / 2);
        return x >= 0
            ? new Complex(t, y / (2 * t))
            : new Complex(Math.Abs(y) / (2 * t), Math.CopySign(t, y));
    }

    /// <summary>The square root of an upper triangular matrix, by the recurrence on its entries.</summary>
    private static Complex[,] TriangularRoot(Complex[,] t)
    {
        int n = t.GetLength(0);
        var flat = new Complex[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r <= c; r++)
            {
                flat[((long)c * n) + r] = t[r, c];
            }
        }

        Complex[] root = MatrixRoots.TriangularSquareRoot(flat, n);
        var result = new Complex[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r <= c; r++)
            {
                result[r, c] = root[((long)c * n) + r];
            }
        }

        return result;
    }

    /// <summary>(I + S) \ R for upper triangular I + S, by back substitution down each column of R.</summary>
    private static Complex[,] UpperTriangularSolve(Complex[,] u, Complex[,] b)
    {
        int n = u.GetLength(0);
        int cols = b.GetLength(1);
        var x = (Complex[,])b.Clone();
        for (int c = 0; c < cols; c++)
        {
            for (int k = n - 1; k >= 0; k--)
            {
                x[k, c] = ComplexSquareSystems.Divide(x[k, c], u[k, k]);
                Complex xk = x[k, c];
                for (int i = 0; i < k; i++)
                {
                    x[i, c] -= xk * u[i, k];
                }
            }
        }

        return x;
    }

    /// <summary>‖Aᵐ‖₁, with Aᵐ by <see cref="IntegerPower"/>.</summary>
    private static double OneNormOfPower(Complex[,] a, int m) => OneNorm(IntegerPower(a, m));

    private static double OneNorm(Complex[,] a)
    {
        double best = 0;
        for (int c = 0; c < a.GetLength(1); c++)
        {
            double sum = 0;
            for (int r = 0; r < a.GetLength(0); r++)
            {
                sum += a[r, c].Magnitude;
            }

            if (double.IsNaN(sum))
            {
                return double.NaN;
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    /// <summary><c>make_real</c>: the real part, when the imaginary part is roundoff.</summary>
    private static Complex[,] MakeReal(Complex[,] x)
    {
        int n = x.GetLength(0);
        double imaginary = 0;
        for (int c = 0; c < x.GetLength(1); c++)
        {
            double sum = 0;
            for (int r = 0; r < n; r++)
            {
                sum += Math.Abs(x[r, c].Imaginary);
            }

            imaginary = double.IsNaN(sum) ? double.NaN : Math.Max(imaginary, sum);
        }

        if (!(imaginary <= 10 * n * Eps * OneNorm(x)))
        {
            return x;
        }

        var real = new Complex[n, x.GetLength(1)];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < x.GetLength(1); c++)
            {
                real[r, c] = new Complex(x[r, c].Real, 0);
            }
        }

        return real;
    }

    /// <summary>Each eigenvalue to the power, as MATLAB's <c>d.^p</c> raises a real or a complex vector.</summary>
    private static Complex[] RaiseAll(Complex[] d, bool real, Complex p)
    {
        var raised = new Complex[d.Length];
        for (int i = 0; i < d.Length; i++)
        {
            raised[i] = Raise(d[i], real, p);
        }

        return raised;
    }

    /// <summary>
    /// One number to a power as MATLAB's <c>.^</c> answers it: a real base with a real power stays
    /// real unless it is negative and the power fractional; 0.5 is the root and -1 the reciprocal.
    /// </summary>
    private static Complex Raise(Complex z, bool real, Complex p)
    {
        if (p.Imaginary == 0)
        {
            double e = p.Real;
            if (real && (z.Real >= 0 || double.IsNaN(z.Real) || double.IsNaN(e) || e == Math.Floor(e)))
            {
                return new Complex(PackedMath.Power(z.Real, e), 0);
            }

            if (e == 0.5)
            {
                return ScalarSqrt(z);
            }

            if (e == -1)
            {
                return Complex.One / z;
            }
        }

        return Complex.Pow(z, p);
    }

    private static Complex[,] ScaleColumns(Complex[,] v, Complex[] s)
    {
        int rows = v.GetLength(0);
        int cols = v.GetLength(1);
        bool realV = AllRealColumnStorage(v);
        bool realS = AllReal(s);
        var w = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                w[r, c] = realV
                    ? new Complex(v[r, c].Real * s[c].Real, realS ? 0 : v[r, c].Real * s[c].Imaginary)
                    : realS ? new Complex(v[r, c].Real * s[c].Real, v[r, c].Imaginary * s[c].Real) : v[r, c] * s[c];
            }
        }

        return w;
    }

    private static bool AllRealColumnStorage(Complex[,] v)
    {
        foreach (Complex value in v)
        {
            if (value.Imaginary != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static Complex[,] Scale(Complex[,] a, double s)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var result = new Complex[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                result[r, c] = new Complex(s * a[r, c].Real, s * a[r, c].Imaginary);
            }
        }

        return result;
    }

    private static void PlaceBlock(Complex[,] target, Complex[,] block, int i)
    {
        target[i, i] = block[0, 0];
        target[i, i + 1] = block[0, 1];
        target[i + 1, i] = block[1, 0];
        target[i + 1, i + 1] = block[1, 1];
    }

    private static Complex[,] MinusIdentity(Complex[,] t)
    {
        var result = (Complex[,])t.Clone();
        for (int i = 0; i < t.GetLength(0); i++)
        {
            result[i, i] -= Complex.One;
        }

        return result;
    }

    /// <summary>
    /// The product of two matrices; a real one multiplies part by part, as MATLAB multiplies a real
    /// matrix by a complex one, so an infinity meets no zero imaginary part it was never given.
    /// </summary>
    private static Complex[,] Product(Complex[,] a, Complex[,] b)
    {
        bool aReal = AllRealColumnStorage(a);
        bool bReal = AllRealColumnStorage(b);
        int m = a.GetLength(0);
        int inner = a.GetLength(1);
        int n = b.GetLength(1);
        if (m == 0 || n == 0 || inner == 0)
        {
            return new Complex[m, n];
        }

        // An infinity or a NaN must meet every zero as MKL's product meets it (Inf*0 is NaN); the
        // managed backend's kernels skip a zero entry, which left zeros(2)^-0.5 [Inf NaN; NaN Inf]
        // where R2025b answers NaN throughout (ADR 0365, found in the managed lane at landing).
        if (!AllFinite(a) || !AllFinite(b))
        {
            return PlainProduct(a, b, aReal, bReal);
        }

        if (!aReal && !bReal)
        {
            return ComplexLinear.Multiply(a, b);
        }

        double[] reA = PartOf(a, imaginary: false);
        double[] reB = PartOf(b, imaginary: false);
        double[] real = DenseProduct.ColumnMajor(reA, m, inner, reB, n);
        double[]? imaginary = null;
        if (!aReal)
        {
            imaginary = DenseProduct.ColumnMajor(PartOf(a, imaginary: true), m, inner, reB, n);
        }
        else if (!bReal)
        {
            imaginary = DenseProduct.ColumnMajor(reA, m, inner, PartOf(b, imaginary: true), n);
        }

        var result = new Complex[m, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                long at = ((long)c * m) + r;
                result[r, c] = new Complex(real[at], imaginary is null ? 0 : imaginary[at]);
            }
        }

        return result;
    }

    private static bool AllFinite(Complex[,] a)
    {
        foreach (Complex z in a)
        {
            if (!double.IsFinite(z.Real) || !double.IsFinite(z.Imaginary))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The product by its definition, every term summed and none skipped; a real factor multiplies the
    /// other part by part, as <see cref="Product"/> does.
    /// </summary>
    private static Complex[,] PlainProduct(Complex[,] a, Complex[,] b, bool aReal, bool bReal)
    {
        int m = a.GetLength(0);
        int inner = a.GetLength(1);
        int n = b.GetLength(1);
        var result = new Complex[m, n];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double re = 0;
                double im = 0;
                for (int k = 0; k < inner; k++)
                {
                    Complex x = a[r, k];
                    Complex y = b[k, c];
                    if (aReal)
                    {
                        re += x.Real * y.Real;
                        im += bReal ? 0 : x.Real * y.Imaginary;
                    }
                    else if (bReal)
                    {
                        re += x.Real * y.Real;
                        im += x.Imaginary * y.Real;
                    }
                    else
                    {
                        re += (x.Real * y.Real) - (x.Imaginary * y.Imaginary);
                        im += (x.Real * y.Imaginary) + (x.Imaginary * y.Real);
                    }
                }

                result[r, c] = new Complex(re, im);
            }
        }

        return result;
    }

    private static double[] PartOf(Complex[,] a, bool imaginary)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var flat = new double[(long)rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[((long)c * rows) + r] = imaginary ? a[r, c].Imaginary : a[r, c].Real;
            }
        }

        return flat;
    }

    private static Complex[,] ConjugateTranspose(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var t = new Complex[cols, rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                t[c, r] = Complex.Conjugate(a[r, c]);
            }
        }

        return t;
    }

    /// <summary>The inverse, or Inf throughout where the matrix is singular (MATLAB's inv, unwarned).</summary>
    private static Complex[,] InverseOrInfinity(Complex[,] a)
    {
        int n = a.GetLength(0);
        var identity = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = Complex.One;
        }

        SquareCondition condition = ComplexSquareSystems.Solve(a, identity, single: false, out Complex[,] inverse, out _);
        if (condition == SquareCondition.Singular)
        {
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    inverse[r, c] = new Complex(double.PositiveInfinity, 0);
                }
            }
        }

        return inverse;
    }

    private static double[,] RealPart(Complex[,] a)
    {
        int n = a.GetLength(0);
        var real = new double[n, a.GetLength(1)];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                real[r, c] = a[r, c].Real;
            }
        }

        return real;
    }

    private static bool AllReal(Complex[] values)
    {
        foreach (Complex value in values)
        {
            if (value.Imaginary != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDiagonal(Complex[,] a)
    {
        int n = a.GetLength(0);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (r != c && a[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsUpperTriangular(Complex[,] a)
    {
        int n = a.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>MATLAB's ishermitian(A) (or ishermitian(A, 'skew')): exact, entry by entry.</summary>
    private static bool IsHermitian(Complex[,] a, bool skew)
    {
        int n = a.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            for (int r = c; r < n; r++)
            {
                Complex mirrored = Complex.Conjugate(a[c, r]);
                if (a[r, c] != (skew ? -mirrored : mirrored))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
