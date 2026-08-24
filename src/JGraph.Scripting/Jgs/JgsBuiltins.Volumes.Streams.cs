using JGraph.Api;
using JGraph.Core.Model;
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

        // Wrapped the way streamline beside it is: streamslice(ax, ...) is a documented form, and
        // without the peel the axes handle was read as the first component of the field. Both doors
        // are wrapped, because M85 gave the verb a second one.
        env.Declare("streamslice", JgsValue.Function(new BuiltinFunction("streamslice",
            OnNamedAxes((args, line, col) => StreamSliceOutputs(args, 1, line, col)[0]))
        {
            BindsAnsAsStatement = false,
            MultiOutput = OnNamedAxesOutputs(StreamSliceOutputs),
        }));
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
        ReadStreamRequest(
            string verb, IReadOnlyList<JgsValue> args, int line, int col, bool? plane = null)
    {
        // Most verbs in this family say which world they are in by their own name -- stream2 against
        // stream3. streamline is the one that does not: MATLAB spells both its plane form and its
        // space form with the same word and lets the argument list decide, so the caller works that
        // out and says so here.
        bool spatial = plane is { } flat
            ? !flat
            : !verb.EndsWith('2') && !verb.Equals("streamslice2", StringComparison.Ordinal);
        VectorField field;
        int next;

        if (spatial)
        {
            // A caller that knew which form it had also knows whether a grid came with it: nine
            // arguments is grid-and-field, six is field alone. Left to the general test both look
            // alike, and the six-argument one was read as a grid it does not have.
            bool? hasGrid = plane is null ? null : args.Count >= 9;
            (field, next) = ReadVectorField(verb, args, line, col, hasGrid);
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
            : TraceFor("streamline", args, line, col, PlaneForm(args));

        return DrawLines(lines);
    }

    /// <summary>
    /// Whether a <c>streamline</c> call is the plane form. MATLAB gives both forms the same name and
    /// separates them by counting: four or six arguments is a plane, nine is space. Six is the one
    /// place both readings fit -- <c>streamline(X, Y, U, V, sx, sy)</c> against
    /// <c>streamline(U, V, W, sx, sy, sz)</c> -- and there the field settles it, because a volume has
    /// pages and a plane does not.
    /// </summary>
    /// <remarks>
    /// Every one of these forms errored before M72: the reading was fixed at three components, so a
    /// plane call handed its two arrays and its two starting arrays to a volume reader, which
    /// measured them against each other and refused.
    /// </remarks>
    private static bool? PlaneForm(IReadOnlyList<JgsValue> args)
    {
        // A trailing options vector is not part of the count.
        int count = args.Count;
        if (count is 5 or 7 or 10)
        {
            count--;
        }

        return count switch
        {
            4 => true,
            6 => !LooksVolumetric(args[0]),
            9 => false,
            _ => null,
        };
    }

    /// <summary>Whether a field argument has pages, which is what makes it a volume rather than a plane.</summary>
    private static bool LooksVolumetric(JgsValue value) =>
        value.Type == JgsType.Array && value.Dims is { Length: >= 3 } dims && dims[2] > 1;

    /// <summary>The lines a drawing verb was handed or, when it was handed a field, traced itself.</summary>
    private static List<List<(double X, double Y, double Z)>> TraceFor(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, bool? plane = null)
    {
        (VectorField field, IReadOnlyList<(double X, double Y, double Z)> starts, StreamlineOptions options) =
            ReadStreamRequest(verb, args, line, col, plane);

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
    private static JgsValue DrawLines(
        List<List<(double X, double Y, double Z)>> lines, bool uniform = false)
    {
        var drawn = new List<Line3DPlot>();
        AxesModel? into = null;
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            if (points.Count < 2)
            {
                continue;
            }

            drawn.Add(AddLineOf(
                ref into,
                [.. points.Select(p => p.X)],
                [.. points.Select(p => p.Y)],
                [.. points.Select(p => p.Z)]));
        }

        // A slice's streamlines are one drawing rather than dozens of series, and MATLAB colours them
        // all alike. Left to the palette each takes the next colour in the order, which turned a
        // twenty-line slice into a plaid of twenty hues and used up the axes' colour order besides.
        // The colour taken is the one the first line would have had, so a slice still sits in the
        // sequence of whatever was plotted before it.
        if (uniform && drawn.Count > 0)
        {
            JGraph.Core.Drawing.Color shared = PaletteColorFor(drawn[0]);
            foreach (Line3DPlot streamline in drawn)
            {
                streamline.Color = shared;
            }
        }

        return HandlesFor<Line3DPlot>(drawn);
    }

    /// <summary>
    /// One piece of a drawing that is made of several — one streamline of a slice, one contour of a
    /// plane, one ribbon of a bundle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first piece goes through the facade, which honours <c>hold</c> and the figure's
    /// <c>NextPlot</c> exactly once; every piece after it joins the axes that one landed in. Sending
    /// all of them through the facade is what these verbs used to do, and it does not work: a verb
    /// that draws with <c>hold</c> off clears the axes first, so a twenty-line slice cleared itself
    /// nineteen times and left one line behind. The handles all came back, and every one of them was
    /// live, which is why nothing noticed — <c>numel(h)</c> was right and the picture was not.
    /// </para>
    /// <para>
    /// This is the same arrangement the composite charts use, where one verb draws bars and a curve
    /// into one axes: ask the facade once, then add.
    /// </para>
    /// </remarks>
    private static Line3DPlot AddLineOf(
        ref AxesModel? into, double[] x, double[] y, double[] z)
    {
        if (into is not null)
        {
            return into.AddLine3D(x, y, z);
        }

        Line3DPlot first = JG.Plot3(x, y, z);
        into = JG.Gca();
        return first;
    }

    /// <summary><see cref="AddLineOf"/> for the verbs whose pieces are surfaces.</summary>
    private static SurfacePlot AddSurfaceOf(
        ref AxesModel? into, double[,] x, double[,] y, double[,] z)
    {
        if (into is not null)
        {
            return into.AddSurface(x, y, z);
        }

        SurfacePlot first = JG.Surf(x, y, z);
        into = JG.Gca();
        return first;
    }

    /// <summary>
    /// <c>streamslice(X, Y, U, V)</c> over a plane and
    /// <c>streamslice(X, Y, Z, U, V, W, sx, sy, sz)</c> through a volume: streamlines started on a
    /// lattice over the field, so a field can be looked at without choosing starting points by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The volume form's trailing triple names *planes*, not starting points — the same three lists
    /// <c>slice</c> takes, any of which may be <c>[]</c> for none. That is what makes this verb a
    /// slicer rather than another spelling of <c>streamline</c>, and it is why the seeding below
    /// happens inside a plane rather than in space: a streamline traced through a volume would leave
    /// the plane it was drawn on immediately, and the picture would stop being a slice.
    /// </para>
    /// <para>
    /// The form is settled by counting, because every argument here can be a matrix and none of them
    /// can be told apart by looking: two or four arguments is a plane, six or nine is a volume, and
    /// the odd counts between are those same forms with a density after them.
    /// </para>
    /// </remarks>
    private static JgsValue[] StreamSliceOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("streamslice", args, 2, 12, line, col);

        (IReadOnlyList<JgsValue> given, bool arrows) = ReadSliceWords(args, line, col);
        (bool spatial, bool gridded) = given.Count switch
        {
            2 or 3 => (false, false),
            4 or 5 => (false, true),
            6 or 7 => (true, false),
            9 or 10 => (true, true),
            _ => throw new JgsRuntimeException(line, col,
                "streamslice takes U, V — or X, Y, U, V — over a plane, and U, V, W with three plane "
                + "lists — or X, Y, Z before them — through a volume, each with an optional density "
                + "after it."),
        };

        int at = spatial ? (gridded ? 9 : 6) : (gridded ? 4 : 2);
        double density = 1;
        if (at < given.Count)
        {
            density = NumOf("streamslice: density", given[at], line, col);
            if (!(density > 0))
            {
                throw new JgsRuntimeException(line, col,
                    "streamslice: the density is a number above 0; 1 is the usual spacing.");
            }
        }

        var lines = new List<List<(double X, double Y, double Z)>>();
        var arrowheads = new List<List<(double X, double Y, double Z)>>();
        double size;

        if (spatial)
        {
            (VectorField volume, int next) = ReadVectorField("streamslice", given, line, col, gridded);
            size = StreamlineIntegrator.TypicalCellOf(volume) * 0.6;
            double[] sx = PlaneList("streamslice", given, next, line, col);
            double[] sy = PlaneList("streamslice", given, next + 1, line, col);
            double[] sz = PlaneList("streamslice", given, next + 2, line, col);
            if (sx.Length + sy.Length + sz.Length == 0)
            {
                throw new JgsRuntimeException(line, col,
                    "streamslice: every plane list is empty, so there is nothing to slice.");
            }

            foreach ((Axis normal, double[] positions) in
                new[] { (Axis.X, sx), (Axis.Y, sy), (Axis.Z, sz) })
            {
                foreach (double where in positions)
                {
                    AddPlaneSlice(volume, normal, where, density, size, arrows, lines, arrowheads);
                }
            }
        }
        else
        {
            VectorField field = ReadFlowField("streamslice", PlanePrefix(given, gridded), line, col);
            size = StreamlineIntegrator.TypicalCellOf(field) * 0.6;
            foreach (List<(double X, double Y, double Z)> traced in SeedOverField(field, density))
            {
                lines.Add(traced);
                AddArrow(traced, Axis.Z, size, arrows, arrowheads);
            }
        }

        // Asked for the vertices, MATLAB hands them over and draws nothing — which is the same
        // arrangement stream2 and stream3 have beside streamline, one verb further along.
        if (wanted >= 2)
        {
            return
            [
                JgsValue.Cell([.. lines.Select(points => VertexList(points, spatial))]),
                JgsValue.Cell([.. arrowheads.Select(points => VertexList(points, spatial))]),
            ];
        }

        var drawn = new List<List<(double X, double Y, double Z)>>(lines);
        drawn.AddRange(arrowheads);
        return [DrawLines(drawn, uniform: true)];
    }

    /// <summary>
    /// The trailing words a slice may carry — the arrow mode and the interpolation method — and the
    /// arguments left once they are taken off. Only trailing strings are read this way, because
    /// nothing else in this verb's argument list is ever a string, and a word in the middle is a
    /// mistake worth reporting rather than a setting worth honouring.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Given, bool Arrows) ReadSliceWords(
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool arrows = true;
        int end = args.Count;
        while (end > 0 && args[end - 1].Type == JgsType.String)
        {
            string word = args[end - 1].AsString;
            if (word.Equals("arrows", StringComparison.OrdinalIgnoreCase))
            {
                arrows = true;
            }
            else if (word.Equals("noarrows", StringComparison.OrdinalIgnoreCase))
            {
                arrows = false;
            }
            else if (!word.Equals("linear", StringComparison.OrdinalIgnoreCase)
                && !word.Equals("nearest", StringComparison.OrdinalIgnoreCase)
                && !word.Equals("cubic", StringComparison.OrdinalIgnoreCase))
            {
                // Every reading here is straight-line, so the method word is checked and then does
                // nothing rather than being ignored silently — the stance slice beside it takes.
                throw new JgsRuntimeException(line, col,
                    $"streamslice: '{word}' is not a word here; it is 'arrows', 'noarrows', or an "
                    + "interpolation method — linear, nearest, or cubic.");
            }

            end--;
        }

        return (end == args.Count ? args : [.. args.Take(end)], arrows);
    }

    /// <summary>
    /// One axis-aligned plane's worth of streamlines, traced *in* the plane and then placed at it.
    /// </summary>
    /// <remarks>
    /// The plane is sampled on the volume's own grid in the two directions it spans, and the two
    /// components of the field that lie in it become a flat field of their own — at which point the
    /// plane form's seeding does the rest. Discarding the third component is the whole point: it is
    /// the part that would take a line off the plane.
    /// </remarks>
    private static void AddPlaneSlice(
        VectorField volume,
        Axis normal,
        double at,
        double density,
        double size,
        bool arrows,
        List<List<(double X, double Y, double Z)>> lines,
        List<List<(double X, double Y, double Z)>> arrowheads)
    {
        (double[] across, double[] down) = normal switch
        {
            Axis.X => (volume.U.Z, volume.U.Y),
            Axis.Y => (volume.U.X, volume.U.Z),
            _ => (volume.U.X, volume.U.Y),
        };

        int wide = across.Length, tall = down.Length;
        var inPlaneAcross = new double[tall, wide, 1];
        var inPlaneDown = new double[tall, wide, 1];
        var unused = new double[tall, wide, 1];

        for (int r = 0; r < tall; r++)
        {
            for (int c = 0; c < wide; c++)
            {
                (double x, double y, double z) = PointOnPlane(normal, at, across[c], down[r]);
                (double u, double v, double w) = volume.Sample(x, y, z);
                (inPlaneAcross[r, c, 0], inPlaneDown[r, c, 0]) = normal switch
                {
                    Axis.X => (w, v),
                    Axis.Y => (u, w),
                    _ => (u, v),
                };
            }
        }

        double[] page = [1];
        var flat = new VectorField(
            new ScalarField(across, down, page, inPlaneAcross),
            new ScalarField(across, down, page, inPlaneDown),
            new ScalarField(across, down, page, unused));

        foreach (List<(double X, double Y, double Z)> traced in SeedOverField(flat, density))
        {
            List<(double X, double Y, double Z)> placed =
                [.. traced.Select(p => PointOnPlane(normal, at, p.X, p.Y))];
            lines.Add(placed);
            AddArrow(placed, normal, size, arrows, arrowheads);
        }
    }

    /// <summary>
    /// Where a point of an axis-aligned plane sits in space. The two in-plane directions are named
    /// the way <see cref="AddSliceContours"/> names them, so a slice of streamlines and a slice of
    /// contours put their pictures in the same place.
    /// </summary>
    private static (double X, double Y, double Z) PointOnPlane(
        Axis normal, double at, double across, double down) => normal switch
        {
            Axis.X => (at, down, across),
            Axis.Y => (across, at, down),
            _ => (across, down, at),
        };

    /// <summary>
    /// The starting lattice, which is what makes this verb different from <c>streamline</c>: MATLAB
    /// spaces the seeds by the density and this does the same, at about one seed per few grid cells.
    /// </summary>
    private static List<List<(double X, double Y, double Z)>> SeedOverField(
        VectorField field, double density)
    {
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

        return lines;
    }

    /// <summary>
    /// One arrowhead halfway along a streamline, saying which way it runs — which is what a slice is
    /// usually asked for and what a bare tangle of lines cannot say.
    /// </summary>
    /// <remarks>
    /// The head is a three-point V lying in the slice's own plane: the barbs are found by turning the
    /// line's direction a quarter turn about the plane's normal, so the head is flat against the
    /// slice however the slice is oriented, and a head on a line that goes nowhere is not drawn at
    /// all rather than drawn as a spike in an arbitrary direction.
    /// </remarks>
    private static void AddArrow(
        List<(double X, double Y, double Z)> points,
        Axis normal,
        double size,
        bool wanted,
        List<List<(double X, double Y, double Z)>> into)
    {
        if (!wanted || points.Count < 2)
        {
            return;
        }

        int tip = points.Count / 2;
        (double X, double Y, double Z) head = points[tip];
        (double X, double Y, double Z) behind = points[tip - 1];

        double dx = head.X - behind.X, dy = head.Y - behind.Y, dz = head.Z - behind.Z;
        double along = System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (!(along > 0))
        {
            return;
        }

        dx /= along;
        dy /= along;
        dz /= along;

        (double nx, double ny, double nz) = normal switch
        {
            Axis.X => (1.0, 0.0, 0.0),
            Axis.Y => (0.0, 1.0, 0.0),
            _ => (0.0, 0.0, 1.0),
        };

        double px = (ny * dz) - (nz * dy);
        double py = (nz * dx) - (nx * dz);
        double pz = (nx * dy) - (ny * dx);
        double sideways = System.Math.Sqrt((px * px) + (py * py) + (pz * pz));
        if (!(sideways > 0))
        {
            return;
        }

        px = px / sideways * size * 0.4;
        py = py / sideways * size * 0.4;
        pz = pz / sideways * size * 0.4;

        (double X, double Y, double Z) root =
            (head.X - (dx * size), head.Y - (dy * size), head.Z - (dz * size));

        into.Add(
        [
            (root.X + px, root.Y + py, root.Z + pz),
            head,
            (root.X - px, root.Y - py, root.Z - pz),
        ]);
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
        AxesModel? into = null;
        foreach (List<(double X, double Y, double Z)> points in lines)
        {
            if (points.Count < 2)
            {
                continue;
            }

            (double[,] x, double[,] y, double[,] z) = StreamGeometry.Ribbon(points, field, width);
            drawn.Add(AddSurfaceOf(ref into, x, y, z));
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
        AxesModel? into = null;
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
            drawn.Add(AddSurfaceOf(ref into, tx, ty, tz));
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
        AxesModel? into = null;
        foreach (double x in atX)
        {
            AddSliceContours(field, levels, 0, x, drawn, ref into);
        }

        foreach (double y in atY)
        {
            AddSliceContours(field, levels, 1, y, drawn, ref into);
        }

        foreach (double z in atZ)
        {
            AddSliceContours(field, levels, 2, z, drawn, ref into);
        }

        return HandlesFor<Line3DPlot>(drawn);
    }

    /// <summary>
    /// The contours of one plane through a field, drawn as lines in space at the plane they came
    /// from — which is the same trick <c>contour3</c> uses to put a flat drawing in a box.
    /// </summary>
    private static void AddSliceContours(
        ScalarField field,
        double[] levels,
        int normal,
        double at,
        List<Line3DPlot> drawn,
        ref AxesModel? into)
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
                drawn.Add(AddLineOf(ref into, [.. px], [.. py], [.. pz]));
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
