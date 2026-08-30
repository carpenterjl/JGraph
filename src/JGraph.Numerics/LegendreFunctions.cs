namespace JGraph.Numerics;

/// <summary>How the associated Legendre functions are scaled.</summary>
public enum LegendreScaling
{
    /// <summary>The classical functions Pₙᵐ, whose size grows like (n+m)!/(n−m)!.</summary>
    Unnormalized,

    /// <summary>Schmidt's semi-normalization, as used in geomagnetism.</summary>
    Schmidt,

    /// <summary>Fully normalized, so that each degree integrates to one over the interval.</summary>
    Full,
}

/// <summary>
/// The associated Legendre functions of one degree, at many arguments.
/// </summary>
/// <remarks>
/// <para>
/// The recurrence runs <em>downward</em> in the order m, from n to nought, and it is carried in the
/// fully normalized scaling whatever scaling was asked for. That is the whole design: the
/// unnormalized functions of a high degree overflow a double long before the normalized ones lose a
/// digit, so the answer is computed where it is well behaved and scaled at the end — and the
/// scaling itself is applied as a running product when its factor alone would overflow, which is
/// what lets degree 150 come back with finite numbers in every row.
/// </para>
/// <para>
/// Near the ends of the interval the seed of that recurrence, (−sin θ)ⁿ, underflows to nothing, and
/// then there is no scale to start from. Those columns are seeded instead at an estimated order
/// where the function is still representable, carried down from an arbitrary tiny value, and
/// normalized afterwards by the sum of squares the recurrence itself produced — the classical trick
/// for a recurrence that is stable in only one direction.
/// </para>
/// </remarks>
public static class LegendreFunctions
{
    /// <summary>
    /// The functions of degree <paramref name="n"/> at every argument, as an (n+1)-by-len block in
    /// column-major order: row m of column j is Pₙᵐ(x[j]).
    /// </summary>
    /// <param name="n">The degree, zero or more.</param>
    /// <param name="x">The arguments, each in [-1, 1].</param>
    /// <param name="scaling">Which scaling the answer carries.</param>
    /// <returns>The (n+1)-by-<c>x.Length</c> block, column-major.</returns>
    public static double[] Associated(int n, double[] x, LegendreScaling scaling)
    {
        int len = x.Length;
        if (n == 0)
        {
            var flat = new double[len];
            double one = scaling == LegendreScaling.Full ? 1.0 / Math.Sqrt(2.0) : 1.0;
            System.Array.Fill(flat, one);
            return flat;
        }

        int rows = n + 1;

        // Three rows of headroom: the recurrence at order m reads orders m+1 and m+2, and the
        // topmost of those is past the end of the answer.
        var p = new double[(n + 3) * len];
        var rootn = new double[(2 * n) + 1];
        for (int k = 0; k <= 2 * n; k++)
        {
            rootn[k] = Math.Sqrt(k);
        }

        var sine = new double[len];
        var twoCot = new double[len];
        var seed = new double[len];
        for (int j = 0; j < len; j++)
        {
            sine[j] = Math.Sqrt(1.0 - (x[j] * x[j]));
            twoCot[j] = -2.0 * x[j] / sine[j];
            seed[j] = Math.Pow(-sine[j], n);
        }

        double tiny = Math.Sqrt(2.2250738585072014e-308);
        for (int j = 0; j < len; j++)
        {
            if (sine[j] > 0 && Math.Abs(seed[j]) <= tiny)
            {
                SeedFromAbove(p, n, len, j, x[j], sine[j], twoCot[j], rootn, tiny);
            }
        }

        double product = 1.0;
        for (int d = 2; d <= 2 * n; d += 2)
        {
            product *= 1.0 - (1.0 / d);
        }

        for (int j = 0; j < len; j++)
        {
            if (x[j] == 1.0 || Math.Abs(seed[j]) < tiny)
            {
                continue;
            }

            p[(j * (n + 3)) + n] = Math.Sqrt(product) * seed[j];
            p[(j * (n + 3)) + n - 1] = p[(j * (n + 3)) + n] * twoCot[j] * n / rootn[2 * n];
            for (int m = n - 2; m >= 0; m--)
            {
                int at = (j * (n + 3)) + m;
                p[at] = ((p[at + 1] * twoCot[j] * (m + 1))
                    - (p[at + 2] * rootn[n + m + 2] * rootn[n - m - 1]))
                    / (rootn[n + m + 1] * rootn[n - m]);
            }
        }

        var y = new double[rows * len];
        for (int j = 0; j < len; j++)
        {
            for (int m = 0; m < rows; m++)
            {
                y[(j * rows) + m] = p[(j * (n + 3)) + m];
            }

            // The two ends of the interval have no sine to build on: only the zeroth order
            // survives, and it is the plain Legendre polynomial's value there.
            if (sine[j] == 0.0)
            {
                y[j * rows] = Math.Pow(x[j], n);
            }
        }

        Rescale(y, n, len, rows, rootn, scaling);
        return y;
    }

