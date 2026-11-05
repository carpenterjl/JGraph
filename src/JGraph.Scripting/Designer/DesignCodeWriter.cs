namespace JGraph.Scripting.Designer;

/// <summary>
/// Writes the code App Designer generates from an app's components (designer plan DS1): the
/// component properties block and <c>createComponents</c>, laid out line for line as R2025b lays
/// them out, so that an app saved by the designer reads in App Designer as App Designer's own.
/// </summary>
/// <remarks>
/// <para>
/// One component's lines transcribe R2025b's generator
/// (<c>appdesigner.internal.codegeneration.ComponentCodeGenerator.getComponentGenerationCode</c>
/// with the per-class adapters): the creation call, then each property in the generator's order
/// (<see cref="DesignComponentClass.Properties"/>) that is set and not at its default under the
/// app's theme. A grid child's <c>Position</c> is never written and its <c>Layout.Row</c> and
/// <c>Layout.Column</c> always are; a property with a <c>Mode</c> sibling is written only once set.
/// The overrides are the adapters': a <c>Text</c> held as a one-element cell is written as its
/// char; the figure's <c>CloseRequestFcn</c> is at its default when empty or <c>closereq</c>, and
/// its <c>Theme</c> is written whenever set; an axes' labels are at their default when empty, its
/// toolbar when visible, and its <c>InnerPosition</c> is written whenever set; a slider's
/// <c>Step</c> whenever set; a date picker's <c>Value</c> compares <c>NaT</c> equal; a table's
/// <c>ColumnEditable</c> and <c>ColumnSortable</c> are at their default when false.
/// </para>
/// <para>
/// The method around them transcribes App Designer's code model (its JavaScript code engine): each
/// component under a blank line and <c>% Create Name</c>, before its children; a check box tree's
/// checked nodes after its children under <c>% Assign Checked Nodes</c>; a context menu's
/// assignments after its menus under <c>% Assign app.Name</c>; the image folder's line first when
/// an image beside the app is used; and the figure shown last.
/// </para>
/// </remarks>
public static class DesignCodeWriter
{
    private const string App = DesignValueText.AppObjectName;

    /// <summary>The classes whose adapter writes a one-element cell <c>Text</c> as its char.</summary>
    private static readonly HashSet<string> TextUnwrapped = new(StringComparer.Ordinal)
    {
        "matlab.ui.control.Button",
        "matlab.ui.control.CheckBox",
        "matlab.ui.control.Hyperlink",
        "matlab.ui.control.Label",
        "matlab.ui.control.RadioButton",
        "matlab.ui.control.StateButton",
        "matlab.ui.control.ToggleButton",
    };

    /// <summary>
    /// The component properties block: its comment, <c>properties (Access = public)</c>, a line per
    /// component, <c>end</c>. The components come each before its children, as in
    /// <c>createComponents</c>, but in the order App Designer's component browser lists them
    /// (measured over the 44 apps R2025b ships): within a container the last made first, as MATLAB's
    /// <c>Children</c> are; but menus, a context menu's items, tabs, tree nodes and toolbar tools in
    /// the order they were made, a figure's menus and toolbars ahead of its other components and its
    /// context menus after them, and a responsive app's own panels in the order made.
    /// </summary>
    public static IReadOnlyList<string> PropertiesBlock(AppDesign design, DesignCodeStyle? style = null) => PropertiesBlock(design, style, testCaseAccess: false);

    /// <summary>
    /// <see cref="PropertiesBlock(AppDesign, DesignCodeStyle?)"/>; for a custom UI component (App
    /// Designer window plan W15, <c>ComponentPropertiesBlockNoFigureBehavior</c>) the block is the
    /// underlying components' - under its own comment, <c>Access = private</c> (or get access for test
    /// cases when <paramref name="testCaseAccess"/>) with <c>Transient, NonCopyable</c>, the root left out.
    /// </summary>
    public static IReadOnlyList<string> PropertiesBlock(AppDesign design, DesignCodeStyle? style, bool testCaseAccess)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (!design.IsComponent)
        {
            return PropertiesBlock(ViewOrder(design), style);
        }

