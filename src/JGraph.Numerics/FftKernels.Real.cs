using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JGraph.Numerics;

/// <summary>
/// The two-dimensional transform of a real matrix, built on the matrix being real (ADR 0334).
/// </summary>
/// <remarks>
/// <para>
/// The general road transforms every column as a complex sequence whose imaginary half is zero,
/// then every row of that, then averages each answer with its mirror through the origin so the
/// spectrum is exactly conjugate-symmetric, as R2025b's is. Two of those three passes are spent on
/// work the input's being real makes redundant. Two real columns <c>a</c> and <c>b</c> packed as
/// one complex column <c>z = a + i·b</c> come out of one transform together:
/// <c>A[k] = (Z[k] + conj Z[−k])/2</c> and <c>B[k] = (Z[k] − conj Z[−k])/2i</c>, each exactly
/// conjugate-symmetric by construction. And a real matrix's spectrum is fixed by its lower half
/// of rows: only rows <c>0 … m/2</c> are transformed along the second dimension, and the rest are
/// written as <c>F[k1, k2] = conj F[m − k1, −k2]</c>.
/// </para>
/// <para>
/// Rows <c>0</c> and <c>m/2</c> are their own mirrors; their inputs are real (the column spectra
/// are real there), and each takes the one-dimensional symmetrization a real input's transform
/// takes. The answer is therefore exactly Hermitian through the origin without the averaging pass,
/// and differs from the general road's in the last bits. The road is taken for power-of-two sides
/// the tile kernel walks directly, a column count in whole tiles of sixteen and at least
/// <see cref="Real2From"/> elements; anything else keeps the general road and its bits.
/// </para>
/// </remarks>
public static partial class FftKernels
{
    /// <summary>The fewest elements a real matrix needs before its transform takes the real road.</summary>
    public const int Real2From = 1 << 16;

