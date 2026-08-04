using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Reading and writing a figure object's properties through a handle — <c>p.Color</c>,
/// <c>p.Visible = 'off'</c>, <c>lgd.ItemHitFcn = @cb</c>. The dot is the only way a script touches a
/// figure object it already made; everything else goes through a verb.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Declares the <c>graphics</c> name so MATLAB's preallocation idiom parses and runs:
    /// <c>graphics.primitive.Line.empty(n, 0)</c> is a class path, and here it is a nest of structs
    /// ending in a builtin. The answer is an empty row rather than an n-by-0 array, because growing
    /// a row is what the loop that follows the preallocation actually does.
    /// </summary>
    private static void RegisterGraphicsNamespace(JgsEnvironment env)
    {
        var empty = JgsValue.Function(new BuiltinFunction(
            "graphics.primitive.Line.empty", static (_, _, _) => JgsValue.Array([])));

        JgsValue Namespace(string field, JgsValue inner) =>
            JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { [field] = inner });

        JgsValue line = Namespace("empty", empty);
        env.Declare("graphics", Namespace("primitive", Namespace("Line", line)));
    }

    // --- Aiming a verb at a named axes ----------------------------------------------------------

    /// <summary>
    /// Splits a leading axes handle off an argument list. Every drawing verb takes one — MATLAB's
    /// <c>plot(ax, x, y)</c>, <c>title(ax, '…')</c>, <c>hold(ax, 'off')</c> — and it always sits first.
    /// </summary>
    internal static (AxesModel? Axes, IReadOnlyList<JgsValue> Remaining) PeelAxes(IReadOnlyList<JgsValue> args)
    {
        if (args.Count > 0
            && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
            && entry.Kind == JgsHandleKind.Axes)
        {
            var rest = new JgsValue[args.Count - 1];
            for (int i = 1; i < args.Count; i++)
            {
                rest[i - 1] = args[i];
            }

            return ((AxesModel)entry.Target, rest);
        }

        return (null, args);
    }

    /// <summary>
    /// Runs a verb against <paramref name="axes"/> and puts the previous current axes back, because
    /// naming an axes in a call does not make it the one an unqualified verb would find next.
    /// </summary>
    internal static JgsValue OnAxes(AxesModel? axes, Func<JgsValue> body)
    {
        if (axes is null)
        {
            return body();
        }

        AxesModel? previous = JG.CurrentAxesOrNull;
        JG.MakeCurrent(axes);
        try
        {
            return body();
        }
        finally
        {
            if (previous is not null && !ReferenceEquals(previous, axes))
            {
                JG.MakeCurrent(previous);
            }
        }
    }

    /// <summary>The plot objects a handle or an array of handles names, in the order given.</summary>
    internal static List<PlotObject> PlotsOf(string verb, JgsValue value, int line, int col)
    {
        var plots = new List<PlotObject>();
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            JgsValue element = value.Type == JgsType.Array ? value.ElementAt(i) : value;
            JgsHandleEntry entry = JgsHandleRegistry.Require(element, line, col);
            if (entry.Target is not PlotObject plot)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} wants handles to plotted series, but one of them names a {entry.Kind.ToString().ToLowerInvariant()}.");
            }

            plots.Add(plot);
        }

        return plots;
    }

    /// <summary>Reads one property off a handle.</summary>
    internal static JgsValue GetHandleProperty(JgsHandleEntry entry, string name, int line, int col) =>
        entry.Kind switch
        {
            JgsHandleKind.Line => GetLineProperty(entry, (LinePlot)entry.Target, name, line, col),
            JgsHandleKind.Axes => GetAxesProperty((AxesModel)entry.Target, name, line, col),
            JgsHandleKind.Legend => GetLegendProperty(entry, (LegendModel)entry.Target, name, line, col),
            _ => throw UnknownProperty(entry, name, line, col),
        };

    /// <summary>Writes one property through a handle.</summary>
    internal static void SetHandleProperty(JgsHandleEntry entry, string name, JgsValue value, int line, int col)
    {
        switch (entry.Kind)
        {
            case JgsHandleKind.Line:
                SetLineProperty(entry, (LinePlot)entry.Target, name, value, line, col);
                return;
            case JgsHandleKind.Axes:
                SetAxesProperty((AxesModel)entry.Target, name, value, line, col);
                return;
            case JgsHandleKind.Legend:
                SetLegendProperty(entry, (LegendModel)entry.Target, name, value, line, col);
                return;
            default:
                throw UnknownProperty(entry, name, line, col);
        }
    }

    // --- Line ---------------------------------------------------------------------------------

    private static JgsValue GetLineProperty(JgsHandleEntry entry, LinePlot plot, string name, int line, int col) =>
        name.ToLowerInvariant() switch
        {
            "color" => ColorValue(ResolveLineColor(plot)),
            "visible" => OnOffValue(plot.Visible),
            "linewidth" => JgsValue.Number(plot.LineWidth),
            "linestyle" => JgsValue.Str(DashName(plot.DashStyle)),
            "marker" => JgsValue.Str(MarkerName(plot.Marker)),
            "markersize" => JgsValue.Number(plot.MarkerSize),
            "displayname" => JgsValue.Str(plot.DisplayName),
            "handlevisibility" => OnOffValue(entry.HandleVisible),
            "xdata" => SeriesValue(plot, x: true),
            "ydata" => SeriesValue(plot, x: false),
            _ => throw UnknownProperty(entry, name, line, col),
        };

    private static void SetLineProperty(
        JgsHandleEntry entry, LinePlot plot, string name, JgsValue value, int line, int col)
    {
        switch (name.ToLowerInvariant())
        {
            case "color":
                plot.Color = OptionColor(value, line, col, "line");
                break;
            case "visible":
                plot.Visible = OnOffOf("Visible", value, line, col);
                break;
            case "linewidth":
                plot.LineWidth = NumOf("line: LineWidth", value, line, col);
                break;
            case "linestyle":
                plot.DashStyle = ParseDash(StrOf("line: LineStyle", value, line, col), plot.DashStyle);
                break;
            case "marker":
                plot.Marker = ParseMarker(StrOf("line: Marker", value, line, col), plot.Marker);
                break;
            case "markersize":
                plot.MarkerSize = NumOf("line: MarkerSize", value, line, col);
                break;
            case "displayname":
                SetDisplayName(plot, StrOf("line: DisplayName", value, line, col));
                break;
            case "handlevisibility":
                entry.HandleVisible = OnOffOf("HandleVisibility", value, line, col);
                break;
            case "xdata":
            case "ydata":
                throw new JgsRuntimeException(line, col,
                    $"'{name}' can be read but not written; plot the series again to change its data.");
            default:
                throw UnknownProperty(entry, name, line, col);
        }
    }

    /// <summary>
    /// A line's colour is only decided at draw time when the script never named one, so reading it
    /// resolves the colour the palette would give this series and writes it down. Answering with a
    /// definite colour is what lets a second series be drawn to match the first.
    /// </summary>
    private static Color ResolveLineColor(LinePlot plot)
    {
        if (plot.Color is { } explicitColor)
        {
            return explicitColor;
        }

        Color resolved = PaletteColorFor(plot);
        plot.Color = resolved;
        return resolved;
    }

    /// <summary>The palette entry a plot's place in its axes' draw order earns it.</summary>
    internal static Color PaletteColorFor(PlotObject plot)
    {
        AxesModel? axes = plot.Axes;
        IReadOnlyList<Color> palette = axes?.ColorOrder ?? Colors.DefaultSeriesOrder;
        if (palette.Count == 0)
        {
            return Colors.Black;
        }

        int index = 0;
        if (axes is not null)
        {
            foreach (PlotObject candidate in axes.Plots.InDrawOrder())
            {
                if (ReferenceEquals(candidate, plot))
                {
                    break;
                }

                index++;
            }
        }

        return palette[index % palette.Count];
    }

    /// <summary>
    /// MATLAB's DisplayName is the legend text; JGraph's plot browser shows the object's Name. A
    /// script that names a series means both.
    /// </summary>
    internal static void SetDisplayName(PlotObject plot, string name)
    {
        plot.DisplayName = name;
        plot.Name = name;
    }

    private static JgsValue SeriesValue(LinePlot plot, bool x)
    {
        int count = plot.Data.Count;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = x ? plot.Data.GetX(i) : plot.Data.GetY(i);
        }

        return JgsMatrix.FromColumnMajor(values, 1, count);
    }

    // --- Axes ---------------------------------------------------------------------------------

    private static JgsValue GetAxesProperty(AxesModel axes, string name, int line, int col) =>
        name.ToLowerInvariant() switch
        {
            "title" => JgsValue.Str(axes.Title),
            "xlabel" => JgsValue.Str(axes.PrimaryXAxis.Label),
            "ylabel" => JgsValue.Str(axes.PrimaryYAxis.Label),
            "xlim" => RangeValue(axes.PrimaryXAxis.Range),
            "ylim" => RangeValue(axes.PrimaryYAxis.Range),
            "visible" => OnOffValue(axes.Visible),
            _ => throw new JgsRuntimeException(line, col,
                $"An axes has no property '{name}'. It answers to Title, XLabel, YLabel, XLim, YLim, and Visible."),
        };

    private static void SetAxesProperty(AxesModel axes, string name, JgsValue value, int line, int col)
    {
        switch (name.ToLowerInvariant())
        {
            case "title":
                axes.Title = StrOf("axes: Title", value, line, col);
                break;
            case "xlabel":
                axes.PrimaryXAxis.Label = StrOf("axes: XLabel", value, line, col);
                break;
            case "ylabel":
                axes.PrimaryYAxis.Label = StrOf("axes: YLabel", value, line, col);
                break;
            case "xlim":
                SetLimits(axes.PrimaryXAxis, "XLim", value, line, col);
                break;
            case "ylim":
                SetLimits(axes.PrimaryYAxis, "YLim", value, line, col);
                break;
            case "visible":
                axes.Visible = OnOffOf("Visible", value, line, col);
                break;
            default:
                throw new JgsRuntimeException(line, col,
                    $"An axes has no property '{name}'. It answers to Title, XLabel, YLabel, XLim, YLim, and Visible.");
        }
    }

    private static void SetLimits(AxisModel axis, string what, JgsValue value, int line, int col)
    {
        double[] limits = ToDoubles($"axes: {what}", value, line, col);
        if (limits.Length != 2)
        {
            throw new JgsRuntimeException(line, col, $"axes: {what} is a [min max] pair.");
        }

        axis.AutoScale = false;
        axis.Range = new DataRange(limits[0], limits[1]);
    }

    private static JgsValue RangeValue(DataRange range) =>
        JgsMatrix.FromColumnMajor([range.Min, range.Max], 1, 2);

    // --- Legend -------------------------------------------------------------------------------

    private static JgsValue GetLegendProperty(
        JgsHandleEntry entry, LegendModel legend, string name, int line, int col) =>
        name.ToLowerInvariant() switch
        {
            "location" => JgsValue.Str(LocationName(legend.Position)),
            "itemhitfcn" => entry.ItemHitFcn ?? JgsValue.Array([]),
            "title" => JgsValue.Str(legend.Title ?? string.Empty),
            "visible" => OnOffValue(legend.Visible),
            _ => throw new JgsRuntimeException(line, col,
                $"A legend has no property '{name}'. It answers to Location, ItemHitFcn, Title, and Visible."),
        };

    private static void SetLegendProperty(
        JgsHandleEntry entry, LegendModel legend, string name, JgsValue value, int line, int col)
    {
        switch (name.ToLowerInvariant())
        {
            case "location":
                legend.Position = ParseLegendLocation(StrOf("legend: Location", value, line, col), line, col);
                break;
            case "itemhitfcn":
                if (value.Type != JgsType.Function)
                {
                    throw new JgsRuntimeException(line, col,
                        "legend: ItemHitFcn is a function handle, such as @(src, event) myCallback(src, event).");
                }

                entry.ItemHitFcn = value;
                break;
            case "title":
                legend.Title = StrOf("legend: Title", value, line, col);
                break;
            case "visible":
                legend.Visible = OnOffOf("Visible", value, line, col);
                break;
            default:
                throw new JgsRuntimeException(line, col,
                    $"A legend has no property '{name}'. It answers to Location, ItemHitFcn, Title, and Visible.");
        }
    }

    /// <summary>
    /// MATLAB's legend placement words. 'best' asks for the least-obstructed corner, which this
    /// renderer does not search for, so it lands on the top right — MATLAB's own most common answer.
    /// </summary>
    internal static LegendPosition ParseLegendLocation(string word, int line, int col) =>
        word.Replace("outside", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant() switch
        {
            "best" or "northeast" => LegendPosition.TopRight,
            "northwest" => LegendPosition.TopLeft,
            "southeast" => LegendPosition.BottomRight,
            "southwest" => LegendPosition.BottomLeft,
            "north" => LegendPosition.Top,
            "south" => LegendPosition.Bottom,
            "east" => LegendPosition.Right,
            "west" => LegendPosition.Left,
            "none" => LegendPosition.Custom,
            _ => throw new JgsRuntimeException(line, col,
                $"legend: '{word}' is not a placement. Use best, north, south, east, west, or a corner such as northeast."),
        };

    private static string LocationName(LegendPosition position) => position switch
    {
        LegendPosition.TopRight => "northeast",
        LegendPosition.TopLeft => "northwest",
        LegendPosition.BottomRight => "southeast",
        LegendPosition.BottomLeft => "southwest",
        LegendPosition.Top => "north",
        LegendPosition.Bottom => "south",
        LegendPosition.Right => "east",
        LegendPosition.Left => "west",
        _ => "none",
    };

    // --- Shared -------------------------------------------------------------------------------

    private static JgsValue ColorValue(Color color) =>
        JgsMatrix.FromColumnMajor([color.R / 255.0, color.G / 255.0, color.B / 255.0], 1, 3);

    private static JgsValue OnOffValue(bool on) => JgsValue.Str(on ? "on" : "off");

    /// <summary>A property whose value is the word 'on' or 'off' (or, forgivingly, a true/false).</summary>
    private static bool OnOffOf(string what, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Bool or JgsType.Number)
        {
            return value.IsTruthy;
        }

        string word = StrOf(what, value, line, col);
        if (word.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (word.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new JgsRuntimeException(line, col, $"{what} is 'on' or 'off', but got '{word}'.");
    }

    private static string DashName(DashStyle style) => style switch
    {
        DashStyle.Dash => "--",
        DashStyle.Dot => ":",
        DashStyle.DashDot or DashStyle.DashDotDot => "-.",
        DashStyle.None => "none",
        _ => "-",
    };

    private static DashStyle ParseDash(string text, DashStyle fallback) =>
        text.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? DashStyle.None
            : LineSpec.Parse(text).Dash ?? fallback;

    private static string MarkerName(MarkerType marker) => marker switch
    {
        MarkerType.Circle => "o",
        MarkerType.Square => "s",
        MarkerType.Diamond => "d",
        MarkerType.TriangleUp => "^",
        MarkerType.TriangleDown => "v",
        MarkerType.Plus => "+",
        MarkerType.Cross => "x",
        MarkerType.Star => "*",
        MarkerType.Point => ".",
        _ => "none",
    };

    private static MarkerType ParseMarker(string text, MarkerType fallback) =>
        text.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? MarkerType.None
            : LineSpec.Parse(text).Marker ?? fallback;

    private static JgsRuntimeException UnknownProperty(JgsHandleEntry entry, string name, int line, int col) =>
        new(line, col, $"A {entry.Kind.ToString().ToLowerInvariant()} has no property '{name}'.");
}
