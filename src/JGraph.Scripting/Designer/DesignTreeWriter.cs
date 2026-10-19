using System.Reflection;
using System.Text.Json;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Designer;

/// <summary>What <see cref="DesignTreeWriter.Write"/> made: the MAT-file, and what it could not put in it.</summary>
/// <param name="Bytes">The MAT-file holding the variable <c>components</c>.</param>
/// <param name="Problems">Each component or property the tree could not hold, one line each; empty when the tree is the whole design.</param>
public sealed record DesignTreeResult(byte[] Bytes, IReadOnlyList<string> Problems)
{
    /// <summary>Whether the tree holds the whole design.</summary>
    public bool IsComplete => Problems.Count == 0;
}

/// <summary>
/// Writes an app's components as App Designer keeps them in <c>appModel.mat</c> (designer plan DS1b):
/// the variable <c>components</c>, a struct whose field <c>UIFigure</c> is the figure, saved as
/// R2025b's MCOS objects, so that App Designer opens a JGraph-made app with its canvas.
/// </summary>
/// <remarks>
/// <para>
/// Each component is its class's base - what a fresh component of the class stores, recorded from
/// R2025b by the probe <c>ds1b_base</c> - and then the properties the generator writes for it
/// (<see cref="DesignCodeWriter.WrittenProperties"/>), stored under their public names in the
/// generator's order. R2025b's <c>load</c> assigns them through the class's own set methods, as
/// <c>createComponents</c> would; a property with a <c>Mode</c> sibling has its mode stored as
/// <c>'manual'</c>, since the base states every stored colour's mode. A grid child's place is its
/// <c>Layout</c>, a <c>GridLayoutOptions</c> object; a container's children are its
/// <c>SerializableChildren</c>, in the order they were made; a callback is its method's name, as
/// App Designer holds it at design time; another component (a context menu, checked nodes) is a
/// reference to that component's object.
/// </para>
/// <para>
/// Every component carries the dynamic property <c>DesignTimeProperties</c> App Designer gives it:
/// its code name, an empty group, its generated lines as App Designer stores them
/// (<see cref="DesignCodeWriter.StoredComponentCode"/>), and for the classes that can show an image
/// the image's path relative to the app. An image is recorded there only: App Designer's loader sets
/// the component's <c>Icon</c> or <c>ImageSource</c> from it.
/// </para>
/// </remarks>
public static partial class DesignTreeWriter
{
    /// <summary>The variable App Designer keeps the tree in.</summary>
    public const string Variable = "components";

    /// <summary>The field of <see cref="Variable"/> that holds the figure, whatever its code name.</summary>
    public const string FigureField = "UIFigure";

    private const string Children = "matlab.ui.container.Container.SerializableChildren";
    private const string Axes = "matlab.ui.control.UIAxes";

    /// <summary>Where an axes keeps each label App Designer's aliases set: its stored part, and whether the text is that part's ruler label.</summary>
    private static readonly Dictionary<string, (string Part, bool InRuler)> LabelParts = new(StringComparer.Ordinal)
    {
        ["TitleString"] = ("Title_IS", false),
        ["SubtitleString"] = ("Subtitle_IS", false),
        ["XLabelString"] = ("XRuler_I", true),
        ["YLabelString"] = ("YRuler_I", true),
        ["ZLabelString"] = ("ZRuler_I", true),
    };
    private const string GridOptions = "matlab.ui.layout.GridLayoutOptions";

    /// <summary>The classes whose design-time controller records an image's relative path.</summary>
    private static readonly HashSet<string> ImageClasses = new(StringComparer.Ordinal)
    {
        "matlab.ui.control.Button",
        "matlab.ui.control.StateButton",
        "matlab.ui.control.ToggleButton",
        "matlab.ui.control.Image",
        "matlab.ui.container.toolbar.PushTool",
        "matlab.ui.container.toolbar.ToggleTool",
        "matlab.ui.container.TreeNode",
    };

    private static readonly Lazy<Dictionary<string, JsonElement>> Bases = new(LoadBases);

