using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Designer;

/// <summary>A component's callback property bound to a callback: what App Designer calls a <c>CallbackBinding</c>.</summary>
public readonly record struct DesignCallbackBinding(DesignComponent Component, string Property);

/// <summary>A group of a class's callback properties as App Designer's inspector lists them; the figure's groups are titled, no other is.</summary>
public sealed record DesignCallbackGroup(string? Title, IReadOnlyList<string> Properties);

/// <summary>
/// What App Designer knows of callbacks per component class (designer plan DS5, from its bundle's
/// component models, read under node by <c>ds5/oracle/callbacks.js</c>): the callback properties its
/// inspector lists, in their groups; the comment it writes over a callback bound to one property
/// alone (<c>CodeGeneration.CallbackComments</c>); and the code a new callback starts with
/// (<c>CodeGeneration.getDefaultCallbackCode</c>); and its rules for a callback's generated comment
/// (<c>CallbackModel._getGeneratedComment</c>) and default name
/// (<c>ApplicationComponentModel.getDefaultCallbackName</c>).
/// </summary>
public sealed partial class DesignCallbacks
{
    /// <summary>The comment over a callback bound to properties of more than one kind, or to none (<c>mixedCallbackFcnComment</c>, which has no comment sign).</summary>
    public const string MixedComment = "Callback function";

    /// <summary>What follows the mixed comment for a callback whose bindings were taken away (<c>unassignedCallbackComment</c>).</summary>
    public const string UnassignedComment = "not associated with a component";

    /// <summary>The comment over the startup function (<c>startUpCallbackComment</c>).</summary>
    public const string StartupComment = "Code that executes after component creation";

    /// <summary>App Designer's right-hand text limit for generated comments (<c>CodeFormatter.getRightHandTextLimitLength</c>).</summary>
    public const int RightHandLimit = 75;

    /// <summary>
    /// What the comment's budget counts as used before the text: two indents of a fresh
    /// <c>CodeFormatter</c>, which is always four spaces wide whatever the file's indentation is.
    /// </summary>
    private const int CommentIndent = 8;

    private readonly Dictionary<string, ClassCallbacks> _classes;

    private DesignCallbacks(Dictionary<string, ClassCallbacks> classes)
    {
        _classes = classes;
    }

    /// <summary>R2025b's table.</summary>
    public static DesignCallbacks R2025b { get; } = Load();

    /// <summary>The callback properties of <paramref name="type"/> in their groups, as App Designer's inspector lists them; empty for a class with none.</summary>
    public IReadOnlyList<DesignCallbackGroup> GroupsOf(string type) =>
        _classes.TryGetValue(type, out ClassCallbacks? c) ? c.Groups : [];

    /// <summary>The callback properties of <paramref name="type"/> in the inspector's order; empty for a class with none.</summary>
    public IReadOnlyList<string> PropertiesOf(string type) =>
        _classes.TryGetValue(type, out ClassCallbacks? c) ? c.Properties : [];

    /// <summary>
    /// The comment App Designer writes over a callback bound to <paramref name="property"/> of a
    /// <paramref name="type"/> alone, with its comment sign as its resources hold it; null when it has
    /// none for it (five properties of R2025b name a resource that does not exist, and take the mixed comment).
    /// </summary>
    public string? CommentFor(string type, string property) =>
        _classes.TryGetValue(type, out ClassCallbacks? c) && c.Comments.TryGetValue(property, out string? comment) ? comment : null;

    /// <summary>
    /// The code a new callback for <paramref name="property"/> of <paramref name="component"/> starts
    /// with, unindented (<c>CallbackModel._onGenerateDefaultCode</c>): one empty line for most; for
    /// some, a line or two reading the event or the component's value, and an empty line after them.
    /// </summary>
    public IReadOnlyList<string> DefaultBody(DesignComponent component, string property)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        if (!_classes.TryGetValue(component.Type, out ClassCallbacks? c) || !c.DefaultCode.TryGetValue(property, out IReadOnlyList<string>? code))
        {
            return [string.Empty];
        }

