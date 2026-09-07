using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The rest of MATLAB's quadrature (M127): <c>integral2</c>, <c>integral3</c> and <c>quad2d</c> over
/// a plane or a solid region, and the four names that came before them — <c>quad</c>, <c>quadl</c>,
/// <c>quadv</c>, <c>dblquad</c> and <c>triplequad</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>integral</c> and <c>quadgk</c> were already here (M43, M121); what was missing was every
/// integral over more than a line. The three modern names share one engine and differ in what they
/// let a caller ask: <c>quad2d</c> exposes the tile budget, the boundary transform and the failure
/// plot, <c>integral2</c> hides all three and adds the <c>'iterated'</c> method that an infinite
/// limit needs, and <c>integral3</c> is <c>integral2</c> called once per abscissa of an outer
/// one-dimensional integration.
/// </para>
/// <para>
/// The five legacy names are here because scripts call them and because their answers are their own.
/// They are not wrappers around <c>integral</c>: each holds every subinterval to an <em>absolute</em>
/// tolerance of its own, and each hands back the number of times it called the integrand, which is a
/// statement about the method rather than about the integral. Reimplementing them as
/// <c>integral</c> would answer a different number to <c>[q, fcnt] = quad(...)</c> and a slightly
/// different <c>q</c> beside it.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The six points of a tile <c>quad2d</c> asks about twice, in a differently shaped array, to
    /// see whether the integrand is really elementwise. Column-major indices into the 14-by-14 tile.
    /// </summary>
    private static readonly int[] Quad2dVectorizationTest = [15, 26, 73, 80, 131, 123];

    /// <summary><c>integral2</c>'s own six, which are not the same six.</summary>
    private static readonly int[] Integral2VectorizationTest = [24, 48, 72, 96, 120, 144];

    /// <summary>Registers the plane and solid integrals and the five names that came before them.</summary>
    internal static void RegisterQuadratureBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        env.DeclareFunction("integral2", JgsValue.Function(new BuiltinFunction(
            "integral2", (args, line, col) => IntegrateOverAPlane(env, host, args, line, col))));

        env.DeclareFunction("integral3", JgsValue.Function(new BuiltinFunction(
            "integral3", (args, line, col) => IntegrateOverASolid(env, host, args, line, col))));

        env.DeclareFunction("quad2d", JgsValue.Function(new BuiltinFunction(
            "quad2d", (args, line, col) => Quad2d(env, host, args, 1, line, col)[0])
        {
            // quad2d's second output is the bound on its own error, which is the one thing it has
            // that integral2 does not: MATLAB's integral2 declares a single output.
            MultiOutput = (args, wanted, line, col) => Quad2d(env, host, args, wanted, line, col),
        }));

        env.DeclareFunction("quad", JgsValue.Function(new BuiltinFunction(
            "quad", (args, line, col) => Recursive(env, host, "quad", args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Recursive(env, host, "quad", args, wanted, line, col),
        }));

        env.DeclareFunction("quadl", JgsValue.Function(new BuiltinFunction(
            "quadl", (args, line, col) => Recursive(env, host, "quadl", args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Recursive(env, host, "quadl", args, wanted, line, col),
        }));

        env.DeclareFunction("quadv", JgsValue.Function(new BuiltinFunction(
            "quadv", (args, line, col) => Recursive(env, host, "quadv", args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Recursive(env, host, "quadv", args, wanted, line, col),
        }));

        env.DeclareFunction("dblquad", JgsValue.Function(new BuiltinFunction(
            "dblquad", (args, line, col) => Dblquad(env, args, line, col))));

        env.DeclareFunction("triplequad", JgsValue.Function(new BuiltinFunction(
            "triplequad", (args, line, col) => Triplequad(env, args, line, col))));
    }

    /// <summary>
    /// What an integration that was told to insist raises when it cannot meet its tolerance. It
    /// carries no message: the caller decides which of its own names the failure is reported under,
    /// the way MATLAB's <c>integral2</c> turns an inner <c>integral</c> failure into its own warning.
    /// </summary>
    private sealed class QuadratureGaveUp : Exception;

    // --- integral2 ---------------------------------------------------------------------------

    /// <summary><c>q = integral2(fun, xmin, xmax, ymin, ymax, ...)</c>.</summary>
    private static JgsValue IntegrateOverAPlane(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 5)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:integral2:NotEnoughInputs",
                "integral2 requires an integrand and four limits.");
        }

        IJgsCallable f = HandleArgument("integral2", args[0], "invalidIntegrand", line, col);
        double xmin = ScalarLimit("integral2", args[1], "invalidXMin", line, col);
        double xmax = ScalarLimit("integral2", args[2], "invalidXMax", line, col);
        bool improper = !double.IsFinite(xmin) || !double.IsFinite(xmax);
        Func<double[], double[]> ymin = CurveOf("integral2", args[3], "invalidYMin", ref improper, line, col);
        Func<double[], double[]> ymax = CurveOf("integral2", args[4], "invalidYMax", ref improper, line, col);
        var options = new PlaneOptions(1e-10, 1e-6, "auto");
        ReadPlaneOptions("integral2", args, 5, ref options, line, col);
        string method = MethodFor("integral2", options.Method, improper, line, col);

        try
        {
            double q = method == "iterated"
                ? Iterated(f, xmin, xmax, ymin, ymax, options.AbsTol, options.RelTol, line, col)
                : Tiled(f, xmin, xmax, ymin, ymax, options.AbsTol, options.RelTol,
                    singular: true, PlaneQuadrature.TiledMaximumFunctionEvaluations,
                    Integral2VectorizationTest, insist: true, env, host, "integral2", line, col).Value;
            return JgsValue.Number(q);
        }
        catch (QuadratureGaveUp)
        {
            Warn(env, host, "The integration was unsuccessful.", line, col);
            return JgsValue.Number(double.NaN);
        }
    }

    // --- integral3 ---------------------------------------------------------------------------

    /// <summary><c>q = integral3(fun, xmin, xmax, ymin, ymax, zmin, zmax, ...)</c>.</summary>
    /// <remarks>
    /// The outer integral is one-dimensional and every one of its abscissae costs a whole plane
    /// integration, which is why its mesh starts at three panels rather than ten: a panel here is
    /// fifteen inner integrations, and ten of them before any adaptation would spend most of the
    /// budget deciding something the adaptation was about to decide better.
    /// </remarks>
    private static JgsValue IntegrateOverASolid(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 7)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:integral3:NotEnoughInputs",
                "integral3 requires an integrand and six limits.");
        }

        IJgsCallable f = HandleArgument("integral3", args[0], "invalidIntegrand", line, col);
        double xmin = ScalarLimit("integral3", args[1], "invalidXMin", line, col);
        double xmax = ScalarLimit("integral3", args[2], "invalidXMax", line, col);

        // The x limits do not decide the method here: they are the outer integral's, and the outer
        // integral is always the one-dimensional one, which handles an infinite limit by itself.
        bool improper = false;
        Func<double[], double[]> ymin = CurveOf("integral3", args[3], "invalidYMin", ref improper, line, col);
        Func<double[], double[]> ymax = CurveOf("integral3", args[4], "invalidYMax", ref improper, line, col);
        Func<double[], double[], double[]> zmin = SurfaceOf("integral3", args[5], "invalidZMin", ref improper, line, col);
        Func<double[], double[], double[]> zmax = SurfaceOf("integral3", args[6], "invalidZMax", ref improper, line, col);
        var options = new PlaneOptions(1e-10, 1e-6, "auto");
        ReadPlaneOptions("integral3", args, 7, ref options, line, col);
        string method = MethodFor("integral3", options.Method, improper, line, col);

        double[] Outer(double[] xs)
        {
            double[] low = ymin(xs);
            double[] high = ymax(xs);
            var values = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                double x = xs[i];
                IJgsCallable slice = SliceOf(f, x, line, col);
                Func<double[], double[]> bottom = ys => zmin(Filled(x, ys.Length), ys);
                Func<double[], double[]> top = ys => zmax(Filled(x, ys.Length), ys);
                values[i] = method == "iterated"
                    ? Iterated(slice, low[i], high[i], bottom, top, options.AbsTol, options.RelTol, line, col)
                    : Tiled(slice, low[i], high[i], bottom, top, options.AbsTol, options.RelTol,
                        singular: true, PlaneQuadrature.TiledMaximumFunctionEvaluations,
                        Integral2VectorizationTest, insist: true, env, host, "integral3", line, col).Value;
            }

            return values;
        }

        try
        {
            Quadrature.MeshResult outer = Quadrature.IntegrateOverAMesh(
                Outer, xmin, xmax, options.AbsTol, options.RelTol, initialIntervalCount: 3);
            ReportMeshTrouble(env, host, outer, line, col);
            return JgsValue.Number(outer.Value);
        }
        catch (QuadratureGaveUp)
        {
            Warn(env, host, "The integration was unsuccessful.", line, col);
            return JgsValue.Number(double.NaN);
        }
    }

    // --- quad2d ------------------------------------------------------------------------------

    /// <summary><c>[q, errbnd] = quad2d(fun, a, b, c, d, ...)</c>.</summary>
    private static JgsValue[] Quad2d(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted,
        int line, int col)
    {
        if (args.Count < 5)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:quad2d:NotEnoughInputs",
                "quad2d requires an integrand and four limits.");
        }

        IJgsCallable f = HandleArgument("quad2d", args[0], "invalidIntegrand", line, col);
        double a = FiniteLimit("quad2d", args[1], "invalidA", line, col);
        double b = FiniteLimit("quad2d", args[2], "invalidB", line, col);
        bool improper = false;
        Func<double[], double[]> low = CurveOf("quad2d", args[3], "invalidC", ref improper, line, col);
        Func<double[], double[]> high = CurveOf("quad2d", args[4], "invalidD", ref improper, line, col);
        if (improper)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:quad2d:invalidC",
                "quad2d: the y limits must be finite scalars or function handles.");
        }

        double absolute = 1e-5;
        double relative = 0.0;
        bool relativeGiven = false;
        bool singular = true;
        int budget = PlaneQuadrature.DefaultMaximumFunctionEvaluations;
        if ((args.Count - 5) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:quad2d:ArgNameValueMismatch",
                "Arguments must occur in name-value pairs.");
        }

        for (int i = 5; i + 1 < args.Count; i += 2)
        {
            string option = Str("quad2d", args, i, line, col).Trim();
            if (Names(option, "AbsTol"))
            {
                absolute = Num("quad2d", args, i + 1, line, col);
            }
            else if (Names(option, "RelTol"))
            {
                relative = Num("quad2d", args, i + 1, line, col);
                relativeGiven = true;
            }
            else if (Names(option, "Singular"))
            {
                singular = Truth(args[i + 1]);
            }
            else if (Names(option, "MaxFunEvals"))
            {
                budget = Count("quad2d", args, i + 1, line, col);
            }
            else if (Names(option, "FailurePlot"))
            {
                // Accepted and ignored: the plot is a picture of the tiles still unrefined, and
                // drawing it would open a window from inside a numeric verb.
                _ = Truth(args[i + 1]);
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:quad2d:invalidOption",
                    $"'{option}' is not a recognized option for quad2d.");
            }
        }

        if (relativeGiven && relative < 100.0 * 2.220446049250313e-16)
        {
            Warn(env, host, "RelTol was increased to 100*eps('double') = 2.22045e-14.", line, col);
        }

        PlaneQuadrature.Result result = Tiled(f, a, b, low, high, absolute, relative,
            singular, budget, Quad2dVectorizationTest, insist: false, env, host, "quad2d", line, col);
        return wanted >= 2
            ? [JgsValue.Number(result.Value), JgsValue.Number(result.ErrorBound)]
            : [JgsValue.Number(result.Value)];
    }

    // --- the tiled and iterated engines, as the three modern names share them ------------------

    /// <summary>
    /// One tiled integration, its warnings raised under the caller's own name, and — when the caller
    /// is one that insists — a failure turned into <see cref="QuadratureGaveUp"/>.
    /// </summary>
    private static PlaneQuadrature.Result Tiled(
        IJgsCallable f, double xmin, double xmax,
        Func<double[], double[]> ymin, Func<double[], double[]> ymax,
        double absolute, double relative, bool singular, int budget, int[] vectorizationTest,
        bool insist, JgsEnvironment env, JGraphScriptGlobals host, string name, int line, int col)
    {
        PlaneQuadrature.Result result;
        try
        {
            result = PlaneQuadrature.Integrate(PlaneOf(name, f, line, col), xmin, xmax, ymin, ymax,
                absolute, relative, singular, budget, vectorizationTest);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:funSizeMismatch",
                $"{name}: {ex.Message}");
        }

        if (!result.Vectorized)
        {
            Warn(env, host, "Integrand function outputs did not match to the required tolerance when "
                + "the same input values were supplied in two separate calls with different size "
                + "input matrices. Check that the function is vectorized properly.", line, col);
        }

        double honoured = Math.Max(relative, 100.0 * 2.220446049250313e-16);
        bool passed = result.ErrorBound <= Math.Max(absolute, honoured * Math.Abs(result.Value));
        switch (result.Trouble)
        {
            case PlaneQuadrature.Trouble.NonFiniteResult:
                Warn(env, host, "Non-finite result. The integration was unsuccessful. Singularity likely.",
                    line, col);
                break;
            case PlaneQuadrature.Trouble.MaximumFunctionEvaluations:
                Warn(env, host, $"Reached the maximum number of function evaluations ({budget}). "
                    + $"The result {(passed ? "passes" : "fails")} the global error test.", line, col);
                break;
            case PlaneQuadrature.Trouble.MinimumTileSize:
                Warn(env, host, "Reached the minimum rectangle size. The result "
                    + $"{(passed ? "passes" : "fails")} the global error test.", line, col);
                break;
            default:
                break;
        }

        bool failed = result.Trouble == PlaneQuadrature.Trouble.NonFiniteResult
            || (result.Trouble != PlaneQuadrature.Trouble.None && !passed);
        return insist && failed ? throw new QuadratureGaveUp() : result;
    }

    /// <summary>
    /// The <c>'iterated'</c> method: a one-dimensional adaptive integration in x whose integrand is
    /// itself a one-dimensional adaptive integration in y. It is what an infinite limit needs, since
    /// a tile has to be a finite rectangle before it can be quartered.
    /// </summary>
    private static double Iterated(
        IJgsCallable f, double xmin, double xmax,
        Func<double[], double[]> ymin, Func<double[], double[]> ymax,
        double absolute, double relative, int line, int col)
    {
        double[] Outer(double[] xs)
        {
            double[] low = ymin(xs);
            double[] high = ymax(xs);
            var values = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                IJgsCallable slice = SliceOf(f, xs[i], line, col);
                Quadrature.MeshResult inner = Quadrature.IntegrateOverAMesh(
                    ys => LineOf("integral2", slice, ys, line, col),
                    low[i], high[i], absolute, relative, initialIntervalCount: 3);
                if (inner.Trouble != Quadrature.Trouble.None)
                {
                    throw new QuadratureGaveUp();
                }

                values[i] = inner.Value;
            }

            return values;
        }

        Quadrature.MeshResult outer = Quadrature.IntegrateOverAMesh(
            Outer, xmin, xmax, absolute, relative, initialIntervalCount: 3);
        return outer.Trouble != Quadrature.Trouble.None ? throw new QuadratureGaveUp() : outer.Value;
    }

    /// <summary>The warning a one-dimensional mesh integration raises when it stops short.</summary>
    private static void ReportMeshTrouble(
        JgsEnvironment env, JGraphScriptGlobals host, Quadrature.MeshResult result, int line, int col)
    {
        switch (result.Trouble)
        {
            case Quadrature.Trouble.MinimumStepSize:
                Warn(env, host, $"Minimum step size reached near x = {Shortly(result.Where)}. There may "
                    + "be a singularity, or the tolerances may be too tight for this problem.", line, col);
                break;
            case Quadrature.Trouble.MaximumIntervalCount:
                Warn(env, host, "Reached the limit on the maximum number of intervals in use. "
                    + $"Approximate bound on error is {result.ErrorBound:0.0e+00}. The integral may not "
                    + "exist, or it may be difficult to approximate numerically to the requested accuracy.",
                    line, col);
                break;
            case Quadrature.Trouble.NonFiniteValue:
                Warn(env, host, "Inf or NaN value encountered.", line, col);
                break;
            default:
                break;
        }
    }

    /// <summary>A number written the way MATLAB's <c>num2str(x, 6)</c> writes it.</summary>
    private static string Shortly(double value) =>
        value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

    // --- quad, quadl and quadv ----------------------------------------------------------------

    /// <summary>
    /// <c>[q, fcnt] = quad(fun, a, b, tol, trace, p1, ...)</c> and the same for <c>quadl</c> and
    /// <c>quadv</c>: one reader, because the three take exactly the same arguments and differ only
    /// in the rule underneath.
    /// </summary>
    private static JgsValue[] Recursive(
        JgsEnvironment env, JGraphScriptGlobals host, string name, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:NotEnoughInputs",
                $"{name} requires a function and two limits.");
        }

        IJgsCallable f = HandleArgument(name, args[0], "invalidFun", line, col);
        double[] lower = ToDoubles(name, args[1], line, col);
        double[] upper = ToDoubles(name, args[2], line, col);
        if (lower.Length != 1 || upper.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:scalarLimits",
                "The limits of integration must be scalars.");
        }

        double tolerance = LegacyQuadrature.DefaultTolerance;
        if (args.Count > 3 && !IsEmptyPlaceholder(args[3]))
        {
            tolerance = Num(name, args, 3, line, col);
        }

        bool tracing = args.Count > 4 && !IsEmptyPlaceholder(args[4]) && Truth(args[4]);
        var extra = new List<JgsValue>();
        for (int i = 5; i < args.Count; i++)
        {
            extra.Add(args[i]);
        }

        LegacyQuadrature.Trace? trace = tracing
            ? (count, from, width, value) => host.print(JgsSprintf.FormatMatlab(
                "%8.0f %16.10f %18.8e %16.10f", [
                    JgsValue.Number(count), JgsValue.Number(from),
                    JgsValue.Number(width), JgsValue.Number(value)]))
            : null;

        double[] value;
        int calls;
        LegacyQuadrature.Trouble trouble;
        if (name == "quadv")
        {
            LegacyQuadrature.ArrayResult answered = LegacyQuadrature.AdaptiveSimpsonOfAnArray(
                x => ToDoubles(name, f.Call([JgsValue.Number(x), .. extra], line, col), line, col),
                lower[0], upper[0], tolerance, trace);
            value = answered.Value;
            calls = answered.FunctionCount;
            trouble = answered.Trouble;
        }
        else
        {
            double[] Sample(double[] at)
            {
                double[] answered = ToDoubles(name, f.Call([Numbers(at), .. extra], line, col), line, col);
                return answered.Length == at.Length ? answered
                    : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:funNotVectorized",
                        "The integrand function must return an output vector of the same length as "
                        + "the input vector.");
            }

            LegacyQuadrature.Result answered = name == "quadl"
                ? LegacyQuadrature.AdaptiveLobatto(Sample, lower[0], upper[0], tolerance, trace)
                : LegacyQuadrature.AdaptiveSimpson(Sample, lower[0], upper[0], tolerance, trace);
            value = [answered.Value];
            calls = answered.FunctionCount;
            trouble = answered.Trouble;
        }

        switch (trouble)
        {
            case LegacyQuadrature.Trouble.MinimumStepSize:
                Warn(env, host, "Minimum step size reached; singularity possible.", line, col);
                break;
            case LegacyQuadrature.Trouble.MaximumFunctionCount:
                Warn(env, host, "Maximum function count exceeded; singularity likely.", line, col);
                break;
            case LegacyQuadrature.Trouble.ImproperFunctionValue:
                Warn(env, host, "Infinite or Not-a-Number function value encountered.", line, col);
                break;
            default:
                break;
        }

        JgsValue answer = value.Length == 1 ? JgsValue.Number(value[0]) : Numbers(value);
        return wanted >= 2 ? [answer, JgsValue.Number(calls)] : [answer];
    }

    // --- dblquad and triplequad ---------------------------------------------------------------

    /// <summary>
    /// <c>q = dblquad(fun, xmin, xmax, ymin, ymax, tol, method)</c>: the outer integral is taken with
    /// the given one-dimensional quadrature function, and its integrand runs the same function again
    /// over x at each y. <c>method</c> is a handle, and any handle with <c>quad</c>'s calling
    /// sequence will do — which is why the outer integration is performed by <em>calling</em> it
    /// rather than by reaching for the engine underneath.
    /// </summary>
    private static JgsValue Dblquad(
        JgsEnvironment env, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 5)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dblquad:NotEnoughInputs",
                "dblquad requires an integrand and four limits.");
        }

        _ = HandleArgument("dblquad", args[0], "invalidFun", line, col);
        JgsValue tolerance = args.Count > 5 && !IsEmptyPlaceholder(args[5])
            ? args[5] : JgsValue.Number(LegacyQuadrature.DefaultTolerance);
        JgsValue rule = args.Count > 6 && !IsEmptyPlaceholder(args[6])
            ? args[6] : QuadratureNamed(env, "quad", line, col);
        var extra = new List<JgsValue>();
        for (int i = 7; i < args.Count; i++)
        {
            extra.Add(args[i]);
        }

        JgsValue inner = JgsValue.Function(new BuiltinFunction("innerintegral", (given, atLine, atCol) =>
        {
            double[] ys = ToDoubles("dblquad", given[0], atLine, atCol);
            var values = new double[ys.Length];
            for (int i = 0; i < ys.Length; i++)
            {
                JgsValue[] call =
                [
                    args[0], args[1], args[2], tolerance, JgsValue.Array([]),
                    JgsValue.Number(ys[i]), .. extra,
                ];
                values[i] = ToDoubles("dblquad", rule.AsCallable.Call(call, atLine, atCol), atLine, atCol)[0];
            }

            return values.Length == 1 ? JgsValue.Number(values[0]) : Numbers(values);
        }));

        return rule.AsCallable.Call(
            [inner, args[3], args[4], tolerance, JgsValue.Array([])], line, col);
    }

    /// <summary>
    /// <c>q = triplequad(fun, xmin, xmax, ymin, ymax, zmin, zmax, tol, method)</c>: <c>dblquad</c>
    /// over y and z whose integrand integrates over x, which is exactly how MATLAB nests it.
    /// </summary>
    private static JgsValue Triplequad(
        JgsEnvironment env, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 7)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:triplequad:NotEnoughInputs",
                "triplequad requires an integrand and six limits.");
        }

        _ = HandleArgument("triplequad", args[0], "invalidFun", line, col);
        JgsValue tolerance = args.Count > 7 && !IsEmptyPlaceholder(args[7])
            ? args[7] : JgsValue.Number(LegacyQuadrature.DefaultTolerance);
        JgsValue rule = args.Count > 8 && !IsEmptyPlaceholder(args[8])
            ? args[8] : QuadratureNamed(env, "quad", line, col);
        var extra = new List<JgsValue>();
        for (int i = 9; i < args.Count; i++)
        {
            extra.Add(args[i]);
        }

        JgsValue inner = JgsValue.Function(new BuiltinFunction("innerintegral", (given, atLine, atCol) =>
        {
            double[] ys = ToDoubles("triplequad", given[0], atLine, atCol);
            JgsValue z = given[1];
            var values = new double[ys.Length];
            for (int i = 0; i < ys.Length; i++)
            {
                JgsValue[] call =
                [
                    args[0], args[1], args[2], tolerance, JgsValue.Array([]),
                    JgsValue.Number(ys[i]), z, .. extra,
                ];
                values[i] = ToDoubles("triplequad", rule.AsCallable.Call(call, atLine, atCol), atLine, atCol)[0];
            }

            return values.Length == 1 ? JgsValue.Number(values[0]) : Numbers(values);
        }));

        return Dblquad(env,
            [inner, args[3], args[4], args[5], args[6], tolerance, rule], line, col);
    }

    /// <summary>The builtin behind a name, as a value that can be passed on as a handle.</summary>
    private static JgsValue QuadratureNamed(JgsEnvironment env, string name, int line, int col) =>
        env.TryGet(name, out JgsValue value) && value.Type == JgsType.Function
            ? value
            : throw new JgsRuntimeException(line, col, $"{name} is not available here.");

    // --- reading the arguments the five names share --------------------------------------------

    /// <summary>The options <c>integral2</c> and <c>integral3</c> take.</summary>
    private record struct PlaneOptions(double AbsTol, double RelTol, string Method);

    private static void ReadPlaneOptions(
        string name, IReadOnlyList<JgsValue> args, int from, ref PlaneOptions options, int line, int col)
    {
        if ((args.Count - from) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:ArgNameValueMismatch",
                "Arguments must occur in name-value pairs.");
        }

        for (int i = from; i + 1 < args.Count; i += 2)
        {
            string option = Str(name, args, i, line, col).Trim();
            if (Names(option, "AbsTol"))
            {
                options.AbsTol = Num(name, args, i + 1, line, col);
            }
            else if (Names(option, "RelTol"))
            {
                options.RelTol = Num(name, args, i + 1, line, col);
            }
            else if (Names(option, "Method"))
            {
                options.Method = Str(name, args, i + 1, line, col).Trim();
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidOption",
                    $"'{option}' is not a recognized option for {name}.");
            }
        }
    }

    /// <summary>
    /// Which method <c>'auto'</c> stands for: <c>'iterated'</c> when a limit is infinite, since a
    /// tile has to be a finite rectangle, and <c>'tiled'</c> otherwise.
    /// </summary>
    private static string MethodFor(string name, string written, bool improper, int line, int col)
    {
        if (Names(written, "auto"))
        {
            return improper ? "iterated" : "tiled";
        }

        if (Names(written, "iterated"))
        {
            return "iterated";
        }

        if (!Names(written, "tiled"))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidMethod",
                $"'{written}' is not one of 'auto', 'tiled' or 'iterated'.");
        }

        return improper
            ? throw new JgsRuntimeException(line, col, $"MATLAB:{name}:nonFiniteLimit",
                "Unbounded integration region. The integration limits must be finite for the "
                + "'tiled' integration method. Consider using the 'iterated' method instead.")
            : "tiled";
    }

    private static IJgsCallable HandleArgument(string name, JgsValue value, string identifier, int line, int col) =>
        value.Type == JgsType.Function
            ? value.AsCallable
            : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:{identifier}",
                $"{name} expects a function handle.");

    private static double ScalarLimit(string name, JgsValue value, string identifier, int line, int col)
    {
        double[] read = ToDoubles(name, value, line, col);
        return read.Length == 1
            ? read[0]
            : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:{identifier}",
                $"{name}: the limit must be a scalar.");
    }

    private static double FiniteLimit(string name, JgsValue value, string identifier, int line, int col)
    {
        double limit = ScalarLimit(name, value, identifier, line, col);
        return double.IsFinite(limit)
            ? limit
            : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:{identifier}",
                $"{name}: the limit must be a finite scalar.");
    }

    /// <summary>
    /// One of the two curves bounding a plane region: a scalar spread across the abscissae, or a
    /// function handle asked about all of them at once. A scalar that is infinite is what makes the
    /// region improper, which is what decides the method.
    /// </summary>
    private static Func<double[], double[]> CurveOf(
        string name, JgsValue value, string identifier, ref bool improper, int line, int col)
    {
        if (value.Type == JgsType.Function)
        {
            IJgsCallable f = value.AsCallable;
            return xs =>
            {
                double[] answered = ToDoubles(name, f.Call([Numbers(xs)], line, col), line, col);
                return answered.Length == xs.Length ? answered
                    : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:yLimitSizeMismatch",
                        $"{name}: a limit function must answer one value for each abscissa.");
            };
        }

        double[] read = ToDoubles(name, value, line, col);
        if (read.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:{identifier}",
                $"{name}: a limit must be a scalar or a function handle.");
        }

        improper |= !double.IsFinite(read[0]);
        double constant = read[0];
        return xs => Filled(constant, xs.Length);
    }

    /// <summary>The same for a surface bounding a solid region, which is a function of x and y.</summary>
    private static Func<double[], double[], double[]> SurfaceOf(
        string name, JgsValue value, string identifier, ref bool improper, int line, int col)
    {
        if (value.Type == JgsType.Function)
        {
            IJgsCallable f = value.AsCallable;
            return (xs, ys) =>
            {
                double[] answered = ToDoubles(
                    name, f.Call([Numbers(xs), Numbers(ys)], line, col), line, col);
                return answered.Length == ys.Length ? answered
                    : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:zLimitSizeMismatch",
                        $"{name}: a limit function must answer one value for each abscissa.");
            };
        }

        double[] read = ToDoubles(name, value, line, col);
        if (read.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:{identifier}",
                $"{name}: a limit must be a scalar or a function handle.");
        }

        improper |= !double.IsFinite(read[0]);
        double constant = read[0];
        return (_, ys) => Filled(constant, ys.Length);
    }

    /// <summary>An integrand of two variables, asked about a whole tile at once.</summary>
    private static PlaneIntegrand PlaneOf(string name, IJgsCallable f, int line, int col) =>
        (xs, ys, rows, cols) =>
        {
            JgsValue answered = f.Call(
                [JgsMatrix.FromColumnMajor(xs, rows, cols), JgsMatrix.FromColumnMajor(ys, rows, cols)],
                line, col);
            double[] values = TileValuesOf(name, answered, line, col);
            return values.Length == xs.Length ? values
                : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:funSizeMismatch",
                    "Integrand output size does not match the input size.");
        };

    /// <summary>An integrand of one variable that came out of fixing the other one.</summary>
    private static double[] LineOf(string name, IJgsCallable f, double[] at, int line, int col)
    {
        double[] answered = ToDoubles(name, f.Call([Numbers(at)], line, col), line, col);
        return answered.Length == at.Length ? answered
            : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:FxNotSameSizeAsX",
                "Output of the function must be the same size as the input.");
    }

    /// <summary>
    /// A two- or three-variable integrand with its first variable fixed, which is what the inner
    /// levels of an iterated or nested integration see.
    /// </summary>
    private static IJgsCallable SliceOf(IJgsCallable f, double x, int line, int col) =>
        new BuiltinFunction("slice", (given, atLine, atCol) =>
        {
            int length = given.Count > 0 ? Math.Max(1, LengthOf(given[0])) : 1;
            JgsValue spread = length == 1
                ? JgsValue.Number(x)
                : SpreadLike(given[0], Filled(x, length));
            return f.Call([spread, .. given], atLine, atCol);
        });

    /// <summary>How many elements a value carries, whatever shape it is in.</summary>
    private static int LengthOf(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool => 1,
        JgsType.Array => JgsMatrix.IsNested(value)
            ? JgsMatrix.RowCount(value) * JgsMatrix.ColCount(value)
            : value.ArrayLength,
        _ => 1,
    };

    /// <summary>A flat buffer given the shape of the value it stands beside.</summary>
    private static JgsValue SpreadLike(JgsValue like, double[] flat) =>
        JgsMatrix.IsMatrix(like)
            ? JgsMatrix.FromColumnMajor(flat, JgsMatrix.RowCount(like), JgsMatrix.ColCount(like))
            : Numbers(flat);

    /// <summary>A constant repeated, which is how a scalar limit becomes a curve.</summary>
    private static double[] Filled(double value, int length)
    {
        var repeated = new double[length];
        Array.Fill(repeated, value);
        return repeated;
    }

    /// <summary>A numeric value read column-major, whichever of the two matrix shapes it is in.</summary>
    private static double[] TileValuesOf(string name, JgsValue value, int line, int col)
    {
        if (!JgsMatrix.IsNested(value))
        {
            return ToDoubles(name, value, line, col);
        }

        double[][] rows = JgsMatrix.ToRows(name, value, line, col);
        int height = rows.Length;
        int width = rows[0].Length;
        var flat = new double[height * width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                flat[r + (c * height)] = rows[r][c];
            }
        }

        return flat;
    }

    /// <summary><c>[]</c> written where an optional argument goes, which asks for the default.</summary>
    private static bool IsEmptyPlaceholder(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0;
}
