namespace JGraph.Signal;

/// <summary>
/// The lattice form: a filter written as a chain of two-way stages rather than as a polynomial, and
/// the conversions that get in and out of it (M133).
/// </summary>
/// <remarks>
/// <para>
/// A lattice stage takes a forward signal and a backward one, mixes each into the other by a single
/// reflection coefficient, and delays the backward one. Chain M of them and the forward output is an
/// FIR filter of order M; feed the chain backwards and it is the all-pole filter with the same
/// coefficients. The attraction is that stability is visible: every reflection coefficient smaller
/// than one in magnitude means a minimum-phase filter, whatever the polynomial's coefficients look
/// like, and rounding a reflection coefficient cannot move a pole outside the circle.
/// </para>
/// <para>
/// The conversions both ways are the Levinson–Durbin recursion, stepped up or stepped down one order
/// at a time. Those two steps — MATLAB calls them <c>levup</c> and <c>levdown</c> — are written out
/// here because <c>tf2latc</c> and <c>latc2tf</c> are M133 names while <c>poly2rc</c>,
/// <c>rc2poly</c> and <c>rlevinson</c>, which are what MATLAB calls to reach them, belong to M137.
/// </para>
/// </remarks>
public static class LatticeFilters
{
    /// <summary>
    /// One order down: the prediction polynomial of order <c>p</c> turned into the one of order
    /// <c>p − 1</c>, which is where a reflection coefficient falls out.
    /// </summary>
    public static (double[] Coefficients, double Error) StepDown(ReadOnlySpan<double> next, double error)
    {
        int n = next.Length - 1;
        double k = next[n];
        if (k == 1.0)
        {
            throw new ArgumentException(
                "A reflection coefficient of exactly one has no lower-order polynomial.", nameof(next));
        }

        double scale = 1 - (k * k);
        var current = new double[n];
        current[0] = 1;
        for (int i = 0; i < n - 1; i++)
        {
            current[i + 1] = (next[i + 1] - (k * next[n - 1 - i])) / scale;
        }

        return (current, error / scale);
    }

    /// <summary>One order up, which is the same recursion read the other way.</summary>
    public static (double[] Coefficients, double Error) StepUp(ReadOnlySpan<double> current, double k, double error)
    {
        int n = current.Length - 1;
        var next = new double[n + 2];
        next[0] = 1;
        for (int i = 0; i < n; i++)
        {
            next[i + 1] = current[i + 1] + (k * current[n - i]);
        }

        next[n + 1] = k;
        return (next, (1 - (k * k)) * error);
    }

    /// <summary>
    /// <c>poly2rc</c>'s arithmetic: a prediction polynomial's reflection coefficients, stepped all
    /// the way down.
    /// </summary>
    public static double[] ReflectionCoefficients(ReadOnlySpan<double> a)
    {
        if (a.Length <= 1)
        {
            return [];
        }

        if (a[0] == 0)
        {
            throw new ArgumentException("A prediction polynomial's first coefficient cannot be zero.", nameof(a));
        }

        int p = a.Length - 1;
        var current = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            current[i] = a[i] / a[0];
        }

        var k = new double[p];
        k[p - 1] = current[p];
        for (int order = p - 1; order >= 1; order--)
        {
            (current, _) = StepDown(current.AsSpan(0, order + 2), 0);
            k[order - 1] = current[order];
        }

