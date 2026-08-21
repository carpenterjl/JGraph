using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    /// <summary>
    /// MATLAB's colormap generators, in the order the documentation lists them. Each returns its
    /// table as an N-by-3 matrix of components in [0, 1]; each is also the name <c>colormap</c>
    /// accepts as a string, so the two forms cannot drift apart.
    /// </summary>
    private static readonly (string Name, Func<Colormap> Map)[] ColormapGenerators =
    [
        ("parula", () => Colormap.Parula),
        ("turbo", () => Colormap.Turbo),
        ("hsv", () => Colormap.Hsv),
        ("hot", () => Colormap.Hot),
        ("cool", () => Colormap.Cool),
        ("spring", () => Colormap.Spring),
        ("summer", () => Colormap.Summer),
        ("autumn", () => Colormap.Autumn),
        ("winter", () => Colormap.Winter),
        ("gray", () => Colormap.Grayscale),
        ("bone", () => Colormap.Bone),
        ("copper", () => Colormap.Copper),
        ("pink", () => Colormap.Pink),
        ("jet", () => Colormap.Jet),
        ("lines", () => Colormap.Lines),
        ("viridis", () => Colormap.Viridis),
        ("flag", () => Colormap.Flag),
        ("prism", () => Colormap.Prism),
    ];

    /// <summary>
    /// How many rows a generator returns when called with no size — MATLAB's default colormap
    /// length, which is what <c>c = parula</c> hands back.
    /// </summary>
    private const int DefaultColormapRows = 256;

    /// <summary>The generator names, for the catalog — which has to list exactly what is registered.</summary>
    internal static IReadOnlyList<string> ColormapGeneratorNames { get; } =
        ColormapGenerators.Select(static generator => generator.Name).ToArray();

    /// <summary>Registers the M45.B color and lighting control verbs.</summary>
    private static void RegisterColorControlBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        // A bare `parula` is the table itself, exactly as `x = eps` is a number (M37's AutoCallsBare).
        void DefineGenerator(string name, Func<Colormap> map) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(
                name,
                (args, line, col) =>
                {
                    ArityRange(name, args, 0, 1, line, col);
                    int rows = args.Count == 1 ? Count(name, args, 0, line, col) : DefaultColormapRows;
                    if (rows < 1)
                    {
                        throw new JgsRuntimeException(line, col, $"{name}: the row count must be at least 1.");
                    }

                    return ColormapTable(map().Resample(rows));
                })
            { AutoCallsBare = true }));

        foreach ((string name, Func<Colormap> map) in ColormapGenerators)
        {
            DefineGenerator(name, map);
        }

        env.Declare("caxis", JgsValue.Function(new BuiltinFunction(
            "caxis", OnNamedAxes((args, line, col) => ColorLimits("caxis", args, line, col)))
        { AutoCallsBare = true }));
        env.Declare("clim", JgsValue.Function(new BuiltinFunction(
            "clim", (args, line, col) => ColorLimits("clim", args, line, col))
        { AutoCallsBare = true }));

        Define("brighten", (args, line, col) =>
        {
            Arity("brighten", args, 1, line, col);
            JG.Brighten(Num("brighten", args, 0, line, col));
            return JgsValue.Null;
        });

        env.Declare("colororder", JgsValue.Function(new BuiltinFunction(
            "colororder", OnNamedAxes((args, line, col) => ColorOrder(args, line, col)))
        { AutoCallsBare = true }));

        // surfl draws, so its handle is kept only when a script asks for it — the DefineSilent rule the
        // other drawing verbs have. Registering it with Define would echo `ans = 1000000.5` at every
        // unsuppressed call, which is the mistake M69 caught in quiver before it shipped.
        env.Declare("surfl", JgsValue.Function(new BuiltinFunction(
            "surfl", OnNamedAxes((args, line, col) => Surfl(args, line, col)))
        { BindsAnsAsStatement = false }));

        // The two dialects read one output differently, the way meshgrid already does: MATLAB's
        // `nx = surfnorm(...)` is the first component, while JGS hands back all three so that
        // `let [nx, ny, nz] = surfnorm(...)` can destructure them.
        Define(
            "surfnorm",
            dialect.IsMatlab
                ? OnNamedAxes((args, line, col) => SurfaceNormals("surfnorm", args, line, col)[0])
                : OnNamedAxes((args, line, col) => JgsValue.Array(SurfaceNormals("surfnorm", args, line, col))),
            (args, _, line, col) => SurfaceNormals("surfnorm", PeelAxes(args).Remaining, line, col));
    }

    /// <summary>
    /// Registers the M45.C camera and aspect verbs. The projection is orthographic with an automatic
    /// fit, so the camera's whole state is an azimuth, an elevation, and how much of the data the axis
    /// limits admit — which is what everything here maps onto.
    /// </summary>
    private static void RegisterCameraBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        Define("campos", (args, line, col) => CameraPosition(args, line, col));
        Define("camtarget", (args, line, col) => FixedCameraVector(
            "camtarget", args, line, col, AxesCenter(), "the camera always looks at the centre of the data box"));
        Define("camup", (args, line, col) => FixedCameraVector(
            "camup", args, line, col, [0, 0, 1], "screen up is always the +Z axis"));

        Define("camorbit", (args, line, col) =>
        {
            ArityRange("camorbit", args, 2, 2, line, col);
            AxesModel axes = JG.Gca();
            axes.Azimuth += Num("camorbit", args, 0, line, col);
            axes.Elevation += Num("camorbit", args, 1, line, col);
            return JgsValue.Null;
        });

        Define("camzoom", (args, line, col) =>
        {
            Arity("camzoom", args, 1, line, col);
            Zoom("camzoom", Num("camzoom", args, 0, line, col), line, col);
            return JgsValue.Null;
        });

        Define("camva", (args, line, col) =>
        {
            ArityRange("camva", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                return JgsValue.Number(DefaultViewAngle);
            }

            // An orthographic camera has no view angle, so this is read as a zoom against MATLAB's
            // default: halving the angle doubles the size of what fills the box.
            double angle = Num("camva", args, 0, line, col);
            if (!(angle > 0 && angle < 180))
            {
                throw new JgsRuntimeException(line, col, "camva: the view angle must be between 0 and 180 degrees.");
            }

            double half = System.Math.PI / 360;
            Zoom("camva", System.Math.Tan(DefaultViewAngle * half) / System.Math.Tan(angle * half), line, col);
            return JgsValue.Null;
        });

        Define("pbaspect", OnNamedAxes((args, line, col) => BoxAspect(args, line, col)));
        Define("daspect", OnNamedAxes((args, line, col) => DataAspect(args, line, col)));
    }

    /// <summary>MATLAB's default camera view angle in degrees, which is the framing an axes starts with.</summary>
    private const double DefaultViewAngle = 6.6086;

    /// <summary>MATLAB's default 3D camera, which is also what a fresh axes carries.</summary>
    private const double DefaultAzimuth = -37.5;
    private const double DefaultElevation = 30;

    /// <summary>
    /// <c>view</c>: read the camera angles, set them from a pair or a two-element vector, or take one
    /// of MATLAB's two shorthands — <c>view(2)</c> for straight down and <c>view(3)</c> for the
    /// default 3D camera.
    /// </summary>
    private static JgsValue View(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("view", args, 0, 2, line, col);
        AxesModel axes = JG.Gca();

        if (args.Count == 0)
        {
            return Numbers([axes.Azimuth, axes.Elevation]);
        }

        if (args.Count == 2)
        {
            JG.View(Num("view", args, 0, line, col), Num("view", args, 1, line, col));
            axes.Is3D = true;
            return JgsValue.Null;
        }

        // view(2) and view(3) arrive as bare scalars, which ToDoubles only accepts as arrays.
        double[] values = args[0].Type is JgsType.Number or JgsType.Bool
            ? [args[0].AsNumber]
            : ToDoubles("view", args[0], line, col);
        switch (values.Length)
        {
            // Both shorthands name which of the two an axes is, not merely where the camera stands:
            // that is the whole content of view(2) and view(3), and setting angles without setting
            // the mode is what made view(3) a silent no-op on an axes that had never been told it
            // was three-dimensional.
            case 1 when values[0] == 2:
                JG.View(0, 90);
                axes.Is3D = false;
                return JgsValue.Null;
            case 1 when values[0] == 3:
                JG.View(DefaultAzimuth, DefaultElevation);
                axes.Is3D = true;
                return JgsValue.Null;
            case 1:
                throw new JgsRuntimeException(line, col, "view: the only shorthands are view(2) and view(3).");
            case 2:
                JG.View(values[0], values[1]);
                axes.Is3D = true;
                return JgsValue.Null;
            default:
                throw new JgsRuntimeException(line, col, $"view: expected [az el], got {values.Length} values.");
        }
    }

    /// <summary>
    /// <c>campos</c>: where the camera sits, in data coordinates. Reading builds the position from the
    /// azimuth and elevation at a distance of two box diagonals; writing reads the direction back off
    /// the vector from the box centre.
    /// </summary>
    /// <remarks>
    /// Divergence: the distance is ignored on write, because an orthographic camera's framing comes
    /// from the axis limits rather than from how far away it is. Use <c>camzoom</c> or the limits.
    /// </remarks>
    private static JgsValue CameraPosition(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("campos", args, 0, 1, line, col);
        AxesModel axes = JG.Gca();
        double[] center = AxesCenter();
        (double xSpan, double ySpan, double zSpan) = AxesSpans();

        if (args.Count == 0)
        {
            double az = axes.Azimuth * System.Math.PI / 180;
            double el = axes.Elevation * System.Math.PI / 180;
            const double Distance = 2;
            return Numbers([
                center[0] + (Distance * xSpan * System.Math.Sin(az) * System.Math.Cos(el)),
                center[1] - (Distance * ySpan * System.Math.Cos(az) * System.Math.Cos(el)),
                center[2] + (Distance * zSpan * System.Math.Sin(el)),
            ]);
        }

        double[] position = ToDoubles("campos", args[0], line, col);
        if (position.Length != 3)
        {
            throw new JgsRuntimeException(line, col, "campos: expected a three-element position.");
        }

        // Undo the per-axis scaling before reading the angles, so a tall Z axis does not tip the
        // camera on its own -- this is the same normalized box the projection works in.
        double dx = (position[0] - center[0]) / xSpan;
        double dy = (position[1] - center[1]) / ySpan;
        double dz = (position[2] - center[2]) / zSpan;
        double horizontal = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (horizontal < 1e-12 && System.Math.Abs(dz) < 1e-12)
        {
            throw new JgsRuntimeException(line, col, "campos: the camera cannot sit at the centre of the data box.");
        }

        axes.Azimuth = System.Math.Atan2(dx, -dy) * 180 / System.Math.PI;
        axes.Elevation = System.Math.Atan2(dz, horizontal) * 180 / System.Math.PI;
        return JgsValue.Null;
    }

    /// <summary>
    /// A camera vector this projection fixes: readable, and settable only to the value it already has.
    /// Accepting and ignoring anything else would be the quiet kind of wrong.
    /// </summary>
    private static JgsValue FixedCameraVector(
        string name, IReadOnlyList<JgsValue> args, int line, int col, double[] value, string why)
    {
        ArityRange(name, args, 0, 1, line, col);
        if (args.Count == 0)
        {
            return Numbers(value);
        }

        double[] requested = ToDoubles(name, args[0], line, col);
        if (requested.Length != 3)
        {
            throw new JgsRuntimeException(line, col, $"{name}: expected a three-element vector.");
        }

        for (int i = 0; i < 3; i++)
        {
            if (System.Math.Abs(requested[i] - value[i]) > 1e-9 * System.Math.Max(1, System.Math.Abs(value[i])))
            {
                throw new JgsRuntimeException(line, col, $"{name} cannot be changed: {why}.");
            }
        }

        return JgsValue.Null;
    }

    /// <summary><c>pbaspect</c>: the relative side lengths of the 3D plot box.</summary>
    private static JgsValue BoxAspect(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("pbaspect", args, 0, 1, line, col);
        AxesModel axes = JG.Gca();
        if (args.Count == 0)
        {
            Vector3D current = axes.PlotBoxAspect;
            return Numbers([current.X, current.Y, current.Z]);
        }

        if (args[0].Type == JgsType.String)
        {
            string word = Str("pbaspect", args, 0, line, col).Trim().ToLowerInvariant();
            if (word is not ("auto" or "manual"))
            {
                throw new JgsRuntimeException(line, col, $"pbaspect: expected 'auto' or 'manual', got '{word}'.");
            }

            if (word == "auto")
            {
                axes.PlotBoxAspect = new Vector3D(1, 1, 1);
            }

            return JgsValue.Null;
        }

        double[] aspect = AspectTriplet("pbaspect", args[0], line, col);
        axes.PlotBoxAspect = new Vector3D(aspect[0], aspect[1], aspect[2]);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>daspect</c>: how many data units one box unit is worth on each axis. It is the plot box
    /// aspect divided through by the data spans, which is why <c>daspect([1 1 1])</c> gives a box
    /// whose sides are proportional to the ranges — the 3D reading of "axis equal".
    /// </summary>
    private static JgsValue DataAspect(IReadOnlyList<JgsValue> args, int line, int col)
    {
        AxesModel axes = JG.Gca();
        (double xSpan, double ySpan, double zSpan) = AxesSpans();

        ArityRange("daspect", args, 0, 1, line, col);
        if (args.Count == 0)
        {
            Vector3D box = axes.PlotBoxAspect;
            double[] ratio = [xSpan / box.X, ySpan / box.Y, zSpan / box.Z];
            double smallest = System.Math.Min(ratio[0], System.Math.Min(ratio[1], ratio[2]));
            return Numbers([ratio[0] / smallest, ratio[1] / smallest, ratio[2] / smallest]);
        }

        if (args[0].Type == JgsType.String)
        {
            string word = Str("daspect", args, 0, line, col).Trim().ToLowerInvariant();
            if (word is not ("auto" or "manual"))
            {
                throw new JgsRuntimeException(line, col, $"daspect: expected 'auto' or 'manual', got '{word}'.");
            }

            if (word == "auto")
            {
                axes.PlotBoxAspect = new Vector3D(1, 1, 1);
            }

            return JgsValue.Null;
        }

        double[] aspect = AspectTriplet("daspect", args[0], line, col);
        axes.PlotBoxAspect = new Vector3D(xSpan / aspect[0], ySpan / aspect[1], zSpan / aspect[2]);
        return JgsValue.Null;
    }

    private static double[] AspectTriplet(string name, JgsValue value, int line, int col)
    {
        double[] aspect = ToDoubles(name, value, line, col);
        if (aspect.Length != 3)
        {
            throw new JgsRuntimeException(line, col, $"{name}: expected three ratios.");
        }

        foreach (double v in aspect)
        {
            if (!double.IsFinite(v) || v <= 0)
            {
                throw new JgsRuntimeException(line, col, $"{name}: every ratio must be finite and positive.");
            }
        }

        return aspect;
    }

    /// <summary>Scales the three axis limits about their centres, which is the only zoom an orthographic fit has.</summary>
    private static void Zoom(string name, double factor, int line, int col)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the zoom factor must be finite and positive.");
        }

        AxesModel axes = JG.Gca();
        ScaleRange(axes.XAxes[0], VisibleX(axes), factor);
        ScaleRange(axes.YAxes[0], VisibleY(axes), factor);
        ScaleRange(axes.ZAxis, VisibleZ(axes), factor);
    }

    private static void ScaleRange(AxisModel axis, DataRange range, double factor)
    {
        double center = (range.Min + range.Max) / 2;
        double half = (range.Max - range.Min) / (2 * factor);
        axis.AutoScale = false;
        axis.Range = new DataRange(center - half, center + half);
    }

    /// <summary>
    /// What an axis is currently showing. A script asks about the camera the moment after it plots,
    /// which is before any render — so an auto-scaled axis still has its empty default range, and
    /// even its data bounds are unfilled, since the layout pass is what computes them. Falling back
    /// to the plots' own extents is what makes <c>daspect</c> and <c>campos</c> mean anything before
    /// the first frame.
    /// </summary>
    private static DataRange VisibleRange(AxesModel axes, AxisModel axis, Func<PlotObject, DataRange> extent)
    {
        if (!axis.AutoScale && axis.Range.IsValid)
        {
            return axis.Range;
        }

        if (axis.DataBounds.IsValid)
        {
            return axis.DataBounds;
        }

        DataRange bounds = DataRange.Empty;
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot.Visible)
            {
                bounds = bounds.Union(extent(plot));
            }
        }

        return bounds.IsValid ? bounds : (axis.Range.IsValid ? axis.Range : DataRange.Unit);
    }

    private static DataRange VisibleX(AxesModel axes) =>
        VisibleRange(axes, axes.XAxes[0], static plot => plot.GetXDataBounds());

    private static DataRange VisibleY(AxesModel axes) =>
        VisibleRange(axes, axes.YAxes[0], static plot => plot.GetYDataBounds());

    private static DataRange VisibleZ(AxesModel axes) =>
        VisibleRange(axes, axes.ZAxis, static plot =>
            plot is IHasZData zData ? zData.GetZDataBounds() : DataRange.Empty);

    private static double[] AxesCenter()
    {
        AxesModel axes = JG.Gca();
        return [Center(VisibleX(axes)), Center(VisibleY(axes)), Center(VisibleZ(axes))];

        static double Center(DataRange range) => (range.Min + range.Max) / 2;
    }

    private static (double X, double Y, double Z) AxesSpans()
    {
        AxesModel axes = JG.Gca();
        return (Span(VisibleX(axes)), Span(VisibleY(axes)), Span(VisibleZ(axes)));

        static double Span(DataRange range)
        {
            double span = range.Max - range.Min;
            return span > 0 ? span : 1;
        }
    }

    /// <summary>
    /// A light direction in camera axes — right, up, and toward the viewer — from an azimuth and an
    /// elevation in degrees. Swinging out from the view axis is what makes a headlight <c>(0, 0)</c>
    /// exactly the camera direction.
    /// </summary>
    private static Vector3D CameraLightPosition(double azimuthDegrees, double elevationDegrees)
    {
        double az = azimuthDegrees * System.Math.PI / 180;
        double el = elevationDegrees * System.Math.PI / 180;
        return new Vector3D(
            System.Math.Sin(az) * System.Math.Cos(el),
            System.Math.Sin(el),
            System.Math.Cos(az) * System.Math.Cos(el));
    }

    /// <summary>A colormap table as the N-by-3 matrix of components in [0, 1] MATLAB returns.</summary>
    private static JgsValue ColormapTable(IReadOnlyList<Color> colors)
    {
        var rows = new double[colors.Count][];
        for (int i = 0; i < colors.Count; i++)
        {
            rows[i] = [colors[i].R / 255.0, colors[i].G / 255.0, colors[i].B / 255.0];
        }

        return MatrixFromRows(rows);
    }

    /// <summary>
    /// <c>caxis</c> / <c>clim</c>: read the limits with no arguments, pin them with a pair or a
    /// two-element vector, or hand them back to the data with the word <c>auto</c>.
    /// </summary>
    private static JgsValue ColorLimits(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 0, 2, line, col);
        if (args.Count == 0)
        {
            (double low, double high) = JG.GetCLim();
            return Numbers([low, high]);
        }

        if (args.Count == 1 && args[0].Type == JgsType.String)
        {
            string word = Str(name, args, 0, line, col).Trim().ToLowerInvariant();
            switch (word)
            {
                case "auto":
                    JG.CLimAuto();
                    return JgsValue.Null;

                // MATLAB's 'manual' freezes the limits where they currently are, which is exactly
                // pinning them to what the data happens to say right now.
                case "manual":
                    (double low, double high) = JG.GetCLim();
                    JG.CLim(low, high);
                    return JgsValue.Null;

                default:
                    throw new JgsRuntimeException(line, col, $"{name}: expected 'auto' or 'manual', got '{word}'.");
            }
        }

        double min, max;
        if (args.Count == 2)
        {
            min = Num(name, args, 0, line, col);
            max = Num(name, args, 1, line, col);
        }
        else
        {
            double[] pair = ToDoubles(name, args[0], line, col);
            if (pair.Length != 2)
            {
                throw new JgsRuntimeException(line, col, $"{name}: expected two limits, got {pair.Length}.");
            }

            (min, max) = (pair[0], pair[1]);
        }

        try
        {
            JG.CLim(min, max);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>colororder</c>: the colors plots cycle through in the current axes. With no arguments it
    /// reports the order in force, which on an untouched axes is the theme's.
    /// </summary>
    private static JgsValue ColorOrder(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("colororder", args, 0, 1, line, col);
        if (args.Count == 0)
        {
            // With no override the theme decides, and the theme lives above this layer — so what is
            // reported is the palette a themeless render would use, which is that default.
            return ColormapTable(JG.GetColorOrder() ?? Colors.DefaultSeriesOrder);
        }

        var colors = new List<Color>();
        if (args[0].Type == JgsType.String)
        {
            colors.Add(OptionColor(args[0], line, col, "colororder"));
        }
        else if (args[0].Type == JgsType.Cell)
        {
            foreach (JgsValue spec in args[0].AsCell)
            {
                colors.Add(OptionColor(spec, line, col, "colororder"));
            }
        }
        else
        {
            double[,] rgb = Matrix("colororder", args, 0, line, col);
            if (rgb.GetLength(1) != 3)
            {
                throw new JgsRuntimeException(
                    line, col, $"colororder: a color matrix needs three columns, got {rgb.GetLength(1)}.");
            }

            for (int r = 0; r < rgb.GetLength(0); r++)
            {
                colors.Add(Color.FromScRgb(
                    System.Math.Clamp(rgb[r, 0], 0, 1),
                    System.Math.Clamp(rgb[r, 1], 0, 1),
                    System.Math.Clamp(rgb[r, 2], 0, 1)));
            }
        }

        if (colors.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "colororder: needs at least one color.");
        }

        JG.ColorOrder(colors);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>surfl</c>: a surface lit from beside the camera. MATLAB's default source sits 45 degrees
    /// round from the view direction at the viewer's own elevation, and the light travels with the
    /// camera here for the reason <c>camlight</c> does — a figure you rotate should keep its
    /// highlight rather than leave it behind.
    /// </summary>
    /// <remarks>
    /// Divergence: MATLAB's <c>surfl</c> colors the surface by <em>reflectance</em> rather than by
    /// height, so its default gray colormap reads as a shaded relief. Here the colormap still follows
    /// height and the lighting is applied on top, which is what every other lit surface does.
    /// </remarks>
    private static JgsValue Surfl(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsValue drawn = Surface3D("surfl", args, line, col,
            (x, y, z) => JG.Surf(x, y, z), z => JG.Surf(z), (x, y, z) => JG.Surf(x, y, z),
            takesColorData: false);

        AxesModel axes = JG.Gca();
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot is SurfacePlot surface)
            {
                surface.FaceLighting = SurfaceLighting.Gouraud;
            }
        }

        axes.Lights.Add(new LightModel
        {
            Name = "Surfl light",
            FollowsCamera = true,
            Position = CameraLightPosition(-45, 0),
        });

        // The surface Surface3D drew, rather than nothing. Until M70 this answered null, so
        // `h = surfl(Z); h.FaceAlpha = 0.5` — the documented spelling — had no object to write to.
        return drawn;
    }

    /// <summary>
    /// <c>surfnorm</c>: the unit surface normal at every grid vertex, in data units. Tangents come
    /// from central differences along the rows and columns, degrading to one-sided at the border,
    /// and their cross product is the normal — the same construction the renderer lights a
    /// parametric surface with, but here left in the caller's own coordinates.
    /// </summary>
    /// <remarks>
    /// Divergence: MATLAB's no-output form draws the surface with its normals as whiskers. That needs
    /// a 3-D line primitive; the three-output form is the one scripts compute with.
    /// </remarks>
    private static JgsValue[] SurfaceNormals(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[,] x, y, z;
        if (args.Count == 1)
        {
            z = Matrix(name, args, 0, line, col);
            (x, y) = IndexGrids(z.GetLength(0), z.GetLength(1));
        }
        else
        {
            Arity(name, args, 3, line, col);
            z = Matrix(name, args, 2, line, col);
            if (IsFullGrid(args[0]) && IsFullGrid(args[1]))
            {
                x = Matrix(name, args, 0, line, col);
                y = Matrix(name, args, 1, line, col);
            }
            else
            {
                double[] xv = GridVector(name, args, 0, firstRow: true, line, col);
                double[] yv = GridVector(name, args, 1, firstRow: false, line, col);
                (x, y) = ExpandGrids(xv, yv);
            }
        }

        int rows = z.GetLength(0);
        int cols = z.GetLength(1);
        if (x.GetLength(0) != rows || x.GetLength(1) != cols || y.GetLength(0) != rows || y.GetLength(1) != cols)
        {
            throw new JgsRuntimeException(line, col, $"{name}: x, y and z must be the same size.");
        }

        var nx = new double[rows][];
        var ny = new double[rows][];
        var nz = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            nx[r] = new double[cols];
            ny[r] = new double[cols];
            nz[r] = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                (double ax, double ay, double az) = Secant(x, y, z, r, c, 0, 1, cols);
                (double bx, double by, double bz) = Secant(x, y, z, r, c, 1, 0, rows);

                double cx = (ay * bz) - (az * by);
                double cy = (az * bx) - (ax * bz);
                double cz = (ax * by) - (ay * bx);
                double length = System.Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz));
                if (length > 1e-300)
                {
                    cx /= length;
                    cy /= length;
                    cz /= length;
                }

                nx[r][c] = cx;
                ny[r][c] = cy;
                nz[r][c] = cz;
            }
        }

        return [MatrixFromRows(nx), MatrixFromRows(ny), MatrixFromRows(nz)];
    }

    /// <summary>
    /// The secant through the neighbours on either side of one vertex, one-sided at the ends. The
    /// step is in grid indices, so uneven spacing is carried by the positions themselves.
    /// </summary>
    private static (double X, double Y, double Z) Secant(
        double[,] x, double[,] y, double[,] z, int r, int c, int dr, int dc, int limit)
    {
        int i = dr != 0 ? r : c;
        int backR = i > 0 ? r - dr : r;
        int backC = i > 0 ? c - dc : c;
        int forwardR = i < limit - 1 ? r + dr : r;
        int forwardC = i < limit - 1 ? c + dc : c;
        return (
            x[forwardR, forwardC] - x[backR, backC],
            y[forwardR, forwardC] - y[backR, backC],
            z[forwardR, forwardC] - z[backR, backC]);
    }

    /// <summary>Unit-spaced X/Y grids for the <c>(z)</c>-only forms, matching MATLAB's column and row indices.</summary>
    private static (double[,] X, double[,] Y) IndexGrids(int rows, int cols)
    {
        var x = new double[rows, cols];
        var y = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                x[r, c] = c + 1;
                y[r, c] = r + 1;
            }
        }

        return (x, y);
    }

    /// <summary>Expands two generating vectors into the full grids the normal computation works on.</summary>
    private static (double[,] X, double[,] Y) ExpandGrids(double[] xv, double[] yv)
    {
        var x = new double[yv.Length, xv.Length];
        var y = new double[yv.Length, xv.Length];
        for (int r = 0; r < yv.Length; r++)
        {
            for (int c = 0; c < xv.Length; c++)
            {
                x[r, c] = xv[c];
                y[r, c] = yv[r];
            }
        }

        return (x, y);
    }

}
