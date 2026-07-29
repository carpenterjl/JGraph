namespace JGraph.Numerics;

/// <summary>
/// Initial-value ODE integration: the Dormand–Prince 5(4) embedded Runge–Kutta pair — the method
/// behind MATLAB's ode45 — with proportional step control and the first-same-as-last reuse of the
/// seventh stage. Accepted steps are returned as they fall; callers wanting specific sample points
/// pass them in the time span and get exact hits (the step is clipped to each target).
/// </summary>
public static class OdeSolvers
{
    /// <summary>One accepted integration step: the time and the state.</summary>
    public readonly record struct OdePoint(double Time, double[] State);

    // The Dormand–Prince tableau.
    private static readonly double[] C = [0, 1.0 / 5, 3.0 / 10, 4.0 / 5, 8.0 / 9, 1, 1];

    private static readonly double[][] A =
    [
        [],
        [1.0 / 5],
        [3.0 / 40, 9.0 / 40],
        [44.0 / 45, -56.0 / 15, 32.0 / 9],
        [19372.0 / 6561, -25360.0 / 2187, 64448.0 / 6561, -212.0 / 729],
        [9017.0 / 3168, -355.0 / 33, 46732.0 / 5247, 49.0 / 176, -5103.0 / 18656],
        [35.0 / 384, 0, 500.0 / 1113, 125.0 / 192, -2187.0 / 6784, 11.0 / 84],
    ];

    private static readonly double[] B5 = [35.0 / 384, 0, 500.0 / 1113, 125.0 / 192, -2187.0 / 6784, 11.0 / 84, 0];

    private static readonly double[] B4 =
        [5179.0 / 57600, 0, 7571.0 / 16695, 393.0 / 640, -92097.0 / 339200, 187.0 / 2100, 1.0 / 40];

    /// <summary>
    /// Integrates <paramref name="derivative"/> from the first to the last entry of
    /// <paramref name="tspan"/>. A two-entry span returns the naturally accepted steps; more entries
    /// are hit exactly and only those points are returned (MATLAB's tspan-vector behavior).
    /// </summary>
    /// <param name="derivative">dy/dt = f(t, y).</param>
    /// <param name="tspan">Strictly monotonic times, at least two.</param>
    /// <param name="initial">The initial state y(t0).</param>
    /// <param name="relativeTolerance">Per-step relative tolerance (MATLAB's default is 1e-3).</param>
    /// <param name="absoluteTolerance">Per-step absolute tolerance (MATLAB's default is 1e-6).</param>
    public static List<OdePoint> DormandPrince(
        Func<double, double[], double[]> derivative,
        IReadOnlyList<double> tspan,
        double[] initial,
        double relativeTolerance = 1e-3,
        double absoluteTolerance = 1e-6)
    {
        if (tspan.Count < 2)
        {
            throw new ArgumentException("tspan needs at least a start and an end time.", nameof(tspan));
        }

        double t = tspan[0];
        double tEnd = tspan[^1];
        double direction = Math.Sign(tEnd - t);
        if (direction == 0)
        {
            throw new ArgumentException("tspan must cover a nonzero interval.", nameof(tspan));
        }

        bool sampleOnly = tspan.Count > 2;
        int nextSample = 1;

        var y = (double[])initial.Clone();
        int n = y.Length;
        var results = new List<OdePoint> { new(t, (double[])y.Clone()) };

        var k = new double[7][];
        k[0] = derivative(t, y);
        if (k[0].Length != n)
        {
            throw new ArgumentException("The derivative must return one value per state variable.");
        }

        double span = Math.Abs(tEnd - t);
        double h = direction * Math.Min(span / 10, 0.1 * span + 1e-6);
        h = direction * Math.Max(Math.Abs(h), 1e-10);
        int steps = 0;

        while (direction * (tEnd - t) > 1e-14 * Math.Max(1, Math.Abs(t)))
        {
            if (++steps > 1_000_000)
            {
                throw new InvalidOperationException("ode45 exceeded one million steps without reaching the end time.");
            }

            // Clip to the end (and to the next requested sample point, so tspan entries are exact).
            if (direction * (t + h - tEnd) > 0)
            {
                h = tEnd - t;
            }

            if (sampleOnly && nextSample < tspan.Count && direction * (t + h - tspan[nextSample]) > 0)
            {
                h = tspan[nextSample] - t;
            }

            for (int stage = 1; stage < 7; stage++)
            {
                var staged = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double sum = 0;
                    for (int prior = 0; prior < stage; prior++)
                    {
                        sum += A[stage][prior] * k[prior][i];
                    }

                    staged[i] = y[i] + (h * sum);
                }

                k[stage] = derivative(t + (C[stage] * h), staged);
            }

            var advanced = new double[n];
            double error = 0;
            for (int i = 0; i < n; i++)
            {
                double fifth = 0;
                double fourth = 0;
                for (int stage = 0; stage < 7; stage++)
                {
                    fifth += B5[stage] * k[stage][i];
                    fourth += B4[stage] * k[stage][i];
                }

                advanced[i] = y[i] + (h * fifth);
                double scale = absoluteTolerance
                    + (relativeTolerance * Math.Max(Math.Abs(y[i]), Math.Abs(advanced[i])));
                double difference = h * (fifth - fourth);
                error = Math.Max(error, Math.Abs(difference) / scale);
            }

            if (error <= 1 || Math.Abs(h) <= 1e-14 * Math.Max(1, Math.Abs(t)))
            {
                t += h;
                y = advanced;
                k[0] = k[6]; // first-same-as-last: stage 7 of this step is stage 1 of the next

                bool record = !sampleOnly;
                if (sampleOnly && nextSample < tspan.Count
                    && Math.Abs(t - tspan[nextSample]) <= 1e-12 * Math.Max(1, Math.Abs(t)))
                {
                    record = true;
                    nextSample++;
                }

                if (record)
                {
                    results.Add(new OdePoint(t, (double[])y.Clone()));
                }
            }
            // A rejected step leaves t, y and the FSAL stage untouched; only h shrinks below.

            // Proportional control with the usual safety factor and growth clamps.
            double factor = error <= 0 ? 5 : 0.9 * Math.Pow(1.0 / error, 0.2);
            factor = Math.Clamp(factor, 0.2, 5.0);
            h *= factor;
        }

        return results;
    }
}
