using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Geometry;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M57 wave B: the three verbs that draw a triangulation or its dual.
/// <para>
/// None of them is a new kind of drawing. A Voronoi diagram and a triangulation are both sets of
/// straight segments, which one line plot with gaps in it draws; a tetrahedral mesh is a set of
/// triangular faces, which is what a patch already is. What the wave adds is the arithmetic that
/// turns a table of vertex numbers into those segments and faces, and the two-output forms that hand
/// that arithmetic back instead of drawing it.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The trailing name-value options the triangulation line verbs accept.</summary>
    private static readonly HashSet<string> MeshLineOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName",
    };

    private static void RegisterMeshPlotBuiltins(JgsEnvironment env)
    {
        // voronoi and triplot draw when they are asked for one output and answer with the geometry
        // when they are asked for two — the rule rose already follows, and the reason both can be
        // written as "work out the segments, then draw them" with one function for each half.
        void DefineDrawOrData(
            string name,
            Func<IReadOnlyList<JgsValue>, int, int, JgsValue> draw,
            Func<IReadOnlyList<JgsValue>, int, int, JgsValue[]> data) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, draw)
            {
                BindsAnsAsStatement = false,
                MultiOutput = (args, wanted, line, col) => wanted >= 2
                    ? data(args, line, col)
                    : [draw(args, line, col)],
            }));

        DefineDrawOrData("voronoi", VoronoiPlot, VoronoiEdgeData);
        DefineDrawOrData("triplot", TriPlot, TriPlotData);

        env.Declare("tetramesh", JgsValue.Function(
            new BuiltinFunction("tetramesh", (args, line, col) => TetraMesh(args, line, col))
            {
                BindsAnsAsStatement = false,
            }));
    }

    // --- voronoi ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>voronoi(x, y)</c>, <c>voronoi(x, y, TRI)</c>, either with a trailing line spec: the points
    /// with a marker on each, and the boundaries between the regions they own.
    /// <para>
    /// Two handles come back, the points first and the edges second, which is the order MATLAB puts
    /// them in. Half the diagram runs to infinity, so the rays are cut off at a box round the whole
    /// picture — see <see cref="VoronoiDiagram.Segments"/> for why there is no other answer.
    /// </para>
    /// </summary>
    private static JgsValue VoronoiPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            (double[] x, double[] y, VoronoiDiagram diagram, string? spec) =
                VoronoiCall(rest, line, col);

            // The points go down first, through the verb that resets the axes, so a second voronoi
            // call replaces the first rather than piling on top of it.
            LinePlot sites = JG.Plot(x, y, ".");
            sites.Name = "Voronoi sites";

            (Point2D From, Point2D To)[] segments = diagram.Segments();
            (double[] px, double[] py) = SegmentPath(segments);
            LinePlot edges = JG.Gca().AddLine(px, py);
            edges.Name = "Voronoi";
            edges.Color ??= PaletteColorFor(edges);
            if (spec is not null)
            {
                ApplyMeshSpec(edges, spec);
            }

            return HandlesFor<PlotObject>([sites, edges]);
        });
    }

    /// <summary>
    /// <c>[vx, vy] = voronoi(...)</c>: the finite edges as two 2-by-n matrices, one column per
    /// segment, which is exactly what <c>plot(vx, vy)</c> wants. Nothing is drawn — asking for the
    /// numbers is asking to draw them yourself.
    /// </summary>
    private static JgsValue[] VoronoiEdgeData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (_, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        (_, _, VoronoiDiagram diagram, _) = VoronoiCall(rest, line, col);
        (Point2D From, Point2D To)[] segments = diagram.Segments();

        return
        [
            JgsMatrix.Build(2, segments.Length, (r, c) => r == 0 ? segments[c].From.X : segments[c].To.X),
            JgsMatrix.Build(2, segments.Length, (r, c) => r == 0 ? segments[c].From.Y : segments[c].To.Y),
        ];
    }

    /// <summary>The arguments both <c>voronoi</c> forms share, and the diagram they name.</summary>
    private static (double[] X, double[] Y, VoronoiDiagram Diagram, string? Spec) VoronoiCall(
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        (IReadOnlyList<JgsValue> positional, string? spec) = PeelLineSpec(args);
        if (positional.Count is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                "voronoi expects (x, y), optionally a triangulation and a line spec.");
        }

        double[] x = DoubleArray("voronoi", positional, 0, line, col);
        double[] y = DoubleArray("voronoi", positional, 1, line, col);
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col, "voronoi needs the same number of x and y coordinates.");
        }

        try
        {
            VoronoiDiagram diagram = positional.Count == 3
                ? Voronoi.FromTriangulation(x, y, Connectivity("voronoi", positional[2], 3, x.Length, line, col))
                : Voronoi.FromPoints(x, y);
            return (x, y, diagram, spec);
        }
        catch (ArgumentException failure)
        {
            throw new JgsRuntimeException(line, col, $"voronoi: {Reason(failure)}");
        }
    }

    // --- triplot ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>triplot(TRI, x, y)</c>, with an optional line spec and name-value options: the edges of
    /// every triangle, drawn as one line that lifts its pen between them.
    /// </summary>
    private static JgsValue TriPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            (double[] px, double[] py, string? spec, List<(string Name, JgsValue Value)> options) =
                TriPlotCall(rest, line, col);

            LinePlot plot = JG.Plot(px, py, spec);
            plot.Name = "Triplot";
            plot.Color ??= PaletteColorFor(plot);
            foreach ((string name, JgsValue value) in options)
            {
                ApplyLineOption("triplot", plot, name, value, line, col);
            }

            return Handle(plot);
        });
    }

    /// <summary>
    /// <c>[xd, yd] = triplot(...)</c>: the same path as two columns, gaps and all, so a caller can
    /// draw it however they like. Nothing is drawn here.
    /// </summary>
    private static JgsValue[] TriPlotData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (_, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        (double[] px, double[] py, _, _) = TriPlotCall(rest, line, col);
        return
        [
            JgsMatrix.Build(px.Length, 1, (r, _) => px[r]),
            JgsMatrix.Build(py.Length, 1, (r, _) => py[r]),
        ];
    }

    /// <summary>The arguments both <c>triplot</c> forms share, and the path they describe.</summary>
    private static (double[] X, double[] Y, string? Spec, List<(string Name, JgsValue Value)> Options) TriPlotCall(
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        (IReadOnlyList<JgsValue> withSpec, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, MeshLineOptionNames);
        (IReadOnlyList<JgsValue> positional, string? spec) = PeelLineSpec(withSpec);
        if (positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "triplot expects (TRI, x, y), optionally a line spec and 'Name', value options.");
        }

        double[] x = DoubleArray("triplot", positional, 1, line, col);
        double[] y = DoubleArray("triplot", positional, 2, line, col);
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col, "triplot needs the same number of x and y coordinates.");
        }

        int[,] table = Connectivity("triplot", positional[0], 3, x.Length, line, col);
        int triangles = table.GetLength(0);

        // Four points per triangle — round the three corners and back to the first — then a gap, so
        // the whole mesh is one series and one handle, as MATLAB's single line object is.
        var px = new double[triangles * 5];
        var py = new double[triangles * 5];
        for (int t = 0; t < triangles; t++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                int v = table[t, corner % 3];
                px[(t * 5) + corner] = x[v];
                py[(t * 5) + corner] = y[v];
            }

            px[(t * 5) + 4] = double.NaN;
            py[(t * 5) + 4] = double.NaN;
        }

        return (px, py, spec, options);
    }

    // --- tetramesh --------------------------------------------------------------------------------

    /// <summary>
    /// <c>tetramesh(T, X)</c> and <c>tetramesh(T, X, C)</c> with trailing patch options: the four
    /// faces of every tetrahedron, drawn as one patch in space.
    /// <para>
    /// MATLAB draws one patch object per tetrahedron so each can be shown and hidden on its own, and
    /// hands back that many handles. This draws them all into one patch, which is one handle — a
    /// deliberate divergence: the depth sort that makes a solid mesh read correctly has to see every
    /// face at once, and a script that wants a tetrahedron to itself can call <c>patch</c> for it.
    /// </para>
    /// </summary>
    private static JgsValue TetraMesh(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            (IReadOnlyList<JgsValue> positional, List<(string Name, JgsValue Value)> options) =
                SplitTrailingOptions(rest, PatchOptionNames);
            if (positional.Count is < 2 or > 3)
            {
                throw new JgsRuntimeException(line, col,
                    "tetramesh expects (T, X) or (T, X, C), then 'Name', value options.");
            }

            double[,] points = RectOf("tetramesh", positional[1], line, col);
            if (points.GetLength(1) != 3)
            {
                throw new JgsRuntimeException(line, col,
                    "tetramesh: the vertices are an m-by-3 matrix of points in space.");
            }

            int vertices = points.GetLength(0);
            var x = new double[vertices];
            var y = new double[vertices];
            var z = new double[vertices];
            for (int v = 0; v < vertices; v++)
            {
                x[v] = points[v, 0];
                y[v] = points[v, 1];
                z[v] = points[v, 2];
            }

            int[,] table = Connectivity("tetramesh", positional[0], 4, vertices, line, col);
            int count = table.GetLength(0);

            // The four faces of a tetrahedron are its four corners taken three at a time.
            int[][] corners = [[0, 1, 2], [0, 1, 3], [0, 2, 3], [1, 2, 3]];
            var faces = new int[count * 4][];
            var colors = new double[count * 4];
            double[]? given = positional.Count == 3
                ? DoubleArray("tetramesh", positional, 2, line, col)
                : null;
            if (given is not null && given.Length != count)
            {
                throw new JgsRuntimeException(line, col,
                    $"tetramesh: C names a colour per tetrahedron, so it needs {count} values, not {given.Length}.");
            }

            for (int t = 0; t < count; t++)
            {
                for (int f = 0; f < 4; f++)
                {
                    faces[(t * 4) + f] = [table[t, corners[f][0]], table[t, corners[f][1]], table[t, corners[f][2]]];

                    // Every face of a tetrahedron takes the same colour, so the solid reads as one
                    // body rather than four unrelated triangles.
                    colors[(t * 4) + f] = given is not null ? given[t] : t + 1;
                }
            }

            PatchPlot patch;
            try
            {
                patch = JG.Patch(x, y, z, faces);
            }
            catch (ArgumentException failure)
            {
                throw new JgsRuntimeException(line, col, $"tetramesh: {Reason(failure)}");
            }

            patch.ColorData = colors;
            patch.Name = "Tetramesh";
            JG.Gca().Is3D = true;
            ApplyPatchOptions("tetramesh", patch, options, line, col);
            return Handle(patch);
        });
    }

    // --- shared -----------------------------------------------------------------------------------

    /// <summary>
    /// A one-based connectivity table of <paramref name="width"/> vertex numbers per row, as every
    /// MATLAB triangulation is, checked against the number of points it may name.
    /// </summary>
    private static int[,] Connectivity(
        string verb, JgsValue value, int width, int points, int line, int col)
    {
        double[,] table = RectOf(verb, value, line, col);
        if (table.GetLength(1) != width)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the connectivity list needs {width} columns, but has {table.GetLength(1)}.");
        }

        int rows = table.GetLength(0);
        var numbers = new int[rows, width];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double entry = table[r, c];
                if (!double.IsFinite(entry) || entry != System.Math.Floor(entry))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: the connectivity list must hold whole vertex numbers.");
                }

                int index = (int)entry - 1;
                if (index < 0 || index >= points)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: vertex number {entry:G6} is outside the {points} points given.");
                }

                numbers[r, c] = index;
            }
        }

        return numbers;
    }

    /// <summary>A trailing line spec split off the arguments, if the call ended with one.</summary>
    private static (IReadOnlyList<JgsValue> Positional, string? Spec) PeelLineSpec(IReadOnlyList<JgsValue> args)
    {
        if (args.Count == 0 || args[^1].Type != JgsType.String)
        {
            return (args, null);
        }

        var positional = new JgsValue[args.Count - 1];
        for (int i = 0; i < positional.Length; i++)
        {
            positional[i] = args[i];
        }

        return (positional, args[^1].AsString);
    }

    /// <summary>Applies a line spec to a series the drawing verb built by hand rather than through <c>plot</c>.</summary>
    private static void ApplyMeshSpec(LinePlot plot, string spec)
    {
        LineSpec parsed = LineSpec.Parse(spec);
        if (parsed.Color is { } color)
        {
            plot.Color = color;
        }

        if (parsed.Dash is { } dash)
        {
            plot.DashStyle = dash;
        }

        if (parsed.Marker is { } marker)
        {
            plot.Marker = marker;
        }
    }

    /// <summary>Segments as one polyline with a gap between each, which is what a NaN sample means.</summary>
    private static (double[] X, double[] Y) SegmentPath((Point2D From, Point2D To)[] segments)
    {
        var x = new double[segments.Length * 3];
        var y = new double[segments.Length * 3];
        for (int s = 0; s < segments.Length; s++)
        {
            x[s * 3] = segments[s].From.X;
            y[s * 3] = segments[s].From.Y;
            x[(s * 3) + 1] = segments[s].To.X;
            y[(s * 3) + 1] = segments[s].To.Y;
            x[(s * 3) + 2] = double.NaN;
            y[(s * 3) + 2] = double.NaN;
        }

        return (x, y);
    }

    /// <summary>
    /// A kernel refusal without .NET's parameter suffix, which is a spelling no script asked about.
    /// </summary>
    private static string Reason(ArgumentException failure)
    {
        int suffix = failure.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return suffix < 0 ? failure.Message : failure.Message[..suffix];
    }
}
