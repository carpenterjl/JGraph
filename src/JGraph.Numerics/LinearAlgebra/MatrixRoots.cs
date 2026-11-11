using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The principal square root and the principal logarithm of a real square matrix, by the Schur
/// method: triangularize once, take the function of the triangle, and carry the answer back.
/// </summary>
/// <remarks>
/// <para>
/// Both names used to be iterations over the full matrix (the every-row plan's I4, ADR 0329): the
/// root was Denman-Beavers, two inversions a step, and the logarithm took that root until the matrix
/// was near the identity and then summed thirty-two terms of a series. On a triangle the root is a
/// recurrence over its entries (Björck and Hammarling) and the logarithm is inverse scaling and
/// squaring with a Padé approximant, each a third of a matrix product; the one cubic step that is
/// left is the Schur form itself, which LAPACK does.
/// </para>
/// <para>
/// A symmetric matrix does not need the triangle at all: its Schur form is diagonal, so the
/// function is taken of the eigenvalues and the eigenvectors carry it back, which is the branch
/// MATLAB's own <c>sqrtm</c> and <c>logm</c> take.
/// </para>
/// <para>
/// A real matrix with complex eigenvalues is triangularized in real arithmetic and its two-by-two
/// blocks are then turned complex by one rotation each (<c>rsf2csf</c>'s rotations, kept). The
/// rotations are undone on the answer rather than applied to the Schur vectors, so the vectors stay
/// real and the way back is two real matrix products rather than two complex ones.
/// </para>
/// <para>
/// The triangular kernels are threaded over pieces that own their outputs outright (a superdiagonal
/// of the root, a row of the Padé solves), and every entry is one dot product computed whole on one
/// thread, so the answer does not depend on the thread count.
/// </para>
/// </remarks>
public static class MatrixRoots
{
    /// <summary>
    /// The answer, real or complex, and what was noticed on the way.
    /// </summary>
    /// <param name="Real">The n-by-n answer, column-major, when it is real.</param>
    /// <param name="Complex">The n-by-n answer, column-major, when it is not.</param>
    /// <param name="Singular">Whether a zero eigenvalue was met.</param>
    /// <param name="NonPositive">Whether a real eigenvalue at or below zero was met.</param>
    public sealed record Answer(double[]? Real, Complex[]? Complex, bool Singular, bool NonPositive);

    /// <summary>Square roots allowed before the logarithm's scaling gives up.</summary>
    private const int MaxSquareRoots = 100;

    /// <summary>The principal square root of an n-by-n column-major real matrix.</summary>
    public static Answer SquareRoot(ReadOnlySpan<double> a, int n) => Evaluate(a, n, logarithm: false);

    /// <summary>The principal logarithm of an n-by-n column-major real matrix.</summary>
    public static Answer Logarithm(ReadOnlySpan<double> a, int n) => Evaluate(a, n, logarithm: true);

    private static Answer Evaluate(ReadOnlySpan<double> a, int n, bool logarithm)
    {
        if (n == 0)
        {
            return new Answer([], null, false, false);
        }

        a = a[..(n * n)];
        if (IsUpperTriangular(a, n))
        {
            return OfTriangle(a, n, logarithm);
        }

        if (IsSymmetric(a, n))
        {
            double[] vectors = a.ToArray();
            var values = new double[n];
            if (LinalgProvider.Current.Syevd(vectors: true, lower: true, n, vectors, n, values) != 0)
            {
                throw new InvalidOperationException("The symmetric eigensolver did not converge.");
            }

            return Spectral(vectors, values, n, logarithm);
        }

        double[] t = a.ToArray();
        var q = new double[(long)n * n];
        var wr = new double[n];
        var wi = new double[n];
        if (LinalgProvider.Current.Gees(vectors: true, n, t, n, wr, wi, q, n) != 0)
        {
            throw new InvalidOperationException("The Schur iteration did not converge.");
        }

        bool singular = false;
        bool nonPositive = false;
        bool negative = false;
        for (int i = 0; i < n; i++)
        {
            if (wi[i] != 0)
            {
                continue;
            }

            singular |= wr[i] == 0;
            nonPositive |= wr[i] <= 0;
            negative |= wr[i] < 0;
        }

        Complex[] triangle = Widen(t, n);
        List<(int At, Complex Cosine, Complex Sine)> rotations = TriangularizeBlocks(triangle, n);
        Complex[] f = logarithm ? TriangularLogarithm(triangle, n) : TriangularSquareRoot(triangle, n);
        UndoRotations(f, n, rotations);

        // A real matrix with no eigenvalue on the closed negative real axis has a real principal
        // root and logarithm, so every imaginary part still in the answer is the rotations' own
        // rounding and is dropped rather than carried back.
        double[] real = Sandwich(q, Part(f, imaginary: false), n);
        if (!negative)
        {
            return new Answer(real, null, singular, nonPositive);
        }

        double[] imaginary = Sandwich(q, Part(f, imaginary: true), n);
        return new Answer(null, Join(real, imaginary), singular, nonPositive);
    }

