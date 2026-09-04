namespace JGraph.Numerics;

/// <summary>dy/dt = f(t, y), answered as a fresh array of the state's length.</summary>
public delegate double[] OdeFunction(double t, double[] y);

/// <summary>
/// What an event function reports at one point: the values whose zeros are being watched, whether
/// a zero of each one ends the integration, and the direction each crossing is watched in — −1 for
/// decreasing, 1 for increasing, 0 for either.
/// </summary>
public readonly record struct OdeEventReading(double[] Values, bool[] Terminal, int[] Direction);

/// <summary>MATLAB's <c>Events</c> function, <c>[value, isterminal, direction] = events(t, y)</c>.</summary>
public delegate OdeEventReading OdeEventFunction(double t, double[] y);

/// <summary>The three calls an output function receives: once before the first step, once per batch of output points, once after the last.</summary>
public enum OdeOutputPhase
{
    /// <summary><c>outputFcn([t0 tfinal], y0, 'init')</c>.</summary>
    Init,

    /// <summary><c>stop = outputFcn(t, y, '')</c> — the points of one step, and whether to stop.</summary>
    Step,

    /// <summary><c>outputFcn([], [], 'done')</c>.</summary>
    Done,
}

/// <summary>
/// MATLAB's <c>OutputFcn</c>. <paramref name="times"/> and <paramref name="states"/> are the
/// points of one batch, one state column per time, already cut down to <c>OutputSel</c>. The answer
/// is whether the integration should stop after this batch.
/// </summary>
public delegate bool OdeOutputFunction(OdeOutputPhase phase, double[] times, double[][] states);

/// <summary>
/// Everything <c>odeset</c> can say that the explicit solvers act on. Null or unset means MATLAB's
/// default, which each solver applies itself because two of the defaults differ by solver.
/// </summary>
public sealed class OdeOptions
{
    /// <summary>Relative tolerance; MATLAB's default is 1e-3.</summary>
    public double RelativeTolerance { get; init; } = 1e-3;

    /// <summary>Absolute tolerance, one entry or one per state; null is MATLAB's 1e-6.</summary>
    public double[]? AbsoluteTolerance { get; init; }

    /// <summary>Measure the error in the 2-norm of the whole state rather than component by component.</summary>
    public bool NormControl { get; init; }

    /// <summary>Points reported per accepted step; null takes the solver's own default.</summary>
    public int? Refine { get; init; }

    /// <summary>MATLAB's <c>MaxStep</c>; null leaves the default tenth of the interval.</summary>
    public double? MaxStep { get; init; }

    /// <summary>MATLAB's <c>MinStep</c>; null leaves the sixteen-ulp floor alone.</summary>
    public double? MinStep { get; init; }

    /// <summary>MATLAB's <c>InitialStep</c>; null leaves the step the slope suggests.</summary>
    public double? InitialStep { get; init; }

    /// <summary>The event function, or null when nothing is watched.</summary>
    public OdeEventFunction? Events { get; init; }

    /// <summary>The output function, or null.</summary>
    public OdeOutputFunction? OutputFunction { get; init; }

    /// <summary>Which state components the output function sees, 0-based; null is all of them.</summary>
    public int[]? OutputSelection { get; init; }

    /// <summary>The components held at or above zero, 0-based; null is none.</summary>
    public int[]? NonNegative { get; init; }

    /// <summary>A constant mass matrix, <c>M·y' = f(t, y)</c>; null is the identity.</summary>
    public double[,]? Mass { get; init; }

    /// <summary>A mass matrix that depends on time, or on time and state; null is none.</summary>
    public Func<double, double[], double[,]>? MassFunction { get; init; }

    /// <summary>Whether <see cref="MassFunction"/> reads the state as well as the time.</summary>
    public bool MassDependsOnState { get; init; }

    /// <summary>Print the step, failure and evaluation counts when the run ends.</summary>
    public bool Stats { get; init; }

    /// <summary>Where a warning goes; null drops it.</summary>
    public Action<string>? Warn { get; init; }

    /// <summary>Where <see cref="Stats"/> prints; null drops the lines.</summary>
    public Action<string>? Print { get; init; }

    /// <summary>
    /// Keep every accepted step's interpolation data, so the solution can be read again later.
    /// The solution-structure form asks for this; the two-array form does not.
    /// </summary>
    public bool RecordSteps { get; init; }

    /// <summary>
    /// Whether the caller wants the output points at all. MATLAB's one-output form keeps only
    /// the mesh, and computes no refined points — which is also why it costs fewer evaluations
    /// on the solvers whose dense output needs stages of its own.
    /// </summary>
    public bool CollectOutput { get; init; } = true;
}

/// <summary>
/// One accepted step, with what its solver's interpolant needs to read the solution inside it.
/// The Runge–Kutta solvers keep the stage slopes of the step; <c>ode113</c> keeps the modified
/// divided differences and the step-size history at the step's end.
/// </summary>
/// <param name="Start">Where the step started.</param>
/// <param name="End">Where it ended.</param>
/// <param name="StartState">The state at the start.</param>
/// <param name="EndState">The state at the end.</param>
/// <param name="Stages">Runge–Kutta: the stage slopes, one array per interpolation stage. Adams: the columns of <c>phi</c>.</param>
/// <param name="Order">Adams: the order the step was taken at.</param>
/// <param name="Psi">Adams: the step-size history <c>psi</c>.</param>
public sealed record OdeStepRecord(
    double Start, double End, double[] StartState, double[] EndState, double[][] Stages, int Order, double[]? Psi);

/// <summary>What an integration answered and what it cost.</summary>
public sealed class OdeResult
{
    /// <summary>Which solver ran — MATLAB's name for it.</summary>
    public required string Solver { get; init; }

    /// <summary>The output times, in the order they were produced.</summary>
    public List<double> Times { get; } = [];

    /// <summary>The state at each output time.</summary>
    public List<double[]> States { get; } = [];

    /// <summary>The accepted steps, when the run was asked to record them.</summary>
    public List<OdeStepRecord> Steps { get; } = [];

    /// <summary>The times events were located at.</summary>
    public List<double> EventTimes { get; } = [];

    /// <summary>The states at those times.</summary>
    public List<double[]> EventStates { get; } = [];

    /// <summary>Which event fired each time, 0-based.</summary>
    public List<int> EventIndices { get; } = [];

    /// <summary>Whether an event function was watching at all.</summary>
    public bool HadEvents { get; set; }

    /// <summary>Accepted steps.</summary>
    public int StepCount { get; set; }

    /// <summary>Attempts the error test rejected.</summary>
    public int Failed { get; set; }

    /// <summary>Calls of the derivative.</summary>
    public int Evaluations { get; set; }

    /// <summary>Where the integration ended — the end of the span, or where an event or the output function stopped it.</summary>
    public double FinalTime { get; set; }
}
