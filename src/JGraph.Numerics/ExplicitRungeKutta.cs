using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// The driver behind <c>ode23</c>, <c>ode45</c>, <c>ode78</c> and <c>ode89</c>: one loop over a
/// <see cref="RungeKuttaScheme"/>, with the step control, the events, the output function, the
/// non-negativity constraint and the mass matrix that the four share.
/// </summary>
/// <remarks>
/// <para>
/// The loop is MATLAB's, step for step, because the fixtures pin the step counts exact and a
/// count is decided on the last bit of an error estimate. Where the four reference files differ
/// from one another — when the step is purified, whether the weights are scaled by it first,
/// what a retry measures against — the scheme says so and the loop follows it.
/// </para>
/// <para>
/// The Verner pairs carry stages the interpolant needs beyond the ones the step itself needs.
/// Those are evaluated only when something reads inside the step — the solution structure, a
/// refined output, a named time, an event — and each costs a derivative call that the count
/// reports, exactly as the reference counts it.
/// </para>
/// </remarks>
public static class ExplicitRungeKutta
{
    /// <summary>Integrates <paramref name="derivative"/> over <paramref name="tspan"/> with <paramref name="scheme"/>.</summary>
    public static OdeResult Run(RungeKuttaScheme scheme, OdeFunction derivative, IReadOnlyList<double> tspan,
        double[] y0, OdeOptions options)
    {
        OdeSetup setup = OdeSetup.Create(scheme.Name, derivative, tspan, y0, options);
        var result = new OdeResult { Solver = scheme.Name, Evaluations = setup.Evaluations };
        int n = setup.N;
        OdeFunction f = setup.Function;
        double rtol = setup.RelativeTolerance;
        double[] threshold = setup.Threshold;
        double pow = scheme.ErrorExponent;
        bool normControl = setup.NormControl;
        int[]? nonNegative = setup.NonNegative;
        double[]? nonNegativeThreshold = setup.NonNegativeThreshold;

        int refine = Math.Max(1, options.Refine ?? scheme.DefaultRefine);
        var output = new OdeOutput(setup, options, refine, result);
        OdeEvents? events = options.Events is null ? null : new OdeEvents(options.Events, setup.T0, y0, result);

        double t0 = setup.T0;
        double tFinal = setup.TFinal;
        double direction = setup.Direction;
        double t = t0;
        var y = (double[])y0.Clone();
        double normY = setup.InitialNorm;
        double normYNew = 0;

        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double[] f0 = setup.InitialSlope;
        double absH;
        if (setup.FirstStep is null)
        {
            // The first step is chosen so that the leading error term is about the tolerance:
            // the slope at the start says how fast the state is moving, and the root is the
            // order of the method.
            absH = Math.Min(hMax, setup.SpanStep);
            double rh = (normControl
                ? NormEstimators.VectorNorm(f0) / Math.Max(normY, threshold[0])
                : setup.WeightedInfinityNorm(f0, y)) / (0.8 * Math.Pow(rtol, pow));
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

        int attemptStages = scheme.AttemptStages;
        int continuation = scheme.ContinuationNodes.Length;
        var k = new double[attemptStages + continuation][];
        k[0] = f0;
        bool firstSameAsLast = scheme.FirstSameAsLast;
        var staged = new double[n];
        var yNew = new double[n];
        var errorEstimate = new double[n];

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

            // A step that would land within a tenth of itself of the end takes the rest instead,
            // rather than leaving a sliver behind that costs a whole step of its own.
            if (1.1 * absH >= Math.Abs(tFinal - t))
            {
                h = tFinal - t;
                absH = Math.Abs(h);
                done = true;
            }

            bool noFailed = true;
            double error;
            double tNew;
            bool nonNegativeReset = false;
            while (true)
            {
                if (!firstSameAsLast)
                {
                    // The Verner pairs take the first slope fresh each step; a retry keeps it.
                    if (t == t0)
                    {
                        k[0] = f0;
                    }
                    else if (noFailed)
                    {
                        k[0] = f(t, y);
                        result.Evaluations++;
                    }
                }

                int internalStages = firstSameAsLast ? attemptStages - 1 : attemptStages;
                for (int s = 1; s < internalStages; s++)
                {
                    Combine(scheme, scheme.A[s], y, k, s, h, staged);
                    k[s] = f(t + (scheme.C[s] * h), staged);
                }

                tNew = done ? tFinal : t + h;
                if (scheme.PurifyBeforeSolution)
                {
                    h = tNew - t;
                }

                // The pairs whose last stage is the next step's first form the solution without
                // it — that stage is the slope at the solution, taken next.
                Combine(scheme, scheme.B, y, k, internalStages, h, yNew);
                if (!scheme.PurifyBeforeSolution)
                {
                    h = tNew - t;
                }

                if (firstSameAsLast)
                {
                    k[attemptStages - 1] = f(tNew, yNew);
                }

                result.Evaluations += attemptStages - 1;

                // The error estimate is the higher-order answer less the lower-order one, measured
                // against the larger of the two states — or, on a retry of a Verner step, against
                // the state the step started from alone.
                bool againstBoth = noFailed || !scheme.RetryWeightIgnoresNewState;
                for (int i = 0; i < n; i++)
                {
                    double estimate = 0;
                    for (int s = 0; s < attemptStages; s++)
                    {
                        estimate += scheme.E[s] * k[s][i];
                    }

                    errorEstimate[i] = estimate;
                }

                bool nonNegativeRejected = false;
                if (normControl)
                {
                    normYNew = NormEstimators.VectorNorm(yNew);
                    double weight = Math.Max(againstBoth ? Math.Max(normY, normYNew) : normY, threshold[0]);
                    error = absH * (NormEstimators.VectorNorm(errorEstimate) / weight);
                    if (nonNegative is not null && error <= rtol && AnyNegative(yNew, nonNegative))
                    {
                        var shortfall = new double[nonNegative.Length];
                        for (int i = 0; i < nonNegative.Length; i++)
                        {
                            shortfall[i] = Math.Max(0, -yNew[nonNegative[i]]);
                        }

                        double errorNonNegative = NormEstimators.VectorNorm(shortfall) / weight;
                        if (errorNonNegative > rtol)
                        {
                            error = errorNonNegative;
                            nonNegativeRejected = true;
                        }
                    }
                }
                else
                {
                    double largest = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double scale = Math.Max(
                            againstBoth ? Math.Max(Math.Abs(y[i]), Math.Abs(yNew[i])) : Math.Abs(y[i]),
                            threshold[i]);
                        largest = Math.Max(largest, Math.Abs(errorEstimate[i]) / scale);
                    }

                    error = absH * largest;
                    if (nonNegative is not null && error <= rtol && AnyNegative(yNew, nonNegative))
                    {
                        double errorNonNegative = 0;
                        for (int i = 0; i < nonNegative.Length; i++)
                        {
                            errorNonNegative = Math.Max(errorNonNegative,
                                Math.Max(0, -yNew[nonNegative[i]]) / nonNegativeThreshold![i]);
                        }

                        if (errorNonNegative > rtol)
                        {
                            error = errorNonNegative;
                            nonNegativeRejected = true;
                        }
                    }
                }

                bool failed = scheme.NanErrorFails ? !(error <= rtol) : error > rtol;
                if (!failed)
                {
                    nonNegativeReset = false;
                    if (nonNegative is not null && AnyNegative(yNew, nonNegative))
                    {
                        foreach (int index in nonNegative)
                        {
                            yNew[index] = Math.Max(yNew[index], 0);
                        }

                        if (normControl)
                        {
                            normYNew = NormEstimators.VectorNorm(yNew);
                        }

                        nonNegativeReset = true;
                    }

                    break;
                }

                result.Failed++;
                if (absH <= hMin)
                {
                    options.Warn?.Invoke(
                        $"Failure at t={t:E6}.  Unable to meet integration tolerances without reducing the step size below the smallest value allowed ({hMin:E6}) at time t.");
                    output.Finish();
                    result.FinalTime = t;
                    return result;
                }

                // The first refusal is answered by the size the error itself asks for, no lower
                // than the scheme's floor; a second one in the same step stops bargaining and halves.
                if (noFailed)
                {
                    noFailed = false;
                    absH = nonNegativeRejected
                        ? Math.Max(hMin, 0.5 * absH)
                        : Math.Max(hMin, absH * Math.Max(scheme.ShrinkFloor, 0.8 * Math.Pow(rtol / error, pow)));
                }
                else
                {
                    absH = Math.Max(hMin, 0.5 * absH);
                }

                h = direction * absH;
                done = false;
            }

            result.StepCount++;

            // Reading inside the step: the interpolant over this step's stages, with the
            // continuation stages evaluated the first time anything asks.
            double tStart = t;
            double hStep = h;
            double[] yStart = y;
            bool haveContinuation = continuation == 0;
            void EnsureContinuation()
            {
                if (haveContinuation)
                {
                    return;
                }

                for (int c = 0; c < continuation; c++)
                {
                    Combine(scheme, scheme.ContinuationWeights[c], yStart, k, attemptStages + c, hStep, staged);
                    k[attemptStages + c] = f(tStart + (scheme.ContinuationNodes[c] * hStep), staged);
                }

                result.Evaluations += continuation;
                haveContinuation = true;
            }

            double[] Interpolate(double at)
            {
                EnsureContinuation();
                return scheme.Interpolate(tStart, hStep, yStart, scheme.InterpolationStagesOf(k), at, null, nonNegative);
            }

            bool stoppedByEvent = false;
            if (events is not null)
            {
                EnsureContinuation();
                (bool stop, double at, double[] state) = events.Locate(t, y, tNew, yNew, Interpolate, t0);
                if (stop)
                {
                    // The step is cut at the event. The stages are re-read off the polynomial at
                    // the shortened step's own nodes, so what is stored for the step describes the
                    // step that was actually taken.
                    RestartStages(scheme, tStart, hStep, yStart, k, at, nonNegative);
                    tNew = at;
                    Array.Copy(state, yNew, n);
                    h = tNew - t;
                    hStep = h;
                    done = true;
                    stoppedByEvent = true;
                }
            }

            if (options.RecordSteps)
            {
                EnsureContinuation();
                var stages = new double[scheme.InterpolationStages.Length][];
                for (int j = 0; j < stages.Length; j++)
                {
                    stages[j] = (double[])k[scheme.InterpolationStages[j]].Clone();
                }

                result.Steps.Add(new OdeStepRecord(t, tNew, (double[])y.Clone(), (double[])yNew.Clone(), stages, 0, null));
            }

            if (output.AfterStep(t, tNew, yNew, Interpolate, stoppedByEvent))
            {
                done = true;
            }

            lastTime = tNew;
            if (done)
            {
                break;
            }

            // A step that had to be retried keeps the size that finally worked: the estimate that
            // rejected it is the only evidence there is, and it has just been shown optimistic.
            if (noFailed)
            {
                double shrink = 1.25 * Math.Pow(error / rtol, pow);
                absH = shrink > 0.2 ? absH / shrink : 5 * absH;
            }

            t = tNew;
            (y, yNew) = (yNew, y);
            if (normControl)
            {
                normY = normYNew;
            }

            if (firstSameAsLast)
            {
                if (nonNegativeReset)
                {
                    // The last stage was taken before the state was clipped; the next step's
                    // first stage must be the slope at the state it actually starts from.
                    k[attemptStages - 1] = f(t, y);
                    result.Evaluations++;
                }

                k[0] = k[attemptStages - 1];
            }
        }

