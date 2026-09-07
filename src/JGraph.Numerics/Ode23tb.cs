using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// <c>ode23tb</c>: TR-BDF2 — one trapezoidal stage to a point inside the step, then one
/// second-order backward difference from there to the step's end, both solved against the same
/// factorization.
/// </summary>
/// <remarks>
/// <para>
/// The two stages are chosen so that the iteration matrix of the second is the iteration matrix of
/// the first: the trapezoidal stage lands at <c>t + (2 − √2)·h</c> exactly so that its coefficient
/// and BDF2's coincide, and one decomposition therefore serves the whole step. That is the design's
/// one idea, and it is why a step here costs about what a step of <c>ode23t</c> costs while damping
/// the way a backward difference formula damps.
/// </para>
/// <para>
/// The error estimate is taken twice: once directly, and once after a solve against the same
/// factorization, which is what stops the estimate from growing without bound on the very stiff
/// components the method is meant to handle. The larger of the second and a sixteenth of the first
/// is what the step is judged on.
/// </para>
/// </remarks>
public static class Ode23tb
{
    /// <summary>MATLAB's name for this solver.</summary>
    public const string Name = "ode23tb";

    private const double Epsilon = OdeStiffSupport.Epsilon;

    /// <summary>Integrates <paramref name="derivative"/> over <paramref name="tspan"/>.</summary>
    public static OdeResult Run(OdeFunction derivative, IReadOnlyList<double> tspan, double[] y0, OdeOptions options)
    {
        int n = y0.Length;
        double t0 = tspan.Count > 0 ? tspan[0] : 0;
        OdeMassType massType = OdeStiffSupport.MassTypeOf(options);
        var result = new OdeResult { Solver = Name, FullStatistics = true };

        if (massType != OdeMassType.None)
        {
            if (options.MassSingular == OdeMassSingularity.Yes)
            {
                throw new OdeArgumentException("MATLAB:ode23tb:MassSingularYes",
                    "ODE23TB cannot solve problems with a singular mass matrix.");
            }

            if (options.MassSingular == OdeMassSingularity.Maybe)
            {
                options.Warn?.Invoke("ODE23TB assumes the mass matrix is not singular.");
            }
        }

        OdeOptions stripped = OdeStiffPrelude.WithoutMass(options, massType, Name);
        OdeSetup setup = OdeSetup.Create(Name, derivative, tspan, y0, stripped);
        result.Evaluations = setup.Evaluations;
        OdeFunction f = setup.Function;
        double rtol = setup.RelativeTolerance;
        double[] threshold = setup.Threshold;
        bool normControl = setup.NormControl;
        int[]? nonNegative = setup.NonNegative;
        double[]? nonNegativeThreshold = setup.NonNegativeThreshold;

        double[,] mass = OdeStiffSupport.MassAt(options, massType, t0, y0, n);
        OdeStiffPrelude.CheckMassSize(Name, mass, n);

        int refine = Math.Max(1, options.Refine ?? 1);
        var output = new OdeOutput(setup, options, refine, result);
        OdeEvents? events = options.Events is null ? null : new OdeEvents(options.Events, t0, y0, result);

        double tFinal = setup.TFinal;
        double direction = setup.Direction;
        double t = t0;
        var y = (double[])y0.Clone();
        double normY = setup.InitialNorm;
        double normYNew = 0;
        double[] f0 = setup.InitialSlope;

        var jacobian = OdeJacobianSource.Read(options, y0);
        if (jacobian.Constant)
        {
            _ = jacobian.Evaluate(f, t0, y, f0, out int constantCost);
            result.Evaluations += constantCost;
            result.PartialDerivatives++;
        }

        OdeMassVectorJacobian? massVector = massType == OdeMassType.StateDependentStrong
            ? new OdeMassVectorJacobian(options, n)
            : null;

        const double Power = 1.0 / 3;
        double alpha = 2 - Math.Sqrt(2);
        double d = alpha / 2;
        double gg = Math.Sqrt(2) / 4;
        double c1 = (alpha - 1) / 3;
        const double C2 = 1.0 / 3;
        double c3 = -alpha / 3;
        double p31 = 1.5 + Math.Sqrt(2);
        double p32 = 2.5 + (2 * Math.Sqrt(2));
        double p33 = -(6 + (4.5 * Math.Sqrt(2)));

        LuDecomposition? massFactored = null;
        double[] yp;
        if (massType != OdeMassType.None)
        {
            massFactored = LuDecomposition.Factor(mass);
            yp = massFactored.Solve(f0);
            result.Decompositions++;
            result.LinearSolves++;
        }
        else
        {
            yp = f0;
        }

        double[,] dfdy = jacobian.Evaluate(f, t0, y, f0, out int firstCost);
        result.Evaluations += firstCost;
        if (!jacobian.Constant)
        {
            result.PartialDerivatives++;
        }

        bool jacobianCurrent = true;
        bool massCurrent = true;
        bool needNewJacobian = false;
        bool needNewMass = false;
        double[,] massMid = mass;
        double[,] massNew = mass;

        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double absH;
        if (setup.FirstStep is null)
        {
            double[] weight = Weights(y, threshold, normControl, normY);
            double rh = 1.43 * Measure(yp, weight, normControl) / Math.Pow(rtol, Power);
            absH = Math.Min(hMax, setup.SpanStep);
            if (absH * rh > 1)
            {
                absH = 1 / rh;
            }

            absH = Math.Max(absH, hMin);

            double first = direction * absH;
            double delta = (t + (direction * Math.Min(
                Math.Sqrt(Epsilon) * Math.Max(Math.Abs(t), Math.Abs(t + first)), absH))) - t;
            double[] shifted = f(t + delta, y);
            result.Evaluations++;
            double[] second = OdeStiffSupport.Multiply(dfdy, yp);
            for (int i = 0; i < n; i++)
            {
                second[i] += (shifted[i] - f0[i]) / delta;
            }

            double[] measured = massType == OdeMassType.None ? second : massFactored!.Solve(second);
            rh = 1.43 * Math.Sqrt(0.5 * Measure(measured, weight, normControl)) / Math.Pow(rtol, Power);
            absH = Math.Min(hMax, setup.SpanStep);
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

        double h = direction * absH;
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            z[i] = h * yp[i];
        }

        double[,]? massSlope = massType == OdeMassType.StateDependentStrong
            ? massVector!.Evaluate(t, y, z)
            : null;

        bool needNewFactors = true;
        LuDecomposition? factored = null;
        double? rate = null;

        output.Begin(y);
        bool done = false;
        double lastTime = t;
        var wt = new double[n];
        while (!done)
        {
            double tiny = OdeSetup.TinyStep(t);
            hMin = Math.Max(tiny, setup.SmallestStep);
            hMax = Math.Max(tiny, setup.LargestStep);
            double absHLast = absH;
            absH = Math.Min(hMax, Math.Max(hMin, absH));
            h = direction * absH;

            if (1.1 * absH >= Math.Abs(tFinal - t))
            {
                h = tFinal - t;
                absH = Math.Abs(h);
                done = true;
            }

            if (absH != absHLast)
            {
                Scale(z, absH / absHLast);
                needNewFactors = true;
            }

            bool noFailed = true;
            double error;
            double tNew;
            double tMid;
            var yNew = new double[n];
            var zNew = new double[n];
            var yMid = new double[n];
            var zMid = new double[n];
            while (true)
            {
                FillWeights(wt, y, threshold, normControl, normY);

                if (needNewJacobian)
                {
                    if (!jacobian.Analytic)
                    {
                        f0 = f(t, y);
                        result.Evaluations++;
                    }

                    dfdy = jacobian.Evaluate(f, t, y, f0, out int cost);
                    result.Evaluations += cost;
                    result.PartialDerivatives++;
                    jacobianCurrent = true;
                    needNewJacobian = false;
                    needNewFactors = true;
                }

                if (needNewMass)
                {
                    mass = options.MassFunction!(t, y);
                    massCurrent = true;
                    if (massType == OdeMassType.StateDependentStrong)
                    {
                        massSlope = massVector!.Evaluate(t, y, z);
                    }

                    needNewMass = false;
                    needNewFactors = true;
                }

                if (needNewFactors)
                {
                    double[,] matrix = OdeStiffSupport.IterationMatrix(mass, d * h, dfdy);
                    if (massSlope is not null)
                    {
                        OdeStiffSupport.AddInPlace(matrix, massSlope);
                    }

                    factored = LuDecomposition.Factor(matrix);
                    result.Decompositions++;
                    rate = null;
                    needNewFactors = false;
                }

                // The first stage is a trapezoidal step to the point that makes the second stage's
                // iteration matrix the same one.
                tMid = t + (alpha * h);
                for (int i = 0; i < n; i++)
                {
                    yMid[i] = y[i] + (alpha * z[i]);
                    zMid[i] = z[i];
                }

                if (massType == OdeMassType.TimeDependent)
                {
                    massMid = options.MassFunction!(tMid, yMid);
                }

                bool firstFailed = IterativeSolve(f, options, massType, massMid, tMid, yMid, zMid, d, h,
                    factored!, rtol, wt, ref rate, out int iterations);
                result.Evaluations += iterations;
                result.LinearSolves += iterations;

                bool secondFailed = false;
                if (!firstFailed)
                {
                    if (normControl)
                    {
                        double norm = OdeStiffSupport.Norm(yMid);
                        for (int i = 0; i < n; i++)
                        {
                            wt[i] = Math.Max(wt[i], norm);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
                        {
                            wt[i] = Math.Max(wt[i], Math.Abs(yMid[i]));
                        }
                    }

                    tNew = done ? tFinal : t + h;
                    for (int i = 0; i < n; i++)
                    {
                        zNew[i] = (p31 * z[i]) + (p32 * zMid[i]) + (p33 * (yMid[i] - y[i]));
                        yNew[i] = y[i] + (gg * (z[i] + zMid[i])) + (d * zNew[i]);
                    }

                    if (massType == OdeMassType.TimeDependent)
                    {
                        massNew = options.MassFunction!(tNew, yNew);
                    }

                    secondFailed = IterativeSolve(f, options, massType, massNew, tNew, yNew, zNew, d, h,
                        factored!, rtol, wt, ref rate, out int again);
                    result.Evaluations += again;
                    result.LinearSolves += again;
                }
                else
                {
                    tNew = done ? tFinal : t + h;
                }

                if (firstFailed || secondFailed)
                {
                    noFailed = false;
                    result.Failed++;
                    if (jacobianCurrent && massCurrent)
                    {
                        if (absH <= hMin)
                        {
                            options.Warn?.Invoke(OdeStiffSupport.ToleranceWarning(t, hMin));
                            output.Finish();
                            result.FinalTime = t;
                            return result;
                        }

                        double previous = absH;
                        absH = Math.Max(0.3 * absH, hMin);
                        h = direction * absH;
                        Scale(z, absH / previous);
                        needNewFactors = true;
                        done = false;
                    }
                    else
                    {
                        needNewJacobian = !jacobianCurrent;
                        needNewMass = !massCurrent;
                    }

                    continue;
                }

                if (normControl)
                {
                    normYNew = OdeStiffSupport.Norm(yNew);
                    for (int i = 0; i < n; i++)
                    {
                        wt[i] = Math.Max(wt[i], normYNew);
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        wt[i] = Math.Max(wt[i], Math.Abs(yNew[i]));
                    }
                }

                var estimate = new double[n];
                for (int i = 0; i < n; i++)
                {
                    estimate[i] = (c1 * z[i]) + (C2 * zMid[i]) + (c3 * zNew[i]);
                }

                double direct = OdeStiffSupport.WeightedMax(estimate, wt);
                double[] damped = factored!.Solve(estimate);
                result.LinearSolves++;
                error = Math.Max(OdeStiffSupport.WeightedMax(damped, wt), direct / 16);

                bool constraintRejected = false;
                if (nonNegative is not null && error <= rtol && OdeStiffSupport.AnyNegative(yNew, nonNegative))
                {
                    double shortfall;
                    if (normControl)
                    {
                        var clipped = new double[nonNegative.Length];
                        for (int i = 0; i < nonNegative.Length; i++)
                        {
                            clipped[i] = Math.Max(0, -yNew[nonNegative[i]]);
                        }

                        shortfall = OdeStiffSupport.Norm(clipped) / wt[0];
                    }
                    else
                    {
                        shortfall = 0;
                        for (int i = 0; i < nonNegative.Length; i++)
                        {
                            shortfall = Math.Max(shortfall,
                                Math.Max(0, -yNew[nonNegative[i]]) / nonNegativeThreshold![i]);
                        }
                    }

                    if (shortfall > rtol)
                    {
                        error = shortfall;
                        constraintRejected = true;
                    }
                }

                if (error <= rtol)
                {
                    break;
                }

                result.Failed++;
                if (absH <= hMin)
                {
                    options.Warn?.Invoke(OdeStiffSupport.ToleranceWarning(t, hMin));
                    output.Finish();
                    result.FinalTime = t;
                    return result;
                }

                noFailed = false;
                double last = absH;
                absH = constraintRejected
                    ? Math.Max(hMin, 0.5 * absH)
                    : Math.Max(last * Math.Max(0.1, 0.7 * Math.Pow(rtol / error, Power)), hMin);
                h = direction * absH;
                Scale(z, absH / last);
                needNewFactors = true;
                done = false;
            }

            result.StepCount++;

            List<int>? clipped2 = null;
            if (nonNegative is not null && OdeStiffSupport.AnyNegative(yNew, nonNegative))
            {
                clipped2 = [];
                foreach (int index in nonNegative)
                {
                    if (yNew[index] < 0)
                    {
                        yNew[index] = 0;
                        clipped2.Add(index);
                    }
                }

                if (normControl)
                {
                    normYNew = OdeStiffSupport.Norm(yNew);
                }
            }

            double tStart = t;
            double[] yStart = y;
            double tEnd = tNew;
            double midpoint = tMid;
            double[] midpointState = yMid;

            double[] Interpolate(double at) =>
                OdeStiffInterpolants.Ntrp23tb(at, tStart, yStart, tEnd, yNew, midpoint, midpointState,
                    null, nonNegative);

            bool stoppedByEvent = false;
            if (events is not null)
            {
                (bool stop, double at, double[] state) = events.Locate(tStart, yStart, tEnd, yNew, Interpolate, t0);
                if (stop)
                {
                    // The trapezoidal point of the shortened step is read off the same quadratic.
                    double shortened = tStart + (alpha * (at - tStart));
                    double[] moved = OdeStiffInterpolants.Ntrp23tb(shortened, tStart, yStart, tEnd, yNew,
                        midpoint, midpointState, null, nonNegative);
                    midpoint = shortened;
                    midpointState = moved;
                    tEnd = at;
                    Array.Copy(state, yNew, n);
                    done = true;
                    stoppedByEvent = true;
                }
            }

            if (options.RecordSteps)
            {
                result.Steps.Add(new OdeStepRecord(tStart, tEnd, (double[])yStart.Clone(),
                    (double[])yNew.Clone(), [(double[])midpointState.Clone()], 0, [midpoint]));
            }

            if (output.AfterStep(tStart, tEnd, yNew, Interpolate, stoppedByEvent))
            {
                done = true;
            }

            lastTime = tEnd;
            if (done)
            {
                break;
            }

            if (clipped2 is not null)
            {
                foreach (int index in clipped2)
                {
                    zNew[index] = 0;
                }
            }

            t = tEnd;
            y = (double[])yNew.Clone();
            if (normControl)
            {
                normY = normYNew;
            }

            z = (double[])zNew.Clone();
            jacobianCurrent = jacobian.Constant;
            if (massType <= OdeMassType.Constant)
            {
                massCurrent = true;
            }
            else if (massType == OdeMassType.TimeDependent)
            {
                mass = massNew;
                massCurrent = true;
            }
            else
            {
                massCurrent = false;
            }

            if (noFailed)
            {
                double q = Math.Pow(error / rtol, Power);
                double ratio = hMax / absH;
                if (0.7 < q * ratio)
                {
                    ratio = 0.7 / q;
                }

                ratio = Math.Min(5, Math.Max(0.2, ratio));
                if (Math.Abs(ratio - 1) > 0.2)
                {
                    absH = ratio * absH;
                    needNewFactors = true;
                    Scale(z, ratio);
                }
            }
        }

        output.Finish();
        result.FinalTime = lastTime;
        return result;
    }

    /// <summary>
    /// The simplified Newton iteration for <c>M·z = h·f(t, v + d·z)</c>, the same one both stages
    /// run against the same factorization. Answers whether it gave up.
    /// </summary>
    private static bool IterativeSolve(OdeFunction f, OdeOptions options, OdeMassType massType, double[,] mass,
        double t, double[] y, double[] z, double d, double h, LuDecomposition factored, double rtol,
        double[] wt, ref double? rate, out int iterations)
    {
        const int MaxIterations = 5;
        const double Kappa = 0.5;
        int n = y.Length;
        double minimumNorm = 100 * Epsilon * OdeStiffSupport.WeightedMax(y, wt);
        double oldNorm = 0;
        iterations = 0;

        for (int iter = 1; iter <= MaxIterations; iter++)
        {
            iterations = iter;
            if (massType >= OdeMassType.StateDependentWeak)
            {
                mass = options.MassFunction!(t, y);
            }

            double[] slope = f(t, y);
            double[] applied = OdeStiffSupport.MassTimes(massType, mass, z);
            var rhs = new double[n];
            for (int i = 0; i < n; i++)
            {
                rhs[i] = (h * slope[i]) - applied[i];
            }

            double[] step = factored.Solve(rhs);
            for (int i = 0; i < n; i++)
            {
                z[i] += step[i];
                y[i] += d * step[i];
            }

            double newNorm = 0;
            for (int i = 0; i < n; i++)
            {
                newNorm = Math.Max(newNorm, Math.Abs(step[i]) / Math.Max(wt[i], Math.Abs(y[i])));
            }

            if (newNorm <= minimumNorm)
            {
                return false;
            }

            if (iter == 1)
            {
                if (rate is { } known)
                {
                    if (newNorm * known / (1 - known) <= 0.1 * Kappa * rtol)
                    {
                        return false;
                    }
                }
                else
                {
                    rate = 0;
                }
            }
            else if (newNorm > 0.9 * oldNorm)
            {
                return true;
            }
            else
            {
                double current = Math.Max(0.9 * rate!.Value, newNorm / oldNorm);
                rate = current;
                double predicted = newNorm * current / (1 - current);
                if (predicted <= Kappa * rtol)
                {
                    return false;
                }

                if (iter == MaxIterations || Kappa * rtol < predicted * Math.Pow(current, MaxIterations - iter))
                {
                    return true;
                }
            }

            oldNorm = newNorm;
        }

        return false;
    }

    private static void Scale(double[] v, double factor)
    {
        for (int i = 0; i < v.Length; i++)
        {
            v[i] *= factor;
        }
    }

    private static void FillWeights(double[] wt, double[] y, double[] threshold, bool normControl, double normY)
    {
        if (normControl)
        {
            Array.Fill(wt, Math.Max(normY, threshold[0]));
            return;
        }

        for (int i = 0; i < y.Length; i++)
        {
            wt[i] = Math.Max(Math.Abs(y[i]), threshold[i]);
        }
    }

    private static double[] Weights(double[] y, double[] threshold, bool normControl, double normY)
    {
        if (normControl)
        {
            return [Math.Max(normY, threshold[0])];
        }

        var weight = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            weight[i] = Math.Max(Math.Abs(y[i]), threshold[i]);
        }

        return weight;
    }

    private static double Measure(double[] v, double[] weight, bool normControl) =>
        normControl ? OdeStiffSupport.Norm(v) / weight[0] : OdeStiffSupport.WeightedMax(v, weight);
}
