using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>A refusal of the arguments to a solver, carrying MATLAB's identifier for it.</summary>
public sealed class OdeArgumentException : ArgumentException
{
    /// <summary>Creates the refusal.</summary>
    public OdeArgumentException(string identifier, string message)
        : base(message)
    {
        Identifier = identifier;
    }

    /// <summary>MATLAB's identifier, such as <c>MATLAB:odearguments:SizeTspan</c>.</summary>
    public string Identifier { get; }
}

/// <summary>
/// What every solver works out before its first step — MATLAB's <c>odearguments</c>,
/// <c>odemass</c> and <c>odenonnegative</c> in one place: the span, the tolerances as the
/// threshold the error is measured against, the step limits, and the derivative with the mass
/// matrix and the non-negativity constraint folded into it.
/// </summary>
internal sealed class OdeSetup
{
    private OdeSetup(int n, double[] tspan)
    {
        N = n;
        Tspan = tspan;
    }

    /// <summary>How many state variables.</summary>
    public int N { get; }

    /// <summary>The times asked for, as given; two entries is a span, more names the output times.</summary>
    public double[] Tspan { get; }

    /// <summary>Where the integration starts.</summary>
    public double T0 { get; private set; }

    /// <summary>Where it ends.</summary>
    public double TFinal { get; private set; }

    /// <summary>+1 forwards, −1 backwards.</summary>
    public double Direction { get; private set; }

    /// <summary>The gap to the second time asked for — what the first step is limited by.</summary>
    public double SpanStep { get; private set; }

    /// <summary>The relative tolerance, raised to a hundred ulps if it was below that.</summary>
    public double RelativeTolerance { get; private set; }

    /// <summary>The scale a component is measured against once it is too small to be measured against itself: <c>AbsTol / RelTol</c>, per component.</summary>
    public double[] Threshold { get; private set; } = [];

    /// <summary>Whether the error is measured in the 2-norm of the whole state.</summary>
    public bool NormControl { get; private set; }

    /// <summary>The 2-norm of the initial state, when <see cref="NormControl"/>.</summary>
    public double InitialNorm { get; private set; }

    /// <summary>The caller's ceiling on the step, or the default tenth of the span.</summary>
    public double LargestStep { get; private set; }

    /// <summary>The caller's floor on the step, or zero.</summary>
    public double SmallestStep { get; private set; }

    /// <summary>The first step the caller asked for, or null to let the slope choose.</summary>
    public double? FirstStep { get; private set; }

    /// <summary>The derivative, with the mass matrix and non-negativity folded in.</summary>
    public OdeFunction Function { get; private set; } = null!;

    /// <summary>The slope at the start, already evaluated.</summary>
    public double[] InitialSlope { get; private set; } = [];

    /// <summary>How many times the derivative was called getting here.</summary>
    public int Evaluations { get; private set; }

    /// <summary>The components held non-negative, 0-based, or null.</summary>
    public int[]? NonNegative { get; private set; }

    /// <summary>The threshold of each non-negative component, in that order.</summary>
    public double[]? NonNegativeThreshold { get; private set; }

    /// <summary>Sixteen ulps of <paramref name="t"/>: the smallest step that still moves the clock there.</summary>
    public static double TinyStep(double t)
    {
        double magnitude = Math.Abs(t);
        return 16 * (magnitude == 0 ? double.Epsilon : Math.BitIncrement(magnitude) - magnitude);
    }

