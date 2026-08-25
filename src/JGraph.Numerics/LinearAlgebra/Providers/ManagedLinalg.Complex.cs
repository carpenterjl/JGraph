using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The complex half of the managed backend (M91): the LU trio, the matrix product, the general
/// eigensolver, and the one-sided Jacobi SVD, all over interleaved column-major
/// <see cref="Complex"/> storage — the same layout LAPACK's z-routines read, so the two backends
/// are interchangeable span for span.
/// </summary>
public sealed partial class ManagedLinalg
{
    /// <inheritdoc />
    public override void Zgemm(int m, int n, int k, ReadOnlySpan<Complex> a, int lda,
        ReadOnlySpan<Complex> b, int ldb, Span<Complex> c, int ldc)
    {
        // The same k-ascending accumulation the boxed complex product has always run, with no
        // zero-skip: 0·Inf must contribute its NaN here exactly as it always has.
        for (int col = 0; col < n; col++)
        {
            for (int row = 0; row < m; row++)
            {
                Complex sum = Complex.Zero;
                for (int i = 0; i < k; i++)
                {
                    sum += a[(i * lda) + row] * b[(col * ldb) + i];
                }

                c[(col * ldc) + row] = sum;
            }
        }
    }

    /// <inheritdoc />
    public override int Zgetrf(int m, int n, Span<Complex> a, int lda, Span<int> ipiv)
    {
        // The real right-looking loop over complex entries. The pivot competition runs on
        // |re| + |im| — LAPACK's cabs1 — rather than the true magnitude, so the two backends pick
        // the same rows and the factors agree to rounding rather than merely both being valid.
        int steps = Math.Min(m, n);
        int firstSingular = 0;
        for (int k = 0; k < steps; k++)
        {
            int best = k;
            double bestAbs = Cabs1(a[(k * lda) + k]);
            for (int r = k + 1; r < m; r++)
            {
                double candidate = Cabs1(a[(k * lda) + r]);
                if (candidate > bestAbs)
                {
                    best = r;
                    bestAbs = candidate;
                }
            }

            ipiv[k] = best + 1;
            if (best != k)
            {
                for (int c = 0; c < n; c++)
                {
                    int origin = c * lda;
                    (a[origin + k], a[origin + best]) = (a[origin + best], a[origin + k]);
                }
            }

            Complex diagonal = a[(k * lda) + k];
            if (diagonal == Complex.Zero)
            {
                firstSingular = firstSingular == 0 ? k + 1 : firstSingular;
                continue;
            }

            Span<Complex> pivotColumn = a.Slice(k * lda, m);
            for (int r = k + 1; r < m; r++)
            {
                pivotColumn[r] /= diagonal;
            }

            for (int c = k + 1; c < n; c++)
            {
                Complex top = a[(c * lda) + k];
                if (top == Complex.Zero)
                {
                    continue;
                }

                Span<Complex> column = a.Slice(c * lda, m);
                for (int r = k + 1; r < m; r++)
                {
                    column[r] -= pivotColumn[r] * top;
                }
            }
        }

        return firstSingular;
    }

    /// <inheritdoc />
    public override void Zgetrs(int n, int nrhs, ReadOnlySpan<Complex> a, int lda,
        ReadOnlySpan<int> ipiv, Span<Complex> b, int ldb)
    {
        // Interchanges, then L forward, then U back — the no-transpose real solve verbatim.
        int steps = Math.Min(ipiv.Length, n);
        for (int step = 0; step < steps; step++)
        {
            int other = ipiv[step] - 1;
            if (other == step || other < 0 || other >= n)
            {
                continue;
            }

            for (int c = 0; c < nrhs; c++)
            {
                int origin = c * ldb;
                (b[origin + step], b[origin + other]) = (b[origin + other], b[origin + step]);
            }
        }

        for (int c = 0; c < nrhs; c++)
        {
            Span<Complex> x = b.Slice(c * ldb, n);
            for (int k = 0; k < n; k++)
            {
                Complex above = x[k];
                if (above == Complex.Zero)
                {
                    continue;
                }

                for (int r = k + 1; r < n; r++)
                {
                    x[r] -= a[(k * lda) + r] * above;
                }
            }

            for (int k = n - 1; k >= 0; k--)
            {
                x[k] /= a[(k * lda) + k];
                Complex above = x[k];
                if (above == Complex.Zero)
                {
                    continue;
                }

                for (int r = 0; r < k; r++)
                {
                    x[r] -= a[(k * lda) + r] * above;
                }
            }
        }
    }