        output.Finish();
        result.FinalTime = lastTime;
        return result;
    }

    /// <summary>
    /// <c>y + h·Σ w·k</c>, or <c>y + Σ k·(h·w)</c> for the scheme that scales its weights first,
    /// over the stages before <paramref name="count"/>, into <paramref name="into"/>.
    /// </summary>
    private static void Combine(RungeKuttaScheme scheme, double[] weights, double[] y, double[][] k, int count,
        double h, double[] into)
    {
        int n = y.Length;
        if (scheme.WeightsScaledByStep)
        {
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int j = 0; j < count; j++)
                {
                    sum += k[j][i] * (h * weights[j]);
                }

                into[i] = y[i] + sum;
            }

            return;
        }

        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < count; j++)
            {
                sum += weights[j] * k[j][i];
            }

            into[i] = y[i] + (h * sum);
        }
    }

    /// <summary>
    /// Replaces every interpolation stage but the first with the slope of the step's own polynomial
    /// at the node the stage sits at, over the step shortened to end at <paramref name="at"/>.
    /// </summary>
    private static void RestartStages(RungeKuttaScheme scheme, double t, double h, double[] y, double[][] k,
        double at, int[]? nonNegative)
    {
        int count = scheme.InterpolationStages.Length;
        double[][] current = scheme.InterpolationStagesOf(k);
        var slopes = new double[count][];
        for (int j = 1; j < count; j++)
        {
            var slope = new double[y.Length];
            scheme.Interpolate(t, h, y, current, t + ((at - t) * scheme.InterpolationNodes[j]), slope, nonNegative);
            slopes[j] = slope;
        }

        // Written back only once every slope is read: the polynomial is made of the stages it
        // is about to replace.
        for (int j = 1; j < count; j++)
        {
            k[scheme.InterpolationStages[j]] = slopes[j];
        }
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
