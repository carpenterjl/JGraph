using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Eigenvalues and eigenvectors of a real square matrix. A symmetric matrix takes the cyclic
/// Jacobi path (real, ascending eigenvalues, orthonormal vectors, MATLAB's symmetric order); a
/// general matrix reduces to Hessenberg form, finds its eigenvalues by complex single-shift QR
/// iteration, and recovers each eigenvector by inverse iteration on the original matrix.
/// </summary>
public sealed class Eigen
{
    private Eigen(Complex[] values, Complex[,] vectors)
    {
        Values = values;
        Vectors = vectors;
    }

    /// <summary>The eigenvalues (real ones carry a zero imaginary part).</summary>
    public Complex[] Values { get; }

    /// <summary>The eigenvectors, one column per eigenvalue, each with unit 2-norm.</summary>
    public Complex[,] Vectors { get; }

    /// <summary>Whether every eigenvalue (and so every vector) is real.</summary>
    public bool IsReal
    {
        get
        {
            foreach (Complex value in Values)
            {
                if (value.Imaginary != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Factors square <paramref name="matrix"/>; the input is not modified.</summary>
    /// <exception cref="ArgumentException">The matrix is not square.</exception>
    public static Eigen Factor(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n)
        {
            throw new ArgumentException("Eigen decomposition needs a square matrix.", nameof(matrix));
        }

        return IsSymmetric(matrix) ? FactorSymmetric(matrix) : FactorGeneral(matrix);
    }

    private static bool IsSymmetric(double[,] a)
    {
        int n = a.GetLength(0);
        double scale = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                scale = Math.Max(scale, Math.Abs(a[r, c]));
            }
        }

        double tolerance = scale * 1e-12;
        for (int r = 0; r < n; r++)
        {
            for (int c = r + 1; c < n; c++)
            {
                if (Math.Abs(a[r, c] - a[c, r]) > tolerance)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // --- Symmetric: cyclic Jacobi -------------------------------------------------------------

    private static Eigen FactorSymmetric(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            v[i, i] = 1;
        }

        for (int sweep = 0; sweep < 60; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    off += a[p, q] * a[p, q];
                }
            }

            if (off < 1e-30)
            {
                break;
            }

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    if (a[p, q] == 0)
                    {
                        continue;
                    }

                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(1 + (theta * theta)));
                    if (theta == 0)
                    {
                        t = 1;
                    }

                    double c = 1 / Math.Sqrt(1 + (t * t));
                    double s = t * c;

                    for (int r = 0; r < n; r++)
                    {
                        double arp = a[r, p];
                        double arq = a[r, q];
                        a[r, p] = (c * arp) - (s * arq);
                        a[r, q] = (s * arp) + (c * arq);
                    }

                    for (int col = 0; col < n; col++)
                    {
                        double apc = a[p, col];
                        double aqc = a[q, col];
                        a[p, col] = (c * apc) - (s * aqc);
                        a[q, col] = (s * apc) + (c * aqc);
                    }

                    for (int r = 0; r < n; r++)
                    {
                        double vrp = v[r, p];
                        double vrq = v[r, q];
                        v[r, p] = (c * vrp) - (s * vrq);
                        v[r, q] = (s * vrp) + (c * vrq);
                    }
                }
            }
        }

        // MATLAB reports a symmetric matrix's eigenvalues in ascending order.
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (x, y) => a[x, x].CompareTo(a[y, y]));

        var values = new Complex[n];
        var vectors = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            values[i] = a[order[i], order[i]];
            for (int r = 0; r < n; r++)
            {
                vectors[r, i] = v[r, order[i]];
            }
        }

        return new Eigen(values, vectors);
    }

    // --- General: Hessenberg + complex shifted QR, then inverse iteration -----------------------

    private static Eigen FactorGeneral(double[,] matrix)
    {
        int n = matrix.GetLength(0);

        // The eigenvalues are read off the real Schur form. Doing it that way rather than running a
        // shifted QR in complex arithmetic is what makes them right: the Schur factor is produced by
        // an orthogonal similarity that is checked by reassembly, so the diagonal blocks carry the
        // matrix's own spectrum — the conjugate pairs come out exactly paired, and the values
        // reproduce the trace and the determinant to the last few digits rather than merely
        // approximately.
        Complex[] values = Schur.Factor(matrix).Eigenvalues;

        var vectors = new Complex[n, n];
        for (int k = 0; k < n; k++)
        {
            Complex[] vector = InverseIteration(matrix, values[k]);
            for (int r = 0; r < n; r++)
            {
                vectors[r, k] = vector[r];
            }
        }

        return new Eigen(values, vectors);
    }

    /// <summary>Inverse iteration: a few solves against (A − λ̃I) pull out λ's eigenvector.</summary>
    private static Complex[] InverseIteration(double[,] matrix, Complex value)
    {
        int n = matrix.GetLength(0);
        double scale = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                scale = Math.Max(scale, Math.Abs(matrix[r, c]));
            }
        }

        // Perturb the shift so the system is merely ill-conditioned, not exactly singular —
        // ill-conditioned is exactly what makes inverse iteration converge in one or two solves.
        Complex shift = value + ((scale + 1) * 1e-10);
        var shifted = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                shifted[r, c] = matrix[r, c];
            }

            shifted[r, r] -= shift;
        }

        var x = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = 1.0 / Math.Sqrt(n);
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            Complex[] next = SolveComplex(shifted, x);
            double norm = 0;
            foreach (Complex entry in next)
            {
                norm += entry.Magnitude * entry.Magnitude;
            }

            norm = Math.Sqrt(norm);
            if (norm == 0 || double.IsNaN(norm) || double.IsInfinity(norm))
            {
                break;
            }

            for (int i = 0; i < n; i++)
            {
                x[i] = next[i] / norm;
            }
        }

        // Fix the free phase: make the largest entry real and positive, so results are stable.
        int biggest = 0;
        for (int i = 1; i < n; i++)
        {
            if (x[i].Magnitude > x[biggest].Magnitude)
            {
                biggest = i;
            }
        }

        if (x[biggest].Magnitude > 0)
        {
            Complex phase = x[biggest] / x[biggest].Magnitude;
            for (int i = 0; i < n; i++)
            {
                x[i] /= phase;
            }
        }

        return x;
    }

    /// <summary>Complex Gaussian elimination with partial pivoting.</summary>
    private static Complex[] SolveComplex(Complex[,] matrix, Complex[] b)
    {
        int n = b.Length;
        var a = (Complex[,])matrix.Clone();
        var x = (Complex[])b.Clone();

        for (int k = 0; k < n; k++)
        {
            int best = k;
            for (int r = k + 1; r < n; r++)
            {
                if (a[r, k].Magnitude > a[best, k].Magnitude)
                {
                    best = r;
                }
            }

            if (best != k)
            {
                for (int c = k; c < n; c++)
                {
                    (a[k, c], a[best, c]) = (a[best, c], a[k, c]);
                }

                (x[k], x[best]) = (x[best], x[k]);
            }

            if (a[k, k] == Complex.Zero)
            {
                a[k, k] = 1e-300; // keep the elimination moving on an exactly singular pivot
            }

            for (int r = k + 1; r < n; r++)
            {
                Complex factor = a[r, k] / a[k, k];
                for (int c = k + 1; c < n; c++)
                {
                    a[r, c] -= factor * a[k, c];
                }

                x[r] -= factor * x[k];
            }
        }

        for (int k = n - 1; k >= 0; k--)
        {
            for (int c = k + 1; c < n; c++)
            {
                x[k] -= a[k, c] * x[c];
            }

            x[k] /= a[k, k];
        }

        return x;
    }
}
