using System.Text;
using System.Text.RegularExpressions;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// An event of a custom UI component with a public callback (App Designer window plan W15; R2025b's
/// <c>CallbackPropertyEventModel</c>): declared in the class's <c>events (HasCallbackProperty,
/// NotifyAccess = private)</c> block, which gives the component a <c>NameFcn</c> property an app
/// binds a callback to, and fired by <c>notify(comp, 'Name')</c>.
/// </summary>
public sealed partial class DesignCodeEvent
{
    /// <summary>An event named <paramref name="name"/> with <paramref name="description"/> as its comment.</summary>
    public DesignCodeEvent(string name, string description = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(description);
        Name = name;
        Description = description;
    }

    /// <summary>The event's name.</summary>
    public string Name { get; set; }

    /// <summary>The comment after the declaration; empty for none.</summary>
    public string Description { get; set; }

    /// <summary>The callback property the event gives the component (<c>publicCallbackAppendingText</c>).</summary>
    public string CallbackProperty => Name + "Fcn";

    /// <summary>The line that fires the event, which the dialog and the menu copy (<c>notify(comp, 'Name');</c>).</summary>
    public string NotifyLine => $"notify({DesignComponentCode.ObjectName}, '{Name}');";

    /// <summary>The declaration line, unindented: the name, then the description as a comment when there is one (<c>EventPropertyBehavior</c>).</summary>
    public string Declaration => Description.Length == 0 ? Name : $"{Name} % {Description}";

    /// <summary>A copy.</summary>
    public DesignCodeEvent Clone() => new(Name, Description);

    /// <summary>Reads an event's declaration line; null when the line is not one.</summary>
    public static DesignCodeEvent? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Match m = EventLine().Match(line);
        return m.Success ? new DesignCodeEvent(m.Groups["name"].Value, m.Groups["comment"].Value.Trim()) : null;
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z]\w*)\s*(?:%\s?(?<comment>.*))?$")]
    private static partial Regex EventLine();
}

/// <summary>A validation function on a managed property (R2025b's <c>ValidationFunctionModel</c>): its name and the arguments after the property.</summary>
/// <param name="Name">The function, <c>mustBePositive</c>.</param>
/// <param name="InputArguments">What follows the property in the call, <c>0, 100</c>; empty for a function of the value alone.</param>
public sealed record DesignValidationFunction(string Name, string InputArguments = "")
{
    /// <summary>The call as the declaration writes it (<c>FunctionCallSignature</c>): <c>mustBeInRange(Value, 0, 100)</c>.</summary>
    public string Signature(string propertyName) =>
        InputArguments.Length == 0 ? $"{Name}({propertyName})" : $"{Name}({propertyName}, {InputArguments})";

    /// <summary>Reads a call as a declaration holds it, for the property <paramref name="propertyName"/>; null when it is not <c>name(property[, arguments])</c>.</summary>
    public static DesignValidationFunction? Parse(string call, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(propertyName);
        string text = call.Trim();
        int open = text.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !text.EndsWith(')') || !Regex.IsMatch(text[..open], @"^[A-Za-z]\w*(\.[A-Za-z]\w*)*$"))
        {
            return null;
        }

        string inside = text[(open + 1)..^1];
        if (inside.Trim() == propertyName)
        {
            return new DesignValidationFunction(text[..open]);
        }

        string head = propertyName + ",";
        return inside.TrimStart().StartsWith(head, StringComparison.Ordinal)
            ? new DesignValidationFunction(text[..open], inside.TrimStart()[head.Length..].Trim())
            : null;
    }
}

/// <summary>
/// A public (or private) property a custom UI component's author adds through App Designer's Add
/// Property dialog (W15; R2025b's <c>ManagedUserPropertyModel</c>): written as one declaration in a
/// <c>properties</c> block of its attributes, shown in the inspector of every app that uses the
/// component, and kept apart from the editable section's own properties.
/// </summary>
public sealed partial class DesignManagedProperty
{
    /// <summary>The attribute every managed property carries, with its access after it (<c>getAccessAttributePrefix</c>).</summary>
    public const string AccessPrefix = "Access = ";

