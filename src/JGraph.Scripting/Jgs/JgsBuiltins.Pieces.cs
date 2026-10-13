using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The charts R2025b builds out of several objects, built that way in the MATLAB dialect (open item
/// 112): a pie is a patch and a text per slice, a raised pie a patch, a surface, a patch and a text,
/// <c>bar3</c> a surface per column, <c>compass</c> and <c>feather</c> a line per arrow (and
/// <c>feather</c>'s baseline), <c>fill</c> a patch per polygon, <c>errorbar</c> a series per column,
/// and <c>meshc</c> and <c>surfc</c> a surface and a contour on the floor. The geometry is R2025b's
/// own (<c>pie.m</c>, <c>pie3.m</c>, <c>makebars.m</c>, <c>compass.m</c>, <c>feather.m</c>), so what a
/// script reads out of <c>h(2).XData</c> is what it would read there. JGS keeps its one-object
/// charts, and this file is never entered from it.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The handles of several objects as a row, which is the shape R2025b's <c>pie</c>, <c>pie3</c>,
    /// <c>bar3</c> and <c>errorbar</c> answer. Not <c>HandleRow</c>: the statistics plots' helper of
    /// that name answers a column, and a list of plots resolves to it first.
    /// </summary>
    private static JgsValue PieceRow(IReadOnlyList<GraphObject> objects)
    {
        if (objects.Count == 1)
        {
            return JgsHandleRegistry.For(objects[0]);
        }

        var handles = new double[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            handles[i] = JgsHandleRegistry.For(objects[i]).AsNumber;
        }

        return JgsMatrix.FromColumnMajor(handles, 1, objects.Count);
    }

    /// <summary>
    /// Runs a builder that adds several objects to one axes: the first object replaces the axes'
    /// contents unless the axes holds, the rest are added, and the hold state is put back after.
    /// </summary>
    private static T AddingPieces<T>(Func<T> body)
    {
        bool holding = JG.IsHolding;
        try
        {
            return body();
        }
        finally
        {
            JG.Hold(holding);
        }
    }

    // --- pie and pie3 ---------------------------------------------------------------------------

    /// <summary>What the pie verbs take after their data, every one of them a JGraph extra R2025b's <c>pie</c> does not read.</summary>
    private sealed class PieExtras
    {
        public Color? EdgeColor;
        public double? LineWidth;
        public double? FaceAlpha;
        public double StartAngle = 90;
        public bool Clockwise;
        public bool ShowLabels = true;
        public double? LabelRadius;
        public double? Height;
        public Colormap? Colormap;
        public string? DisplayName;
    }

    private static PieExtras ReadPieExtras(
        string verb, IReadOnlyList<JgsValue> args, int start, IReadOnlyList<string> names, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        var extras = new PieExtras();
        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf(verb, args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "edgecolor":
                    extras.EdgeColor = OptionColor(value, line, col, verb);
                    break;
                case "linewidth":
                    extras.LineWidth = NumOf($"{verb}: LineWidth", value, line, col);
                    break;
                case "facealpha":
                    extras.FaceAlpha = NumOf($"{verb}: FaceAlpha", value, line, col);
                    break;
                case "startangle":
                    extras.StartAngle = NumOf($"{verb}: StartAngle", value, line, col);
                    break;
                case "clockwise":
                    extras.Clockwise = JgsGraphicsProperties.ToOnOff($"{verb}: Clockwise", value, line, col);
                    break;
                case "height" when verb == "pie3":
                    extras.Height = NumOf($"{verb}: Height", value, line, col);
                    break;
                case "showlabels":
                    extras.ShowLabels = JgsGraphicsProperties.ToOnOff($"{verb}: ShowLabels", value, line, col);
                    break;
                case "labelradius":
                    extras.LabelRadius = NumOf($"{verb}: LabelRadius", value, line, col);
                    break;
                case "colormap":
                    extras.Colormap = OptionColormap(verb, value, line, col);
                    break;
                case "displayname":
                    extras.DisplayName = StrOf($"{verb}: DisplayName", value, line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{verb} has no option '{name}'. It takes {string.Join(", ", names)}.");
            }
        }

        return extras;
    }

    /// <summary>
    /// Reads <c>pie(x)</c>, <c>pie(x, explode)</c>, <c>pie(x, labels)</c>, <c>pie(x, explode, labels)</c>
    /// and <c>pie(x, format)</c> as R2025b reads them: a negative share is dropped with
    /// <c>MATLAB:pie:NegativeData</c> (all of them negative is refused), explode and labels have to
    /// count the shares, and a char row is a printf format for the percentages. The JGraph option
    /// pairs may follow. Then draws the slices.
    /// </summary>
    private static JgsValue MatlabPieVerb(
        string verb,
        IReadOnlyList<JgsValue> rest,
        JGraphScriptGlobals host,
        IReadOnlyList<string> optionNames,
        int line,
        int col)
    {
        if (rest[0].Type is not (JgsType.Array or JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pie:NonNumericData",
                "Must have numeric data in the pie chart.");
        }

        double[] given = ToDoubles(verb, rest[0], line, col);
        if (given.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} needs at least one value.");
        }

        if (given.Any(static v => !double.IsFinite(v)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pie:NonFiniteData",
                "All data must be finite in pie chart.");
        }

        if (given.All(static v => v < 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pie:NoNonNegatveData",
                "At least 1 value must be nonnegative.");
        }

        // The two optional positions are told apart by type: numbers explode, a cell or a string
        // array labels, a char row formats — unless it is one of the option names, which starts the
        // JGraph extras.
        double[]? explodeFlags = null;
        string[]? labels = null;
        string? format = null;
        int at = 1;
        while (at < rest.Count && at <= 2)
        {
            JgsValue next = rest[at];
            if (next.Type == JgsType.String)
            {
                if (optionNames.Contains(next.AsString, StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }

                if (labels is not null || format is not null)
                {
                    break;
                }

                // A char row with a conversion in it formats the percentages; without one it is a
                // single label, which only a one-slice pie can take.
                if (next.AsString.Contains('%'))
                {
                    format = next.AsString;
                }
                else if (given.Length == 1)
                {
                    labels = [next.AsString];
                }
                else
                {
                    throw LabelsMismatch();
                }
            }
            else if (IsTextList(next))
            {
                if (labels is not null || format is not null)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb} takes {verb}(x), {verb}(x, explode), {verb}(x, labels), or {verb}(x, explode, labels).");
                }

                JgsValue[] elements = next.Type == JgsType.Cell ? next.AsCell : next.BoxedElements();
                if (elements.Length != given.Length || elements.Any(static e => e.Type != JgsType.String))
                {
                    throw LabelsMismatch();
                }

                labels = elements.Select(static e => e.AsString).ToArray();
            }
            else if (next.Type is JgsType.Array or JgsType.Number or JgsType.Bool)
            {
                if (explodeFlags is not null || labels is not null || format is not null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:pie:InvalidExplodeTypeNumeric",
                        "Specify exploded slices with a logical or numeric vector.");
                }

                explodeFlags = ToDoubles(verb, next, line, col);
                if (explodeFlags.Length != given.Length)
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:ExplodeLengthMismatch",
                        "X and EXPLODE must be the same length.");
                }
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} takes {verb}(x), {verb}(x, explode), {verb}(x, labels), or {verb}(x, explode, labels).");
            }

            at++;
        }

        int[] kept = Enumerable.Range(0, given.Length).Where(i => given[i] >= 0).ToArray();
        if (kept.Length < given.Length)
        {
            Warn(host, "MATLAB:pie:NegativeData", "Ignoring negative data.");
        }

        double[] values = kept.Select(i => given[i]).ToArray();
        bool[] exploded = kept.Select(i => explodeFlags is not null && explodeFlags[i] != 0).ToArray();
        string[]? words = labels is null ? null : kept.Select(i => labels[i]).ToArray();
        if (words is null && format is not null)
        {
            double total = values.Sum();
            double[] shares = total > 1 + Math.Sqrt(2.220446049250313e-16)
                ? values.Select(v => v / total).ToArray()
                : values;
            words = shares.Select(share => share < 0.01 && share != 0
                ? "< 1%"
                : JgsSprintf.FormatMatlab(format, [JgsValue.Number(share * 100)])).ToArray();
        }

        return MatlabPie(verb, values, exploded, words, rest, at, optionNames, line, col);

        JgsRuntimeException LabelsMismatch() => new(line, col, $"MATLAB:{verb}:StringLengthMismatch",
            verb == "pie3"
                ? "Cell array of character vectors must be the same length as X."
                : "Labels must have the same number of elements as X or be a valid format string.");
    }

    /// <summary>
    /// R2025b's <c>pie</c> and <c>pie3</c>: the slices as <c>pie.m</c> and <c>pie3.m</c> draw them — a
    /// fan of <c>max(1, ceil(100 x))</c> arc points per slice from the top, counter-clockwise, each
    /// slice a patch whose <c>CData</c> is its number so the colormap colours it, a text at the
    /// slice's middle angle, and for the raised pie a bottom patch, a side surface and a top patch
    /// 0.35 high. The handles come back in a row, patch then text (patch, surface, patch, text).
    /// The JGraph extras (a start angle, a direction, a label radius, a colormap, …) still shape the
    /// slices at construction.
    /// </summary>
    private static JgsValue MatlabPie(
        string verb,
        double[] values,
        bool[] exploded,
        string[]? labels,
        IReadOnlyList<JgsValue> args,
        int optionStart,
        IReadOnlyList<string> optionNames,
        int line,
        int col)
    {
        bool raised = verb == "pie3";
        PieExtras extras = ReadPieExtras(verb, args, optionStart, optionNames, line, col);

        // pie.m: shares over one are normalised, a total under one leaves a gap.
        double[] x = (double[])values.Clone();
        double total = x.Sum();
        if (total > 1 + Math.Sqrt(2.220446049250313e-16))
        {
            for (int i = 0; i < x.Length; i++)
            {
                x[i] /= total;
            }
        }

        double theta0 = extras.StartAngle * Math.PI / 180;
        double turn = extras.Clockwise ? -1 : 1;
        double labelRadius = extras.LabelRadius ?? (raised ? 1.25 : 1.1);
        double height = extras.Height ?? 0.35;
        const int MaxPoints = 100;
        const double ExplodeRadius = 0.1;
        int count = x.Length;

        return AddingPieces(() =>
        {
            var made = new List<GraphObject>();
            bool holding = JG.IsHolding;
            for (int i = 0; i < count; i++)
            {
                int n = Math.Max(1, (int)Math.Ceiling(MaxPoints * x[i]));
                var xx = new double[n + 3];
                var yy = new double[n + 3];
                for (int k = 0; k <= n; k++)
                {
                    double theta = theta0 + (turn * x[i] * k / n * 2 * Math.PI);
                    xx[k + 1] = Math.Cos(theta);
                    yy[k + 1] = Math.Sin(theta);
                }

                double centreTheta = theta0 + (turn * x[i] * Math.PI);
                double xText = labelRadius * Math.Cos(centreTheta);
                double yText = labelRadius * Math.Sin(centreTheta);
                if (exploded[i])
                {
                    double dx = ExplodeRadius * Math.Cos(centreTheta);
                    double dy = ExplodeRadius * Math.Sin(centreTheta);
                    xText += dx;
                    yText += dy;
                    for (int k = 0; k < xx.Length; k++)
                    {
                        xx[k] += dx;
                        yy[k] += dy;
                    }
                }

                theta0 += turn * x[i] * 2 * Math.PI;

                string label = labels is not null ? labels[i]
                    : x[i] < 0.01 && x[i] != 0 ? "< 1%"
                    : raised
                        ? Math.Round(x[i] * 100, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
                        : JgsSprintf.FormatMatlab("%.0f%%", [JgsValue.Number(x[i] * 100)]);

                PatchPlot bottom = Slice(xx, yy, 0, i);
                made.Add(bottom);
                if (raised)
                {
                    SurfacePlot side = SliceSide(xx, yy, height, i);
                    PatchPlot top = Slice(xx, yy, height, i);
                    made.Add(side);
                    made.Add(top);
                    if (labels is not null)
                    {
                        SetDisplayName(side, labels[i]);
                    }
                }
                else if (labels is not null)
                {
                    SetDisplayName(bottom, labels[i]);
                }

                if (extras.ShowLabels)
                {
                    // pie3.m lowers a label in the near-front quarter so it clears the slices; the
                    // numbers are tied to view(3) and only apply when the pie owns its axes.
                    double zText = raised && !(yText < 0.3 && xText < 0.8 && !holding) ? height : 0;
                    TextAnnotation text = JG.Text(xText, yText, zText, label);
                    if (raised)
                    {
                        text.HorizontalAlignment = HorizontalAlignment.Center;
                    }
                    else
                    {
                        (text.HorizontalAlignment, text.VerticalAlignment) = AlignmentFromAngle(centreTheta);
                    }

                    made.Add(text);
                }
            }

            AxesModel axes = JG.Gca();
            if (!holding)
            {
                // pie.m: axis equal off [-1.2 1.2 -1.2 1.2] on a fresh axes, whose Box is off
                // (probe_box); pie3.m adds view(3).
                axes.EqualAspect = true;
                axes.Visible = false;
                axes.FrameVisible = false;
                JG.XLim(-1.2, 1.2);
                JG.YLim(-1.2, 1.2);
                if (raised)
                {
                    axes.Is3D = true;
                    JG.View(-37.5, 30);
                }
            }

            if (extras.Colormap is { } map)
            {
                axes.Colormap = map;
                foreach (GraphObject piece in made)
                {
                    if (piece is PatchPlot patch) patch.Colormap = map;
                    if (piece is SurfacePlot surface) surface.Colormap = map;
                }
            }

            return PieceRow(made);

            PatchPlot Slice(double[] sx, double[] sy, double z, int index)
            {
                var sz = new double[sx.Length];
                Array.Fill(sz, z);
                var face = new int[sx.Length];
                for (int k = 0; k < face.Length; k++)
                {
                    face[k] = k;
                }

                PatchPlot patch = JG.Patch(sx, sy, sz, [face]);
                patch.IsFlat = !raised; // a pie's wedge lies in the plane; a pie3's stands (open item 114)
                JG.Hold(true);
                var colour = new double[sx.Length];
                Array.Fill(colour, index + 1);
                patch.ColorData = colour;
                patch.Shading = PatchShading.Flat;
                patch.AutoScaleColor = false;
                patch.ColorMin = 1;
                patch.ColorMax = Math.Max(2, count);
                if (extras.EdgeColor is { } edge) patch.EdgeColor = edge;
                if (extras.LineWidth is { } width) patch.EdgeWidth = width;
                if (extras.FaceAlpha is { } alpha) patch.FaceAlpha = alpha;
                if (extras.DisplayName is { } name) SetDisplayName(patch, name);
                return patch;
            }

            SurfacePlot SliceSide(double[] sx, double[] sy, double z, int index)
            {
                int rows = sx.Length;
                var gx = new double[rows, 2];
                var gy = new double[rows, 2];
                var gz = new double[rows, 2];
                var colour = new double[rows, 2];
                for (int r = 0; r < rows; r++)
                {
                    gx[r, 0] = gx[r, 1] = sx[r];
                    gy[r, 0] = gy[r, 1] = sy[r];
                    gz[r, 1] = z;
                    colour[r, 0] = colour[r, 1] = index + 1;
                }

                SurfacePlot side = JG.Surf(gx, gy, gz);
                JG.Hold(true);
                side.CData = colour;
                side.AutoScaleColor = false;
                side.ColorMin = 1;
                side.ColorMax = Math.Max(2, count);
                if (extras.EdgeColor is { } edge) side.EdgeColor = edge;
                if (extras.FaceAlpha is { } alpha) side.FaceAlpha = alpha;
                if (extras.DisplayName is { } name) SetDisplayName(side, name);
                return side;
            }
        });
    }

    /// <summary>pie.m's label alignment: the angle rounded to 45 degrees says which side of the point the text hangs on.</summary>
    private static (HorizontalAlignment Horizontal, VerticalAlignment Vertical) AlignmentFromAngle(double radians)
    {
        double degrees = radians * 180 / Math.PI;
        double angle = Math.Round(degrees / 45, MidpointRounding.AwayFromZero) * 45 % 360;
        if (angle < 0)
        {
            angle += 360;
        }

        HorizontalAlignment horizontal = angle is 90 or 270 ? HorizontalAlignment.Center
            : angle > 90 && angle < 270 ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        VerticalAlignment vertical = angle is 0 or 180 ? VerticalAlignment.Middle
            : angle > 180 && angle < 360 ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        return (horizontal, vertical);
    }

    // --- bar3 and bar3h -------------------------------------------------------------------------

    /// <summary>
    /// R2025b's <c>bar3</c>: a surface per column of heights, each box six rows of four vertices
    /// with NaN rows and columns between the boxes, laid out as <c>makebars.m</c> lays them out —
    /// detached columns along x a unit apart, grouped ones side by side at one x, stacked ones on
    /// top of each other — and coloured by the column's number through the colormap. The axes
    /// counts its rows downwards (<c>YDir</c> reverse) and looks from MATLAB's corner.
    /// </summary>
    private static JgsValue MatlabBar3(
        string verb,
        bool horizontal,
        double[,] heights,
        double[]? positions,
        double? width,
        Bar3DStyle style,
        Color? face,
        IReadOnlyList<JgsValue> args,
        int optionStart,
        int line,
        int col)
    {
        if ((args.Count - optionStart) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        int n = heights.GetLength(0);
        int m = heights.GetLength(1);
        double barWidth = width ?? 0.8;
        int plotType = style switch { Bar3DStyle.Grouped => 0, Bar3DStyle.Stacked => 1, _ => 2 };
        double baseValue = 0;
        var later = new List<(string Name, JgsValue Value)>();
        for (int i = optionStart; i < args.Count; i += 2)
        {
            string name = StrOf(verb, args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "barwidth":
                    barWidth = NumOf($"{verb}: BarWidth", value, line, col);
                    break;
                case "style":
                    plotType = ParseBar3Style(verb, StrOf($"{verb}: Style", value, line, col), line, col) switch
                    {
                        Bar3DStyle.Grouped => 0,
                        Bar3DStyle.Stacked => 1,
                        _ => 2,
                    };
                    break;
                case "horizontal":
                    horizontal = JgsGraphicsProperties.ToOnOff($"{verb}: Horizontal", value, line, col);
                    break;
                case "basevalue":
                    baseValue = NumOf($"{verb}: BaseValue", value, line, col);
                    break;
                case "facecolor" or "edgecolor" or "facealpha" or "linewidth" or "colormap" or "displayname":
                    later.Add((name, value));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{verb} has no option '{name}'. It takes {string.Join(", ", Bar3OptionNames)}.");
            }
        }

        // makebars.m sorts the positions and takes the rows with them.
        double[] y = positions is null ? Counting(n) : (double[])positions.Clone();
        int[] order = Enumerable.Range(0, n).OrderBy(i => y[i]).ToArray();
        y = order.Select(i => y[i]).ToArray();
        var z = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double h = heights[order[i], j];
                z[i, j] = double.IsFinite(h) ? h : 0;
            }
        }

        double groupWidth = m == 1 || plotType != 0 ? 1 : Math.Min(0.8, m / (m + 1.5));
        int nn = 6 * n;
        var xx = new double[nn, 4];
        for (int r = 0; r < nn; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                xx[r, c] = ((c >= 2 ? 1 : 0) - 0.5) * barWidth * groupWidth / (plotType == 0 ? m : 1);
            }
        }

        // Each box is six rows of four: its floor everywhere, its height in rows 2 and 3 of columns
        // 2 and 3; a stacked column's whole box rides on the columns before it.
        var zz = new double[nn, 4 * m];
        var yy = new double[nn, 4 * m];
        for (int i = 0; i < n; i++)
        {
            double stacked = 0;
            for (int j = 0; j < m; j++)
            {
                double floor = baseValue + (plotType == 1 ? stacked : 0);
                for (int r = 0; r < 6; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        zz[(6 * i) + r, (4 * j) + c] = floor;
                    }
                }

                zz[(6 * i) + 1, (4 * j) + 1] = zz[(6 * i) + 1, (4 * j) + 2] = floor + z[i, j];
                zz[(6 * i) + 2, (4 * j) + 1] = zz[(6 * i) + 2, (4 * j) + 2] = floor + z[i, j];
                stacked += z[i, j];
            }
        }

        // Where each box starts along y and how wide its slot is (t and delta), per row and column.
        var t = new double[n, m];
        var delta = new double[n, m];
        bool uniform = n >= 2 && Enumerable.Range(0, n - 2).All(i => Math.Abs(y[i + 2] - (2 * y[i + 1]) + y[i]) < 2.220446049250313e-16 * Math.Abs(y.Max()));
        double span = y.Max() - y.Min();
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                if (n < 2 || span == 0)
                {
                    delta[i, j] = 1;
                    t[i, j] = y[i] - 0.5;
                }
                else if (uniform)
                {
                    if (plotType != 0)
                    {
                        delta[i, j] = span * groupWidth / (n - 1);
                        t[i, j] = y[i] - (0.5 * delta[i, j]);
                    }
                    else
                    {
                        delta[i, j] = span * groupWidth / (n - 1) / m;
                        t[i, j] = y[i] - (0.5 * delta[i, j] * m) + (j * delta[i, j]);
                    }
                }
                else
                {
                    double dy = Enumerable.Range(0, n - 1).Select(k => y[k + 1] - y[k]).Min();
                    if (plotType != 0)
                    {
                        delta[i, j] = dy * groupWidth;
                        t[i, j] = y[i] - (delta[i, j] / 2);
                    }
                    else
                    {
                        delta[i, j] = dy * groupWidth / m;
                        t[i, j] = y[i] - (delta[i, j] / 2 * m) + (j * delta[i, j]);
                    }
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double near = t[i, j] + ((1 - barWidth) / 2 * delta[i, j]);
                double far = t[i, j] + ((1 + barWidth) / 2 * delta[i, j]);
                for (int c = 0; c < 4; c++)
                {
                    yy[6 * i, (4 * j) + c] = near;
                    yy[(6 * i) + 1, (4 * j) + c] = near;
                    yy[(6 * i) + 2, (4 * j) + c] = far;
                    yy[(6 * i) + 3, (4 * j) + c] = far;
                    yy[(6 * i) + 4, (4 * j) + c] = near;
                }
            }
        }

        // The NaNs that keep the boxes apart: the outer columns of rows 1, 4, 5 and 6 of each box
        // and the inner columns of row 6.
        for (int i = 0; i < n; i++)
        {
            foreach (int r in new[] { 6 * i, (6 * i) + 3, (6 * i) + 4, (6 * i) + 5 })
            {
                xx[r, 0] = xx[r, 3] = double.NaN;
                for (int j = 0; j < m; j++)
                {
                    yy[r, 4 * j] = yy[r, (4 * j) + 3] = double.NaN;
                    zz[r, 4 * j] = zz[r, (4 * j) + 3] = double.NaN;
                }
            }

            int last = (6 * i) + 5;
            xx[last, 1] = xx[last, 2] = double.NaN;
            for (int j = 0; j < m; j++)
            {
                yy[last, (4 * j) + 1] = yy[last, (4 * j) + 2] = double.NaN;
                zz[last, (4 * j) + 1] = zz[last, (4 * j) + 2] = double.NaN;
            }
        }

        return AddingPieces(() =>
        {
            bool holding = JG.IsHolding;
            var made = new List<GraphObject>();
            for (int j = 0; j < m; j++)
            {
                double xAt = plotType == 2 ? j + 1 : 1;
                var gx = new double[nn, 4];
                var gy = new double[nn, 4];
                var gz = new double[nn, 4];
                var colour = new double[nn, 4];
                for (int r = 0; r < nn; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        gx[r, c] = xx[r, c] + xAt;
                        gy[r, c] = yy[r, (4 * j) + c];
                        gz[r, c] = zz[r, (4 * j) + c];
                        colour[r, c] = j + 1;
                    }
                }

                SurfacePlot surface = horizontal ? JG.Surf(gx, gz, gy) : JG.Surf(gx, gy, gz);
                JG.Hold(true);
                surface.CData = colour;
                surface.AutoScaleColor = false;
                surface.ColorMin = 1;
                surface.ColorMax = Math.Max(2, m);
                surface.Name = "Bar3";
                surface.Tag = "bar3";
                if (face is { } chosen)
                {
                    surface.FaceColor = chosen;
                }

                JgsHandleEntry entry = JgsHandleRegistry.EntryFor(surface);
                foreach ((string name, JgsValue value) in later)
                {
                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                }

                made.Add(surface);
            }

            AxesModel axes = JG.Gca();
            if (!holding)
            {
                // bar3.m: integer row positions become the ticks, the columns the x ticks, the
                // limits hug the columns, the rows count downwards and the camera sits at view(3).
                if (n < 16 && y.All(v => v == Math.Floor(v)))
                {
                    (horizontal ? axes.ZAxis : axes.PrimaryYAxis).TickPositions = y.Distinct().ToArray();
                }

                double[] columns = Enumerable.Range(0, m).Select(j => plotType == 2 ? j + 1.0 : 1.0).Distinct().ToArray();
                IReadOnlyList<double>? ticks = columns.Length < 2 ? Array.Empty<double>() : columns.Length <= 16 ? columns : null;
                axes.PrimaryXAxis.TickPositions = ticks;
                double reach = plotType == 0 ? barWidth / m / 2 : barWidth / 2;
                JG.XLim(1 - reach, columns.Max() + reach);
                axes.PrimaryYAxis.Inverted = true;
                axes.Is3D = true;
                JG.View(-37.5, 30);
                axes.Grid.ShowMajor = true;
            }

            return PieceRow(made);
        });
    }

    // --- compass and feather --------------------------------------------------------------------

    /// <summary>
    /// R2025b's <c>compass</c> and <c>feather</c>: a line per arrow, each the five points of
    /// <c>compass.m</c>'s arrow (<c>[0 1 .8 1 .8]</c> along, <c>[0 0 .08 0 -.08]</c> across) scaled
    /// and turned by the reading, as an angle and a radius on the polar axes for <c>compass</c>, and
    /// with a NaN between shaft and head, shifted a sample along, plus a baseline for
    /// <c>feather</c>. The arrows share one colour-order seat, as R2025b's do.
    /// </summary>
    private static JgsValue MatlabArrowLines(
        string verb, double[] u, double[] v, IReadOnlyList<JgsValue> tail, int line, int col)
    {
        string? spec = null;
        int at = 0;
        if (tail.Count > 0 && tail[0].Type == JgsType.String && IsLineSpecWord(tail[0].AsString)
            && !QuiverOptionNames.Contains(tail[0].AsString) && !LineOptionNames.Contains(tail[0].AsString))
        {
            spec = tail[0].AsString;
            at = 1;
        }

        if ((tail.Count - at) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} takes its components, then a line spec and name/value options.");
        }

        bool heads = true;
        var options = new List<(string Name, JgsValue Value)>();
        for (int i = at; i < tail.Count; i += 2)
        {
            string name = StrOf(verb, tail[i], line, col);
            switch (name.ToLowerInvariant())
            {
                case "showarrowhead":
                    heads = JgsGraphicsProperties.ToOnOff($"{verb}: ShowArrowHead", tail[i + 1], line, col);
                    break;
                // The arrows are the readings and were never scaled, so the scaling words change nothing.
                case "autoscale" or "autoscalefactor" or "maxheadsize":
                    break;
                default:
                    options.Add((name, tail[i + 1]));
                    break;
            }
        }

        bool feather = verb == "feather";
        double[] along = heads ? [0, 1, 0.8, 1, 0.8] : [0, 1];
        double[] across = heads ? [0, 0, 0.08, 0, -0.08] : [0, 0];
        if (feather && heads)
        {
            along = [0, 1, double.NaN, 0.8, 1, 0.8];
            across = [0, 0, double.NaN, 0.08, 0, -0.08];
        }

        return AddingPieces(() =>
        {
            var made = new List<LinePlot>();
            for (int k = 0; k < u.Length; k++)
            {
                var xs = new double[along.Length];
                var ys = new double[along.Length];
                for (int p = 0; p < along.Length; p++)
                {
                    // (along + i across) * (u + i v)
                    double re = (along[p] * u[k]) - (across[p] * v[k]);
                    double im = (along[p] * v[k]) + (across[p] * u[k]);
                    if (feather)
                    {
                        xs[p] = re + k + 1;
                        ys[p] = im;
                    }
                    else
                    {
                        xs[p] = Math.Atan2(im, re);
                        ys[p] = Math.Sqrt((re * re) + (im * im));
                    }
                }

                made.Add(JG.Plot(xs, ys, spec));
                JG.Hold(true);
            }

            if (feather)
            {
                made.Add(JG.Plot([1, u.Length], [0, 0], spec));
            }

            if (made.Count > 0)
            {
                // Every arrow sits on the first one's seat. feather.m then hands the next plot the
                // seat after it; compass.m leaves the count where its lines took it (probe_112c:
                // a second compass on hold takes the third colour).
                foreach (LinePlot arrow in made)
                {
                    if (arrow.Color is null)
                    {
                        SeatSeries(arrow);
                    }
                }

                int seat = made[0].SeriesIndex;
                foreach (LinePlot arrow in made)
                {
                    arrow.SeriesIndex = seat;
                    JgsHandleEntry entry = JgsHandleRegistry.EntryFor(arrow);
                    foreach ((string name, JgsValue value) in options)
                    {
                        JgsGraphicsProperties.Set(entry, name, value, line, col);
                    }
                }

                if (feather && seat >= 0)
                {
                    JG.Gca().NextSeriesIndex = seat + 1;
                }
            }

            return HandlesFor(made);
        });
    }

    // --- fill and fill3 -------------------------------------------------------------------------

    /// <summary>
    /// R2025b's <c>fill</c> and <c>fill3</c>: a patch per polygon — one per column of a matrix, and
    /// one per <c>(x, y, c)</c> group — coloured by a colour, by one value each out of a vector, or
    /// by a column of values per vertex. The first replaces the axes unless it holds; the rest add.
    /// </summary>
    private static JgsValue MatlabFillPolygons(
        string verb,
        IReadOnlyList<JgsValue> data,
        List<(string Name, JgsValue Value)> options,
        bool threeD,
        int line,
        int col)
    {
        int needed = threeD ? 4 : 3;
        if (data.Count == 0 || data.Count % needed != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} expects {(threeD ? "(x, y, z, c)" : "(x, y, c)")}.");
        }

        return AddingPieces(() =>
        {
            var made = new List<PatchPlot>();
            bool lifted = false;
            for (int g = 0; g < data.Count; g += needed)
            {
                double[][] xs = Polygons(verb, data, g, line, col);
                double[][] ys = Polygons(verb, data, g + 1, line, col);
                double[][] zs = threeD
                    ? Polygons(verb, data, g + 2, line, col)
                    : xs.Select(static column => new double[column.Length]).ToArray();
                JgsValue colour = data[g + needed - 1];
                int polygons = Math.Max(xs.Length, Math.Max(ys.Length, zs.Length));
                for (int p = 0; p < polygons; p++)
                {
                    double[] px = Pick(xs, p), py = Pick(ys, p), pz = Pick(zs, p);
                    if (px.Length != py.Length || px.Length != pz.Length)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{verb}: polygon {p + 1} has {px.Length}, {py.Length} and {pz.Length} coordinates; they must match.");
                    }

                    var face = new int[px.Length];
                    for (int k = 0; k < face.Length; k++)
                    {
                        face[k] = k;
                    }

                    PatchPlot patch;
                    try
                    {
                        patch = JG.Patch(px, py, pz, [face]);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new JgsRuntimeException(line, col, ex.Message);
                    }

                    patch.IsFlat = !threeD; // fill's polygons lie in the plane (open item 114)
                    JG.Hold(true);
                    lifted |= pz.Any(z => double.IsFinite(z) && z != 0);
                    ColourPolygon(verb, patch, colour, p, polygons, line, col);
                    ApplyPatchOptions(verb, patch, options, line, col);
                    made.Add(patch);
                }
            }

            if (threeD && lifted)
            {
                JG.Gca().Is3D = true;
            }

            return HandlesFor(made);
        });
    }

    /// <summary>
    /// One polygon's share of a <c>fill</c> colour argument: a colour is every polygon's; a vector
    /// with one value per polygon gives this one its value; a matrix with a column per polygon gives
    /// it a value per vertex; a vector the length of its vertices is a value per vertex.
    /// </summary>
    private static void ColourPolygon(
        string verb, PatchPlot patch, JgsValue colour, int index, int polygons, int line, int col)
    {
        int vertices = patch.X.Count;
        if (IsSingleColor(colour, vertices) && !(polygons > 1 && colour.Type == JgsType.Array && colour.ArrayLength == polygons))
        {
            patch.FaceColor = OptionColor(colour, line, col, verb);
            return;
        }

        if (colour.Type == JgsType.String)
        {
            patch.FaceColor = OptionColor(colour, line, col, verb);
            return;
        }

        int rows = JgsMatrix.RowCount(colour);
        int cols = JgsMatrix.ColCount(colour);
        double[] values = Numbers($"{verb}: colors", colour, line, col);
        if (rows > 1 && cols > 1 && cols == polygons && rows == vertices)
        {
            var column = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                column[r] = values[(index * rows) + r];
            }

            patch.ColorData = column;
            patch.Shading = PatchShading.Interp;
            return;
        }

        if (values.Length == polygons)
        {
            patch.ColorData = [values[index]];
            patch.Shading = PatchShading.Flat;
            return;
        }

        if (values.Length == vertices)
        {
            patch.ColorData = values;
            patch.Shading = PatchShading.Interp;
            return;
        }

        throw new JgsRuntimeException(line, col,
            $"{verb}: the color argument has {values.Length} values, but there are {polygons} polygons of {vertices} vertices.");
    }

    // --- meshc and surfc ------------------------------------------------------------------------

    /// <summary>
    /// R2025b's <c>meshc</c> and <c>surfc</c>: the surface, then a contour of the same grid added to
    /// the axes with its curves on the floor (<c>ZLocation</c> 'zmin') and R2025b's round levels;
    /// the handles come back as a column, surface first. A surface over a grid that is not a mesh of
    /// two vectors has no height field to trace, so it comes back alone.
    /// </summary>
    private static JgsValue MatlabSurfaceWithContour(
        string name, bool wireframe, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[]? gridX = null;
        double[]? gridY = null;
        double[,]? field = null;

        JgsValue surface = Surface3D(name, args, line, col,
            (x, y, z) =>
            {
                (gridX, gridY, field) = (x, y, z);
                return wireframe ? JG.Mesh(x, y, z) : JG.Surf(x, y, z);
            },
            z =>
            {
                (gridX, gridY, field) = (Counting(z.GetLength(1)), Counting(z.GetLength(0)), z);
                return wireframe ? JG.Mesh(z) : JG.Surf(z);
            },
            (x, y, z) => wireframe ? JG.Mesh(x, y, z) : JG.Surf(x, y, z));

        if (field is null || gridX is null || gridY is null
            || JgsHandleRegistry.Require(surface, line, col).Target is not PlotObject drawn)
        {
            return surface;
        }

        ContourPlot floor = AddingPieces(() =>
        {
            JG.Hold(true);
            ContourPlot contour = JG.Contour(gridX, gridY, field);
            contour.RoundLevels = true;
            contour.ZLocation = ContourZLocation.Min;
            contour.XImplied = drawn is SurfacePlot { XImplied: true };
            contour.YImplied = drawn is SurfacePlot { YImplied: true };
            return contour;
        });

        return JgsGraphicsProperties.HandleColumn([drawn, floor]);
    }
}
