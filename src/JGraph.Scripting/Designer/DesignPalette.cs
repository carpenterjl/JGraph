using System.Reflection;
using System.Text.Json;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>Where App Designer puts a component's label (<c>LabeledComponentPositionConfig</c>).</summary>
public enum DesignLabelPlace
{
    /// <summary>No label.</summary>
    None,

    /// <summary>To the left, centred on the component, the text right-aligned.</summary>
    WestMiddle,

    /// <summary>To the left, level with the component's top, the text right-aligned.</summary>
    WestTop,

    /// <summary>Below, centred under the component, the text centred.</summary>
    SouthCenter,
}

/// <summary>
/// One entry of App Designer's component library: its name, its category, the class it makes, the
/// label it comes with, and the components it holds when it makes more than one.
/// </summary>
/// <param name="Name">The name the library shows: <c>Edit Field (Text)</c>.</param>
/// <param name="Category">The library's section: Common, Containers, Figure Tools, Instrumentation.</param>
/// <param name="Type">The class it makes: <c>matlab.ui.control.EditField</c>.</param>
/// <param name="Label">The label it comes with.</param>
public sealed record DesignPaletteEntry(string Name, string Category, string Type, DesignLabelPlace Label = DesignLabelPlace.None)
{
    /// <summary>The class of the components it makes inside the first (a radio button group's buttons); null when none.</summary>
    public string? ChildType { get; init; }

    /// <summary>How many of <see cref="ChildType"/> it makes.</summary>
    public int ChildCount { get; init; }

    /// <summary>Whether it goes in the figure only, whatever the drop is over (the figure tools).</summary>
    public bool FigureOnly { get; init; }

    /// <summary>Why the designer does not make it yet; null when it does (every entry is made since DS4).</summary>
    public string? NotYet { get; init; }

    /// <summary>The custom component the entry makes (designer plan DS7b); null for one of App Designer's own.</summary>
    public DesignUserComponent? UserComponent { get; init; }

    /// <summary>What names the entry among every entry a document offers: its name, or its class for a custom component, whose name may be a built-in's.</summary>
    public string Key => UserComponent is null ? Name : "user:" + UserComponent.ClassName;
}

/// <summary>
/// App Designer's component library (designer plan DS3), as R2025b's App Designer offers it for an
/// app of <c>uifigure</c> components: its four sections in order, each entry sorted by name as the
/// library sorts them, and what each entry makes - the class's design-time component (the adapter's
/// values, recorded by the probe <c>ds3_palette</c>), named and placed as the entry's definition
/// does it, with its label or the components it holds.
/// </summary>
/// <remarks>
/// <para>
/// A labelled entry makes a label and its component: the label sized to its text as App Designer
/// sizes it (the text's width, at least 20, and the larger of 5 and 3% of it more; 22 high), and the
/// two placed by the entry's calculator - west of the component 15 pixels away, centred on it or
/// level with its top, or under it and centred, 15 pixels below. A radio or toggle button group
/// comes with three buttons where App Designer's definition puts them, the first selected; a tab
/// group with two tabs; a tree with a node holding three; a menu bar with two menus; a context menu
/// with two items; a toolbar with a push tool.
/// </para>
/// <para>
/// The design-time sizes and the outer boxes of the components whose labels or ticks stand outside
/// their position (a knob, a slider, a switch) are App Designer's (its component defaults); the
/// sizes agree with what the adapters' design-time components hold in R2025b.
/// </para>
/// </remarks>
public sealed class DesignPalette
{
    /// <summary>The library's sections in order.</summary>
    public static readonly IReadOnlyList<string> Categories = ["Common", "Containers", "Figure Tools", "Instrumentation"];

    /// <summary>How far a labelled component stands from its label (<c>componentOffset</c>).</summary>
    public const double LabelOffset = 15;

    private const string Label = "matlab.ui.control.Label";