    private readonly List<string> _attributes = [];

    /// <summary>A property named <paramref name="name"/> with <paramref name="access"/> (<c>public</c>, <c>private</c> or <c>protected</c>).</summary>
    public DesignManagedProperty(string name, string access = "public")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(access);
        Name = name;
        _attributes.Add(AccessPrefix + access);
    }

    /// <summary>The property's name.</summary>
    public string Name { get; set; }

    /// <summary>The comment after the declaration; empty for none.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The class after the size (<c>double</c>); empty for none.</summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>The default value as typed (<c>0</c>, <c>'abc'</c>, <c>[0 100]</c>); empty for none.</summary>
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>The size's dimensions as typed (<c>1</c>, <c>:</c>); empty for none.</summary>
    public List<string> Size { get; } = [];

    /// <summary>The validation functions, in the order added.</summary>
    public List<DesignValidationFunction> ValidationFunctions { get; } = [];

    /// <summary>
    /// The MATLAB attributes, sorted as the client sorts them: the access, and any of <c>AbortSet</c>
    /// (with <c>GetObservable</c> and <c>SetObservable</c>, which the dialog adds with it),
    /// <c>Constant</c> and <c>Hidden</c>. They name the block the property is declared in.
    /// </summary>
    public IReadOnlyList<string> Attributes => _attributes;

    /// <summary>The access: <c>public</c>, <c>private</c> or <c>protected</c>.</summary>
    public string Access
    {
        get => _attributes.First(static a => a.StartsWith(AccessPrefix, StringComparison.Ordinal))[AccessPrefix.Length..];
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _attributes.RemoveAll(static a => a.StartsWith(AccessPrefix, StringComparison.Ordinal));
            _attributes.Add(AccessPrefix + value);
            _attributes.Sort(StringComparer.Ordinal);
        }
    }

    /// <summary>Whether the property is public: shown in an app's inspector and read by its code.</summary>
    public bool IsPublic => Access == "public";

    /// <summary>The attributes other than the access: <c>AbortSet</c>, <c>Constant</c>, <c>Hidden</c>, <c>GetObservable</c>, <c>SetObservable</c>.</summary>
    public IReadOnlyList<string> Flags => _attributes.Where(static a => !a.StartsWith(AccessPrefix, StringComparison.Ordinal)).ToArray();

    /// <summary>Sets the attributes other than the access; <c>AbortSet</c> brings <c>GetObservable</c> and <c>SetObservable</c> with it, as the dialog does.</summary>
    public void SetFlags(IEnumerable<string> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        string access = Access;
        _attributes.Clear();
        _attributes.Add(AccessPrefix + access);
        foreach (string flag in flags)
        {
            if (!_attributes.Contains(flag, StringComparer.Ordinal))
            {
                _attributes.Add(flag);
            }
        }

        if (_attributes.Contains("AbortSet", StringComparer.Ordinal))
        {
            foreach (string observable in (string[])["GetObservable", "SetObservable"])
            {
                if (!_attributes.Contains(observable, StringComparer.Ordinal))
                {
                    _attributes.Add(observable);
                }
            }
        }

        _attributes.Sort(StringComparer.Ordinal);
    }

    /// <summary>The block the property is declared in, by its attributes run together (<c>getAttributeBlockId</c>); blocks are written in this key's order.</summary>
    public string BlockId => string.Concat(_attributes.Order(StringComparer.Ordinal));

    /// <summary>The block's first line, unindented: <c>properties (Access = public)</c>.</summary>
    public string BlockLine => DesignComponentCode.BlockLine(_attributes);

    /// <summary>
    /// The declaration, unindented, as R2025b's <c>PropertyBuilder</c> writes it: the name, the size in
    /// parentheses, the class, the validation functions in braces, <c>= value;</c> and the description
    /// as a comment, each present when it has something, one space apart.
    /// </summary>
    public string Declaration
    {
        get
        {
            var text = new StringBuilder(Name);
            if (Size.Count > 0)
            {
                text.Append(" (").Append(string.Join(",", Size)).Append(')');
            }

            if (DataType.Length > 0)
            {
                text.Append(' ').Append(DataType);
            }

            if (ValidationFunctions.Count > 0)
            {
                text.Append(" {").Append(string.Join(", ", ValidationFunctions.Select(f => f.Signature(Name)))).Append('}');
            }

            if (DefaultValue.Length > 0)
            {
                text.Append(" = ").Append(DefaultValue).Append(';');
            }

            if (Description.Length > 0)
            {
                text.Append(" % ").Append(Description);
            }

            return text.ToString();
        }
    }

    /// <summary>A copy, with the attributes as they are.</summary>
    public DesignManagedProperty Clone()
    {
        var copy = new DesignManagedProperty(Name, Access) { Description = Description, DataType = DataType, DefaultValue = DefaultValue };
        copy.Size.AddRange(Size);
        copy.ValidationFunctions.AddRange(ValidationFunctions);
        copy._attributes.Clear();
        copy._attributes.AddRange(_attributes);
        return copy;
    }

    /// <summary>
    /// Reads a declaration written as <see cref="Declaration"/> writes one, in a block whose
    /// attributes are <paramref name="attributes"/>; null when the line is not one.
    /// </summary>
    public static DesignManagedProperty? Parse(string line, IReadOnlyList<string> attributes)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(attributes);
        string access = attributes.FirstOrDefault(static a => a.StartsWith(AccessPrefix, StringComparison.Ordinal))?[AccessPrefix.Length..] ?? "public";
        (string code, string? comment) = DesignComponentCode.SplitComment(line.Trim());
        code = code.TrimEnd();
        Match head = DeclarationHead().Match(code);
        if (!head.Success || DesignNames.IsKeyword(head.Groups["name"].Value))
        {
            return null;
        }

        var property = new DesignManagedProperty(head.Groups["name"].Value, access) { Description = comment?.Trim() ?? string.Empty };
        property._attributes.Clear();
        property._attributes.AddRange(attributes);
        if (!property._attributes.Any(static a => a.StartsWith(AccessPrefix, StringComparison.Ordinal)))
        {
            property._attributes.Add(AccessPrefix + access);
        }

        property._attributes.Sort(StringComparer.Ordinal);
        if (head.Groups["size"].Success)
        {
            property.Size.AddRange(head.Groups["size"].Value.Split(',').Select(static d => d.Trim()).Where(static d => d.Length > 0));
        }

        int at = head.Length;
        string rest = code[at..].TrimStart();

        // The class: a name that is not the validators' brace or the default's equals.
        Match type = TypeName().Match(rest);
        if (type.Success)
        {
            property.DataType = type.Value;
            rest = rest[type.Length..].TrimStart();
        }

        if (rest.StartsWith('{'))
        {
            int close = DesignComponentCode.MatchingBrace(rest, 0);
            if (close < 0)
            {
                return null;
            }

            foreach (string call in DesignComponentCode.SplitTopLevel(rest[1..close]))
            {
                if (DesignValidationFunction.Parse(call, property.Name) is not { } function)
                {
                    return null;
                }

                property.ValidationFunctions.Add(function);
            }

            rest = rest[(close + 1)..].TrimStart();
        }

        if (rest.StartsWith('='))
        {
            string value = rest[1..].Trim();
            if (!value.EndsWith(';'))
            {
                return null; // App Designer ends a default with a semicolon; a line without one is the user's
            }

            property.DefaultValue = value[..^1].TrimEnd();
            rest = string.Empty;
        }

        return rest.Length == 0 ? property : null;
    }

    /// <inheritdoc />
    public override string ToString() => Declaration;

    [GeneratedRegex(@"^(?<name>[A-Za-z]\w*)(?:\s*\((?<size>[^)]*)\))?")]
    private static partial Regex DeclarationHead();

    [GeneratedRegex(@"^[A-Za-z]\w*(?:\.[A-Za-z]\w*)*")]
    private static partial Regex TypeName();
}

