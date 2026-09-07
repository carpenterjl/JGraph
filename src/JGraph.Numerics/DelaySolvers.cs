namespace JGraph.Numerics;

/// <summary>The right-hand side of a delay problem: <c>f(t, y(t), [y(d₁) … y(d_k)])</c>.</summary>
public delegate double[] DdeFunction(double t, double[] y, double[][] z);

/// <summary>The delayed arguments themselves, when they are not constant lags — <c>ddesd</c>'s <c>d(t, y)</c>.</summary>
public delegate double[] DdeDelayFunction(double t, double[] y);

/// <summary>The event function of a delay problem, which sees the delayed values as well.</summary>
public delegate OdeEventReading DdeEventFunction(double t, double[] y, double[][] z);

/// <summary>What the solution is before the integration starts.</summary>
public sealed class DdeHistory
{
    /// <summary>A constant history, when the solution is the same column for every <c>t ≤ t0</c>.</summary>
    public double[]? Constant { get; init; }

    /// <summary>A history given as a function of time.</summary>
    public Func<double, double[]>? Function { get; init; }

    /// <summary>A previous run this one continues, whose mesh is read for arguments inside it.</summary>
    public DdeSolution? Previous { get; init; }

    /// <summary>The value at <paramref name="t"/>, before the run began.</summary>
    public double[] Before(double t)
    {
        if (Constant is { } constant)
        {
            return constant;
        }

        if (Function is { } function)
        {
            return function(t);
        }

        // A continuation asks the run it continues for its own history.
        return Previous!.History.Before(t);
    }
}

/// <summary>Everything <c>ddeset</c> can say that a delay solver acts on.</summary>
public sealed record DdeOptions
{
    /// <summary>Relative tolerance; MATLAB's default is 1e-3.</summary>
    public double RelativeTolerance { get; init; } = 1e-3;

    /// <summary>Absolute tolerance, one entry or one per state; null is MATLAB's 1e-6.</summary>
    public double[]? AbsoluteTolerance { get; init; }

    /// <summary>Measure the error in the norm of the whole state rather than component by component.</summary>
    public bool NormControl { get; init; }

    /// <summary>Times where the history or the equation has a jump — <c>dde23</c>'s <c>Jumps</c>.</summary>
    public double[]? Jumps { get; init; }

    /// <summary>An initial value different from what the history says at <c>t0</c>.</summary>
    public double[]? InitialY { get; init; }

    /// <summary>MATLAB's <c>MaxStep</c>; null leaves the default tenth of the interval.</summary>
    public double? MaxStep { get; init; }

    /// <summary>MATLAB's <c>MinStep</c>; null leaves the sixteen-ulp floor alone.</summary>
    public double? MinStep { get; init; }

    /// <summary>MATLAB's <c>InitialStep</c>; null leaves the step the slope suggests.</summary>
    public double? InitialStep { get; init; }

    /// <summary>Points handed to the output function per accepted step; MATLAB's default is one.</summary>
    public int? Refine { get; init; }

    /// <summary>The event function, or null.</summary>
    public DdeEventFunction? Events { get; init; }

    /// <summary>The output function, or null.</summary>
    public OdeOutputFunction? OutputFunction { get; init; }

    /// <summary>Which components the output function sees, 0-based; null is all of them.</summary>
    public int[]? OutputSelection { get; init; }

    /// <summary>Print the step, failure and evaluation counts when the run ends.</summary>
    public bool Stats { get; init; }

    /// <summary>Where a warning goes; null drops it.</summary>
    public Action<string>? Warn { get; init; }

    /// <summary>Where <see cref="Stats"/> prints; null drops the lines.</summary>
    public Action<string>? Print { get; init; }
}

/// <summary>What a delay solver answered — MATLAB's <c>sol</c> structure, field for field.</summary>
public sealed class DdeSolution
{
    /// <summary>Which solver ran.</summary>
    public required string Solver { get; init; }

    /// <summary>The history the run started from.</summary>
    public required DdeHistory History { get; init; }

    /// <summary>The mesh.</summary>
    public required double[] X { get; init; }

    /// <summary>The solution at the mesh points, one column each.</summary>
    public required double[][] Y { get; init; }

    /// <summary>The slope at the mesh points.</summary>
    public required double[][] Yp { get; init; }

    /// <summary>The discontinuities that were tracked; only <c>dde23</c> reports them.</summary>
    public double[]? Discont { get; init; }

    /// <summary>Whether the run was watching for events.</summary>
    public bool HadEvents { get; init; }

    /// <summary>Where events were found.</summary>
    public double[] EventTimes { get; init; } = [];

    /// <summary>The states there.</summary>
    public double[][] EventStates { get; init; } = [];

    /// <summary>Which event fired each time, 0-based.</summary>
    public int[] EventIndices { get; init; } = [];

    /// <summary>Accepted steps.</summary>
    public int Steps { get; init; }

    /// <summary>Attempts that were rejected.</summary>
    public int Failed { get; init; }

    /// <summary>Calls of the derivative.</summary>
    public int Evaluations { get; init; }

    /// <summary>Where the integration ended.</summary>
    public double FinalTime { get; init; }

    /// <summary>Whether the run was an initial-value neutral problem — <c>ddensd</c>'s <c>sol.IVP</c>.</summary>
    public bool InitialValueProblem { get; init; }

    /// <summary>The consistent pair an initial-value neutral problem started from, or null.</summary>
    public (double[] Y0, double[] Yp0)? InitialPair { get; init; }
}

