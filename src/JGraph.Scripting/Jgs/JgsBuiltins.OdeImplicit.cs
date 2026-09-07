using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The fully implicit pair (M126): <c>ode15i</c>, which integrates <c>f(t, y, y') = 0</c>, and
/// <c>decic</c>, which finds the consistent starting point it needs.
/// </summary>
/// <remarks>
/// <para>
/// These are apart from the rest of the family because the equation is: the residual takes three
/// arguments rather than two, the solver takes five rather than four, and the options carry each of
/// <c>Jacobian</c>, <c>JPattern</c> and <c>Vectorized</c> as a two-element cell — one half for
/// <c>df/dy</c> and one for <c>df/dy'</c>. Everything downstream of a step is shared: the same
/// output modes, the same event locator, the same solution structure, the same <c>deval</c>.
/// </para>
/// <para>
/// The event function sees three arguments too. Its slope at a time inside a step is read off the
/// step's own interpolant, which is what makes an event on <c>y'</c> — a velocity reaching zero,
/// say — locatable to the accuracy of the bracketing search rather than to the accuracy of a
/// difference quotient.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers <c>ode15i</c> and <c>decic</c>.</summary>
    internal static void RegisterOdeImplicitBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        env.DeclareFunction("ode15i", JgsValue.Function(new BuiltinFunction("ode15i",
            (args, line, col) => SolveImplicitOde(env, host, args, 1, line, col)[0])
        {
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => SolveImplicitOde(env, host, args, wanted, line, col),
        }));

        env.DeclareFunction("decic", JgsValue.Function(new BuiltinFunction("decic",
            (args, line, col) => Decic(env, host, args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Decic(env, host, args, wanted, line, col),
        }));
    }

    /// <summary>
    /// <c>[t, y] = ode15i(odefun, tspan, y0, yp0, options)</c>, the five-output event form, and the
    /// solution structure for one output.
    /// </summary>
    private static JgsValue[] SolveImplicitOde(JgsEnvironment env, JGraphScriptGlobals host,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("ode15i", args, 4, 5, line, col);
        IJgsCallable f = OdeFunctionOf(env, "ode15i", args[0], line, col);
        double[] tspan = ToDoubles("ode15i", args[1], line, col);
        double[] initial = ToDoubles("ode15i", args[2], line, col);
        double[] initialSlope = ToDoubles("ode15i", args[3], line, col);
        JgsValue? options = args.Count > 4 && args[4].Type == JgsType.Struct ? args[4] : null;
        if (args.Count > 4 && options is null && !(args[4].Type == JgsType.Array && args[4].ArrayLength == 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odearguments:OptionsNotStruct",
                "ode15i: the fifth argument must be an options structure created with odeset, or [].");
        }

        int states = initial.Length;
        bool statement = wanted == 0;
        bool asSolution = wanted == 1;
        ImplicitOdeFunction residual = ImplicitResidualOf(f, "ode15i", states, line, col);
        ImplicitOdeOptions settings = ImplicitOptionsFrom(env, host, options, states, statement, asSolution,
            line, col);

        OdeResult result;
        try
        {
            result = Ode15i.Run(residual, tspan, initial, initialSlope, settings);
        }
        catch (OdeArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new JgsRuntimeException(line, col, $"ode15i: {ex.Message}");
        }

        if (statement)
        {
            return [];
        }

        if (asSolution)
        {
            return [OdeSolution("ode15i", args[0], options, result, tspan[0], initial)];
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

    /// <summary>
    /// <c>[y0, yp0] = decic(odefun, t0, y0, fixed_y0, yp0, fixed_yp0, options)</c>, with the residual
    /// norm in a third output.
    /// </summary>
    private static JgsValue[] Decic(JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        ArityRange("decic", args, 6, 7, line, col);
        IJgsCallable f = OdeFunctionOf(env, "decic", args[0], line, col);
        double t0 = Num("decic", args, 1, line, col);
        double[] y0 = ToDoubles("decic", args[2], line, col);
        bool[]? fixedY = PinnedOf("decic", args[3], y0.Length, line, col);
        double[] yp0 = ToDoubles("decic", args[4], line, col);
        bool[]? fixedYp = PinnedOf("decic", args[5], y0.Length, line, col);
        JgsValue? options = args.Count > 6 && args[6].Type == JgsType.Struct ? args[6] : null;

        int states = y0.Length;
        ImplicitOdeFunction residual = ImplicitResidualOf(f, "decic", states, line, col);
        ImplicitOdeOptions settings = ImplicitOptionsFrom(env, host, options, states, statement: false,
            asSolution: false, line, col);

        Numerics.Decic.Consistent answer;
        try
        {
            answer = Numerics.Decic.Solve(residual, t0, y0, fixedY, yp0, fixedYp, settings);
        }
        catch (OdeArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new JgsRuntimeException(line, col, $"decic: {ex.Message}");
        }

        JgsValue state = JgsMatrix.FromColumnMajorDims(answer.Y, [states, 1]);
        JgsValue slope = JgsMatrix.FromColumnMajorDims(answer.Yp, [states, 1]);
        return wanted >= 3
            ? [state, slope, JgsValue.Number(answer.ResidualNorm)]
            : [state, slope];
    }

    /// <summary>The script's three-argument residual, as the numerics layer's delegate.</summary>
    private static ImplicitOdeFunction ImplicitResidualOf(IJgsCallable f, string name, int states, int line, int col)
    {
        return (t, y, yp) =>
        {
            JgsValue state = JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1]);
            JgsValue slope = JgsMatrix.FromColumnMajorDims((double[])yp.Clone(), [states, 1]);
            double[] answer = ToDoubles(name, f.Call([JgsValue.Number(t), state, slope], line, col), line, col);
            if (answer.Length != states)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:odearguments:SizeIC",
                    $"{name}: the residual returned a vector of length {answer.Length}, but the length of the "
                    + $"initial conditions vector is {states}.");
            }

            return answer;
        };
    }

    /// <summary>A vector of zeros and ones read as which components may not move; empty is none.</summary>
    private static bool[]? PinnedOf(string name, JgsValue value, int states, int line, int col)
    {
        if (value.Type == JgsType.Array && value.ArrayLength == 0)
        {
            return null;
        }

        double[] given = ToDoubles(name, value, line, col);
        if (given.Length == 0)
        {
            return null;
        }

        if (given.Length != states)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:decic:FixedLength",
                $"{name}: fixed_y0 and fixed_yp0 must have {states} entries or be empty.");
        }

        var pinned = new bool[states];
        for (int i = 0; i < states; i++)
        {
            pinned[i] = given[i] != 0;
        }

        return pinned;
    }

    /// <summary>
    /// The options of a fully implicit problem: the shared ones through the ordinary reader, and the
    /// three that come as pairs read here.
    /// </summary>
    private static ImplicitOdeOptions ImplicitOptionsFrom(JgsEnvironment env, JGraphScriptGlobals host,
        JgsValue? options, int states, bool statement, bool asSolution, int line, int col)
    {
        OdeOptions common = OdeOptionsFrom(env, host, "ode15i", options, states, statement, asSolution, line, col);

        JgsValue? Field(string name) =>
            options is not null && TryReadField(options, name, out JgsValue value) && !IsUnsetOption(value)
                ? value
                : null;

        // Each of the three may be a two-element cell — one entry per partial derivative — or a
        // single value that stands for both, which is what a scalar 'Vectorized' means.
        (JgsValue? First, JgsValue? Second) Pair(string name)
        {
            JgsValue? value = Field(name);
            if (value is null)
            {
                return (null, null);
            }

            if (value.Type != JgsType.Cell)
            {
                return (value, value);
            }

            IReadOnlyList<JgsValue> cells = value.AsCell;
            JgsValue? first = cells.Count > 0 && !IsUnsetOption(cells[0]) ? cells[0] : null;
            JgsValue? second = cells.Count > 1 && !IsUnsetOption(cells[1]) ? cells[1] : null;
            return (first, second);
        }

        (JgsValue? jacobianY, JgsValue? jacobianYp) = Pair("Jacobian");
        (JgsValue? patternY, JgsValue? patternYp) = Pair("JPattern");
        (JgsValue? vectorY, JgsValue? vectorYp) = Pair("Vectorized");

        double[,]? matrixY = null;
        double[,]? matrixYp = null;
        Func<double, double[], double[], (double[,], double[,])>? jacobianFunction = null;
        if (jacobianY is { } given && (given.Type == JgsType.Function || IsTextScalar(given))
            && ReferenceEquals(jacobianY, jacobianYp))
        {
            // One handle answering both, which is the form the documentation shows.
            IJgsCallable handle = OdeFunctionOf(env, "ode15i", given, line, col);
            jacobianFunction = (t, y, yp) =>
            {
                JgsValue state = JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1]);
                JgsValue slope = JgsMatrix.FromColumnMajorDims((double[])yp.Clone(), [states, 1]);
                JgsValue[] answers = CallForOutputs(handle, [JgsValue.Number(t), state, slope], 2, line, col);
                return (SquareRect("Jacobian", answers[0], line, col),
                    SquareRect("Jacobian", answers[1], line, col));
            };
        }
        else
        {
            matrixY = jacobianY is not null ? SquareRect("Jacobian", jacobianY, line, col) : null;
            matrixYp = jacobianYp is not null ? SquareRect("Jacobian", jacobianYp, line, col) : null;
        }

        return new ImplicitOdeOptions
        {
            Common = common with { Events = null },
            JacobianY = matrixY,
            JacobianYp = matrixYp,
            JacobianFunction = jacobianFunction,
            PatternY = patternY is not null ? PatternOf("JPattern", patternY, states, line, col) : null,
            PatternYp = patternYp is not null ? PatternOf("JPattern", patternYp, states, line, col) : null,
            VectorizedY = vectorY is not null && Truth(vectorY),
            VectorizedYp = vectorYp is not null && Truth(vectorYp),
            JacobianConstant = common.JacobianConstant,
            Events = ImplicitEventsOf(env, host, options, states, line, col),
        };
    }

    /// <summary>The <c>Events</c> option as a three-argument reading of value, terminal and direction.</summary>
    private static ImplicitOdeEventFunction? ImplicitEventsOf(JgsEnvironment env, JGraphScriptGlobals host,
        JgsValue? options, int states, int line, int col)
    {
        _ = host;
        if (options is null || !TryReadField(options, "Events", out JgsValue value) || IsUnsetOption(value))
        {
            return null;
        }

        IJgsCallable handle = OdeFunctionOf(env, "ode15i", value, line, col);
        return (t, y, yp) =>
        {
            JgsValue state = JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1]);
            JgsValue slope = JgsMatrix.FromColumnMajorDims((double[])yp.Clone(), [states, 1]);
            JgsValue[] outputs = CallForOutputs(handle, [JgsValue.Number(t), state, slope], 3, line, col);
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
}
