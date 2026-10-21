using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Where a component shows on the designer's canvas (designer plan DS2): its box on the figure's
/// surface, top-left origin, in pixels; the part of the surface its containers leave it; and
/// whether it shows at all - a component on a tab that is not the one showing does not.
/// </summary>
/// <param name="Component">The component.</param>
/// <param name="Box">Its <c>Position</c>, on the surface.</param>
/// <param name="Clip">What its containers leave of the surface; the box is seen only within it.</param>
/// <param name="Shown">Whether it shows: every tab above it is the one its group shows.</param>
/// <param name="Hidden">Whether its <c>Visible</c> is set off, which the canvas still draws, faded.</param>
public readonly record struct DesignBox(DesignComponent Component, Rect2D Box, Rect2D Clip, bool Shown, bool Hidden);

/// <summary>
/// An app's components as the runtime makes them, for the visual designer's canvas (designer plan
/// DS2): the canvas is the runtime's own renderer, so the design is shown by making its components
/// as <c>createComponents</c> does - each component's generated lines run by an interpreter of the
/// designer's own - in a figure no script can reach. An edit goes the same way, through the
/// component's own set, and what the component keeps is read back (DS1's finding: a component holds
/// what it takes, not always what it was given).
/// </summary>
/// <remarks>
/// <para>
/// Design time is not run time: no callback is wired, and a component whose <c>Visible</c> is off is
/// made visible, as App Designer's canvas shows it (faded, which the canvas draws). The figure is
/// made here, never registered, so it has no number and is in no search; the lines run with the
/// console's session out of reach (<see cref="JgsCallbackDispatcher.Isolate"/>) and with no frame
/// sent from this thread (<see cref="ScriptComponentFrames.HoldOnThisThread"/>).
/// </para>
/// <para>
/// Every call runs on the calling thread; one thread at a time, which the canvas's is.
/// </para>
/// </remarks>
public sealed class DesignRuntime : IDisposable
{
    private const string ValueVariable = "designer__value";

    private const string App = DesignValueText.AppObjectName;

    private readonly JgsEnvironment _environment;
    private readonly Interpreter _interpreter;
    private readonly JGraphScriptGlobals _globals;
    private readonly Dictionary<DesignComponent, GraphObject> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GraphObject, DesignComponent> _components = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _problems = [];
    private readonly Dictionary<string, DesignUserClass?> _userClasses = new(StringComparer.Ordinal);
    private UiLayoutResult? _layout;
    private bool _disposed;

    private DesignRuntime(AppDesign design, string? appFolder, IReadOnlyList<string> pathFolders)
    {
        Design = design;
        AppFolder = appFolder;
        PathFolders = pathFolders;
        var context = new ScriptContext(new DesignOutput(_problems), static (_, _) => { }, appFolder);
        var globals = new JGraphScriptGlobals(context);
        _globals = globals;
        _environment = JgsBuiltins.CreateGlobals(globals, CancellationToken.None, JgsDialect.Matlab);
        _interpreter = new Interpreter(_environment, CancellationToken.None, hook: null, echo: null, JgsDialect.Matlab)
        {
            Host = globals,
        };

        // The makers that resolve handles through the interpreter (uitreenode among them), as a
        // session has them; the path builtins among them give the interpreter its path.
        JgsBuiltins.RegisterEvalBuiltins(_environment, _interpreter, globals);

        // A custom component's class is found as the session finds it: in the app's folder (the
        // runtime's current folder), and on the session's path (designer plan DS7b).
        if (pathFolders.Count > 0)
        {
            JgsFunctionPath path = _interpreter.FunctionPath ??= new JgsFunctionPath(_interpreter, globals);
            foreach (string folder in pathFolders)
            {
                path.Add(folder, atEnd: true);
            }
        }
        _environment.Builtins.Seal();
        Figure = JgsBuiltins.NewUiFigureModel();
        Figure.IsDesignTime = true;
        JgsHandleRegistry.Keep(Figure);
        _objects[design.Figure] = Figure;
        _components[Figure] = design.Figure;
    }

    /// <summary>The design the components were made from.</summary>
    public AppDesign Design { get; }

    /// <summary>
    /// The theme the design's code is written under: a property at its default under it is not
    /// written, so the component is left with its own.
    /// </summary>
    public DesignTheme Theme { get; private set; } = DesignTheme.Light;

    /// <summary>The app's folder, where its images are; null for an app not yet saved.</summary>
    public string? AppFolder { get; }

    /// <summary>The session's path folders, which a custom component's class is found on besides the app's folder (designer plan DS7b).</summary>
    public IReadOnlyList<string> PathFolders { get; }

    /// <summary>The figure the components are in.</summary>
    public FigureModel Figure { get; }

    /// <summary>Each line the runtime refused, with what it said; empty when the whole design was made.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Makes <paramref name="design"/>'s components: its figure here, and every other component by
    /// its generated lines, in the order <c>createComponents</c> makes them.
    /// </summary>
    /// <param name="design">The design.</param>
    /// <param name="appFolder">The app's folder, which an image beside the app is read from.</param>
    /// <param name="theme">The theme the design's code is written under.</param>
    /// <param name="pathFolders">The session's path folders, which a custom component's class is found on; none when null.</param>
    public static DesignRuntime Build(AppDesign design, string? appFolder, DesignTheme theme = DesignTheme.Light, IReadOnlyList<string>? pathFolders = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        var runtime = new DesignRuntime(design, appFolder, pathFolders ?? []) { Theme = theme };
        runtime.Make();
        return runtime;
    }