    /// <summary>Works everything out, refusing what MATLAB refuses with the identifier it uses.</summary>
    public static OdeSetup Create(string solver, OdeFunction derivative, IReadOnlyList<double> tspan,
        double[] y0, OdeOptions options)
    {
        if (tspan.Count == 0 || y0.Length == 0)
        {
            throw new OdeArgumentException("MATLAB:odearguments:TspanOrY0NotSupplied",
                $"When the first argument to {solver} is a function handle, the tspan and y0 arguments must be supplied.");
        }

        if (tspan.Count < 2)
        {
            throw new OdeArgumentException("MATLAB:odearguments:SizeTspan",
                $"When the first argument to {solver} is a function handle, the tspan argument must have at least two elements.");
        }

        var setup = new OdeSetup(y0.Length, [.. tspan]);
        foreach (double time in tspan)
        {
            if (double.IsNaN(time))
            {
                throw new OdeArgumentException("MATLAB:odearguments:TspanNaNValues",
                    "The entries in tspan must not contain NaN values.");
            }
        }

        setup.T0 = tspan[0];
        setup.TFinal = tspan[^1];
        setup.SpanStep = Math.Abs(tspan[1] - setup.T0);
        if (setup.T0 == setup.TFinal)
        {
            throw new OdeArgumentException("MATLAB:odearguments:TspanEndpointsNotDistinct",
                "The last entry in tspan must be different from the first entry.");
        }

        setup.Direction = Math.Sign(setup.TFinal - setup.T0);
        for (int i = 1; i < tspan.Count; i++)
        {
            if (setup.Direction * (tspan[i] - tspan[i - 1]) <= 0)
            {
                throw new OdeArgumentException("MATLAB:odearguments:TspanNotMonotonic",
                    "The entries in tspan must strictly increase or decrease.");
            }
        }

        int n = y0.Length;
        double[] f0 = derivative(setup.T0, y0);
        setup.Evaluations = 1;
        if (f0.Length != n)
        {
            throw new OdeArgumentException("MATLAB:odearguments:SizeIC",
                $"{solver}: the derivative returned a vector of length {f0.Length}, but the length of the initial conditions vector is {n}. "
                + "The vector returned by the derivative and the initial conditions vector must have the same number of elements.");
        }

        double rtol = options.RelativeTolerance;
        if (rtol <= 0 || double.IsNaN(rtol))
        {
            throw new OdeArgumentException("MATLAB:odearguments:RelTolNotPosScalar",
                "RelTol must be a positive scalar.");
        }

        const double epsilon = 2.220446049250313e-16;
        if (rtol < 100 * epsilon)
        {
            rtol = 100 * epsilon;
            options.Warn?.Invoke($"RelTol has been increased to {rtol:G6}.");
        }

        setup.RelativeTolerance = rtol;

        double[] atol = options.AbsoluteTolerance ?? [1e-6];
        foreach (double a in atol)
        {
            if (a <= 0 || double.IsNaN(a))
            {
                throw new OdeArgumentException("MATLAB:odearguments:AbsTolNotPos", "AbsTol must be positive.");
            }
        }

        setup.NormControl = options.NormControl;
        if (setup.NormControl)
        {
            if (atol.Length != 1)
            {
                throw new OdeArgumentException("MATLAB:odearguments:NonScalarAbsTol",
                    "Norm control requires AbsTol to be a scalar.");
            }

            setup.InitialNorm = NormEstimators.VectorNorm(y0);
        }
        else if (atol.Length != 1 && atol.Length != n)
        {
            throw new OdeArgumentException("MATLAB:odearguments:SizeAbsTolInconsistent",
                $"Sizes of AbsTol ({atol.Length}) and initial conditions ({n}) are inconsistent.");
        }

        var threshold = new double[n];
        for (int i = 0; i < n; i++)
        {
            threshold[i] = atol[atol.Length == 1 ? 0 : i] / rtol;
        }

        setup.Threshold = threshold;

        // Sixteen ulps of the larger endpoint: a span so short that a tenth of it would not move
        // the clock still gets a step that does.
        double safeLargest = 16 * epsilon * Math.Max(Math.Abs(setup.T0), Math.Abs(setup.TFinal));
        double length = Math.Abs(setup.TFinal - setup.T0);
        bool defaultLargest = options.MaxStep is null;
        double largest;
        if (defaultLargest)
        {
            largest = Math.Max(0.1 * length, safeLargest);
        }
        else
        {
            largest = options.MaxStep!.Value;
            if (largest <= 0)
            {
                throw new OdeArgumentException("MATLAB:odearguments:MaxStepLEzero", "MaxStep must be greater than zero.");
            }

            largest = Math.Min(length, largest);
        }

        double smallest = 0;
        if (options.MinStep is { } minimum)
        {
            if (minimum <= 0)
            {
                throw new OdeArgumentException("MATLAB:odearguments:MinStepLEzero", "MinStep must be greater than zero.");
            }

            smallest = Math.Min(length, minimum);
            if (defaultLargest)
            {
                largest = Math.Max(smallest, largest);
            }
            else if (largest < smallest)
            {
                throw new OdeArgumentException("MATLAB:odearguments:InconsistentMinStep",
                    "MinStep must not be greater than MaxStep.");
            }
        }

        setup.LargestStep = largest;
        setup.SmallestStep = smallest;

        if (options.InitialStep is { } first)
        {
            double tried = Math.Abs(first);
            if (tried <= 0)
            {
                throw new OdeArgumentException("MATLAB:odearguments:InitialStepLEzero",
                    "InitialStep must be greater than zero.");
            }

            setup.FirstStep = tried;
        }
        else if (largest == smallest)
        {
            setup.FirstStep = smallest;
        }

        // The mass matrix goes inside the derivative: M·y' = f becomes y' = M⁻¹·f, factored once
        // for a constant M and per call otherwise. The slope at the start is taken again through
        // the wrapped function, as the reference does, which is why it costs one more evaluation.
        OdeFunction f = derivative;
        if (options.Mass is { } constant)
        {
            if (constant.GetLength(0) != n || constant.GetLength(1) != n)
            {
                throw new OdeArgumentException("MATLAB:odemass:MassSize",
                    $"The mass matrix must be {n}-by-{n}.");
            }

            LuDecomposition lu = LuDecomposition.Factor(constant);
            OdeFunction inner = f;
            f = (t, y) => lu.Solve(inner(t, y));
            f0 = f(setup.T0, y0);
            setup.Evaluations++;
        }
        else if (options.MassFunction is { } massOf)
        {
            OdeFunction inner = f;
            f = (t, y) => LuDecomposition.Factor(massOf(t, y)).Solve(inner(t, y));
            f0 = f(setup.T0, y0);
            setup.Evaluations++;
        }

        if (options.NonNegative is { Length: > 0 } nonNegative)
        {
            foreach (int index in nonNegative)
            {
                if (index < 0 || index >= n)
                {
                    throw new OdeArgumentException("MATLAB:odenonnegative:NonNegativeIndicesInvalid",
                        "Some indices in NonNegative are invalid.");
                }

                if (y0[index] < 0)
                {
                    throw new OdeArgumentException("MATLAB:odenonnegative:NonNegativeViolatedAtT0",
                        "Some elements of the initial conditions are negative for the NonNegative components.");
                }
            }

            var nonNegativeThreshold = new double[nonNegative.Length];
            for (int i = 0; i < nonNegative.Length; i++)
            {
                nonNegativeThreshold[i] = threshold[nonNegative[i]];
            }

            // A component at or below zero is not allowed to head further down: its slope is
            // clipped at zero where it is negative.
            OdeFunction inner = f;
            f = (t, y) =>
            {
                double[] slope = inner(t, y);
                foreach (int index in nonNegative)
                {
                    if (y[index] <= 0 && slope[index] < 0)
                    {
                        slope[index] = 0;
                    }
                }

                return slope;
            };

            f0 = f(setup.T0, y0);
            setup.Evaluations++;
            setup.NonNegative = nonNegative;
            setup.NonNegativeThreshold = nonNegativeThreshold;
        }

        setup.Function = f;
        setup.InitialSlope = f0;
        return setup;
    }

