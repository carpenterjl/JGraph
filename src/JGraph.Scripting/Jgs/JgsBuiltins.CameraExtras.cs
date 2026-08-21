using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    /// <summary>
    /// M54 wave F: the camera verbs M45 left out. Two of them — <c>viewmtx</c> and
    /// <c>makehgtform</c> — are pure matrix arithmetic that answers the same numbers whatever this
    /// build draws, and live in <see cref="CameraMatrices"/>. The other five move the view, and each
    /// one lands on the only camera state an orthographic auto-fitting projection has: the two view
    /// angles, the roll, and the limits that decide what is inside the box.
    /// </summary>
    private static void RegisterCameraExtraBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        Define("viewmtx", (args, line, col) => ViewMatrixOf(args, line, col));
        Define("makehgtform", (args, line, col) => HgTransform(args, line, col));

        Define("camroll", (args, line, col) =>
        {
            (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
            Arity("camroll", rest, 1, line, col);
            AxesModel axes = named ?? JG.Gca();
            axes.Roll += Num("camroll", rest, 0, line, col);
            return JgsValue.Null;
        });

        Define("camdolly", (args, line, col) => CamDolly(args, line, col));
        Define("campan", (args, line, col) => CamPan(args, line, col));
        Define("camlookat", (args, line, col) => CamLookAt(args, line, col));
        Define("camproj", (args, line, col) => CamProjection(args, line, col));
    }

    /// <summary>
    /// <c>viewmtx(az, el)</c>, with the optional perspective angle and target the documentation
    /// gives it. The answer is a matrix, not a change to the figure — nothing here touches the axes.
    /// </summary>
    private static JgsValue ViewMatrixOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("viewmtx", args, 2, 4, line, col);
        double az = Num("viewmtx", args, 0, line, col);
        double el = Num("viewmtx", args, 1, line, col);

        if (args.Count == 2)
        {
            return Matrix4(CameraMatrices.ViewMatrix(az, el));
        }

        double phi = Num("viewmtx", args, 2, line, col);
        var target = new Vector3D(0, 0, 0);
        if (args.Count == 4)
        {
            double[] xc = ToDoubles("viewmtx: the target", args[3], line, col);
            if (xc.Length != 3)
            {
                throw new JgsRuntimeException(line, col, "viewmtx: the target is a three-element point.");
            }

            target = new Vector3D(xc[0], xc[1], xc[2]);
        }

        return Matrix4(CameraMatrices.ViewMatrix(az, el, phi, target));
    }

    /// <summary>
    /// <c>makehgtform</c>: the product of the transforms named, in the order named. Angles are in
    /// radians, as MATLAB's are, and the empty call is the identity.
    /// </summary>
    private static JgsValue HgTransform(IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[,] result = CameraMatrices.Identity();
        int i = 0;
        while (i < args.Count)
        {
            string word = StrOf("makehgtform", args[i], line, col).ToLowerInvariant();
            i++;

            double[,] step;
            switch (word)
            {
                case "translate":
                {
                    Vector3D t = Triple("makehgtform: translate", args, ref i, line, col, fill: 0);
                    step = CameraMatrices.Translate(t.X, t.Y, t.Z);
                    break;
                }

                case "scale":
                    // A lone scale factor is uniform, which is the one place a single number does not
                    // mean "x only" — MATLAB's own reading, and the one every scale call relies on.
                    Vector3D s = Triple("makehgtform: scale", args, ref i, line, col, fill: null);
                    step = CameraMatrices.Scale(s.X, s.Y, s.Z);
                    break;

                case "xrotate":
                    step = CameraMatrices.RotateAbout(new Vector3D(1, 0, 0), Angle(word, args, ref i, line, col));
                    break;

                case "yrotate":
                    step = CameraMatrices.RotateAbout(new Vector3D(0, 1, 0), Angle(word, args, ref i, line, col));
                    break;

                case "zrotate":
                    step = CameraMatrices.RotateAbout(new Vector3D(0, 0, 1), Angle(word, args, ref i, line, col));
                    break;

                case "axisrotate":
                {
                    if (i + 1 >= args.Count)
                    {
                        throw new JgsRuntimeException(line, col,
                            "makehgtform: axisrotate takes an [x y z] axis and an angle in radians.");
                    }

                    double[] axis = ToDoubles("makehgtform: the rotation axis", args[i], line, col);
                    if (axis.Length != 3)
                    {
                        throw new JgsRuntimeException(line, col, "makehgtform: the rotation axis has three components.");
                    }

                    i++;
                    step = CameraMatrices.RotateAbout(
                        new Vector3D(axis[0], axis[1], axis[2]), NumOf("makehgtform", args[i++], line, col));
                    break;
                }

                default:
                    throw new JgsRuntimeException(line, col,
                        $"makehgtform: '{word}' is not one of translate, scale, xrotate, yrotate, zrotate, axisrotate.");
            }

            result = CameraMatrices.Multiply(result, step);
        }

        return Matrix4(result);
    }

    /// <summary>
    /// The three components a <c>translate</c> or <c>scale</c> step is given, whether as one vector,
    /// three numbers, or — when <paramref name="fill"/> is null — a single number meaning all three.
    /// </summary>
    private static Vector3D Triple(
        string what, IReadOnlyList<JgsValue> args, ref int i, int line, int col, double? fill)
    {
        if (i >= args.Count)
        {
            throw new JgsRuntimeException(line, col, $"{what} needs its values.");
        }

        if (args[i].Type is not (JgsType.Number or JgsType.Bool))
        {
            double[] vector = ToDoubles(what, args[i], line, col);
            i++;
            if (vector.Length != 3)
            {
                throw new JgsRuntimeException(line, col, $"{what}: expected three components.");
            }

            return new Vector3D(vector[0], vector[1], vector[2]);
        }

        double first = NumOf(what, args[i++], line, col);
        bool three = i + 1 < args.Count
            && args[i].Type is JgsType.Number or JgsType.Bool
            && args[i + 1].Type is JgsType.Number or JgsType.Bool;
        if (!three)
        {
            return fill is { } same ? new Vector3D(first, same, same) : new Vector3D(first, first, first);
        }

        return new Vector3D(first, NumOf(what, args[i++], line, col), NumOf(what, args[i++], line, col));
    }

    /// <summary>The one angle a rotate step takes, in radians.</summary>
    private static double Angle(string word, IReadOnlyList<JgsValue> args, ref int i, int line, int col)
    {
        if (i >= args.Count)
        {
            throw new JgsRuntimeException(line, col, $"makehgtform: {word} takes an angle in radians.");
        }

        return NumOf($"makehgtform: {word}", args[i++], line, col);
    }

    /// <summary>A 4-by-4 matrix as the value a script indexes with.</summary>
    private static JgsValue Matrix4(double[,] m)
    {
        var rows = new double[4][];
        for (int r = 0; r < 4; r++)
        {
            rows[r] = [m[r, 0], m[r, 1], m[r, 2], m[r, 3]];
        }

        return MatrixFromRows(rows);
    }

    /// <summary>
    /// <c>camdolly</c>: slide the view sideways. MATLAB moves the camera and its target together;
    /// here the camera is fixed to the box, so the same motion is a shift of all three limits — which
    /// is the same picture, and the reason <c>'fixtarget'</c> has nothing to mean.
    /// </summary>
    private static JgsValue CamDolly(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("camdolly", rest, 3, 5, line, col);
        AxesModel axes = named ?? JG.Gca();

        double dx = Num("camdolly", rest, 0, line, col);
        double dy = Num("camdolly", rest, 1, line, col);
        double dz = Num("camdolly", rest, 2, line, col);

        if (rest.Count >= 4)
        {
            string mode = Str("camdolly", rest, 3, line, col).ToLowerInvariant();
            if (mode == "fixtarget")
            {
                throw new JgsRuntimeException(line, col,
                    "camdolly: 'fixtarget' moves the camera away from its target, and this camera always "
                    + "looks at the centre of the data box. Use 'movetarget'.");
            }

            if (mode != "movetarget")
            {
                throw new JgsRuntimeException(line, col,
                    $"camdolly: the target mode is 'movetarget' or 'fixtarget', but got '{mode}'.");
            }
        }

        string system = rest.Count >= 5 ? Str("camdolly", rest, 4, line, col).ToLowerInvariant() : "camera";
        (double sx, double sy, double sz) = Spans(axes);

        (double ox, double oy, double oz) = system switch
        {
            // Camera units are fractions of the scene, resolved through the screen basis so that a
            // move "right" is right on screen whichever way the box is turned.
            "camera" => ScreenOffset(axes, dx * sx, dy * sy, dz * sz),
            "data" => (dx, dy, dz),
            "pixels" => throw new JgsRuntimeException(line, col,
                "camdolly: 'pixels' needs the plot rectangle, which is only decided when the figure is "
                + "laid out. Use 'camera' (fractions of the scene) or 'data'."),
            _ => throw new JgsRuntimeException(line, col,
                $"camdolly: the coordinate system is 'camera', 'data' or 'pixels', but got '{system}'."),
        };

        Shift(axes, ox, oy, oz);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>campan</c>: swing the camera so it looks somewhere else. The target is what moves, by the
    /// tangent of the angle at the camera's distance, which here is a shift of the limits.
    /// </summary>
    private static JgsValue CamPan(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("campan", rest, 2, 4, line, col);
        AxesModel axes = named ?? JG.Gca();

        double dtheta = Num("campan", rest, 0, line, col);
        double dphi = Num("campan", rest, 1, line, col);

        string system = rest.Count >= 3 ? Str("campan", rest, 2, line, col).ToLowerInvariant() : "camera";
        if (system is not ("camera" or "data"))
        {
            throw new JgsRuntimeException(line, col,
                $"campan: the coordinate system is 'camera' or 'data', but got '{system}'.");
        }

        if (rest.Count >= 4)
        {
            // MATLAB's fourth argument names the axis to swing about in the data system; with a
            // camera pinned to the box there is nothing left for it to choose.
            throw new JgsRuntimeException(line, col,
                "campan: the direction argument names an axis to swing about, which this camera does not "
                + "have — it always looks at the centre of the data box.");
        }

        (double sx, double sy, double sz) = Spans(axes);
        double distance = CameraDistance;
        double right = distance * System.Math.Tan(dtheta * System.Math.PI / 180.0);
        double up = distance * System.Math.Tan(dphi * System.Math.PI / 180.0);

        (double ox, double oy, double oz) = system == "camera"
            ? ScreenOffset(axes, right * sx, up * sy, 0)
            : (right * sx, up * sy, 0);

        Shift(axes, ox, oy, oz);
        return JgsValue.Null;
    }

    /// <summary>
    /// How far the camera stands off the box, in box widths. The same two diagonals <c>campos</c>
    /// reports, so a pan and a camera position agree about where the camera is.
    /// </summary>
    private const double CameraDistance = 2.0;

    /// <summary>
    /// <c>camlookat</c>: frame the objects named, or everything in the axes. The limits become the
    /// objects' own extents with MATLAB's small margin around them, which is the whole of what
    /// "look at" can mean to a camera that always looks at the centre.
    /// </summary>
    private static JgsValue CamLookAt(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("camlookat", args, 0, 1, line, col);

        AxesModel axes;
        IReadOnlyList<PlotObject> targets;
        if (args.Count == 0)
        {
            axes = JG.Gca();
            targets = [.. axes.Plots];
        }
        else if (JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? single) && single.Target is AxesModel named)
        {
            axes = named;
            targets = [.. named.Plots];
        }
        else
        {
            var plots = new List<PlotObject>();
            AxesModel? owner = null;
            foreach (double handle in ToDoubles("camlookat", args[0], line, col))
            {
                JgsHandleEntry entry = JgsHandleRegistry.Require(JgsValue.Number(handle), line, col);
                if (entry.Target is not PlotObject plot)
                {
                    throw new JgsRuntimeException(line, col, "camlookat wants plot objects or one axes.");
                }

                plots.Add(plot);
                owner ??= plot.Parent as AxesModel;
            }

            axes = owner ?? JG.Gca();
            targets = plots;
        }

        if (targets.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "camlookat: there is nothing in the axes to look at.");
        }

        DataRange x = DataRange.Empty, y = DataRange.Empty, z = DataRange.Empty;
        foreach (PlotObject plot in targets)
        {
            x = x.Union(plot.GetXDataBounds());
            y = y.Union(plot.GetYDataBounds());
            if (plot is IHasZData depth)
            {
                z = z.Union(depth.GetZDataBounds());
            }
        }

        Frame(axes.PrimaryXAxis, x);
        Frame(axes.ActiveYAxis, y);
        Frame(axes.ZAxis, z);
        return JgsValue.Null;
    }

    /// <summary>Pins one ruler around an extent, with MATLAB's margin and a fallback for a flat one.</summary>
    private static void Frame(AxisModel ruler, DataRange bounds)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        // A tenth of the extent on each side, and a unit either way when the extent is a point —
        // otherwise a single sample would be framed by a range of zero width.
        double span = bounds.Max - bounds.Min;
        double margin = span > 0 ? span * 0.1 : 1;
        ruler.AutoScale = false;
        ruler.Range = new DataRange(bounds.Min - margin, bounds.Max + margin);
    }

    /// <summary>
    /// <c>camproj</c>: whether the camera draws with parallel rays or through a viewpoint. Since M74
    /// the word is real — perspective divides by the distance from the camera, so near faces grow and
    /// the box's parallel edges converge.
    /// </summary>
    private static JgsValue CamProjection(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("camproj", rest, 0, 1, line, col);
        AxesModel axes = named ?? JG.Gca();
        if (rest.Count == 0)
        {
            return JgsValue.Str(axes.Projection == ProjectionType.Perspective ? "perspective" : "orthographic");
        }

        string word = Str("camproj", rest, 0, line, col).ToLowerInvariant();
        axes.Projection = word switch
        {
            "orthographic" => ProjectionType.Orthographic,
            "perspective" => ProjectionType.Perspective,
            _ => throw new JgsRuntimeException(line, col,
                $"camproj: the projection is 'orthographic' or 'perspective', but got '{word}'."),
        };

        return JgsValue.Null;
    }

    /// <summary>The three visible spans, fitted first so an auto-scaling ruler is not still a placeholder.</summary>
    private static (double X, double Y, double Z) Spans(AxesModel axes)
    {
        axes.RecomputeDataBounds();
        return (
            axes.PrimaryXAxis.Range.Max - axes.PrimaryXAxis.Range.Min,
            axes.ActiveYAxis.Range.Max - axes.ActiveYAxis.Range.Min,
            axes.ZAxis.Range.Max - axes.ZAxis.Range.Min);
    }

    /// <summary>
    /// A move given as right/up/toward-the-viewer, resolved into data axes through the camera's own
    /// screen basis. On a 2-D axes the basis is the identity, so right is +x and up is +y.
    /// </summary>
    private static (double X, double Y, double Z) ScreenOffset(AxesModel axes, double right, double up, double towards)
    {
        if (!axes.Is3D)
        {
            return (right, up, towards);
        }

        var projection = new Projection3D(
            axes.PrimaryXAxis.Range, axes.ActiveYAxis.Range, axes.ZAxis.Range,
            axes.Azimuth, axes.Elevation, new Rect2D(0, 0, 1, 1), axes.PlotBoxAspect, axes.Roll);

        Vector3D r = projection.ScreenRight, u = projection.ScreenUp, d = projection.ViewDirection;
        return (
            (right * r.X) + (up * u.X) + (towards * d.X),
            (right * r.Y) + (up * u.Y) + (towards * d.Y),
            (right * r.Z) + (up * u.Z) + (towards * d.Z));
    }

    /// <summary>Slides all three rulers, freezing them: a panned view is one the data no longer chooses.</summary>
    private static void Shift(AxesModel axes, double dx, double dy, double dz)
    {
        Slide(axes.PrimaryXAxis, dx);
        Slide(axes.ActiveYAxis, dy);
        if (axes.Is3D)
        {
            Slide(axes.ZAxis, dz);
        }

        static void Slide(AxisModel ruler, double delta)
        {
            if (delta == 0)
            {
                return;
            }

            ruler.AutoScale = false;
            ruler.Range = new DataRange(ruler.Range.Min + delta, ruler.Range.Max + delta);
        }
    }
}