    /// <summary>
    /// Where each class's position sits in the box its ticks or labels take (App Designer's
    /// <c>Location</c> less <c>OuterLocation</c>), and that box's size, for the classes where they differ.
    /// </summary>
    private static readonly Dictionary<string, (double Left, double Bottom, double Width, double Height)> Outer = new(StringComparer.Ordinal)
    {
        ["matlab.ui.control.DiscreteKnob"] = (27, 0, 127, 78),
        ["matlab.ui.control.Knob"] = (26, 19, 114, 103),
        ["matlab.ui.control.RangeSlider"] = (7, 30, 166, 39),
        ["matlab.ui.control.Slider"] = (7, 30, 166, 39),
        ["matlab.ui.control.RockerSwitch"] = (0, 21, 20, 87),
        ["matlab.ui.control.ToggleSwitch"] = (0, 21, 20, 87),
        ["matlab.ui.control.Switch"] = (21, 0, 88, 20),
    };

    /// <summary>
    /// The text a compound entry's children hold before they are named among themselves: App
    /// Designer's component defaults, but a tab group's tabs, whose titles its definition empties first.
    /// </summary>
    private static readonly Dictionary<string, string> StartTexts = new(StringComparer.Ordinal)
    {
        ["matlab.ui.control.RadioButton"] = "Radio Button",
        ["matlab.ui.control.ToggleButton"] = "Toggle Button",
        ["matlab.ui.container.TreeNode"] = "Tree Node",
        ["matlab.ui.container.Tab"] = string.Empty,
        ["matlab.ui.container.Menu"] = string.Empty,
    };

    private readonly Dictionary<string, (Size2D Size, IReadOnlyList<KeyValuePair<string, DesignValue>> Values)> _designTime = new(StringComparer.Ordinal);

    private DesignPalette(DesignCatalog catalog)
    {
        Catalog = catalog;
        Load();
        Entries =
        [
            new("Axes", "Common", "matlab.ui.control.UIAxes"),
            new("Button", "Common", "matlab.ui.control.Button"),
            new("Check Box", "Common", "matlab.ui.control.CheckBox"),
            new("Color Picker", "Common", "matlab.ui.control.ColorPicker", DesignLabelPlace.WestMiddle),
            new("Date Picker", "Common", "matlab.ui.control.DatePicker", DesignLabelPlace.WestMiddle),
            new("Drop Down", "Common", "matlab.ui.control.DropDown", DesignLabelPlace.WestMiddle),
            new("Edit Field (Numeric)", "Common", "matlab.ui.control.NumericEditField", DesignLabelPlace.WestMiddle),
            new("Edit Field (Text)", "Common", "matlab.ui.control.EditField", DesignLabelPlace.WestMiddle),
            new("HTML", "Common", "matlab.ui.control.HTML"),
            new("Hyperlink", "Common", "matlab.ui.control.Hyperlink"),
            new("Image", "Common", "matlab.ui.control.Image"),
            new("Label", "Common", Label),
            new("List Box", "Common", "matlab.ui.control.ListBox", DesignLabelPlace.WestTop),
            new("Radio Button Group", "Common", "matlab.ui.container.ButtonGroup") { ChildType = "matlab.ui.control.RadioButton", ChildCount = 3 },
            new("Slider", "Common", "matlab.ui.control.Slider", DesignLabelPlace.WestMiddle),
            new("Slider (Range)", "Common", "matlab.ui.control.RangeSlider", DesignLabelPlace.WestMiddle),
            new("Spinner", "Common", "matlab.ui.control.Spinner", DesignLabelPlace.WestMiddle),
            new("State Button", "Common", "matlab.ui.control.StateButton"),
            new("Table", "Common", "matlab.ui.control.Table"),
            new("Text Area", "Common", "matlab.ui.control.TextArea", DesignLabelPlace.WestTop),
            new("Toggle Button Group", "Common", "matlab.ui.container.ButtonGroup") { ChildType = "matlab.ui.control.ToggleButton", ChildCount = 3 },
            new("Tree", "Common", "matlab.ui.container.Tree") { ChildType = "matlab.ui.container.TreeNode", ChildCount = 4 },
            new("Tree (Check Box)", "Common", "matlab.ui.container.CheckBoxTree") { ChildType = "matlab.ui.container.TreeNode", ChildCount = 4 },
            new("Grid Layout", "Containers", "matlab.ui.container.GridLayout"),
            new("Panel", "Containers", "matlab.ui.container.Panel"),
            new("Tab Group", "Containers", "matlab.ui.container.TabGroup") { ChildType = "matlab.ui.container.Tab", ChildCount = 2 },
            new("Context Menu", "Figure Tools", "matlab.ui.container.ContextMenu") { ChildType = "matlab.ui.container.Menu", ChildCount = 2, FigureOnly = true },
            new("Menu Bar", "Figure Tools", "matlab.ui.container.Menu") { ChildCount = 2, FigureOnly = true },
            new("Toolbar", "Figure Tools", "matlab.ui.container.Toolbar") { ChildType = "matlab.ui.container.toolbar.PushTool", ChildCount = 1, FigureOnly = true },
            new("Gauge", "Instrumentation", "matlab.ui.control.Gauge", DesignLabelPlace.SouthCenter),
            new("Gauge (90 Degree)", "Instrumentation", "matlab.ui.control.NinetyDegreeGauge", DesignLabelPlace.SouthCenter),
            new("Gauge (Linear)", "Instrumentation", "matlab.ui.control.LinearGauge", DesignLabelPlace.SouthCenter),
            new("Gauge (Semicircular)", "Instrumentation", "matlab.ui.control.SemicircularGauge", DesignLabelPlace.SouthCenter),
            new("Knob", "Instrumentation", "matlab.ui.control.Knob", DesignLabelPlace.SouthCenter),
            new("Knob (Discrete)", "Instrumentation", "matlab.ui.control.DiscreteKnob", DesignLabelPlace.SouthCenter),
            new("Lamp", "Instrumentation", "matlab.ui.control.Lamp", DesignLabelPlace.WestMiddle),
            new("Switch", "Instrumentation", "matlab.ui.control.Switch", DesignLabelPlace.SouthCenter),
            new("Switch (Rocker)", "Instrumentation", "matlab.ui.control.RockerSwitch", DesignLabelPlace.SouthCenter),
            new("Switch (Toggle)", "Instrumentation", "matlab.ui.control.ToggleSwitch", DesignLabelPlace.SouthCenter),
        ];
    }