    /// <summary>The axes parts the label aliases write, by stored name.</summary>
    private static readonly Lazy<Dictionary<string, JsonElement>> Parts = new(() =>
        Bases.Value.TryGetValue(Axes, out JsonElement axes) && axes.TryGetProperty("parts", out JsonElement parts)
            ? parts.EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    /// <summary>
    /// The MAT-file holding <paramref name="design"/>'s components as App Designer saves them.
    /// <paramref name="appFolder"/> is the folder the app is saved in, where its images are; the
    /// stored code names an image by its full path there, as App Designer's does.
    /// </summary>
    public static DesignTreeResult Write(AppDesign design, DesignCodeStyle? style = null, string? appFolder = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        var problems = new List<string>();
        McosObject root = Tree(design, style, appFolder, problems);
        return new DesignTreeResult(MatMcosWriter.Write(root, Variable, FigureField), problems);
    }

    /// <summary>The figure's object, every component made and filled in.</summary>
    internal static McosObject Tree(AppDesign design, DesignCodeStyle style, string? appFolder, List<string> problems)
    {
        // Every component's object first, so that a property can refer to any of them.
        var objects = new Dictionary<DesignComponent, McosObject>(ReferenceEqualityComparer.Instance);
        foreach (DesignComponent component in design.Components)
        {
            if (Bases.Value.TryGetValue(component.Type, out JsonElement b))
            {
                objects[component] = Build(b);
            }
            else
            {
                problems.Add($"{component.CodeName}: App Designer's tree has no {component.Type} this designer can write.");
            }
        }

        foreach ((DesignComponent component, McosObject o) in objects)
        {
            Fill(design, component, o, objects, style, appFolder, problems);
        }

        return objects.TryGetValue(design.Figure, out McosObject? figure)
            ? figure
            : throw new InvalidOperationException("The design's figure cannot be written.");
    }

    private static void Fill(
        AppDesign design, DesignComponent component, McosObject o, Dictionary<DesignComponent, McosObject> objects,
        DesignCodeStyle style, string? appFolder, List<string> problems, Dictionary<string, List<string>>? setBy = null,
        IReadOnlyList<(string Name, DesignValue Value)>? properties = null)
    {
        // What each property stored, by name, for a save that copies only some properties onto an
        // object App Designer saved (DS6).
        void Put(string property, string name, McosValue value)
        {
            o.Set(name, value);
            if (setBy is not null)
            {
                if (!setBy.TryGetValue(property, out List<string>? names))
                {
                    setBy[property] = names = [];
                }

                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        string? image = null;
        double[]? row = null;
        double[]? column = null;
        var late = new List<(string Name, McosValue Value)>();
        foreach ((string name, DesignValue value) in properties ?? DesignCodeWriter.WrittenProperties(component, style.Theme))
        {
            switch (name)
            {
                case "Layout.Row":
                    row = Numbers(value);
                    continue;
                case "Layout.Column":
                    column = Numbers(value);
                    continue;
            }

            if (value is ImageFileValue file)
            {
                // App Designer's loader sets the image from the relative path; its own tree also
                // holds the full path the app was saved with, which the writer stores where it knows it.
                image = file.RelativePath;
                if (appFolder is not null)
                {
                    Put(name, name, new McosValue.Value(MatData.Text(Path.Combine([appFolder, .. file.Parts]))));
                }

                continue;
            }

            if (component.Type == Axes && LabelParts.TryGetValue(name, out (string Part, bool InRuler) where))
            {
                if (Data(value) is not { } text || !Parts.Value.TryGetValue(where.Part, out JsonElement recorded))
                {
                    problems.Add($"{component.CodeName}.{name}: its value cannot be written into App Designer's tree.");
                    continue;
                }

                // The axes' own text object (its title, its subtitle) or its ruler's label, recorded
                // whole from R2025b, with the design's text in it.
                McosObject part = Build(recorded);
                McosObject label = where.InRuler && part.Get("Label_IS") is McosValue.Refs { Objects: [var inRuler] } ? inRuler : part;
                label.Set("String_I", new McosValue.Value(text));
                Put(name, where.Part, new McosValue.Refs([part]));
                continue;
            }

            if (component.Type == Axes && name == "ToolbarVisible")
            {
                // The axes' default toolbar is not saved, so App Designer gives an axes whose toolbar
                // is hidden one of its own, which is (DesignTimeUIAxesController). One button is
                // enough for App Designer to see it as its own and remove it when the toolbar is
                // shown again; nothing of it is seen while it is hidden.
                if (value is not (OnOffValue { IsOn: false } or CharValue { Text: "off" }))
                {
                    problems.Add($"{component.CodeName}.{name}: its value cannot be written into App Designer's tree.");
                    continue;
                }

                var button = new McosObject("matlab.ui.controls.ToolbarPushButton");
                button.Set("Icon", new McosValue.Value(MatData.Text("restoreview")));
                button.Set("TagMode", new McosValue.Word("manual"));
                button.Set("Tag_I", new McosValue.Value(MatData.Text("restoreview")));
                button.Set("Tooltip", new McosValue.Value(MatData.Text("Restore view")));
                var toolbar = new McosObject("matlab.ui.controls.AxesToolbar");
                toolbar.Set("SerializableChildren", new McosValue.Refs([button]));
                toolbar.Set("VisibleMode", new McosValue.Word("manual"));
                toolbar.Set("Visible_I", new McosValue.Word("off"));
                toolbar.Set("SerializableMode", new McosValue.Word("manual"));
                Put(name, "Toolbar_IS", new McosValue.Refs([toolbar]));
                Put(name, "ToolbarMode", new McosValue.Value(MatData.Text("manual")));
                continue;
            }

            if (name == "AD_ColormapString")
            {
                // App Designer names an axes' colormap by comparing its 256 rows with each map it
                // offers, so the rows must be MATLAB's exactly (an open item).
                problems.Add($"{component.CodeName}.{name}: a colormap other than parula is not yet written into App Designer's tree.");
                continue;
            }

            if (Stored(DesignCodeWriter.WrittenValue(component.Type, name, value), design, objects) is not { } stored)
            {
                problems.Add($"{component.CodeName}.{name}: its value cannot be written into App Designer's tree.");
                continue;
            }

            // A property naming the component's own children (a tree's checked nodes) is assigned
            // once they are its children, as createComponents assigns it after making them.
            if (value is ComponentsValue)
            {
                late.Add((name, stored));
                continue;
            }

            Put(name, name == "AD_AliasedThemeChangedFcn" ? "ThemeChangedFcn" : name, stored);
            if (component.Class?.Property(name) is { HasMode: true })
            {
                Put(name, name + "Mode", new McosValue.Word("manual"));
            }
        }

        // An axes keeps its place in position stores that load applies after the stored names, so
        // the base's (a fresh axes' 400-by-300) would undo the design's Position: an axes the design
        // places stores no place but the design's.
        if (component.Type == Axes && o.Has("Position"))
        {
            DropAxesPlace(o);
        }

        if (row is not null || column is not null)
        {
            var options = new McosObject(GridOptions);
            options.Set("Row", new McosValue.Value(new MatData.Numbers("double", [1, (row ?? [1]).Length], row ?? [1])));
            options.Set("Column", new McosValue.Value(new MatData.Numbers("double", [1, (column ?? [1]).Length], column ?? [1])));
            Put("Layout.Row", "Layout", new McosValue.Refs([options]));
            Put("Layout.Column", "Layout", new McosValue.Refs([options]));
        }

        if (component.Get("ContextMenu") is ComponentsValue { CodeNames: [string menu] }
            && design.Find(menu) is { } contextMenu && objects.TryGetValue(contextMenu, out McosObject? menuObject))
        {
            o.Set("ContextMenu", new McosValue.Refs([menuObject]));
        }

        if (component.Type == "matlab.ui.container.Tab")
        {
            TabSize(component, o);
        }

        // MATLAB's Children order, the last made first: load puts each in front as it attaches it.
        McosObject[] children = component.Children.Where(objects.ContainsKey).Select(c => objects[c]).Reverse().ToArray();
        if (children.Length > 0)
        {
            o.Set(Children, new McosValue.Refs(children));
        }

        // A tab group made in code selects its first tab.
        if (component.Type == "matlab.ui.container.TabGroup" && !late.Exists(static l => l.Name == "SelectedTab")
            && component.Children.FirstOrDefault(objects.ContainsKey) is { } firstTab)
        {
            late.Add(("SelectedTab", new McosValue.Refs([objects[firstTab]])));
        }

        foreach ((string name, McosValue value) in late)
        {
            Put(name, name, value);
        }

        var fields = new List<(string, MatData)>
        {
            ("CodeName", MatData.Text(component.CodeName)),
            ("GroupId", MatData.Text(string.Empty)),
            ("ComponentCode", Column(DesignCodeWriter.StoredComponentCode(component, style, appFolder))),
        };
        if (image is not null || ImageClasses.Contains(component.Type))
        {
            fields.Add(("ImageRelativePath", MatData.Text(image ?? string.Empty)));
        }

        // A responsive app's grid on the figure and the panels in it are App Designer's own
        // responsive containers; its reader refuses a responsive app without them
        // (MLAPPResponsiveAppValidator: "IncompatibleAppVersion", found by ds6_verify).
        if (design.IsResponsive && IsResponsiveContainer(design, component))
        {
            fields.Add(("AppTypeProperties", MatData.Struct(("IsResponsiveContainer", new MatData.Numbers("logical", [1, 1], [1])))));
        }

        o.Dynamic.Add(DynamicProperty("DesignTimeProperties", MatData.Struct([.. fields])));
    }

    /// <summary>The grid a responsive app's figure holds, or a container in it.</summary>
    private static bool IsResponsiveContainer(AppDesign design, DesignComponent component) =>
        DesignResponsive.Grid(design) is { } grid
        && (ReferenceEquals(component, grid) || (component.Type == "matlab.ui.container.Panel" && ReferenceEquals(component.Parent, grid)));

    /// <summary>
    /// Takes out an axes' own position stores, which load applies after the stored names: an axes
    /// the design places stores no place but the design's.
    /// </summary>
    private static void DropAxesPlace(McosObject o)
    {
        foreach (string stale in o.Entries.Select(static e => e.Name)
            .Where(static n => n is "Position_I" or "InnerPosition_I" or "OuterPosition_I"
                || n.EndsWith(".OuterPositionStorage", StringComparison.Ordinal) || n.EndsWith(".InnerPositionStorage", StringComparison.Ordinal))
            .ToArray())
        {
            o.Remove(stale);
        }
    }

    /// <summary>
    /// A tab's size is its tab group's when the tab is made (the code positions the group first),
    /// held in its two position stores; the base holds a fresh group's.
    /// </summary>
    private static void TabSize(DesignComponent component, McosObject o)
    {
        double[] size = component.Parent?.Get("Position") is NumericValue { Data.Count: 4 } groupPosition
            && component.Parent.IsInGrid is false
            ? [groupPosition.Data[2], groupPosition.Data[3]]
            : [250, 210];
        foreach (string store in (string[])["StorageUnitPos", "matlab.ui.internal.mixin.ReadOnlyPositionable.OuterPositionStorage"])
        {
            if (o.Get(store) is McosValue.Refs { Objects: [var unitPos] })
            {
                unitPos.Set("matlab.ui.internal.UnitPos.PositionCache", new McosValue.Value(MatData.Row(1, 1, size[0], size[1])));
            }
        }
    }

    /// <summary>A value as the tree stores it, or null when it has no stored form here.</summary>
    private static McosValue? Stored(DesignValue value, AppDesign design, Dictionary<DesignComponent, McosObject> objects)
    {
        if (value is ComponentsValue named)
        {
            var targets = new List<McosObject>();
            foreach (string codeName in named.CodeNames)
            {
                if (design.Find(codeName) is not { } c || !objects.TryGetValue(c, out McosObject? target))
                {
                    return null;
                }

                targets.Add(target);
            }

            return targets.Count > 0 && targets.All(t => t.ClassName == targets[0].ClassName) ? new McosValue.Refs(targets) : null;
        }

        if (value is DatetimeValue dates)
        {
            // A datetime is an object of its own, storing its milliseconds since 1970 (NaN for NaT).
            var datetime = new McosObject("datetime");
            datetime.Set("data", new McosValue.Value(new MatData.Numbers("double", [dates.Rows, dates.Cols],
                dates.Items.Select(static d => d is { } date ? UnixMilliseconds(date) : double.NaN).ToArray())));
            return new McosValue.Refs([datetime]);
        }

        return Data(value) is { } data ? new McosValue.Value(data) : null;
    }

    /// <summary>Milliseconds from 1970-01-01 to the start of <paramref name="date"/> on the proleptic Gregorian calendar, any year.</summary>
    private static double UnixMilliseconds(DesignDate date)
    {
        // Days from the civil date (H. Hinnant's algorithm), for years .NET's DateTime does not hold.
        long y = date.Year - (date.Month <= 2 ? 1 : 0);
        long era = (y >= 0 ? y : y - 399) / 400;
        long yearOfEra = y - (era * 400);
        long dayOfYear = ((153 * (date.Month + (date.Month > 2 ? -3 : 9))) + 2) / 5 + date.Day - 1;
        long dayOfEra = (yearOfEra * 365) + (yearOfEra / 4) - (yearOfEra / 100) + dayOfYear;
        long days = (era * 146097) + dayOfEra - 719468;
        return days * 86400000.0;
    }

    /// <summary>A plain value as MAT data: a string or an on/off value as char, which the property's set method converts as it would the code's.</summary>
    private static MatData? Data(DesignValue value) => value switch
    {
        NumericValue n => new MatData.Numbers(n.IsLogical ? "logical" : "double", [n.Rows, n.Cols], [.. n.Data]),
        CharValue c => MatData.Text(c.Text),
        StringValue s => MatData.Text(s.Text),
        OnOffValue onOff => MatData.Text(onOff.IsOn ? "on" : "off"),
        CallbackValue callback => MatData.Text(callback.Method),
        CellValue cell => cell.Items.Select(Data).ToArray() is var items && items.All(static i => i is not null)
            ? new MatData.Cells([cell.Rows, cell.Cols], items!)
            : null,
        _ => null,
    };

    private static double[] Numbers(DesignValue value) => value is NumericValue n ? [.. n.Data] : [1];

    private static MatData.Cells Column(IReadOnlyList<string> lines) =>
        new([lines.Count, lines.Count == 0 ? 0 : 1], lines.Select(static l => (MatData)MatData.Text(l)).ToArray());

    /// <summary>A <c>meta.DynamicProperty</c> as <c>addprop</c> makes one and App Designer saves it.</summary>
    private static McosObject DynamicProperty(string name, MatData value)
    {
        var p = new McosObject("meta.DynamicProperty");
        p.Set("DynamicName_", new McosValue.Value(MatData.Text(name)));
        p.Set("DynamicType_", new McosValue.Value(MatData.Text("any")));
        p.Set("DynamicValue_", new McosValue.Value(value));
        p.Set("Name", new McosValue.Value(MatData.Text(name)));
        p.Set("Description", new McosValue.Value(MatData.Text(string.Empty)));
        p.Set("DetailedDescription", new McosValue.Value(MatData.Text(string.Empty)));
        p.Set("GetAccess", new McosValue.Value(MatData.Text("public")));
        p.Set("SetAccess", new McosValue.Value(MatData.Text("public")));
        foreach (string flag in (string[])["Dependent", "Constant", "Abstract", "Transient", "Hidden", "GetObservable", "SetObservable", "AbortSet"])
        {
            p.Set(flag, new McosValue.Literal(0));
        }

        p.Set("NonCopyable", new McosValue.Literal(1));
        p.Set("WeakHandle", new McosValue.Literal(0));
        p.Set("PartialMatchPriority", new McosValue.Value(MatData.Scalar(1)));
        return p;
    }

    // --- The recorded bases --------------------------------------------------------------------

    private static Dictionary<string, JsonElement> LoadBases()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-base.json")
            ?? throw new InvalidOperationException("The designer's component bases are not embedded.");
        using JsonDocument json = JsonDocument.Parse(stream);
        var bases = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement c in json.RootElement.GetProperty("classes").EnumerateArray())
        {
            bases[c.GetProperty("class").GetString()!] = c.Clone();
        }

        return bases;
    }

    /// <summary>A new object from a recorded base: its stored names in order, its helper objects and dynamic properties made anew.</summary>
    private static McosObject Build(JsonElement b)
    {
        var o = new McosObject(b.GetProperty("class").GetString()!);
        foreach (JsonElement e in b.GetProperty("entries").EnumerateArray())
        {
            string name = e.GetProperty("n").GetString()!;
            McosValue value = e.TryGetProperty("word", out JsonElement w) ? new McosValue.Word(w.GetString()!)
                : e.TryGetProperty("literal", out JsonElement l) ? new McosValue.Literal(l.GetInt32())
                : e.TryGetProperty("refs", out JsonElement r) ? new McosValue.Refs(r.EnumerateArray().Select(Build).ToArray())
                : new McosValue.Value(Decode(e.GetProperty("value")));
            o.Set(name, value);
        }

        if (b.TryGetProperty("dynamic", out JsonElement dynamic))
        {
            o.Dynamic.AddRange(dynamic.EnumerateArray().Select(Build));
        }

        return o;
    }

    /// <summary>A recorded value: <c>{c: class, s: size, v: data}</c>, a struct's field names under <c>f</c>.</summary>
    private static MatData Decode(JsonElement e)
    {
        string cls = e.GetProperty("c").GetString()!;
        int[] size = e.GetProperty("s").EnumerateArray().Select(static s => s.GetInt32()).ToArray();
        JsonElement v = e.GetProperty("v");
        return cls switch
        {
            "char" => new MatData.Chars(size, v.GetString()!),
            "cell" => new MatData.Cells(size, v.EnumerateArray().Select(Decode).ToArray()),
            "struct" => new MatData.Structs(size, e.GetProperty("f").EnumerateArray().Select(static f => f.GetString()!).ToArray(),
                v.EnumerateArray().Select(static el => el.EnumerateArray().Select(Decode).ToArray()).ToArray()),
            _ => new MatData.Numbers(cls, size, v.EnumerateArray().Select(static d => d.ValueKind == JsonValueKind.String
                ? d.GetString() switch { "Inf" => double.PositiveInfinity, "-Inf" => double.NegativeInfinity, _ => double.NaN }
                : d.GetDouble()).ToArray()),
        };
    }
}
