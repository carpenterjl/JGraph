using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// <c>ode23t</c>: the trapezoidal rule with a free interpolant — the only solver of the family that
/// adds no numerical damping at all, and so the one to use when a problem is only moderately stiff
/// and its oscillations are part of the answer.
/// </summary>
/// <remarks>
/// <para>
/// The trapezoidal rule is the implicit method that sits exactly on the boundary of stability: it
/// damps nothing it should not, and it damps nothing it should either. Every other stiff formula
/// here buys robustness by pulling fast transients to zero faster than the equation does; this one
/// declines to, which is what makes it right for a lightly damped mechanical or circuit problem and
/// wrong for one whose fast modes must simply go away.
/// </para>
/// <para>
/// The first step is taken with backward Euler, because a trapezoidal step needs two points behind
/// it; the solver then doubles the step and switches, and it switches back to backward Euler if the
/// iteration fails three times in one step. Both changes are the reference's, and both show in the
/// step count.
/// </para>
/// </remarks>
public static class Ode23t
{
    /// <summary>MATLAB's name for this solver.</summary>
    public const string Name = "ode23t";

    private const double Epsilon = OdeStiffSupport.Epsilon;

    /// <summary>Integrates <paramref name="derivative"/> over <paramref name="tspan"/>.</summary>
    public static OdeResult Run(OdeFunction derivative, IReadOnlyList<double> tspan, double[] y0, OdeOptions options)
    {
        int n = y0.Length;
        double t0 = tspan.Count > 0 ? tspan[0] : 0;
        OdeMassType massType = OdeStiffSupport.MassTypeOf(options);
        var result = new OdeResult { Solver = Name, FullStatistics = true };

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

        bool dae = false;
        int icType = 2;
        if (massType != OdeMassType.None)
        {
            int nz = OdeStiffSupport.NonZeroCount(mass);
            if (nz == 0)
            {
                throw new OdeArgumentException("MATLAB:ode23t:MassMatrixAllZero",
                    "The mass matrix must have some nonzero entries.");
            }

            dae = options.MassSingular switch
            {
                OdeMassSingularity.No => false,
                OdeMassSingularity.Yes => true,
                _ => Epsilon * nz * OdeStiffSupport.ConditionEstimate(mass) > 1,
            };

            icType = massType >= OdeMassType.StateDependentWeak ? 3 : OdeStiffSupport.IsDiagonal(mass) ? 1 : 2;
        }

        OdeMassVectorJacobian? massVector = massType == OdeMassType.StateDependentStrong
            ? new OdeMassVectorJacobian(options, n)
            : null;

        var slopeGuess = new double[n];
        bool slopeAccepted = false;
        if (dae && options.InitialSlopeGuess is { } guess)
        {
            if (guess.Length != n)
            {
                throw new OdeArgumentException("MATLAB:ode23t:YoYPoLengthMismatch",
                    "InitialSlope must have the same length as the initial conditions.");
            }

            slopeGuess = (double[])guess.Clone();
            double[] product = OdeStiffSupport.Multiply(mass, slopeGuess);
            slopeAccepted = OdeStiffSupport.Norm(OdeStiffSupport.Subtract(product, f0))
                <= 1e-3 * rtol * Math.Max(OdeStiffSupport.Norm(product), OdeStiffSupport.Norm(f0));
        }

        double[] yp;
        double[,] dfdy;
        LuDecomposition? massFactored = null;
        if (dae && !slopeAccepted)
        {
            DaeInitialConditions.Consistent consistent = icType < 3
                ? DaeInitialConditions.Type12(f, t0, icType == 1, mass, y, slopeGuess, f0, rtol, jacobian)
                : DaeInitialConditions.Type3(f, options, massType, [.. tspan], setup.FirstStep, mass, y,
                    slopeGuess, f0, rtol, jacobian, massVector);
            y = consistent.Y;
            yp = consistent.Yp;
            f0 = consistent.F;
            dfdy = consistent.Jacobian;
            result.Evaluations += consistent.Evaluations;
            result.PartialDerivatives += consistent.PartialDerivatives;
            if (massType >= OdeMassType.StateDependentWeak)
            {
                mass = options.MassFunction!(t0, y);
            }
        }
        else
        {
            if (massType == OdeMassType.None)
            {
                yp = f0;
            }
            else if (dae)
            {
                yp = slopeGuess;
            }
            else
            {
                massFactored = LuDecomposition.Factor(mass);
                yp = massFactored.Solve(f0);
                result.Decompositions++;
                result.LinearSolves++;
            }

            dfdy = jacobian.Evaluate(f, t0, y, f0, out int firstCost);
            result.Evaluations += firstCost;
            if (!jacobian.Constant)
            {
                result.PartialDerivatives++;
            }
        }

        bool jacobianCurrent = true;
        bool massCurrent = true;
        bool needNewJacobian = false;
        bool needNewMass = false;
        double[,] massNew = mass;

        double power = 0.5;
        double gamma = 1;
        bool start = true;

        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double absH;
        if (setup.FirstStep is null)
        {
            double[] weight = Weights(y, threshold, normControl, normY);
            double rh = 1.43 * Measure(yp, weight, normControl) / Math.Pow(rtol, power);
            absH = Math.Min(hMax, setup.SpanStep);
            if (absH * rh > 1)
            {
                absH = 1 / rh;
            }

            absH = Math.Max(absH, hMin);

            if (!dae)
            {
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
                rh = 1.43 * Math.Sqrt(0.5 * Measure(measured, weight, normControl) / rtol);
                absH = Math.Min(hMax, setup.SpanStep);
                if (absH * rh > 1)
                {
                    absH = 1 / rh;
                }

                absH = Math.Max(absH, hMin);
            }
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
        double[] rowScale = [];
        LuDecomposition? factored = null;
        double? rate = null;

        double t1 = 0;
        double t2 = 0;
        double[] y1 = [];
        double[] y2 = [];

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
            int iterationFailures = 0;
            double error;
            double tNew;
            var yNew = new double[n];
            var zNew = new double[n];
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
                    double[,] matrix = OdeStiffSupport.IterationMatrix(mass, gamma * h, dfdy);
                    if (massSlope is not null)
                    {
                        OdeStiffSupport.AddInPlace(matrix, massSlope);
                    }

                    if (dae)
                    {
                        rowScale = OdeStiffSupport.ScaleRows(matrix);
                    }

                    factored = LuDecomposition.Factor(matrix);
                    result.Decompositions++;
                    rate = null;
                    needNewFactors = false;
                }

                tNew = done ? tFinal : t + h;
                h = tNew - t;

                if (start)
                {
                    // Backward Euler: the state moves by the scaled derivative alone.
                    Array.Copy(z, zNew, n);
                    for (int i = 0; i < n; i++)
                    {
                        yNew[i] = y[i] + z[i];
                    }
                }
                else
                {
                    double a1 = (tNew - t2) * (tNew - t1) / ((t - t2) * (t - t1));
                    double a2 = (tNew - t2) * (tNew - t) / ((t1 - t2) * (t1 - t));
                    double a3 = (tNew - t1) * (tNew - t) / ((t2 - t1) * (t2 - t));
                    for (int i = 0; i < n; i++)
                    {
                        yNew[i] = (a1 * y[i]) + (a2 * y1[i]) + (a3 * y2[i]);
                        zNew[i] = (2 * (yNew[i] - y[i])) - z[i];
                    }
                }

                if (massType == OdeMassType.TimeDependent)
                {
                    massNew = options.MassFunction!(tNew, yNew);
                }

                bool iterationFailed = IterativeSolve(f, options, massType, massNew, tNew, yNew, zNew, gamma, h,
                    factored!, rtol, wt, ref rate, dae, rowScale, out int iterations);
                result.Evaluations += iterations;
                result.LinearSolves += iterations;

                if (iterationFailed)
                {
                    noFailed = false;
                    result.Failed++;
                    iterationFailures++;
                    if (jacobianCurrent && massCurrent)
                    {
                        if (absH <= hMin)
                        {
                            options.Warn?.Invoke(OdeStiffSupport.ToleranceWarning(t, hMin));
                            output.Finish();
                            result.FinalTime = t;
                            return result;
                        }

                        if (iterationFailures == 3 && !start)
                        {
                            // Three refusals in one step: go back to backward Euler and choose the
                            // step its own error estimate asks for.
                            start = true;
                            power = 0.5;
                            gamma = 1;
                            var estimate = new double[n];
                            for (int i = 0; i < n; i++)
                            {
                                estimate[i] = (y[i] / ((t - t1) * (t - t2)))
                                    + (y1[i] / ((t1 - t) * (t1 - t2)))
                                    + (y2[i] / ((t2 - t) * (t2 - t1)));
                            }

                            double restartError = OdeStiffSupport.WeightedMax(estimate, wt) * absH * absH;
                            double q = Math.Sqrt(restartError / rtol);
                            double ratio = hMax / absH;
                            if (0.7 < q * ratio)
                            {
                                ratio = 0.7 / q;
                            }

                            double previous = absH;
                            absH = Math.Max(ratio * absH, hMin);
                            if (absH >= Math.Abs(tFinal - t))
                            {
                                absH = Math.Abs(tFinal - t);
                                done = true;
                            }
                            else
                            {
                                done = false;
                            }

                            h = direction * absH;
                            Scale(z, absH / previous);
                            needNewFactors = true;
                        }
                        else
                        {
                            double previous = absH;
                            absH = Math.Max(0.3 * absH, hMin);
                            h = direction * absH;
                            Scale(z, absH / previous);
                            needNewFactors = true;
                            done = false;
                        }
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

                if (start)
                {
                    // Backward Euler's error is half h²y'', approximated by differencing the
                    // scaled derivative across the step.
                    var estimate = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        estimate[i] = 0.5 * (zNew[i] - z[i]);
                    }

                    error = OdeStiffSupport.WeightedMax(estimate, wt);
                }
                else
                {
                    // The trapezoidal rule's is −h³y'''/12, approximated by differentiating a cubic
                    // through the four points the solver already has.
                    var estimate = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        estimate[i] = ((yNew[i] / ((tNew - t2) * (tNew - t1) * (tNew - t)))
                            + (y[i] / ((t - tNew) * (t - t2) * (t - t1)))
                            + (y1[i] / ((t1 - tNew) * (t1 - t) * (t1 - t2)))
                            + (y2[i] / ((t2 - t) * (t2 - t1) * (t2 - tNew)))) / 2;
                    }

                    error = OdeStiffSupport.WeightedMax(estimate, wt) * absH * absH * absH;
                }

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
                    : Math.Max(last * Math.Max(0.1, 0.7 * Math.Pow(rtol / error, power)), hMin);
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
            double hStep = h;
            double[] zStart = z;
            double[] zEnd = zNew;

            double[] Interpolate(double at) =>
                OdeStiffInterpolants.Ntrp23t(at, tStart, yStart, yNew, hStep, zStart, zEnd, null, nonNegative);

            bool stoppedByEvent = false;
            if (events is not null)
            {
                (bool stop, double at, double[] state) = events.Locate(tStart, yStart, tEnd, yNew, Interpolate, t0);
                if (stop)
                {
                    var slope = new double[n];
                    OdeStiffInterpolants.Ntrp23t(at, tStart, yStart, yNew, hStep, zStart, zEnd, slope, nonNegative);
                    double shortened = at - tStart;
                    var rescaled = new double[n];
                    var ending = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        rescaled[i] = shortened / hStep * zStart[i];
                        ending[i] = shortened * slope[i];
                    }

                    zStart = rescaled;
                    zEnd = ending;
                    tEnd = at;
                    Array.Copy(state, yNew, n);
                    hStep = tEnd - tStart;
                    done = true;
                    stoppedByEvent = true;
                }
            }