/// <summary>One of the validation functions App Designer's Add Property dialog offers (<c>MATLABValidationFunctions</c>).</summary>
/// <param name="Category">The menu's heading.</param>
/// <param name="Name">The function.</param>
/// <param name="Description">The menu item's description.</param>
/// <param name="InputCount">How many inputs follow the property: none, one, or three for <c>mustBeInRange</c>.</param>
/// <param name="Placeholder">What the input field shows when empty; empty for a function without inputs.</param>
public sealed record DesignValidationFunctionEntry(string Category, string Name, string Description, int InputCount, string Placeholder);

/// <summary>One of the MATLAB attributes the dialog offers beside the access (<c>MATLABPropertyAttributes</c>).</summary>
public sealed record DesignPropertyAttributeEntry(string Name, string Description);

/// <summary>
/// What App Designer writes for a custom UI component (App Designer window plan W15), read from R2025b's
/// client: its <c>ComponentAppCodeEngineConfig</c> and the behaviors it names (<c>ComponentClassBehavior</c>,
/// <c>ComponentPropertiesBlockNoFigureBehavior</c>, <c>ComponentEventBlockBehavior</c>,
/// <c>ManagedUserPropertyGroupBehavior</c>, <c>ComponentLifecycleMethodBlockBehavior</c>,
/// <c>ComponentSetupMethodBehavior</c>, <c>ComponentInheritedPropertiesInitBehavior</c>,
/// <c>ComponentPostSetupCallbackBehavior</c>), its <c>ComponentAppFactory</c> and
/// <c>ComponentFactoryTemplateData</c>, and the dialogs' tables; the texts measured to compile and run in
/// R2025b by the probe <c>w15_component</c>.
/// </summary>
public static partial class DesignComponentCode
{
    /// <summary>The name the generated code calls the component: <c>comp</c> (<c>defaultMethodObjectArgumentName</c>).</summary>
    public const string ObjectName = "comp";

