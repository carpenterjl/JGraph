using System.Text.RegularExpressions;

namespace JGraph.Scripting.Designer;

/// <summary>How the designer lays out the code it writes.</summary>
/// <param name="IndentUnit">One level of indentation: four spaces unless the app was written otherwise.</param>
/// <param name="Theme">The theme whose defaults decide which properties are written.</param>
public sealed record DesignCodeStyle(string IndentUnit = "    ", DesignTheme Theme = DesignTheme.Light)
{
    /// <summary>App Designer's own layout: four spaces, the light theme.</summary>
    public static DesignCodeStyle Default { get; } = new();

    /// <summary><paramref name="depth"/> levels of indentation.</summary>
    public string Indent(int depth) => string.Concat(Enumerable.Repeat(IndentUnit, depth));

    /// <summary>
    /// The columns one level of indentation counts for (App Designer's <c>IndentSize</c> setting):
    /// the unit's width in spaces, and four for a tab, which the setting keeps whether or not it
    /// inserts spaces.
    /// </summary>
    public int IndentSize => IndentUnit == "\t" ? 4 : IndentUnit.Length;

    /// <summary>
    /// The theme <paramref name="design"/>'s code is written under (designer plan DS3): its figure's
    /// <c>Theme</c> when set, as App Designer's design-time controller takes the figure's theme for
    /// its generator; else the light theme, R2025b's usual one.
    /// </summary>
    public static DesignTheme ThemeOf(AppDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.Figure.Get("Theme") switch
        {
            CharValue { Text: "dark" } or StringValue { Text: "dark" } => DesignTheme.Dark,
            _ => DesignTheme.Light,
        };
    }
}

/// <summary>What reading an app's code gave: the design, and every line the reader could not place.</summary>
/// <param name="Design">The design; null when the code has no <c>createComponents</c> or no figure.</param>
/// <param name="Style">The code's layout, as found.</param>
/// <param name="Problems">
/// The lines of <c>createComponents</c> that are not code App Designer generates, with their line
/// numbers (one-based). A design read with problems is missing what those lines do.
/// </param>
public sealed record DesignReadResult(AppDesign? Design, DesignCodeStyle Style, IReadOnlyList<string> Problems)
{
    /// <summary>Whether every line was placed: writing the design gives back what the code does.</summary>
    public bool IsComplete => Design is not null && Problems.Count == 0;
}

/// <summary>
/// Reads an App Designer class's generated <c>createComponents</c> into the designer's model
/// (designer plan DS1). Every App Designer app carries this code, so every app opens in the
/// designer; the component tree App Designer saves beside it is not read.
/// </summary>
/// <remarks>
/// The reader takes the statements App Designer's generator and code model write - a creation call,
/// a property set, a grid child's <c>Layout.Row</c> and <c>Layout.Column</c>, an axes' <c>title</c>,
/// <c>xlabel</c>, <c>ylabel</c>, <c>zlabel</c> and <c>subtitle</c> calls, <c>colormap</c>, a
/// <c>Toolbar.Visible</c> set, a context menu's assignment - one per line. Comments, blank lines,
/// the <c>pathToMLAPP</c> line and the figure's closing <c>Visible = 'on'</c> are the writer's to
/// make again and are passed over. Anything else is a problem the result lists.
/// </remarks>
public static partial class DesignCodeReader
{
    internal const string ComponentPropertiesComment = "% Properties that correspond to app components";

    internal const string ResponsivePropertiesComment = "% Properties that correspond to apps with auto-reflow";