        return k;
    }

    /// <summary>
    /// <c>rc2poly</c>'s arithmetic: the prediction polynomial these reflection coefficients build.
    /// </summary>
    public static double[] PredictionPolynomial(ReadOnlySpan<double> k)
    {
        if (k.Length == 0)
        {
            return [1.0];
        }

        double[] a = [1.0, k[0]];
        for (int i = 1; i < k.Length; i++)
        {
            (a, _) = StepUp(a, k[i], 0);
        }

        return a;
    }

    /// <summary>
    /// <c>rlevinson</c>'s second output: the lower-order prediction polynomials stacked as columns,
    /// which is the matrix that turns ladder coefficients into a numerator and back.
    /// </summary>
    public static double[,] PredictionMatrix(ReadOnlySpan<double> a)
    {
        int size = a.Length;
        if (size < 2)
        {
            throw new ArgumentException("A prediction polynomial needs at least two coefficients.", nameof(a));
        }

        var normalised = new double[size];
        for (int i = 0; i < size; i++)
        {
            normalised[i] = a[i] / a[0];
        }

        var u = new double[size, size];
        for (int i = 0; i < size; i++)
        {
            u[i, size - 1] = normalised[size - 1 - i];
        }

        int p = size - 1;
        double[] current = normalised;
        for (int order = p - 1; order >= 1; order--)
        {
            (current, _) = StepDown(current.AsSpan(0, order + 2), 0);
            for (int i = 0; i <= order; i++)
            {
                u[i, order] = current[order - i];
            }
        }

        u[0, 0] = 1;
        return u;
    }

    // --- Transfer function to lattice and back ----------------------------------------------------

    /// <summary>
    /// <c>tf2latc</c>: the lattice and ladder coefficients of a filter given as a ratio.
    /// </summary>
    /// <param name="num">The numerator.</param>
    /// <param name="den">The denominator.</param>
    /// <returns>The reflection coefficients and, for a filter with zeros, the ladder ones.</returns>
    public static (double[] K, double[] V) ToLattice(ReadOnlySpan<double> num, ReadOnlySpan<double> den)
    {
        double[] a = TrimTrailingZeros(den);
        double[] b = TrimTrailingZeros(num);

        if (a.Length == 0 || a[0] == 0)
        {
            throw new ArgumentException("A denominator's leading coefficient cannot be zero.", nameof(den));
        }

        double head = a[0];
        var bn = new double[b.Length];
        for (int i = 0; i < b.Length; i++)
        {
            bn[i] = b[i] / head;
        }

        var an = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            an[i] = a[i] / head;
        }

        if (an.Length == 1)
        {
            // No poles at all: the numerator alone is the FIR lattice.
            return (ReflectionCoefficients(bn), []);
        }

        if (bn.Length == 1)
        {
            // All-pole: the ladder is the numerator followed by zeros.
            double[] k = ReflectionCoefficients(an);
            var v = new double[k.Length + 1];
            v[0] = bn[0];
            return (k, v);
        }

        (double[] padded, double[] denominator) = FilterCoefficients.EqualLength(bn, an);
        int m = denominator.Length;
        double[] reflection = ReflectionCoefficients(denominator);
        double[,] u = PredictionMatrix(denominator);

        var ladder = new double[m];
        for (int row = m - 1; row >= 0; row--)
        {
            double sum = 0;
            for (int c = 0; c < m; c++)
            {
                sum += u[row, c] * ladder[c];
            }

            ladder[row] = padded[row] - sum;
        }

        return (reflection, ladder);
    }

    /// <summary>
    /// <c>latc2tf</c>: the ratio a lattice stands for, in whichever of its four readings the caller
    /// asks for.
    /// </summary>
    public static (double[] Numerator, double[] Denominator) FromLattice(ReadOnlySpan<double> k, string kind)
    {
        switch (kind)
        {
            case "fir":
            case "min":
            case "max":
            {
                if (k.Length == 0)
                {
                    return ([1.0], [1.0]);
                }

                double[] num = PredictionPolynomial(k);
                if (kind == "max")
                {
                    num = FilterCoefficients.Reversed(num);
                }

                return (num, [1.0]);
            }

            case "allpass":
            {
                if (k.Length == 0)
                {
                    return ([1.0], [1.0]);
                }

                double[] den = PredictionPolynomial(k);
                return (FilterCoefficients.Reversed(den), den);
            }

            default:
                throw new ArgumentException($"'{kind}' is not one of a lattice's readings.", nameof(kind));
        }
    }

    /// <summary>The ladder form: the reflection coefficients give the poles and the ladder the zeros.</summary>
    public static (double[] Numerator, double[] Denominator) FromLadder(ReadOnlySpan<double> k, ReadOnlySpan<double> v)
    {
        if (k.Length == 0 && v.Length == 0)
        {
            return ([], []);
        }

        if (k.Length == 0 && v.Length == 1)
        {
            return ([v[0]], [1.0]);
        }

        int difference = v.Length - k.Length - 1;
        double[] reflection = k.ToArray();
        double[] ladder = v.ToArray();
        if (difference > 0)
        {
            Array.Resize(ref reflection, reflection.Length + difference);
        }
        else if (difference < 0)
        {
            Array.Resize(ref ladder, ladder.Length - difference);
        }

        double[] den = PredictionPolynomial(reflection);
        double[,] u = PredictionMatrix(den);

        int m = den.Length;
        var num = new double[m];
        for (int r = 0; r < m; r++)
        {
            double sum = 0;
            for (int c = 0; c < m && c < ladder.Length; c++)
            {
                sum += u[r, c] * ladder[c];
            }

            num[r] = sum;
        }

        return (num, den);
    }

    // --- Running a lattice ------------------------------------------------------------------------

    /// <summary>What a lattice run answers: two signals and the delay line it finished with.</summary>
    public readonly record struct LatticeRun(double[] Forward, double[] Backward, double[] Final);

    /// <summary>
    /// The feed-forward lattice: the forward output is the FIR filter's, the backward output is its
    /// mirror image, and the state is one delayed backward sample per stage.
    /// </summary>
    public static LatticeRun RunFeedForward(ReadOnlySpan<double> k, ReadOnlySpan<double> x, ReadOnlySpan<double> zi)
    {
        int m = k.Length;
        var state = new double[m];
        for (int i = 0; i < m && i < zi.Length; i++)
        {
            state[i] = zi[i];
        }

        var f = new double[x.Length];
        var g = new double[x.Length];

        for (int n = 0; n < x.Length; n++)
        {
            double forward = x[n];
            double backward = x[n];
            for (int stage = 0; stage < m; stage++)
            {
                double delayed = state[stage];
                state[stage] = backward;
                backward = (k[stage] * forward) + delayed;
                forward += k[stage] * delayed;
            }

            f[n] = forward;
            g[n] = backward;
        }

        return new LatticeRun(f, g, state);
    }

    /// <summary>
    /// The feedback lattice: the same stages walked from the last to the first, which turns the FIR
    /// filter into the all-pole one with those reflection coefficients. The ladder coefficients, if
    /// there are any, tap the backward signals to put the zeros back.
    /// </summary>
    public static LatticeRun RunFeedback(
        ReadOnlySpan<double> k, ReadOnlySpan<double> v, ReadOnlySpan<double> x, ReadOnlySpan<double> zi)
    {
        int m = k.Length;
        var state = new double[m];
        for (int i = 0; i < m && i < zi.Length; i++)
        {
            state[i] = zi[i];
        }

        var f = new double[x.Length];
        var g = new double[x.Length];
        var backwards = new double[m + 1];

        for (int n = 0; n < x.Length; n++)
        {
            double forward = x[n];
            for (int stage = m - 1; stage >= 0; stage--)
            {
                forward -= k[stage] * state[stage];
                backwards[stage + 1] = (k[stage] * forward) + state[stage];
            }

            backwards[0] = forward;
            for (int stage = 0; stage < m; stage++)
            {
                state[stage] = backwards[stage];
            }

            if (v.Length == 0)
            {
                f[n] = forward;
            }
            else
            {
                double sum = 0;
                for (int stage = 0; stage < v.Length && stage <= m; stage++)
                {
                    sum += v[stage] * backwards[stage];
                }

                f[n] = sum;
            }

            g[n] = backwards[m];
        }

        return new LatticeRun(f, g, state);
    }

    /// <summary>A polynomial with its trailing zeros removed, which is MATLAB's own first move.</summary>
    private static double[] TrimTrailingZeros(ReadOnlySpan<double> p)
    {
        int last = p.Length - 1;
        while (last > 0 && p[last] == 0)
        {
            last--;
        }

        return p[..(last + 1)].ToArray();
    }
}
