using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Reading and writing a figure object's properties through a handle — <c>p.Color</c>,
/// <c>p.Visible = 'off'</c>, <c>lgd.ItemHitFcn = @cb</c>.
/// <para>
/// M51 answered these from one hand-written switch per kind of object, and M53 added a fourth for the
/// shapes its statistics verbs drew. The switches are gone: <see cref="JgsGraphicsProperties"/> knows
/// what every object answers to, so the dot and <c>get</c>/<c>set</c> now agree by construction rather
/// than by two lists being kept in step.
/// </para>
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
    /// <para>
    /// A ruler handle names its axes too, because on a two-sided axes that is the only handle a script
    /// has for one side: <c>plotyy</c> answers with the two rulers, and <c>title(AX(1), …)</c> has to
    /// mean the axes they belong to.
    /// </para>
    /// </summary>
    internal static (AxesModel? Axes, IReadOnlyList<JgsValue> Remaining) PeelAxes(IReadOnlyList<JgsValue> args)
    {
        (AxesModel? axes, _, IReadOnlyList<JgsValue> rest) = PeelRuler(args);
        return (axes, rest);
    }

    /// <summary>
    /// The same split, keeping the ruler when the handle named one. The verbs that speak about a
    /// single ruler — <c>ylim</c>, <c>yticks</c>, <c>ylabel</c> — use this, so that naming a ruler
    /// aims at that side rather than at whichever side <c>yyaxis</c> last made active.
    /// </summary>
    internal static (AxesModel? Axes, AxisModel? Ruler, IReadOnlyList<JgsValue> Remaining) PeelRuler(
        IReadOnlyList<JgsValue> args)
    {
        if (args.Count == 0 || !JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry))
        {
            return (null, null, args);
        }

        (AxesModel? axes, AxisModel? ruler) = entry.Target switch
        {
            AxesModel named => (named, (AxisModel?)null),
            AxisModel named => (named.Parent as AxesModel, named),
            _ => (null, null),
        };

        if (axes is null)
        {
            return (null, null, args);
        }

        var rest = new JgsValue[args.Count - 1];
        for (int i = 1; i < args.Count; i++)
        {
            rest[i - 1] = args[i];
        }

        return (axes, ruler, rest);
    }

    /// <summary>
    /// Runs a verb against <paramref name="axes"/> and puts the previous current axes back, because
    /// naming an axes in a call does not make it the one an unqualified verb would find next.
    /// </summary>
    internal static T OnAxes<T>(AxesModel? axes, Func<T> body)
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

    /// <summary>
    /// Wraps a verb's body so a leading axes handle is peeled off before the body reads its own
    /// arguments, and the verb draws into that axes without making it current.
    /// <para>
    /// M51 gave <c>plot</c> and the titling family this, one verb at a time. M69's form probe then
    /// measured what "one verb at a time" had reached: <c>surf(ax, Z)</c>, <c>mesh(ax, Z)</c>,
    /// <c>stem(ax, x, y)</c> and about a hundred others still read the handle as *data*, which is why
    /// <c>line(ax, x, y)</c> complained that its three coordinates had lengths 1, 3 and 3. The
    /// argument list is the same for every one of them, so the split belongs here rather than in each
    /// verb, and a verb opts in by wrapping rather than by remembering to call two helpers in order.
    /// </para>
    /// </summary>
    internal static Func<IReadOnlyList<JgsValue>, int, int, JgsValue> OnNamedAxes(
        Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
        (args, line, col) =>
        {
            (AxesModel? axes, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
            return OnAxes(axes, () => body(rest, line, col));
        };

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
                    $"{verb} wants handles to plotted series, but one of them names a {entry.TypeName}.");
            }

            plots.Add(plot);
        }

        return plots;
    }

    /// <summary>Reads one property off a handle.</summary>
    internal static JgsValue GetHandleProperty(JgsHandleEntry entry, string name, int line, int col) =>
        JgsGraphicsProperties.Get(entry, name, line, col);

    /// <summary>Writes one property through a handle.</summary>
    internal static void SetHandleProperty(JgsHandleEntry entry, string name, JgsValue value, int line, int col) =>
        JgsGraphicsProperties.Set(entry, name, value, line, col);

    // --- Shared vocabulary ----------------------------------------------------------------------

    /// <summary>
    /// A line's colour is only decided at draw time when the script never named one, so reading it
    /// resolves the colour the palette would give this series and writes it down. Answering with a
    /// definite colour is what lets a second series be drawn to match the first.
    /// </summary>
    internal static Color ResolveSeriesColor(LinePlot plot)
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

    internal static string LegendLocationWord(LegendPosition position) => position switch
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

    internal static string DashWord(DashStyle style) => style switch
    {
        DashStyle.Dash => "--",
        DashStyle.Dot => ":",
        DashStyle.DashDot or DashStyle.DashDotDot => "-.",
        DashStyle.None => "none",
        _ => "-",
    };

    internal static DashStyle ParseDashWord(string text, DashStyle fallback) =>
        text.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? DashStyle.None
            : LineSpec.Parse(text).Dash ?? fallback;

    internal static string MarkerWord(MarkerType marker) => marker switch
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

    internal static MarkerType ParseMarkerWord(string text, MarkerType fallback) =>
        text.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? MarkerType.None
            : LineSpec.Parse(text).Marker ?? fallback;
}
