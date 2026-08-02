using System.Numerics;
using JGraph.Signal;

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
        int n = values.Length;
        if (n == 0)
        {
            return [];
        }

        if (n == 1)
        {
            return [values[0]];
        }

        // The even extension: x0…x(n-1) followed by x(n-1)…x0. Its DFT, turned by a half-sample
        // phase, is real and is twice the unnormalized DCT-II.
        var extended = new Complex[2 * n];
        for (int i = 0; i < n; i++)
        {
            extended[i] = new Complex(values[i], 0);
            extended[(2 * n) - 1 - i] = new Complex(values[i], 0);
        }

        Fft.Transform(extended, inverse: false);

        var result = new double[n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double angle = -Math.PI * k / (2.0 * n);
            double half = (extended[k] * Complex.FromPolarCoordinates(1, angle)).Real / 2.0;
            result[k] = half * (k == 0 ? first : rest);
        }

        return result;
    }

    /// <summary>The orthonormal DCT-III of one line — the exact inverse of <see cref="Forward(ReadOnlySpan{double})"/>.</summary>
    /// <param name="coefficients">The coefficients.</param>
    /// <returns>The samples, the same length.</returns>
    public static double[] Inverse(ReadOnlySpan<double> coefficients)
    {
        int n = coefficients.Length;
        if (n == 0)
        {
            return [];
        }

        if (n == 1)
        {
            return [coefficients[0]];
        }

        // x(j) = Σ w(k)·cos(π·k·(2j+1)/2n) with the orthonormal weights folded into w. Written as a
        // length-2n inverse transform of a half-filled, half-sample-shifted spectrum, that sum is
        // one FFT rather than n².
        var spectrum = new Complex[2 * n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double weight = coefficients[k] * (k == 0 ? first : rest);
            spectrum[k] = Complex.FromPolarCoordinates(weight, Math.PI * k / (2.0 * n));
        }

        Fft.Transform(spectrum, inverse: true);

        var result = new double[n];
        for (int j = 0; j < n; j++)
        {
            result[j] = spectrum[j].Real * 2 * n;
        }

        return result;
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
