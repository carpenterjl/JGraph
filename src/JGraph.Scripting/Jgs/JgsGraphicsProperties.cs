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
        JgsGraphicsRoot => "root",
        JgsGraphicsGroup { Transforms: true } => "hgtransform",
        JgsGraphicsGroup => "hggroup",
        FigureModel => "figure",
        ContextMenuModel => "uicontextmenu",
        MenuItemModel => "uimenu",

        // A circle is a different class in MATLAB, and findobj(gcf, 'Type', 'polaraxes') is how a
        // script finds one. Here it is a mode, so the mode is what the name is read off — the same
        // shape of decision as a stairstep line below.
        AxesModel { IsPolar: true } => "polaraxes",
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
        StemPlot or Stem3DPlot => "stem",

        // MATLAB builds bar3 out of one surface per column and pie3 out of a surface per wedge, and
        // a script that looks for either goes looking for surfaces. Each of these is one object
        // rather than that handful, but the name it answers to is still the one MATLAB uses.
        Bar3DPlot or Pie3DPlot => "surface",
        HistogramPlot => "histogram",
        PolarHistogramPlot => "histogram",
        ErrorBarPlot => "errorbar",
        SurfacePlot => "surface",
        ContourPlot => "contour",
        PatchPlot => "patch",
        QuiverPlot => "quiver",
        ImagePlot or RgbImagePlot => "image",
        // A label sized to what it says is a text object; one given a box of its own is a textbox,
        // which is the same reading as the arrows below and as the stairstep line above.
        TextAnnotation { Box: not null } => "textbox",
        TextAnnotation => "text",

        // MATLAB mints a distinct object for each shape of arrow, and a script looking for one asks
        // by that name. Here they are one object whose properties say which shape it is — the same
        // decision as the stairstep line above, and read the same way.
        ArrowAnnotation { Text.Length: > 0 } => "textarrow",
        ArrowAnnotation { ShowTailHead: true } => "doublearrow",
        ArrowAnnotation { ShowHead: false } => "line",
        ArrowAnnotation => "arrow",
        EllipseAnnotation => "ellipse",
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
    /// <summary>
    /// Every group a script has made, so that an object can be asked which one owns it. Groups sit
    /// beside the render tree (see <see cref="JgsGraphicsGroup"/>), so there is nowhere in the model
    /// to hang this — and a live figure holds only its own objects, which is the point.
    /// </summary>
    private static readonly List<JgsGraphicsGroup> Groups = new();

    /// <summary>Records a group so that <c>Parent</c> can find it from one of its members.</summary>
    public static void Remember(JgsGraphicsGroup group)
    {
        lock (Groups)
        {
            Groups.Add(group);
        }
    }

    /// <summary>Forgets every group — what a fresh run means.</summary>
    public static void ForgetGroups()
    {
        lock (Groups)
        {
            Groups.Clear();
        }
    }

    /// <summary>The group holding <paramref name="target"/>, or null when nothing does.</summary>
    private static JgsGraphicsGroup? GroupOwning(GraphObject target)
    {
        lock (Groups)
        {
            foreach (JgsGraphicsGroup group in Groups)
            {
                if (target is PlotObject plot && group.Members.Contains(plot))
                {
                    return group;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Moves an object to a new owner: into a group, or from one axes to another. Anything else is
    /// refused by name rather than silently ignored, because a script that reparents and gets no
    /// error will believe it happened.
    /// </summary>
    private static void Reparent(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        JgsHandleEntry owner = JgsHandleRegistry.Require(value, line, col);

        // Menus move between figures, context menus, and one another; every such move is a move.
        switch (entry.Target, owner.Target)
        {
            case (ContextMenuModel movingMenu, FigureModel figureOwner):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    (movingMenu.Parent as FigureModel)?.ContextMenus.Remove(movingMenu);
                    figureOwner.ContextMenus.Add(movingMenu);
                }

                return;
            case (MenuItemModel movingItem, ContextMenuModel menuOwner):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    RemoveMenuItem(movingItem);
                    menuOwner.Items.Add(movingItem);
                }

                return;
            case (MenuItemModel movingItem, MenuItemModel itemOwner):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    RemoveMenuItem(movingItem);
                    itemOwner.Items.Add(movingItem);
                }

                return;
            case (ContextMenuModel or MenuItemModel, _):
                throw new JgsRuntimeException(line, col,
                    $"A menu belongs to a figure, a context menu or another menu, not to a {owner.TypeName}.");
        }

        if (entry.Target is not PlotObject plot)
        {
            throw new JgsRuntimeException(line, col,
                $"Only a drawn object can be given a new parent, and this handle names a {entry.TypeName}.");
        }

        switch (owner.Target)
        {
            case JgsGraphicsGroup group:
                group.Adopt(plot);
                return;
            case AxesModel axes when !ReferenceEquals(plot.Parent, axes):
                // A move, not a deletion: the removal half must not run the plot's DeleteFcn.
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    (plot.Parent as AxesModel)?.Plots.Remove(plot);
                    axes.Plots.Add(plot);
                }

                return;
            case AxesModel:
                return;
            default:
                throw new JgsRuntimeException(line, col,
                    $"A drawn object belongs to an axes or a group, not to a {owner.TypeName}.");
        }
    }

    /// <summary>Takes a menu item out of whichever items collection holds it.</summary>
    private static void RemoveMenuItem(MenuItemModel item)
    {
        switch (item.Parent)
        {
            case ContextMenuModel menu:
                menu.Items.Remove(item);
                break;
            case MenuItemModel owner:
                owner.Items.Remove(item);
                break;
        }
    }

    public static IReadOnlyList<GraphObject> ChildrenOf(GraphObject target)
    {
        var children = new List<GraphObject>();
        switch (target)
        {
            case JgsGraphicsGroup group:
                children.AddRange(group.Members);
                break;
            case FigureModel figure:
                children.AddRange(figure.Axes);
                children.AddRange(figure.Annotations);
                children.AddRange(figure.ContextMenus);
                break;
            case ContextMenuModel menu:
                children.AddRange(menu.Items);
                break;
            case MenuItemModel item:
                children.AddRange(item.Items);
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
            all.Add(axes.RAxis);
            all.Add(axes.ThetaAxis);
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
        // Writable, which is how an object joins a group: MATLAB says it at construction —
        // plot(x, y, 'Parent', g) — and this build says it afterwards, because a 'Parent' the
        // property table understands works for every drawn object at once, where a construction
        // option would have to be taught to each of the drawing verbs one at a time.
        Put(table, "Parent",
            entry => entry.Target.Parent is { } parent
                ? JgsHandleRegistry.For(parent)
                : GroupOwning(entry.Target) is { } group
                    ? JgsHandleRegistry.For(group)
                    : JgsValue.Array([]),
            (entry, value, line, col) => Reparent(entry, value, line, col));
        Put(table, "Children", entry => HandleRow(ChildrenOf(entry.Target)));

        // The common callback and interaction block, on every object at once (M71). The callbacks
        // live on the handle entry — script-side state, gone when the object is — and the scalars
        // map onto what the model already tracks for its own interaction layer.
        AddCallbackSlot(table, "ButtonDownFcn",
            static entry => entry.ButtonDownFcn, static (entry, value) => entry.ButtonDownFcn = value);
        AddCallbackSlot(table, "CreateFcn",
            static entry => entry.CreateFcn, static (entry, value) => entry.CreateFcn = value);
        AddCallbackSlot(table, "DeleteFcn",
            static entry => entry.DeleteFcn, static (entry, value) => entry.DeleteFcn = value);
        Put(table, "Interruptible",
            entry => OnOff(entry.Interruptible),
            (entry, value, line, col) => entry.Interruptible = ToOnOff("Interruptible", value, line, col));
        Put(table, "BusyAction",
            entry => JgsValue.Str(entry.BusyActionQueues ? "queue" : "cancel"),
            (entry, value, line, col) => entry.BusyActionQueues =
                JgsBuiltins.StrOf("BusyAction", value, line, col).ToLowerInvariant() switch
                {
                    "queue" => true,
                    "cancel" => false,
                    var word => throw new JgsRuntimeException(line, col,
                        $"BusyAction is 'queue' or 'cancel', not '{word}'."),
                });
        Put(table, "Selected",
            entry => OnOff(entry.Target.IsSelected),
            (entry, value, line, col) => entry.Target.IsSelected = ToOnOff("Selected", value, line, col));
        Put(table, "SelectionHighlight",
            entry => OnOff(entry.Target.SelectionHighlight),
            (entry, value, line, col) =>
                entry.Target.SelectionHighlight = ToOnOff("SelectionHighlight", value, line, col));
        // HitTest is the model's Selectable: both mean "may a click land on this object", read by
        // the same shared hit test the figure window uses.
        Put(table, "HitTest",
            entry => OnOff(entry.Target.Selectable),
            (entry, value, line, col) => entry.Target.Selectable = ToOnOff("HitTest", value, line, col));
        Put(table, "PickableParts",
            entry => JgsValue.Str(entry.PickableParts),
            (entry, value, line, col) => entry.PickableParts =
                JgsBuiltins.StrOf("PickableParts", value, line, col).ToLowerInvariant() switch
                {
                    "visible" => "visible",
                    "all" => "all",
                    "none" => "none",
                    var word => throw new JgsRuntimeException(line, col,
                        $"PickableParts is 'visible', 'all' or 'none', not '{word}'."),
                });
        Put(table, "BeingDeleted", entry => OnOff(entry.Target.BeingDeleted));

        // The right-click menu an object shows, as a handle to a uicontextmenu. 'UIContextMenu' is
        // the spelling MATLAB used before R2020a; scripts still write it, so both name one slot.
        void PutContextMenu(string spelling) => Put(table, spelling,
            entry => entry.ContextMenu is { } menu ? JgsHandleRegistry.For(menu) : JgsValue.Array([]),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.Array && value.ArrayLength == 0)
                {
                    entry.ContextMenu = null;
                    return;
                }

                JgsHandleEntry menu = JgsHandleRegistry.Require(value, line, col);
                if (menu.Target is not ContextMenuModel)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{spelling} takes a uicontextmenu handle, and this handle names a {menu.TypeName}.");
                }

                entry.ContextMenu = menu.Target;
            });
        PutContextMenu("ContextMenu");
        PutContextMenu("UIContextMenu");

        if (typeof(ContextMenuModel).IsAssignableFrom(type))
        {
            AddCallbackSlot(table, "ContextMenuOpeningFcn",
                static entry => entry.ContextMenuOpeningFcn,
                static (entry, value) => entry.ContextMenuOpeningFcn = value);
        }

        if (typeof(MenuItemModel).IsAssignableFrom(type))
        {
            AddCallbackSlot(table, "MenuSelectedFcn",
                static entry => entry.MenuSelectedFcn,
                static (entry, value) => entry.MenuSelectedFcn = value);

            // MATLAB's Separator, Checked and Enable are on/off words over what reflection would
            // otherwise expose as bools.
            Put(table, "Checked",
                entry => OnOff(((MenuItemModel)entry.Target).Checked),
                (entry, value, line, col) =>
                    ((MenuItemModel)entry.Target).Checked = ToOnOff("Checked", value, line, col));
            Put(table, "Enable",
                entry => OnOff(((MenuItemModel)entry.Target).Enable),
                (entry, value, line, col) =>
                    ((MenuItemModel)entry.Target).Enable = ToOnOff("Enable", value, line, col));
            Put(table, "Separator",
                entry => OnOff(((MenuItemModel)entry.Target).Separator),
                (entry, value, line, col) =>
                    ((MenuItemModel)entry.Target).Separator = ToOnOff("Separator", value, line, col));

            // The spellings MATLAB used before R2017b, still written everywhere: 'Label' is Text,
            // 'Callback' is MenuSelectedFcn. Same slots, older names.
            Put(table, "Label",
                entry => JgsValue.Str(((MenuItemModel)entry.Target).Text),
                (entry, value, line, col) =>
                    ((MenuItemModel)entry.Target).Text = JgsBuiltins.StrOf("Label", value, line, col));
            AddCallbackSlot(table, "Callback",
                static entry => entry.MenuSelectedFcn,
                static (entry, value) => entry.MenuSelectedFcn = value);
        }

        if (typeof(JgsGraphicsGroup).IsAssignableFrom(type))
        {
            Put(table, "Matrix",
                entry => JgsBuiltins.MatrixToRows(((JgsGraphicsGroup)entry.Target).Matrix),
                (entry, value, line, col) =>
                {
                    var group = (JgsGraphicsGroup)entry.Target;
                    if (!group.Transforms)
                    {
                        throw new JgsRuntimeException(line, col,
                            "A plain group has no matrix; use hgtransform for one that moves its members.");
                    }

                    group.SetMatrix(JgsBuiltins.TransformMatrix("hgtransform", value, line, col));
                });

            // Hiding a group hides what is in it, which is most of what a group is for. The members
            // are ordinary objects in the axes, so this reaches through to each of them.
            Put(table, "Visible",
                entry => OnOff(entry.Target.Visible),
                (entry, value, line, col) => ((JgsGraphicsGroup)entry.Target).ShowMembers(
                    ToOnOff("Visible", value, line, col)));
        }

        if (typeof(JgsGraphicsRoot).IsAssignableFrom(type))
        {
            // A rectangle of four numbers is not something the reflection bridge carries, and the
            // root has nothing but rectangles — so its whole surface is curated.
            Put(table, "ScreenSize",
                entry => Row(((JgsGraphicsRoot)entry.Target).ScreenSize));
            Put(table, "MonitorPositions",
                entry => Row(((JgsGraphicsRoot)entry.Target).ScreenSize));
            Put(table, "CurrentFigure", _ => JG.CurrentFigureNumber > 0
                ? JgsValue.Number(JG.CurrentFigureNumber)
                : JgsValue.Array([]));
        }

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
            // M60: these could be read and not written, which is the sixth time this file has
            // carried half of a property — and the one that costs most, because moving a series by
            // writing its data is the ordinary way to redraw one without drawing it again. Writing
            // either coordinate keeps the other, since a series is set as a pair.
            Put(table, "XData",
                entry => SeriesRow((XYPlot)entry.Target, x: true),
                (entry, value, line, col) => SetSeriesData(entry, value, x: true, line, col));
            Put(table, "YData",
                entry => SeriesRow((XYPlot)entry.Target, x: false),
                (entry, value, line, col) => SetSeriesData(entry, value, x: false, line, col));
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

            // CData is what MATLAB calls the same channel, and what scatter's own fourth argument
            // writes — a value a call can set but a handle cannot read back is the gap M54 exists to
            // close, so the name the verb takes is the name the handle answers to.
            Put(table, "CData",
                entry => Row([.. ((ScatterPlot)entry.Target).ColorData ?? []]),
                (entry, value, line, col) => ((ScatterPlot)entry.Target).ColorData =
                    JgsBuiltins.ToDoubles("CData", value, line, col));
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

            // How far the spread moved each marker. MATLAB has no name for this — it says only how
            // wide the spread may be — but the offsets are the one part of a swarm chart a script
            // cannot work out for itself, and reading them is how it checks the chart it asked for.
            Put(table, "XJitterOffsets", entry => Row([.. ((ScatterPlot)entry.Target).XOffsets]));
            Put(table, "YJitterOffsets", entry => Row([.. ((ScatterPlot)entry.Target).YOffsets]));
        }

        if (typeof(PatchPlot).IsAssignableFrom(type))
        {
            // M58: a patch's vertices were unreachable, which fimplicit3 makes worth having — the
            // surface it draws is the whole answer, and a script can only check it by reading the
            // vertices back. Faces are answered the way a script wrote them, counting from one.
            Put(table, "XData", entry => Row([.. ((PatchPlot)entry.Target).X]));
            Put(table, "YData", entry => Row([.. ((PatchPlot)entry.Target).Y]));
            Put(table, "ZData", entry => Row([.. ((PatchPlot)entry.Target).Z]));
            Put(table, "Faces", entry => FaceTable((PatchPlot)entry.Target));

            // M59: 'Vertices' is the other half of the pair a script writes a patch with, and it was
            // missing while 'Faces' answered — so `patch('Faces', F, 'Vertices', V)` could be read
            // back only halfway. It is the same n-by-3 table the verb was handed.
            Put(table, "Vertices", entry => VertexTable((PatchPlot)entry.Target));
        }

        if (typeof(SurfacePlot).IsAssignableFrom(type))
        {
            // M58: a surface's readings were unreachable, which a function plotter makes worth having
            // — the only way to check where fsurf looked is to ask the surface it drew. A surface over
            // a rectangular grid answers with its two vectors; a parametric one with its two grids,
            // which is what MATLAB always answers with.
            Put(table, "XData", entry => ((SurfacePlot)entry.Target).XGrid is { } grid
                ? Grid(grid)
                : Row(((SurfacePlot)entry.Target).X));
            Put(table, "YData", entry => ((SurfacePlot)entry.Target).YGrid is { } grid
                ? Grid(grid)
                : Row(((SurfacePlot)entry.Target).Y));
            Put(table, "ZData", entry => Grid(((SurfacePlot)entry.Target).Z));
        }

        if (typeof(ContourPlot).IsAssignableFrom(type))
        {
            Put(table, "XData", entry => Row(((ContourPlot)entry.Target).X));
            Put(table, "YData", entry => Row(((ContourPlot)entry.Target).Y));
            Put(table, "ZData", entry => Grid(((ContourPlot)entry.Target).Z));
        }

        if (typeof(Line3DPlot).IsAssignableFrom(type))
        {
            // M58: a line in space kept its three coordinates unreachable, so `get(h, 'ZData')` on a
            // plot3 handle named a property the object did not answer to while the same call on a
            // plot handle did. Reflection does not carry them because they are plain arrays behind
            // a SetData that takes all three at once; writing one keeps the other two.
            Put(table, "XData",
                entry => Row([.. ((Line3DPlot)entry.Target).X]),
                (entry, value, line, col) => SetLine3Data(entry, value, 0, line, col));
            Put(table, "YData",
                entry => Row([.. ((Line3DPlot)entry.Target).Y]),
                (entry, value, line, col) => SetLine3Data(entry, value, 1, line, col));
            Put(table, "ZData",
                entry => Row([.. ((Line3DPlot)entry.Target).Z]),
                (entry, value, line, col) => SetLine3Data(entry, value, 2, line, col));
        }

        if (typeof(Scatter3DPlot).IsAssignableFrom(type))
        {
            // A marker chart in space keeps its three coordinates as plain arrays, which reflection
            // does not carry; writing one of them redraws the cloud against the other two.
            Put(table, "XData",
                entry => Row([.. ((Scatter3DPlot)entry.Target).X]),
                (entry, value, line, col) => SetScatter3Data(entry, value, 0, line, col));
            Put(table, "YData",
                entry => Row([.. ((Scatter3DPlot)entry.Target).Y]),
                (entry, value, line, col) => SetScatter3Data(entry, value, 1, line, col));
            Put(table, "ZData",
                entry => Row([.. ((Scatter3DPlot)entry.Target).Z]),
                (entry, value, line, col) => SetScatter3Data(entry, value, 2, line, col));

            Put(table, "SizeData",
                entry => Row([.. ((Scatter3DPlot)entry.Target).SizeData ?? []]),
                (entry, value, line, col) => ((Scatter3DPlot)entry.Target).SizeData =
                    JgsBuiltins.ToDoubles("SizeData", value, line, col));
            Put(table, "CData",
                entry => Row([.. ((Scatter3DPlot)entry.Target).ColorData ?? []]),
                (entry, value, line, col) => ((Scatter3DPlot)entry.Target).ColorData =
                    JgsBuiltins.ToDoubles("CData", value, line, col));
            Put(table, "BubbleDiameters", entry => Row(
                [.. Enumerable.Range(0, ((Scatter3DPlot)entry.Target).SizeData?.Count ?? 0)
                    .Select(((Scatter3DPlot)entry.Target).DiameterAt)]));

            Put(table, "XJitterOffsets", entry => Row([.. ((Scatter3DPlot)entry.Target).XOffsets]));
            Put(table, "YJitterOffsets", entry => Row([.. ((Scatter3DPlot)entry.Target).YOffsets]));
            Put(table, "ZJitterOffsets", entry => Row([.. ((Scatter3DPlot)entry.Target).ZOffsets]));
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

        if (typeof(Pie3DPlot).IsAssignableFrom(type))
        {
            // The same four a flat pie answers with — none of them a type reflection can carry — and
            // the labels read back as what is written rather than as what was set, so an unlabelled
            // pie answers with its percentages.
            Put(table, "Values",
                entry => Row(((Pie3DPlot)entry.Target).Values),
                (entry, value, line, col) => ((Pie3DPlot)entry.Target).Values =
                    JgsBuiltins.ToDoubles("Values", value, line, col));
            Put(table, "Explode",
                entry => Row(((Pie3DPlot)entry.Target).Explode ?? []),
                (entry, value, line, col) => ((Pie3DPlot)entry.Target).Explode =
                    JgsBuiltins.ToDoubles("Explode", value, line, col));
            Put(table, "Labels",
                entry => JgsValue.Cell(Array.ConvertAll(
                    WrittenLabels((Pie3DPlot)entry.Target), JgsValue.Str)),
                (entry, value, line, col) => ((Pie3DPlot)entry.Target).Labels =
                    TextRows("Labels", value, line, col));
            Put(table, "Colormap",
                entry => ValueBridge.ToValue(((Pie3DPlot)entry.Target).Colormap),
                (entry, value, line, col) => ((Pie3DPlot)entry.Target).Colormap =
                    (Colormap)ValueBridge.FromValue(typeof(Colormap), "Colormap", value, line, col)!);
        }

        if (typeof(StemPlot).IsAssignableFrom(type))
        {
            // MATLAB's stem calls the floor BaseValue and the model calls it Baseline. Both names
            // answer, rather than one of them being a near miss a script has to discover.
            Put(table, "BaseValue",
                entry => JgsValue.Number(((StemPlot)entry.Target).Baseline),
                (entry, value, line, col) => ((StemPlot)entry.Target).Baseline =
                    JgsBuiltins.NumOf("BaseValue", value, line, col));
        }

        if (typeof(Stem3DPlot).IsAssignableFrom(type))
        {
            Put(table, "BaseValue",
                entry => JgsValue.Number(((Stem3DPlot)entry.Target).Baseline),
                (entry, value, line, col) => ((Stem3DPlot)entry.Target).Baseline =
                    JgsBuiltins.NumOf("BaseValue", value, line, col));

            // A spatial stem carries its three coordinate arrays rather than a series, so the three
            // names a script reads them by have to be spelled out — and the dash pattern, which the
            // model calls a DashStyle and MATLAB calls a LineStyle.
            Put(table, "XData", entry => Row([.. ((Stem3DPlot)entry.Target).X]));
            Put(table, "YData", entry => Row([.. ((Stem3DPlot)entry.Target).Y]));
            Put(table, "ZData", entry => Row([.. ((Stem3DPlot)entry.Target).Z]));
            Put(table, "LineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((Stem3DPlot)entry.Target).DashStyle)),
                (entry, value, line, col) =>
                {
                    var plot = (Stem3DPlot)entry.Target;
                    plot.DashStyle = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("LineStyle", value, line, col), plot.DashStyle);
                });
        }

        if (typeof(Bar3DPlot).IsAssignableFrom(type))
        {
            // The heights are a matrix, which is the one shape reflection cannot carry, and the row
            // positions answer with the counting numbers a bare bar3 stood the rows on rather than
            // with nothing — the same "say what was drawn" rule the contour's LevelList follows.
            Put(table, "ZData",
                entry => Grid(((Bar3DPlot)entry.Target).ZData),
                (entry, value, line, col) => ((Bar3DPlot)entry.Target).ZData =
                    JgsBuiltins.Matrix("ZData", [value], 0, line, col));
            Put(table, "YData",
                entry => Row(RowPositionsOf((Bar3DPlot)entry.Target)),
                (entry, value, line, col) => ((Bar3DPlot)entry.Target).RowPositions =
                    JgsBuiltins.ToDoubles("YData", value, line, col));
            Put(table, "Colormap",
                entry => ValueBridge.ToValue(((Bar3DPlot)entry.Target).Colormap),
                (entry, value, line, col) => ((Bar3DPlot)entry.Target).Colormap =
                    (Colormap)ValueBridge.FromValue(typeof(Colormap), "Colormap", value, line, col)!);
        }

        if (typeof(PolarHistogramPlot).IsAssignableFrom(type))
        {
            // The three arrays a polar histogram is made of. Writing any one of them re-counts or
            // re-cuts the others, which is the model's business — all this layer does is make sure
            // the names a call accepts are names a handle answers to.
            Put(table, "Data",
                entry => Row(((PolarHistogramPlot)entry.Target).Data),
                (entry, value, line, col) => ((PolarHistogramPlot)entry.Target).Data =
                    JgsBuiltins.ToDoubles("Data", value, line, col));
            Put(table, "BinEdges",
                entry => Row(((PolarHistogramPlot)entry.Target).BinEdges),
                (entry, value, line, col) => ((PolarHistogramPlot)entry.Target).BinEdges =
                    JgsBuiltins.ToDoubles("BinEdges", value, line, col));
            Put(table, "BinCounts",
                entry => Row(((PolarHistogramPlot)entry.Target).BinCounts),
                (entry, value, line, col) => ((PolarHistogramPlot)entry.Target).BinCounts =
                    JgsBuiltins.ToDoubles("BinCounts", value, line, col));
            Put(table, "BinLimits",
                entry => Row(((PolarHistogramPlot)entry.Target).BinLimits),
                (entry, value, line, col) => ((PolarHistogramPlot)entry.Target).BinLimits =
                    JgsBuiltins.ToDoubles("BinLimits", value, line, col));

            // What is drawn, as opposed to what was counted: the same numbers only while the
            // normalization is 'count'.
            Put(table, "Values", entry => Row(((PolarHistogramPlot)entry.Target).BinHeights));
        }

        if (typeof(QuiverPlot).IsAssignableFrom(type))
        {
            // The six arrays an arrow field is made of. The model hides them from the inspector — a
            // column of numbers is not a property row — so reflection cannot find them, and a script
            // that drew a compass or a feather has nothing else to ask about what it drew.
            for (int slot = 0; slot < ArrowFieldNames.Length; slot++)
            {
                int which = slot;
                Put(table, ArrowFieldNames[which],
                    entry => Row(ArrowField((QuiverPlot)entry.Target, which)),
                    (entry, value, line, col) => SetArrowField(entry, value, line, col, which));
            }
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

        if (typeof(BinScatterPlot).IsAssignableFrom(type))
        {
            // The readings, the edges and the counts are all read-only: the grid is worked out from
            // the readings, so a new set of them is a new chart rather than an edit to this one, and
            // the counts are the answer the chart exists to give.
            Put(table, "XData", entry => Row([.. ((BinScatterPlot)entry.Target).X]));
            Put(table, "YData", entry => Row([.. ((BinScatterPlot)entry.Target).Y]));
            Put(table, "XBinEdges", entry => Row([.. ((BinScatterPlot)entry.Target).XBinEdges]));
            Put(table, "YBinEdges", entry => Row([.. ((BinScatterPlot)entry.Target).YBinEdges]));
            Put(table, "Values", entry => Grid(((BinScatterPlot)entry.Target).Values));

            Put(table, "NumBins",
                entry => Row(((BinScatterPlot)entry.Target).NumBinsX, ((BinScatterPlot)entry.Target).NumBinsY),
                (entry, value, line, col) =>
                {
                    (int across, int up) = JgsBuiltins.BinCounts("NumBins", value, line, col);
                    var plot = (BinScatterPlot)entry.Target;
                    plot.NumBinsX = across;
                    plot.NumBinsY = up;
                });

            // MATLAB always answers with limits, so the ones taken from the readings are reported
            // rather than left absent — they are the ends of the edges either way.
            Put(table, "XLimits",
                entry => Ends(((BinScatterPlot)entry.Target).XBinEdges),
                (entry, value, line, col) => ((BinScatterPlot)entry.Target).XLimits =
                    JgsBuiltins.SpanOption("XLimits", value, line, col));
            Put(table, "YLimits",
                entry => Ends(((BinScatterPlot)entry.Target).YBinEdges),
                (entry, value, line, col) => ((BinScatterPlot)entry.Target).YLimits =
                    JgsBuiltins.SpanOption("YLimits", value, line, col));

            Put(table, "ShowEmptyBins",
                entry => OnOff(((BinScatterPlot)entry.Target).ShowEmptyBins),
                (entry, value, line, col) => ((BinScatterPlot)entry.Target).ShowEmptyBins =
                    ToOnOff("ShowEmptyBins", value, line, col));
            Put(table, "Colormap",
                entry => ValueBridge.ToValue(((BinScatterPlot)entry.Target).Colormap),
                (entry, value, line, col) => ((BinScatterPlot)entry.Target).Colormap =
                    (Colormap)ValueBridge.FromValue(typeof(Colormap), "Colormap", value, line, col)!);
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
            AddCallbackSlot(table, "CloseRequestFcn",
                static entry => entry.CloseRequestFcn, static (entry, value) => entry.CloseRequestFcn = value);
            AddCallbackSlot(table, "SizeChangedFcn",
                static entry => entry.SizeChangedFcn, static (entry, value) => entry.SizeChangedFcn = value);
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

        if (typeof(AnnotationObject).IsAssignableFrom(type))
        {
            AddAnnotationAliases(type, table);
        }
    }

    /// <summary>
    /// The spellings an annotation answers to. MATLAB gives every annotation a <c>Position</c> in
    /// normalized figure units, and this is the one place that unit flip lives: MATLAB measures y up
    /// from the bottom of the figure and this model measures it down from the top, so a box written
    /// at y is read back at y and stored in between at <c>1 - y - height</c>.
    /// </summary>
    private static void AddAnnotationAliases(Type type, IDictionary<string, GraphicsProperty> table)
    {
        if (typeof(TextAnnotation).IsAssignableFrom(type))
        {
            Put(table, "String",
                entry => JgsValue.Str(((TextAnnotation)entry.Target).Text),
                (entry, value, line, col) => ((TextAnnotation)entry.Target).Text =
                    JgsBuiltins.AnnotationString("String", value, line, col));
            Put(table, "FontName",
                entry => JgsValue.Str(((TextAnnotation)entry.Target).FontFamily),
                (entry, value, line, col) => ((TextAnnotation)entry.Target).FontFamily =
                    JgsBuiltins.StrOf("FontName", value, line, col));
            Put(table, "BackgroundColor",
                entry => OptionalColorRow(((TextAnnotation)entry.Target).Background),
                (entry, value, line, col) => ((TextAnnotation)entry.Target).Background =
                    NoneOrColor(value, line, col, "textbox"));
            Put(table, "EdgeColor",
                entry => OptionalColorRow(((TextAnnotation)entry.Target).BorderColor),
                (entry, value, line, col) => ((TextAnnotation)entry.Target).BorderColor =
                    NoneOrColor(value, line, col, "textbox"));
            AddAnnotationPosition(
                table,
                entry =>
                {
                    var text = (TextAnnotation)entry.Target;
                    return text.Box is { } box
                        ? (new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Bottom))
                        : (text.Position, text.Position);
                },
                (entry, a, b) =>
                {
                    var text = (TextAnnotation)entry.Target;
                    text.Position = a;
                    text.Box = Rect2D.FromCorners(a, b);
                });
        }

        if (typeof(ArrowAnnotation).IsAssignableFrom(type))
        {
            Put(table, "String",
                entry => JgsValue.Str(((ArrowAnnotation)entry.Target).Text),
                (entry, value, line, col) => ((ArrowAnnotation)entry.Target).Text =
                    JgsBuiltins.AnnotationString("String", value, line, col));
            Put(table, "FontName",
                entry => JgsValue.Str(((ArrowAnnotation)entry.Target).FontFamily),
                (entry, value, line, col) => ((ArrowAnnotation)entry.Target).FontFamily =
                    JgsBuiltins.StrOf("FontName", value, line, col));

            // An arrow is measured by its two ends rather than by a box, which is how MATLAB spells
            // it too: X and Y are each a pair, tail first.
            Put(table, "X",
                entry => Row(((ArrowAnnotation)entry.Target).Start.X, ((ArrowAnnotation)entry.Target).End.X),
                (entry, value, line, col) =>
                {
                    var arrow = (ArrowAnnotation)entry.Target;
                    double[] pair = Numbers("X", value, 2, line, col);
                    arrow.Start = new Point2D(pair[0], arrow.Start.Y);
                    arrow.End = new Point2D(pair[1], arrow.End.Y);
                });
            Put(table, "Y",
                entry => Row(Up(((ArrowAnnotation)entry.Target).Start.Y), Up(((ArrowAnnotation)entry.Target).End.Y)),
                (entry, value, line, col) =>
                {
                    var arrow = (ArrowAnnotation)entry.Target;
                    double[] pair = Numbers("Y", value, 2, line, col);
                    arrow.Start = new Point2D(arrow.Start.X, Up(pair[0]));
                    arrow.End = new Point2D(arrow.End.X, Up(pair[1]));
                });
            AddAnnotationPosition(
                table,
                entry => Corners(((ArrowAnnotation)entry.Target).Start, ((ArrowAnnotation)entry.Target).End),
                (entry, a, b) =>
                {
                    var arrow = (ArrowAnnotation)entry.Target;
                    arrow.Start = a;
                    arrow.End = b;
                });
        }

        if (typeof(ShapeAnnotation).IsAssignableFrom(type))
        {
            Put(table, "FaceColor",
                entry => OptionalColorRow(((ShapeAnnotation)entry.Target).Fill),
                (entry, value, line, col) => ((ShapeAnnotation)entry.Target).Fill =
                    NoneOrColor(value, line, col, "shape"));
            Put(table, "Color",
                entry => OptionalColorRow(((ShapeAnnotation)entry.Target).Stroke),
                (entry, value, line, col) => ((ShapeAnnotation)entry.Target).Stroke =
                    NoneOrColor(value, line, col, "shape"));
            Put(table, "EdgeColor",
                entry => OptionalColorRow(((ShapeAnnotation)entry.Target).Stroke),
                (entry, value, line, col) => ((ShapeAnnotation)entry.Target).Stroke =
                    NoneOrColor(value, line, col, "shape"));
            AddAnnotationPosition(
                table,
                entry => Corners(((ShapeAnnotation)entry.Target).Corner1, ((ShapeAnnotation)entry.Target).Corner2),
                (entry, a, b) =>
                {
                    var shape = (ShapeAnnotation)entry.Target;
                    shape.Corner1 = a;
                    shape.Corner2 = b;
                });
        }
    }

    /// <summary>
    /// The <c>[x y w h]</c> reading of an annotation's two governing points, in MATLAB's units. The
    /// pair of callbacks is what differs per annotation kind; the flip and the box arithmetic do not.
    /// </summary>
    private static void AddAnnotationPosition(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, (Point2D A, Point2D B)> read,
        Action<JgsHandleEntry, Point2D, Point2D> write)
    {
        Put(table, "Position",
            entry =>
            {
                (Point2D a, Point2D b) = read(entry);
                double left = System.Math.Min(a.X, b.X);
                double width = System.Math.Abs(b.X - a.X);
                double height = System.Math.Abs(b.Y - a.Y);
                double bottom = Up(System.Math.Max(a.Y, b.Y));
                return Row(left, bottom, width, height);
            },
            (entry, value, line, col) =>
            {
                double[] box = Numbers("Position", value, 4, line, col);
                write(
                    entry,
                    new Point2D(box[0], Up(box[1] + box[3])),
                    new Point2D(box[0] + box[2], Up(box[1])));
            });
    }

    /// <summary>
    /// Flips one normalized y between MATLAB's origin and this model's. It is its own inverse, which
    /// is why reading and writing a position both call it and neither needs a second spelling.
    /// </summary>
    internal static double Up(double y) => 1 - y;

    private static (Point2D A, Point2D B) Corners(Point2D a, Point2D b) => (a, b);

    private static Point2D Anchor(Point2D a, Point2D b) => new(a.X, System.Math.Max(a.Y, b.Y));

    private static JgsValue OptionalColorRow(Color? color) =>
        color is { } present ? ColorRow(present) : JgsValue.Str("none");

    /// <summary>A colour or the word 'none', which is how MATLAB turns a fill or an edge off.</summary>
    private static Color? NoneOrColor(JgsValue value, int line, int col, string verb) =>
        value.Type == JgsType.String && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : JgsBuiltins.OptionColor(value, line, col, verb);

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

        // The angular rulers answer to the same spellings, which is what makes M54's tick and limit
        // machinery work on a circle without a second copy of any of it.
        AddLimit(table, "RLim", axes => axes.RAxis);
        AddLimit(table, "ThetaLim", axes => axes.ThetaAxis);
        AddLabel(table, "RLabel", axes => axes.RAxis);
        AddScale(table, "RScale", axes => axes.RAxis);
        AddDirection(table, "RDir", axes => axes.RAxis);
        AddTicks(table, "R", axes => axes.RAxis);
        AddTicks(table, "Theta", axes => axes.ThetaAxis);

        // MATLAB's polar axes spells the turn ThetaDir; the model calls it ThetaDirection, and both
        // are the same enum, so one is written in terms of the other rather than beside it.
        Put(table, "ThetaDir",
            entry => JgsValue.Str(Axes(entry).ThetaDirection == Core.Model.ThetaDirection.Clockwise
                ? "clockwise"
                : "counterclockwise"),
            (entry, value, line, col) => Axes(entry).ThetaDirection =
                JgsBuiltins.StrOf("ThetaDir", value, line, col).ToLowerInvariant() switch
                {
                    "clockwise" => Core.Model.ThetaDirection.Clockwise,
                    "counterclockwise" => Core.Model.ThetaDirection.CounterClockwise,
                    var word => throw new JgsRuntimeException(line, col,
                        $"ThetaDir is clockwise or counterclockwise, but got '{word}'."),
                });

        Put(table, "XAxis", entry => JgsHandleRegistry.For(Axes(entry).PrimaryXAxis));
        Put(table, "YAxis", entry => JgsHandleRegistry.For(Axes(entry).ActiveYAxis));
        Put(table, "ZAxis", entry => JgsHandleRegistry.For(Axes(entry).ZAxis));
        Put(table, "RAxis", entry => JgsHandleRegistry.For(Axes(entry).RAxis));
        Put(table, "ThetaAxis", entry => JgsHandleRegistry.For(Axes(entry).ThetaAxis));
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

    /// <summary>
    /// A callback property over an entry slot. Unset reads as empty, the way MATLAB answers a
    /// callback nobody assigned; a write takes a function handle or, to clear, an empty array —
    /// anything else is refused by name. Writing never runs the callback: <c>CreateFcn</c> only
    /// fires at creation, and the others only when their event happens.
    /// </summary>
    private static void AddCallbackSlot(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, JgsValue?> read,
        Action<JgsHandleEntry, JgsValue?> write) =>
        Put(table, name,
            entry => read(entry) ?? JgsValue.Array([]),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.Function)
                {
                    write(entry, value);
                }
                else if (value.Type == JgsType.Array && value.ArrayLength == 0)
                {
                    write(entry, null);
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} is a function handle, such as @(src, event) myCallback(src, event), or [] to clear it.");
                }
            });

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

    /// <summary>
    /// Writes one of a 3-D scatter's three coordinate arrays, keeping the other two. The model takes
    /// all three at once because a point needs all three to exist, so the two that were not written
    /// are handed straight back — and a length that does not match them is the model's own error.
    /// </summary>
    /// <summary>
    /// A patch's faces as a table, one row per face, counting the vertices from one — which is how a
    /// script wrote them and how MATLAB answers. A face with fewer corners than the widest one is
    /// padded with gaps, since a rectangular answer is the only kind a matrix can be.
    /// </summary>
    private static JgsValue FaceTable(PatchPlot patch)
    {
        IReadOnlyList<int[]> faces = patch.Faces;
        int widest = 0;
        foreach (int[] face in faces)
        {
            widest = System.Math.Max(widest, face.Length);
        }

        var table = new double[faces.Count, widest];
        for (int r = 0; r < faces.Count; r++)
        {
            for (int c = 0; c < widest; c++)
            {
                table[r, c] = c < faces[r].Length ? faces[r][c] + 1 : double.NaN;
            }
        }

        return Grid(table);
    }

    /// <summary>A patch's vertices as the n-by-3 table a script writes them in.</summary>
    private static JgsValue VertexTable(PatchPlot patch)
    {
        int count = patch.X.Count;
        var table = new double[count, 3];
        for (int r = 0; r < count; r++)
        {
            table[r, 0] = patch.X[r];
            table[r, 1] = patch.Y[r];
            table[r, 2] = patch.Z[r];
        }

        return Grid(table);
    }

    /// <summary>
    /// Writes one coordinate of a flat series, keeping the other. A series is held as a pair, so
    /// the untouched half is read back out and handed in again — the same shape as the three-
    /// coordinate writer below.
    /// </summary>
    private static void SetSeriesData(
        JgsHandleEntry entry, JgsValue value, bool x, int line, int col)
    {
        var plot = (XYPlot)entry.Target;
        double[] written = JgsBuiltins.ToDoubles(x ? "XData" : "YData", value, line, col);
        double[] kept = Coordinates(plot, x: !x);
        if (written.Length != kept.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{(x ? "XData" : "YData")} has {written.Length} values where the series has {kept.Length}. "
                + "Both coordinates are written together — set them in one call, or draw the series again.");
        }

        plot.SetData(x ? written : kept, x ? kept : written);
    }

    /// <summary>One coordinate of a series as a plain array.</summary>
    private static double[] Coordinates(XYPlot plot, bool x)
    {
        var values = new double[plot.Data.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = x ? plot.Data.GetX(i) : plot.Data.GetY(i);
        }

        return values;
    }

    private static void SetLine3Data(
        JgsHandleEntry entry, JgsValue value, int which, int line, int col)
    {
        var plot = (Line3DPlot)entry.Target;
        double[] written = JgsBuiltins.ToDoubles(
            which switch { 0 => "XData", 1 => "YData", _ => "ZData" }, value, line, col);

        try
        {
            plot.SetData(
                which == 0 ? written : [.. plot.X],
                which == 1 ? written : [.. plot.Y],
                which == 2 ? written : [.. plot.Z]);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    private static void SetScatter3Data(
        JgsHandleEntry entry, JgsValue value, int which, int line, int col)
    {
        var plot = (Scatter3DPlot)entry.Target;
        double[] written = JgsBuiltins.ToDoubles(
            which switch { 0 => "XData", 1 => "YData", _ => "ZData" }, value, line, col);

        double[] x = which == 0 ? written : [.. plot.X];
        double[] y = which == 1 ? written : [.. plot.Y];
        double[] z = which == 2 ? written : [.. plot.Z];

        try
        {
            plot.SetData(x, y, z);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>The two ends of a run of bin edges, which is the span the bins fill.</summary>
    private static JgsValue Ends(IReadOnlyList<double> edges) =>
        edges.Count == 0 ? Row() : Row(edges[0], edges[^1]);

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
    private static string[] WrittenLabels(PiePlot pie) =>
        WrittenLabels(pie.Slices(), pie.LabelOf);

    /// <summary>The same, for the raised pie, which divides the circle by the same arithmetic.</summary>
    private static string[] WrittenLabels(Pie3DPlot pie) =>
        WrittenLabels(pie.Slices(), pie.LabelOf);

    private static string[] WrittenLabels(
        IReadOnlyList<PieSlice> slices, Func<int, double, string> labelOf)
    {
        var labels = new string[slices.Count];
        for (int i = 0; i < slices.Count; i++)
        {
            labels[i] = labelOf(slices[i].Index, slices[i].Fraction);
        }

        return labels;
    }

    /// <summary>
    /// Where a 3-D bar chart's rows actually stand — the positions it was given, or the counting
    /// numbers it stood them on when it was given none.
    /// </summary>
    private static double[] RowPositionsOf(Bar3DPlot bars)
    {
        if (bars.RowPositions is { } given)
        {
            return given;
        }

        var counting = new double[bars.ZData.GetLength(0)];
        for (int r = 0; r < counting.Length; r++)
        {
            counting[r] = r + 1;
        }

        return counting;
    }

    /// <summary>A rows-by-columns grid of numbers, as the matrix a script would have written.</summary>
    private static JgsValue Grid(double[,] values) =>
        JgsMatrix.Build(values.GetLength(0), values.GetLength(1), (r, c) => values[r, c]);

    /// <summary>The six arrays of an arrow field, in the order the model takes them.</summary>
    private static readonly string[] ArrowFieldNames =
        ["XData", "YData", "ZData", "UData", "VData", "WData"];

    private static double[] ArrowField(QuiverPlot plot, int slot) => [.. slot switch
    {
        0 => plot.X,
        1 => plot.Y,
        2 => plot.Z,
        3 => plot.U,
        4 => plot.V,
        _ => plot.W,
    }];

    /// <summary>
    /// Replaces one of an arrow field's six arrays. They only mean anything together — an arrow needs
    /// a tail and a direction — so the whole set goes back at once, and a replacement of the wrong
    /// length is refused rather than leaving a field half rewritten.
    /// </summary>
    private static void SetArrowField(
        JgsHandleEntry entry, JgsValue value, int line, int col, int slot)
    {
        var plot = (QuiverPlot)entry.Target;
        double[][] fields =
            [.. Enumerable.Range(0, ArrowFieldNames.Length).Select(i => ArrowField(plot, i))];
        fields[slot] = JgsBuiltins.ToDoubles(ArrowFieldNames[slot], value, line, col);

        try
        {
            plot.SetData(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
        }
        catch (ArgumentException)
        {
            throw new JgsRuntimeException(line, col,
                $"{ArrowFieldNames[slot]}: an arrow field holds one value per arrow, so this needs "
                + $"{plot.X.Count} values rather than {fields[slot].Length}.");
        }
    }

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
