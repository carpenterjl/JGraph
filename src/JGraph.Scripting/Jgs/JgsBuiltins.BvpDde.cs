using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The boundary-value and delay solvers: <c>bvp4c</c>, <c>bvp5c</c> and the three functions that
/// prepare a guess for them — <c>bvpinit</c>, <c>bvpxtend</c>, <c>bvpset</c>, <c>bvpget</c> — and
/// <c>dde23</c>, <c>ddesd</c>, <c>ddensd</c> with <c>ddeset</c> and <c>ddeget</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both families answer a solution structure that <c>deval</c> reads, and both build it out of the
/// same two Hermite polynomials — the cubic through two points and their slopes for <c>bvp4c</c> and
/// the three delay solvers, and the quartic that also passes through an interval's midpoint for
/// <c>bvp5c</c>. That is why they are registered together and why <c>deval</c> needs only two new
/// branches to read all five.
/// </para>
/// <para>
/// The adapter's whole job is argument shape. A boundary value problem's derivative is called
/// <c>f(x, y)</c>, or <c>f(x, y, region)</c> on a multipoint problem, or <c>f(x, y, p)</c> when there
/// are unknown parameters, or with both — and the boundary conditions see one column per region.
/// A delay problem's derivative is called <c>f(t, y, Z)</c> with one column of <c>Z</c> per delayed
/// argument, and a neutral one gets two such matrices. The numerics layer knows nothing of any of
/// this: it is handed delegates that already have the right shape.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The eight settings <c>bvpset</c> holds, in MATLAB's own field order.</summary>
    private static readonly string[] BvpsetFields =
    [
        "AbsTol", "RelTol", "SingularTerm", "FJacobian", "BCJacobian", "Stats", "Nmax", "Vectorized",
    ];

    /// <summary>The twelve settings <c>ddeset</c> holds, in MATLAB's own field order.</summary>
    private static readonly string[] DdesetFields =
    [
        "AbsTol", "Events", "InitialStep", "InitialY", "Jumps", "MaxStep", "NormControl",
        "OutputFcn", "OutputSel", "Refine", "RelTol", "Stats",
    ];

    /// <summary>Registers the two collocation solvers, the three delay solvers and their helpers.</summary>
    internal static void RegisterBvpDdeBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        env.Builtins.Register("bvpset", JgsValue.Function(new BuiltinFunction(
            "bvpset", (args, line, col) => OptionsStructure("bvpset", BvpsetFields, args, line, col))
        {
            AutoCallsBare = true,
        }));
        env.Builtins.Register("bvpget", JgsValue.Function(new BuiltinFunction("bvpget",
            (args, line, col) => OptionsSetting("bvpget", BvpsetFields, args, line, col))));
        env.Builtins.Register("ddeset", JgsValue.Function(new BuiltinFunction(
            "ddeset", (args, line, col) => OptionsStructure("ddeset", DdesetFields, args, line, col))
        {
            AutoCallsBare = true,
        }));
        env.Builtins.Register("ddeget", JgsValue.Function(new BuiltinFunction("ddeget",
            (args, line, col) => OptionsSetting("ddeget", DdesetFields, args, line, col))));

        env.Builtins.Register("bvpinit", JgsValue.Function(new BuiltinFunction("bvpinit",
            (args, line, col) => BvpInit(env, args, line, col))));
        env.Builtins.Register("bvpxtend", JgsValue.Function(new BuiltinFunction("bvpxtend",
            (args, line, col) => BvpExtend(args, line, col))));

        env.Builtins.Register("bvp4c", JgsValue.Function(new BuiltinFunction("bvp4c",
            (args, line, col) => SolveBvp(env, host, "bvp4c", args, line, col))));
        env.Builtins.Register("bvp5c", JgsValue.Function(new BuiltinFunction("bvp5c",
            (args, line, col) => SolveBvp(env, host, "bvp5c", args, line, col))));

        env.Builtins.Register("dde23", JgsValue.Function(new BuiltinFunction("dde23",
            (args, line, col) => SolveDde(env, host, "dde23", args, line, col))));
        env.Builtins.Register("ddesd", JgsValue.Function(new BuiltinFunction("ddesd",
            (args, line, col) => SolveDde(env, host, "ddesd", args, line, col))));
        env.Builtins.Register("ddensd", JgsValue.Function(new BuiltinFunction("ddensd",
            (args, line, col) => SolveDde(env, host, "ddensd", args, line, col))));
    }

    // --- the two options structures ---------------------------------------------------------------

    /// <summary><c>bvpset</c> and <c>ddeset</c>: every field present, every unset one <c>[]</c>.</summary>
    private static JgsValue OptionsStructure(string name, string[] names, IReadOnlyList<JgsValue> args,
        int line, int col)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in names)
        {
            fields[field] = JgsValue.Array([]);
        }

        int from = 0;
        while (from < args.Count && args[from].Type != JgsType.String && !IsTextScalar(args[from]))
        {
            if (args[from].Type == JgsType.Array && args[from].ArrayLength == 0)
            {
                from++;
                continue;
            }

            if (args[from].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:NoPropNameOrStruct",
                    $"Expected argument {from + 1} to be a property name or an options structure "
                    + $"created with {name.ToUpperInvariant()}.");
            }

            foreach (string field in names)
            {
                if (TryReadField(args[from], field, out JgsValue value) && !IsUnsetOption(value))
                {
                    fields[field] = value;
                }
            }

            from++;
        }

        if ((args.Count - from) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:ArgNameValueMismatch",
                "Arguments must occur in name-value pairs.");
        }

        for (int i = from; i < args.Count; i += 2)
        {
            fields[OptionNamed(name, names, TextArgument(name, args, i, line, col), line, col)] = args[i + 1];
        }

        return JgsValue.Struct(fields);
    }

    /// <summary><c>bvpget</c> and <c>ddeget</c>: one setting, or what to use instead.</summary>
    private static JgsValue OptionsSetting(string name, string[] names, IReadOnlyList<JgsValue> args,
        int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:NotEnoughInputs",
                "Not enough input arguments.");
        }

        JgsValue fallback = args.Count > 2 ? args[2] : JgsValue.Array([]);
        if (args[0].Type == JgsType.Array && args[0].ArrayLength == 0)
        {
            return fallback;
        }

        if (args[0].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:OptsNotStruct",
                $"First argument must be an options structure created with {name[..3].ToUpperInvariant()}SET.");
        }

        string field = OptionNamed(name, names, TextArgument(name, args, 1, line, col), line, col);
        return TryReadField(args[0], field, out JgsValue value) && !IsUnsetOption(value) ? value : fallback;
    }

    /// <summary>The full property name an abbreviation stands for, as the <c>odeset</c> family reads one.</summary>
    private static string OptionNamed(string caller, string[] names, string typed, int line, int col)
    {
        string wanted = typed.Trim();
        var matches = new List<string>();
        foreach (string field in names)
        {
            if (field.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(field);
            }
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        foreach (string field in matches)
        {
            if (string.Equals(field, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        throw matches.Count == 0
            ? new JgsRuntimeException(line, col, $"MATLAB:{caller}:InvalidPropName",
                $"Unrecognized property name '{wanted}'.")
            : new JgsRuntimeException(line, col, $"MATLAB:{caller}:AmbiguousPropName",
                $"Ambiguous property name '{wanted}' ({string.Join(", ", matches)}).");
    }

    // --- bvpinit and bvpxtend ----------------------------------------------------------------------

    /// <summary>
    /// <c>solinit = bvpinit(x, yinit)</c>, with the unknown parameters and the region argument a
    /// multipoint problem needs. The structure remembers the guess it was given, because
    /// <c>bvp5c</c> asks for the solution at points that are not mesh points.
    /// </summary>
    private static JgsValue BvpInit(JgsEnvironment env, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("bvpinit", args, 2, 16, line, col);
        if (args[0].Type == JgsType.Struct)
        {
            // The backwards-compatible form: a solution and a new interval, extended at both ends.
            double[] interval = ToDoubles("bvpinit", args[1], line, col);
            if (interval.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvpinit:NoSolInterval",
                    "bvpinit: the second argument must give both ends of the new interval.");
            }

            JgsValue solution = args[0];
            if (!solution.AsStruct.ContainsKey("solver"))
            {
                var copied = new Dictionary<string, JgsValue>(solution.AsStruct, StringComparer.Ordinal)
                {
                    ["solver"] = JgsValue.Str("bvp4c"),
                };
                solution = JgsValue.Struct(copied);
            }

            JgsValue[] first = args.Count > 2
                ? [solution, JgsValue.Number(interval[0]), JgsValue.Str("solution"), args[2]]
                : [solution, JgsValue.Number(interval[0]), JgsValue.Str("solution")];
            JgsValue extended = BvpExtend(first, line, col);
            JgsValue[] second = args.Count > 2
                ? [extended, JgsValue.Number(interval[^1]), JgsValue.Str("solution"), args[2]]
                : [extended, JgsValue.Number(interval[^1]), JgsValue.Str("solution")];
            extended = BvpExtend(second, line, col);

            var trimmed = new Dictionary<string, JgsValue>(extended.AsStruct, StringComparer.Ordinal);
            string solver = TextOf(extended.AsStruct["solver"]);
            trimmed.Remove("solver");
            trimmed.Remove(solver == "bvp5c" ? "idata" : "yp");
            return JgsValue.Struct(trimmed);
        }

        double[] mesh = ToDoubles("bvpinit", args[0], line, col);
        int count = mesh.Length;
        if (count > 0 && mesh[0] == mesh[^1])
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvpinit:XSameEndPts",
                "bvpinit: the end points of the mesh must differ.");
        }

        bool increasing = mesh[0] < mesh[^1];
        for (int i = 0; i + 1 < count; i++)
        {
            if (increasing ? mesh[i + 1] < mesh[i] : mesh[i + 1] > mesh[i])
            {
                throw new JgsRuntimeException(line, col,
                    increasing ? "MATLAB:bvpinit:IncreasingXNotMonotonic" : "MATLAB:bvpinit:DecreasingXNotMonotonic",
                    "bvpinit: the entries of the mesh must be ordered.");
            }
        }

        int[] interfaces = BoundaryValueSolvers.InterfacesOf(mesh);
        JgsValue guess = args[1];
        JgsValue[] extras = args.Count > 3 ? [.. args.Skip(3)] : [];

        double[] flat;
        int n;
        if (guess.Type == JgsType.Function || IsTextScalar(guess))
        {
            IJgsCallable handle = OdeFunctionOf(env, "bvpinit", guess, line, col);
            var columns = new double[count][];
            int region = 0;
            for (int i = 0; i < count; i++)
            {
                if (region < interfaces.Length && i > interfaces[region])
                {
                    region++;
                }

                JgsValue[] call = interfaces.Length > 0
                    ? [JgsValue.Number(mesh[i]), JgsValue.Number(region + 1), .. extras]
                    : [JgsValue.Number(mesh[i]), .. extras];
                columns[i] = ToDoubles("bvpinit", handle.Call(call, line, col), line, col);
            }

            n = columns[0].Length;
            flat = new double[n * count];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(columns[i], 0, flat, i * n, System.Math.Min(n, columns[i].Length));
            }
        }
        else
        {
            // A guess that is neither a row nor a column says nothing about which component is
            // which, and MATLAB refuses it rather than reading it in some order of its own.
            int[] dims = guess.Dims;
            if (guess.ArrayLength == 0 || (dims.Length > 1 && dims[0] != 1 && dims[1] != 1))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvpinit:SolGuessNotVector",
                    "bvpinit: the guess for the solution must be a vector.");
            }

            double[] constant = ToDoubles("bvpinit", guess, line, col);
            n = constant.Length;
            flat = new double[n * count];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(constant, 0, flat, i * n, n);
            }
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["solver"] = JgsValue.Str("bvpinit"),
            ["x"] = JgsMatrix.FromColumnMajorDims((double[])mesh.Clone(), [1, count]),
            ["y"] = JgsMatrix.FromColumnMajorDims(flat, [n, count]),
        };

        if (args.Count > 2 && !(args[2].Type == JgsType.Array && args[2].ArrayLength == 0))
        {
            fields["parameters"] = args[2];
        }

        fields["yinit"] = guess;
        return JgsValue.Struct(fields);
    }

    /// <summary>
    /// <c>solinit = bvpxtend(sol, xnew, ynew)</c>: the solution carried past one of its own ends,
    /// with the new value given outright or extrapolated three ways.
    /// </summary>
    private static JgsValue BvpExtend(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("bvpxtend", args, 2, 4, line, col);
        JgsValue sol = args[0];
        if (sol.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvpxtend:YnewIncorrectType",
                "bvpxtend expects a solution structure.");
        }

        var fields = new Dictionary<string, JgsValue>(sol.AsStruct, StringComparer.Ordinal);
        if (args.Count == 4)
        {
            fields["parameters"] = args[3];
        }

        double[] x = ToDoubles("bvpxtend", sol.AsStruct["x"], line, col);
        double[] y = ToDoubles("bvpxtend", sol.AsStruct["y"], line, col);
        int mesh = x.Length;
        int n = y.Length / mesh;
        double a = x[0];
        double b = x[^1];
        double xnew = Num("bvpxtend", args, 1, line, col);

        if (System.Math.Abs(xnew - a) <= 100 * Spacing(System.Math.Max(System.Math.Abs(xnew), System.Math.Abs(a))))
        {
            x[0] = xnew;
            fields["x"] = JgsMatrix.FromColumnMajorDims(x, [1, mesh]);
            return JgsValue.Struct(fields);
        }

        if (System.Math.Abs(xnew - b) <= 100 * Spacing(System.Math.Max(System.Math.Abs(xnew), System.Math.Abs(b))))
        {
            x[^1] = xnew;
            fields["x"] = JgsMatrix.FromColumnMajorDims(x, [1, mesh]);
            return JgsValue.Struct(fields);
        }

        bool forward = a < b;
        bool atLeft;
        if (forward)
        {
            if (xnew < a)
            {
                atLeft = true;
            }
            else if (xnew <= b)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvpxtend:XnewInsideInterval",
                    "bvpxtend: the new point must lie outside the interval the solution covers.");
            }
            else
            {
                atLeft = false;
            }
        }
        else
        {
            if (xnew > a)
            {
                atLeft = true;
            }
            else if (xnew >= b)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvpxtend:XnewInsideInterval",
                    "bvpxtend: the new point must lie outside the interval the solution covers.");
            }
            else
            {
                atLeft = false;
            }
        }

        string method = string.Empty;
        double[] given = [];
        if (args.Count < 3 || (args[2].Type == JgsType.Array && args[2].ArrayLength == 0))
        {
            method = "constant";
        }
        else if (IsTextScalar(args[2]))
        {
            string typed = TextOf(args[2]).Trim();
            string[] known = ["constant", "linear", "solution"];
            var matches = known.Where(m => m.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvpxtend:UnexpectedExtrapolationMethod",
                    $"bvpxtend: '{typed}' is not one of 'constant', 'linear' or 'solution'.");
            }

            method = matches[0];
        }
        else
        {
            given = ToDoubles("bvpxtend", args[2], line, col);
        }

        string solver = sol.AsStruct.TryGetValue("solver", out JgsValue? solverValue) && IsTextScalar(solverValue)
            ? TextOf(solverValue)
            : throw new JgsRuntimeException(line, col, "MATLAB:bvpxtend:NoSolverInSol",
                "bvpxtend: the solution structure does not say which solver made it.");
        bool five = solver == "bvp5c";
        double[] yp = five
            ? ToDoubles("bvpxtend", sol.AsStruct["idata"].AsStruct["yp"], line, col)
            : ToDoubles("bvpxtend", sol.AsStruct["yp"], line, col);
        double[] ymid = five
            ? ToDoubles("bvpxtend", sol.AsStruct["idata"].AsStruct["ymid"], line, col)
            : [];

        double[] Column(double[] flat, int index) => flat.AsSpan(index * n, n).ToArray();

        int edge = atLeft ? 0 : mesh - 1;
        int inner = atLeft ? 1 : mesh - 2;
        double[] value;
        double[] slope;
        switch (method)
        {
            case "":
                value = given;
                slope = new double[n];
                break;

            case "linear":
                value = new double[n];
                slope = Column(yp, edge);
                for (int r = 0; r < n; r++)
                {
                    value[r] = y[(edge * n) + r] + ((xnew - x[edge]) * slope[r]);
                }

                break;

            case "solution":
                (value, slope) = five
                    ? BoundaryValueSolvers.Hermite4(xnew, x[System.Math.Min(edge, inner)],
                        Column(y, System.Math.Min(edge, inner)), x[System.Math.Max(edge, inner)],
                        Column(y, System.Math.Max(edge, inner)), Column(ymid, System.Math.Min(edge, inner)),
                        Column(yp, System.Math.Min(edge, inner)), Column(yp, System.Math.Max(edge, inner)))
                    : BoundaryValueSolvers.Hermite3(xnew, x[System.Math.Min(edge, inner)],
                        Column(y, System.Math.Min(edge, inner)), x[System.Math.Max(edge, inner)],
                        Column(y, System.Math.Max(edge, inner)), Column(yp, System.Math.Min(edge, inner)),
                        Column(yp, System.Math.Max(edge, inner)));
                break;

            default:
                value = Column(y, edge);
                slope = new double[n];
                break;
        }

        double[] midpoint = [];
        if (five)
        {
            midpoint = new double[n];
            if (method == "solution")
            {
                int low = System.Math.Min(edge, inner);
                int high = System.Math.Max(edge, inner);
                double middle = atLeft ? (xnew + a) / 2 : (b + xnew) / 2;
                midpoint = BoundaryValueSolvers.Hermite4(middle, x[low], Column(y, low), x[high],
                    Column(y, high), Column(ymid, low), Column(yp, low), Column(yp, high)).Value;
            }
            else
            {
                for (int r = 0; r < n; r++)
                {
                    midpoint[r] = (value[r] + y[(edge * n) + r]) / 2;
                }
            }
        }

        int joined = mesh + 1;
        var newX = new double[joined];
        var newY = new double[n * joined];
        var newYp = new double[n * joined];
        int offset = atLeft ? 1 : 0;
        Array.Copy(x, 0, newX, offset, mesh);
        Array.Copy(y, 0, newY, offset * n, y.Length);
        Array.Copy(yp, 0, newYp, offset * n, yp.Length);
        int at = atLeft ? 0 : mesh;
        newX[at] = xnew;
        Array.Copy(value, 0, newY, at * n, n);
        Array.Copy(slope, 0, newYp, at * n, n);

        fields["x"] = JgsMatrix.FromColumnMajorDims(newX, [1, joined]);
        fields["y"] = JgsMatrix.FromColumnMajorDims(newY, [n, joined]);
        if (five)
        {
            int midCount = (ymid.Length / n) + 1;
            var newMid = new double[n * midCount];
            Array.Copy(ymid, 0, newMid, offset * n, ymid.Length);
            Array.Copy(midpoint, 0, newMid, (atLeft ? 0 : midCount - 1) * n, n);
            var idata = new Dictionary<string, JgsValue>(sol.AsStruct["idata"].AsStruct, StringComparer.Ordinal)
            {
                ["yp"] = JgsMatrix.FromColumnMajorDims(newYp, [n, joined]),
                ["ymid"] = JgsMatrix.FromColumnMajorDims(newMid, [n, midCount]),
            };
            fields["idata"] = JgsValue.Struct(idata);
        }
        else
        {
            fields["yp"] = JgsMatrix.FromColumnMajorDims(newYp, [n, joined]);
        }

        return JgsValue.Struct(fields);
    }


    // --- bvp4c and bvp5c ---------------------------------------------------------------------------

    /// <summary><c>sol = bvp4c(odefun, bcfun, solinit, options, p1, p2, ...)</c>, and the same for <c>bvp5c</c>.</summary>
    private static JgsValue SolveBvp(JgsEnvironment env, JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 3, name == "bvp4c" ? 16 : 4, line, col);
        IJgsCallable ode = OdeFunctionOf(env, name, args[0], line, col);
        IJgsCallable bc = OdeFunctionOf(env, name, args[1], line, col);
        JgsValue solinit = args[2];
        if (solinit.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:SolinitNotStruct",
                $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): SOLINIT must be a structure.");
        }

        if (!TryReadField(solinit, "x", out JgsValue xValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:NoXInSolinit",
                $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): SOLINIT must have a field 'x'.");
        }

        if (!TryReadField(solinit, "y", out JgsValue yValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:NoYInSolinit",
                $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): SOLINIT must have a field 'y'.");
        }

        double[] mesh = ToDoubles(name, xValue, line, col);
        double[] flat = ToDoubles(name, yValue, line, col);
        int meshCount = mesh.Length;
        if (meshCount < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:SolinitXNotEnoughPts",
                $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): SOLINIT.x must have at least two entries.");
        }

        if (flat.Length == 0 || flat.Length % meshCount != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:SolXSolYSizeMismatch",
                $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): SOLINIT.y must have one column per entry of SOLINIT.x.");
        }

        int n = flat.Length / meshCount;
        double[]? parameters = TryReadField(solinit, "parameters", out JgsValue parameterValue)
            && !IsUnsetOption(parameterValue)
                ? ToDoubles(name, parameterValue, line, col)
                : null;
        int npar = parameters?.Length ?? 0;
        int[] interfaces = BoundaryValueSolvers.InterfacesOf(mesh);
        int nregions = interfaces.Length + 1;

        JgsValue? options = args.Count > 3 && args[3].Type == JgsType.Struct ? args[3] : null;
        JgsValue[] extras = name == "bvp4c" && args.Count > 4 ? [.. args.Skip(4)] : [];

        JgsValue Column(double[] values) =>
            JgsMatrix.FromColumnMajorDims((double[])values.Clone(), [values.Length, 1]);

        JgsValue[] Trailing(int region, double[] p) =>
        [
            .. nregions > 1 ? new[] { JgsValue.Number(region) } : [],
            .. npar > 0 ? new[] { Column(p) } : [],
            .. extras,
        ];

        double[] Derivative(double x, double[] y, int region, double[] p)
        {
            JgsValue answer = ode.Call([JgsValue.Number(x), Column(y), .. Trailing(region, p)], line, col);
            double[] slope = ToDoubles(name, answer, line, col);
            if (slope.Length != n)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:ODEfunOutputSize",
                    $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): ODEFUN must return a column vector of length {n}.");
            }

            return slope;
        }

        double[][] VectorDerivative(double[] xs, double[][] ys, int region, double[] p)
        {
            int columns = xs.Length;
            var block = new double[n * columns];
            for (int c = 0; c < columns; c++)
            {
                Array.Copy(ys[c], 0, block, c * n, n);
            }

            JgsValue answer = ode.Call(
            [
                JgsMatrix.FromColumnMajorDims((double[])xs.Clone(), [1, columns]),
                JgsMatrix.FromColumnMajorDims(block, [n, columns]),
                .. Trailing(region, p),
            ], line, col);
            double[] slopes = ToDoubles(name, answer, line, col);
            if (slopes.Length != n * columns)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:ODEfunOutputSize",
                    $"{name}: a vectorized ODEFUN must answer one column per point.");
            }

            var split = new double[columns][];
            for (int c = 0; c < columns; c++)
            {
                split[c] = new double[n];
                Array.Copy(slopes, c * n, split[c], 0, n);
            }

            return split;
        }

        JgsValue Block(double[][] columns)
        {
            var block = new double[columns.Length * columns[0].Length];
            for (int c = 0; c < columns.Length; c++)
            {
                Array.Copy(columns[c], 0, block, c * columns[0].Length, columns[0].Length);
            }

            return JgsMatrix.FromColumnMajorDims(block, [columns[0].Length, columns.Length]);
        }

        double[] Boundary(double[][] ya, double[][] yb, double[] p)
        {
            JgsValue answer = bc.Call(
            [
                Block(ya), Block(yb), .. npar > 0 ? new[] { Column(p) } : [], .. extras,
            ], line, col);
            double[] residual = ToDoubles(name, answer, line, col);
            if (residual.Length != (n * nregions) + npar)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:BCfunOutputSize",
                    $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): BCFUN must return a column vector of length {(n * nregions) + npar}.");
            }

            return residual;
        }

        BvpOptions settings = BvpOptionsFrom(env, host, name, options, ode, bc, n, npar, nregions,
            Trailing, Column, Block, line, col);

        var guess = new BvpGuess(mesh, flat, parameters);
        if (TryReadField(solinit, "solver", out JgsValue solverValue) && IsTextScalar(solverValue))
        {
            string from = TextOf(solverValue);
            if (from == "bvpinit" && TryReadField(solinit, "yinit", out JgsValue yinit) && !IsUnsetOption(yinit))
            {
                if (yinit.Type == JgsType.Function || IsTextScalar(yinit))
                {
                    IJgsCallable handle = OdeFunctionOf(env, name, yinit, line, col);
                    guess = guess with
                    {
                        Kind = BvpGuessKind.BvpInit,
                        PointGuess = (at, region) => ToDoubles(name, handle.Call(
                            nregions > 1
                                ? [JgsValue.Number(at), JgsValue.Number(region), .. extras]
                                : [JgsValue.Number(at), .. extras], line, col), line, col),
                    };
                }
                else
                {
                    guess = guess with
                    {
                        Kind = BvpGuessKind.BvpInit,
                        ConstantGuess = ToDoubles(name, yinit, line, col),
                    };
                }
            }
            else if (from is "bvp4c" or "bvp5c")
            {
                JgsValue previous = solinit;
                guess = guess with
                {
                    Kind = BvpGuessKind.Solution,
                    Interpolate = points =>
                    {
                        JgsValue read = Deval(
                            [previous, JgsMatrix.FromColumnMajorDims((double[])points.Clone(), [1, points.Length])],
                            line, col)[0];
                        double[] values = ToDoubles(name, read, line, col);
                        var columns = new double[points.Length][];
                        for (int c = 0; c < points.Length; c++)
                        {
                            columns[c] = new double[n];
                            Array.Copy(values, c * n, columns[c], 0, n);
                        }

                        return columns;
                    },
                };
            }
        }

        BvpSolution solution;
        try
        {
            solution = name == "bvp4c"
                ? BoundaryValueSolvers.Bvp4c(Derivative, VectorDerivative, Boundary, guess, settings)
                : BoundaryValueSolvers.Bvp5c(Derivative, VectorDerivative, Boundary, guess, settings);
        }
        catch (BvpException failure)
        {
            throw new JgsRuntimeException(line, col, failure.Identifier, failure.Message);
        }

        int outMesh = solution.X.Length;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["solver"] = JgsValue.Str(name),
        };

        if (name == "bvp5c" && solution.Parameters is { Length: > 0 } found5)
        {
            fields["parameters"] = JgsMatrix.FromColumnMajorDims((double[])found5.Clone(), [found5.Length, 1]);
        }

        fields["x"] = JgsMatrix.FromColumnMajorDims((double[])solution.X.Clone(), [1, outMesh]);
        fields["y"] = JgsMatrix.FromColumnMajorDims((double[])solution.Y.Clone(), [n, outMesh]);
        if (name == "bvp4c")
        {
            fields["yp"] = JgsMatrix.FromColumnMajorDims((double[])solution.Yp.Clone(), [n, outMesh]);
            if (solution.Parameters is { Length: > 0 } found4)
            {
                fields["parameters"] = JgsMatrix.FromColumnMajorDims((double[])found4.Clone(), [found4.Length, 1]);
            }
        }
        else
        {
            int mid = solution.Ymid!.Length / n;
            fields["idata"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["ymid"] = JgsMatrix.FromColumnMajorDims((double[])solution.Ymid.Clone(), [n, mid]),
                ["yp"] = JgsMatrix.FromColumnMajorDims((double[])solution.Yp.Clone(), [n, outMesh]),
            });
        }

        fields["stats"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["nmeshpoints"] = JgsValue.Number(solution.MeshPoints),
            [name == "bvp4c" ? "maxres" : "maxerr"] = JgsValue.Number(solution.MaxResidual),
            ["nODEevals"] = JgsValue.Number(solution.OdeEvaluations),
            ["nBCevals"] = JgsValue.Number(solution.BoundaryEvaluations),
        });

        return JgsValue.Struct(fields);
    }

    /// <summary>The <c>bvpset</c> structure read into the numerics layer's own record.</summary>
    private static BvpOptions BvpOptionsFrom(JgsEnvironment env, JGraphScriptGlobals host, string name,
        JgsValue? options, IJgsCallable ode, IJgsCallable bc, int n, int npar, int nregions,
        Func<int, double[], JgsValue[]> trailing, Func<double[], JgsValue> column,
        Func<double[][], JgsValue> block, int line, int col)
    {
        _ = ode;
        _ = bc;
        JgsValue? Field(string field) =>
            options is not null && TryReadField(options, field, out JgsValue value) && !IsUnsetOption(value)
                ? value
                : null;

        bool Flag(string field) => Field(field) is { } value && IsTextScalar(value)
            && string.Equals(TextOf(value), "on", StringComparison.OrdinalIgnoreCase);

        double[]? absolute = Field("AbsTol") is { } atol ? ToDoubles(name, atol, line, col) : null;
        if (absolute is { Length: > 1 } && absolute.Length != n)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bvparguments:SizeAbsTol",
                $"Error calling {name.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): AbsTol must be a scalar or a vector of length {n}.");
        }

        double[,]? singular = Field("SingularTerm") is { } term ? RectOf("SingularTerm", term, line, col) : null;

        BvpOdeJacobianFunction? jacobian = null;
        double[,]? constantJacobian = null;
        double[,]? constantParameterJacobian = null;
        if (Field("FJacobian") is { } fjac)
        {
            if (fjac.Type == JgsType.Function || IsTextScalar(fjac))
            {
                IJgsCallable handle = OdeFunctionOf(env, name, fjac, line, col);
                jacobian = (x, y, region, p) =>
                {
                    JgsValue[] answers = CallForOutputs(handle,
                        [JgsValue.Number(x), column(y), .. trailing(region, p)], npar > 0 ? 2 : 1, line, col);
                    double[,] dy = RectOf("FJacobian", answers[0], line, col);
                    double[,]? dp = npar > 0 && answers.Length > 1
                        ? RectOf("FJacobian", answers[1], line, col)
                        : null;
                    return (dy, dp);
                };
            }
            else if (fjac.Type == JgsType.Cell)
            {
                JgsValue[] pair = fjac.AsCell;
                constantJacobian = RectOf("FJacobian", pair[0], line, col);
                if (pair.Length > 1)
                {
                    constantParameterJacobian = RectOf("FJacobian", pair[1], line, col);
                }
            }
            else
            {
                constantJacobian = RectOf("FJacobian", fjac, line, col);
            }
        }

        BvpBoundaryJacobianFunction? boundaryJacobian = null;
        double[,]? constantYa = null;
        double[,]? constantYb = null;
        double[,]? constantP = null;
        if (Field("BCJacobian") is { } bcjac)
        {
            if (bcjac.Type == JgsType.Cell)
            {
                JgsValue[] parts = bcjac.AsCell;
                constantYa = RectOf("BCJacobian", parts[0], line, col);
                constantYb = RectOf("BCJacobian", parts[1], line, col);
                if (parts.Length > 2)
                {
                    constantP = RectOf("BCJacobian", parts[2], line, col);
                }
            }
            else
            {
                IJgsCallable handle = OdeFunctionOf(env, name, bcjac, line, col);
                boundaryJacobian = (ya, yb, p) =>
                {
                    JgsValue[] answers = CallForOutputs(handle,
                        [block(ya), block(yb), .. npar > 0 ? new[] { column(p) } : []],
                        npar > 0 ? 3 : 2, line, col);
                    double[,] dya = RectOf("BCJacobian", answers[0], line, col);
                    double[,] dyb = RectOf("BCJacobian", answers[1], line, col);
                    double[,]? dp = npar > 0 && answers.Length > 2
                        ? RectOf("BCJacobian", answers[2], line, col)
                        : null;
                    return (dya, dyb, dp);
                };
            }
        }

        _ = nregions;
        return new BvpOptions
        {
            RelativeTolerance = OdeNumber(options, "RelTol") ?? 1e-3,
            AbsoluteTolerance = absolute,
            MaxMeshPoints = OdeNumber(options, "Nmax") is { } nmax ? (int)nmax : null,
            Vectorized = Flag("Vectorized"),
            Stats = Flag("Stats"),
            SingularTerm = singular,
            JacobianFunction = jacobian,
            ConstantJacobian = constantJacobian,
            ConstantParameterJacobian = constantParameterJacobian,
            BoundaryJacobianFunction = boundaryJacobian,
            ConstantBoundaryJacobianYa = constantYa,
            ConstantBoundaryJacobianYb = constantYb,
            ConstantBoundaryJacobianP = constantP,
            Warn = message => Warn(env, host, message, line, col),
            Print = text => host.print(text.TrimEnd('\n')),
        };
    }


    // --- dde23, ddesd and ddensd --------------------------------------------------------------------

    /// <summary>
    /// <c>sol = dde23(ddefun, lags, history, tspan, options, p1, ...)</c> and the two solvers that
    /// take delay functions instead of lags.
    /// </summary>
    private static JgsValue SolveDde(JgsEnvironment env, JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool neutral = name == "ddensd";
        int fixedArgs = neutral ? 5 : 4;
        ArityRange(name, args, fixedArgs, 16, line, col);
        IJgsCallable dde = OdeFunctionOf(env, name, args[0], line, col);
        JgsValue historyValue = args[neutral ? 3 : 2];
        double[] tspan = ToDoubles(name, args[neutral ? 4 : 3], line, col);
        JgsValue? options = args.Count > fixedArgs && args[fixedArgs].Type == JgsType.Struct
            ? args[fixedArgs]
            : null;
        JgsValue[] extras = args.Count > fixedArgs + 1 ? [.. args.Skip(fixedArgs + 1)] : [];

        (double[]? lags, DdeDelayFunction? delays) = DelaysOf(env, name, args[1], extras, line, col);
        (double[]? derivativeLags, DdeDelayFunction? derivativeDelays) = neutral
            ? DelaysOf(env, name, args[2], extras, line, col)
            : (null, null);

        // The history: a constant column, a function of time, or a solution this run continues.
        DdeHistory history;
        (double[] Y0, double[] Yp0)? initialPair = null;
        JgsValue reportedHistory = historyValue;
        if (historyValue.Type == JgsType.Cell)
        {
            JgsValue[] pair = historyValue.AsCell;
            initialPair = (ToDoubles(name, pair[0], line, col), ToDoubles(name, pair[1], line, col));
            history = new DdeHistory { Constant = initialPair.Value.Y0 };
        }
        else if (historyValue.Type == JgsType.Struct)
        {
            DdeSolution previous = SolutionOf(env, name, historyValue, line, col);
            history = new DdeHistory { Previous = previous };
            reportedHistory = historyValue.AsStruct.TryGetValue("history", out JgsValue? kept)
                ? kept
                : JgsValue.Array([]);
        }
        else if (historyValue.Type == JgsType.Function || IsTextScalar(historyValue))
        {
            IJgsCallable handle = OdeFunctionOf(env, name, historyValue, line, col);
            history = new DdeHistory
            {
                Function = t => ToDoubles(name, handle.Call([JgsValue.Number(t), .. extras], line, col), line, col),
            };
        }
        else
        {
            history = new DdeHistory { Constant = ToDoubles(name, historyValue, line, col) };
        }

        int neq = initialPair?.Y0.Length
            ?? (history.Previous is { } carried ? carried.Y[^1].Length : history.Before(tspan[0]).Length);

        JgsValue Column(double[] values) =>
            JgsMatrix.FromColumnMajorDims((double[])values.Clone(), [values.Length, 1]);

        JgsValue Delayed(double[][] z)
        {
            if (z.Length == 0)
            {
                return JgsMatrix.FromColumnMajorDims([], [neq, 0]);
            }

            var block = new double[neq * z.Length];
            for (int c = 0; c < z.Length; c++)
            {
                Array.Copy(z[c], 0, block, c * neq, neq);
            }

            return JgsMatrix.FromColumnMajorDims(block, [neq, z.Length]);
        }

        DdeOptions settings = DdeOptionsFrom(env, host, name, options, neq, extras, Column, Delayed, line, col);

        DdeSolution solution;
        try
        {
            if (neutral)
            {
                double[] Neutral(double t, double[] y, double[][] z, double[][] zp) =>
                    ToDoubles(name, dde.Call(
                        [JgsValue.Number(t), Column(y), Delayed(z), Delayed(zp), .. extras], line, col), line, col);

                Func<double, double[], double[][], double[][], OdeEventReading>? watch = null;
                if (SetOption(options, "Events") is { } eventsValue)
                {
                    IJgsCallable handle = OdeFunctionOf(env, name, eventsValue, line, col);
                    watch = (t, y, z, zp) => EventReadingOf(handle,
                        [JgsValue.Number(t), Column(y), Delayed(z), Delayed(zp), .. extras], line, col);
                }

                solution = DelaySolvers.Ddensd(Neutral, lags, delays, derivativeLags, derivativeDelays,
                    history, initialPair, tspan, settings with { Events = null }, watch);
            }
            else
            {
                double[] Derivative(double t, double[] y, double[][] z) =>
                    ToDoubles(name, dde.Call(
                        [JgsValue.Number(t), Column(y), Delayed(z), .. extras], line, col), line, col);

                solution = name == "dde23"
                    ? DelaySolvers.Dde23(Derivative, lags ?? [], history, tspan, settings)
                    : DelaySolvers.Ddesd(Derivative, lags, delays, history, tspan, settings);
            }
        }
        catch (BvpException failure)
        {
            throw new JgsRuntimeException(line, col, failure.Identifier, failure.Message);
        }

        int mesh = solution.X.Length;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["solver"] = JgsValue.Str(name),
        };

        if (neutral && initialPair is { } consistent)
        {
            fields["history"] = JgsValue.Cell([Column(consistent.Y0), Column(consistent.Yp0)]);
        }
        else
        {
            fields["history"] = reportedHistory;
        }

        if (solution.Discont is { } discontinuities)
        {
            fields["discont"] = JgsMatrix.FromColumnMajorDims((double[])discontinuities.Clone(),
                [1, discontinuities.Length]);
        }

        fields["x"] = JgsMatrix.FromColumnMajorDims((double[])solution.X.Clone(), [1, mesh]);
        fields["y"] = JgsMatrix.Build(neq, mesh, (r, c) => solution.Y[c][r]);
        if (solution.HadEvents)
        {
            int found = solution.EventTimes.Length;
            fields["xe"] = JgsMatrix.Build(found == 0 ? 0 : 1, found, (_, c) => solution.EventTimes[c]);
            fields["ye"] = JgsMatrix.Build(found == 0 ? 0 : neq, found, (r, c) => solution.EventStates[c][r]);
            fields["ie"] = JgsMatrix.Build(found == 0 ? 0 : 1, found, (_, c) => solution.EventIndices[c] + 1);
        }

        fields["stats"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["nsteps"] = JgsValue.Number(solution.Steps),
            ["nfailed"] = JgsValue.Number(solution.Failed),
            ["nfevals"] = JgsValue.Number(solution.Evaluations),
            ["tfinal"] = JgsValue.Number(solution.FinalTime),
        });
        fields["yp"] = JgsMatrix.Build(neq, mesh, (r, c) => solution.Yp[c][r]);
        if (neutral)
        {
            fields["IVP"] = JgsValue.Bool(solution.InitialValueProblem);
        }

        return JgsValue.Struct(fields);
    }

    /// <summary>Constant lags or a delay function, whichever the caller wrote.</summary>
    private static (double[]? Lags, DdeDelayFunction? Delays) DelaysOf(JgsEnvironment env, string name,
        JgsValue given, JgsValue[] extras, int line, int col)
    {
        if (given.Type == JgsType.Function || IsTextScalar(given))
        {
            IJgsCallable handle = OdeFunctionOf(env, name, given, line, col);
            return (null, (t, y) => ToDoubles(name, handle.Call(
            [
                JgsValue.Number(t),
                JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [y.Length, 1]),
                .. extras,
            ], line, col), line, col));
        }

        return (ToDoubles(name, given, line, col), null);
    }

    /// <summary>One field of an options structure, or null when it was not set.</summary>
    private static JgsValue? SetOption(JgsValue? options, string name) =>
        options is not null && TryReadField(options, name, out JgsValue value) && !IsUnsetOption(value)
            ? value
            : null;

    /// <summary>What an event function reported, in the numerics layer's own shape.</summary>
    private static OdeEventReading EventReadingOf(IJgsCallable handle, JgsValue[] inputs, int line, int col)
    {
        JgsValue[] outputs = CallForOutputs(handle, inputs, 3, line, col);
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
    }

    /// <summary>The <c>ddeset</c> structure read into the numerics layer's own record.</summary>
    private static DdeOptions DdeOptionsFrom(JgsEnvironment env, JGraphScriptGlobals host, string name,
        JgsValue? options, int neq, JgsValue[] extras, Func<double[], JgsValue> column,
        Func<double[][], JgsValue> delayed, int line, int col)
    {
        bool Flag(string field) => SetOption(options, field) is { } value && IsTextScalar(value)
            && string.Equals(TextOf(value), "on", StringComparison.OrdinalIgnoreCase);

        DdeEventFunction? events = null;
        if (SetOption(options, "Events") is { } eventsValue)
        {
            IJgsCallable handle = OdeFunctionOf(env, name, eventsValue, line, col);
            events = (t, y, z) => EventReadingOf(handle,
                [JgsValue.Number(t), column(y), delayed(z), .. extras], line, col);
        }

        OdeOutputFunction? outputFunction = null;
        if (SetOption(options, "OutputFcn") is { } outputValue)
        {
            IJgsCallable handle = OdeFunctionOf(env, name, outputValue, line, col);
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
                JgsValue answer = handle.Call([t, y, JgsValue.Str(flag), .. extras], line, col);
                return phase == OdeOutputPhase.Step && answer.Type != JgsType.Null && Truth(answer);
            };
        }

        int[]? selection = null;
        if (SetOption(options, "OutputSel") is { } sel)
        {
            selection = OneBasedIndices("OutputSel", ToDoubles(name, sel, line, col), neq, line, col);
        }

        return new DdeOptions
        {
            RelativeTolerance = OdeNumber(options, "RelTol") ?? 1e-3,
            AbsoluteTolerance = SetOption(options, "AbsTol") is { } atol
                ? ToDoubles(name, atol, line, col)
                : null,
            NormControl = Flag("NormControl"),
            Jumps = SetOption(options, "Jumps") is { } jumps ? ToDoubles(name, jumps, line, col) : null,
            InitialY = SetOption(options, "InitialY") is { } y0 ? ToDoubles(name, y0, line, col) : null,
            MaxStep = OdeNumber(options, "MaxStep"),
            InitialStep = OdeNumber(options, "InitialStep"),
            Refine = OdeNumber(options, "Refine") is { } refine ? (int)refine : null,
            Events = events,
            OutputFunction = outputFunction,
            OutputSelection = selection,
            Stats = Flag("Stats"),
            Warn = message => Warn(env, host, message, line, col),
            Print = text => host.print(text.TrimEnd('\n')),
        };
    }

    /// <summary>A delay solution structure taken back apart, so a second call can continue it.</summary>
    private static DdeSolution SolutionOf(JgsEnvironment env, string name, JgsValue given, int line, int col)
    {
        double[] x = ToDoubles(name, given.AsStruct["x"], line, col);
        double[] y = ToDoubles(name, given.AsStruct["y"], line, col);
        double[] yp = ToDoubles(name, given.AsStruct["yp"], line, col);
        int mesh = x.Length;
        int neq = y.Length / mesh;
        var states = new double[mesh][];
        var slopes = new double[mesh][];
        for (int c = 0; c < mesh; c++)
        {
            states[c] = new double[neq];
            slopes[c] = new double[neq];
            Array.Copy(y, c * neq, states[c], 0, neq);
            Array.Copy(yp, c * neq, slopes[c], 0, neq);
        }

        JgsValue kept = given.AsStruct.TryGetValue("history", out JgsValue? stored) ? stored : JgsValue.Array([]);
        DdeHistory history;
        if (kept.Type == JgsType.Function || IsTextScalar(kept))
        {
            IJgsCallable handle = OdeFunctionOf(env, name, kept, line, col);
            history = new DdeHistory
            {
                Function = t => ToDoubles(name, handle.Call([JgsValue.Number(t)], line, col), line, col),
            };
        }
        else if (kept.Type == JgsType.Cell)
        {
            history = new DdeHistory { Constant = ToDoubles(name, kept.AsCell[0], line, col) };
        }
        else
        {
            history = new DdeHistory { Constant = ToDoubles(name, kept, line, col) };
        }

        return new DdeSolution
        {
            Solver = given.AsStruct.TryGetValue("solver", out JgsValue? solver) && IsTextScalar(solver)
                ? TextOf(solver)
                : name,
            History = history,
            X = x,
            Y = states,
            Yp = slopes,
            Discont = given.AsStruct.TryGetValue("discont", out JgsValue? discont)
                ? ToDoubles(name, discont, line, col)
                : null,
        };
    }

    /// <summary>MATLAB's <c>eps(x)</c>: the distance to the next double above.</summary>
    private static double Spacing(double x)
    {
        double magnitude = System.Math.Abs(x);
        if (magnitude == 0)
        {
            return 4.9406564584124654e-324;
        }

        long bits = BitConverter.DoubleToInt64Bits(magnitude);
        return BitConverter.Int64BitsToDouble(bits + 1) - magnitude;
    }
}
