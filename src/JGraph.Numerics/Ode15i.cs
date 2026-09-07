using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// <c>ode15i</c>: the backward differentiation formulas of orders one to five in Lagrange form, for
/// a fully implicit equation <c>f(t, y, y') = 0</c>.
/// </summary>
/// <remarks>
/// <para>
/// Everything else here solves <c>M(t, y)·y' = f(t, y)</c>, which is implicit in the sense that the
/// slope has a matrix in front of it and explicit in the sense that the equation can be read as one
/// for <c>y'</c>. This solver does not require that: the state and the slope enter the residual
/// however the problem writes them, so the iteration matrix is <c>df/dy + (γ/h)·df/dy'</c> and both
/// partial derivatives have to be known.
/// </para>
/// <para>
/// The mesh is not quasi-constant, so the formulas are carried in Lagrange form rather than in
/// backward differences: each step recomputes its coefficients from the mesh it actually has, which
/// costs a small triangular recurrence and buys a step size that may change freely. The order is
/// chosen by comparing scaled derivatives of orders k − 1, k and k + 2, and the initial conditions
/// must already be consistent — <c>decic</c> is what makes them so.
/// </para>
/// </remarks>
public static class Ode15i
{
    /// <summary>MATLAB's name for this solver.</summary>
    public const string Name = "ode15i";

    private const double Epsilon = OdeStiffSupport.Epsilon;
    private const int MaxIterations = 4;

    /// <summary>The leading coefficients of the BDFs of orders one to five, in Lagrange form.</summary>
    private static readonly double[] Leading = [1, 3.0 / 2, 11.0 / 6, 25.0 / 12, 137.0 / 60];

    /// <summary>The remaining coefficients, column k being the formula of order k.</summary>
    private static readonly double[,] Trailing = new double[,]
    {
        { -1, -2, -3, -4, -5 },
        { 0, 1.0 / 2, 3.0 / 2, 3, 5 },
        { 0, 0, -1.0 / 3, -4.0 / 3, -10.0 / 3 },
        { 0, 0, 0, 1.0 / 4, 5.0 / 4 },
        { 0, 0, 0, 0, -1.0 / 5 },
    };

    /// <summary>Column k is the scaled derivative of order k off an equally spaced mesh.</summary>
    private static readonly double[,] Scaled = new double[,]
    {
        { 1, 1, 1, 1, 1, 1 },
        { -1, -2, -3, -4, -5, -6 },
        { 0, 1, 3, 6, 10, 15 },
        { 0, 0, -1, -4, -10, -20 },
        { 0, 0, 0, 1, 5, 15 },
        { 0, 0, 0, 0, -1, -6 },
        { 0, 0, 0, 0, 0, 1 },
    };

