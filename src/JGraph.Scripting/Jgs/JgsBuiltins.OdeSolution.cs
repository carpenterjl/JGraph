using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>ode45</c> asked for one output, and <c>deval</c> (M123).
/// </summary>
/// <remarks>
/// <para>
/// A solver that returns two arrays has answered a table of times. MATLAB's one-output form answers
/// a <em>solution</em>: a structure that remembers the polynomial the pair carried across every step
/// it took, so the answer can be asked for at a time nobody named while it was running. That is why
/// <c>sol.x</c> holds fewer points than <c>[t, y]</c> does for the same call — eleven against
/// forty-one at <c>Refine</c> 4 — and why it is not a coarser answer: the thirty missing points are
/// still available, they are just not computed until somebody asks.
/// </para>
/// <para>
/// The structure is MATLAB's own, field for field, including <c>idata.f3d</c> — the stage slopes of
/// every step, laid out as an n-by-7-by-steps array. Keeping them there rather than in a handle
/// beside the structure is what makes a solution an ordinary value: it can be saved, loaded, passed
/// to a function and read by a script that has never heard of this file.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers <c>deval</c>; <c>ode45</c>'s own registration is beside the solver.</summary>
    private static void RegisterOdeSolutionBuiltins(JgsEnvironment env)
    {
        env.Declare("deval", JgsValue.Function(new BuiltinFunction("deval", (args, line, col) =>
            Deval(args, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) =>
            {
                JgsValue[] both = Deval(args, line, col);
                return wanted >= 2 ? both : [both[0]];
            },
        }));
    }

    /// <summary>
    /// The solution structure for a run that has finished, in MATLAB's own shape.
    /// </summary>
    /// <param name="odefun">The function that was integrated, kept as <c>extdata.odefun</c>.</param>
    /// <param name="options">The options structure, or null when the call gave none.</param>
    /// <param name="points">The mesh: one point per accepted step, plus the start.</param>
    /// <param name="recording">The steps and what the run cost.</param>
    /// <param name="states">How many state variables the system has.</param>
    internal static JgsValue OdeSolution(
        JgsValue odefun,
        JgsValue? options,
        IReadOnlyList<OdeSolvers.OdePoint> points,
        OdeRecording recording,
        int states)
    {
        int mesh = points.Count;
        JgsValue x = JgsMatrix.Build(1, mesh, (_, c) => points[c].Time);
        JgsValue y = JgsMatrix.Build(states, mesh, (r, c) => points[c].State[r]);

        // The stage slopes, n by 7 by steps, in column-major order: page s is the step that ends at
        // x(s+1), which is the page deval reaches for when a wanted time falls inside it.
        int steps = recording.Steps.Count;
        var f3d = new double[states * 7 * System.Math.Max(steps, 0)];
        for (int s = 0; s < steps; s++)
        {
            double[][] stages = recording.Steps[s].Stages;
            for (int stage = 0; stage < 7; stage++)
            {
                for (int i = 0; i < states; i++)
                {
                    f3d[i + (stage * states) + (s * states * 7)] = stages[stage][i];
                }
            }
        }

        var stats = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["nsteps"] = JgsValue.Number(steps),
            ["nfailed"] = JgsValue.Number(recording.Failed),
            ["nfevals"] = JgsValue.Number(recording.Evaluations),
            ["tfinal"] = JgsValue.Number(mesh > 0 ? points[^1].Time : double.NaN),
        };

        var extdata = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["odefun"] = odefun,
            ["options"] = options ?? JgsValue.Array([]),
            ["varargin"] = JgsValue.Cell([]),
        };

        var idata = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["f3d"] = JgsMatrix.FromColumnMajorDims(f3d, [states, 7, System.Math.Max(steps, 1)]),
            ["idxNonNegative"] = JgsValue.Array([]),
        };

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["solver"] = JgsValue.Str("ode45"),
            ["extdata"] = JgsValue.Struct(extdata),
            ["x"] = x,
            ["y"] = y,
            ["stats"] = JgsValue.Struct(stats),
            ["idata"] = JgsValue.Struct(idata),
        });
    }

    /// <summary><c>deval(sol, t)</c>, <c>deval(sol, t, idx)</c> and the derivative in a second output.</summary>
    private static JgsValue[] Deval(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("deval", args, 2, 3, line, col);

        // MATLAB takes the two the other way round as well, because a solution is recognisable and a
        // time vector is not: deval(t, sol) is the same call.
        JgsValue solution = args[0];
        JgsValue at = args[1];
        if (solution.Type != JgsType.Struct && at.Type == JgsType.Struct)
        {
            (solution, at) = (at, solution);
        }

        IReadOnlyList<OdeSolvers.OdeStep> steps = StepsOf(solution, line, col);
        double[] wanted = ToDoubles("deval", at, line, col);

        int states = steps.Count > 0 ? steps[0].State.Length : 0;
        int[]? rows = null;
        if (args.Count == 3)
        {
            double[] picked = ToDoubles("deval", args[2], line, col);
            rows = new int[picked.Length];
            for (int i = 0; i < picked.Length; i++)
            {
                int index = (int)picked[i];
                if (index < 1 || index > states)
                {
                    throw new JgsRuntimeException(line, col,
                        $"deval: component {index} is outside the solution's {states} state(s).");
                }

                rows[i] = index - 1;
            }
        }

        int height = rows?.Length ?? states;
        var values = new double[height * wanted.Length];
        var slopes = new double[height * wanted.Length];
        for (int c = 0; c < wanted.Length; c++)
        {
            double[] state = OdeSolvers.Interpolate(steps, wanted[c], out double[] slope);
            for (int r = 0; r < height; r++)
            {
                int from = rows is null ? r : rows[r];
                values[r + (c * height)] = state[from];
                slopes[r + (c * height)] = slope[from];
            }
        }

        return
        [
            JgsMatrix.FromColumnMajorDims(values, [height, wanted.Length]),
            JgsMatrix.FromColumnMajorDims(slopes, [height, wanted.Length]),
        ];
    }

    /// <summary>The steps a solution structure describes, rebuilt from its own fields.</summary>
    /// <remarks>
    /// Read back out of the structure rather than kept alongside it, so a solution that has been
    /// saved to a file and loaded again reads exactly the same. The step length is the gap between
    /// two mesh times and the state is the column at the step's start, which is all the continuous
    /// extension needs beyond the stages themselves.
    /// </remarks>
    private static List<OdeSolvers.OdeStep> StepsOf(JgsValue solution, int line, int col)
    {
        if (solution.Type != JgsType.Struct
            || !solution.AsStruct.TryGetValue("x", out JgsValue? x)
            || !solution.AsStruct.TryGetValue("y", out JgsValue? y)
            || !solution.AsStruct.TryGetValue("idata", out JgsValue? idata)
            || idata.Type != JgsType.Struct
            || !idata.AsStruct.TryGetValue("f3d", out JgsValue? f3d))
        {
            throw new JgsRuntimeException(line, col,
                "deval expects a solution structure from a solver called with one output, "
                + "such as sol = ode45(f, tspan, y0).");
        }

        double[] times = ToDoubles("deval", x, line, col);
        double[] states = ToDoubles("deval", y, line, col);
        double[] stages = ToDoubles("deval", f3d, line, col);
        int mesh = times.Length;
        int n = mesh > 0 ? states.Length / System.Math.Max(mesh, 1) : 0;
        if (mesh < 2 || n < 1)
        {
            throw new JgsRuntimeException(line, col, "deval: this solution has no steps to read.");
        }

        var steps = new List<OdeSolvers.OdeStep>(mesh - 1);
        for (int s = 0; s < mesh - 1; s++)
        {
            var stage = new double[7][];
            for (int j = 0; j < 7; j++)
            {
                var row = new double[n];
                for (int i = 0; i < n; i++)
                {
                    int flat = i + (j * n) + (s * n * 7);
                    row[i] = flat < stages.Length ? stages[flat] : 0;
                }

                stage[j] = row;
            }

            var start = new double[n];
            for (int i = 0; i < n; i++)
            {
                start[i] = states[i + (s * n)];
            }

            steps.Add(new OdeSolvers.OdeStep(times[s], times[s + 1] - times[s], start, stage));
        }

        return steps;
    }
}