        string access = testCaseAccess ? $"GetAccess = {DesignComponentCode.TestCaseGetAccess}, SetAccess = private" : "Access = private";
        return PropertiesBlock(ViewOrder(design).Skip(1), style, DesignComponentCode.PropertiesComment, $"properties ({access}, Transient, NonCopyable)");
    }

    /// <summary>
    /// The component properties block declaring <paramref name="components"/> in that order: what
    /// App Designer's block reads once its builders were put in some other order than the browser's
    /// (a component moved; a file of an older release). The names are padded as
    /// <c>_padNames</c> pads them: up to 30 characters, two spaces past the longest; longer, two of their own.
    /// </summary>
    public static IReadOnlyList<string> PropertiesBlock(IEnumerable<DesignComponent> components, DesignCodeStyle? style = null) =>
        PropertiesBlock(components, style, DesignCodeReader.ComponentPropertiesComment, "properties (Access = public)");

    /// <summary>The block over <paramref name="components"/> under <paramref name="comment"/>, opened by <paramref name="blockLine"/>.</summary>
    public static IReadOnlyList<string> PropertiesBlock(IEnumerable<DesignComponent> components, DesignCodeStyle? style, string comment, string blockLine)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(blockLine);
        style ??= DesignCodeStyle.Default;
        DesignComponent[] declared = components.ToArray();
        int longest = declared.Select(static c => c.CodeName.Length).Where(static n => n <= 30).DefaultIfEmpty(int.MinValue).Max();
        var lines = new List<string>
        {
            style.Indent(1) + comment,
            style.Indent(1) + blockLine,
        };
        foreach (DesignComponent c in declared)
        {
            int padding = c.CodeName.Length <= 30 ? 2 + longest - c.CodeName.Length : 2;
            lines.Add(style.Indent(2) + c.CodeName + new string(' ', padding) + c.Type);
        }

        lines.Add(style.Indent(1) + "end");
        return lines;
    }

    /// <summary>Every component in the order App Designer's component browser lists them (its <c>componentViewOrder</c>): the figure, then each container's children in <see cref="BrowserOrder"/>, depth first.</summary>
    public static IEnumerable<DesignComponent> ViewOrder(AppDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return Walk(design.Figure);

        IEnumerable<DesignComponent> Walk(DesignComponent component)
        {
            yield return component;
            foreach (DesignComponent child in BrowserOrder(design, component))
            {
                foreach (DesignComponent c in Walk(child))
                {
                    yield return c;
                }
            }
        }
    }

    /// <summary>
    /// <paramref name="text"/>, an app's class, with its <c>createComponents</c> written again from
    /// <paramref name="design"/> (designer plan DS2): what the visual designer saves. The component
    /// properties block is written again too when it no longer declares the design's components;
    /// when it does, it is kept in whatever order it has. Every other line - the user's code among
    /// them - is kept as it was, and so are the text's line endings.
    /// </summary>
    /// <returns>The new text; null when the text has no properties block or no <c>createComponents</c> where App Designer puts them.</returns>
    public static string? Splice(string text, AppDesign design, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        int comment = lines.FindIndex(static l => l.Trim() == DesignCodeReader.ComponentPropertiesComment);
        if (comment < 0 || comment + 1 >= lines.Count || !lines[comment + 1].Trim().StartsWith("properties", StringComparison.Ordinal))
        {
            return null;
        }

        int blockEnd = lines.FindIndex(comment + 2, static l => l.Trim() == "end");
        int function = lines.FindIndex(static l => l.Trim() == "function createComponents(app)");
        if (blockEnd < 0 || function < 0 || function < blockEnd)
        {
            return null;
        }

        string indent = lines[function][..(lines[function].Length - lines[function].TrimStart().Length)];
        int methodEnd = lines.FindIndex(function + 1, l => l == indent + "end");
        if (methodEnd < 0)
        {
            return null;
        }

        int methodStart = function > 0 && lines[function - 1].Trim() == "% Create UIFigure and components" ? function - 1 : function;

        // The later span first, so that the earlier one's lines stay where they were found.
        lines.RemoveRange(methodStart, methodEnd - methodStart + 1);
        lines.InsertRange(methodStart, Method(design, style));
        IReadOnlyList<string> block = PropertiesBlock(design, style);
        if (!Declarations(lines.Skip(comment + 2).Take(blockEnd - comment - 2)).SequenceEqual(Declarations(block.Skip(2).SkipLast(1))))
        {
            lines.RemoveRange(comment, blockEnd - comment + 1);
            lines.InsertRange(comment, block);
        }

        return string.Join(newline, lines);

        // A block's declarations, name and class, in no particular order.
        static IEnumerable<string> Declarations(IEnumerable<string> declared) =>
            declared.Select(static l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Where(static l => l.Length > 0)
                .Order(StringComparer.Ordinal);
    }

    /// <summary>A container's children in the order App Designer's component browser lists them.</summary>
    public static IEnumerable<DesignComponent> BrowserOrder(AppDesign design, DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(container);
        IReadOnlyList<DesignComponent> children = container.Children;
        bool keepsOrder = container.Type is "matlab.ui.container.Menu" or "matlab.ui.container.ContextMenu"
                or "matlab.ui.container.TabGroup" or "matlab.ui.container.Tree" or "matlab.ui.container.CheckBoxTree"
                or "matlab.ui.container.TreeNode" or "matlab.ui.container.Toolbar"
            || ReferenceEquals(container, DesignResponsive.Grid(design));
        if (keepsOrder)
        {
            return children;
        }

        static bool Bar(DesignComponent c) => c.Type is "matlab.ui.container.Menu" or "matlab.ui.container.Toolbar";
        static bool Context(DesignComponent c) => c.Type == "matlab.ui.container.ContextMenu";
        return children.Where(Bar)
            .Concat(children.Where(static c => !Bar(c) && !Context(c)).Reverse())
            .Concat(children.Where(Context));
    }

    /// <summary><c>createComponents</c>, from the comment above it to its <c>end</c>; a custom UI component's <c>setup</c> (<see cref="Method(AppDesign, DesignCodeStyle?, string?)"/>).</summary>
    public static IReadOnlyList<string> Method(AppDesign design, DesignCodeStyle? style = null) => Method(design, style, null);

    /// <summary>
    /// <c>createComponents(app)</c> for an app, or <c>setup(comp)</c> for a custom UI component (App
    /// Designer window plan W15, <c>ComponentSetupMethodBehavior</c>): the component's own lines first
    /// - its <c>Position</c>, then the inherited properties set off their defaults
    /// (<c>ComponentInheritedPropertiesInitBehavior</c>) - then each underlying component under
    /// <c>% Create Name</c>, made in <c>comp</c>, and at the end the startup function
    /// <paramref name="postSetup"/>'s call under its comment when the component has one; no figure
    /// is made or shown.
    /// </summary>
    public static IReadOnlyList<string> Method(AppDesign design, DesignCodeStyle? style, string? postSetup)
    {
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        string at = style.Indent(3);
        bool component = design.IsComponent;
        string objectName = component ? DesignComponentCode.ObjectName : App;
        var lines = new List<string>
        {
            style.Indent(2) + (component ? DesignComponentCode.SetupComment : "% Create UIFigure and components"),
            style.Indent(2) + (component ? "function setup(comp)" : "function createComponents(app)"),
        };

        if (design.UsesImageFiles)
        {
            lines.Add(string.Empty);
            lines.Add(at + "% Get the file path for locating images");
            lines.Add(at + DesignCodeReader.PathToAppLine);
        }

        DesignComponent[] all = design.Components.ToArray();
        if (component)
        {
            lines.Add(string.Empty);
            lines.AddRange(RootLines(design, style.Theme).Select(l => at + l));
            foreach (DesignComponent child in design.Figure.Children)
            {
                Write(child);
            }

            if (postSetup is not null)
            {
                lines.Add(at); // the whitespace builder at the function's depth
                lines.Add(at + DesignComponentCode.PostSetupComment);
                lines.Add(at + $"{postSetup}({objectName})");
            }

            lines.Add(style.Indent(2) + "end");
            return lines;
        }

        Write(design.Figure);

        lines.Add(string.Empty);
        lines.Add(at + "% Show the figure after all components are created");
        lines.Add(at + $"{App}.{design.Figure.CodeName}.Visible = 'on';");
        lines.Add(style.Indent(2) + "end");
        return lines;

        void Write(DesignComponent component)
        {
            // The code model writes a component's end properties - a check box tree's checked
            // nodes - after its children, which they name.
            IReadOnlyList<string> code = ComponentCode(component, style, objectName);
            bool hasEnd = component.Type == "matlab.ui.container.CheckBoxTree";
            bool IsEnd(string line) => hasEnd && line.StartsWith($"{objectName}.{component.CodeName}.CheckedNodes = ", StringComparison.Ordinal);

            lines.Add(string.Empty);
            lines.Add(at + (ReferenceEquals(component, design.Figure)
                ? $"% Create {component.CodeName} and hide until all components are created"
                : $"% Create {component.CodeName}"));
            lines.AddRange(code.Where(l => !IsEnd(l)).Select(l => at + l));

            foreach (DesignComponent child in component.Children)
            {
                Write(child);
            }

            if (code.Where(IsEnd).ToArray() is { Length: > 0 } end)
            {
                lines.Add(string.Empty);
                lines.Add(at + "% Assign Checked Nodes");
                lines.AddRange(end.Select(l => at + l));
            }

            if (component.Type == "matlab.ui.container.ContextMenu")
            {
                DesignComponent[] assigned = all
                    .Where(c => c.Get("ContextMenu") is ComponentsValue { CodeNames: [string menu] } && menu == component.CodeName)
                    .ToArray();
                if (assigned.Length > 0)
                {
                    lines.Add(at); // App Designer indents this one blank line
                    lines.Add(at + $"% Assign {objectName}.{component.CodeName}");
                    lines.AddRange(assigned.Select(c => at + $"{objectName}.{c.CodeName}.ContextMenu = {objectName}.{component.CodeName};"));
                }
            }
        }
    }

    /// <summary>
    /// A custom UI component's own lines at the top of <c>setup</c> (<c>ComponentInheritedPropertiesInitBehavior</c>):
    /// <c>comp.Position</c> always, then <c>BackgroundColor</c>, <c>Visible</c>, <c>Interruptible</c>,
    /// <c>BusyAction</c> and <c>HandleVisibility</c> each when set and not its default under
    /// <paramref name="theme"/>; the root's other properties are not the component's.
    /// </summary>
    public static IReadOnlyList<string> RootLines(AppDesign design, DesignTheme theme)
    {
        ArgumentNullException.ThrowIfNull(design);
        DesignComponent root = design.Figure;
        string target = DesignComponentCode.ObjectName;
        var lines = new List<string>
        {
            $"{target}.Position = {DesignValueText.Format(root.Get("Position") ?? NumericValue.Row(DesignComponentCode.DefaultPosition), target)};",
        };
        foreach (string name in DesignComponentCode.InheritedProperties)
        {
            if (root.Get(name) is { } value && !IsRootDefault(name, value, theme))
            {
                lines.Add($"{target}.{name} = {DesignValueText.Format(value, target)};");
            }
        }

        return lines;
    }

    /// <summary>Whether an inherited property of a component's root holds its default (the figure adapter's defaults by theme: the background colour, and <c>on</c>, <c>on</c>, <c>queue</c>, <c>on</c>).</summary>
    public static bool IsRootDefault(string name, DesignValue value, DesignTheme theme)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        DesignValue? fallback = name switch
        {
            "BackgroundColor" => NumericValue.Row(theme == DesignTheme.Dark ? [0.1294, 0.1294, 0.1294] : [0.94, 0.94, 0.94]),
            "Visible" or "Interruptible" or "HandleVisibility" => new CharValue("on"),
            "BusyAction" => new CharValue("queue"),
            _ => null,
        };
        if (fallback is null)
        {
            return false;
        }

        if (value is OnOffValue on && fallback is CharValue c)
        {
            return on.IsOn == (c.Text == "on");
        }

        return DesignValueText.IsEqual(value, fallback);
    }

    /// <summary>
    /// One component's lines as R2025b's generator makes them, unindented and finished as the code
    /// model finishes them (a callback as <c>createCallbackFcn</c>, an image beside the app as
    /// <c>fullfile(pathToMLAPP, ...)</c>): its creation call and the properties written for it, in
    /// the generator's order. This is what App Designer keeps as the component's
    /// <c>DesignTimeProperties.ComponentCode</c>, once its placeholders are filled in. Properties the
    /// catalog does not order come last, in the order they were set, so that nothing set is lost.
    /// </summary>
    public static IReadOnlyList<string> ComponentCode(DesignComponent component, DesignCodeStyle? style = null) => ComponentCode(component, style, null);

    /// <summary>
    /// <see cref="ComponentCode(DesignComponent, DesignCodeStyle?)"/> with the object named
    /// <paramref name="objectName"/>: <c>app</c>, or <c>comp</c> for a custom UI component's, under
    /// which a child of the component's root is made in <c>comp</c> itself
    /// (<c>ComponentInitComponentParentBehavior</c>); null for the component's own name. The
    /// design-time runtime names every design <c>app</c>, its root among them.
    /// </summary>
    public static IReadOnlyList<string> ComponentCode(DesignComponent component, DesignCodeStyle? style, string? objectName)
    {
        ArgumentNullException.ThrowIfNull(component);
        style ??= DesignCodeStyle.Default;
        objectName ??= component.ObjectName;
        string target = $"{objectName}.{component.CodeName}";
        var lines = new List<string> { $"{target} = {Creation(component, ParentText(component, objectName))};" };
        lines.AddRange(WrittenProperties(component, style.Theme).Select(w => PropertyLine(target, component.Type, w.Name, w.Value, stored: false, objectName: objectName)));
        return lines;
    }

    /// <summary>The parent in a creation call: <c>app.Parent</c>; <c>comp</c> alone for a child of a custom UI component's root written as <c>comp</c>; empty for the root.</summary>
    private static string ParentText(DesignComponent component, string objectName) => component.Parent switch
    {
        null => string.Empty,
        { IsComponentRoot: true } when objectName == DesignComponentCode.ObjectName => objectName,
        { } parent => $"{objectName}.{parent.CodeName}",
    };

    /// <summary>
    /// One component's lines as App Designer keeps them in its <c>DesignTimeProperties.ComponentCode</c>
    /// (designer plan DS1b): R2025b's generator's lines before the code model finishes them - the
    /// component as <c>app.ad_CODENAME_ad</c>, its parent as <c>app.ad_PARENTCODENAME_ad</c>, a
    /// callback as its method's name in quotes, an image beside the app as its full path in quotes
    /// under <paramref name="appFolder"/> (its relative path when the folder is not known; the code
    /// model replaces that line from the recorded relative path either way).
    /// </summary>
    public static IReadOnlyList<string> StoredComponentCode(DesignComponent component, DesignCodeStyle? style = null, string? appFolder = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        style ??= DesignCodeStyle.Default;
        // A custom UI component's tree stores its lines under comp, the parent token kept (measured: the generator under the CustomUIComponent type, probe w15_component).
        string objectName = component.ObjectName;
        string target = objectName + ".ad_CODENAME_ad";
        var lines = new List<string> { $"{target} = {Creation(component, component.Parent is null ? string.Empty : objectName + ".ad_PARENTCODENAME_ad")};" };
        lines.AddRange(WrittenProperties(component, style.Theme).Select(w => PropertyLine(target, component.Type, w.Name, w.Value, stored: true, appFolder, objectName)));
        return lines;
    }

    private static string Creation(DesignComponent component, string parent) => component.Class is { } cls
        ? cls.CreationCall(parent)
        : component.ExtraArguments.Length == 0 ? $"{component.Function}({parent})" : $"{component.Function}({parent}, {component.ExtraArguments})";

    /// <summary>
    /// The properties R2025b's generator writes for <paramref name="component"/> under
    /// <paramref name="theme"/>, in its order: each catalog property that is set and not at its
    /// default (a grid child's <c>Layout.Row</c> and <c>Layout.Column</c> in <c>Layout</c>'s place,
    /// its <c>Position</c> never), then the properties the catalog does not order, as they were set.
    /// A component's <c>ContextMenu</c> is not among them: the code model assigns it.
    /// </summary>
    public static IReadOnlyList<(string Name, DesignValue Value)> WrittenProperties(DesignComponent component, DesignTheme theme)
    {
        ArgumentNullException.ThrowIfNull(component);
        var written = new List<(string, DesignValue)>();
        var placed = new HashSet<string>(StringComparer.Ordinal) { "ContextMenu" };
        if (component.Class is { } known)
        {
            if (component.IsInGrid)
            {
                placed.Add("Position"); // App Designer no longer writes it for a grid child (R2025a on)
            }

            foreach (DesignProperty property in known.Properties)
            {
                placed.Add(property.Name);
                if (property.Name == "Position" && component.IsInGrid)
                {
                    continue;
                }

                if (property.Name == "Layout")
                {
                    placed.Add("Layout.Row");
                    placed.Add("Layout.Column");
                    if (component.IsInGrid)
                    {
                        foreach (string part in (string[])["Layout.Row", "Layout.Column"])
                        {
                            if (component.Get(part) is { } place)
                            {
                                written.Add((part, place));
                            }
                        }
                    }

                    continue;
                }

                if (component.Get(property.Name) is { } value && !IsDefault(component, property, value, theme))
                {
                    written.Add((property.Name, value));
                }
            }
        }

        foreach ((string name, DesignValue value) in component.Properties)
        {
            if (!placed.Contains(name))
            {
                written.Add((name, value));
            }
        }

        return written;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is the default of <paramref name="property"/> for
    /// <paramref name="component"/> under <paramref name="theme"/>, so that the generator leaves its
    /// line out: the class's adapter's <c>isDefault</c>.
    /// </summary>
    public static bool IsDefault(DesignComponent component, DesignProperty property, DesignValue value, DesignTheme theme)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        string type = component.Type;
        string name = property.Name;
        switch (type, name)
        {
            case ("matlab.ui.Figure", "CloseRequestFcn"):
                return value.IsEmpty || value is CharValue { Text: "closereq" };
            case ("matlab.ui.Figure", "Theme"):
            case ("matlab.ui.control.UIAxes", "InnerPosition"):
            case ("matlab.ui.control.Slider" or "matlab.ui.control.RangeSlider", "Step"):
                return false; // set means its mode is manual: written whatever it holds
            case (_, "AD_ColormapString"):
                return value is CharValue { Text: "parula" };
            case ("matlab.ui.control.UIAxes", "TitleString" or "XLabelString" or "YLabelString" or "ZLabelString" or "SubtitleString"):
                return value is CharValue { Text.Length: 0 } || value is CellValue { Items: [CharValue { Text.Length: 0 }] };
            case ("matlab.ui.control.UIAxes", "ToolbarVisible"):
                return value is OnOffValue { IsOn: true } or CharValue { Text: "on" } || (value is NumericValue { Data: [1] });
            case ("matlab.ui.control.Table", "ColumnEditable" or "ColumnSortable")
                when value is NumericValue { IsLogical: true, Data: [0] }:
                return true;
            default:
                break;
        }

        if (property.DefaultFor(theme) is not { } defaultValue)
        {
            return false;
        }

        if (DesignValueText.IsEqual(value, defaultValue, nanEqual: (type, name) == ("matlab.ui.control.DatePicker", "Value"))
            || (value.IsEmpty && defaultValue.IsEmpty))
        {
            return true;
        }

        // A Text held as a one-element cell of the default text is the default.
        return name == "Text" && TextUnwrapped.Contains(type)
            && value is CellValue { Items: [var only] } && DesignValueText.IsEqual(only, defaultValue);
    }

    /// <summary>
    /// The line that sets <paramref name="name"/> to <paramref name="value"/> as App Designer stores
    /// it in <c>DesignTimeProperties.ComponentCode</c> (see <see cref="StoredComponentCode"/>).
    /// </summary>
    public static string StoredPropertyLine(DesignComponent component, string name, DesignValue value, string? appFolder = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(value);
        return PropertyLine(component.ObjectName + ".ad_CODENAME_ad", component.Type, name, value, stored: true, appFolder, component.ObjectName);
    }

    /// <summary>The line that sets <paramref name="name"/> to <paramref name="value"/>, unindented, under the object's own name (<c>app</c>, or <c>comp</c> for a custom UI component's).</summary>
    public static string PropertyLine(DesignComponent component, string name, DesignValue value) => PropertyLine(component, name, value, null);

    /// <summary>The line that sets <paramref name="name"/> to <paramref name="value"/>, unindented, under <paramref name="objectName"/> (the object's own when null).</summary>
    public static string PropertyLine(DesignComponent component, string name, DesignValue value, string? objectName)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(value);
        objectName ??= component.ObjectName;
        return PropertyLine($"{objectName}.{component.CodeName}", component.Type, name, value, stored: false, objectName: objectName);
    }

    /// <summary>
    /// The line that sets <paramref name="name"/> on <paramref name="target"/>; when
    /// <paramref name="stored"/>, in the form App Designer stores (a callback's method name and an
    /// image's relative path in quotes).
    /// </summary>
    private static string PropertyLine(string target, string type, string name, DesignValue value, bool stored, string? appFolder = null, string objectName = App)
    {
        value = WrittenValue(type, name, value);
        string text = stored && value is CallbackValue callback ? $"'{callback.Method}'"
            : stored && value is ImageFileValue image ? DesignValueText.Format(new CharValue(ImagePath(image, appFolder)))
            : DesignValueText.Format(value, objectName);
        if (DesignCodeReader.LabelAliases.FirstOrDefault(a => a.Value == name) is { Key: { } labelFunction })
        {
            return $"{labelFunction}({target}, {text})";
        }

        switch (name)
        {
            case "AD_ColormapString":
                return $"colormap({target}, {text});";
            case "ToolbarVisible":
                return $"{target}.Toolbar.Visible = {text};";
            case "AD_AliasedThemeChangedFcn":
                return $"{target}.ThemeChangedFcn = {text};";
            default:
                return $"{target}.{name} = {text};";
        }
    }

    /// <summary>
    /// The value a property's line sets: a <c>Text</c> held as a one-element cell is set as its char
    /// for the classes whose adapter writes it so; anything else as it is.
    /// </summary>
    internal static DesignValue WrittenValue(string type, string name, DesignValue value) =>
        name == "Text" && TextUnwrapped.Contains(type) && value is CellValue { Items: [var only] } ? only : value;

    /// <summary>An image beside the app as the generator writes it: its full path where the app's folder is known, else its relative path.</summary>
    private static string ImagePath(ImageFileValue image, string? appFolder) =>
        appFolder is null ? image.RelativePath : Path.Combine([appFolder, .. image.Parts]);
}
