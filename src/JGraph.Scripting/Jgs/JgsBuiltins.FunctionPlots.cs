using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Maths.Contours;
using JGraph.Maths.Sampling;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M58: the function plotters. Every verb here is handed a function rather than data, and its work is
/// deciding where to read that function — the drawing afterwards is an ordinary plot of the readings.
/// </summary>
/// <remarks>
/// Nothing in this file is a new kind of chart. <c>fplot</c> hands its readings to <c>plot</c>,
/// <c>fplot3</c> to <c>plot3</c>, <c>fsurf</c> and <c>fmesh</c> to the surface, and the implicit pair
/// to the contour machinery — so a saved figure holds a line, a surface or a patch and not a function,
/// which is the one thing about these verbs a script has to know.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The properties the curve plotters accept after their functions.</summary>
    private static readonly string[] FunctionLineOptionNames =
    [
        "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName",
        "MeshDensity", "ShowPoles",
    ];

    private static readonly HashSet<string> FunctionLineOptions =
        new(FunctionLineOptionNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>MATLAB's own starting count for a curve plotter, before any refinement.</summary>
    private const int CurveMeshDensity = 23;

    private static void RegisterFunctionPlotBuiltins(JgsEnvironment env)
    {
        // The legacy two-output form answers with the readings instead of drawing them, which is the
        // only way a script can measure where the sampler chose to look.
        env.Declare("fplot", JgsValue.Function(new BuiltinFunction("fplot",
            (args, line, col) => FunctionLine("fplot", spatial: false, args, line, col))
        {
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) => wanted >= 2
                ? FunctionLineData("fplot", args, line, col)
                : [FunctionLine("fplot", spatial: false, args, line, col)],
        }));

        env.Declare("fplot3", JgsValue.Function(new BuiltinFunction("fplot3",
            (args, line, col) => FunctionLine("fplot3", spatial: true, args, line, col))
        { BindsAnsAsStatement = false }));

        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        DefineSilent("fsurf", (args, line, col) => FunctionSurface("fsurf", wireframe: false, args, line, col));
        DefineSilent("fmesh", (args, line, col) => FunctionSurface("fmesh", wireframe: true, args, line, col));
        DefineSilent("fcontour", (args, line, col) => FunctionContour("fcontour", args, line, col));
        DefineSilent("fimplicit", (args, line, col) => Implicit("fimplicit", args, line, col));
        DefineSilent("fimplicit3", (args, line, col) => Implicit3("fimplicit3", args, line, col));
    }

    /// <summary>What a curve plotter was asked to draw, once the arguments have been read.</summary>
    private sealed record CurveRequest(
        List<IJgsCallable> Functions,
        double Low,
        double High,
        string? Spec,
        List<(string Name, JgsValue Value)> Options);

    /// <summary>
    /// <c>fplot(f)</c>, <c>fplot(f, [a b])</c>, the parametric <c>fplot(fx, fy, [t0 t1])</c>, and
    /// <c>fplot3(fx, fy, fz, [t0 t1])</c> — each followed by a line spec and by name/value pairs.
    /// </summary>
    private static JgsValue FunctionLine(
        string verb, bool spatial, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            CurveRequest request = ReadCurveRequest(verb, spatial, rest, line, col);
            AdaptiveSamples samples = SampleCurve(verb, request, line, col);

            if (spatial)
            {
                Line3DPlot spatial = JG.Plot3(
                    samples.Components[0], samples.Components[1], samples.Components[2], request.Spec);
                foreach ((string name, JgsValue value) in request.Options)
                {
                    ApplyLineOption(verb, spatial, name, value, line, col);
                }

                return HandlesFor<Line3DPlot>([spatial]);
            }

            bool parametric = request.Functions.Count == 2;
            LinePlot plot = JG.Plot(
                parametric ? samples.Components[0] : samples.Parameters,
                parametric ? samples.Components[1] : samples.Values,
                request.Spec);
            foreach ((string name, JgsValue value) in request.Options)
            {
                ApplyLineOption(verb, plot, name, value, line, col);
            }

            return HandlesFor<LinePlot>([plot]);
        });
    }

    /// <summary>
    /// The legacy <c>[X, Y] = fplot(___)</c>, which answers with the readings and draws nothing.
    /// </summary>
    private static JgsValue[] FunctionLineData(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (_, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        CurveRequest request = ReadCurveRequest(verb, spatial: false, rest, line, col);
        AdaptiveSamples samples = SampleCurve(verb, request, line, col);

        return request.Functions.Count == 2
            ? [Numbers(samples.Components[0]), Numbers(samples.Components[1])]
            : [Numbers(samples.Parameters), Numbers(samples.Values)];
    }

    private static CurveRequest ReadCurveRequest(
        string verb, bool spatial, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int wanted = spatial ? 3 : 2;
        string shape = spatial
            ? $"{verb}(fx, fy, fz) takes three functions of one parameter, with an optional [t0 t1]."
            : $"{verb}(f) takes a function of x, or {verb}(fx, fy) two functions of one parameter, with an optional interval.";

        var functions = new List<IJgsCallable>();
        int i = 0;
        while (i < args.Count && args[i].Type == JgsType.Function && functions.Count < wanted)
        {
            functions.Add(args[i].AsCallable);
            i++;
        }

        if (functions.Count == 0 || (spatial && functions.Count != 3))
        {
            throw new JgsRuntimeException(line, col, shape);
        }

        (double low, double high) = ReadDomain(verb, args, ref i, -5, 5, line, col);

        string? spec = null;
        if (i < args.Count && args[i].Type == JgsType.String && LooksLikeLineSpec(args[i].AsString))
        {
            spec = args[i].AsString;
            i++;
        }

        return new CurveRequest(
            functions, low, high, spec,
            ReadNamedOptions(verb, args, i, FunctionLineOptions, FunctionLineOptionNames, line, col));
    }

    /// <summary>
    /// Whether a string in the place a line spec could go is one, rather than the first half of a
    /// name/value pair.
    /// </summary>
    /// <remarks>
    /// The two are told apart by their letters, not by a list of the option names: a spec is built
    /// from colour letters, marker glyphs and dashes and nothing else, so a misspelt option word is
    /// refused by name instead of being taken for a spec and leaving its value to be complained about
    /// as an odd argument.
    /// </remarks>
    private static bool LooksLikeLineSpec(string text) =>
        text.Length > 0 && text.All(c => "bgrcmykwo+*.xsd^v><ph-:".Contains(c, StringComparison.Ordinal));

    /// <summary>
    /// Reads an optional <c>[low high]</c> at <paramref name="index"/>, leaving the default alone when
    /// there is none.
    /// </summary>
    private static (double Low, double High) ReadDomain(
        string verb,
        IReadOnlyList<JgsValue> args,
        ref int index,
        double low,
        double high,
        int line,
        int col)
    {
        if (index >= args.Count || args[index].Type is not (JgsType.Array or JgsType.Number))
        {
            return (low, high);
        }

        double[] span = ToDoubles(verb, args[index], line, col);
        if (span.Length != 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: an interval is two values, [low high].");
        }

        if (!(span[0] < span[1]) || !double.IsFinite(span[0]) || !double.IsFinite(span[1]))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: an interval runs from a finite value to a larger one.");
        }

        index++;
        return (span[0], span[1]);
    }

    /// <summary>
    /// Reads the trailing <c>'Name', value</c> pairs, refusing a word that is not one of them by name
    /// rather than passing it over.
    /// </summary>
    private static List<(string Name, JgsValue Value)> ReadNamedOptions(
        string verb,
        IReadOnlyList<JgsValue> args,
        int start,
        HashSet<string> known,
        IReadOnlyList<string> spelled,
        int line,
        int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        var options = new List<(string, JgsValue)>();
        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf(verb, args[i], line, col);
            if (!known.Contains(name))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} has no option '{name}'. It takes {string.Join(", ", spelled)}.");
            }

            options.Add((name, args[i + 1]));
        }

        return options;
    }

    /// <summary>
    /// Reads the curve at the places the sampler chooses. <c>MeshDensity</c> is how many even readings
    /// it starts from, and <c>ShowPoles</c> off is what a script says when it wants the values a pole
    /// really takes rather than a break where the curve left.
    /// </summary>
    private static AdaptiveSamples SampleCurve(string verb, CurveRequest request, int line, int col)
    {
        var settings = new AdaptiveSamplerOptions
        {
            SeedCount = MeshDensityOf(verb, request.Options, CurveMeshDensity, line, col),
            PoleFactor = ShowPolesWanted(verb, request.Options, line, col)
                ? new AdaptiveSamplerOptions().PoleFactor
                : double.PositiveInfinity,
        };

        return AdaptiveSampler1D.Sample(
            Evaluator(verb, request.Functions, line, col),
            request.Functions.Count,
            request.Low,
            request.High,
            settings);
    }

    private static int MeshDensityOf(
        string verb, List<(string Name, JgsValue Value)> options, int fallback, int line, int col)
    {
        if (Option(options, "MeshDensity") is not { } value)
        {
            return fallback;
        }

        double density = NumOf($"{verb}: MeshDensity", value, line, col);
        if (density < 3 || density != System.Math.Floor(density))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: MeshDensity is a whole number of readings, three or more.");
        }

        return (int)density;
    }

    private static bool ShowPolesWanted(
        string verb, List<(string Name, JgsValue Value)> options, int line, int col) =>
        Option(options, "ShowPoles") is not { } value
        || JgsGraphicsProperties.ToOnOff($"{verb}: ShowPoles", value, line, col);

    /// <summary>
    /// Turns the script's function handles into the one thing the sampler asks for: a round of
    /// parameters in, a row of readings per component out.
    /// </summary>
    /// <remarks>
    /// A handle written the MATLAB way — <c>@(x) sin(x)./x</c> — answers a whole array at once, and
    /// asking it once per reading would be needlessly slow. So each function is offered the round as
    /// an array, and only the ones that cannot answer that way (a handle written with <c>*</c> where
    /// it meant <c>.*</c>, or one that returns a single number whatever it is handed) are asked one
    /// parameter at a time. The choice is made from the answer's length rather than from the handle's
    /// text, and it is made once per function.
    /// </remarks>
    private static Func<IReadOnlyList<double>, double[][]> Evaluator(
        string verb, IReadOnlyList<IJgsCallable> functions, int line, int col)
    {
        var vectorized = new bool?[functions.Count];
        return parameters =>
        {
            double[] xs = parameters as double[] ?? [.. parameters];
            var rows = new double[functions.Count][];
            for (int k = 0; k < functions.Count; k++)
            {
                rows[k] = ReadingsOf(verb, functions[k], [xs], ref vectorized[k], line, col);
            }

            return rows;
        };
    }

    // --- the surface plotters -------------------------------------------------------------------

    /// <summary>The properties <c>fsurf</c> and <c>fmesh</c> accept after their functions.</summary>
    private static readonly string[] FunctionSurfaceOptionNames =
    [
        "EdgeColor", "FaceColor", "FaceAlpha", "LineStyle", "LineWidth",
        "ShowContours", "DisplayName", "MeshDensity", "ShowPoles",
    ];

    /// <summary>The properties <c>fcontour</c> accepts after its function.</summary>
    private static readonly string[] FunctionContourOptionNames =
    [
        "LevelList", "LevelStep", "LineWidth", "Fill", "ShowText",
        "DisplayName", "MeshDensity", "ShowPoles",
    ];

    private static readonly HashSet<string> FunctionSurfaceOptions =
        new(FunctionSurfaceOptionNames, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FunctionContourOptions =
        new(FunctionContourOptionNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>MATLAB's grid density for a surface plotter, and for a contour one.</summary>
    private const int SurfaceMeshDensity = 35;

    private const int ContourMeshDensity = 71;

    /// <summary>What a surface plotter was asked to draw, once its arguments have been read.</summary>
    private sealed record SurfaceRequest(
        List<IJgsCallable> Functions,
        double XLow,
        double XHigh,
        double YLow,
        double YHigh,
        List<(string Name, JgsValue Value)> Options);

    /// <summary>
    /// <c>fsurf(f)</c> and <c>fmesh(f)</c> over a rectangle, or the parametric
    /// <c>fsurf(fx, fy, fz)</c> over a rectangle of parameters.
    /// </summary>
    private static JgsValue FunctionSurface(
        string verb, bool wireframe, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            SurfaceRequest request = ReadSurfaceRequest(
                verb, rest, FunctionSurfaceOptions, FunctionSurfaceOptionNames, line, col);
            int density = MeshDensityOf(verb, request.Options, SurfaceMeshDensity, line, col);
            double poles = PoleFactorFor(verb, request.Options, line, col);

            double[] xs = GridSampler.EvenlySpaced(request.XLow, request.XHigh, density);
            double[] ys = GridSampler.EvenlySpaced(request.YLow, request.YHigh, density);
            bool contoured = ShowContoursWanted(verb, request.Options, line, col);

            SurfacePlot surface;
            if (request.Functions.Count == 3)
            {
                // A parametric surface reads all three coordinates over the same rectangle of
                // parameters, so x and y are grids rather than the axes of one.
                double[,] x = SampleGrid(verb, request.Functions[0], xs, ys, poles, line, col);
                double[,] y = SampleGrid(verb, request.Functions[1], xs, ys, poles, line, col);
                double[,] z = SampleGrid(verb, request.Functions[2], xs, ys, poles, line, col);
                surface = wireframe
                    ? contoured ? JG.MeshC(x, y, z) : JG.Mesh(x, y, z)
                    : contoured ? JG.SurfC(x, y, z) : JG.Surf(x, y, z);
            }
            else
            {
                double[,] z = SampleGrid(verb, request.Functions[0], xs, ys, poles, line, col);
                surface = wireframe
                    ? contoured ? JG.MeshC(xs, ys, z) : JG.Mesh(xs, ys, z)
                    : contoured ? JG.SurfC(xs, ys, z) : JG.Surf(xs, ys, z);
            }

            ApplyProperties(verb, surface, request.Options, line, col);
            return HandlesFor<SurfacePlot>([surface]);
        });
    }

    /// <summary><c>fcontour(f)</c> over a rectangle, drawn as iso-lines or filled bands.</summary>
    private static JgsValue FunctionContour(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            SurfaceRequest request = ReadSurfaceRequest(
                verb, rest, FunctionContourOptions, FunctionContourOptionNames, line, col);
            if (request.Functions.Count != 1)
            {
                throw new JgsRuntimeException(line, col, $"{verb}(f) takes one function of x and y.");
            }

            int density = MeshDensityOf(verb, request.Options, ContourMeshDensity, line, col);
            double[] xs = GridSampler.EvenlySpaced(request.XLow, request.XHigh, density);
            double[] ys = GridSampler.EvenlySpaced(request.YLow, request.YHigh, density);
            double[,] z = SampleGrid(
                verb, request.Functions[0], xs, ys,
                PoleFactorFor(verb, request.Options, line, col), line, col);

            double[]? levels = LevelsFor(verb, request.Options, z, line, col);
            bool filled = Option(request.Options, "Fill") is { } fill
                && JgsGraphicsProperties.ToOnOff($"{verb}: Fill", fill, line, col);

            ContourPlot contour = filled ? JG.ContourF(xs, ys, z, levels) : JG.Contour(xs, ys, z, levels);
            ApplyProperties(verb, contour, request.Options, line, col);
            return HandlesFor<ContourPlot>([contour]);
        });
    }

    private static SurfaceRequest ReadSurfaceRequest(
        string verb,
        IReadOnlyList<JgsValue> args,
        HashSet<string> known,
        IReadOnlyList<string> spelled,
        int line,
        int col)
    {
        var functions = new List<IJgsCallable>();
        int i = 0;
        while (i < args.Count && args[i].Type == JgsType.Function && functions.Count < 3)
        {
            functions.Add(args[i].AsCallable);
            i++;
        }

        if (functions.Count is not (1 or 3))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}(f) takes one function of x and y, or three functions of two parameters for a parametric surface.");
        }

        // One interval covers both directions, four give each its own — MATLAB's two spellings.
        double xLow = -5, xHigh = 5, yLow = -5, yHigh = 5;
        if (i < args.Count && args[i].Type is JgsType.Array or JgsType.Number)
        {
            double[] span = ToDoubles(verb, args[i], line, col);
            if (span.Length is not (2 or 4))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: a domain is [min max] for both directions, or [xmin xmax ymin ymax].");
            }

            (xLow, xHigh) = (span[0], span[1]);
            (yLow, yHigh) = span.Length == 4 ? (span[2], span[3]) : (span[0], span[1]);
            if (!(xLow < xHigh) || !(yLow < yHigh)
                || !span.All(double.IsFinite))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: each side of a domain runs from a finite value to a larger one.");
            }

            i++;
        }

        return new SurfaceRequest(
            functions, xLow, xHigh, yLow, yHigh,
            ReadNamedOptions(verb, args, i, known, spelled, line, col));
    }

    /// <summary>
    /// Reads a function of two parameters over the whole grid, a row at a time so that a handle that
    /// can take an array is handed one, and breaks the readings that ran away.
    /// </summary>
    private static double[,] SampleGrid(
        string verb,
        IJgsCallable f,
        double[] xs,
        double[] ys,
        double poleFactor,
        int line,
        int col)
    {
        // Rows index y, which is the convention every surface and contour in this build reads.
        var z = new double[ys.Length, xs.Length];
        var row = new double[xs.Length];
        bool? vectorized = null;

        for (int r = 0; r < ys.Length; r++)
        {
            Array.Fill(row, ys[r]);
            double[] values = ReadingsOf(verb, f, [xs, row], ref vectorized, line, col);
            for (int c = 0; c < xs.Length; c++)
            {
                z[r, c] = values[c];
            }
        }

        GridSampler.BreakRunaways(z, poleFactor);
        return z;
    }

    /// <summary>The levels <c>fcontour</c> was told to draw, or none to let the axes choose.</summary>
    private static double[]? LevelsFor(
        string verb, List<(string Name, JgsValue Value)> options, double[,] z, int line, int col)
    {
        if (Option(options, "LevelList") is { } list)
        {
            double[] levels = ToDoubles($"{verb}: LevelList", list, line, col);
            return levels.Length > 0
                ? levels
                : throw new JgsRuntimeException(line, col, $"{verb}: LevelList needs at least one level.");
        }

        if (Option(options, "LevelStep") is not { } step)
        {
            return null;
        }

        double spacing = NumOf($"{verb}: LevelStep", step, line, col);
        if (!(spacing > 0) || !double.IsFinite(spacing))
        {
            throw new JgsRuntimeException(line, col, $"{verb}: LevelStep is a positive spacing.");
        }

        // A step is a list once the readings say how far it has to reach.
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (double value in z)
        {
            if (double.IsFinite(value))
            {
                low = System.Math.Min(low, value);
                high = System.Math.Max(high, value);
            }
        }

        if (!double.IsFinite(low) || !double.IsFinite(high))
        {
            return null;
        }

        var stepped = new List<double>();
        for (double level = System.Math.Ceiling(low / spacing) * spacing;
             level <= high && stepped.Count < 1000;
             level += spacing)
        {
            stepped.Add(level);
        }

        return stepped.Count > 0 ? [.. stepped] : null;
    }

    private static bool ShowContoursWanted(
        string verb, List<(string Name, JgsValue Value)> options, int line, int col) =>
        Option(options, "ShowContours") is { } value
        && JgsGraphicsProperties.ToOnOff($"{verb}: ShowContours", value, line, col);

    private static double PoleFactorFor(
        string verb, List<(string Name, JgsValue Value)> options, int line, int col) =>
        ShowPolesWanted(verb, options, line, col)
            ? new AdaptiveSamplerOptions().PoleFactor
            : double.PositiveInfinity;

    /// <summary>
    /// Writes the drawing options onto what was drawn, through the same property table <c>set</c>
    /// uses — so a name that means one thing to a script means the same thing here, and the four
    /// options that told the sampler where to look are skipped rather than written twice.
    /// </summary>
    private static void ApplyProperties(
        string verb, PlotObject plot, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "meshdensity" or "showpoles" or "showcontours" or "fill"
                    or "levellist" or "levelstep":
                    continue;

                // MATLAB's names for two of these are not this build's: a surface's edges are a mesh
                // with a width rather than a line with a style, and its transparency is the opacity
                // every drawn object here has.
                case "linewidth" when plot is SurfacePlot:
                    JgsGraphicsProperties.Set(
                        JgsHandleRegistry.EntryFor(plot), "EdgeWidth", value, line, col);
                    continue;
                case "facealpha":
                    JgsGraphicsProperties.Set(
                        JgsHandleRegistry.EntryFor(plot), "Opacity", value, line, col);
                    continue;
                case "linestyle" when plot is SurfacePlot:
                    ApplySurfaceLineStyle(verb, (SurfacePlot)plot, value, line, col);
                    continue;

                default:
                    JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(plot), name, value, line, col);
                    continue;
            }
        }
    }

    /// <summary>
    /// A surface's edges are drawn as a mesh, which is either there or not: <c>'none'</c> takes them
    /// away and <c>'-'</c> puts them back. The dashed spellings are refused by name rather than
    /// accepted and ignored, because a script that asks for a dashed mesh and gets a solid one has
    /// been told something untrue about its own figure.
    /// </summary>
    private static void ApplySurfaceLineStyle(
        string verb, SurfacePlot surface, JgsValue value, int line, int col)
    {
        string word = StrOf($"{verb}: LineStyle", value, line, col);
        switch (word)
        {
            case "none":
                surface.EdgeColor = null;
                break;
            case "-":
                surface.EdgeColor ??= Core.Drawing.Color.FromRgb(0, 0, 0);
                break;
            default:
                throw new JgsRuntimeException(line, col,
                    $"{verb}: LineStyle on a surface is 'none' or '-'. This build draws a surface's edges "
                        + "as a mesh, which has no dash pattern of its own.");
        }
    }

    // --- the implicit plotters ------------------------------------------------------------------

    /// <summary>The properties <c>fimplicit</c> accepts after its function.</summary>
    private static readonly string[] ImplicitLineOptionNames =
    [
        "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName", "MeshDensity",
    ];

    /// <summary>The properties <c>fimplicit3</c> accepts after its function.</summary>
    private static readonly string[] ImplicitSurfaceOptionNames =
    [
        "EdgeColor", "FaceColor", "FaceAlpha", "DisplayName", "MeshDensity",
    ];

    private static readonly HashSet<string> ImplicitLineOptions =
        new(ImplicitLineOptionNames, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ImplicitSurfaceOptions =
        new(ImplicitSurfaceOptionNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>MATLAB's grid density for the two implicit plotters.</summary>
    private const int ImplicitLineMeshDensity = 151;

    private const int ImplicitSurfaceMeshDensity = 35;

    /// <summary>
    /// <c>fimplicit(f)</c> draws where <c>f(x, y)</c> is zero — the curve the function does not
    /// solve for and cannot be asked to.
    /// </summary>
    /// <remarks>
    /// This is marching squares at one level, which is <c>fcontour(f, 'LevelList', 0)</c> in
    /// everything but what it hands back. It is a line rather than a contour because that is what it
    /// is: one curve, whose points a script reads through <c>XData</c> the way it reads any other
    /// line's. The pieces of a curve that comes apart — a hyperbola has two branches — are joined by
    /// a gap, so one object holds all of it without drawing anything between the parts.
    /// </remarks>
    private static JgsValue Implicit(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            SurfaceRequest request = ReadSurfaceRequest(
                verb, rest, ImplicitLineOptions, ImplicitLineOptionNames, line, col);
            if (request.Functions.Count != 1)
            {
                throw new JgsRuntimeException(line, col, $"{verb}(f) takes one function of x and y.");
            }

            int density = MeshDensityOf(verb, request.Options, ImplicitLineMeshDensity, line, col);
            double[] xs = GridSampler.EvenlySpaced(request.XLow, request.XHigh, density);
            double[] ys = GridSampler.EvenlySpaced(request.YLow, request.YHigh, density);

            // Nothing is trimmed as a runaway here: the only reading that matters is where the field
            // changes sign, and a value far from zero is as good a witness to that as a near one.
            double[,] z = SampleGrid(verb, request.Functions[0], xs, ys, double.PositiveInfinity, line, col);

            double spacing = System.Math.Min(
                (request.XHigh - request.XLow) / (density - 1),
                (request.YHigh - request.YLow) / (density - 1));
            IReadOnlyList<Core.Primitives.Point2D[]> paths = ContourPaths.Assemble(
                MarchingSquares.Lines(xs, ys, z, 0), spacing / 1000);

            var px = new List<double>();
            var py = new List<double>();
            foreach (Core.Primitives.Point2D[] path in paths)
            {
                if (px.Count > 0)
                {
                    px.Add(double.NaN);
                    py.Add(double.NaN);
                }

                foreach (Core.Primitives.Point2D point in path)
                {
                    px.Add(point.X);
                    py.Add(point.Y);
                }
            }

            LinePlot plot = JG.Plot([.. px], [.. py]);
            foreach ((string name, JgsValue value) in request.Options)
            {
                if (!name.Equals("MeshDensity", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyLineOption(verb, plot, name, value, line, col);
                }
            }

            return HandlesFor<LinePlot>([plot]);
        });
    }

    /// <summary>
    /// <c>fimplicit3(f)</c> draws the surface where <c>f(x, y, z)</c> is zero, as a patch over the
    /// triangulation <see cref="MarchingTetrahedra"/> cuts out of the box.
    /// </summary>
    private static JgsValue Implicit3(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            var functions = new List<IJgsCallable>();
            int i = 0;
            while (i < rest.Count && rest[i].Type == JgsType.Function && functions.Count < 1)
            {
                functions.Add(rest[i].AsCallable);
                i++;
            }

            if (functions.Count != 1)
            {
                throw new JgsRuntimeException(line, col, $"{verb}(f) takes one function of x, y and z.");
            }

            double[] box = [-5, 5, -5, 5, -5, 5];
            if (i < rest.Count && rest[i].Type is JgsType.Array or JgsType.Number)
            {
                double[] given = ToDoubles(verb, rest[i], line, col);
                if (given.Length is not (2 or 6) || !given.All(double.IsFinite))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: a box is [min max] for every direction, or [xmin xmax ymin ymax zmin zmax], all finite.");
                }

                box = given.Length == 6 ? given : [.. Enumerable.Repeat(given, 3).SelectMany(v => v)];
                for (int side = 0; side < 6; side += 2)
                {
                    if (!(box[side] < box[side + 1]))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{verb}: each side of a box runs from a value to a larger one.");
                    }
                }

                i++;
            }

            List<(string Name, JgsValue Value)> options = ReadNamedOptions(
                verb, rest, i, ImplicitSurfaceOptions, ImplicitSurfaceOptionNames, line, col);
            int density = MeshDensityOf(verb, options, ImplicitSurfaceMeshDensity, line, col);

            double[] xs = GridSampler.EvenlySpaced(box[0], box[1], density);
            double[] ys = GridSampler.EvenlySpaced(box[2], box[3], density);
            double[] zs = GridSampler.EvenlySpaced(box[4], box[5], density);
            double[,,] field = SampleField(verb, functions[0], xs, ys, zs, line, col);

            IsoMesh mesh = MarchingTetrahedra.Surface(xs, ys, zs, field, 0);
            if (mesh.Faces.Length == 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: the function is never zero anywhere in the box, so there is no surface to draw.");
            }

            PatchPlot patch = JG.Patch(mesh.X, mesh.Y, mesh.Z, mesh.Faces);
            ApplyProperties(verb, patch, options, line, col);
            return HandlesFor<PatchPlot>([patch]);
        });
    }

    /// <summary>
    /// Reads a function of three parameters over the whole box, a run along x at a time, into the
    /// <c>[row, column, page]</c> field the surface finder reads.
    /// </summary>
    private static double[,,] SampleField(
        string verb, IJgsCallable f, double[] xs, double[] ys, double[] zs, int line, int col)
    {
        var field = new double[ys.Length, xs.Length, zs.Length];
        var yRow = new double[xs.Length];
        var zRow = new double[xs.Length];
        bool? vectorized = null;

        for (int r = 0; r < ys.Length; r++)
        {
            Array.Fill(yRow, ys[r]);
            for (int p = 0; p < zs.Length; p++)
            {
                Array.Fill(zRow, zs[p]);
                double[] values = ReadingsOf(verb, f, [xs, yRow, zRow], ref vectorized, line, col);
                for (int c = 0; c < xs.Length; c++)
                {
                    field[r, c, p] = values[c];
                }
            }
        }

        return field;
    }

    /// <summary>
    /// Reads one function at every point of a round, whether the round is a run of parameters along a
    /// curve or a row across a grid, and whether the function can take them together or one at a time.
    /// </summary>
    private static double[] ReadingsOf(
        string verb,
        IJgsCallable f,
        IReadOnlyList<double[]> arguments,
        ref bool? vectorized,
        int line,
        int col)
    {
        int count = arguments[0].Length;
        if (vectorized != false)
        {
            try
            {
                var whole = new JgsValue[arguments.Count];
                for (int a = 0; a < arguments.Count; a++)
                {
                    whole[a] = Numbers([.. arguments[a]]);
                }

                double[] answered = ToDoubles(verb, f.Call(whole, line, col), line, col);
                if (answered.Length == count)
                {
                    vectorized = true;
                    return answered;
                }
            }
            catch (JgsException)
            {
                // The handle cannot take an array. That is not an error yet — it is the other way of
                // asking, and the error it would raise is raised again below with its own parameter.
            }

            vectorized = false;
        }

        var values = new double[count];
        var one = new JgsValue[arguments.Count];
        for (int i = 0; i < count; i++)
        {
            for (int a = 0; a < arguments.Count; a++)
            {
                one[a] = JgsValue.Number(arguments[a][i]);
            }

            JgsValue answer = f.Call(one, line, col);
            if (answer.Type is JgsType.Number or JgsType.Bool)
            {
                values[i] = answer.AsNumber;
                continue;
            }

            double[] single = ToDoubles(verb, answer, line, col);
            values[i] = single.Length == 1
                ? single[0]
                : throw new JgsRuntimeException(line, col,
                    $"{verb}: the function has to answer with one value for each parameter it is given.");
        }

        return values;
    }
}
