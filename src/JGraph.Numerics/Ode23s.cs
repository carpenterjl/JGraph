using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// <c>ode23s</c>: a modified Rosenbrock pair of orders two and three — one step, three solves
/// against a single factorization, and a fresh Jacobian every step.
/// </summary>
/// <remarks>
/// <para>
/// A Rosenbrock method is what an implicit method becomes when the Newton iteration is written out
/// and truncated after one pass per stage: the stages are linear systems rather than nonlinear ones,
/// so there is no iteration to converge and no convergence to fail. That is the whole of its
/// character. It is the solver to reach for at crude tolerances, where <c>ode15s</c>'s order
/// machinery costs more than it saves, and the solver to avoid at tight ones, where a second-order
/// formula takes far too many steps.
/// </para>
/// <para>
/// The price of having no iteration is that the Jacobian must be right: the method is only
/// second-order accurate when it is, so <c>ode23s</c> refreshes it at every single step rather than
/// holding one across many. It also cannot solve a differential-algebraic equation, and it takes
/// only a constant mass matrix — both refusals are MATLAB's and both are kept here.
/// </para>
/// </remarks>
public static class Ode23s
{
    /// <summary>MATLAB's name for this solver.</summary>
    public const string Name = "ode23s";

    private const double Epsilon = OdeStiffSupport.Epsilon;

