using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    /// <summary>
    /// The option names <c>patch</c> and <c>fill</c> accept. They double as the disambiguator for
    /// <c>patch(x, y, c)</c> against <c>patch(x, y, z, c)</c>: the fourth argument is a Z coordinate
    /// unless it is one of these words.
    /// </summary>
    private static readonly HashSet<string> PatchOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Faces", "Vertices", "FaceColor", "EdgeColor", "LineWidth", "FaceAlpha", "EdgeAlpha",
        "FaceVertexCData", "CData", "DisplayName", "LineStyle", "FaceLighting",
        "FaceVertexAlphaData", "AlphaDataMapping",
    };

    private static readonly HashSet<string> Scatter3OptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Marker", "MarkerFaceColor", "MarkerEdgeColor", "MarkerFaceAlpha", "MarkerEdgeAlpha",
        "SizeData", "CData", "DisplayName", "LineWidth", "AlphaData", "AlphaDataMapping",

        // The spread properties belong to every marker chart in space, not only to swarmchart3.
        "XJitter", "YJitter", "ZJitter", "XJitterWidth", "YJitterWidth", "ZJitterWidth",
    };

    private static readonly HashSet<string> LineOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName",

        // M86. plot3 and line make Line objects, and MATLAB documents both marker colours for a
        // Line â€” so a call that named either of them was refused by a verb that draws markers.
        "MarkerFaceColor", "MarkerEdgeColor",
    };

    private static readonly HashSet<string> TextOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Color", "FontSize", "FontWeight", "FontAngle", "HorizontalAlignment", "VerticalAlignment",
        "BackgroundColor", "EdgeColor",
    };

    /// <summary>
    /// Registers the M45.D drawing primitives: <c>plot3</c>, <c>scatter3</c>, <c>fill</c>,
    /// <c>fill3</c>, <c>patch</c>, <c>line</c>, <c>text</c> and <c>surface</c>.
    /// </summary>
    private static void RegisterPrimitive3DBuiltins(JgsEnvironment env)
    {
        // These hand back a handle but stay quiet as a bare statement, exactly as the 2-D verbs do.
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        Define("plot3", OnNamedAxes((args, line, col) => Plot3(args, line, col)));
        Define("scatter3", OnNamedAxes((args, line, col) => Scatter3(args, line, col)));

        Define("fill", OnNamedAxes((args, line, col) => FillPatch("fill", args, line, col, Dimensions.Two)));
        Define("fill3", OnNamedAxes((args, line, col) => FillPatch("fill3", args, line, col, Dimensions.Three)));
        Define("patch", OnNamedAxes((args, line, col) => FillPatch("patch", args, line, col, Dimensions.Either)));

        Define("line", OnNamedAxes((args, line, col) => LinePrimitive(args, line, col)));
        Define("text", OnNamedAxes((args, line, col) => TextPrimitive(args, line, col)));

        // MATLAB's low-level `surface` is `surf` without the axes reset. It shares the dispatcher, so
        // the meshgrid collapse and the parametric path behave identically.
        Define("surface", OnNamedAxes((args, line, col) =>
        {
            if (!AddsWithoutReplace("surface"))
            {
                return Surface3D("surface", args, line, col, (x, y, z) => JG.Surf(x, y, z), JG.Surf, (x, y, z) => JG.Surf(x, y, z));
            }

            // R2025b's surface leaves a flat axes flat (open item 110): view(3) is what turns it.
            using IDisposable lowLevel = JG.AddingWithoutReplace();
            AxesModel axes = JG.Gca();
            bool was3D = axes.Is3D, grid = axes.Grid.ShowMajor;
            JgsValue made = Surface3D("surface", args, line, col, (x, y, z) => JG.Surf(x, y, z), JG.Surf, (x, y, z) => JG.Surf(x, y, z));
            axes.Is3D = was3D;
            axes.Grid.ShowMajor = grid;
            return made;
        }));
    }

    /// <summary>Which coordinate forms a fill/patch verb accepts.</summary>
    private enum Dimensions
    {
        /// <summary>(x, y, c) only â€” <c>fill</c>.</summary>
        Two,

        /// <summary>(x, y, z, c) only â€” <c>fill3</c>.</summary>
        Three,

        /// <summary>Either, decided by whether a fourth data argument is present â€” <c>patch</c>.</summary>
        Either,
    }

    // --- plot3 --------------------------------------------------------------------------------

    /// <summary>
    /// <c>plot3(x, y, z[, spec])</c>, repeated for as many groups as are given. Matrix arguments plot
    /// one line per column, the same rule <c>plot</c> follows for a matrix Y.
    /// </summary>
    private static JgsValue Plot3(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, LineOptionNames);
        if (data.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "plot3 expects (x, y, z) groups, each with an optional spec.");
        }

        var created = new List<Line3DPlot>();
        bool wasHolding = JG.IsHolding;
        try
        {
            int i = 0;
            while (i < data.Count)
            {
                if (i + 2 >= data.Count)
                {
                    throw new JgsRuntimeException(line, col, "plot3 expects (x, y, z) groups, each with an optional spec.");
                }

                double[][] xs = Polygons("plot3", data, i, line, col);
                double[][] ys = Polygons("plot3", data, i + 1, line, col);
                double[][] zs = Polygons("plot3", data, i + 2, line, col);
                i += 3;

                string? spec = null;
                if (i < data.Count && data[i].Type == JgsType.String)
                {
                    spec = data[i].AsString;
                    i++;
                }

                int lines = System.Math.Max(xs.Length, System.Math.Max(ys.Length, zs.Length));
                for (int k = 0; k < lines; k++)
                {
                    try
                    {
                        created.Add(JG.Plot3(Pick(xs, k), Pick(ys, k), Pick(zs, k), spec));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new JgsRuntimeException(line, col, ex.Message);
                    }

                    JG.Hold(true);
                }
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        foreach ((string name, JgsValue value) in options)
        {
            foreach (Line3DPlot plot in created)
            {
                ApplyLineOption("plot3", plot, name, value, line, col);
            }
        }

        return HandlesFor(created);
    }

    // --- scatter3 -----------------------------------------------------------------------------

    /// <summary>
    /// <c>scatter3(x, y, z[, s][, c][, 'filled'])</c> plus name-value options. <c>s</c> is a marker
    /// area, scalar or per point; <c>c</c> is either one color for the whole cloud (a spec letter, a
    /// name, or an [r g b] triplet) or one colormapped value per point.
    /// </summary>
    private static JgsValue Scatter3(IReadOnlyList<JgsValue> args, int line, int col) =>
        Scatter3Series("scatter3", args, line, col, bubbles: false);

    /// <summary>
    /// The body <c>scatter3</c>, <c>swarmchart3</c> and <c>bubblechart3</c> share. The three read the
    /// same arguments and draw the same object; what a verb decides is whether the points are spread
    /// where they crowd and whether the sizes are values or areas.
    /// </summary>
    private static JgsValue Scatter3Series(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, bool bubbles)
    {
        // The table form names three channels where the array form passes three arrays; everything
        // after them is read exactly as it is below, which is why the peel comes first.
        (IReadOnlyList<JgsValue> named, ScatterSource? source) =
            PeelScatterTable(verb, args, spatial: true, sized: bubbles, line, col);

        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(named, Scatter3OptionNames);

        bool filled = false;
        var positional = new List<JgsValue>();
        foreach (JgsValue value in data)
        {
            if (value.Type == JgsType.String && value.AsString.Equals("filled", StringComparison.OrdinalIgnoreCase))
            {
                filled = true;
                continue;
            }

            positional.Add(value);
        }

        // A colour word straight after the coordinates is c, not a size, as R2025b reads
        // scatter3(x, y, z, 'g') (open item 101); the default size stands in front of it.
        if (positional.Count == 4 && positional[3].Type == JgsType.String)
        {
            positional.Insert(3, JgsMatrix.FromColumnMajor([], 0, 0));
        }

        if (positional.Count is < 3 or > 5)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} expects (x, y, z), optionally followed by sizes and colors.");
        }

        double[] x = DoubleArray(verb, positional, 0, line, col);
        double[] y = DoubleArray(verb, positional, 1, line, col);
        double[] z = DoubleArray(verb, positional, 2, line, col);

        Scatter3DPlot plot;
        try
        {
            plot = JG.Scatter3(x, y, z);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        // 'filled' paints the interiors in the data's colour and leaves the edges off, as the 2-D
        // chart's does; a bubble chart is filled at 0.6 with its edges following the data (item 107).
        plot.Filled = filled || bubbles;
        if (filled && !bubbles)
        {
            plot.EdgeColor = Colors.Transparent;
        }

        if (bubbles)
        {
            plot.FaceAlpha = 0.6;
        }

        plot.BubbleSizing = bubbles;
        if (verb == "swarmchart3")
        {
            // A swarm in space spreads both of the coordinates that are not the height, which is what
            // gives the cloud a footprint instead of a line. Options said in the call still win.
            plot.XJitter = JitterStyle.Density;
            plot.YJitter = JitterStyle.Density;
        }

        if (positional.Count >= 4)
        {
            ApplySizeData(verb, plot, positional[3], line, col);
        }

        if (positional.Count == 5)
        {
            ApplyScatterColor(verb, plot, positional[4], line, col);
        }

        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "marker":
                    plot.Marker = ParseMarker(verb, value, line, col);
                    break;
                // The marker words a 2-D chart takes, which no longer paint the whole cloud (item 107).
                case "markerfacecolor":
                    MarkerFace(verb, plot, value, line, col);
                    break;
                case "markeredgecolor":
                    MarkerEdge(verb, plot, value, line, col);
                    break;
                case "markerfacealpha":
                    plot.FaceAlpha = MarkerAlpha("MarkerFaceAlpha", value, line, col, MarkerClass(plot));
                    break;
                case "markeredgealpha":
                    plot.EdgeAlpha = MarkerAlpha("MarkerEdgeAlpha", value, line, col, MarkerClass(plot));
                    break;
                case "sizedata":
                    ApplySizeData(verb, plot, value, line, col);
                    break;
                case "cdata":
                    ApplyScatterColor(verb, plot, value, line, col);
                    break;
                case "linewidth":
                    plot.EdgeWidth = NumOf($"{verb}: LineWidth", value, line, col);
                    break;
                case "xjitter":
                    plot.XJitter = ParseJitter($"{verb}: XJitter", value, line, col);
                    break;
                case "yjitter":
                    plot.YJitter = ParseJitter($"{verb}: YJitter", value, line, col);
                    break;
                case "zjitter":
                    plot.ZJitter = ParseJitter($"{verb}: ZJitter", value, line, col);
                    break;
                case "xjitterwidth":
                    plot.XJitterWidth = JitterWidth($"{verb}: XJitterWidth", value, line, col);
                    break;
                case "yjitterwidth":
                    plot.YJitterWidth = JitterWidth($"{verb}: YJitterWidth", value, line, col);
                    break;
                case "zjitterwidth":
                    plot.ZJitterWidth = JitterWidth($"{verb}: ZJitterWidth", value, line, col);
                    break;
                case "displayname":
                    plot.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                    break;

                // The transparency channel takes the property's own words and refusals (open item 111).
                case "alphadata":
                case "alphadatamapping":
                    JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(plot), name, value, line, col);
                    break;
            }
        }

        return Sourced(plot, source);
    }

    /// <summary>
    /// The sizes of a marker chart in space. A bubble chart's are data values and go through as they
    /// are; everyone else's are areas in points squared, so the diameter is their square root â€” and a
    /// single one of either is the whole cloud's.
    /// </summary>
    private static void ApplySizeData(string verb, Scatter3DPlot plot, JgsValue value, int line, int col)
    {
        double[] sizes = Numbers($"{verb}: sizes", value, line, col);
        if (sizes.Length == 0)
        {
            // [] is MATLAB's placeholder for the default size, written to reach the colour after it.
            return;
        }

        if (plot.BubbleSizing)
        {
            // A bubble's size is a reading, so one of them is a reading every bubble shares rather
            // than a marker size for the whole cloud â€” the scale still has to see it as data.
            double[] values = sizes.Length == 1 && plot.X.Count != 1
                ? [.. Enumerable.Repeat(sizes[0], plot.X.Count)]
                : sizes;

            try
            {
                plot.SizeData = values;
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            return;
        }

        if (sizes.Length == 1)
        {
            // MATLAB's s is an area in points squared; the model draws a marker of that diameter, so
            // a scalar goes straight onto MarkerSize the same way sqrt does for the per-point form.
            plot.MarkerSize = System.Math.Sqrt(System.Math.Max(0, sizes[0]));
            return;
        }

        try
        {
            plot.SizeData = sizes;
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    private static void ApplyScatterColor(
        string verb, Scatter3DPlot plot, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            plot.Color = SpecColor(verb, value, line, col);
            return;
        }

        double[] values = Numbers($"{verb}: colors", value, line, col);
        if (values.Length == 1 && plot.X.Count != 1)
        {
            // A scalar c is one colormap index for the whole cloud, which the model expresses as the
            // same value at every point rather than as a separate mode.
            plot.ColorData = Enumerable.Repeat(values[0], plot.X.Count).ToArray();
            return;
        }

        // R2025b's three shapes, a colour per point among them (open item 107).
        ApplyMarkerColors(plot, ReadMarkerColors(verb, value, plot.X.Count, strict: true, line, col));
    }

    /// <summary>
    /// A colour named in a marker chart's own arguments, refused in R2025b's words when it is not one
    /// (probe_107: <c>scatter(x, y, 20, 'q')</c>).
    /// </summary>
    internal static Color SpecColor(string verb, JgsValue value, int line, int col)
    {
        try
        {
            return OptionColor(value, line, col, verb);
        }
        catch (JgsRuntimeException) when (JgsRunningDialect.ThreadIsMatlab)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:checkpvpairs:UnrecognizedLineStyleParameter",
                "Error in color/linetype argument.");
        }
    }

    // --- fill, fill3, patch -------------------------------------------------------------------

    /// <summary>
    /// Whether a verb is MATLAB's low-level one, in a MATLAB-dialect script: R2025b's <c>patch</c> and
    /// <c>surface</c> add to the axes without newplot and leave its view alone (open item 110). JGS's
    /// replace, as they always did.
    /// </summary>
    private static bool AddsWithoutReplace(string verb) =>
        JgsRunningDialect.ThreadIsMatlab && verb is "patch" or "surface";

    private static JgsValue FillPatch(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, Dimensions dimensions)
    {
        using IDisposable? lowLevel = AddsWithoutReplace(verb) ? JG.AddingWithoutReplace() : null;
        if (verb == "patch" && PatchCoordinatesByName(args, line, col) is { } byName)
        {
            PatchPlot made = FromCoordinates(verb, byName.Coordinates, line, col, dimensions, coloured: false);
            static bool Named(IEnumerable<(string Name, JgsValue Value)> pairs, string name) =>
                pairs.Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!Named(byName.Options, "FaceColor") && !Named(byName.Options, "CData") && !Named(byName.Options, "FaceVertexCData")
                && !Named(byName.Options, "Parent") && !Named(byName.Others, "Parent"))
            {
                // No colour named: R2025b fills with the ink when the axes came first (probe_114),
                // and with the colour order's blue when the axes came as 'Parent' (oi_findprop_detach_computed).
                made.FaceColor = Colors.DarkGray;
            }

            ApplyPatchOptions(verb, made, byName.Options, line, col);
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(made);
            foreach ((string name, JgsValue value) in byName.Others)
            {
                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }

            if (lowLevel is null && made.Z.Any(z => double.IsFinite(z) && z != 0))
            {
                JG.Gca().Is3D = true;
            }

            return Handle(made);
        }

        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, PatchOptionNames);

        // patch(fv) â€” a struct holding faces and vertices, which is what every verb in the M59 volume
        // family hands back when a script asks for the shape instead of the drawing. Reading it here
        // is what closes the loop between 'fv = isosurface(â€¦)' and 'patch(fv)'.
        if (data.Count == 1 && data[0].Type == JgsType.Struct)
        {
            foreach ((string name, JgsValue value) in FaceVertexFields(verb, data[0], line, col))
            {
                options.Insert(0, (name, value));
            }

            data = [];
        }

        // R2025b's fill and fill3 are a patch per polygon (open item 112); patch stays one object.
        if (JgsRunningDialect.ThreadIsMatlab && verb is "fill" or "fill3" && data.Count > 0)
        {
            return MatlabFillPolygons(verb, data, options, dimensions == Dimensions.Three, line, col);
        }

        PatchPlot patch = data.Count == 0
            ? FromFacesAndVertices(verb, options, line, col)
            : FromCoordinates(verb, data, line, col, dimensions);

        ApplyPatchOptions(verb, patch, options, line, col);

        // A patch that carries heights is a three-dimensional object, and an axes holding one has to
        // say so: until M72 only the surface verbs set the flag, so an axes whose only child was
        // patch(isosurface(...)) stayed flat -- view(3) wrote angles nothing then read, and the
        // camera never moved. Zero heights are how a two-dimensional patch spells 'no z', so they
        // leave a plain fill alone.
        if (lowLevel is null && patch.Z.Any(z => double.IsFinite(z) && z != 0))
        {
            JG.Gca().Is3D = true;
        }

        return Handle(patch);
    }

    /// <summary>
    /// The faces, vertices and per-vertex colours of a <c>patch(fv)</c> struct, as the name/value
    /// pairs the <c>'Faces'</c>/<c>'Vertices'</c> form already reads.
    /// </summary>
    /// <remarks>
    /// The field names are matched without regard to case because MATLAB writes them lower-case in a
    /// struct and capitalised as properties, and a script that built its own struct by hand may have
    /// used either. A struct without both is refused by name rather than treated as coordinates,
    /// because the mistake is almost always a misspelt field.
    /// </remarks>
    private static List<(string Name, JgsValue Value)> FaceVertexFields(
        string verb, JgsValue value, int line, int col)
    {
        Dictionary<string, JgsValue> fields = value.AsStruct;
        var found = new List<(string, JgsValue)>();

        foreach (string wanted in (string[])["Faces", "Vertices", "FaceVertexCData"])
        {
            foreach ((string name, JgsValue field) in fields)
            {
                if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add((wanted, field));
                    break;
                }
            }
        }

        if (found.Count < 2 || found[0].Item1 != "Faces" || found[1].Item1 != "Vertices")
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: a struct has to hold both 'faces' and 'vertices'; this one holds "
                + $"{(fields.Count == 0 ? "nothing" : string.Join(", ", fields.Keys))}.");
        }

        return found;
    }

    /// <summary>
    /// The <c>patch('Faces', F, 'Vertices', V, â€¦)</c> form. V is n-by-2 or n-by-3; F is one row per
    /// face, holding 1-based vertex numbers as MATLAB writes them.
    /// </summary>
    private static PatchPlot FromFacesAndVertices(
        string verb, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        JgsValue? facesValue = Option(options, "Faces");
        JgsValue? verticesValue = Option(options, "Vertices");
        if (facesValue is null || verticesValue is null)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} needs coordinates, or both 'Faces' and 'Vertices'.");
        }

        double[,] vertices = RowsOf(verb, verticesValue, line, col);
        int count = vertices.GetLength(0);
        int components = vertices.GetLength(1);
        if (components is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: 'Vertices' has {components} columns; it needs 2 (x, y) or 3 (x, y, z).");
        }

        var x = new double[count];
        var y = new double[count];
        var z = new double[count];
        for (int i = 0; i < count; i++)
        {
            x[i] = vertices[i, 0];
            y[i] = vertices[i, 1];
            z[i] = components == 3 ? vertices[i, 2] : 0;
        }

        double[,] faceRows = RowsOf(verb, facesValue, line, col);
        var faces = new int[faceRows.GetLength(0)][];
        for (int f = 0; f < faces.Length; f++)
        {
            // A face row may be padded with NaN so that a mixed triangle/quad table stays rectangular,
            // which is exactly how MATLAB writes one; the padding is dropped rather than rejected.
            var indices = new List<int>(faceRows.GetLength(1));
            for (int k = 0; k < faceRows.GetLength(1); k++)
            {
                double raw = faceRows[f, k];
                if (double.IsNaN(raw))
                {
                    continue;
                }

                int index = (int)System.Math.Round(raw) - 1;
                if (index < 0 || index >= count)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: face {f + 1} refers to vertex {raw}, but there are only {count}.");
                }

                indices.Add(index);
            }

            faces[f] = indices.ToArray();
        }

        PatchPlot built = JG.Patch(x, y, z, faces);
        built.IsFlat = components == 2; // two columns: a patch in the plane (open item 114)
        return built;
    }

    /// <summary>
    /// A face table or a vertex list: a matrix, or one row written as a vector — a patch of one face,
    /// <c>patch('Faces', [1 2 3], …)</c>, which was refused as no matrix (open item 110).
    /// </summary>
    private static double[,] RowsOf(string verb, JgsValue value, int line, int col)
    {
        if (JgsMatrix.IsMatrix(value))
        {
            return Matrix(verb, [value], 0, line, col);
        }

        double[] row = ToDoubles(verb, value, line, col);
        var one = new double[1, row.Length];
        for (int k = 0; k < row.Length; k++)
        {
            one[0, k] = row[k];
        }

        return one;
    }

    /// <summary>
    /// <c>patch()</c> and <c>patch('XData', x, 'YData', y, …)</c>, the low-level form whose
    /// coordinates are properties (open item 93, probe <c>probe_93d</c>): the coordinates, R2025b's
    /// triangle for one left out; the names the patch verbs read; and the other properties. Null for
    /// any other form, the <c>'Faces'</c>/<c>'Vertices'</c> one included.
    /// </summary>
    private static (JgsValue[] Coordinates, List<(string Name, JgsValue Value)> Options, List<(string Name, JgsValue Value)> Others)?
        PatchCoordinatesByName(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && !IsTextScalar(args[0]))
        {
            return null;
        }

        if (args.Count % 2 != 0)
        {
            return null;
        }

        JgsValue x = Column(new double[] { 0, 1, 0 }), y = Column(new double[] { 0, 0, 1 });
        JgsValue? z = null;
        bool named = args.Count == 0;
        var options = new List<(string Name, JgsValue Value)>();
        var others = new List<(string Name, JgsValue Value)>();
        for (int i = 0; i < args.Count; i += 2)
        {
            string name = StrOf("patch", args[i], line, col);
            switch (name.ToLowerInvariant())
            {
                case "faces":
                case "vertices":
                    return null;
                case "xdata": x = args[i + 1]; named = true; break;
                case "ydata": y = args[i + 1]; named = true; break;
                case "zdata": z = args[i + 1]; named = true; break;
                default:
                    (PatchOptionNames.Contains(name) ? options : others).Add((name, args[i + 1]));
                    break;
            }
        }

        return named ? (z is null ? [x, y] : [x, y, z], options, others) : null;
    }

    /// <summary>
    /// The coordinate form. A vector argument is one polygon; a matrix is one polygon per column,
    /// which is how <c>fill</c> draws several at once. Without <paramref name="coloured"/> the data
    /// is the coordinates alone (the low-level form).
    /// </summary>
    private static PatchPlot FromCoordinates(
        string verb, IReadOnlyList<JgsValue> data, int line, int col, Dimensions dimensions, bool coloured = true)
    {
        int colour = coloured ? 1 : 0;
        bool threeD = dimensions switch
        {
            Dimensions.Two => false,
            Dimensions.Three => true,
            _ => data.Count >= 3 + colour,
        };

        int needed = (threeD ? 3 : 2) + colour;
        if (data.Count != needed)
        {
            string shape = threeD ? "(x, y, z, c)" : "(x, y, c)";
            throw new JgsRuntimeException(line, col, $"{verb} expects {shape}.");
        }

        double[][] xs = Polygons(verb, data, 0, line, col);
        double[][] ys = Polygons(verb, data, 1, line, col);
        double[][] zs = threeD
            ? Polygons(verb, data, 2, line, col)
            : xs.Select(static column => new double[column.Length]).ToArray();

        int polygons = System.Math.Max(xs.Length, System.Math.Max(ys.Length, zs.Length));
        var x = new List<double>();
        var y = new List<double>();
        var z = new List<double>();
        var faces = new int[polygons][];
        for (int p = 0; p < polygons; p++)
        {
            double[] px = Pick(xs, p), py = Pick(ys, p), pz = Pick(zs, p);
            if (px.Length != py.Length || px.Length != pz.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: polygon {p + 1} has {px.Length}, {py.Length} and {pz.Length} coordinates; they must match.");
            }

            var face = new int[px.Length];
            for (int v = 0; v < px.Length; v++)
            {
                face[v] = x.Count;
                x.Add(px[v]);
                y.Add(py[v]);
                z.Add(pz[v]);
            }

            faces[p] = face;
        }

        PatchPlot patch;
        try
        {
            patch = JG.Patch([.. x], [.. y], [.. z], faces);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        patch.IsFlat = !threeD; // no z: a patch in the plane (open item 114)
        if (threeD && !AddsWithoutReplace(verb))
        {
            JG.Gca().Is3D = true;
        }

        if (coloured)
        {
            ApplyPatchColor(verb, patch, data[needed - 1], line, col);
        }

        return patch;
    }

    /// <summary>
    /// Reads the color argument. A string is a color name; a numeric triplet in [0, 1] is an RGB
    /// value; anything else is colormapped data, per face when its length matches the face count and
    /// per vertex when it matches the vertex count.
    /// </summary>
    private static void ApplyPatchColor(string verb, PatchPlot patch, JgsValue value, int line, int col)
    {
        if (IsSingleColor(value, patch.X.Count))
        {
            patch.FaceColor = OptionColor(value, line, col, verb);
            return;
        }

        double[] values = Numbers($"{verb}: colors", value, line, col);
        if (values.Length != patch.Faces.Count && values.Length != patch.X.Count)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the color argument has {values.Length} values, but the patch has "
                    + $"{patch.Faces.Count} faces and {patch.X.Count} vertices.");
        }

        JgsGraphicsProperties.SetPatchColorData(patch, values); // and the shading the data implies (open item 114)
    }

    private static void ApplyPatchOptions(
        string verb, PatchPlot patch, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "faces":
                case "vertices":
                    break; // already consumed by the constructor
                case "facecolor":
                    if (IsNone(value))
                    {
                        patch.FaceColor = Colors.Transparent;
                    }
                    else if (value.Type == JgsType.String
                        && value.AsString.Equals("interp", StringComparison.OrdinalIgnoreCase))
                    {
                        patch.Shading = PatchShading.Interp;
                    }
                    else if (value.Type == JgsType.String
                        && value.AsString.Equals("flat", StringComparison.OrdinalIgnoreCase))
                    {
                        patch.Shading = PatchShading.Flat;
                    }
                    else
                    {
                        patch.FaceColor = OptionColor(value, line, col, verb);
                    }

                    break;
                case "edgecolor":
                    patch.EdgeColor = IsNone(value) ? null : OptionColor(value, line, col, verb);
                    break;
                case "linewidth":
                    patch.EdgeWidth = NumOf($"{verb}: LineWidth", value, line, col);
                    break;
                case "linestyle":
                    if (IsNone(value))
                    {
                        patch.EdgeColor = null;
                    }

                    break;
                // The patch's own two alphas, which its handle reads back; this wrote the object's
                // opacity until open item 102, so the value drew but read back as 1.
                // ...and since open item 107, the property's own reading: 'flat' and 'interp' taken,
                // R2025b's refusals; the per-vertex alphas those words read come the same way.
                case "facealpha":
                case "edgealpha":
                case "facevertexalphadata":
                case "alphadatamapping":
                    try
                    {
                        JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(patch), name, value, line, col);
                    }
                    catch (JgsRuntimeException refused) when (refused.Identifier.StartsWith("MATLAB:hg:dt_conv", StringComparison.Ordinal))
                    {
                        // Given with the call, R2025b's patch says only the reason, not which property.
                        throw new JgsRuntimeException(line, col, refused.Identifier,
                            refused.Message[(refused.Message.IndexOf('\n') + 1)..]);
                    }

                    break;
                case "facevertexcdata":
                case "cdata":
                    ApplyPatchColor(verb, patch, value, line, col);
                    break;
                case "displayname":
                    patch.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                    break;
            }
        }
    }

    // --- line and text ------------------------------------------------------------------------

    /// <summary>
    /// MATLAB's low-level <c>line(x, y[, z])</c>. Unlike <c>plot</c> it does not clear the axes â€” it
    /// is the primitive every high-level verb is built on â€” so it goes straight to the current axes
    /// rather than through the replace-on-new-plot path.
    /// </summary>
    private static JgsValue LinePrimitive(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // line() and line('XData', x, 'YData', y, â€¦) are the low-level form, which takes every
        // default, its colour, style and marker among them (open item 93, probe_93d).
        if (args.Count == 0 || IsTextScalar(args[0]))
        {
            return JgsGraphicsProperties.MakingLowLevel(() => LineFromProperties(args, line, col));
        }

        // The coordinates are numbers, so the first text after them begins the options â€” any
        // property a line has, not only the ones this verb reads itself (U4).
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) = SplitOptionsAt(args, 2);
        if (data.Count is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col, "line expects (x, y) or (x, y, z), then 'Name', value options.");
        }

        return MakeLine(data, options, line, col);
    }

    /// <summary>
    /// <c>line('XData', x, 'YData', y, â€¦)</c>: the coordinates are properties like any other, and one
    /// left out is R2025b's <c>[0 1]</c>.
    /// </summary>
    private static JgsValue LineFromProperties(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:plot:minrhs", "Not enough input arguments.");
        }

        JgsValue x = Row([0, 1]), y = Row([0, 1]);
        JgsValue? z = null;
        var options = new List<(string Name, JgsValue Value)>();
        for (int i = 0; i < args.Count; i += 2)
        {
            string name = StrOf("line", args[i], line, col);
            switch (name.ToLowerInvariant())
            {
                case "xdata": x = args[i + 1]; break;
                case "ydata": y = args[i + 1]; break;
                case "zdata": z = args[i + 1]; break;
                default: options.Add((name, args[i + 1])); break;
            }
        }

        return MakeLine(z is null ? [x, y] : [x, y, z], options, line, col);
    }

    private static JgsValue MakeLine(IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        AxesModel axes = JG.Gca();
        double[] x = DoubleArray("line", data, 0, line, col);
        double[] y = DoubleArray("line", data, 1, line, col);
        try
        {
            if (data.Count == 3)
            {
                Line3DPlot plot = axes.AddLine3D(x, y, DoubleArray("line", data, 2, line, col));
                foreach ((string name, JgsValue value) in options)
                {
                    if (LineOptionNames.Contains(name))
                    {
                        ApplyLineOption("line", plot, name, value, line, col);
                    }
                    else
                    {
                        JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(plot), name, value, line, col);
                    }
                }

                return Handle(plot);
            }
            else
            {
                LinePlot plot = axes.AddLine(x, y);
                foreach ((string name, JgsValue value) in options)
                {
                    if (LineOptionNames.Contains(name))
                    {
                        ApplyLineOption("line", plot, name, value, line, col);
                    }
                    else
                    {
                        JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(plot), name, value, line, col);
                    }
                }

                return Handle(plot);
            }
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary><c>text(x, y, str)</c> or <c>text(x, y, z, str)</c>, plus 'Name', value options.</summary>
    private static JgsValue TextPrimitive(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // The label is the first text; whatever follows it is options â€” any property a text has,
        // not only the ones this verb reads itself (U4).
        bool heightGiven = args.Count > 3
            && (args[2].Type is JgsType.Number or JgsType.Bool || (args[2].Type == JgsType.Array && !args[2].IsStringArray));
        int labelAt = heightGiven ? 3 : 2;
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) = SplitOptionsAt(args, labelAt + 1);
        if (data.Count is < 3 or > 4)
        {
            throw new JgsRuntimeException(line, col, "text expects (x, y, string) or (x, y, z, string).");
        }

        bool threeD = data.Count == 4;
        double x = Num("text", data, 0, line, col);
        double y = Num("text", data, 1, line, col);
        double z = threeD ? Num("text", data, 2, line, col) : 0;
        // The label is text, or a cell or a string array of lines.
        JgsValue said = data[threeD ? 3 : 2];
        bool cell = said.Type == JgsType.Cell;
        string label = cell
            ? AnnotationString("text", said, line, col)
            : said.IsStringArray && said.ArrayLength != 1
                ? string.Join("\n", Enumerable.Range(0, said.ArrayLength).Select(i => TextOf(said.ElementAt(i))))
                : Str("text", data, threeD ? 3 : 2, line, col);

        TextAnnotation annotation = JG.Text(x, y, z, label);
        annotation.LinesAreCell = cell;
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                case "color":
                    annotation.Color = OptionColor(value, line, col, "text");
                    break;
                case "backgroundcolor":
                    annotation.Background = OptionColor(value, line, col, "text");
                    break;
                case "edgecolor":
                    annotation.BorderColor = IsNone(value) ? null : OptionColor(value, line, col, "text");
                    break;
                case "fontsize":
                    annotation.FontSize = NumOf("text: FontSize", value, line, col);
                    break;
                case "fontweight":
                    annotation.Bold = StrOf("text: FontWeight", value, line, col)
                        .Equals("bold", StringComparison.OrdinalIgnoreCase);
                    break;
                case "fontangle":
                    annotation.Italic = StrOf("text: FontAngle", value, line, col)
                        .Equals("italic", StringComparison.OrdinalIgnoreCase);
                    break;
                case "horizontalalignment":
                    annotation.HorizontalAlignment = StrOf("text: HorizontalAlignment", value, line, col)
                        .ToLowerInvariant() switch
                    {
                        "center" => HorizontalAlignment.Center,
                        "right" => HorizontalAlignment.Right,
                        _ => HorizontalAlignment.Left,
                    };
                    break;
                case "verticalalignment":
                    annotation.VerticalAlignment = StrOf("text: VerticalAlignment", value, line, col)
                        .ToLowerInvariant() switch
                    {
                        "top" => VerticalAlignment.Top,
                        "middle" => VerticalAlignment.Middle,
                        _ => VerticalAlignment.Bottom,
                    };
                    break;
                // text(x, y, s, 'Units', u): x and y were given in u, so nothing moves (open item 46).
                case "units":
                    JgsGraphicsProperties.SetTextUnits(
                        JgsHandleRegistry.EntryFor(annotation), StrOf("text: Units", value, line, col).ToLowerInvariant(), convert: false, line, col);
                    break;
                default:
                    JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(annotation), name, value, line, col);
                    break;
            }
        }

        // A text is an annotation rather than a series, so it does not go through Handle â€” but a
        // handle is a handle, and set(t, 'String', â€¦) has to reach it like anything else.
        return JgsHandleRegistry.For(annotation);
    }

    private static void ApplyLineOption(
        string verb, Line3DPlot plot, string name, JgsValue value, int line, int col)
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
                plot.DashStyle = ParseDash(verb, value, line, col) ?? plot.DashStyle;
                break;
            case "marker":
                plot.Marker = ParseMarker(verb, value, line, col);
                break;
            case "markersize":
                plot.MarkerSize = NumOf($"{verb}: MarkerSize", value, line, col);
                break;
            case "markerfacecolor":
                plot.MarkerFaceColor = NoneOrOptionColor(value, line, col, verb);
                break;
            case "markeredgecolor":
                plot.MarkerEdgeColor = OptionColor(value, line, col, verb);
                break;
            case "displayname":
                plot.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                break;
        }
    }

    private static void ApplyLineOption(
        string verb, LinePlot plot, string name, JgsValue value, int line, int col)
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
                plot.DashStyle = ParseDash(verb, value, line, col) ?? plot.DashStyle;
                break;
            case "marker":
                plot.Marker = ParseMarker(verb, value, line, col);
                break;
            case "markersize":
                plot.MarkerSize = NumOf($"{verb}: MarkerSize", value, line, col);
                break;
            case "markerfacecolor":
                plot.MarkerFaceColor = NoneOrOptionColor(value, line, col, verb);
                break;
            case "markeredgecolor":
                plot.MarkerEdgeColor = OptionColor(value, line, col, verb);
                break;
            case "displayname":
                plot.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                break;
        }
    }

    /// <summary>
    /// A marker fill, where <c>'none'</c> is an unfilled marker rather than a colour â€” the same
    /// reading the property table gives the name, so a verb and a <c>set</c> agree.
    /// </summary>
    private static Color? NoneOrOptionColor(JgsValue value, int line, int col, string verb) =>
        value.Type == JgsType.String
        && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : OptionColor(value, line, col, verb);

    // --- shared argument reading --------------------------------------------------------------

    /// <summary>
    /// Splits trailing 'Name', value pairs off an argument list. Options begin at the first string
    /// that names one â€” the same rule <c>plot</c> uses, so a spec string or a color word before them
    /// stays data.
    /// </summary>
    /// <summary>
    /// Splits a call into its data and its options where the options begin at the first text at or
    /// after <paramref name="from"/> â€” the form of a primitive whose data are all numbers.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Data, List<(string Name, JgsValue Value)> Options) SplitOptionsAt(
        IReadOnlyList<JgsValue> args, int from)
    {
        int start = args.Count;
        for (int i = from; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                start = i;
                break;
            }
        }

        var options = new List<(string, JgsValue)>();
        for (int i = start; i + 1 < args.Count; i += 2)
        {
            options.Add((IsTextScalar(args[i]) ? TextOf(args[i]) : string.Empty, args[i + 1]));
        }

        return ([.. args.Take(start)], options);
    }

    private static (IReadOnlyList<JgsValue> Data, List<(string Name, JgsValue Value)> Options) SplitTrailingOptions(
        IReadOnlyList<JgsValue> args, HashSet<string> names)
    {
        int start = args.Count;
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (args[i].Type == JgsType.String && names.Contains(args[i].AsString))
            {
                start = i;
                break;
            }
        }

        if (start == args.Count)
        {
            return (args, []);
        }

        var data = new List<JgsValue>();
        for (int i = 0; i < start; i++)
        {
            data.Add(args[i]);
        }

        var options = new List<(string, JgsValue)>();
        for (int i = start; i + 1 < args.Count; i += 2)
        {
            options.Add((args[i].Type == JgsType.String ? args[i].AsString : string.Empty, args[i + 1]));
        }

        return (data, options);
    }

    private static JgsValue? Option(List<(string Name, JgsValue Value)> options, string name)
    {
        foreach ((string candidate, JgsValue value) in options)
        {
            if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// A coordinate argument as one array per polygon or line: a vector is a single one, a matrix is
    /// one per column, which is MATLAB's rule for <c>fill</c> and <c>plot3</c> alike.
    /// </summary>
    private static double[][] Polygons(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        int rows = JgsMatrix.RowCount(value);
        int cols = JgsMatrix.ColCount(value);
        if (rows <= 1 || cols <= 1)
        {
            return [DoubleArray(name, args, index, line, col)];
        }

        double[,] matrix = Matrix(name, args, index, line, col);
        var columns = new double[cols][];
        for (int c = 0; c < cols; c++)
        {
            var column = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                column[r] = matrix[r, c];
            }

            columns[c] = column;
        }

        return columns;
    }

    /// <summary>
    /// The n-th column of a set, or the only one there is. A single vector paired with a matrix is
    /// reused for every column, which is what makes <c>fill(x, Y, c)</c> work with a shared X.
    /// </summary>
    private static double[] Pick(double[][] columns, int index) =>
        columns.Length == 1 ? columns[0] : columns[index];

    /// <summary>
    /// Whether a color argument names one color rather than carrying mapped data: a string always
    /// does, and a numeric triple does when its components are all in [0, 1] â€” which is the RGB range
    /// MATLAB uses. With three points a row is the colour and a column the data, as R2025b reads them
    /// (probe <c>probe_93d</c>).
    /// </summary>
    private static bool IsSingleColor(JgsValue value, int points)
    {
        if (value.Type == JgsType.String)
        {
            return true;
        }

        if (value.Type != JgsType.Array || value.ArrayLength != 3 || (points == 3 && value.Rows != 1))
        {
            return false;
        }

        for (int i = 0; i < 3; i++)
        {
            JgsValue component = value.ElementAt(i);
            if (component.Type is not (JgsType.Number or JgsType.Bool))
            {
                return false;
            }

            double v = component.AsNumber;
            if (!(v >= 0) || !(v <= 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A numeric argument as an array, whether it arrived as one or as a bare scalar. A scalar
    /// <see cref="JgsValue"/> has no element buffer at all, so handing one to <c>ToDoubles</c> is a
    /// null reference rather than a friendly error â€” which is exactly what <c>scatter3(x, y, z, 36)</c>
    /// would hit.
    /// </summary>
    private static double[] Numbers(string what, JgsValue value, int line, int col) =>
        value.Type is JgsType.Number or JgsType.Bool
            ? [value.AsNumber]
            : ToDoubles(what, value, line, col);

    private static bool IsNone(JgsValue value) =>
        value.Type == JgsType.String && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static DashStyle? ParseDash(string verb, JgsValue value, int line, int col)
    {
        string style = StrOf($"{verb}: LineStyle", value, line, col);
        return style.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? DashStyle.None
            : LineSpec.Parse(style).Dash;
    }

    private static MarkerType ParseMarker(string verb, JgsValue value, int line, int col)
    {
        string marker = StrOf($"{verb}: Marker", value, line, col);
        return marker.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? MarkerType.None
            : LineSpec.Parse(marker).Marker ?? MarkerType.None;
    }
}
