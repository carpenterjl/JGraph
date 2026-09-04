using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// The variable-order Adams–Bashforth–Moulton predictor–corrector behind MATLAB's <c>ode113</c>:
/// orders one to twelve, a step-size history carried as modified divided differences, and the
/// interpolant that reads the solution off those differences.
/// </summary>
/// <remarks>
/// <para>
/// The method is Shampine and Gordon's, as <c>ode113.m</c> and <c>ntrp113.m</c> run it, and its
/// recurrences are written over arrays indexed from one — the way the method's own literature and
/// the reference index them. The arrays here are one longer than they need to be and their first
/// slot is unused, so that every subscript below reads as the recurrence it is rather than as a
/// translation of one; that is the one place in this project where a zero slot is spent on purpose.
/// </para>
/// <para>
/// The error constants <c>gstar</c> are the reference's own four-figure values, not recomputed
/// ones: a fixture pins the step counts exact, and a constant good to six figures would take
/// different steps than one good to four.
/// </para>
/// </remarks>
public static class AdamsPece
{
    /// <summary>MATLAB's name for the solver.</summary>
    public const string Name = "ode113";

    private const int MaxOrder = 12;

    private static readonly double[] GStar =
    [
        0, 0.5000, 0.0833, 0.0417, 0.0264, 0.0188, 0.0143, 0.0114, 0.00936, 0.00789, 0.00679, 0.00592, 0.00524, 0.00468,
    ];