    /// <summary>Integrates <paramref name="residual"/> from <paramref name="y0"/> and <paramref name="yp0"/>.</summary>
    public static OdeResult Run(ImplicitOdeFunction residual, IReadOnlyList<double> tspan, double[] y0,
        double[] yp0, ImplicitOdeOptions options)
    {
        int n = y0.Length;
        OdeOptions common = options.Common;
        var result = new OdeResult { Solver = Name, FullStatistics = true };

        if (yp0.Length != n)
        {
            throw new OdeArgumentException("MATLAB:ode15i:YoYPoLengthMismatch",
                "The initial conditions y0 and yp0 must have the same length.");
        }

        if (common.NonNegative is { Length: > 0 })
        {
            common.Warn?.Invoke("ODE15I ignores the NonNegative property.");
        }

        OdeOptions stripped = OdeStiffPrelude.WithoutMassOrConstraint(common, Name, warnConstraint: false);
        OdeSetup setup = OdeSetup.Create(Name, (t, y) => residual(t, y, yp0), tspan, y0, stripped);
        result.Evaluations = setup.Evaluations;
        double rtol = setup.RelativeTolerance;
        double[] threshold = setup.Threshold;

        double t0 = setup.T0;
        double tFinal = setup.TFinal;
        double direction = setup.Direction;
        double t = t0;
        var y = (double[])y0.Clone();
        var yp = (double[])yp0.Clone();
        double[] f0 = setup.InitialSlope;   // the residual at the starting point

        ImplicitJacobianPair jacobians = ImplicitJacobianPair.Create(residual, t0, y0, yp0, f0, options,
            out int initialCost);
        result.PartialDerivatives++;
        result.Evaluations += initialCost;
        bool derivativesCurrent = true;

        int maxk = Math.Clamp(common.MaxOrder ?? 5, 1, 5);
        int refine = Math.Max(1, common.Refine ?? 1);
        var output = new OdeOutput(setup, common, refine, result);

        // The event function of an implicit problem reads the slope too, and the slope at a time
        // inside a step is the derivative of the same polynomial the state comes off.
        Func<double, double[], double[]> slopeAt = (_, _) => yp0;
        OdeEvents? events = options.Events is null
            ? null
            : new OdeEvents((et, ey) => options.Events(et, ey, slopeAt(et, ey)), t0, y0, result);

        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double absH;
        if (setup.FirstStep is null)
        {
            var weight = new double[n];
            for (int i = 0; i < n; i++)
            {
                weight[i] = Math.Max(Math.Abs(y[i]), threshold[i]);
            }

            double rh = 1.25 * OdeStiffSupport.WeightedMax(yp, weight) / Math.Sqrt(rtol);
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
        int k = 1;
        int klast = 0;                 // a dummy that forces the first factorization
        double absHLast = absH;
        bool raisedOrder = false;

        var mesh = new double[maxk + 2];
        var meshSolution = new double[maxk + 2][];
        for (int j = 0; j < meshSolution.Length; j++)
        {
            meshSolution[j] = new double[n];
        }

        mesh[0] = t0;
        Array.Copy(y0, meshSolution[0], n);

        // A fictitious point one step behind, so that the first step has a line to predict along.
        mesh[1] = t0 - h;
        for (int i = 0; i < n; i++)
        {
            meshSolution[1][i] = y0[i] - (h * yp0[i]);
        }

        int constantSteps = 1;
        LuDecomposition? factored = null;
        bool haveRate = false;
        double rate = 1;

        output.Begin(y);
        bool done = false;
        double lastTime = t;
        var invwt = new double[n];
        while (!done)
        {
            double tiny = OdeSetup.TinyStep(t);
            hMin = Math.Max(tiny, setup.SmallestStep);
            hMax = Math.Max(tiny, setup.LargestStep);
            absH = Math.Min(hMax, Math.Max(hMin, absH));

            if (1.1 * absH >= Math.Abs(tFinal - t))
            {
                h = tFinal - t;
                absH = Math.Abs(h);
                done = true;
            }

            int failures = 0;
            double tNew;
            var yNew = new double[n];
            var ypNew = new double[n];
            double optimalError;
            int bestOrder;
            double scaledK = 0;
            double scaledKp1;
            while (true)
            {
                bool gotNew = false;
                for (int i = 0; i < n; i++)
                {
                    invwt[i] = 1 / Math.Max(Math.Abs(y[i]), threshold[i]);
                }

                double[] predicted = new double[n];
                double errorConstant = 0;
                while (!gotNew)
                {
                    h = direction * absH;
                    tNew = done ? tFinal : t + h;
                    h = tNew - t;

                    if (absH != absHLast || k != klast)
                    {
                        if (absH != absHLast)
                        {
                            constantSteps = 0;
                        }

                        var matrix = new double[n, n];
                        double weightYp = Leading[k - 1] / h;
                        for (int r = 0; r < n; r++)
                        {
                            for (int c = 0; c < n; c++)
                            {
                                matrix[r, c] = jacobians.Y[r, c] + (weightYp * jacobians.Yp[r, c]);
                            }
                        }

                        factored = LuDecomposition.Factor(matrix);
                        result.Decompositions++;
                        haveRate = false;
                        rate = 1;
                    }

                    // Predict the state and the slope at tnew off the polynomial through the mesh.
                    double[,] weights = LagrangeWeights.Compute(mesh.AsSpan(0, k + 1), tNew, 1);
                    Array.Clear(yNew);
                    Array.Clear(ypNew);
                    for (int j = 0; j <= k; j++)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            yNew[i] += meshSolution[j][i] * weights[j, 0];
                            ypNew[i] += meshSolution[j][i] * weights[j, 1];
                        }
                    }

                    predicted = (double[])yNew.Clone();
                    double minimumNorm = 100 * Epsilon * OdeStiffSupport.ScaledMax(predicted, invwt);

                    // The local truncation error constant of this formula on this mesh.
                    errorConstant = -1.0 / (k + 1);
                    for (int j = 2; j <= k; j++)
                    {
                        double product = 1;
                        for (int m = 1; m <= k + 1; m++)
                        {
                            product *= ((t - ((j - 1) * h)) - mesh[m - 1]) / (h * m);
                        }

                        errorConstant -= Trailing[j - 1, k - 1] * product;
                    }

                    errorConstant = Math.Abs(errorConstant);

                    bool tooSlow = false;
                    int iterations = 0;
                    double savedNorm = 0;
                    for (int iter = 1; iter <= MaxIterations; iter++)
                    {
                        iterations = iter;
                        double[] value = residual(tNew, yNew, ypNew);
                        var rhs = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            rhs[i] = -value[i];
                        }

                        double[] step = factored!.Solve(rhs);
                        double newNorm = OdeStiffSupport.ScaledMax(step, invwt);
                        double weightYp = Leading[k - 1] / h;
                        for (int i = 0; i < n; i++)
                        {
                            yNew[i] += step[i];
                            ypNew[i] += weightYp * step[i];
                        }

                        if (iter == 1)
                        {
                            if (newNorm <= minimumNorm)
                            {
                                gotNew = true;
                                break;
                            }

                            savedNorm = newNorm;
                        }
                        else
                        {
                            rate = Math.Pow(newNorm / savedNorm, 1.0 / (iter - 1));
                            haveRate = true;
                            if (rate > 0.9)
                            {
                                tooSlow = true;
                                break;
                            }
                        }

                        if (haveRate && newNorm * rate / (1 - rate) <= 0.33 * rtol)
                        {
                            gotNew = true;
                            break;
                        }

                        if (iter == MaxIterations)
                        {
                            tooSlow = true;
                            break;
                        }
                    }

                    result.Evaluations += iterations;
                    result.LinearSolves += iterations;
                    if (!tooSlow)
                    {
                        continue;
                    }

                    result.Failed++;
                    absHLast = absH;
                    klast = k;
                    if (!derivativesCurrent)
                    {
                        double[] value = [];
                        if (jacobians.NeedsResidual)
                        {
                            value = residual(t, y, yp);
                            result.Evaluations++;
                        }

                        jacobians.Update(residual, t, y, yp, value, out int cost);
                        result.PartialDerivatives++;
                        result.Evaluations += cost;
                        derivativesCurrent = true;
                        klast = 0;   // force a new iteration matrix
                    }
                    else if (absH <= hMin)
                    {
                        common.Warn?.Invoke(OdeStiffSupport.ToleranceWarning(t, hMin));
                        output.Finish();
                        result.FinalTime = t;
                        result.FinalSlope = yp;
                        return result;
                    }
                    else
                    {
                        absH = 0.25 * absH;
                        done = false;
                    }
                }

                tNew = done ? tFinal : t + h;

                // The scaled derivative of order k + 1, off the difference between the predicted
                // and the corrected state: the error estimate and the order chooser both read it.
                double gap = 1;
                for (int m = 1; m <= k + 1; m++)
                {
                    gap *= absH * m / (tNew - mesh[m - 1]);
                }

                var difference = new double[n];
                for (int i = 0; i < n; i++)
                {
                    difference[i] = yNew[i] - predicted[i];
                }

                scaledKp1 = OdeStiffSupport.ScaledMax(difference, invwt) * Math.Abs(gap);
                optimalError = scaledKp1 / (k + 1);
                double error = scaledKp1 * errorConstant;

                bestOrder = k;
                if (k > 1)
                {
                    scaledK = ScaledDerivative(yNew, meshSolution, mesh, tNew, k, k, constantSteps >= k, absH, invwt);
                    if (k == 2)
                    {
                        if (scaledK <= 0.5 * scaledKp1)
                        {
                            bestOrder = k - 1;
                            optimalError = scaledK / k;
                        }
                    }
                    else
                    {
                        double scaledKm1 = ScaledDerivative(yNew, meshSolution, mesh, tNew, k - 1, k - 1,
                            constantSteps >= k - 1, absH, invwt);
                        if (Math.Max(scaledKm1, scaledK) <= scaledKp1)
                        {
                            bestOrder = k - 1;
                            optimalError = scaledK / k;
                        }
                    }
                }

                if (error <= rtol)
                {
                    break;
                }

                result.Failed++;
                if (absH <= hMin)
                {
                    common.Warn?.Invoke(OdeStiffSupport.ToleranceWarning(t, hMin));
                    output.Finish();
                    result.FinalTime = t;
                    result.FinalSlope = yp;
                    return result;
                }

                absHLast = absH;
                klast = k;
                failures++;
                switch (failures)
                {
                    case 1:
                        absH *= Math.Min(0.9, Math.Max(0.25,
                            0.9 * Math.Pow(0.5 * rtol / optimalError, 1.0 / (bestOrder + 1))));
                        break;
                    case 2:
                        absH *= 0.25;
                        break;
                    default:
                        bestOrder = 1;
                        absH *= 0.25;
                        break;
                }

                absH = Math.Max(absH, hMin);
                if (absH < absHLast)
                {
                    done = false;
                }

                k = bestOrder;
            }

