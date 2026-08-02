using System.Numerics;
using JGraph.Signal;

namespace JGraph.Imaging;

/// <summary>
/// The two-dimensional Fourier transform over a row-major grid, and the frequency-domain
/// convolution and correlation built on it.
/// </summary>
/// <remarks>
/// A 2-D transform is separable: transform every row, then every column. That is the whole of it,
/// and it is written here once because three different things in this project need it —
/// normalized cross-correlation, the Radon ramp filter, and the deconvolution family — and each of
/// them would otherwise grow its own row-then-column loop.
/// </remarks>
public static class FourierGrid
{
    /// <summary>Transforms <paramref name="grid"/> in place, rows then columns.</summary>
    /// <param name="grid">A <paramref name="height"/>-by-<paramref name="width"/> row-major grid.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="inverse">Whether to run the inverse transform (which divides by the element count).</param>
    public static void Transform(Complex[] grid, int height, int width, bool inverse)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Length != height * width)
        {
            throw new ArgumentException("the grid does not match the given size.", nameof(grid));
        }

        var row = new Complex[width];
        for (int r = 0; r < height; r++)
        {
            Array.Copy(grid, r * width, row, 0, width);
            Fft.Transform(row, inverse);
            Array.Copy(row, 0, grid, r * width, width);
        }

        var column = new Complex[height];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                column[r] = grid[(r * width) + c];
            }

            Fft.Transform(column, inverse);
            for (int r = 0; r < height; r++)
            {
                grid[(r * width) + c] = column[r];
            }
        }
    }

    /// <summary>Transforms a real grid, returning a fresh spectrum.</summary>
    /// <param name="values">The samples, row-major.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="width">The number of columns.</param>
    /// <returns>The spectrum, row-major.</returns>
    public static Complex[] Forward(ReadOnlySpan<double> values, int height, int width)
    {
        var grid = new Complex[height * width];
        for (int i = 0; i < grid.Length; i++)
        {
            grid[i] = new Complex(values[i], 0);
        }

        Transform(grid, height, width, inverse: false);
        return grid;
    }

    /// <summary>
    /// Embeds <paramref name="source"/> at the top-left of a zero grid of the given size — the
    /// padding every frequency-domain product needs so that a circular result reads as a linear one.
    /// </summary>
    /// <param name="source">The values to embed.</param>
    /// <param name="height">The padded height.</param>
    /// <param name="width">The padded width.</param>
    /// <returns>A row-major grid of <paramref name="height"/>·<paramref name="width"/> samples.</returns>
    public static double[] Embed(double[,] source, int height, int width)
    {
        ArgumentNullException.ThrowIfNull(source);
        int rows = source.GetLength(0);
        int cols = source.GetLength(1);
        var padded = new double[height * width];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                padded[(r * width) + c] = source[r, c];
            }
        }

        return padded;
    }

    /// <summary>
    /// The circular cross-correlation of two grids of the same size — <c>ifft2(fft2(a) ·
    /// conj(fft2(b)))</c>, whose value at <c>(u, v)</c> is the sum of <c>a(u+i, v+j)·b(i, j)</c>.
    /// </summary>
    /// <param name="a">The grid being searched, row-major.</param>
    /// <param name="b">The grid being looked for, row-major.</param>
    /// <param name="height">The number of rows in both.</param>
    /// <param name="width">The number of columns in both.</param>
    /// <returns>The real part of the correlation, row-major.</returns>
    public static double[] Correlate(ReadOnlySpan<double> a, ReadOnlySpan<double> b, int height, int width)
    {
        Complex[] left = Forward(a, height, width);
        Complex[] right = Forward(b, height, width);
        for (int i = 0; i < left.Length; i++)
        {
            left[i] *= Complex.Conjugate(right[i]);
        }

        Transform(left, height, width, inverse: true);
        var result = new double[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            result[i] = left[i].Real;
        }

        return result;
    }

    /// <summary>
    /// Moves the zero frequency to the middle of the grid, the way <c>fftshift</c> does — which is
    /// what makes a spectrum something you can resample in polar coordinates around its centre.
    /// </summary>
    /// <param name="grid">The values to shift, row-major.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="width">The number of columns.</param>
    /// <returns>A fresh row-major grid with the quadrants swapped diagonally.</returns>
    public static double[] Shift(ReadOnlySpan<double> grid, int height, int width)
    {
        var shifted = new double[height * width];
        int halfRow = height / 2;
        int halfCol = width / 2;
        for (int r = 0; r < height; r++)
        {
            int target = (r + halfRow) % height;
            for (int c = 0; c < width; c++)
            {
                shifted[(target * width) + ((c + halfCol) % width)] = grid[(r * width) + c];
            }
        }

        return shifted;
    }

    /// <summary>
    /// The same quadrant swap over a complex grid, and its exact inverse.
    /// </summary>
    /// <remarks>
    /// For an odd size the two directions are genuinely different — the origin moves to
    /// <c>floor(n/2)</c> going one way and has to come back from it going the other — and a design
    /// that samples a response on a centred grid has to undo the shift before transforming it. Both
    /// directions live here so the convention is decided in one place.
    /// </remarks>
    /// <param name="grid">The values to shift, row-major.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="inverse">Whether to undo a shift rather than apply one.</param>
    /// <returns>A fresh row-major grid.</returns>
    public static Complex[] Shift(ReadOnlySpan<Complex> grid, int height, int width, bool inverse)
    {
        var shifted = new Complex[height * width];
        int halfRow = height / 2;
        int halfCol = width / 2;
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                if (inverse)
                {
                    shifted[(r * width) + c] = grid[(((r + halfRow) % height) * width) + ((c + halfCol) % width)];
                }
                else
                {
                    shifted[(((r + halfRow) % height) * width) + ((c + halfCol) % width)] = grid[(r * width) + c];
                }
            }
        }

        return shifted;
    }
}
