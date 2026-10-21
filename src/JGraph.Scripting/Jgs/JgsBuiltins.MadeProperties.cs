using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What every graphics maker takes besides its data (open items 136 and 137, ADR 0255): any property
/// of the object it makes as a trailing <c>'Name', value</c> pair, and a group or a transform in
/// place of the axes.
/// <para>
/// R2025b's makers read the object's whole property table after the data and the line spec
/// (<c>plot(ax, x, y, 'r-o', 'Tag', 'first')</c>, <c>bar(ax, y, 'UserData', 7)</c>,
/// <c>light(ax, 'Tag', t)</c>; probe <c>oi136_options</c>), where each maker here read a list of its
/// own and refused the rest. Teaching each parser the whole table would be twenty edits that drift;
/// this wraps the maker instead. The trailing pairs are found from the end, a pair at a time, while
/// the word in the name's place is one the maker reads itself or a property of what it makes; the
/// maker's own stay where they are, the rest are taken out and set on every object it made, through
/// the property table, which resolves a start of a name and refuses an unknown one in R2025b's
/// words (<c>Unrecognized property Foo for class Line.</c>). A refused one takes back what the call
/// made, as R2025b's leaves nothing behind.
/// </para>
/// <para>
/// A group or a transform first (<c>line(g, x, y)</c>, <c>plot(g, x, y)</c>), or named by
/// <c>'Parent'</c>, puts what is made in the group: drawn in the group's axes as though it were
/// held, which R2025b's makers do for a group however the axes' <c>NextPlot</c> stands (probe
/// <c>oi136_unheld</c>), and joined to the group. An axes named by <c>'Parent'</c> is where the
/// maker draws, over an axes given first, as R2025b's <c>'Parent'</c> wins.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// How one maker takes what is not its data.
    /// </summary>
    /// <param name="Made">The model class of what it makes, whose property table the trailing names are read against.</param>
    /// <param name="Own">The option names the maker reads itself; they stay where they are.</param>
    /// <param name="GroupFirst">Whether R2025b's maker takes a group or a transform first (<c>quiver</c>, <c>scatter3</c>, <c>surf</c>, <c>mesh</c> and <c>pcolor</c> read one as data).</param>
    /// <param name="KeepsOptions">The maker sets every property itself, in order (<c>line</c>, <c>text</c>, <c>surface</c>, <c>image</c>): only a group first and a <c>'Parent'</c> naming a group or an axes are taken.</param>
    /// <param name="OwnParent">The maker reads its parent itself (<c>hggroup</c>, <c>hgtransform</c>).</param>
    internal sealed record MadeShape(
        Type Made,
        IReadOnlyCollection<string> Own,
        bool GroupFirst = true,
        bool KeepsOptions = false,
        bool OwnParent = false)
    {
        private readonly HashSet<string> _own = new(Own, StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the maker reads <paramref name="word"/> itself.</summary>
        public bool Reads(string word) => _own.Contains(word);
    }

    /// <summary>
    /// Wraps a maker so that it takes any property of what it makes as a trailing option, and a group
    /// or a transform in place of the axes.
    /// </summary>
    internal static Func<IReadOnlyList<JgsValue>, int, int, JgsValue> Making(
        MadeShape shape, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
        (args, line, col) => MakeWithProperties(shape, args, line, col, body);

    private static JgsValue MakeWithProperties(
        MadeShape shape, IReadOnlyList<JgsValue> args, int line, int col,
        Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body)
    {
        IReadOnlyList<JgsValue> rest = args;
        JgsGraphicsGroup? group = null;
        if (shape.GroupFirst && rest.Count > 0 && GroupNamedBy(rest[0]) is { } leading)
        {
            group = leading;
            rest = [.. rest.Skip(1)];
        }

        List<(string Name, JgsValue Value)> foreign;
        JgsHandleEntry? parent;
        (rest, foreign, parent) = PeelMadeProperties(shape, rest);

        AxesModel? axes = null;
        switch (parent?.Target)
        {
            case JgsGraphicsGroup named:
                group = named;
                break;
            case AxesModel given:
                group = null;
                axes = given;
                break;
        }

        if (group is not null)
        {
            axes = group.Home ?? JG.Gca();
        }

        // A parent named apart from the data replaces an axes given first.
        if (axes is not null && rest.Count > 0 && !IsTextScalar(rest[0])
            && JgsHandleRegistry.TryGet(rest[0], out JgsHandleEntry? first) && first.Target is AxesModel)
        {
            rest = [.. rest.Skip(1)];
        }

        // What the call makes is told from what the axes it draws in held before it, so that a
        // refusal - the maker's own or a property's - takes back all of it.
        AxesModel? watched = axes ?? LeadingAxes(rest) ?? JG.CurrentAxesOrNull;
        var before = new HashSet<GraphObject>(DrawnIn(watched), ReferenceEqualityComparer.Instance);
        try
        {
            JgsValue made = OnAxes(axes, () => group is null ? body(rest, line, col) : Held(() => body(rest, line, col)));
            if (foreign.Count == 0 && group is null)
            {
                return made;
            }

            List<JgsHandleEntry> created = EntriesMade(made);
            bool createFcn = false;
            foreach (JgsHandleEntry entry in created)
            {
                foreach ((string name, JgsValue value) in foreign)
                {
                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                    createFcn |= name.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase);
                }

                if (group is not null)
                {
                    JgsGraphicsProperties.Set(entry, "Parent", JgsHandleRegistry.For(group), line, col);
                }
            }

            // R2025b's one moment for CreateFcn: after every option is set, with the object as gcbo.
            if (createFcn)
            {
                foreach (JgsHandleEntry entry in created)
                {
                    JgsCallbackDispatcher.Current?.FireCreateFcn(entry.Target);
                }
            }

            return made;
        }
        catch (JgsRuntimeException)
        {
            AxesModel? drawnIn = axes ?? LeadingAxes(rest) ?? JG.CurrentAxesOrNull;
            foreach (GraphObject left in DrawnIn(drawnIn).Where(o => !before.Contains(o)).ToList())
            {
                Unmake(left);
            }

            JgsHandleRegistry.DropUnreachable();
            throw;
        }
    }

    /// <summary>
    /// Finds the trailing pairs from the end, a pair at a time, while the word in the name's place is
    /// one the maker reads or a property of what it makes: the maker's own are left where they are,
    /// a <c>'Parent'</c> naming a group or an axes is taken out to place what is made, and the rest
    /// are taken out to be set on it.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Remaining, List<(string Name, JgsValue Value)> Foreign, JgsHandleEntry? Parent)
        PeelMadeProperties(MadeShape shape, IReadOnlyList<JgsValue> args)
    {
        var taken = new HashSet<int>();
        var foreign = new List<(string Name, JgsValue Value)>();
        JgsHandleEntry? parent = null;
        IReadOnlyDictionary<string, GraphicsProperty> table = JgsGraphicsProperties.TableFor(shape.Made);
        for (int i = args.Count - 2; i >= 0; i -= 2)
        {
            if (!IsTextScalar(args[i]))
            {
                break;
            }

            string word = TextOf(args[i]);
            bool own = shape.Reads(word);
            if (!own && !shape.KeepsOptions && !NamesAProperty(word, table))
            {
                break;
            }

            // The last 'Parent' wins; scanning from the end, that is the first one met.
            if (!shape.OwnParent && word.Equals("Parent", StringComparison.OrdinalIgnoreCase)
                && JgsHandleRegistry.TryGet(args[i + 1], out JgsHandleEntry? named)
                && named.Target is AxesModel or JgsGraphicsGroup)
            {
                // A maker that reads an axes parent itself keeps it (patch's colour depends on it).
                if (own && named.Target is AxesModel && parent is null)
                {
                    continue;
                }

                parent ??= named;
                taken.Add(i);
                continue;
            }

            // CreateFcn runs once every other property is set, so it is always set here, for every
            // maker; line, which sets every option itself, only stored it (R2025b, probe oi136_create).
            bool createFcn = word.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase);
            if ((own || shape.KeepsOptions) && !createFcn)
            {
                continue;
            }

            foreign.Insert(0, (word, args[i + 1]));
            taken.Add(i);
        }

        if (taken.Count == 0)
        {
            return (args, foreign, parent);
        }

        var rest = new List<JgsValue>(args.Count - (2 * taken.Count));
        for (int i = 0; i < args.Count; i++)
        {
            if (taken.Contains(i))
            {
                i++; // and its value
                continue;
            }

            rest.Add(args[i]);
        }

        return (rest, foreign, parent);
    }

    /// <summary>
    /// Whether a word in a name's place is a property of what is made: the whole name in either
    /// dialect; in the MATLAB dialect also the start of one, and any capitalised word a line spec or a
    /// colour could not be, which the property table then refuses in R2025b's words.
    /// </summary>
    private static bool NamesAProperty(string word, IReadOnlyDictionary<string, GraphicsProperty> table)
    {
        if (table.ContainsKey(word))
        {
            return true;
        }

        if (!JgsRunningDialect.ThreadIsMatlab || word.Length < 2 || IsLineSpecWord(word)
            || word.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return char.IsUpper(word[0])
            || table.Values.Any(property => property.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The group or transform a value names, when it is one.</summary>
    private static JgsGraphicsGroup? GroupNamedBy(JgsValue value) =>
        !IsTextScalar(value) && JgsHandleRegistry.TryGet(value, out JgsHandleEntry? entry)
            ? entry.Target as JgsGraphicsGroup
            : null;

    /// <summary>The axes an argument list names first, when it does.</summary>
    private static AxesModel? LeadingAxes(IReadOnlyList<JgsValue> args) =>
        args.Count > 0 && !IsTextScalar(args[0]) && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
            ? entry.Target as AxesModel
            : null;

    /// <summary>Runs a maker on the current axes as though it were held, and puts the hold back.</summary>
    private static JgsValue Held(Func<JgsValue> make)
    {
        bool holding = JG.IsHolding;
        JG.Hold(true);
        try
        {
            return make();
        }
        finally
        {
            JG.Hold(holding);
        }
    }

    /// <summary>What an axes draws: its plots, its text and its lights.</summary>
    private static IEnumerable<GraphObject> DrawnIn(AxesModel? axes) =>
        axes is null
            ? []
            : axes.Plots.Cast<GraphObject>().Concat(axes.Annotations).Concat(axes.Lights);

    /// <summary>The handle entries a maker answered with.</summary>
    private static List<JgsHandleEntry> EntriesMade(JgsValue made)
    {
        var entries = new List<JgsHandleEntry>();
        int count = made.Type == JgsType.Array ? made.ArrayLength : made.Type == JgsType.Number ? 1 : 0;
        for (int i = 0; i < count; i++)
        {
            JgsValue one = made.Type == JgsType.Array ? made.ElementAt(i) : made;
            if (JgsHandleRegistry.TryGet(one, out JgsHandleEntry? entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Takes back an object a refused call made: out of its group and its axes, quietly, because to
    /// the script it was never made.
    /// </summary>
    private static void Unmake(GraphObject made)
    {
        JgsGraphicsProperties.GroupOwning(made)?.Release(made);
        using (GraphObjectLifecycle.SuppressNotifications())
        {
            switch (made)
            {
                case PlotObject plot when plot.Parent is AxesModel axes:
                    axes.Plots.Remove(plot);
                    break;
                case AnnotationObject note when note.Parent is AxesModel axes:
                    axes.Annotations.Remove(note);
                    break;
                case LightModel light when light.Parent is AxesModel axes:
                    axes.Lights.Remove(light);
                    break;
            }
        }
    }

    // --- the makers' shapes ----------------------------------------------------------------------
    // Properties, not fields: the option lists they name live in other files of this partial class,
    // whose static fields may not be set yet while this file's are.

    private static readonly string[] RectangleOptionNames =
        ["Position", "Curvature", "FaceColor", "EdgeColor", "LineWidth", "DisplayName"];

    private static readonly string[] LightOptionNames = ["Position", "Color", "Style", "Visible"];

    private static readonly string[] GroupOptionNames = ["Matrix", "Tag", "Visible", "Parent"];

    /// <summary><c>plot</c>, the log plots and <c>stairs</c>, which share plot's option list.</summary>
    internal static MadeShape PlotMade => new(typeof(LinePlot), PlotOptionNames);

    internal static MadeShape Plot3Made => new(typeof(Line3DPlot), LineOptionNames);

    internal static MadeShape LineMade => new(typeof(LinePlot), [], KeepsOptions: true);

    internal static MadeShape StemMade => new(typeof(StemPlot), StemOptions.Names);

    internal static MadeShape Stem3Made => new(typeof(Stem3DPlot), Stem3OptionNames);

    internal static MadeShape ScatterMade => new(typeof(ScatterPlot), MarkerChartOptionNames);

    internal static MadeShape Scatter3Made => new(typeof(Scatter3DPlot), Scatter3OptionNames, GroupFirst: false);

    internal static MadeShape BarMade => new(typeof(BarPlot), BarOptionNames);

    internal static MadeShape AreaMade => new(typeof(AreaPlot), AreaOptionNames);

    internal static MadeShape ErrorBarMade => new(typeof(ErrorBarPlot), ErrorBarOptions.Names);

    internal static MadeShape HistogramMade => new(typeof(HistogramPlot), HistogramChartOptions.Names);

    /// <summary><c>fill</c>, <c>fill3</c> and <c>patch</c>; the coordinates by name and an axes parent are patch's own too.</summary>
    internal static MadeShape PatchMade => new(typeof(PatchPlot), [.. PatchOptionNames, "XData", "YData", "ZData", "Parent"]);

    internal static MadeShape RectangleMade => new(typeof(PatchPlot), RectangleOptionNames);

    internal static MadeShape LightMade => new(typeof(LightModel), LightOptionNames);

    internal static MadeShape TextMade => new(typeof(TextAnnotation), [], KeepsOptions: true);

    internal static MadeShape SurfaceMade => new(typeof(SurfacePlot), [], KeepsOptions: true);

    internal static MadeShape ImageMade => new(typeof(ImagePlot), [], KeepsOptions: true);

    internal static MadeShape QuiverMade => new(typeof(QuiverPlot), QuiverOptionNames, GroupFirst: false);

    internal static MadeShape PcolorMade => new(typeof(SurfacePlot), [], GroupFirst: false);

    internal static MadeShape GroupMade => new(typeof(JgsGraphicsGroup), GroupOptionNames, GroupFirst: false, OwnParent: true);
}
