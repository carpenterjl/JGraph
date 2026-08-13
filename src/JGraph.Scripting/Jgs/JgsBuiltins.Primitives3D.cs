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
        "Faces", "Vertices", "FaceColor", "EdgeColor", "LineWidth", "FaceAlpha",
        "FaceVertexCData", "CData", "DisplayName", "LineStyle", "FaceLighting",
    };

    private static readonly HashSet<string> Scatter3OptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Marker", "MarkerFaceColor", "MarkerEdgeColor", "SizeData", "CData", "DisplayName", "LineWidth",

        // The spread properties belong to every marker chart in space, not only to swarmchart3.
        "XJitter", "YJitter", "ZJitter", "XJitterWidth", "YJitterWidth", "ZJitterWidth",
    };

    private static readonly HashSet<string> LineOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Color", "LineWidth", "LineStyle", "Marker", "MarkerSize", "DisplayName",
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
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        Define("plot3", (args, line, col) => Plot3(args, line, col));
        Define("scatter3", (args, line, col) => Scatter3(args, line, col));

        Define("fill", (args, line, col) => FillPatch("fill", args, line, col, Dimensions.Two));
        Define("fill3", (args, line, col) => FillPatch("fill3", args, line, col, Dimensions.Three));
        Define("patch", (args, line, col) => FillPatch("patch", args, line, col, Dimensions.Either));

        Define("line", (args, line, col) => LinePrimitive(args, line, col));
        Define("text", (args, line, col) => TextPrimitive(args, line, col));

        // MATLAB's low-level `surface` is `surf` without the axes reset. It shares the dispatcher, so
        // the meshgrid collapse and the parametric path behave identically.
        Define("surface", (args, line, col) => Surface3D("surface", args, line, col,
            (x, y, z) => JG.Surf(x, y, z), JG.Surf, (x, y, z) => JG.Surf(x, y, z)));
    }

    /// <summary>Which coordinate forms a fill/patch verb accepts.</summary>
    private enum Dimensions
    {
        /// <summary>(x, y, c) only — <c>fill</c>.</summary>
        Two,

        /// <summary>(x, y, z, c) only — <c>fill3</c>.</summary>
        Three,

        /// <summary>Either, decided by whether a fourth data argument is present — <c>patch</c>.</summary>
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
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, Scatter3OptionNames);

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

        plot.Filled = filled;
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
                case "markerfacecolor":
                    plot.Filled = true;
                    plot.Color = OptionColor(value, line, col, verb);
                    break;
                case "markeredgecolor":
                    plot.Color = OptionColor(value, line, col, verb);
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
            }
        }

        return Handle(plot);
    }

    /// <summary>
    /// The sizes of a marker chart in space. A bubble chart's are data values and go through as they
    /// are; everyone else's are areas in points squared, so the diameter is their square root — and a
    /// single one of either is the whole cloud's.
    /// </summary>
    private static void ApplySizeData(string verb, Scatter3DPlot plot, JgsValue value, int line, int col)
    {
        double[] sizes = Numbers($"{verb}: sizes", value, line, col);
        if (plot.BubbleSizing)
        {
            // A bubble's size is a reading, so one of them is a reading every bubble shares rather
            // than a marker size for the whole cloud — the scale still has to see it as data.
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
        if (IsSingleColor(value, plot.X.Count))
        {
            plot.Color = OptionColor(value, line, col, verb);
            return;
        }

        double[] values = Numbers($"{verb}: colors", value, line, col);
        if (values.Length == 1 && plot.X.Count != 1)
        {
            // A scalar c is one colormap index for the whole cloud, which the model expresses as the
            // same value at every point rather than as a separate mode.
            values = Enumerable.Repeat(values[0], plot.X.Count).ToArray();
        }

        try
        {
            plot.ColorData = values;
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    // --- fill, fill3, patch -------------------------------------------------------------------

    private static JgsValue FillPatch(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, Dimensions dimensions)
    {
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, PatchOptionNames);

        // patch(fv) — a struct holding faces and vertices, which is what every verb in the M59 volume
        // family hands back when a script asks for the shape instead of the drawing. Reading it here
        // is what closes the loop between 'fv = isosurface(…)' and 'patch(fv)'.
        if (data.Count == 1 && data[0].Type == JgsType.Struct)
        {
            foreach ((string name, JgsValue value) in FaceVertexFields(verb, data[0], line, col))
            {
                options.Insert(0, (name, value));
            }

            data = [];
        }

        PatchPlot patch = data.Count == 0
            ? FromFacesAndVertices(verb, options, line, col)
            : FromCoordinates(verb, data, line, col, dimensions);

        ApplyPatchOptions(verb, patch, options, line, col);
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
    /// The <c>patch('Faces', F, 'Vertices', V, …)</c> form. V is n-by-2 or n-by-3; F is one row per
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

        double[,] vertices = Matrix(verb, [verticesValue], 0, line, col);
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

        double[,] faceRows = Matrix(verb, [facesValue], 0, line, col);
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

        return JG.Patch(x, y, z, faces);
    }

    /// <summary>
    /// The coordinate form. A vector argument is one polygon; a matrix is one polygon per column,
    /// which is how <c>fill</c> draws several at once.
    /// </summary>
    private static PatchPlot FromCoordinates(
        string verb, IReadOnlyList<JgsValue> data, int line, int col, Dimensions dimensions)
    {
        bool threeD = dimensions switch
        {
            Dimensions.Two => false,
            Dimensions.Three => true,
            _ => data.Count >= 4,
        };

        int needed = threeD ? 4 : 3;
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

        if (threeD)
        {
            JG.Gca().Is3D = true;
        }

        ApplyPatchColor(verb, patch, data[needed - 1], line, col);
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

        patch.ColorData = values;
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
                case "facealpha":
                    patch.Opacity = NumOf($"{verb}: FaceAlpha", value, line, col);
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
    /// MATLAB's low-level <c>line(x, y[, z])</c>. Unlike <c>plot</c> it does not clear the axes — it
    /// is the primitive every high-level verb is built on — so it goes straight to the current axes
    /// rather than through the replace-on-new-plot path.
    /// </summary>
    private static JgsValue LinePrimitive(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, LineOptionNames);
        if (data.Count is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col, "line expects (x, y) or (x, y, z), then 'Name', value options.");
        }

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
                    ApplyLineOption("line", plot, name, value, line, col);
                }

                return Handle(plot);
            }
            else
            {
                LinePlot plot = axes.AddLine(x, y);
                foreach ((string name, JgsValue value) in options)
                {
                    ApplyLineOption("line", plot, name, value, line, col);
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
        (IReadOnlyList<JgsValue> data, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(args, TextOptionNames);
        if (data.Count is < 3 or > 4)
        {
            throw new JgsRuntimeException(line, col, "text expects (x, y, string) or (x, y, z, string).");
        }

        bool threeD = data.Count == 4;
        double x = Num("text", data, 0, line, col);
        double y = Num("text", data, 1, line, col);
        double z = threeD ? Num("text", data, 2, line, col) : 0;
        string label = Str("text", data, threeD ? 3 : 2, line, col);

        TextAnnotation annotation = JG.Text(x, y, z, label);
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
            }
        }

        // A text is an annotation rather than a series, so it does not go through Handle — but a
        // handle is a handle, and set(t, 'String', …) has to reach it like anything else.
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
            case "displayname":
                plot.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                break;
        }
    }

    // --- shared argument reading --------------------------------------------------------------

    /// <summary>
    /// Splits trailing 'Name', value pairs off an argument list. Options begin at the first string
    /// that names one — the same rule <c>plot</c> uses, so a spec string or a color word before them
    /// stays data.
    /// </summary>
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
    /// does, and a numeric triple does when its components are all in [0, 1] — which is the RGB range
    /// MATLAB uses. The one thing this cannot tell apart is a three-element data vector whose values
    /// happen to lie in [0, 1]; pass those through <c>'CData'</c>.
    /// </summary>
    private static bool IsSingleColor(JgsValue value, int points)
    {
        if (value.Type == JgsType.String)
        {
            return true;
        }

        if (value.Type != JgsType.Array || value.ArrayLength != 3 || points == 3)
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
    /// null reference rather than a friendly error — which is exactly what <c>scatter3(x, y, z, 36)</c>
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
