using System.Reflection;
using System.Text.Json;

namespace JGraph.Scripting.Designer;

/// <summary>How the inspector edits a property.</summary>
public enum DesignEditorKind
{
    /// <summary>Text, as the property's characters (<c>Text</c>, <c>Title</c>, <c>Tag</c>).</summary>
    Text,

    /// <summary>A value written as MATLAB writes it: numbers, a cell of items, limits.</summary>
    Value,

    /// <summary>On or off.</summary>
    OnOff,

    /// <summary>True or false.</summary>
    Logical,

    /// <summary>One of a list of choices.</summary>
    Choice,

    /// <summary>A colour, <c>[r g b]</c>.</summary>
    Color,

    /// <summary>Another component of the app: a context menu.</summary>
    Component,

    /// <summary>Shown and not edited: a property the generator never writes (a mode, a read-only property).</summary>
    ReadOnly,
}

/// <summary>One property as the inspector shows it.</summary>
/// <param name="Name">The property's name as the design holds it (App Designer's alias where it has one).</param>
/// <param name="Kind">How it is edited.</param>
/// <param name="Choices">The choices of a <see cref="DesignEditorKind.Choice"/>; empty otherwise.</param>
public sealed record DesignEditor(string Name, DesignEditorKind Kind, IReadOnlyList<string> Choices);

/// <summary>A row of the inspector: one property, or properties edited together (an item's value and the items).</summary>
public sealed record DesignInspectorRow(IReadOnlyList<DesignEditor> Editors);

/// <summary>A group of the inspector, as App Designer titles and opens it.</summary>
public sealed record DesignInspectorGroup(string Title, bool Expanded, IReadOnlyList<DesignInspectorRow> Rows);

/// <summary>
/// App Designer's inspector (designer plan DS3): for each component class, the groups R2025b's
/// property view for it shows (<c>inspector.internal.&lt;Class&gt;PropertyView</c>), in order, with
/// their titles, whether they open expanded, and their rows, as the probe <c>ds3_inspector</c>
/// records them. A row naming a property the view does not have is left out, as the inspector
/// leaves it out.
/// </summary>
/// <remarks>
/// A property the code generator writes (the catalog's) is edited; any other - a mode, a read-only
/// property, an object - is shown and not edited, since a save could not keep it. How each is edited
/// follows the class the view types it as: on/off, a choice of the class's members or enumerated
/// values, a colour (a <c>double</c> property named for one), text for a char or a file's path, and the value as
/// MATLAB writes it for the rest; the figure's theme is a choice of light and dark. A menu's
/// <c>Label</c> is its <c>Text</c>, an axes' <c>AD_AliasedVisible</c> its <c>Visible</c>.
/// </remarks>
public sealed class DesignInspector
{
    private readonly Dictionary<string, IReadOnlyList<DesignInspectorGroup>> _byType = new(StringComparer.Ordinal);

    private DesignInspector()
    {
    }

    /// <summary>The inspector recorded from R2025b, over the catalog <see cref="DesignCatalog.R2025b"/>.</summary>
    public static DesignInspector R2025b { get; } = Load(DesignCatalog.R2025b);

    /// <summary>The groups the inspector shows for <paramref name="type"/>; empty for a class it does not know.</summary>
    public IReadOnlyList<DesignInspectorGroup> GroupsOf(string type) =>
        _byType.TryGetValue(type, out IReadOnlyList<DesignInspectorGroup>? groups) ? groups : [];

    /// <summary>The design's name for a property the inspector names <paramref name="name"/> on <paramref name="type"/>.</summary>
    public static string DesignName(string type, string name) => (type, name) switch
    {
        ("matlab.ui.container.Menu", "Label") => "Text",
        ("matlab.ui.control.UIAxes", "AD_AliasedVisible") => "Visible",
        _ => name,
    };

    private static DesignInspector Load(DesignCatalog catalog)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-inspector.json")
            ?? throw new InvalidOperationException("The designer's inspector is not embedded.");
        using JsonDocument json = JsonDocument.Parse(stream);
        var inspector = new DesignInspector();
        foreach (JsonElement c in json.RootElement.GetProperty("classes").EnumerateArray())
        {
            string type = c.GetProperty("type").GetString()!;
            DesignComponentClass? known = catalog.ByType(type);
            JsonElement properties = c.GetProperty("properties");
            var groups = new List<DesignInspectorGroup>();
            foreach (JsonElement g in Items(c.GetProperty("groups")))
            {
                var rows = new List<DesignInspectorRow>();
                foreach (JsonElement item in Items(g.GetProperty("items")))
                {
                    var editors = new List<DesignEditor>();
                    foreach (JsonElement n in Items(item.GetProperty("names")))
                    {
                        string name = n.GetString()!;
                        if (!properties.TryGetProperty(name, out JsonElement p) || p.GetProperty("class").GetString() == "?")
                        {
                            continue; // the view does not have it, so the inspector does not show it
                        }

                        string[] members = Items(p.GetProperty("members")).Select(static m => m.GetString()!).ToArray();
                        if (p.GetProperty("class").GetString() == "inspector.internal.datatype.Theme")
                        {
                            members = ["light", "dark"]; // the inspector's theme choice: the figure's two themes
                        }

                        string designName = DesignName(type, name);
                        editors.Add(new DesignEditor(designName, KindOf(known, designName, p.GetProperty("class").GetString()!, members), members));
                    }

                    // A sub-group's properties are rows of their own; an editor group's are one row.
                    if (item.GetProperty("kind").GetString() == "editor")
                    {
                        if (editors.Count > 0)
                        {
                            rows.Add(new DesignInspectorRow(editors));
                        }
                    }
                    else
                    {
                        rows.AddRange(editors.Select(static e => new DesignInspectorRow([e])));
                    }
                }

                if (rows.Count > 0)
                {
                    groups.Add(new DesignInspectorGroup(g.GetProperty("title").GetString()!, g.GetProperty("expanded").GetBoolean(), rows));
                }
            }

            inspector._byType[type] = groups;
        }

        return inspector;
    }

    private static DesignEditorKind KindOf(DesignComponentClass? known, string name, string className, string[] members)
    {
        if (name == "ContextMenu")
        {
            return DesignEditorKind.Component;
        }

        if (known?.Property(name) is null)
        {
            return DesignEditorKind.ReadOnly;
        }

        return className switch
        {
            "matlab.lang.OnOffSwitchState" => DesignEditorKind.OnOff,
            "logical" => DesignEditorKind.Logical,
            "char" or "internal.matlab.editorconverters.datatype.FullPath" or "internal.matlab.editorconverters.datatype.FileName" => DesignEditorKind.Text,
            "double" when name.EndsWith("Color", StringComparison.Ordinal) => DesignEditorKind.Color,
            _ when members.Length > 0 => DesignEditorKind.Choice,
            _ => DesignEditorKind.Value,
        };
    }

    private static IEnumerable<JsonElement> Items(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray(),
        JsonValueKind.Null => [],
        _ => [element],
    };
}