/// <summary>
/// The delay differential solvers: <c>dde23</c> for constant lags, <c>ddesd</c> for delays that
/// depend on the state, and <c>ddensd</c> for neutral problems whose delayed derivative is
/// approximated by a difference quotient.
/// </summary>
/// <remarks>
/// <para>
/// A delay problem is an initial value problem whose right-hand side reads the solution in the
/// past, and the past is a polynomial the solver has already written down. That is the whole idea:
/// the interpolant that an explicit Runge–Kutta pair carries for its own dense output is exactly
/// what a delayed argument needs, so <c>dde23</c> is <c>ode23</c> with the history read off its own
/// Hermite cubic — and, when a lag is shorter than the step, off the tentative solution at the end
/// of the step being taken, iterated until it stops moving.
/// </para>
/// <para>
/// What a delay problem has that an initial value problem does not is a discontinuity structure it
/// can predict. The history and the solution generally disagree in the first derivative at
/// <c>t0</c>; that disagreement propagates to <c>t0 + τ</c> in the second derivative, to
/// <c>t0 + 2τ</c> in the third, and so on, smoothing as it goes. <c>dde23</c> works out where those
/// points are before it starts and steps exactly onto each of them, four levels deep — five when a
/// jump or an initial value has been declared, because then the solution itself is discontinuous
/// and one level of smoothing has been given away.
/// </para>
/// <para>
/// <c>ddesd</c> cannot predict them, because a state-dependent delay moves with the solution. It
/// controls the residual of its interpolant instead — sampled at two points inside each step,
/// scaled by the factor that turns that sample into an error estimate — and lets the step control
/// find the discontinuities by refusing steps near them.
/// </para>
/// </remarks>
public static class DelaySolvers
{
    /// <summary>MATLAB's name for the constant-lag solver.</summary>
    public const string Dde23Name = "dde23";

    /// <summary>MATLAB's name for the state-dependent solver.</summary>
    public const string DdesdName = "ddesd";

    /// <summary>MATLAB's name for the neutral solver.</summary>
    public const string DdensdName = "ddensd";

    private const double Epsilon = 2.220446049250313e-16;

    // --- dde23 ------------------------------------------------------------------------------------

    /// <summary>Integrates a system of delay equations with constant lags.</summary>
    public static DdeSolution Dde23(DdeFunction dde, double[] lags, DdeHistory history,
        double[] tspan, DdeOptions options)
    {
        double t0 = tspan[0];
        double tfinal = tspan[^1];
        if (tfinal <= t0)
        {
            throw new BvpException("MATLAB:dde23:TspandEndLTtspan1",
                "The final time must be greater than the initial time.");
        }

        double[] y0;
        if (history.Previous is { } previous)
        {
            if (previous.X[^1] != t0)
            {
                throw new BvpException("MATLAB:dde23:NotContinueFromHistoryEnd",
                    "The solution structure must end at the initial time.");
            }

            y0 = previous.Y[^1];
        }
        else
        {
            y0 = history.Before(t0);
        }

        int maxlevel = 4;
        if (options.InitialY is { Length: > 0 } given)
        {
            y0 = given;
            maxlevel = 5;
        }

        y0 = (double[])y0.Clone();
        int neq = y0.Length;

        double[] discontinuities;
        double[] reported;
        double minlag;
        if (lags.Length == 0)
        {
            discontinuities = [tfinal];
            reported = [tfinal];
            minlag = double.PositiveInfinity;
        }
        else
        {
            minlag = double.PositiveInfinity;
            double maxlag = 0;
            foreach (double lag in lags)
            {
                if (lag <= 0)
                {
                    throw new BvpException("MATLAB:dde23:NotPosLags", "The lags must all be positive.");
                }

                minlag = Math.Min(minlag, lag);
                maxlag = Math.Max(maxlag, lag);
            }

            var level = new List<double> { t0 };
            var kept = new List<double>();
            if (history.Previous is { Discont: { } before })
            {
                int last = -1;
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] < t0 - maxlag)
                    {
                        last = i;
                    }
                }

