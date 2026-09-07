namespace JGraph.Signal;

/// <summary>
/// Zero-phase filtering: the same filter run forwards and then backwards, so that whatever it does
/// to the phase it undoes (M132).
/// </summary>
/// <remarks>
/// <para>
/// Running a filter twice in opposite directions cancels its phase exactly and squares its
/// magnitude, which is why this is the tool of choice when a waveform's shape matters more than its
/// causality. The difficulty is entirely at the ends. A filter started from rest sees a step at the
/// first sample and rings for as long as its impulse response lasts, and the backward pass does the
/// same at the other end, so the naive version of this idea is right in the middle and wrong at both
/// edges.
/// </para>
/// <para>
/// The fix here is Gustafsson's and it is MATLAB's: extend the signal at each end by reflecting it
/// through its endpoint, and start each pass from the steady state the filter would be in if its
/// input had always been that endpoint's value. The reflection length is three times the filter
/// order, which is long enough for the transient to have died before the real data begins.
/// </para>
/// <para>
/// This lands in M132 because <c>demod</c> needs it — MATLAB's amplitude and quadrature demodulation
/// low-pass the mixed signal with a zero-phase fifth-order Butterworth. The <c>filtfilt</c> name
/// itself, its second-order-section form and its threading belong to M133.
/// </para>
/// </remarks>
public static class ZeroPhaseFilter
{
    /// <summary>Filters <paramref name="x"/> forwards and backwards through <c>b</c> over <c>a</c>.</summary>
    public static double[] Apply(ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> x)
    {
        if (b.Length == 0 || a.Length == 0)
        {
            return [];
        }

        if (a[0] == 0)
        {
            throw new ArgumentException("A filter's leading denominator coefficient cannot be zero.", nameof(a));
        }

        int m = System.Math.Max(b.Length, a.Length);
        var bn = new double[m];
        var an = new double[m];
        for (int i = 0; i < b.Length; i++)
        {
            bn[i] = b[i] / a[0];
        }

        for (int i = 0; i < a.Length; i++)
        {
            an[i] = a[i] / a[0];
        }

        int order = FilterOrder(bn, an);
        int edge = System.Math.Max(1, 3 * order);
        if (x.Length <= edge)
        {
            throw new ArgumentException(
                $"Zero-phase filtering needs more than {edge} samples for a filter of order {order}.", nameof(x));
        }

        double[] zi = SteadyState(bn, an, m);

        var extended = new double[x.Length + (2 * edge)];
        for (int i = 0; i < edge; i++)
        {
            extended[i] = (2 * x[0]) - x[edge - i];
        }

        for (int i = 0; i < x.Length; i++)
        {
            extended[edge + i] = x[i];
        }

        for (int i = 0; i < edge; i++)
        {
            extended[edge + x.Length + i] = (2 * x[^1]) - x[x.Length - 2 - i];
        }

        double[] once = RunFrom(bn, an, extended, zi, extended[0]);
        Array.Reverse(once);
        double[] twice = RunFrom(bn, an, once, zi, once[0]);

        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            y[i] = twice[twice.Length - 1 - edge - i];
        }

        return y;
    }

    /// <summary>
    /// The steady-state delay line, exposed for M133's <c>filtfilt</c>, which needs one per stage of
    /// a cascade rather than the single one this class's own pass uses.
    /// </summary>
    public static double[] SteadyStateOf(double[] b, double[] a, int m) => SteadyState(b, a, m);

    /// <summary>One pass started from <paramref name="zi"/> scaled by the first sample.</summary>
    private static double[] RunFrom(double[] b, double[] a, double[] x, double[] zi, double first)
    {
        var state = new double[zi.Length];
        for (int i = 0; i < zi.Length; i++)
        {
            state[i] = zi[i] * first;
        }

        return DigitalFilter.Filter(b, a, x, state);
    }

    /// <summary>The order of the filter, which is the last coefficient either side that is not zero.</summary>
    private static int FilterOrder(double[] b, double[] a)
    {
        int order = 0;
        for (int i = b.Length - 1; i >= 0; i--)
        {
            if (b[i] != 0)
            {
                order = System.Math.Max(order, i);
                break;
            }
        }

        for (int i = a.Length - 1; i >= 0; i--)
        {
            if (a[i] != 0)
            {
                order = System.Math.Max(order, i);
                break;
            }
        }

        return order;
    }

    /// <summary>
    /// The delay line a filter settles into when its input has been a constant one for ever, which
    /// is what each pass is started from after scaling by the sample it starts at.
    /// </summary>
    private static double[] SteadyState(double[] b, double[] a, int m)
    {
        int n = m - 1;
        if (n <= 0)
        {
            return [];
        }

        // The steady state solves (I - S) z = b(2:end) - b(1)*a(2:end), where S is the companion
        // shift of the denominator. The matrix is small and the system is solved where it is built.
        var matrix = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            matrix[i, 0] = (i == 0 ? 1.0 : 0.0) + a[i + 1];
        }

        for (int j = 1; j < n; j++)
        {
            matrix[j, j] = 1.0;
            matrix[j - 1, j] = -1.0;
        }

        var rhs = new double[n];
        for (int i = 0; i < n; i++)
        {
            rhs[i] = b[i + 1] - (b[0] * a[i + 1]);
        }

        return SolveDense(matrix, rhs);
    }

    /// <summary>Gaussian elimination with partial pivoting on a small dense system.</summary>
    private static double[] SolveDense(double[,] matrix, double[] rhs)
    {
        int n = rhs.Length;
        for (int column = 0; column < n; column++)
        {
            int pivot = column;
            for (int row = column + 1; row < n; row++)
            {
                if (System.Math.Abs(matrix[row, column]) > System.Math.Abs(matrix[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (pivot != column)
            {
                for (int j = 0; j < n; j++)
                {
                    (matrix[column, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[column, j]);
                }

                (rhs[column], rhs[pivot]) = (rhs[pivot], rhs[column]);
            }

            double head = matrix[column, column];
            if (head == 0)
            {
                continue;
            }

            for (int row = column + 1; row < n; row++)
            {
                double factor = matrix[row, column] / head;
                if (factor == 0)
                {
                    continue;
                }

                for (int j = column; j < n; j++)
                {
                    matrix[row, j] -= factor * matrix[column, j];
                }

                rhs[row] -= factor * rhs[column];
            }
        }

        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = rhs[i];
            for (int j = i + 1; j < n; j++)
            {
                sum -= matrix[i, j] * x[j];
            }

            x[i] = matrix[i, i] == 0 ? 0 : sum / matrix[i, i];
        }

        return x;
    }
}