    /// <summary>The class every custom UI component derives from (<c>ComponentBaseStrategy</c>).</summary>
    public const string Superclass = "matlab.ui.componentcontainer.ComponentContainer";

    /// <summary>The comment over the underlying components' properties block (<c>customComponentPropertiesComment</c>).</summary>
    public const string PropertiesComment = "% Properties that correspond to underlying components";

    /// <summary>The comment over the events block (<c>componentEventBlockComment</c>).</summary>
    public const string EventsComment = "% Events with associated public callbacks";

    /// <summary>The events block's own line.</summary>
    public const string EventsBlockLine = "events (HasCallbackProperty, NotifyAccess = private)";

    /// <summary>The comment over <c>update</c> (<c>componentUpdateMethodDescriptionComment</c>).</summary>
    public const string UpdateComment = "% Code that executes when the value of a property is changed";

    /// <summary>The comment a fresh <c>update</c> holds (<c>componentUpdateMethodHelpComment</c>).</summary>
    public const string UpdateHelpComment = "% Use this function to update the underlying components";

    /// <summary>The comment over <c>setup</c> (<c>componentSetupMethodComment</c>).</summary>
    public const string SetupComment = "% Create the underlying components";

    /// <summary>The comment over the startup function's call at the end of <c>setup</c> (<c>runPostSetupFcnComment</c>).</summary>
    public const string PostSetupComment = "% Execute the postSetup function";

    /// <summary>The name App Designer gives a component's startup function (<c>InspectableComponentNodeModel.getDefaultCallbackName</c> for <c>PostSetupFcn</c>).</summary>
    public const string DefaultStartupName = "postSetupFcn";

    /// <summary>The callback property the component's own row shows for its startup function (<c>CallbackFcnData</c>).</summary>
    public const string StartupProperty = "PostSetupFcn";

