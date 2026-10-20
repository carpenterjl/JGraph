using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using JGraph.Core.Primitives;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>The two file formats an App Designer app is kept in.</summary>
public enum DesignFileFormat
{
    /// <summary>The <c>.mlapp</c> package: the class with its generated methods, and App Designer's model beside it.</summary>
    Mlapp,

    /// <summary>
    /// The plain-text <c>.m</c> file (designer plan DS7a): the class under <c>matlab.apps.App</c>
    /// without <c>createComponents</c>, constructor or <c>delete</c>, and the components in an
    /// appendix of XML after it.
    /// </summary>
    PlainText,
}

/// <summary>
/// What a plain-text app's appendix holds besides its components (designer plan DS7a): the app
/// details App Designer's App Details dialog edits, the internal data its metadata model keeps,
/// and the thumbnail its save writes. Read from the file and written back as they were; a new
/// app's are App Designer's defaults.
/// </summary>
public sealed class DesignPlainTextDetails
{
    /// <summary>The release whose App Designer wrote the file (<c>MATLABRelease</c>).</summary>
    public const string Release = "R2025b";

    /// <summary>The app's name as App Details shows it; a new app's is its class name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The author (<c>Author</c>).</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>The version (<c>Version</c>); App Designer starts an app at <c>1.0</c>.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>The one-line summary (<c>Summary</c>).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The description (<c>Description</c>), which may run over several lines.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The app's identifier (<c>AppId</c>, the metadata model's <c>Uuid</c>); one is made for a new app.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>The release that wrote the file (<c>MATLABRelease</c>); empty when the section lacks it.</summary>
    public string MatlabRelease { get; set; } = Release;

    /// <summary>The oldest release that opens the file (<c>MinimumSupportedMATLABRelease</c>); empty when absent.</summary>
    public string MinimumSupportedMatlabRelease { get; set; } = string.Empty;

    /// <summary>The file format version (<c>FileFormatVersion</c>); R2025b never writes one.</summary>
    public string FileFormatVersion { get; set; } = string.Empty;

    /// <summary>The products the app needs (<c>RequiredProducts</c>).</summary>
    public List<string> RequiredProducts { get; } = [];

    /// <summary>The custom component classes the app uses (<c>UserComponents</c>).</summary>
    public List<string> UserComponents { get; } = [];

    /// <summary>The images kept beside the app (<c>ImageRelativePaths</c>).</summary>
    public List<string> ImageRelativePaths { get; } = [];

    /// <summary>Whether the file ends in a thumbnail section, as every file App Designer saves does.</summary>
    public bool HasThumbnail { get; set; } = true;

    /// <summary>Whether App Designer captures the thumbnail itself (<c>autoCapture</c>).</summary>
    public bool ThumbnailAutoCapture { get; set; } = true;

    /// <summary>The thumbnail as a data URI; empty when the file holds none.</summary>
    public string ThumbnailDataUri { get; set; } = string.Empty;

    /// <summary>
    /// The details a new plain-text app starts with (<c>_AppFactory.getNewAppData</c>): its name,
    /// version 1.0, a fresh id, this release, and the oldest release that opens a plain-text app -
    /// R2025a, or R2019a for a responsive one, as <c>ResponsiveAppFactory</c> overrides it.
    /// </summary>
    public static DesignPlainTextDetails NewApp(string className, bool responsive)
    {
        ArgumentNullException.ThrowIfNull(className);
        return new DesignPlainTextDetails
        {
            Name = className,
            Version = "1.0",
            AppId = Guid.NewGuid().ToString(),
            MinimumSupportedMatlabRelease = responsive ? "R2019a" : "R2025a",
        };
    }
}

/// <summary>What reading a plain-text app gave: the design, as the code reader gives it, and the appendix's other sections.</summary>
/// <param name="Read">The design, its style and the problems found; the design is null when the file is not a plain-text app.</param>
/// <param name="Details">The appendix's details, as read.</param>
public sealed record DesignPlainTextReadResult(DesignReadResult Read, DesignPlainTextDetails Details);

/// <summary>
/// App Designer's plain-text app format (designer plan DS7a), which R2025b carries behind the
/// feature <c>AppDesignerPlainTextFileFormat</c>: a <c>.m</c> file whose class, written under
/// <c>matlab.apps.App</c>, holds the user's code alone, and whose <c>%[appendix]</c> holds the
/// components as XML - one element per component named by its class's last segment, its set
/// properties as child elements sorted by name, a <c>Children</c> element for what it holds - with
/// further sections for the run configuration, the app details, the internal data and the
/// thumbnail. The writer is App Designer's <c>PlainTextFileContentGroupBehavior</c> and its XML
/// builders transcribed, checked against those modules run under node (<c>ds7/oracle</c>); the
/// reader does what its MATLAB side does when it turns the appendix back into a
/// <c>createComponents</c> (<c>appdesigner.internal.artifactgenerator</c>).
/// </summary>
public static partial class DesignPlainText
{
    /// <summary>The class a plain-text app derives from (<c>PlainTextStrategy.getSuperClassNames</c>).</summary>
    public const string Superclass = "matlab.apps.App";