    /// <summary>The library as R2025b's App Designer offers it.</summary>
    public static DesignPalette R2025b { get; } = new(DesignCatalog.R2025b);

    /// <summary>The catalog the entries' classes come from.</summary>
    public DesignCatalog Catalog { get; }

    /// <summary>Every entry, section by section, each section sorted by name.</summary>
    public IReadOnlyList<DesignPaletteEntry> Entries { get; }

    /// <summary>The entry named <paramref name="name"/>, or null.</summary>
    public DesignPaletteEntry? Find(string name) => Entries.FirstOrDefault(e => e.Name == name);

    /// <summary>The size of a new component of <paramref name="type"/>, as its adapter makes it at design time.</summary>
    public Size2D DesignTimeSize(string type) =>
        _designTime.TryGetValue(type, out var made) ? made.Size : new Size2D(100, 22);

    /// <summary>The values the design-time component of <paramref name="type"/> holds that its generated code writes, its position apart.</summary>
    public IReadOnlyList<KeyValuePair<string, DesignValue>> DesignTimeValues(string type) =>
        _designTime.TryGetValue(type, out var made) ? made.Values : [];

    /// <summary>
    /// The size a new label with <paramref name="text"/> is given (<c>updateLabelSize</c>): the
    /// text's width in the label's font (12-pixel Helvetica), at least 20, up to a whole pixel, and the
    /// larger of 5 and 3% of it more; 22 high. App Designer measures the text in its browser; the
    /// shared text measure with the width taken up to a whole pixel gives the width App Designer gave
    /// 73 of the 128 one-line labels in its default font in the apps R2025b ships, and the rest are
    /// wider, as a label resized by hand is.
    /// </summary>
    public static Size2D LabelSize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        double measured = System.Math.Max(UiFit.TextWidth([text], "Helvetica", 12, bold: false, italic: false), 20);
        double width = System.Math.Ceiling(measured - 1e-9);
        double margin = System.Math.Round(System.Math.Max(5, 0.03 * width), MidpointRounding.AwayFromZero);
        return new Size2D(System.Math.Max(width + margin, 15), 22);
    }

    // --- making ----------------------------------------------------------------------------------

    /// <summary>
    /// Makes what <paramref name="entry"/> makes for <paramref name="design"/>, named against what the
    /// design already holds: its components, each with what it holds inside it, not yet in the
    /// design. Their positions are in <paramref name="parent"/>'s pixels, from
    /// <paramref name="corner"/> as the entry's definition places them from the drop's corner; a
    /// drawn <paramref name="size"/> is the component's own (null for the class's). The figure tools
    /// have none.
    /// </summary>
    /// <returns>The components to put in <paramref name="parent"/>, in order, and where in its children each goes (null for last).</returns>
    public IReadOnlyList<(DesignComponent Component, int? Index)> Make(
        DesignPaletteEntry entry, AppDesign design, DesignComponent parent, Point2D corner, Size2D? size = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(parent);
        if (entry.NotYet is { } why)
        {
            throw new InvalidOperationException(why);
        }

        var names = new Names(design);
        if (entry.Label != DesignLabelPlace.None)
        {
            return MakeLabeled(entry, names, corner, size);
        }

        if (entry.Type == "matlab.ui.container.Menu")
        {
            // A menu bar: two menus at the front of the figure's children.
            DesignComponent[] menus = MakeChildren("matlab.ui.container.Menu", entry.ChildCount, names);
            return menus.Select(static (m, i) => (m, (int?)i)).ToArray();
        }

        DesignComponent made = MakeOne(entry.Type, names);
        if (entry.Type == "matlab.ui.container.GridLayout")
        {
            // A grid has no position: it fills its container, or its cells in a grid (DS4).
            return [(made, null)];
        }

        if (entry.FigureOnly)
        {
            foreach (DesignComponent child in MakeChildren(entry.ChildType!, entry.ChildCount, names))
            {
                made.Add(child);
            }

            return [(made, null)];
        }

        Size2D own = size ?? DesignTimeSize(entry.Type);
        made.Set("Position", NumericValue.Row(corner.X, corner.Y, own.Width, own.Height));
        if (entry.ChildType is { } childType)
        {
            DesignComponent[] children = MakeChildren(childType, entry.ChildCount, names);
            Fill(entry, made, children);
        }

        return [(made, null)];
    }

    /// <summary>One component of <paramref name="type"/>: its design-time values, its code name and its text.</summary>
    private DesignComponent MakeOne(string type, Names names)
    {
        (string codeName, string? text) = DesignNames.ForNew(type, names.CodeNames, names.Texts);
        DesignComponent component = New(codeName, type);
        if (text is not null && DesignNames.Of(type)?.TextProperty is { } property)
        {
            component.Set(property, new CharValue(text));
            names.Texts.Add(text);
        }

        names.CodeNames.Add(codeName);
        return component;
    }

    private DesignComponent New(string codeName, string type)
    {
        DesignComponentClass cls = Catalog.ByType(type) ?? throw new InvalidOperationException($"The catalog has no class '{type}'.");
        var component = new DesignComponent(codeName, cls);
        foreach ((string name, DesignValue value) in DesignTimeValues(type))
        {
            component.Set(name, value);
        }

        return component;
    }

    /// <summary>
    /// The components a compound entry makes inside its first, named among themselves
    /// (<c>applyNameChangesForChildren</c>): each one's text counted against the texts the rest hold
    /// so far, which start as the class's own.
    /// </summary>
    private DesignComponent[] MakeChildren(string type, int count, Names names)
    {
        DesignNaming naming = DesignNames.Of(type)!;
        var texts = Enumerable.Repeat(StartTexts.GetValueOrDefault(type, string.Empty), count).ToList();
        var made = new DesignComponent[count];
        for (int i = 0; i < count; i++)
        {
            (string codeName, string? text) = DesignNames.ForNew(type, names.CodeNames, naming.TextProperty is null ? [] : texts);
            DesignComponent component = New(codeName, type);
            if (text is not null && naming.TextProperty is { } property)
            {
                component.Set(property, new CharValue(text));
                texts[i] = text;
                names.Texts.Add(text);
            }

            names.CodeNames.Add(codeName);
            made[i] = component;
        }

        return made;
    }

    /// <summary>Puts a compound entry's children where its definition puts them (<c>_updateChildrenDefaults</c>).</summary>
    private static void Fill(DesignPaletteEntry entry, DesignComponent made, DesignComponent[] children)
    {
        switch (entry.ChildType)
        {
            case "matlab.ui.control.RadioButton":
                Place(children[0], 11, 60, 58, 22);
                Place(children[1], 11, 38, 65, 22);
                Place(children[2], 11, 16, 65, 22);
                children[0].Set("Value", NumericValue.Logical(true));
                break;
            case "matlab.ui.control.ToggleButton":
                Place(children[0], 11, 53, 100, 22);
                Place(children[1], 11, 32, 100, 22);
                Place(children[2], 11, 11, 100, 22);
                children[0].Set("Value", NumericValue.Logical(true));
                break;
            case "matlab.ui.container.TreeNode":
                // One node holding the rest.
                made.Add(children[0]);
                foreach (DesignComponent node in children.Skip(1))
                {
                    children[0].Add(node);
                }

                return;
        }

        foreach (DesignComponent child in children)
        {
            made.Add(child);
        }

        static void Place(DesignComponent c, double x, double y, double w, double h) => c.Set("Position", NumericValue.Row(x, y, w, h));
    }

    /// <summary>
    /// A labelled entry: the label and its component, the label first as App Designer makes them, placed
    /// by the entry's calculator within the box whose bottom-left corner is <paramref name="corner"/>.
    /// A drawn <paramref name="size"/> is the component's own.
    /// </summary>
    private IReadOnlyList<(DesignComponent Component, int? Index)> MakeLabeled(DesignPaletteEntry entry, Names names, Point2D corner, Size2D? size)
    {
        (string codeName, string labelCodeName, string text) = DesignNames.ForNewLabeled(entry.Type, names.CodeNames, names.Texts);
        names.CodeNames.Add(codeName);
        names.CodeNames.Add(labelCodeName);
        names.Texts.Add(text);

        DesignComponent label = New(labelCodeName, Label);
        label.Set("HorizontalAlignment", new CharValue(entry.Label == DesignLabelPlace.SouthCenter ? "center" : "right"));
        label.Set("Text", new CharValue(text));
        DesignComponent component = New(codeName, entry.Type);

        Size2D labelSize = LabelSize(text);
        Size2D own = size ?? DesignTimeSize(entry.Type);
        (double left, double bottom, double outerW, double outerH) = Outer.TryGetValue(entry.Type, out var o)
            ? (o.Left, o.Bottom, o.Width + (own.Width - DesignTimeSize(entry.Type).Width), o.Height + (own.Height - DesignTimeSize(entry.Type).Height))
            : (0, 0, own.Width, own.Height);

        // The calculators give each one's outer corner from the box's bottom-left: the label's, and
        // the component's, whose position then sits inside its outer box.
        double labelX, labelY, componentX, componentY;
        switch (entry.Label)
        {
            case DesignLabelPlace.WestMiddle:
            {
                double centre = (own.Height / 2) + bottom;
                double half = labelSize.Height / 2;
                labelX = 0;
                labelY = half > centre ? 0 : JsRound(centre - half);
                componentX = labelSize.Width + LabelOffset;
                componentY = 0;
                break;
            }

            case DesignLabelPlace.WestTop:
            {
                double top = own.Height + bottom;
                labelX = 0;
                labelY = labelSize.Height > top ? 0 : top - labelSize.Height - 2;
                componentX = labelSize.Width + LabelOffset;
                componentY = 0;
                break;
            }

            default: // SouthCenter
            {
                double half = outerW / 2;
                double labelHalf = labelSize.Width / 2;
                double over = labelHalf - half;
                labelX = over > 0 ? 0 : JsRound(-over);
                componentX = over > 0 ? JsRound(over) : 0;
                labelY = 0;
                componentY = labelSize.Height + LabelOffset;
                break;
            }
        }

        label.Set("Position", NumericValue.Row(corner.X + labelX, corner.Y + labelY, labelSize.Width, labelSize.Height));
        component.Set("Position", NumericValue.Row(corner.X + componentX + left, corner.Y + componentY + bottom, own.Width, own.Height));
        return [(label, null), (component, null)];
    }

    /// <summary>JavaScript's <c>Math.round</c>: halves up.</summary>
    private static double JsRound(double x) => System.Math.Floor(x + 0.5);

    /// <summary>The text a value shows, as App Designer's <c>toString</c> of it: a cell's items joined by commas.</summary>
    internal static string? Text(DesignValue value) => value switch
    {
        CharValue c => c.Text,
        StringValue s => s.Text,
        CellValue cell => string.Join(",", cell.Items.Select(static i => Text(i) ?? string.Empty)),
        _ => null,
    };

    /// <summary>The code names and texts a design holds, which new names are counted against.</summary>
    private sealed class Names
    {
        public Names(AppDesign design)
        {
            foreach (DesignComponent c in design.Components)
            {
                CodeNames.Add(c.CodeName);
                if (DesignNames.Of(c.Type)?.TextProperty is { } property
                    && (c.Get(property) ?? c.Class?.Property(property)?.DefaultFor(DesignTheme.Light)) is { } value
                    && Text(value) is { } text)
                {
                    Texts.Add(text);
                }
            }
        }

        public List<string> CodeNames { get; } = [];

        public List<string> Texts { get; } = [];
    }

    // --- the recorded design-time components -----------------------------------------------------

    private void Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-palette.json")
            ?? throw new InvalidOperationException("The designer's palette is not embedded.");
        using JsonDocument json = JsonDocument.Parse(stream);
        foreach (JsonElement c in json.RootElement.GetProperty("classes").EnumerateArray())
        {
            string type = c.GetProperty("type").GetString()!;
            double[] position = c.GetProperty("position") is { ValueKind: JsonValueKind.Array } p
                ? p.EnumerateArray().Select(static v => v.GetDouble()).ToArray()
                : [];
            Size2D size = position.Length == 4 ? new Size2D(position[2], position[3]) : new Size2D(0, 0);
            string[] lines = c.GetProperty("lines") switch
            {
                { ValueKind: JsonValueKind.Array } a => a.EnumerateArray().Select(static l => l.GetString()!).ToArray(),
                { ValueKind: JsonValueKind.String } one => [one.GetString()!],
                _ => [],
            };
            _designTime[type] = (size, Values(type, lines));
        }
    }

    /// <summary>
    /// What a design-time component's generated lines set, read as the designer reads any app's code:
    /// the lines put after a figure's in a <c>createComponents</c> of their own.
    /// </summary>
    private IReadOnlyList<KeyValuePair<string, DesignValue>> Values(string type, string[] lines)
    {
        if (Catalog.ByType(type) is not { IsFigure: false })
        {
            return [];
        }

        var text = new List<string>
        {
            "    function createComponents(app)",
            "        app.UIFigure = uifigure('Visible', 'off');",
        };
        text.AddRange(lines.Select(static l => "        " + l.Replace("ad_PARENTCODENAME_ad", "UIFigure", StringComparison.Ordinal)
            .Replace("ad_CODENAME_ad", "Made", StringComparison.Ordinal)));
        text.Add("    end");
        DesignReadResult read = DesignCodeReader.Read(string.Join("\n", text), Catalog);
        if (read.Design?.Find("Made") is not { } made || read.Problems.Count > 0)
        {
            throw new InvalidDataException($"The recorded design-time lines of {type} do not read: {string.Join(" ", read.Problems)}");
        }

        // The adapters' design-time defaults then set a radio or toggle button's Value false
        // (customizeComponentDesignTimeDefaults): alone in its group the made one is selected.
        bool unselected = type is "matlab.ui.control.RadioButton" or "matlab.ui.control.ToggleButton";
        return made.Properties.Where(p => p.Key != "Position" && !(unselected && p.Key == "Value")).ToArray();
    }
}
