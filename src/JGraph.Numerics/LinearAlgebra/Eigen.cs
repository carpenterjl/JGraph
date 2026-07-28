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
        Complex[] values = EigenvaluesOf(matrix);

        // Conjugate pairing: a real matrix's complex eigenvalues come in exact conjugate pairs;
        // the iteration's rounding is symmetrized away so downstream code can rely on it.
        for (int i = 0; i < n; i++)
        {
            if (values[i].Imaginary == 0)
            {
                continue;
            }

            for (int j = i + 1; j < n; j++)
            {
                if (values[j].Imaginary == 0 || Math.Sign(values[j].Imaginary) == Math.Sign(values[i].Imaginary))
                {
                    continue;
                }

                Complex mean = (values[i] + Complex.Conjugate(values[j])) / 2;
                values[i] = mean;
                values[j] = Complex.Conjugate(mean);
                break;
            }
        }

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

    private static Complex[] EigenvaluesOf(double[,] matrix)
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

        Complex[,] h = ToComplexHessenberg(matrix);
        var values = new Complex[n];
        int hi = n - 1;
        int stuck = 0;

        while (hi >= 0)
        {
            // Deflate: a negligible subdiagonal splits off the trailing eigenvalue.
            if (hi == 0 || h[hi, hi - 1].Magnitude <=
                1e-14 * (h[hi - 1, hi - 1].Magnitude + h[hi, hi].Magnitude + (scale * 1e-30)))
            {
                values[hi] = h[hi, hi];
                hi--;
                stuck = 0;
                continue;
            }

            if (++stuck > 500)
            {
                // The iteration has stalled; take the trailing entry as the best available answer.
                values[hi] = h[hi, hi];
                hi--;
                stuck = 0;
                continue;
            }

            // Wilkinson shift: the trailing 2x2's eigenvalue closer to the corner entry.
            Complex a = h[hi - 1, hi - 1];
            Complex b = h[hi - 1, hi];
            Complex c = h[hi, hi - 1];
            Complex d = h[hi, hi];
            Complex trace = a + d;
            Complex det = (a * d) - (b * c);
            Complex disc = Complex.Sqrt((trace * trace / 4) - det);
            Complex e1 = (trace / 2) + disc;
            Complex e2 = (trace / 2) - disc;
            Complex shift = (e1 - d).Magnitude < (e2 - d).Magnitude ? e1 : e2;

            QrStep(h, hi, shift);
        }

        // Rounding fuzz: an eigenvalue whose imaginary part is negligible against its size is real.
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(values[i].Imaginary) <= 1e-10 * (1 + values[i].Magnitude))
            {
                values[i] = values[i].Real;
            }
        }

        return values;
    }

    /// <summary>One shifted QR step on the leading (hi+1) block of Hessenberg <paramref name="h"/>, via Givens rotations.</summary>
    private static void QrStep(Complex[,] h, int hi, Complex shift)
    {
        int m = hi + 1;
        var cs = new Complex[m - 1];
        var sn = new Complex[m - 1];

        for (int i = 0; i <= hi; i++)
        {
            h[i, i] -= shift;
        }

        // Zero the subdiagonal with Givens rotations (QR of the shifted Hessenberg block)…
        for (int k = 0; k < m - 1; k++)
        {
            Complex x = h[k, k];
            Complex y = h[k + 1, k];
            double r = Math.Sqrt((x.Magnitude * x.Magnitude) + (y.Magnitude * y.Magnitude));
            if (r == 0)
            {
                cs[k] = 1;
                sn[k] = 0;
                continue;
            }

            cs[k] = x / r;
            sn[k] = y / r;
            for (int c = k; c <= hi; c++)
            {
                Complex top = h[k, c];
                Complex bottom = h[k + 1, c];
                h[k, c] = (Complex.Conjugate(cs[k]) * top) + (Complex.Conjugate(sn[k]) * bottom);
                h[k + 1, c] = (-sn[k] * top) + (cs[k] * bottom);
            }
        }

        // …then multiply the rotations back on the right (RQ) and restore the shift.
        for (int k = 0; k < m - 1; k++)
        {
            int rows = Math.Min(k + 2, hi);
            for (int r = 0; r <= rows; r++)
            {
                Complex left = h[r, k];
                Complex right = h[r, k + 1];
                h[r, k] = (left * cs[k]) + (right * sn[k]);
                h[r, k + 1] = (left * -Complex.Conjugate(sn[k])) + (right * Complex.Conjugate(cs[k]));
            }
        }

        for (int i = 0; i <= hi; i++)
        {
            h[i, i] += shift;
        }
    }

    /// <summary>Householder reduction to upper Hessenberg form, returned as a complex matrix.</summary>
    private static Complex[,] ToComplexHessenberg(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();

        for (int k = 0; k < n - 2; k++)
        {
            double norm = 0;
            for (int r = k + 1; r < n; r++)
            {
                norm += a[r, k] * a[r, k];
            }

            norm = Math.Sqrt(norm);
            if (norm == 0)
            {
                continue;
            }

            if (a[k + 1, k] < 0)
            {
                norm = -norm;
            }

            var v = new double[n];
            v[k + 1] = a[k + 1, k] + norm;
            double vNorm = v[k + 1] * v[k + 1];
            for (int r = k + 2; r < n; r++)
            {
                v[r] = a[r, k];
                vNorm += v[r] * v[r];
            }

            if (vNorm == 0)
            {
                continue;
            }

            // A ← (I − 2vvᵀ/vᵀv) · A · (I − 2vvᵀ/vᵀv)
            for (int c = 0; c < n; c++)
            {
                double s = 0;
                for (int r = k + 1; r < n; r++)
                {
                    s += v[r] * a[r, c];
                }

                s = 2 * s / vNorm;
                for (int r = k + 1; r < n; r++)
                {
                    a[r, c] -= s * v[r];
                }
            }

            for (int r = 0; r < n; r++)
            {
                double s = 0;
                for (int c = k + 1; c < n; c++)
                {
                    s += a[r, c] * v[c];
                }

                s = 2 * s / vNorm;
                for (int c = k + 1; c < n; c++)
                {
                    a[r, c] -= s * v[c];
                }
            }
        }

        var h = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                h[r, c] = r <= c + 1 ? a[r, c] : 0; // clean the eliminated fuzz below the subdiagonal
            }
        }

        return h;
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
