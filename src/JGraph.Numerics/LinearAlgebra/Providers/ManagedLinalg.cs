namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The managed <see cref="DenseLinalg"/>: the saxpy product kernel that used to live in
/// <see cref="DenseProduct"/>, behind the provider contract. It preserves that kernel's numeric
/// behavior exactly — k-ascending accumulation and the skip of zero factors — so switching a
/// script between representations or falling back from native never changes managed results.
/// </summary>
public sealed partial class ManagedLinalg : DenseLinalg
{
    /// <summary>Below this many flops the column loop runs serially; above, in parallel.</summary>
    private const long ParallelFlopThreshold = 1_000_000;

    /// <inheritdoc />
    public override bool IsNative => false;

    /// <inheritdoc />
    public override string Description => "managed kernels";

    /// <inheritdoc />
    public override unsafe void Gemm(bool transA, bool transB, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> b, int ldb, Span<double> c, int ldc)
    {
        if (m == 0 || n == 0)
        {
            return;
        }

        for (int col = 0; col < n; col++)
        {
            c.Slice(col * ldc, m).Clear();
        }

        if (k == 0)
        {
            return;
        }

        if (transA || transB)
        {
            TransposedProduct(transA, transB, m, n, k, a, lda, b, ldb, c, ldc);
            return;
        }

        long flops = 2L * m * k * n;
        fixed (double* fa = a)
        fixed (double* fb = b)
        fixed (double* fc = c)
        {
            // The pointers ride through the lambda as nint: a fixed local cannot be captured, a
            // plain integer can, and the spans stay pinned for the whole synchronous loop.
            nint pa = (nint)fa;
            nint pb = (nint)fb;
            nint pc = (nint)fc;
            if (flops < ParallelFlopThreshold)
            {
                for (int col = 0; col < n; col++)
                {
                    MultiplyColumn(pa, m, k, lda, pb, ldb, pc, ldc, col);
                }
            }
            else
            {
                Parallel.For(0, n, col => MultiplyColumn(pa, m, k, lda, pb, ldb, pc, ldc, col));
            }
        }
    }

    private static unsafe void MultiplyColumn(nint a, int m, int k, int lda, nint b, int ldb, nint c, int ldc, int col)
    {
        double* pa = (double*)a;
        double* column = (double*)c + ((long)col * ldc);
        double* factors = (double*)b + ((long)col * ldb);
        for (int i = 0; i < k; i++)
        {
            double factor = factors[i];
            if (factor == 0)
            {
                continue;
            }

            double* source = pa + ((long)i * lda);
            for (int r = 0; r < m; r++)
            {
                column[r] += factor * source[r];
            }
        }
    }

    /// <inheritdoc />
    public override void Syrk(bool transposeFirst, int n, int k,
        ReadOnlySpan<double> a, int lda, Span<double> c, int ldc)
    {
        // Lower triangle by k-ascending dot products, then the mirror — symmetric by construction,
        // like the native path, rather than by the accident of a particular accumulation order.
        for (int j = 0; j < n; j++)
        {
            for (int i = j; i < n; i++)
            {
                double sum = 0;
                if (transposeFirst)
                {
                    // A is k×n: (AᵀA)(i,j) = Σₚ A(p,i)·A(p,j) — two contiguous columns.
                    ReadOnlySpan<double> ci = a.Slice(i * lda, k);
                    ReadOnlySpan<double> cj = a.Slice(j * lda, k);
                    for (int p = 0; p < k; p++)
                    {
                        sum += ci[p] * cj[p];
                    }
                }
                else
                {
                    // A is n×k: (AAᵀ)(i,j) = Σₚ A(i,p)·A(j,p) — two strided rows.
                    for (int p = 0; p < k; p++)
                    {
                        sum += a[(p * lda) + i] * a[(p * lda) + j];
                    }
                }

                c[(j * ldc) + i] = sum;
            }
        }

        MirrorLowerTriangle(c, n, ldc);
    }