    /// <summary>The title of the component's own row's callback group.</summary>
    public const string StartupGroupTitle = "PRIVATE CALLBACKS";

    /// <summary>The words the inspector's first tab and the browser's top row use for the component itself (<c>CodeNameGeneration.DefaultText</c>).</summary>
    public const string DefaultText = "Custom UI Component";

    /// <summary>The code name the design-time figure stands under for a component (<c>ComponentFactoryTemplateData.FigureProps.CodeName</c>).</summary>
    public const string RootCodeName = "CustomUIComponent";

    /// <summary>The method names a component reserves (<c>AppTypeMethodEnum.getReservedMethodNames</c>).</summary>
    public static readonly IReadOnlyList<string> ReservedMethods = ["update", "postSetup", "setup"];

    /// <summary>The access a <c>Give Test Cases Access</c> component gives its underlying components' properties (<c>setGetSetAccess</c>).</summary>
    public const string TestCaseGetAccess = "?matlab.unittest.TestCase";

    /// <summary>The position a new component is made with (<c>ComponentFactoryTemplateData</c>).</summary>
    public static readonly double[] DefaultPosition = [1, 1, 320, 240];

    /// <summary>
    /// The inherited properties <c>setup</c> sets after <c>Position</c> when they are not at their
    /// defaults (<c>ComponentInheritedPropertiesInitBehavior._propertyList</c>), in that order.
    /// </summary>
    public static readonly IReadOnlyList<string> InheritedProperties = ["BackgroundColor", "Visible", "Interruptible", "BusyAction", "HandleVisibility"];

    /// <summary>The data types the dialog's combo box lists (<c>_createDataTypeList</c>); any other class name may be typed.</summary>
    public static readonly IReadOnlyList<string> DataTypes = ["double", "char", "string", "logical"];

    /// <summary>The access choices (<c>MATLABPropertyAttributes</c>'s first entry).</summary>
    public static readonly IReadOnlyList<string> AccessChoices = ["public", "private", "protected"];

    /// <summary>The attributes beside the access, with the dialog's descriptions.</summary>
    public static readonly IReadOnlyList<DesignPropertyAttributeEntry> PropertyAttributes =
    [
        new("AbortSet", "When selected, MATLAB does not set the property value if the new value is the same as the current value."),
        new("Constant", "When selected, the property value cannot be changed once it is initialized in the property block."),
        new("Hidden", "When selected, the property is not shown in property lists."),
    ];

