using System.Globalization;
using System.Text.Json;

namespace JGraph.Scripting.Designer;

/// <summary>
/// One custom (user-authored) component as its folder's <c>resources/appDesigner.json</c> registers
/// it for App Designer's library (designer plan DS7b; App Designer's
/// <c>appdesigner.customcomponent.configureMetadata</c> writes the file, its
/// <c>UserComponentRegistrationService</c> reads it).
/// </summary>
/// <param name="ClassName">The class, package-qualified when it is in one (<c>ds7pkg.PkgGauge</c>).</param>
/// <param name="ComponentName">The name the library shows.</param>
/// <param name="Description">The description shown in the entry's tooltip; a help line by default.</param>
/// <param name="Category">The library section the author chose; shown as <c>Category (Custom)</c>.</param>
/// <param name="Icon">The 24-by-24 icon as a data URI; empty for the stock icon.</param>
/// <param name="AuthorName">The author's name, or empty.</param>
/// <param name="AuthorEmail">The author's email, or empty.</param>
/// <param name="Version">The version, <c>1.0</c>.</param>
/// <param name="DefaultPosition">The position a new one is made with; its width and height are what count.</param>
/// <param name="Folder">The folder holding the class (the metadata file's <c>resources</c> folder's parent).</param>
public sealed record DesignUserComponent(
    string ClassName, string ComponentName, string Description, string Category, string Icon,
    string AuthorName, string AuthorEmail, string Version, IReadOnlyList<double> DefaultPosition, string Folder)
{
    /// <summary>The class's name without its package: the default code name of a new one.</summary>
    public string ShortName => ClassName[(ClassName.LastIndexOf('.') + 1)..];

    /// <summary>The library section's title: the category with <c>(Custom)</c> after it, as the library titles a user's set.</summary>
    public string SectionTitle => Category + " " + DesignUserComponents.CustomSuffix;

    /// <summary>The size a new one is made with: the default position's width and height, 100 by 50 when the file gives none.</summary>
    public (double Width, double Height) DefaultSize =>
        DefaultPosition.Count == 4 ? (DefaultPosition[2], DefaultPosition[3]) : (100, 50);
}

/// <summary>
/// A custom component's class as the designer knows it once its running class has been described
/// (designer plan DS7b, <see cref="DesignRuntime.UserClassOf"/>): what the generator writes for it,
/// the inspector's groups for it, and its callback properties.
/// </summary>
/// <param name="Class">The class for the generator: its properties in <c>properties()</c> order with their defaults, <c>Position</c> last.</param>
/// <param name="InspectorGroups">The inspector's groups: the class's own properties under its short name, then the common six.</param>
/// <param name="CallbackProperties">The callback properties the Callbacks tab lists: each <c>HasCallbackProperty</c> event's, then <c>SizeChangedFcn</c>.</param>
public sealed record DesignUserClass(DesignComponentClass Class, IReadOnlyList<DesignInspectorGroup> InspectorGroups, IReadOnlyList<string> CallbackProperties);

/// <summary>
/// Custom components found for an app (designer plan DS7b): every <c>resources/appDesigner.json</c>
/// in the app's folder and on the session's path, read as App Designer's registration service reads
/// them (<c>UserComponentDefaults</c>, <c>UserComponentLibraryEntries</c>), and the library sections
/// they make. Measured in R2025b by the probe <c>ds7_uac</c> (its section 1: the file's keys and
/// their order, the defaults the schema fills in, a packaged class's folder).
/// </summary>
public static class DesignUserComponents
{
    /// <summary>The metadata file's path under a folder.</summary>
    public const string MetadataFile = "resources/appDesigner.json";

    /// <summary>What the library adds to a user's category title (<c>AppDesigner.userComponentIdentifier</c>).</summary>
    public const string CustomSuffix = "(Custom)";

    private const string DefaultDescription = "This is a custom component created by MATLAB user";
    private const string DefaultCategory = "My Category";

    /// <summary>
    /// The components registered under each of <paramref name="folders"/>, in the folders' order;
    /// a class registered twice is the later set's, as the library keeps the later set's defaults.
    /// A file that cannot be read registers nothing, as the service drops the whole set.
    /// </summary>
    public static IReadOnlyList<DesignUserComponent> Discover(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var found = new List<DesignUserComponent>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            string full;
            try
            {
                full = Path.GetFullPath(folder);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                continue;
            }

            if (!seen.Add(full))
            {
                continue;
            }

            string file = Path.Combine(full, "resources", "appDesigner.json");
            if (!File.Exists(file))
            {
                continue;
            }

            IReadOnlyList<DesignUserComponent> set;
            try
            {
                set = Read(File.ReadAllText(file), full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                continue;
            }

            foreach (DesignUserComponent component in set)
            {
                found.RemoveAll(c => string.Equals(c.ClassName, component.ClassName, StringComparison.Ordinal));
                found.Add(component);
            }
        }

        return found;
    }

