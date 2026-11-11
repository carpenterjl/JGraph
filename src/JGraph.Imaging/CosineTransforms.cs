using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using JGraph.Numerics;

namespace JGraph.Imaging;

/// <summary>
/// The two-dimensional discrete cosine transform behind <c>dct2</c>, <c>idct2</c> and
/// <c>dctmtx</c> — the transform JPEG is built on, and the reason a photograph survives being
/// thrown away to a twentieth of its size.
/// </summary>
/// <remarks>
/// <para>
/// The DCT is a Fourier transform of the signal's <em>even extension</em>. Mirroring a length-n
/// signal to length 2n makes it periodic without a step at the join, and a periodic signal with no
/// step has no high-frequency energy to speak of — which is exactly why the DCT concentrates a
/// picture into its first few coefficients where the DFT smears it across all of them. That
/// identity is the implementation, in Makhoul's form: the even-indexed samples in order, then
/// the odd-indexed ones reversed, is a length-n sequence whose DFT carries the DCT once where
/// the even extension carried it twice — one FFT of length n per line, O(n log n) where the
/// definition reads O(n²) (ADR 0153).
/// </para>
/// <para>
/// Everything is the orthonormal form MATLAB uses, so the transform is its own inverse transposed
/// and <c>dct2(A)</c> equals <c>D·A·Dᵀ</c> for <c>D = dctmtx(n)</c>. Preserving that identity is
/// what lets a script check its own arithmetic.
/// </para>
/// </remarks>
public static class CosineTransforms
{
    /// <summary>
    /// The n-by-n orthonormal DCT-II matrix: row k holds the kth cosine basis function, so
    /// <c>D·A·Dᵀ</c> is the two-dimensional transform and <c>Dᵀ·B·D</c> undoes it.
    /// </summary>
    /// <param name="n">The side length; must be positive.</param>
    /// <returns>The transform matrix.</returns>
    public static double[,] Matrix(int n)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "dctmtx needs a positive size.");
        }

        var matrix = new double[n, n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double scale = k == 0 ? first : rest;
            for (int j = 0; j < n; j++)
            {
                matrix[k, j] = scale * Math.Cos(Math.PI * k * ((2 * j) + 1) / (2.0 * n));
            }
        }

        return matrix;
    }

    /// <summary>The orthonormal DCT-II of one line.</summary>
    /// <param name="values">The samples.</param>
    /// <returns>The coefficients, the same length.</returns>
    public static double[] Forward(ReadOnlySpan<double> values)
    {
        var result = new double[values.Length];
        Forward(values, result, inside: false);
        return result;
    }

    /// <summary>
    /// The orthonormal DCT-II of one line, written into <paramref name="result"/> (the same length
    /// as <paramref name="values"/>). <paramref name="inside"/> lets the one transform underneath
    /// thread itself when the caller has nothing else for the machine — a single long line rather
    /// than a batch of short ones. The arithmetic is the same either way, and the same as the boxed
    /// road this replaced: the even extension over two planes of doubles instead of an array of
    /// <see cref="Complex"/>, the same FFT, and the half-sample turn spelled as the Complex multiply
    /// spelled it, <c>(re·cos − im·sin)</c>.
    /// </summary>
    /// <param name="values">The samples.</param>
    /// <param name="result">Where the coefficients go; must not overlap <paramref name="values"/>.</param>
    /// <param name="inside">Whether the transform may spend threads within itself.</param>
    public static void Forward(ReadOnlySpan<double> values, Span<double> result, bool inside)
    {
        int n = values.Length;
        if (result.Length != n)
        {
            throw new ArgumentException("the result must be the length of the input.", nameof(result));
        }

        if (n == 0)
        {
            return;
        }

        if (n == 1)
        {
            result[0] = values[0];
            return;
        }

        if (TakesHalfLength(n))
        {
            ForwardHalf(values, result, inside);
            return;
        }

        // Makhoul's reordering: the even-indexed samples in order, then the odd-indexed ones in
        // reverse, is a length-n sequence whose DFT, turned by a quarter-sample phase, has the
        // unnormalized DCT-II as its real part — one transform of length n where the even
        // extension needed one of length 2n (ADR 0153, 07b).
        var pool = ArrayPool<double>.Shared;
        double[] re = pool.Rent(n);
        double[] im = pool.Rent(n);
        try
        {
            // The passes around the transform are elementwise, so a long line spreads them over
            // threads in grains that own their elements: the same arithmetic per element, the same
            // bits (ADR 0334). The turn's cosine and sine per coefficient were most of the line's
            // time once the transform itself was fast.
            int threshold = inside ? ParallelKernels.ComputeBoundThreshold : int.MaxValue;
            unsafe
            {
                fixed (double* source = values)
                {
                    double* from = source;
                    ParallelKernels.For(n, threshold, null, (start, length) =>
                        ReorderForward(new ReadOnlySpan<double>(from, n), re, im, start, start + length));
                }
            }

            FftKernels.Transform(re.AsSpan(0, n), im.AsSpan(0, n), n, inverse: false, inside);

            unsafe
            {
                fixed (double* target = result)
                {
                    double* into = target;
                    ParallelKernels.For(n, threshold, null, (start, length) =>
                        TurnForward(re, im, new Span<double>(into, n), start, start + length));
                }
            }
        }
        finally
        {
            pool.Return(re);
            pool.Return(im);
        }
    }

    /// <summary>The orthonormal DCT-III of one line — the exact inverse of <see cref="Forward(ReadOnlySpan{double})"/>.</summary>
    /// <param name="coefficients">The coefficients.</param>
    /// <returns>The samples, the same length.</returns>
    public static double[] Inverse(ReadOnlySpan<double> coefficients)
    {
        var result = new double[coefficients.Length];
        Inverse(coefficients, result, inside: false);
        return result;
    }

    /// <summary>
    /// The orthonormal DCT-III of one line, written into <paramref name="result"/>; the counterpart
    /// of <see cref="Forward(ReadOnlySpan{double}, Span{double}, bool)"/>, with the same arithmetic
    /// as the boxed road: the half-filled, half-sample-shifted spectrum built as
    /// <c>(w·cos, w·sin)</c>, one inverse FFT, and the real part scaled back.
    /// </summary>
    /// <param name="coefficients">The coefficients.</param>
    /// <param name="result">Where the samples go; must not overlap <paramref name="coefficients"/>.</param>
    /// <param name="inside">Whether the transform may spend threads within itself.</param>
    public static void Inverse(ReadOnlySpan<double> coefficients, Span<double> result, bool inside)
    {
        int n = coefficients.Length;
        if (result.Length != n)
        {
            throw new ArgumentException("the result must be the length of the input.", nameof(result));
        }

        if (n == 0)
        {
            return;
        }

        if (n == 1)
        {
            result[0] = coefficients[0];
            return;
        }

        if (TakesHalfLength(n))
        {
            InverseHalf(coefficients, result, inside);
            return;
        }

        // The mirror of the forward road. With C the unnormalized DCT-II coefficients, the DFT of
        // Makhoul's reordered sequence is V[k] = e^{iπk/2n}·(C[k] − i·C[n−k]) (V[0] = C[0]): the
        // real part of e^{−iπk/2n}·V[k] is C[k] by construction, and its imaginary part is −C[n−k]
        // because V is the spectrum of a real sequence. One inverse transform of length n, then
        // the reordering undone (ADR 0153, 07b).
        var pool = ArrayPool<double>.Shared;
        double[] re = pool.Rent(n);
        double[] im = pool.Rent(n);
        try
        {
            int threshold = inside ? ParallelKernels.ComputeBoundThreshold : int.MaxValue;
            unsafe
            {
                fixed (double* source = coefficients)
                {
                    double* from = source;
                    ParallelKernels.For(n, threshold, null, (start, length) =>
                        TurnInverse(new ReadOnlySpan<double>(from, n), re, im, start, start + length));
                }
            }

            FftKernels.Transform(re.AsSpan(0, n), im.AsSpan(0, n), n, inverse: true, inside);

            unsafe
            {
                fixed (double* target = result)
                {
                    double* into = target;
                    ParallelKernels.For(n, threshold, null, (start, length) =>
                        ReorderInverse(re, new Span<double>(into, n), start, start + length));
                }
            }
        }
        finally
        {
            pool.Return(re);
            pool.Return(im);
        }
    }

    /// <summary>
    /// The shortest even line that takes the half-length road: below it the full-length
    /// transform is cheap and its bits stay what they were.
    /// </summary>
    public const int HalfLengthFrom = 1 << 12;

    /// <summary>
    /// Whether an even line from <see cref="HalfLengthFrom"/> up is transformed as a complex
    /// sequence of half its length (<c>JGRAPH_DCT_HALF</c>, on unless it is <c>0</c>; ADR 0334).
    /// </summary>
    public static bool HalfLength { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_DCT_HALF") != "0";

    private static bool TakesHalfLength(int n) => HalfLength && n >= HalfLengthFrom && (n & 1) == 0;

    /// <summary>
    /// The forward transform of an even line through one complex FFT of half its length (ADR
    /// 0334). Makhoul's reordered sequence <c>v</c> is real, so its even and odd samples are packed
    /// as one complex sequence <c>z[j] = v[2j] + i·v[2j+1]</c>; with <c>Z</c> its DFT of length
    /// <c>m = n/2</c>, the halves separate as <c>E[k] = (Z[k] + conj Z[m−k])/2</c> and
    /// <c>O[k] = (Z[k] − conj Z[m−k])/2i</c>, and <c>V[k] = E[k] + e^{−2πik/n}·O[k]</c> is the
    /// full-length spectrum, Hermitian, so each pass over <c>k ≤ m</c> writes the coefficients at
    /// <c>k</c> and <c>n − k</c> together. Half the transform's work; the doubles differ from the
    /// full-length road's in their last bits.
    /// </summary>
    private static void ForwardHalf(ReadOnlySpan<double> values, Span<double> result, bool inside)
    {
        int n = values.Length;
        int m = n / 2;
        var pool = ArrayPool<double>.Shared;
        double[] zr = pool.Rent(m);
        double[] zi = pool.Rent(m);
        try
        {
            int threshold = inside ? ParallelKernels.ComputeBoundThreshold : int.MaxValue;
            unsafe
            {
                fixed (double* source = values)
                {
                    double* from = source;
                    ParallelKernels.For(m, threshold, null, (start, length) =>
                        PackHalf(new ReadOnlySpan<double>(from, n), zr, zi, start, start + length));
                }
            }

            FftKernels.Transform(zr.AsSpan(0, m), zi.AsSpan(0, m), m, inverse: false, inside);

            unsafe
            {
                fixed (double* target = result)
                {
                    double* into = target;
                    ParallelKernels.For(m + 1, threshold, null, (start, length) =>
                        SplitForward(zr, zi, new Span<double>(into, n), start, start + length));
                }
            }
        }
        finally
        {
            pool.Return(zr);
            pool.Return(zi);
        }
    }

    /// <summary>
    /// <c>z[j] = v[2j] + i·v[2j+1]</c> for <c>j</c> in <c>[from, to)</c>, where <c>v</c> is
    /// Makhoul's reordering of <paramref name="values"/> (even samples in order, odd ones reversed).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void PackHalf(ReadOnlySpan<double> values, double[] zr, double[] zi, int from, int to)
    {
        int n = values.Length;
        int m = n / 2;
        for (int j = from; j < to; j++)
        {
            int p = 2 * j;
            zr[j] = p < m ? values[2 * p] : values[(2 * (n - 1 - p)) + 1];
            int q = p + 1;
            zi[j] = q < m ? values[2 * q] : values[(2 * (n - 1 - q)) + 1];
        }
    }

    /// <summary>
    /// The split and the quarter-sample turn for <c>k</c> in <c>[from, to)</c> of <c>[0, m]</c>:
    /// the coefficient at <c>k</c> and, for <c>0 &lt; k &lt; m</c>, the one at <c>n − k</c>, whose
    /// spectrum value is the conjugate and whose turn is the same angle's sine and cosine swapped.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SplitForward(double[] zr, double[] zi, Span<double> result, int from, int to)
    {
        int n = result.Length;
        int m = n / 2;
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = from; k < to; k++)
        {
            int a = k == m ? 0 : k;
            int b = k == 0 ? 0 : m - k;
            double ar = zr[a], ai = zi[a];
            double br = zr[b], bi = -zi[b];
            double er = 0.5 * (ar + br);
            double ei = 0.5 * (ai + bi);
            double or = 0.5 * (ai - bi);
            double oi = -0.5 * (ar - br);
            (double ws, double wc) = Math.SinCos(-2.0 * Math.PI * k / n);
            double vr = er + ((wc * or) - (ws * oi));
            double vi = ei + ((wc * oi) + (ws * or));
            (double ts, double tc) = Math.SinCos(Math.PI * k / (2.0 * n));
            result[k] = ((vr * tc) + (vi * ts)) * (k == 0 ? first : rest);
            if (k > 0 && k < m)
            {
                result[n - k] = ((vr * ts) - (vi * tc)) * rest;
            }
        }
    }

    /// <summary>
    /// The inverse transform of an even line through one inverse FFT of half its length (ADR
    /// 0334): the full-length spectrum <c>V</c> the full road would build is formed at <c>k</c> and
    /// <c>k + m</c>, folded to <c>Z[k] = E[k] + i·O[k]</c> with <c>E = (V[k] + V[k+m])/2</c> and
    /// <c>O = (V[k] − V[k+m])·e^{2πik/n}/2</c>, transformed back at length <c>m</c>, and the real
    /// and imaginary parts read out as the even and odd samples of Makhoul's sequence.
    /// </summary>
    private static void InverseHalf(ReadOnlySpan<double> coefficients, Span<double> result, bool inside)
    {
        int n = coefficients.Length;
        int m = n / 2;
        var pool = ArrayPool<double>.Shared;
        double[] zr = pool.Rent(m);
        double[] zi = pool.Rent(m);
        try
        {
            int threshold = inside ? ParallelKernels.ComputeBoundThreshold : int.MaxValue;
            unsafe
            {
                fixed (double* source = coefficients)
                {
                    double* from = source;
                    ParallelKernels.For(m, threshold, null, (start, length) =>
                        FoldInverse(new ReadOnlySpan<double>(from, n), zr, zi, start, start + length));
                }
            }

            FftKernels.Transform(zr.AsSpan(0, m), zi.AsSpan(0, m), m, inverse: true, inside);

            unsafe
            {
                fixed (double* target = result)
                {
                    double* into = target;
                    ParallelKernels.For(n, threshold, null, (start, length) =>
                        UnpackHalf(zr, zi, new Span<double>(into, n), start, start + length));
                }
            }
        }
        finally
        {
            pool.Return(zr);
            pool.Return(zi);
        }
    }

    /// <summary>The full road's spectrum value at <c>k</c>: <c>e^{iπk/2n}·(C[k] − i·C[n−k])</c>, scaled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double Re, double Im) Spectrum(ReadOnlySpan<double> coefficients, int k)
    {
        int n = coefficients.Length;
        if (k == 0)
        {
            return (coefficients[0] * Math.Sqrt(n), 0);
        }

        double rest = Math.Sqrt(n / 2.0);
        double a = coefficients[k] * rest;
        double b = coefficients[n - k] * rest;
        (double s, double c) = Math.SinCos(Math.PI * k / (2.0 * n));
        return ((a * c) + (b * s), (a * s) - (b * c));
    }

    /// <summary><c>Z[k] = E[k] + i·O[k]</c> for <c>k</c> in <c>[from, to)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FoldInverse(ReadOnlySpan<double> coefficients, double[] zr, double[] zi, int from, int to)
    {
        int n = coefficients.Length;
        int m = n / 2;
        for (int k = from; k < to; k++)
        {
            (double pr, double pi) = Spectrum(coefficients, k);
            (double qr, double qi) = Spectrum(coefficients, k + m);
            double er = 0.5 * (pr + qr);
            double ei = 0.5 * (pi + qi);
            double dr = 0.5 * (pr - qr);
            double di = 0.5 * (pi - qi);
            (double ws, double wc) = Math.SinCos(2.0 * Math.PI * k / n);
            double or = (dr * wc) - (di * ws);
            double oi = (dr * ws) + (di * wc);
            zr[k] = er - oi;
            zi[k] = ei + or;
        }
    }

    /// <summary>
    /// The answer's samples <c>[from, to)</c>: Makhoul's sequence <c>v</c> (even samples in
    /// <paramref name="zr"/>, odd in <paramref name="zi"/>) with its reordering undone.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void UnpackHalf(double[] zr, double[] zi, Span<double> result, int from, int to)
    {
        int n = result.Length;
        for (int i = from; i < to; i++)
        {
            int q = (i & 1) == 0 ? i / 2 : n - 1 - (i / 2);
            result[i] = (q & 1) == 0 ? zr[q / 2] : zi[q / 2];
        }
    }

    /// <summary>
    /// Makhoul's reordering for the elements <c>[from, to)</c> of the reordered sequence: the
    /// even-indexed samples in order, then the odd-indexed ones reversed; the imaginary plane
    /// cleared.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ReorderForward(ReadOnlySpan<double> values, double[] re, double[] im, int from, int to)
    {
        int n = values.Length;
        int half = (n + 1) / 2;
        for (int j = from; j < to; j++)
        {
            re[j] = j < half ? values[2 * j] : values[(2 * (n - 1 - j)) + 1];
            im[j] = 0;
        }
    }

    /// <summary>The forward quarter-sample turn and the orthonormal scale for coefficients <c>[from, to)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void TurnForward(double[] re, double[] im, Span<double> result, int from, int to)
    {
        int n = result.Length;
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = from; k < to; k++)
        {
            double angle = -Math.PI * k / (2.0 * n);
            double unnormalized = (re[k] * Math.Cos(angle)) - (im[k] * Math.Sin(angle));
            result[k] = unnormalized * (k == 0 ? first : rest);
        }
    }

    /// <summary>The inverse's half-filled, turned spectrum for bins <c>[from, to)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void TurnInverse(ReadOnlySpan<double> coefficients, double[] re, double[] im, int from, int to)
    {
        int n = coefficients.Length;
        double first = Math.Sqrt(n);
        double rest = Math.Sqrt(n / 2.0);
        for (int k = from; k < to; k++)
        {
            if (k == 0)
            {
                re[0] = coefficients[0] * first;
                im[0] = 0;
                continue;
            }

            double a = coefficients[k] * rest;
            double b = coefficients[n - k] * rest;
            double angle = Math.PI * k / (2.0 * n);
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            re[k] = (a * c) + (b * s);
            im[k] = (a * s) - (b * c);
        }
    }

    /// <summary>The reordering undone for the samples <c>[from, to)</c> of the answer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ReorderInverse(double[] re, Span<double> result, int from, int to)
    {
        int n = result.Length;
        for (int i = from; i < to; i++)
        {
            result[i] = (i & 1) == 0 ? re[i / 2] : re[n - 1 - (i / 2)];
        }
    }

    /// <summary>The two-dimensional DCT: the one-dimensional transform down each column, then along each row.</summary>
    /// <param name="values">The samples.</param>
    /// <returns>The coefficients, the same size.</returns>
    public static double[,] Forward(double[,] values) => Separable(values, line => Forward((ReadOnlySpan<double>)line));

    /// <summary>The two-dimensional inverse DCT.</summary>
    /// <param name="coefficients">The coefficients.</param>
    /// <returns>The samples, the same size.</returns>
    public static double[,] Inverse(double[,] coefficients) =>
        Separable(coefficients, line => Inverse((ReadOnlySpan<double>)line));

    /// <summary>
    /// Pads with zeros or crops to the requested size — <c>dct2(A, m, n)</c>'s first act, and the
    /// reason a transform can be taken at a size the picture does not have.
    /// </summary>
    /// <param name="values">The samples.</param>
    /// <param name="rows">The wanted row count.</param>
    /// <param name="cols">The wanted column count.</param>
    /// <returns>An array of exactly the requested size.</returns>
    public static double[,] Resize(double[,] values, int rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (rows < 1 || cols < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "the transform size must be positive.");
        }

        int sourceRows = values.GetLength(0);
        int sourceCols = values.GetLength(1);
        if (sourceRows == rows && sourceCols == cols)
        {
            return (double[,])values.Clone();
        }

        var resized = new double[rows, cols];
        for (int r = 0; r < Math.Min(rows, sourceRows); r++)
        {
            for (int c = 0; c < Math.Min(cols, sourceCols); c++)
            {
                resized[r, c] = values[r, c];
            }
        }

        return resized;
    }

    private static double[,] Separable(double[,] values, Func<double[], double[]> line)
    {
        ArgumentNullException.ThrowIfNull(values);
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var result = new double[rows, cols];

        var column = new double[rows];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                column[r] = values[r, c];
            }

            double[] transformed = line(column);
            for (int r = 0; r < rows; r++)
            {
                result[r, c] = transformed[r];
            }
        }

        var row = new double[cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                row[c] = result[r, c];
            }

            double[] transformed = line(row);
            for (int c = 0; c < cols; c++)
            {
                result[r, c] = transformed[c];
            }
        }

        return result;
    }
}
