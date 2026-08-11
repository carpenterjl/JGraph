using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>One named property of one kind of figure object, as a script sees it.</summary>
internal sealed class GraphicsProperty
{
    public GraphicsProperty(
        string name,
        Func<JgsHandleEntry, JgsValue> read,
        Action<JgsHandleEntry, JgsValue, int, int>? write = null)
    {
        Name = name;
        Read = read;
        Write = write;
    }

    /// <summary>The property's name in the spelling a script should see it printed in.</summary>
    public string Name { get; }

    public Func<JgsHandleEntry, JgsValue> Read { get; }

    /// <summary>Null when the property can be read but not written.</summary>
    public Action<JgsHandleEntry, JgsValue, int, int>? Write { get; }
}

/// <summary>
/// The one description of what properties a figure object has and what its type is called. Everything
/// that names a property goes through here: the dot (<c>p.Color</c>), <c>get</c>/<c>set</c>, and
/// <c>findobj</c>'s filters.
/// <para>
/// The table for a given object is built from two layers. The lower one is <b>reflection over the
/// model's own browsable properties</b> — the very metadata the property inspector reads — so a plot
/// object added by a later milestone arrives with its whole property surface already reachable, and
/// cannot silently arrive without one. The upper one is a small <b>curated alias layer</b> giving the
/// MATLAB spellings for the things MATLAB names differently or splits apart: <c>XLim</c> is the
/// primary X ruler's range, <c>Position</c> is the axes' normalized bounds, <c>Color</c> on an axes is
/// its background, <c>Parent</c> and <c>Children</c> mint handles.
/// </para>
/// M51 hand-wrote one switch per kind of object, and M53 had to add a fourth for the shapes its
/// statistics verbs drew. This replaces all of them: the switch was a list that had to be extended by
/// hand every time the model grew, which is exactly the thing that goes stale.
/// </summary>
internal static class JgsGraphicsProperties
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, GraphicsProperty>> Tables = new();

    // --- Type names -----------------------------------------------------------------------------

    /// <summary>
    /// What <c>get(h, 'Type')</c> answers, in MATLAB's spelling where MATLAB has one. Anything not
    /// named here falls back to its own class name with the modelling suffix dropped, so a chart type
    /// added later gets a sensible answer without an edit — and one that is easy to correct if MATLAB
    /// happens to call it something else.
    /// </summary>
    public static string TypeNameOf(GraphObject target) => target switch
    {
        FigureModel => "figure",
        AxesModel => "axes",
        AxisModel => "numericruler",
        LegendModel => "legend",
        ColorbarModel => "colorbar",
        GridModel => "grid",
        LightModel => "light",
        LinePlot or Line3DPlot => "line",
        ScatterPlot or Scatter3DPlot => "scatter",
        BarPlot => "bar",
        StemPlot => "stem",
        HistogramPlot => "histogram",
        ErrorBarPlot => "errorbar",
        SurfacePlot => "surface",
        ContourPlot => "contour",
        PatchPlot => "patch",
        QuiverPlot => "quiver",
        ImagePlot or RgbImagePlot => "image",
        TextAnnotation => "text",
        ArrowAnnotation => "arrow",
        ShapeAnnotation => "rectangle",
        DataTipAnnotation => "datatip",
        _ => FallbackTypeName(target.GetType()),
    };

    private static string FallbackTypeName(Type type)
    {
        string name = type.Name;
        foreach (string suffix in new[] { "Plot", "Model", "Annotation" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return name.ToLowerInvariant();
    }

    // --- The figure tree ------------------------------------------------------------------------

    /// <summary>
    /// What <c>get(h, 'Children')</c> answers: the content of an object, not its furniture. An axes'
    /// children are the things drawn in it, not its rulers — a ruler is reached through
    /// <c>ax.XAxis</c>, which is where MATLAB puts it too.
    /// </summary>
    public static IReadOnlyList<GraphObject> ChildrenOf(GraphObject target)
    {
        var children = new List<GraphObject>();
        switch (target)
        {
            case FigureModel figure:
                children.AddRange(figure.Axes);
                children.AddRange(figure.Annotations);
                break;
            case AxesModel axes:
                children.AddRange(axes.Plots);
                children.AddRange(axes.Annotations);
                children.AddRange(axes.Lights);
                if (axes.Legend.Visible)
                {
                    children.Add(axes.Legend);
                }

                if (axes.Colorbar.Visible)
                {
                    children.Add(axes.Colorbar);
                }

                break;
        }

        return children;
    }

    /// <summary>
    /// Everything under an object, furniture included. This is what a search walks and what decides
    /// whether a handle is still alive; it is deliberately wider than <see cref="ChildrenOf"/> so that
    /// <c>findobj(fig, 'Type', 'numericruler')</c> finds something and a closed figure lets go of all
    /// of its parts, not just the drawn ones.
    /// </summary>
    public static IReadOnlyList<GraphObject> DescendantsOf(GraphObject target)
    {
        var all = new List<GraphObject>(ChildrenOf(target));
        if (target is AxesModel axes)
        {
            all.AddRange(axes.XAxes);
            all.AddRange(axes.YAxes);
            all.Add(axes.ZAxis);
            all.Add(axes.Grid);
            if (!axes.Legend.Visible)
            {
                all.Add(axes.Legend);
            }

            if (!axes.Colorbar.Visible)
            {
                all.Add(axes.Colorbar);
            }
        }

        return all;
    }

    // --- Reading and writing --------------------------------------------------------------------

    /// <summary>Every property name an object answers to, alphabetically.</summary>
    public static IReadOnlyList<string> NamesOf(GraphObject target)
    {
        var names = TableFor(target.GetType()).Values.Select(static p => p.Name).ToList();
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static bool TryFind(GraphObject target, string name, out GraphicsProperty property) =>
        TableFor(target.GetType()).TryGetValue(name.ToLowerInvariant(), out property!);

    public static JgsValue Get(JgsHandleEntry entry, string name, int line, int col)
    {
        if (!TryFind(entry.Target, name, out GraphicsProperty property))
        {
            throw Unknown(entry.Target, name, line, col);
        }

        return property.Read(entry);
    }

    public static void Set(JgsHandleEntry entry, string name, JgsValue value, int line, int col)
    {
        if (!TryFind(entry.Target, name, out GraphicsProperty property))
        {
            throw Unknown(entry.Target, name, line, col);
        }

        if (property.Write is null)
        {
            throw new JgsRuntimeException(line, col,
                $"'{property.Name}' can be read but not written on a {TypeNameOf(entry.Target)}.");
        }

        property.Write(entry, value, line, col);
    }

    /// <summary>
    /// The error for a name nothing answers to. It lists the near spellings rather than the whole
    /// surface, because a table of eighty names is not an answer to "you meant which of these?".
    /// </summary>
    private static JgsRuntimeException Unknown(GraphObject target, string name, int line, int col)
    {
        string type = TypeNameOf(target);
        List<string> near = NamesOf(target)
            .Where(candidate => Resembles(candidate, name))
            .Take(6)
            .ToList();

        string suggestion = near.Count > 0
            ? $" Did you mean {string.Join(", ", near)}?"
            : $" It answers to {NamesOf(target).Count} properties; ask get(h) for the list.";

        return new JgsRuntimeException(line, col, $"A {type} has no property '{name}'.{suggestion}");
    }

    /// <summary>A cheap near-miss: same start, same end, or one is contained in the other.</summary>
    private static bool Resembles(string candidate, string typed)
    {
        if (typed.Length < 2)
        {
            return false;
        }

        return candidate.Contains(typed, StringComparison.OrdinalIgnoreCase)
            || typed.Contains(candidate, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(typed[..2], StringComparison.OrdinalIgnoreCase);
    }

    // --- Building the table ---------------------------------------------------------------------

    public static IReadOnlyDictionary<string, GraphicsProperty> TableFor(Type type) =>
        Tables.GetOrAdd(type, Build);

    private static IReadOnlyDictionary<string, GraphicsProperty> Build(Type type)
    {
        var table = new Dictionary<string, GraphicsProperty>(StringComparer.OrdinalIgnoreCase);
        AddReflected(type, table);
        AddAliases(type, table);
        return table;
    }

    /// <summary>
    /// Every browsable property the model declares, addressed by its own name. A property whose type
    /// no bridge understands is left out rather than half-supported; the guardrail test in the suite
    /// is what keeps that list of exclusions short and deliberate.
    /// </summary>
    private static void AddReflected(Type type, IDictionary<string, GraphicsProperty> table)
    {
        foreach (PropertyInfo info in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (info.GetIndexParameters().Length > 0 || info.GetMethod is null || !info.CanRead)
            {
                continue;
            }

            if (info.GetCustomAttribute<BrowsableAttribute>() is { Browsable: false })
            {
                continue;
            }

            if (!ValueBridge.Handles(info.PropertyType))
            {
                continue;
            }

            PropertyInfo captured = info;
            bool writable = captured.SetMethod is { IsPublic: true };
            table[captured.Name] = new GraphicsProperty(
                captured.Name,
                entry => ValueBridge.ToValue(captured.GetValue(entry.Target)),
                writable
                    ? (entry, value, line, col) => captured.SetValue(
                        entry.Target,
                        ValueBridge.FromValue(captured.PropertyType, captured.Name, value, line, col))
                    : null);
        }
    }

    /// <summary>
    /// The MATLAB spellings, applied from the most general type to the most specific so that a
    /// specific one wins. These are the properties MATLAB names differently, splits differently, or
    /// computes — never a re-statement of something reflection already found.
    /// </summary>
    private static void AddAliases(Type type, IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Type", entry => JgsValue.Str(TypeNameOf(entry.Target)));
        Put(table, "Tag",
            entry => JgsValue.Str(entry.Target.Tag ?? string.Empty),
            (entry, value, line, col) => entry.Target.Tag = JgsBuiltins.StrOf("Tag", value, line, col));
        Put(table, "UserData",
            entry => entry.Target.UserData is JgsValue stored ? stored : JgsValue.Array([]),
            (entry, value, _, _) => entry.Target.UserData = value);
        Put(table, "HandleVisibility",
            entry => OnOff(entry.HandleVisible),
            (entry, value, line, col) => entry.HandleVisible = ToOnOff("HandleVisibility", value, line, col));
        Put(table, "Parent", entry => entry.Target.Parent is { } parent
            ? JgsHandleRegistry.For(parent)
            : JgsValue.Array([]));
        Put(table, "Children", entry => HandleRow(ChildrenOf(entry.Target)));

        if (typeof(PlotObject).IsAssignableFrom(type))
        {
            // DisplayName is the legend text in MATLAB and the browser label here; a script naming a
            // series means both, which is why this one is not left to reflection.
            Put(table, "DisplayName",
                entry => JgsValue.Str(((PlotObject)entry.Target).DisplayName),
                (entry, value, line, col) => JgsBuiltins.SetDisplayName(
                    (PlotObject)entry.Target, JgsBuiltins.StrOf("DisplayName", value, line, col)));
        }

        if (typeof(XYPlot).IsAssignableFrom(type))
        {
            Put(table, "XData", entry => SeriesRow((XYPlot)entry.Target, x: true));
            Put(table, "YData", entry => SeriesRow((XYPlot)entry.Target, x: false));
        }

        if (typeof(LinePlot).IsAssignableFrom(type))
        {
            Put(table, "Color",
                entry => ColorRow(JgsBuiltins.ResolveSeriesColor((LinePlot)entry.Target)),
                (entry, value, line, col) =>
                    ((LinePlot)entry.Target).Color = JgsBuiltins.OptionColor(value, line, col, "line"));
            Put(table, "LineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((LinePlot)entry.Target).DashStyle)),
                (entry, value, line, col) =>
                {
                    var plot = (LinePlot)entry.Target;
                    plot.DashStyle = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("LineStyle", value, line, col), plot.DashStyle);
                });
            Put(table, "Marker",
                entry => JgsValue.Str(JgsBuiltins.MarkerWord(((LinePlot)entry.Target).Marker)),
                (entry, value, line, col) =>
                {
                    var plot = (LinePlot)entry.Target;
                    plot.Marker = JgsBuiltins.ParseMarkerWord(
                        JgsBuiltins.StrOf("Marker", value, line, col), plot.Marker);
                });
        }

        if (typeof(ScatterPlot).IsAssignableFrom(type))
        {
            Put(table, "Color",
                entry => ColorRow(((ScatterPlot)entry.Target).Color ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target)),
                (entry, value, line, col) =>
                    ((ScatterPlot)entry.Target).Color = JgsBuiltins.OptionColor(value, line, col, "scatter"));
        }

        if (typeof(BarPlot).IsAssignableFrom(type))
        {
            AddSameColor(table, "Color", "FaceColor",
                entry => ((BarPlot)entry.Target).FillColor ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target),
                (entry, color) => ((BarPlot)entry.Target).FillColor = color,
                "bar");
        }

        if (typeof(PatchPlot).IsAssignableFrom(type))
        {
            AddSameColor(table, "Color", "FaceColor",
                entry => ((PatchPlot)entry.Target).FaceColor ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target),
                (entry, color) => ((PatchPlot)entry.Target).FaceColor = color,
                "patch");
        }

        if (typeof(ContourPlot).IsAssignableFrom(type))
        {
            // MATLAB calls the levels LevelList, and answers with the ones actually drawn rather than
            // with nothing when they were chosen automatically — which is what clabel needs to see.
            Put(table, "LevelList",
                entry => Row(((ContourPlot)entry.Target).ResolvedLevels),
                (entry, value, line, col) => ((ContourPlot)entry.Target).Levels =
                    JgsBuiltins.ToDoubles("LevelList", value, line, col));
        }

        if (typeof(ConstantLinePlot).IsAssignableFrom(type))
        {
            Put(table, "Color",
                entry => ColorRow(((ConstantLinePlot)entry.Target).Color
                    ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target)),
                (entry, value, line, col) =>
                    ((ConstantLinePlot)entry.Target).Color = JgsBuiltins.OptionColor(value, line, col, "line"));
            Put(table, "LineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((ConstantLinePlot)entry.Target).Dash)),
                (entry, value, line, col) =>
                {
                    var plot = (ConstantLinePlot)entry.Target;
                    plot.Dash = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("LineStyle", value, line, col), plot.Dash);
                });

            // MATLAB says which ruler the line is constant against; here it is the direction it runs.
            Put(table, "InterceptAxis",
                entry => JgsValue.Str(((ConstantLinePlot)entry.Target).Direction == ConstantLineDirection.Vertical
                    ? "x"
                    : "y"),
                (entry, value, line, col) => ((ConstantLinePlot)entry.Target).Direction =
                    JgsBuiltins.StrOf("InterceptAxis", value, line, col).ToLowerInvariant() switch
                    {
                        "x" => ConstantLineDirection.Vertical,
                        "y" => ConstantLineDirection.Horizontal,
                        _ => throw new JgsRuntimeException(line, col, "InterceptAxis is 'x' or 'y'."),
                    });
            Put(table, "Alpha",
                entry => JgsValue.Number(((ConstantLinePlot)entry.Target).Opacity),
                (entry, value, line, col) => ((ConstantLinePlot)entry.Target).Opacity =
                    JgsBuiltins.NumOf("Alpha", value, line, col));
        }

        if (typeof(AxesModel).IsAssignableFrom(type))
        {
            AddAxesAliases(table);
        }

        if (typeof(AxisModel).IsAssignableFrom(type))
        {
            AddRulerAliases(table);
        }

        if (typeof(FigureModel).IsAssignableFrom(type))
        {
            Put(table, "Number", entry => JgsValue.Number(JG.GetFigureNumber((FigureModel)entry.Target)));
            Put(table, "Color",
                entry => ColorRow(((FigureModel)entry.Target).Background),
                (entry, value, line, col) =>
                    ((FigureModel)entry.Target).Background = JgsBuiltins.OptionColor(value, line, col, "figure"));
            Put(table, "Position",
                entry => Row(0, 0, ((FigureModel)entry.Target).Size.Width, ((FigureModel)entry.Target).Size.Height),
                (entry, value, line, col) =>
                {
                    double[] box = Numbers("Position", value, 4, line, col);
                    ((FigureModel)entry.Target).Size = new Size2D(box[2], box[3]);
                });
            Put(table, "CurrentAxes", entry => ((FigureModel)entry.Target).Axes.Count > 0
                ? JgsHandleRegistry.For(JG.CurrentAxesOrNull ?? ((FigureModel)entry.Target).Axes[0])
                : JgsValue.Array([]));
        }

        if (typeof(LegendModel).IsAssignableFrom(type))
        {
            Put(table, "Location",
                entry => JgsValue.Str(JgsBuiltins.LegendLocationWord(((LegendModel)entry.Target).Position)),
                (entry, value, line, col) => ((LegendModel)entry.Target).Position =
                    JgsBuiltins.ParseLegendLocation(JgsBuiltins.StrOf("Location", value, line, col), line, col));
            Put(table, "ItemHitFcn",
                entry => entry.ItemHitFcn ?? JgsValue.Array([]),
                (entry, value, line, col) =>
                {
                    if (value.Type != JgsType.Function)
                    {
                        throw new JgsRuntimeException(line, col,
                            "legend: ItemHitFcn is a function handle, such as @(src, event) myCallback(src, event).");
                    }

                    entry.ItemHitFcn = value;
                });
            Put(table, "String", entry => JgsValue.Cell(
                ((LegendModel)entry.Target).Entries.Select(e => JgsValue.Str(e.Label ?? string.Empty)).ToArray()));
        }
    }

    private static void AddAxesAliases(IDictionary<string, GraphicsProperty> table)
    {
        // Every y-facing spelling answers for the ruler yyaxis has made active, which is how MATLAB
        // reads them: on a two-sided axes, get(ax, 'YLim') is the side you are working on.
        AddLimit(table, "XLim", axes => axes.PrimaryXAxis);
        AddLimit(table, "YLim", axes => axes.ActiveYAxis);
        AddLimit(table, "ZLim", axes => axes.ZAxis);

        AddLabel(table, "XLabel", axes => axes.PrimaryXAxis);
        AddLabel(table, "YLabel", axes => axes.ActiveYAxis);
        AddLabel(table, "ZLabel", axes => axes.ZAxis);

        AddScale(table, "XScale", axes => axes.PrimaryXAxis);
        AddScale(table, "YScale", axes => axes.ActiveYAxis);
        AddScale(table, "ZScale", axes => axes.ZAxis);

        AddDirection(table, "XDir", axes => axes.PrimaryXAxis);
        AddDirection(table, "YDir", axes => axes.ActiveYAxis);
        AddDirection(table, "ZDir", axes => axes.ZAxis);

        AddTicks(table, "X", axes => axes.PrimaryXAxis);
        AddTicks(table, "Y", axes => axes.ActiveYAxis);
        AddTicks(table, "Z", axes => axes.ZAxis);

        Put(table, "XAxis", entry => JgsHandleRegistry.For(Axes(entry).PrimaryXAxis));
        Put(table, "YAxis", entry => JgsHandleRegistry.For(Axes(entry).ActiveYAxis));
        Put(table, "ZAxis", entry => JgsHandleRegistry.For(Axes(entry).ZAxis));
        Put(table, "Legend", entry => JgsHandleRegistry.For(Axes(entry).Legend));

        Put(table, "Color",
            entry => ColorRow(Axes(entry).Background),
            (entry, value, line, col) => Axes(entry).Background =
                JgsBuiltins.OptionColor(value, line, col, "axes"));

        Put(table, "Position",
            entry => RectRow(Axes(entry).NormalizedBounds),
            (entry, value, line, col) =>
            {
                double[] box = Numbers("Position", value, 4, line, col);
                Axes(entry).NormalizedBounds = new Rect2D(box[0], box[1], box[2], box[3]);
            });

        Put(table, "Box",
            entry => OnOff(Axes(entry).FrameVisible),
            (entry, value, line, col) => Axes(entry).FrameVisible = ToOnOff("Box", value, line, col));

        Put(table, "View",
            entry => Row(Axes(entry).Azimuth, Axes(entry).Elevation),
            (entry, value, line, col) =>
            {
                double[] angles = Numbers("View", value, 2, line, col);
                Axes(entry).Azimuth = angles[0];
                Axes(entry).Elevation = angles[1];
            });

        Put(table, "NextPlot",
            entry => JgsValue.Str(Axes(entry).Hold ? "add" : "replace"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("NextPlot", value, line, col);
                Axes(entry).Hold = word.ToLowerInvariant() switch
                {
                    "add" => true,
                    "replace" or "replacechildren" or "replaceall" => false,
                    _ => throw new JgsRuntimeException(line, col,
                        $"NextPlot is 'add' or 'replace', but got '{word}'."),
                };
            });

        // The grid is one thing here, not one per direction, so XGrid and YGrid are two names for it.
        // Turning either on turns the whole grid on, which is what `grid on` has always done; a
        // per-direction grid would be a model change, and it is recorded as a divergence instead.
        foreach (string name in new[] { "XGrid", "YGrid", "ZGrid" })
        {
            string which = name;
            Put(table, which,
                entry => OnOff(Axes(entry).Grid.ShowMajor),
                (entry, value, line, col) => Axes(entry).Grid.ShowMajor = ToOnOff(which, value, line, col));
        }
    }

    private static void AddRulerAliases(IDictionary<string, GraphicsProperty> table)
    {
        // Fitted before it is read, for the same reason ax.XLim is: while a ruler auto-scales, its
        // stored range is the placeholder it was created with, and ax.XAxis.Limits has to agree with
        // ax.XLim or one of the two spellings is lying.
        Put(table, "Limits",
            entry =>
            {
                AxisModel ruler = Ruler(entry);
                if (ruler.AutoScale && ruler.Parent is AxesModel owner)
                {
                    owner.RecomputeDataBounds();
                }

                return Row(ruler.Range.Min, ruler.Range.Max);
            },
            (entry, value, line, col) =>
            {
                double[] limits = Numbers("Limits", value, 2, line, col);
                AxisModel ruler = Ruler(entry);
                ruler.AutoScale = false;
                ruler.Range = new DataRange(limits[0], limits[1]);
            });

        // The same six answers the tick verbs give, under the names a ruler wears in MATLAB.
        Put(table, "TickValues",
            entry => JgsRulerTicks.ReadValues(Ruler(entry)),
            (entry, value, line, col) => JgsRulerTicks.WriteValues("TickValues", Ruler(entry), value, line, col));
        Put(table, "TickLabels",
            entry => JgsRulerTicks.ReadLabels(Ruler(entry)),
            (entry, value, line, col) => JgsRulerTicks.WriteLabels("TickLabels", Ruler(entry), value, line, col));
        Put(table, "TickLabelRotation",
            entry => JgsRulerTicks.ReadAngle(Ruler(entry)),
            (entry, value, line, col) => JgsRulerTicks.WriteAngle("TickLabelRotation", Ruler(entry), value, line, col));

        Put(table, "Scale",
            entry => JgsValue.Str(Ruler(entry).Scale == AxisScaleType.Logarithmic ? "log" : "linear"),
            (entry, value, line, col) => Ruler(entry).Scale =
                ScaleOf(JgsBuiltins.StrOf("Scale", value, line, col), line, col));

        Put(table, "Direction",
            entry => JgsValue.Str(Ruler(entry).Inverted ? "reverse" : "normal"),
            (entry, value, line, col) => Ruler(entry).Inverted =
                DirectionOf(JgsBuiltins.StrOf("Direction", value, line, col), line, col));

        Put(table, "Color",
            entry => ColorRow(Ruler(entry).TickLabelStyle.Color),
            (entry, value, line, col) =>
            {
                AxisModel ruler = Ruler(entry);
                Color color = JgsBuiltins.OptionColor(value, line, col, "ruler");
                ruler.TickLabelStyle = ruler.TickLabelStyle.WithColor(color);
                ruler.LabelStyle = ruler.LabelStyle.WithColor(color);
            });
    }

    // --- Alias plumbing -------------------------------------------------------------------------

    private static AxesModel Axes(JgsHandleEntry entry) => (AxesModel)entry.Target;

    private static AxisModel Ruler(JgsHandleEntry entry) => (AxisModel)entry.Target;

    private static void Put(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, JgsValue> read,
        Action<JgsHandleEntry, JgsValue, int, int>? write = null) =>
        table[name] = new GraphicsProperty(name, read, write);

    /// <summary>Two spellings of one colour — MATLAB's <c>Color</c> and <c>FaceColor</c> on filled shapes.</summary>
    private static void AddSameColor(
        IDictionary<string, GraphicsProperty> table,
        string first,
        string second,
        Func<JgsHandleEntry, Color> read,
        Action<JgsHandleEntry, Color> write,
        string what)
    {
        foreach (string name in new[] { first, second })
        {
            Put(table, name,
                entry => ColorRow(read(entry)),
                (entry, value, line, col) => write(entry, JgsBuiltins.OptionColor(value, line, col, what)));
        }
    }

    /// <summary>
    /// A limit pair. Reading fits the axis first: while an axis auto-scales, the stored range is still
    /// the placeholder it was created with, and the limits a script means are the ones it would see
    /// drawn. That is the M51 lesson, and it is why this is not a plain reflected property.
    /// </summary>
    private static void AddLimit(
        IDictionary<string, GraphicsProperty> table, string name, Func<AxesModel, AxisModel> pick)
    {
        Func<AxesModel, AxisModel> chosen = pick;
        Put(table, name,
            entry =>
            {
                AxesModel axes = Axes(entry);
                AxisModel axis = chosen(axes);
                if (axis.AutoScale)
                {
                    axes.RecomputeDataBounds();
                }

                return Row(axis.Range.Min, axis.Range.Max);
            },
            (entry, value, line, col) =>
            {
                double[] limits = Numbers(name, value, 2, line, col);
                AxisModel axis = chosen(Axes(entry));
                axis.AutoScale = false;
                axis.Range = new DataRange(limits[0], limits[1]);
            });
    }

    /// <summary>
    /// An axes' tick properties for one direction — <c>XTick</c>, <c>XTickLabel</c>,
    /// <c>XTickLabelRotation</c>. These are the ruler's own answers reached through the axes, which is
    /// how MATLAB spells them and how nearly every script that touches ticks writes them.
    /// </summary>
    private static void AddTicks(
        IDictionary<string, GraphicsProperty> table, string letter, Func<AxesModel, AxisModel> pick)
    {
        Func<AxesModel, AxisModel> chosen = pick;

        Put(table, letter + "Tick",
            entry => JgsRulerTicks.ReadValues(chosen(Axes(entry))),
            (entry, value, line, col) =>
                JgsRulerTicks.WriteValues(letter + "Tick", chosen(Axes(entry)), value, line, col));

        Put(table, letter + "TickLabel",
            entry => JgsRulerTicks.ReadLabels(chosen(Axes(entry))),
            (entry, value, line, col) =>
                JgsRulerTicks.WriteLabels(letter + "TickLabel", chosen(Axes(entry)), value, line, col));

        Put(table, letter + "TickLabelRotation",
            entry => JgsRulerTicks.ReadAngle(chosen(Axes(entry))),
            (entry, value, line, col) =>
                JgsRulerTicks.WriteAngle(letter + "TickLabelRotation", chosen(Axes(entry)), value, line, col));
    }

    private static void AddLabel(
        IDictionary<string, GraphicsProperty> table, string name, Func<AxesModel, AxisModel> pick)
    {
        Func<AxesModel, AxisModel> chosen = pick;
        Put(table, name,
            entry => JgsValue.Str(chosen(Axes(entry)).Label),
            (entry, value, line, col) => chosen(Axes(entry)).Label = JgsBuiltins.StrOf(name, value, line, col));
    }

    private static void AddScale(
        IDictionary<string, GraphicsProperty> table, string name, Func<AxesModel, AxisModel> pick)
    {
        Func<AxesModel, AxisModel> chosen = pick;
        Put(table, name,
            entry => JgsValue.Str(chosen(Axes(entry)).Scale == AxisScaleType.Logarithmic ? "log" : "linear"),
            (entry, value, line, col) => chosen(Axes(entry)).Scale =
                ScaleOf(JgsBuiltins.StrOf(name, value, line, col), line, col));
    }

    private static void AddDirection(
        IDictionary<string, GraphicsProperty> table, string name, Func<AxesModel, AxisModel> pick)
    {
        Func<AxesModel, AxisModel> chosen = pick;
        Put(table, name,
            entry => JgsValue.Str(chosen(Axes(entry)).Inverted ? "reverse" : "normal"),
            (entry, value, line, col) => chosen(Axes(entry)).Inverted =
                DirectionOf(JgsBuiltins.StrOf(name, value, line, col), line, col));
    }

    private static AxisScaleType ScaleOf(string word, int line, int col) => word.ToLowerInvariant() switch
    {
        "linear" => AxisScaleType.Linear,
        "log" => AxisScaleType.Logarithmic,
        _ => throw new JgsRuntimeException(line, col, $"A scale is 'linear' or 'log', but got '{word}'."),
    };

    private static bool DirectionOf(string word, int line, int col) => word.ToLowerInvariant() switch
    {
        "normal" => false,
        "reverse" => true,
        _ => throw new JgsRuntimeException(line, col, $"A direction is 'normal' or 'reverse', but got '{word}'."),
    };

    // --- Value shapes ---------------------------------------------------------------------------

    internal static JgsValue OnOff(bool on) => JgsValue.Str(on ? "on" : "off");

    /// <summary>A property whose value is the word 'on' or 'off' (or, forgivingly, a true/false).</summary>
    internal static bool ToOnOff(string what, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Bool or JgsType.Number)
        {
            return value.IsTruthy;
        }

        string word = JgsBuiltins.StrOf(what, value, line, col);
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

    internal static JgsValue ColorRow(Color color) =>
        JgsMatrix.FromColumnMajor([color.R / 255.0, color.G / 255.0, color.B / 255.0], 1, 3);

    internal static JgsValue Row(params double[] values) =>
        JgsMatrix.FromColumnMajor(values, 1, values.Length);

    private static JgsValue RectRow(Rect2D rect) => Row(rect.X, rect.Y, rect.Width, rect.Height);

    internal static JgsValue HandleRow(IReadOnlyList<GraphObject> objects)
    {
        // MATLAB lists children most-recently-added first, and scripts index Children(1) expecting
        // the newest thing.
        var handles = new double[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            handles[i] = JgsHandleRegistry.For(objects[objects.Count - 1 - i]).AsNumber;
        }

        return JgsMatrix.FromColumnMajor(handles, 1, handles.Length);
    }

    private static JgsValue SeriesRow(XYPlot plot, bool x)
    {
        int count = plot.Data.Count;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = x ? plot.Data.GetX(i) : plot.Data.GetY(i);
        }

        return JgsMatrix.FromColumnMajor(values, 1, count);
    }

    private static double[] Numbers(string what, JgsValue value, int expected, int line, int col)
    {
        double[] numbers = JgsBuiltins.ToDoubles(what, value, line, col);
        if (numbers.Length != expected)
        {
            throw new JgsRuntimeException(line, col, $"{what} takes {expected} numbers, but got {numbers.Length}.");
        }

        return numbers;
    }

    /// <summary>
    /// The translation between a model property's CLR type and a script value. Everything the model
    /// actually uses for a browsable property is here; a type that is not is simply not exposed, which
    /// the guardrail test turns into a decision someone has to make rather than an omission.
    /// </summary>
    private static class ValueBridge
    {
        public static bool Handles(Type type)
        {
            Type bare = Nullable.GetUnderlyingType(type) ?? type;
            return bare == typeof(double) || bare == typeof(float) || bare == typeof(int)
                || bare == typeof(byte) || bare == typeof(bool) || bare == typeof(string)
                || bare == typeof(Color) || bare == typeof(Rect2D) || bare == typeof(DataRange)
                || bare == typeof(Vector3D) || bare == typeof(Size2D) || bare == typeof(Point2D)
                || bare == typeof(TextStyle) || bare == typeof(LineStyle)
                || bare == typeof((double, double)) || bare == typeof(Colormap)
                || bare.IsEnum;
        }

        public static JgsValue ToValue(object? clr) => clr switch
        {
            null => JgsValue.Array([]),
            double d => JgsValue.Number(d),
            float f => JgsValue.Number(f),
            int i => JgsValue.Number(i),
            byte b => JgsValue.Number(b),
            bool on => OnOff(on),
            string s => JgsValue.Str(s),
            Color color => ColorRow(color),
            Rect2D rect => Row(rect.X, rect.Y, rect.Width, rect.Height),
            DataRange range => Row(range.Min, range.Max),
            Vector3D v => Row(v.X, v.Y, v.Z),
            Size2D size => Row(size.Width, size.Height),
            Point2D point => Row(point.X, point.Y),
            TextStyle style => StyleStruct(style),
            LineStyle stroke => StrokeStruct(stroke),
            Colormap map => ColorRows(map.Stops),
            (double low, double high) => Row(low, high),
            Enum word => JgsValue.Str(word.ToString().ToLowerInvariant()),
            _ => JgsValue.Array([]),
        };

        public static object? FromValue(Type target, string what, JgsValue value, int line, int col)
        {
            Type bare = Nullable.GetUnderlyingType(target) ?? target;

            if (bare == typeof(double))
            {
                return JgsBuiltins.NumOf(what, value, line, col);
            }

            if (bare == typeof(float))
            {
                return (float)JgsBuiltins.NumOf(what, value, line, col);
            }

            if (bare == typeof(int))
            {
                return (int)System.Math.Round(JgsBuiltins.NumOf(what, value, line, col));
            }

            if (bare == typeof(byte))
            {
                return (byte)System.Math.Clamp(System.Math.Round(JgsBuiltins.NumOf(what, value, line, col)), 0, 255);
            }

            if (bare == typeof(bool))
            {
                return ToOnOff(what, value, line, col);
            }

            if (bare == typeof(string))
            {
                return JgsBuiltins.StrOf(what, value, line, col);
            }

            if (bare == typeof(Color))
            {
                return JgsBuiltins.OptionColor(value, line, col, what);
            }

            if (bare == typeof(Rect2D))
            {
                double[] box = Numbers(what, value, 4, line, col);
                return new Rect2D(box[0], box[1], box[2], box[3]);
            }

            if (bare == typeof(DataRange))
            {
                double[] pair = Numbers(what, value, 2, line, col);
                return new DataRange(pair[0], pair[1]);
            }

            if (bare == typeof(Vector3D))
            {
                double[] xyz = Numbers(what, value, 3, line, col);
                return new Vector3D(xyz[0], xyz[1], xyz[2]);
            }

            if (bare == typeof(Size2D))
            {
                double[] wh = Numbers(what, value, 2, line, col);
                return new Size2D(wh[0], wh[1]);
            }

            if (bare == typeof(Point2D))
            {
                double[] xy = Numbers(what, value, 2, line, col);
                return new Point2D(xy[0], xy[1]);
            }

            if (bare == typeof(TextStyle))
            {
                return StyleOf(what, value, line, col);
            }

            if (bare == typeof(LineStyle))
            {
                return StrokeOf(what, value, line, col);
            }

            if (bare == typeof((double, double)))
            {
                double[] pair = Numbers(what, value, 2, line, col);
                return (pair[0], pair[1]);
            }

            if (bare == typeof(Colormap))
            {
                double[][] rows = JgsMatrix.ToRows(what, value, line, col);
                var rgb = new double[rows.Length, 3];
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i].Length != 3)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{what} is an n-by-3 matrix of red, green and blue in [0, 1].");
                    }

                    for (int c = 0; c < 3; c++)
                    {
                        rgb[i, c] = rows[i][c];
                    }
                }

                return Colormap.FromRows("custom", rgb);
            }

            if (bare.IsEnum)
            {
                string word = JgsBuiltins.StrOf(what, value, line, col);
                foreach (string candidate in Enum.GetNames(bare))
                {
                    if (candidate.Equals(word, StringComparison.OrdinalIgnoreCase))
                    {
                        return Enum.Parse(bare, candidate);
                    }
                }

                throw new JgsRuntimeException(line, col,
                    $"{what} is one of {string.Join(", ", Enum.GetNames(bare).Select(static n => n.ToLowerInvariant()))}, but got '{word}'.");
            }

            throw new JgsRuntimeException(line, col, $"{what} cannot be set from a {value.TypeName}.");
        }

        /// <summary>A font, as the five fields MATLAB spreads it across.</summary>
        private static JgsValue StyleStruct(TextStyle style) => JgsValue.Struct(
            new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["FontName"] = JgsValue.Str(style.FontFamily),
                ["FontSize"] = JgsValue.Number(style.FontSize),
                ["FontWeight"] = JgsValue.Str(style.Bold ? "bold" : "normal"),
                ["FontAngle"] = JgsValue.Str(style.Italic ? "italic" : "normal"),
                ["Color"] = ColorRow(style.Color),
            });

        /// <summary>A stroke, as the three fields MATLAB spreads it across.</summary>
        private static JgsValue StrokeStruct(LineStyle stroke) => JgsValue.Struct(
            new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Color"] = ColorRow(stroke.Color),
                ["LineWidth"] = JgsValue.Number(stroke.Width),
                ["LineStyle"] = JgsValue.Str(JgsBuiltins.DashWord(stroke.Dash)),
            });

        private static LineStyle StrokeOf(string what, JgsValue value, int line, int col)
        {
            if (value.Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col,
                    $"{what} is a struct with Color, LineWidth and LineStyle.");
            }

            Dictionary<string, JgsValue> fields = value.AsStruct;
            var stroke = LineStyle.Default;
            if (fields.TryGetValue("Color", out JgsValue? color) && color is not null)
            {
                stroke = stroke.WithColor(JgsBuiltins.OptionColor(color, line, col, what));
            }

            if (fields.TryGetValue("LineWidth", out JgsValue? width) && width is not null)
            {
                stroke = stroke.WithWidth(JgsBuiltins.NumOf("LineWidth", width, line, col));
            }

            if (fields.TryGetValue("LineStyle", out JgsValue? dash) && dash is not null)
            {
                stroke = stroke.WithDash(JgsBuiltins.ParseDashWord(
                    JgsBuiltins.StrOf("LineStyle", dash, line, col), stroke.Dash));
            }

            return stroke;
        }

        /// <summary>A list of colours as the n-by-3 matrix MATLAB uses for a colormap.</summary>
        private static JgsValue ColorRows(IReadOnlyList<Color> colors)
        {
            var flat = new double[colors.Count * 3];
            for (int i = 0; i < colors.Count; i++)
            {
                flat[i] = colors[i].R / 255.0;
                flat[colors.Count + i] = colors[i].G / 255.0;
                flat[(2 * colors.Count) + i] = colors[i].B / 255.0;
            }

            return JgsMatrix.FromColumnMajor(flat, colors.Count, 3);
        }

        private static TextStyle StyleOf(string what, JgsValue value, int line, int col)
        {
            if (value.Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col,
                    $"{what} is a struct with FontName, FontSize, FontWeight, FontAngle and Color.");
            }

            Dictionary<string, JgsValue> fields = value.AsStruct;
            var style = TextStyle.Default;
            if (fields.TryGetValue("FontName", out JgsValue? name) && name is not null)
            {
                style = new TextStyle(style.Color, style.FontSize,
                    JgsBuiltins.StrOf("FontName", name, line, col), style.Bold, style.Italic);
            }

            if (fields.TryGetValue("FontSize", out JgsValue? size) && size is not null)
            {
                style = style.WithSize(JgsBuiltins.NumOf("FontSize", size, line, col));
            }

            if (fields.TryGetValue("FontWeight", out JgsValue? weight) && weight is not null)
            {
                style = style.WithBold(
                    JgsBuiltins.StrOf("FontWeight", weight, line, col)
                        .Equals("bold", StringComparison.OrdinalIgnoreCase));
            }

            if (fields.TryGetValue("Color", out JgsValue? color) && color is not null)
            {
                style = style.WithColor(JgsBuiltins.OptionColor(color, line, col, what));
            }

            return style;
        }
    }
}