    /// <summary>The validation functions the dialog offers, by category, in its order (<c>MATLABValidationFunctions</c>).</summary>
    public static readonly IReadOnlyList<DesignValidationFunctionEntry> ValidationFunctions =
    [
        new("Numeric", "mustBePositive", "Validate that value is positive", 0, ""),
        new("Numeric", "mustBeNonpositive", "Validate that value is not positive", 0, ""),
        new("Numeric", "mustBeNonnegative", "Validate that value is not negative", 0, ""),
        new("Numeric", "mustBeNegative", "Validate that value is negative", 0, ""),
        new("Numeric", "mustBeFinite", "Validate that value is finite", 0, ""),
        new("Numeric", "mustBeNonNan", "Validate that value is not NaN", 0, ""),
        new("Numeric", "mustBeNonzero", "Validate that value is not zero", 0, ""),
        new("Numeric", "mustBeNonsparse", "Validate that value is not sparse", 0, ""),
        new("Numeric", "mustBeReal", "Validate that value is real", 0, ""),
        new("Numeric", "mustBeInteger", "Validate that value is integer", 0, ""),
        new("Numeric", "mustBeNonmissing", "Validate that value is not missing", 0, ""),
        new("Comparison", "mustBeGreaterThan", "Validate that value is greater than another value", 1, "numeric value"),
        new("Comparison", "mustBeLessThan", "Validate that value is less than another value", 1, "numeric value"),
        new("Comparison", "mustBeGreaterThanOrEqual", "Validate that value is greater than or\nequal to another value", 1, "numeric value"),
        new("Comparison", "mustBeLessThanOrEqual", "Validate that value is less than or\nequal to another value", 1, "numeric value"),
        new("Data Types", "mustBeA", "Validate that value comes from one of specified classes", 1, "class names Eg:[\"double\"]"),
        new("Data Types", "mustBeNumeric", "Validate that value is numeric", 0, ""),
        new("Data Types", "mustBeNumericOrLogical", "Validate that value is numeric or logical", 0, ""),
        new("Data Types", "mustBeFloat", "Validate that value is floating-point array", 0, ""),
        new("Data Types", "mustBeUnderlyingType", "Validate that value has specified underlying type", 1, "data type Eg:\"double\""),
        new("Size", "mustBeNonempty", "Validate that value is not empty", 0, ""),
        new("Size", "mustBeScalarOrEmpty", "Validate that value is scalar or empty", 0, ""),
        new("Size", "mustBeVector", "Validate that value is vector", 0, ""),
        new("Membership and Range", "mustBeMember", "Validate that value is member of specified set", 1, "scalar | array"),
        new("Membership and Range", "mustBeInRange", "Validate that value is in the specified range", 3, "lower, upper, boundflags"),
        new("Text", "mustBeFile", "Validate that input path refers to file", 0, ""),
        new("Text", "mustBeFolder", "Validate that input path refers to folder", 0, ""),
        new("Text", "mustBeNonzeroLengthText", "Validate that value is text with nonzero length", 0, ""),
        new("Text", "mustBeText", "Validate that value is string array,\ncharacter vector, or cell array of character vectors", 0, ""),
        new("Text", "mustBeTextScalar", "Validate that value is single piece of text", 0, ""),
        new("Text", "mustBeValidVariableName", "Validate that input name is valid variable name", 0, ""),
    ];

    /// <summary>The words the Add Property dialog's and the inspector's text uses for a public property (<c>addComponentPublicPropertyDescription</c>).</summary>
    public const string PublicPropertyDescription = "Public properties can be set and queried when the component is added to an app";

    /// <summary>The Event–Callback Pairs tab's help line (<c>addEventHelpText</c>).</summary>
    public const string AddEventHelpText = "Add an event to create a new public callback";

    /// <summary>The Event button's and the Events tab's description (<c>addEventDescription</c>).</summary>
    public const string AddEventDescription = "Add an event to create a new public callback.\nPublic callbacks are accessible when the component is added to an app.";

    /// <summary>The menu item that copies an event's trigger line (<c>copyEventTriggerCodeMenuItemText</c>).</summary>
    public const string CopyTriggerCode = "Copy Code to Trigger Event";

    /// <summary>What the dialog says under a name ending in Fcn (<c>eventDialogPublicCallbackError</c>).</summary>
    public const string FcnSuffixWarning = "Fcn will be auto appended to the name";

    /// <summary>The first line of a <c>properties</c> block with <paramref name="attributes"/>, unindented: <c>properties (Access = public)</c>.</summary>
    public static string BlockLine(IEnumerable<string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        string[] list = [.. attributes];
        return list.Length == 0 ? "properties" : "properties (" + string.Join(", ", list) + ")";
    }

    /// <summary>The attributes a <c>properties (...)</c> line declares, in order; null when the line is not one.</summary>
    public static IReadOnlyList<string>? AttributesOf(string blockLine)
    {
        ArgumentNullException.ThrowIfNull(blockLine);
        Match m = PropertiesLine().Match(blockLine);
        if (!m.Success)
        {
            return null;
        }

        return m.Groups["attributes"].Success
            ? m.Groups["attributes"].Value.Split(',').Select(static a => Squeeze(a)).Where(static a => a.Length > 0).ToArray()
            : [];
    }

