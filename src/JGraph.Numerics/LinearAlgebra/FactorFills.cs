namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The copies into and out of a dense factorization — the input's private copy, a triangle cut out
/// of LAPACK's packed answer, the other triangle cleared — cut by columns across threads once a
/// matrix is big enough for the memory traffic to be the cost.
/// </summary>
/// <remarks>
/// <para>
/// At n = 2000 a factorization's copy in and its outputs are 32 MB apiece, and the fresh pages they
/// land on fault in one at a time on whichever thread first writes them. ADR 0321 measured lu's
/// three outputs at 19 ms of a 56 ms call on one thread; the every-row plan's I3 spreads them over
/// the cores (ADR 0332).
/// </para>
/// <para>
/// Every fill here moves elements and computes nothing, so how the columns are cut changes no bit
/// of any answer: a block owns its columns outright and nothing is folded across blocks.
/// </para>
/// </remarks>
public static unsafe class FactorFills
{
    /// <summary>Elements (1M, 8 MB) from which a fill is split across threads.</summary>
    public const long ParallelElements = 1L << 20;

    /// <summary>Column blocks a large fill is cut into: enough to even out a triangle's taper.</summary>
    private const int ColumnBlocks = 64;

    /// <summary>Elements per piece of a plain copy (128K, 1 MB).</summary>
    private const int CopyGrain = 1 << 17;

    /// <summary>Whether a fill over this many elements is worth more than one thread.</summary>
    public static bool Threaded(long elements) => elements >= ParallelElements && ParallelKernels.MaxDegree > 1;

    /// <summary>Copies <paramref name="source"/> into the start of <paramref name="destination"/>.</summary>
    /// <exception cref="ArgumentException">The destination is shorter than the source.</exception>
    public static void Copy(ReadOnlySpan<double> source, Span<double> destination)
    {
        long length = source.Length;
        if (destination.Length < length)
        {
            throw new ArgumentException("The destination is shorter than the source.", nameof(destination));
        }

        if (!Threaded(length))
        {
            source.CopyTo(destination);
            return;
        }

        fixed (double* s = source)
        fixed (double* d = destination)
        {
            nint from = (nint)s;
            nint to = (nint)d;
            int grains = (int)((length + CopyGrain - 1) / CopyGrain);
            ParallelKernels.ForBlocks(grains, parallel: true, g =>
            {
                long start = (long)g * CopyGrain;
                long bytes = Math.Min(CopyGrain, length - start) * sizeof(double);
                Buffer.MemoryCopy((double*)from + start, (double*)to + start, bytes, bytes);
            });
        }
    }

    /// <summary>
    /// The upper trapezoid of a column-major source with leading dimension
    /// <paramref name="sourceLead"/>, written into a <paramref name="rows"/>-by-<paramref name="columns"/>
    /// destination: rows 0 to <c>min(c, rows − 1)</c> of column c. With <paramref name="clearRest"/>
    /// the rows below are written as zeros; without it they are left as the destination had them,
    /// which for a freshly allocated result is zero already.
    /// </summary>
    /// <exception cref="ArgumentException">A span is too short for the shape.</exception>
    public static void Upper(ReadOnlySpan<double> source, int sourceLead, int rows, int columns,
        Span<double> destination, bool clearRest)
    {
        CheckShape(source, sourceLead, rows, columns, destination);
        if (rows == 0 || columns == 0)
        {
            return;
        }

        fixed (double* s = source)
        fixed (double* d = destination)
        {
            nint from = (nint)s;
            nint to = (nint)d;
            ForColumns(rows, columns, (first, end) =>
            {
                for (int c = first; c < end; c++)
                {
                    int keep = Math.Min(c + 1, rows);
                    double* column = (double*)to + ((long)c * rows);
                    Buffer.MemoryCopy((double*)from + ((long)c * sourceLead), column,
                        keep * (long)sizeof(double), keep * (long)sizeof(double));
                    if (clearRest && keep < rows)
                    {
                        new Span<double>(column + keep, rows - keep).Clear();
                    }
                }
            });
        }
    }

