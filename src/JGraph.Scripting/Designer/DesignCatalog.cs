using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Designer;

/// <summary>The theme App Designer generates an app's code under, which decides some defaults (colours).</summary>
public enum DesignTheme
{
    /// <summary>No theme: the defaults of releases before themes.</summary>
    Unthemed,

    /// <summary>The light theme, R2025b's usual one.</summary>
    Light,

    /// <summary>The dark theme.</summary>
    Dark,
}

/// <summary>
/// What App Designer's component code generator knows about each <c>uifigure</c> component class
/// (designer plan DS1), as R2025b's adapters report it: recorded by the probe <c>ds1_catalog</c>
/// (<c>tools/matlab-checklist/ui-probes/ds1</c>) and embedded from there.
/// </summary>
public sealed partial class DesignCatalog
{
    private readonly Dictionary<string, DesignComponentClass> _byType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DesignComponentClass> _byCreation = new(StringComparer.Ordinal);

    private DesignCatalog(string release, IReadOnlyList<DesignComponentClass> classes)
    {
        Release = release;
        Classes = classes;
        foreach (DesignComponentClass c in classes)
        {
            _byType[c.Type] = c;
            _byCreation[CreationKey(c.Function, c.Style)] = c;
        }
    }

    /// <summary>The catalog recorded from R2025b.</summary>
    public static DesignCatalog R2025b { get; } = Load();

    /// <summary>The release the catalog was recorded from.</summary>
    public string Release { get; }

    /// <summary>Every component class, figure included.</summary>
    public IReadOnlyList<DesignComponentClass> Classes { get; }

    /// <summary>The class named <paramref name="type"/> (<c>matlab.ui.control.Button</c>), or null.</summary>
    public DesignComponentClass? ByType(string type) => _byType.GetValueOrDefault(type);

    /// <summary>
    /// The class whose type ends in <paramref name="shortName"/> after the last dot - the element
    /// name a plain-text app's appendix gives a component (designer plan DS7a) - or null.
    /// </summary>
    public DesignComponentClass? ByShortName(string shortName)
    {
        ArgumentNullException.ThrowIfNull(shortName);
        return Classes.FirstOrDefault(c => string.Equals(c.ShortName, shortName, StringComparison.Ordinal));
    }

    /// <summary>
    /// The class a creation call makes: <c>uibutton</c> with the style <c>'push'</c>, <c>uilabel</c>
    /// with none. Null when no class is made that way.
    /// </summary>
    public DesignComponentClass? ByCreation(string function, string? style) =>
        _byCreation.GetValueOrDefault(CreationKey(function, style));

    private static string CreationKey(string function, string? style) => style is null ? function : function + "/" + style;

    private static DesignCatalog Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-catalog.json")
            ?? throw new InvalidOperationException("The designer's component catalog is not embedded.");
        using JsonDocument json = JsonDocument.Parse(stream);
        JsonElement root = json.RootElement;
        var classes = new List<DesignComponentClass>();
        foreach (JsonElement c in root.GetProperty("classes").EnumerateArray())
        {
            string creation = c.GetProperty("creation").GetString()!;
            Match call = Creation().Match(creation);
            if (!call.Success)
            {
                throw new InvalidDataException($"The catalog's creation call '{creation}' is not one the designer reads.");
            }

            var properties = new List<DesignProperty>();
            foreach (JsonElement p in Items(c.GetProperty("properties")))
            {
                var defaults = new Dictionary<DesignTheme, DesignValue>();
                foreach (JsonProperty d in p.GetProperty("default").EnumerateObject())
                {
                    DesignTheme theme = d.Name switch
                    {
                        "unthemed" => DesignTheme.Unthemed,
                        "light" => DesignTheme.Light,
                        "dark" => DesignTheme.Dark,
                        _ => throw new InvalidDataException($"Unknown theme '{d.Name}'."),
                    };
                    defaults[theme] = Decode(d.Value);
                }

                properties.Add(new DesignProperty(p.GetProperty("name").GetString()!, p.GetProperty("mode").GetBoolean(), defaults));
            }

            string? valueProperty = c.GetProperty("valueProperty") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
            classes.Add(new DesignComponentClass(
                c.GetProperty("type").GetString()!,
                call.Groups["fn"].Value,
                call.Groups["style"].Success ? call.Groups["style"].Value : null,
                call.Groups["parent"].Success,
                string.IsNullOrEmpty(valueProperty) ? null : valueProperty,
                properties));
        }

