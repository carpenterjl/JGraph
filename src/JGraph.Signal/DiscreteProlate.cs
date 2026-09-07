namespace JGraph.Signal;

/// <summary>
/// <c>dpss</c>: the discrete prolate spheroidal (Slepian) sequences and the fraction of their energy
/// that lands inside the band (M132).
/// </summary>
/// <remarks>
/// <para>
/// The Slepian sequences are the answer to a question about concentration: of all sequences of
/// length N, which one puts the largest share of its energy into the frequency band |f| &lt; W, and
/// which is the best of those orthogonal to it, and so on. Written directly that is an eigenproblem
/// on a full N-by-N kernel whose entries are sinc functions, and it is famously ill-conditioned —
/// the eigenvalues crowd against one and the matrix cannot tell the sequences apart.
/// </para>
/// <para>
/// MATLAB does not solve that problem, and neither does this. There is a symmetric tridiagonal
/// matrix that commutes with the sinc kernel and therefore shares its eigenvectors, and whose own
/// eigenvalues are well separated. So the sequences come from the tridiagonal matrix, by inverse
/// iteration on its eigenvalues, and the concentrations come afterwards from an autocorrelation
/// against the sinc kernel. Two different matrices, one set of vectors.
/// </para>
/// <para>
/// A sign convention is needed because an eigenvector is only defined up to its sign. MATLAB's is
/// that the odd-numbered sequences have a positive mean and the even-numbered ones a positive
/// second sample, and that is reproduced here exactly, because it is what makes two runs of
/// <c>dpss</c> agree with each other.
/// </para>
/// </remarks>
public static class DiscreteProlate
{
    /// <summary>
    /// The sequences <c>k1</c> through <c>k2</c> (one-based, inclusive) of length
    /// <paramref name="n"/> for time-bandwidth product <paramref name="nw"/>, as columns, with
    /// their concentrations.
    /// </summary>
    public static (double[,] Sequences, double[] Concentrations) Compute(int n, double nw, int k1, int k2)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "dpss needs a sequence length of at least one.");
        }

        if (nw < 0 || nw >= n / 2.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nw), "dpss needs a time-bandwidth product below half the sequence length.");
        }

        if (k1 < 1 || k2 < k1 || k2 > n)
        {
            throw new ArgumentOutOfRangeException(nameof(k2), "dpss needs 1 <= K1 <= K2 <= N.");
        }

        double w = nw / n;
        var diagonal = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = n - 1 - (2.0 * i);
            diagonal[i] = t * t * 0.25 * System.Math.Cos(2.0 * System.Math.PI * w);
        }

        var offDiagonal = new double[System.Math.Max(0, n - 1)];
        for (int i = 1; i <= n - 1; i++)
        {
            offDiagonal[i - 1] = (double)i * (n - i) / 2.0;
        }

        // The wanted sequences are the ones with the largest eigenvalues, and the tridiagonal
        // matrix orders them the other way round, so the index window is measured from the top.
        double[] ascending = SelectedEigenvalues(diagonal, offDiagonal, n - k2 + 1, n - k1 + 1);
        int count = ascending.Length;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ascending[count - 1 - i];
        }

        var sequences = new double[n, count];
        var angles = new double[n];
        for (int i = 0; i < n; i++)
        {
            angles[i] = n == 1 ? 0.0 : (double)i / (n - 1) * System.Math.PI;
        }

        var shifted = new double[n];
        for (int j = 0; j < count; j++)
        {
            for (int i = 0; i < n; i++)
            {
                shifted[i] = diagonal[i] - values[j];
            }

            var vector = new double[n];
            for (int i = 0; i < n; i++)
            {
                vector[i] = System.Math.Sin((j + k1) * angles[i]);
            }

            // Three solves against a matrix that is singular to working precision: the first turns
            // any starting vector towards the eigenvector, and the other two are what MATLAB does
            // to be sure of it.
            for (int pass = 0; pass < 3; pass++)
            {
                vector = SolveTridiagonal(shifted, offDiagonal, vector);
                Normalise(vector);
            }

            for (int i = 0; i < n; i++)
            {
                sequences[i, j] = vector[i];
            }
        }

        Polarise(sequences, n, count, k1);
        double[] concentrations = Concentration(sequences, n, count, w);
        return (sequences, concentrations);
    }

    /// <summary>Divides a vector by its two-norm in place.</summary>
    private static void Normalise(double[] v)
    {
        double sum = 0;
        foreach (double x in v)
        {
            sum += x * x;
        }

        double norm = System.Math.Sqrt(sum);
        if (norm == 0 || double.IsNaN(norm))
        {
            return;
        }

        for (int i = 0; i < v.Length; i++)
        {
            v[i] /= norm;
        }
    }

    /// <summary>Applies MATLAB's sign convention to the columns.</summary>
    private static void Polarise(double[,] sequences, int n, int count, int k1)
    {
        var means = new double[count];
        for (int j = 0; j < count; j++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += sequences[i, j];
            }

            means[j] = sum / n;
        }

        for (int j = 0; j < count; j++)
        {
            int order = k1 + j;
            bool flip = (order % 2 != 0)
                ? means[j] < 0
                : n > 1 && sequences[1, j] < 0;
            if (!flip)
            {
                continue;
            }

            for (int i = 0; i < n; i++)
            {
                sequences[i, j] = -sequences[i, j];
            }
        }
    }

    /// <summary>The share of each sequence's energy inside the band.</summary>
    /// <remarks>
    /// The autocorrelation of a sequence against itself, weighted by the band's sinc kernel. MATLAB
    /// reaches it through <c>fftfilt</c>, which for these lengths is the same sum written the fast
    /// way; the sum is written directly here because the answer is one number per sequence and the
    /// transform's own rounding would be the only difference.
    /// </remarks>
    private static double[] Concentration(double[,] sequences, int n, int count, double w)
    {
        var kernel = new double[n];
        kernel[0] = 2.0 * w;
        for (int i = 1; i < n; i++)
        {
            kernel[i] = 4.0 * w * Sinc(2.0 * w * i);
        }

        var answer = new double[count];
        for (int j = 0; j < count; j++)
        {
            double total = 0;
            for (int m = 0; m < n; m++)
            {
                // q(m) is the m-th sample of the convolution of the reversed sequence with itself.
                double q = 0;
                for (int k = 0; k <= m; k++)
                {
                    q += sequences[n - 1 - k, j] * sequences[m - k, j];
                }

                total += q * kernel[n - 1 - m];
            }

            answer[j] = System.Math.Min(1.0, System.Math.Max(0.0, total));
        }

        return answer;
    }

    /// <summary>The normalised sinc, <c>sin(pi x) / (pi x)</c>.</summary>
    private static double Sinc(double x) =>
        x == 0 ? 1.0 : System.Math.Sin(System.Math.PI * x) / (System.Math.PI * x);

    /// <summary>
    /// Eigenvalues <paramref name="lower"/> through <paramref name="upper"/> (one-based, ascending)
    /// of a symmetric tridiagonal matrix, by bisection on the Sturm count.
    /// </summary>
    private static double[] SelectedEigenvalues(double[] diagonal, double[] offDiagonal, int lower, int upper)
    {
        int n = diagonal.Length;
        double left = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double radius = (i > 0 ? System.Math.Abs(offDiagonal[i - 1]) : 0)
                + (i < n - 1 ? System.Math.Abs(offDiagonal[i]) : 0);
            left = System.Math.Min(left, diagonal[i] - radius);
            right = System.Math.Max(right, diagonal[i] + radius);
        }

        double slack = System.Math.Max(1.0, System.Math.Abs(left) + System.Math.Abs(right)) * 1e-12;
        left -= slack;
        right += slack;

        var found = new double[upper - lower + 1];
        for (int index = lower; index <= upper; index++)
        {
            double lo = left;
            double hi = right;
            for (int step = 0; step < 200; step++)
            {
                double mid = (lo + hi) / 2.0;
                if (mid <= lo || mid >= hi)
                {
                    break;
                }

                if (CountBelow(diagonal, offDiagonal, mid) >= index)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            found[index - lower] = (lo + hi) / 2.0;
        }

        return found;
    }

    /// <summary>How many eigenvalues lie strictly below <paramref name="x"/>.</summary>
    private static int CountBelow(double[] diagonal, double[] offDiagonal, double x)
    {
        int n = diagonal.Length;
        int count = 0;
        double q = diagonal[0] - x;
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                double e = offDiagonal[i - 1];
                q = diagonal[i] - x - (e * e / q);
            }

            if (q < 0)
            {
                count++;
            }

            if (q == 0)
            {
                // A pivot of exactly zero would divide the next step by nothing; nudging it keeps
                // the count monotone, which is all bisection asks of it.
                q = -1e-300;
            }
        }

        return count;
    }

    /// <summary>
    /// Solves a symmetric tridiagonal system by Gaussian elimination with partial pivoting, the way
    /// LAPACK's band solver does, so that a matrix which is singular to working precision produces a
    /// large answer rather than a refusal.
    /// </summary>
    private static double[] SolveTridiagonal(double[] diagonal, double[] offDiagonal, double[] rhs)
    {
        int n = diagonal.Length;
        var d = (double[])diagonal.Clone();
        var upper = new double[System.Math.Max(0, n - 1)];
        var upper2 = new double[System.Math.Max(0, n - 2)];
        var lower = new double[System.Math.Max(0, n - 1)];
        var b = (double[])rhs.Clone();
        for (int i = 0; i < n - 1; i++)
        {
            upper[i] = offDiagonal[i];
            lower[i] = offDiagonal[i];
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (System.Math.Abs(d[i]) >= System.Math.Abs(lower[i]))
            {
                double pivot = d[i] == 0 ? Tiny : d[i];
                double factor = lower[i] / pivot;
                d[i] = pivot;
                d[i + 1] -= factor * upper[i];
                b[i + 1] -= factor * b[i];
            }
            else
            {
                double factor = d[i] / lower[i];
                d[i] = lower[i];
                double swap = d[i + 1];
                d[i + 1] = upper[i] - (factor * swap);
                if (i < n - 2)
                {
                    upper2[i] = upper[i + 1];
                    upper[i + 1] = -factor * upper2[i];
                }

                upper[i] = swap;
                (b[i], b[i + 1]) = (b[i + 1], b[i] - (factor * b[i + 1]));
            }
        }

        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = b[i];
            if (i < n - 1)
            {
                sum -= upper[i] * x[i + 1];
            }

            if (i < n - 2)
            {
                sum -= upper2[i] * x[i + 2];
            }

            double pivot = d[i] == 0 ? Tiny : d[i];
            x[i] = sum / pivot;
        }

        return x;
    }

    /// <summary>The stand-in for a pivot that came out exactly zero.</summary>
    private const double Tiny = 1e-300;
}
