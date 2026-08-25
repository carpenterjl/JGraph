using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The dense operations over complex rectangles that MATLAB's operators map onto — the product,
/// the square solve, the determinant, and the inverse — routed through the active backend's
/// z-routines. Until M91 the product was a boxed triple loop and the LU lived in the scripting
/// layer; the solve did not exist at all, and <c>z\b</c> was refused.
/// </summary>
public static class ComplexLinear
{
    /// <summary>The product A·B; inner dimensions must already agree.</summary>
    public static Complex[,] Multiply(Complex[,] a, Complex[,] b)
    {
        int m = a.GetLength(0);
        int inner = a.GetLength(1);
        int n = b.GetLength(1);
        var product = new Complex[m, n];
        if (m == 0 || n == 0 || inner == 0)
        {
            return product;
        }

        Complex[] flatA = Flatten(a);
        Complex[] flatB = Flatten(b);
        var flat = new Complex[(long)m * n];
        LinalgProvider.Current.Zgemm(m, n, inner, flatA, m, flatB, inner, flat, m);
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                product[r, c] = flat[(c * (long)m) + r];
            }
        }

        return product;
    }

    /// <summary>
    /// Solves the square system A·X = B, or reports false when A is singular to the
    /// factorization's precision. Neither input is modified.
    /// </summary>
    public static bool TrySolve(Complex[,] a, Complex[,] b, out Complex[,] solution)
    {
        int n = a.GetLength(0);
        int nrhs = b.GetLength(1);
        Complex[] factor = Flatten(a);
        var pivots = new int[n];
        if (LinalgProvider.Current.Zgetrf(n, n, factor, n, pivots) != 0)
        {
            solution = new Complex[0, 0];
            return false;
        }

        Complex[] rhs = Flatten(b);
        LinalgProvider.Current.Zgetrs(n, nrhs, factor, n, pivots, rhs, n);
        solution = new Complex[n, nrhs];
        for (int c = 0; c < nrhs; c++)
        {
            for (int r = 0; r < n; r++)
            {
                solution[r, c] = rhs[(c * (long)n) + r];
            }
        }

        return true;
    }

    /// <summary>
    /// The determinant: the pivot product, signed by the row swaps. A singular matrix answers the
    /// exact zero its zero pivot puts into the product.
    /// </summary>
    public static Complex Determinant(Complex[,] a)
    {
        int n = a.GetLength(0);
        if (n == 0)
        {
            return Complex.One;
        }

        Complex[] factor = Flatten(a);
        var pivots = new int[n];
        _ = LinalgProvider.Current.Zgetrf(n, n, factor, n, pivots);

        Complex determinant = Complex.One;
        for (int i = 0; i < n; i++)
        {
            determinant *= factor[(i * (long)n) + i];
            if (pivots[i] != i + 1)
            {
                determinant = -determinant;
            }
        }

        return determinant;
    }

    /// <summary>The inverse, or false when the matrix is singular. The input is not modified.</summary>
    public static bool TryInvert(Complex[,] a, out Complex[,] inverse)
    {
        int n = a.GetLength(0);
        Complex[] factor = Flatten(a);
        var pivots = new int[n];
        if (LinalgProvider.Current.Zgetrf(n, n, factor, n, pivots) != 0
            || LinalgProvider.Current.Zgetri(n, factor, n, pivots) != 0)
        {
            inverse = new Complex[0, 0];
            return false;
        }

        inverse = new Complex[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                inverse[r, c] = factor[(c * (long)n) + r];
            }
        }

        return true;
    }

    private static Complex[] Flatten(Complex[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var flat = new Complex[(long)rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * (long)rows) + r] = matrix[r, c];
            }
        }

        return flat;
    }
}