    /// <summary>The axes label functions and the aliases App Designer's generator holds their text under.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LabelAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "TitleString",
        ["xlabel"] = "XLabelString",
        ["ylabel"] = "YLabelString",
        ["zlabel"] = "ZLabelString",
        ["subtitle"] = "SubtitleString",
    };

    /// <summary>Reads the class text <paramref name="text"/>.</summary>
    public static DesignReadResult Read(string text, DesignCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        catalog ??= DesignCatalog.R2025b;
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var problems = new List<string>();

        Dictionary<string, string> declared = DeclaredTypes(lines);
        int start = Array.FindIndex(lines, static l => CreateComponentsLine().IsMatch(l));
        if (start < 0)
        {
            return new DesignReadResult(null, DesignCodeStyle.Default, ["The code has no createComponents method."]);
        }

        string indent = CreateComponentsLine().Match(lines[start]).Groups[1].Value;
        var style = new DesignCodeStyle(IndentUnitOf(indent));
        int end = Array.FindIndex(lines, start + 1, l => l == indent + "end");
        if (end < 0)
        {
            return new DesignReadResult(null, style, ["createComponents is not closed."]);
        }

        AppDesign? design = null;
        var byName = new Dictionary<string, DesignComponent>(StringComparer.Ordinal);
        for (int i = start + 1; i < end; i++)
        {
            string line = lines[i].Trim();
            string where = $"line {i + 1}: {line}";
            if (line.Length == 0 || line.StartsWith('%') || line == PathToAppLine)
            {
                continue;
            }

            Match m;
            if ((m = Creation().Match(line)).Success)
            {
                string name = m.Groups["name"].Value;
                string function = m.Groups["fn"].Value;
                string arguments = m.Groups["args"].Value;
                if (byName.ContainsKey(name))
                {
                    problems.Add(where + " (a second component of that name)");
                    continue;
                }

                if (design is null)
                {
                    DesignComponentClass? figureClass = catalog.ByCreation(function, null);
                    if (figureClass is not { IsFigure: true } || arguments != "'Visible', 'off'")
                    {
                        problems.Add(where + " (the first component made is not the figure)");
                        continue;
                    }

                    var figure = new DesignComponent(name, figureClass);
                    design = new AppDesign(figure);
                    byName[name] = figure;
                    continue;
                }

                Match call = ParentArguments().Match(arguments);
                if (!call.Success || !byName.TryGetValue(call.Groups["parent"].Value, out DesignComponent? parent))
                {
                    problems.Add(where + " (its parent is not a component made before it)");
                    continue;
                }

                string extra = call.Groups["extra"].Value;
                Match styled = StyleArgument().Match(extra);
                DesignComponentClass? cls = catalog.ByCreation(function, styled.Success ? styled.Groups[1].Value : extra.Length == 0 ? null : "\0");
                if (cls is not null && declared.TryGetValue(name, out string? type) && type != cls.Type)
                {
                    cls = null; // the declared class wins over a guess from the call
                }

                var component = cls is not null
                    ? new DesignComponent(name, cls)
                    : new DesignComponent(name, declared.GetValueOrDefault(name, function), function, extra);
                parent.Add(component);
                byName[name] = component;
                continue;
            }

            if (design is null)
            {
                problems.Add(where + " (before the figure is made)");
                continue;
            }

            if (!TryReadSet(line, design, byName, out string? problem))
            {
                problems.Add(where + (problem is null ? string.Empty : $" ({problem})"));
            }
        }

        if (design is null)
        {
            problems.Add("createComponents makes no figure.");
        }
        else
        {
            // Responsive when the code says so, by the properties block App Designer writes for one alone:
            // the text's counterpart of the file's AppType, which App Designer never infers from the
            // components (DS4b). The figure's SizeChangedFcn names the layout callback, as App Designer's
            // template gives it on load (ResponsiveAppFactory.applyDesignTimeData), whatever the code
            // says - where there is one: in a class that still has it, or in a template laid out as App
            // Designer lays an app out, whose callback a save puts back (DesignResponsive.Splice).
            design.IsResponsive = Array.Exists(lines, static l => l.Trim() == ResponsivePropertiesComment);
            if (design.IsResponsive && design.Figure.Get("SizeChangedFcn") is not CallbackValue
                && (Array.Exists(lines, static l => LayoutFunction().IsMatch(l))
                    || (DesignResponsive.IsTemplate(design) && JGraph.Scripting.Jgs.JgsMlappLayout.Of(text) is { IsAppLayout: true, IsComplete: true })))
            {
                design.Figure.Set("SizeChangedFcn", new CallbackValue(DesignResponsive.Callback));
            }
            style = style with { Theme = DesignCodeStyle.ThemeOf(design) };
        }

        return new DesignReadResult(design, style, problems);
    }

    /// <summary>Reads one statement that sets something on a component already made.</summary>
    private static bool TryReadSet(string line, AppDesign design, Dictionary<string, DesignComponent> byName, out string? problem)
    {
        problem = null;
        Match m;
        if ((m = Label().Match(line)).Success)
        {
            return Put(m.Groups["name"].Value, LabelAliases[m.Groups["fn"].Value], m.Groups["value"].Value, out problem);
        }

        if ((m = Colormap().Match(line)).Success)
        {
            return Put(m.Groups["name"].Value, "AD_ColormapString", m.Groups["value"].Value, out problem);
        }

        if (!(m = PropertySet().Match(line)).Success)
        {
            return false;
        }

        string name = m.Groups["name"].Value;
        string property = m.Groups["property"].Value;
        string value = m.Groups["value"].Value;
        switch (property)
        {
            case "Layout.Row" or "Layout.Column":
                return Put(name, property, value, out problem);
            case "Toolbar.Visible":
                return Put(name, "ToolbarVisible", value, out problem);
            case "Visible" when name == design.Figure.CodeName && value == "'on'":
                return true; // the figure shown once every component is made: the writer's to write
            case "ThemeChangedFcn" when name == design.Figure.CodeName:
                return Put(name, "AD_AliasedThemeChangedFcn", value, out problem);
            default:
                return !property.Contains('.', StringComparison.Ordinal) && Put(name, property, value, out problem);
        }

        bool Put(string component, string key, string rhs, out string? why)
        {
            why = null;
            if (!byName.TryGetValue(component, out DesignComponent? target))
            {
                why = "no component of that name is made before it";
                return false;
            }

            if (target.Get(key) is not null)
            {
                why = "the property is set twice";
                return false;
            }

            DesignValue parsed = DesignValueText.Parse(rhs);
            if (key == "ContextMenu" && parsed is not ComponentsValue { Bracketed: false, CodeNames.Count: 1 })
            {
                why = "a context menu is assigned as something other than a component of the app";
                return false;
            }

            target.Set(key, parsed);
            return true;
        }
    }

    /// <summary>The component properties block's names and classes.</summary>
    internal static Dictionary<string, string> DeclaredTypes(string[] lines)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        int comment = Array.FindIndex(lines, static l => l.Trim() == ComponentPropertiesComment);
        if (comment < 0 || comment + 1 >= lines.Length || !lines[comment + 1].Trim().StartsWith("properties", StringComparison.Ordinal))
        {
            return declared;
        }

        for (int i = comment + 2; i < lines.Length && lines[i].Trim() != "end"; i++)
        {
            Match m = Declaration().Match(lines[i]);
            if (m.Success)
            {
                declared[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }

        return declared;
    }

    /// <summary>One level of indentation, from the indentation of a method two levels in.</summary>
    private static string IndentUnitOf(string methodIndent) =>
        methodIndent.Length > 0 && methodIndent.Length % 2 == 0 && methodIndent.Distinct().Count() == 1
            ? methodIndent[..(methodIndent.Length / 2)]
            : "    ";

    internal const string PathToAppLine = "pathToMLAPP = fileparts(mfilename('fullpath'));";

    [GeneratedRegex(@"^(\s*)function createComponents\(app\)\s*$")]
    private static partial Regex CreateComponentsLine();

    /// <summary>The signature of a responsive app's layout callback, its parameters however written.</summary>
    [GeneratedRegex(@"^\s*function\s+updateAppLayout\s*\(")]
    private static partial Regex LayoutFunction();

    [GeneratedRegex(@"^app\.(?<name>[A-Za-z]\w*) = (?<fn>[A-Za-z]\w*)\((?<args>.*)\);$")]
    private static partial Regex Creation();

    [GeneratedRegex(@"^app\.(?<parent>[A-Za-z]\w*)(?:, (?<extra>.*))?$")]
    private static partial Regex ParentArguments();

    [GeneratedRegex(@"^'(\w+)'$")]
    private static partial Regex StyleArgument();

    [GeneratedRegex(@"^app\.(?<name>[A-Za-z]\w*)\.(?<property>[A-Za-z]\w*(?:\.[A-Za-z]\w*)?) = (?<value>.*);$")]
    private static partial Regex PropertySet();

    [GeneratedRegex(@"^(?<fn>title|xlabel|ylabel|zlabel|subtitle)\(app\.(?<name>[A-Za-z]\w*), (?<value>.*)\)$")]
    private static partial Regex Label();

    [GeneratedRegex(@"^colormap\(app\.(?<name>[A-Za-z]\w*), (?<value>.*)\);$")]
    private static partial Regex Colormap();

    [GeneratedRegex(@"^\s*([A-Za-z]\w*)\s+([\w.]+)\s*$")]
    private static partial Regex Declaration();
}