    /// <summary>Integrates <paramref name="derivative"/> over <paramref name="tspan"/>.</summary>
    public static OdeResult Run(OdeFunction derivative, IReadOnlyList<double> tspan, double[] y0, OdeOptions options)
    {
        OdeSetup setup = OdeSetup.Create(Name, derivative, tspan, y0, options);
        var result = new OdeResult { Solver = Name, Evaluations = setup.Evaluations };
        int n = setup.N;
        OdeFunction f = setup.Function;
        double rtol = setup.RelativeTolerance;
        double[] threshold = setup.Threshold;
        bool normControl = setup.NormControl;
        int[]? nonNegative = setup.NonNegative;
        double[]? nonNegativeThreshold = setup.NonNegativeThreshold;

        int refine = Math.Max(1, options.Refine ?? 1);
        var output = new OdeOutput(setup, options, refine, result);
        OdeEvents? events = options.Events is null ? null : new OdeEvents(options.Events, setup.T0, y0, result);

        double t0 = setup.T0;
        double tFinal = setup.TFinal;
        double direction = setup.Direction;
        double t = t0;
        var y = (double[])y0.Clone();
        double[] yp = setup.InitialSlope;

        var two = new double[15];
        for (int i = 1; i <= 13; i++)
        {
            two[i] = Math.Pow(2, i);
        }

        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double absH;
        if (setup.FirstStep is null)
        {
            absH = Math.Min(hMax, setup.SpanStep);
            double rh = (normControl
                ? NormEstimators.VectorNorm(yp) / Math.Max(setup.InitialNorm, threshold[0])
                : setup.WeightedInfinityNorm(yp, y)) / (0.25 * Math.Sqrt(rtol));
            if (absH * rh > 1)
            {
                absH = 1 / rh;
            }

            absH = Math.Max(absH, hMin);
        }
        else
        {
            absH = Math.Min(hMax, Math.Max(hMin, setup.FirstStep.Value));
        }

        // The method's state: phi holds the modified divided differences (column i for each
        // state variable), psi the step-size history, and the rest are the recurrence's
        // coefficients. k is the order; K the range 1..k.
        int k = 1;
        var phi = new double[15][];
        for (int i = 0; i <= 14; i++)
        {
            phi[i] = new double[n];
        }

        Array.Copy(yp, phi[1], n);
        var psi = new double[13];
        var alpha = new double[13];
        var beta = new double[13];
        var sig = new double[14];
        sig[1] = 1;
        var w = new double[13];
        var v = new double[13];
        var g = new double[14];
        g[1] = 1;
        g[2] = 0.5;
        double hLast = 0;
        int kLast = 0;
        bool phase1 = true;
        int ns = 0;
        var p = new double[n];
        var phikp1 = new double[n];
        var yLast = new double[n];
        double[][]? phiStart = null;
        double[]? psiStart = null;

        output.Begin(y);
        bool done = false;
        double lastTime = t;
        while (!done)
        {
            double tiny = OdeSetup.TinyStep(t);
            hMin = Math.Max(tiny, setup.SmallestStep);
            hMax = Math.Max(tiny, setup.LargestStep);
            absH = Math.Min(hMax, Math.Max(hMin, absH));
            double h = direction * absH;
            if (1.1 * absH >= Math.Abs(tFinal - t))
            {
                h = tFinal - t;
                absH = Math.Abs(h);
                done = true;
            }

            if (events is not null)
            {
                phiStart = CloneColumns(phi);
                psiStart = (double[])psi.Clone();
            }

            int failed = 0;
            double[] invwt = InverseWeights(y, threshold, normControl);
            double err;
            double erk = 0;
            double erkm1 = 0;
            double erkm2 = 0;
            int knew;
            double tLast;
            while (true)
            {
                if (h != hLast)
                {
                    ns = 0;
                }

                if (ns <= kLast)
                {
                    ns++;
                }

                if (k >= ns)
                {
                    beta[ns] = 1;
                    alpha[ns] = 1.0 / ns;
                    double temp1 = h * ns;
                    sig[ns + 1] = 1;
                    for (int i = ns + 1; i <= k; i++)
                    {
                        double temp2 = psi[i - 1];
                        psi[i - 1] = temp1;
                        temp1 = temp2 + h;
                        beta[i] = beta[i - 1] * psi[i - 1] / temp2;
                        alpha[i] = h / temp1;
                        sig[i + 1] = i * alpha[i] * sig[i];
                    }

                    psi[k] = temp1;

                    if (ns == 1)
                    {
                        for (int i = 1; i <= k; i++)
                        {
                            v[i] = 1.0 / (i * (i + 1.0));
                            w[i] = v[i];
                        }
                    }
                    else
                    {
                        if (k > kLast)
                        {
                            v[k] = 1.0 / (k * (k + 1.0));
                            for (int j = 1; j <= ns - 2; j++)
                            {
                                v[k - j] -= alpha[j + 1] * v[k - j + 1];
                            }
                        }

                        for (int iq = 1; iq <= k + 1 - ns; iq++)
                        {
                            v[iq] -= alpha[ns] * v[iq + 1];
                            w[iq] = v[iq];
                        }

                        g[ns + 1] = w[1];
                    }

                    for (int i = ns + 2; i <= k + 1; i++)
                    {
                        for (int iq = 1; iq <= k + 2 - i; iq++)
                        {
                            w[iq] -= alpha[i - 1] * w[iq + 1];
                        }

                        g[i] = w[1];
                    }
                }

                for (int i = ns + 1; i <= k; i++)
                {
                    Scale(phi[i], beta[i]);
                }

                Array.Copy(phi[k + 1], phi[k + 2], n);
                Array.Clear(phi[k + 1]);

                // The predictor, and the differences updated as it is summed.
                Array.Clear(p);
                for (int i = k; i >= 1; i--)
                {
                    for (int c = 0; c < n; c++)
                    {
                        p[c] += g[i] * phi[i][c];
                        phi[i][c] += phi[i + 1][c];
                    }
                }

                for (int c = 0; c < n; c++)
                {
                    p[c] = y[c] + (h * p[c]);
                }

                tLast = t;
                t = tLast + h;
                if (done)
                {
                    t = tFinal;
                }

                yp = f(t, p);
                result.Evaluations++;
                for (int c = 0; c < n; c++)
                {
                    phikp1[c] = yp[c] - phi[1][c];
                }

                double temp3 = WeightedNorm(phikp1, invwt, normControl);
                err = absH * (g[k] - g[k + 1]) * temp3;
                erk = absH * sig[k + 1] * GStar[k] * temp3;
                erkm1 = k >= 2 ? absH * sig[k] * GStar[k - 1] * WeightedNorm(Sum(phi[k], phikp1), invwt, normControl) : 0;
                erkm2 = k >= 3 ? absH * sig[k - 1] * GStar[k - 2] * WeightedNorm(Sum(phi[k - 1], phikp1), invwt, normControl) : 0;

                knew = k;
                if (k == 2 && erkm1 <= 0.5 * erk)
                {
                    knew = k - 1;
                }

                if (k > 2 && Math.Max(erkm1, erkm2) <= erk)
                {
                    knew = k - 1;
                }

                if (nonNegative is not null && err <= rtol && AnyNegative(y, nonNegative))
                {
                    double errorNonNegative;
                    if (normControl)
                    {
                        var shortfall = new double[nonNegative.Length];
                        for (int i = 0; i < nonNegative.Length; i++)
                        {
                            shortfall[i] = Math.Max(0, -y[nonNegative[i]]);
                        }

                        errorNonNegative = NormEstimators.VectorNorm(shortfall) * invwt[0];
                    }
                    else
                    {
                        errorNonNegative = 0;
                        for (int i = 0; i < nonNegative.Length; i++)
                        {
                            errorNonNegative = Math.Max(errorNonNegative,
                                Math.Max(0, -y[nonNegative[i]]) / nonNegativeThreshold![i]);
                        }
                    }

                    if (errorNonNegative > rtol)
                    {
                        err = errorNonNegative;
                    }
                }

                if (err <= rtol)
                {
                    break;
                }

                // A refused step: the differences are unwound, the history stepped back, and the
                // third refusal in a row drops to first order.
                result.Failed++;
                if (absH <= hMin)
                {
                    options.Warn?.Invoke(
                        $"Failure at t={tLast:E6}.  Unable to meet integration tolerances without reducing the step size below the smallest value allowed ({hMin:E6}) at time t.");
                    output.Finish();
                    result.FinalTime = tLast;
                    return result;
                }

                phase1 = false;
                t = tLast;
                for (int i = 1; i <= k; i++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        phi[i][c] = (phi[i][c] - phi[i + 1][c]) / beta[i];
                    }
                }

                for (int i = 2; i <= k; i++)
                {
                    psi[i - 1] = psi[i] - h;
                }

                failed++;
                double reduce = 0.5;
                if (failed == 3)
                {
                    knew = 1;
                }
                else if (failed > 3)
                {
                    reduce = Math.Min(0.5, Math.Sqrt(0.5 * rtol / erk));
                }

                absH = Math.Max(reduce * absH, hMin);
                h = direction * absH;
                k = knew;
                done = false;
            }

            result.StepCount++;
            kLast = k;
            hLast = h;
            Array.Copy(y, yLast, n);

            // The corrector, and the differences brought up to date with its slope.
            for (int c = 0; c < n; c++)
            {
                y[c] = p[c] + (h * g[k + 1] * phikp1[c]);
            }

            yp = f(t, y);
            result.Evaluations++;
            for (int c = 0; c < n; c++)
            {
                phi[k + 1][c] = yp[c] - phi[1][c];
                phi[k + 2][c] = phi[k + 1][c] - phi[k + 2][c];
            }

            for (int i = 1; i <= k; i++)
            {
                for (int c = 0; c < n; c++)
                {
                    phi[i][c] += phi[k + 1][c];
                }
            }

            if (knew == k - 1 || k == MaxOrder)
            {
                phase1 = false;
            }

            // The order for the next step: raised while the run is starting up, lowered when the
            // lower-order estimate was already better, and otherwise compared with the estimate
            // one order up.
            if (phase1)
            {
                k++;
            }
            else if (knew == k - 1)
            {
                k--;
                erk = erkm1;
            }
            else if (k + 1 <= ns)
            {
                double erkp1 = absH * GStar[k + 1] * WeightedNorm(phi[k + 2], invwt, normControl);
                if (k == 1)
                {
                    if (erkp1 < 0.5 * erk)
                    {
                        k++;
                        erk = erkp1;
                    }
                }
                else if (erkm1 <= Math.Min(erk, erkp1))
                {
                    k--;
                    erk = erkm1;
                }
                else if (k < MaxOrder && erkp1 < erk)
                {
                    k++;
                    erk = erkp1;
                }
            }

            int[]? clipped = null;
            if (nonNegative is not null && AnyNegative(y, nonNegative))
            {
                var indices = new List<int>();
                foreach (int index in nonNegative)
                {
                    if (y[index] < 0)
                    {
                        y[index] = 0;
                        indices.Add(index);
                    }
                }

                clipped = [.. indices];
            }

            // Reading inside the step: the interpolant works back from the step's end.
            double tEnd = t;
            double[] yEnd = y;
            int order = kLast;
            double[][] phiNow = phi;
            double[] psiNow = psi;
            double[] Interpolate(double at) => Interpolant(at, tEnd, yEnd, order, phiNow, psiNow, null, nonNegative);

            bool stoppedByEvent = false;
            if (events is not null)
            {
                (bool stop, double at, double[] state) = events.Locate(tLast, yLast, t, y, Interpolate, t0);
                if (stop)
                {
                    // The step is cut at the event, and the differences are rebuilt for the
                    // shortened step from the ones the step started with and the slope at the event.
                    var slopeAtEvent = new double[n];
                    Interpolant(at, t, y, kLast, phi, psi, slopeAtEvent, nonNegative);
                    Array.Copy(psiStart!, psi, psi.Length);
                    double hzc = at - tLast;
                    beta[1] = 1;
                    double temp1 = hzc;
                    for (int i = 2; i <= kLast; i++)
                    {
                        double temp2 = psi[i - 1];
                        psi[i - 1] = temp1;
                        temp1 = temp2 + hzc;
                        beta[i] = beta[i - 1] * psi[i - 1] / temp2;
                    }

                    psi[kLast] = temp1;
                    for (int i = 1; i <= 14; i++)
                    {
                        Array.Copy(phiStart![i], phi[i], n);
                    }

                    for (int i = 2; i <= kLast; i++)
                    {
                        Scale(phi[i], beta[i]);
                    }

                    // phi(:, 1:klast+2) = cumsum([ypzc, -phi(:, 1:klast+1)], 2): each column is the
                    // running sum of the slope at the event and the negated columns before it.
                    var previous = new double[kLast + 2][];
                    for (int i = 1; i <= kLast + 1; i++)
                    {
                        previous[i] = (double[])phi[i].Clone();
                    }

                    Array.Copy(slopeAtEvent, phi[1], n);
                    for (int i = 2; i <= kLast + 2; i++)
                    {
                        for (int c = 0; c < n; c++)
                        {
                            phi[i][c] = phi[i - 1][c] - previous[i - 1][c];
                        }
                    }

                    t = at;
                    Array.Copy(state, y, n);
                    tEnd = t;
                    done = true;
                    stoppedByEvent = true;
                }
            }

            if (options.RecordSteps)
            {
                var columns = new double[14][];
                for (int i = 1; i <= 14; i++)
                {
                    columns[i - 1] = (double[])phi[i].Clone();
                }

                result.Steps.Add(new OdeStepRecord(tLast, t, (double[])yLast.Clone(), (double[])y.Clone(),
                    columns, kLast, psi[1..13]));
            }

            if (output.AfterStep(tLast, t, y, Interpolate, stoppedByEvent))
            {
                done = true;
            }

            lastTime = t;
            if (done)
            {
                break;
            }

            if (phase1)
            {
                absH *= 2;
            }
            else if (0.5 * rtol >= erk * two[k + 1])
            {
                absH *= 2;
            }
            else if (0.5 * rtol < erk)
            {
                double reduce = Math.Pow(0.5 * rtol / erk, 1.0 / (k + 1));
                absH *= Math.Max(0.5, Math.Min(0.9, reduce));
            }

            if (clipped is not null)
            {
                foreach (int index in clipped)
                {
                    for (int i = 1; i <= 14; i++)
                    {
                        phi[i][index] = 0;
                    }
                }
            }
        }

