using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>
/// The three designs that are neither least squares nor an exchange: the constrained least-squares
/// pair and the maximally flat filter (M134).
/// </summary>
/// <remarks>
/// <para>
/// <c>fircls</c> and <c>fircls1</c> minimise the squared error subject to the response staying
/// between two given curves. That is a quadratic program, and MATLAB solves it the way quadratic
/// programs were solved before there were quadratic programming solvers: guess which constraints
/// are active, solve the equality-constrained problem, drop any constraint whose multiplier came
/// back negative, and repeat. Each step needs the response's local extremes to sub-grid accuracy,
/// which is a short Newton refinement on the derivative of the cosine series.
/// </para>
/// <para>
/// <c>maxflat</c> is a different thing altogether: no optimisation, a table. It works out from the
/// requested zero and pole counts which distribution of zeros between the band edge and the
/// passband gives a monotonic response with the cutoff where it was asked for, reads that off a
/// table it builds by root-finding, and then writes the filter down in closed form from binomial
/// coefficients. The binomials are the generalised ones, defined for negative arguments, which is
/// why the helper is longer than a factorial ratio.
/// </para>
/// </remarks>
public static class ConstrainedFirDesign
{
    /// <summary>The convergence threshold both constrained designs use.</summary>
    private const double Small = 1e-8;

    /// <summary>How many multiplier iterations either design is allowed.</summary>
    private const int MaxIterations = 20000;

    /// <summary>
    /// <c>fircls</c>: the least-squares design of a multiband response whose value in each band is
    /// held between an upper and a lower bound.
    /// </summary>
    public static double[] ConstrainedLeastSquares(
        int order, ReadOnlySpan<double> edges, ReadOnlySpan<double> magnitudes,
        ReadOnlySpan<double> upper, ReadOnlySpan<double> lower, out bool converged)
    {
        int bands = magnitudes.Length;
        if (upper.Length != bands || lower.Length != bands)
        {
            throw new ArgumentException("fircls needs one upper and one lower bound per band.", nameof(upper));
        }

        if (edges.Length != bands + 1)
        {
            throw new ArgumentException("fircls needs one more band edge than it has bands.", nameof(edges));
        }

        if (edges[0] != 0 || edges[^1] != 1)
        {
            throw new ArgumentException("fircls's band edges must run from 0 to 1.", nameof(edges));
        }

        for (int i = 1; i < edges.Length; i++)
        {
            if (edges[i] <= edges[i - 1])
            {
                throw new ArgumentException("fircls's band edges must increase.", nameof(edges));
            }
        }

        for (int i = 0; i < bands; i++)
        {
            if (upper[i] <= lower[i] || lower[i] > magnitudes[i] || upper[i] < magnitudes[i])
            {
                throw new ArgumentException(
                    "fircls needs each band's bounds to straddle its amplitude.", nameof(upper));
            }
        }

        order = FirWindowDesign.CheckOrder(order, edges[^1], magnitudes, exception: false, out _);
        int n = order + 1;
        int grid = 1 << (int)System.Math.Ceiling(System.Math.Log2(3.0 * n));
        bool odd = n % 2 == 1;
        int m = odd ? (n - 1) / 2 : n / 2;
        double r = System.Math.Sqrt(2);

        var be = new double[edges.Length];
        for (int i = 0; i < be.Length; i++)
        {
            be[i] = edges[i] * System.Math.PI;
        }

        var v = new double[odd ? m + 1 : m];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = odd ? i : i + 0.5;
        }

        double tt = odd ? 1 - (1 / r) : 0;

        // The unconstrained least-squares answer, which is the Fourier series of the desired
        // response and is where the search starts.
        var c = new double[v.Length];
        var upperGrid = new List<double>();
        var lowerGrid = new List<double>();
        for (int k = 0; k < bands; k++)
        {
            for (int i = 0; i < v.Length; i++)
            {
                c[i] += magnitudes[k] * (BandIntegral(be[k + 1], v[i], odd, r) - BandIntegral(be[k], v[i], odd, r));
            }

            int q = (int)System.Math.Round(grid * (be[k + 1] - be[k]) / System.Math.PI, MidpointRounding.AwayFromZero);
            for (int i = 0; i < q; i++)
            {
                upperGrid.Add(upper[k]);
                lowerGrid.Add(lower[k]);
            }
        }

        while (upperGrid.Count < grid + 1)
        {
            upperGrid.Add(upper[bands - 1]);
            lowerGrid.Add(lower[bands - 1]);
        }

        double[] u = [.. upperGrid.GetRange(0, grid + 1)];
        double[] l = [.. lowerGrid.GetRange(0, grid + 1)];