    /// <summary>The line the appendix begins with.</summary>
    public const string AppendixMarker = JgsMlappLayout.AppendixMarker;

    private const string SectionDelimiter = "%---";
    private const string XmlPreamble = "<?xml version='1.0' encoding='UTF-8'?>";
    private const string LayoutGrammar = "app:layout";
    private const string GroupsGrammar = "app:componentGroups";
    private const string RunConfigurationGrammar = "app:runConfiguration";
    private const string AppDetailsGrammar = "app:appDetails";
    private const string InternalDataGrammar = "app:internalData";
    private const string ThumbnailGrammar = "app:thumbnail";
    private const string LayoutRoot = "Components";
    private const string RunConfigurationRoot = "RunConfiguration";
    private const string AppDetailsRoot = "AppDetails";
    private const string InternalDataRoot = "InternalData";
    private const string ThumbnailRoot = "Thumbnail";
    private const string FigureTag = "UIFigure";

    /// <summary>What the problem for a layout section that is not well-formed XML begins with; the parser's message follows.</summary>
    public const string MalformedXmlProblem = "The layout section is not well-formed XML: ";

    /// <summary>The comment App Designer's save writes over the thumbnail (<c>AppThumbnailUtils.createThumbnailAppendixSectionStr</c>).</summary>
    private const string ThumbnailComment =
        "<!-- Thumbnail is used by file previewers. To change how the thumbnail is captured or stored, use the App Details dialog box in App Designer. -->";

    private const string ThumbnailPreamble = "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>";