        return code.Select(l => l.Replace("ad_OBJECTNAME_ad", DesignValueText.AppObjectName, StringComparison.Ordinal)
                .Replace("ad_CODENAME_ad", component.CodeName, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>The property a design holds a callback under, by the name the component has it under: the figure's aliased <c>ThemeChangedFcn</c> is its own again.</summary>
    public static string PropertyName(string designProperty) =>
        designProperty == "AD_AliasedThemeChangedFcn" ? "ThemeChangedFcn" : designProperty;

    /// <summary>The design's name for a component's callback <paramref name="property"/>.</summary>
    public static string DesignPropertyName(DesignComponent component, string property) =>
        property == "ThemeChangedFcn" && component.Class is { IsFigure: true } ? "AD_AliasedThemeChangedFcn" : property;

    /// <summary>Every property of every component of <paramref name="design"/> bound to the callback <paramref name="name"/>, in the design's order.</summary>
    public static IReadOnlyList<DesignCallbackBinding> BindingsOf(AppDesign design, string name)
    {
        ArgumentNullException.ThrowIfNull(design);
        var bindings = new List<DesignCallbackBinding>();
        foreach (DesignComponent component in design.Components)
        {
            foreach ((string property, DesignValue value) in component.Properties)
            {
                if (value is CallbackValue callback && callback.Method == name)
                {
                    bindings.Add(new DesignCallbackBinding(component, PropertyName(property)));
                }
            }
        }

        return bindings;
    }

    /// <summary>
    /// The bindings of the callback <paramref name="name"/> in the order App Designer's reader adds
    /// them on load (<c>findall</c> over the figure: the figure itself, then each container's children
    /// newest first, depth first - the reverse of the order <c>createComponents</c> makes them in).
    /// </summary>
    public static IReadOnlyList<DesignCallbackBinding> BindingsAsLoaded(AppDesign design, string name)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(name);
        var bindings = new List<DesignCallbackBinding>();
        Walk(design.Figure);
        return bindings;

        void Walk(DesignComponent component)
        {
            foreach ((string property, DesignValue value) in component.Properties)
            {
                if (value is CallbackValue callback && callback.Method == name)
                {
                    bindings.Add(new DesignCallbackBinding(component, PropertyName(property)));
                }
            }

            for (int i = component.Children.Count - 1; i >= 0; i--)
            {
                Walk(component.Children[i]);
            }
        }
    }

    /// <summary>
    /// The bindings of <paramref name="callback"/> in the order App Designer's collection holds them
    /// (<see cref="DesignCallback.Bindings"/>): those the callback records that the design still has,
    /// then any the design has that it does not record, in the design's order.
    /// </summary>
    public static IReadOnlyList<DesignCallbackBinding> OrderedBindings(AppDesign design, DesignCallback callback)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(callback);
        IReadOnlyList<DesignCallbackBinding> real = BindingsOf(design, callback.Name);
        var ordered = callback.Bindings.Where(real.Contains).ToList();
        ordered.AddRange(real.Where(b => !ordered.Contains(b)));
        return ordered;
    }

    /// <summary>
    /// The comment App Designer generates over a component callback (<c>CallbackModel._getGeneratedComment</c>),
    /// as lines each beginning with the comment sign: the property's comment - the first binding's
    /// class's - when every binding is to the same property, else "Callback function"; then the bound
    /// components' code names, sorted (by character code, so capitals first), joined by commas, within
    /// the right-hand limit less eight - the names that do not fit replaced by "...and N other
    /// components", a first name that does not fit put on a line of its own. A callback with no
    /// binding reads "Callback function" alone, as App Designer makes it before any binding is seen,
    /// and "not associated with a component" once bindings were taken from it.
    /// </summary>
    public IReadOnlyList<string> Comment(AppDesign design, DesignCallback callback)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(callback);
        IReadOnlyList<DesignCallbackBinding> bindings = OrderedBindings(design, callback);
        string a = MixedComment;
        var lines = new List<string>();
        if (bindings.Count > 0)
        {
            string property = bindings[0].Property;
            if (bindings.All(b => b.Property == property) && CommentFor(bindings[0].Component.Type, property) is { } own)
            {
                a = own;
            }

            var names = new Queue<string>(bindings.Select(static b => b.Component.CodeName).Order(StringComparer.Ordinal));
            if (names.Count > 0)
            {
                a += ": ";
            }

            int i = CommentIndent + a.Length;
            lines.Add(a);
            int placed = 0;
            int total = names.Count;
            while (names.Count > 0)
            {
                string x = names.Dequeue() + (names.Count == 0 ? string.Empty : ", ");
                if (i + x.Length > RightHandLimit)
                {
                    if (placed > 0)
                    {
                        int left = total - placed;
                        lines.Add(Format(left == 1 ? "...and 1 other component" : $"...and {left} other components"));
                        break;
                    }

                    placed++;
                    string r = Format(x);
                    lines.Add(r);
                    i = CommentIndent + r.Length;
                }
                else
                {
                    placed++;
                    lines[^1] += x;
                    i += x.Length;
                }
            }
        }
        else
        {
            lines.Add(callback.BindingsChanged ? $"{a}: {UnassignedComment}" : a);
        }

