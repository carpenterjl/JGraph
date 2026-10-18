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
    public static IReadOnlyList<string> PropertiesBlock(AppDesign design, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        var components = new List<DesignComponent>();
        Walk(design.Figure);

        void Walk(DesignComponent component)
        {
            components.Add(component);
            foreach (DesignComponent child in BrowserOrder(design, component))
            {
                Walk(child);
            }
        }

        // Names up to 30 characters line their classes up two spaces past the longest of them;
        // a longer name takes two spaces of its own.
        int longest = components.Select(static c => c.CodeName.Length).Where(static n => n <= 30).DefaultIfEmpty(int.MinValue).Max();
        var lines = new List<string>
        {
            style.Indent(1) + DesignCodeReader.ComponentPropertiesComment,
            style.Indent(1) + "properties (Access = public)",
        };
        foreach (DesignComponent c in components)
        {
            int padding = c.CodeName.Length <= 30 ? 2 + longest - c.CodeName.Length : 2;
            lines.Add(style.Indent(2) + c.CodeName + new string(' ', padding) + c.Type);
        }

        lines.Add(style.Indent(1) + "end");
        return lines;
    }

    /// <summary>A container's children in the order App Designer's component browser lists them.</summary>
    private static IEnumerable<DesignComponent> BrowserOrder(AppDesign design, DesignComponent container)
    {
        IReadOnlyList<DesignComponent> children = container.Children;
        bool keepsOrder = container.Type is "matlab.ui.container.Menu" or "matlab.ui.container.ContextMenu"
                or "matlab.ui.container.TabGroup" or "matlab.ui.container.Tree" or "matlab.ui.container.CheckBoxTree"
                or "matlab.ui.container.TreeNode" or "matlab.ui.container.Toolbar"
            || (design.IsResponsive && ReferenceEquals(container.Parent, design.Figure)
                && container.Type == "matlab.ui.container.GridLayout");
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

    /// <summary><c>createComponents</c>, from the comment above it to its <c>end</c>.</summary>
    public static IReadOnlyList<string> Method(AppDesign design, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        string at = style.Indent(3);
        var lines = new List<string>
        {
            style.Indent(2) + "% Create UIFigure and components",
            style.Indent(2) + "function createComponents(app)",
        };

        if (design.UsesImageFiles)
        {
            lines.Add(string.Empty);
            lines.Add(at + "% Get the file path for locating images");
            lines.Add(at + DesignCodeReader.PathToAppLine);
        }

        DesignComponent[] all = design.Components.ToArray();
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
            IReadOnlyList<string> code = ComponentCode(component, style);
            bool hasEnd = component.Type == "matlab.ui.container.CheckBoxTree";
            bool IsEnd(string line) => hasEnd && line.StartsWith($"{App}.{component.CodeName}.CheckedNodes = ", StringComparison.Ordinal);

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
                    lines.Add(at + $"% Assign {App}.{component.CodeName}");
                    lines.AddRange(assigned.Select(c => at + $"{App}.{c.CodeName}.ContextMenu = {App}.{component.CodeName};"));
                }
            }
        }
    }

    /// <summary>
    /// One component's lines as R2025b's generator makes them, unindented and finished as the code
    /// model finishes them (a callback as <c>createCallbackFcn</c>, an image beside the app as
    /// <c>fullfile(pathToMLAPP, ...)</c>): its creation call and the properties written for it, in
    /// the generator's order. This is what App Designer keeps as the component's
    /// <c>DesignTimeProperties.ComponentCode</c>, once its placeholders are filled in. Properties the
    /// catalog does not order come last, in the order they were set, so that nothing set is lost.
    /// </summary>
    public static IReadOnlyList<string> ComponentCode(DesignComponent component, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        style ??= DesignCodeStyle.Default;
        string parent = component.Parent is { } p ? $"{App}.{p.CodeName}" : string.Empty;
        string creation = component.Class is { } cls
            ? cls.CreationCall(parent)
            : component.ExtraArguments.Length == 0 ? $"{component.Function}({parent})" : $"{component.Function}({parent}, {component.ExtraArguments})";
        var lines = new List<string> { $"{App}.{component.CodeName} = {creation};" };

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
                                lines.Add(PropertyLine(component, part, place));
                            }
                        }
                    }

                    continue;
                }

                if (component.Get(property.Name) is { } value && !IsDefault(component, property, value, style.Theme))
                {
                    lines.Add(PropertyLine(component, property.Name, value));
                }
            }
        }

        foreach ((string name, DesignValue value) in component.Properties)
        {
            if (!placed.Contains(name))
            {
                lines.Add(PropertyLine(component, name, value));
            }
        }

        return lines;
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

    /// <summary>The line that sets <paramref name="name"/> to <paramref name="value"/>, unindented.</summary>
    public static string PropertyLine(DesignComponent component, string name, DesignValue value)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(value);
        string target = $"{App}.{component.CodeName}";
        if (DesignCodeReader.LabelAliases.FirstOrDefault(a => a.Value == name) is { Key: { } labelFunction })
        {
            return $"{labelFunction}({target}, {DesignValueText.Format(value)})";
        }

        switch (name)
        {
            case "AD_ColormapString":
                return $"colormap({target}, {DesignValueText.Format(value)});";
            case "ToolbarVisible":
                return $"{target}.Toolbar.Visible = {DesignValueText.Format(value)};";
            case "AD_AliasedThemeChangedFcn":
                return $"{target}.ThemeChangedFcn = {DesignValueText.Format(value)};";
            case "Text" when TextUnwrapped.Contains(component.Type) && value is CellValue { Items: [var only] }:
                return $"{target}.Text = {DesignValueText.Format(only)};";
            default:
                return $"{target}.{name} = {DesignValueText.Format(value)};";
        }
    }
}