    /// <summary>Integrates <paramref name="derivative"/> over <paramref name="tspan"/>.</summary>
    public static OdeResult Run(OdeFunction derivative, IReadOnlyList<double> tspan, double[] y0, OdeOptions options)
    {
        int n = y0.Length;
        double t0 = tspan.Count > 0 ? tspan[0] : 0;
        OdeMassType massType = OdeStiffSupport.MassTypeOf(options);
        var result = new OdeResult { Solver = Name, FullStatistics = true };

        if (massType > OdeMassType.Constant)
        {
            throw new OdeArgumentException("MATLAB:ode23s:NonConstantMassMatrix",
                "ODE23S cannot solve problems with a non-constant mass matrix.");
        }

        if (massType == OdeMassType.Constant)
        {
            if (options.MassSingular == OdeMassSingularity.Yes)
            {
                throw new OdeArgumentException("MATLAB:ode23s:MassSingularYes",
                    "ODE23S cannot solve problems with a singular mass matrix.");
            }

            if (options.MassSingular == OdeMassSingularity.Maybe)
            {
                options.Warn?.Invoke("ODE23S assumes the mass matrix is not singular.");
            }
        }

        OdeOptions stripped = OdeStiffPrelude.WithoutMassOrConstraint(options, Name, warnConstraint: true);
        OdeSetup setup = OdeSetup.Create(Name, derivative, tspan, y0, stripped);
        result.Evaluations = setup.Evaluations;
        OdeFunction f = setup.Function;
        double rtol = setup.RelativeTolerance;
        double[] threshold = setup.Threshold;
        bool normControl = setup.NormControl;

        double[,] mass = OdeStiffSupport.MassAt(options, massType, t0, y0, n);
        OdeStiffPrelude.CheckMassSize(Name, mass, n);
        bool hasMass = massType == OdeMassType.Constant;

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

        const double Power = 1.0 / 3;
        double d = 1 / (2 + Math.Sqrt(2));
        double e32 = 6 + Math.Sqrt(2);

        LuDecomposition? massFactored = null;
        double[] yp;
        if (hasMass)
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

        double[,] dfdy = jacobian.Evaluate(f, t, y, f0, out int firstCost);
        result.Evaluations += firstCost;
        if (!jacobian.Constant)
        {
            result.PartialDerivatives++;
        }

        double sqrtEps = Math.Sqrt(Epsilon);
        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double absH;
        if (setup.FirstStep is null)
        {
            double[] weight = Weights(y, threshold, normControl, normY);
            double rh = 1.25 * Measure(yp, weight, normControl) / Math.Pow(rtol, Power);
            absH = Math.Min(hMax, setup.SpanStep);
            if (absH * rh > 1)
            {
                absH = 1 / rh;
            }

            absH = Math.Max(absH, hMin);

            double first = direction * absH;
            double delta = (t + (direction * Math.Min(sqrtEps * Math.Max(Math.Abs(t), Math.Abs(t + first)), absH))) - t;
            double[] shifted = f(t + delta, y);
            result.Evaluations++;
            double[] second = OdeStiffSupport.Multiply(dfdy, yp);
            for (int i = 0; i < n; i++)
            {
                second[i] += (shifted[i] - f0[i]) / delta;
            }

            double[] measured = hasMass ? massFactored!.Solve(second) : second;
            rh = 1.25 * Math.Sqrt(0.5 * Measure(measured, weight, normControl)) / Math.Pow(rtol, Power);
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

            if (!jacobian.Constant && result.StepCount > 0)
            {
                dfdy = jacobian.Evaluate(f, t, y, f0, out int cost);
                result.Evaluations += cost;
                result.PartialDerivatives++;
            }

            // The derivative in time as well as in state: a Rosenbrock method needs both, and the
            // one extra evaluation it costs is charged once per step rather than once per attempt.
            double tdel = (t + (direction * Math.Min(sqrtEps * Math.Max(Math.Abs(t), Math.Abs(t + h)), absH))) - t;
            double[] shiftedValue = f(t + tdel, y);
            var dfdt = new double[n];
            for (int i = 0; i < n; i++)
            {
                dfdt[i] = (shiftedValue[i] - f0[i]) / tdel;
            }

            result.Evaluations++;

            bool noFailed = true;
            double error;
            double tNew;
            var yNew = new double[n];
            double[] k1;
            double[] k2;
            double[] f2;
            while (true)
            {
                double[,] iteration = OdeStiffSupport.IterationMatrix(mass, h * d, dfdy);
                var k1aux = new double[n];
                for (int i = 0; i < n; i++)
                {
                    k1aux[i] = f0[i] + (h * d * dfdt[i]);
                }

                LuDecomposition factored = LuDecomposition.Factor(iteration);
                k1 = factored.Solve(k1aux);

                var midpoint = new double[n];
                for (int i = 0; i < n; i++)
                {
                    midpoint[i] = y[i] + (0.5 * h * k1[i]);
                }

                double[] f1 = f(t + (0.5 * h), midpoint);
                double[] massK1 = hasMass ? OdeStiffSupport.Multiply(mass, k1) : k1;
                var second = new double[n];
                for (int i = 0; i < n; i++)
                {
                    second[i] = f1[i] - massK1[i];
                }

                k2 = factored.Solve(second);
                for (int i = 0; i < n; i++)
                {
                    k2[i] += k1[i];
                }

                tNew = done ? tFinal : t + h;
                h = tNew - t;

                for (int i = 0; i < n; i++)
                {
                    yNew[i] = y[i] + (h * k2[i]);
                }

                f2 = f(tNew, yNew);
                double[] massK2 = hasMass ? OdeStiffSupport.Multiply(mass, k2) : k2;
                var k3aux = new double[n];
                for (int i = 0; i < n; i++)
                {
                    k3aux[i] = f2[i] - (e32 * (massK2[i] - f1[i])) - (2 * (massK1[i] - f0[i])) + (h * d * dfdt[i]);
                }

                double[] k3 = factored.Solve(k3aux);
                result.Decompositions++;
                result.Evaluations += 2;
                result.LinearSolves += 3;

                var estimate = new double[n];
                for (int i = 0; i < n; i++)
                {
                    estimate[i] = k1[i] - (2 * k2[i]) + k3[i];
                }

                if (normControl)
                {
                    normYNew = OdeStiffSupport.Norm(yNew);
                    error = absH / 6 * (OdeStiffSupport.Norm(estimate)
                        / Math.Max(Math.Max(normY, normYNew), threshold[0]));
                }
                else
                {
                    double largest = 0;
                    for (int i = 0; i < n; i++)
                    {
                        largest = Math.Max(largest, Math.Abs(estimate[i])
                            / Math.Max(Math.Max(Math.Abs(y[i]), Math.Abs(yNew[i])), threshold[i]));
                    }

                    error = absH / 6 * largest;
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
                absH = Math.Max(hMin, absH * Math.Max(0.1, 0.8 * Math.Pow(rtol / error, Power)));
                h = direction * absH;
                done = false;
            }

            result.StepCount++;

            double tStart = t;
            double[] yStart = y;
            double tEnd = tNew;
            double hStep = tNew - t;
            double[] stage1 = k1;
            double[] stage2 = k2;

            double[] Interpolate(double at) =>
                OdeStiffInterpolants.Ntrp23s(at, tStart, yStart, hStep, stage1, stage2, null);

            bool stoppedByEvent = false;
            if (events is not null)
            {
                (bool stop, double at, double[] state) = events.Locate(tStart, yStart, tEnd, yNew, Interpolate, t0);
                if (stop)
                {
                    // The stages of the shortened step are the slopes of the same polynomial at the
                    // nodes the shortened step puts them at.
                    var atFirst = new double[n];
                    var atSecond = new double[n];
                    OdeStiffInterpolants.Ntrp23s(tStart + ((at - tStart) * d), tStart, yStart, hStep,
                        stage1, stage2, atFirst);
                    OdeStiffInterpolants.Ntrp23s(tStart + ((at - tStart) * 0.5), tStart, yStart, hStep,
                        stage1, stage2, atSecond);
                    stage1 = atFirst;
                    stage2 = atSecond;
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
                    (double[])yNew.Clone(), [(double[])stage1.Clone(), (double[])stage2.Clone()], 0, null));
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

            if (noFailed)
            {
                double temp = 1.25 * Math.Pow(error / rtol, Power);
                absH = temp > 0.2 ? absH / temp : 5 * absH;
            }

            t = tEnd;
            y = (double[])yNew.Clone();
            if (normControl)
            {
                normY = normYNew;
            }

            // The formula is first-same-as-last: the derivative at the new point has been taken.
            f0 = f2;
        }

        output.Finish();
        result.FinalTime = lastTime;
        return result;
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