        return lines.Select(Format).ToArray();
    }

    /// <summary>A comment with its sign (<c>CodeFormatter.formatComment</c>).</summary>
    public static string Format(string comment) =>
        comment.StartsWith('%') ? comment : "% " + comment;

    /// <summary>
    /// A comment wrapped by words to <paramref name="width"/> characters (<c>CodeFormatter.wrapComment</c>),
    /// each line under the comment's own sign and spacing: a word is added to a line while the line
    /// stays within the width, else it starts the next. A single word longer than the width stands alone.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string comment, int width)
    {
        ArgumentNullException.ThrowIfNull(comment);
        comment = Format(comment);
        Match sign = CommentSign().Match(comment);
        string prefix = sign.Success ? sign.Value : "% ";
        string rest = comment[(sign.Success ? sign.Length : 0)..];
        var lines = new List<string>();
        foreach (string segment in rest.Split('\n'))
        {
            foreach (string word in segment.Split([' ', '\t']))
            {
                if (lines.Count == 0)
                {
                    lines.Add(prefix + word);
                    continue;
                }

                string joined = lines[^1] + " " + word;
                if (joined.Length <= width)
                {
                    lines[^1] = joined;
                }
                else
                {
                    lines.Add(prefix + word);
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// The name App Designer offers a new callback for <paramref name="property"/> of
    /// <paramref name="component"/> (<c>ApplicationComponentModel.getDefaultCallbackName</c>): the
    /// code name, then the property without its trailing <c>Fcn</c> or <c>Callback</c>; the class's
    /// default code name taken out of the property once when both the code name and the property
    /// hold it, so a Button's ButtonPushedFcn is ButtonPushed. Not yet made unique.
    /// </summary>
    public static string DefaultName(DesignComponent component, string property)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        string rest = property;
        int fcn = property.LastIndexOf("Fcn", StringComparison.Ordinal);
        if (fcn >= 0)
        {
            rest = property[..fcn];
        }

        int callback = property.LastIndexOf("Callback", StringComparison.Ordinal);
        if (callback >= 0)
        {
            rest = property[..callback];
        }

        if (R2025b.DefaultCodeNameOf(component.Type) is { Length: > 0 } stem
            && component.CodeName.Contains(stem, StringComparison.Ordinal) && property.Contains(stem, StringComparison.Ordinal))
        {
            int at = rest.IndexOf(stem, StringComparison.Ordinal);
            if (at >= 0)
            {
                rest = rest.Remove(at, stem.Length);
            }
        }

        return component.CodeName + rest;
    }

    /// <summary>The class's default code name (<c>CodeNameGeneration.DefaultCodeName</c>): what a new component of it is called before a number.</summary>
    public string? DefaultCodeNameOf(string type) =>
        _classes.TryGetValue(type, out ClassCallbacks? c) ? c.DefaultCodeName : DesignNames.Of(type)?.CodeName;

    /// <summary>
    /// <paramref name="name"/> made unique among <paramref name="namesInUse"/> as App Designer makes a
    /// callback's (<c>UniqueNameGenerator(false, '').generateVariableName</c>): the name itself when it
    /// is free, else with the first free number after the highest in use.
    /// </summary>
    public static string Unique(string name, IEnumerable<string> namesInUse)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(namesInUse);
        return DesignNames.GenerateVariableName([.. namesInUse], name, keepWhole: false);
    }

    private static DesignCallbacks Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-callbacks.json")
            ?? throw new InvalidOperationException("The designer's callbacks table is not embedded.");
        using JsonDocument json = JsonDocument.Parse(stream);
        var classes = new Dictionary<string, ClassCallbacks>(StringComparer.Ordinal);
        foreach (JsonProperty c in json.RootElement.GetProperty("classes").EnumerateObject())
        {
            var groups = new List<DesignCallbackGroup>();
            if (c.Value.TryGetProperty("groups", out JsonElement list))
            {
                foreach (JsonElement g in list.EnumerateArray())
                {
                    string? title = g.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    groups.Add(new DesignCallbackGroup(title, g.GetProperty("properties").EnumerateArray().Select(static e => e.GetString()!).ToArray()));
                }
            }

            var comments = new Dictionary<string, string>(StringComparer.Ordinal);
            if (c.Value.TryGetProperty("comments", out JsonElement table))
            {
                foreach (JsonProperty p in table.EnumerateObject())
                {
                    comments[p.Name] = p.Value.GetString()!;
                }
            }

            var defaults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (c.Value.TryGetProperty("defaultCode", out JsonElement code))
            {
                foreach (JsonProperty p in code.EnumerateObject())
                {
                    defaults[p.Name] = p.Value.EnumerateArray().Select(static e => e.GetString()!).ToArray();
                }
            }

            string? defaultCodeName = c.Value.TryGetProperty("defaultCodeName", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            classes[c.Name] = new ClassCallbacks(defaultCodeName, groups, [.. groups.SelectMany(static g => g.Properties)], comments, defaults);
        }

        return new DesignCallbacks(classes);
    }

    private sealed record ClassCallbacks(
        string? DefaultCodeName,
        IReadOnlyList<DesignCallbackGroup> Groups,
        IReadOnlyList<string> Properties,
        IReadOnlyDictionary<string, string> Comments,
        IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultCode);

    [GeneratedRegex(@"^%[^\S\r\n]*")]
    private static partial Regex CommentSign();
}