    // --- custom components (designer plan DS7b) --------------------------------------------------

    private const string UserVariable = "designer__uac";

    /// <summary>What the generator never writes for any class (<c>CommonPropertiesThatDoNotGenerateCode</c> and <c>UserComponentAdapter</c>'s own list); <c>Layout</c> is the design's row and column.</summary>
    private static readonly HashSet<string> NeverGenerated = new(StringComparer.Ordinal)
    {
        "Children", "CreateFcn", "DeleteFcn", "DesignTimeProperties", "InnerPosition", "OuterPosition", "Parent", "UserData",
        "Interactions", "ContextMenuId", "ContextMenu", "ButtonDownFcn", "Clipping", "Units", "Layout",
    };

    /// <summary>The inherited properties whose hidden <c>Mode</c> sibling App Designer reads: written only once set (probe <c>ds7_uac</c>, the mode siblings).</summary>
    private static readonly HashSet<string> Moded = new(StringComparer.Ordinal) { "Visible", "BackgroundColor", "SizeChangedFcn", "HandleVisibility", "Tag" };

    /// <summary>
    /// The class of the custom component <paramref name="className"/> as the designer knows it,
    /// described from the running class the first time it is asked for (designer plan DS7b; App
    /// Designer's <c>UserComponentAdapter</c>, <c>UserComponentPropertyView</c> and
    /// <c>getCallbackPropertyNames</c>, measured by the probe <c>ds7_uac</c>): a fresh instance is
    /// made in the design's figure and read - its <c>properties()</c> in order, less what the
    /// generator never writes and the properties only the class may set, <c>Position</c> last, each
    /// with the fresh instance's value as its default (the background colour by theme, as App
    /// Designer's defaults have it); the inspector's groups; the callback properties - and deleted.
    /// Null when the name is no custom component's class the runtime can make: not a class on the
    /// app's folder or the path, not a <c>ComponentContainer</c>, or one whose construction fails.
    /// </summary>
    public DesignUserClass? UserClassOf(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (_userClasses.TryGetValue(className, out DesignUserClass? known))
        {
            return known;
        }

        using Scope scope = Enter();
        DesignUserClass? described = Describe(className);
        _userClasses[className] = described;
        return described;
    }

    /// <summary>Whether the runtime finds a class named <paramref name="className"/> at all (on the app's folder or the path), whatever it is.</summary>
    public bool KnowsClass(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        using Scope scope = Enter();
        try
        {
            return _interpreter.FindClass(className) is not null;
        }
        catch (JgsException)
        {
            return true; // a class file that does not load is a class found (MATLAB's "errored" case)
        }
    }

    private DesignUserClass? Describe(string className)
    {
        JgsClass? cls;
        try
        {
            cls = _interpreter.FindClass(className);
        }
        catch (JgsException ex)
        {
            _problems.Add($"{className}: {ex.Message}");
            return null;
        }

        if (cls is not { IsComponentContainer: true })
        {
            return null;
        }

        // What the figure holds before: a construction that fails part way (a setup that refuses)
        // leaves its parts in the figure, which R2025b's design-time controller marks and this
        // runtime deletes, so the canvas does not show them.
        UiObject[] before = [.. Figure.Components];
        if (Run($"{UserVariable} = {className}({App}.{Design.Figure.CodeName});") is not null)
        {
            RemoveStrays(before);
            return null;
        }

        try
        {
            if (Run($"{ValueVariable} = properties({UserVariable});") is not null
                || !_environment.TryGet(ValueVariable, out JgsValue listed) || JgsBuiltins.ClassOf(listed, JgsDialect.Matlab) != "cell")
            {
                return null;
            }

            string[] names = listed.AsCell.Select(static v => v.Type == JgsType.String ? v.AsString : string.Empty).Where(static n => n.Length > 0).ToArray();

            // The callback properties: each HasCallbackProperty event's, in the class's order, then the container's.
            var callbacks = new List<string>();
            foreach (string name in names)
            {
                if (name.EndsWith("Fcn", StringComparison.Ordinal) && FindProperty(cls, name) is { Dependent: true }
                    && cls.Event(name[..^3]) is { HasCallbackProperty: true }
                    && !JgsBuiltinClasses.ComponentContainerProperties.Contains(name, StringComparer.Ordinal))
                {
                    callbacks.Add(name);
                }
            }

            callbacks.Add("SizeChangedFcn");

            // The generator's list: public to set, not a dependent property with a getter alone,
            // none of the never-written ones; Position last.
            var generated = new List<string>();
            foreach (string name in names)
            {
                if (name == "Position" || NeverGenerated.Contains(name))
                {
                    continue;
                }

                ClassProperty? property = FindProperty(cls, name);
                if (property is not null && (!property.SetAccess.IsPublic || (property.Dependent && cls.HasGetter(name) && !cls.HasSetter(name))))
                {
                    continue;
                }

                generated.Add(name);
            }

            generated.Add("Position");

            var properties = new List<DesignProperty>();
            foreach (string name in generated)
            {
                Dictionary<DesignTheme, DesignValue> defaults = name == "BackgroundColor" ? BackgroundDefaults() : [];
                if (name != "BackgroundColor" && ReadValue($"{UserVariable}.{name}") is { } value)
                {
                    defaults[DesignTheme.Unthemed] = value;
                }

                properties.Add(new DesignProperty(name, Moded.Contains(name), defaults));
            }

            var designClass = new DesignComponentClass(className, className, null, takesParent: true, valueProperty: null, properties) { IsUserComponent = true };
            return new DesignUserClass(designClass, InspectorGroups(cls, className, names, callbacks), callbacks);
        }
        finally
        {
            Run($"delete({UserVariable});");
            RemoveStrays(before);
        }
    }