            result.StepCount++;

            double tStart = t;
            double[] yStart = y;
            double tEnd = tNew;
            int behind = k;
            double[] behindMesh = mesh[..behind];
            var behindStates = new double[behind][];
            for (int j = 0; j < behind; j++)
            {
                behindStates[j] = (double[])meshSolution[j].Clone();
            }

            double[] Interpolate(double at) =>
                OdeStiffInterpolants.Ntrp15i(at, tEnd, yNew, behindMesh, behindStates, null);

            bool stoppedByEvent = false;
            if (events is not null)
            {
                double endTime = tEnd;
                double[] endSlope = ypNew;
                slopeAt = (at, _) =>
                {
                    if (at == endTime)
                    {
                        return endSlope;
                    }

                    var slope = new double[n];
                    OdeStiffInterpolants.Ntrp15i(at, endTime, yNew, behindMesh, behindStates, slope);
                    return slope;
                };

                (bool stop, double at2, double[] state) = events.Locate(tStart, yStart, tEnd, yNew, Interpolate, t0);
                if (stop)
                {
                    tEnd = at2;
                    Array.Copy(state, yNew, n);
                    done = true;
                    stoppedByEvent = true;
                }
            }

            if (common.RecordSteps)
            {
                result.Steps.Add(new OdeStepRecord(tStart, tEnd, (double[])yStart.Clone(),
                    (double[])yNew.Clone(), [], k, null));
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

            t = tEnd;
            y = (double[])yNew.Clone();
            yp = (double[])ypNew.Clone();
            for (int j = mesh.Length - 1; j > 0; j--)
            {
                mesh[j] = mesh[j - 1];
                meshSolution[j] = meshSolution[j - 1];
            }

            mesh[0] = t;
            meshSolution[0] = (double[])y.Clone();
            derivativesCurrent = jacobians.Constant;

            klast = k;
            absHLast = absH;
            constantSteps = Math.Min(constantSteps + 1, maxk + 2);

            // Raising the order is only considered from a settled mesh, and never twice running.
            if (constantSteps >= k + 2 && bestOrder >= k && k != maxk && !raisedOrder)
            {
                var above = new double[n];
                for (int j = 0; j <= k + 2; j++)
                {
                    double weight = Scaled[j, k + 1];
                    if (weight == 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < n; i++)
                    {
                        above[i] += meshSolution[j][i] * weight;
                    }
                }

                double scaledKp2 = OdeStiffSupport.ScaledMax(above, invwt);
                if (k > 1 && scaledK <= Math.Min(scaledKp1, scaledKp2))
                {
                    bestOrder = k - 1;
                    optimalError = scaledK / k;
                }
                else if ((k == 1 && scaledKp2 < 0.5 * scaledKp1) || (k > 1 && scaledKp2 < scaledKp1))
                {
                    bestOrder = k + 1;
                    optimalError = scaledKp2 / (k + 2);
                }
            }

            double temp = Math.Pow(optimalError / (0.5 * rtol), 1.0 / (bestOrder + 1));
            if (temp <= 0.5)
            {
                absH *= 2;
            }
            else if (temp > 1)
            {
                absH *= Math.Max(0.5, Math.Min(0.9, 1 / temp));
            }

            raisedOrder = bestOrder > k;
            k = bestOrder;
        }