    /// <summary>
    /// The strictly lower part of an n-by-n column-major source written into an n-by-n destination,
    /// with ones on the diagonal when <paramref name="unitDiagonal"/> (LU's L) and the source's own
    /// diagonal otherwise (a lower Cholesky factor). With <paramref name="clearRest"/> the rows above
    /// the diagonal are written as zeros. With a <paramref name="rowOrder"/>, factored row s of every
    /// column lands on row <c>rowOrder[s]</c> instead — L with the row interchanges undone — which
    /// needs a destination that arrives zeroed, since the rows it skips are scattered.
    /// </summary>
    /// <exception cref="ArgumentException">A span is too short, or a row order is given with clearing.</exception>
    public static void Lower(ReadOnlySpan<double> source, int n, Span<double> destination,
        bool unitDiagonal, bool clearRest, int[]? rowOrder)
    {
        CheckShape(source, n, n, n, destination);
        if (rowOrder is not null && (clearRest || rowOrder.Length < n))
        {
            throw new ArgumentException("A row order scatters into a zeroed destination of n rows.", nameof(rowOrder));
        }

        if (n == 0)
        {
            return;
        }

        fixed (double* s = source)
        fixed (double* d = destination)
        {
            nint from = (nint)s;
            nint to = (nint)d;
            ForColumns(n, n, (first, end) =>
            {
                for (int c = first; c < end; c++)
                {
                    long origin = (long)c * n;
                    double* input = (double*)from + origin;
                    double* column = (double*)to + origin;
                    if (rowOrder is not null)
                    {
                        column[rowOrder[c]] = unitDiagonal ? 1 : input[c];
                        for (int r = c + 1; r < n; r++)
                        {
                            column[rowOrder[r]] = input[r];
                        }

                        continue;
                    }

                    if (clearRest && c > 0)
                    {
                        new Span<double>(column, c).Clear();
                    }

                    column[c] = unitDiagonal ? 1 : input[c];
                    long below = n - c - 1;
                    if (below > 0)
                    {
                        Buffer.MemoryCopy(input + c + 1, column + c + 1,
                            below * sizeof(double), below * sizeof(double));
                    }
                }
            });
        }
    }

    /// <summary>
    /// Clears the triangle a factorization did not write, in place: below the diagonal for an upper
    /// factor, above it for a lower one.
    /// </summary>
    public static void ClearOtherTriangle(Span<double> square, int n, bool keepLower)
    {
        if ((long)square.Length < (long)n * n)
        {
            throw new ArgumentException("The span must hold n² elements.", nameof(square));
        }

        if (n == 0)
        {
            return;
        }

        fixed (double* d = square)
        {
            nint to = (nint)d;
            ForColumns(n, n, (first, end) =>
            {
                for (int c = first; c < end; c++)
                {
                    double* column = (double*)to + ((long)c * n);
                    if (keepLower)
                    {
                        new Span<double>(column, c).Clear();
                    }
                    else if (c + 1 < n)
                    {
                        new Span<double>(column + c + 1, n - c - 1).Clear();
                    }
                }
            });
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> over column ranges <c>[first, end)</c> of a rows-by-columns
    /// matrix: one range on the calling thread for a small matrix, otherwise
    /// <see cref="ColumnBlocks"/> ranges whose bounds depend on the shape alone.
    /// </summary>
    public static void ForColumns(int rows, int columns, Action<int, int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (columns <= 0)
        {
            return;
        }

        if (columns == 1 || !Threaded((long)rows * columns))
        {
            body(0, columns);
            return;
        }

        int blocks = Math.Min(columns, ColumnBlocks);
        ParallelKernels.ForBlocks(blocks, parallel: true, b =>
        {
            int first = (int)((long)columns * b / blocks);
            int end = (int)((long)columns * (b + 1) / blocks);
            body(first, end);
        });
    }

    private static void CheckShape(ReadOnlySpan<double> source, int sourceLead, int rows, int columns,
        Span<double> destination)
    {
        if (rows < 0 || columns < 0 || sourceLead < Math.Min(rows, 1))
        {
            throw new ArgumentException("The shape is not a matrix.", nameof(rows));
        }

        if (columns > 0 && (long)source.Length < ((long)(columns - 1) * sourceLead) + Math.Min(columns, rows))
        {
            throw new ArgumentException("The source is too short for the shape.", nameof(source));
        }

        if ((long)destination.Length < (long)rows * columns)
        {
            throw new ArgumentException("The destination is too short for the shape.", nameof(destination));
        }
    }
}