    // --- the three shapes of input ---------------------------------------------------------------

    /// <summary>A matrix that is already upper triangular: no Schur form, and no way back.</summary>
    private static Answer OfTriangle(ReadOnlySpan<double> a, int n, bool logarithm)
    {
        bool singular = false;
        bool nonPositive = false;
        bool negative = false;
        bool diagonal = true;
        for (int c = 0; c < n; c++)
        {
            double d = a[(c * n) + c];
            singular |= d == 0;
            nonPositive |= d <= 0;
            negative |= d < 0;
            for (int r = 0; r < c && diagonal; r++)
            {
                diagonal = a[(c * n) + r] == 0;
            }
        }

        Complex[] f;
        if (diagonal)
        {
            // Entry by entry, so that the root of a diagonal of exact squares is exact and two zeros
            // on the diagonal never meet in the recurrence's divisor.
            f = new Complex[n * n];
            for (int i = 0; i < n; i++)
            {
                f[(i * n) + i] = OfScalar(a[(i * n) + i], logarithm);
            }
        }
        else
        {
            Complex[] triangle = Widen(a, n);
            f = logarithm ? TriangularLogarithm(triangle, n) : TriangularSquareRoot(triangle, n);
        }

        return negative
            ? new Answer(null, f, singular, nonPositive)
            : new Answer(Part(f, imaginary: false), null, singular, nonPositive);
    }

    /// <summary>A symmetric matrix: the function of the eigenvalues, carried back by the vectors.</summary>
    private static Answer Spectral(double[] v, double[] w, int n, bool logarithm)
    {
        bool singular = false;
        bool nonPositive = false;
        bool negative = false;
        foreach (double value in w)
        {
            singular |= value == 0;
            nonPositive |= value <= 0;
            negative |= value < 0;
        }

        DenseLinalg backend = LinalgProvider.Current;
        var scaled = new double[(long)n * n];
        var real = new double[(long)n * n];
        if (!logarithm && !negative)
        {
            // V·diag(√w)·Vᵀ is B·Bᵀ with B = V·diag(w^¼): one triangle computed and mirrored, so
            // the root of a symmetric matrix is exactly symmetric and costs half a product.
            ScaleColumns(v, scaled, n, w, static value => Math.Sqrt(Math.Sqrt(value)));
            backend.Syrk(transposeFirst: false, n, n, scaled, n, real, n);
            return new Answer(real, null, singular, nonPositive);
        }

        ScaleColumns(v, scaled, n, w, logarithm
            ? static value => value == 0 ? double.NegativeInfinity : Math.Log(Math.Abs(value))
            : static value => value >= 0 ? Math.Sqrt(value) : 0.0);
        backend.Gemm(false, true, n, n, n, scaled, n, v, n, real, n);
        Symmetrize(real, n);
        if (!negative)
        {
            return new Answer(real, null, singular, nonPositive);
        }

        var imaginary = new double[(long)n * n];
        ScaleColumns(v, scaled, n, w, logarithm
            ? static value => value < 0 ? Math.PI : 0.0
            : static value => value < 0 ? Math.Sqrt(-value) : 0.0);
        backend.Gemm(false, true, n, n, n, scaled, n, v, n, imaginary, n);
        Symmetrize(imaginary, n);
        return new Answer(null, Join(real, imaginary), singular, nonPositive);
    }