    /// <inheritdoc />
    public override int Zgetri(int n, Span<Complex> a, int lda, ReadOnlySpan<int> ipiv)
    {
        // Solving against the identity, like the real fallback: 2·n³ where zgetri is 4/3·n³, and
        // the native path is where the arithmetic gets cheaper.
        var inverse = new Complex[(long)n * n];
        for (int i = 0; i < n; i++)
        {
            inverse[((long)i * n) + i] = Complex.One;
        }

        Zgetrs(n, n, a, lda, ipiv, inverse, n);
        for (int c = 0; c < n; c++)
        {
            inverse.AsSpan(c * n, n).CopyTo(a.Slice(c * lda, n));
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Zgeev(bool vectors, int n, Span<Complex> a, int lda,
        Span<Complex> w, Span<Complex> vr, int ldvr)
    {
        if (n == 0)
        {
            return 0;
        }

        // Balance first, as zgeev itself does — powers of two, so the spectrum is untouched to the
        // bit and a badly scaled matrix keeps its digits.
        var work = new Complex[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            a.Slice(c * lda, n).CopyTo(work.AsSpan(c * n, n));
        }

        double[] scaling = BalanceComplex(work, n);

        var matrix = new Complex[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                matrix[r, c] = work[(c * n) + r];
            }
        }

        Complex[] values = ComplexEigen.ValuesManaged((Complex[,])matrix.Clone());
        for (int i = 0; i < n; i++)
        {
            w[i] = values[i];
        }

        if (!vectors)
        {
            return 0;
        }

        for (int j = 0; j < n; j++)
        {
            Complex[] vector = Unbalance(InverseIterationComplex(matrix, values[j]), scaling, n);
            for (int r = 0; r < n; r++)
            {
                vr[(j * ldvr) + r] = vector[r];
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Zgesdd(SvdVectors job, int m, int n, Span<Complex> a, int lda,
        Span<double> s, Span<Complex> u, int ldu, Span<Complex> vt, int ldvt)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        // The real backend's orientation dance, conjugated: a wide matrix factors as Aᴴ = P·Σ·Qᴴ,
        // which hands back A = Q·Σ·Pᴴ with the two factors swapped.
        bool transposed = m < n;
        int rows = transposed ? n : m;
        int order = transposed ? m : n;
        bool complete = job == SvdVectors.All && rows > order;

        Complex[] rotated = OneSidedJacobiComplex(a, lda, m, n, transposed, rows, order, complete, s,
            out Complex[] turned);

        if (job == SvdVectors.None)
        {
            return 0;
        }

        int width = complete ? rows : order;
        if (!transposed)
        {
            for (int c = 0; c < width; c++)
            {
                new ReadOnlySpan<Complex>(rotated, c * rows, rows).CopyTo(u.Slice(c * ldu, m));
            }

            // V arrives as columns and leaves conjugated as rows: the second factor is Vᴴ.
            for (int c = 0; c < order; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    vt[(r * ldvt) + c] = Complex.Conjugate(turned[(c * order) + r]);
                }
            }
        }
        else
        {
            for (int c = 0; c < order; c++)
            {
                new ReadOnlySpan<Complex>(turned, c * order, order).CopyTo(u.Slice(c * ldu, m));
            }

            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    vt[(r * ldvt) + c] = Complex.Conjugate(rotated[(c * rows) + r]);
                }
            }
        }

        return 0;
    }

    /// <summary>LAPACK's cabs1: the pivot yardstick |re| + |im|, cheaper than a true magnitude.</summary>
    private static double Cabs1(Complex value) => Math.Abs(value.Real) + Math.Abs(value.Imaginary);

    /// <summary>
    /// One-sided Jacobi over complex columns: each rotation is the unitary 2×2 that makes a pair of
    /// columns orthogonal, its phase read off their inner product. Squares nothing — the Gram
    /// matrix is never formed — so small singular values keep their digits, which the old
    /// eig-of-the-Gram-embedding path could not promise.
    /// </summary>
    private static Complex[] OneSidedJacobiComplex(ReadOnlySpan<Complex> a, int lda, int m, int n,
        bool transposed, int rows, int order, bool complete, Span<double> values, out Complex[] turned)
    {
        var b = new Complex[(long)rows * (complete ? rows : order)];
        for (int c = 0; c < order; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                b[(c * rows) + r] = transposed
                    ? Complex.Conjugate(a[(r * lda) + c])
                    : a[(c * lda) + r];
            }
        }

        var v = new Complex[(long)order * order];
        for (int i = 0; i < order; i++)
        {
            v[(i * order) + i] = Complex.One;
        }