    /// <summary>The largest of <c>|v| / max(|y|, threshold)</c> over the state — the norm the first step and the error test are measured in.</summary>
    public double WeightedInfinityNorm(double[] values, double[] state)
    {
        double largest = 0;
        for (int i = 0; i < values.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(values[i]) / Math.Max(Math.Abs(state[i]), Threshold[i]));
        }

        return largest;
    }
}

/// <summary>
/// Where the points of a run go — MATLAB's three output modes and its output function protocol,
/// shared by every solver. A solver hands in each accepted step with a way to read inside it, and
/// this decides which times to report: the step's own end, <c>Refine</c> points through it, or
/// only the times the caller named.
/// </summary>
internal sealed class OdeOutput
{
    private readonly OdeSetup _setup;
    private readonly OdeOptions _options;
    private readonly OdeResult _result;
    private readonly int _refine;
    private readonly int[]? _selection;
    private int _next = 1;   // the next named time still to be reported

    public OdeOutput(OdeSetup setup, OdeOptions options, int refine, OdeResult result)
    {
        _setup = setup;
        _options = options;
        _result = result;
        _refine = refine;
        _selection = options.OutputSelection;
    }

    /// <summary>Reports the initial point and tells the output function the run is starting.</summary>
    public void Begin(double[] y0)
    {
        if (_options.CollectOutput)
        {
            _result.Times.Add(_setup.T0);
            _result.States.Add((double[])y0.Clone());
        }

        _options.OutputFunction?.Invoke(OdeOutputPhase.Init, [_setup.T0, _setup.TFinal], [Select(y0)]);
    }