        output.Finish();
        result.FinalTime = lastTime;
        result.FinalSlope = yp;
        return result;
    }

    /// <summary>
    /// The scaled derivative of order <paramref name="order"/> from the new state and the mesh
    /// behind it — off the equally spaced weights when the mesh has settled, and off Lagrange
    /// weights for the mesh actually there when it has not.
    /// </summary>
    private static double ScaledDerivative(double[] yNew, double[][] meshSolution, double[] mesh, double tNew,
        int order, int columns, bool settled, double absH, double[] invwt)
    {
        int n = yNew.Length;
        var combined = new double[n];
        if (settled)
        {
            double weight = Scaled[0, order - 1];
            for (int i = 0; i < n; i++)
            {
                combined[i] = yNew[i] * weight;
            }

            for (int j = 1; j <= columns; j++)
            {
                weight = Scaled[j, order - 1];
                if (weight == 0)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    combined[i] += meshSolution[j - 1][i] * weight;
                }
            }

            return OdeStiffSupport.ScaledMax(combined, invwt);
        }

        var nodes = new double[columns + 1];
        nodes[0] = tNew;
        for (int j = 0; j < columns; j++)
        {
            nodes[j + 1] = mesh[j];
        }

        double[,] weights = LagrangeWeights.Compute(nodes, tNew, order);
        for (int i = 0; i < n; i++)
        {
            combined[i] = yNew[i] * weights[0, order];
        }

        for (int j = 1; j <= columns; j++)
        {
            for (int i = 0; i < n; i++)
            {
                combined[i] += meshSolution[j - 1][i] * weights[j, order];
            }
        }

        return OdeStiffSupport.ScaledMax(combined, invwt) * Math.Pow(absH, order);
    }
}