        return new DesignCatalog(root.GetProperty("release").GetString()!, classes);
    }

    /// <summary>A JSON value that MATLAB's <c>jsonencode</c> writes as one element or an array of them.</summary>
    private static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [element];

    /// <summary>A value as the probe encodes it: <c>{c: class, s: size, v: data}</c>.</summary>
    private static DesignValue Decode(JsonElement e)
    {
        string cls = e.GetProperty("c").GetString()!;
        int[] size = Items(e.GetProperty("s")).Select(static s => s.GetInt32()).ToArray();
        int rows = size[0];
        int cols = size.Skip(1).Aggregate(1, static (a, b) => a * b);
        int count = rows * cols;
        JsonElement[] data = e.TryGetProperty("v", out JsonElement v) ? Items(v).ToArray() : [];
        switch (cls)
        {
            case "double" or "single" or "logical" or "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64":
                return new NumericValue(rows, cols, data.Select(Number).ToArray(), cls == "logical");
            case "char":
                return count == 0 ? new CharValue(string.Empty)
                    : rows == 1 ? new CharValue(data[0].GetString()!)
                    : new OpaqueValue("char", isEmpty: false);
            case "string":
                return count == 1 ? new StringValue(data[0].GetString()!) : new OpaqueValue("string", count == 0);
            case "cell":
                return new CellValue(rows, cols, data.Select(Decode).ToArray());
            case "onoff":
                return data[0].GetString() == "on" ? OnOffValue.On : OnOffValue.Off;
            case "enum":
                return new CharValue(data[0].GetString()!);
            case "datetime":
                return new DatetimeValue(rows, cols, data.Select(static d => d.ValueKind == JsonValueKind.String
                    ? (DesignDate?)null
                    : new DesignDate(d[0].GetInt32(), d[1].GetInt32(), d[2].GetInt32())).ToArray());
            default:
                return new OpaqueValue(e.TryGetProperty("class", out JsonElement c) ? c.GetString()! : cls, count == 0);
        }
    }

    private static double Number(JsonElement d) => d.ValueKind == JsonValueKind.String
        ? d.GetString() switch
        {
            "Inf" => double.PositiveInfinity,
            "-Inf" => double.NegativeInfinity,
            _ => double.NaN,
        }
        : d.GetDouble();

    [GeneratedRegex(@"^(?<fn>\w+)\((?:(?<parent>%PARENT%)(?:, '(?<style>\w+)')?|'Visible', 'off')\)$")]
    private static partial Regex Creation();
}

/// <summary>One component class as App Designer's generator sees it.</summary>
public sealed class DesignComponentClass
{
    private readonly Dictionary<string, int> _index;

    internal DesignComponentClass(string type, string function, string? style, bool takesParent, string? valueProperty, IReadOnlyList<DesignProperty> properties)
    {
        Type = type;
        Function = function;
        Style = style;
        TakesParent = takesParent;
        ValueProperty = valueProperty;
        Properties = properties;
        _index = properties.Select(static (p, i) => (p.Name, i)).ToDictionary(static e => e.Name, static e => e.i, StringComparer.Ordinal);
    }

    /// <summary>The class, as <c>matlab.ui.control.Button</c>.</summary>
    public string Type { get; }

    /// <summary>The class's name without its package, as <c>Button</c>.</summary>
    public string ShortName => Type[(Type.LastIndexOf('.') + 1)..];

    /// <summary>The function that makes one, as <c>uibutton</c>.</summary>
    public string Function { get; }

    /// <summary>The style argument after the parent, as <c>push</c>; null when the call has none.</summary>
    public string? Style { get; }

    /// <summary>Whether the creation call takes the parent (every class but the figure).</summary>
    public bool TakesParent { get; }

    /// <summary>The property App Designer treats as the component's value, written last; null when none.</summary>
    public string? ValueProperty { get; }

    /// <summary>The properties the generator writes, in the order it writes them.</summary>
    public IReadOnlyList<DesignProperty> Properties { get; }

    /// <summary>Whether this is the figure.</summary>
    public bool IsFigure => Type == "matlab.ui.Figure";

    /// <summary>The property named <paramref name="name"/>, or null when the generator never writes it.</summary>
    public DesignProperty? Property(string name) => _index.TryGetValue(name, out int i) ? Properties[i] : null;

    /// <summary>Where <paramref name="name"/> comes in the generator's order, or -1.</summary>
    public int IndexOf(string name) => _index.GetValueOrDefault(name, -1);

    /// <summary>The creation call with the parent named <paramref name="parent"/>, as <c>uibutton(app.Panel, 'push')</c>.</summary>
    public string CreationCall(string parent) => !TakesParent
        ? Function + "('Visible', 'off')"
        : Style is null
            ? $"{Function}({parent})"
            : $"{Function}({parent}, '{Style}')";

    /// <inheritdoc/>
    public override string ToString() => Type;
}

/// <summary>One property the generator writes for a class.</summary>
public sealed class DesignProperty
{
    private readonly IReadOnlyDictionary<DesignTheme, DesignValue> _defaults;

    internal DesignProperty(string name, bool hasMode, IReadOnlyDictionary<DesignTheme, DesignValue> defaults)
    {
        Name = name;
        HasMode = hasMode;
        _defaults = defaults;
    }

    /// <summary>The property's name, or App Designer's alias for it (<c>TitleString</c>, <c>AD_ColormapString</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// Whether the property has a <c>Mode</c> sibling. Such a property is written only once it has
    /// been set, which turns its mode to manual.
    /// </summary>
    public bool HasMode { get; }

    /// <summary>
    /// The run-time default under <paramref name="theme"/>, which a value is compared with; null when
    /// the property has none to compare (an alias App Designer handles itself).
    /// </summary>
    public DesignValue? DefaultFor(DesignTheme theme) =>
        _defaults.TryGetValue(theme, out DesignValue? themed) ? themed : _defaults.GetValueOrDefault(DesignTheme.Unthemed);

    /// <inheritdoc/>
    public override string ToString() => Name.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A recorded default the designer does not take apart (an object): only whether it is empty is known.</summary>
internal sealed class OpaqueValue : DesignValue
{
    private readonly bool _isEmpty;

    public OpaqueValue(string className, bool isEmpty)
    {
        ClassName = className;
        _isEmpty = isEmpty;
    }

    public string ClassName { get; }

    public override bool IsEmpty => _isEmpty;
}