    /// <summary>
    /// Reports what an accepted step from <paramref name="t"/> to <paramref name="tNew"/> produces,
    /// reading inside it with <paramref name="interpolate"/> where a time falls there. Answers
    /// whether the output function asked to stop.
    /// </summary>
    public bool AfterStep(double t, double tNew, double[] yNew, Func<double, double[]> interpolate, bool stoppedByEvent)
    {
        if (!_options.CollectOutput && _options.OutputFunction is null)
        {
            return false;
        }

        var times = new List<double>();
        var states = new List<double[]>();
        double[] tspan = _setup.Tspan;
        if (tspan.Length > 2)
        {
            // Only the times the caller named, off the polynomial; a step that stopped on an
            // event reports the event's own point as well.
            while (_next < tspan.Length)
            {
                double wanted = tspan[_next];
                if (_setup.Direction * (tNew - wanted) < 0)
                {
                    if (stoppedByEvent)
                    {
                        times.Add(tNew);
                        states.Add(yNew);
                    }

                    break;
                }

                times.Add(wanted);
                states.Add(wanted == tNew ? yNew : interpolate(wanted));
                _next++;
            }
        }
        else if (_refine <= 1)
        {
            times.Add(tNew);
            states.Add(yNew);
        }
        else
        {
            for (int j = 1; j < _refine; j++)
            {
                double inside = t + ((tNew - t) * ((double)j / _refine));
                times.Add(inside);
                states.Add(interpolate(inside));
            }

            times.Add(tNew);
            states.Add(yNew);
        }

        if (times.Count == 0)
        {
            return false;
        }

        if (_options.CollectOutput)
        {
            for (int i = 0; i < times.Count; i++)
            {
                _result.Times.Add(times[i]);
                _result.States.Add((double[])states[i].Clone());
            }
        }

        if (_options.OutputFunction is { } output)
        {
            var selected = new double[states.Count][];
            for (int i = 0; i < states.Count; i++)
            {
                selected[i] = Select(states[i]);
            }

            return output(OdeOutputPhase.Step, [.. times], selected);
        }

        return false;
    }

    /// <summary>Tells the output function the run is over, and prints the statistics if asked.</summary>
    public void Finish()
    {
        _options.OutputFunction?.Invoke(OdeOutputPhase.Done, [], []);
        if (_options.Stats && _options.Print is { } print)
        {
            print($"{_result.StepCount} successful steps\n");
            print($"{_result.Failed} failed attempts\n");
            print($"{_result.Evaluations} function evaluations\n");
        }
    }

    private double[] Select(double[] state)
    {
        if (_selection is null)
        {
            return (double[])state.Clone();
        }

        var picked = new double[_selection.Length];
        for (int i = 0; i < picked.Length; i++)
        {
            picked[i] = state[_selection[i]];
        }

        return picked;
    }
}

/// <summary>
/// The event locator every solver shares — MATLAB's <c>odezero</c>: a bracketing search over one
/// step's interpolant for the zeros of the event function, with the bookkeeping that decides which
/// crossings count and whether one of them ends the run.
/// </summary>
internal sealed class OdeEvents
{
    private const double SmallestNormal = 2.2250738585072014e-308;

    private readonly OdeEventFunction _events;
    private readonly OdeResult _result;
    private double[] _last;

    public OdeEvents(OdeEventFunction events, double t0, double[] y0, OdeResult result)
    {
        _events = events;
        _result = result;
        _last = events(t0, y0).Values;
        result.HadEvents = true;
    }

