using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The ODE family: the explicit solvers (M125) <c>ode23</c>, <c>ode45</c>, <c>ode78</c>,
/// <c>ode89</c> and <c>ode113</c> and the stiff ones (M126) <c>ode15s</c>, <c>ode23s</c>,
/// <c>ode23t</c> and <c>ode23tb</c> on one path, <c>odextend</c>, and the four output functions
/// <c>odeset</c> can name — <c>odeplot</c>, <c>odeprint</c>, <c>odephas2</c>, <c>odephas3</c>.
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
/// them — <c>Events</c>, <c>OutputFcn</c>, a <c>Mass</c> or a <c>Jacobian</c> that is a function —
/// become callbacks that call the script's handles, so the solver never sees a script value. The
/// fields the explicit family stores and does not read are the ones the stiff family does:
/// <c>Jacobian</c>, <c>JPattern</c>, <c>JConstant</c>, <c>Vectorized</c>, <c>BDF</c>,
/// <c>MaxOrder</c>, <c>MassSingular</c>, <c>InitialSlope</c>, <c>MStateDependence</c> and
/// <c>MvPattern</c>. One reader serves both, and each solver takes what it can use.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly string[] OdeSolverNames =
        ["ode23", "ode45", "ode78", "ode89", "ode113", "ode15s", "ode23s", "ode23t", "ode23tb"];

    /// <summary>The solvers that read the Jacobian and can carry a singular mass matrix.</summary>
    private static readonly string[] OdeStiffSolverNames = ["ode15s", "ode23s", "ode23t", "ode23tb"];

    [ThreadStatic]
    private static List<double>? _odePlotTimes;

    [ThreadStatic]
    private static List<double[]>? _odePlotStates;

    /// <summary>Registers the nine solvers, <c>odextend</c>, and the output functions.</summary>
    internal static void RegisterOdeFamilyBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        foreach (string solver in OdeSolverNames)
        {
            string name = solver;
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => SolveOde(env, host, name, args, 1, line, col)[0])
            {
                // A solver written as a statement draws rather than answers, so it has to be told
                // when nobody wants its numbers.
                KnowsWhenDiscarded = true,
                MultiOutput = (args, wanted, line, col) => SolveOde(env, host, name, args, wanted, line, col),
            }));
        }

        env.DeclareFunction("odextend", JgsValue.Function(new BuiltinFunction("odextend",
            (args, line, col) => Odextend(env, host, args, line, col))));

        env.DeclareFunction("odeplot", JgsValue.Function(new BuiltinFunction("odeplot",
            (args, line, col) => OdePlotFunction(env, "odeplot", args, line, col))));
        env.DeclareFunction("odephas2", JgsValue.Function(new BuiltinFunction("odephas2",
            (args, line, col) => OdePlotFunction(env, "odephas2", args, line, col))));
        env.DeclareFunction("odephas3", JgsValue.Function(new BuiltinFunction("odephas3",
            (args, line, col) => OdePlotFunction(env, "odephas3", args, line, col))));
        env.DeclareFunction("odeprint", JgsValue.Function(new BuiltinFunction("odeprint",
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

        // Vectorized: the same handle over several states at once, which is what turns a numerical
        // Jacobian from n calls into one. The states go across as the columns of a matrix and the
        // slopes come back the same way.
        double[][] Vectorized(double t, double[][] states2)
        {
            int columns = states2.Length;
            var flat = new double[states * columns];
            for (int c = 0; c < columns; c++)
            {
                Array.Copy(states2[c], 0, flat, c * states, states);
            }

            JgsValue block = JgsMatrix.FromColumnMajorDims(flat, [states, columns]);
            JgsValue answer = f.Call([JgsValue.Number(t), block], line, col);
            double[] slopes = ToDoubles(solver, answer, line, col);
            if (slopes.Length != states * columns)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:odearguments:SizeIC",
                    $"{solver}: a vectorized derivative must answer one column per state column.");
            }

            var split = new double[columns][];
            for (int c = 0; c < columns; c++)
            {
                split[c] = new double[states];
                Array.Copy(slopes, c * states, split[c], 0, states);
            }

            return split;
        }

        OdeOptions settings = OdeOptionsFrom(env, host, solver, options, states, statement, asSolution, line, col)
            with { VectorizedDerivative = Vectorized };
        try
        {
            RungeKuttaScheme? scheme = RungeKuttaScheme.Named(solver);
            return solver switch
            {
                Ode15s.Name => Ode15s.Run(Derivative, tspan, initial, settings),
                Ode23s.Name => Ode23s.Run(Derivative, tspan, initial, settings),
                Ode23t.Name => Ode23t.Run(Derivative, tspan, initial, settings),
                Ode23tb.Name => Ode23tb.Run(Derivative, tspan, initial, settings),
                _ => scheme is not null
                    ? ExplicitRungeKutta.Run(scheme, Derivative, tspan, initial, settings)
                    : AdamsPece.Run(Derivative, tspan, initial, settings),
            };
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

        bool stiff = Array.IndexOf(OdeStiffSolverNames, solver) >= 0;
        double[,]? mass = null;
        Func<double, double[], double[,]>? massFunction = null;
        bool massDependsOnState = false;
        bool massStronglyStateDependent = false;
        var massSingular = OdeMassSingularity.Maybe;
        if (Field("MassSingular") is { } ms && IsTextScalar(ms))
        {
            massSingular = TextOf(ms).ToLowerInvariant() switch
            {
                "yes" => OdeMassSingularity.Yes,
                "no" => OdeMassSingularity.No,
                "maybe" => OdeMassSingularity.Maybe,
                _ => throw new JgsRuntimeException(line, col, "MATLAB:odeset:MassSingularInvalid",
                    "MassSingular must be 'yes', 'no' or 'maybe'."),
            };
        }

        if (Field("Mass") is { } massValue)
        {
            if (!stiff)
            {
                // MATLAB's explicit solvers invert the mass matrix, so a singular one is refused
                // outright and an unknown one is assumed non-singular with a warning.
                if (massSingular == OdeMassSingularity.Yes)
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:MassSingularYes",
                        $"{solver.ToUpperInvariant()} cannot solve problems with a singular mass matrix.");
                }

                if (massSingular == OdeMassSingularity.Maybe)
                {
                    Warn(env, host, $"{solver.ToUpperInvariant()} does not support a singular mass matrix; a non-singular one is assumed.", line, col);
                }
            }

            if (massValue.Type == JgsType.Function || IsTextScalar(massValue))
            {
                IJgsCallable handle = OdeFunctionOf(env, solver, massValue, line, col);
                string dependence = Field("MStateDependence") is { } dep && IsTextScalar(dep)
                    ? TextOf(dep).ToLowerInvariant()
                    : "weak";
                massDependsOnState = dependence != "none";
                massStronglyStateDependent = dependence == "strong";
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

        // The Jacobian: a matrix, a handle, or neither, in which case the solver differences it.
        // Only the stiff solvers read it -- the explicit family stores it and leaves it alone, so
        // one that does not fit the problem is not the explicit family's business to refuse. And
        // ode15i's is a pair, read where its other paired options are.
        double[,]? jacobian = null;
        Func<double, double[], double[,]>? jacobianFunction = null;
        if (stiff && Field("Jacobian") is { } jacobianValue)
        {
            if (jacobianValue.Type == JgsType.Function || IsTextScalar(jacobianValue))
            {
                IJgsCallable handle = OdeFunctionOf(env, solver, jacobianValue, line, col);
                jacobianFunction = (t, y) => SquareRect("Jacobian",
                    handle.Call([JgsValue.Number(t), JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1])], line, col),
                    line, col);
            }
            else
            {
                jacobian = SquareRect("Jacobian", jacobianValue, line, col);
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
            MassStronglyStateDependent = massStronglyStateDependent,
            MassVectorPattern = stiff && Field("MvPattern") is { } mv
                ? PatternOf("MvPattern", mv, states, line, col)
                : null,
            MassSingular = massSingular,
            InitialSlopeGuess = stiff && Field("InitialSlope") is { } slope0
                ? ToDoubles("odeset", slope0, line, col)
                : null,
            Jacobian = jacobian,
            JacobianFunction = jacobianFunction,
            JacobianConstant = Flag("JConstant"),
            JacobianPattern = stiff && Field("JPattern") is { } jp
                ? PatternOf("JPattern", jp, states, line, col)
                : null,
            Vectorized = stiff && Flag("Vectorized"),
            Bdf = Flag("BDF"),
            MaxOrder = Number("MaxOrder") is { } order ? (int)order : null,
            Stats = Flag("Stats"),
            Warn = message => Warn(env, host, message, line, col),
            Print = text => host.print(text.TrimEnd('\n')),
            RecordSteps = asSolution,
            CollectOutput = !asSolution,
        };
    }

    /// <summary>A sparsity pattern out of an option: every nonzero entry is a place the matrix may fill.</summary>
    private static bool[,] PatternOf(string option, JgsValue value, int states, int line, int col)
    {
        double[] entries = ToDoubles("odeset", value, line, col);
        int[] dims = value.Dims;
        int rows = dims.Length > 0 ? dims[0] : 0;
        int columns = rows > 0 ? entries.Length / rows : 0;
        if (rows != states || columns != states)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:odeset:{option}Invalid",
                $"{option} must be a {states}-by-{states} matrix of zeros and ones.");
        }

        var pattern = new bool[rows, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                pattern[r, c] = entries[r + (c * rows)] != 0;
            }
        }

        return pattern;
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
        bool implicitSolver = solver == "ode15i";
        if (!implicitSolver && Array.IndexOf(OdeSolverNames, solver) < 0)
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

        // A fully implicit continuation needs a consistent pair, so its state argument is the two
        // columns [y0 yp0] and its default slope is the one the run it continues ended at.
        double[] y0;
        double[] yp0 = [];
        if (args.Count > 3 && !(args[3].Type == JgsType.Array && args[3].ArrayLength == 0))
        {
            double[] supplied = ToDoubles("odextend", args[3], line, col);
            if (implicitSolver)
            {
                if (supplied.Length != 2 * states)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:odextend:BadInitialState",
                        $"odextend: an ode15i solution is continued from a {states}-by-2 array [y0 yp0].");
                }

                y0 = supplied[..states];
                yp0 = supplied[states..];
            }
            else
            {
                y0 = supplied;
            }
        }
        else
        {
            y0 = new double[states];
            for (int i = 0; i < states; i++)
            {
                y0[i] = yFlat[i + ((mesh - 1) * states)];
            }

            if (implicitSolver)
            {
                yp0 = extdata.Type == JgsType.Struct && extdata.AsStruct.TryGetValue("ypfinal", out JgsValue? kept0)
                    ? ToDoubles("odextend", kept0, line, col)
                    : throw new JgsRuntimeException(line, col, "MATLAB:odextend:NoYpFinal",
                        "odextend: the ode15i solution carries no final slope to continue from.");
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

        OdeResult result;
        if (implicitSolver)
        {
            ImplicitOdeFunction residual = ImplicitResidualOf(f, solver, states, line, col);
            ImplicitOdeOptions settings = ImplicitOptionsFrom(env, host, options, states, statement: false,
                asSolution: true, line, col);
            try
            {
                result = Ode15i.Run(residual, [last, tFinal], y0, yp0, settings);
            }
            catch (OdeArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
            }
        }
        else
        {
            result = RunOdeSolver(env, host, solver, f, [last, tFinal], y0, options,
                asSolution: true, statement: false, line, col);
        }

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
