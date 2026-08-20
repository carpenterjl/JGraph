using System.Runtime.CompilerServices;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M67 wave B: the handle-graphics objects the model had no answer for — a line a script adds points
/// to as it goes, a shape drawn in the data's own coordinates, the root everything hangs from, and
/// the small verbs that go with them.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterLivingGraphicsBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // `h = animatedline`, `ax = axes` and `get(groot, …)` with no parentheses are the forms every
        // script uses, so the bare name has to make the thing rather than hand back the verb.
        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineBare("animatedline", AnimatedLine);
        DefineSilent("addpoints", AddPoints);
        Define("getpoints", (args, line, col) => GetPoints(args, line, col)[0]);
        DefineSilent("clearpoints", ClearPoints);

        DefineBare("rectangle", Rectangle);
        DefineBare("groot", (args, line, col) =>
        {
            Arity("groot", args, 0, line, col);
            return JgsHandleRegistry.For(JgsGraphicsRoot.Instance);
        });

        DefineSilent("reset", Reset);
        DefineBare("axes", Axes);

        DefineBare("hggroup", (args, line, col) => Group("hggroup", args, line, col));
        DefineBare("hgtransform", (args, line, col) => Group("hgtransform", args, line, col));

        Define("frame2im", Frame2Im);
        Define("im2frame", Im2Frame);
        DefineSilent("waitfor", WaitFor);
    }

    // --- animatedline -------------------------------------------------------------------------------

    /// <summary>
    /// How many points each animated line has been told to keep, or absent for all of them.
    /// <para>
    /// It lives here rather than on the plot object because <c>MaximumNumPoints</c> is the only thing
    /// an animated line has that an ordinary line does not, and a whole model class — with a rendering
    /// case, a serialized form and a format version to bump — to carry one integer would be paying for
    /// the wrong thing. The consequence is recorded: <c>get(h, 'Type')</c> on an animated line answers
    /// <c>'line'</c>, and a figure saved and reloaded keeps the line and forgets the cap.
    /// </para>
    /// </summary>
    private static readonly ConditionalWeakTable<PlotObject, StrongBox<int>> AnimatedCaps = new();

    private static JgsValue AnimatedLine(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            var spec = new OptionSpec(
                "animatedline",
                [],
                [
                    "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName",
                    "MaximumNumPoints",
                ]);
            ParsedArgs parsed = spec.Parse(rest, 3, line, col);

            var seeds = new List<double[]>();
            foreach (JgsValue positional in parsed.Positional)
            {
                seeds.Add(ToDoubles("animatedline", positional, line, col));
            }

            if (seeds.Count == 1)
            {
                throw new JgsRuntimeException(line, col,
                    "animatedline takes no points, or (x, y), or (x, y, z) to start with.");
            }

            int count = seeds.Count == 0 ? 0 : seeds.Min(s => s.Length);
            PlotObject drawn = seeds.Count == 3
                ? JG.Plot3(seeds[0][..count], seeds[1][..count], seeds[2][..count])
                : JG.Plot(
                    seeds.Count == 0 ? [] : seeds[0][..count],
                    seeds.Count == 0 ? [] : seeds[1][..count]);

            // An empty animated line is flat, exactly as MATLAB's is: the dimensionality is fixed when
            // the object is made, so addpoints with a z on a line created without one is refused by
            // name rather than quietly dropping the third coordinate.
            if (parsed.Named("MaximumNumPoints") is { } cap)
            {
                int keep = (int)ScalarOf("animatedline: MaximumNumPoints", cap, line, col);
                if (keep < 1)
                {
                    throw new JgsRuntimeException(line, col,
                        "animatedline: 'MaximumNumPoints' is how many points to keep, so it has to be at least one.");
                }

                AnimatedCaps.AddOrUpdate(drawn, new StrongBox<int>(keep));
                TrimToCap(drawn);
            }

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(drawn);
            foreach (string name in new[]
                     { "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName" })
            {
                if (parsed.Named(name) is { } value)
                {
                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                }
            }

            return JgsHandleRegistry.For(drawn);
        });
    }

    private static JgsValue AddPoints(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 3 or > 4)
        {
            throw new JgsRuntimeException(line, col,
                "addpoints takes the line to add to and then (x, y) or (x, y, z).");
        }

        PlotObject target = AnimatedTarget("addpoints", args[0], line, col);
        double[] x = ToDoubles("addpoints", args[1], line, col);
        double[] y = ToDoubles("addpoints", args[2], line, col);
        double[]? z = args.Count == 4 ? ToDoubles("addpoints", args[3], line, col) : null;
        if (x.Length != y.Length || (z is not null && z.Length != x.Length))
        {
            throw new JgsRuntimeException(line, col,
                "addpoints: the coordinates have to be the same length as each other.");
        }

        switch (target)
        {
            case Line3DPlot spatial when z is not null:
                spatial.SetData(
                    [.. spatial.X, .. x], [.. spatial.Y, .. y], [.. spatial.Z, .. z]);
                break;
            case Line3DPlot:
                throw new JgsRuntimeException(line, col,
                    "addpoints: this line was made with three coordinates, so its points need a z as well.");
            case LinePlot flat when z is null:
                var xs = new List<double>();
                var ys = new List<double>();
                for (int i = 0; i < flat.Data.Count; i++)
                {
                    xs.Add(flat.Data.GetX(i));
                    ys.Add(flat.Data.GetY(i));
                }

                xs.AddRange(x);
                ys.AddRange(y);
                flat.SetData([.. xs], [.. ys]);
                break;
            default:
                throw new JgsRuntimeException(line, col,
                    "addpoints: this line was made flat, so a z has nowhere to go. "
                    + "Use animatedline(x, y, z) to make one that travels through space.");
        }

        TrimToCap(target);
        return JgsValue.Null;
    }

    private static JgsValue[] GetPoints(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("getpoints", args, 1, line, col);
        PlotObject target = AnimatedTarget("getpoints", args[0], line, col);
        if (target is Line3DPlot spatial)
        {
            return
            [
                Numbers([.. spatial.X]), Numbers([.. spatial.Y]), Numbers([.. spatial.Z]),
            ];
        }

        var flat = (LinePlot)target;
        var xs = new double[flat.Data.Count];
        var ys = new double[flat.Data.Count];
        for (int i = 0; i < xs.Length; i++)
        {
            xs[i] = flat.Data.GetX(i);
            ys[i] = flat.Data.GetY(i);
        }

        return [Numbers(xs), Numbers(ys)];
    }

    private static JgsValue ClearPoints(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("clearpoints", args, 1, line, col);
        switch (AnimatedTarget("clearpoints", args[0], line, col))
        {
            case Line3DPlot spatial:
                spatial.SetData([], [], []);
                break;
            case LinePlot flat:
                flat.SetData([], []);
                break;
        }

        return JgsValue.Null;
    }

    /// <summary>The line a point-adding verb was aimed at, refusing anything that is not one.</summary>
    private static PlotObject AnimatedTarget(string verb, JgsValue handle, int line, int col)
    {
        JgsHandleEntry entry = JgsHandleRegistry.Require(handle, line, col);
        return entry.Target is LinePlot or Line3DPlot
            ? (PlotObject)entry.Target
            : throw new JgsRuntimeException(line, col,
                $"{verb} works on a line, and this handle names a {entry.TypeName}.");
    }

    /// <summary>Drops the oldest points of a line that was told how many to keep.</summary>
    private static void TrimToCap(PlotObject target)
    {
        if (!AnimatedCaps.TryGetValue(target, out StrongBox<int>? cap))
        {
            return;
        }

        int keep = cap.Value;
        switch (target)
        {
            case Line3DPlot spatial when spatial.X.Count > keep:
                int from = spatial.X.Count - keep;
                spatial.SetData(
                    [.. spatial.X.Skip(from)], [.. spatial.Y.Skip(from)], [.. spatial.Z.Skip(from)]);
                break;
            case LinePlot flat when flat.Data.Count > keep:
                int start = flat.Data.Count - keep;
                var xs = new double[keep];
                var ys = new double[keep];
                for (int i = 0; i < keep; i++)
                {
                    xs[i] = flat.Data.GetX(start + i);
                    ys[i] = flat.Data.GetY(start + i);
                }

                flat.SetData(xs, ys);
                break;
        }
    }

    // --- rectangle ----------------------------------------------------------------------------------

    /// <summary>How many segments a fully rounded corner is drawn with.</summary>
    private const int CornerSegments = 12;

    /// <summary>
    /// <c>rectangle</c> draws in the data's own coordinates, which is what tells it apart from the
    /// <c>annotation('rectangle', …)</c> this build already had: that one is placed on the figure and
    /// stays put when the axes are zoomed, and this one is part of the picture and moves with it.
    /// <para>
    /// It is a patch rather than an object of its own, because a rectangle with a curvature is a
    /// rounded polygon and a patch is exactly a polygon with a fill and an edge. The curvature is the
    /// fraction of the shorter side each corner is rounded by, as MATLAB defines it, so
    /// <c>[1 1]</c> on a square gives a circle.
    /// </para>
    /// </summary>
    private static JgsValue Rectangle(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            var spec = new OptionSpec(
                "rectangle",
                [],
                ["Position", "Curvature", "FaceColor", "EdgeColor", "LineWidth", "DisplayName"]);
            ParsedArgs parsed = spec.Parse(rest, 0, line, col);

            double[] box = parsed.Named("Position") is { } given
                ? ToDoubles("rectangle: Position", given, line, col)
                : [0, 0, 1, 1];
            if (box.Length != 4)
            {
                throw new JgsRuntimeException(line, col,
                    "rectangle: 'Position' is [x y width height].");
            }

            if (!(box[2] > 0) || !(box[3] > 0))
            {
                throw new JgsRuntimeException(line, col,
                    "rectangle: a rectangle needs a positive width and height.");
            }

            double[] curvature = parsed.Named("Curvature") is { } bend
                ? ToDoubles("rectangle: Curvature", bend, line, col)
                : [0, 0];
            curvature = curvature.Length switch
            {
                // One number curves both directions by the same fraction of the shorter side, which
                // is how MATLAB reads a scalar curvature.
                1 => [curvature[0], curvature[0]],
                2 => curvature,
                _ => throw new JgsRuntimeException(line, col,
                    "rectangle: 'Curvature' is one number, or [horizontal vertical]."),
            };

            (double[] xs, double[] ys) = RectangleOutline(box, curvature, line, col);
            PatchPlot patch = JG.Fill(xs, ys, Colors.Transparent);
            patch.Name = "Rectangle";
            patch.FaceVisible = false;
            patch.EdgeColor = Colors.Black;

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(patch);
            if (parsed.Named("FaceColor") is { } face)
            {
                patch.FaceVisible = true;
                patch.FaceColor = OptionColor(face, line, col, "rectangle");
            }

            foreach (string name in new[] { "EdgeColor", "LineWidth", "DisplayName" })
            {
                if (parsed.Named(name) is { } value)
                {
                    JgsGraphicsProperties.Set(
                        entry, name == "LineWidth" ? "EdgeWidth" : name, value, line, col);
                }
            }

            return JgsHandleRegistry.For(patch);
        });
    }

    /// <summary>The outline of a rectangle whose corners are rounded by <paramref name="curvature"/>.</summary>
    private static (double[] X, double[] Y) RectangleOutline(
        double[] box, double[] curvature, int line, int col)
    {
        (double x, double y, double width, double height) = (box[0], box[1], box[2], box[3]);
        double across = System.Math.Clamp(curvature[0], 0, 1);
        double up = System.Math.Clamp(curvature[1], 0, 1);
        if (curvature[0] < 0 || curvature[1] < 0 || curvature[0] > 1 || curvature[1] > 1)
        {
            throw new JgsRuntimeException(line, col,
                "rectangle: a curvature is a fraction between 0 (square) and 1 (fully rounded).");
        }

        // MATLAB measures both radii against the shorter side, which is what makes [1 1] a circle on
        // a square and an ellipse on anything else rather than a shape with two different roundings.
        double shorter = System.Math.Min(width, height);
        double radiusX = across * shorter / 2;
        double radiusY = up * shorter / 2;
        if (radiusX <= 0 || radiusY <= 0)
        {
            return ([x, x + width, x + width, x], [y, y, y + height, y + height]);
        }

        var xs = new List<double>();
        var ys = new List<double>();

        // The four corners, anticlockwise from the bottom right, each a quarter ellipse about the
        // centre the straight edges leave room for.
        (double CentreX, double CentreY, double From)[] corners =
        [
            (x + width - radiusX, y + radiusY, -System.Math.PI / 2),
            (x + width - radiusX, y + height - radiusY, 0),
            (x + radiusX, y + height - radiusY, System.Math.PI / 2),
            (x + radiusX, y + radiusY, System.Math.PI),
        ];

        foreach ((double centreX, double centreY, double from) in corners)
        {
            for (int i = 0; i <= CornerSegments; i++)
            {
                double angle = from + (System.Math.PI / 2 * i / CornerSegments);
                xs.Add(centreX + (radiusX * System.Math.Cos(angle)));
                ys.Add(centreY + (radiusY * System.Math.Sin(angle)));
            }
        }

        return ([.. xs], [.. ys]);
    }

    // --- the root, reset, and the axes constructor ---------------------------------------------------

    private static JgsValue Reset(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("reset", args, 1, line, col);
        foreach (JgsHandleEntry entry in HandleList("reset", args[0], line, col))
        {
            switch (entry.Target)
            {
                case FigureModel figure:
                    // A reset figure keeps its number and loses everything drawn on it, which is what
                    // clf already means — so reset says it by reusing it rather than by repeating it.
                    figure.Axes.Clear();
                    figure.Annotations.Clear();
                    break;
                case AxesModel axes:
                    ClearAxes(axes, reset: true);
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"reset works on a figure or an axes, and this handle names a {entry.TypeName}.");
            }
        }

        return JgsValue.Null;
    }

    private static JgsValue Axes(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // axes(h) with a handle selects an existing axes; anything else makes one.
        if (args.Count == 1 && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? existing)
            && existing.Target is AxesModel chosen)
        {
            JG.MakeCurrent(chosen);
            return JgsHandleRegistry.For(chosen);
        }

        AxesModel axes = JG.CurrentFigure.AddAxes();
        JG.MakeCurrent(axes);

        var spec = new OptionSpec(
            "axes", [], ["Position", "XLim", "YLim", "ZLim", "Color", "Box", "Tag", "Title"]);
        ParsedArgs parsed = spec.Parse(args, 0, line, col);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(axes);
        foreach (string name in new[]
                 { "Position", "XLim", "YLim", "ZLim", "Color", "Box", "Tag", "Title" })
        {
            if (parsed.Named(name) is { } value)
            {
                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }
        }

        return JgsHandleRegistry.For(axes);
    }

    // --- groups and transforms ----------------------------------------------------------------------

    private static JgsValue Group(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        var group = new JgsGraphicsGroup(transforms: verb == "hgtransform");
        JgsGraphicsProperties.Remember(group);
        var spec = new OptionSpec(verb, [], ["Matrix", "Tag", "Visible", "Parent"]);
        ParsedArgs parsed = spec.Parse(args, 0, line, col);

        // 'Parent' on a group names the axes it belongs to, which this build has no use for — a group
        // is beside the render tree, so it has no place in an axes to be put. Reading it and doing
        // nothing is the honest answer; refusing would fail a script that is doing nothing wrong.
        if (parsed.Named("Tag") is { } tag)
        {
            group.Tag = StrOf(verb, tag, line, col);
        }

        if (parsed.Named("Matrix") is { } matrix)
        {
            RequireTransform(verb, group, line, col);
            group.SetMatrix(TransformMatrix(verb, matrix, line, col));
        }

        return JgsHandleRegistry.For(group);
    }

    /// <summary>Refuses a matrix on a plain group, which has nothing to do with one.</summary>
    private static void RequireTransform(string verb, JgsGraphicsGroup group, int line, int col)
    {
        if (!group.Transforms)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: a plain group has no matrix; use hgtransform for one that moves its members.");
        }
    }

    /// <summary>A value read as the 4-by-4 a transform is given.</summary>
    internal static double[,] TransformMatrix(string verb, JgsValue value, int line, int col)
    {
        double[,] rows = Rectangle($"{verb}: Matrix", value, line, col);
        return rows.GetLength(0) == 4 && rows.GetLength(1) == 4
            ? rows
            : throw new JgsRuntimeException(line, col,
                $"{verb}: a transform is a 4-by-4 matrix; makehgtform builds one.");
    }

    // --- frames both ways ---------------------------------------------------------------------------

    private static JgsValue Frame2Im(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("frame2im", args, 1, line, col);
        if (args[0].Type != JgsType.Struct || !HasField(args[0], "cdata"))
        {
            throw new JgsRuntimeException(line, col,
                "frame2im: a frame is what getframe answers with — a struct holding cdata.");
        }

        // A true-colour frame's picture is its cdata and nothing else, so this is a field read with a
        // name. It exists because a script that says frame2im means "give me the picture", and having
        // to know which field holds it is exactly what the verb is for.
        return args[0].AsStruct["cdata"];
    }

    private static JgsValue Im2Frame(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                "im2frame takes a picture, and a colour table for an indexed one.");
        }

        JgsValue picture = args[0];
        if (args.Count == 2)
        {
            // An indexed picture and its colour table become the true-colour picture they describe,
            // because that is what a frame holds and there is nowhere in one to keep the table.
            double[,] map = ColormapRows("im2frame", args, 1, line, col);
            picture = IndexedToTrueColour("im2frame", picture, map, line, col);
        }

        IReadOnlyList<int> dims = JgsMatrix.DimsOf(picture);
        if (dims.Count != 3 || dims[2] != 3)
        {
            throw new JgsRuntimeException(line, col,
                "im2frame: a frame's picture is a height-by-width-by-3 array of colours.");
        }

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["cdata"] = picture,
            ["colormap"] = JgsValue.Array([]),
        });
    }

    /// <summary>An indexed picture and its colour table as one height-by-width-by-3 array of bytes.</summary>
    private static JgsValue IndexedToTrueColour(
        string verb, JgsValue indices, double[,] map, int line, int col)
    {
        IReadOnlyList<int> dims = JgsMatrix.DimsOf(indices);
        if (dims.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: an indexed picture is a height-by-width array of colour numbers.");
        }

        double[] flat = ToDoubles(verb, indices, line, col);
        int rows = map.GetLength(0);
        var levels = new double[flat.Length * 3];
        for (int channel = 0; channel < 3; channel++)
        {
            for (int i = 0; i < flat.Length; i++)
            {
                // A colour number is 1-based in MATLAB whichever dialect the script is written in,
                // because it indexes the table the script itself handed over.
                int at = System.Math.Clamp((int)System.Math.Round(flat[i]) - 1, 0, rows - 1);
                levels[(channel * flat.Length) + i] =
                    System.Math.Round(System.Math.Clamp(map[at, channel], 0, 1) * 255);
            }
        }

        JgsValue picture = JgsMatrix.FromColumnMajorDims(levels, [dims[0], dims[1], 3]);
        picture.SetNumericClass(JgsNumericClass.UInt8);
        return picture;
    }

    // --- waitfor ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>waitfor</c> stops until something a person does changes the thing it was given: until the
    /// object is deleted, until a named property changes, or until it takes a given value. While it
    /// waits it delivers queued callbacks — a wait with no way to answer the click that would end it
    /// would never end — and it wakes at once for Stop.
    /// <para>
    /// All of that presumes somewhere a person's events can come from. Where no event pump is
    /// installed — a headless <c>-batch</c>, a one-shot run — nothing between here and the end of
    /// the run can change a property, so the wait is already over the moment it starts, and this
    /// returns. Returning rather than refusing is the point: a script that opens a figure and waits
    /// for it is asking to stay alive until the window closes, and under <c>-batch</c> the answer to
    /// that is that the run ends. Refusing would fail a script that is doing nothing wrong, and
    /// hanging would fail the run itself.
    /// </para>
    /// </summary>
    private static JgsValue WaitFor(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 1 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                "waitfor takes the object to wait on, and optionally a property name and the value to wait for.");
        }

        // The handle is still checked, so waitfor on something that is not an object says so.
        JgsHandleEntry entry = JgsHandleRegistry.Require(args[0], line, col);
        string? property = null;
        if (args.Count > 1)
        {
            property = StrOf("waitfor", args[1], line, col);
            if (!JgsGraphicsProperties.TryFind(entry.Target, property, out _))
            {
                throw new JgsRuntimeException(line, col,
                    $"waitfor: a {entry.TypeName} has no '{property}' to wait for.");
            }
        }

        if (!ScriptEventQueue.PumpInstalled || JgsCallbackDispatcher.Current is not { } dispatcher)
        {
            return JgsValue.Null;
        }

        // waitfor(h, prop) returns on any change from the value it saw at entry; the three-argument
        // form returns on equality with the value asked for — at once if it already holds.
        JgsValue? watched = property is null ? null : JgsGraphicsProperties.Get(entry, property, line, col);
        while (true)
        {
            if (!JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? alive)
                || !ReferenceEquals(alive.Target, entry.Target))
            {
                return JgsValue.Null;
            }

            if (property is not null)
            {
                JgsValue current = JgsGraphicsProperties.Get(entry, property, line, col);
                bool satisfied = args.Count == 3
                    ? JgsStdlib.DeepEquals(current, args[2], nanEqual: true)
                    : !JgsStdlib.DeepEquals(current, watched!, nanEqual: true);
                if (satisfied)
                {
                    return JgsValue.Null;
                }
            }

            CancellationToken token = dispatcher.StatementToken;
            token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
            token.ThrowIfCancellationRequested();
            dispatcher.Drain();
        }
    }
}

/// <summary>
/// The object every figure hangs from — MATLAB's <c>groot</c>. It is a real object rather than a
/// number so that <c>get</c>, <c>set</c> and <c>findobj</c> reach it through the one property table
/// everything else goes through; there is exactly one of it, for the same reason there is one screen.
/// </summary>
internal sealed class JgsGraphicsRoot : GraphObject
{
    private JgsGraphicsRoot() => Name = "Root";

    public static JgsGraphicsRoot Instance { get; } = new();

    /// <summary>
    /// The screen, in pixels, as <c>[left bottom width height]</c>. The script layer cannot see a
    /// display — the window that could is in the host — so this answers the size the figure model
    /// itself defaults to rather than guessing at hardware.
    /// </summary>
    public double[] ScreenSize { get; } = [1, 1, 1280, 800];

    /// <summary>The units the screen size is given in; pixels, and not settable.</summary>
    public string Units => "pixels";
}