    /// <summary>Deletes what the figure holds beyond <paramref name="before"/>: the parts a custom component left in it.</summary>
    private void RemoveStrays(UiObject[] before)
    {
        foreach (UiObject stray in Figure.Components.ToArray())
        {
            if (!before.Contains(stray))
            {
                JgsBuiltins.TryDeleteGraphics(JgsHandleRegistry.For(stray), _globals);
            }
        }
    }

    /// <summary>The default background colour by theme: the fresh instance's own when its setup set one, else App Designer's per theme.</summary>
    private Dictionary<DesignTheme, DesignValue> BackgroundDefaults()
    {
        NumericValue light = NumericValue.Row(0.96078431372549, 0.96078431372549, 0.96078431372549);
        NumericValue dark = NumericValue.Row(0.129411764705882, 0.129411764705882, 0.129411764705882);
        if (ReadValue($"{UserVariable}.BackgroundColor") is NumericValue { Data.Count: 3 } own
            && !(own.Data.Zip(light.Data).All(static p => System.Math.Abs(p.First - p.Second) < 1e-6) || own.Data.Zip(dark.Data).All(static p => System.Math.Abs(p.First - p.Second) < 1e-6)))
        {
            return new Dictionary<DesignTheme, DesignValue> { [DesignTheme.Unthemed] = own, [DesignTheme.Light] = own, [DesignTheme.Dark] = own };
        }

        return new Dictionary<DesignTheme, DesignValue> { [DesignTheme.Unthemed] = light, [DesignTheme.Light] = light, [DesignTheme.Dark] = dark };
    }

    /// <summary>
    /// The inspector's groups for a custom component (<c>UserComponentPropertyView</c>, the
    /// design-time controller's editor filter): the class's own public properties that the view
    /// shows - with a non-empty default or a declared class; not a callback; a cell only as a
    /// vector, a char only as a row, a string only as a scalar - each edited by what it holds and
    /// shown without editing when only the class may set it, those the inspector has no editor for
    /// (a struct, a date, an object, an enumeration) left out; under the class's short name,
    /// expanded; then the six groups every component shows.
    /// </summary>
    private IReadOnlyList<DesignInspectorGroup> InspectorGroups(JgsClass cls, string className, IReadOnlyList<string> names, IReadOnlyList<string> callbacks)
    {
        var own = new List<DesignInspectorRow>();
        foreach (string name in names)
        {
            if (JgsBuiltinClasses.ComponentContainerProperties.Contains(name, StringComparer.Ordinal) || callbacks.Contains(name, StringComparer.Ordinal)
                || FindProperty(cls, name) is not { } property)
            {
                continue;
            }

            DesignValue? value = ReadValue($"{UserVariable}.{name}");
            bool shown = (property.Spec.Default is not null && value is { IsEmpty: false }) || property.Spec.ClassName is not null;
            DesignEditorKind? kind = value switch
            {
                NumericValue { IsLogical: true } => DesignEditorKind.Logical,
                NumericValue => DesignEditorKind.Value,
                CharValue or StringValue => DesignEditorKind.Text,
                CellValue cell when cell.Rows == 1 || cell.Cols == 1 => DesignEditorKind.Value,
                _ => null,
            };
            if (!shown || kind is null)
            {
                continue;
            }

            own.Add(new DesignInspectorRow([new DesignEditor(name, property.SetAccess.IsPublic ? kind.Value : DesignEditorKind.ReadOnly, [])]));
        }

        var groups = new List<DesignInspectorGroup>();
        if (own.Count > 0)
        {
            groups.Add(new DesignInspectorGroup(className[(className.LastIndexOf('.') + 1)..], true, own));
        }

        static DesignInspectorRow Row(string name, DesignEditorKind kind, params string[] choices) => new([new DesignEditor(name, kind, choices)]);
        groups.Add(new DesignInspectorGroup("Color", true, [Row("BackgroundColor", DesignEditorKind.Color)]));
        groups.Add(new DesignInspectorGroup("Interactivity", false, [Row("Visible", DesignEditorKind.OnOff), Row("ContextMenu", DesignEditorKind.Component)]));
        groups.Add(new DesignInspectorGroup("Position", false, [Row("Position", DesignEditorKind.Value)]));
        groups.Add(new DesignInspectorGroup("Callback Execution Control", false, [Row("Interruptible", DesignEditorKind.OnOff), Row("BusyAction", DesignEditorKind.Choice, "queue", "cancel")]));
        groups.Add(new DesignInspectorGroup("Parent / Child", false, [Row("HandleVisibility", DesignEditorKind.Choice, "on", "callback", "off")]));
        groups.Add(new DesignInspectorGroup("Identifiers", false, [Row("Tag", DesignEditorKind.Text)]));
        return groups;
    }