    /// <summary>
    /// The components a metadata file's <paramref name="json"/> registers for the classes in
    /// <paramref name="folder"/>: the <c>components</c> array, each with the schema's defaults where
    /// a key is missing (<c>fixMetadataStructure</c>), the version as a number or a string, the
    /// default position as numbers or as the schema's own text.
    /// </summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static IReadOnlyList<DesignUserComponent> Read(string json, string folder)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(folder);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("components", out JsonElement components)
            || components.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var read = new List<DesignUserComponent>();
        foreach (JsonElement c in components.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string className = Text(c, "className", string.Empty);
            if (className.Length == 0)
            {
                continue;
            }

            read.Add(new DesignUserComponent(
                className,
                Text(c, "componentName", "Custom Component"),
                Text(c, "description", DefaultDescription),
                Text(c, "category", DefaultCategory),
                Text(c, "icon", string.Empty),
                Text(c, "authorName", string.Empty),
                Text(c, "authorEmail", string.Empty),
                Text(c, "version", "1.0"),
                Position(c),
                folder));
        }

        return read;
    }

    /// <summary>
    /// The library's sections for <paramref name="components"/> (<c>UserComponentLibraryEntries</c>,
    /// <c>AppDesignerComponentLibraryModel</c>): one per category, titled <c>Category (Custom)</c>, in
    /// case-insensitive order of their titles after the built-in sections; the entries of each by
    /// their names in ordinal order, upper case first.
    /// </summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<DesignUserComponent> Entries)> Sections(IEnumerable<DesignUserComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return components
            .GroupBy(static c => c.SectionTitle, StringComparer.Ordinal)
            .OrderBy(static g => g.Key.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(static g => (g.Key, (IReadOnlyList<DesignUserComponent>)g.OrderBy(static c => c.ComponentName, StringComparer.Ordinal).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// The custom component classes <paramref name="design"/> uses, each once, in the order the
    /// design holds them: what the package's metadata lists as <c>userComponents</c> and a plain-text
    /// app's internal data as <c>UserComponents</c>. A component is one when its class is a user's,
    /// or - read from code the designer has no class for - when it is made by a call of its own
    /// class's name (a custom component's constructor; the Simulink components are made by functions).
    /// </summary>
    public static IReadOnlyList<string> ClassesOf(AppDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var classes = new List<string>();
        foreach (DesignComponent component in design.Components)
        {
            if (IsUserComponent(component) && !classes.Contains(component.Type, StringComparer.Ordinal))
            {
                classes.Add(component.Type);
            }
        }

        return classes;
    }

    /// <summary>
    /// Whether <paramref name="component"/> is a custom component: its class is a user's, or - read
    /// from code the designer has no class for - it is made by a call of its own class's name (a
    /// custom component's constructor; the Simulink components are made by functions).
    /// </summary>
    public static bool IsUserComponent(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Class is { IsUserComponent: true }
            || (component.Class is null && component.Function == component.Type
                && !component.Type.StartsWith("matlab.", StringComparison.Ordinal) && !component.Type.StartsWith("simulink.", StringComparison.Ordinal));
    }

    private static string Text(JsonElement c, string key, string fallback)
    {
        if (!c.TryGetProperty(key, out JsonElement v))
        {
            return fallback;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? fallback,
            JsonValueKind.Number => v.TryGetDouble(out double d) ? d.ToString("0.0###", CultureInfo.InvariantCulture) : fallback,
            _ => fallback,
        };
    }

    /// <summary>
    /// The default position: an array of numbers, or the schema's text <c>[100 100 100 100]</c>
    /// (what MATLAB fills in when it rewrites a file missing the key); a file without the key gives
    /// the client's own fallback, 100 by 50 (<c>UserComponentDefaults</c>).
    /// </summary>
    private static IReadOnlyList<double> Position(JsonElement c)
    {
        double[] fallback = [0, 0, 100, 50];
        if (!c.TryGetProperty("defaultPosition", out JsonElement v))
        {
            return fallback;
        }

        if (v.ValueKind == JsonValueKind.Array)
        {
            var numbers = new List<double>();
            foreach (JsonElement e in v.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Number)
                {
                    return fallback;
                }

                numbers.Add(e.GetDouble());
            }

            return numbers.Count == 4 ? numbers : fallback;
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            string[] parts = (v.GetString() ?? string.Empty).Trim('[', ']', ' ').Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            var numbers = new List<double>();
            foreach (string part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    return fallback;
                }

                numbers.Add(d);
            }

            return numbers.Count == 4 ? numbers : fallback;
        }

        return fallback;
    }
}