            if (options.RecordSteps)
            {
                result.Steps.Add(new OdeStepRecord(tStart, tEnd, (double[])yStart.Clone(),
                    (double[])yNew.Clone(), [(double[])zStart.Clone(), (double[])zEnd.Clone()], 0, null));
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

            if (start)
            {
                // The trapezoidal rule needs two points behind it; the first step's own interpolant
                // supplies the midpoint, and the step is then doubled, which the factorization
                // survives because the order rises with it.
                t1 = tStart + (0.5 * hStep);
                y1 = OdeStiffInterpolants.Ntrp23t(t1, tStart, yStart, yNew, hStep, zStart, zNew, null, nonNegative);
                t2 = tStart;
                y2 = yStart;

                t = tEnd;
                y = (double[])yNew.Clone();
                if (normControl)
                {
                    normY = normYNew;
                }

                z = (double[])zNew.Clone();
                AdvanceMass(ref mass, massNew, massType, ref massCurrent);
                jacobianCurrent = jacobian.Constant;

                start = false;
                power = 1.0 / 3;
                gamma = 0.5;
                absH = 2 * absH;
                Scale(z, 2);
            }
            else
            {
                t2 = t1;
                y2 = y1;
                t1 = t;
                y1 = y;

                t = tEnd;
                y = (double[])yNew.Clone();
                if (normControl)
                {
                    normY = normYNew;
                }

                z = (double[])zNew.Clone();
                AdvanceMass(ref mass, massNew, massType, ref massCurrent);
                jacobianCurrent = jacobian.Constant;

                if (noFailed)
                {
                    double q = Math.Pow(error / rtol, power);
                    double ratio = hMax / absH;
                    if (0.7 < q * ratio)
                    {
                        ratio = 0.7 / q;
                    }

                    ratio = Math.Min(5, Math.Max(0.2, ratio));
                    if (Math.Abs(ratio - 1) > 0.2)
                    {
                        // A step within twenty per cent of the one just taken is not worth a new
                        // factorization, so it is left alone.
                        absH = ratio * absH;
                        needNewFactors = true;
                        Scale(z, ratio);
                    }
                }
            }
        }

        output.Finish();
        result.FinalTime = lastTime;
        return result;
    }

    /// <summary>
    /// The simplified Newton iteration for <c>M·z = h·f(t, v + γ·z)</c>: the factorization is held
    /// constant, so what changes per pass is only the residual. Answers whether it gave up.
    /// </summary>
    private static bool IterativeSolve(OdeFunction f, OdeOptions options, OdeMassType massType, double[,] mass,
        double t, double[] y, double[] z, double gamma, double h, LuDecomposition factored, double rtol,
        double[] wt, ref double? rate, bool dae, double[] rowScale, out int iterations)
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
                if (dae)
                {
                    rhs[i] *= rowScale[i];
                }
            }

            double[] step = factored.Solve(rhs);
            double newNorm = 0;
            for (int i = 0; i < n; i++)
            {
                z[i] += step[i];
                y[i] += gamma * step[i];
            }

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

    private static void AdvanceMass(ref double[,] mass, double[,] massNew, OdeMassType massType, ref bool massCurrent)
    {
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