    /// <summary>The attributes a managed block may carry: the access, and the dialog's flags with the two AbortSet brings.</summary>
    public static bool IsManagedBlock(IReadOnlyList<string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.Count(static a => a.StartsWith(DesignManagedProperty.AccessPrefix, StringComparison.Ordinal)) == 1
            && attributes.All(static a => a.StartsWith(DesignManagedProperty.AccessPrefix, StringComparison.Ordinal)
                || a is "AbortSet" or "Constant" or "Hidden" or "GetObservable" or "SetObservable")
            && AccessChoices.Contains(attributes.First(static a => a.StartsWith(DesignManagedProperty.AccessPrefix, StringComparison.Ordinal))[DesignManagedProperty.AccessPrefix.Length..]);
    }

    /// <summary>An attribute as the client writes it: one space each side of its equals sign, none elsewhere.</summary>
    private static string Squeeze(string attribute)
    {
        string a = attribute.Trim();
        int eq = a.IndexOf('=', StringComparison.Ordinal);
        return eq < 0 ? a : a[..eq].Trim() + " = " + a[(eq + 1)..].Trim();
    }

    /// <summary>The design of a new component (<c>ComponentFactoryTemplateData</c>): the root at [1 1 320 240], with a grid named GridLayout when <paramref name="withGrid"/>.</summary>
    public static AppDesign NewDesign(bool withGrid)
    {
        DesignComponentClass figureClass = DesignCatalog.R2025b.ByType("matlab.ui.Figure") ?? throw new InvalidOperationException("The catalog has no figure.");
        var root = new DesignComponent(RootCodeName, figureClass);
        root.Set("Position", NumericValue.Row(DefaultPosition));
        var design = new AppDesign(root) { IsComponent = true };
        if (withGrid)
        {
            DesignComponentClass gridClass = DesignCatalog.R2025b.ByType("matlab.ui.container.GridLayout") ?? throw new InvalidOperationException("The catalog has no grid.");
            root.Add(new DesignComponent("GridLayout", gridClass));
        }

        return design;
    }

    /// <summary>The class text of a new component named <paramref name="className"/>, with or without the grid, as App Designer's code engine writes it.</summary>
    public static string NewText(string className, bool withGrid)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        return DesignClassWriter.Render(NewDesign(withGrid), new DesignCode(className) { IsComponent = true });
    }

    /// <summary>Splits <paramref name="line"/> at the first comment sign outside quotes: the code, and the comment's text after the sign (null for none).</summary>
    public static (string Code, string? Comment) SplitComment(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                // A quote opens a text unless it follows a value, where a single quote is a transpose.
                bool transpose = c == '\'' && i > 0 && (char.IsLetterOrDigit(line[i - 1]) || line[i - 1] is ')' or ']' or '}' or '.' or '\'');
                if (!transpose)
                {
                    quote = c;
                }

                continue;
            }

            if (c == '%')
            {
                return (line[..i], line[(i + 1)..]);
            }
        }

        return (line, null);
    }

    /// <summary>The index of the brace that closes the one at <paramref name="open"/>, quotes respected; -1 when none.</summary>
    public static int MatchingBrace(string text, int open)
    {
        ArgumentNullException.ThrowIfNull(text);
        int depth = 0;
        char quote = '\0';
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    break;
                case '{' or '(' or '[':
                    depth++;
                    break;
                case '}' or ')' or ']':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
                default:
                    break;
            }
        }

        return -1;
    }

    /// <summary>The parts of <paramref name="text"/> between commas outside brackets and quotes, trimmed, empty parts left out.</summary>
    public static IReadOnlyList<string> SplitTopLevel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string>();
        int depth = 0;
        char quote = '\0';
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    break;
                case '{' or '(' or '[':
                    depth++;
                    break;
                case '}' or ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
                default:
                    break;
            }
        }

        parts.Add(text[start..].Trim());
        return parts.Where(static p => p.Length > 0).ToArray();
    }

    [GeneratedRegex(@"^\s*properties\s*(?:\((?<attributes>[^)]*)\))?\s*$")]
    private static partial Regex PropertiesLine();
}
