namespace JGraph.Numerics;

/// <summary>
/// The Dormand–Prince 5(4) pair — MATLAB's <c>ode45</c> — as the two calls the rest of the
/// project has known it by since M43 and M119. The pair itself lives in
/// <see cref="RungeKuttaScheme.DormandPrince"/> and runs on <see cref="ExplicitRungeKutta"/>
/// beside the three solvers M125 added; these are the same calls answered by that driver.
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

        if (tspan[0] == tspan[^1])
        {
            throw new ArgumentException("tspan must cover a nonzero interval.", nameof(tspan));
        }

        var options = new OdeOptions
        {
            RelativeTolerance = relativeTolerance,
            AbsoluteTolerance = [absoluteTolerance],
            Refine = Math.Max(1, refine),
            MaxStep = largestStep is { } ceiling && ceiling > 0 ? ceiling : null,
            InitialStep = firstStep is { } chosen && chosen > 0 ? chosen : null,
            RecordSteps = recording is not null,
        };

        OdeResult result = ExplicitRungeKutta.Run(
            RungeKuttaScheme.DormandPrince, (t, y) => derivative(t, y), tspan, initial, options);

        var points = new List<OdePoint>(result.Times.Count);
        for (int i = 0; i < result.Times.Count; i++)
        {
            points.Add(new OdePoint(result.Times[i], result.States[i]));
        }

        if (recording is not null)
        {
            foreach (OdeStepRecord step in result.Steps)
            {
                recording.Steps.Add(new OdeStep(step.Start, step.End - step.Start, step.StartState, step.Stages));
            }

            recording.Failed = result.Failed;
            recording.Evaluations = result.Evaluations;
        }

        return points;
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
    /// A time outside the recorded span is extrapolated from the nearest step rather than refused.
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
        slope = new double[step.State.Length];
        return RungeKuttaScheme.DormandPrince.Interpolate(step.Start, step.Step, step.State, step.Stages, at, slope, null);
    }
}