        double[] a = (double[])c.Clone();
        return MultibandLoop(a, c, u, l, v, tt, m, odd, grid, r, out converged);
    }

    /// <summary>The Fourier coefficient of a brick wall up to <paramref name="omega"/>.</summary>
    private static double BandIntegral(double omega, double v, bool odd, double r)
    {
        if (odd)
        {
            return v == 0
                ? 2 * omega / r / System.Math.PI
                : 2 * System.Math.Sin(omega * v) / v / System.Math.PI;
        }

        return 4 * System.Math.Sin(omega * v) / (2 * v) / System.Math.PI;
    }

    /// <summary>
    /// <c>fircls1</c>: the constrained least-squares lowpass or highpass, whose bounds are one
    /// passband ripple and one stopband ripple rather than a curve per band.
    /// </summary>
    public static double[] ConstrainedLowpass(
        int order, double cutoff, double passRipple, double stopRipple, bool highpass,
        double? transitionWeight, (double Pass, double Stop, double Weight)? weighting, out bool converged)
    {
        if (cutoff <= 0 || cutoff >= 1)
        {
            throw new ArgumentException("fircls1's cutoff must lie strictly between 0 and 1.", nameof(cutoff));
        }

        double[] up = highpass ? [stopRipple, 1 + passRipple] : [1 + passRipple, stopRipple];
        double[] lo = highpass ? [-stopRipple, 1 - passRipple] : [1 - passRipple, -stopRipple];

        order = FirWindowDesign.CheckOrder(order, 1, [highpass ? 1 : 0], exception: false, out _);
        int n = order + 1;
        bool odd = n % 2 == 1;
        double wo = cutoff * System.Math.PI;
        int grid = 1 << (int)System.Math.Ceiling(System.Math.Log2(5.0 * n));
        double r = System.Math.Sqrt(2);

        int q = (int)System.Math.Round(wo * grid / System.Math.PI, MidpointRounding.AwayFromZero);
        var u = new double[grid + 1];
        var l = new double[grid + 1];
        for (int i = 0; i <= grid; i++)
        {
            u[i] = i < q ? up[0] : up[1];
            l[i] = i < q ? lo[0] : lo[1];
        }

        int m = odd ? (n - 1) / 2 : n / 2;
        var v = new double[odd ? m + 1 : m];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = odd ? i : i + 0.5;
        }

        double tt = odd ? 1 - (1 / r) : 0;
        double reference = weighting is null ? wo : weighting.Value.Pass * System.Math.PI;

        var c = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            c[i] = odd
                ? (v[i] == 0 ? 2 * reference / r / System.Math.PI : 2 * System.Math.Sin(reference * v[i]) / v[i] / System.Math.PI)
                : 2 * System.Math.Sin(reference * v[i]) / (v[i] * System.Math.PI);
        }

        if (highpass && odd)
        {
            for (int i = 0; i < c.Length; i++)
            {
                c[i] = -c[i];
            }

            c[0] += r;
        }

        double[,]? weight = null;
        if (weighting is not null)
        {
            weight = WeightMatrix(m, odd, r, reference, weighting.Value.Stop * System.Math.PI, weighting.Value.Weight, highpass);
        }

        double[] a = weight is null ? (double[])c.Clone() : Apply(weight, c);
        return LowpassLoop(
            a, c, u, l, v, tt, m, odd, grid, r, weight, transitionWeight, wo, highpass, up, lo, out converged);
    }

    /// <summary>The inverse of the weighted inner-product matrix a two-band weighting asks for.</summary>
    private static double[,] WeightMatrix(int m, bool odd, double r, double wp, double ws, double k, bool highpass)
    {
        int size = odd ? m + 1 : m;
        double[] tp;
        double[] hk;

        if (odd)
        {
            tp = new double[m + 1];
            hk = new double[m + 1];
            tp[0] = highpass
                ? (System.Math.PI - wp + (k * ws)) / System.Math.PI
                : (wp + (k * (System.Math.PI - ws))) / System.Math.PI;
            for (int i = 1; i <= m; i++)
            {
                tp[i] = Term(i, wp, ws, k, highpass);
            }

            for (int i = 0; i <= m; i++)
            {
                hk[i] = Term(m + i, wp, ws, k, highpass);
            }
        }
        else
        {
            tp = new double[m];
            var tp2 = new double[m];
            hk = new double[m + 1];
            tp[0] = (wp + (k * (System.Math.PI - ws))) / System.Math.PI;
            for (int i = 1; i < m; i++)
            {
                tp[i] = Term(i, wp, ws, k, false);
            }

            for (int i = 1; i <= m; i++)
            {
                tp2[i - 1] = Term(i, wp, ws, k, false);
            }

            for (int i = 0; i < m; i++)
            {
                hk[i] = Term(m + i, wp, ws, k, false);
            }

            var even = new double[m, m];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    double toeplitz = i >= j ? tp[i - j] : tp[j - i];
                    double hankelValue = i + j < m ? tp2[i + j] : hk[i + j - m + 1];
                    even[i, j] = toeplitz + hankelValue;
                }
            }

            return Linear.Solve(even, Linear.Identity(m));
        }

        var matrix = new double[size, size];
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                double toeplitz = i >= j ? tp[i - j] : tp[j - i];
                double hankelValue = i + j < size ? tp[i + j] : hk[i + j - size + 1];
                matrix[i, j] = toeplitz + hankelValue;
            }
        }

        for (int j = 0; j < size; j++)
        {
            matrix[0, j] /= r;
        }

        for (int i = 0; i < size; i++)
        {
            matrix[i, 0] /= r;
        }

        return Linear.Solve(matrix, Linear.Identity(size));
    }

    private static double Term(int i, double wp, double ws, double k, bool highpass) =>
        i == 0
            ? 0
            : (highpass
                ? (-System.Math.Sin(i * wp) + (k * System.Math.Sin(i * ws))) / i
                : (System.Math.Sin(i * wp) - (k * System.Math.Sin(i * ws))) / i) / System.Math.PI;

    /// <summary>
    /// <c>fircls</c>'s active-set loop. Each pass finds the response's extremes, keeps the ones
    /// outside the bounds, and solves for the multipliers that pull them back. The extra
    /// bookkeeping is the re-entry: when the constraints dropped on the previous pass are violated
    /// by the new answer, the worst of them goes back in before anything else happens, and that is
    /// what makes the loop terminate rather than cycle between two active sets.
    /// </summary>
    private static double[] MultibandLoop(
        double[] a, double[] c, double[] u, double[] l, double[] v, double tt, int m, bool odd,
        int grid, double r, out bool converged)
    {
        int size = v.Length;
        List<int> kmax = [];
        List<int> kmin = [];
        List<double> cmax = [];
        List<double> cmin = [];
        List<int> okmax = [];
        List<int> okmin = [];
        List<double> ocmax = [];
        List<double> ocmin = [];
        double uvo = 0;
        double lvo = 0;
        int k1 = 0;
        int k2 = 0;
        converged = false;

        for (int it = 0; it < MaxIterations; it++)
        {
            if (uvo < -Small / 10 || lvo < -Small / 10)
            {
                if (uvo < lvo)
                {
                    kmax.Add(okmax[k1]);
                    cmax.Add(ocmax[k1]);
                    okmax.RemoveAt(k1);
                    ocmax.RemoveAt(k1);
                }
                else
                {
                    kmin.Add(okmin[k2]);
                    cmin.Add(ocmin[k2]);
                    okmin.RemoveAt(k2);
                    ocmin.RemoveAt(k2);
                }
            }
            else
            {
                okmax = [.. kmax];
                okmin = [.. kmin];
                ocmax = [.. cmax];
                ocmin = [.. cmin];

                double[] response = Amplitude(a, m, odd, grid, r);
                kmax = LocalMaxima(response);
                kmin = LocalMaxima(Negated(response));
                if (!odd)
                {
                    DropEnd(kmax, grid);
                    DropEnd(kmin, grid);
                }

                cmax = Refine(a, v, kmax, grid);
                cmin = Refine(a, v, kmin, grid);
                double[] amax = Evaluate(a, v, cmax, tt);
                double[] amin = Evaluate(a, v, cmin, tt);

                Select(kmax, cmax, ref amax, i => amax[i] > u[kmax[i]] - Small);
                Select(kmin, cmin, ref amin, i => amin[i] < l[kmin[i]] + Small);

                double error = 0;
                for (int i = 0; i < kmax.Count; i++)
                {
                    error = System.Math.Max(error, amax[i] - u[kmax[i]]);
                }

                for (int i = 0; i < kmin.Count; i++)
                {
                    error = System.Math.Max(error, l[kmin[i]] - amin[i]);
                }

                if (error < Small)
                {
                    converged = true;
                    break;
                }
            }

            if (!ActiveSetStep(a, c, u, l, kmax, kmin, cmax, cmin, size, odd, r, null))
            {
                break;
            }

            uvo = 0;
            if (ocmax.Count > 0)
            {
                double[] old = Evaluate(a, v, ocmax, tt);
                uvo = double.PositiveInfinity;
                for (int i = 0; i < old.Length; i++)
                {
                    double value = u[okmax[i]] - old[i];
                    if (value < uvo)
                    {
                        uvo = value;
                        k1 = i;
                    }
                }
            }

            lvo = 0;
            if (ocmin.Count > 0)
            {
                double[] old = Evaluate(a, v, ocmin, tt);
                lvo = double.PositiveInfinity;
                for (int i = 0; i < old.Length; i++)
                {
                    double value = old[i] - l[okmin[i]];
                    if (value < lvo)
                    {
                        lvo = value;
                        k2 = i;
                    }
                }
            }
        }

        return Assemble(a, m, odd, r);
    }

    /// <summary>
    /// <c>fircls1</c>'s active-set loop. It has no re-entry rule; instead, when more constraints
    /// are active than there are coefficients to satisfy them, it drops the one at whichever end of
    /// the band the response is further from violating — which is decided from the slope of the
    /// response at zero and at Nyquist rather than from the response itself.
    /// </summary>
    private static double[] LowpassLoop(
        double[] a, double[] c, double[] u, double[] l, double[] v, double tt, int m, bool odd,
        int grid, double r, double[,]? weight, double? transition, double cutoff, bool highpass,
        double[] up, double[] lo, out bool converged)
    {
        int size = v.Length;
        converged = false;

        for (int it = 0; it < MaxIterations; it++)
        {
            double[] response = Amplitude(a, m, odd, grid, r);
            List<int> kmax = LocalMaxima(response);
            List<int> kmin = LocalMaxima(Negated(response));
            if (!odd)
            {
                DropEnd(kmax, grid);
                DropEnd(kmin, grid);
            }

            List<double> cmax = Refine(a, v, kmax, grid);
            List<double> cmin = Refine(a, v, kmin, grid);

            if (transition is double wt)
            {
                double omega = wt * System.Math.PI;
                bool passEdge = highpass ? omega > cutoff : omega < cutoff;
                if (passEdge)
                {
                    kmin.Add(highpass ? grid : 0);
                    cmin.Add(omega);
                }
                else
                {
                    kmax.Add(highpass ? 0 : grid - 1);
                    cmax.Add(omega);
                }
            }

            double[] amax = Evaluate(a, v, cmax, tt);
            double[] amin = Evaluate(a, v, cmin, tt);
            Select(kmax, cmax, ref amax, i => amax[i] > u[kmax[i]] - (100 * Small));
            Select(kmin, cmin, ref amin, i => amin[i] < l[kmin[i]] + (100 * Small));

            if (kmax.Count + kmin.Count > size)
            {
                DropOne(a, m, odd, r, up, lo, kmax, kmin, cmax, cmin, ref amax, ref amin);
            }

            double error = 0;
            for (int i = 0; i < kmax.Count; i++)
            {
                error = System.Math.Max(error, amax[i] - u[kmax[i]]);
            }

            for (int i = 0; i < kmin.Count; i++)
            {
                error = System.Math.Max(error, l[kmin[i]] - amin[i]);
            }

            if (error < Small)
            {
                converged = true;
                break;
            }

            if (!ActiveSetStep(a, c, u, l, kmax, kmin, cmax, cmin, size, odd, r, weight))
            {
                break;
            }
        }

        return Assemble(a, m, odd, r);
    }

    /// <summary>
    /// Drops the constraint at the end of the band the response is furthest from violating, which is
    /// read off the slope of the cosine series at zero and at Nyquist.
    /// </summary>
    private static void DropOne(
        double[] a, int m, bool odd, double r, double[] up, double[] lo,
        List<int> kmax, List<int> kmin, List<double> cmax, List<double> cmin,
        ref double[] amax, ref double[] amin)
    {
        double e0 = 0;
        double epi = 1;
        if (odd)
        {
            double h0 = a[0] / r;
            double hpi = a[0] / r;
            double slope0 = 0;
            double slopePi = 0;
            for (int i = 1; i <= m; i++)
            {
                h0 += a[i];
                hpi += i % 2 == 0 ? a[i] : -a[i];
                slope0 -= a[i] * i * i;
                slopePi += i % 2 == 1 ? a[i] * i * i : -(a[i] * i * i);
            }

            e0 = slope0 > 0 ? lo[0] - h0 : h0 - up[0];
            epi = slopePi > 0 ? lo[1] - hpi : hpi - up[1];
        }

        bool fromTop = e0 > epi;
        int atMin = Extreme(kmin, fromTop);
        int atMax = Extreme(kmax, fromTop);
        bool dropMax;
        if (fromTop)
        {
            dropMax = atMin < 0 || (atMax >= 0 && kmin[atMin] < kmax[atMax]);
        }
        else
        {
            dropMax = atMin >= 0 && atMax >= 0 && kmin[atMin] >= kmax[atMax];
        }

        if (dropMax && atMax >= 0)
        {
            kmax.RemoveAt(atMax);
            cmax.RemoveAt(atMax);
            amax = Without(amax, atMax);
        }
        else if (atMin >= 0)
        {
            kmin.RemoveAt(atMin);
            cmin.RemoveAt(atMin);
            amin = Without(amin, atMin);
        }
    }

    private static int Extreme(List<int> indices, bool largest)
    {
        if (indices.Count == 0)
        {
            return -1;
        }

        int at = 0;
        for (int i = 1; i < indices.Count; i++)
        {
            if (largest ? indices[i] > indices[at] : indices[i] < indices[at])
            {
                at = i;
            }
        }

        return at;
    }

    private static double[] Without(double[] values, int index)
    {
        var result = new double[values.Length - 1];
        Array.Copy(values, result, index);
        Array.Copy(values, index + 1, result, index, values.Length - index - 1);
        return result;
    }

    private static void DropEnd(List<int> indices, int grid)
    {
        if (indices.Count > 0 && indices[^1] == grid)
        {
            indices.RemoveAt(indices.Count - 1);
        }
    }

    private static void Select(List<int> indices, List<double> frequencies, ref double[] values, Func<int, bool> keep)
    {
        var keptIndices = new List<int>();
        var keptFrequencies = new List<double>();
        var kept = new List<double>();
        for (int i = 0; i < indices.Count; i++)
        {
            if (keep(i))
            {
                keptIndices.Add(indices[i]);
                keptFrequencies.Add(frequencies[i]);
                kept.Add(values[i]);
            }
        }

        indices.Clear();
        indices.AddRange(keptIndices);
        frequencies.Clear();
        frequencies.AddRange(keptFrequencies);
        values = [.. kept];
    }

    /// <summary>
    /// One equality-constrained solve, with any negative multiplier's constraint dropped and the
    /// solve repeated. Answers false when nothing is left to constrain.
    /// </summary>
    private static bool ActiveSetStep(
        double[] a, double[] c, double[] u, double[] l,
        List<int> kmax, List<int> kmin, List<double> cmax, List<double> cmin,
        int size, bool odd, double r, double[,]? weight)
    {
        var rows = new List<double[]>();
        var d = new List<double>();
        foreach (double frequency in cmax)
        {
            rows.Add(Row(frequency, size, odd, r, 1));
        }

        foreach (double frequency in cmin)
        {
            rows.Add(Row(frequency, size, odd, r, -1));
        }

        for (int i = 0; i < kmax.Count; i++)
        {
            d.Add(u[kmax[i]]);
        }

        for (int i = 0; i < kmin.Count; i++)
        {
            d.Add(-l[kmin[i]]);
        }

        int maxCount = kmax.Count;
        while (rows.Count > 0)
        {
            double[] mu = Multipliers(rows, d, c, weight);
            int worst = 0;
            for (int i = 1; i < mu.Length; i++)
            {
                if (mu[i] < mu[worst])
                {
                    worst = i;
                }
            }

            if (mu[worst] >= 0)
            {
                var next = new double[size];
                for (int j = 0; j < size; j++)
                {
                    double correction = 0;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        correction += rows[i][j] * mu[i];
                    }

                    next[j] = c[j] - correction;
                }

                double[] updated = weight is null ? next : Apply(weight, next);
                Array.Copy(updated, a, size);
                return true;
            }

            rows.RemoveAt(worst);
            d.RemoveAt(worst);
            if (worst >= maxCount)
            {
                int at = worst - maxCount;
                if (at < kmin.Count)
                {
                    kmin.RemoveAt(at);
                    cmin.RemoveAt(at);
                }
            }
            else
            {
                kmax.RemoveAt(worst);
                cmax.RemoveAt(worst);
                maxCount--;
            }
        }

        return false;
    }

    /// <summary>The cosine coefficients written back out as an impulse response.</summary>
    private static double[] Assemble(double[] a, int m, bool odd, double r)
    {
        var h = new double[odd ? (2 * m) + 1 : 2 * m];
        if (odd)
        {
            for (int i = 0; i < m; i++)
            {
                h[i] = a[m - i] / 2;
            }

            h[m] = a[0] / r;
            for (int i = 1; i <= m; i++)
            {
                h[m + i] = a[i] / 2;
            }
        }
        else
        {
            for (int i = 0; i < m; i++)
            {
                h[i] = a[m - 1 - i] / 2;
                h[m + i] = a[i] / 2;
            }
        }

        return h;
    }

    /// <summary>The Lagrange multipliers of the current active set.</summary>
    private static double[] Multipliers(List<double[]> rows, List<double> d, double[] c, double[,]? weight)
    {
        int n = rows.Count;
        int size = c.Length;
        var normal = new double[n, n];
        var rhs = new double[n, 1];

        for (int i = 0; i < n; i++)
        {
            double[] weighted = weight is null ? rows[i] : Apply(weight, rows[i]);
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < size; k++)
                {
                    sum += weighted[k] * rows[j][k];
                }

                normal[i, j] = sum;
            }

            double product = 0;
            double[] target = weight is null ? c : Apply(weight, c);
            for (int k = 0; k < size; k++)
            {
                product += rows[i][k] * target[k];
            }

            rhs[i, 0] = product - d[i];
        }

        double[,] solved = Linear.Solve(normal, rhs);
        var mu = new double[n];
        for (int i = 0; i < n; i++)
        {
            mu[i] = solved[i, 0];
        }

        return mu;
    }

    private static double[] Row(double frequency, int size, bool odd, double r, double sign)
    {
        var row = new double[size];
        for (int j = 0; j < size; j++)
        {
            double v = odd ? j : j + 0.5;
            row[j] = sign * System.Math.Cos(frequency * v);
        }

        if (odd)
        {
            row[0] /= r;
        }

        return row;
    }

    /// <summary>The amplitude response on the whole grid, by one transform of the cosine series.</summary>
    private static double[] Amplitude(double[] a, int m, bool odd, int grid, double r)
    {
        Complex[] spectrum;
        if (odd)
        {
            var buffer = new double[2 * grid];
            buffer[0] = a[0] * r;
            for (int i = 1; i <= m; i++)
            {
                buffer[i] = a[i];
                buffer[(2 * grid) - i] = a[i];
            }

            spectrum = Fft.Forward(buffer);
        }
        else
        {
            var buffer = new double[4 * grid];
            for (int i = 1; i <= m; i++)
            {
                buffer[(2 * i) - 1] = a[i - 1];
                buffer[(4 * grid) - (2 * i) + 1] = a[i - 1];
            }

            spectrum = Fft.Forward(buffer);
        }

        var response = new double[grid + 1];
        for (int i = 0; i <= grid; i++)
        {
            response[i] = spectrum[i].Real / 2;
        }

        return response;
    }

    /// <summary>The response at a set of frequencies, from the cosine series directly.</summary>
    private static double[] Evaluate(double[] a, double[] v, List<double> frequencies, double tt)
    {
        var result = new double[frequencies.Count];
        for (int i = 0; i < frequencies.Count; i++)
        {
            double sum = 0;
            for (int j = 0; j < v.Length; j++)
            {
                sum += System.Math.Cos(frequencies[i] * v[j]) * a[j];
            }

            result[i] = sum - (tt * a[0]);
        }

        return result;
    }

    /// <summary>
    /// Five Newton steps on the response's derivative, which moves each grid maximum onto the real
    /// one — the grid is fine but not fine enough for a constraint to be placed on it.
    /// </summary>
    private static List<double> Refine(double[] a, double[] v, List<int> indices, int grid)
    {
        var w = new List<double>(indices.Count);
        foreach (int index in indices)
        {
            w.Add(index * System.Math.PI / grid);
        }

        for (int step = 0; step < 5; step++)
        {
            for (int i = 0; i < w.Count; i++)
            {
                double first = 0;
                double second = 0;
                for (int j = 0; j < v.Length; j++)
                {
                    first -= System.Math.Sin(w[i] * v[j]) * v[j] * a[j];
                    second -= System.Math.Cos(w[i] * v[j]) * v[j] * v[j] * a[j];
                }

                if (second != 0)
                {
                    w[i] -= first / second;
                }
            }
        }

        return w;
    }

    /// <summary>The indices of a sequence's local maxima, endpoints included when they rise to them.</summary>
    private static List<int> LocalMaxima(double[] x)
    {
        var k = new List<int>();
        int n = x.Length;
        for (int i = 1; i < n - 1; i++)
        {
            if (x[i - 1] <= x[i] && x[i] > x[i + 1])
            {
                k.Add(i);
            }
        }

        if (n > 1 && x[0] > x[1])
        {
            k.Add(0);
        }

        if (n > 1 && x[n - 1] > x[n - 2])
        {
            k.Add(n - 1);
        }

        k.Sort();
        return k;
    }

    private static double[] Negated(double[] x)
    {
        var result = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            result[i] = -x[i];
        }

        return result;
    }

    private static void Keep(
        List<int> indices, List<double> frequencies, double[] values, bool[] mask,
        out List<int> keptIndices, out List<double> keptFrequencies, out double[] keptValues)
    {
        keptIndices = [];
        keptFrequencies = [];
        var kept = new List<double>();
        for (int i = 0; i < indices.Count; i++)
        {
            if (mask[i])
            {
                keptIndices.Add(indices[i]);
                keptFrequencies.Add(frequencies[i]);
                kept.Add(values[i]);
            }
        }

        keptValues = [.. kept];
    }

    private static double[] Apply(double[,] matrix, double[] x)
    {
        int n = x.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < n; j++)
            {
                sum += matrix[i, j] * x[j];
            }

            result[i] = sum;
        }

        return result;
    }

    // --- The maximally flat design ---------------------------------------------------------------

    /// <summary>What <c>maxflat</c> answers: two polynomials and the two halves of the numerator.</summary>
    public readonly record struct MaximallyFlat(
        double[] B, double[] A, double[] EdgeZeros, double[] PassbandZeros, double[] Sections, int Rows, double Gain);

    /// <summary>
    /// <c>maxflat</c>: the filter of the given zero and pole counts whose response is as flat as it
    /// can be at both ends, with its half-power point at <paramref name="cutoff"/>.
    /// </summary>
    public static MaximallyFlat FlatDesign(int zeros, int poles, double cutoff, bool symmetric)
    {
        if (zeros < 1 || poles < 0)
        {
            throw new ArgumentException("maxflat needs a positive zero count.", nameof(zeros));
        }

        if (cutoff <= 0 || cutoff >= 1)
        {
            throw new ArgumentException("maxflat's cutoff must lie strictly between 0 and 1.", nameof(cutoff));
        }

        int z = zeros;
        if (symmetric)
        {
            poles = 0;
            if (z % 2 == 1)
            {
                throw new ArgumentException("A symmetric maxflat design needs an even zero count.", nameof(zeros));
            }

            if (z >= 140)
            {
                throw new ArgumentException("A symmetric maxflat design is limited to fewer than 140 zeros.", nameof(zeros));
            }

            z /= 2;
        }

        double flat = symmetric ? System.Math.Sqrt(0.5) : 0.5;
        double[,] table = SpecificationTable(z, poles, flat);
        double wo = cutoff * System.Math.PI;

        int chosen = -1;
        for (int i = 0; i < table.GetLength(0); i++)
        {
            if (table[i, 3] < (wo / System.Math.PI) + 1e-7 && table[i, 4] > (wo / System.Math.PI) - 1e-7)
            {
                chosen = i;
            }
        }

        if (chosen < 0)
        {
            throw new ArgumentException("maxflat cannot place that cutoff with those zero and pole counts.", nameof(cutoff));
        }

        int l = (int)table[chosen, 0];
        int mm = (int)table[chosen, 1];
        int nn = (int)table[chosen, 2];
        double xo = (1 - System.Math.Cos(wo)) / 2;

        double[] s;
        double[] q;
        double c1;
        if (nn == 0)
        {
            double[] rr = Shifted(Coefficients(mm, k => Choose(mm - k - 1, 0) * Choose(l + k - 1, k)), leading: true);
            double[] tt = Shifted(Coefficients(mm, k => Choose(mm - k - 2, -1) * Choose(l + k, k)), leading: false);
            c1 = (flat / System.Math.Pow(1 - xo, l)) - Horner(rr, xo);
            double c2 = Horner(tt, xo);
            s = Combine(c2, rr, c1, tt);
            q = [1];
        }
        else if (mm == 0)
        {
            int top = System.Math.Min(l, nn);
            var raw = new double[top + 1];
            for (int i = 0; i <= top; i++)
            {
                int k = top - i;
                raw[i] = Choose(l, k) * System.Math.Pow(-1, k);
            }

            if (l < nn)
            {
                var padded = new double[nn + 1];
                Array.Copy(raw, 0, padded, nn - l, raw.Length);
                raw = padded;
            }

            c1 = ((System.Math.Pow(1 - xo, l) / flat) - Horner(raw, xo)) / System.Math.Pow(xo, nn);
            raw[0] += c1;
            q = raw;
            s = [1];
        }
        else
        {
            double[] rr = Shifted(Coefficients(mm, k => Choose(mm + nn - k - 1, nn) * Choose(l - nn + k - 1, k)), leading: true);
            double[] tt = Shifted(Coefficients(mm, k => Choose(mm + nn - k - 2, nn - 1) * Choose(l - nn + k, k)), leading: false);
            var agr = new double[nn + 1];
            var agt = new double[nn + 1];
            for (int i = 0; i <= nn; i++)
            {
                int k = nn - i;
                agr[i] = Choose(mm + nn - k - 1, mm - 1) * Choose(mm + l - 1, k) * System.Math.Pow(-1, k);
                agt[i] = Choose(mm + nn - k - 1, mm - 1) * Choose(mm + l - 1, k - 1) * System.Math.Pow(-1, k - 1);
            }

            c1 = (System.Math.Pow(1 - xo, l) * Horner(rr, xo)) - (flat * Horner(agr, xo));
            double c2 = (flat * Horner(agt, xo)) - (System.Math.Pow(1 - xo, l) * Horner(tt, xo));
            s = Combine(c2, rr, c1, tt);
            q = Combine(c2, agr, c1, agt);
        }

        Complex[] numeratorRoots = MapRoots(s, mm, symmetric, c1 != 0);
        double[] b2 = FilterCoefficients.RealPolynomial(numeratorRoots);
        Complex[] denominatorRoots = MapRoots(q, nn, symmetric, keepAll: true);
        double[] a = FilterCoefficients.RealPolynomial(denominatorRoots);

        if (symmetric)
        {
            l *= 2;
        }

        double[] b1 = [1];
        for (int t = 0; t < l; t++)
        {
            b1 = FilterCoefficients.Convolve(b1, [1, 1]);
        }

        double sumA = Sum(a);
        double sumB1 = Sum(b1);
        double sumB2 = Sum(b2);
        if (System.Math.Abs(sumB2) < 100 * 2.220446049250313e-16)
        {
            throw new ArgumentException("maxflat cannot design that filter; its numerator vanishes.", nameof(cutoff));
        }

        var scaled = new double[b2.Length];
        for (int i = 0; i < b2.Length; i++)
        {
            scaled[i] = b2[i] * sumA / (sumB1 * sumB2);
        }

        double[] b = FilterCoefficients.Convolve(b1, scaled);
        (double[] sections, int rows) = BuildSections(numeratorRoots, denominatorRoots, l);
        return new MaximallyFlat(b, a, b1, scaled, sections, rows, sumA / sumB2);
    }

    /// <summary>
    /// The cascade form <c>maxflat</c> answers: the numerator's roots against poles at the origin,
    /// the denominator's poles against zeros at the origin, and one half-band section per factor of
    /// <c>(1 + z⁻¹)</c> — which is the one section a root finder cannot be asked for, because those
    /// zeros are all at the same place.
    /// </summary>
    private static (double[] Sections, int Rows) BuildSections(Complex[] zeros, Complex[] poles, int edgeZeros)
    {
        var numerator = new List<double[]>();
        if (zeros.Length > 0)
        {
            SecondOrderSections.Cascade top = SecondOrderSections.FromRoots(zeros, new Complex[zeros.Length], 1);
            for (int i = 0; i < top.Rows; i++)
            {
                numerator.Add(RowOf(top, i));
            }
        }

        for (int i = 0; i < edgeZeros / 2; i++)
        {
            numerator.Add([0.25, 0.5, 0.25, 0, 0, 0]);
        }

        if (edgeZeros % 2 == 1)
        {
            numerator.Add([0.5, 0.5, 0, 0, 0, 0]);
        }

        var denominator = new List<double[]>();
        if (poles.Length > 0)
        {
            SecondOrderSections.Cascade bottom = SecondOrderSections.FromRoots(new Complex[poles.Length], poles, 1);
            for (int i = 0; i < bottom.Rows; i++)
            {
                denominator.Add(RowOf(bottom, i));
            }
        }
        else
        {
            for (int i = 0; i < numerator.Count; i++)
            {
                denominator.Add([1, 0, 0, 1, 0, 0]);
            }
        }

        while (numerator.Count < denominator.Count)
        {
            numerator.Add([1, 0, 0, 1, 0, 0]);
        }

        while (denominator.Count < numerator.Count)
        {
            denominator.Add([1, 0, 0, 1, 0, 0]);
        }

        int rows = numerator.Count;
        var sos = new double[rows * 6];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                sos[(j * rows) + i] = numerator[i][j];
                sos[((j + 3) * rows) + i] = denominator[i][j + 3];
            }
        }

        return (sos, rows);
    }

    private static double[] RowOf(SecondOrderSections.Cascade cascade, int row)
    {
        var section = new double[6];
        for (int j = 0; j < 6; j++)
        {
            section[j] = cascade.Sections[(j * cascade.Rows) + row];
        }

        return section;
    }

    /// <summary>The roots of the design polynomial, mapped back from the flatness variable to z.</summary>
    private static Complex[] MapRoots(double[] polynomial, int count, bool symmetric, bool keepAll)
    {
        Complex[] roots = FrequencyTransforms.Roots(ToComplex(polynomial));
        var mapped = new List<Complex>(2 * roots.Length);
        foreach (Complex root in roots)
        {
            Complex t = 1 - (2 * root);
            Complex offset = Complex.Sqrt((t * t) - 1);
            mapped.Add(t + offset);
            mapped.Add(t - offset);
        }

        // Sorting by magnitude and keeping the first half is what picks the roots inside the unit
        // circle, which is the only choice that is both stable and minimum phase.
        mapped.Sort((x, y) =>
        {
            int byMagnitude = Complex.Abs(x).CompareTo(Complex.Abs(y));
            return byMagnitude != 0 ? byMagnitude : x.Phase.CompareTo(y.Phase);
        });

        if (symmetric)
        {
            return [.. mapped];
        }

        if (keepAll)
        {
            return [.. mapped.GetRange(0, System.Math.Min(count, mapped.Count))];
        }

        var kept = new List<Complex>();
        int take = count;
        if (mapped.Count == 0)
        {
            return [];
        }

        kept.AddRange(mapped.GetRange(0, System.Math.Min(take, mapped.Count)));
        return [.. kept];
    }

    /// <summary>
    /// The table of admissible zero splits: for each way of dividing the zeros between the band edge
    /// and the passband, the range of cutoffs that split can hit.
    /// </summary>
    private static double[,] SpecificationTable(int z, int p, double flat)
    {
        if (z <= p)
        {
            return new double[,] { { z, 0, p, 0, 1 } };
        }

        var rows = new List<double[]>();
        int l = z;
        int n = p;

        if (n > 0)
        {
            double c = n % 2 == 0 ? 0 : Choose(l - 1, n);
            var y = new double[l + 1];
            for (int i = 0; i <= n; i++)
            {
                y[i] = Choose(l, i) * (1 - flat) * System.Math.Pow(-1, i);
            }

            for (int i = n + 1; i <= l; i++)
            {
                y[i] = Choose(l, i) * System.Math.Pow(-1, i);
            }

            y[n] -= c * flat;
            Array.Reverse(y);
            double r = NearestHalfRoot(y);
            rows.Add([z, 0, p, 0, System.Math.Acos(1 - (2 * r)) / System.Math.PI]);
        }

        if (z == 1 && p == 0)
        {
            double r = 1 - flat;
            rows.Clear();
            rows.Add([1, 0, 0, 0, System.Math.Acos(1 - (2 * r)) / System.Math.PI]);
        }

        for (int m = 1; m <= z - p - 1; m++)
        {
            l = z - m;
            var gr = new double[m + l + 1];
            var gt = new double[m + l + 1];
            for (int i = 0; i <= m + l; i++)
            {
                int k = m + l - i;
                gr[i] = Choose(m + n - k - 1, m - 1) * Choose(m + l - 1, k) * System.Math.Pow(-1, k);
                gt[i] = Choose(m + n - k - 1, m - 1) * Choose(m + l - 1, k - 1) * System.Math.Pow(-1, k - 1);
            }

            double[] agr = gr[(m + l - n)..];
            double[] agt = gt[(m + l - n)..];

            double c = n % 2 == 0 ? (double)(l - n) / (m + n) : (double)(l - n) / n;
            double[] y = Assemble(m + l - n, agr, agt, flat, c, gr, gt);
            double rMax = NearestHalfRoot(y);

            if (n % 2 == 0)
            {
                y = Assemble(m + l - n, agr, agt, flat, -1, gr, gt);
            }
            else
            {
                y = new double[m + l + 1];
                for (int i = 0; i < agt.Length; i++)
                {
                    y[m + l - n + i] = flat * agt[i];
                }

                for (int i = 0; i < y.Length; i++)
                {
                    y[i] -= gt[i];
                }
            }

            double rMin = NearestHalfRoot(y);
            rows.Add([l, m, n,
                System.Math.Acos(1 - (2 * rMin)) / System.Math.PI,
                System.Math.Acos(1 - (2 * rMax)) / System.Math.PI]);
        }

        if (n > 0)
        {
            int m = z - p;
            l = z - m;
            rows.Add([l, m, n, rows[z - p - 1][4], 1]);
        }

        var table = new double[rows.Count, 5];
        for (int i = 0; i < rows.Count; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                table[i, j] = rows[i][j];
            }
        }

        return table;
    }

    private static double[] Assemble(int offset, double[] agr, double[] agt, double flat, double c, double[] gr, double[] gt)
    {
        var y = new double[gr.Length];
        for (int i = 0; i < agr.Length; i++)
        {
            y[offset + i] = (flat * agr[i]) + (c * flat * agt[i]);
        }

        for (int i = 0; i < y.Length; i++)
        {
            y[i] -= gr[i] + (c * gt[i]);
        }

        return y;
    }

    /// <summary>The real root nearest a half, which is the one that means anything here.</summary>
    private static double NearestHalfRoot(double[] y)
    {
        Complex[] roots = FrequencyTransforms.Roots(ToComplex(y));
        double best = double.NaN;
        double distance = double.PositiveInfinity;
        foreach (Complex root in roots)
        {
            if (root.Imaginary != 0)
            {
                continue;
            }

            double d = System.Math.Abs(root.Real - 0.5);
            if (d < distance)
            {
                distance = d;
                best = root.Real;
            }
        }

        if (double.IsNaN(best))
        {
            throw new ArgumentException("maxflat cannot design that filter.", nameof(y));
        }

        return best;
    }

    private static double[] Coefficients(int m, Func<int, double> term)
    {
        var result = new double[m];
        for (int i = 0; i < m; i++)
        {
            result[i] = term(m - 1 - i);
        }

        return result;
    }

    private static double[] Shifted(double[] values, bool leading)
    {
        var result = new double[values.Length + 1];
        Array.Copy(values, 0, result, leading ? 1 : 0, values.Length);
        return result;
    }

    private static double[] Combine(double c2, double[] first, double c1, double[] second)
    {
        int n = System.Math.Max(first.Length, second.Length);
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = i >= n - first.Length ? first[i - (n - first.Length)] : 0;
            double b = i >= n - second.Length ? second[i - (n - second.Length)] : 0;
            result[i] = (c2 * a) + (c1 * b);
        }

        return result;
    }

    /// <summary>The generalised binomial coefficient, which is defined for negative arguments too.</summary>
    private static double Choose(int n, int k)
    {
        if (n >= 0)
        {
            return k >= 0 && n >= k ? System.Math.Round(Product(1, n) / (Product(1, k) * Product(1, n - k))) : 0;
        }

        if (k >= 0)
        {
            return System.Math.Pow(-1, k) * Product(1, k - n - 1) / (Product(1, k) * Product(1, -n - 1));
        }

        return n >= k
            ? System.Math.Pow(-1, n - k) * Product(1, -k - 1) / (Product(1, n - k) * Product(1, -n - 1))
            : 0;
    }

    private static double Product(int from, int to)
    {
        double result = 1;
        for (int i = from; i <= to; i++)
        {
            result *= i;
        }

        return result;
    }

    private static double Horner(double[] coefficients, double x)
    {
        double sum = 0;
        foreach (double c in coefficients)
        {
            sum = (sum * x) + c;
        }

        return sum;
    }

    private static double Sum(double[] values)
    {
        double total = 0;
        foreach (double v in values)
        {
            total += v;
        }

        return total;
    }

    private static Complex[] ToComplex(double[] values)
    {
        var result = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = values[i];
        }

        return result;
    }
}