    /// <summary>
    /// Searches the step from (<paramref name="t"/>, <paramref name="y"/>) to (<paramref name="tNew"/>,
    /// <paramref name="yNew"/>) for events, records those found, and answers whether a terminal one
    /// stops the run — and where.
    /// </summary>
    public (bool Stop, double At, double[] State) Locate(double t, double[] y, double tNew, double[] yNew,
        Func<double, double[]> interpolate, double t0)
    {
        double tolerance = 128 * Math.Max(Spacing(t), Spacing(tNew));
        tolerance = Math.Min(tolerance, Math.Abs(tNew - t));
        double direction = Math.Sign(tNew - t);

        double tLeft = t;
        double[] yLeft = y;
        double[] vLeft = _last;
        OdeEventReading atNew = _events(tNew, yNew);
        double[] vNew = atNew.Values;
        int m = vNew.Length;
        int[] watched = atNew.Direction.Length == m ? atNew.Direction : new int[m];
        bool[] terminal = atNew.Terminal;

        double tRight = tNew;
        double[] yRight = yNew;
        double[] vRight = vNew;
        double tTry = tRight;
        double[] vTry = vRight;
        bool stop = false;
        double stopAt = tNew;
        double[] stopState = yNew;

        while (true)
        {
            int lastMoved = 0;
            List<int> crossing;
            while (true)
            {
                crossing = Crossings(vLeft, vRight, watched);
                if (crossing.Count == 0)
                {
                    if (lastMoved != 0)
                    {
                        throw new InvalidOperationException("odezero: an event was lost during the search for it.");
                    }

                    _last = vNew;
                    return (false, tNew, yNew);
                }

                double delta = tRight - tLeft;
                if (Math.Abs(delta) <= tolerance)
                {
                    break;
                }

                bool atStartZero = false;
                if (tLeft == t)
                {
                    foreach (int j in crossing)
                    {
                        if (vLeft[j] == 0 && vRight[j] != 0)
                        {
                            atStartZero = true;
                            break;
                        }
                    }
                }

                if (atStartZero)
                {
                    tTry = tLeft + (direction * 0.5 * tolerance);
                }
                else
                {
                    double change = 1;
                    foreach (int j in crossing)
                    {
                        double maybe;
                        if (vLeft[j] == 0)
                        {
                            if (direction * tTry > direction * tRight && vTry[j] != vRight[j])
                            {
                                maybe = 1.0 - (vRight[j] * (tTry - tRight) / ((vTry[j] - vRight[j]) * delta));
                                if (maybe < 0 || maybe > 1)
                                {
                                    maybe = 0.5;
                                }
                            }
                            else
                            {
                                maybe = 0.5;
                            }
                        }
                        else if (vRight[j] == 0)
                        {
                            if (direction * tTry < direction * tLeft && vTry[j] != vLeft[j])
                            {
                                maybe = vLeft[j] * (tLeft - tTry) / ((vTry[j] - vLeft[j]) * delta);
                                if (maybe < 0 || maybe > 1)
                                {
                                    maybe = 0.5;
                                }
                            }
                            else
                            {
                                maybe = 0.5;
                            }
                        }
                        else
                        {
                            maybe = -vLeft[j] / (vRight[j] - vLeft[j]);
                        }

                        if (maybe < change)
                        {
                            change = maybe;
                        }
                    }

                    change *= Math.Abs(delta);
                    change = Math.Max(0.5 * tolerance, Math.Min(change, Math.Abs(delta) - (0.5 * tolerance)));
                    tTry = tLeft + (direction * change);
                }

                double[] yTry = interpolate(tTry);
                vTry = _events(tTry, yTry).Values;
                if (Crossings(vLeft, vTry, watched).Count > 0)
                {
                    (tRight, tTry) = (tTry, tRight);
                    (yRight, _) = (yTry, yRight);
                    (vRight, vTry) = (vTry, vRight);
                    if (lastMoved == 2)
                    {
                        vLeft = Halved(vLeft);
                    }

                    lastMoved = 2;
                }
                else
                {
                    (tLeft, tTry) = (tTry, tLeft);
                    (yLeft, _) = (yTry, yLeft);
                    (vLeft, vTry) = (vTry, vLeft);
                    if (lastMoved == 1)
                    {
                        vRight = Halved(vRight);
                    }

                    lastMoved = 1;
                }
            }

            bool anyTerminal = false;
            foreach (int j in crossing)
            {
                _result.EventTimes.Add(tRight);
                _result.EventStates.Add((double[])yRight.Clone());
                _result.EventIndices.Add(j);
                anyTerminal |= terminal[j];
            }

            if (anyTerminal)
            {
                if (tLeft != t0)
                {
                    stop = true;
                    stopAt = tRight;
                    stopState = yRight;
                }

                break;
            }

            if (Math.Abs(tNew - tRight) <= tolerance)
            {
                break;
            }

            tTry = tRight;
            vTry = vRight;
            tLeft = tRight + (direction * 0.5 * tolerance);
            yLeft = interpolate(tLeft);
            vLeft = _events(tLeft, yLeft).Values;
            tRight = tNew;
            yRight = yNew;
            vRight = vNew;
        }

        _last = vNew;
        return (stop, stopAt, stopState);
    }

    private static List<int> Crossings(double[] left, double[] right, int[] watched)
    {
        var found = new List<int>();
        for (int j = 0; j < left.Length; j++)
        {
            if (Math.Sign(left[j]) != Math.Sign(right[j]) && watched[j] * (right[j] - left[j]) >= 0)
            {
                found.Add(j);
            }
        }

        return found;
    }

    /// <summary>Half of each value that can still be halved — the search's way of shrinking a bracket whose end refuses to move.</summary>
    private static double[] Halved(double[] values)
    {
        var halved = (double[])values.Clone();
        for (int i = 0; i < halved.Length; i++)
        {
            double maybe = 0.5 * values[i];
            if (Math.Abs(maybe) >= SmallestNormal)
            {
                halved[i] = maybe;
            }
        }

        return halved;
    }

    private static double Spacing(double t)
    {
        double magnitude = Math.Abs(t);
        return magnitude == 0 ? double.Epsilon : Math.BitIncrement(magnitude) - magnitude;
    }
}