    /// <inheritdoc />
    public override int Getrf(int m, int n, Span<double> a, int lda, Span<int> ipiv)
    {
        // The right-looking partial-pivot loop the LU factorization has always run, moved from
        // row-major rectangles onto column-major spans. Same pivots (the first of any tied maxima),
        // same k-ascending elimination, same choice to leave a zero pivot's column alone — so the
        // managed backend answers today what it answered before the seam existed.
        int steps = Math.Min(m, n);
        int firstSingular = 0;
        for (int k = 0; k < steps; k++)
        {
            int best = k;
            double bestAbs = Math.Abs(a[(k * lda) + k]);
            for (int r = k + 1; r < m; r++)
            {
                double candidate = Math.Abs(a[(k * lda) + r]);
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

            double diagonal = a[(k * lda) + k];
            if (diagonal == 0)
            {
                // Singular: the zero stays on U's diagonal and the caller's IsSingular reports it.
                firstSingular = firstSingular == 0 ? k + 1 : firstSingular;
                continue;
            }

            Span<double> pivotColumn = a.Slice(k * lda, m);
            for (int r = k + 1; r < m; r++)
            {
                pivotColumn[r] /= diagonal;
            }

            for (int c = k + 1; c < n; c++)
            {
                double top = a[(c * lda) + k];
                if (top == 0)
                {
                    continue;
                }

                Span<double> column = a.Slice(c * lda, m);
                for (int r = k + 1; r < m; r++)
                {
                    column[r] -= pivotColumn[r] * top;
                }
            }
        }

        return firstSingular;
    }

    /// <inheritdoc />
    public override void Getrs(bool transpose, int n, int nrhs,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<int> ipiv, Span<double> b, int ldb)
    {
        if (transpose)
        {
            SolveTransposed(n, nrhs, a, lda, ipiv, b, ldb);
            return;
        }

        ApplyInterchanges(ipiv, n, nrhs, b, ldb, forward: true);
        for (int c = 0; c < nrhs; c++)
        {
            Span<double> x = b.Slice(c * ldb, n);
            for (int k = 0; k < n; k++)
            {
                double above = x[k];
                if (above == 0)
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
                double above = x[k];
                if (above == 0)
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

    /// <summary>The transposed solve from the same factors: Uᵀ forward, Lᵀ back, interchanges undone.</summary>
    private static void SolveTransposed(int n, int nrhs,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<int> ipiv, Span<double> b, int ldb)
    {
        for (int c = 0; c < nrhs; c++)
        {
            Span<double> x = b.Slice(c * ldb, n);
            for (int k = 0; k < n; k++)
            {
                ReadOnlySpan<double> column = a.Slice(k * lda, n);
                double sum = x[k];
                for (int j = 0; j < k; j++)
                {
                    sum -= column[j] * x[j];
                }

                x[k] = sum / column[k];
            }

            for (int k = n - 1; k >= 0; k--)
            {
                double sum = x[k];
                for (int j = k + 1; j < n; j++)
                {
                    sum -= a[(k * lda) + j] * x[j];
                }

                x[k] = sum;
            }
        }

        ApplyInterchanges(ipiv, n, nrhs, b, ldb, forward: false);
    }

    /// <summary>
    /// LAPACK's row interchanges over a column-major right-hand side — pure movement and no
    /// arithmetic, so a permuted row is the same bits whichever backend recorded the pivots.
    /// </summary>
    private static void ApplyInterchanges(ReadOnlySpan<int> ipiv, int n, int nrhs, Span<double> b, int ldb, bool forward)
    {
        int steps = Math.Min(ipiv.Length, n);
        for (int step = 0; step < steps; step++)
        {
            int i = forward ? step : steps - 1 - step;
            int other = ipiv[i] - 1;
            if (other == i || other < 0 || other >= n)
            {
                continue;
            }

            for (int c = 0; c < nrhs; c++)
            {
                int origin = c * ldb;
                (b[origin + i], b[origin + other]) = (b[origin + other], b[origin + i]);
            }
        }
    }

    /// <inheritdoc />
    public override int Getri(int n, Span<double> a, int lda, ReadOnlySpan<int> ipiv)
    {
        // Solving against the identity — 2·n³ where LAPACK's dgetri is 4/3·n³, and exactly what the
        // inverse has always cost here. The fallback keeps its numbers; the native path is where
        // the arithmetic gets cheaper.
        var inverse = new double[(long)n * n];
        for (int i = 0; i < n; i++)
        {
            inverse[((long)i * n) + i] = 1;
        }

        Getrs(transpose: false, n, n, a, lda, ipiv, inverse, n);
        for (int c = 0; c < n; c++)
        {
            inverse.AsSpan(c * n, n).CopyTo(a.Slice(c * lda, n));
        }

        return 0;
    }

    /// <inheritdoc />
    public override double Gecon(int n, ReadOnlySpan<double> a, int lda, double anorm)
    {
        // The exact reciprocal condition number rather than LAPACK's estimate: inverting outright is
        // affordable at the sizes a managed fallback runs, and it is what rcond answered before the
        // seam existed. The native backend estimates instead — the recorded divergence in ADR 0089.
        for (int i = 0; i < n; i++)
        {
            if (a[(i * lda) + i] == 0)
            {
                return 0;
            }
        }

        var factors = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            a.Slice(c * lda, n).CopyTo(factors.AsSpan(c * n, n));
        }

        // U⁻¹·L⁻¹ rather than A⁻¹ itself: the pivoting permutation reorders the inverse's columns
        // and leaves every column sum where it was, so the 1-norm — and the answer — is the same.
        var unpermuted = new int[n];
        for (int i = 0; i < n; i++)
        {
            unpermuted[i] = i + 1;
        }

        Getri(n, factors, n, unpermuted);
        double product = anorm * OneNorm(n, n, factors, n);
        return product == 0 ? 0 : 1.0 / product;
    }

    /// <inheritdoc />
    public override int Potrf(bool lower, int n, Span<double> a, int lda)
    {
        // The dot-product form, in place. The two triangles are mirror images: for a symmetric input
        // the upper factorization sums exactly the products the lower one sums, in the same order,
        // so here — unlike under a blocked native kernel — the two factors transpose into one
        // another to the bit.
        for (int outer = 0; outer < n; outer++)
        {
            for (int inner = 0; inner <= outer; inner++)
            {
                // Lower walks row `outer` of L; upper walks column `outer` of R.
                int i = lower ? outer : inner;
                int j = lower ? inner : outer;
                double sum = a[(j * lda) + i];
                for (int k = 0; k < inner; k++)
                {
                    sum -= lower
                        ? a[(k * lda) + i] * a[(k * lda) + j]
                        : a[(i * lda) + k] * a[(j * lda) + k];
                }

                if (inner == outer)
                {
                    if (sum <= 0)
                    {
                        return outer + 1;
                    }

                    a[(j * lda) + i] = Math.Sqrt(sum);
                }
                else
                {
                    a[(j * lda) + i] = sum / a[(inner * lda) + inner];
                }
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Trtrs(bool lower, bool transpose, int n, int nrhs,
        ReadOnlySpan<double> a, int lda, Span<double> b, int ldb)
    {
        // LAPACK checks the whole diagonal before solving anything, so a singular triangle is
        // reported rather than half-answered; matching that keeps the two backends' failures alike.
        for (int i = 0; i < n; i++)
        {
            if (a[(i * lda) + i] == 0)
            {
                return i + 1;
            }
        }

        bool forward = lower ^ transpose;
        for (int c = 0; c < nrhs; c++)
        {
            Span<double> x = b.Slice(c * ldb, n);
            for (int step = 0; step < n; step++)
            {
                int i = forward ? step : n - 1 - step;
                double sum = x[i];
                if (forward)
                {
                    for (int j = 0; j < i; j++)
                    {
                        sum -= Entry(a, lda, transpose, i, j) * x[j];
                    }
                }
                else
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        sum -= Entry(a, lda, transpose, i, j) * x[j];
                    }
                }

                x[i] = sum / Entry(a, lda, transpose, i, i);
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Gels(int m, int n, int nrhs, Span<double> a, int lda, Span<double> b, int ldb) =>
        Linear.LeastSquaresManaged(m, n, nrhs, a, lda, b, ldb);

    /// <summary>Entry (row, column) of A, or of Aᵀ when the solve is against the transpose.</summary>
    private static double Entry(ReadOnlySpan<double> a, int lda, bool transpose, int row, int column) =>
        transpose ? a[(row * lda) + column] : a[(column * lda) + row];

    /// <summary>
    /// The transposed variants, serial: the script paths never pass a transpose flag to the managed
    /// backend (a materialized transpose reaches here as a plain operand), so these exist for the
    /// contract's completeness and for direct provider callers. Same k-ascending, zero-skipping
    /// accumulation as the saxpy kernel.
    /// </summary>
    private static void TransposedProduct(bool transA, bool transB, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> b, int ldb, Span<double> c, int ldc)
    {
        for (int col = 0; col < n; col++)
        {
            Span<double> column = c.Slice(col * ldc, m);
            for (int i = 0; i < k; i++)
            {
                double factor = transB ? b[(i * ldb) + col] : b[(col * ldb) + i];
                if (factor == 0)
                {
                    continue;
                }

                if (transA)
                {
                    // Logical A(r, i) is stored at a[r*lda + i].
                    for (int r = 0; r < m; r++)
                    {
                        column[r] += factor * a[(r * lda) + i];
                    }
                }
                else
                {
                    ReadOnlySpan<double> source = a.Slice(i * lda, m);
                    for (int r = 0; r < m; r++)
                    {
                        column[r] += factor * source[r];
                    }
                }
            }
        }
    }
}