    /// <summary>A property of <paramref name="cls"/> or of a class it derives from; null when none declares it.</summary>
    private static ClassProperty? FindProperty(JgsClass cls, string name)
    {
        if (cls.Property(name) is { } own)
        {
            return own;
        }

        foreach (JgsClass super in cls.Supers)
        {
            if (FindProperty(super, name) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>What <paramref name="expression"/> holds, as a design value; null when it cannot be read or is no design value. Within a scope already entered.</summary>
    private DesignValue? ReadValue(string expression) =>
        Run($"{ValueVariable} = {expression};") is null && _environment.TryGet(ValueVariable, out JgsValue value) ? ValueOf(value, null) : null;

    /// <summary>The object made for <paramref name="component"/>, or null when it could not be made.</summary>
    public GraphObject? ObjectOf(DesignComponent component) => _objects.GetValueOrDefault(component);

    /// <summary>The component <paramref name="target"/> was made for, or null for an object the design did not make.</summary>
    public DesignComponent? ComponentOf(GraphObject target) => _components.GetValueOrDefault(target);

    /// <summary>
    /// Sets <paramref name="name"/> on <paramref name="component"/> to <paramref name="value"/> by
    /// the line the generator writes for it, through the component's own set.
    /// </summary>
    /// <returns>Null when the component took it, else what the runtime said.</returns>
    public string? Set(DesignComponent component, string name, DesignValue value)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (!_objects.ContainsKey(component))
        {
            return $"{component.CodeName} was not made.";
        }

        using Scope scope = Enter();
        return Run(DesignCodeWriter.PropertyLine(component, name, value));
    }

    /// <summary>
    /// The numbers <paramref name="component"/>'s <paramref name="name"/> holds, as the runtime reads
    /// it; null when it is not numbers or cannot be read.
    /// </summary>
    public double[]? ReadNumbers(DesignComponent component, string name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        if (!_objects.ContainsKey(component))
        {
            return null;
        }

        using Scope scope = Enter();
        return Numbers(component, name);
    }

    /// <summary>
    /// What <paramref name="component"/>'s <paramref name="name"/> holds, as a design value (designer
    /// plan DS3: what a component keeps of what it was given is what App Designer's generator writes);
    /// null when it cannot be read or holds what no design value is - an object, another component, a
    /// callback, a colormap's name. <paramref name="like"/>, the value set or the property's default,
    /// says what kind an on/off property's text and a date are read as.
    /// </summary>
    public DesignValue? Read(DesignComponent component, string name, DesignValue? like = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        if (!_objects.ContainsKey(component))
        {
            return null;
        }

        if (IsEnd(component, name))
        {
            using Scope checkedScope = Enter();
            return CheckedNodes(component, like as ComponentsValue);
        }

        if (ReadExpression(component, name, like) is not { } expression)
        {
            return null;
        }

        using Scope scope = Enter();
        if (like is DatetimeValue)
        {
            return Dates(expression);
        }

        return Run($"{ValueVariable} = {expression};") is null && _environment.TryGet(ValueVariable, out JgsValue value)
            ? ValueOf(value, like)
            : null;
    }

    /// <summary>The expression that reads a design property from its component; null for one that is not read back.</summary>
    private static string? ReadExpression(DesignComponent component, string name, DesignValue? like)
    {
        if (like is CallbackValue or ComponentsValue or ImageFileValue or ExpressionValue
            || name is "AD_ColormapString" or "AD_AliasedThemeChangedFcn" or "ContextMenu" or "CheckedNodes" or "SelectedTab"
            || name.EndsWith("Fcn", StringComparison.Ordinal))
        {
            return null;
        }

        string target = $"{App}.{component.CodeName}";
        if (DesignCodeReader.LabelAliases.FirstOrDefault(a => a.Value == name) is { Key: { } function })
        {
            string part = function switch
            {
                "title" => "Title",
                "subtitle" => "Subtitle",
                _ => char.ToUpperInvariant(function[0]) + function[1..2] + "Label", // xlabel -> XLabel
            };
            return $"{target}.{part}.String";
        }

        return name switch
        {
            "ToolbarVisible" => $"{target}.Toolbar.Visible",
            "Theme" when component.Parent is null => $"{target}.Theme.BaseColorStyle", // as the generator writes a theme
            _ => $"{target}.{name}",
        };
    }

    /// <summary>A datetime array as its dates, <c>NaT</c> as null; null when it is not one.</summary>
    /// <summary>
    /// A check-box tree's checked nodes as the component keeps them (designer plan DS8, the coercion
    /// DS1 left to the inspector: the nodes written, their descendants, then the parents whose children
    /// are all checked, as R2025b keeps them - probe <c>u9</c>), by code name in the component's order;
    /// null when none is checked or a node is not the design's. Within a scope already entered.
    /// </summary>
    private ComponentsValue? CheckedNodes(DesignComponent tree, ComponentsValue? like)
    {
        if (Run($"{ValueVariable} = {App}.{tree.CodeName}.CheckedNodes;") is not null
            || !_environment.TryGet(ValueVariable, out JgsValue value) || value.ArrayLength == 0)
        {
            return null;
        }

        var names = new List<string>();
        for (int i = 0; i < value.ArrayLength; i++)
        {
            if (!JgsHandleRegistry.TryGet(value.ElementAt(i), out JgsHandleEntry? entry) || ComponentOf(entry.Target) is not { } node)
            {
                return null;
            }

            names.Add(node.CodeName);
        }

        // Several nodes are a bracketed list whatever form the one node set came in (a comma list is no value).
        return new ComponentsValue(names, names.Count > 1 || like?.Bracketed == true);
    }

    private DatetimeValue? Dates(string expression)
    {
        if (Run($"{ValueVariable} = {expression}; {ValueVariable} = [size({ValueVariable}), "
                + $"reshape([year({ValueVariable}(:)), month({ValueVariable}(:)), day({ValueVariable}(:))], 1, [])];") is not null
            || Numbers(ValueVariable) is not { Length: >= 2 } read)
        {
            return null;
        }

        int rows = (int)read[0];
        int cols = (int)read[1];
        int count = rows * cols;
        if (read.Length != 2 + (3 * count))
        {
            return null;
        }

        var dates = new DesignDate?[count];
        for (int i = 0; i < count; i++)
        {
            double y = read[2 + i];
            double m = read[2 + count + i];
            double d = read[2 + (2 * count) + i];
            dates[i] = double.IsNaN(y) ? null : new DesignDate((int)y, (int)m, (int)d);
        }

        return new DatetimeValue(rows, cols, dates);
    }

    /// <summary>A value of the language as a design value; null for one no design value is.</summary>
    private static DesignValue? ValueOf(JgsValue value, DesignValue? like)
    {
        switch (JgsBuiltins.ClassOf(value, JgsDialect.Matlab))
        {
            case "char" when !value.IsCharMatrix && value.Type == JgsType.String:
                string text = value.AsString;
                return like is OnOffValue && text is "on" or "off" ? (text == "on" ? OnOffValue.On : OnOffValue.Off) : new CharValue(text);
            case "char" when value.Type == JgsType.Array && value.ArrayLength == 0:
                return new CharValue(string.Empty);
            case "string" when value.ArrayLength == 1:
                return new StringValue(value.ElementAt(0).AsString);
            case "double" or "logical" or "single" or "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64":
                // A custom component's typed numbers are design numbers too (DS7b): the class's setter types them again.
                bool logical = JgsBuiltins.IsLogicalValue(value);
                switch (value.Type)
                {
                    case JgsType.Number:
                        return NumericValue.Scalar(value.AsNumber);
                    case JgsType.Bool:
                        return NumericValue.Logical(value.AsBool);
                    case JgsType.Array when !value.IsNd && value.Rows * value.Cols == value.ArrayLength:
                        var data = new double[value.ArrayLength];
                        for (int i = 0; i < data.Length; i++)
                        {
                            JgsValue element = value.ElementAt(i);
                            if (element.Type is not (JgsType.Number or JgsType.Bool))
                            {
                                return null;
                            }

                            data[i] = element.Type == JgsType.Bool ? (element.AsBool ? 1 : 0) : element.AsNumber;
                        }

                        return new NumericValue(value.Rows, value.Cols, data, logical);
                    default:
                        return null;
                }

            case "cell" when !value.IsNd:
                JgsValue[] items = value.AsCell;
                if (value.Rows * value.Cols != items.Length)
                {
                    return null;
                }

                var converted = new DesignValue[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    if (ValueOf(items[i], null) is not { } item)
                    {
                        return null;
                    }

                    converted[i] = item;
                }

                return new CellValue(value.Rows, value.Cols, converted);
            default:
                return null;
        }
    }

    /// <summary>The numbers a variable of the designer's interpreter holds; null when it is not numbers.</summary>
    private double[]? Numbers(string variable) =>
        _environment.TryGet(variable, out JgsValue value) ? NumbersOf(value) : null;

    /// <summary><see cref="ReadNumbers"/> within a scope already entered.</summary>
    private double[]? Numbers(DesignComponent component, string name) =>
        Run($"{ValueVariable} = app.{component.CodeName}.{name};") is null ? Numbers(ValueVariable) : null;

    /// <summary>A value's elements when it is a number or an array of them; null otherwise.</summary>
    private static double[]? NumbersOf(JgsValue value)
    {
        if (value.Type == JgsType.Number)
        {
            return [value.AsNumber];
        }

        if (value.Type != JgsType.Array || value.IsStringArray || value.IsCharMatrix)
        {
            return null;
        }

        var numbers = new double[value.ArrayLength];
        for (int i = 0; i < numbers.Length; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type != JgsType.Number)
            {
                return null;
            }

            numbers[i] = element.AsNumber;
        }

        return numbers;
    }

    /// <summary>
    /// <paramref name="component"/>'s <c>Position</c> as the runtime holds it - a pixel rectangle in
    /// its parent, MATLAB's way: from the parent's bottom-left corner, which is pixel 1 - or null for
    /// a component with no place of its own.
    /// </summary>
    public Rect2D? PositionOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!_objects.ContainsKey(component))
        {
            return null;
        }