    /// <summary>
    /// Whether the real road is taken (<c>JGRAPH_FFT_REAL</c>, on unless it is <c>0</c>); off, a
    /// real matrix's <c>fft2</c> keeps the general road, which is the measurement's off condition.
    /// </summary>
    public static bool RealRoads { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_FFT_REAL") != "0";

    /// <summary>Whether a real <paramref name="rows"/>-by-<paramref name="cols"/> matrix takes <see cref="RealForward2"/>.</summary>
    public static bool FitsReal2(int rows, int cols) =>
        RealRoads
        && IsPowerOfTwo(rows) && IsPowerOfTwo(cols)
        && rows >= 2 * Lanes && rows <= SixStepThreshold
        && cols >= 2 * Lanes && cols <= SixStepThreshold
        && (long)rows * cols >= Real2From;

    /// <summary>
    /// The unscaled forward transform of a real column-major <paramref name="rows"/>-by-
    /// <paramref name="cols"/> matrix in <paramref name="src"/>, written whole into the two planes,
    /// exactly conjugate-symmetric through the origin. The matrix must satisfy
    /// <see cref="FitsReal2"/>; the destination must not overlap the source.
    /// </summary>
    public static void RealForward2(
        NumericBuffer src, NumericBuffer dstRe, NumericBuffer dstIm, int rows, int cols)
    {
        if (!FitsReal2(rows, cols))
        {
            throw new ArgumentException("the matrix's shape does not take the real road.");
        }

        int m = rows;
        int n = cols;
        int half = m / 2;

        // Pass one: sixteen columns a tile, two per complex lane, split into the lower half of
        // each column's spectrum (the upper half is never read).
        int tiles = n / (2 * Lanes);
        ParallelKernels.ForBlocks(tiles, tiles > 1, tile =>
        {
            int first = tile * 2 * Lanes;
            double[] rented = ArrayPool<double>.Shared.Rent(2 * m * Lanes);
            try
            {
                Span<double> tr = rented.AsSpan(0, m * Lanes);
                Span<double> ti = rented.AsSpan(m * Lanes, m * Lanes);
                ref double tre = ref MemoryMarshal.GetReference(tr);
                ref double tim = ref MemoryMarshal.GetReference(ti);
                ref double sre = ref MemoryMarshal.GetReference(src.AsSpan());
                CollectRows(ref Unsafe.Add(ref sre, first * m), ref tre, m, Lanes, m);
                CollectRows(ref Unsafe.Add(ref sre, (first + Lanes) * m), ref tim, m, Lanes, m);
                Stages(tr, ti, m, Lanes, inverse: false);
                SplitColumns(tr, ti, dstRe, dstIm, m, first);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(rented);
            }
        });

        // Pass two: rows 0 … m/2 along the second dimension, eight neighbouring rows a tile.
        int wanted = half + 1;
        int groups = ((wanted - 1) / Lanes) + 1;
        ParallelKernels.ForBlocks(groups, groups > 1, g =>
        {
            int first = g * Lanes;
            int lanes = Math.Min(Lanes, wanted - first);
            double[] rented = ArrayPool<double>.Shared.Rent(2 * n * lanes);
            try
            {
                Span<double> tr = rented.AsSpan(0, n * lanes);
                Span<double> ti = rented.AsSpan(n * lanes, n * lanes);
                ref double tre = ref MemoryMarshal.GetReference(tr);
                ref double tim = ref MemoryMarshal.GetReference(ti);
                ref double dre = ref MemoryMarshal.GetReference(dstRe.AsSpan());
                ref double dim = ref MemoryMarshal.GetReference(dstIm.AsSpan());
                for (int j = 0; j < n; j++)
                {
                    int from = first + (j * m);
                    CopyRun(ref Unsafe.Add(ref dre, from), ref Unsafe.Add(ref tre, j * lanes), lanes);
                    CopyRun(ref Unsafe.Add(ref dim, from), ref Unsafe.Add(ref tim, j * lanes), lanes);
                }

                Stages(tr, ti, n, lanes, inverse: false);
                for (int l = 0; l < lanes; l++)
                {
                    int row = first + l;
                    if (row == 0 || row == half)
                    {
                        Symmetrize(tr, ti, n, lanes, l);
                    }
                }

                for (int j = 0; j < n; j++)
                {
                    int to = first + (j * m);
                    CopyRun(ref Unsafe.Add(ref tre, j * lanes), ref Unsafe.Add(ref dre, to), lanes);
                    CopyRun(ref Unsafe.Add(ref tim, j * lanes), ref Unsafe.Add(ref dim, to), lanes);
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(rented);
            }
        });

        // Pass three: the upper rows as the conjugates of their mirrors through the origin.
        int blocks = Math.Max(1, Math.Min(n, ParallelKernels.MaxDegree * 4));
        ParallelKernels.ForBlocks(blocks, blocks > 1, b =>
        {
            int from = (int)((long)n * b / blocks);
            int to = (int)((long)n * (b + 1) / blocks);
            MirrorColumns(dstRe, dstIm, m, n, from, to);
        });

        GC.KeepAlive(src);
        GC.KeepAlive(dstRe);
        GC.KeepAlive(dstIm);
    }

    /// <summary>
    /// One tile's sixteen column spectra, rows 0 … m/2, from its eight packed lanes: lane
    /// <c>l</c> holds column <c>first + l</c> as its real part and column <c>first + 8 + l</c> as
    /// its imaginary part.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SplitColumns(
        Span<double> tr, Span<double> ti, NumericBuffer dstRe, NumericBuffer dstIm, int m, int first)
    {
        int half = m / 2;
        Span<double> dr = dstRe.AsSpan();
        Span<double> di = dstIm.AsSpan();
        for (int k = 0; k <= half; k++)
        {
            int a = k * Lanes;
            int b = ((m - k) & (m - 1)) * Lanes;
            bool own = k == 0 || k == half;
            for (int l = 0; l < Lanes; l++)
            {
                double zr = tr[a + l];
                double zi = ti[a + l];
                double wr = tr[b + l];
                double wi = ti[b + l];
                int left = ((first + l) * m) + k;
                int right = ((first + Lanes + l) * m) + k;
                dr[left] = 0.5 * (zr + wr);
                di[left] = own ? 0 : 0.5 * (zi - wi);
                dr[right] = 0.5 * (zi + wi);
                di[right] = own ? 0 : -0.5 * (zr - wr);
            }
        }
    }

    /// <summary>Columns <c>[from, to)</c>, rows <c>m/2 + 1 … m − 1</c>: <c>F[k1, k2] = conj F[m − k1, −k2]</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void MirrorColumns(NumericBuffer dstRe, NumericBuffer dstIm, int m, int n, int from, int to)
    {
        int half = m / 2;
        Span<double> dr = dstRe.AsSpan();
        Span<double> di = dstIm.AsSpan();
        for (int k2 = from; k2 < to; k2++)
        {
            int mine = k2 * m;
            int theirs = ((n - k2) & (n - 1)) * m;
            for (int k1 = half + 1; k1 < m; k1++)
            {
                dr[mine + k1] = dr[theirs + m - k1];
                di[mine + k1] = -di[theirs + m - k1];
            }
        }
    }
}
