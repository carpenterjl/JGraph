using JGraph.Api;
using JGraph.Maths.Contours;
using JGraph.Maths.Volumes;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M59: the verbs that follow a field rather than measure it — streamlines and the shapes built along
/// them, plus the cones and the sliced contours that show a field a plane at a time.
/// </summary>
/// <remarks>
/// <para>
/// <c>stream2</c> and <c>stream3</c> answer with the traced points and draw nothing; <c>streamline</c>
/// draws points it is handed, or traces them first when it is handed a field instead. Keeping the
/// tracing and the drawing in separate verbs is MATLAB's arrangement and a good one — it is what lets
/// a script measure a line before deciding how to show it.
/// </para>
/// <para>
/// A traced line is a list of points, and the three shapes built along one — the ribbon, the tube and
/// the cone — differ only in what is swept along it. That work is
/// <see cref="JGraph.Maths.Volumes.StreamGeometry"/>; what is left here is reading the arguments and
/// choosing the object to draw with.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterStreamBuiltins(JgsEnvironment env)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        env.Declare("stream2", JgsValue.Function(new BuiltinFunction("stream2",
            (args, line, col) => Stream("stream2", args, line, col))));
        env.Declare("stream3", JgsValue.Function(new BuiltinFunction("stream3",
            (args, line, col) => Stream("stream3", args, line, col))));

        DefineSilent("streamline", OnNamedAxes((args, line, col) => Streamline(args, line, col)));
        DefineSilent("streamslice", (args, line, col) => StreamSlice(args, line, col));
        DefineSilent("streamribbon", (args, line, col) => StreamRibbon(args, line, col));
        DefineSilent("streamtube", (args, line, col) => StreamTube(args, line, col));
        DefineSilent("coneplot", (args, line, col) => ConePlot(args, line, col));
        DefineSilent("contourslice", (args, line, col) => ContourSlice(args, line, col));
    }

    // --- Tracing ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>stream3(X, Y, Z, U, V, W, sx, sy, sz)</c> and the two-dimensional <c>stream2</c>: the
    /// traced points of every line, as a cell of vertex tables.
    /// </summary>
    private static JgsValue Stream(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (VectorField field, IReadOnlyList<(double X, double Y, double Z)> starts, StreamlineOptions options) =
            ReadStreamRequest(verb, args, line, col);

        var lines = new List<JgsValue>(starts.Count);
        foreach ((double x, double y, double z) in starts)
        {
            IReadOnlyList<(double X, double Y, double Z)> traced =
                StreamlineIntegrator.Trace(field, x, y, z, options);
            lines.Add(VertexList(traced, verb == "stream3"));
        }

        return JgsValue.Cell([.. lines]);
    }

    /// <summary>A traced line as the n-by-2 or n-by-3 table of points MATLAB hands back.</summary>
    private static JgsValue VertexList(
        IReadOnlyList<(double X, double Y, double Z)> points, bool spatial)
    {
        int count = points.Count;
        int columns = spatial ? 3 : 2;
        var flat = new double[count * columns];
        for (int i = 0; i < count; i++)
        {
            flat[i] = points[i].X;
            flat[i + count] = points[i].Y;
            if (spatial)
            {
                flat[i + (2 * count)] = points[i].Z;
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [count, columns]);
    }

    /// <summary>
    /// What every tracing verb is given: the field, where to start, and how far to go. The plane form
    /// is the same reading with the third direction left out.
    /// </summary>
    private static (VectorField Field, List<(double X, double Y, double Z)> Starts, StreamlineOptions Options)
        ReadStreamRequest(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool spatial = !verb.EndsWith('2') && !verb.Equals("streamslice2", StringComparison.Ordinal);
        VectorField field;
        int next;

        if (spatial)
        {
            (field, next) = ReadVectorField(verb, args, line, col);
        }
        else
        {
            // A plane field's grid is two coordinate arrays, not three, so the count is what says
            // whether it is there: stream2 always needs its two starting arrays, which makes six
            // arguments the shortest gridded call and four the shortest ungridded one.
            bool gridded = args.Count >= 6;
            field = ReadFlowField(verb, PlanePrefix(args, gridded), line, col);
            next = gridded ? 4 : 2;
        }

        int coordinates = spatial ? 3 : 2;
        if (args.Count < next + coordinates)
        {
            throw new JgsRuntimeException(line, col, spatial
                ? $"{verb} needs starting points: {verb}(X, Y, Z, U, V, W, sx, sy, sz)."
                : $"{verb} needs starting points: {verb}(X, Y, U, V, sx, sy).");
        }

        double[] sx = ToDoubles(verb, args[next], line, col);
        double[] sy = ToDoubles(verb, args[next + 1], line, col);
        double[] sz = spatial ? ToDoubles(verb, args[next + 2], line, col) : new double[sx.Length];
        next += coordinates;

        if (sx.Length != sy.Length || sx.Length != sz.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the starting points have to be the same length in every direction.");
        }

        var starts = new List<(double, double, double)>(sx.Length);
        for (int i = 0; i < sx.Length; i++)
        {
            starts.Add((sx[i], sy[i], spatial ? sz[i] : field.U.Z[0]));
        }

        StreamlineOptions options = ReadStreamOptions(verb, args, next, line, col);
        return (field, starts, options);
    }

    /// <summary>The <c>[stepsize maxverts]</c> tail every tracing verb takes.</summary>
    private static StreamlineOptions ReadStreamOptions(
        string verb, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        if (at >= args.Count)
        {
            return default;
        }

        double[] given = ToDoubles(verb, args[at], line, col);
        if (given.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the tracing options are [stepsize] or [stepsize maxverts].");
        }

        double step = given[0];
        int budget = given.Length == 2 ? (int)System.Math.Round(given[1]) : 0;
        return new StreamlineOptions(step, budget);
    }

    /// <summary>
    /// The plane form's field arguments, which <see cref="ReadFlowField"/> counts to decide whether a
    /// grid came with them.
    /// </summary>
    private static IReadOnlyList<JgsValue> PlanePrefix(IReadOnlyList<JgsValue> args, bool gridded) =>
        gridded ? [args[0], args[1], args[2], args[3]] : [args[0], args[1]];

    // --- Drawing ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>streamline(vertices)</c> or <c>streamline(X, Y, Z, U, V, W, sx, sy, sz)</c>: the traced
    /// lines, drawn.
    /// </summary>
    private static JgsValue Streamline(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("streamline", args, 1, 11, line, col);
        List<List<(double X, double Y, double Z)>> lines = args[0].Type == JgsType.Cell
            ? ReadTracedLines("streamline", args[0], line, col)
            : TraceFor("streamline", args, line, col);

        return DrawLines(lines);
    }

    /// <summary>The lines a drawing verb was handed or, when it was handed a field, traced itself.</summary>
    private static List<List<(double X, double Y, double Z)>> TraceFor(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (VectorField field, IReadOnlyList<(double X, double Y, double Z)> starts, StreamlineOptions options) =
            ReadStreamRequest(verb, args, line, col);

        var lines = new List<List<(double X, double Y, double Z)>>();
        foreach ((double x, double y, double z) in starts)
        {
            lines.Add([.. StreamlineIntegrator.Trace(field, x, y, z, options)]);
        }

        return lines;
    }

    /// <summary>A cell of vertex tables read back into lines of points.</summary>
    private static List<List<(double X, double Y, double Z)>> ReadTracedLines(
        string verb, JgsValue value, int line, int col)
    {
        var lines = new List<List<(double X, double Y, double Z)>>();
        for (int i = 0; i < value.ArrayLength; i++)
        {
            JgsValue entry = value.ElementAt(i);
            if (entry.Type is not (JgsType.Array or JgsType.Number))
            {
                continue;
            }

            double[,] table = Rectangle($"{verb}: vertices", entry, line, col);
            var points = new List<(double, double, double)>(table.GetLength(0));
            for (int r = 0; r < table.GetLength(0); r++)
            {
                points.Add((
                    table[r, 0],
                    table.GetLength(1) > 1 ? table[r, 1] : 0,
                    table.GetLength(1) > 2 ? table[r, 2] : 0));
            }

            lines.Add(points);
        }

        return lines;
    }

    /// <summary>
    /// The lines drawn, as one line object each — which is what makes <c>set(h, 'Color', 'r')</c> over
    /// the returned handles colour the whole family at once.
    /// </summary>
    private static JgsValue DrawLines(List<List<(double X, double Y, double Z)>> lines)
    {
        var drawn = new List<Line3DPlot>();
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            if (points.Count < 2)
            {
                continue;
            }

            drawn.Add(JG.Plot3(
                [.. points.Select(p => p.X)],
                [.. points.Select(p => p.Y)],
                [.. points.Select(p => p.Z)]));
        }

        return HandlesFor<Line3DPlot>(drawn);
    }

    /// <summary>
    /// <c>streamslice(X, Y, U, V)</c>: streamlines started on a lattice over the field, so a plane can
    /// be looked at without choosing starting points by hand.
    /// </summary>
    private static JgsValue StreamSlice(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("streamslice", args, 2, 8, line, col);

        // streamslice needs no starting points, so four arguments is a grid and a field while two
        // is the field alone.
        bool gridded = args.Count >= 4;
        VectorField field = ReadFlowField("streamslice", PlanePrefix(args, gridded), line, col);
        double density = 1;
        int at = gridded ? 4 : 2;
        if (at < args.Count)
        {
            density = Num("streamslice", args, at, line, col);
            if (!(density > 0))
            {
                throw new JgsRuntimeException(line, col,
                    "streamslice: the density is a number above 0; 1 is the usual spacing.");
            }
        }

        // The starting lattice is what makes this verb different from streamline: MATLAB spaces the
        // seeds by the density and this does the same, at about one seed per few grid cells.
        int alongX = System.Math.Max(2, (int)System.Math.Round(field.U.Columns * density / 4));
        int alongY = System.Math.Max(2, (int)System.Math.Round(field.U.Rows * density / 4));

        var lines = new List<List<(double X, double Y, double Z)>>();
        var options = new StreamlineOptions(0.1, 2000);
        for (int i = 0; i < alongX; i++)
        {
            for (int j = 0; j < alongY; j++)
            {
                double x = Between(field.U.X, i, alongX);
                double y = Between(field.U.Y, j, alongY);
                List<(double X, double Y, double Z)> traced =
                    [.. StreamlineIntegrator.Trace(field, x, y, field.U.Z[0], options)];
                if (traced.Count >= 2)
                {
                    lines.Add(traced);
                }
            }
        }

        return DrawLines(lines);
    }

    private static double Between(double[] positions, int index, int count)
    {
        double low = positions[0];
        double high = positions[^1];
        return low + ((high - low) * (index + 0.5) / count);
    }

    /// <summary>
    /// <c>streamribbon(X, Y, Z, U, V, W, sx, sy, sz)</c>: a band along each streamline, turning the
    /// way the field turns.
    /// </summary>
    private static JgsValue StreamRibbon(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("streamribbon", args, 2, 11, line, col);
        (List<List<(double X, double Y, double Z)>> lines, VectorField field, double width) =
            ReadSweptRequest("streamribbon", args, line, col);

        var drawn = new List<SurfacePlot>();
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            if (points.Count < 2)
            {
                continue;
            }

            (double[,] x, double[,] y, double[,] z) = StreamGeometry.Ribbon(points, field, width);
            drawn.Add(JG.Surf(x, y, z));
        }

        return HandlesFor<SurfacePlot>(drawn);
    }

    /// <summary>
    /// <c>streamtube(X, Y, Z, U, V, W, sx, sy, sz)</c>: a round tube along each streamline, whose
    /// width follows how much the field is spreading there.
    /// </summary>
    private static JgsValue StreamTube(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("streamtube", args, 2, 11, line, col);
        (List<List<(double X, double Y, double Z)>> lines, VectorField field, double scale) =
            ReadSweptRequest("streamtube", args, line, col);

        // The tube's width says how much the field spreads out, which is what divergence measures.
        // It is read at each point and clamped to a band around the base width, because a field can
        // spread arbitrarily fast and a tube that thin or that fat says nothing.
        ScalarField spreading = field.Divergence();
        double baseRadius = scale * StreamlineIntegrator.TypicalCellOf(field) / 2;

        var drawn = new List<SurfacePlot>();
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            if (points.Count < 2)
            {
                continue;
            }

            var radii = new List<double>(points.Count);
            foreach ((double x, double y, double z) in points)
            {
                double spread = spreading.Sample(x, y, z);
                double factor = double.IsFinite(spread)
                    ? System.Math.Clamp(1 + (spread / 4), 0.25, 4)
                    : 1;
                radii.Add(baseRadius * factor);
            }

            (double[,] tx, double[,] ty, double[,] tz) = StreamGeometry.Tube(points, radii, 12);
            drawn.Add(JG.Surf(tx, ty, tz));
        }

        return HandlesFor<SurfacePlot>(drawn);
    }

    /// <summary>
    /// What the ribbon and the tube share: lines to sweep along — traced here or handed in — the
    /// field they were traced from, and how wide to make the shape.
    /// </summary>
    private static (List<List<(double X, double Y, double Z)>> Lines, VectorField Field, double Width)
        ReadSweptRequest(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args[0].Type == JgsType.Cell)
        {
            // streamribbon(vertices, X, Y, Z, U, V, W) — the lines are given, and the field after
            // them is only there to say how the shape should turn or spread.
            List<List<(double X, double Y, double Z)>> given =
                ReadTracedLines(verb, args[0], line, col);
            IReadOnlyList<JgsValue> rest = [.. args.Skip(1)];
            (VectorField field, int next) = ReadVectorField(verb, rest, line, col);
            double width = next < rest.Count ? NumOf($"{verb}: width", rest[next], line, col) : 1;
            return (given, field, width);
        }

        (VectorField traced, IReadOnlyList<(double X, double Y, double Z)> starts, StreamlineOptions options) =
            ReadStreamRequest(verb, args, line, col);

        var lines = new List<List<(double X, double Y, double Z)>>();
        foreach ((double x, double y, double z) in starts)
        {
            lines.Add([.. StreamlineIntegrator.Trace(traced, x, y, z, options)]);
        }

        // Anything after the starting points and the tracing options is the width.
        double scale = 1;
        for (int i = args.Count - 1; i >= 0; i--)
        {
            if (args[i].Type == JgsType.Number)
            {
                scale = args[i].AsNumber;
                break;
            }
        }

        return (lines, traced, scale);
    }

    /// <summary>
    /// <c>coneplot(X, Y, Z, U, V, W, Cx, Cy, Cz)</c>: an arrowhead at each of the given points,
    /// pointing the way the field points there.
    /// </summary>
    private static JgsValue ConePlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("coneplot", args, 4, 12, line, col);
        (VectorField field, int next) = ReadVectorField("coneplot", args, line, col);

        if (args.Count < next + 3)
        {
            throw new JgsRuntimeException(line, col,
                "coneplot needs the places to put its cones: coneplot(X, Y, Z, U, V, W, Cx, Cy, Cz).");
        }

        double[] cx = ToDoubles("coneplot", args[next], line, col);
        double[] cy = ToDoubles("coneplot", args[next + 1], line, col);
        double[] cz = ToDoubles("coneplot", args[next + 2], line, col);
        next += 3;

        if (cx.Length != cy.Length || cx.Length != cz.Length)
        {
            throw new JgsRuntimeException(line, col,
                "coneplot: the places have to be the same length in every direction.");
        }

        double scale = 1;
        bool arrows = false;
        for (int i = next; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String)
            {
                string word = args[i].AsString;
                if (word.Equals("quiver", StringComparison.OrdinalIgnoreCase))
                {
                    arrows = true;
                }
                else if (!word.Equals("nointerp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"coneplot: '{word}' is not a word here; it is 'quiver' or 'nointerp'.");
                }
            }
            else
            {
                scale = Num("coneplot", args, i, line, col);
            }
        }

        double size = scale * StreamlineIntegrator.TypicalCellOf(field);

        // 'quiver' asks for arrows rather than solid cones, which the quiver plot already draws.
        if (arrows)
        {
            var ux = new double[cx.Length];
            var uy = new double[cx.Length];
            var uz = new double[cx.Length];
            for (int i = 0; i < cx.Length; i++)
            {
                (ux[i], uy[i], uz[i]) = field.Sample(cx[i], cy[i], cz[i]);
            }

            return Handle(JG.Quiver3(cx, cy, cz, ux, uy, uz));
        }

        var mesh = new List<IsoMesh>();
        for (int i = 0; i < cx.Length; i++)
        {
            (double u, double v, double w) = field.Sample(cx[i], cy[i], cz[i]);
            if (!double.IsFinite(u) || !double.IsFinite(v) || !double.IsFinite(w))
            {
                continue;
            }

            mesh.Add(StreamGeometry.Cone(
                cx[i], cy[i], cz[i], u, v, w, size, size / 3, 8));
        }

        IsoMesh all = Merged(mesh);
        if (all.Faces.Length == 0)
        {
            throw new JgsRuntimeException(line, col,
                "coneplot: none of the places has a direction — they may all be outside the grid.");
        }

        // Every cone is one patch, so a script gets one handle rather than a cloud of them.
        return Handle(JG.Patch(all.X, all.Y, all.Z, all.Faces));
    }

    /// <summary>Several meshes as one, with the face numbering shifted to match.</summary>
    private static IsoMesh Merged(List<IsoMesh> meshes)
    {
        var x = new List<double>();
        var y = new List<double>();
        var z = new List<double>();
        var faces = new List<int[]>();

        foreach (IsoMesh mesh in meshes)
        {
            int offset = x.Count;
            x.AddRange(mesh.X);
            y.AddRange(mesh.Y);
            z.AddRange(mesh.Z);
            foreach (int[] face in mesh.Faces)
            {
                faces.Add([.. face.Select(v => v + offset)]);
            }
        }

        return new IsoMesh([.. x], [.. y], [.. z], [.. faces]);
    }

    /// <summary>
    /// <c>contourslice(X, Y, Z, V, Sx, Sy, Sz)</c>: contours drawn on planes cut through a field,
    /// each plane's contours placed in space at the plane it came from.
    /// </summary>
    private static JgsValue ContourSlice(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("contourslice", args, 4, 9, line, col);
        (ScalarField field, int next) = ReadScalarField("contourslice", args, line, col);

        if (args.Count < next + 3)
        {
            throw new JgsRuntimeException(line, col,
                "contourslice needs the planes to cut on: contourslice(X, Y, Z, V, Sx, Sy, Sz).");
        }

        double[] atX = PlaneList("contourslice", args[next], line, col);
        double[] atY = PlaneList("contourslice", args[next + 1], line, col);
        double[] atZ = PlaneList("contourslice", args[next + 2], line, col);
        next += 3;

        double[] levels = next < args.Count
            ? LevelsFrom(field, ToDoubles("contourslice", args[next], line, col))
            : EvenLevels(field, 5);

        var drawn = new List<Line3DPlot>();
        foreach (double x in atX)
        {
            AddSliceContours(field, levels, 0, x, drawn);
        }

        foreach (double y in atY)
        {
            AddSliceContours(field, levels, 1, y, drawn);
        }

        foreach (double z in atZ)
        {
            AddSliceContours(field, levels, 2, z, drawn);
        }

        return HandlesFor<Line3DPlot>(drawn);
    }

    /// <summary>
    /// The contours of one plane through a field, drawn as lines in space at the plane they came
    /// from — which is the same trick <c>contour3</c> uses to put a flat drawing in a box.
    /// </summary>
    private static void AddSliceContours(
        ScalarField field, double[] levels, int normal, double at, List<Line3DPlot> into)
    {
        // The plane is sampled on the two directions it spans, at the grid's own spacing.
        (double[] across, double[] down) = normal switch
        {
            0 => (field.Z, field.Y),
            1 => (field.X, field.Z),
            _ => (field.X, field.Y),
        };

        var readings = new double[down.Length, across.Length];
        for (int r = 0; r < down.Length; r++)
        {
            for (int c = 0; c < across.Length; c++)
            {
                (double x, double y, double z) = normal switch
                {
                    0 => (at, down[r], across[c]),
                    1 => (across[c], at, down[r]),
                    _ => (across[c], down[r], at),
                };
                readings[r, c] = field.Sample(x, y, z);
            }
        }

        foreach (double level in levels)
        {
            // A level's contour on one plane may come apart into several pieces, and they are joined
            // by a break rather than drawn separately — one level on one plane is one thing, and a
            // script that colours it should not have to find all of its fragments.
            var px = new List<double>();
            var py = new List<double>();
            var pz = new List<double>();

            foreach (Core.Primitives.Point2D[] path in
                MarchingSquares.Lines(across, down, readings, level))
            {
                if (path.Length < 2)
                {
                    continue;
                }

                if (px.Count > 0)
                {
                    px.Add(double.NaN);
                    py.Add(double.NaN);
                    pz.Add(double.NaN);
                }

                foreach (Core.Primitives.Point2D point in path)
                {
                    (double x, double y, double z) = normal switch
                    {
                        0 => (at, point.Y, point.X),
                        1 => (point.X, at, point.Y),
                        _ => (point.X, point.Y, at),
                    };
                    px.Add(x);
                    py.Add(y);
                    pz.Add(z);
                }
            }

            if (px.Count >= 2)
            {
                into.Add(JG.Plot3([.. px], [.. py], [.. pz]));
            }
        }
    }

    /// <summary>The planes to cut on, which may be none at all in a given direction.</summary>
    private static double[] PlaneList(string verb, JgsValue value, int line, int col) =>
        value.Type == JgsType.Null ? [] : ToDoubles(verb, value, line, col);

    /// <summary>The levels to contour at: a count when one number is given, the levels themselves otherwise.</summary>
    private static double[] LevelsFrom(ScalarField field, double[] given) =>
        given.Length == 1 && given[0] >= 1 && given[0] == System.Math.Floor(given[0])
            ? EvenLevels(field, (int)given[0])
            : given;

    private static double[] EvenLevels(ScalarField field, int count)
    {
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int r = 0; r < field.Rows; r++)
        {
            for (int c = 0; c < field.Columns; c++)
            {
                for (int p = 0; p < field.Pages; p++)
                {
                    double value = field.Values[r, c, p];
                    if (double.IsFinite(value))
                    {
                        low = System.Math.Min(low, value);
                        high = System.Math.Max(high, value);
                    }
                }
            }
        }

        if (!double.IsFinite(low) || high <= low)
        {
            return [];
        }

        var levels = new double[count];
        for (int i = 0; i < count; i++)
        {
            levels[i] = low + ((high - low) * (i + 1) / (count + 1));
        }

        return levels;
    }
}