        for (int sweep = 0; sweep < MaximumSweeps; sweep++)
        {
            bool rotatedAny = false;
            for (int p = 0; p < order - 1; p++)
            {
                for (int q = p + 1; q < order; q++)
                {
                    double alpha = 0, beta = 0;
                    Complex gamma = Complex.Zero;
                    for (int r = 0; r < rows; r++)
                    {
                        Complex bp = b[(p * rows) + r];
                        Complex bq = b[(q * rows) + r];
                        alpha += (bp.Real * bp.Real) + (bp.Imaginary * bp.Imaginary);
                        beta += (bq.Real * bq.Real) + (bq.Imaginary * bq.Imaginary);
                        gamma += Complex.Conjugate(bp) * bq;
                    }

                    double size = gamma.Magnitude;
                    if (size <= Epsilon * Math.Sqrt(alpha * beta) || size == 0)
                    {
                        continue;
                    }

                    rotatedAny = true;

                    // The phase turns the complex pair problem into the real one: with
                    // γ = |γ|·e^{iφ}, the rotation [c, s·e^{iφ}; −s·e^{−iφ}, c] diagonalizes the
                    // 2×2 Hermitian Gram block for the same real t the real sweep uses.
                    Complex phase = gamma / size;
                    double zeta = (beta - alpha) / (2 * size);
                    double direction = zeta >= 0 ? 1 : -1;
                    double t = direction / (Math.Abs(zeta) + Math.Sqrt(1 + (zeta * zeta)));
                    double c = 1 / Math.Sqrt(1 + (t * t));
                    double sn = c * t;
                    Complex forward = sn * phase;
                    Complex backward = sn * Complex.Conjugate(phase);

                    for (int r = 0; r < rows; r++)
                    {
                        Complex bp = b[(p * rows) + r];
                        b[(p * rows) + r] = (c * bp) - (backward * b[(q * rows) + r]);
                        b[(q * rows) + r] = (forward * bp) + (c * b[(q * rows) + r]);
                    }

                    for (int r = 0; r < order; r++)
                    {
                        Complex vp = v[(p * order) + r];
                        v[(p * order) + r] = (c * vp) - (backward * v[(q * order) + r]);
                        v[(q * order) + r] = (forward * vp) + (c * v[(q * order) + r]);
                    }
                }
            }

            if (!rotatedAny)
            {
                break;
            }
        }

        var sigma = new double[order];
        for (int c = 0; c < order; c++)
        {
            double norm = 0;
            for (int r = 0; r < rows; r++)
            {
                Complex entry = b[(c * rows) + r];
                norm += (entry.Real * entry.Real) + (entry.Imaginary * entry.Imaginary);
            }

            sigma[c] = Math.Sqrt(norm);
        }

        SortDescendingComplex(sigma, b, rows, v, order);

        // The same cutoff as the real sweep, for the same reason: a column the sweeps annihilated
        // has no direction left, and normalizing its rounding dust manufactures a false basis.
        double cutoff = rows * Epsilon * (order > 0 ? sigma[0] : 0);
        int width = complete ? rows : order;
        var missing = new bool[width];
        for (int c = 0; c < order; c++)
        {
            values[c] = sigma[c];
            if (sigma[c] > cutoff)
            {
                for (int r = 0; r < rows; r++)
                {
                    b[(c * rows) + r] /= sigma[c];
                }
            }
            else
            {
                Array.Clear(b, c * rows, rows);
                missing[c] = true;
            }
        }

        for (int c = order; c < width; c++)
        {
            missing[c] = true;
        }

        CompleteBasisComplex(b, rows, width, missing);

