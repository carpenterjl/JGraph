using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// <c>ode15s</c>: the Klopfenstein–Shampine numerical differentiation formulas of orders one to
/// five, at quasi-constant step size, on backward differences — MATLAB's first answer to a stiff
/// problem, and the only solver of the family that also solves an index-1 differential-algebraic
/// equation.
/// </summary>
/// <remarks>
/// <para>
/// The method is what a stiff problem asks for; the bookkeeping is what makes it affordable. Each
/// step solves a nonlinear equation for the state at its end by a simplified Newton iteration whose
/// matrix <c>M − h·γ·J</c> is factored once and then reused — refactored only when the step size
/// changes, when the order changes, or when the iteration starts converging too slowly to be worth
/// continuing. The Jacobian behind it is refreshed less often still, only when a slow iteration
/// cannot be blamed on the step size, because forming it numerically costs one derivative
/// evaluation per state component.
/// </para>
/// <para>
/// The order is not on a schedule. Once a step has been taken at one size and order long enough for
/// the differences to be comparable, the differences themselves say what the error would be one
/// order below and one above, and whichever permits the longest next step wins. That is why
/// <c>nsteps</c> here is a small number where an explicit solver's is enormous, and why the step
/// counts are worth pinning exactly: the count is the algorithm in one number.
/// </para>
/// </remarks>
public static class Ode15s
{
    /// <summary>MATLAB's name for this solver.</summary>
    public const string Name = "ode15s";

    private const double Epsilon = OdeStiffSupport.Epsilon;
    private const int MaxIterations = 4;

    /// <summary>The leading coefficients of the backward differentiation formulas of orders one to five.</summary>
    private static readonly double[] G = [1, 3.0 / 2, 11.0 / 6, 25.0 / 12, 137.0 / 60];

    /// <summary>Klopfenstein's κ, which turns the BDFs into the NDFs; <c>BDF</c> <c>'on'</c> zeroes them.</summary>
    private static readonly double[] Kappa = [-37.0 / 200, -1.0 / 9, -0.0823, -0.0415, 0];

    /// <summary>The change of basis that rescales backward differences to a new step — its own inverse.</summary>
    private static readonly double[,] DifU = new double[,]
    {
        { -1, -2, -3, -4, -5 },
        { 0, 1, 3, 6, 10 },
        { 0, 0, -1, -4, -10 },
        { 0, 0, 0, 1, 5 },
        { 0, 0, 0, 0, -1 },
    };

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

