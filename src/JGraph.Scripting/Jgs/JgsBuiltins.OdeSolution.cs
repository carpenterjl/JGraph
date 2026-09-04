using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The solution structure a solver answers when asked for one output, and <c>deval</c>, which
/// reads it (M123, generalised to the whole explicit family in M125).
/// </summary>
/// <remarks>
/// <para>
/// A solver that returns two arrays has answered a table of times. MATLAB's one-output form answers
/// a <em>solution</em>: a structure that remembers the polynomial the method carried across every
/// step it took, so the answer can be asked for at a time nobody named while it was running. That
/// is why <c>sol.x</c> holds fewer points than <c>[t, y]</c> does for the same call, and why it is
/// not a coarser answer: the missing points are still available, they are just not computed until
/// somebody asks.
/// </para>
/// <para>
/// The structure is MATLAB's own, field for field, and so is what <c>idata</c> carries for each
/// solver: the stage slopes of every step as an n-by-stages-by-mesh array for the Runge–Kutta
/// pairs, the modified divided differences and the step history for <c>ode113</c>. The first page
/// of each belongs to the initial point and is zero, exactly as MATLAB leaves it, because
/// <c>deval</c> reaches for the page of the step that <em>ends</em> at a mesh point. Keeping the
/// data there rather than in a handle beside the structure is what makes a solution an ordinary
/// value: it can be saved, loaded, passed to a function and read by a script that has never heard
/// of this file — and <c>deval</c> rebuilds every step from those fields alone.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers <c>deval</c>; the solvers register themselves beside <c>odextend</c>.</summary>
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

    /// <summary>The solution structure for a run that has finished, in MATLAB's own shape.</summary>
    /// <param name="solver">Which solver ran.</param>
    /// <param name="odefun">The function that was integrated, kept as <c>extdata.odefun</c>.</param>
    /// <param name="options">The options structure, or null when the call gave none.</param>
    /// <param name="result">What the run answered, its steps recorded.</param>
    /// <param name="t0">Where it started.</param>
    /// <param name="y0">The state it started from.</param>
    internal static JgsValue OdeSolution(string solver, JgsValue odefun, JgsValue? options, OdeResult result,
        double t0, double[] y0)
    {
        int states = y0.Length;
        IReadOnlyList<OdeStepRecord> steps = result.Steps;
        int mesh = steps.Count + 1;
        JgsValue x = JgsMatrix.Build(1, mesh, (_, c) => c == 0 ? t0 : steps[c - 1].End);
        JgsValue y = JgsMatrix.Build(states, mesh, (r, c) => c == 0 ? y0[r] : steps[c - 1].EndState[r]);

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["solver"] = JgsValue.Str(solver),
            ["extdata"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["odefun"] = odefun,
                ["options"] = options ?? JgsValue.Array([]),
                ["varargin"] = JgsValue.Cell([]),
            }),
            ["x"] = x,
            ["y"] = y,
        };

        if (result.HadEvents)
        {
            int found = result.EventTimes.Count;
            fields["xe"] = JgsMatrix.Build(found == 0 ? 0 : 1, found, (_, c) => result.EventTimes[c]);
            fields["ye"] = JgsMatrix.Build(found == 0 ? 0 : states, found, (r, c) => result.EventStates[c][r]);
            fields["ie"] = JgsMatrix.Build(found == 0 ? 0 : 1, found, (_, c) => result.EventIndices[c] + 1);
        }

        fields["stats"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["nsteps"] = JgsValue.Number(result.StepCount),
            ["nfailed"] = JgsValue.Number(result.Failed),
            ["nfevals"] = JgsValue.Number(result.Evaluations),
            ["tfinal"] = JgsValue.Number(result.FinalTime),
        });

        JgsValue nonNegative = options is not null
            && TryReadField(options, "NonNegative", out JgsValue given) && !IsUnsetOption(given)
                ? given
                : JgsValue.Array([]);

        var idata = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (solver == AdamsPece.Name)
        {
            // klastvec, phi3d and psi2d, trimmed to the highest order the run reached, as
            // odefinalize trims them.
            int kmax = 0;
            foreach (OdeStepRecord step in steps)
            {
                kmax = System.Math.Max(kmax, step.Order);
            }

            var klast = new double[mesh];
            var phi = new double[states * (kmax + 1) * mesh];
            var psi = new double[kmax * mesh];
            for (int page = 1; page < mesh; page++)
            {
                OdeStepRecord step = steps[page - 1];
                klast[page] = step.Order;
                for (int c = 0; c <= kmax; c++)
                {
                    for (int r = 0; r < states; r++)
                    {
                        phi[r + (c * states) + (page * states * (kmax + 1))] = step.Stages[c][r];
                    }
                }

                for (int c = 0; c < kmax; c++)
                {
                    psi[c + (page * kmax)] = step.Psi![c];
                }
            }

            idata["klastvec"] = JgsMatrix.FromColumnMajorDims(klast, [1, mesh]);
            idata["phi3d"] = JgsMatrix.FromColumnMajorDims(phi, [states, kmax + 1, mesh]);
            idata["psi2d"] = JgsMatrix.FromColumnMajorDims(psi, [kmax, mesh]);
        }
        else
        {
            int width = RungeKuttaScheme.Named(solver)!.InterpolationStages.Length;
            var f3d = new double[states * width * mesh];
            for (int page = 1; page < mesh; page++)
            {
                double[][] stages = steps[page - 1].Stages;
                for (int stage = 0; stage < width; stage++)
                {
                    for (int r = 0; r < states; r++)
                    {
                        f3d[r + (stage * states) + (page * states * width)] = stages[stage][r];
                    }
                }
            }

            idata["f3d"] = JgsMatrix.FromColumnMajorDims(f3d, [states, width, mesh]);
        }

        idata["idxNonNegative"] = nonNegative;
        fields["idata"] = JgsValue.Struct(idata);
        return JgsValue.Struct(fields);
    }

    /// <summary>
    /// Two solutions of the same solver, the second starting where the first ended, as one:
    /// <c>odextend</c>'s join. A second solution that starts from the first's own last state
    /// continues it smoothly and drops its duplicate first point; one started from a new state
    /// keeps both, so the mesh shows the jump.
    /// </summary>
    private static JgsValue JoinedSolutions(string solver, JgsValue first, JgsValue second, int states, int line, int col)
    {
        Dictionary<string, JgsValue> a = first.AsStruct;
        Dictionary<string, JgsValue> b = second.AsStruct;
        double[] xa = ToDoubles("odextend", a["x"], line, col);
        double[] xb = ToDoubles("odextend", b["x"], line, col);
        double[] ya = ToDoubles("odextend", a["y"], line, col);
        double[] yb = ToDoubles("odextend", b["y"], line, col);
        int meshA = xa.Length;
        int meshB = xb.Length;

        bool smooth = true;
        for (int r = 0; r < states; r++)
        {
            if (ya[r + ((meshA - 1) * states)] != yb[r])
            {
                smooth = false;
                break;
            }
        }

        int from = smooth ? 1 : 0;
        int mesh = meshA + meshB - from;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["solver"] = b["solver"],
            ["extdata"] = b["extdata"],
            ["x"] = JgsMatrix.Build(1, mesh, (_, c) => c < meshA ? xa[c] : xb[c - meshA + from]),
            ["y"] = JgsMatrix.Build(states, mesh, (r, c) =>
                c < meshA ? ya[r + (c * states)] : yb[r + ((c - meshA + from) * states)]),
        };

        bool eventsA = a.ContainsKey("xe");
        bool eventsB = b.ContainsKey("xe");
        if (eventsA || eventsB)
        {
            double[] xe = [.. eventsA ? ToDoubles("odextend", a["xe"], line, col) : [], .. eventsB ? ToDoubles("odextend", b["xe"], line, col) : []];
            double[] ye = [.. eventsA ? ToDoubles("odextend", a["ye"], line, col) : [], .. eventsB ? ToDoubles("odextend", b["ye"], line, col) : []];
            double[] ie = [.. eventsA ? ToDoubles("odextend", a["ie"], line, col) : [], .. eventsB ? ToDoubles("odextend", b["ie"], line, col) : []];
            int found = xe.Length;
            fields["xe"] = JgsMatrix.FromColumnMajorDims(xe, [found == 0 ? 0 : 1, found]);
            fields["ye"] = JgsMatrix.FromColumnMajorDims(ye, [found == 0 ? 0 : states, found]);
            fields["ie"] = JgsMatrix.FromColumnMajorDims(ie, [found == 0 ? 0 : 1, found]);
        }

        Dictionary<string, JgsValue> statsA = a["stats"].AsStruct;
        Dictionary<string, JgsValue> statsB = b["stats"].AsStruct;
        double Stat(Dictionary<string, JgsValue> stats, string name) =>
            stats.TryGetValue(name, out JgsValue? value) ? value.AsNumber : 0;
        fields["stats"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["nsteps"] = JgsValue.Number(Stat(statsA, "nsteps") + Stat(statsB, "nsteps")),
            ["nfailed"] = JgsValue.Number(Stat(statsA, "nfailed") + Stat(statsB, "nfailed")),
            ["nfevals"] = JgsValue.Number(Stat(statsA, "nfevals") + Stat(statsB, "nfevals")),
            ["tfinal"] = JgsValue.Number(Stat(statsB, "tfinal")),
        });

        Dictionary<string, JgsValue> idataA = a["idata"].AsStruct;
        Dictionary<string, JgsValue> idataB = b["idata"].AsStruct;
        var idata = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (solver == AdamsPece.Name)
        {
            double[] ka = ToDoubles("odextend", idataA["klastvec"], line, col);
            double[] kb = ToDoubles("odextend", idataB["klastvec"], line, col);
            int[] psiDimsA = idataA["psi2d"].Dims;
            int[] psiDimsB = idataB["psi2d"].Dims;
            int kA = psiDimsA[0];
            int kB = psiDimsB[0];
            int kmax = System.Math.Max(kA, kB);
            double[] phiA = ToDoubles("odextend", idataA["phi3d"], line, col);
            double[] phiB = ToDoubles("odextend", idataB["phi3d"], line, col);
            double[] psiA = ToDoubles("odextend", idataA["psi2d"], line, col);
            double[] psiB = ToDoubles("odextend", idataB["psi2d"], line, col);

            var klast = new double[mesh];
            var phi = new double[states * (kmax + 1) * mesh];
            var psi = new double[kmax * mesh];
            for (int page = 0; page < mesh; page++)
            {
                bool fromA = page < meshA;
                int source = fromA ? page : page - meshA + from;
                klast[page] = fromA ? ka[source] : kb[source];
                int width = fromA ? kA + 1 : kB + 1;
                double[] phiSource = fromA ? phiA : phiB;
                for (int c = 0; c < width && c <= kmax; c++)
                {
                    for (int r = 0; r < states; r++)
                    {
                        phi[r + (c * states) + (page * states * (kmax + 1))] = phiSource[r + (c * states) + (source * states * width)];
                    }
                }

                int rows = fromA ? kA : kB;
                double[] psiSource = fromA ? psiA : psiB;
                for (int c = 0; c < rows; c++)
                {
                    psi[c + (page * kmax)] = psiSource[c + (source * rows)];
                }
            }

            idata["klastvec"] = JgsMatrix.FromColumnMajorDims(klast, [1, mesh]);
            idata["phi3d"] = JgsMatrix.FromColumnMajorDims(phi, [states, kmax + 1, mesh]);
            idata["psi2d"] = JgsMatrix.FromColumnMajorDims(psi, [kmax, mesh]);
        }
        else
        {
            int width = RungeKuttaScheme.Named(solver)!.InterpolationStages.Length;
            double[] fa = ToDoubles("odextend", idataA["f3d"], line, col);
            double[] fb = ToDoubles("odextend", idataB["f3d"], line, col);
            int page = states * width;
            var f3d = new double[page * mesh];
            Array.Copy(fa, 0, f3d, 0, System.Math.Min(fa.Length, page * meshA));
            Array.Copy(fb, from * page, f3d, meshA * page, System.Math.Min(fb.Length - (from * page), (meshB - from) * page));
            idata["f3d"] = JgsMatrix.FromColumnMajorDims(f3d, [states, width, mesh]);
        }

        idata["idxNonNegative"] = idataB.TryGetValue("idxNonNegative", out JgsValue? nn) ? nn : JgsValue.Array([]);
        fields["idata"] = JgsValue.Struct(idata);
        return JgsValue.Struct(fields);
    }

    // --- deval ------------------------------------------------------------------------------

    /// <summary>A solution structure taken apart into what its solver's interpolant reads.</summary>
    private sealed class OdeSolutionData
    {
        public required string Solver { get; init; }
        public required double[] Times { get; init; }
        public required double[] States { get; init; }   // column-major, n by mesh
        public required int N { get; init; }
        public RungeKuttaScheme? Scheme { get; init; }
        public double[]? Stages { get; init; }            // f3d, column-major
        public int StageWidth { get; init; }
        public double[]? Orders { get; init; }            // klastvec
        public double[]? Phi { get; init; }               // phi3d, column-major
        public int PhiWidth { get; init; }
        public double[]? Psi { get; init; }               // psi2d, column-major
        public int PsiRows { get; init; }
        public int[]? NonNegative { get; init; }

        public int Mesh => Times.Length;

        public double[] StateAt(int mesh)
        {
            var state = new double[N];
            Array.Copy(States, mesh * N, state, 0, N);
            return state;
        }

        /// <summary>The solution inside step <paramref name="step"/> (from mesh point step to step + 1) at <paramref name="at"/>.</summary>
        public double[] Read(int step, double at, double[]? slope)
        {
            if (Scheme is { } scheme)
            {
                var stages = new double[StageWidth][];
                for (int j = 0; j < StageWidth; j++)
                {
                    var stage = new double[N];
                    int offset = (j * N) + ((step + 1) * N * StageWidth);
                    for (int r = 0; r < N; r++)
                    {
                        stage[r] = offset + r < Stages!.Length ? Stages[offset + r] : 0;
                    }

                    stages[j] = stage;
                }

                return scheme.Interpolate(Times[step], Times[step + 1] - Times[step], StateAt(step), stages, at, slope, NonNegative);
            }

            int page = step + 1;
            int order = (int)Orders![page];
            var phi = new double[15][];
            for (int c = 0; c < 15; c++)
            {
                phi[c] = new double[N];
                if (c >= 1 && c <= PhiWidth)
                {
                    int offset = ((c - 1) * N) + (page * N * PhiWidth);
                    for (int r = 0; r < N; r++)
                    {
                        phi[c][r] = offset + r < Phi!.Length ? Phi[offset + r] : 0;
                    }
                }
            }

            var psi = new double[13];
            for (int c = 1; c <= PsiRows && c <= 12; c++)
            {
                int offset = (c - 1) + (page * PsiRows);
                psi[c] = offset < Psi!.Length ? Psi[offset] : 0;
            }

            return AdamsPece.Interpolant(at, Times[page], StateAt(page), order, phi, psi, slope, NonNegative);
        }
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

        OdeSolutionData data = SolutionDataOf(solution, line, col);
        double[] wanted = ToDoubles("deval", at, line, col);
        int n = data.N;

        int[]? rows = null;
        if (args.Count == 3)
        {
            double[] picked = ToDoubles("deval", args[2], line, col);
            rows = new int[picked.Length];
            for (int i = 0; i < picked.Length; i++)
            {
                int index = (int)picked[i];
                if (index != picked[i] || index < 1 || index > n)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:deval:IDXInvalidSolComp",
                        $"deval: component {picked[i]} is outside the solution's {n} component(s).");
                }

                rows[i] = index - 1;
            }
        }

        double[] t = data.Times;
        int mesh = data.Mesh;
        double direction = System.Math.Sign(t[^1] - t[0]);
        foreach (double time in wanted)
        {
            if (double.IsNaN(time) || direction * (time - t[0]) < 0 || direction * (time - t[^1]) > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:deval:SolOutsideInterval",
                    $"Attempting to evaluate the solution outside the interval [{t[0]:E6}, {t[^1]:E6}] where it is defined.");
            }
        }

        int height = rows?.Length ?? n;
        var values = new double[height * wanted.Length];
        var slopes = new double[height * wanted.Length];
        for (int c = 0; c < wanted.Length; c++)
        {
            double time = wanted[c];
            var slope = new double[n];
            double[] state;

            // The mesh point at or before the time, in the direction the solution runs.
            int bottom = 0;
            for (int i = 0; i < mesh; i++)
            {
                if (direction * (t[i] - time) <= 0)
                {
                    bottom = i;
                }
                else
                {
                    break;
                }
            }

            if (t[bottom] == time)
            {
                if (bottom == mesh - 1)
                {
                    // The last mesh point is the end of the last step, and the step is read there.
                    state = data.StateAt(bottom);
                    if (mesh > 1)
                    {
                        data.Read(bottom - 1, time, slope);
                    }
                }
                else if (bottom > 0 && t[bottom] == t[bottom - 1])
                {
                    // An interface point: two states at one time, from a solution extended with a
                    // jump. MATLAB answers their average and says so.
                    double[] left = data.StateAt(bottom - 1);
                    double[] right = data.StateAt(bottom);
                    state = new double[n];
                    bool differ = false;
                    for (int r = 0; r < n; r++)
                    {
                        state[r] = (left[r] + right[r]) / 2;
                        differ |= left[r] != right[r];
                    }

                    var slopeLeft = new double[n];
                    var slopeRight = new double[n];
                    if (bottom >= 2)
                    {
                        data.Read(bottom - 2, time, slopeLeft);
                    }

                    data.Read(bottom, time, slopeRight);
                    for (int r = 0; r < n; r++)
                    {
                        slope[r] = (slopeLeft[r] + slopeRight[r]) / 2;
                    }

                    // MATLAB warns here that the solution is not unique; the average is still the
                    // answer, and differ is what the warning would have said.
                    _ = differ;
                }
                else
                {
                    state = data.StateAt(bottom);
                    data.Read(bottom, time, slope);
                }
            }
            else
            {
                state = data.Read(bottom, time, slope);
            }

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

    /// <summary>
    /// The pieces of a solution structure its solver's interpolant reads, rebuilt from the
    /// structure's own fields so that a solution saved to a file and loaded again reads the same.
    /// </summary>
    private static OdeSolutionData SolutionDataOf(JgsValue solution, int line, int col)
    {
        if (solution.Type != JgsType.Struct
            || !solution.AsStruct.TryGetValue("x", out JgsValue? x)
            || !solution.AsStruct.TryGetValue("y", out JgsValue? y))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:deval:SolNotFromDiffEqSolver",
                "deval expects a solution structure from a solver called with one output, "
                + "such as sol = ode45(f, tspan, y0).");
        }

        if (!solution.AsStruct.TryGetValue("solver", out JgsValue? solverValue) || !IsTextScalar(solverValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:deval:NoSolverInStruct",
                "deval: the solution structure does not say which solver made it.");
        }

        string solver = TextOf(solverValue);
        RungeKuttaScheme? scheme = RungeKuttaScheme.Named(solver);
        if (scheme is null && solver != AdamsPece.Name)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:deval:InvalidSolver",
                $"deval cannot read a solution from '{solver}'.");
        }

        double[] times = ToDoubles("deval", x, line, col);
        double[] states = ToDoubles("deval", y, line, col);
        int mesh = times.Length;
        int n = mesh > 0 ? states.Length / mesh : 0;
        if (mesh < 1 || n < 1)
        {
            throw new JgsRuntimeException(line, col, "deval: this solution has no points to read.");
        }

        if (!solution.AsStruct.TryGetValue("idata", out JgsValue? idata) || idata.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:deval:SolNotFromDiffEqSolver",
                "deval: the solution structure carries no interpolation data.");
        }

        int[]? nonNegative = null;
        if (idata.AsStruct.TryGetValue("idxNonNegative", out JgsValue? nn) && nn.Type == JgsType.Array && nn.ArrayLength > 0)
        {
            double[] given = ToDoubles("deval", nn, line, col);
            nonNegative = new int[given.Length];
            for (int i = 0; i < given.Length; i++)
            {
                nonNegative[i] = (int)given[i] - 1;
            }
        }

        if (scheme is not null)
        {
            if (!idata.AsStruct.TryGetValue("f3d", out JgsValue? f3d))
            {
                throw new JgsRuntimeException(line, col, "deval: the solution structure has no f3d to read.");
            }

            int[] dims = f3d.Dims;
            return new OdeSolutionData
            {
                Solver = solver,
                Times = times,
                States = states,
                N = n,
                Scheme = scheme,
                Stages = ToDoubles("deval", f3d, line, col),
                StageWidth = dims.Length > 1 ? dims[1] : scheme.InterpolationStages.Length,
                NonNegative = nonNegative,
            };
        }

        if (!idata.AsStruct.TryGetValue("klastvec", out JgsValue? klast)
            || !idata.AsStruct.TryGetValue("phi3d", out JgsValue? phi)
            || !idata.AsStruct.TryGetValue("psi2d", out JgsValue? psi))
        {
            throw new JgsRuntimeException(line, col, "deval: the ode113 solution structure is missing its differences.");
        }

        int[] phiDims = phi.Dims;
        int[] psiDims = psi.Dims;
        return new OdeSolutionData
        {
            Solver = solver,
            Times = times,
            States = states,
            N = n,
            Orders = ToDoubles("deval", klast, line, col),
            Phi = ToDoubles("deval", phi, line, col),
            PhiWidth = phiDims.Length > 1 ? phiDims[1] : 1,
            Psi = ToDoubles("deval", psi, line, col),
            PsiRows = psiDims[0],
            NonNegative = nonNegative,
        };
    }
}
