using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The explicit ODE family (M125): <c>ode23</c>, <c>ode45</c>, <c>ode78</c>, <c>ode89</c> and
/// <c>ode113</c> on one path, <c>odextend</c>, and the four output functions <c>odeset</c> can
/// name — <c>odeplot</c>, <c>odeprint</c>, <c>odephas2</c>, <c>odephas3</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every solver takes the same four arguments, reads the same options structure, and answers in
/// the same three shapes: a solution structure for one output, the pair of arrays for two, and
/// the pair with the event times, states and indices for five. Written as a statement, with no
/// output at all, a solver draws its answer through <c>odeplot</c> instead — which is MATLAB's
/// rule, and the reason the output functions are here beside the solvers rather than among the
/// graphics.
/// </para>
/// <para>
/// The options are read once into the numerics layer's own record; the function handles among
/// them — <c>Events</c>, <c>OutputFcn</c>, a <c>Mass</c> that is a function — become callbacks
/// that call the script's handles, so the solver never sees a script value. The five fields the
/// stiff solvers will read (<c>Jacobian</c>, <c>JPattern</c>, <c>JConstant</c>, <c>BDF</c>,
/// <c>MaxOrder</c>) and the two only a state-dependent mass matrix needs are accepted and left
/// alone, as MATLAB's explicit solvers leave them.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly string[] OdeSolverNames = ["ode23", "ode45", "ode78", "ode89", "ode113"];

    [ThreadStatic]
    private static List<double>? _odePlotTimes;

    [ThreadStatic]
    private static List<double[]>? _odePlotStates;

    /// <summary>Registers the five solvers, <c>odextend</c>, and the output functions.</summary>
    internal static void RegisterOdeFamilyBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        foreach (string solver in OdeSolverNames)
        {
            string name = solver;
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => SolveOde(env, host, name, args, 1, line, col)[0])
            {
                // A solver written as a statement draws rather than answers, so it has to be told
                // when nobody wants its numbers.
                KnowsWhenDiscarded = true,
                MultiOutput = (args, wanted, line, col) => SolveOde(env, host, name, args, wanted, line, col),
            }));
        }

        env.Declare("odextend", JgsValue.Function(new BuiltinFunction("odextend",
            (args, line, col) => Odextend(env, host, args, line, col))));

        env.Declare("odeplot", JgsValue.Function(new BuiltinFunction("odeplot",
            (args, line, col) => OdePlotFunction(env, "odeplot", args, line, col))));
        env.Declare("odephas2", JgsValue.Function(new BuiltinFunction("odephas2",
            (args, line, col) => OdePlotFunction(env, "odephas2", args, line, col))));
        env.Declare("odephas3", JgsValue.Function(new BuiltinFunction("odephas3",
            (args, line, col) => OdePlotFunction(env, "odephas3", args, line, col))));
        env.Declare("odeprint", JgsValue.Function(new BuiltinFunction("odeprint",
            (args, line, col) => OdePrint(host, args, line, col))));
    }

    // --- the solvers --------------------------------------------------------------------------

    /// <summary>
    /// <c>[t, y] = odeXX(f, tspan, y0, options)</c>, <c>[t, y, te, ye, ie] = ...</c>,
    /// <c>sol = odeXX(...)</c>, and the statement form that draws.
    /// </summary>
    private static JgsValue[] SolveOde(JgsEnvironment env, JGraphScriptGlobals host, string solver,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(solver, args, 3, 4, line, col);
        IJgsCallable f = OdeFunctionOf(env, solver, args[0], line, col);
        double[] tspan = ToDoubles(solver, args[1], line, col);
        double[] initial = ToDoubles(solver, args[2], line, col);
        JgsValue? options = args.Count > 3 && args[3].Type == JgsType.Struct ? args[3] : null;
        if (args.Count > 3 && options is null && !(args[3].Type == JgsType.Array && args[3].ArrayLength == 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odearguments:OptionsNotStruct",
                $"{solver}: the fourth argument must be an options structure created with odeset, or [].");
        }

        bool statement = wanted == 0;
        bool asSolution = wanted == 1;
        OdeResult result = RunOdeSolver(env, host, solver, f, tspan, initial, options,
            asSolution, statement, line, col);

        if (statement)
        {
            return [];
        }

        int states = initial.Length;
        if (asSolution)
        {
            return [OdeSolution(solver, args[0], options, result, tspan[0], initial)];
        }

        int mesh = result.Times.Count;
        JgsValue times = JgsMatrix.Build(mesh, 1, (r, _) => result.Times[r]);
        JgsValue trajectory = JgsMatrix.Build(mesh, states, (r, c) => result.States[r][c]);
        if (wanted <= 2)
        {
            return [times, trajectory];
        }

        int found = result.EventTimes.Count;
        return
        [
            times,
            trajectory,
            JgsMatrix.Build(found, 1, (r, _) => result.EventTimes[r]),
            JgsMatrix.Build(found, states, (r, c) => result.EventStates[r][c]),
            JgsMatrix.Build(found, 1, (r, _) => result.EventIndices[r] + 1),
        ];
    }

    /// <summary>The derivative, as a handle or as the name of one.</summary>
    private static IJgsCallable OdeFunctionOf(JgsEnvironment env, string solver, JgsValue given, int line, int col)
    {
        if (given.Type == JgsType.Function)
        {
            return given.AsCallable;
        }

        if (IsTextScalar(given) && env.TryGet(TextOf(given), out JgsValue named) && named.Type == JgsType.Function)
        {
            return named.AsCallable;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:odearguments:FunctionHandleRequired",
            $"{solver} expects a function handle f(t, y) as its first argument.");
    }

    /// <summary>Runs one solver over the numerics layer, turning its refusals into the script's.</summary>
    private static OdeResult RunOdeSolver(JgsEnvironment env, JGraphScriptGlobals host, string solver,
        IJgsCallable f, double[] tspan, double[] initial, JgsValue? options,
        bool asSolution, bool statement, int line, int col)
    {
        int states = initial.Length;
        double[] Derivative(double t, double[] y)
        {
            JgsValue column = JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1]);
            JgsValue slope = f.Call([JgsValue.Number(t), column], line, col);
            double[] dy = ToDoubles(solver, slope, line, col);
            if (dy.Length != states)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:odearguments:SizeIC",
                    $"{solver}: the derivative returned a vector of length {dy.Length}, but the length of initial "
                    + $"conditions vector is {states}. The vector returned by the derivative and the initial "
                    + "conditions vector must have the same number of elements.");
            }

            return dy;
        }

        OdeOptions settings = OdeOptionsFrom(env, host, solver, options, states, statement, asSolution, line, col);
        try
        {
            RungeKuttaScheme? scheme = RungeKuttaScheme.Named(solver);
            return scheme is not null
                ? ExplicitRungeKutta.Run(scheme, Derivative, tspan, initial, settings)
                : AdamsPece.Run(Derivative, tspan, initial, settings);
        }
        catch (OdeArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new JgsRuntimeException(line, col, $"{solver}: {ex.Message}");
        }
    }

    /// <summary>
    /// The options structure read into the numerics layer's record, with the script's handles
    /// wrapped as callbacks. A statement-form solve gets <c>odeplot</c> as its output function.
    /// </summary>
    private static OdeOptions OdeOptionsFrom(JgsEnvironment env, JGraphScriptGlobals host, string solver,
        JgsValue? options, int states, bool statement, bool asSolution, int line, int col)
    {
        JgsValue? Field(string name) =>
            options is not null && TryReadField(options, name, out JgsValue value) && !IsUnsetOption(value)
                ? value
                : null;

        double? Number(string name) => OdeNumber(options, name);

        bool Flag(string name)
        {
            JgsValue? value = Field(name);
            return value is not null && Truth(value);
        }

        double[]? absolute = Field("AbsTol") is { } atol ? ToDoubles("odeset", atol, line, col) : null;

        OdeEventFunction? events = null;
        if (Field("Events") is { } eventsValue)
        {
            IJgsCallable handle = OdeFunctionOf(env, solver, eventsValue, line, col);
            events = (t, y) =>
            {
                JgsValue column = JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1]);
                JgsValue[] outputs = CallForOutputs(handle, [JgsValue.Number(t), column], 3, line, col);
                double[] values = ToDoubles("Events", outputs[0], line, col);
                double[] terminal = outputs.Length > 1 ? ToDoubles("Events", outputs[1], line, col) : [];
                double[] direction = outputs.Length > 2 ? ToDoubles("Events", outputs[2], line, col) : [];
                var stops = new bool[values.Length];
                var directions = new int[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    stops[i] = terminal.Length > i ? terminal[i] != 0 : terminal.Length == 1 && terminal[0] != 0;
                    directions[i] = direction.Length > i ? System.Math.Sign(direction[i])
                        : direction.Length == 1 ? System.Math.Sign(direction[0]) : 0;
                }

                return new OdeEventReading(values, stops, directions);
            };
        }

        JgsValue? outputValue = Field("OutputFcn");
        if (outputValue is null && statement && env.TryGet("odeplot", out JgsValue plotter))
        {
            outputValue = plotter;
        }

        OdeOutputFunction? outputFunction = null;
        if (outputValue is not null)
        {
            IJgsCallable handle = OdeFunctionOf(env, solver, outputValue, line, col);
            outputFunction = (phase, times, columns) =>
            {
                JgsValue t = phase == OdeOutputPhase.Done
                    ? JgsValue.Array([])
                    : JgsMatrix.FromColumnMajorDims((double[])times.Clone(), [1, times.Length]);
                int rows = columns.Length > 0 ? columns[0].Length : 0;
                JgsValue y = phase == OdeOutputPhase.Done
                    ? JgsValue.Array([])
                    : JgsMatrix.Build(rows, columns.Length, (r, c) => columns[c][r]);
                string flag = phase switch
                {
                    OdeOutputPhase.Init => "init",
                    OdeOutputPhase.Done => "done",
                    _ => string.Empty,
                };
                JgsValue answer = handle.Call([t, y, JgsValue.Str(flag)], line, col);
                return phase == OdeOutputPhase.Step && answer.Type != JgsType.Null && Truth(answer);
            };
        }

        int[]? selection = null;
        if (Field("OutputSel") is { } sel)
        {
            selection = OneBasedIndices("OutputSel", ToDoubles("odeset", sel, line, col), states, line, col);
        }

        int[]? nonNegative = null;
        if (Field("NonNegative") is { } nonNeg)
        {
            nonNegative = OneBasedIndices("NonNegative", ToDoubles("odeset", nonNeg, line, col), states, line, col);
        }

        double[,]? mass = null;
        Func<double, double[], double[,]>? massFunction = null;
        bool massDependsOnState = false;
        if (Field("Mass") is { } massValue)
        {
            string singular = Field("MassSingular") is { } ms && IsTextScalar(ms) ? TextOf(ms).ToLowerInvariant() : "no";
            if (singular == "yes")
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:MassSingularYes",
                    $"{solver.ToUpperInvariant()} cannot solve problems with a singular mass matrix.");
            }

            if (singular == "maybe")
            {
                Warn(env, host, $"{solver.ToUpperInvariant()} does not support a singular mass matrix; a non-singular one is assumed.", line, col);
            }

            if (massValue.Type == JgsType.Function || IsTextScalar(massValue))
            {
                IJgsCallable handle = OdeFunctionOf(env, solver, massValue, line, col);
                string dependence = Field("MStateDependence") is { } dep && IsTextScalar(dep)
                    ? TextOf(dep).ToLowerInvariant()
                    : "weak";
                massDependsOnState = dependence != "none";
                bool withState = massDependsOnState;
                massFunction = (t, y) =>
                {
                    JgsValue answer = withState
                        ? handle.Call([JgsValue.Number(t), JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1])], line, col)
                        : handle.Call([JgsValue.Number(t)], line, col);
                    return SquareRect("Mass", answer, line, col);
                };
            }
            else
            {
                mass = SquareRect("Mass", massValue, line, col);
            }
        }

        return new OdeOptions
        {
            RelativeTolerance = Number("RelTol") ?? 1e-3,
            AbsoluteTolerance = absolute,
            NormControl = Flag("NormControl"),
            Refine = Number("Refine") is { } refine ? (int)refine : null,
            MaxStep = Number("MaxStep"),
            MinStep = Number("MinStep"),
            InitialStep = Number("InitialStep"),
            Events = events,
            OutputFunction = outputFunction,
            OutputSelection = selection,
            NonNegative = nonNegative,
            Mass = mass,
            MassFunction = massFunction,
            MassDependsOnState = massDependsOnState,
            Stats = Flag("Stats"),
            Warn = message => Warn(env, host, message, line, col),
            Print = text => host.print(text.TrimEnd('\n')),
            RecordSteps = asSolution,
            CollectOutput = !asSolution,
        };
    }

    /// <summary>One-based component indices out of an option, checked against the state's size.</summary>
    private static int[] OneBasedIndices(string option, double[] given, int states, int line, int col)
    {
        var indices = new int[given.Length];
        for (int i = 0; i < given.Length; i++)
        {
            int index = (int)given[i];
            if (index != given[i] || index < 1 || index > states)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:odeset:{option}Invalid",
                    $"{option}: {given[i]} is not the index of one of the {states} state component(s).");
            }

            indices[i] = index - 1;
        }

        return indices;
    }

    /// <summary>A warning raised the way the script's own <c>warning</c> raises one, so <c>lastwarn</c> sees it.</summary>
    private static void Warn(JgsEnvironment env, JGraphScriptGlobals host, string message, int line, int col)
    {
        if (env.TryGet("warning", out JgsValue warning) && warning.Type == JgsType.Function)
        {
            warning.AsCallable.Call([JgsValue.Str(message)], line, col);
            return;
        }

        host.print("Warning: " + message);
    }

    // --- odextend ---------------------------------------------------------------------------

    /// <summary>
    /// <c>solext = odextend(sol, odefun, tfinal, y0, options)</c>: the solution carried on from
    /// where it stopped, by the solver that made it, and the two structures joined into one.
    /// </summary>
    private static JgsValue Odextend(JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int line, int col)
    {
        ArityRange("odextend", args, 3, 5, line, col);
        JgsValue sol = args[0];
        if (sol.Type != JgsType.Struct || !sol.AsStruct.TryGetValue("solver", out JgsValue? solverValue)
            || !IsTextScalar(solverValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odextend:SOLNotODEsolverStruct",
                "The first argument must be a solution structure returned by an ODE solver.");
        }

        string solver = TextOf(solverValue);
        if (Array.IndexOf(OdeSolverNames, solver) < 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odextend:InvalidSolverNameInSOL",
                $"odextend cannot extend a solution from '{solver}'.");
        }

        double[] x = ToDoubles("odextend", sol.AsStruct["x"], line, col);
        double[] yFlat = ToDoubles("odextend", sol.AsStruct["y"], line, col);
        int mesh = x.Length;
        int states = mesh > 0 ? yFlat.Length / mesh : 0;
        double tFinal = Num("odextend", args, 2, line, col);
        double first = x[0];
        double last = x[^1];
        bool forward = first < last;
        if (forward ? tFinal < first : tFinal > first)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odextend:SolutionCannotBeExtended",
                $"The solution is defined on [{first:G}, {last:G}] and cannot be extended to {tFinal:G}.");
        }

        if (forward ? tFinal <= last : tFinal >= last)
        {
            Warn(env, host, $"The solution is already available on [{first:G}, {tFinal:G}].", line, col);
            return sol;
        }

        JgsValue extdata = sol.AsStruct.TryGetValue("extdata", out JgsValue? ext)
            ? ext
            : JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal));
        JgsValue odefunValue = args[1].Type == JgsType.Array && args[1].ArrayLength == 0
            ? (extdata.Type == JgsType.Struct && extdata.AsStruct.TryGetValue("odefun", out JgsValue? stored)
                ? stored
                : throw new JgsRuntimeException(line, col, "MATLAB:odextend:NoOdefun",
                    "odextend: the solution carries no odefun to continue with; pass one."))
            : args[1];
        IJgsCallable f = OdeFunctionOf(env, solver, odefunValue, line, col);

        double[] y0;
        if (args.Count > 3 && !(args[3].Type == JgsType.Array && args[3].ArrayLength == 0))
        {
            y0 = ToDoubles("odextend", args[3], line, col);
        }
        else
        {
            y0 = new double[states];
            for (int i = 0; i < states; i++)
            {
                y0[i] = yFlat[i + ((mesh - 1) * states)];
            }
        }

        JgsValue? options = null;
        if (args.Count > 4 && args[4].Type == JgsType.Struct)
        {
            options = args[4];
        }
        else if (extdata.Type == JgsType.Struct && extdata.AsStruct.TryGetValue("options", out JgsValue? kept)
                 && kept.Type == JgsType.Struct)
        {
            options = kept;
        }

        OdeResult result = RunOdeSolver(env, host, solver, f, [last, tFinal], y0, options,
            asSolution: true, statement: false, line, col);
        JgsValue extension = OdeSolution(solver, odefunValue, options, result, last, y0);
        return JoinedSolutions(solver, sol, extension, states, line, col);
    }

    // --- the output functions ---------------------------------------------------------------

    /// <summary>
    /// <c>odeplot</c>, <c>odephas2</c> and <c>odephas3</c>: the points are kept as the solver
    /// hands them over, and drawn once at <c>'done'</c> — the picture MATLAB's animated lines end
    /// on, without the animation.
    /// </summary>
    private static JgsValue OdePlotFunction(JgsEnvironment env, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        string flag = args.Count > 2 && IsTextScalar(args[2]) ? TextOf(args[2]) : string.Empty;
        switch (flag)
        {
            case "init":
                _odePlotTimes = [];
                _odePlotStates = [];
                double[] span = ToDoubles(name, args[0], line, col);
                _odePlotTimes.Add(span.Length > 0 ? span[0] : 0);
                _odePlotStates.Add(ToDoubles(name, args[1], line, col));
                break;

            case "done":
                DrawOdePlot(env, name, line, col);
                _odePlotTimes = null;
                _odePlotStates = null;
                break;

            default:
                _odePlotTimes ??= [];
                _odePlotStates ??= [];
                double[] times = ToDoubles(name, args[0], line, col);
                double[] values = ToDoubles(name, args[1], line, col);
                int rows = times.Length > 0 ? values.Length / times.Length : 0;
                for (int c = 0; c < times.Length; c++)
                {
                    var column = new double[rows];
                    Array.Copy(values, c * rows, column, 0, rows);
                    _odePlotTimes.Add(times[c]);
                    _odePlotStates.Add(column);
                }

                break;
        }

        return JgsValue.Number(0);
    }

    private static void DrawOdePlot(JgsEnvironment env, string name, int line, int col)
    {
        List<double>? times = _odePlotTimes;
        List<double[]>? states = _odePlotStates;
        if (times is null || states is null || times.Count == 0)
        {
            return;
        }

        int components = states[0].Length;
        int count = times.Count;
        JgsValue Component(int index) => JgsMatrix.Build(count, 1, (r, _) => index < states[r].Length ? states[r][index] : 0);

        string drawer = name == "odephas3" ? "plot3" : "plot";
        if (!env.TryGet(drawer, out JgsValue plotter) || plotter.Type != JgsType.Function)
        {
            return;
        }

        JgsValue style = JgsValue.Str("-o");
        switch (name)
        {
            case "odephas2":
                if (components < 2)
                {
                    throw new JgsRuntimeException(line, col, "odephas2 needs at least two solution components.");
                }

                plotter.AsCallable.Call([Component(0), Component(1), style], line, col);
                break;

            case "odephas3":
                if (components < 3)
                {
                    throw new JgsRuntimeException(line, col, "odephas3 needs at least three solution components.");
                }

                plotter.AsCallable.Call([Component(0), Component(1), Component(2), style], line, col);
                if (env.TryGet("grid", out JgsValue grid) && grid.Type == JgsType.Function)
                {
                    grid.AsCallable.Call([JgsValue.Str("on")], line, col);
                }

                break;

            default:
                JgsValue t = JgsMatrix.Build(count, 1, (r, _) => times[r]);
                JgsValue y = JgsMatrix.Build(count, components, (r, c) => c < states[r].Length ? states[r][c] : 0);
                plotter.AsCallable.Call([t, y, style], line, col);
                break;
        }
    }

    /// <summary><c>odeprint</c>: the time and the state of every point, printed as they arrive.</summary>
    private static JgsValue OdePrint(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("odeprint", args, 2, 3, line, col);
        string flag = args.Count > 2 && IsTextScalar(args[2]) ? TextOf(args[2]) : string.Empty;
        switch (flag)
        {
            case "done":
                host.print(string.Empty);
                break;

            case "init":
                double[] span = ToDoubles("odeprint", args[0], line, col);
                host.print("t =");
                host.print(string.Empty);
                host.print(JgsValue.Number(span.Length > 0 ? span[0] : 0).Display());
                host.print(string.Empty);
                host.print("y =");
                host.print(string.Empty);
                host.print(args[1].Display());
                host.print(string.Empty);
                break;

            default:
                host.print("t =");
                host.print(string.Empty);
                host.print(args[0].Display());
                host.print(string.Empty);
                host.print("y =");
                host.print(string.Empty);
                host.print(args[1].Display());
                host.print(string.Empty);
                break;
        }

        return JgsValue.Number(0);
    }
}