        output.Finish();
        result.FinalTime = lastTime;
        return result;
    }

    /// <summary>
    /// The solution at <paramref name="at"/> off the differences at the end of a step —
    /// MATLAB's <c>ntrp113</c>: <paramref name="phi"/> holds columns 1..14 at index 1..14 (index 0
    /// unused) and <paramref name="psi"/> entries 1..12 likewise. The slope is written to
    /// <paramref name="slope"/> when one is given.
    /// </summary>
    public static double[] Interpolant(double at, double tEnd, double[] yEnd, int order, double[][] phi,
        double[] psi, double[]? slope, int[]? nonNegative)
    {
        int n = yEnd.Length;
        int ki = order + 1;
        double hi = at - tEnd;
        var w = new double[14];
        var g = new double[14];
        var rho = new double[14];
        for (int i = 1; i <= 13; i++)
        {
            w[i] = 1.0 / i;
        }

        g[1] = 1;
        rho[1] = 1;
        double term = 0;
        for (int j = 2; j <= ki; j++)
        {
            double gamma = (hi + term) / psi[j - 1];
            double eta = hi / psi[j - 1];
            for (int i = 1; i <= ki + 1 - j; i++)
            {
                w[i] = (gamma * w[i]) - (eta * w[i + 1]);
            }

            g[j] = w[1];
            rho[j] = gamma * rho[j - 1];
            term = psi[j - 1];
        }

        var value = new double[n];
        for (int c = 0; c < n; c++)
        {
            double sum = 0;
            double rate = 0;
            for (int i = 1; i <= ki; i++)
            {
                sum += phi[i][c] * g[i];
                rate += phi[i][c] * rho[i];
            }

            value[c] = yEnd[c] + (hi * sum);
            if (slope is not null)
            {
                slope[c] = rate;
            }
        }

        if (nonNegative is not null)
        {
            foreach (int index in nonNegative)
            {
                if (value[index] < 0)
                {
                    value[index] = 0;
                    if (slope is not null)
                    {
                        slope[index] = 0;
                    }
                }
            }
        }

        return value;
    }

    private static double[] InverseWeights(double[] y, double[] threshold, bool normControl)
    {
        if (normControl)
        {
            return [1 / Math.Max(NormEstimators.VectorNorm(y), threshold[0])];
        }

        var invwt = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            invwt[i] = 1 / Math.Max(Math.Abs(y[i]), threshold[i]);
        }

        return invwt;
    }

    private static double WeightedNorm(double[] values, double[] invwt, bool normControl)
    {
        if (normControl)
        {
            return NormEstimators.VectorNorm(values) * invwt[0];
        }

        double largest = 0;
        for (int i = 0; i < values.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(values[i] * invwt[i]));
        }

        return largest;
    }

    private static double[] Sum(double[] a, double[] b)
    {
        var sum = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            sum[i] = a[i] + b[i];
        }

        return sum;
    }

    private static void Scale(double[] values, double by)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] *= by;
        }
    }

    private static double[][] CloneColumns(double[][] columns)
    {
        var copy = new double[columns.Length][];
        for (int i = 0; i < columns.Length; i++)
        {
            copy[i] = (double[])columns[i].Clone();
        }

        return copy;
    }

    private static bool AnyNegative(double[] state, int[] indices)
    {
        foreach (int index in indices)
        {
            if (state[index] < 0)
            {
                return true;
            }
        }

        return false;
    }
}
