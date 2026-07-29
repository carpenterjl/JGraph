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
    /// The eigenvalues of square complex <paramref name="matrix"/>, in deflation order.
    /// Values-only: eigenvectors are not computed.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the QR iteration fails to converge.</exception>
    public static Complex[] Values(Complex[,] matrix)
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
    /// The singular values of a general complex matrix, descending. Computed from the Hermitian
    /// Gram matrix AᴴA through its real symmetric 2n embedding [Re −Im; Im Re], whose spectrum is
    /// each eigenvalue of AᴴA twice — unambiguous for the Hermitian case, unlike the general one.
    /// </summary>
    public static double[] SingularValues(Complex[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var gram = new Complex[cols, cols];
        for (int r = 0; r < cols; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < rows; k++)
                {
                    sum += Complex.Conjugate(matrix[k, r]) * matrix[k, c];
                }

                gram[r, c] = sum;
            }
        }

        var embedded = new double[2 * cols, 2 * cols];
        for (int r = 0; r < cols; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                embedded[r, c] = gram[r, c].Real;
                embedded[r, c + cols] = -gram[r, c].Imaginary;
                embedded[r + cols, c] = gram[r, c].Imaginary;
                embedded[r + cols, c + cols] = gram[r, c].Real;
            }
        }

        Eigen eigen = Eigen.Factor(embedded);
        double[] doubled = eigen.Values.Select(static v => v.Real).OrderByDescending(static v => v).ToArray();
        var singular = new double[cols];
        for (int i = 0; i < cols; i++)
        {
            singular[i] = Math.Sqrt(Math.Max(0, doubled[2 * i]));
        }

        return singular;
    }
}