        turned = v;
        return b;
    }

    private static void SortDescendingComplex(double[] sigma, Complex[] u, int rows, Complex[] v, int order)
    {
        for (int i = 0; i < order - 1; i++)
        {
            int biggest = i;
            for (int j = i + 1; j < order; j++)
            {
                if (sigma[j] > sigma[biggest])
                {
                    biggest = j;
                }
            }

            if (biggest != i)
            {
                (sigma[i], sigma[biggest]) = (sigma[biggest], sigma[i]);
                SwapColumnsComplex(u, rows, i, biggest);
                SwapColumnsComplex(v, order, i, biggest);
            }
        }
    }

    private static void SwapColumnsComplex(Complex[] matrix, int rows, int a, int b)
    {
        for (int r = 0; r < rows; r++)
        {
            (matrix[(a * rows) + r], matrix[(b * rows) + r]) =
                (matrix[(b * rows) + r], matrix[(a * rows) + r]);
        }
    }

    /// <summary>
    /// The real basis completion's complex twin: largest-residual pivoting and a doubled
    /// Gram–Schmidt sweep, with every projection an Hermitian inner product.
    /// </summary>
    private static void CompleteBasisComplex(Complex[] u, int rows, int columns, bool[] missing)
    {
        var residual = new double[rows];
        Array.Fill(residual, 1.0);
        for (int c = 0; c < columns; c++)
        {
            if (missing[c])
            {
                continue;
            }

            for (int r = 0; r < rows; r++)
            {
                Complex entry = u[(c * rows) + r];
                residual[r] -= (entry.Real * entry.Real) + (entry.Imaginary * entry.Imaginary);
            }
        }

        var candidate = new Complex[rows];
        for (int c = 0; c < columns; c++)
        {
            if (!missing[c])
            {
                continue;
            }

            int basis = 0;
            for (int r = 1; r < rows; r++)
            {
                if (residual[r] > residual[basis])
                {
                    basis = r;
                }
            }

            Array.Clear(candidate);
            candidate[basis] = Complex.One;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int other = 0; other < columns; other++)
                {
                    if (missing[other])
                    {
                        continue;
                    }

                    Complex projection = Complex.Zero;
                    for (int r = 0; r < rows; r++)
                    {
                        projection += Complex.Conjugate(u[(other * rows) + r]) * candidate[r];
                    }

                    for (int r = 0; r < rows; r++)
                    {
                        candidate[r] -= projection * u[(other * rows) + r];
                    }
                }
            }

            double norm = 0;
            for (int r = 0; r < rows; r++)
            {
                norm += (candidate[r].Real * candidate[r].Real)
                    + (candidate[r].Imaginary * candidate[r].Imaginary);
            }

            if (norm <= 0)
            {
                residual[basis] = 0;
                continue;
            }

            norm = Math.Sqrt(norm);
            for (int r = 0; r < rows; r++)
            {
                Complex entry = candidate[r] / norm;
                u[(c * rows) + r] = entry;
                residual[r] -= (entry.Real * entry.Real) + (entry.Imaginary * entry.Imaginary);
            }

            missing[c] = false;
        }
    }

    /// <summary>
    /// <see cref="Balancing.InPlace"/> over complex entries: the same power-of-two similarity, the
    /// row and column norms measured by magnitude. Exact on both components, so the spectrum is the
    /// original's to the bit.
    /// </summary>
    private static double[] BalanceComplex(Span<Complex> a, int n)
    {
        var scale = new double[n];
        Array.Fill(scale, 1.0);

        const double Radix = 2.0;
        const double Squared = Radix * Radix;

        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 100)
        {
            changed = false;
            for (int i = 0; i < n; i++)
            {
                double row = 0;
                double column = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    row += a[(j * n) + i].Magnitude;
                    column += a[(i * n) + j].Magnitude;
                }

                if (row == 0 || column == 0)
                {
                    continue;
                }

                double factor = 1;
                double scaled = column;
                double before = column + row;

                while (scaled < row / Radix)
                {
                    factor *= Radix;
                    scaled *= Squared;
                }

                while (scaled >= row * Radix)
                {
                    factor /= Radix;
                    scaled /= Squared;
                }

                if ((scaled + (row / factor)) >= 0.95 * before)
                {
                    continue;
                }

                changed = true;
                scale[i] *= factor;
                for (int j = 0; j < n; j++)
                {
                    a[(j * n) + i] /= factor;
                    a[(i * n) + j] *= factor;
                }
            }
        }

        return scale;
    }

    /// <summary>Inverse iteration against a complex matrix — the real path's twin, solve for solve.</summary>
    private static Complex[] InverseIterationComplex(Complex[,] matrix, Complex value)
    {
        int n = matrix.GetLength(0);
        double scale = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                scale = Math.Max(scale, matrix[r, c].Magnitude);
            }
        }

        Complex shift = value + ((scale + 1) * 1e-10);
        var shifted = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                shifted[r, c] = matrix[r, c];
            }

            shifted[r, r] -= shift;
        }

        var x = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = 1.0 / Math.Sqrt(n);
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            Complex[] next = SolveComplex(shifted, x);
            double norm = 0;
            foreach (Complex entry in next)
            {
                norm += entry.Magnitude * entry.Magnitude;
            }

            norm = Math.Sqrt(norm);
            if (norm == 0 || double.IsNaN(norm) || double.IsInfinity(norm))
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                x[i] = next[i] / norm;
            }
        }

        int biggest = 0;
        for (int i = 1; i < n; i++)
        {
            if (x[i].Magnitude > x[biggest].Magnitude)
            {
                biggest = i;
            }
        }

        if (x[biggest].Magnitude > 0)
        {
            Complex phase = x[biggest] / x[biggest].Magnitude;
            for (int i = 0; i < n; i++)
            {
                x[i] /= phase;
            }
        }

        return x;
    }
}