                if (last >= 0)
                {
                    kept.AddRange(before[..(last + 1)]);
                    level.Clear();
                    level.AddRange(before[(last + 1)..]);
                    level.Add(t0);
                }
            }

            if (options.Jumps is { Length: > 0 } jumps)
            {
                var inside = jumps.Where(j => t0 - maxlag <= j && j <= tfinal).ToList();
                if (inside.Count > 0)
                {
                    level.AddRange(inside);
                    level.Sort();
                    maxlevel = 5;
                }
            }

            var sortedLags = (double[])lags.Clone();
            Array.Sort(sortedLags);
            var found = new List<double>(level);
            for (int depth = 2; depth <= maxlevel; depth++)
            {
                var next = new List<double>();
                foreach (double lag in sortedLags)
                {
                    foreach (double point in level)
                    {
                        double moved = point + lag;
                        if (moved <= tfinal)
                        {
                            next.Add(moved);
                        }
                    }
                }

                if (next.Count == 0)
                {
                    break;
                }

                if (next.Count > 1)
                {
                    next.Sort();
                    next = Distinct(next);
                }

                found.AddRange(next);
                level = next;
            }

            if (found.Count > 1)
            {
                found.Sort();
                found = Distinct(found);
            }

            discontinuities = [.. found];
            reported = history.Previous is { Discont: { } carried }
                ? [.. carried, .. found]
                : [.. found];
            _ = kept;
        }

        var stepped = new List<double>(discontinuities);
        if (Math.Abs(tfinal - stepped[^1]) <= 10 * Spacing(tfinal))
        {
            stepped[^1] = tfinal;
        }
        else
        {
            stepped.Add(tfinal);
        }

        stepped.RemoveAll(point => point <= t0);
        int nextDiscontinuity = 0;

        const double Power = 1.0 / 3;
        double[][] b =
        [
            [0.5, 0, 2.0 / 9],
            [0, 0.75, 1.0 / 3],
            [0, 0, 4.0 / 9],
            [0, 0, 0],
        ];
        double[] e = [-5.0 / 72, 1.0 / 12, 1.0 / 9, -1.0 / 8];

        var x = new List<double> { t0 };
        var y = new List<double[]> { y0 };
        var yp = new List<double[]>();

        double[][] z0 = LagValues(t0, null, lags, null, history, x, y, yp);
        double[] f0 = dde(t0, y0, z0);
        int evaluations = 1;
        yp.Add(f0);

        double rtol = options.RelativeTolerance;
        if (rtol < 100 * Epsilon)
        {
            rtol = 100 * Epsilon;
            options.Warn?.Invoke($"RelTol has been increased to {rtol:G}.");
        }

        double[] atol = options.AbsoluteTolerance is { Length: > 0 } tolerance
            ? tolerance
            : [1e-6];
        bool normControl = options.NormControl;
        double[] threshold = new double[normControl ? 1 : neq];
        for (int i = 0; i < threshold.Length; i++)
        {
            threshold[i] = (atol.Length == 1 ? atol[0] : atol[i]) / rtol;
        }

        double span = tfinal - t0;
        double hmax0 = Math.Min(span, options.MaxStep ?? (0.1 * span));
        double userhmin = options.MinStep is { } floorStep ? Math.Min(span, floorStep) : 0;
        double? htry = options.InitialStep;

        double tiny = 16 * Spacing(t0);
        double hmin = Math.Max(tiny, userhmin);
        double hmax = Math.Max(tiny, hmax0);
        double normY = normControl ? Norm(y0) : 0;
        double h;
        if (htry is null)
        {
            h = Math.Min(hmax, span);
            double rh = normControl
                ? Norm(f0) / Math.Max(normY, threshold[0]) / (0.8 * Math.Pow(rtol, Power))
                : WeightedNorm(f0, y0, threshold) / (0.8 * Math.Pow(rtol, Power));
            if (h * rh > 1)
            {
                h = 1 / rh;
            }

            h = Math.Max(h, hmin);
        }
        else
        {
            h = Math.Min(hmax, Math.Max(hmin, htry.Value));
        }

        h = Math.Min(h, 0.5 * minlag);

        var result = new OdeResult { Solver = Dde23Name };
        var output = new DdeOutput(options, t0, tfinal, tspan, y0, neq);
        DdeEvents? events = options.Events is null
            ? null
            : new DdeEvents(options.Events, t0, y0, z0, result);

        double t = t0;
        double[] state = y0;
        var stages = new double[4][];
        stages[0] = f0;
        int steps = 0;
        int failed = 0;
        bool done = false;
        double lastTime = t0;

        while (!done)
        {
            tiny = 16 * Spacing(t);
            hmin = Math.Max(tiny, userhmin);
            hmax = Math.Max(tiny, hmax0);
            h = Math.Min(hmax, Math.Max(hmin, h));

            bool hitDiscontinuity = false;
            double distance = stepped[nextDiscontinuity] - t;
            if (Math.Min(1.1 * h, hmax) >= distance)
            {
                h = distance;
                hitDiscontinuity = true;
            }
            else if (2 * h >= distance)
            {
                h = distance / 2;
            }

            if (!hitDiscontinuity && minlag < h && h < 2 * minlag)
            {
                h = minlag;
            }

            bool noFailed = true;
            double error = 0;
            double tnew;
            double[] ynew;
            bool iterationFailed;
            while (true)
            {
                // The stage weights are formed from the step the control asked for, and the step is
                // only then purified against the point it is required to land on. A step stretched
                // onto a discontinuity therefore takes its stages at the nodes of the step it meant
                // to take, which is what MATLAB does and what the step counts here depend on.
                double proposed = h;
                double t1 = t + (0.5 * proposed);
                double t2 = t + (0.75 * proposed);
                tnew = t + proposed;
                if (hitDiscontinuity)
                {
                    tnew = stepped[nextDiscontinuity];
                }

                h = tnew - t;
                int iterations = minlag < h ? 5 : 1;
                List<double>? iterX = null;
                List<double[]>? iterY = null;
                List<double[]>? iterYp = null;
                double[]? previousY = null;
                iterationFailed = false;
                ynew = state;
                for (int iteration = 1; iteration <= iterations; iteration++)
                {
                    List<double> mx = iterX ?? x;
                    List<double[]> my = iterY ?? y;
                    List<double[]> myp = iterYp ?? yp;
                    double[][] z = LagValues(t1, null, lags, null, history, mx, my, myp);
                    stages[1] = dde(t1, Combine(state, stages, b, 0, proposed), z);
                    z = LagValues(t2, null, lags, null, history, mx, my, myp);
                    stages[2] = dde(t2, Combine(state, stages, b, 1, proposed), z);
                    double[] candidate = Combine(state, stages, b, 2, proposed);
                    z = LagValues(tnew, null, lags, null, history, mx, my, myp);
                    stages[3] = dde(tnew, candidate, z);
                    evaluations += 3;
                    ynew = candidate;
                    if (iterations > 1)
                    {
                        if (iteration > 1)
                        {
                            double errit = normControl
                                ? Norm(Difference(ynew, previousY!))
                                    / Math.Max(Math.Max(normY, Norm(ynew)), threshold[0])
                                : WeightedDifference(ynew, previousY!, state, threshold);
                            if (errit <= 0.1 * rtol)
                            {
                                break;
                            }
                        }

                        // The tentative solution at the end of this step joins the history the next
                        // iteration reads, which is how a step longer than a lag is taken at all.
                        iterX = [.. x, tnew];
                        iterY = [.. y, ynew];
                        iterYp = [.. yp, stages[3]];
                        previousY = ynew;
                        iterationFailed = iteration == iterations;
                    }
                }

                if (iterationFailed)
                {
                    failed++;
                    if (h <= hmin)
                    {
                        options.Warn?.Invoke(
                            $"Failure at t={t:E6}.  Unable to meet integration tolerances without reducing the step size below the smallest value allowed ({hmin:E6}) at time t.");
                        return Finish(Dde23Name, history, x, y, yp, reported, result, events, output,
                            steps, failed, evaluations, t, options);
                    }

                    h = 0.5 * h;
                    if (h < 2 * minlag)
                    {
                        h = minlag;
                    }

                    hitDiscontinuity = false;
                    continue;
                }

                error = h * ErrorNorm(stages, e, state, ynew, threshold, normControl, normY);
                if (error <= rtol)
                {
                    break;
                }

                failed++;
                if (h <= hmin)
                {
                    options.Warn?.Invoke(
                        $"Failure at t={t:E6}.  Unable to meet integration tolerances without reducing the step size below the smallest value allowed ({hmin:E6}) at time t.");
                    return Finish(Dde23Name, history, x, y, yp, reported, result, events, output,
                        steps, failed, evaluations, t, options);
                }

                if (noFailed)
                {
                    noFailed = false;
                    h = Math.Max(hmin, h * Math.Max(0.5, 0.8 * Math.Pow(rtol / error, Power)));
                }
                else
                {
                    h = Math.Max(hmin, 0.5 * h);
                }

                hitDiscontinuity = false;
            }

            steps++;
            double[] slope = stages[3];
            if (events is not null)
            {
                List<double> ex = [.. x, tnew];
                List<double[]> ey = [.. y, ynew];
                List<double[]> eyp = [.. yp, slope];
                events.Reads((tt, _) => LagValues(tt, null, lags, null, history, ex, ey, eyp));
                double[] left = stages[0];
                (bool stop, double at, double[] found) = events.Locate(t, state, tnew, ynew,
                    time => BoundaryValueSolvers.Hermite3(time, t, state, tnew, ynew, left, slope).Value, t0);
                if (stop)
                {
                    slope = BoundaryValueSolvers.Hermite3(at, t, state, tnew, ynew, left, slope).Slope;
                    tnew = at;
                    ynew = found;
                    done = true;
                }
            }

            x.Add(tnew);
            y.Add(ynew);
            yp.Add(slope);
            output.AfterStep(t, state, tnew, ynew, stages[0], slope, done);
            lastTime = tnew;

            if (!done && hitDiscontinuity)
            {
                nextDiscontinuity++;
                done = nextDiscontinuity >= stepped.Count;
            }

            if (done)
            {
                break;
            }

            t = tnew;
            state = ynew;
            if (normControl)
            {
                normY = Norm(state);
            }

            stages[0] = slope;
            if (noFailed)
            {
                double shrink = 1.25 * Math.Pow(error / rtol, Power);
                h = shrink > 0.2 ? h / shrink : 5 * h;
                h = Math.Min(Math.Max(hmin, h), hmax);
            }
        }

        return Finish(Dde23Name, history, x, y, yp, reported, result, events, output,
            steps, failed, evaluations, lastTime, options);
    }

    // --- ddesd and ddensd -------------------------------------------------------------------------

    /// <summary>Integrates a system of delay equations whose delays may depend on time and state.</summary>
    public static DdeSolution Ddesd(DdeFunction dde, double[]? lags, DdeDelayFunction? delays,
        DdeHistory history, double[] tspan, DdeOptions options) =>
        Ddesd(DdesdName, dde, lags, delays, history, tspan, options);

    private static DdeSolution Ddesd(string name, DdeFunction dde, double[]? lags, DdeDelayFunction? delays,
        DdeHistory history, double[] tspan, DdeOptions options)
    {
        double t0 = tspan[0];
        double tfinal = tspan[^1];
        if (tfinal <= t0)
        {
            throw new BvpException($"MATLAB:{name}:TspandEndLTtspan1",
                "The final time must be greater than the initial time.");
        }

        if (options.Jumps is { Length: > 0 })
        {
            throw new BvpException($"MATLAB:{name}:JumpsOptionNotAvailable",
                "The 'Jumps' option is available only in DDE23.");
        }

        double[] y0;
        if (history.Previous is { } previous)
        {
            if (previous.X[^1] != t0)
            {
                throw new BvpException($"MATLAB:{name}:NotContinueFromHistoryEnd",
                    "The solution structure must end at the initial time.");
            }

            y0 = previous.Y[^1];
        }
        else
        {
            y0 = history.Before(t0);
        }

        if (options.InitialY is { Length: > 0 } given)
        {
            y0 = given;
        }

        y0 = (double[])y0.Clone();
        int neq = y0.Length;

        const double Power = 0.25;
        double[] sample = [0.5 - (Math.Sqrt(3) / 6), 0.5 + (Math.Sqrt(3) / 6)];

        var x = new List<double> { t0 };
        var y = new List<double[]> { y0 };
        var yp = new List<double[]>();
        double[][] z0 = LagValues(t0, y0, lags, delays, history, x, y, yp);
        double[] f0 = dde(t0, y0, z0);
        int evaluations = 1;
        yp.Add(f0);

        double rtol = options.RelativeTolerance;
        if (rtol < 100 * Epsilon)
        {
            rtol = 100 * Epsilon;
            options.Warn?.Invoke($"RelTol has been increased to {rtol:G}.");
        }

        double[] atol = options.AbsoluteTolerance is { Length: > 0 } tolerance ? tolerance : [1e-6];
        bool normControl = options.NormControl;
        double[] threshold = new double[normControl ? 1 : neq];
        for (int i = 0; i < threshold.Length; i++)
        {
            threshold[i] = (atol.Length == 1 ? atol[0] : atol[i]) / rtol;
        }

        double span = tfinal - t0;
        double hmax0 = Math.Min(span, options.MaxStep ?? (0.1 * span));
        double userhmin = options.MinStep is { } floorStep ? Math.Min(span, floorStep) : 0;
        double? htry = options.InitialStep;

        double tiny = 16 * Spacing(t0);
        double hmin = Math.Max(tiny, userhmin);
        double hmax = Math.Max(tiny, hmax0);
        double normY = normControl ? Norm(y0) : 0;
        double h;
        if (htry is null)
        {
            h = Math.Min(hmax, span);
            double rh = normControl
                ? Norm(f0) / Math.Max(normY, threshold[0]) / (0.8 * Math.Pow(rtol, Power))
                : WeightedNorm(f0, y0, threshold) / (0.8 * Math.Pow(rtol, Power));
            if (h * rh > 1)
            {
                h = 1 / rh;
            }

            h = Math.Max(h, hmin);
        }
        else
        {
            h = Math.Min(hmax, Math.Max(hmin, htry.Value));
        }

        var result = new OdeResult { Solver = name };
        var output = new DdeOutput(options, t0, tfinal, tspan, y0, neq);
        DdeEvents? events = options.Events is null
            ? null
            : new DdeEvents(options.Events, t0, y0, z0, result);

        double t = t0;
        double[] state = y0;
        double[] slope = f0;
        double[] slopeNew = f0;
        double[] ynew = y0;
        var stages = new double[4][];
        int steps = 0;
        int failed = 0;
        bool first = true;
        bool done = false;
        double lastTime = t0;

        while (!done)
        {
            tiny = 16 * Spacing(t);
            hmin = Math.Max(tiny, userhmin);
            hmax = Math.Max(tiny, hmax0);
            h = Math.Min(hmax, Math.Max(hmin, h));

            bool laststep = false;
            double distance = tfinal - t;
            if (Math.Min(1.1 * h, hmax) >= distance)
            {
                h = distance;
                laststep = true;
            }
            else if (2 * h >= distance)
            {
                h = distance / 2;
            }

            bool noFailed = true;
            double error;
            while (true)
            {
                double tnew = laststep ? tfinal : t + h;

                // The step's own end is guessed before it is taken, so that a delayed argument
                // landing inside the step has something to read. The first step extends the slope
                // linearly; afterwards the guess is the Hermite cubic read at the end of the step,
                // which is the last value the previous attempt — or the previous step — reached.
                double[] predicted;
                double[] predictedSlope;
                if (first)
                {
                    predicted = new double[neq];
                    for (int i = 0; i < neq; i++)
                    {
                        predicted[i] = state[i] + (h * f0[i]);
                    }

                    predictedSlope = f0;
                    slope = f0;
                }
                else
                {
                    predicted = ynew;
                    predictedSlope = slopeNew;
                }

                x.Add(tnew);
                y.Add(predicted);
                yp.Add(predictedSlope);

                double[] hB = [0, 0.5 * h, 0.5 * h, h];
                double[] hC = [h / 6, h / 3, h / 3, h / 6];
                stages[0] = slope;
                bool implicitStep = false;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int j = 1; j < 4; j++)
                    {
                        var temporary = new double[neq];
                        for (int i = 0; i < neq; i++)
                        {
                            temporary[i] = state[i] + (hB[j] * stages[j - 1][i]);
                        }

                        double[][] z = LagValues(t + hB[j], temporary, lags, delays, history, x, y, yp,
                            out bool touched);
                        stages[j] = dde(t + hB[j], temporary, z);
                        implicitStep |= touched;
                    }

                    ynew = new double[neq];
                    for (int i = 0; i < neq; i++)
                    {
                        double sum = 0;
                        for (int j = 0; j < 4; j++)
                        {
                            sum += hC[j] * stages[j][i];
                        }

                        ynew[i] = state[i] + sum;
                    }

                    double[][] zEnd = LagValues(tnew, ynew, lags, delays, history, x, y, yp, out bool endTouched);
                    slopeNew = dde(tnew, ynew, zEnd);
                    implicitStep |= endTouched;
                    y[^1] = ynew;
                    yp[^1] = slopeNew;
                    evaluations += 4;
                    if (!implicitStep)
                    {
                        break;
                    }
                }

                // The residual of the Hermite cubic against the equation at two interior points,
                // scaled by the constant that turns that sample into an error estimate.
                error = 0;
                double[] weight = new double[threshold.Length];
                if (normControl)
                {
                    weight[0] = Math.Max(Math.Max(normY, Norm(ynew)), threshold[0]);
                }
                else
                {
                    for (int i = 0; i < neq; i++)
                    {
                        weight[i] = Math.Max(Math.Max(Math.Abs(state[i]), Math.Abs(ynew[i])), threshold[i]);
                    }
                }

                for (int s = 0; s < 2; s++)
                {
                    double xm = t + (h * sample[s]);
                    (double[] ym, double[] ypm) =
                        BoundaryValueSolvers.Hermite3(xm, t, state, tnew, ynew, slope, slopeNew);
                    double[][] z = LagValues(xm, ym, lags, delays, history, x, y, yp);
                    double[] value = dde(xm, ym, z);
                    evaluations++;
                    double measure;
                    if (normControl)
                    {
                        var residual = new double[neq];
                        for (int i = 0; i < neq; i++)
                        {
                            residual[i] = ypm[i] - value[i];
                        }

                        measure = h * Norm(residual) / weight[0];
                    }
                    else
                    {
                        measure = 0;
                        for (int i = 0; i < neq; i++)
                        {
                            measure = Math.Max(measure, Math.Abs((ypm[i] - value[i]) / weight[i]));
                        }

                        measure *= h;
                    }

                    error = 2.1342 * Math.Max(error, measure);
                    if (error > rtol && !noFailed)
                    {
                        break;
                    }
                }

                if (error <= rtol)
                {
                    done = laststep;
                    lastTime = tnew;
                    break;
                }

                failed++;
                x.RemoveAt(x.Count - 1);
                y.RemoveAt(y.Count - 1);
                yp.RemoveAt(yp.Count - 1);
                if (h <= hmin)
                {
                    options.Warn?.Invoke(
                        $"Failure at t={t:E6}.  Unable to meet integration tolerances without reducing the step size below the smallest value allowed ({hmin:E6}) at time t.");
                    return Finish(name, history, x, y, yp, null, result, events, output,
                        steps, failed, evaluations, t, options);
                }

                h = noFailed ? h * Math.Max(0.5, 0.8 * Math.Pow(rtol / error, Power)) : h * 0.5;
                h = Math.Max(hmin, h);
                noFailed = false;
                laststep = false;
            }

            steps++;
            double stepStart = t;
            double[] stepState = state;
            double tEnd = x[^1];
            if (events is not null)
            {
                double[] leftSlope = slope;
                double[] rightSlope = slopeNew;
                events.Reads((tt, yy) => LagValues(tt, yy, lags, delays, history, x, y, yp));
                (bool stop, double at, double[] found) = events.Locate(stepStart, stepState, tEnd, ynew,
                    time => BoundaryValueSolvers.Hermite3(time, stepStart, stepState, tEnd, ynew,
                        leftSlope, rightSlope).Value, t0);
                if (stop)
                {
                    (double[] value, double[] derivative) = BoundaryValueSolvers.Hermite3(at, stepStart,
                        stepState, tEnd, ynew, leftSlope, rightSlope);
                    x[^1] = at;
                    y[^1] = value;
                    yp[^1] = derivative;
                    tEnd = at;
                    ynew = value;
                    slopeNew = derivative;
                    lastTime = at;
                    done = true;
                    _ = found;
                }
            }

            output.AfterStep(stepStart, stepState, tEnd, ynew, slope, slopeNew, done);

            if (done)
            {
                break;
            }

            first = false;
            t = tEnd;
            state = ynew;
            slope = slopeNew;
            if (normControl)
            {
                normY = Norm(state);
            }

            if (noFailed)
            {
                h /= Math.Max(0.5, 1.25 * Math.Pow(error / rtol, Power));
                h = Math.Min(Math.Max(hmin, h), hmax);
            }
        }

        return Finish(name, history, x, y, yp, null, result, events, output,
            steps, failed, evaluations, lastTime, options);
    }

    /// <summary>
    /// Integrates a neutral problem by replacing every delayed derivative with the difference
    /// quotient of the solution over a short interval behind the delayed argument, and handing the
    /// resulting retarded problem to <c>ddesd</c>.
    /// </summary>
    public static DdeSolution Ddensd(
        Func<double, double[], double[][], double[][], double[]> dde,
        double[]? solutionLags, DdeDelayFunction? solutionDelays,
        double[]? derivativeLags, DdeDelayFunction? derivativeDelays,
        DdeHistory history, (double[] Y0, double[] Yp0)? initialPair, double[] tspan, DdeOptions options,
        Func<double, double[], double[][], double[][], OdeEventReading>? neutralEvents)
    {
        double a = tspan[0];
        double delta = Math.Sqrt(Epsilon);
        double minimumChange = delta * Math.Max(Math.Abs(tspan[0]), Math.Abs(tspan[^1]));

        bool ivp = initialPair is not null;
        double[] initialY = initialPair?.Y0 ?? [];
        double[] initialSlope = initialPair?.Yp0 ?? [];
        if (history.Previous is { InitialValueProblem: true, InitialPair: { } carried })
        {
            ivp = true;
            initialY = carried.Y0;
            initialSlope = carried.Yp0;
            a = history.Previous.X[0];
        }

        double[] SolutionDelays(double t, double[] y) =>
            solutionDelays is { } f ? f(t, y) : solutionLags is { } lags ? lags.Select(l => t - l).ToArray() : [];
        double[] DerivativeDelays(double t, double[] y) =>
            derivativeDelays is { } f ? f(t, y) : derivativeLags is { } lags ? lags.Select(l => t - l).ToArray() : [];

        double[] probeY = ivp ? initialY : history.Before(a);
        int ny = SolutionDelays(a, probeY).Length;
        int nyp = DerivativeDelays(a, probeY).Length;
        double t0 = a;

        double[] All(double t, double[] y)
        {
            double[] d = SolutionDelays(t, y);
            double[] dp = DerivativeDelays(t, y);
            foreach (double value in dp)
            {
                if (value > t)
                {
                    throw new BvpException("MATLAB:ddensd:DELYPGreaterThanT",
                        $"At t = {t:G}, a derivative delay is greater than t.");
                }
            }

            if (ivp)
            {
                foreach (double value in d)
                {
                    if (value < t0)
                    {
                        throw new BvpException("MATLAB:ddensd:DELYLessThanT0",
                            $"At t = {t:G}, a solution delay is less than the initial time.");
                    }
                }

                foreach (double value in dp)
                {
                    if (value < t0)
                    {
                        throw new BvpException("MATLAB:ddensd:DELYPLessThanT0",
                            $"At t = {t:G}, a derivative delay is less than the initial time.");
                    }

                    if (value == t && t > t0)
                    {
                        throw new BvpException("MATLAB:ddensd:IVPDELYPEqualT",
                            $"At t = {t:G}, a derivative delay equals t.");
                    }
                }
            }
            else
            {
                foreach (double value in dp)
                {
                    if (value == t)
                    {
                        throw new BvpException("MATLAB:ddensd:DELYPEqualT",
                            $"At t = {t:G}, a derivative delay equals t.");
                    }
                }
            }

            var all = new double[d.Length + (2 * dp.Length)];
            Array.Copy(d, all, d.Length);
            for (int k = 0; k < dp.Length; k++)
            {
                all[d.Length + k] = dp[k];
                all[d.Length + dp.Length + k] = dp[k] - Math.Max(delta * Math.Abs(dp[k]), minimumChange);
            }

            return all;
        }

        double[][] DelayedSlopes(double t, double[] y, double[][] z)
        {
            double[] all = All(t, y);
            var answer = new double[nyp][];
            for (int k = 0; k < nyp; k++)
            {
                double step = all[ny + k] - all[ny + nyp + k];
                var column = new double[y.Length];
                if (ivp && all[ny + nyp + k] <= t0)
                {
                    Array.Copy(initialSlope, column, initialSlope.Length);
                }
                else
                {
                    for (int i = 0; i < y.Length; i++)
                    {
                        column[i] = (z[ny + k][i] - z[ny + nyp + k][i]) / step;
                    }
                }

                answer[k] = column;
            }

            return answer;
        }

        DdeFunction retarded = (t, y, z) => dde(t, y, z[..ny], DelayedSlopes(t, y, z));
        DdeEventFunction? events = neutralEvents is { } watch
            ? (t, y, z) => watch(t, y, z[..ny], DelayedSlopes(t, y, z))
            : null;

        double rtol = options.RelativeTolerance;
        if (rtol < 1e-5)
        {
            rtol = 1e-5;
            options.Warn?.Invoke($"RelTol has been increased to {rtol:G}.");
        }

        DdeHistory inner = ivp && history.Previous is null
            ? new DdeHistory { Constant = initialY }
            : history;

        DdeSolution answer = Ddesd(DdensdName, retarded, null, (t, y) => All(t, y), inner, tspan,
            options with { RelativeTolerance = rtol, Events = events });

        return new DdeSolution
        {
            Solver = DdensdName,
            History = ivp ? new DdeHistory { Constant = initialY } : answer.History,
            X = answer.X,
            Y = answer.Y,
            Yp = answer.Yp,
            HadEvents = answer.HadEvents,
            EventTimes = answer.EventTimes,
            EventStates = answer.EventStates,
            EventIndices = answer.EventIndices,
            Steps = answer.Steps,
            Failed = answer.Failed,
            Evaluations = answer.Evaluations,
            FinalTime = answer.FinalTime,
            InitialValueProblem = ivp,
            InitialPair = ivp ? (initialY, initialSlope) : null,
        };
    }

    // --- shared plumbing --------------------------------------------------------------------------

    /// <summary>Assembles the answer, joining the run onto the solution it continues when there is one.</summary>
    private static DdeSolution Finish(string solver, DdeHistory history, List<double> x, List<double[]> y,
        List<double[]> yp, double[]? discontinuities, OdeResult result, DdeEvents? events, DdeOutput output,
        int steps, int failed, int evaluations, double finalTime, DdeOptions options)
    {
        output.Finish();
        _ = events;
        if (options.Stats)
        {
            options.Print?.Invoke($"{steps} successful steps");
            options.Print?.Invoke($"{failed} failed attempts");
            options.Print?.Invoke($"{evaluations} function evaluations");
        }

        double[] times = [.. x];
        double[][] states = [.. y];
        double[][] slopes = [.. yp];
        DdeHistory reported = history;
        if (history.Previous is { } previous)
        {
            times = [.. previous.X, .. times];
            states = [.. previous.Y, .. states];
            slopes = [.. previous.Yp, .. slopes];
            reported = previous.History;
        }

        return new DdeSolution
        {
            Solver = solver,
            History = reported,
            X = times,
            Y = states,
            Yp = slopes,
            Discont = discontinuities,
            HadEvents = result.HadEvents,
            EventTimes = [.. result.EventTimes],
            EventStates = [.. result.EventStates],
            EventIndices = [.. result.EventIndices],
            Steps = steps,
            Failed = failed,
            Evaluations = evaluations,
            FinalTime = finalTime,
        };
    }

    /// <summary>The event machinery, with the delayed arguments read at the point being tested.</summary>
    private sealed class DdeEvents
    {
        private readonly OdeEvents _events;
        private Func<double, double[], double[][]> _lookup;

        public DdeEvents(DdeEventFunction events, double t0, double[] y0, double[][] z0, OdeResult result)
        {
            _lookup = (_, _) => z0;
            _events = new OdeEvents((t, state) => events(t, state, _lookup(t, state)), t0, y0, result);
        }

        /// <summary>How the delayed arguments are found at a point inside the step being searched.</summary>
        public void Reads(Func<double, double[], double[][]> lookup) => _lookup = lookup;

        public (bool Stop, double At, double[] State) Locate(double t, double[] y, double tnew, double[] ynew,
            Func<double, double[]> interpolate, double t0) =>
            _events.Locate(t, y, tnew, ynew, interpolate, t0);
    }

    /// <summary>The output function, with MATLAB's three ways of choosing what it sees.</summary>
    private sealed class DdeOutput
    {
        private readonly OdeOutputFunction? _function;
        private readonly int[]? _selection;
        private readonly double[] _tspan;
        private readonly int _mode;
        private readonly int _refine;
        private int _next = 1;

        public DdeOutput(DdeOptions options, double t0, double tfinal, double[] tspan, double[] y0, int neq)
        {
            _function = options.OutputFunction;
            _selection = options.OutputSelection;
            _tspan = tspan;
            _refine = Math.Max(1, options.Refine ?? 1);
            _mode = tspan.Length > 2 ? 1 : _refine <= 1 ? 2 : 3;
            _ = neq;
            _function?.Invoke(OdeOutputPhase.Init, [t0, tfinal], [Select(y0)]);
        }

        public void AfterStep(double t, double[] y, double tnew, double[] ynew, double[] slope,
            double[] slopeNew, bool stopped)
        {
            if (_function is null)
            {
                return;
            }

            var times = new List<double>();
            var states = new List<double[]>();
            switch (_mode)
            {
                case 2:
                    times.Add(tnew);
                    states.Add(Select(ynew));
                    break;

                case 3:
                    for (int i = 1; i < _refine; i++)
                    {
                        double at = t + ((tnew - t) * i / _refine);
                        times.Add(at);
                        states.Add(Select(BoundaryValueSolvers.Hermite3(at, t, y, tnew, ynew, slope, slopeNew).Value));
                    }

                    times.Add(tnew);
                    states.Add(Select(ynew));
                    break;

                default:
                    while (_next < _tspan.Length)
                    {
                        if (tnew < _tspan[_next])
                        {
                            if (stopped)
                            {
                                times.Add(tnew);
                                states.Add(Select(ynew));
                            }

                            break;
                        }

                        times.Add(_tspan[_next]);
                        states.Add(Select(_tspan[_next] == tnew
                            ? ynew
                            : BoundaryValueSolvers.Hermite3(_tspan[_next], t, y, tnew, ynew, slope, slopeNew).Value));
                        _next++;
                    }

                    break;
            }

            if (times.Count > 0)
            {
                _function(OdeOutputPhase.Step, [.. times], [.. states]);
            }
        }

        public void Finish() => _function?.Invoke(OdeOutputPhase.Done, [], []);

        private double[] Select(double[] state)
        {
            if (_selection is null)
            {
                return state;
            }

            var chosen = new double[_selection.Length];
            for (int i = 0; i < _selection.Length; i++)
            {
                chosen[i] = state[_selection[i]];
            }

            return chosen;
        }
    }

    private static double[][] LagValues(double tnow, double[]? ynow, double[]? lags, DdeDelayFunction? delays,
        DdeHistory history, IReadOnlyList<double> x, IReadOnlyList<double[]> y, IReadOnlyList<double[]> yp) =>
        LagValues(tnow, ynow, lags, delays, history, x, y, yp, out _);

    private static double[][] LagValues(double tnow, double[]? ynow, double[]? lags, DdeDelayFunction? delays,
        DdeHistory history, IReadOnlyList<double> x, IReadOnlyList<double[]> y, IReadOnlyList<double[]> yp,
        out bool insideStep)
    {
        insideStep = false;
        double[] arguments;
        if (delays is { } function)
        {
            arguments = function(tnow, ynow!);
            for (int j = 0; j < arguments.Length; j++)
            {
                arguments[j] = Math.Min(tnow, arguments[j]);
            }
        }
        else
        {
            double[] given = lags ?? [];
            arguments = new double[given.Length];
            for (int j = 0; j < given.Length; j++)
            {
                arguments[j] = tnow - given[j];
                if (ynow is not null)
                {
                    arguments[j] = Math.Min(tnow, arguments[j]);
                }
            }
        }

        if (arguments.Length == 0)
        {
            return [];
        }

        IReadOnlyList<double> mesh = x;
        IReadOnlyList<double[]> values = y;
        IReadOnlyList<double[]> slopes = yp;
        if (mesh.Count > 1)
        {
            foreach (double at in arguments)
            {
                insideStep |= at > mesh[^2];
            }
        }

        if (history.Previous is { } previous && mesh.Count > 0)
        {
            bool below = false;
            bool reachable = false;
            foreach (double at in arguments)
            {
                below |= at < mesh[0];
                reachable |= at >= previous.X[0];
            }

            if (below && reachable)
            {
                mesh = [.. previous.X, .. mesh];
                values = [.. previous.Y, .. values];
                slopes = [.. previous.Yp, .. slopes];
            }
        }

        int neq = values[0].Length;
        var z = new double[arguments.Length][];
        for (int j = 0; j < arguments.Length; j++)
        {
            double at = arguments[j];
            if (at < mesh[0])
            {
                double[] before = history.Before(at);
                z[j] = (double[])before.Clone();
                continue;
            }

            if (at == mesh[0])
            {
                z[j] = (double[])values[0].Clone();
                continue;
            }

            if (at == mesh[^1])
            {
                z[j] = (double[])values[^1].Clone();
                continue;
            }

            int bottom = Bracket(mesh, at);
            double h = mesh[bottom + 1] - mesh[bottom];
            double s = (at - mesh[bottom]) / h;
            double s2 = s * s;
            double s3 = s * s2;
            var column = new double[neq];
            for (int r = 0; r < neq; r++)
            {
                double gradient = (values[bottom + 1][r] - values[bottom][r]) / h;
                double c = (3 * gradient) - (2 * slopes[bottom][r]) - slopes[bottom + 1][r];
                double d = slopes[bottom][r] + slopes[bottom + 1][r] - (2 * gradient);
                column[r] = values[bottom][r] + ((h * d * s3) + (h * c * s2) + (h * slopes[bottom][r] * s));
            }

            z[j] = column;
        }

        return z;
    }

    /// <summary>The index of the mesh interval containing <paramref name="at"/>, extrapolating past the end.</summary>
    private static int Bracket(IReadOnlyList<double> mesh, double at)
    {
        int low = 0;
        int high = mesh.Count - 1;
        if (at >= mesh[^1])
        {
            return mesh.Count - 2;
        }

        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (mesh[middle] <= at)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static double[] Combine(double[] y, double[][] stages, double[][] b, int column, double h)
    {
        var answer = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            double sum = 0;
            for (int j = 0; j <= column + 1 && j < 4; j++)
            {
                if (stages[j] is not null && b[j][column] != 0)
                {
                    sum += h * b[j][column] * stages[j][i];
                }
            }

            answer[i] = y[i] + sum;
        }

        return answer;
    }

    private static double ErrorNorm(double[][] stages, double[] e, double[] y, double[] ynew,
        double[] threshold, bool normControl, double normY)
    {
        int n = y.Length;
        var estimate = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < 4; j++)
            {
                sum += e[j] * stages[j][i];
            }

            estimate[i] = sum;
        }

        if (normControl)
        {
            return Norm(estimate) / Math.Max(Math.Max(normY, Norm(ynew)), threshold[0]);
        }

        double largest = 0;
        for (int i = 0; i < n; i++)
        {
            double weight = Math.Max(Math.Max(Math.Abs(y[i]), Math.Abs(ynew[i])), threshold[i]);
            largest = Math.Max(largest, Math.Abs(estimate[i] / weight));
        }

        return largest;
    }

    private static List<double> Distinct(List<double> sorted)
    {
        var kept = new List<double> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i] - sorted[i - 1]) > 10 * Spacing(sorted[i - 1]))
            {
                kept.Add(sorted[i]);
            }
        }

        return kept;
    }

    private static double[] Difference(double[] a, double[] b)
    {
        var answer = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            answer[i] = a[i] - b[i];
        }

        return answer;
    }

    private static double WeightedDifference(double[] ynew, double[] previous, double[] y, double[] threshold)
    {
        double largest = 0;
        for (int i = 0; i < ynew.Length; i++)
        {
            double weight = Math.Max(Math.Max(Math.Abs(y[i]), Math.Abs(ynew[i])), threshold[i]);
            largest = Math.Max(largest, Math.Abs((ynew[i] - previous[i]) / weight));
        }

        return largest;
    }

    private static double WeightedNorm(double[] f, double[] y, double[] threshold)
    {
        double largest = 0;
        for (int i = 0; i < f.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(f[i] / Math.Max(Math.Abs(y[i]), threshold[i])));
        }

        return largest;
    }

    private static double Norm(double[] v)
    {
        double sum = 0;
        foreach (double value in v)
        {
            sum += value * value;
        }

        return Math.Sqrt(sum);
    }

    private static double Spacing(double t)
    {
        double magnitude = Math.Abs(t);
        if (magnitude == 0)
        {
            return 4.9406564584124654e-324;
        }

        long bits = BitConverter.DoubleToInt64Bits(magnitude);
        return BitConverter.Int64BitsToDouble(bits + 1) - magnitude;
    }
}
