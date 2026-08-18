using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Maths.Geometry;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    /// <summary>The trailing name-value options an arrow field accepts.</summary>
    private static readonly HashSet<string> QuiverOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Color", "LineWidth", "LineStyle", "AutoScale", "AutoScaleFactor", "ShowArrowHead",
        "MaxHeadSize", "DisplayName",
    };

    /// <summary>
    /// Registers the M45.E surface variants and shape generators. Every one of them is a
    /// rearrangement of geometry the object model already draws — a padded grid, a polygon per row,
    /// a two-column parametric strip — rather than a new kind of drawing.
    /// </summary>
    private static void RegisterSurfaceVariantBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // For a verb that hands back a handle: drawing is what the statement is for, so the handle
        // does not echo as `ans` the way an ordinary value would. Same rule `plot` has always had.
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        Define("surfc", OnNamedAxes((args, line, col) => Surface3D("surfc", args, line, col,
            (x, y, z) => JG.SurfC(x, y, z), z => JG.SurfC(z), (x, y, z) => JG.SurfC(x, y, z))));

        Define("meshz", OnNamedAxes((args, line, col) => Surface3D("meshz", args, line, col,
            (x, y, z) => JG.MeshZ(x, y, z), z => JG.MeshZ(z), (x, y, z) => JG.MeshZ(x, y, z))));

        // A waterfall needs a row coordinate per curve and a column coordinate along it, so a
        // parametric grid has nowhere to put its second position -- it collapses to the generating
        // vectors, which is also what a meshgrid pair means.
        DefineSilent("waterfall", OnNamedAxes((args, line, col) => Waterfall(args, line, col)));
        DefineSilent("ribbon", OnNamedAxes((args, line, col) => Ribbon(args, line, col)));
        // contour3 lives with contour and contourf in RegisterDecorationBuiltins, which runs later
        // and wins. The registration that stood here was shadowed and never ran.

        DefineSilent("quiver", OnNamedAxes((args, line, col) => Quiver("quiver", args, line, col, three: false)));
        DefineSilent("quiver3", OnNamedAxes((args, line, col) => Quiver("quiver3", args, line, col, three: true)));

        DefineSilent("trisurf", OnNamedAxes((args, line, col) => Triangulated("trisurf", args, line, col, wireframe: false)));
        DefineSilent("trimesh", OnNamedAxes((args, line, col) => Triangulated("trimesh", args, line, col, wireframe: true)));

        DefineShape(env, dialect, "sphere", (args, line, col) =>
        {
            ArityRange("sphere", args, 0, 1, line, col);
            return ShapeGrids.Sphere(Facets("sphere", args, 0, 20, line, col));
        });

        DefineShape(env, dialect, "cylinder", (args, line, col) =>
        {
            ArityRange("cylinder", args, 0, 2, line, col);
            double[] radii = args.Count >= 1 ? Numbers("cylinder", args[0], line, col) : [1, 1];
            return ShapeGrids.Cylinder(radii, Facets("cylinder", args, 1, 20, line, col));
        });

        DefineShape(env, dialect, "ellipsoid", (args, line, col) =>
        {
            ArityRange("ellipsoid", args, 6, 7, line, col);
            return ShapeGrids.Ellipsoid(
                Num("ellipsoid", args, 0, line, col),
                Num("ellipsoid", args, 1, line, col),
                Num("ellipsoid", args, 2, line, col),
                Num("ellipsoid", args, 3, line, col),
                Num("ellipsoid", args, 4, line, col),
                Num("ellipsoid", args, 5, line, col),
                Facets("ellipsoid", args, 6, 20, line, col));
        });
    }

    /// <summary>
    /// Registers one shape generator. The two dialects read a single output differently, the same
    /// way <c>meshgrid</c> and <c>surfnorm</c> already do: MATLAB's <c>sphere(20)</c> draws — that is
    /// what a graphics verb with no outputs means there — while JGS hands back all three grids so
    /// <c>let [X, Y, Z] = sphere(20)</c> can destructure them. Asking for several outputs draws
    /// nothing in either dialect.
    /// </summary>
    private static void DefineShape(
        JgsEnvironment env,
        JgsDialect dialect,
        string name,
        Func<IReadOnlyList<JgsValue>, int, int, (double[,] X, double[,] Y, double[,] Z)> build)
    {
        // sphere, cylinder and ellipsoid all document a leading axes, and all three come through
        // here, so the peel belongs in the shared helper rather than three times over.
        JgsValue Single(IReadOnlyList<JgsValue> rawArgs, int line, int col)
        {
            (AxesModel? target, IReadOnlyList<JgsValue> args) = PeelAxes(rawArgs);
            return OnAxes(target, () => Drawn(args, line, col));
        }

        JgsValue Drawn(IReadOnlyList<JgsValue> args, int line, int col)
        {
            (double[,] x, double[,] y, double[,] z) = Build(args, line, col);
            if (!dialect.IsMatlab)
            {
                return JgsValue.Array([Grid(x), Grid(y), Grid(z)]);
            }

            JG.Surf(x, y, z);
            return JgsValue.Null;
        }

        JgsValue[] Multi(IReadOnlyList<JgsValue> rawArgs, int wanted, int line, int col)
        {
            (AxesModel? _, IReadOnlyList<JgsValue> args) = PeelAxes(rawArgs);
            (double[,] x, double[,] y, double[,] z) = Build(args, line, col);
            return [Grid(x), Grid(y), Grid(z)];
        }

        (double[,] X, double[,] Y, double[,] Z) Build(IReadOnlyList<JgsValue> args, int line, int col)
        {
            try
            {
                return build(args, line, col);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        }

        env.Declare(name, JgsValue.Function(
            new BuiltinFunction(name, Single) { MultiOutput = Multi, AutoCallsBare = true }));
    }

    /// <summary>A facet count argument, or the default when it was left out.</summary>
    private static int Facets(string name, IReadOnlyList<JgsValue> args, int index, int fallback, int line, int col)
    {
        if (index >= args.Count)
        {
            return fallback;
        }

        int n = Count(name, args, index, line, col);
        if (n < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the facet count must be at least 1.");
        }

        return n;
    }

    /// <summary>
    /// <c>waterfall(z)</c> or <c>waterfall(x, y, z)</c>: each row of z becomes a curve in space with
    /// the area under it filled down to a common base.
    /// </summary>
    private static JgsValue Waterfall(IReadOnlyList<JgsValue> args, int line, int col)
    {
        try
        {
            if (args.Count == 1)
            {
                return JgsHandleRegistry.For(JG.Waterfall(Matrix("waterfall", args, 0, line, col)));
            }

            Arity("waterfall", args, 3, line, col);
            return JgsHandleRegistry.For(JG.Waterfall(
                GridVector("waterfall", args, 0, firstRow: true, line, col),
                GridVector("waterfall", args, 1, firstRow: false, line, col),
                Matrix("waterfall", args, 2, line, col)));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>
    /// <c>ribbon(y)</c>, <c>ribbon(x, y)</c> or <c>ribbon(x, y, width)</c>: the columns of y as flat
    /// strips standing side by side.
    /// </summary>
    private static JgsValue Ribbon(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("ribbon", args, 1, 3, line, col);
        double[,] z = Matrix("ribbon", args, args.Count == 1 ? 0 : 1, line, col);
        double[] rowPositions = args.Count == 1
            ? Ramp1(z.GetLength(0))
            : GridVector("ribbon", args, 0, firstRow: false, line, col);
        double width = args.Count == 3 ? Num("ribbon", args, 2, line, col) : 0.75;

        try
        {
            // One strip per column, so the answer is a row of handles rather than one — which is
            // also what MATLAB hands back, and what `set(h, 'FaceAlpha', 0.5)` then walks.
            return JgsGraphicsProperties.HandleRow([.. JG.Ribbon(rowPositions, z, width)]);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>
    /// <c>trisurf(tri, x, y, z)</c> / <c>trimesh(...)</c>, with an optional per-vertex color array.
    /// The triangle table is one-based, as everything MATLAB's <c>delaunay</c> produces is.
    /// </summary>
    private static JgsValue Triangulated(
        string name, IReadOnlyList<JgsValue> args, int line, int col, bool wireframe)
    {
        ArityRange(name, args, 4, 5, line, col);
        double[,] table = Matrix(name, args, 0, line, col);
        double[] x = DoubleArray(name, args, 1, line, col);
        double[] y = DoubleArray(name, args, 2, line, col);
        double[] z = DoubleArray(name, args, 3, line, col);
        double[]? c = args.Count == 5 ? DoubleArray(name, args, 4, line, col) : null;

        int faceCount = table.GetLength(0);
        int perFace = table.GetLength(1);
        if (perFace < 3)
        {
            throw new JgsRuntimeException(
                line, col, $"{name}: the triangle table needs at least three columns, but has {perFace}.");
        }

        var faces = new int[faceCount][];
        for (int f = 0; f < faceCount; f++)
        {
            var face = new int[perFace];
            for (int v = 0; v < perFace; v++)
            {
                double index = table[f, v];
                if (!double.IsFinite(index) || index != System.Math.Floor(index))
                {
                    throw new JgsRuntimeException(
                        line, col, $"{name}: the triangle table must hold whole vertex numbers.");
                }

                face[v] = (int)index - 1;
            }

            faces[f] = face;
        }

        try
        {
            return JgsHandleRegistry.For(wireframe
                ? JG.TriMesh(faces, x, y, z, c)
                : JG.TriSurf(faces, x, y, z, c));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>
    /// <c>quiver(x, y, u, v)</c> / <c>quiver3(x, y, z, u, v, w)</c>, each also accepting the
    /// components alone, a trailing scale, a line spec, and name-value options.
    /// </summary>
    private static JgsValue Quiver(string verb, IReadOnlyList<JgsValue> args, int line, int col, bool three)
    {
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, QuiverOptionNames);

        // 'filled' fills the arrowheads, which these already are; accepting and dropping it keeps a
        // MATLAB script from failing over a word that changes nothing here.
        var positional = new List<JgsValue>(data);
        for (int i = positional.Count - 1; i >= 0; i--)
        {
            if (positional[i].Type == JgsType.String
                && positional[i].AsString.Equals("filled", StringComparison.OrdinalIgnoreCase))
            {
                positional.RemoveAt(i);
            }
        }

        // A trailing string is a line spec, and a trailing number after the components is the scale.
        LineSpec spec = default;
        bool hasSpec = false;
        if (positional.Count > 0 && positional[^1].Type == JgsType.String)
        {
            spec = LineSpec.Parse(positional[^1].AsString);
            hasSpec = true;
            positional.RemoveAt(positional.Count - 1);
        }

        int components = three ? 6 : 4;
        double? scale = null;
        if (positional.Count == components + 1 || positional.Count == (components / 2) + 1)
        {
            scale = NumOf($"{verb}: the scale", positional[^1], line, col);
            positional.RemoveAt(positional.Count - 1);
        }

        if (positional.Count != components && positional.Count != components / 2)
        {
            throw new JgsRuntimeException(
                line,
                col,
                three
                    ? $"{verb} takes (u, v, w) or (x, y, z, u, v, w), with an optional scale."
                    : $"{verb} takes (u, v) or (x, y, u, v), with an optional scale.");
        }

        double[][] fields = new double[positional.Count][];
        int width = 0, height = 0;
        for (int i = 0; i < positional.Count; i++)
        {
            fields[i] = ToDoubles(verb, positional[i], line, col);
            int rows = JgsMatrix.RowCount(positional[i]);
            if (rows > 1 && fields[i].Length > rows)
            {
                height = rows;
                width = fields[i].Length / rows;
            }
        }

        // Positions given as vectors against matrix components are a meshgrid in shorthand, which is
        // what quiver(x, y, U, V) means whenever U is a matrix.
        int count = fields[^1].Length;
        if (positional.Count == components && height > 1)
        {
            fields[0] = Spread(fields[0], height, width, downColumns: false);
            fields[1] = Spread(fields[1], height, width, downColumns: true);
            if (three)
            {
                fields[2] = Spread(fields[2], height, width, downColumns: true);
            }
        }

        foreach (double[] field in fields)
        {
            if (field.Length != count)
            {
                throw new JgsRuntimeException(
                    line, col, $"{verb}: every argument must hold the same number of values.");
            }
        }

        double[] Zeros() => new double[count];
        double[] Index()
        {
            // Components alone are placed on the grid their own shape implies, exactly as MATLAB
            // does for quiver(U, V): column index across, row index down.
            var indices = new double[count];
            int rows = height > 1 ? height : count;
            for (int i = 0; i < count; i++)
            {
                indices[i] = i % rows;
            }

            return indices;
        }

        QuiverPlot plot;
        try
        {
            plot = positional.Count == components
                ? three
                    ? JG.Quiver3(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5])
                    : JG.Quiver(fields[0], fields[1], fields[2], fields[3])
                : three
                    ? JG.Quiver3(Index(), Zeros(), Zeros(), fields[0], fields[1], fields[2])
                    : JG.Quiver(Index(), Zeros(), fields[0], fields[1]);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        if (scale is { } factor)
        {
            // MATLAB reads quiver(..., 0) as "leave the components alone", and any other number as a
            // multiplier on top of the automatic fit.
            plot.AutoScale = factor != 0;
            if (factor == 0)
            {
                plot.Scale = 1;
            }
            else
            {
                plot.AutoScaleFactor = 0.9 * factor;
            }
        }

        if (hasSpec)
        {
            if (spec.Color is { } color)
            {
                plot.Color = color;
            }

            if (spec.Dash == DashStyle.None)
            {
                plot.LineWidth = 0;
            }
        }

        ApplyQuiverOptions(verb, plot, options, line, col);

        // The arrows are a graphics object like any other, so hand back a handle to them. Until M69's
        // property probe asked, this returned nothing and `h = quiver(...); h.LineWidth = 2` — which
        // is how MATLAB documents the verb — had no object to write to.
        return JgsHandleRegistry.For(plot);
    }

    private static void ApplyQuiverOptions(
        string verb, QuiverPlot plot, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "color":
                    plot.Color = OptionColor(value, line, col, verb);
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf($"{verb}: LineWidth", value, line, col);
                    break;
                case "linestyle":
                    if (IsNone(value))
                    {
                        plot.LineWidth = 0;
                    }

                    break;
                case "autoscale":
                    plot.AutoScale = Toggle($"{verb}: AutoScale", value, line, col);
                    break;
                case "autoscalefactor":
                    plot.AutoScaleFactor = NumOf($"{verb}: AutoScaleFactor", value, line, col);
                    break;
                case "showarrowhead":
                    plot.ShowArrowHead = Toggle($"{verb}: ShowArrowHead", value, line, col);
                    break;
                case "maxheadsize":
                    plot.MaxHeadSize = NumOf($"{verb}: MaxHeadSize", value, line, col);
                    break;
                case "displayname":
                    plot.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{verb}: unknown option '{name}'.");
            }
        }
    }

    /// <summary>
    /// A generating vector spread over a grid, in the same column-major order the flattened
    /// components arrive in: down the columns for a row coordinate, across them for a column one.
    /// </summary>
    private static double[] Spread(double[] values, int rows, int cols, bool downColumns)
    {
        if (values.Length == rows * cols)
        {
            return values;
        }

        var spread = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                int source = downColumns ? r : c;
                spread[(c * rows) + r] = values[System.Math.Min(source, values.Length - 1)];
            }
        }

        return spread;
    }

    /// <summary>A ramp starting at one, which is where MATLAB's implicit row and column numbers start.</summary>
    private static double[] Ramp1(int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = i + 1;
        }

        return values;
    }

    /// <summary>A coordinate grid as a script matrix.</summary>
    private static JgsValue Grid(double[,] values) =>
        JgsMatrix.Build(values.GetLength(0), values.GetLength(1), (r, c) => values[r, c]);

    /// <summary>
    /// A MATLAB on/off property as a boolean. The words are what a script writes; a logical is what
    /// a computed flag arrives as, and both have to be accepted for either to be usable.
    /// </summary>
    private static bool Toggle(string what, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            string word = value.AsString;
            if (word.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (word.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new JgsRuntimeException(line, col, $"{what} must be 'on' or 'off', but was '{word}'.");
        }

        return NumOf(what, value, line, col) != 0;
    }
}