    /// <summary>The property names the design holds under a generator alias, and the names the appendix writes them under.</summary>
    private static readonly IReadOnlyDictionary<string, string> AppendixNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TitleString"] = "Title.String",
        ["XLabelString"] = "XLabel.String",
        ["YLabelString"] = "YLabel.String",
        ["ZLabelString"] = "ZLabel.String",
        ["SubtitleString"] = "Subtitle.String",
        ["ToolbarVisible"] = "Toolbar.Visible",
        ["AD_ColormapString"] = "Colormap",
        ["AD_AliasedThemeChangedFcn"] = "ThemeChangedFcn",
    };

    private static readonly IReadOnlyDictionary<string, string> DesignNames =
        AppendixNames.ToDictionary(static p => p.Value, static p => p.Key, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="text"/> is a plain-text app: a class file with an appendix holding a layout section.</summary>
    public static bool IsPlainTextApp(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int appendix = text.IndexOf(AppendixMarker, StringComparison.Ordinal);
        return appendix >= 0 && text.IndexOf("%[" + LayoutGrammar + "]", appendix, StringComparison.Ordinal) > appendix;
    }

    // --- writing ---------------------------------------------------------------------------------

    /// <summary>
    /// The whole file: the class as <see cref="DesignClassWriter"/> writes it for this format, then
    /// the appendix (<see cref="AppendixLines"/>), joined by <paramref name="newline"/>. App Designer's
    /// save ends the file after the thumbnail's <c>%}</c> with no line ending; one is added when
    /// <paramref name="trailingNewline"/>.
    /// </summary>
    public static string Render(AppDesign design, DesignCode code, DesignPlainTextDetails details, DesignCodeStyle? style = null, Func<DesignComponent, Size2D?>? sizeOf = null, string newline = "\n", bool trailingNewline = false)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(details);
        style ??= DesignCodeStyle.Default;
        var lines = new List<string>(DesignClassWriter.Lines(design, code, style, sizeOf, DesignFileFormat.PlainText));
        lines.AddRange(AppendixLines(design, code, details, style));
        string text = string.Join(newline, lines);
        if (newline != "\n")
        {
            // A value with a line break of its own (an app detail of several lines) ends its lines as the file does.
            text = text.Replace("\n", newline, StringComparison.Ordinal);
        }

        return trailingNewline ? text + newline : text;
    }

    /// <summary>
    /// The appendix, from the empty line after the class's <c>end</c> to the thumbnail's <c>%}</c>
    /// (<c>PlainTextFileContentGroupBehavior</c>, <c>XMLAppendixBehavior</c>): the layout section
    /// always; the run configuration when the app has a startup function or runs as a single
    /// instance; the app details when any is set; the internal data when any is set; the thumbnail
    /// when the file carries one (every file App Designer saves does). Component groups are App
    /// Designer's own (open item 150) and are not written.
    /// </summary>
    public static IReadOnlyList<string> AppendixLines(AppDesign design, DesignCode code, DesignPlainTextDetails details, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(details);
        style ??= DesignCodeStyle.Default;
        var lines = new List<string> { string.Empty, AppendixMarker };

        // The layout section: its builder writes no empty line before it.
        lines.Add(SectionDelimiter);
        lines.Add($"%[{LayoutGrammar}]");
        lines.Add("%{");
        lines.Add(XmlPreamble);
        lines.Add($"<{LayoutRoot}>");
        Component(lines, design, design.Figure, 1, style);
        lines.Add($"</{LayoutRoot}>");
        lines.Add("%}");

        // The run configuration (XMLRunConfigPropertiesGroupBehavior): its two elements by name.
        var run = new List<(string Name, string Value)>();
        if (code.SingleInstance)
        {
            run.Add(("SingleRunningInstance", "true"));
        }

        if (code.Startup is { } startup)
        {
            run.Add(("StartupFcn", startup.Name));
        }
        else if (code.UnplacedStartup is { } unplaced)
        {
            run.Add(("StartupFcn", unplaced)); // named by the file, written outside the callbacks block
        }

        Section(lines, RunConfigurationGrammar, RunConfigurationRoot, run, style);

        // The app details (XMLAppDetailsPropertiesGroupBehavior): the five, those set, by name.
        var appDetails = new List<(string, string)>();
        foreach ((string name, string value) in (ReadOnlySpan<(string, string)>)
            [("Author", details.Author), ("Description", details.Description), ("Name", details.Name), ("Summary", details.Summary), ("Version", details.Version)])
        {
            if (value.Length > 0)
            {
                appDetails.Add((name, value));
            }
        }

        Section(lines, AppDetailsGrammar, AppDetailsRoot, appDetails, style);

        // The internal data (XMLAppInternalDetailsPropertiesGroupBehavior): Uuid written as AppId;
        // a list of one as its element, a longer one as Product elements, as App Designer's
        // builders write them once the list is set.
        var internalData = new List<(string, string)>();
        var lists = new List<(string Name, IReadOnlyList<string> Items)>();
        void Scalar(string name, string value)
        {
            if (value.Length > 0)
            {
                internalData.Add((name, value));
            }
        }

        Scalar("AppId", details.AppId);
        Scalar("AppType", design.IsResponsive ? "Responsive" : "Standard");
        Scalar("FileFormatVersion", details.FileFormatVersion);
        Scalar("MATLABRelease", details.MatlabRelease);
        Scalar("MinimumSupportedMATLABRelease", details.MinimumSupportedMatlabRelease);

        // The images kept beside the app are listed, which is how the reader tells an image beside
        // the app from a file named on the path, both written as the file's name in quotes. The
        // list is the design's now, not the file's: an image taken out of the app leaves it.
        List<string> images = [.. ImagesOf(design)];

        // The custom component classes are the design's too, as App Designer's class tracker keeps
        // them (DS7b); the products are the file's.
        List<string> userComponents = [.. DesignUserComponents.ClassesOf(design)];

        // Every list is written as Product elements, one per entry, whatever its length: the form
        // a saved file shows once App Designer's trackers have set the lists again after the
        // engine is built (the builder's one-element form is never reached).
        foreach ((string name, List<string> items) in (ReadOnlySpan<(string, List<string>)>)
            [("ImageRelativePaths", images), ("RequiredProducts", details.RequiredProducts), ("UserComponents", userComponents)])
        {
            if (items.Count > 0)
            {
                internalData.Add((name, string.Empty));
                lists.Add((name, items));
            }
        }

        Section(lines, InternalDataGrammar, InternalDataRoot, internalData, style, lists);

        // The thumbnail, which App Designer's MATLAB side appends on save after two line endings.
        if (details.HasThumbnail)
        {
            lines.Add(string.Empty);
            lines.Add(SectionDelimiter);
            lines.Add($"%[{ThumbnailGrammar}]");
            lines.Add("%{");
            lines.Add(ThumbnailComment);
            lines.Add(ThumbnailPreamble);
            lines.Add($"<{ThumbnailRoot} autoCapture='{(details.ThumbnailAutoCapture ? "true" : "false")}'>{details.ThumbnailDataUri}</{ThumbnailRoot}>");
            lines.Add("%}");
        }

        return lines;
    }

    /// <summary>One component's element and, inside it, its properties by name and its children (<c>XMLComponentInitBehavior</c>).</summary>
    private static void Component(List<string> lines, AppDesign design, DesignComponent component, int depth, DesignCodeStyle style)
    {
        string tag = ReferenceEquals(component, design.Figure) ? FigureTag : ShortName(component.Type);
        lines.Add(style.Indent(depth) + $"<{tag} name='{component.CodeName}'>");
        foreach ((string name, string value) in AppendixProperties(component, style.Theme).OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            Element(lines, name, value, depth + 1, style);
        }

        if (component.Children.Count > 0)
        {
            lines.Add(style.Indent(depth + 1) + "<Children>");
            foreach (DesignComponent child in component.Children)
            {
                Component(lines, design, child, depth + 2, style);
            }

            lines.Add(style.Indent(depth + 1) + "</Children>");
        }

        lines.Add(style.Indent(depth) + $"</{tag}>");
    }

    /// <summary>
    /// The properties the appendix writes for <paramref name="component"/>, named as App Designer's
    /// design-time controller names them in <c>DirtyProps</c> (the generator's list, its aliases
    /// under their user-facing names, a grid child's <c>Layout.Row</c> and <c>Layout.Column</c>) and
    /// valued as <c>getPropertyValueString</c> values them: a callback by its function's name, a
    /// context menu by its code name, anything else as the generated line's right-hand side.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> AppendixProperties(DesignComponent component, DesignTheme theme)
    {
        ArgumentNullException.ThrowIfNull(component);
        var result = new List<(string, string)>();
        foreach ((string name, DesignValue value) in DesignCodeWriter.WrittenProperties(component, theme))
        {
            result.Add((AppendixNames.GetValueOrDefault(name, name), AppendixValue(component, name, value)));
        }

        // The context menu is the code model's to assign in createComponents, and the appendix's
        // own property here (getComponentDirtyProps adds it to the list).
        if (component.Get("ContextMenu") is ComponentsValue { Bracketed: false, CodeNames: [string menu] })
        {
            result.Add(("ContextMenu", menu));
        }

        return result;
    }

    /// <summary>
    /// The text an appendix element holds for a design value (<c>VisualComponentAdapter.getPropertyValueString</c>):
    /// a property ending in <c>Fcn</c> is its function's bare name; a context menu its code name; a
    /// char is put in quotes with its inner quotes left single, which the loader doubles again; an
    /// on/off state is its char; anything else is the generated line's right-hand side
    /// (<c>propertyValueToString</c>). Three of App Designer's rules are not followed, each a
    /// divergence recorded in ADR 0251: a callback property named <c>...Callback</c> is written bare
    /// like the <c>Fcn</c> ones, where App Designer's char rule quotes it and its own loader then
    /// cannot parse the line (measured: R2025b fails to open such a file; its run strips the quotes);
    /// a char with a line break is written in the generator's <c>['a' newline 'b']</c> form, which the
    /// loader evaluates whole, where App Designer writes the two characters <c>\n</c> that its loader
    /// never turns back; and a cell of one char stays a cell, where App Designer writes the char alone.
    /// </summary>
    private static string AppendixValue(DesignComponent component, string name, DesignValue value)
    {
        value = DesignCodeWriter.WrittenValue(component.Type, name, value);

        // The figure's colormap and a tree's checked nodes are written bare, as their adapters write
        // them (getColormapString; CheckBoxTreeAdapter: code names between brackets, a space apart).
        // A tree's selected nodes are written the same way, which the loader reads, where R2025b
        // writes the generator's placeholder form that its own loader cannot run (a divergence).
        return value switch
        {
            CallbackValue callback => callback.Method,
            ComponentsValue { Bracketed: false, CodeNames: [string one] } when name == "ContextMenu" => one,
            ComponentsValue refs when name is "CheckedNodes" or "SelectedNodes" => "[" + string.Join(" ", refs.CodeNames) + "]",
            ImageFileValue image => "'" + image.RelativePath + "'",
            CharValue c when name == "AD_ColormapString" => c.Text,
            CharValue c when c.Text.Contains('\n') => DesignValueText.CharText(c.Text),
            CharValue c => "'" + c.Text + "'",
            _ => DesignValueText.Format(value),
        };
    }

    /// <summary>
    /// What the loader makes of an element's text before it reads it as a value
    /// (<c>sanitizeComponentPropertyValue</c>): stripped, and a value of more than two characters
    /// in single quotes has every inner quote doubled, since the writer left them single; an empty
    /// value is <c>''</c>.
    /// </summary>
    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string value = text.Trim();
        if (value.Length == 0)
        {
            return "''";
        }

        if (value.Length > 2 && value[0] == '\'' && value[^1] == '\'')
        {
            string inner = value[1..^1];
            if (inner.Contains('\''))
            {
                value = "'" + inner.Replace("'", "''", StringComparison.Ordinal) + "'";
            }
        }

        return value;
    }

    /// <summary>A dynamic section (<c>_XMLDynamicSectionBehavior</c>): an empty line, the delimiter, the grammar, the XML; nothing when it has no elements.</summary>
    private static void Section(List<string> lines, string grammar, string root, IReadOnlyList<(string Name, string Value)> elements, DesignCodeStyle style, IReadOnlyList<(string Name, IReadOnlyList<string> Items)>? lists = null)
    {
        if (elements.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(SectionDelimiter);
        lines.Add($"%[{grammar}]");
        lines.Add("%{");
        lines.Add(XmlPreamble);
        lines.Add($"<{root}>");
        foreach ((string name, string value) in elements.OrderBy(static e => e.Name, StringComparer.Ordinal))
        {
            if (lists?.FirstOrDefault(l => l.Name == name) is { Items: { Count: > 0 } items })
            {
                lines.Add(style.Indent(1) + $"<{name}>");
                foreach (string item in items)
                {
                    Element(lines, "Product", item, 2, style);
                }

                lines.Add(style.Indent(1) + $"</{name}>");
            }
            else
            {
                Element(lines, name, value, 1, style);
            }
        }

        lines.Add($"</{root}>");
        lines.Add("%}");
    }

    /// <summary>One element with a text value (<c>XMLElementBuilder</c>): on one line, its text escaped; an empty value as an open and a close line.</summary>
    private static void Element(List<string> lines, string name, string value, int depth, DesignCodeStyle style)
    {
        if (value.Length == 0)
        {
            lines.Add(style.Indent(depth) + $"<{name}>");
            lines.Add(style.Indent(depth) + $"</{name}>");
            return;
        }

        lines.Add(style.Indent(depth) + $"<{name}>{Escape(value)}</{name}>");
    }

    /// <summary>
    /// <c>XMLElementBuilder._escape</c>: <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> escaped; a value
    /// in single quotes has its line breaks written <c>\n</c> and its inner quotes <c>&apos;</c>.
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string a = value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
        if (a.StartsWith('\'') && a.EndsWith('\''))
        {
            // substring(1, length - 1) in JavaScript, which for a lone quote swaps its ends and gives the quote itself.
            string inner = a.Length >= 2 ? a[1..^1] : a;
            inner = inner.Replace("\n", "\\n", StringComparison.Ordinal);
            a = "'" + inner.Replace("'", "&apos;", StringComparison.Ordinal) + "'";
        }

        return a;
    }

    private static string ShortName(string type) => type[(type.LastIndexOf('.') + 1)..];

    /// <summary>The relative paths of the images the design keeps beside the app, in the order they are met.</summary>
    internal static IEnumerable<string> ImagesOf(AppDesign design) =>
        design.Components.SelectMany(static c => c.Properties).Select(static p => p.Value).OfType<ImageFileValue>()
            .Select(static i => i.RelativePath).Distinct(StringComparer.Ordinal);

    // --- reading ---------------------------------------------------------------------------------

    /// <summary>
    /// Reads a plain-text app's <paramref name="text"/>: its layout section into a design, as App
    /// Designer's MATLAB side reads it (<c>createInitCodeLinesForComponent</c>: an element per
    /// component, its class by the element's name, its properties from the child elements), and its
    /// other sections into the details. A file with no appendix or no layout section gives no design.
    /// </summary>
    public static DesignPlainTextReadResult Read(string text, DesignCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        catalog ??= DesignCatalog.R2025b;
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var problems = new List<string>();
        var details = new DesignPlainTextDetails { MatlabRelease = string.Empty, HasThumbnail = false };
        Dictionary<string, string> sections = Sections(lines);
        if (!sections.TryGetValue(LayoutGrammar, out string? layoutXml))
        {
            return new DesignPlainTextReadResult(
                new DesignReadResult(null, DesignCodeStyle.Default, ["The file has no %[app:layout] section."]), details);
        }

        ReadDetails(sections, details, problems);
        var style = new DesignCodeStyle(IndentUnitOf(lines));
        XElement? root = ParseXml(layoutXml, "layout", problems);
        XElement? figureElement = root?.Elements().FirstOrDefault();
        if (root is null || figureElement is null || figureElement.Name.LocalName != FigureTag)
        {
            problems.Add("The layout section has no UIFigure element.");
            return new DesignPlainTextReadResult(new DesignReadResult(null, style, problems), details);
        }

        Dictionary<string, string> declared = DesignCodeReader.DeclaredTypes(lines);
        var byName = new Dictionary<string, DesignComponent>(StringComparer.Ordinal);
        DesignComponentClass figureClass = catalog.ByType("matlab.ui.Figure")!;
        string? figureName = figureElement.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(figureName))
        {
            // R2025b refuses an element with no name (AppAppendixException), the figure's included.
            problems.Add($"<{FigureTag}>: the figure element has no name.");
            return new DesignPlainTextReadResult(new DesignReadResult(null, style, problems), details);
        }

        var figure = new DesignComponent(figureName, figureClass);
        var design = new AppDesign(figure);
        byName[figureName] = figure;
        var images = new HashSet<string>(details.ImageRelativePaths, StringComparer.Ordinal);
        ReadProperties(figureElement, figure, design, images, problems);
        ReadChildren(figureElement, figure, design, byName, declared, images, catalog, problems);

        // Responsive when the class says so, as the code reader decides it (DS4b).
        design.IsResponsive = Array.Exists(lines, static l => l.Trim() == DesignCodeReader.ResponsivePropertiesComment);
        style = style with { Theme = DesignCodeStyle.ThemeOf(design) };
        return new DesignPlainTextReadResult(new DesignReadResult(design, style, problems), details);
    }

    private static void ReadChildren(XElement element, DesignComponent parent, AppDesign design, Dictionary<string, DesignComponent> byName, Dictionary<string, string> declared, HashSet<string> images, DesignCatalog catalog, List<string> problems)
    {
        // The last Children element is the one R2025b's generator reads (its XPath takes the last).
        XElement? children = element.Elements().LastOrDefault(static e => e.Name.LocalName == "Children");
        if (children is null)
        {
            return;
        }

        foreach (XElement child in children.Elements())
        {
            string tag = child.Name.LocalName;
            string? name = child.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                problems.Add($"<{tag}>: a component element has no name.");
                continue;
            }

            if (byName.ContainsKey(name))
            {
                problems.Add($"<{tag} name='{name}'>: a second component of that name.");
                continue;
            }

            DesignComponentClass? cls = catalog.ByShortName(tag);
            if (cls is { IsFigure: true })
            {
                cls = null;
            }

            if (cls is not null && declared.TryGetValue(name, out string? type) && type != cls.Type)
            {
                cls = null; // the declared class wins over the element's name
            }

            // An element the catalog does not know is a custom component (DS7b): its class as the
            // properties block declares it, else the element's name, made by a call of that name.
            DesignComponent component = cls is not null
                ? new DesignComponent(name, cls)
                : new DesignComponent(name, declared.GetValueOrDefault(name, tag), declared.GetValueOrDefault(name, tag), string.Empty);
            parent.Add(component);
            byName[name] = component;
            ReadProperties(child, component, design, images, problems);
            ReadChildren(child, component, design, byName, declared, images, catalog, problems);
        }
    }

    /// <summary>The property elements of <paramref name="element"/> onto <paramref name="component"/>, each but <c>Children</c>.</summary>
    private static void ReadProperties(XElement element, DesignComponent component, AppDesign design, HashSet<string> images, List<string> problems)
    {
        IReadOnlyList<string> callbacks = CallbackProperties(component);
        foreach (XElement property in element.Elements())
        {
            string tag = property.Name.LocalName;
            if (tag == "Children")
            {
                continue;
            }

            string text = property.Value;
            string key = DesignNames.GetValueOrDefault(tag, tag);
            if (ReferenceEquals(component, design.Figure))
            {
                key = key switch
                {
                    "ThemeChangedFcn" => "AD_AliasedThemeChangedFcn",
                    "AD_ColormapString" or "ColormapString" => "AD_ColormapString",
                    _ => key,
                };
            }
            else if (key is "Colormap" or "AD_ColormapString")
            {
                // Another component's colormap is the design's colormap(app.X, map) line, which this
                // writer puts out under Colormap as it does the figure's; App Designer writes none.
                key = "AD_ColormapString";
            }

            if (component.Get(key) is not null)
            {
                problems.Add($"{component.CodeName}.{tag}: the property is set twice.");
                continue;
            }

            if (key is "Visible" && ReferenceEquals(component, design.Figure))
            {
                continue; // the figure is shown once everything is made: the writer's to decide
            }

            DesignValue? value = ReadValue(component, key, text, callbacks, images);
            if (value is null)
            {
                if (IsCallbackProperty(component, key, callbacks) && Sanitize(text) == "''")
                {
                    continue; // an empty callback element is a property left unset
                }

                problems.Add($"{component.CodeName}.{tag}: '{text}' is not a value the designer reads.");
                continue;
            }

            component.Set(key, value);
        }
    }

    /// <summary>The design's value for an element's text, by what the property is (<c>translateProperty</c>'s cases).</summary>
    private static DesignValue? ReadValue(DesignComponent component, string key, string text, IReadOnlyList<string> callbacks, HashSet<string> images)
    {
        string trimmed = Sanitize(text);
        if (key == "AD_ColormapString")
        {
            return IsName(trimmed) ? new CharValue(trimmed) : DesignValueText.Parse(trimmed);
        }

        if (key == "ContextMenu")
        {
            return trimmed.Length > 0 && IsName(trimmed) ? new ComponentsValue([trimmed], bracketed: false) : null;
        }

        if (key is "CheckedNodes" or "SelectedNodes")
        {
            // Bare code names a space apart (what R2025b writes for checked nodes and its loader
            // reads); the generator's placeholder or app-prefixed forms are taken by their last part.
            string[] names = trimmed.Trim('[', ']').Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static n => n[(n.LastIndexOf('.') + 1)..])
                .ToArray();
            return names.Length > 0 && names.All(IsName) ? new ComponentsValue(names, bracketed: true) : null;
        }

        if (IsCallbackProperty(component, key, callbacks))
        {
            // App Designer writes a Fcn's function by its bare name and a Callback's in quotes; the
            // generated createCallbackFcn form is taken too. An empty one is a property left unset.
            return trimmed == "''" ? null
                : IsName(trimmed) ? new CallbackValue(trimmed)
                : DesignValueText.Parse(trimmed) switch
                {
                    CallbackValue cb => cb,
                    CharValue { Text: var method } when method.Length > 0 && IsName(method) => new CallbackValue(method),
                    _ => null,
                };
        }

        // An image's name in quotes is an image kept beside the app when the internal data lists it
        // (what this writer lists), else a preset icon or a file named on the path, as the loader
        // takes it (exist, then fullfile with the app's folder).
        if (key is "ImageSource" or "Icon" && DesignValueText.Parse(trimmed) is CharValue file && file.Text.Length > 0)
        {
            string relative = file.Text.Replace('\\', '/');
            return images.Contains(relative)
                ? new ImageFileValue(relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
                : file;
        }

        return DesignValueText.Parse(trimmed);
    }

    /// <summary>
    /// Whether <paramref name="key"/> is a callback property of <paramref name="component"/>: one of
    /// <paramref name="callbacks"/> (its class's), the figure's aliased theme callback, or - for a
    /// class the catalog does not know (a custom component) - any property its name says is one
    /// (<c>extractCallbackPropertyNamesFromMetaClass</c> lists every Callback-typed one).
    /// </summary>
    private static bool IsCallbackProperty(DesignComponent component, string key, IReadOnlyList<string> callbacks) =>
        callbacks.Contains(key, StringComparer.Ordinal) || key == "AD_AliasedThemeChangedFcn"
        || (component.Class is null && (key.EndsWith("Fcn", StringComparison.Ordinal) || key.EndsWith("Callback", StringComparison.Ordinal)));

    /// <summary>The callback properties of a component's class: App Designer's table's, else the catalog's by their names.</summary>
    private static IReadOnlyList<string> CallbackProperties(DesignComponent component)
    {
        IReadOnlyList<string> listed = DesignCallbacks.R2025b.PropertiesOf(component.Type);
        if (listed.Count > 0 || component.Class is null)
        {
            return listed;
        }

        return component.Class.Properties.Select(static p => DesignCallbacks.PropertyName(p.Name))
            .Where(static n => n.EndsWith("Fcn", StringComparison.Ordinal) || n.EndsWith("Callback", StringComparison.Ordinal))
            .ToArray();
    }

    private static bool IsName(string text) => NameWord().IsMatch(text);

    /// <summary>The run configuration, app details, internal data and thumbnail sections into <paramref name="details"/>.</summary>
    private static void ReadDetails(Dictionary<string, string> sections, DesignPlainTextDetails details, List<string> problems)
    {
        if (sections.TryGetValue(AppDetailsGrammar, out string? appDetails) && ParseXml(appDetails, "app details", problems) is { } d)
        {
            details.Author = Text(d, "Author");
            details.Description = Text(d, "Description");
            details.Name = Text(d, "Name");
            details.Summary = Text(d, "Summary");
            details.Version = Text(d, "Version");
        }

        if (sections.TryGetValue(InternalDataGrammar, out string? internalData) && ParseXml(internalData, "internal data", problems) is { } i)
        {
            details.AppId = Text(i, "AppId");
            details.MatlabRelease = Text(i, "MATLABRelease");
            details.MinimumSupportedMatlabRelease = Text(i, "MinimumSupportedMATLABRelease");
            details.FileFormatVersion = Text(i, "FileFormatVersion");
            details.RequiredProducts.AddRange(List(i, "RequiredProducts"));
            details.UserComponents.AddRange(List(i, "UserComponents"));
            details.ImageRelativePaths.AddRange(List(i, "ImageRelativePaths"));
        }

        if (sections.TryGetValue(ThumbnailGrammar, out string? thumbnail))
        {
            details.HasThumbnail = true;
            if (ParseXml(thumbnail, "thumbnail", problems) is { } t)
            {
                details.ThumbnailAutoCapture = t.Attribute("autoCapture")?.Value != "false";
                details.ThumbnailDataUri = t.Value.Trim();
            }
        }

        static string Text(XElement root, string name) => root.Element(name)?.Value.Trim() ?? string.Empty;

        static IEnumerable<string> List(XElement root, string name)
        {
            if (root.Element(name) is not { } element)
            {
                return [];
            }

            return element.HasElements
                ? element.Elements().Select(static e => e.Value.Trim()).Where(static v => v.Length > 0)
                : element.Value.Trim() is { Length: > 0 } one ? [one] : [];
        }
    }

    /// <summary>The run configuration section's startup function name and single-instance setting, for the code model; both absent when there is no section.</summary>
    public static (string? Startup, bool SingleInstance) RunConfiguration(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (!Sections(lines).TryGetValue(RunConfigurationGrammar, out string? xml) || ParseXml(xml, "run configuration", []) is not { } root)
        {
            return (null, false);
        }

        string startup = root.Element("StartupFcn")?.Value.Trim() ?? string.Empty;
        return (startup.Length > 0 ? startup : null, root.Element("SingleRunningInstance")?.Value.Trim() == "true");
    }

    /// <summary>
    /// Each section's XML text by its grammar name, cut as <c>getAppendixByGrammarName</c> cuts it:
    /// from the <c>%{</c> after the grammar line to the <c>%}</c> after the root's last closing tag.
    /// </summary>
    private static Dictionary<string, string> Sections(string[] lines)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        int appendix = Array.FindIndex(lines, static l => l.Trim() == AppendixMarker);
        if (appendix < 0)
        {
            return sections;
        }

        for (int i = appendix + 1; i < lines.Length; i++)
        {
            Match m = GrammarLine().Match(lines[i]);
            if (!m.Success)
            {
                continue;
            }

            string grammar = m.Groups[1].Value;
            int open = Array.FindIndex(lines, i + 1, static l => l.Trim() == "%{");
            if (open < 0)
            {
                break;
            }

            int close = Array.FindIndex(lines, open + 1, static l => l.Trim() == "%}");
            if (close < 0)
            {
                break;
            }

            sections[grammar] = string.Join('\n', lines, open + 1, close - open - 1);
            i = close;
        }

        return sections;
    }

    /// <summary>The root element of a section's XML, or null with a problem recorded.</summary>
    private static XElement? ParseXml(string xml, string what, List<string> problems)
    {
        try
        {
            // The thumbnail section's comment stands before its declaration, which an XML parser
            // refuses; MATLAB's reader takes the section by its strings, so the comment goes.
            string text = xml.Trim();
            while (text.StartsWith("<!--", StringComparison.Ordinal) && text.IndexOf("-->", StringComparison.Ordinal) is var end and >= 0)
            {
                text = text[(end + 3)..].TrimStart();
            }

            return XDocument.Parse(text, LoadOptions.PreserveWhitespace).Root;
        }
        catch (XmlException ex)
        {
            problems.Add(what == "layout" ? MalformedXmlProblem + ex.Message : $"The {what} section is not well-formed XML: {ex.Message}");
            return null;
        }
    }

    /// <summary>One level of indentation: what the component properties block's <c>properties</c> line is indented by, else four spaces.</summary>
    private static string IndentUnitOf(string[] lines)
    {
        int comment = Array.FindIndex(lines, static l => l.Trim() == DesignCodeReader.ComponentPropertiesComment);
        if (comment >= 0 && comment + 1 < lines.Length)
        {
            string line = lines[comment + 1];
            string indent = line[..(line.Length - line.TrimStart().Length)];
            if (indent.Length > 0 && indent.Distinct().Count() == 1)
            {
                return indent;
            }
        }

        return "    ";
    }

    [GeneratedRegex(@"^%\[([a-z]+:[A-Za-z]+)\]\s*$")]
    private static partial Regex GrammarLine();

    [GeneratedRegex(@"^[A-Za-z]\w*$")]
    private static partial Regex NameWord();
}