    /// <summary>
    /// Seeds a column whose (−sin θ)ⁿ has underflowed: start at an order where the function is
    /// still representable, carry the recurrence down from an arbitrary tiny value, then set the
    /// scale from the sum of squares the whole column must satisfy.
    /// </summary>
    private static void SeedFromAbove(
        double[] p, int n, int len, int j, double xj, double sj, double cot, double[] rootn, double tiny)
    {
        // Where the function first becomes representable, by the asymptotic estimate MATLAB uses.
        double v = 9.2 - (Math.Log(tiny) / (n * sj));
        double w = 1.0 / Math.Log(v);
        double estimate = 1.0 + (n * sj * v * w * (1.0058 + (w * (3.819 - (w * 12.173)))));
        int start = Math.Max(1, (int)Math.Min(n, Math.Floor(estimate)));

        int column = j * (n + 3);
        for (int m = start; m <= n; m++)
        {
            p[column + m] = 0.0;
        }

        // The sign of the arbitrary starting value is chosen so the column comes out with the
        // sign the function has there; the magnitude cancels in the scaling below.
        double spacing = 2.220446049250313e-16;
        double signed = ((start % 2) - 0.5) < 0 ? -spacing : spacing;
        if (xj < 0)
        {
            signed = (((n + 1) % 2) - 0.5) < 0 ? -spacing : spacing;
        }

        p[column + start - 1] = signed;
        double sumOfSquares = tiny;
        for (int m = start - 2; m >= 0; m--)
        {
            int at = column + m;
            p[at] = ((p[at + 1] * cot * (m + 1))
                - (p[at + 2] * rootn[n + m + 2] * rootn[n - m - 1]))
                / (rootn[n + m + 1] * rootn[n - m]);
            sumOfSquares += p[at] * p[at];
        }

        double scale = 1.0 / Math.Sqrt((2.0 * sumOfSquares) - (p[column] * p[column]));
        for (int m = 0; m <= start; m++)
        {
            p[column + m] *= scale;
        }
    }

    /// <summary>Applies the asked-for scaling to the normalized block.</summary>
    private static void Rescale(
        double[] y, int n, int len, int rows, double[] rootn, LegendreScaling scaling)
    {
        switch (scaling)
        {
            case LegendreScaling.Unnormalized:
                for (int m = 1; m <= n - 1; m++)
                {
                    MultiplyRow(y, len, rows, m, rootn, n - m + 1, n + m);
                }

                MultiplyRow(y, len, rows, n, rootn, 1, 2 * n);
                break;

            case LegendreScaling.Schmidt:
                for (int m = 1; m < rows; m++)
                {
                    // The sign alternates from the first order up; the zeroth order is left alone.
                    double sign = m % 2 == 0 ? 1.0 : -1.0;
                    for (int j = 0; j < len; j++)
                    {
                        y[(j * rows) + m] *= Math.Sqrt(2.0) * sign;
                    }
                }

                break;

            case LegendreScaling.Full:
                double factor = Math.Sqrt(n + 0.5);
                for (int m = 0; m < rows; m++)
                {
                    double sign = m % 2 == 0 ? 1.0 : -1.0;
                    for (int j = 0; j < len; j++)
                    {
                        y[(j * rows) + m] *= factor * sign;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Multiplies one order's row by √(first · … · last). The product is taken whole when it is
    /// finite and threaded through each element when it is not, which is the only reason the
    /// unnormalized functions of a high degree have any finite rows at all.
    /// </summary>
    private static void MultiplyRow(
        double[] y, int len, int rows, int m, double[] rootn, int first, int last)
    {
        double whole = 1.0;
        for (int k = first; k <= last; k++)
        {
            whole *= rootn[k];
        }

        if (!double.IsInfinity(whole))
        {
            for (int j = 0; j < len; j++)
            {
                y[(j * rows) + m] *= whole;
            }

            return;
        }

        for (int j = 0; j < len; j++)
        {
            double running = y[(j * rows) + m];
            for (int k = first; k <= last; k++)
            {
                running *= rootn[k];
            }

            y[(j * rows) + m] = running;
        }
    }
}
