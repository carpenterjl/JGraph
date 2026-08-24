namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The managed <see cref="DenseLinalg"/>: the saxpy product kernel that used to live in
/// <see cref="DenseProduct"/>, behind the provider contract. It preserves that kernel's numeric
/// behavior exactly — k-ascending accumulation and the skip of zero factors — so switching a
/// script between representations or falling back from native never changes managed results.
/// </summary>
public sealed class ManagedLinalg : DenseLinalg
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
