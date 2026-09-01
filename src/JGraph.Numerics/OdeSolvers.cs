namespace JGraph.Numerics;

/// <summary>
/// Initial-value ODE integration: the Dormand–Prince 5(4) embedded Runge–Kutta pair — the method
/// behind MATLAB's ode45 — with its step control, the first-same-as-last reuse of the seventh
/// stage, and the pair's own continuous extension.
/// </summary>
/// <remarks>
/// <para>
/// The continuous extension is what makes the answer the shape MATLAB's is. An accepted step is a
/// coarse thing: over sixty time units of the Lorenz attractor the method takes some eight hundred
/// of them, and a curve drawn corner to corner through eight hundred points reads as a polygon
/// rather than a trajectory. MATLAB reports four points per step instead of one — the step's own
/// endpoint and three places inside it, each read off a quartic that agrees with the method at both
/// ends — and those three cost no derivative evaluations at all, because the polynomial is built
/// from the stages the step has already paid for.
/// </para>
/// <para>
/// Times asked for by name are read off the same polynomial. A caller who names them is not asking
/// the method to land on them, and clipping the step to each one would make the integration follow
/// the request rather than the equation.
/// </para>
/// </remarks>
public static class OdeSolvers
{
    /// <summary>One point of the answer: the time and the state.</summary>
    public readonly record struct OdePoint(double Time, double[] State);

    /// <summary>Points reported per accepted step when the caller names no times — MATLAB's <c>Refine</c>.</summary>
    public const int DefaultRefine = 4;

    /// <summary>
    /// One accepted step, with everything the pair's continuous extension needs to be read again:
    /// where the step started, how long it was, the state at its start and its seven stage slopes.
    /// </summary>
    public readonly record struct OdeStep(double Start, double Step, double[] State, double[][] Stages);

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

    /// <summary>
    /// The fifth-order weights less the fourth-order ones, which is the step's own error estimate:
    /// MATLAB's <c>E</c>. Kept as the difference rather than as two sets of weights because the
    /// difference is the only thing either set is wanted for.
    /// </summary>
    private static readonly double[] E =
        [71.0 / 57600, 0, -71.0 / 16695, 71.0 / 1920, -17253.0 / 339200, 22.0 / 525, -1.0 / 40];

    /// <summary>
    /// The continuous extension, stage by stage and then by power of the fraction through the step:
    /// MATLAB's <c>BI</c>. Each row sums to its <see cref="B5"/> weight, which is what makes the
    /// polynomial pass through the step's own endpoint at a fraction of one.
    /// </summary>
    private static readonly double[][] Interpolant =
    [
        [1, -183.0 / 64, 37.0 / 12, -145.0 / 128],
        [0, 0, 0, 0],
        [0, 1500.0 / 371, -1000.0 / 159, 1000.0 / 371],
        [0, -125.0 / 32, 125.0 / 12, -375.0 / 64],
        [0, 9477.0 / 3392, -729.0 / 106, 25515.0 / 6784],
        [0, -11.0 / 7, 11.0 / 3, -55.0 / 28],
        [0, 3.0 / 2, -4, 5.0 / 2],
    ];

    /// <summary>
    /// Integrates <paramref name="derivative"/> from the first to the last entry of
    /// <paramref name="tspan"/>. A two-entry span returns <paramref name="refine"/> points for every
    /// accepted step; more entries name the times wanted and only those are returned.
    /// </summary>
    /// <param name="derivative">dy/dt = f(t, y).</param>
    /// <param name="tspan">Strictly monotonic times, at least two.</param>
    /// <param name="initial">The initial state y(t0).</param>
    /// <param name="relativeTolerance">Per-step relative tolerance (MATLAB's default is 1e-3).</param>
    /// <param name="absoluteTolerance">Per-step absolute tolerance (MATLAB's default is 1e-6).</param>
    /// <param name="refine">Points reported per accepted step when no times are named.</param>
    /// <param name="largestStep">MATLAB's <c>MaxStep</c>; null leaves the default tenth of the interval.</param>
    /// <param name="firstStep">MATLAB's <c>InitialStep</c>; null leaves the step the slope suggests.</param>
    /// <param name="recording">
    /// Filled in with every accepted step and what the run cost, when the caller wants to read the
    /// solution again later. Null leaves the solver exactly as it was.
    /// </param>
    public static List<OdePoint> DormandPrince(
        Func<double, double[], double[]> derivative,
        IReadOnlyList<double> tspan,
        double[] initial,
        double relativeTolerance = 1e-3,
        double absoluteTolerance = 1e-6,
        int refine = DefaultRefine,
        double? largestStep = null,
        double? firstStep = null,
        OdeRecording? recording = null)
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

        bool namedTimes = tspan.Count > 2;
        int nextNamed = 1;
        refine = Math.Max(1, refine);