        // Is the mass matrix singular? A DAE's initial state has to satisfy the algebraic part of
        // the equation before the first step can be taken, and a plausible guess almost never does.
        bool dae = false;
        int icType = 2;
        if (massType != OdeMassType.None)
        {
            int nz = OdeStiffSupport.NonZeroCount(mass);
            if (nz == 0)
            {
                throw new OdeArgumentException("MATLAB:ode15s:MassMatrixAllZero",
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
                throw new OdeArgumentException("MATLAB:ode15s:YoYPoLengthMismatch",
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
        double[,] massNew = mass;

        int maxk = Math.Clamp(options.MaxOrder ?? 5, 1, 5);
        var kappa = new double[5];
        if (!options.Bdf)
        {
            Array.Copy(Kappa, kappa, 5);
        }

        var invGa = new double[5];
        var erconst = new double[5];
        for (int i = 0; i < 5; i++)
        {
            invGa[i] = 1 / (G[i] * (1 - kappa[i]));
            erconst[i] = (kappa[i] * G[i]) + (1.0 / (i + 2));
        }

        var difU = new double[maxk, maxk];
        for (int i = 0; i < maxk; i++)
        {
            for (int j = 0; j < maxk; j++)
            {
                difU[i, j] = DifU[i, j];
            }
        }

        double hMin = Math.Max(OdeSetup.TinyStep(t), setup.SmallestStep);
        double hMax = Math.Max(OdeSetup.TinyStep(t), setup.LargestStep);
        double absH;
        if (setup.FirstStep is null)
        {
            double[] weight = Weights(y, threshold, normControl, normY);
            double rh = 1.25 * Measure(yp, weight, normControl) / Math.Sqrt(rtol);
            absH = Math.Min(hMax, setup.SpanStep);
            if (absH * rh > 1)
            {
                absH = 1 / rh;
            }

            absH = Math.Max(absH, hMin);

            if (!dae)
            {
                // BDF1's error is half h²y'', so the second derivative settles the first step —
                // and the Jacobian and one extra evaluation are all it takes to approximate it.
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
                rh = 1.25 * Math.Sqrt(0.5 * Measure(measured, weight, normControl) / rtol);
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
        int k = 1;
        int klast = k;
        double absHLast = absH;
        var dif = new double[maxk + 2][];
        for (int j = 0; j < dif.Length; j++)
        {
            dif[j] = new double[n];
        }

        for (int i = 0; i < n; i++)
        {
            dif[0][i] = h * yp[i];
        }

        double hInvGak = h * invGa[k - 1];
        int constantSteps = 0;
        double[] rowScale = [];
        double[,]? massSlope = null;
        LuDecomposition factored;
        bool haveRate = false;
        double convergenceRate = 0;

        if (massType == OdeMassType.StateDependentStrong)
        {
            massSlope = massVector!.Evaluate(t, y, Psi(dif, k, G, invGa));
        }

        Refactor();

        void Refactor()
        {
            double[,] matrix = OdeStiffSupport.IterationMatrix(mass, hInvGak, dfdy);
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
            haveRate = false;
        }

        output.Begin(y);
        bool done = false;
        bool atMinimum = false;
        double lastTime = t;
        var pred = new double[n];
        var difkp1 = new double[n];
        var invwt = new double[n];

        while (!done)
        {
            double tiny = OdeSetup.TinyStep(t);
            hMin = Math.Max(tiny, setup.SmallestStep);
            hMax = Math.Max(tiny, setup.LargestStep);
            absH = Math.Min(hMax, Math.Max(hMin, absH));
            if (absH == hMin)
            {
                if (atMinimum)
                {
                    absH = absHLast;
                }

                atMinimum = true;
            }
            else
            {
                atMinimum = false;
            }

            h = direction * absH;
            if (1.1 * absH >= Math.Abs(tFinal - t))
            {
                h = tFinal - t;
                absH = Math.Abs(h);
                done = true;
            }

            if (absH != absHLast || k != klast)
            {
                Rescale(dif, k, absH / absHLast, difU, maxk);
                hInvGak = h * invGa[k - 1];
                constantSteps = 0;
                Refactor();
            }

            bool noFailed = true;
            double error;
            double tNew = t;
            var yNew = new double[n];
            while (true)
            {
                bool gotNew = false;
                while (!gotNew)
                {
                    double[] psi = Psi(dif, k, G, invGa);
                    tNew = done ? tFinal : t + h;
                    h = tNew - t;
                    for (int i = 0; i < n; i++)
                    {
                        double sum = 0;
                        for (int j = 0; j < k; j++)
                        {
                            sum += dif[j][i];
                        }

                        pred[i] = y[i] + sum;
                        yNew[i] = pred[i];
                    }

                    Array.Clear(difkp1);
                    double minimumNorm;
                    if (normControl)
                    {
                        normYNew = OdeStiffSupport.Norm(yNew);
                        double scale = 1 / Math.Max(Math.Max(normY, normYNew), threshold[0]);
                        Array.Fill(invwt, scale);
                        minimumNorm = 100 * Epsilon * normYNew * scale;
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
                        {
                            invwt[i] = 1 / Math.Max(Math.Max(Math.Abs(y[i]), Math.Abs(yNew[i])), threshold[i]);
                        }

                        minimumNorm = 100 * Epsilon * OdeStiffSupport.ScaledMax(yNew, invwt);
                    }

                    if (massType == OdeMassType.TimeDependent)
                    {
                        massNew = options.MassFunction!(tNew, yNew);
                    }

                    bool tooSlow = false;
                    int iterations = 0;
                    double oldNorm = 0;
                    for (int iter = 1; iter <= MaxIterations; iter++)
                    {
                        iterations = iter;
                        if (massType >= OdeMassType.StateDependentWeak)
                        {
                            massNew = options.MassFunction!(tNew, yNew);
                        }

                        double[] slope = f(tNew, yNew);
                        var combined = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            combined[i] = psi[i] + difkp1[i];
                        }

                        double[] applied = OdeStiffSupport.MassTimes(massType, massNew, combined);
                        var rhs = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            rhs[i] = (hInvGak * slope[i]) - applied[i];
                            if (dae)
                            {
                                rhs[i] *= rowScale[i];
                            }
                        }

                        double[] step = factored.Solve(rhs);
                        double newNorm = normControl
                            ? OdeStiffSupport.Norm(step) * invwt[0]
                            : OdeStiffSupport.ScaledMax(step, invwt);
                        for (int i = 0; i < n; i++)
                        {
                            difkp1[i] += step[i];
                            yNew[i] = pred[i] + difkp1[i];
                        }

                        if (newNorm <= minimumNorm)
                        {
                            gotNew = true;
                            break;
                        }

                        if (iter == 1)
                        {
                            if (haveRate)
                            {
                                // A rate carried over from an earlier step is trusted, but only
                                // against a tighter test than the one it earned.
                                if (newNorm * convergenceRate / (1 - convergenceRate) <= 0.05 * rtol)
                                {
                                    gotNew = true;
                                    break;
                                }
                            }
                            else
                            {
                                convergenceRate = 0;
                            }
                        }
                        else if (newNorm > 0.9 * oldNorm)
                        {
                            tooSlow = true;
                            break;
                        }
                        else
                        {
                            convergenceRate = Math.Max(0.9 * convergenceRate, newNorm / oldNorm);
                            haveRate = true;
                            double predicted = newNorm * convergenceRate / (1 - convergenceRate);
                            if (predicted <= 0.5 * rtol)
                            {
                                gotNew = true;
                                break;
                            }

                            if (iter == MaxIterations
                                || 0.5 * rtol < predicted * Math.Pow(convergenceRate, MaxIterations - iter))
                            {
                                tooSlow = true;
                                break;
                            }
                        }

                        oldNorm = newNorm;
                    }

                    result.Evaluations += iterations;
                    result.LinearSolves += iterations;
                    if (!tooSlow)
                    {
                        continue;
                    }

                    result.Failed++;
                    if (!jacobianCurrent || !massCurrent)
                    {
                        // A stale linearization is the first thing to blame; only if it is current
                        // does the step size have to give way.
                        if (!jacobianCurrent)
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
                        }

                        if (!massCurrent)
                        {
                            mass = options.MassFunction!(t, y);
                            massCurrent = true;
                            if (massType == OdeMassType.StateDependentStrong)
                            {
                                massSlope = massVector!.Evaluate(t, y, psi);
                            }
                        }
                    }
                    else if (absH <= hMin)
                    {
                        options.Warn?.Invoke(OdeStiffSupport.ToleranceWarning(t, hMin));
                        output.Finish();
                        result.FinalTime = t;
                        return result;
                    }
                    else
                    {
                        absHLast = absH;
                        absH = Math.Max(0.3 * absH, hMin);
                        h = direction * absH;
                        done = false;
                        Rescale(dif, k, absH / absHLast, difU, maxk);
                        hInvGak = h * invGa[k - 1];
                        constantSteps = 0;
                    }

                    Refactor();
                }

                // difkp1 is the backward difference of order k + 1: the whole error estimate.
                error = (normControl
                    ? OdeStiffSupport.Norm(difkp1) * invwt[0]
                    : OdeStiffSupport.ScaledMax(difkp1, invwt)) * erconst[k - 1];

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

                        shortfall = OdeStiffSupport.Norm(clipped) * invwt[0];
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

                absHLast = absH;
                if (noFailed)
                {
                    noFailed = false;
                    double best = absH * Math.Max(0.1, 0.833 * Math.Pow(rtol / error, 1.0 / (k + 1)));
                    if (k > 1)
                    {
                        var lowered = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            lowered[i] = dif[k - 1][i] + difkp1[i];
                        }

                        double below = (normControl
                            ? OdeStiffSupport.Norm(lowered) * invwt[0]
                            : OdeStiffSupport.ScaledMax(lowered, invwt)) * erconst[k - 2];
                        double belowStep = absH * Math.Max(0.1, 0.769 * Math.Pow(rtol / below, 1.0 / k));
                        if (belowStep > best)
                        {
                            best = Math.Min(absH, belowStep);
                            k--;
                        }
                    }

                    absH = Math.Max(hMin, best);
                }
                else
                {
                    absH = Math.Max(hMin, 0.5 * absH);
                }

                h = direction * absH;
                if (absH < absHLast)
                {
                    done = false;
                }

                Rescale(dif, k, absH / absHLast, difU, maxk);
                hInvGak = h * invGa[k - 1];
                constantSteps = 0;
                Refactor();
            }

            result.StepCount++;

            for (int i = 0; i < n; i++)
            {
                dif[k + 1][i] = difkp1[i] - dif[k][i];
                dif[k][i] = difkp1[i];
            }

            for (int j = k - 1; j >= 0; j--)
            {
                for (int i = 0; i < n; i++)
                {
                    dif[j][i] += dif[j + 1][i];
                }
            }

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

            double[] Interpolate(double at) =>
                OdeStiffInterpolants.Ntrp15s(at, tEnd, yNew, hStep, dif, k, null, nonNegative);

            bool stoppedByEvent = false;
            if (events is not null)
            {
                (bool stop, double at, double[] state) = events.Locate(tStart, yStart, tEnd, yNew, Interpolate, t0);
                if (stop)
                {
                    // The differences are rebuilt over the shortened step, off the polynomial the
                    // full step carried, so that what is stored describes the step that was taken.
                    var aux = new double[k + 1][];
                    for (int j = 0; j <= k; j++)
                    {
                        aux[j] = OdeStiffInterpolants.Ntrp15s(at - (j * (at - tStart)), tEnd, yNew, hStep,
                            dif, k, null, nonNegative);
                    }

                    for (int j = 1; j <= k; j++)
                    {
                        for (int c = k; c >= j; c--)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                aux[c][i] = aux[c - 1][i] - aux[c][i];
                            }
                        }
                    }

                    for (int j = 0; j < k; j++)
                    {
                        Array.Copy(aux[j + 1], dif[j], n);
                    }

                    tEnd = at;
                    Array.Copy(state, yNew, n);
                    h = tEnd - tStart;
                    hStep = h;
                    done = true;
                    stoppedByEvent = true;
                }
            }

            if (options.RecordSteps)
            {
                var columns = new double[maxk + 2][];
                for (int j = 0; j < columns.Length; j++)
                {
                    columns[j] = (double[])dif[j].Clone();
                }

                result.Steps.Add(new OdeStepRecord(tStart, tEnd, (double[])yStart.Clone(),
                    (double[])yNew.Clone(), columns, k, null));
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

            klast = k;
            absHLast = absH;
            constantSteps = Math.Min(constantSteps + 1, maxk + 2);
            if (constantSteps >= k + 2)
            {
                // Long enough at one size and order for the differences to be comparable: whichever
                // of k − 1, k and k + 1 permits the longest next step is the order taken.
                double temp = 1.2 * Math.Pow(error / rtol, 1.0 / (k + 1));
                double best = temp > 0.1 ? absH / temp : 10 * absH;
                int bestOrder = k;
                if (k > 1)
                {
                    double below = (normControl
                        ? OdeStiffSupport.Norm(dif[k - 1]) * invwt[0]
                        : OdeStiffSupport.ScaledMax(dif[k - 1], invwt)) * erconst[k - 2];
                    temp = 1.3 * Math.Pow(below / rtol, 1.0 / k);
                    double stepBelow = temp > 0.1 ? absH / temp : 10 * absH;
                    if (stepBelow > best)
                    {
                        best = stepBelow;
                        bestOrder = k - 1;
                    }
                }

                if (k < maxk)
                {
                    double above = (normControl
                        ? OdeStiffSupport.Norm(dif[k + 1]) * invwt[0]
                        : OdeStiffSupport.ScaledMax(dif[k + 1], invwt)) * erconst[k];
                    temp = 1.4 * Math.Pow(above / rtol, 1.0 / (k + 2));
                    double stepAbove = temp > 0.1 ? absH / temp : 10 * absH;
                    if (stepAbove > best)
                    {
                        best = stepAbove;
                        bestOrder = k + 1;
                    }
                }

                if (best > absH)
                {
                    absH = best;
                    k = bestOrder;
                }
            }

            t = tEnd;
            y = (double[])yNew.Clone();
            if (clipped2 is not null)
            {
                // The differences describe the unperturbed solution; where the constraint moved a
                // component they have to describe the constrained one instead.
                foreach (int index in clipped2)
                {
                    for (int j = 0; j < dif.Length; j++)
                    {
                        dif[j][index] = 0;
                    }
                }
            }

            if (normControl)
            {
                normY = normYNew;
            }

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
        }

        output.Finish();
        result.FinalTime = lastTime;
        return result;
    }

