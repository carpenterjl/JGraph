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
        BubbleLegendModel => "bubblelegend",
        GridModel => "grid",
        LightModel => "light",
        // A stairstep is a line with one property set, and MATLAB still names it separately — which
        // is the one place a type name here depends on the object rather than only on its class.
        LinePlot { Steps: not StepMode.None } => "stair",
        LinePlot or Line3DPlot => "line",
        ScatterPlot or Scatter3DPlot => "scatter",
        BarPlot => "bar",
        AreaPlot => "area",
        PiePlot => "pie",
        HeatmapPlot => "heatmap",
        BoxChartPlot => "boxchart",
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

                if (axes.BubbleLegend.Visible)
                {
                    children.Add(axes.BubbleLegend);
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

            if (!axes.BubbleLegend.Visible)
            {
                all.Add(axes.BubbleLegend);
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

            // The per-point channels are plain arrays, which reflection does not carry, and the five
            // names MATLAB spells differently than the model does. A bubble chart is a scatter with
            // sizes here exactly as it is in MATLAB, so it answers to all of them.
            Put(table, "SizeData",
                entry => Row([.. ((ScatterPlot)entry.Target).SizeData ?? []]),
                (entry, value, line, col) => ((ScatterPlot)entry.Target).SizeData =
                    JgsBuiltins.ToDoubles("SizeData", value, line, col));
            Put(table, "ColorData",
                entry => Row([.. ((ScatterPlot)entry.Target).ColorData ?? []]),
                (entry, value, line, col) => ((ScatterPlot)entry.Target).ColorData =
                    JgsBuiltins.ToDoubles("ColorData", value, line, col));
            Put(table, "Marker",
                entry => JgsValue.Str(JgsBuiltins.MarkerWord(((ScatterPlot)entry.Target).Marker)),
                (entry, value, line, col) =>
                {
                    var plot = (ScatterPlot)entry.Target;
                    plot.Marker = JgsBuiltins.ParseMarkerWord(
                        JgsBuiltins.StrOf("Marker", value, line, col), plot.Marker);
                });
            Put(table, "MarkerFaceColor",
                entry => ColorRow(((ScatterPlot)entry.Target).Fill
                    ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target)),
                (entry, value, line, col) =>
                {
                    var plot = (ScatterPlot)entry.Target;
                    plot.Fill = JgsBuiltins.WithAlpha(
                        JgsBuiltins.OptionColor(value, line, col, "scatter"), (plot.Fill?.A ?? 255) / 255.0);
                });
            Put(table, "MarkerEdgeColor",
                entry => ColorRow(((ScatterPlot)entry.Target).Color
                    ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target)),
                (entry, value, line, col) =>
                {
                    var plot = (ScatterPlot)entry.Target;
                    plot.Color = JgsBuiltins.WithAlpha(
                        JgsBuiltins.OptionColor(value, line, col, "scatter"), (plot.Color?.A ?? 255) / 255.0);
                });

            // MATLAB keeps the transparency in its own property; here it is the colour's own alpha,
            // so the two names read and write the same byte from opposite directions.
            AddAlpha(table, "MarkerFaceAlpha",
                entry => ((ScatterPlot)entry.Target).Fill,
                (entry, color) => ((ScatterPlot)entry.Target).Fill = color);
            AddAlpha(table, "MarkerEdgeAlpha",
                entry => ((ScatterPlot)entry.Target).Color,
                (entry, color) => ((ScatterPlot)entry.Target).Color = color);

            Put(table, "LineWidth",
                entry => JgsValue.Number(((ScatterPlot)entry.Target).EdgeWidth),
                (entry, value, line, col) => ((ScatterPlot)entry.Target).EdgeWidth =
                    JgsBuiltins.NumOf("LineWidth", value, line, col));

            // The diameters the chart actually drew, which is what a script wants to check and cannot
            // work out from SizeData without repeating the scale by hand.
            Put(table, "BubbleDiameters", entry => Row(
                [.. Enumerable.Range(0, ((ScatterPlot)entry.Target).SizeData?.Count ?? 0)
                    .Select(((ScatterPlot)entry.Target).DiameterAt)]));
        }

        if (typeof(BubbleLegendModel).IsAssignableFrom(type))
        {
            // Location is MATLAB's name for which corner it sits in; the fractional placement behind
            // it is reached the same way the legend's is, which is to say by dragging or by Position.
            Put(table, "Location",
                entry => JgsValue.Str(JgsBuiltins.LegendLocationWord(((BubbleLegendModel)entry.Target).Position)),
                (entry, value, line, col) => ((BubbleLegendModel)entry.Target).Position =
                    JgsBuiltins.ParseLegendLocation(JgsBuiltins.StrOf("Location", value, line, col), line, col));
            Put(table, "Box",
                entry => OnOff(((BubbleLegendModel)entry.Target).ShowBorder),
                (entry, value, line, col) => ((BubbleLegendModel)entry.Target).ShowBorder =
                    ToOnOff("Box", value, line, col));
            Put(table, "FontSize",
                entry => JgsValue.Number(((BubbleLegendModel)entry.Target).TextStyle.FontSize),
                (entry, value, line, col) =>
                {
                    var model = (BubbleLegendModel)entry.Target;
                    model.TextStyle = model.TextStyle.WithSize(JgsBuiltins.NumOf("FontSize", value, line, col));
                });

            // What the legend says it is showing — the values under its bubbles, at the scale of the
            // axes it belongs to.
            Put(table, "BubbleValues", entry => Row(
                [.. ((BubbleLegendModel)entry.Target).ValuesFor(
                    (entry.Target.Parent as AxesModel)?.BubbleScale
                        ?? new BubbleScale(DataRange.Unit, BubbleScale.DefaultSizeRange))]));
        }

        if (typeof(BarPlot).IsAssignableFrom(type))
        {
            AddSameColor(table, "Color", "FaceColor",
                entry => ((BarPlot)entry.Target).FillColor ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target),
                (entry, color) => ((BarPlot)entry.Target).FillColor = color,
                "bar");

            // The four bar properties this build spells differently than MATLAB does. Everything
            // else — FaceAlpha, Horizontal, EdgeColor — already reads under MATLAB's own name.
            Put(table, "LineWidth",
                entry => JgsValue.Number(((BarPlot)entry.Target).EdgeWidth),
                (entry, value, line, col) => ((BarPlot)entry.Target).EdgeWidth =
                    JgsBuiltins.NumOf("LineWidth", value, line, col));
            Put(table, "BarWidth",
                entry => JgsValue.Number(((BarPlot)entry.Target).BarWidthFraction),
                (entry, value, line, col) => ((BarPlot)entry.Target).BarWidthFraction =
                    JgsBuiltins.NumOf("BarWidth", value, line, col));
            Put(table, "BaseValue",
                entry => JgsValue.Number(((BarPlot)entry.Target).Baseline),
                (entry, value, line, col) => ((BarPlot)entry.Target).Baseline =
                    JgsBuiltins.NumOf("BaseValue", value, line, col));
            Put(table, "LineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((BarPlot)entry.Target).Dash)),
                (entry, value, line, col) =>
                {
                    var plot = (BarPlot)entry.Target;
                    plot.Dash = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("LineStyle", value, line, col), plot.Dash);
                });
        }

        if (typeof(AreaPlot).IsAssignableFrom(type))
        {
            // Everything else an area has — FaceColor, FaceAlpha, BaseValue, ShowBaseLine — is
            // already reachable by reflection under the name MATLAB uses. Only the dash pattern is
            // spelled differently here than there.
            Put(table, "LineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((AreaPlot)entry.Target).Dash)),
                (entry, value, line, col) =>
                {
                    var plot = (AreaPlot)entry.Target;
                    plot.Dash = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("LineStyle", value, line, col), plot.Dash);
                });
        }

        if (typeof(PiePlot).IsAssignableFrom(type))
        {
            // A pie has no X and no Y, so the four things a script asks it about are the values it
            // was given, how far each wedge is pushed out, what is written beside them, and the
            // colours they came from. None of the four is a type reflection can carry.
            Put(table, "Values",
                entry => Row(((PiePlot)entry.Target).Values),
                (entry, value, line, col) => ((PiePlot)entry.Target).Values =
                    JgsBuiltins.ToDoubles("Values", value, line, col));
            Put(table, "Explode",
                entry => Row(((PiePlot)entry.Target).Explode ?? []),
                (entry, value, line, col) => ((PiePlot)entry.Target).Explode =
                    JgsBuiltins.ToDoubles("Explode", value, line, col));
            Put(table, "Labels",
                entry => JgsValue.Cell(Array.ConvertAll(WrittenLabels((PiePlot)entry.Target), JgsValue.Str)),
                (entry, value, line, col) => ((PiePlot)entry.Target).Labels =
                    TextRows("Labels", value, line, col));
            Put(table, "Colormap",
                entry => ValueBridge.ToValue(((PiePlot)entry.Target).Colormap),
                (entry, value, line, col) => ((PiePlot)entry.Target).Colormap =
                    (Colormap)ValueBridge.FromValue(typeof(Colormap), "Colormap", value, line, col)!);
        }

        if (typeof(HeatmapPlot).IsAssignableFrom(type))
        {
            Put(table, "ColorData",
                entry => Grid(((HeatmapPlot)entry.Target).ColorData),
                (entry, value, line, col) =>
                {
                    var plot = (HeatmapPlot)entry.Target;
                    plot.ColorData = JgsBuiltins.HeatmapGrid(value, line, col);

                    // The rulers name one cell each, so a grid of a different size renames them.
                    plot.Axes?.LabelCells(plot);
                });
            Put(table, "XData",
                entry => JgsValue.Cell(((HeatmapPlot)entry.Target).ColumnLabels().Select(JgsValue.Str).ToArray()),
                (entry, value, line, col) => Rename(entry, value, line, col, x: true));
            Put(table, "YData",
                entry => JgsValue.Cell(((HeatmapPlot)entry.Target).RowLabels().Select(JgsValue.Str).ToArray()),
                (entry, value, line, col) => Rename(entry, value, line, col, x: false));
            Put(table, "Colormap",
                entry => ValueBridge.ToValue(((HeatmapPlot)entry.Target).Colormap),
                (entry, value, line, col) => ((HeatmapPlot)entry.Target).Colormap =
                    (Colormap)ValueBridge.FromValue(typeof(Colormap), "Colormap", value, line, col)!);

            // MATLAB always answers with limits, so the automatic ones are worked out rather than
            // reported as absent — and setting them is what makes them stop moving with the data.
            Put(table, "ColorLimits",
                entry => Row(((HeatmapPlot)entry.Target).EffectiveLimits().Min,
                    ((HeatmapPlot)entry.Target).EffectiveLimits().Max),
                (entry, value, line, col) =>
                {
                    double[] pair = JgsBuiltins.ToDoubles("ColorLimits", value, line, col);
                    ((HeatmapPlot)entry.Target).ColorLimits = pair.Length == 2 && pair[0] < pair[1]
                        ? new DataRange(pair[0], pair[1])
                        : throw new JgsRuntimeException(line, col,
                            "ColorLimits is two increasing numbers, such as [0 10].");
                });
            // MATLAB says all three of "off", "work it out" and a colour in this one property, so
            // the two words have to be read here rather than left to the colour bridge.
            Put(table, "CellLabelColor",
                entry => ((HeatmapPlot)entry.Target) switch
                {
                    { ShowCellLabels: false } => JgsValue.Str("none"),
                    { CellLabelColor: null } => JgsValue.Str("auto"),
                    var plot => ColorRow(plot.CellLabelColor!.Value),
                },
                (entry, value, line, col) =>
                    JgsBuiltins.SetCellLabelColor((HeatmapPlot)entry.Target, value, line, col));
            Put(table, "CellLabelFormat",
                entry => JgsValue.Str(((HeatmapPlot)entry.Target).CellLabelFormat ?? "auto"),
                (entry, value, line, col) => ((HeatmapPlot)entry.Target).CellLabelFormat =
                    JgsRulerTicks.ToNetFormat(
                        "CellLabelFormat", JgsBuiltins.StrOf("CellLabelFormat", value, line, col), line, col));
            Put(table, "FontSize",
                entry => JgsValue.Number(((HeatmapPlot)entry.Target).CellLabelStyle.FontSize),
                (entry, value, line, col) =>
                {
                    var plot = (HeatmapPlot)entry.Target;
                    plot.CellLabelStyle = plot.CellLabelStyle.WithSize(
                        JgsBuiltins.NumOf("FontSize", value, line, col));
                });
            Put(table, "FontColor",
                entry => ColorRow(((HeatmapPlot)entry.Target).CellLabelStyle.Color),
                (entry, value, line, col) =>
                {
                    var plot = (HeatmapPlot)entry.Target;
                    plot.CellLabelStyle = plot.CellLabelStyle.WithColor(
                        JgsBuiltins.OptionColor(value, line, col, "heatmap"));
                });

            // A heatmap is a plot on ordinary axes here rather than MATLAB's chart container, so the
            // four properties that belong to the container answer for the axes it is drawn on.
            Put(table, "Title",
                entry => JgsValue.Str(Owner(entry)?.Title ?? string.Empty),
                (entry, value, line, col) => Owning(entry, line, col).Title =
                    JgsBuiltins.StrOf("Title", value, line, col));
            Put(table, "XLabel",
                entry => JgsValue.Str(Owner(entry)?.PrimaryXAxis.Label ?? string.Empty),
                (entry, value, line, col) => Owning(entry, line, col).PrimaryXAxis.Label =
                    JgsBuiltins.StrOf("XLabel", value, line, col));
            Put(table, "YLabel",
                entry => JgsValue.Str(Owner(entry)?.PrimaryYAxis.Label ?? string.Empty),
                (entry, value, line, col) => Owning(entry, line, col).PrimaryYAxis.Label =
                    JgsBuiltins.StrOf("YLabel", value, line, col));
            Put(table, "ColorbarVisible",
                entry => OnOff(Owner(entry)?.Colorbar.Visible ?? false),
                (entry, value, line, col) => Owning(entry, line, col).Colorbar.Visible =
                    ToOnOff("ColorbarVisible", value, line, col));
        }

        if (typeof(BoxChartPlot).IsAssignableFrom(type))
        {
            // The observations and their grouping are plain arrays, which reflection does not carry,
            // and the three words MATLAB spells differently than the model does.
            Put(table, "XData",
                entry => Row(((BoxChartPlot)entry.Target).XData ?? []),
                (entry, value, line, col) => ((BoxChartPlot)entry.Target).XData =
                    JgsBuiltins.ToDoubles("XData", value, line, col));
            Put(table, "YData",
                entry => Row(((BoxChartPlot)entry.Target).YData),
                (entry, value, line, col) => ((BoxChartPlot)entry.Target).YData =
                    JgsBuiltins.ToDoubles("YData", value, line, col));
            Put(table, "MarkerStyle",
                entry => JgsValue.Str(JgsBuiltins.MarkerWord(((BoxChartPlot)entry.Target).MarkerStyle)),
                (entry, value, line, col) =>
                {
                    var plot = (BoxChartPlot)entry.Target;
                    plot.MarkerStyle = JgsBuiltins.ParseMarkerWord(
                        JgsBuiltins.StrOf("MarkerStyle", value, line, col), plot.MarkerStyle);
                });
            Put(table, "WhiskerLineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((BoxChartPlot)entry.Target).WhiskerLineStyle)),
                (entry, value, line, col) =>
                {
                    var plot = (BoxChartPlot)entry.Target;
                    plot.WhiskerLineStyle = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("WhiskerLineStyle", value, line, col), plot.WhiskerLineStyle);
                });
            Put(table, "Orientation",
                entry => JgsValue.Str(((BoxChartPlot)entry.Target).Horizontal ? "horizontal" : "vertical"),
                (entry, value, line, col) => ((BoxChartPlot)entry.Target).Horizontal =
                    JgsBuiltins.BoxOrientationWord(value, line, col));

            // The summary the chart actually drew, which is the thing a script wants to check and
            // cannot work out from YData without repeating the quartile convention by hand.
            Put(table, "MedianValues",
                entry => Row([.. ((BoxChartPlot)entry.Target).Groups().Select(g => g.Summary.Median)]));
            Put(table, "BoxPositions",
                entry => Row([.. ((BoxChartPlot)entry.Target).Groups().Select(g => g.Position)]));
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

        // MATLAB always answers with limits, so the automatic ones are worked out rather than reported
        // as absent — and setting them is what makes them stop moving with the data.
        Put(table, "BubbleSizeLimits",
            entry => Row(Axes(entry).ResolveBubbleLimits().Min, Axes(entry).ResolveBubbleLimits().Max),
            (entry, value, line, col) =>
            {
                double[] pair = Numbers("BubbleSizeLimits", value, 2, line, col);
                Axes(entry).BubbleSizeLimits = pair[1] > pair[0]
                    ? new DataRange(pair[0], pair[1])
                    : throw new JgsRuntimeException(line, col,
                        "BubbleSizeLimits is two increasing numbers, such as [0 100].");
            });

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

        Put(table, "Label",
            entry => JgsValue.Str(Ruler(entry).Label),
            (entry, value, line, col) => Ruler(entry).Label = JgsBuiltins.StrOf("Label", value, line, col));

        // A ruler handle is the only handle a script has for one side of a two-sided axes — plotyy
        // answers with two of them — so the axes-shaped spellings answer here too, on the ruler whose
        // direction they name and nowhere else.
        AddDirected(table, "XLim", AxisOrientation.Horizontal, "Limits");
        AddDirected(table, "YLim", AxisOrientation.Vertical, "Limits");
        AddDirected(table, "XColor", AxisOrientation.Horizontal, "Color");
        AddDirected(table, "YColor", AxisOrientation.Vertical, "Color");
        AddDirected(table, "XLabel", AxisOrientation.Horizontal, "Label");
        AddDirected(table, "YLabel", AxisOrientation.Vertical, "Label");
        AddDirected(table, "XScale", AxisOrientation.Horizontal, "Scale");
        AddDirected(table, "YScale", AxisOrientation.Vertical, "Scale");
    }

    /// <summary>
    /// Adds a letter-shaped spelling of a ruler property, which answers only on a ruler pointing the
    /// way the letter says. Reading <c>YLim</c> off an x ruler is a mistake worth naming rather than
    /// an alias worth honouring.
    /// </summary>
    private static void AddDirected(
        IDictionary<string, GraphicsProperty> table, string name, AxisOrientation direction, string underlying)
    {
        GraphicsProperty target = table[underlying];
        string spelling = name;

        JgsHandleEntry Checked(JgsHandleEntry entry, int line, int col)
        {
            if (Ruler(entry).Orientation != direction)
            {
                throw new JgsRuntimeException(line, col,
                    $"{spelling} names a {(direction == AxisOrientation.Horizontal ? "horizontal" : "vertical")} "
                    + $"ruler, and this handle is the other one. Its own limits and label answer to "
                    + $"Limits and Label.");
            }

            return entry;
        }

        table[spelling] = new GraphicsProperty(
            spelling,
            entry => target.Read(Checked(entry, 0, 0)),
            target.Write is null
                ? null
                : (entry, value, line, col) => target.Write(Checked(entry, line, col), value, line, col));
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
    /// The transparency of one colour, under the name MATLAB gives it. A colour that has not been
    /// chosen yet is fully opaque, which is what MATLAB answers too.
    /// </summary>
    private static void AddAlpha(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, Color?> read,
        Action<JgsHandleEntry, Color> write)
    {
        Put(table, name,
            entry => JgsValue.Number((read(entry)?.A ?? 255) / 255.0),
            (entry, value, line, col) =>
            {
                double alpha = JgsBuiltins.NumOf(name, value, line, col);
                if (alpha is < 0 or > 1)
                {
                    throw new JgsRuntimeException(line, col, $"{name} is between 0 and 1, but got {alpha:G6}.");
                }

                write(entry, JgsBuiltins.WithAlpha(read(entry) ?? Colors.Black, alpha));
            });
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

    /// <summary>
    /// What a pie actually writes beside its wedges — the labels it was given, or the percentages it
    /// worked out. Reading back what is drawn rather than what was set is the useful answer here,
    /// and it is the same rule the contour's LevelList already follows.
    /// </summary>
    private static string[] WrittenLabels(PiePlot pie)
    {
        IReadOnlyList<PieSlice> slices = pie.Slices();
        var labels = new string[slices.Count];
        for (int i = 0; i < slices.Count; i++)
        {
            labels[i] = pie.LabelOf(slices[i].Index, slices[i].Fraction);
        }

        return labels;
    }

    /// <summary>A rows-by-columns grid of numbers, as the matrix a script would have written.</summary>
    private static JgsValue Grid(double[,] values) =>
        JgsMatrix.Build(values.GetLength(0), values.GetLength(1), (r, c) => values[r, c]);

    /// <summary>
    /// Renames a heatmap's columns or rows, and points the ruler at the new names — a name that is
    /// not on the ruler is not on the chart, however faithfully the plot remembers it.
    /// </summary>
    private static void Rename(JgsHandleEntry entry, JgsValue value, int line, int col, bool x)
    {
        var plot = (HeatmapPlot)entry.Target;
        string[] names = TextRows(x ? "XData" : "YData", value, line, col);
        int expected = x ? plot.Columns : plot.Rows;
        if (names.Length != expected)
        {
            throw new JgsRuntimeException(line, col,
                $"{(x ? "XData" : "YData")}: there are {names.Length} names but {expected} are needed.");
        }

        if (x)
        {
            plot.XData = names;
        }
        else
        {
            plot.YData = names;
        }

        plot.Axes?.LabelCells(plot);
    }

    /// <summary>The axes a chart is drawn on, when it is drawn on one.</summary>
    private static AxesModel? Owner(JgsHandleEntry entry) => (entry.Target as PlotObject)?.Axes;

    private static AxesModel Owning(JgsHandleEntry entry, int line, int col) =>
        Owner(entry) ?? throw new JgsRuntimeException(line, col,
            "this property belongs to the axes, and the chart is not on one.");

    /// <summary>A cell of char rows or an array of strings, as plain text.</summary>
    private static string[] TextRows(string what, JgsValue value, int line, int col)
    {
        JgsValue[] elements = value.Type switch
        {
            JgsType.Cell => value.AsCell,
            JgsType.Array when !value.IsPacked && !value.IsPackedComplex => value.BoxedElements(),
            JgsType.String => [value],
            _ => throw new JgsRuntimeException(line, col,
                $"{what} is a cell of char rows or an array of strings."),
        };

        var words = new string[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, $"{what}: element {i + 1} is not text.");
            }

            words[i] = elements[i].AsString;
        }

        return words;
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