        // The scale a state is measured against once it is too small to measure against itself.
        // MATLAB carries the two tolerances as this one ratio and compares the error to the relative
        // tolerance alone, which is the same test written with one division instead of two.
        double threshold = absoluteTolerance / relativeTolerance;

        var y = (double[])initial.Clone();
        int n = y.Length;
        var results = new List<OdePoint> { new(t, (double[])y.Clone()) };

        var k = new double[7][];
        k[0] = derivative(t, y);
        if (recording is not null)
        {
            recording.Evaluations++;
        }

        if (k[0].Length != n)
        {
            throw new ArgumentException("The derivative must return one value per state variable.");
        }

        double span = Math.Abs(tEnd - t);

        // A tenth of the interval unless the caller named a ceiling, which is MATLAB's rule for
        // MaxStep: the option replaces the default rather than being taken alongside it.
        double hMax = largestStep is { } ceiling && ceiling > 0
            ? Math.Min(span, ceiling)
            : 0.1 * span;

        // The first step is chosen so that the leading error term is about the tolerance: the slope
        // at the start says how fast the state is moving, and the fifth root is the order of the
        // method. A guess that is merely reasonable costs a rejected step; this one rarely does.
        double absH = Math.Min(hMax, namedTimes ? Math.Abs(tspan[1] - t) : span);
        double rh = InfinityNorm(k[0], y, threshold) / (0.8 * Math.Pow(relativeTolerance, 0.2));
        if (absH * rh > 1)
        {
            absH = 1 / rh;
        }

        if (firstStep is { } chosen && chosen > 0)
        {
            // InitialStep says where to start, not where to stay: the step control takes over from
            // the first accepted step onwards exactly as it would have.
            absH = Math.Min(hMax, Math.Abs(chosen));
        }

        absH = Math.Max(absH, MinimumStep(t));

        var advanced = new double[n];
        var staged = new double[n];
        int attempts = 0;
        bool done = false;

