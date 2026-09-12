using System.Buffers;
using System.Numerics;
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
/// identity is not just an explanation here, it is the implementation: one FFT of length 2n per
/// line, giving O(n log n) where the definition reads O(n²).
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

        // The even extension: x0…x(n-1) followed by x(n-1)…x0. Its DFT, turned by a half-sample
        // phase, is real and is twice the unnormalized DCT-II.
        int length = 2 * n;
        var pool = ArrayPool<double>.Shared;
        double[] re = pool.Rent(length);
        double[] im = pool.Rent(length);
        try
        {
            Array.Clear(im, 0, length);
            for (int i = 0; i < n; i++)
            {
                re[i] = values[i];
                re[length - 1 - i] = values[i];
            }

            FftKernels.Transform(re.AsSpan(0, length), im.AsSpan(0, length), length, inverse: false, inside);

            double first = Math.Sqrt(1.0 / n);
            double rest = Math.Sqrt(2.0 / n);
            for (int k = 0; k < n; k++)
            {
                double angle = -Math.PI * k / (2.0 * n);
                double half = ((re[k] * Math.Cos(angle)) - (im[k] * Math.Sin(angle))) / 2.0;
                result[k] = half * (k == 0 ? first : rest);
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

        // x(j) = Σ w(k)·cos(π·k·(2j+1)/2n) with the orthonormal weights folded into w. Written as a
        // length-2n inverse transform of a half-filled, half-sample-shifted spectrum, that sum is
        // one FFT rather than n².
        int length = 2 * n;
        var pool = ArrayPool<double>.Shared;
        double[] re = pool.Rent(length);
        double[] im = pool.Rent(length);
        try
        {
            Array.Clear(re, 0, length);
            Array.Clear(im, 0, length);
            double first = Math.Sqrt(1.0 / n);
            double rest = Math.Sqrt(2.0 / n);
            for (int k = 0; k < n; k++)
            {
                double weight = coefficients[k] * (k == 0 ? first : rest);
                double angle = Math.PI * k / (2.0 * n);
                re[k] = weight * Math.Cos(angle);
                im[k] = weight * Math.Sin(angle);
            }

            FftKernels.Transform(re.AsSpan(0, length), im.AsSpan(0, length), length, inverse: true, inside);

            for (int j = 0; j < n; j++)
            {
                result[j] = re[j] * 2 * n;
            }
        }
        finally
        {
            pool.Return(re);
            pool.Return(im);
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