    /// <summary><c>dif(:,1:k) · (G(1:k) · invGa(k))</c> — the constant part of the step's equation.</summary>
    private static double[] Psi(double[][] dif, int k, double[] g, double[] invGa)
    {
        int n = dif[0].Length;
        var psi = new double[n];
        for (int j = 0; j < k; j++)
        {
            double weight = g[j] * invGa[k - 1];
            for (int i = 0; i < n; i++)
            {
                psi[i] += dif[j][i] * weight;
            }
        }

        return psi;
    }

    /// <summary>
    /// Rescales the first <paramref name="k"/> backward differences to a step
    /// <paramref name="ratio"/> times as long — the change of basis that lets the step size move
    /// without the history being thrown away and rebuilt.
    /// </summary>
    private static void Rescale(double[][] dif, int k, double ratio, double[,] difU, int maxk)
    {
        var cumulative = new double[maxk, maxk];
        for (int j = 0; j < maxk; j++)
        {
            double running = 1;
            for (int i = 0; i < maxk; i++)
            {
                running *= (i - ((j + 1) * ratio)) / (i + 1);
                cumulative[i, j] = running;
            }
        }

        int n = dif[0].Length;
        var updated = new double[k][];
        for (int j = 0; j < k; j++)
        {
            updated[j] = new double[n];
            for (int p = 0; p < k; p++)
            {
                double weight = 0;
                for (int q = 0; q < maxk; q++)
                {
                    weight += cumulative[p, q] * difU[q, j];
                }

                if (weight == 0)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    updated[j][i] += dif[p][i] * weight;
                }
            }
        }

        for (int j = 0; j < k; j++)
        {
            dif[j] = updated[j];
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
