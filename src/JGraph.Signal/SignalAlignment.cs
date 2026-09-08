namespace JGraph.Signal;

/// <summary>Which distance two samples are compared by.</summary>
public enum SampleMetric
{
    /// <summary>The sum of the absolute differences over the rows.</summary>
    Absolute,

    /// <summary>The root of the sum of the squared differences.</summary>
    Euclidean,

    /// <summary>The sum of the squared differences.</summary>
    Squared,

    /// <summary>The symmetric Kullback–Leibler divergence, for non-negative data.</summary>
    SymmetricKl,
}

/// <summary>
/// Aligning one signal with another: dynamic time warping, the edit distance on real sequences, and
/// the search for a signal inside a longer record.
/// </summary>
/// <remarks>
/// All three fill the same table. What differs is what goes in a cell — an accumulated distance for
/// warping, a count of edits for the edit distance — and where the path is allowed to start. The
/// traceback is shared, and it is written to prefer the same step MATLAB's does when two are tied,
/// because a warping path is an output and not merely a means to one.
/// </remarks>
public static class SignalAlignment
{
    /// <summary>The distance between one column of each signal.</summary>
    public static double Distance(double[,] x, int i, double[,] y, int j, SampleMetric metric)
    {
        int rows = x.GetLength(0);
        double total = 0;
        switch (metric)
        {
            case SampleMetric.Absolute:
                for (int r = 0; r < rows; r++)
                {
                    total += System.Math.Abs(x[r, i] - y[r, j]);
                }

                return total;
            case SampleMetric.Squared:
                for (int r = 0; r < rows; r++)
                {
                    double d = x[r, i] - y[r, j];
                    total += d * d;
                }

                return total;
            case SampleMetric.SymmetricKl:
                const double tiny = 2.2250738585072014e-308;
                for (int r = 0; r < rows; r++)
                {
                    double a = System.Math.Max(x[r, i], tiny);
                    double b = System.Math.Max(y[r, j], tiny);
                    total += (a - b) * (System.Math.Log(a) - System.Math.Log(b));
                }

                return total;
            default:
                for (int r = 0; r < rows; r++)
                {
                    double d = x[r, i] - y[r, j];
                    total += d * d;
                }

                return System.Math.Sqrt(total);
        }
    }

    /// <summary>The cumulative warping distance table, with an optional band around the diagonal.</summary>
    public static double[,] WarpTable(double[,] x, double[,] y, SampleMetric metric, int? band)
    {
        int nx = x.GetLength(1);
        int ny = y.GetLength(1);
        var c = new double[nx, ny];
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                c[i, j] = double.NaN;
            }
        }

        double slope = ny > 1 ? (nx - 1.0) / (ny - 1) : 0;
        int Lower(int j) => band is int r
            ? System.Math.Max((int)System.Math.Ceiling((j * slope) - r), 0)
            : 0;
        int Upper(int j) => band is int r
            ? System.Math.Min((int)System.Math.Floor((j * slope) + r), nx - 1)
            : nx - 1;

        double running = 0;
        for (int i = Lower(0); i <= Upper(0); i++)
        {
            running += Distance(x, i, y, 0, metric);
            c[i, 0] = running;
        }

        for (int j = 1; j < ny; j++)
        {
            int lower = Lower(j);
            int upper = Upper(j);
            for (int i = lower; i <= upper; i++)
            {
                double best = double.PositiveInfinity;
                if (i > 0 && j > 0 && !double.IsNaN(c[i - 1, j - 1]))
                {
                    best = c[i - 1, j - 1];
                }

                if (i > 0 && !double.IsNaN(c[i - 1, j]))
                {
                    best = System.Math.Min(best, c[i - 1, j]);
                }

                if (!double.IsNaN(c[i, j - 1]))
                {
                    best = System.Math.Min(best, c[i, j - 1]);
                }

                if (double.IsPositiveInfinity(best))
                {
                    best = i > 0 && !double.IsNaN(c[i - 1, j]) ? c[i - 1, j] : 0;
                }

                c[i, j] = best + Distance(x, i, y, j, metric);
            }
        }

        return c;
    }

    /// <summary>The edit-distance table: a step across costs nothing when the two samples are close.</summary>
    public static double[,] EditTable(double[,] x, double[,] y, double tolerance, SampleMetric metric)
    {
        int nx = x.GetLength(1);
        int ny = y.GetLength(1);
        var c = new double[nx, ny];
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                double match = Distance(x, i, y, j, metric) > tolerance ? 1 : 0;
                double diagonal = i > 0 && j > 0 ? c[i - 1, j - 1] : System.Math.Max(i, j);
                double up = i > 0 ? c[i - 1, j] : j + 1;
                double left = j > 0 ? c[i, j - 1] : i + 1;
                c[i, j] = System.Math.Min(diagonal + match, System.Math.Min(up + 1, left + 1));
            }
        }

        return c;
    }

    /// <summary>
    /// The path back through a cumulative table, from its far corner to its near one. Ties break
    /// the way MATLAB's do: a diagonal step is taken when it is no worse than either single step.
    /// </summary>
    public static (int[] X, int[] Y) Traceback(double[,] c)
    {
        int m = c.GetLength(0);
        int n = c.GetLength(1);
        var ix = new List<int> { m - 1 };
        var iy = new List<int> { n - 1 };
        int i = m - 1;
        int j = n - 1;
        while (i > 0 || j > 0)
        {
            if (j == 0)
            {
                i--;
            }
            else if (i == 0)
            {
                j--;
            }
            else
            {
                double diagonal = c[i - 1, j - 1];
                double up = c[i - 1, j];
                double left = c[i, j - 1];
                bool stepUp = up <= left || diagonal <= left || double.IsNaN(left);
                bool stepLeft = left < up || diagonal <= up || double.IsNaN(up);
                if (stepUp)
                {
                    i--;
                }

                if (stepLeft)
                {
                    j--;
                }
            }

            ix.Add(i);
            iy.Add(j);
        }

        ix.Reverse();
        iy.Reverse();
        return ([.. ix], [.. iy]);
    }

    /// <summary>
    /// The distance from a pattern to every window of the same length in a longer record, which is
    /// what <c>findsignal</c>'s fixed alignment minimises.
    /// </summary>
    public static double[] SlidingDistance(double[,] data, double[,] pattern, SampleMetric metric)
    {
        int n = data.GetLength(1);
        int m = pattern.GetLength(1);
        if (n < m)
        {
            return [];
        }

        var distance = new double[n - m + 1];
        for (int start = 0; start <= n - m; start++)
        {
            double total = 0;
            for (int k = 0; k < m; k++)
            {
                total += Distance(data, start + k, pattern, k, metric);
            }

            distance[start] = total;
        }

        return distance;
    }
}