        while (!done)
        {
            double hMin = MinimumStep(t);
            absH = Math.Min(hMax, Math.Max(hMin, absH));
            double h = direction * absH;

            // A step that would land within a tenth of itself of the end takes the rest instead,
            // rather than leaving a sliver behind that costs a whole step of its own.
            if (1.1 * absH >= Math.Abs(tEnd - t))
            {
                h = tEnd - t;
                absH = Math.Abs(h);
                done = true;
            }

            bool failedHere = false;
            double error;

            while (true)
            {
                if (++attempts > 1_000_000)
                {
                    throw new InvalidOperationException(
                        "ode45 exceeded one million steps without reaching the end time.");
                }

                for (int stage = 1; stage < 7; stage++)
                {
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
                    if (recording is not null)
                    {
                        recording.Evaluations++;
                    }
                }

                error = 0;
                for (int i = 0; i < n; i++)
                {
                    double fifth = 0;
                    double estimate = 0;
                    for (int stage = 0; stage < 7; stage++)
                    {
                        fifth += B5[stage] * k[stage][i];
                        estimate += E[stage] * k[stage][i];
                    }

                    advanced[i] = y[i] + (h * fifth);
                    double scale = Math.Max(Math.Max(Math.Abs(y[i]), Math.Abs(advanced[i])), threshold);
                    error = Math.Max(error, Math.Abs(estimate) / scale);
                }

                error *= absH;

                if (error <= relativeTolerance || absH <= hMin)
                {
                    break;
                }

                // The first refusal is answered by the size the error itself asks for; a second one
                // in the same step stops bargaining and halves, because a step failing twice is a
                // step whose error estimate is not to be trusted about its own size.
                absH = failedHere
                    ? Math.Max(hMin, 0.5 * absH)
                    : Math.Max(hMin, absH * Math.Max(0.1, 0.8 * Math.Pow(relativeTolerance / error, 0.2)));

                if (recording is not null)
                {
                    recording.Failed++;
                }

                failedHere = true;
                done = false;
                h = direction * absH;
            }

            double tNext = done ? tEnd : t + h;

            // The step's own stages, kept before the first-same-as-last rotation overwrites them.
            // This is what lets the solution be read again at a time nobody asked for while it ran:
            // the pair carries a fourth-order polynomial across every step it took, and without the
            // stages that polynomial is gone the moment the next step starts.
            if (recording is not null)
            {
                var stages = new double[7][];
                for (int stage = 0; stage < 7; stage++)
                {
                    stages[stage] = (double[])k[stage].Clone();
                }

                recording.Steps.Add(new OdeStep(tNext - h, h, (double[])y.Clone(), stages));
            }

            if (namedTimes)
            {
                while (nextNamed < tspan.Count && direction * (tspan[nextNamed] - tNext) <= 0)
                {
                    double named = tspan[nextNamed];

                    // A named time that is the step's own end is answered by the step, not by the
                    // polynomial through it: the two agree to rounding, and the step is the answer.
                    results.Add(new OdePoint(
                        named, named == tNext ? (double[])advanced.Clone() : Read(named)));
                    nextNamed++;
                }
            }
            else
            {
                for (int j = 1; j < refine; j++)
                {
                    double inside = t + (h * j / refine);
                    results.Add(new OdePoint(inside, Read(inside)));
                }

                results.Add(new OdePoint(tNext, (double[])advanced.Clone()));
            }

            // A step that had to be retried keeps the size that finally worked: the estimate that
            // rejected it is the only evidence there is, and it has just been shown to be optimistic.
            if (!failedHere)
            {
                double shrink = 1.25 * Math.Pow(error / relativeTolerance, 0.2);
                absH = shrink > 0.2 ? absH / shrink : 5 * absH;
            }

            t = tNext;
            (y, advanced) = (advanced, y);
            k[0] = k[6]; // first-same-as-last: stage 7 of this step is stage 1 of the next

            // The state at a time inside the step just taken, off the pair's continuous extension.
            // Declared here so it closes over the step's own stages; it is only ever called before
            // they are overwritten.
            double[] Read(double at)
            {
                // The step runs from tNext - h to tNext whether or not it was stretched to the end,
                // and s is how far through it the wanted time sits.
                double s = (at - (tNext - h)) / h;
                var read = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double sum = 0;
                    for (int stage = 0; stage < 7; stage++)
                    {
                        double[] row = Interpolant[stage];
                        double power = s;
                        double weight = 0;
                        for (int p = 0; p < 4; p++)
                        {
                            weight += row[p] * power;
                            power *= s;
                        }

                        sum += weight * k[stage][i];
                    }

                    read[i] = y[i] + (h * sum);
                }

                return read;
            }
        }

        return results;
    }

    /// <summary>
    /// The state and the slope at <paramref name="at"/>, off the continuous extension of whichever
    /// recorded step covers it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same polynomial the solver uses for its own refined output, evaluated after the fact:
    /// reading a solution at a time nobody named is the same operation as reporting four points per
    /// step, and it is right that they share the coefficients rather than agreeing by construction.
    /// </para>
    /// <para>
    /// A time outside the recorded span is extrapolated from the nearest step rather than refused,
    /// which is what MATLAB's <c>deval</c> does after warning about it.
    /// </para>
    /// </remarks>
    /// <param name="steps">The accepted steps, in the order they were taken.</param>
    /// <param name="at">The time wanted.</param>
    /// <param name="slope">dy/dt there, from the derivative of the same polynomial.</param>
    /// <returns>The state at <paramref name="at"/>.</returns>
    public static double[] Interpolate(IReadOnlyList<OdeStep> steps, double at, out double[] slope)
    {
        if (steps.Count == 0)
        {
            throw new ArgumentException("A solution with no steps cannot be read.", nameof(steps));
        }

        // Which step covers the time. The steps run in whichever direction the integration did, so
        // the comparison is written against the step's own end rather than against a fixed order.
        int found = steps.Count - 1;
        for (int i = 0; i < steps.Count; i++)
        {
            OdeStep candidate = steps[i];
            double end = candidate.Start + candidate.Step;
            bool covered = candidate.Step > 0
                ? at <= end
                : at >= end;
            if (covered)
            {
                found = i;
                break;
            }
        }

        OdeStep step = steps[found];
        double s = (at - step.Start) / step.Step;
        int n = step.State.Length;
        var state = new double[n];
        slope = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            double rate = 0;
            for (int stage = 0; stage < 7; stage++)
            {
                double[] row = Interpolant[stage];
                double power = s;   // s^(q+1), the weight's own term
                double lower = 1;   // s^q, one power down, which is what the derivative takes
                double weight = 0;
                double gradient = 0;
                for (int q = 0; q < 4; q++)
                {
                    weight += row[q] * power;
                    gradient += row[q] * (q + 1) * lower;
                    lower = power;
                    power *= s;
                }

                sum += weight * step.Stages[stage][i];
                rate += gradient * step.Stages[stage][i];
            }

            state[i] = step.State[i] + (step.Step * sum);
            slope[i] = rate;
        }

        return state;
    }

    /// <summary>
    /// The largest of <c>|v| / max(|y|, threshold)</c> over the state, which is the norm both the
    /// first step and the error test are measured in.
    /// </summary>
    private static double InfinityNorm(double[] values, double[] state, double threshold)
    {
        double largest = 0;
        for (int i = 0; i < values.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(values[i]) / Math.Max(Math.Abs(state[i]), threshold));
        }

        return largest;
    }

    /// <summary>
    /// Sixteen times the spacing of the doubles around <paramref name="t"/> — the smallest step that
    /// still moves the clock by enough to be seen at that magnitude.
    /// </summary>
    private static double MinimumStep(double t)
    {
        double magnitude = Math.Abs(t);
        return 16 * (magnitude == 0 ? double.Epsilon : Math.BitIncrement(magnitude) - magnitude);
    }
}