    private static void ScaleColumns(double[] v, double[] scaled, int n, double[] w, Func<double, double> f)
    {
        for (int c = 0; c < n; c++)
        {
            double by = f(w[c]);
            int at = c * n;
            for (int r = 0; r < n; r++)
            {
                scaled[at + r] = v[at + r] * by;
            }
        }
    }

    private static void Symmetrize(double[] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < c; r++)
            {
                double mean = 0.5 * (a[(c * n) + r] + a[(r * n) + c]);
                a[(c * n) + r] = mean;
                a[(r * n) + c] = mean;
            }
        }
    }

    /// <summary>The function of one real number, on the principal branch.</summary>
    private static Complex OfScalar(double value, bool logarithm)
    {
        if (logarithm)
        {
            return value > 0 ? new Complex(Math.Log(value), 0.0)
                : value == 0 ? new Complex(double.NegativeInfinity, 0.0)
                : value < 0 ? new Complex(Math.Log(-value), Math.PI)
                : new Complex(double.NaN, 0.0);
        }

        return value >= 0 ? new Complex(Math.Sqrt(value), 0.0)
            : value < 0 ? new Complex(0.0, Math.Sqrt(-value))
            : new Complex(double.NaN, 0.0);
    }

    /// <summary>The function of one complex number, exact where the number is real.</summary>
    private static Complex OfScalar(Complex value, bool logarithm) =>
        value.Imaginary == 0 ? OfScalar(value.Real, logarithm)
        : logarithm ? Complex.Log(value)
        : Complex.Sqrt(value);

    // --- real Schur form to complex and back -----------------------------------------------------

    /// <summary>
    /// Turns each two-by-two block of a real Schur form into a complex triangle by one rotation, in
    /// place, and answers the rotations so that they can be undone on a function of the triangle.
    /// </summary>
    /// <remarks>
    /// The rotation is <see cref="SchurConversion.RealToComplex"/>'s, taken from the block's
    /// eigenvalue with the positive imaginary part. The blocks of a real Schur form do not overlap,
    /// so the rotations commute and the order they are undone in does not matter.
    /// </remarks>
    private static List<(int At, Complex Cosine, Complex Sine)> TriangularizeBlocks(Complex[] t, int n)
    {
        var rotations = new List<(int, Complex, Complex)>();
        for (int m = n - 1; m >= 1; m--)
        {
            Complex below = t[((m - 1) * n) + m];
            if (below == Complex.Zero)
            {
                continue;
            }

            Complex mu = SchurConversion.FirstEigenvalue(
                t[((m - 1) * n) + m - 1], t[(m * n) + m - 1], below, t[(m * n) + m]) - t[(m * n) + m];
            double r = double.Hypot(mu.Magnitude, below.Magnitude);
            if (r == 0)
            {
                t[((m - 1) * n) + m] = Complex.Zero;
                continue;
            }

            Complex c = new(mu.Real / r, mu.Imaginary / r);
            Complex s = new(below.Real / r, below.Imaginary / r);
            Complex cc = Complex.Conjugate(c);
            Complex sc = Complex.Conjugate(s);

            // Rows m-1 and m: T := G·T with G = [conj(c) s; -s c].
            for (int j = m - 1; j < n; j++)
            {
                Complex x = t[(j * n) + m - 1];
                Complex y = t[(j * n) + m];
                t[(j * n) + m - 1] = (cc * x) + (s * y);
                t[(j * n) + m] = (c * y) - (s * x);
            }

            // Columns m-1 and m: T := T·Gᴴ.
            int left = (m - 1) * n;
            int right = m * n;
            for (int i = 0; i <= m; i++)
            {
                Complex x = t[left + i];
                Complex y = t[right + i];
                t[left + i] = (x * c) + (y * sc);
                t[right + i] = (y * cc) - (x * sc);
            }

            t[left + m] = Complex.Zero;
            rotations.Add((m, c, s));
        }

        return rotations;
    }

    /// <summary>F := Ĝᴴ·F·Ĝ: a function of the complex triangle taken back to the real Schur basis.</summary>
    private static void UndoRotations(Complex[] f, int n, List<(int At, Complex Cosine, Complex Sine)> rotations)
    {
        foreach ((int m, Complex c, Complex s) in rotations)
        {
            Complex cc = Complex.Conjugate(c);
            Complex sc = Complex.Conjugate(s);

            // Rows m-1 and m: F := Gᴴ·F with Gᴴ = [c -conj(s); conj(s) conj(c)].
            for (int j = m - 1; j < n; j++)
            {
                Complex x = f[(j * n) + m - 1];
                Complex y = f[(j * n) + m];
                f[(j * n) + m - 1] = (c * x) - (sc * y);
                f[(j * n) + m] = (sc * x) + (cc * y);
            }

            // Columns m-1 and m: F := F·G.
            int left = (m - 1) * n;
            int right = m * n;
            for (int i = 0; i <= m; i++)
            {
                Complex x = f[left + i];
                Complex y = f[right + i];
                f[left + i] = (x * cc) - (y * s);
                f[right + i] = (x * s) + (y * c);
            }
        }
    }

    /// <summary>Q·F·Qᵀ for a real orthogonal Q, by two matrix products.</summary>
    private static double[] Sandwich(double[] q, double[] f, int n)
    {
        DenseLinalg backend = LinalgProvider.Current;
        var half = new double[(long)n * n];
        backend.Gemm(false, false, n, n, n, q, n, f, n, half, n);
        backend.Gemm(false, true, n, n, n, half, n, q, n, f, n);
        return f;
    }

    // --- the triangular kernels ------------------------------------------------------------------

    /// <summary>
    /// The square root of an upper triangular matrix, by the recurrence on its entries, taken a
    /// superdiagonal at a time.
    /// </summary>
    /// <remarks>
    /// Entry (i, j) needs row i and column j of the root between the two, all of which lie on
    /// superdiagonals nearer the diagonal. So a whole superdiagonal can be found at once, and its
    /// entries are handed out across threads. The root is kept twice, by columns and by rows, so
    /// that both halves of every dot product are contiguous.
    /// </remarks>
    internal static Complex[] TriangularSquareRoot(Complex[] t, int n)
    {
        var byColumns = new Complex[(long)n * n];
        var byRows = new Complex[(long)n * n];
        for (int j = 0; j < n; j++)
        {
            Complex d = OfScalar(t[(j * n) + j], logarithm: false);
            byColumns[(j * n) + j] = d;
            byRows[(j * n) + j] = d;
        }

        for (int d = 1; d < n; d++)
        {
            int count = n - d;
            int distance = d;
            bool threaded = d >= 8 && (long)count * d >= 16384;
            ParallelKernels.ForBlocks(count, threaded, i => RootEntry(t, byColumns, byRows, n, i, i + distance));
        }

        return byColumns;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void RootEntry(Complex[] t, Complex[] byColumns, Complex[] byRows, int n, int i, int j)
    {
        int between = j - i - 1;
        Complex sum = t[(j * n) + i];
        if (between > 0)
        {
            sum -= Dot(byRows.AsSpan((i * n) + i + 1, between), byColumns.AsSpan((j * n) + i + 1, between));
        }

        Complex value = Divide(sum, byColumns[(i * n) + i] + byColumns[(j * n) + j]);
        byColumns[(j * n) + i] = value;
        byRows[(i * n) + j] = value;
    }

    /// <summary>
    /// A quotient that divides by a real divisor the way real arithmetic does, so that a zero
    /// divisor answers an infinity or a NaN part by part rather than a NaN for the whole number.
    /// </summary>
    private static Complex Divide(Complex numerator, Complex divisor) =>
        divisor.Imaginary != 0 ? numerator / divisor
        : new Complex(numerator.Real / divisor.Real,
            numerator.Imaginary == 0 ? 0.0 : numerator.Imaginary / divisor.Real);

    /// <summary>
    /// The logarithm of an upper triangular matrix by inverse scaling and squaring: square roots
    /// until the triangle is near the identity, a Padé approximant there, and the doubling back.
    /// </summary>
    /// <remarks>
    /// The thresholds and the choice of degree are <see cref="MatrixFunction"/>'s, which
    /// <c>funm(A, @log)</c> has always used; only the kernels under them differ.
    /// </remarks>
    internal static Complex[] TriangularLogarithm(Complex[] t, int n)
    {
        if (n == 1)
        {
            return [OfScalar(t[0], logarithm: true)];
        }

        double[] limits =
        [
            1.6206284795015669e-002,
            5.3873532631381268e-002,
            1.1352802267628663e-001,
            1.8662860613541296e-001,
            2.6429608311114350e-001,
        ];

        Complex[] work = t;
        int roots = 0;
        int degree;
        int passes = 0;
        while (true)
        {
            double distance = OneNormFromIdentity(work, n);
            if (distance <= limits[^1])
            {
                passes++;
                int first = Position(limits, distance) + 3;
                int second = Position(limits, distance / 2) + 3;
                if (first - second < 2 || passes == 2)
                {
                    degree = first;
                    break;
                }
            }

            if (roots == MaxSquareRoots)
            {
                degree = 16;
                break;
            }

            work = TriangularSquareRoot(work, n);
            roots++;
        }

        Complex[] value = PadeLogarithm(work, n, degree);
        if (roots > 0)
        {
            double scale = Math.Pow(2.0, roots);
            for (int i = 0; i < value.Length; i++)
            {
                value[i] *= scale;
            }
        }

        // The diagonal of a triangle's logarithm is the logarithm of its diagonal, and taking it
        // there directly keeps log(1) an exact zero and a negative eigenvalue's imaginary part an
        // exact π, neither of which the approximant promises.
        for (int i = 0; i < n; i++)
        {
            value[(i * n) + i] = OfScalar(t[(i * n) + i], logarithm: true);
        }

        return value;
    }

    /// <summary>
    /// The Gauss-Legendre form of the Padé approximant to <c>log(I + A)</c>: the sum over the nodes
    /// of <c>w·A·(I + s·A)⁻¹</c>, each a triangular solve from the right.
    /// </summary>
    /// <remarks>
    /// A solve from the right is a forward substitution along each row, and no row reads another,
    /// so the rows are handed out across threads and each takes every node in turn.
    /// </remarks>
    private static Complex[] PadeLogarithm(Complex[] work, int n, int degree)
    {
        var a = new Complex[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            int at = c * n;
            for (int r = 0; r <= c; r++)
            {
                a[at + r] = work[at + r];
            }

            a[at + c] -= Complex.One;
        }

        (double[] nodes, double[] weights) = MatrixFunction.GaussLegendre(degree);
        var sum = new Complex[(long)n * n];
        ParallelKernels.ForBlocks(n, n >= 48, row => PadeRow(a, sum, n, row, nodes, weights));
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void PadeRow(Complex[] a, Complex[] sum, int n, int row, double[] nodes, double[] weights)
    {
        var x = new Complex[n];
        for (int j = 0; j < nodes.Length; j++)
        {
            double node = (nodes[j] + 1) / 2;
            double weight = weights[j] / 2;
            for (int c = row; c < n; c++)
            {
                int at = c * n;
                Complex value = a[at + row];
                if (c > row)
                {
                    value -= node * Dot(x.AsSpan(row, c - row), a.AsSpan(at + row, c - row));
                }

                value /= Complex.One + (node * a[at + c]);
                x[c] = value;
                sum[at + row] += weight * value;
            }
        }
    }

    /// <summary>The unconjugated dot product of two complex vectors.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Complex Dot(ReadOnlySpan<Complex> a, ReadOnlySpan<Complex> b)
    {
        int length = a.Length;
        ref double pa = ref Unsafe.As<Complex, double>(ref MemoryMarshal.GetReference(a));
        ref double pb = ref Unsafe.As<Complex, double>(ref MemoryMarshal.GetReference(b));
        double real = 0.0;
        double imaginary = 0.0;
        int i = 0;
        if (Vector256.IsHardwareAccelerated && length >= 4)
        {
            // Two complex numbers a vector: the straight product holds ar·br and ai·bi, and the
            // product against the swapped partner holds ar·bi and ai·br.
            Vector256<double> straight0 = Vector256<double>.Zero;
            Vector256<double> crossed0 = Vector256<double>.Zero;
            Vector256<double> straight1 = Vector256<double>.Zero;
            Vector256<double> crossed1 = Vector256<double>.Zero;
            for (; i + 4 <= length; i += 4)
            {
                nuint at = (nuint)(2 * i);
                Vector256<double> a0 = Vector256.LoadUnsafe(ref pa, at);
                Vector256<double> b0 = Vector256.LoadUnsafe(ref pb, at);
                Vector256<double> a1 = Vector256.LoadUnsafe(ref pa, at + 4);
                Vector256<double> b1 = Vector256.LoadUnsafe(ref pb, at + 4);
                straight0 += a0 * b0;
                crossed0 += a0 * Swapped(b0);
                straight1 += a1 * b1;
                crossed1 += a1 * Swapped(b1);
            }

            Vector256<double> straight = straight0 + straight1;
            Vector256<double> crossed = crossed0 + crossed1;
            real = (straight.GetElement(0) - straight.GetElement(1))
                + (straight.GetElement(2) - straight.GetElement(3));
            imaginary = (crossed.GetElement(0) + crossed.GetElement(1))
                + (crossed.GetElement(2) + crossed.GetElement(3));
        }

        for (; i < length; i++)
        {
            double ar = Unsafe.Add(ref pa, 2 * i);
            double ai = Unsafe.Add(ref pa, (2 * i) + 1);
            double br = Unsafe.Add(ref pb, 2 * i);
            double bi = Unsafe.Add(ref pb, (2 * i) + 1);
            real += (ar * br) - (ai * bi);
            imaginary += (ar * bi) + (ai * br);
        }

        return new Complex(real, imaginary);
    }

    /// <summary>Each complex number's two parts exchanged.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Swapped(Vector256<double> v) =>
        Avx.IsSupported ? Avx.Permute(v, 0b0101) : Vector256.Shuffle(v, Vector256.Create(1L, 0L, 3L, 2L));

    // --- small helpers ---------------------------------------------------------------------------

    private static double OneNormFromIdentity(Complex[] a, int n)
    {
        double worst = 0.0;
        for (int c = 0; c < n; c++)
        {
            int at = c * n;
            double sum = (a[at + c] - Complex.One).Magnitude;
            for (int r = 0; r < c; r++)
            {
                sum += a[at + r].Magnitude;
            }

            // A NaN compares false with everything, and has to stop the scaling rather than pass
            // for a triangle that is already at the identity.
            if (double.IsNaN(sum))
            {
                return double.NaN;
            }

            worst = Math.Max(worst, sum);
        }

        return worst;
    }

    private static int Position(double[] limits, double value)
    {
        for (int i = 0; i < limits.Length; i++)
        {
            if (value <= limits[i])
            {
                return i;
            }
        }

        return limits.Length - 1;
    }

    private static bool IsUpperTriangular(ReadOnlySpan<double> a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[(c * n) + r] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsSymmetric(ReadOnlySpan<double> a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[(c * n) + r] != a[(r * n) + c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The upper triangle and the first subdiagonal of a real matrix, in complex storage.</summary>
    private static Complex[] Widen(ReadOnlySpan<double> a, int n)
    {
        var wide = new Complex[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            int at = c * n;
            int last = Math.Min(c + 1, n - 1);
            for (int r = 0; r <= last; r++)
            {
                wide[at + r] = new Complex(a[at + r], 0.0);
            }
        }

        return wide;
    }

    private static double[] Part(Complex[] f, bool imaginary)
    {
        var part = new double[f.Length];
        for (int i = 0; i < f.Length; i++)
        {
            part[i] = imaginary ? f[i].Imaginary : f[i].Real;
        }

        return part;
    }

    private static Complex[] Join(double[] real, double[] imaginary)
    {
        var joined = new Complex[real.Length];
        for (int i = 0; i < real.Length; i++)
        {
            joined[i] = new Complex(real[i], imaginary[i]);
        }

        return joined;
    }
}