        using Scope scope = Enter();
        return Position(component);
    }

    /// <summary><see cref="PositionOf"/> within a scope already entered.</summary>
    private Rect2D? Position(DesignComponent component) =>
        component.Class?.Properties.Any(static p => p.Name == "Position") == true
        && Numbers(component, "Position") is [var x, var y, var w, var h]
            ? new Rect2D(x, y, w, h)
            : null;

    /// <summary>
    /// The boxes of every component the canvas shows, back to front: a container before what it
    /// holds, and in each container its axes behind its other components, each kind in the order it
    /// was made. The figure, menus, toolbars and tree nodes have none.
    /// </summary>
    public IReadOnlyList<DesignBox> Boxes()
    {
        using Scope scope = Enter();
        UiLayoutResult layout = Layout();
        var placed = new Dictionary<GraphObject, UiComponentPlacement>(ReferenceEqualityComparer.Instance);
        foreach (UiComponentPlacement placement in layout.Components)
        {
            placed[placement.Component.Source] = placement;
        }

        var boxes = new List<DesignBox>();
        Rect2D surface = InnerOnSurface(Design.Figure);
        foreach (DesignComponent child in PaintOrder(Design.Figure))
        {
            Place(child, surface, null, shown: true);
        }

        return boxes;

        // Each box is where the window draws it: the layout's, which scrolls a scrollable container's
        // content and shows a tab group's one tab. An axes has its own place in a grid's cell, or by
        // its Position in its container's inner area.
        void Place(DesignComponent component, Rect2D clip, UiPanelPlacement? parent, bool shown)
        {
            if (!_objects.TryGetValue(component, out GraphObject? target))
            {
                return;
            }

            bool hidden = component.Get("Visible") is { } visible && IsOff(visible);
            if (target is UiComponentModel && placed.TryGetValue(target, out UiComponentPlacement at))
            {
                boxes.Add(new DesignBox(component, at.Box, at.Clip, shown && at.Visible, hidden));
            }
            else if (target is UiContainerModel container && layout.Find(container) is { } panel)
            {
                boxes.Add(new DesignBox(component, panel.Box, panel.Clip, shown && panel.Visible, hidden));
                // A grid's children are drawn past it at design time when its tracks overflow (UiLayout).
                Rect2D within = panel.Grid is not null ? panel.Clip : Intersect(panel.Clip, panel.Viewport ?? panel.Inner);
                foreach (DesignComponent child in PaintOrder(component))
                {
                    Place(child, within, panel, shown && panel.Visible);
                }
            }
            else if (target is AxesModel axes)
            {
                Rect2D? box = parent?.AxesCells is { } cells && cells.TryGetValue(axes, out Rect2D cell) ? cell
                    : Position(component) is { } position && component.Parent is { } holder ? ToSurface(holder, position)
                    : null;
                if (box is { } b)
                {
                    boxes.Add(new DesignBox(component, b, clip, shown, hidden));
                }
            }
        }
    }

    /// <summary>
    /// The area <paramref name="container"/> places its children in, on the surface: the figure's
    /// whole surface, or a container's inner area - where its content is when it scrolls.
    /// </summary>
    public Rect2D InnerOnSurface(DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        UiLayoutResult layout = Layout();
        if (ObjectOf(container) is UiContainerModel inner && !ReferenceEquals(container, Design.Figure)
            && layout.Find(inner) is { } placement)
        {
            return placement.Inner;
        }

        return layout.FigureScroll is { } scroll
            ? new Rect2D(-scroll.ScrollX, -scroll.ScrollY, scroll.Content.Width, scroll.Content.Height)
            : new Rect2D(0, 0, Figure.Size.Width, Figure.Size.Height);
    }

    /// <summary>
    /// The cells of <paramref name="grid"/> as the canvas draws them (designer plan DS4): its box, and
    /// each row's and column's place on the surface, as the shared layout laid them out at design
    /// time; null for a grid not laid out.
    /// </summary>
    public GridCells? CellsOf(DesignComponent grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (ObjectOf(grid) is not UiGridLayoutModel model)
        {
            return null;
        }

        using Scope scope = Enter();
        if (Layout().Find(model) is not { Grid: { } arrangement } placement)
        {
            return null;
        }

        IReadOnlyList<double> padding = placement.GridPadding;
        double left = padding.Count > 0 ? padding[0] : 0;
        double top = padding.Count > 3 ? padding[3] : 0;
        Rect2D origin = placement.GridOrigin;
        var rows = new (double Start, double Size)[arrangement.RowHeights.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = (origin.Y + top + arrangement.RowStarts[i], arrangement.RowHeights[i]);
        }

        var columns = new (double Start, double Size)[arrangement.ColumnWidths.Count];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = (origin.X + left + arrangement.ColumnStarts[i], arrangement.ColumnWidths[i]);
        }

        return new GridCells(placement.Box, padding, model.RowSpacing, model.ColumnSpacing, rows, columns);
    }

    /// <summary>The layout of the components as they stand, worked out again after anything changed them.</summary>
    private UiLayoutResult Layout() => _layout ??= UiLayout.Compute(TakeFrame(), Figure.Size);

    /// <summary>A <c>Position</c> in <paramref name="parent"/> - MATLAB's pixels - as a box on the surface.</summary>
    public Rect2D ToSurface(DesignComponent parent, Rect2D position)
    {
        Rect2D inner = InnerOnSurface(parent);
        return new Rect2D(inner.X + position.X - 1, inner.Y + inner.Height - (position.Y - 1) - position.Height, position.Width, position.Height);
    }

    /// <summary>A box on the surface as a <c>Position</c> in <paramref name="parent"/>.</summary>
    public Rect2D ToPosition(DesignComponent parent, Rect2D box)
    {
        Rect2D inner = InnerOnSurface(parent);
        return new Rect2D(box.X - inner.X + 1, inner.Y + inner.Height - box.Y - box.Height + 1, box.Width, box.Height);
    }

    /// <summary>
    /// What <paramref name="component"/>'s shape allows: the proportions the runtime keeps it in, the
    /// side a slider keeps, and the least size App Designer's canvas resizes it to.
    /// </summary>
    public DesignShape ShapeOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        GraphObject? target = ObjectOf(component);
        Size2D minimum = target is UiTabGroupModel or UiButtonGroupModel ? DesignGeometry.GroupMinimum : DesignShape.Free.Minimum;
        return target switch
        {
            UiSliderModel slider => new DesignShape(null, slider.Vertical, !slider.Vertical, minimum),
            UiObject shaped when JgsGraphicsProperties.AspectRatioOf(shaped) is { } ratio => new DesignShape(ratio, false, false, minimum),
            _ => DesignShape.Free with { Minimum = minimum },
        };
    }

    /// <summary>Moves <paramref name="component"/> into <paramref name="parent"/> through its <c>Parent</c>; null when it went.</summary>
    public string? SetParent(DesignComponent component, DesignComponent parent)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(parent);
        if (!_objects.ContainsKey(component) || !_objects.ContainsKey(parent))
        {
            return $"{component.CodeName} or {parent.CodeName} was not made.";
        }

        using Scope scope = Enter();
        return Run($"app.{component.CodeName}.Parent = app.{parent.CodeName};");
    }

    /// <summary>
    /// Shows <paramref name="tab"/> in its group on the canvas, as a click on its heading in App
    /// Designer does. What a group shows at design time is the canvas's, not the design's.
    /// </summary>
    public void ShowTab(DesignComponent tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (ObjectOf(tab) is UiTabModel { Group: { } group } shown)
        {
            group.Select(shown);
            _layout = null;
        }
    }

    /// <summary>Takes a frame of the components, as a flush takes one: for the canvas to draw.</summary>
    public UiFrame TakeFrame()
    {
        // A container told it was resized runs no callback of the console's session. A custom
        // component's owed update runs first, as a drawnow runs it (DS7b): the canvas shows the
        // component as its own update leaves it.
        using Scope scope = Enter();
        JgsComponentContainers.RunUpdates(_globals);
        return ScriptComponentFrames.TakeFrame(Figure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            JgsHandleRegistry.Release(Figure);
        }
    }

    // --- making ----------------------------------------------------------------------------------

    private void Make()
    {
        using Scope scope = Enter();
        _environment.Declare("pathToMLAPP", JgsValue.Str(AppFolder ?? string.Empty));
        foreach (DesignComponent component in Design.Components)
        {
            MakeOne(component, _problems);
        }

        foreach (DesignComponent component in Design.Components)
        {
            MakeEnd(component, _problems);
        }
    }

    /// <summary>
    /// Makes <paramref name="component"/>, just put in the design, and everything inside it, by their
    /// generated lines, as <c>createComponents</c> would (designer plan DS3: a component added from
    /// the palette, or one whose deletion is undone).
    /// </summary>
    /// <returns>What the runtime refused, one line each; empty when every line ran.</returns>
    public IReadOnlyList<string> MakeNew(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var problems = new List<string>();
        using Scope scope = Enter();
        DesignComponent[] made = Walk(component).ToArray();
        foreach (DesignComponent c in made)
        {
            MakeOne(c, problems);
        }

        foreach (DesignComponent c in made)
        {
            MakeEnd(c, problems);
        }

        _problems.AddRange(problems);
        return problems;
    }

    /// <summary>
    /// Deletes <paramref name="component"/>'s object, and with it everything inside it, as a
    /// component taken out of the design leaves.
    /// </summary>
    public void Remove(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (_objects.ContainsKey(component))
        {
            using Scope scope = Enter();
            Run($"delete({App}.{component.CodeName});");
        }

        foreach (DesignComponent c in Walk(component))
        {
            if (_objects.Remove(c, out GraphObject? target))
            {
                _components.Remove(target);
            }
        }

        _layout = null;
    }

    /// <summary>Sets the theme the design's code is written under; the components already made keep what they hold.</summary>
    public void SetTheme(DesignTheme theme) => Theme = theme;

    /// <summary>Forgets what the runtime refused for the component named <paramref name="codeName"/>, once the component is gone from the design (DS7b).</summary>
    internal void ForgetProblemsOf(string codeName) => _problems.RemoveAll(p => p.StartsWith(codeName + ":", StringComparison.Ordinal));

    private void MakeOne(DesignComponent component, List<string> problems)
    {
        if (!ReferenceEquals(component, Design.Figure))
        {
            // A custom component whose construction fails part way leaves its parts in the figure (DS7b).
            UiObject[] before = DesignUserComponents.IsUserComponent(component) ? [.. Figure.Components] : [];
            string creation = DesignCodeWriter.ComponentCode(component)[0];
            if (Run(creation) is { } refused)
            {
                problems.Add($"{component.CodeName}: {refused}");
                if (before.Length > 0 || DesignUserComponents.IsUserComponent(component))
                {
                    RemoveStrays(before);
                }

                return;
            }

            if (Made(component) is not { } made)
            {
                problems.Add($"{component.CodeName}: its creation made nothing.");
                return;
            }

            _objects[component] = made;
            _components[made] = component;
        }

        foreach ((string name, DesignValue value) in DesignCodeWriter.WrittenProperties(component, Theme))
        {
            // Design time: no callback is wired, and a hidden component is still shown. A check
            // box tree's checked nodes name nodes made after it, so they wait for them.
            if (value is CallbackValue || name == "Visible" || IsEnd(component, name))
            {
                continue;
            }

            if (Run(DesignCodeWriter.PropertyLine(component, name, value)) is { } refused)
            {
                problems.Add($"{component.CodeName}.{name}: {refused}");
            }
        }
    }

    private void MakeEnd(DesignComponent component, List<string> problems)
    {
        foreach ((string name, DesignValue value) in DesignCodeWriter.WrittenProperties(component, Theme))
        {
            if (IsEnd(component, name) && _objects.ContainsKey(component)
                && Run(DesignCodeWriter.PropertyLine(component, name, value)) is { } refused)
            {
                problems.Add($"{component.CodeName}.{name}: {refused}");
            }
        }
    }

    private static IEnumerable<DesignComponent> Walk(DesignComponent component)
    {
        yield return component;
        foreach (DesignComponent child in component.Children)
        {
            foreach (DesignComponent c in Walk(child))
            {
                yield return c;
            }
        }
    }

    private static bool IsEnd(DesignComponent component, string name) =>
        component.Type == "matlab.ui.container.CheckBoxTree" && name == "CheckedNodes";

    /// <summary>The object the creation line put in <c>app</c> under the component's name.</summary>
    private GraphObject? Made(DesignComponent component) =>
        _environment.TryGet("app", out JgsValue app) && app.Type == JgsType.Struct && !app.IsStructArray
        && app.AsStruct.TryGetValue(component.CodeName, out JgsValue? handle)
        && JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? entry)
            ? entry.Target
            : null;

    /// <summary>Runs one statement; null when it ran, else what stopped it.</summary>
    private string? Run(string line)
    {
        _layout = null;
        try
        {
            _interpreter.BeginStatement(CancellationToken.None);
            _interpreter.Run(Parser.Parse(line, string.Empty, JgsDialect.Matlab));
            return null;
        }
        catch (JgsException ex)
        {
            // A refusal wrapping the class's own error (a custom component's setup) names it too.
            return ex is JgsRuntimeException { Carried: JgsValue thrown } && CauseOf(thrown) is { } cause
                ? ex.Message + " Caused by: " + cause
                : ex.Message;
        }
    }

    /// <summary>The message of the first cause an exception value carries; null when it carries none.</summary>
    private static string? CauseOf(JgsValue exception)
    {
        try
        {
            return exception.Type == JgsType.Struct && exception.AsStruct.TryGetValue("cause", out JgsValue? causes)
                && causes.Type == JgsType.Cell && causes.AsCell is [JgsValue first, ..]
                && first.Type == JgsType.Struct && first.AsStruct.TryGetValue("message", out JgsValue? message) && message.Type == JgsType.String
                ? message.AsString
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Enters the designer's isolation, and binds <c>app</c> to the components made so far by
    /// handles minted now: a <c>clear</c> at the console forgets every handle, the designer's too.
    /// </summary>
    private Scope Enter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scope = new Scope(JgsCallbackDispatcher.Isolate(), ScriptComponentFrames.HoldOnThisThread());
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((DesignComponent component, GraphObject target) in _objects)
        {
            fields[component.CodeName] = JgsHandleRegistry.For(target);
        }

        _environment.Declare("app", JgsValue.Struct(fields));
        return scope;
    }

    private sealed class Scope(IDisposable isolation, IDisposable hold) : IDisposable
    {
        public void Dispose()
        {
            hold.Dispose();
            isolation.Dispose();
        }
    }

    // --- geometry --------------------------------------------------------------------------------

    /// <summary>A container's children in painting order: its axes behind its other components, each kind as made.</summary>
    private IEnumerable<DesignComponent> PaintOrder(DesignComponent container) =>
        container.Children.Where(c => ObjectOf(c) is AxesModel)
            .Concat(container.Children.Where(c => ObjectOf(c) is not AxesModel));

    private static Rect2D Intersect(Rect2D a, Rect2D b)
    {
        double left = System.Math.Max(a.X, b.X);
        double top = System.Math.Max(a.Y, b.Y);
        double right = System.Math.Min(a.X + a.Width, b.X + b.Width);
        double bottom = System.Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right <= left || bottom <= top ? new Rect2D(left, top, 0, 0) : new Rect2D(left, top, right - left, bottom - top);
    }

    private static bool IsOff(DesignValue value) => value switch
    {
        OnOffValue onOff => !onOff.IsOn,
        CharValue text => string.Equals(text.Text, "off", StringComparison.OrdinalIgnoreCase),
        StringValue text => string.Equals(text.Text, "off", StringComparison.OrdinalIgnoreCase),
        NumericValue { Data: [var d] } => d == 0,
        _ => false,
    };

    /// <summary>What the designer's interpreter writes: kept as problems, never shown at the console.</summary>
    private sealed class DesignOutput(List<string> problems) : IScriptOutput
    {
        public void Write(string text)
        {
        }

        public void WriteLine(string text)
        {
        }

        public void WriteError(string text) => problems.Add(text);
    }
}
