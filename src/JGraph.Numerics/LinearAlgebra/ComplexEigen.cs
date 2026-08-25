using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Eigenvalues of a general complex matrix: Householder reduction to upper Hessenberg, then
/// explicit single-shift QR with the Wilkinson shift. Complex arithmetic is what makes the single
/// shift sufficient — the real double-shift dance in <see cref="Schur"/> exists only to avoid
/// complex intermediates, and here they are the point.
/// </summary>
public static class ComplexEigen
{
    /// <summary>
    /// The eigenvalues of square complex <paramref name="matrix"/>, through the active backend.
    /// Values-only: eigenvectors are not computed.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the eigensolver fails to converge.</exception>
    public static Complex[] Values(Complex[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (n == 0)
        {
            return [];
        }

        Complex[] work = FlattenRect(matrix, n, n);
        var values = new Complex[n];
        if (LinalgProvider.Current.Zgeev(vectors: false, n, work, n, values, [], 1) != 0)
        {
            throw new InvalidOperationException("Complex eigenvalue iteration did not converge.");
        }

        return values;
    }

    /// <summary>
    /// The eigenvalues with their right eigenvectors — <c>[V, D] = eig(A)</c> for a complex A.
    /// Each vector has unit length with its largest component's phase fixed, which is both
    /// backends' convention.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the eigensolver fails to converge.</exception>
    public static (Complex[] Values, Complex[,] Vectors) Factor(Complex[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (n == 0)
        {
            return ([], new Complex[0, 0]);
        }

        Complex[] work = FlattenRect(matrix, n, n);
        var values = new Complex[n];
        var flat = new Complex[(long)n * n];
        if (LinalgProvider.Current.Zgeev(vectors: true, n, work, n, values, flat, n) != 0)
        {
            throw new InvalidOperationException("Complex eigenvalue iteration did not converge.");
        }

        var vectors = new Complex[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                vectors[r, c] = flat[(c * n) + r];
            }
        }

        return (values, vectors);
    }

    /// <summary>
    /// The managed single-shift QR iteration behind <see cref="Values"/>, reached directly by
    /// <see cref="ManagedLinalg"/>. Overwrites the matrix it is handed.
    /// </summary>
    internal static Complex[] ValuesManaged(Complex[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (n == 0)
        {
            return [];
        }

        if (n == 1)
        {
            return [matrix[0, 0]];
        }

        Complex[,] h = ToHessenberg(matrix);
        var values = new Complex[n];
        int high = n - 1;
        int sinceDeflation = 0;

        while (high >= 0)
        {
            // Deflate every negligible subdiagonal from the bottom up.
            int low = high;
            while (low > 0 && !Negligible(h, low))
            {
                low--;
            }

            if (low == high)
            {
                values[high] = h[high, high];
                high--;
                sinceDeflation = 0;
                continue;
            }

            if (++sinceDeflation > 30 * n)
            {
                throw new InvalidOperationException("Complex eigenvalue iteration did not converge.");
            }

            // Wilkinson shift: the eigenvalue of the trailing 2x2 closer to the corner entry.
            // Every tenth stagnant sweep takes an exceptional shift to break symmetry cycles.
            Complex shift;
            if (sinceDeflation % 10 == 0)
            {
                shift = h[high, high] + new Complex(h[high, high - 1].Magnitude, 0);
            }
            else
            {
                Complex a = h[high - 1, high - 1];
                Complex b = h[high - 1, high];
                Complex c = h[high, high - 1];
                Complex d = h[high, high];
                Complex trace = a + d;
                Complex discriminant = Complex.Sqrt((trace * trace) - (4 * ((a * d) - (b * c))));
                Complex first = (trace + discriminant) / 2;
                Complex second = (trace - discriminant) / 2;
                shift = (first - d).Magnitude <= (second - d).Magnitude ? first : second;
            }

            QrStep(h, low, high, shift);
        }

        return values;
    }

    /// <summary>Whether the subdiagonal entering row <paramref name="row"/> is negligible (and zeroed).</summary>
    private static bool Negligible(Complex[,] h, int row)
    {
        double scale = h[row - 1, row - 1].Magnitude + h[row, row].Magnitude;
        if (scale == 0)
        {
            scale = 1;
        }

        if (h[row, row - 1].Magnitude <= 1e-15 * scale)
        {
            h[row, row - 1] = Complex.Zero;
            return true;
        }

        return false;
    }

    /// <summary>One explicit shifted QR iteration on the active block, by complex Givens rotations.</summary>
    private static void QrStep(Complex[,] h, int low, int high, Complex shift)
    {
        int n = h.GetLength(0);
        for (int i = low; i <= high; i++)
        {
            h[i, i] -= shift;
        }

        int rotations = high - low;
        var cosines = new double[rotations];
        var sines = new Complex[rotations];

        // Q^H (H - sI): zero each subdiagonal in turn.
        for (int k = low; k < high; k++)
        {
            (double c, Complex s) = Givens(h[k, k], h[k + 1, k]);
            cosines[k - low] = c;
            sines[k - low] = s;
            for (int col = k; col < n; col++)
            {
                Complex top = h[k, col];
                Complex bottom = h[k + 1, col];
                h[k, col] = (c * top) + (s * bottom);
                h[k + 1, col] = (-Complex.Conjugate(s) * top) + (c * bottom);
            }
        }

        // (R) Q: apply the same rotations on the right, restoring Hessenberg form.
        for (int k = low; k < high; k++)
        {
            double c = cosines[k - low];
            Complex s = sines[k - low];
            int limit = Math.Min(k + 2, high);
            for (int row = low; row <= limit; row++)
            {
                Complex left = h[row, k];
                Complex right = h[row, k + 1];
                h[row, k] = (c * left) + (Complex.Conjugate(s) * right);
                h[row, k + 1] = (-s * left) + (c * right);
            }
        }

        for (int i = low; i <= high; i++)
        {
            h[i, i] += shift;
        }
    }

    /// <summary>The complex Givens rotation sending (f, g) to (r, 0), with a real cosine.</summary>
    private static (double C, Complex S) Givens(Complex f, Complex g)
    {
        double fMagnitude = f.Magnitude;
        double gMagnitude = g.Magnitude;
        if (gMagnitude == 0)
        {
            return (1, Complex.Zero);
        }

        if (fMagnitude == 0)
        {
            return (0, Complex.Conjugate(g) / gMagnitude);
        }

        double r = double.Hypot(fMagnitude, gMagnitude);
        double c = fMagnitude / r;
        Complex s = (f / fMagnitude) * Complex.Conjugate(g) / r;
        return (c, s);
    }

    /// <summary>Householder reduction of a copy of <paramref name="matrix"/> to upper Hessenberg form.</summary>
    private static Complex[,] ToHessenberg(Complex[,] matrix)
    {
        int n = matrix.GetLength(0);
        var h = (Complex[,])matrix.Clone();
        var v = new Complex[n];
        for (int k = 0; k < n - 2; k++)
        {
            double norm = 0;
            for (int i = k + 1; i < n; i++)
            {
                norm = double.Hypot(norm, h[i, k].Magnitude);
            }

            if (norm <= 1e-300)
            {
                continue;
            }

            // alpha carries the phase of the pivot so the reflection is numerically benign.
            Complex pivot = h[k + 1, k];
            Complex alpha = pivot == Complex.Zero ? -norm : -(pivot / pivot.Magnitude) * norm;

            double vNorm = 0;
            for (int i = k + 1; i < n; i++)
            {
                v[i] = h[i, k];
            }

            v[k + 1] -= alpha;
            for (int i = k + 1; i < n; i++)
            {
                vNorm = double.Hypot(vNorm, v[i].Magnitude);
            }

            if (vNorm <= 1e-300)
            {
                continue;
            }

            for (int i = k + 1; i < n; i++)
            {
                v[i] /= vNorm;
            }

            // H ← (I − 2vvᴴ) H
            for (int col = k; col < n; col++)
            {
                Complex dot = Complex.Zero;
                for (int i = k + 1; i < n; i++)
                {
                    dot += Complex.Conjugate(v[i]) * h[i, col];
                }

                dot *= 2;
                for (int i = k + 1; i < n; i++)
                {
                    h[i, col] -= dot * v[i];
                }
            }

            // H ← H (I − 2vvᴴ)
            for (int row = 0; row < n; row++)
            {
                Complex dot = Complex.Zero;
                for (int i = k + 1; i < n; i++)
                {
                    dot += h[row, i] * v[i];
                }

                dot *= 2;
                for (int i = k + 1; i < n; i++)
                {
                    h[row, i] -= dot * Complex.Conjugate(v[i]);
                }
            }
        }

        return h;
    }

    /// <summary>
    /// The singular values of a general complex matrix, descending, through the active backend.
    /// Until M91 these came from the eigenvalues of the Gram matrix AᴴA — squaring the condition
    /// number on the way — and now they come from a genuine complex SVD on both backends.
    /// </summary>
    public static double[] SingularValues(Complex[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        if (rows == 0 || cols == 0)
        {
            return [];
        }

        Complex[] work = FlattenRect(matrix, rows, cols);
        var values = new double[Math.Min(rows, cols)];
        if (LinalgProvider.Current.Zgesdd(SvdVectors.None, rows, cols, work, rows, values, [], 1, [], 1) != 0)
        {
            throw new InvalidOperationException("The complex singular value decomposition did not converge.");
        }

        return values;
    }

    /// <summary>
    /// The complex SVD's factors, A = U·Σ·Vᴴ — <c>[U, S, V] = svd(A)</c> for a complex A, in
    /// MATLAB's shapes: full m×m and n×n factors, or both cut to min(m, n) columns for the
    /// economy forms. V is handed back as V itself, not the Vᴴ LAPACK stores.
    /// </summary>
    public static (Complex[,] U, double[] S, Complex[,] V) Svd(Complex[,] matrix, bool economy)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        int k = Math.Min(rows, cols);
        SvdVectors job = economy ? SvdVectors.Economy : SvdVectors.All;
        int uColumns = economy ? k : rows;
        int vtRows = economy ? k : cols;

        Complex[] work = FlattenRect(matrix, rows, cols);
        var values = new double[k];
        var u = new Complex[(long)rows * uColumns];
        var vt = new Complex[(long)vtRows * cols];
        if (LinalgProvider.Current.Zgesdd(job, rows, cols, work, rows, values, u, rows, vt, vtRows) != 0)
        {
            throw new InvalidOperationException("The complex singular value decomposition did not converge.");
        }

        var left = new Complex[rows, uColumns];
        for (int c = 0; c < uColumns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                left[r, c] = u[(c * (long)rows) + r];
            }
        }

        var right = new Complex[cols, vtRows];
        for (int c = 0; c < vtRows; c++)
        {
            for (int r = 0; r < cols; r++)
            {
                right[r, c] = Complex.Conjugate(vt[(r * (long)vtRows) + c]);
            }
        }

        return (left, values, right);
    }

    private static Complex[] FlattenRect(Complex[,] matrix, int rows, int cols)
    {
        var work = new Complex[(long)rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                work[(c * (long)rows) + r] = matrix[r, c];
            }
        }

        return work;
    }
}
